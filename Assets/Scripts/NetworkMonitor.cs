// Network Monitor - Watches network connectivity for NDI streaming
// Uses NDI-aware health signals instead of internet reachability alone.
// NDI operates over LAN multicast/mDNS and does not require internet access.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Monitors network connectivity with signals relevant to NDI streaming.
    /// Unlike <c>Application.internetReachability</c>, this checks for:
    ///   1. An active network interface with a valid LAN address (Wi-Fi or Ethernet)
    ///   2. Whether NDI source discovery is actively finding sources
    ///   3. Whether the NDI receiver is experiencing sustained frame drops
    /// This avoids both false negatives (NDI works fine on LAN without internet)
    /// and false positives (internet reachable but multicast/discovery broken).
    /// </summary>
    public class NetworkMonitor : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Seconds between connectivity checks")]
        [SerializeField] private float checkInterval = 3.0f;

        [Tooltip("Number of consecutive failures before reporting disconnect")]
        [SerializeField] private int failureThreshold = 2;

        [Tooltip("Attempt automatic reconnection on network recovery")]
        [SerializeField] private bool autoReconnect = true;

        [Tooltip("Max seconds to wait for the source to reappear after network recovery")]
        [SerializeField] private float reconnectTimeoutSeconds = 30.0f;

        /// <summary>Fired when network connectivity is lost.</summary>
        public event Action OnNetworkLost;

        /// <summary>Fired when network connectivity is restored.</summary>
        public event Action OnNetworkRestored;

        public bool IsNetworkAvailable { get; private set; } = true;

        /// <summary>True when a usable LAN interface (with a private IP) is detected.</summary>
        public bool HasLanInterface { get; private set; }

        /// <summary>True when NDI discovery is finding at least one source.</summary>
        public bool HasDiscoverySources { get; private set; }

        private float _checkTimer;
        private int _consecutiveFailures;
        private bool _wasConnected = true;
        private NDIReceiver _receiver;
        private NDISourceDiscovery _discovery;

        // Reconnection state
        private bool _waitingForReconnect;
        private string _reconnectSourceName;
        private float _reconnectTimer;

        private void Start()
        {
            _receiver = FindFirstObjectByType<NDIReceiver>();
            if (_discovery == null)
                _discovery = FindFirstObjectByType<NDISourceDiscovery>();
        }

        /// <summary>
        /// Set the source discovery reference (called by SceneBootstrapper).
        /// </summary>
        public void SetDiscovery(NDISourceDiscovery discovery)
        {
            _discovery = discovery;
        }

        private void Update()
        {
            _checkTimer += Time.unscaledDeltaTime;

            if (_checkTimer >= checkInterval)
            {
                _checkTimer = 0;
                CheckConnectivity();
            }

            if (_waitingForReconnect)
            {
                TryReconnect();
            }
        }

        private void CheckConnectivity()
        {
            // Layer 1: Check for a usable LAN interface (Wi-Fi or Ethernet with a
            // private/link-local IP). This replaces Application.internetReachability
            // which does not reflect local network availability.
            HasLanInterface = CheckLanInterface();

            // Layer 2: Check whether NDI discovery is seeing sources. If we have a
            // LAN interface but discovery returns nothing for extended periods, the
            // multicast/mDNS path may be broken.
            HasDiscoverySources = _discovery != null &&
                _discovery.CurrentSources != null &&
                _discovery.CurrentSources.Count > 0;

            // Consider the network usable if we have a LAN interface.
            // Discovery having zero sources is normal during startup or when no
            // NDI senders are running, so we don't treat that as a failure.
            bool networkUsable = HasLanInterface;

            // Also accept Unity's reachability as a secondary signal — on some
            // platforms the .NET NetworkInterface APIs may not be available.
            if (!networkUsable)
            {
                networkUsable = Application.internetReachability != NetworkReachability.NotReachable;
            }

            if (!networkUsable)
            {
                _consecutiveFailures++;

                if (_consecutiveFailures >= failureThreshold && _wasConnected)
                {
                    _wasConnected = false;
                    IsNetworkAvailable = false;
                    Debug.LogWarning("[Network] Connectivity lost (no LAN interface detected).");

                    // Remember that we need to reconnect when network returns
                    if (autoReconnect && _receiver != null &&
                        !string.IsNullOrEmpty(_receiver.LastConnectedSourceName) &&
                        (_receiver.State == NDIReceiver.ConnectionState.Connected ||
                         _receiver.State == NDIReceiver.ConnectionState.Connecting))
                    {
                        _reconnectSourceName = _receiver.LastConnectedSourceName;
                    }

                    OnNetworkLost?.Invoke();
                }
            }
            else
            {
                _consecutiveFailures = 0;

                if (!_wasConnected)
                {
                    _wasConnected = true;
                    IsNetworkAvailable = true;
                    Debug.Log("[Network] Connectivity restored (LAN interface available).");
                    OnNetworkRestored?.Invoke();

                    // Start waiting for the source to reappear in discovery
                    if (autoReconnect && _receiver != null && _discovery != null &&
                        !string.IsNullOrEmpty(_reconnectSourceName) &&
                        (_receiver.State == NDIReceiver.ConnectionState.Error ||
                         _receiver.State == NDIReceiver.ConnectionState.Disconnected))
                    {
                        Debug.Log($"[Network] Will auto-reconnect to \"{_reconnectSourceName}\" " +
                            "once it reappears in source discovery.");
                        _waitingForReconnect = true;
                        _reconnectTimer = 0f;
                    }
                }
            }
        }

        /// <summary>
        /// Check for a network interface that has a LAN-usable IP address.
        /// Looks for interfaces that are Up, have a unicast IPv4 address in a
        /// private or link-local range, and are not loopback.
        /// </summary>
        private static bool CheckLanInterface()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var props = nic.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;

                        byte[] bytes = addr.Address.GetAddressBytes();
                        // Accept private ranges (10.x, 172.16-31.x, 192.168.x)
                        // and link-local (169.254.x) which can still carry NDI
                        if (bytes[0] == 10 ||
                            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                            (bytes[0] == 192 && bytes[1] == 168) ||
                            (bytes[0] == 169 && bytes[1] == 254))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // NetworkInterface APIs may not be available on all platforms.
                // Fall through to let the caller use Unity's reachability as fallback.
                Debug.LogWarning($"[Network] LAN interface check failed: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Pure logic: find the first source whose Name matches the target.
        /// Returns null if no match or if the list is empty.
        /// </summary>
        internal static NDISourceDiscovery.DiscoveredSource FindReconnectMatch(
            List<NDISourceDiscovery.DiscoveredSource> sources, string targetName)
        {
            if (sources == null || string.IsNullOrEmpty(targetName))
                return null;

            return sources.FirstOrDefault(s => s.Name == targetName);
        }

        private void TryReconnect()
        {
            _reconnectTimer += Time.unscaledDeltaTime;

            if (_reconnectTimer > reconnectTimeoutSeconds)
            {
                Debug.LogWarning($"[Network] Auto-reconnect timed out after {reconnectTimeoutSeconds}s. " +
                    $"Source \"{_reconnectSourceName}\" did not reappear.");
                _waitingForReconnect = false;
                _reconnectSourceName = null;
                return;
            }

            // Check if the source has reappeared in the discovery list
            if (_discovery == null || _discovery.CurrentSources == null || _discovery.CurrentSources.Count == 0)
                return;

            var match = FindReconnectMatch(_discovery.CurrentSources, _reconnectSourceName);

            if (match != null)
            {
                Debug.Log($"[Network] Source \"{_reconnectSourceName}\" found. Reconnecting...");
                _waitingForReconnect = false;
                _reconnectSourceName = null;
                _receiver.Connect(match);
            }
        }
    }
}

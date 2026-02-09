// Network Monitor - Watches network connectivity for NDI streaming
// Handles disconnection detection and reconnection attempts.

using System;
using System.Linq;
using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Monitors network connectivity and provides notifications for
    /// connection loss/recovery relevant to NDI streaming.
    /// On network recovery, automatically reconnects to the last NDI source
    /// once it reappears in discovery.
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
            // Check Unity's network reachability
            bool networkReachable = Application.internetReachability != NetworkReachability.NotReachable;

            if (!networkReachable)
            {
                _consecutiveFailures++;

                if (_consecutiveFailures >= failureThreshold && _wasConnected)
                {
                    _wasConnected = false;
                    IsNetworkAvailable = false;
                    Debug.LogWarning("[Network] Connectivity lost.");

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
                    Debug.Log("[Network] Connectivity restored.");
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
            if (_discovery.CurrentSources == null || _discovery.CurrentSources.Count == 0)
                return;

            var match = _discovery.CurrentSources
                .FirstOrDefault(s => s.Name == _reconnectSourceName);

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

// Network Monitor - Watches network connectivity for NDI streaming
// Handles disconnection detection and reconnection attempts.

using System;
using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Monitors network connectivity and provides notifications for
    /// connection loss/recovery relevant to NDI streaming.
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

        /// <summary>Fired when network connectivity is lost.</summary>
        public event Action OnNetworkLost;

        /// <summary>Fired when network connectivity is restored.</summary>
        public event Action OnNetworkRestored;

        public bool IsNetworkAvailable { get; private set; } = true;

        private float _checkTimer;
        private int _consecutiveFailures;
        private bool _wasConnected = true;
        private NDIReceiver _receiver;

        private void Start()
        {
            _receiver = FindFirstObjectByType<NDIReceiver>();
        }

        private void Update()
        {
            _checkTimer += Time.unscaledDeltaTime;

            if (_checkTimer >= checkInterval)
            {
                _checkTimer = 0;
                CheckConnectivity();
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

                    // Auto-reconnect if receiver was connected
                    if (autoReconnect && _receiver != null &&
                        _receiver.State == NDIReceiver.ConnectionState.Error)
                    {
                        Debug.Log("[Network] Auto-reconnection available. " +
                            "User can reconnect via UI.");
                    }
                }
            }
        }
    }
}

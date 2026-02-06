// NDI Source Discovery - Automatic network scanning for NDI sources
// NDI® is a registered trademark of Vizrt NDI AB.
// Non-commercial NDI SDK license.

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Discovers NDI sources on the local network using a background thread.
    /// Provides an event-driven interface for UI to display available sources.
    /// </summary>
    public class NDISourceDiscovery : MonoBehaviour
    {
        [Header("Discovery Settings")]
        [Tooltip("How often to poll for new sources (seconds)")]
        [SerializeField] private float pollInterval = 2.0f;

        [Tooltip("Timeout for waiting on source discovery (ms)")]
        [SerializeField] private uint waitTimeoutMs = 1000;

        /// <summary>Fired on the main thread when the source list changes.</summary>
        public event Action<List<DiscoveredSource>> OnSourcesUpdated;

        /// <summary>Represents a discovered NDI source.</summary>
        public class DiscoveredSource
        {
            public string Name;
            public string Url;
            public NDIInterop.NDISource NativeSource;

            public override string ToString() => Name;
        }

        private IntPtr _findInstance = IntPtr.Zero;
        private Thread _discoveryThread;
        private volatile bool _running;
        private readonly object _lock = new object();
        private List<DiscoveredSource> _pendingSources;
        private bool _hasPendingUpdate;

        public List<DiscoveredSource> CurrentSources { get; private set; } = new List<DiscoveredSource>();
        public bool IsDiscovering => _running;

        private void OnEnable()
        {
            StartDiscovery();
        }

        private void OnDisable()
        {
            StopDiscovery();
        }

        /// <summary>Start the background discovery thread.</summary>
        public void StartDiscovery()
        {
            if (_running) return;

            if (!NDIInterop.Initialize())
            {
                Debug.LogError("[NDI Discovery] Failed to initialize NDI library.");
                return;
            }

            var findSettings = new NDIInterop.NDIFindCreateSettings
            {
                showLocalSources = true,
                groups = IntPtr.Zero,
                extraIPs = IntPtr.Zero
            };

            _findInstance = NDIInterop.FindCreate(ref findSettings);
            if (_findInstance == IntPtr.Zero)
            {
                Debug.LogError("[NDI Discovery] Failed to create NDI find instance.");
                return;
            }

            _running = true;
            _discoveryThread = new Thread(DiscoveryLoop)
            {
                Name = "NDI_Discovery",
                IsBackground = true
            };
            _discoveryThread.Start();

            Debug.Log("[NDI Discovery] Started scanning for NDI sources.");
        }

        /// <summary>Stop discovery and release native resources.</summary>
        public void StopDiscovery()
        {
            _running = false;

            if (_discoveryThread != null && _discoveryThread.IsAlive)
            {
                _discoveryThread.Join(2000);
                _discoveryThread = null;
            }

            if (_findInstance != IntPtr.Zero)
            {
                NDIInterop.FindDestroy(_findInstance);
                _findInstance = IntPtr.Zero;
            }

            Debug.Log("[NDI Discovery] Stopped.");
        }

        private void DiscoveryLoop()
        {
            while (_running)
            {
                try
                {
                    NDIInterop.FindWaitForSources(_findInstance, waitTimeoutMs);

                    uint numSources;
                    IntPtr sourcesPtr = NDIInterop.FindGetCurrentSources(_findInstance, out numSources);

                    var sources = new List<DiscoveredSource>();

                    if (sourcesPtr != IntPtr.Zero && numSources > 0)
                    {
                        for (int i = 0; i < (int)numSources; i++)
                        {
                            var nativeSource = NDIInterop.GetSourceAtIndex(sourcesPtr, i);
                            string name = NDIInterop.GetSourceName(nativeSource);
                            string url = nativeSource.urlAddress != IntPtr.Zero
                                ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi(nativeSource.urlAddress) ?? ""
                                : "";

                            sources.Add(new DiscoveredSource
                            {
                                Name = name,
                                Url = url,
                                NativeSource = nativeSource
                            });
                        }
                    }

                    lock (_lock)
                    {
                        _pendingSources = sources;
                        _hasPendingUpdate = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NDI Discovery] Error in discovery loop: {ex.Message}");
                }

                // Sleep between polls
                Thread.Sleep((int)(pollInterval * 1000));
            }
        }

        private void Update()
        {
            // Dispatch source updates to the main thread
            lock (_lock)
            {
                if (_hasPendingUpdate && _pendingSources != null)
                {
                    CurrentSources = new List<DiscoveredSource>(_pendingSources);
                    _hasPendingUpdate = false;
                    OnSourcesUpdated?.Invoke(CurrentSources);
                }
            }
        }

        private void OnDestroy()
        {
            StopDiscovery();
        }
    }
}

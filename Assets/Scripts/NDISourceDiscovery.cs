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
    /// Handles missing native library gracefully by reporting the error state.
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

        /// <summary>Fired on the main thread when the NDI library is unavailable.</summary>
        public event Action<string> OnNDILibraryUnavailable;

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

        /// <summary>True if the NDI native library failed to load.</summary>
        public bool IsNDIUnavailable { get; private set; }

        /// <summary>Human-readable reason if NDI is unavailable.</summary>
        public string NDIUnavailableReason { get; private set; }

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

            // Attempt to initialize NDI - handle missing native library
            try
            {
                if (!NDIInterop.Initialize())
                {
                    Debug.LogError("[NDI Discovery] NDIlib_initialize returned false. NDI library may be corrupt or incompatible.");
                    IsNDIUnavailable = true;
                    NDIUnavailableReason = "NDI library initialization failed. The library may be corrupt or incompatible with this device.";
                    OnNDILibraryUnavailable?.Invoke(NDIUnavailableReason);
                    return;
                }
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogError($"[NDI Discovery] Native NDI library not found: {ex.Message}");
                Debug.LogError("[NDI Discovery] Expected: Assets/Plugins/NDI/Android/arm64-v8a/libndi.so");
                Debug.LogError("[NDI Discovery] Download NDI SDK from https://ndi.video/tools/ndi-sdk/");
                IsNDIUnavailable = true;
                NDIUnavailableReason = "libndi.so not found. Download the NDI SDK from ndi.video and place libndi.so in Plugins/NDI/Android/arm64-v8a/.";
                OnNDILibraryUnavailable?.Invoke(NDIUnavailableReason);
                return;
            }
            catch (EntryPointNotFoundException ex)
            {
                Debug.LogError($"[NDI Discovery] NDI library version mismatch: {ex.Message}");
                IsNDIUnavailable = true;
                NDIUnavailableReason = "NDI library version mismatch. Ensure you have the NDI Advanced SDK (not the standard SDK).";
                OnNDILibraryUnavailable?.Invoke(NDIUnavailableReason);
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
                try
                {
                    NDIInterop.FindDestroy(_findInstance);
                }
                catch (DllNotFoundException)
                {
                    // libndi.so was removed/unloaded — nothing to tear down.
                }
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

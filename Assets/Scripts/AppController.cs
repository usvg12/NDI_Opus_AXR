// App Controller - Main application entry point and lifecycle manager
// Initializes NDI, configures XR passthrough, and wires components together.

using UnityEngine;
using UnityEngine.XR.Management;

namespace NDIViewer
{
    /// <summary>
    /// Main controller that bootstraps the NDI XR Viewer application.
    /// Manages XR session lifecycle, passthrough mode, and component wiring.
    /// </summary>
    public class AppController : MonoBehaviour
    {
        [Header("Core Components")]
        [SerializeField] private NDISourceDiscovery sourceDiscovery;
        [SerializeField] private NDIReceiver receiver;
        [SerializeField] private NDIVideoDisplay videoDisplay;
        [SerializeField] private SpatialWindowController windowController;
        [SerializeField] private UIController uiController;

        [Header("XR Settings")]
        [Tooltip("Target frame rate for VR rendering (72-90 Hz)")]
        [SerializeField] private int targetFrameRate = 72;

        [Tooltip("Enable passthrough (mixed reality) mode on start")]
        [SerializeField] private bool enablePassthrough = true;

        [Header("Performance")]
        [Tooltip("GPU performance level (0=low, 4=max)")]
        [SerializeField] private int gpuPerformanceLevel = 3;

        private bool _initialized;
        private bool _shutdown;

        private void Awake()
        {
            // Set target frame rate for VR rendering
            Application.targetFrameRate = targetFrameRate;
            QualitySettings.vSyncCount = 0;

            // Disable screen timeout for continuous streaming
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // Configure quality for VR performance
            QualitySettings.antiAliasing = 2; // 2x MSAA (balance quality/perf)
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadows = ShadowQuality.Disable; // No shadows needed for video viewer

            Debug.Log("[App] NDI XR Viewer starting...");
        }

        private void Start()
        {
            InitializeXR();
            InitializeComponents();
            _initialized = true;
        }

        private void InitializeXR()
        {
            // Ensure XR is loaded and running
            var xrManager = XRGeneralSettings.Instance?.Manager;
            if (xrManager == null)
            {
                Debug.LogError("[App] XR Plugin Management not configured. Check Project Settings.");
                return;
            }

            if (!xrManager.isInitializationComplete)
            {
                Debug.Log("[App] XR initialization in progress...");
            }

            // Configure passthrough mode via OpenXR
            if (enablePassthrough)
            {
                ConfigurePassthrough();
            }

            Debug.Log($"[App] XR initialized. Target FPS: {targetFrameRate}");
        }

        private void ConfigurePassthrough()
        {
            // Request passthrough/alpha blend environment mode
            // On AndroidXR, this is configured via manifest (environment.blend.mode = ALPHA_BLEND)
            // and the OpenXR extension XR_ANDROID_passthrough_camera_state
            //
            // At runtime, set the camera background to transparent to see the passthrough feed:
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                // Transparent background allows passthrough to show through
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor = Color.clear;

                // Enable HDR for best passthrough quality
                mainCamera.allowHDR = true;

                Debug.Log("[App] Passthrough mode configured (transparent camera background).");
            }
        }

        private void InitializeComponents()
        {
            // Validate that all required components are assigned
            if (sourceDiscovery == null)
                Debug.LogError("[App] NDISourceDiscovery not assigned!");

            if (receiver == null)
                Debug.LogError("[App] NDIReceiver not assigned!");

            if (videoDisplay == null)
                Debug.LogError("[App] NDIVideoDisplay not assigned!");

            if (windowController == null)
                Debug.LogError("[App] SpatialWindowController not assigned!");

            if (uiController == null)
                Debug.LogError("[App] UIController not assigned!");

            // Show placeholder on video display
            videoDisplay?.ShowPlaceholder();

            Debug.Log("[App] All components initialized.");
        }

        /// <summary>
        /// Called when the application is paused/resumed (e.g., headset removed).
        /// </summary>
        private void OnApplicationPause(bool isPaused)
        {
            if (!_initialized) return;

            if (isPaused)
            {
                Debug.Log("[App] Application paused - maintaining NDI connection.");
                // NDI receiver continues in background - no action needed
                // The OS will handle XR session suspend
            }
            else
            {
                Debug.Log("[App] Application resumed.");
            }
        }

        /// <summary>
        /// Clean shutdown: disconnect NDI, release resources.
        /// Idempotent — safe to call from both OnApplicationQuit and OnDestroy.
        /// </summary>
        private void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;

            Debug.Log("[App] Shutting down NDI XR Viewer...");

            // Disconnect NDI receiver
            if (receiver != null && receiver.State != NDIReceiver.ConnectionState.Disconnected)
            {
                receiver.Disconnect();
            }

            // Stop source discovery
            if (sourceDiscovery != null)
            {
                sourceDiscovery.StopDiscovery();
            }

            // Shutdown NDI library
            NDIInterop.Destroy();

            Debug.Log("[App] Shutdown complete.");
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}

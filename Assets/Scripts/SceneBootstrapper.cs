// Scene Bootstrapper - Programmatically sets up the complete scene hierarchy
// Creates all GameObjects, attaches components, and wires references.
// This allows the project to work without serialized scene dependencies.

using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace NDIViewer
{
    /// <summary>
    /// Creates the entire scene hierarchy at runtime. Self-launches via
    /// RuntimeInitializeOnLoadMethod so the boot scene can be completely empty
    /// (no serialized script references or meta-file GUIDs required).
    /// Builds:
    /// - XR Origin with camera and hand interactors
    /// - NDI video display window (spatial, grabbable)
    /// - Floating UI control panel
    /// - All manager components (NDI discovery, receiver, network, performance)
    /// - Diagnostics overlay (hidden by default, toggleable)
    /// </summary>
    public class SceneBootstrapper : MonoBehaviour
    {
        private static bool s_initialized;

        [Header("Optional Overrides")]
        [Tooltip("Custom SBS shader (auto-found if null)")]
        [SerializeField] private Shader sbsShader;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            if (s_initialized)
                return;

            // If a SceneBootstrapper already exists in the scene (e.g. placed
            // manually), let its Awake() handle initialization instead.
            if (FindFirstObjectByType<SceneBootstrapper>() != null)
                return;

            var go = new GameObject("Scene Bootstrapper");
            go.AddComponent<SceneBootstrapper>();
        }

        private void Awake()
        {
            if (s_initialized)
            {
                Debug.LogWarning("[Bootstrapper] Already initialized — skipping duplicate.");
                Destroy(gameObject);
                return;
            }

            s_initialized = true;
            Debug.Log("[Bootstrapper] Building NDI XR Viewer scene...");
            BuildScene();
            Debug.Log("[Bootstrapper] Scene build complete.");
        }

        private void BuildScene()
        {
            // ─── 1. XR Interaction Manager ────────────────────────────
            var interactionManagerGO = new GameObject("XR Interaction Manager");
            interactionManagerGO.AddComponent<XRInteractionManager>();

            // ─── 2. Event System for XR UI ────────────────────────────
            // Guard against duplicate EventSystems — a second instance silently
            // breaks all UI input, which is a common XR friction point.
            var existingEventSystem = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (existingEventSystem != null)
            {
                Debug.LogWarning("[Bootstrapper] Destroying pre-existing EventSystem to avoid " +
                    "duplicate input module conflicts.");
                DestroyImmediate(existingEventSystem.gameObject);
            }

            var eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var xrInputModule = eventSystemGO.AddComponent<XRUIInputModule>();

            // Explicitly enable XR pointer input behaviour. Without this the
            // module may fall back to mouse/gamepad processing on some platforms.
            xrInputModule.enableXRInput = true;
            xrInputModule.enableMouseInput = false;
            xrInputModule.enableTouchInput = false;

            Debug.Log("[Bootstrapper] XRUIInputModule configured " +
                "(XR input enabled, mouse/touch disabled).");

            // ─── 3. XR Origin (Camera Rig) ────────────────────────────
            var xrOriginGO = new GameObject("XR Origin");
            var xrRigSetup = xrOriginGO.AddComponent<XRRigSetup>();

            // Camera offset (tracking space)
            var cameraOffsetGO = new GameObject("Camera Offset");
            cameraOffsetGO.transform.SetParent(xrOriginGO.transform);
            cameraOffsetGO.transform.localPosition = Vector3.zero;

            // Main Camera
            var cameraGO = new GameObject("Main Camera");
            cameraGO.transform.SetParent(cameraOffsetGO.transform);
            cameraGO.transform.localPosition = new Vector3(0, 1.6f, 0); // Eye height
            cameraGO.tag = "MainCamera";

            var camera = cameraGO.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            camera.allowHDR = true;

            cameraGO.AddComponent<AudioListener>();

            // Passthrough configurator
            var passthroughConfig = cameraGO.AddComponent<PassthroughConfigurator>();

            // ─── 3b. XR Permission Manager ──────────────────────────
            var permManagerGO = new GameObject("XR Permission Manager");
            var permManager = permManagerGO.AddComponent<XRPermissionManager>();

            // Wire permission awareness: passthrough depends on CAMERA,
            // hand tracking depends on HAND_TRACKING. The permission
            // manager fires OnPermissionsResolved once all requests complete.
            permManager.OnPermissionsResolved += result =>
            {
                if (result.Camera == XRPermissionManager.PermissionStatus.Denied)
                {
                    Debug.LogWarning("[Bootstrapper] Camera permission denied — disabling passthrough.");
                    passthroughConfig.SetPassthroughEnabled(false);
                }
                if (result.HandTracking == XRPermissionManager.PermissionStatus.Denied)
                {
                    Debug.LogWarning("[Bootstrapper] Hand tracking permission denied — " +
                        "hand interaction may be unavailable.");
                }
            };

            // Kick off runtime permission requests
            permManager.RequestAllPermissions();

            // ─── 4. NDI Managers ──────────────────────────────────────
            var managersGO = new GameObject("NDI Managers");

            var sourceDiscovery = managersGO.AddComponent<NDISourceDiscovery>();
            var receiver = managersGO.AddComponent<NDIReceiver>();
            var networkMonitor = managersGO.AddComponent<NetworkMonitor>();
            networkMonitor.SetDiscovery(sourceDiscovery);
            var performanceMonitor = managersGO.AddComponent<PerformanceMonitor>();

            // ─── 5. Video Display Window ──────────────────────────────
            var videoWindowGO = new GameObject("NDI Video Window");

            // Mesh components for rendering
            var meshFilter = videoWindowGO.AddComponent<MeshFilter>();
            var meshRenderer = videoWindowGO.AddComponent<MeshRenderer>();

            // Video display (creates quad mesh and material)
            var videoDisplay = videoWindowGO.AddComponent<NDIVideoDisplay>();

            // Spatial window (makes it grabbable, movable, resizable)
            var rb = videoWindowGO.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // Collider for grab interaction
            var collider = videoWindowGO.AddComponent<BoxCollider>();
            collider.size = new Vector3(1, 1, 0.01f);

            var windowController = videoWindowGO.AddComponent<SpatialWindowController>();

            // Optional: composition layer rendering for sharper video output
            var compositionLayerRenderer = videoWindowGO.AddComponent<CompositionLayerVideoRenderer>();
            compositionLayerRenderer.SetReferences(receiver, videoDisplay, windowController);

            // ─── 6. UI Control Panel ──────────────────────────────────
            var uiBuilderGO = new GameObject("UI Builder");
            var uiBuilder = uiBuilderGO.AddComponent<UIPanelBuilder>();
            var panelGO = uiBuilder.Build();

            // Create UIController and wire references via explicit API
            var uiControllerGO = new GameObject("UI Controller");
            var uiController = uiControllerGO.AddComponent<UIController>();
            uiController.SetReferences(sourceDiscovery, receiver, videoDisplay,
                windowController, uiBuilder, compositionLayerRenderer);

            // ─── 7. App Controller ────────────────────────────────────
            var appControllerGO = new GameObject("App Controller");
            var appController = appControllerGO.AddComponent<AppController>();
            appController.SetReferences(sourceDiscovery, receiver, videoDisplay,
                windowController, uiController);

            // ─── 8. Diagnostics Overlay ───────────────────────────────
            var diagGO = new GameObject("Diagnostics");
            var diagnostics = diagGO.AddComponent<DiagnosticsOverlay>();
            diagnostics.SetReferences(receiver, performanceMonitor, compositionLayerRenderer);
            diagnostics.Initialize();

            // ─── 9. Initial state ─────────────────────────────────────
            // Video frame forwarding is handled by UIController.OnVideoFrameReceived,
            // which also updates the resolution display. No additional subscription needed.

            // Show placeholder initially
            videoDisplay.ShowPlaceholder();
        }

    }
}

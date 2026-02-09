// Scene Bootstrapper - Programmatically sets up the complete scene hierarchy
// Creates all GameObjects, attaches components, and wires references.
// This allows the project to work without serialized scene dependencies.

using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace NDIViewer
{
    /// <summary>
    /// Creates the entire scene hierarchy at runtime. Attach this to an empty
    /// GameObject in a minimal scene. It will build:
    /// - XR Origin with camera and hand interactors
    /// - NDI video display window (spatial, grabbable)
    /// - Floating UI control panel
    /// - All manager components (NDI discovery, receiver, network, performance)
    /// - Diagnostics overlay (hidden by default, toggleable)
    /// </summary>
    public class SceneBootstrapper : MonoBehaviour
    {
        [Header("Optional Overrides")]
        [Tooltip("Custom SBS shader (auto-found if null)")]
        [SerializeField] private Shader sbsShader;

        private void Awake()
        {
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

            // ─── 4. NDI Managers ──────────────────────────────────────
            var managersGO = new GameObject("NDI Managers");

            var sourceDiscovery = managersGO.AddComponent<NDISourceDiscovery>();
            var receiver = managersGO.AddComponent<NDIReceiver>();
            var networkMonitor = managersGO.AddComponent<NetworkMonitor>();
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
            videoWindowGO.AddComponent<CompositionLayerVideoRenderer>();

            // ─── 6. UI Control Panel ──────────────────────────────────
            var uiBuilderGO = new GameObject("UI Builder");
            var uiBuilder = uiBuilderGO.AddComponent<UIPanelBuilder>();
            var panelGO = uiBuilder.Build();

            // Create UIController and wire references
            var uiControllerGO = new GameObject("UI Controller");
            var uiController = uiControllerGO.AddComponent<UIController>();

            // Wire UI controller references via reflection (since fields are serialized)
            WireUIController(uiController, sourceDiscovery, receiver, videoDisplay,
                windowController, uiBuilder);

            // ─── 7. App Controller ────────────────────────────────────
            var appControllerGO = new GameObject("App Controller");
            var appController = appControllerGO.AddComponent<AppController>();
            WireAppController(appController, sourceDiscovery, receiver, videoDisplay,
                windowController, uiController);

            // ─── 8. Diagnostics Overlay ───────────────────────────────
            var diagGO = new GameObject("Diagnostics");
            var diagnostics = diagGO.AddComponent<DiagnosticsOverlay>();
            diagnostics.SetReferences(receiver, performanceMonitor);
            diagnostics.Initialize();

            // ─── 9. Wire receiver events ──────────────────────────────
            receiver.OnVideoFrameReceived += (texture, info) =>
            {
                videoDisplay.UpdateVideoFrame(texture, info);
            };

            // Show placeholder initially
            videoDisplay.ShowPlaceholder();
        }

        /// <summary>
        /// Wire UIController serialized fields via reflection.
        /// In a full Unity project, these would be set in the Inspector.
        /// </summary>
        private void WireUIController(UIController uiCtrl,
            NDISourceDiscovery discovery, NDIReceiver receiver,
            NDIVideoDisplay display, SpatialWindowController window,
            UIPanelBuilder builder)
        {
            var type = typeof(UIController);
            var flags = System.Reflection.BindingFlags.NonPublic |
                       System.Reflection.BindingFlags.Instance;

            type.GetField("sourceDiscovery", flags)?.SetValue(uiCtrl, discovery);
            type.GetField("receiver", flags)?.SetValue(uiCtrl, receiver);
            type.GetField("videoDisplay", flags)?.SetValue(uiCtrl, display);
            type.GetField("windowController", flags)?.SetValue(uiCtrl, window);

            type.GetField("sourceDropdown", flags)?.SetValue(uiCtrl, builder.SourceDropdown);
            type.GetField("connectButton", flags)?.SetValue(uiCtrl, builder.ConnectButton);
            type.GetField("connectButtonText", flags)?.SetValue(uiCtrl, builder.ConnectButtonText);
            type.GetField("sbsToggle", flags)?.SetValue(uiCtrl, builder.SBSToggle);
            type.GetField("sbsToggleLabel", flags)?.SetValue(uiCtrl, builder.SBSToggleLabel);
            type.GetField("statusText", flags)?.SetValue(uiCtrl, builder.StatusText);
            type.GetField("resolutionText", flags)?.SetValue(uiCtrl, builder.ResolutionText);
            type.GetField("fpsText", flags)?.SetValue(uiCtrl, builder.FpsText);
            type.GetField("resetPositionButton", flags)?.SetValue(uiCtrl, builder.ResetPositionButton);
            type.GetField("connectionIndicator", flags)?.SetValue(uiCtrl, builder.ConnectionIndicator);
        }

        private void WireAppController(AppController appCtrl,
            NDISourceDiscovery discovery, NDIReceiver receiver,
            NDIVideoDisplay display, SpatialWindowController window,
            UIController uiCtrl)
        {
            var type = typeof(AppController);
            var flags = System.Reflection.BindingFlags.NonPublic |
                       System.Reflection.BindingFlags.Instance;

            type.GetField("sourceDiscovery", flags)?.SetValue(appCtrl, discovery);
            type.GetField("receiver", flags)?.SetValue(appCtrl, receiver);
            type.GetField("videoDisplay", flags)?.SetValue(appCtrl, display);
            type.GetField("windowController", flags)?.SetValue(appCtrl, window);
            type.GetField("uiController", flags)?.SetValue(appCtrl, uiCtrl);
        }
    }
}

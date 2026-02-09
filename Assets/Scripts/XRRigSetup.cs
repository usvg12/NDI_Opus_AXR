// XR Rig Setup - Configures the XR Origin, hand tracking, and interaction system
// For AndroidXR with OpenXR on Samsung Galaxy XR
// Aligned with XR Interaction Toolkit 3.4 and Android XR developer guidelines:
// https://developer.android.com/develop/xr/unity

using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace NDIViewer
{
    /// <summary>
    /// Configures the XR interaction rig at runtime.
    /// Sets up hand tracking interactors, eye gaze, and ray interaction.
    /// Uses XRI 3.4 APIs with the Hand Interaction Profile for Android XR.
    /// Attach to the XR Origin root GameObject.
    /// </summary>
    public class XRRigSetup : MonoBehaviour
    {
        [Header("Interaction Settings")]
        [Tooltip("Maximum ray interaction distance in meters")]
        [SerializeField] private float rayMaxDistance = 10f;

        [Tooltip("Enable eye gaze for UI interaction")]
        [SerializeField] private bool enableEyeGaze = true;

        [Header("Hand Tracking")]
        [Tooltip("Enable hand tracking for grab interactions")]
        [SerializeField] private bool enableHandTracking = true;

        [Header("References (auto-populated if null)")]
        [SerializeField] private Camera xrCamera;
        [SerializeField] private XRInteractionManager interactionManager;

        private void Awake()
        {
            // Ensure interaction manager exists
            if (interactionManager == null)
            {
                interactionManager = FindFirstObjectByType<XRInteractionManager>();
                if (interactionManager == null)
                {
                    var go = new GameObject("XR Interaction Manager");
                    go.transform.SetParent(transform);
                    interactionManager = go.AddComponent<XRInteractionManager>();
                }
            }

            // Ensure XR camera is set
            if (xrCamera == null)
            {
                xrCamera = Camera.main;
            }
        }

        private void Start()
        {
            ConfigureCamera();

            if (enableHandTracking)
            {
                SetupHandInteractors();
            }

            ValidateUIInputPipeline();
            Debug.Log("[XR Rig] Setup complete.");
        }

        /// <summary>
        /// Validates that the XR UI input pipeline is fully wired. Logs warnings
        /// for missing components that would cause silent input failures.
        /// </summary>
        private void ValidateUIInputPipeline()
        {
            var eventSystem = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem == null)
            {
                Debug.LogError("[XR Rig] No EventSystem found. UI input will not work.");
                return;
            }

            var inputModule = eventSystem.GetComponent<XRUIInputModule>();
            if (inputModule == null)
            {
                Debug.LogError("[XR Rig] EventSystem is missing XRUIInputModule. " +
                    "Standard InputModule will not process XR hand/controller input.");
            }
            else if (!inputModule.enableXRInput)
            {
                Debug.LogWarning("[XR Rig] XRUIInputModule.enableXRInput is false. " +
                    "XR controller/hand input will be ignored.");
            }

            // Check for multiple EventSystems (common source of silent breakage)
            var allEventSystems = FindObjectsByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsSortMode.None);
            if (allEventSystems.Length > 1)
            {
                Debug.LogError($"[XR Rig] {allEventSystems.Length} EventSystems found. " +
                    "Multiple EventSystems cause unpredictable input routing. Remove duplicates.");
            }
        }

        private void ConfigureCamera()
        {
            if (xrCamera == null) return;

            // Configure for passthrough: transparent background
            xrCamera.clearFlags = CameraClearFlags.SolidColor;
            xrCamera.backgroundColor = Color.clear;

            // Clipping planes suitable for XR
            xrCamera.nearClipPlane = 0.1f;
            xrCamera.farClipPlane = 100f;

            // Enable stereo rendering
            xrCamera.stereoTargetEye = StereoTargetEyeMask.Both;
        }

        private void SetupHandInteractors()
        {
            // Create left and right hand interactors if they don't exist
            SetupHandInteractor("Left Hand", true);
            SetupHandInteractor("Right Hand", false);
        }

        private void SetupHandInteractor(string handName, bool isLeft)
        {
            // Check if hand interactor already exists
            var existing = transform.Find(handName);
            if (existing != null) return;

            var handGO = new GameObject(handName);
            handGO.transform.SetParent(transform);
            handGO.transform.localPosition = Vector3.zero;
            handGO.transform.localRotation = Quaternion.identity;

            // Add ray interactor for distance interaction (UI panels, window grab)
            // On Android XR with Hand Interaction Profile, the ray origin tracks
            // the hand's aim pose as defined by the OpenXR hand interaction extension.
            var rayInteractor = handGO.AddComponent<XRRayInteractor>();
            rayInteractor.maxRaycastDistance = rayMaxDistance;
            rayInteractor.enableUIInteraction = true;

            // Add direct interactor for close-range grab (pinch gesture)
            var directGO = new GameObject("Direct Interactor");
            directGO.transform.SetParent(handGO.transform);
            directGO.transform.localPosition = Vector3.zero;
            var directInteractor = directGO.AddComponent<XRDirectInteractor>();

            // Add line visual for ray
            var lineRenderer = handGO.AddComponent<LineRenderer>();
            lineRenderer.startWidth = 0.005f;
            lineRenderer.endWidth = 0.002f;
            lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            {
                color = new Color(0.4f, 0.7f, 1f, 0.6f)
            };

            var lineVisual = handGO.AddComponent<XRInteractorLineVisual>();
            lineVisual.lineLength = rayMaxDistance;

            Debug.Log($"[XR Rig] Created {handName} interactor (Hand Interaction Profile).");
        }
    }
}

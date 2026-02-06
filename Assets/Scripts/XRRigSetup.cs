// XR Rig Setup - Configures the XR Origin, hand tracking, and interaction system
// For AndroidXR with OpenXR on Samsung Galaxy XR

using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace NDIViewer
{
    /// <summary>
    /// Configures the XR interaction rig at runtime.
    /// Sets up hand tracking interactors, eye gaze, and ray interaction.
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

            Debug.Log("[XR Rig] Setup complete.");
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
            var rayInteractor = handGO.AddComponent<XRRayInteractor>();
            rayInteractor.maxRaycastDistance = rayMaxDistance;
            rayInteractor.enableUIInteraction = true;

            // Add direct interactor for close-range grab
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

            Debug.Log($"[XR Rig] Created {handName} interactor.");
        }
    }
}

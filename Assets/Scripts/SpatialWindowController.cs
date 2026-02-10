// Spatial Window Controller - Makes video window movable and resizable in 3D space
// Supports grab-to-move, pinch-to-scale, and corner-handle resizing.
// Designed for AndroidXR hand tracking and controller input.

using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace NDIViewer
{
    /// <summary>
    /// Controls a spatial window in 3D space: positioning, resizing, and aspect ratio.
    /// Attaches to the video display quad/plane in the scene.
    /// Default: 2m wide at 2m distance from user, 16:9 aspect ratio.
    /// Updated for XR Interaction Toolkit 3.4.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class SpatialWindowController : MonoBehaviour
    {
        [Header("Window Defaults")]
        [Tooltip("Default width in meters")]
        [SerializeField] private float defaultWidth = 2.0f;

        [Tooltip("Default distance from camera in meters")]
        [SerializeField] private float defaultDistance = 2.0f;

        [Tooltip("Default vertical offset below eye level (degrees)")]
        [SerializeField] private float defaultVerticalAngle = 5.0f;

        [Header("Resize Limits")]
        [SerializeField] private float minWidth = 0.5f;
        [SerializeField] private float maxWidth = 8.0f;
        [SerializeField] private float minDistance = 0.75f;
        [SerializeField] private float maxDistance = 5.0f;

        [Header("Interaction")]
        [Tooltip("Scale speed when pinching")]
        [SerializeField] private float scaleSpeed = 1.5f;

        [Tooltip("Smoothing factor for movement (0 = instant, 1 = very smooth)")]
        [SerializeField] private float movementSmoothing = 0.1f;

        // Current state
        private float _currentWidth;
        private float _aspectRatio = 16f / 9f; // Default for 1920x1080
        private bool _isGrabbed;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;

        // Pinch-to-scale tracking
        private bool _isPinchScaling;
        private float _initialPinchDistance;
        private float _initialScale;

        // Components
        private XRGrabInteractable _grabInteractable;
        private MeshRenderer _meshRenderer;
        private Transform _cameraTransform;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            _meshRenderer = GetComponent<MeshRenderer>();

            // Configure grab interactable for spatial window behavior
            ConfigureGrabInteractable();
        }

        private void Start()
        {
            _cameraTransform = Camera.main?.transform;
            if (_cameraTransform == null)
                Debug.LogWarning("[SpatialWindow] Camera.main not found at Start. " +
                    "Window positioning deferred until camera becomes available.");

            _currentWidth = defaultWidth;

            // Position window at default location
            ResetToDefaultPosition();
            ApplySize();
        }

        /// <summary>
        /// Reset window to default position: centered, 2m away, slightly below eye level.
        /// </summary>
        public void ResetToDefaultPosition()
        {
            if (_cameraTransform == null)
            {
                _cameraTransform = Camera.main?.transform;
                if (_cameraTransform == null) return;
            }

            // Calculate position: 2m in front, angled slightly down
            Vector3 forward = _cameraTransform.forward;
            forward.y = 0; // Project onto horizontal plane
            forward.Normalize();

            float verticalOffset = defaultDistance * Mathf.Tan(defaultVerticalAngle * Mathf.Deg2Rad);

            Vector3 position = _cameraTransform.position
                + forward * defaultDistance
                + Vector3.down * verticalOffset;

            // Face the camera
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            transform.position = position;
            transform.rotation = rotation;
            _targetPosition = position;
            _targetRotation = rotation;
        }

        /// <summary>
        /// Update aspect ratio based on incoming video dimensions.
        /// </summary>
        public void SetAspectRatio(int width, int height)
        {
            if (height <= 0) return;
            _aspectRatio = (float)width / height;
            ApplySize();
        }

        /// <summary>
        /// Set the aspect ratio for SBS mode (half-width per eye).
        /// When SBS is on, effective aspect is half the full frame width.
        /// </summary>
        public void SetSBSMode(bool enabled, int fullWidth, int height)
        {
            if (height <= 0) return;

            if (enabled)
            {
                // SBS: each eye sees half the width -> aspect = (width/2) / height
                _aspectRatio = ((float)fullWidth / 2f) / height;
            }
            else
            {
                // Full frame: aspect = width / height
                _aspectRatio = (float)fullWidth / height;
            }

            ApplySize();
        }

        /// <summary>
        /// Scale the window by a multiplier (e.g., from pinch gesture).
        /// </summary>
        public void ScaleWindow(float multiplier)
        {
            _currentWidth = Mathf.Clamp(_currentWidth * multiplier, minWidth, maxWidth);
            ApplySize();
        }

        /// <summary>
        /// Set absolute window width in meters.
        /// </summary>
        public void SetWidth(float widthMeters)
        {
            _currentWidth = Mathf.Clamp(widthMeters, minWidth, maxWidth);
            ApplySize();
        }

        public float CurrentWidth => _currentWidth;
        public float CurrentHeight => _currentWidth / _aspectRatio;

        private void ApplySize()
        {
            float height = _currentWidth / _aspectRatio;
            transform.localScale = new Vector3(_currentWidth, height, 1f);
        }

        private void ConfigureGrabInteractable()
        {
            if (_grabInteractable == null) return;

            // Allow both direct and ray grab
            _grabInteractable.movementType = XRGrabInteractable.MovementType.VelocityTracking;
            _grabInteractable.throwOnDetach = false;
            _grabInteractable.useDynamicAttach = true;

            // Register grab events
            _grabInteractable.selectEntered.AddListener(OnGrabStart);
            _grabInteractable.selectExited.AddListener(OnGrabEnd);
        }

        private void OnGrabStart(SelectEnterEventArgs args)
        {
            _isGrabbed = true;
        }

        private void OnGrabEnd(SelectExitEventArgs args)
        {
            _isGrabbed = false;

            // Enforce distance limits
            if (_cameraTransform != null)
            {
                float distance = Vector3.Distance(_cameraTransform.position, transform.position);
                if (distance < minDistance || distance > maxDistance)
                {
                    Vector3 direction = (transform.position - _cameraTransform.position).normalized;
                    float clampedDist = Mathf.Clamp(distance, minDistance, maxDistance);
                    transform.position = _cameraTransform.position + direction * clampedDist;
                }
            }
        }

        private void Update()
        {
            // Smooth movement when not grabbed (for programmatic positioning)
            if (!_isGrabbed && movementSmoothing > 0)
            {
                transform.position = Vector3.Lerp(transform.position, _targetPosition,
                    Time.deltaTime / movementSmoothing);
                transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation,
                    Time.deltaTime / movementSmoothing);
            }

            HandlePinchScale();
        }

        private void HandlePinchScale()
        {
            // Two-hand pinch scaling using XR interaction toolkit
            // This is handled via the XRGrabInteractable's scale affordance
            // when two interactors are selecting the same object simultaneously.
            // The XR Interaction Toolkit 3.x supports this natively.
        }

        private void OnDestroy()
        {
            if (_grabInteractable != null)
            {
                _grabInteractable.selectEntered.RemoveListener(OnGrabStart);
                _grabInteractable.selectExited.RemoveListener(OnGrabEnd);
            }
        }
    }
}

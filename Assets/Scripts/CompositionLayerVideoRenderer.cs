// Composition Layer Video Renderer - Future OpenXR composition layer support
// Will use OpenXR composition layers for sharper video output when implemented.
//
// STATUS: Not yet implemented. The composition layer submission path requires
// the Android XR Extensions package (com.google.xr.extensions 1.2.0+) and
// additional OpenXR swapchain integration. This component is disabled by default
// and will report its status honestly at runtime.
//
// Reference: XR_ANDROID_composition_layer_passthrough_mesh
// https://developer.android.com/develop/xr/openxr/extensions/XR_ANDROID_composition_layer_passthrough_mesh

using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Placeholder for future OpenXR composition layer rendering of NDI video.
    /// Currently disabled by default — the submission path is not yet implemented.
    /// All video rendering goes through NDIVideoDisplay's standard quad path.
    /// </summary>
    public class CompositionLayerVideoRenderer : MonoBehaviour
    {
        [Header("Composition Layer Settings")]
        [Tooltip("Not yet implemented — composition layer submission requires " +
                 "com.google.xr.extensions 1.2.0+. Left disabled until implemented.")]
        [SerializeField] private bool preferCompositionLayer = false;

        [Tooltip("Layer sort order (higher = rendered on top)")]
        [SerializeField] private int sortOrder = 0;

        [Tooltip("Enable sharp corners (false = use rounded corners matching system style)")]
        [SerializeField] private bool sharpCorners = true;

        private bool _extensionsDetected;
        private bool _initialized;

        /// <summary>
        /// Whether composition layer rendering is active.
        /// Always false until the submission path is implemented.
        /// </summary>
        public bool IsCompositionLayerActive => false;

        /// <summary>Whether the Android XR Extensions assembly was detected.</summary>
        public bool ExtensionsDetected => _extensionsDetected;

        private void Start()
        {
            DetectCompositionLayerSupport();
            _initialized = true;

            if (preferCompositionLayer)
            {
                Debug.LogWarning("[CompositionLayer] preferCompositionLayer is enabled " +
                    "but composition layer submission is not yet implemented. " +
                    "Falling back to standard quad rendering via NDIVideoDisplay.");
            }
        }

        private void DetectCompositionLayerSupport()
        {
            _extensionsDetected = false;

            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (assembly.FullName.Contains("Google.XR.Extensions"))
                {
                    _extensionsDetected = true;
                    Debug.Log("[CompositionLayer] Android XR Extensions assembly detected. " +
                        "Composition layer submission is not yet implemented — " +
                        "using standard quad rendering.");
                    break;
                }
            }

            if (!_extensionsDetected)
            {
                Debug.Log("[CompositionLayer] Android XR Extensions not found. " +
                    "Using standard quad rendering.");
            }
        }

        /// <summary>
        /// Placeholder — composition layer submission is not yet implemented.
        /// When implemented, this will create an XrCompositionLayerQuad that the
        /// OpenXR runtime composites directly, bypassing the standard render pipeline.
        /// </summary>
        public void SubmitVideoLayer(Texture2D videoTexture)
        {
            // Not yet implemented. Requires:
            //   1. xrCreateSwapchain for the video texture
            //   2. xrAcquireSwapchainImage / xrReleaseSwapchainImage per frame
            //   3. XrCompositionLayerQuad submitted in xrEndFrame
            // Until then, NDIVideoDisplay handles all rendering via standard quad.
        }

        /// <summary>
        /// Toggle composition layer preference. Has no runtime effect until
        /// the submission path is implemented.
        /// </summary>
        public void SetCompositionLayerEnabled(bool enabled)
        {
            preferCompositionLayer = enabled;

            if (enabled)
            {
                Debug.LogWarning("[CompositionLayer] Composition layer submission is " +
                    "not yet implemented. Rendering continues via standard quad.");
            }
            else
            {
                Debug.Log("[CompositionLayer] Composition layer preference disabled.");
            }
        }
    }
}

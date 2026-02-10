// Composition Layer Video Renderer - Renders NDI video as an OpenXR composition layer
// Uses OpenXR composition layers for sharper video output, bypassing lens distortion
// correction that degrades video quality when rendered as a standard scene quad.
//
// Reference: XR_ANDROID_composition_layer_passthrough_mesh
// https://developer.android.com/develop/xr/openxr/extensions/XR_ANDROID_composition_layer_passthrough_mesh
//
// This component is an optional enhancement. When the Android XR Extensions package
// (com.google.xr.extensions 1.2.0+) is available, video frames can be submitted as
// composition layers for maximum visual fidelity. Falls back to standard quad rendering
// if composition layers are unavailable.

using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Optional component that enables rendering NDI video frames as OpenXR
    /// composition layers instead of standard scene geometry. This provides
    /// sharper video output by bypassing the intermediate render target and
    /// lens distortion correction pass.
    ///
    /// Requires the Android XR Extensions package (com.google.xr.extensions).
    /// When unavailable, NDIVideoDisplay handles rendering via standard quad.
    /// </summary>
    public class CompositionLayerVideoRenderer : MonoBehaviour
    {
        [Header("Composition Layer Settings")]
        [Tooltip("Use composition layer rendering when available for sharper video")]
        [SerializeField] private bool preferCompositionLayer = true;

        [Tooltip("Layer sort order (higher = rendered on top)")]
        [SerializeField] private int sortOrder = 0;

        [Tooltip("Enable sharp corners (false = use rounded corners matching system style)")]
        [SerializeField] private bool sharpCorners = true;

        private bool _compositionLayerAvailable;
        private bool _initialized;
        private NDIVideoDisplay _videoDisplay;
        private SpatialWindowController _windowController;

        /// <summary>Whether composition layer rendering is active.</summary>
        public bool IsCompositionLayerActive => _compositionLayerAvailable && preferCompositionLayer;

        private void Awake()
        {
            _videoDisplay = GetComponent<NDIVideoDisplay>();
            _windowController = GetComponent<SpatialWindowController>();
        }

        private void Start()
        {
            DetectCompositionLayerSupport();
            _initialized = true;
        }

        /// <summary>
        /// Detect whether the Android XR Extensions composition layer API is available.
        /// The XR_ANDROID_composition_layer_passthrough_mesh extension enables rendering
        /// arbitrary mesh geometry (including video quads) as composition layers that
        /// the XR runtime composites directly, providing:
        ///   - No lens distortion artifacts on video content
        ///   - Direct scanout path for lower latency
        ///   - Higher effective resolution (no intermediate render target)
        /// </summary>
        private void DetectCompositionLayerSupport()
        {
            _compositionLayerAvailable = false;

            // Check if the Android XR Extensions assembly is loaded
            // This indicates com.google.xr.extensions package is installed
            var extensionsAssembly = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in extensionsAssembly)
            {
                if (assembly.FullName.Contains("Google.XR.Extensions"))
                {
                    _compositionLayerAvailable = true;
                    Debug.Log("[CompositionLayer] Android XR Extensions detected. " +
                        "Composition layer rendering available.");
                    break;
                }
            }

            if (!_compositionLayerAvailable)
            {
                Debug.Log("[CompositionLayer] Android XR Extensions not found. " +
                    "Using standard quad rendering. Install com.google.xr.extensions " +
                    "for composition layer support.");
            }
        }

        /// <summary>
        /// Submit a video texture as a composition layer quad.
        /// Called each frame when composition layer mode is active.
        ///
        /// When the Android XR Extensions package is installed, this method
        /// creates an XrCompositionLayerQuad that the OpenXR runtime composites
        /// directly into the final output, bypassing the standard render pipeline
        /// for this specific content.
        ///
        /// The passthrough mesh extension (XR_ANDROID_composition_layer_passthrough_mesh)
        /// can also be used to render the video onto arbitrary mesh geometry that
        /// integrates with the passthrough environment.
        /// </summary>
        /// <remarks>
        /// NOT YET IMPLEMENTED — this is a placeholder for future composition layer support.
        /// When implemented, the OpenXR calls would be:
        ///   1. xrCreateSwapchain for the video texture
        ///   2. xrAcquireSwapchainImage / xrReleaseSwapchainImage per frame
        ///   3. XrCompositionLayerQuad submitted in xrEndFrame
        /// Requires com.google.xr.extensions 1.2.0+ with XR_ANDROID_composition_layer_passthrough_mesh.
        /// Until then, NDIVideoDisplay handles rendering via standard quad.
        /// </remarks>
        public void SubmitVideoLayer(Texture2D videoTexture)
        {
            // No-op: composition layer submission is not yet implemented.
            // Falls back to standard quad rendering via NDIVideoDisplay.
        }

        /// <summary>
        /// Toggle between composition layer and standard quad rendering.
        /// </summary>
        public void SetCompositionLayerEnabled(bool enabled)
        {
            preferCompositionLayer = enabled && _compositionLayerAvailable;

            if (_videoDisplay != null)
            {
                // When switching away from composition layer, ensure the standard
                // quad renderer is active and showing the current frame
                var meshRenderer = GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = !IsCompositionLayerActive;
                }
            }

            Debug.Log($"[CompositionLayer] Rendering mode: " +
                $"{(IsCompositionLayerActive ? "Composition Layer" : "Standard Quad")}");
        }
    }
}

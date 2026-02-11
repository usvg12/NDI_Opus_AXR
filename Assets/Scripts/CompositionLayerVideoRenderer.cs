// Composition Layer Video Renderer - OpenXR composition layer submission for NDI video
// Submits NDI video frames as an XR composition layer quad, bypassing the standard
// eye texture for sharper video output. Falls back to NDIVideoDisplay's quad path
// when composition layers are unavailable.
//
// Uses the Unity XR Composition Layers package (com.unity.xr.compositionlayers)
// via the XR_COMPOSITION_LAYERS_AVAILABLE define. When the package is not installed
// or the runtime does not support composition layers, this component gracefully
// falls back to standard quad rendering.

using System;
using UnityEngine;

#if XR_COMPOSITION_LAYERS_AVAILABLE
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Layers;
using Unity.XR.CompositionLayers.Extensions;
#endif

namespace NDIViewer
{
    /// <summary>
    /// Renders NDI video frames via OpenXR composition layers for maximum sharpness.
    /// The XR runtime composites the video texture directly at display resolution,
    /// bypassing the eye texture entirely. When composition layers are unavailable,
    /// rendering falls back to the standard quad path (NDIVideoDisplay + shader).
    /// </summary>
    public class CompositionLayerVideoRenderer : MonoBehaviour
    {
        [Header("Composition Layer Settings")]
        [Tooltip("Prefer composition layer rendering when available.")]
        [SerializeField] private bool preferCompositionLayer = false;

        // References wired by SceneBootstrapper.SetReferences()
        private NDIReceiver _receiver;
        private NDIVideoDisplay _videoDisplay;
        private SpatialWindowController _windowController;
        private MeshRenderer _meshRenderer;

        // State
        private bool _compositionLayerActive;
        private bool _compositionLayerSupported;
        private bool _sbsEnabled;
        private bool _initialized;
        private bool _subscribedToFrames;

#if XR_COMPOSITION_LAYERS_AVAILABLE
        private CompositionLayer _compositionLayer;
        private TexturesExtension _texturesExtension;

        // SBS stereo: separate render textures for left/right eye halves
        private RenderTexture _leftEyeRT;
        private RenderTexture _rightEyeRT;
#endif

        /// <summary>Whether composition layer rendering is currently active.</summary>
        public bool IsCompositionLayerActive => _compositionLayerActive;

        /// <summary>Whether the runtime supports composition layer rendering.</summary>
        public bool IsCompositionLayerSupported => _compositionLayerSupported;

        /// <summary>Whether the user prefers composition layer rendering.</summary>
        public bool PreferCompositionLayer => preferCompositionLayer;

        /// <summary>
        /// Wire component references. Called by SceneBootstrapper after component creation.
        /// </summary>
        public void SetReferences(
            NDIReceiver receiver,
            NDIVideoDisplay videoDisplay,
            SpatialWindowController windowController)
        {
            _receiver = receiver;
            _videoDisplay = videoDisplay;
            _windowController = windowController;
        }

        private void Start()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            DetectCompositionLayerSupport();
            _initialized = true;

            if (preferCompositionLayer)
            {
                SetCompositionLayerEnabled(true);
            }
        }

        private void DetectCompositionLayerSupport()
        {
#if XR_COMPOSITION_LAYERS_AVAILABLE
            _compositionLayerSupported = true;
            Debug.Log("[CompositionLayer] XR Composition Layers package available. " +
                "Composition layer rendering can be enabled.");
#else
            _compositionLayerSupported = false;
            Debug.Log("[CompositionLayer] XR Composition Layers package not available. " +
                "Using standard quad rendering.");
#endif
        }

        /// <summary>
        /// Enable or disable composition layer rendering. When enabled, the MeshRenderer
        /// is hidden and video frames are submitted as a composition layer. When disabled
        /// or unavailable, the standard quad rendering path (NDIVideoDisplay) is used.
        /// </summary>
        public void SetCompositionLayerEnabled(bool enabled)
        {
            if (enabled && !_compositionLayerSupported)
            {
                Debug.LogWarning("[CompositionLayer] Cannot enable — " +
                    "composition layers not supported on this configuration.");
                return;
            }

            preferCompositionLayer = enabled;

            if (enabled == _compositionLayerActive) return;

            if (enabled)
            {
                ActivateCompositionLayer();
            }
            else
            {
                DeactivateCompositionLayer();
            }
        }

        /// <summary>Toggle composition layer mode and return new state.</summary>
        public bool ToggleCompositionLayer()
        {
            SetCompositionLayerEnabled(!_compositionLayerActive);
            return _compositionLayerActive;
        }

        /// <summary>
        /// Set SBS stereo mode. In composition layer mode, this splits the texture
        /// into left/right eye halves submitted as separate per-eye textures.
        /// </summary>
        public void SetSBSMode(bool enabled)
        {
            _sbsEnabled = enabled;
        }

        /// <summary>
        /// Pure logic: determine the render mode string for diagnostics display.
        /// </summary>
        internal static string GetRenderModeString(bool compositionLayerActive, bool compositionLayerSupported)
        {
            if (compositionLayerActive)
                return "Composition Layer";
            if (compositionLayerSupported)
                return "Quad (comp layer available)";
            return "Quad";
        }

        /// <summary>
        /// Pure logic: determine whether composition layer can be activated given current state.
        /// </summary>
        internal static bool CanActivateCompositionLayer(bool supported, bool alreadyActive)
        {
            return supported && !alreadyActive;
        }

        private void ActivateCompositionLayer()
        {
#if XR_COMPOSITION_LAYERS_AVAILABLE
            if (!CreateCompositionLayerComponents())
            {
                Debug.LogWarning("[CompositionLayer] Failed to create composition layer. " +
                    "Staying on quad rendering.");
                return;
            }

            _compositionLayer.enabled = true;

            // Hide the mesh renderer so the quad path doesn't render
            if (_meshRenderer != null)
                _meshRenderer.enabled = false;

            _compositionLayerActive = true;

            // Subscribe to NDI frame events for composition layer updates
            SubscribeToFrameEvents();

            Debug.Log("[CompositionLayer] Composition layer activated. " +
                "Video bypasses eye texture for sharper output.");
#else
            Debug.LogWarning("[CompositionLayer] Composition layers not available at compile time.");
#endif
        }

        private void DeactivateCompositionLayer()
        {
#if XR_COMPOSITION_LAYERS_AVAILABLE
            if (_compositionLayer != null)
                _compositionLayer.enabled = false;
#endif

            // Re-enable the mesh renderer for quad path
            if (_meshRenderer != null)
                _meshRenderer.enabled = true;

            _compositionLayerActive = false;

            // Unsubscribe from frame events (NDIVideoDisplay handles its own via UIController)
            UnsubscribeFromFrameEvents();

            Debug.Log("[CompositionLayer] Composition layer deactivated. Using quad rendering.");
        }

        private void SubscribeToFrameEvents()
        {
            if (_subscribedToFrames || _receiver == null) return;
            _receiver.OnVideoFrameReceived += OnVideoFrameReceived;
            _subscribedToFrames = true;
        }

        private void UnsubscribeFromFrameEvents()
        {
            if (!_subscribedToFrames || _receiver == null) return;
            _receiver.OnVideoFrameReceived -= OnVideoFrameReceived;
            _subscribedToFrames = false;
        }

#if XR_COMPOSITION_LAYERS_AVAILABLE
        private bool CreateCompositionLayerComponents()
        {
            try
            {
                if (_compositionLayer == null)
                {
                    _compositionLayer = gameObject.AddComponent<CompositionLayer>();
                    _compositionLayer.ChangeLayerDataType(typeof(QuadLayerData));
                    _compositionLayer.AddSuggestedExtensions();
                    _compositionLayer.enabled = false;
                }

                _texturesExtension = GetComponent<TexturesExtension>();
                if (_texturesExtension == null)
                {
                    Debug.LogError("[CompositionLayer] TexturesExtension not created by " +
                        "AddSuggestedExtensions(). Composition layers cannot function.");
                    CleanupCompositionLayerComponents();
                    return false;
                }

                Debug.Log("[CompositionLayer] Composition layer components created successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CompositionLayer] Error creating composition layer: {ex.Message}");
                CleanupCompositionLayerComponents();
                return false;
            }
        }

        private void CleanupCompositionLayerComponents()
        {
            // Destroy extensions first, then the layer (per Unity docs)
            var extensions = GetComponents<CompositionLayerExtension>();
            foreach (var ext in extensions)
                Destroy(ext);

            if (_compositionLayer != null)
            {
                Destroy(_compositionLayer);
                _compositionLayer = null;
            }

            _texturesExtension = null;
        }
#endif

        private void OnVideoFrameReceived(Texture2D texture, NDIReceiver.FrameInfo info)
        {
            if (!_compositionLayerActive) return;

#if XR_COMPOSITION_LAYERS_AVAILABLE
            if (_texturesExtension == null) return;

            if (_sbsEnabled)
            {
                UpdateSBSTextures(texture, info);
            }
            else
            {
                // Non-SBS: full texture to both eyes
                _texturesExtension.LeftTexture = texture;
                _texturesExtension.TargetEye = TexturesExtension.TargetEyeEnum.Both;
            }

            // Update window aspect ratio
            if (_windowController != null)
            {
                _windowController.SetSBSMode(_sbsEnabled, info.Width, info.Height);
            }
#endif
        }

#if XR_COMPOSITION_LAYERS_AVAILABLE
        private void UpdateSBSTextures(Texture2D texture, NDIReceiver.FrameInfo info)
        {
            int halfWidth = info.Width / 2;
            int height = info.Height;

            // Create or resize per-eye render textures
            if (_leftEyeRT == null || _leftEyeRT.width != halfWidth || _leftEyeRT.height != height)
            {
                ReleaseEyeRenderTextures();
                _leftEyeRT = new RenderTexture(halfWidth, height, 0, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _rightEyeRT = new RenderTexture(halfWidth, height, 0, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _leftEyeRT.Create();
                _rightEyeRT.Create();

                Debug.Log($"[CompositionLayer] SBS eye textures created: {halfWidth}x{height}");
            }

            // Blit left half (u: 0.0-0.5) and right half (u: 0.5-1.0) into separate textures.
            // Scale=(0.5, 1) crops to half width; offset shifts the sample origin.
            Graphics.Blit(texture, _leftEyeRT, new Vector2(0.5f, 1f), new Vector2(0f, 0f));
            Graphics.Blit(texture, _rightEyeRT, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f));

            _texturesExtension.LeftTexture = _leftEyeRT;
            _texturesExtension.RightTexture = _rightEyeRT;
            _texturesExtension.TargetEye = TexturesExtension.TargetEyeEnum.Individual;
        }

        private void ReleaseEyeRenderTextures()
        {
            if (_leftEyeRT != null)
            {
                _leftEyeRT.Release();
                Destroy(_leftEyeRT);
                _leftEyeRT = null;
            }
            if (_rightEyeRT != null)
            {
                _rightEyeRT.Release();
                Destroy(_rightEyeRT);
                _rightEyeRT = null;
            }
        }
#endif

        private void OnDestroy()
        {
            UnsubscribeFromFrameEvents();

#if XR_COMPOSITION_LAYERS_AVAILABLE
            ReleaseEyeRenderTextures();
            CleanupCompositionLayerComponents();
#endif
        }
    }
}

// Composition Layer Video Renderer - OpenXR composition layer submission for NDI video
// Submits NDI video frames as an XR composition layer quad, bypassing the standard
// eye texture for sharper video output. Falls back to NDIVideoDisplay's quad path
// when composition layers are unavailable.
//
// Uses the Unity XR Composition Layers package (com.unity.xr.compositionlayers)
// via the XR_COMPOSITION_LAYERS_AVAILABLE define. When the package is not installed
// or the runtime does not support composition layers, this component gracefully
// falls back to standard quad rendering.
//
// Alpha channel: NDI sources using RGBX FourCC have an undefined alpha byte that
// may be 0x00. The XR compositor respects alpha (unlike the standard Opaque quad
// shader), so we force alpha=1 via a dedicated blit material to prevent invisible
// video in the composition layer path.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

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
        private SpatialWindowController _windowController;
        private MeshRenderer _meshRenderer;

        // State
        private bool _compositionLayerActive;
        private bool _compositionLayerSupported;
        private bool _sbsEnabled;
        private bool _subscribedToFrames;

#if XR_COMPOSITION_LAYERS_AVAILABLE
        private CompositionLayer _compositionLayer;
        private TexturesExtension _texturesExtension;

        // SBS stereo: separate render textures for left/right eye halves
        private RenderTexture _leftEyeRT;
        private RenderTexture _rightEyeRT;

        // Non-SBS: single render texture with alpha forced opaque
        private RenderTexture _opaqueRT;
#endif

        // Material that copies RGB and forces alpha=1 via OpaqueBlitCopy shader.
        // Shared across SBS and non-SBS paths to prevent invisible video when
        // NDI sources deliver RGBX with undefined (potentially 0x00) alpha bytes.
        // The shader lives at Assets/Shaders/NDI_OpaqueBlitCopy.shader and must be
        // included in "Always Included Shaders" or referenced by a material in the build.
        private Material _opaqueBlitMaterial;

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
            SpatialWindowController windowController)
        {
            _receiver = receiver;
            _windowController = windowController;
        }

        private void Start()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            DetectCompositionLayerSupport();

            if (preferCompositionLayer)
            {
                SetCompositionLayerEnabled(true);
            }
        }

        private void DetectCompositionLayerSupport()
        {
#if XR_COMPOSITION_LAYERS_AVAILABLE
            // Compile-time: package is available. Now verify at runtime that an
            // XR display subsystem is actually running (package installed but no
            // active XR session means composition layers won't function).
            bool runtimeReady = false;
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (display.running)
                {
                    runtimeReady = true;
                    break;
                }
            }

            _compositionLayerSupported = runtimeReady;

            if (runtimeReady)
            {
                Debug.Log("[CompositionLayer] XR Composition Layers package available and " +
                    "XR display subsystem running. Composition layer rendering can be enabled.");
            }
            else
            {
                Debug.LogWarning("[CompositionLayer] XR Composition Layers package available " +
                    "but no XR display subsystem is running. Composition layers disabled " +
                    "until an XR session starts.");
            }
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
        /// Returns true if the requested state was achieved.
        /// </summary>
        public bool SetCompositionLayerEnabled(bool enabled)
        {
            if (enabled && !_compositionLayerSupported)
            {
                Debug.LogWarning("[CompositionLayer] Cannot enable — " +
                    "composition layers not supported on this configuration.");
                return false;
            }

            preferCompositionLayer = enabled;

            if (enabled == _compositionLayerActive) return true;

            if (enabled)
            {
                ActivateCompositionLayer();
                // Activation may fail (runtime rejects the layer). Report actual state.
                return _compositionLayerActive;
            }
            else
            {
                DeactivateCompositionLayer();
                return true;
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

        /// <summary>
        /// Pure logic: compute the half-width dimension for SBS eye textures.
        /// </summary>
        internal static int ComputeSBSHalfWidth(int fullWidth)
        {
            return fullWidth / 2;
        }

        /// <summary>
        /// Pure logic: determine whether eye render textures need reallocation.
        /// </summary>
        internal static bool NeedReallocateEyeTextures(
            int currentWidth, int currentHeight, int targetHalfWidth, int targetHeight)
        {
            return currentWidth != targetHalfWidth || currentHeight != targetHeight;
        }

        private void ActivateCompositionLayer()
        {
#if XR_COMPOSITION_LAYERS_AVAILABLE
            if (!CreateCompositionLayerComponents())
            {
                Debug.LogWarning("[CompositionLayer] Failed to create composition layer. " +
                    "Staying on quad rendering.");
                // Reset preference so the toggle syncs back
                preferCompositionLayer = false;
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
            preferCompositionLayer = false;
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

        /// <summary>
        /// Get or create the blit material that forces alpha=1. Lazily created on first use.
        /// </summary>
        private Material GetOpaqueBlitMaterial()
        {
            if (_opaqueBlitMaterial != null) return _opaqueBlitMaterial;

            var shader = Shader.Find("Hidden/NDIViewer/OpaqueBlitCopy");
            if (shader == null)
            {
                // Shader not in project as a file; create from inline source.
                // ShaderUtil is editor-only, so at runtime we fall back to a
                // manual alpha-force approach if the shader isn't pre-compiled.
                // For runtime, the shader must be included via "Always Included Shaders"
                // or exist as a .shader asset. As a last resort, fall back to the
                // default blit (alpha passthrough) and force alpha in the source texture.
                Debug.LogWarning("[CompositionLayer] OpaqueBlitCopy shader not found. " +
                    "Falling back to standard Blit (alpha may be incorrect).");
                return null;
            }

            _opaqueBlitMaterial = new Material(shader);
            return _opaqueBlitMaterial;
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
                UpdateNonSBSTexture(texture, info);
            }

            // Update window aspect ratio
            if (_windowController != null)
            {
                _windowController.SetSBSMode(_sbsEnabled, info.Width, info.Height);
            }
#endif
        }

#if XR_COMPOSITION_LAYERS_AVAILABLE
        /// <summary>
        /// Non-SBS path: blit the full NDI texture into an opaque render texture
        /// (alpha forced to 1) and submit to the composition layer for both eyes.
        /// </summary>
        private void UpdateNonSBSTexture(Texture2D texture, NDIReceiver.FrameInfo info)
        {
            int w = info.Width;
            int h = info.Height;

            // Create or resize the opaque RT
            if (_opaqueRT == null || _opaqueRT.width != w || _opaqueRT.height != h)
            {
                if (_opaqueRT != null)
                {
                    _opaqueRT.Release();
                    Destroy(_opaqueRT);
                }
                _opaqueRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _opaqueRT.Create();
            }

            // Blit with alpha=1 forced via material, or standard blit as fallback
            var mat = GetOpaqueBlitMaterial();
            if (mat != null)
            {
                Graphics.Blit(texture, _opaqueRT, mat);
            }
            else
            {
                // Fallback: standard blit (alpha passthrough). Force alpha in-place
                // on the source texture as a last resort.
                ForceTextureAlphaOpaque(texture);
                Graphics.Blit(texture, _opaqueRT);
            }

            _texturesExtension.LeftTexture = _opaqueRT;
            _texturesExtension.TargetEye = TexturesExtension.TargetEyeEnum.Both;
        }

        private void UpdateSBSTextures(Texture2D texture, NDIReceiver.FrameInfo info)
        {
            int halfWidth = ComputeSBSHalfWidth(info.Width);
            int height = info.Height;

            // Create or resize per-eye render textures
            if (_leftEyeRT == null ||
                NeedReallocateEyeTextures(_leftEyeRT.width, _leftEyeRT.height, halfWidth, height))
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
            // Use the opaque blit material to force alpha=1, preventing invisible video
            // from NDI RGBX sources with undefined alpha bytes.
            var mat = GetOpaqueBlitMaterial();
            if (mat != null)
            {
                // Material-based blit with alpha=1 (preferred path)
                mat.SetTexture("_MainTex", texture);
                mat.SetTextureScale("_MainTex", new Vector2(0.5f, 1f));

                mat.SetTextureOffset("_MainTex", new Vector2(0f, 0f));
                Graphics.Blit(texture, _leftEyeRT, mat);

                mat.SetTextureOffset("_MainTex", new Vector2(0.5f, 0f));
                Graphics.Blit(texture, _rightEyeRT, mat);

                // Reset to avoid polluting state
                mat.SetTextureScale("_MainTex", Vector2.one);
                mat.SetTextureOffset("_MainTex", Vector2.zero);
            }
            else
            {
                // Fallback: standard blit (alpha passthrough). Force alpha first.
                ForceTextureAlphaOpaque(texture);
                Graphics.Blit(texture, _leftEyeRT, new Vector2(0.5f, 1f), new Vector2(0f, 0f));
                Graphics.Blit(texture, _rightEyeRT, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f));
            }

            _texturesExtension.LeftTexture = _leftEyeRT;
            _texturesExtension.RightTexture = _rightEyeRT;
            _texturesExtension.TargetEye = TexturesExtension.TargetEyeEnum.Individual;
        }

        /// <summary>
        /// Last-resort fallback: force alpha=0xFF directly in the texture pixel data.
        /// Only used when the opaque blit shader is unavailable. This modifies the
        /// source texture in-place, which also affects the quad path (harmless since
        /// the quad shader ignores alpha on Opaque geometry).
        /// </summary>
        private static void ForceTextureAlphaOpaque(Texture2D texture)
        {
            var pixels = texture.GetRawTextureData<byte>();
            // RGBA32: every 4th byte is alpha
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF;
            }
            texture.Apply(false);
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
            if (_opaqueRT != null)
            {
                _opaqueRT.Release();
                Destroy(_opaqueRT);
                _opaqueRT = null;
            }
            CleanupCompositionLayerComponents();
#endif

            if (_opaqueBlitMaterial != null)
            {
                Destroy(_opaqueBlitMaterial);
                _opaqueBlitMaterial = null;
            }
        }
    }
}

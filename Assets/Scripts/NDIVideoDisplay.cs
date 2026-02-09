// NDI Video Display - Renders NDI video frames onto a spatial 3D quad
// Manages the SBS shader material and stereo rendering.
// Includes test pattern fallback when NDI library is unavailable.

using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Renders NDI video frames onto the spatial window mesh.
    /// Controls the SBS stereo shader and texture updates.
    /// When NDI is unavailable, displays a built-in SBS test pattern
    /// with a status message overlay.
    /// Attach to the same GameObject as SpatialWindowController.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    public class NDIVideoDisplay : MonoBehaviour
    {
        [Header("Shader References")]
        [Tooltip("The SBS stereo shader to use for rendering")]
        [SerializeField] private Shader sbsShader;

        [Header("Display Settings")]
        [Tooltip("Initial brightness (0.5 - 2.0)")]
        [SerializeField] private float brightness = 1.0f;

        [Tooltip("Initial contrast (0.5 - 2.0)")]
        [SerializeField] private float contrast = 1.0f;

        // Material and shader property IDs (cached for performance)
        private Material _material;
        private static readonly int MainTexProp = Shader.PropertyToID("_MainTex");
        private static readonly int SBSEnabledProp = Shader.PropertyToID("_SBSEnabled");
        private static readonly int BrightnessProp = Shader.PropertyToID("_Brightness");
        private static readonly int ContrastProp = Shader.PropertyToID("_Contrast");

        // State
        private bool _sbsEnabled;
        private SpatialWindowController _windowController;
        private MeshRenderer _meshRenderer;
        private bool _testPatternActive;
        private Texture2D _placeholderTexture;

        public bool SBSEnabled => _sbsEnabled;
        public Material DisplayMaterial => _material;
        public bool IsTestPatternActive => _testPatternActive;

        private void Awake()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            _windowController = GetComponent<SpatialWindowController>();

            // Create a quad mesh if one doesn't exist
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter.sharedMesh == null)
            {
                meshFilter.sharedMesh = CreateVideoQuad();
            }

            InitializeMaterial();
        }

        private void InitializeMaterial()
        {
            // Use provided shader or find it by name
            if (sbsShader == null)
            {
                sbsShader = Shader.Find("NDIViewer/SBS_Stereo");
            }

            if (sbsShader == null)
            {
                Debug.LogError("[NDI Display] SBS_Stereo shader not found. Using Unlit fallback.");
                sbsShader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            _material = new Material(sbsShader);
            _material.SetFloat(SBSEnabledProp, 0f);
            _material.SetFloat(BrightnessProp, brightness);
            _material.SetFloat(ContrastProp, contrast);

            _meshRenderer.material = _material;
        }

        /// <summary>
        /// Update the display with a new NDI video frame texture.
        /// Called by NDIReceiver.OnVideoFrameReceived.
        /// </summary>
        public void UpdateVideoFrame(Texture2D texture, NDIReceiver.FrameInfo info)
        {
            if (_material == null || texture == null) return;

            _testPatternActive = false;
            _material.SetTexture(MainTexProp, texture);

            // Update window aspect ratio based on video dimensions and SBS mode
            if (_windowController != null)
            {
                _windowController.SetSBSMode(_sbsEnabled, info.Width, info.Height);
            }
        }

        /// <summary>
        /// Toggle SBS 3D stereo mode.
        /// OFF: Full SBS frame displayed as-is on single plane.
        /// ON: Left half to left eye, right half to right eye for stereoscopic 3D.
        /// </summary>
        public void SetSBSMode(bool enabled)
        {
            _sbsEnabled = enabled;

            if (_material != null)
            {
                _material.SetFloat(SBSEnabledProp, enabled ? 1f : 0f);
            }

            Debug.Log($"[NDI Display] SBS 3D Mode: {(enabled ? "ON" : "OFF")}");
        }

        /// <summary>Toggle SBS mode and return new state.</summary>
        public bool ToggleSBSMode()
        {
            SetSBSMode(!_sbsEnabled);
            return _sbsEnabled;
        }

        /// <summary>Set brightness (0.5 - 2.0).</summary>
        public void SetBrightness(float value)
        {
            brightness = Mathf.Clamp(value, 0.5f, 2.0f);
            if (_material != null) _material.SetFloat(BrightnessProp, brightness);
        }

        /// <summary>Set contrast (0.5 - 2.0).</summary>
        public void SetContrast(float value)
        {
            contrast = Mathf.Clamp(value, 0.5f, 2.0f);
            if (_material != null) _material.SetFloat(ContrastProp, contrast);
        }

        /// <summary>
        /// Show a solid color when no video is playing (e.g., disconnected state).
        /// </summary>
        public void ShowPlaceholder()
        {
            if (_material == null) return;

            _testPatternActive = false;

            // Reuse placeholder texture (avoid per-call allocation)
            if (_placeholderTexture == null)
            {
                _placeholderTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color32[16];
                for (int i = 0; i < 16; i++)
                    pixels[i] = new Color32(20, 20, 25, 255);
                _placeholderTexture.SetPixels32(pixels);
                _placeholderTexture.Apply();
            }

            _material.SetTexture(MainTexProp, _placeholderTexture);
        }

        /// <summary>
        /// Switch to the built-in SBS test pattern. Called when NDI library is unavailable.
        /// The test pattern animates each frame to show the display pipeline is working.
        /// </summary>
        public void ShowTestPattern()
        {
            if (_material == null) return;

            _testPatternActive = true;
            var tex = TestPatternGenerator.GetTexture();
            _material.SetTexture(MainTexProp, tex);

            // Set SBS mode on and configure aspect ratio for the test pattern
            SetSBSMode(true);
            if (_windowController != null)
            {
                _windowController.SetSBSMode(true,
                    TestPatternGenerator.PATTERN_WIDTH, TestPatternGenerator.PATTERN_HEIGHT);
            }

            Debug.Log("[NDI Display] Test pattern active (NDI library unavailable). " +
                "SBS mode enabled for stereo validation.");
        }

        private void Update()
        {
            // Animate test pattern if active
            if (_testPatternActive && _material != null)
            {
                var tex = TestPatternGenerator.UpdatePattern();
                _material.SetTexture(MainTexProp, tex);
            }
        }

        /// <summary>Create a simple quad mesh for video display.</summary>
        private Mesh CreateVideoQuad()
        {
            var mesh = new Mesh { name = "NDI_VideoQuad" };

            // Unit quad centered at origin, facing -Z (toward camera)
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f)
            };

            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };

            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.normals = new Vector3[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back
            };

            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
            if (_placeholderTexture != null)
            {
                Destroy(_placeholderTexture);
            }
        }
    }
}

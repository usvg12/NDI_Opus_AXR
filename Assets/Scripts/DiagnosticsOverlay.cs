// Diagnostics Overlay - Lightweight on-screen stats for validating NDI streaming performance
// Shows FPS, frame timing, dropped frames, upload time, and stride/format diagnostics.

using UnityEngine;
using TMPro;

namespace NDIViewer
{
    /// <summary>
    /// Displays real-time diagnostic stats on a world-space text overlay.
    /// Attach to any GameObject; call SetReferences() to wire NDIReceiver and PerformanceMonitor.
    /// Toggle visibility with SetVisible(). Stats update every 0.5s to avoid per-frame string allocs.
    /// </summary>
    public class DiagnosticsOverlay : MonoBehaviour
    {
        private NDIReceiver _receiver;
        private PerformanceMonitor _perfMonitor;
        private TMP_Text _statsText;
        private Canvas _canvas;
        private GameObject _overlayRoot;

        private float _updateTimer;
        private const float UPDATE_INTERVAL = 0.5f;
        private bool _visible;

        // Cached stats to avoid per-frame string formatting
        private int _lastDropped;
        private int _lastTotal;
        private float _lastUploadMs;
        private float _lastRecvFps;
        private float _lastRenderFps;
        private int _lastStrideFixups;
        private int _lastFormatMismatches;
        private float _lastResScale;

        public bool IsVisible => _visible;

        public void SetReferences(NDIReceiver receiver, PerformanceMonitor perfMonitor)
        {
            _receiver = receiver;
            _perfMonitor = perfMonitor;
        }

        public void Initialize()
        {
            BuildOverlay();
            SetVisible(false); // Hidden by default
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_overlayRoot != null)
                _overlayRoot.SetActive(visible);
        }

        public void ToggleVisible()
        {
            SetVisible(!_visible);
        }

        private void BuildOverlay()
        {
            _overlayRoot = new GameObject("DiagnosticsOverlay");
            _overlayRoot.transform.SetParent(transform, false);

            // World-space canvas, small, attached near the main camera
            var canvasGO = new GameObject("DiagCanvas");
            canvasGO.transform.SetParent(_overlayRoot.transform, false);

            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            var rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400, 200);
            rt.localScale = Vector3.one * 0.0008f; // ~0.8mm per pixel
            rt.localPosition = Vector3.zero;

            // Semi-transparent background
            var bg = canvasGO.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0f, 0f, 0f, 0.6f);

            // Stats text
            var textGO = new GameObject("StatsText");
            textGO.transform.SetParent(canvasGO.transform, false);

            _statsText = textGO.AddComponent<TextMeshProUGUI>();
            _statsText.fontSize = 14;
            _statsText.color = new Color(0.8f, 1f, 0.8f, 1f);
            _statsText.alignment = TextAlignmentOptions.TopLeft;
            _statsText.enableWordWrapping = true;
            _statsText.overflowMode = TextOverflowModes.Truncate;
            _statsText.text = "NDI Diagnostics\n(waiting for data)";

            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(8, 4);
            textRT.offsetMax = new Vector2(-8, -4);
        }

        private void LateUpdate()
        {
            if (!_visible || _receiver == null) return;

            // Position overlay below-left of the main camera gaze
            var cam = Camera.main;
            if (cam != null)
            {
                _overlayRoot.transform.position = cam.transform.position
                    + cam.transform.forward * 1.2f
                    + cam.transform.right * -0.4f
                    + cam.transform.up * -0.3f;
                _overlayRoot.transform.rotation = Quaternion.LookRotation(
                    cam.transform.forward, Vector3.up);
            }

            _updateTimer += Time.unscaledDeltaTime;
            if (_updateTimer < UPDATE_INTERVAL) return;
            _updateTimer = 0f;

            // Gather stats
            _lastRecvFps = _receiver.CurrentFps;
            _lastDropped = _receiver.DroppedFrames;
            _lastTotal = _receiver.TotalFrames;
            _lastUploadMs = _receiver.LastUploadTimeMs;
            _lastStrideFixups = _receiver.StrideFixups;
            _lastFormatMismatches = _receiver.FormatMismatches;
            _lastRenderFps = _perfMonitor != null ? _perfMonitor.CurrentFps : 1f / Time.unscaledDeltaTime;
            _lastResScale = _perfMonitor != null ? _perfMonitor.ResolutionScale : 1f;

            var info = _receiver.LastFrameInfo;
            float dropRate = _lastTotal > 0 ? (float)_lastDropped / _lastTotal * 100f : 0f;

            _statsText.text =
                $"NDI Diagnostics\n" +
                $"Recv FPS: {_lastRecvFps:F1}  Render FPS: {_lastRenderFps:F1}\n" +
                $"Resolution: {info.Width}x{info.Height}  Upload: {_lastUploadMs:F1}ms\n" +
                $"Frames: {_lastTotal}  Dropped: {_lastDropped} ({dropRate:F1}%)\n" +
                $"Stride fixups: {_lastStrideFixups}  Format warns: {_lastFormatMismatches}\n" +
                $"Res scale: {_lastResScale:P0}  State: {_receiver.State}";

            // Also emit structured log every 5 seconds for offline analysis
            if (Time.frameCount % (int)(5f / Time.unscaledDeltaTime + 1) == 0)
            {
                Debug.Log($"[NDI Stats] recv_fps={_lastRecvFps:F1} render_fps={_lastRenderFps:F1} " +
                    $"upload_ms={_lastUploadMs:F2} dropped={_lastDropped}/{_lastTotal} " +
                    $"stride_fixups={_lastStrideFixups} format_warns={_lastFormatMismatches} " +
                    $"res_scale={_lastResScale:F2} res={info.Width}x{info.Height}");
            }
        }
    }
}

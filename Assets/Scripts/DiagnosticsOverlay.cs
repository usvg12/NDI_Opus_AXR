// Diagnostics Overlay - Lightweight on-screen stats for validating NDI streaming performance
// Shows FPS, frame timing, dropped frames, upload time, and stride/format diagnostics.

using System.IO;
using System.Text;
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
        private CompositionLayerVideoRenderer _compLayerRenderer;
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

        // StringBuilder for zero-alloc display updates
        private readonly StringBuilder _sb = new StringBuilder(256);

        // Persistent session log
        private StreamWriter _logWriter;
        private float _logTimer;
        private const float LOG_INTERVAL = 5.0f;
        private static readonly string LogDir = Path.Combine(Application.persistentDataPath, "logs");

        public bool IsVisible => _visible;

        public void SetReferences(NDIReceiver receiver, PerformanceMonitor perfMonitor,
            CompositionLayerVideoRenderer compLayerRenderer = null)
        {
            _receiver = receiver;
            _perfMonitor = perfMonitor;
            _compLayerRenderer = compLayerRenderer;
        }

        public void Initialize()
        {
            BuildOverlay();
            SetVisible(false); // Hidden by default
            OpenSessionLog();
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

            // Composition layer status
            string renderMode = _compLayerRenderer != null
                ? CompositionLayerVideoRenderer.GetRenderModeString(
                    _compLayerRenderer.IsCompositionLayerActive,
                    _compLayerRenderer.IsCompositionLayerSupported)
                : "Quad";

            // Build display string with reusable StringBuilder (zero per-update alloc)
            _sb.Clear();
            _sb.Append("NDI Diagnostics\nRecv FPS: ").Append(_lastRecvFps.ToString("F1"))
               .Append("  Render FPS: ").Append(_lastRenderFps.ToString("F1"))
               .Append("\nResolution: ").Append(info.Width).Append('x').Append(info.Height)
               .Append("  Upload: ").Append(_lastUploadMs.ToString("F1")).Append("ms")
               .Append("\nFrames: ").Append(_lastTotal)
               .Append("  Dropped: ").Append(_lastDropped)
               .Append(" (").Append(dropRate.ToString("F1")).Append("%)")
               .Append("\nStride fixups: ").Append(_lastStrideFixups)
               .Append("  Format warns: ").Append(_lastFormatMismatches)
               .Append("\nRes scale: ").Append((_lastResScale * 100f).ToString("F0")).Append('%')
               .Append("  State: ").Append(_receiver.State)
               .Append("\nRender mode: ").Append(renderMode);
            _statsText.SetText(_sb);

            // Persistent session log + console log every LOG_INTERVAL seconds
            _logTimer += UPDATE_INTERVAL;
            if (_logTimer >= LOG_INTERVAL)
            {
                _logTimer = 0f;

                _sb.Clear();
                _sb.Append("[NDI Stats] recv_fps=").Append(_lastRecvFps.ToString("F1"))
                   .Append(" render_fps=").Append(_lastRenderFps.ToString("F1"))
                   .Append(" upload_ms=").Append(_lastUploadMs.ToString("F2"))
                   .Append(" dropped=").Append(_lastDropped).Append('/').Append(_lastTotal)
                   .Append(" stride_fixups=").Append(_lastStrideFixups)
                   .Append(" format_warns=").Append(_lastFormatMismatches)
                   .Append(" res_scale=").Append(_lastResScale.ToString("F2"))
                   .Append(" res=").Append(info.Width).Append('x').Append(info.Height);

                string logLine = _sb.ToString();
                Debug.Log(logLine);

                if (_logWriter != null)
                {
                    _logWriter.Write(Time.unscaledTime.ToString("F1"));
                    _logWriter.Write(',');
                    _logWriter.WriteLine(logLine);
                }
            }
        }

        private void OpenSessionLog()
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                string filename = $"ndi_session_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
                string path = Path.Combine(LogDir, filename);
                _logWriter = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = true };
                _logWriter.WriteLine("time_s,stats");
                Debug.Log($"[DiagnosticsOverlay] Session log: {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DiagnosticsOverlay] Could not open session log: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            if (_logWriter != null)
            {
                _logWriter.Flush();
                _logWriter.Dispose();
                _logWriter = null;
            }
        }
    }
}

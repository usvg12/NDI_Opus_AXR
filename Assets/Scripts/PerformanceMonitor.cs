// Performance Monitor - Tracks rendering performance for VR optimization
// Monitors frame rate, GPU load, and triggers quality adjustments.

using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Monitors rendering performance and adjusts quality settings
    /// to maintain 72+ FPS in the headset. Critical for VR comfort.
    /// </summary>
    public class PerformanceMonitor : MonoBehaviour
    {
        [Header("Performance Targets")]
        [Tooltip("Minimum acceptable FPS before quality reduction")]
        [SerializeField] private float minFpsThreshold = 68f;

        [Tooltip("FPS target to restore quality")]
        [SerializeField] private float restoreFpsThreshold = 80f;

        [Tooltip("Seconds of sustained low FPS before adjusting")]
        [SerializeField] private float adjustmentDelay = 2.0f;

        [Header("Quality Levels")]
        [Tooltip("Allow dynamic resolution scaling")]
        [SerializeField] private bool enableDynamicResolution = true;

        [Tooltip("Minimum resolution scale (0.5 = 50%)")]
        [SerializeField] private float minResolutionScale = 0.7f;

        // State
        private float _currentFps;
        private float _fpsAccumulator;
        private int _fpsFrameCount;
        private float _fpsTimer;
        private float _lowFpsTimer;
        private float _currentResolutionScale = 1.0f;
        private bool _qualityReduced;

        public float CurrentFps => _currentFps;
        public float ResolutionScale => _currentResolutionScale;
        public bool IsQualityReduced => _qualityReduced;

        /// <summary>
        /// Pure logic: compute the new resolution scale after a quality reduction step.
        /// </summary>
        internal static float ComputeReducedScale(float currentScale, float minScale)
        {
            return Mathf.Max(currentScale - 0.1f, minScale);
        }

        /// <summary>
        /// Pure logic: compute the new resolution scale after a quality restore step.
        /// </summary>
        internal static float ComputeRestoredScale(float currentScale)
        {
            return Mathf.Min(currentScale + 0.05f, 1.0f);
        }

        /// <summary>
        /// Pure logic: determine whether quality should be reduced, restored, or held.
        /// Returns -1 for reduce, +1 for restore, 0 for hold.
        /// </summary>
        internal static int EvaluateScalingDirection(
            float fps, float minFpsThreshold, float restoreFpsThreshold,
            bool qualityCurrentlyReduced)
        {
            if (fps < minFpsThreshold)
                return -1;
            if (fps > restoreFpsThreshold && qualityCurrentlyReduced)
                return 1;
            return 0;
        }

        private void Update()
        {
            // Calculate FPS
            _fpsAccumulator += Time.unscaledDeltaTime;
            _fpsFrameCount++;
            _fpsTimer += Time.unscaledDeltaTime;

            if (_fpsTimer >= 0.5f) // Update every 0.5 seconds
            {
                _currentFps = _fpsFrameCount / _fpsAccumulator;
                _fpsFrameCount = 0;
                _fpsAccumulator = 0;
                _fpsTimer = 0;

                EvaluatePerformance();
            }
        }

        private void EvaluatePerformance()
        {
            if (_currentFps < minFpsThreshold)
            {
                _lowFpsTimer += 0.5f;

                if (_lowFpsTimer >= adjustmentDelay && enableDynamicResolution)
                {
                    ReduceQuality();
                }
            }
            else if (_currentFps > restoreFpsThreshold && _qualityReduced)
            {
                _lowFpsTimer = 0;
                RestoreQuality();
            }
            else
            {
                _lowFpsTimer = 0;
            }
        }

        private void ReduceQuality()
        {
            if (_currentResolutionScale <= minResolutionScale) return;

            _currentResolutionScale = Mathf.Max(_currentResolutionScale - 0.1f, minResolutionScale);
            _qualityReduced = true;

            // Apply resolution scale via XR rendering.
            // NOTE: This only affects the standard eye texture used by the URP rasterization
            // pipeline. When CompositionLayerVideoRenderer is active, the video texture is
            // submitted directly to the XR compositor and is NOT affected by this scale.
            // Composition layers bypass the eye texture entirely, rendering at native display
            // resolution regardless of this setting.
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = _currentResolutionScale;

            Debug.Log($"[Performance] Reduced resolution scale to {_currentResolutionScale:F1} " +
                $"(FPS: {_currentFps:F0})");
        }

        private void RestoreQuality()
        {
            if (_currentResolutionScale >= 1.0f)
            {
                _qualityReduced = false;
                return;
            }

            _currentResolutionScale = Mathf.Min(_currentResolutionScale + 0.05f, 1.0f);

            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = _currentResolutionScale;

            if (_currentResolutionScale >= 1.0f)
            {
                _qualityReduced = false;
                Debug.Log("[Performance] Quality restored to full resolution.");
            }
        }
    }
}

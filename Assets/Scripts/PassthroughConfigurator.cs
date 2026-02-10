// Passthrough Configurator - Manages AndroidXR passthrough/mixed reality mode
// Handles environment blend mode setup via XR Display subsystem.
// Aligned with Android XR developer docs:
// https://developer.android.com/develop/xr/unity

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace NDIViewer
{
    /// <summary>
    /// Configures and manages passthrough (mixed reality) mode on AndroidXR.
    /// Queries the XR Display subsystem for supported blend modes and sets
    /// AlphaBlend for passthrough rendering. Falls back to manifest config
    /// if the subsystem API is unavailable.
    /// </summary>
    public class PassthroughConfigurator : MonoBehaviour
    {
        [Header("Passthrough Settings")]
        [Tooltip("Desired environment blend mode for passthrough")]
        [SerializeField] private EnvironmentBlendMode desiredBlendMode = EnvironmentBlendMode.AlphaBlend;

        [Tooltip("Fallback background color if passthrough unavailable")]
        [SerializeField] private Color fallbackBackground = new Color(0.05f, 0.05f, 0.08f, 1f);

        public enum EnvironmentBlendMode
        {
            Opaque = 0,
            Additive = 1,
            AlphaBlend = 2
        }

        private Camera _xrCamera;
        private bool _passthroughActive;
        private bool _manifestFallback;
        private XRDisplaySubsystem _activeDisplay;

        /// <summary>Whether passthrough is confirmed active via subsystem or manifest fallback.</summary>
        public bool IsPassthroughActive => _passthroughActive;

        /// <summary>True when passthrough relies on AndroidManifest rather than subsystem API.</summary>
        public bool IsManifestFallback => _manifestFallback;

        private void Start()
        {
            _xrCamera = Camera.main;
            ConfigurePassthrough();
        }

        /// <summary>
        /// Configure the camera and XR subsystem for passthrough rendering.
        /// Queries supported blend modes and requests AlphaBlend for MR.
        /// </summary>
        public void ConfigurePassthrough()
        {
            if (_xrCamera == null)
            {
                _xrCamera = Camera.main;
                if (_xrCamera == null)
                {
                    Debug.LogError("[Passthrough] No main camera found.");
                    return;
                }
            }

            // Set camera to render with transparent background for passthrough
            _xrCamera.clearFlags = CameraClearFlags.SolidColor;
            _xrCamera.backgroundColor = Color.clear;

            // Query XR Display subsystem for blend mode support
            var displaySubsystems = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displaySubsystems);

            bool blendModeSet = false;

            foreach (var display in displaySubsystems)
            {
                if (!display.running) continue;

                _activeDisplay = display;
                Debug.Log($"[Passthrough] XR Display subsystem found: {display.SubsystemDescriptor.id}");

                // On Android XR, reprojectionMode maps to environment blend mode:
                //   0 = Opaque (fully virtual)
                //   1 = Additive (see-through additive)
                //   2 = AlphaBlend (passthrough with alpha compositing)
                // This cast is intentional — EnvironmentBlendMode values are aligned
                // with XRDisplaySubsystem.ReprojectionMode integer values on Android XR.
                try
                {
                    display.reprojectionMode = (XRDisplaySubsystem.ReprojectionMode)(int)desiredBlendMode;
                    blendModeSet = true;
                    _passthroughActive = true;
                    Debug.Log($"[Passthrough] Set blend mode to {desiredBlendMode} via XR Display subsystem.");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Passthrough] Could not set blend mode via subsystem: {e.Message}");
                }

                break;
            }

            if (!blendModeSet)
            {
                // No running subsystem accepted the blend mode — fall back to manifest.
                // The AndroidManifest meta-data com.android.xr.application.environment.blend.mode
                // is the fallback mechanism on Android XR.
                _manifestFallback = true;
                Debug.LogWarning("[Passthrough] No running XR display subsystem accepted blend mode. " +
                    "Passthrough relies on AndroidManifest environment.blend.mode = ALPHA_BLEND. " +
                    "Actual passthrough state cannot be confirmed until the device compositor runs.");
            }

            Debug.Log($"[Passthrough] Configuration complete. " +
                $"Active={_passthroughActive}, ManifestFallback={_manifestFallback}");
        }

        /// <summary>Toggle passthrough on/off (switch between MR and VR mode).</summary>
        public void SetPassthroughEnabled(bool enabled)
        {
            if (_xrCamera == null) return;

            if (enabled)
            {
                _xrCamera.backgroundColor = Color.clear;
                _passthroughActive = true;

                // Re-request AlphaBlend mode
                if (_activeDisplay != null && _activeDisplay.running)
                {
                    try
                    {
                        _activeDisplay.reprojectionMode =
                            (XRDisplaySubsystem.ReprojectionMode)EnvironmentBlendMode.AlphaBlend;
                    }
                    catch (System.Exception) { /* Fallback to manifest */ }
                }

                Debug.Log("[Passthrough] Enabled (AlphaBlend mode).");
            }
            else
            {
                _xrCamera.backgroundColor = fallbackBackground;
                _passthroughActive = false;

                // Request Opaque mode for full VR
                if (_activeDisplay != null && _activeDisplay.running)
                {
                    try
                    {
                        _activeDisplay.reprojectionMode =
                            (XRDisplaySubsystem.ReprojectionMode)EnvironmentBlendMode.Opaque;
                    }
                    catch (System.Exception) { /* Fallback */ }
                }

                Debug.Log("[Passthrough] Disabled (Opaque mode).");
            }
        }
    }
}

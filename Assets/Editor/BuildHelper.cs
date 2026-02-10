// Build Helper - Editor script for building the AndroidXR APK
// Configures build settings, runs preflight checks, and outputs build artifacts.

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace NDIViewer.Editor
{
    /// <summary>
    /// Provides menu items and automation for building the AndroidXR APK.
    /// Accessible via Unity menu: NDI XR Viewer > Build
    /// </summary>
    public static class BuildHelper
    {
        private const string APP_NAME = "NDI_XR_Viewer";
        private const string PACKAGE_NAME = "com.ndiviewer.androidxr";
        private const int MIN_SDK = 34;
        private const int TARGET_SDK = 35;
        private const string NDI_LIB_PATH = "Assets/Plugins/NDI/Android/arm64-v8a/libndi.so";
        private const string VERSION_FILE = "Assets/Editor/version.txt";

        [MenuItem("NDI XR Viewer/Build APK (Debug)")]
        public static void BuildDebugAPK()
        {
            BuildAPK(false);
        }

        [MenuItem("NDI XR Viewer/Build APK (Release)")]
        public static void BuildReleaseAPK()
        {
            BuildAPK(true);
        }

        [MenuItem("NDI XR Viewer/Configure Build Settings")]
        public static void ConfigureBuildSettings()
        {
            ApplyBuildSettings(false);
            Debug.Log("[Build] Build settings configured for Samsung Galaxy XR.");
        }

        [MenuItem("NDI XR Viewer/Run Preflight Checks")]
        public static void RunPreflightChecks()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            ValidatePreflight(errors, warnings, release: true);

            foreach (string w in warnings)
                Debug.LogWarning($"[Build Preflight] {w}");

            if (errors.Count > 0)
            {
                foreach (string e in errors)
                    Debug.LogError($"[Build Preflight] {e}");
                Debug.LogError($"[Build Preflight] {errors.Count} error(s) found.");
            }
            else
            {
                Debug.Log("[Build Preflight] All checks passed.");
            }
        }

        private static void BuildAPK(bool release)
        {
            // Run preflight validation
            var errors = new List<string>();
            var warnings = new List<string>();
            ValidatePreflight(errors, warnings, release);

            foreach (string w in warnings)
                Debug.LogWarning($"[Build Preflight] {w}");

            if (errors.Count > 0)
            {
                foreach (string e in errors)
                    Debug.LogError($"[Build Preflight] {e}");
                Debug.LogError($"[Build] Aborting — {errors.Count} preflight error(s). " +
                    "Fix the issues above or use 'Run Preflight Checks' for details.");
                return;
            }

            ApplyBuildSettings(release);

            string buildDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
            Directory.CreateDirectory(buildDir);

            string suffix = release ? "release" : "debug";
            string apkPath = Path.Combine(buildDir, $"{APP_NAME}_{suffix}.apk");

            var scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] No scenes found. Add scenes to Build Settings or Assets/Scenes/.");
                return;
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = release
                    ? BuildOptions.None
                    : BuildOptions.Development | BuildOptions.AllowDebugging
            };

            Debug.Log($"[Build] Starting {suffix} build -> {apkPath}");
            Debug.Log($"[Build] Version: {PlayerSettings.bundleVersion} " +
                $"(code {PlayerSettings.Android.bundleVersionCode})");

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] SUCCESS: {apkPath} ({summary.totalSize / (1024 * 1024):F1} MB)");
                EditorUtility.RevealInFinder(apkPath);
            }
            else
            {
                Debug.LogError($"[Build] FAILED: {summary.result} ({summary.totalErrors} errors)");
            }
        }

        /// <summary>
        /// Validate build prerequisites. Populates errors (fatal) and warnings (non-fatal).
        /// </summary>
        private static void ValidatePreflight(List<string> errors, List<string> warnings, bool release)
        {
            // Check NDI native library
            if (!File.Exists(NDI_LIB_PATH))
            {
                warnings.Add($"NDI native library not found at {NDI_LIB_PATH}. " +
                    "The app will run in test-pattern mode without NDI receive capability.");
            }

            // Check scenes
            var scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                errors.Add("No scenes found in Build Settings or Assets/Scenes/. " +
                    "At least one scene is required.");
            }

            // Release-specific checks
            if (release)
            {
                // Keystore validation
                if (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName) ||
                    !File.Exists(PlayerSettings.Android.keystoreName))
                {
                    warnings.Add("No Android keystore configured. The APK will be signed " +
                        "with the debug keystore, which is not suitable for distribution. " +
                        "Set keystore via Edit > Project Settings > Player > Publishing Settings.");
                }

                // Version sanity check
                if (PlayerSettings.bundleVersion == "0.0.0" ||
                    PlayerSettings.Android.bundleVersionCode <= 0)
                {
                    errors.Add("Version not set. Update version.txt or set version before release build.");
                }
            }
        }

        private static void ApplyBuildSettings(bool release)
        {
            // Switch to Android platform
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);

            // Package name
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android, PACKAGE_NAME);

            // Version — read from version.txt if available, else use fallback
            ApplyVersioning();

            // SDK levels
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)MIN_SDK;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)TARGET_SDK;

            // Architecture: ARM64 only (required for XR)
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Graphics API: Vulkan (required for AndroidXR)
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);

            // Rendering
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Build system
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

            // Performance settings
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);

            // XR-specific
            PlayerSettings.virtualRealitySplashScreen = null;

            // Stripping — use higher stripping for release to reduce APK size
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android,
                release ? ManagedStrippingLevel.Medium : ManagedStrippingLevel.Low);

            Debug.Log("[Build] Applied AndroidXR build settings:");
            Debug.Log($"  Package: {PACKAGE_NAME}");
            Debug.Log($"  Version: {PlayerSettings.bundleVersion} (code {PlayerSettings.Android.bundleVersionCode})");
            Debug.Log($"  Min SDK: {MIN_SDK}, Target SDK: {TARGET_SDK}");
            Debug.Log($"  Architecture: ARM64");
            Debug.Log($"  Graphics: Vulkan");
            Debug.Log($"  Stereo: Single Pass Instanced");
            Debug.Log($"  Stripping: {(release ? "Medium" : "Low")}");
        }

        /// <summary>
        /// Read version from Assets/Editor/version.txt (format: "1.2.3" on first line,
        /// optional integer version code on second line). Falls back to 1.0.0 / 1
        /// if the file doesn't exist.
        /// </summary>
        private static void ApplyVersioning()
        {
            string version = "1.0.0";
            int versionCode = 1;

            if (File.Exists(VERSION_FILE))
            {
                string[] lines = File.ReadAllLines(VERSION_FILE);
                if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
                    version = lines[0].Trim();
                if (lines.Length > 1 && int.TryParse(lines[1].Trim(), out int code) && code > 0)
                    versionCode = code;

                Debug.Log($"[Build] Version from {VERSION_FILE}: {version} (code {versionCode})");
            }
            else
            {
                Debug.Log($"[Build] No {VERSION_FILE} found, using default: {version} (code {versionCode})");
            }

            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = versionCode;
        }

        private static string[] GetEnabledScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            var enabledScenes = new List<string>();

            foreach (var scene in scenes)
            {
                if (scene.enabled)
                    enabledScenes.Add(scene.path);
            }

            // If no scenes configured, try to find the main scene
            if (enabledScenes.Count == 0)
            {
                string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
                foreach (string guid in guids)
                {
                    enabledScenes.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            return enabledScenes.ToArray();
        }
    }
}
#endif

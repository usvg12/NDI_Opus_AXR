// Build Helper - Editor script for building the AndroidXR APK
// Configures build settings, signs the APK, and outputs build artifacts.

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

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
            ApplyBuildSettings();
            Debug.Log("[Build] Build settings configured for Samsung Galaxy XR.");
        }

        private static void BuildAPK(bool release)
        {
            ApplyBuildSettings();

            string buildDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
            Directory.CreateDirectory(buildDir);

            string suffix = release ? "release" : "debug";
            string apkPath = Path.Combine(buildDir, $"{APP_NAME}_{suffix}.apk");

            var buildOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = release
                    ? BuildOptions.None
                    : BuildOptions.Development | BuildOptions.AllowDebugging
            };

            Debug.Log($"[Build] Starting {suffix} build -> {apkPath}");

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

        private static void ApplyBuildSettings()
        {
            // Switch to Android platform
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);

            // Package name
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android, PACKAGE_NAME);

            // Version
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;

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

            // Stripping
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android,
                ManagedStrippingLevel.Low);

            Debug.Log("[Build] Applied AndroidXR build settings:");
            Debug.Log($"  Package: {PACKAGE_NAME}");
            Debug.Log($"  Min SDK: {MIN_SDK}, Target SDK: {TARGET_SDK}");
            Debug.Log($"  Architecture: ARM64");
            Debug.Log($"  Graphics: Vulkan");
            Debug.Log($"  Stereo: Single Pass Instanced");
        }

        private static string[] GetEnabledScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            var enabledScenes = new System.Collections.Generic.List<string>();

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

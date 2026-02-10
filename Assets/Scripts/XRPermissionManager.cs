// XR Permission Manager - Runtime permission gating for XR-critical features
// On modern Android/XR stacks, permissions declared in AndroidManifest.xml
// still require runtime request/grant before the feature is available.
// Without this, hand tracking, eye tracking, camera passthrough, and
// scene understanding can silently fail after install.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace NDIViewer
{
    /// <summary>
    /// Requests and monitors Android runtime permissions required for XR features.
    /// Must be initialized early in the application lifecycle (before XRRigSetup
    /// and PassthroughConfigurator attempt to use gated features).
    ///
    /// Permissions managed:
    ///   - android.permission.CAMERA (passthrough)
    ///   - android.permission.HAND_TRACKING (hand interaction)
    ///   - android.permission.EYE_TRACKING_FINE (eye gaze / foveated rendering)
    ///   - android.permission.SCENE_UNDERSTANDING (spatial awareness)
    /// </summary>
    public class XRPermissionManager : MonoBehaviour
    {
        /// <summary>Status of a single permission after the request flow completes.</summary>
        public enum PermissionStatus
        {
            /// <summary>Not yet requested.</summary>
            Pending,
            /// <summary>User granted the permission.</summary>
            Granted,
            /// <summary>User denied the permission (feature should degrade gracefully).</summary>
            Denied
        }

        /// <summary>Snapshot of all managed permission states.</summary>
        public class PermissionResult
        {
            public PermissionStatus Camera;
            public PermissionStatus HandTracking;
            public PermissionStatus EyeTracking;
            public PermissionStatus SceneUnderstanding;

            /// <summary>True when all permissions have been resolved (granted or denied).</summary>
            public bool AllResolved =>
                Camera != PermissionStatus.Pending &&
                HandTracking != PermissionStatus.Pending &&
                EyeTracking != PermissionStatus.Pending &&
                SceneUnderstanding != PermissionStatus.Pending;
        }

        /// <summary>Fired on the main thread once all permission requests have been resolved.</summary>
        public event Action<PermissionResult> OnPermissionsResolved;

        /// <summary>Current state of all managed permissions.</summary>
        public PermissionResult CurrentResult { get; private set; } = new PermissionResult();

        /// <summary>True after all permission requests have completed.</summary>
        public bool IsResolved => CurrentResult.AllResolved;

        // Android permission strings
        private const string PERM_CAMERA = "android.permission.CAMERA";
        private const string PERM_HAND_TRACKING = "android.permission.HAND_TRACKING";
        private const string PERM_EYE_TRACKING = "android.permission.EYE_TRACKING_FINE";
        private const string PERM_SCENE_UNDERSTANDING = "android.permission.SCENE_UNDERSTANDING";

        /// <summary>
        /// Begins the runtime permission request flow. Call once during app startup.
        /// On non-Android platforms, all permissions are immediately marked Granted.
        /// </summary>
        public void RequestAllPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(RequestPermissionsCoroutine());
#else
            // In editor or non-Android builds, treat all as granted
            CurrentResult.Camera = PermissionStatus.Granted;
            CurrentResult.HandTracking = PermissionStatus.Granted;
            CurrentResult.EyeTracking = PermissionStatus.Granted;
            CurrentResult.SceneUnderstanding = PermissionStatus.Granted;
            Debug.Log("[Permissions] Non-Android platform — all permissions auto-granted.");
            OnPermissionsResolved?.Invoke(CurrentResult);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator RequestPermissionsCoroutine()
        {
            Debug.Log("[Permissions] Starting runtime permission requests...");

            // Build the list of permissions that still need requesting
            var permissionsToRequest = new List<string>();
            var permissionMap = new Dictionary<string, Action<PermissionStatus>>
            {
                { PERM_CAMERA, s => CurrentResult.Camera = s },
                { PERM_HAND_TRACKING, s => CurrentResult.HandTracking = s },
                { PERM_EYE_TRACKING, s => CurrentResult.EyeTracking = s },
                { PERM_SCENE_UNDERSTANDING, s => CurrentResult.SceneUnderstanding = s },
            };

            // Check which permissions are already granted
            foreach (var kvp in permissionMap)
            {
                if (Permission.HasUserAuthorizedPermission(kvp.Key))
                {
                    kvp.Value(PermissionStatus.Granted);
                    Debug.Log($"[Permissions] {kvp.Key} — already granted.");
                }
                else
                {
                    permissionsToRequest.Add(kvp.Key);
                }
            }

            // Request all outstanding permissions
            foreach (string perm in permissionsToRequest)
            {
                var callback = new PermissionCallbacks();
                bool resolved = false;
                PermissionStatus result = PermissionStatus.Pending;

                callback.PermissionGranted += _ =>
                {
                    result = PermissionStatus.Granted;
                    resolved = true;
                };
                callback.PermissionDenied += _ =>
                {
                    result = PermissionStatus.Denied;
                    resolved = true;
                };
                callback.PermissionDeniedAndDontAskAgain += _ =>
                {
                    result = PermissionStatus.Denied;
                    resolved = true;
                };

                Debug.Log($"[Permissions] Requesting: {perm}");
                Permission.RequestUserPermission(perm, callback);

                // Wait for the callback to fire
                float timeout = 30f;
                float elapsed = 0f;
                while (!resolved && elapsed < timeout)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!resolved)
                {
                    Debug.LogWarning($"[Permissions] {perm} — request timed out, treating as denied.");
                    result = PermissionStatus.Denied;
                }

                permissionMap[perm](result);
                string statusLabel = result == PermissionStatus.Granted ? "GRANTED" : "DENIED";
                Debug.Log($"[Permissions] {perm} — {statusLabel}");
            }

            LogPermissionSummary();
            OnPermissionsResolved?.Invoke(CurrentResult);
        }
#endif

        private void LogPermissionSummary()
        {
            Debug.Log("[Permissions] === Permission Summary ===");
            Debug.Log($"[Permissions]   Camera (passthrough):      {CurrentResult.Camera}");
            Debug.Log($"[Permissions]   Hand Tracking:             {CurrentResult.HandTracking}");
            Debug.Log($"[Permissions]   Eye Tracking (fine):       {CurrentResult.EyeTracking}");
            Debug.Log($"[Permissions]   Scene Understanding:       {CurrentResult.SceneUnderstanding}");

            if (CurrentResult.Camera == PermissionStatus.Denied)
                Debug.LogWarning("[Permissions] Camera denied — passthrough will not be available.");
            if (CurrentResult.HandTracking == PermissionStatus.Denied)
                Debug.LogWarning("[Permissions] Hand tracking denied — hand interaction will be unavailable.");
        }

        // ─── Query helpers for other components ─────────────────────

        public bool IsCameraGranted => CurrentResult.Camera == PermissionStatus.Granted;
        public bool IsHandTrackingGranted => CurrentResult.HandTracking == PermissionStatus.Granted;
        public bool IsEyeTrackingGranted => CurrentResult.EyeTracking == PermissionStatus.Granted;
        public bool IsSceneUnderstandingGranted => CurrentResult.SceneUnderstanding == PermissionStatus.Granted;
    }
}

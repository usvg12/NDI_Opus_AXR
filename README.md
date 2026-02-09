# NDI XR Viewer — AndroidXR NDI Streaming App

Real-time NDI video streaming viewer for Samsung Galaxy XR headset running in passthrough (mixed reality) mode. Receives NDI streams over the local network and displays them as spatial windows in 3D space with optional stereoscopic Side-by-Side (SBS) 3D rendering.

## Features

- **NDI Source Discovery**: Automatic scanning of local network for NDI sources
- **Low-Latency Streaming**: Background thread capture with direct RGBA buffer upload (latency depends on network conditions, NDI source, and device load — typically 2-4 frames)
- **Side-by-Side 3D**: Toggle between flat display and stereoscopic SBS mode
  - **SBS OFF**: Full 3840x1080 frame displayed as-is on a single plane
  - **SBS ON**: Left half (1920x1080) rendered to left eye, right half to right eye
- **Spatial Window**: Grabbable, movable, resizable video window in 3D space
- **Passthrough Mode**: Mixed reality — see your real environment behind the video
- **Floating UI Panel**: Source selector, connect button, SBS toggle, status/FPS display
- **Dynamic Resolution Scaling**: Reduces XR eye texture resolution under sustained low FPS to maintain headset comfort (see Performance section for conditions)
- **Network Resilience**: Disconnect detection with reconnection support
- **Composition Layer Support**: Optional OpenXR composition layer rendering for sharper video output (requires Android XR Extensions)
- **Built-in Test Pattern**: When NDI library is unavailable, displays an animated SBS test pattern for stereo rendering validation
- **Diagnostics Overlay**: Optional on-screen stats (recv FPS, render FPS, upload time, dropped frames, stride fixups) for performance validation

## Project Structure

```
NDI_Opus_AXR/
├── Assets/
│   ├── Scripts/
│   │   ├── NDIInterop.cs              # P/Invoke bindings for NDI native library
│   │   ├── NDISourceDiscovery.cs       # Background thread NDI source scanner
│   │   ├── NDIReceiver.cs             # Connects to NDI source, captures video frames
│   │   ├── NDIVideoDisplay.cs         # Renders frames onto spatial quad with SBS shader
│   │   ├── SpatialWindowController.cs # Grab, move, resize video window in 3D space
│   │   ├── UIController.cs            # Wires UI elements to NDI/display components
│   │   ├── UIPanelBuilder.cs          # Programmatically builds the floating UI panel
│   │   ├── AppController.cs           # Main app lifecycle and initialization
│   │   ├── XRRigSetup.cs             # XR Origin, hand tracking, ray interactors
│   │   ├── SceneBootstrapper.cs       # Builds entire scene hierarchy at runtime
│   │   ├── PassthroughConfigurator.cs # Manages passthrough/mixed reality mode
│   │   ├── CompositionLayerVideoRenderer.cs # Optional OpenXR composition layer rendering
│   │   ├── NetworkMonitor.cs          # Network connectivity monitoring
│   │   ├── PerformanceMonitor.cs      # FPS tracking and dynamic quality adjustment
│   │   ├── DiagnosticsOverlay.cs      # On-screen stats overlay for validation
│   │   └── TestPatternGenerator.cs    # Built-in SBS test pattern (NDI lib fallback)
│   ├── Shaders/
│   │   └── NDI_SBS_Stereo.shader      # URP stereo shader with SBS eye splitting
│   ├── Editor/
│   │   └── BuildHelper.cs             # Menu-driven APK build automation
│   ├── Plugins/
│   │   ├── NDI/
│   │   │   └── README_NDI_SDK.md      # NDI SDK download and integration instructions
│   │   └── Android/
│   │       ├── AndroidManifest.xml     # XR permissions, passthrough, OpenXR config
│   │       ├── mainTemplate.gradle     # Gradle build template for ARM64
│   │       └── gradleTemplate.properties
│   ├── Scenes/                         # Place your Unity scene here
│   ├── Prefabs/
│   ├── Resources/
│   └── StreamingAssets/
├── Packages/
│   └── manifest.json                   # Unity package dependencies (all auto-resolved)
├── ProjectSettings/
│   ├── ProjectSettings.asset           # Player settings (Android, Vulkan, API 34+)
│   ├── XRPluginManagement.asset        # OpenXR loader configuration
│   ├── OpenXRSettings.asset            # OpenXR features and interaction profiles
│   ├── URPSettings.asset               # URP optimized for XR streaming
│   └── QualitySettings.asset           # Quality levels tuned for VR performance
├── .gitignore
└── README.md
```

## Requirements

### Development Environment
- **Unity 6** (6000.1.17f1 or later — required by Android XR Extensions 1.2.0)
- **Android Build Support** module installed in Unity Hub
- **Android SDK** API level 34+ (installed via Unity Hub or Android Studio)
- **Android NDK** r26+ (installed via Unity Hub)
- **JDK 11** (bundled with Unity)

### Unity Packages (all auto-installed via manifest.json)
- OpenXR Plugin (`com.unity.xr.openxr` 1.15.1+)
- OpenXR Android XR (`com.unity.xr.openxr.androidxr` 1.2.0+) — Android XR loader
- XR Plugin Management (`com.unity.xr.management` 4.5.1+)
- XR Interaction Toolkit (`com.unity.xr.interaction.toolkit` 3.4.0+) — [Docs](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4)
- XR Hands (`com.unity.xr.hands` 1.6.0+)
- AR Foundation (`com.unity.xr.arfoundation` 6.2.0+) — Required by Android XR Extensions 1.2.0
- Android XR Extensions (`com.google.xr.extensions` 1.2.0+) — [GitHub](https://github.com/android/android-xr-unity-package)
- Input System (`com.unity.inputsystem` 1.11.2+)
- Universal Render Pipeline (`com.unity.render-pipelines.universal` 17.0.3+)
- TextMeshPro (`com.unity.textmeshpro`)

All packages above are declared in `Packages/manifest.json` with a scoped registry for Android XR packages. Unity resolves them automatically on project open — no manual package installation is required.

### NDI SDK (required for NDI streaming, not required for test pattern mode)
- **NDI Advanced SDK for Android** — Download from [ndi.video](https://ndi.video/tools/ndi-sdk/)
- Free for non-commercial use; review license for commercial deployment
- **If the NDI SDK is not installed**, the app will still launch and display a built-in SBS test pattern so you can validate stereo rendering, spatial interaction, and passthrough without any external dependencies.

### Target Device
- **Samsung Galaxy XR** headset (or any AndroidXR-compatible device)
- Android API 34+
- ARM64 architecture

## Setup Instructions

### Step 1: Clone and Open Project

```bash
git clone <repository-url>
```

Open the project folder in **Unity 6** via Unity Hub. All packages will be resolved automatically from `manifest.json` — no manual package installation is needed.

### Step 2: Install NDI SDK (optional — needed for actual NDI streaming)

1. Download the **NDI Advanced SDK** from https://ndi.video/tools/ndi-sdk/
2. Accept the license agreement
3. Create the directory and copy the Android ARM64 library:
   ```bash
   mkdir -p Assets/Plugins/NDI/Android/arm64-v8a/
   ```
4. Copy from the extracted SDK:
   ```
   NDI_SDK/lib/arm64-v8a/libndi.so → Assets/Plugins/NDI/Android/arm64-v8a/libndi.so
   ```
5. In Unity, select the `.so` file and configure the import settings:
   - Platform: **Android**
   - CPU: **ARM64**
   - Load on startup: **Yes**

**Verification**: After import, check the Unity console for `[NDI Discovery] Started scanning for NDI sources.` on play. If you see `[NDI Discovery] Native NDI library not found`, the `.so` file is not placed correctly or import settings are wrong.

**Without NDI SDK**: The app launches into test pattern mode automatically. The UI shows "NDI Library Missing" and a built-in SBS color bar pattern with "L"/"R" markers is displayed for stereo validation.

### Step 3: Configure XR Plugin Management

1. Go to **Edit > Project Settings > XR Plug-in Management**
2. Select the **Android** tab
3. Check **OpenXR**
4. Under OpenXR settings:
   - Set **Render Mode** to **Single Pass Instanced**
   - Enable **Submit Depth Buffer**
   - Add **Hand Interaction Profile** under Interaction Profiles
   - Enable features: Hand Tracking, Eye Gaze, Passthrough, Composition Layers, Foveated Rendering

### Step 4: Configure Build Settings

1. Go to **File > Build Settings**
2. Select **Android** platform and click **Switch Platform**
3. Click **Player Settings**:
   - **Company Name**: Your company
   - **Product Name**: NDI XR Viewer
   - **Package Name**: `com.ndiviewer.androidxr`
   - **Minimum API Level**: 34
   - **Target API Level**: 35
   - **Target Architectures**: ARM64 only
   - **Graphics APIs**: Vulkan only (remove OpenGL ES)
   - **Color Space**: Linear
   - **Stereo Rendering Mode**: Single Pass Instanced

   Or use the menu: **NDI XR Viewer > Configure Build Settings**

### Step 5: Create the Scene

1. Create a new scene: **File > New Scene** (choose **Empty**)
2. Save it to `Assets/Scenes/NDIViewerMain.unity`
3. Add an empty GameObject and attach the `SceneBootstrapper` component
4. The bootstrapper will create the entire scene hierarchy at runtime:
   - XR Origin with camera and hand interactors
   - NDI video display window
   - Floating UI control panel
   - All manager components
   - Diagnostics overlay (hidden by default)

5. Add the scene to Build Settings: **File > Build Settings > Add Open Scenes**

### Step 6: Build APK

**Option A — Unity Menu:**
- **NDI XR Viewer > Build APK (Debug)** — Development build with debugging
- **NDI XR Viewer > Build APK (Release)** — Optimized release build

**Option B — Manual:**
1. **File > Build Settings**
2. Ensure your scene is in the scene list
3. Click **Build** or **Build and Run**
4. Choose output path (e.g., `Builds/NDI_XR_Viewer.apk`)

**Option C — Command Line:**
```bash
Unity -batchmode -projectPath . -executeMethod NDIViewer.Editor.BuildHelper.BuildReleaseAPK -quit -logFile build.log
```

### Step 7: Deploy to Samsung Galaxy XR

```bash
# Enable developer mode on the headset
# Connect via USB or Wi-Fi ADB

# Install APK
adb install -r Builds/NDI_XR_Viewer_release.apk

# Launch
adb shell am start -n com.ndiviewer.androidxr/com.unity3d.player.UnityPlayerActivity

# View logs (NDI-specific)
adb logcat -s Unity | grep -E "\[NDI|NDI Stats\]"
```

## Usage

1. **Launch** the app on Samsung Galaxy XR — it starts in passthrough mode
2. **NDI Sources** are automatically discovered on the local network
3. Select a source from the **dropdown** on the floating control panel
4. Press **Connect** to start receiving the video stream
5. The video appears on a **2m wide window** positioned 2m in front of you
6. **Grab and move** the window by pinching and dragging with your hands
7. **Resize** using two-hand pinch-to-scale gesture
8. Toggle **SBS 3D Mode** to enable stereoscopic viewing:
   - OFF: Full 3840x1080 frame on a flat plane
   - ON: Left/right eye split for 3D depth perception
9. Press **Reset Window** to return the video window to its default position
10. If NDI library is missing, the app automatically shows a **test pattern** with SBS enabled

## Architecture

### Data Flow
```
NDI Source (Network)
       │
       ▼
NDISourceDiscovery ──── background thread scanning
       │                 (graceful fallback if libndi.so missing)
       ▼ (user selects source)
NDIReceiver ──────────── background thread frame capture
       │                  ├── P/Invoke → libndi.so (native)
       │                  ├── Stride-safe RGBA copy (strips padding if present)
       │                  ├── Double-buffered swap (lock-free GPU upload)
       │                  └── Performance counters + diagnostics
       ▼
NDIVideoDisplay ──────── main thread texture upload (outside lock)
       │                  ├── Texture2D.LoadRawTextureData
       │                  ├── SBS_Stereo shader (eye splitting)
       │                  └── Test pattern fallback (if NDI unavailable)
       ▼
SpatialWindowController ─ 3D positioning, grab, resize
       │
       ▼
OpenXR / AndroidXR ────── stereo rendering to headset
```

### Key Design Decisions

- **Background threads** for NDI discovery and frame capture minimize main thread load
- **Direct RGBA buffer copy** avoids color conversion overhead; stride padding is stripped if present
- **Double-buffered frame transfer** — capture thread writes to a back buffer, swaps under a brief lock; GPU upload happens outside the lock so the capture thread is never blocked by texture upload
- **Single shader** handles both flat and SBS modes via a float toggle (no material swap)
- **Single Pass Instanced** stereo rendering for GPU efficiency
- **SceneBootstrapper** creates everything at runtime, avoiding serialized scene dependencies
- **Reflection-based wiring** connects SerializeField references programmatically
- **Dynamic resolution scaling** drops eye texture resolution under sustained low FPS, restores when stable
- **Graceful NDI fallback** — if `libndi.so` is missing, the app switches to a built-in SBS test pattern instead of showing a blank panel

### Performance Characteristics

| Metric | Notes |
|--------|-------|
| Headset target | 72 Hz (configurable via AppController) |
| NDI stream latency | Depends on network, source resolution, and device load. Capture timeout is 16ms. Use DiagnosticsOverlay to measure actual values on-device. |
| Input resolution | Tested with 3840x1080 SBS; other resolutions are supported |
| Resolution scaling | Eye texture scales 70%-100% based on sustained FPS (PerformanceMonitor) |
| Frame stride handling | Automatically strips row padding if stride != width*4 |
| Lock contention | GPU upload is outside the lock; lock only covers reference swap (~microseconds) |

To validate performance on your specific setup, enable the DiagnosticsOverlay (see Diagnostics section).

### Diagnostics

The `DiagnosticsOverlay` component provides real-time stats:
- **Recv FPS**: NDI frames received per second
- **Render FPS**: Unity render loop FPS
- **Upload time**: GPU texture upload duration (LoadRawTextureData + Apply)
- **Dropped frames**: NDI frames dropped (network/processing)
- **Stride fixups**: Frames where stride padding was stripped
- **Format warnings**: Frames with unexpected pixel format
- **Resolution scale**: Current XR eye texture scale

The overlay is hidden by default. To enable it programmatically:
```csharp
FindObjectOfType<DiagnosticsOverlay>()?.SetVisible(true);
```

Structured logs are also emitted every ~5 seconds to `adb logcat`:
```
[NDI Stats] recv_fps=29.8 render_fps=72.0 upload_ms=1.23 dropped=0/892 stride_fixups=0 format_warns=0 res_scale=1.00 res=3840x1080
```

## Shader: NDI_SBS_Stereo

The custom URP shader (`Assets/Shaders/NDI_SBS_Stereo.shader`) handles:

- **SBS OFF**: UV coordinates pass through unmodified — displays full frame
- **SBS ON**: Uses `unity_StereoEyeIndex` to determine current eye:
  - Left eye (index 0): samples UV x range [0.0, 0.5]
  - Right eye (index 1): samples UV x range [0.5, 1.0]
- Supports both **Single Pass Instanced** and **Multi-pass** stereo rendering
- Includes **brightness** and **contrast** adjustments
- Falls back to built-in render pipeline if URP is unavailable

## Troubleshooting

| Issue | Solution |
|-------|----------|
| No NDI sources found | Ensure headset is on same network/VLAN as NDI source. Check multicast permissions. Verify `libndi.so` is installed (see Step 2). |
| "NDI Library Missing" in UI | `libndi.so` is not present. Download NDI Advanced SDK from ndi.video, copy `lib/arm64-v8a/libndi.so` to `Assets/Plugins/NDI/Android/arm64-v8a/`. Set import: Platform=Android, CPU=ARM64, Load on startup=Yes. Rebuild APK. |
| Black video window | Check ADB logs: `adb logcat -s Unity \| grep NDI`. Look for stride/format warnings. Verify NDI source is sending RGBA/RGBX format. |
| Garbled/shifted image | Likely stride mismatch. Check logs for `[NDI Receiver] Stride mismatch detected`. The receiver now strips padding automatically; if garbling persists, the NDI source may be sending an unsupported pixel format. |
| Low FPS / stuttering | Reduce NDI source resolution. Check DiagnosticsOverlay for upload_ms (should be <3ms). Check PerformanceMonitor scaling. Disable post-processing. |
| SBS mode looks wrong | Verify input is Full SBS (3840x1080, left-right layout). Half SBS is not supported. |
| Passthrough not visible | Ensure `AndroidManifest.xml` has `ALPHA_BLEND` blend mode. Camera background must be `Color.clear`. |
| Build fails | Confirm Vulkan is the only Graphics API. ARM64 only. Min SDK 34. See `BuildHelper.cs` settings. |
| App crashes on launch | Check `adb logcat` for missing native libraries or permission denials. |
| Package resolution errors on import | All packages are in `manifest.json` with scoped registry. Ensure Unity 6 (6000.1.17f1+) is used. Delete `Library/` folder and re-open if packages fail to resolve. |
| Test pattern shows instead of video | NDI library is not installed. This is expected if you haven't completed Step 2. The test pattern confirms the rendering pipeline works. |

## License

This project code is provided as-is for development purposes.

**NDI SDK**: NDI® is a registered trademark of Vizrt NDI AB. The NDI SDK is used under the NDI SDK License Agreement. Free for non-commercial use. For commercial deployment, review the [NDI SDK license terms](https://ndi.video/sdk/license/).

**AndroidXR**: Android is a trademark of Google LLC. Samsung Galaxy XR is a trademark of Samsung Electronics.

## Developer Documentation References

- [XR Interaction Toolkit 3.4 Documentation](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4)
- [Android XR Unity Developer Guide](https://developer.android.com/develop/xr/unity)
- [XR_ANDROID_composition_layer_passthrough_mesh Extension](https://developer.android.com/develop/xr/openxr/extensions/XR_ANDROID_composition_layer_passthrough_mesh)
- [Android XR Unity Package (GitHub)](https://github.com/android/android-xr-unity-package)

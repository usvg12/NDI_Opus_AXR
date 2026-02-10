# Testing & Simulation Guide — NDI XR Viewer

This document describes how to verify the NDI XR Viewer works as intended, covering desktop simulation, on-device testing, network validation, and performance checks.

## Prerequisites

| Tool | Purpose |
|------|---------|
| **NDI Tools** (ndi.tv) | Broadcast test NDI sources from a PC |
| **OBS Studio + NDI plugin** | Send arbitrary video as an NDI source |
| **ADB** | Deploy APK, read logs, run shell commands |
| **Samsung Galaxy XR headset** | On-device XR testing |
| **Unity 6000.0.x** | Editor play-mode testing |

---

## 1. Simulating an NDI Source (No Headset)

You don't need a camera or capture card — any PC on the same LAN can broadcast NDI.

### Option A: NDI Test Patterns (simplest)

1. Download **NDI Tools** from <https://ndi.video/tools/>
2. Launch **NDI Test Patterns Generator**
3. It immediately broadcasts a test card on the local network
4. The app's source discovery should find it within a few seconds

### Option B: OBS Studio (custom content)

1. Install OBS Studio and the **obs-ndi** plugin
2. Add any video source (media file, screen capture, webcam)
3. Go to **Tools → NDI Output Settings** and enable the main output
4. OBS now broadcasts its scene as an NDI source

### Option C: SBS 3D Test Content

To test the stereoscopic Side-by-Side mode:

1. In OBS, add a media source with a side-by-side 3D test video (two views placed left-right in a single frame)
2. Enable NDI output
3. On the headset app, connect to the source and toggle **SBS 3D mode**
4. Verify each eye sees its respective half of the frame

---

## 2. Unity Editor Play-Mode Testing

Some components can be partially tested without a headset:

```
Window → General → Test Runner   (if Unity Test Framework is installed)
```

### What works in the Editor

- **Scene bootstrapping** — `SceneBootstrapper` creates the hierarchy; verify GameObjects appear
- **UI construction** — `UIPanelBuilder` creates the control panel; inspect it in the scene
- **Source discovery** — If `libndi.so` were available on desktop, discovery would work (but the Android-only native lib won't load)
- **Performance monitor** — FPS tracking logic runs; resolution scaling thresholds can be verified

### What requires a device

- XR hand tracking and spatial interaction
- Passthrough / mixed-reality blending
- NDI native library calls (`libndi.so` is ARM64 Android only)
- Stereo rendering (Single Pass Instanced)

### Adding Unit Tests

To add the Unity Test Framework:

1. In `Packages/manifest.json`, add:
   ```json
   "com.unity.test-framework": "1.4.5"
   ```
2. Create `Assets/Tests/EditMode/` and `Assets/Tests/PlayMode/` directories
3. Create Assembly Definitions (`.asmdef`) for each test folder

Example test for `PerformanceMonitor` logic:

```csharp
using NUnit.Framework;

[TestFixture]
public class PerformanceMonitorTests
{
    [Test]
    public void ResolutionScale_ReducesWhenFpsBelowThreshold()
    {
        // PerformanceMonitor reduces scale when FPS < 68
        // and restores when FPS > 80
        float lowFps = 60f;
        float highFps = 85f;
        float minScale = 0.7f;

        // Verify the thresholds match expected behavior
        Assert.Less(lowFps, 68f, "Low FPS should be below reduction threshold");
        Assert.Greater(highFps, 80f, "High FPS should be above restore threshold");
        Assert.GreaterOrEqual(minScale, 0.7f, "Min scale should not go below 70%");
    }
}
```

---

## 3. On-Device Testing

### Deploy the APK

```bash
# Build from Unity (menu or command line)
# Menu: NDI XR Viewer → Build APK (Debug)
# Or command line:
Unity -batchmode -quit \
  -executeMethod NDIViewer.Editor.BuildHelper.BuildDebugAPK

# Install on headset (debug build)
adb install -r Builds/NDI_XR_Viewer_debug.apk

# Launch
adb shell am start -n com.ndiviewer.androidxr/.MainActivity
```

### Monitor Logs

```bash
# All Unity + NDI logs
adb logcat -s Unity NDI

# Filter by component
adb logcat -s Unity | grep "\[NDI\]"        # NDI operations
adb logcat -s Unity | grep "\[App\]"        # App lifecycle
adb logcat -s Unity | grep "\[Performance\]" # FPS and resolution
adb logcat -s Unity | grep "\[Network\]"    # Connectivity
adb logcat -s Unity | grep "\[Passthrough\]" # MR mode
```

### Functional Test Checklist

Run through each item and verify via headset observation + logcat:

| # | Test Case | Expected Result | Log Tag |
|---|-----------|----------------|---------|
| 1 | App launches | Scene hierarchy created, UI panel visible | `[App]` |
| 2 | Passthrough activates | See-through camera feed visible | `[Passthrough]` |
| 3 | NDI source discovery | Dropdown populates with sources on LAN | `[NDI]` |
| 4 | Connect to source | Video appears on spatial quad | `[NDI]` |
| 5 | SBS 3D toggle | Left/right eye split correctly | Visual check |
| 6 | Grab and move window | Spatial window follows hand | Visual check |
| 7 | Resize window | Quad scales proportionally | Visual check |
| 8 | Disconnect/reconnect | Handles gracefully, no crash | `[NDI]` `[Network]` |
| 9 | Network loss | NetworkMonitor detects, UI shows status | `[Network]` |
| 10 | Sustained playback (5 min) | FPS stays ≥68, no memory leak | `[Performance]` |

---

## 4. Network Simulation

Test how the app handles degraded network conditions.

### Add Latency

```bash
# Add 50ms delay to Wi-Fi
adb shell tc qdisc add dev wlan0 root netem delay 50ms

# Verify
adb shell tc qdisc show dev wlan0
```

### Simulate Packet Loss

```bash
# 5% packet loss
adb shell tc qdisc add dev wlan0 root netem loss 5%
```

### Simulate Bandwidth Limit

```bash
# Limit to 50 Mbit/s
adb shell tc qdisc add dev wlan0 root tbf rate 50mbit burst 32kbit latency 50ms
```

### Clean Up

```bash
adb shell tc qdisc del dev wlan0 root
```

### What to Observe

- Frame drops or stuttering in the video quad
- `[NDI]` log messages about connection state changes
- `[Network]` log messages about connectivity loss
- `[Performance]` log showing FPS dipping and resolution scaling kicking in
- App should not crash — verify with `adb logcat -s Unity | grep -i exception`

---

## 5. Performance Validation

### Built-in Monitoring

`PerformanceMonitor.cs` automatically tracks:

| Metric | Threshold | Action |
|--------|-----------|--------|
| FPS | < 68 | Reduces resolution scale toward 70% |
| FPS | > 80 | Restores resolution scale toward 100% |
| Resolution Scale | 70% – 100% | Adjusted every 0.5s |

### Manual Performance Check

```bash
# Watch FPS in real-time
adb logcat -s Unity | grep "\[Performance\]"

# Check for GC spikes (garbage collection)
adb logcat -s Unity | grep "GC_"

# Monitor memory
adb shell dumpsys meminfo com.ndiviewer.androidxr
```

### Performance Targets

| Metric | Target | Minimum |
|--------|--------|---------|
| Frame rate | 90 FPS | 72 FPS |
| Frame latency | < 20ms | < 40ms |
| Resolution scale | 100% | 70% |
| Memory usage | < 512 MB | < 768 MB |

---

## 6. Troubleshooting Test Failures

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| No NDI sources found | Firewall blocking mDNS/NDI ports | Allow UDP 5353 + TCP 5960-5990 on PC |
| Black video quad | `libndi.so` not loaded | Check `Assets/Plugins/NDI/Android/arm64-v8a/libndi.so` exists |
| Crash on launch | Missing XR feature | Check `adb logcat` for OpenXR errors; verify headset firmware |
| SBS looks wrong | Source isn't SBS format | Use actual SBS content (left-right layout in single frame) |
| Low FPS | GPU overloaded | Check resolution scale in logs; reduce video resolution on source |
| Hand tracking fails | Permissions denied | Verify `AndroidManifest.xml` has hand tracking permission |

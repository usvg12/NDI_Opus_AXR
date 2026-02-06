# NDI SDK Integration

## Required: Download and Install NDI SDK

This project requires the NDI SDK which cannot be redistributed due to licensing.

### Steps:

1. Download the NDI SDK from: https://ndi.video/tools/ndi-sdk/
2. Accept the NDI SDK license agreement
3. Extract the SDK and copy the following files into this directory:

### Required Files:
```
Assets/Plugins/NDI/
├── libndi.so                    (Android ARM64 native library)
├── NDI.cs                       (C# managed wrapper - provided in this project)
├── Processing.NDI.Lib.dll       (If using official .NET wrapper)
└── README_NDI_SDK.md            (This file)
```

### For Android ARM64 builds:
- Copy `lib/arm64-v8a/libndi.so` from the NDI Advanced SDK for Android
- Place it in `Assets/Plugins/NDI/Android/arm64-v8a/libndi.so`

### License Note:
NDI® is a registered trademark of Vizrt NDI AB. This project uses the NDI SDK
under the NDI SDK License Agreement. The NDI SDK is free for non-commercial use.
For commercial use, please review the NDI SDK license terms.

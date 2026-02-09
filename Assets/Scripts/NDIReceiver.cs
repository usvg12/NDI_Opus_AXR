// NDI Receiver - Connects to an NDI source and delivers video frames to a Texture2D
// NDI® is a registered trademark of Vizrt NDI AB.
// Non-commercial NDI SDK license.

using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NDIViewer
{
    /// <summary>
    /// Receives NDI video frames from a connected source and updates a Texture2D.
    /// Runs frame capture on a background thread for minimal main-thread impact.
    /// Uses double-buffering to minimize lock contention between capture and render threads.
    /// </summary>
    public class NDIReceiver : MonoBehaviour
    {
        [Header("Receiver Settings")]
        [Tooltip("Receiver name visible on the NDI network")]
        [SerializeField] private string receiverName = "Unity XR Viewer";

        [Tooltip("Frame capture timeout in ms (lower = more responsive, higher CPU)")]
        [SerializeField] private uint captureTimeoutMs = 16;

        /// <summary>Fired when connection state changes.</summary>
        public event Action<ConnectionState> OnConnectionStateChanged;

        /// <summary>Fired when a new video frame is ready on the main thread.</summary>
        public event Action<Texture2D, FrameInfo> OnVideoFrameReceived;

        public enum ConnectionState
        {
            Disconnected,
            Connecting,
            Connected,
            Error
        }

        public struct FrameInfo
        {
            public int Width;
            public int Height;
            public float Fps;
            public long Timestamp;
        }

        // Public state
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public Texture2D VideoTexture { get; private set; }
        public FrameInfo LastFrameInfo { get; private set; }
        public int DroppedFrames { get; private set; }
        public int TotalFrames { get; private set; }

        /// <summary>Name of the last source passed to Connect(), used for auto-reconnection.</summary>
        public string LastConnectedSourceName { get; private set; }

        // Native handles
        private IntPtr _recvInstance = IntPtr.Zero;
        private Thread _captureThread;
        private volatile bool _capturing;

        // Double-buffered frame transfer: capture thread writes to _backBuffer,
        // then swaps the reference under a brief lock. Main thread reads _frontBuffer
        // outside the lock for GPU upload.
        private readonly object _frameLock = new object();
        private byte[] _backBuffer;      // Capture thread writes here (tightly packed RGBA)
        private byte[] _frontBuffer;     // Main thread reads from here
        private int _frameWidth;
        private int _frameHeight;
        private float _frameFps;
        private long _frameTimestamp;
        private volatile bool _newFrameAvailable;
        private volatile bool _connectionChanged;
        private volatile ConnectionState _pendingState;

        // Stride-stripping scratch buffer (reused, never allocated per-frame
        // unless dimensions change)
        private byte[] _strideScratch;

        // Performance tracking
        private float _fpsTimer;
        private int _fpsCounter;
        private float _currentFps;

        // Diagnostics (public for stats overlay)
        /// <summary>Last measured GPU upload time in milliseconds.</summary>
        public float LastUploadTimeMs { get; private set; }
        /// <summary>Frames where stride != width*bpp (padding was stripped).</summary>
        public int StrideFixups { get; private set; }
        /// <summary>Frames where FourCC was not RGBA/RGBX (skipped).</summary>
        public int FormatMismatches { get; private set; }

        /// <summary>
        /// Connect to the specified NDI source. Disconnects any existing connection first.
        /// </summary>
        public void Connect(NDISourceDiscovery.DiscoveredSource source)
        {
            if (source == null)
            {
                Debug.LogError("[NDI Receiver] Cannot connect: source is null.");
                return;
            }

            // Remember source name for auto-reconnection
            LastConnectedSourceName = source.Name;

            // Disconnect existing connection
            Disconnect();

            SetState(ConnectionState.Connecting);

            try
            {
                // Create receiver with RGBA format for direct texture upload
                var recvSettings = new NDIInterop.NDIRecvCreateSettings
                {
                    sourceToConnectTo = new NDIInterop.NDISource(),
                    colorFormat = NDIInterop.NDIRecvColorFormat.RGBX_RGBA,
                    bandwidth = NDIInterop.NDIRecvBandwidth.Highest,
                    allowVideoFields = false,
                    name = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(receiverName)
                };

                _recvInstance = NDIInterop.RecvCreate(ref recvSettings);

                // Free the marshalled string
                if (recvSettings.name != IntPtr.Zero)
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(recvSettings.name);

                if (_recvInstance == IntPtr.Zero)
                {
                    Debug.LogError("[NDI Receiver] Failed to create receiver instance.");
                    SetState(ConnectionState.Error);
                    return;
                }

                // Connect to the source
                var ndiSource = source.NativeSource;
                NDIInterop.RecvConnect(_recvInstance, ref ndiSource);

                // Start capture thread
                _capturing = true;
                _captureThread = new Thread(CaptureLoop)
                {
                    Name = "NDI_Capture",
                    IsBackground = true,
                    Priority = System.Threading.ThreadPriority.AboveNormal
                };
                _captureThread.Start();

                Debug.Log($"[NDI Receiver] Connecting to: {source.Name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NDI Receiver] Connection error: {ex.Message}");
                SetState(ConnectionState.Error);
            }
        }

        /// <summary>
        /// Disconnect from the current NDI source and release resources.
        /// </summary>
        public void Disconnect()
        {
            _capturing = false;

            if (_captureThread != null && _captureThread.IsAlive)
            {
                _captureThread.Join(2000);
                _captureThread = null;
            }

            if (_recvInstance != IntPtr.Zero)
            {
                NDIInterop.RecvDestroy(_recvInstance);
                _recvInstance = IntPtr.Zero;
            }

            if (VideoTexture != null)
            {
                Destroy(VideoTexture);
                VideoTexture = null;
            }

            lock (_frameLock)
            {
                _backBuffer = null;
                _frontBuffer = null;
                _strideScratch = null;
                _newFrameAvailable = false;
            }

            DroppedFrames = 0;
            TotalFrames = 0;
            StrideFixups = 0;
            FormatMismatches = 0;
            _currentFps = 0;

            SetState(ConnectionState.Disconnected);
            Debug.Log("[NDI Receiver] Disconnected.");
        }

        private void CaptureLoop()
        {
            bool firstFrame = true;

            while (_capturing)
            {
                try
                {
                    var videoFrame = new NDIInterop.NDIVideoFrame();

                    NDIInterop.NDIFrameType frameType = NDIInterop.RecvCapture(
                        _recvInstance,
                        ref videoFrame,
                        IntPtr.Zero,  // No audio capture
                        IntPtr.Zero,  // No metadata capture
                        captureTimeoutMs);

                    switch (frameType)
                    {
                        case NDIInterop.NDIFrameType.Video:
                            ProcessVideoFrame(ref videoFrame);
                            if (firstFrame)
                            {
                                firstFrame = false;
                                _pendingState = ConnectionState.Connected;
                                _connectionChanged = true;
                            }
                            NDIInterop.RecvFreeVideo(_recvInstance, ref videoFrame);
                            break;

                        case NDIInterop.NDIFrameType.Error:
                            Debug.LogError("[NDI Receiver] Received error frame.");
                            _pendingState = ConnectionState.Error;
                            _connectionChanged = true;
                            break;

                        case NDIInterop.NDIFrameType.None:
                            // Timeout, continue polling
                            break;

                        case NDIInterop.NDIFrameType.StatusChange:
                            Debug.Log("[NDI Receiver] Status change received.");
                            break;
                    }

                    // Update performance counters
                    if (_recvInstance != IntPtr.Zero)
                    {
                        var totalPerf = new NDIInterop.NDIRecvPerformance();
                        var droppedPerf = new NDIInterop.NDIRecvPerformance();
                        NDIInterop.RecvGetPerformance(_recvInstance, ref totalPerf, ref droppedPerf);
                        TotalFrames = (int)totalPerf.totalFrames;
                        DroppedFrames = (int)droppedPerf.droppedFrames;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NDI Receiver] Capture error: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Process a video frame on the capture thread.
        /// Copies pixel data into back buffer (stripping stride padding if needed),
        /// then swaps into front buffer under a brief lock.
        /// </summary>
        private void ProcessVideoFrame(ref NDIInterop.NDIVideoFrame frame)
        {
            if (frame.data == IntPtr.Zero || frame.width <= 0 || frame.height <= 0)
                return;

            // Validate pixel format: we requested RGBX_RGBA, so we expect RGBA or RGBX (both 4 bpp)
            bool isRGBA = frame.fourCC == NDIInterop.NDIFourCC.RGBA ||
                          frame.fourCC == NDIInterop.NDIFourCC.RGBX;
            bool isBGRA = frame.fourCC == NDIInterop.NDIFourCC.BGRA ||
                          frame.fourCC == NDIInterop.NDIFourCC.BGRX;
            if (!isRGBA && !isBGRA)
            {
                // Unsupported format - log once per N frames to avoid spam
                FormatMismatches++;
                if (FormatMismatches <= 3 || FormatMismatches % 100 == 0)
                {
                    Debug.LogWarning($"[NDI Receiver] Unsupported FourCC: 0x{(uint)frame.fourCC:X8} " +
                        $"(expected RGBA/RGBX). Frame skipped. Count: {FormatMismatches}");
                }
                return;
            }
            if (isBGRA && FormatMismatches == 0)
            {
                Debug.Log("[NDI Receiver] Receiving BGRA/BGRX format; will convert to RGBA.");
                FormatMismatches++;
            }

            int bpp = 4; // bytes per pixel for RGBA/RGBX/BGRA/BGRX
            int tightStride = frame.width * bpp;
            int tightByteCount = tightStride * frame.height;
            int nativeStride = frame.lineStrideBytes;

            // Ensure nativeStride is sane
            if (nativeStride < tightStride)
            {
                Debug.LogError($"[NDI Receiver] lineStrideBytes ({nativeStride}) < width*4 ({tightStride}). Frame corrupt, skipping.");
                return;
            }

            // Allocate/reuse back buffer (only reallocate on dimension change)
            if (_backBuffer == null || _backBuffer.Length != tightByteCount)
            {
                _backBuffer = new byte[tightByteCount];
                Debug.Log($"[NDI Receiver] Allocated back buffer: {frame.width}x{frame.height} " +
                    $"({tightByteCount} bytes, stride={nativeStride})");
            }

            // Copy frame data, handling stride padding
            if (nativeStride == tightStride)
            {
                // Fast path: no padding, single bulk copy
                System.Runtime.InteropServices.Marshal.Copy(frame.data, _backBuffer, 0, tightByteCount);
            }
            else
            {
                // Stride has padding: copy row-by-row, stripping extra bytes.
                // Use scratch buffer to avoid per-row P/Invoke overhead:
                // copy entire native buffer then compact in managed code.
                StrideFixups++;
                int nativeByteCount = nativeStride * frame.height;

                if (_strideScratch == null || _strideScratch.Length < nativeByteCount)
                {
                    _strideScratch = new byte[nativeByteCount];
                    Debug.Log($"[NDI Receiver] Stride mismatch detected: native={nativeStride}, " +
                        $"tight={tightStride}. Allocated stride scratch buffer ({nativeByteCount} bytes).");
                }

                // Single bulk copy from native
                System.Runtime.InteropServices.Marshal.Copy(frame.data, _strideScratch, 0, nativeByteCount);

                // Strip padding row by row (managed memory, fast)
                for (int row = 0; row < frame.height; row++)
                {
                    Buffer.BlockCopy(_strideScratch, row * nativeStride,
                                     _backBuffer, row * tightStride, tightStride);
                }
            }

            // Convert BGRA/BGRX → RGBA/RGBX by swapping R and B channels in-place
            if (isBGRA)
            {
                for (int i = 0; i < tightByteCount; i += 4)
                {
                    byte tmp = _backBuffer[i];       // B
                    _backBuffer[i] = _backBuffer[i + 2]; // R → position 0
                    _backBuffer[i + 2] = tmp;            // B → position 2
                }
            }

            // Swap back buffer into front buffer under a brief lock.
            // The lock only protects the reference swap and metadata, not the heavy copy.
            lock (_frameLock)
            {
                // Swap references: old front becomes available for next capture
                var temp = _frontBuffer;
                _frontBuffer = _backBuffer;
                _backBuffer = temp; // Reuse old front as next back (avoid allocation)

                _frameWidth = frame.width;
                _frameHeight = frame.height;
                _frameFps = frame.frameRateD > 0 ? (float)frame.frameRateN / frame.frameRateD : 0;
                _frameTimestamp = frame.timestamp;
                _newFrameAvailable = true;
            }
        }

        private void Update()
        {
            // Handle connection state changes on main thread
            if (_connectionChanged)
            {
                _connectionChanged = false;
                SetState(_pendingState);
            }

            // Process new video frame on main thread
            if (_newFrameAvailable)
            {
                byte[] uploadBuffer = null;
                int w = 0, h = 0;
                float fps = 0;
                long ts = 0;

                // Brief lock: snapshot the front buffer reference and metadata,
                // then release. GPU upload happens outside the lock.
                lock (_frameLock)
                {
                    if (_frontBuffer != null && _frameWidth > 0 && _frameHeight > 0)
                    {
                        uploadBuffer = _frontBuffer;
                        w = _frameWidth;
                        h = _frameHeight;
                        fps = _frameFps;
                        ts = _frameTimestamp;
                    }
                    _newFrameAvailable = false;
                }

                if (uploadBuffer != null)
                {
                    // Create or resize texture as needed (outside lock)
                    if (VideoTexture == null ||
                        VideoTexture.width != w ||
                        VideoTexture.height != h)
                    {
                        if (VideoTexture != null) Destroy(VideoTexture);

                        // Use RGBA32 for direct buffer upload
                        VideoTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                        {
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp
                        };

                        Debug.Log($"[NDI Receiver] Created texture: {w}x{h}");
                    }

                    // Validate buffer size matches texture expectation
                    int expectedBytes = w * h * 4;
                    if (uploadBuffer.Length != expectedBytes)
                    {
                        Debug.LogError($"[NDI Receiver] Buffer size mismatch: buffer={uploadBuffer.Length}, " +
                            $"expected={expectedBytes} ({w}x{h}x4). Skipping upload.");
                    }
                    else
                    {
                        // GPU upload (outside lock - capture thread is not blocked)
                        long uploadStart = Stopwatch.GetTimestamp();
                        VideoTexture.LoadRawTextureData(uploadBuffer);
                        VideoTexture.Apply(false);
                        long uploadEnd = Stopwatch.GetTimestamp();
                        LastUploadTimeMs = (float)(uploadEnd - uploadStart) / Stopwatch.Frequency * 1000f;
                    }

                    LastFrameInfo = new FrameInfo
                    {
                        Width = w,
                        Height = h,
                        Fps = fps,
                        Timestamp = ts
                    };

                    OnVideoFrameReceived?.Invoke(VideoTexture, LastFrameInfo);
                }

                // FPS calculation
                _fpsCounter++;
                _fpsTimer += Time.unscaledDeltaTime;
                if (_fpsTimer >= 1.0f)
                {
                    _currentFps = _fpsCounter / _fpsTimer;
                    _fpsCounter = 0;
                    _fpsTimer = 0;
                }
            }
        }

        private void SetState(ConnectionState newState)
        {
            if (State == newState) return;
            State = newState;
            OnConnectionStateChanged?.Invoke(newState);
            Debug.Log($"[NDI Receiver] State: {newState}");
        }

        /// <summary>Current measured receive FPS.</summary>
        public float CurrentFps => _currentFps;

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}

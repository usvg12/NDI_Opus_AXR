// NDI Receiver - Connects to an NDI source and delivers video frames to a Texture2D
// NDI® is a registered trademark of Vizrt NDI AB.
// Non-commercial NDI SDK license.

using System;
using System.Threading;
using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Receives NDI video frames from a connected source and updates a Texture2D.
    /// Runs frame capture on a background thread for minimal main-thread impact.
    /// Optimized for low-latency streaming (target < 50ms).
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

        // Native handles
        private IntPtr _recvInstance = IntPtr.Zero;
        private Thread _captureThread;
        private volatile bool _capturing;

        // Frame buffer for thread-safe transfer
        private readonly object _frameLock = new object();
        private byte[] _frameBuffer;
        private int _frameWidth;
        private int _frameHeight;
        private float _frameFps;
        private long _frameTimestamp;
        private volatile bool _newFrameAvailable;
        private volatile bool _connectionChanged;
        private volatile ConnectionState _pendingState;

        // Performance tracking
        private float _fpsTimer;
        private int _fpsCounter;
        private float _currentFps;

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
                _frameBuffer = null;
                _newFrameAvailable = false;
            }

            DroppedFrames = 0;
            TotalFrames = 0;
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

        private void ProcessVideoFrame(ref NDIInterop.NDIVideoFrame frame)
        {
            if (frame.data == IntPtr.Zero || frame.width <= 0 || frame.height <= 0)
                return;

            int byteCount = frame.lineStrideBytes * frame.height;

            lock (_frameLock)
            {
                // Reallocate buffer if dimensions changed
                if (_frameBuffer == null || _frameBuffer.Length != byteCount)
                {
                    _frameBuffer = new byte[byteCount];
                }

                // Copy frame data (this is the critical path for latency)
                System.Runtime.InteropServices.Marshal.Copy(frame.data, _frameBuffer, 0, byteCount);

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
                lock (_frameLock)
                {
                    if (_frameBuffer != null && _frameWidth > 0 && _frameHeight > 0)
                    {
                        // Create or resize texture as needed
                        if (VideoTexture == null ||
                            VideoTexture.width != _frameWidth ||
                            VideoTexture.height != _frameHeight)
                        {
                            if (VideoTexture != null) Destroy(VideoTexture);

                            // Use RGBA32 for direct buffer upload
                            VideoTexture = new Texture2D(
                                _frameWidth, _frameHeight,
                                TextureFormat.RGBA32, false)
                            {
                                filterMode = FilterMode.Bilinear,
                                wrapMode = TextureWrapMode.Clamp
                            };

                            Debug.Log($"[NDI Receiver] Created texture: {_frameWidth}x{_frameHeight}");
                        }

                        // Upload pixel data to GPU
                        VideoTexture.LoadRawTextureData(_frameBuffer);
                        VideoTexture.Apply(false);

                        LastFrameInfo = new FrameInfo
                        {
                            Width = _frameWidth,
                            Height = _frameHeight,
                            Fps = _frameFps,
                            Timestamp = _frameTimestamp
                        };

                        OnVideoFrameReceived?.Invoke(VideoTexture, LastFrameInfo);
                    }

                    _newFrameAvailable = false;
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

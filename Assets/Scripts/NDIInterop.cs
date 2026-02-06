// NDI Interop - P/Invoke bindings for NDI SDK native library
// NDI® is a registered trademark of Vizrt NDI AB.
// This code uses the NDI SDK under non-commercial license terms.
// For commercial use, review the NDI SDK License Agreement at https://ndi.video

using System;
using System.Runtime.InteropServices;

namespace NDIViewer
{
    /// <summary>
    /// P/Invoke declarations for the NDI native library (libndi).
    /// Provides low-level access to NDI find, receive, and frame operations.
    /// </summary>
    public static class NDIInterop
    {
        private const string NDI_LIB = "ndi";

        // ─── Initialization ───────────────────────────────────────────

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_initialize")]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool Initialize();

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_destroy")]
        public static extern void Destroy();

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_version")]
        public static extern IntPtr Version();

        // ─── Find ─────────────────────────────────────────────────────

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_find_create_v2")]
        public static extern IntPtr FindCreate(ref NDIFindCreateSettings settings);

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_find_destroy")]
        public static extern void FindDestroy(IntPtr instance);

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_find_wait_for_sources")]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool FindWaitForSources(IntPtr instance, uint timeoutMs);

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_find_get_current_sources")]
        public static extern IntPtr FindGetCurrentSources(IntPtr instance, out uint numSources);

        // ─── Receive ──────────────────────────────────────────────────

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_recv_create_v3")]
        public static extern IntPtr RecvCreate(ref NDIRecvCreateSettings settings);

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_recv_destroy")]
        public static extern void RecvDestroy(IntPtr instance);

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_recv_connect")]
        public static extern void RecvConnect(IntPtr instance, ref NDISource source);

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_recv_capture_v3")]
        public static extern NDIFrameType RecvCapture(
            IntPtr instance,
            ref NDIVideoFrame videoFrame,
            IntPtr audioFrame,
            IntPtr metadataFrame,
            uint timeoutMs);

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_recv_free_video_v2")]
        public static extern void RecvFreeVideo(IntPtr instance, ref NDIVideoFrame videoFrame);

        [DllImport(NDI_LIB, EntryPoint = "NDIlib_recv_get_performance")]
        public static extern void RecvGetPerformance(
            IntPtr instance,
            ref NDIRecvPerformance totalPerf,
            ref NDIRecvPerformance droppedPerf);

        // ─── Structures ───────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        public struct NDIFindCreateSettings
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool showLocalSources;
            public IntPtr groups;
            public IntPtr extraIPs;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NDISource
        {
            public IntPtr name;
            public IntPtr urlAddress;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NDIRecvCreateSettings
        {
            public NDISource sourceToConnectTo;
            public NDIRecvColorFormat colorFormat;
            public NDIRecvBandwidth bandwidth;
            [MarshalAs(UnmanagedType.U1)]
            public bool allowVideoFields;
            public IntPtr name;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NDIVideoFrame
        {
            public int width;
            public int height;
            public NDIFourCC fourCC;
            public int frameRateN;
            public int frameRateD;
            public float pictureAspectRatio;
            public NDIFrameFormat frameFormat;
            public long timecode;
            public IntPtr data;
            public int lineStrideBytes;
            public IntPtr metadata;
            public long timestamp;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NDIRecvPerformance
        {
            public long totalFrames;
            public long droppedFrames;
        }

        // ─── Enums ────────────────────────────────────────────────────

        public enum NDIFrameType
        {
            None = 0,
            Video = 1,
            Audio = 2,
            Metadata = 3,
            Error = 4,
            StatusChange = 100
        }

        public enum NDIRecvBandwidth
        {
            MetadataOnly = -10,
            AudioOnly = 10,
            Lowest = 0,
            Highest = 100
        }

        public enum NDIRecvColorFormat
        {
            BGRX_BGRA = 0,
            UYVY_BGRA = 1,
            RGBX_RGBA = 2,
            UYVY_RGBA = 3,
            Fastest = 100,
            Best = 101
        }

        public enum NDIFourCC : uint
        {
            UYVY = 0x59565955,
            BGRA = 0x41524742,
            BGRX = 0x58524742,
            RGBA = 0x41424752,
            RGBX = 0x58424752,
            NV12 = 0x3231564E,
            I420 = 0x30323449,
            P216 = 0x36313250
        }

        public enum NDIFrameFormat
        {
            Progressive = 1,
            Interleaved = 0,
            Field0 = 2,
            Field1 = 3
        }

        // ─── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Marshal an NDISource struct from an unmanaged pointer at a given array index.
        /// </summary>
        public static NDISource GetSourceAtIndex(IntPtr sourcesPtr, int index)
        {
            int structSize = Marshal.SizeOf<NDISource>();
            IntPtr elementPtr = new IntPtr(sourcesPtr.ToInt64() + index * structSize);
            return Marshal.PtrToStructure<NDISource>(elementPtr);
        }

        /// <summary>
        /// Read the NDI source name string from the native struct.
        /// </summary>
        public static string GetSourceName(NDISource source)
        {
            if (source.name == IntPtr.Zero) return "(unknown)";
            return Marshal.PtrToStringAnsi(source.name) ?? "(unknown)";
        }
    }
}

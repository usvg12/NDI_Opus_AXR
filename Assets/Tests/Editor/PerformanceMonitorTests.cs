// Edit-mode tests for PerformanceMonitor resolution scaling logic
// and DiagnosticsOverlay formatting correctness.

using NUnit.Framework;
using UnityEngine;

namespace NDIViewer.Tests
{
    [TestFixture]
    public class PerformanceMonitorTests
    {
        [Test]
        public void ResolutionScale_DefaultsToOne()
        {
            var go = new GameObject("PerfMon_Test");
            var monitor = go.AddComponent<PerformanceMonitor>();

            Assert.AreEqual(1.0f, monitor.ResolutionScale, 0.001f,
                "Default resolution scale should be 1.0");
            Assert.IsFalse(monitor.IsQualityReduced,
                "Quality should not be reduced initially");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void CurrentFps_DefaultsToZero()
        {
            var go = new GameObject("PerfMon_Test");
            var monitor = go.AddComponent<PerformanceMonitor>();

            Assert.AreEqual(0f, monitor.CurrentFps, 0.001f,
                "FPS should be 0 before any frames are processed");

            Object.DestroyImmediate(go);
        }
    }

    [TestFixture]
    public class VersionFileTests
    {
        [Test]
        public void VersionFile_ExistsAndIsValid()
        {
            string versionPath = System.IO.Path.Combine(
                Application.dataPath, "Editor", "version.txt");

            Assert.IsTrue(System.IO.File.Exists(versionPath),
                "version.txt should exist at Assets/Editor/version.txt");

            string[] lines = System.IO.File.ReadAllLines(versionPath);
            Assert.IsTrue(lines.Length >= 1, "version.txt should have at least one line");

            string version = lines[0].Trim();
            Assert.IsTrue(
                System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"),
                $"Version '{version}' should match X.Y.Z format");

            if (lines.Length >= 2)
            {
                Assert.IsTrue(int.TryParse(lines[1].Trim(), out int code) && code > 0,
                    $"Version code '{lines[1].Trim()}' should be a positive integer");
            }
        }
    }

    [TestFixture]
    public class NDIReceiverStateTests
    {
        [Test]
        public void Receiver_StartsDisconnected()
        {
            var go = new GameObject("Receiver_Test");
            var receiver = go.AddComponent<NDIReceiver>();

            Assert.AreEqual(NDIReceiver.ConnectionState.Disconnected, receiver.State,
                "Receiver should start in Disconnected state");
            Assert.IsNull(receiver.VideoTexture,
                "VideoTexture should be null before connection");
            Assert.AreEqual(0, receiver.DroppedFrames);
            Assert.AreEqual(0, receiver.TotalFrames);
            Assert.AreEqual(0, receiver.StrideFixups);
            Assert.AreEqual(0, receiver.FormatMismatches);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Receiver_DisconnectIsIdempotent()
        {
            var go = new GameObject("Receiver_Test");
            var receiver = go.AddComponent<NDIReceiver>();

            // Disconnect when already disconnected should not throw
            Assert.DoesNotThrow(() => receiver.Disconnect(),
                "Disconnect() should be safe to call when already disconnected");
            Assert.DoesNotThrow(() => receiver.Disconnect(),
                "Disconnect() should be idempotent");

            Object.DestroyImmediate(go);
        }
    }

    [TestFixture]
    public class FrameInfoTests
    {
        [Test]
        public void FrameInfo_DefaultsToZero()
        {
            var info = new NDIReceiver.FrameInfo();

            Assert.AreEqual(0, info.Width);
            Assert.AreEqual(0, info.Height);
            Assert.AreEqual(0f, info.Fps, 0.001f);
            Assert.AreEqual(0L, info.Timestamp);
        }

        [Test]
        public void FrameInfo_StoresValues()
        {
            var info = new NDIReceiver.FrameInfo
            {
                Width = 1920,
                Height = 1080,
                Fps = 59.94f,
                Timestamp = 123456789L
            };

            Assert.AreEqual(1920, info.Width);
            Assert.AreEqual(1080, info.Height);
            Assert.AreEqual(59.94f, info.Fps, 0.01f);
            Assert.AreEqual(123456789L, info.Timestamp);
        }
    }
}

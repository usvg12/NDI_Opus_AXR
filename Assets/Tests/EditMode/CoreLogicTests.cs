// Edit-mode regression tests for deterministic runtime logic.
// Covers selection persistence, reconnect matching, and resolution scaling.

using System.Collections.Generic;
using NUnit.Framework;

namespace NDIViewer.Tests
{
    public class SelectionPersistenceTests
    {
        private static List<NDISourceDiscovery.DiscoveredSource> MakeSources(params (string name, string url)[] items)
        {
            var list = new List<NDISourceDiscovery.DiscoveredSource>();
            foreach (var (name, url) in items)
                list.Add(new NDISourceDiscovery.DiscoveredSource { Name = name, Url = url });
            return list;
        }

        [Test]
        public void FindSelectionIndexByUrl_ReturnsCorrectIndex()
        {
            var sources = MakeSources(
                ("Camera A", "ndi://192.168.1.10:5961"),
                ("Camera B", "ndi://192.168.1.20:5961"),
                ("Camera C", "ndi://192.168.1.30:5961"));

            int idx = UIController.FindSelectionIndexByUrl(sources, "ndi://192.168.1.20:5961");
            Assert.AreEqual(1, idx);
        }

        [Test]
        public void FindSelectionIndexByUrl_ReturnsZeroWhenNoMatch()
        {
            var sources = MakeSources(
                ("Camera A", "ndi://192.168.1.10:5961"),
                ("Camera B", "ndi://192.168.1.20:5961"));

            int idx = UIController.FindSelectionIndexByUrl(sources, "ndi://192.168.1.99:5961");
            Assert.AreEqual(0, idx);
        }

        [Test]
        public void FindSelectionIndexByUrl_ReturnsZeroForNullUrl()
        {
            var sources = MakeSources(("Camera A", "ndi://192.168.1.10:5961"));

            Assert.AreEqual(0, UIController.FindSelectionIndexByUrl(sources, null));
            Assert.AreEqual(0, UIController.FindSelectionIndexByUrl(sources, ""));
        }

        [Test]
        public void FindSelectionIndexByUrl_ReturnsZeroForEmptyList()
        {
            Assert.AreEqual(0, UIController.FindSelectionIndexByUrl(
                new List<NDISourceDiscovery.DiscoveredSource>(), "ndi://x"));
        }

        [Test]
        public void FindSelectionIndexByUrl_ReturnsZeroForNullList()
        {
            Assert.AreEqual(0, UIController.FindSelectionIndexByUrl(null, "ndi://x"));
        }

        [Test]
        public void FindSelectionIndexByUrl_MatchesByUrlNotName()
        {
            // Two sources share the same name but have different URLs
            var sources = MakeSources(
                ("OBS", "ndi://192.168.1.10:5961"),
                ("OBS", "ndi://192.168.1.20:5961"));

            int idx = UIController.FindSelectionIndexByUrl(sources, "ndi://192.168.1.20:5961");
            Assert.AreEqual(1, idx);
        }
    }

    public class ReconnectMatchingTests
    {
        private static List<NDISourceDiscovery.DiscoveredSource> MakeSources(params string[] names)
        {
            var list = new List<NDISourceDiscovery.DiscoveredSource>();
            foreach (var n in names)
                list.Add(new NDISourceDiscovery.DiscoveredSource { Name = n, Url = $"ndi://{n}" });
            return list;
        }

        [Test]
        public void FindReconnectMatch_FindsByName()
        {
            var sources = MakeSources("Camera A", "Camera B", "Camera C");

            var match = NetworkMonitor.FindReconnectMatch(sources, "Camera B");
            Assert.IsNotNull(match);
            Assert.AreEqual("Camera B", match.Name);
        }

        [Test]
        public void FindReconnectMatch_ReturnsNullWhenNotFound()
        {
            var sources = MakeSources("Camera A", "Camera B");

            var match = NetworkMonitor.FindReconnectMatch(sources, "Camera X");
            Assert.IsNull(match);
        }

        [Test]
        public void FindReconnectMatch_ReturnsNullForNullName()
        {
            var sources = MakeSources("Camera A");

            Assert.IsNull(NetworkMonitor.FindReconnectMatch(sources, null));
            Assert.IsNull(NetworkMonitor.FindReconnectMatch(sources, ""));
        }

        [Test]
        public void FindReconnectMatch_ReturnsNullForNullList()
        {
            Assert.IsNull(NetworkMonitor.FindReconnectMatch(null, "Camera A"));
        }

        [Test]
        public void FindReconnectMatch_ReturnsNullForEmptyList()
        {
            Assert.IsNull(NetworkMonitor.FindReconnectMatch(
                new List<NDISourceDiscovery.DiscoveredSource>(), "Camera A"));
        }

        [Test]
        public void FindReconnectMatch_IsCaseSensitive()
        {
            var sources = MakeSources("Camera A");

            Assert.IsNull(NetworkMonitor.FindReconnectMatch(sources, "camera a"));
        }
    }

    public class ResolutionScalingTests
    {
        [Test]
        public void ComputeReducedScale_DecrementsByPointOne()
        {
            float result = PerformanceMonitor.ComputeReducedScale(1.0f, 0.7f);
            Assert.AreEqual(0.9f, result, 0.001f);
        }

        [Test]
        public void ComputeReducedScale_ClampsToMinimum()
        {
            float result = PerformanceMonitor.ComputeReducedScale(0.75f, 0.7f);
            Assert.AreEqual(0.7f, result, 0.001f);
        }

        [Test]
        public void ComputeReducedScale_AtMinimumStaysAtMinimum()
        {
            float result = PerformanceMonitor.ComputeReducedScale(0.7f, 0.7f);
            Assert.AreEqual(0.7f, result, 0.001f);
        }

        [Test]
        public void ComputeRestoredScale_IncrementsByPointZeroFive()
        {
            float result = PerformanceMonitor.ComputeRestoredScale(0.8f);
            Assert.AreEqual(0.85f, result, 0.001f);
        }

        [Test]
        public void ComputeRestoredScale_ClampsToOne()
        {
            float result = PerformanceMonitor.ComputeRestoredScale(0.98f);
            Assert.AreEqual(1.0f, result, 0.001f);
        }

        [Test]
        public void EvaluateScalingDirection_ReduceWhenBelowMinFps()
        {
            int dir = PerformanceMonitor.EvaluateScalingDirection(60f, 68f, 80f, false);
            Assert.AreEqual(-1, dir);
        }

        [Test]
        public void EvaluateScalingDirection_RestoreWhenAboveThresholdAndReduced()
        {
            int dir = PerformanceMonitor.EvaluateScalingDirection(85f, 68f, 80f, true);
            Assert.AreEqual(1, dir);
        }

        [Test]
        public void EvaluateScalingDirection_HoldWhenAboveThresholdButNotReduced()
        {
            int dir = PerformanceMonitor.EvaluateScalingDirection(85f, 68f, 80f, false);
            Assert.AreEqual(0, dir);
        }

        [Test]
        public void EvaluateScalingDirection_HoldWhenInMiddleRange()
        {
            int dir = PerformanceMonitor.EvaluateScalingDirection(72f, 68f, 80f, true);
            Assert.AreEqual(0, dir);
        }

        [Test]
        public void EvaluateScalingDirection_HoldAtExactMinThreshold()
        {
            // At exactly minFpsThreshold (not below), should hold
            int dir = PerformanceMonitor.EvaluateScalingDirection(68f, 68f, 80f, true);
            Assert.AreEqual(0, dir);
        }

        [Test]
        public void EvaluateScalingDirection_RestoreAtExactRestoreThreshold()
        {
            // At exactly restoreFpsThreshold (not above), should hold
            int dir = PerformanceMonitor.EvaluateScalingDirection(80f, 68f, 80f, true);
            Assert.AreEqual(0, dir);
        }

        [Test]
        public void FullScaleDownSequence_ReachesFloor()
        {
            float scale = 1.0f;
            float minScale = 0.7f;

            // Simulate repeated reductions
            while (scale > minScale)
            {
                float newScale = PerformanceMonitor.ComputeReducedScale(scale, minScale);
                Assert.Less(newScale, scale + 0.001f, "Scale must not increase on reduction");
                scale = newScale;
            }

            Assert.AreEqual(minScale, scale, 0.001f);
        }

        [Test]
        public void FullScaleUpSequence_ReachesCeiling()
        {
            float scale = 0.7f;

            // Simulate repeated restorations
            while (scale < 1.0f)
            {
                float newScale = PerformanceMonitor.ComputeRestoredScale(scale);
                Assert.Greater(newScale, scale - 0.001f, "Scale must not decrease on restoration");
                scale = newScale;
            }

            Assert.AreEqual(1.0f, scale, 0.001f);
        }
    }
}

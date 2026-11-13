using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>DG-05: the version stamp, the autoplay command line and the frame-time statistics (pure parts).</summary>
    public sealed class BuildStampTests
    {
        [Test]
        public void StampRoundTripsAndFormatsTheTitleLabel()
        {
            string text = BuildInfo.Serialize("1.0.214", "73d62bb", true, "2026-11-12T20:00:00Z", "StandaloneOSX", false);
            var f = BuildInfo.Parse(text);
            Assert.AreEqual("1.0.214", f["version"]);
            Assert.AreEqual("73d62bb", f["commit"]);
            Assert.AreEqual("1", f["dirty"]);
            Assert.AreEqual("StandaloneOSX", f["target"]);
            Assert.IsFalse(f.ContainsKey("# written by criticalmass.editor.build for one player build, deleted after it (dg-05)."));
            Assert.AreEqual("v1.0.214 (73d62bb+dirty)", BuildInfo.Format("1.0.214", "73d62bb+dirty", false));
            Assert.AreEqual("v1.0.214 (73d62bb) dev build", BuildInfo.Format("1.0.214", "73d62bb", true));
            Assert.AreEqual("v dev", BuildInfo.Format("dev", "", false));
            Assert.AreEqual(0, BuildInfo.Parse(null).Count);
            Assert.AreEqual(0, BuildInfo.Parse("no equals sign\n=novalue\n").Count);
        }

        [Test]
        public void EditorHasNoStampSoTheVersionReadsDev()
        {
            Assert.AreEqual("dev", BuildInfo.Version);
            Assert.AreEqual("v dev", BuildInfo.Label);
        }

        [Test]
        public void AutoplayFlagsParseCaseInsensitivelyAndIgnoreUnityFlags()
        {
            AutoplayArgs.Options o = AutoplayArgs.Parse(new[]
            {
                "/x/CriticalMass", "-screen-width", "1920", "-AUTOPLAY", "-quitAfterMatch", "-autoplaySpeed", "8",
                "-perfLog", "/tmp/p.json", "-novsync", "-logFile", "Player.log",
            });
            Assert.IsTrue(o.Autoplay);
            Assert.IsTrue(o.QuitAfterMatch);
            Assert.IsTrue(o.NoVsync);
            Assert.AreEqual(8.0, o.Speed);
            Assert.AreEqual("/tmp/p.json", o.PerfLog);
            Assert.AreEqual(900.0, o.TimeoutSec);

            AutoplayArgs.Options none = AutoplayArgs.Parse(new[] { "CriticalMass" });
            Assert.IsFalse(none.Autoplay || none.QuitAfterMatch || none.NoVsync);
            Assert.AreEqual(0.0, none.Speed);
            Assert.IsNull(none.PerfLog);

            AutoplayArgs.Options bad = AutoplayArgs.Parse(new[] { "-autoplaySpeed", "-autoplay", "-autoplayTimeout", "NaN", "-perfLog" });
            Assert.AreEqual(0.0, bad.Speed, "a flag is never read as a speed");
            Assert.IsTrue(bad.Autoplay);
            Assert.AreEqual(900.0, bad.TimeoutSec);
            Assert.IsNull(bad.PerfLog);
            Assert.AreEqual(0, AutoplayArgs.Parse(null).Speed);
        }

        [Test]
        public void FrameStatisticsUseNearestRankAndCountHitches()
        {
            var frames = new System.Collections.Generic.List<float>();
            for (int i = 1; i <= 100; i++) frames.Add(i);   // 1..100 ms
            PerfOverlay.Report r = PerfOverlay.Analyse(frames);
            Assert.AreEqual(100, r.Frames);
            Assert.AreEqual(50.0, r.P50);
            Assert.AreEqual(95.0, r.P95);
            Assert.AreEqual(99.0, r.P99);
            Assert.AreEqual(100.0, r.Worst);
            Assert.AreEqual(50, r.Over50);              // 51..100
            Assert.AreEqual(67, r.Over33);              // 34..100
            Assert.AreEqual(50.5, r.Mean, 1e-9);
            Assert.AreEqual(4.95, r.WorstAtSecond, 1e-6);   // sum of 1..99 ms before the worst frame

            Assert.AreEqual(0, PerfOverlay.Analyse(new float[0]).Frames);
            Assert.AreEqual(9.0, PerfOverlay.Percentile(new[] { 9f, 7f, 8f }, 2, 100));   // only the first 2 count (7, 9)
            Assert.AreEqual(7.0, PerfOverlay.Percentile(new[] { 9f, 7f, 8f }, 2, 50));
            Assert.AreEqual(0.0, PerfOverlay.Percentile(null, 3, 50));

            string json = PerfOverlay.ToJson(r, new[] { ("result", "\"MATCH END\"") });
            StringAssert.Contains("\"result\": \"MATCH END\"", json);
            StringAssert.Contains("\"p95\": 95", json);
            StringAssert.Contains("\"50\": 50", json);
        }
    }
}

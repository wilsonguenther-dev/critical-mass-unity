using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// perf/start-hitch-1 (2026-11-14): the scene-start report names its parts largest first and never reports a negative
    /// remainder, counts only between a load and its first update, times the first ticks after a load; the M9 spare
    /// models are spread over a few frames; the old world art is released one frame before the load call.
    /// </summary>
    public sealed class StartHitchTests
    {
        [Test]
        public void ReportListsPartsLargestFirstAndTheRemainder()
        {
            string line = LoadTimer.Format(1, 120, 300, new[] { "CombatFx.Start", "ActorViews.Start", "HudInteract.Start" },
                new[] { 20.0, 150.0, 5.0 }, 3);
            StringAssert.Contains("load call to scene arrival 120 ms", line);
            StringAssert.Contains("scene arrival to first update 300 ms", line);
            Assert.Less(line.IndexOf("ActorViews.Start 150.0"), line.IndexOf("CombatFx.Start 20.0"), "largest part first");
            Assert.Less(line.IndexOf("CombatFx.Start 20.0"), line.IndexOf("HudInteract.Start 5.0"));
            StringAssert.EndsWith("unnamed 125", line);
            StringAssert.EndsWith("unnamed 0", LoadTimer.Format(0, -1, 10, new[] { "a" }, new[] { 50.0 }, 1), "never negative");
            StringAssert.DoesNotContain("load call", LoadTimer.Format(0, -1, 10, new string[0], new double[0], 0), "boot: no load call");
        }

        [Test]
        public void PartsCountOnlyBetweenTheLoadAndItsFirstUpdate()
        {
            LoadTimer.SceneLoaded(LoadTimer.Now());
            Assert.IsTrue(LoadTimer.Armed);
            long t = LoadTimer.Now();
            LoadTimer.Part("Test.Start", t);
            LoadTimer.Part("Test.Start", t);   // same name: summed into one part
            LoadTimer.FirstUpdate();
            Assert.IsFalse(LoadTimer.Armed, "one report per load");
            StringAssert.Contains("Test.Start", LoadTimer.LastReport);
            Assert.AreEqual(LoadTimer.LastReport.IndexOf("Test.Start"), LoadTimer.LastReport.LastIndexOf("Test.Start"));
            string before = LoadTimer.LastReport;
            LoadTimer.Part("Late.Start", LoadTimer.Now());
            LoadTimer.FirstUpdate();
            Assert.AreEqual(before, LoadTimer.LastReport, "nothing after the report");
        }

        [Test]
        public void FirstTicksAfterALoadAreTimed()
        {
            LoadTimer.SceneLoaded(LoadTimer.Now());
            for (int i = 0; i < LoadTimer.TicksWatched; i++)
            {
                Assert.IsTrue(LoadTimer.WatchingTicks);
                LoadTimer.TickDone(LoadTimer.Now());
            }
            Assert.IsFalse(LoadTimer.WatchingTicks, "only the first ticks: no stopwatch in a running match");
            StringAssert.StartsWith("[CM.Load] first ticks after the load:", LoadTimer.LastTicks);
            LoadTimer.FirstUpdate();
        }

        [Test]
        public void SpareModelsAreSpreadOverAFewFrames()
        {
            Assert.AreEqual(2, ActorViews.PrewarmPerClass, "M9: the same spares as before, only later");
            Assert.GreaterOrEqual(ActorViews.SparesPerFrame, 1);
            int frames = (ClassIds.All.Count * ActorViews.PrewarmPerClass + ActorViews.SparesPerFrame - 1) / ActorViews.SparesPerFrame;
            Assert.LessOrEqual(frames, 12, "all spares are built well inside the loading screen");
        }

        [Test]
        public void OldWorldArtGoesOneFrameBeforeTheLoad()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Runtime/Flow/FlowDirector.cs"));
            int release = src.IndexOf("WorldView.ReleaseAllForLoad();");
            int load = src.IndexOf("SceneManager.LoadScene(_loadPath);");
            Assert.Greater(release, 0);
            Assert.Greater(load, release, "the release returns first; the load call comes on the next frame");
            StringAssert.Contains("LoadTimer.LoadIssued();", src.Substring(release, load - release));
        }
    }
}

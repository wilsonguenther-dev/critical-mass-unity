using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menus;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>first-run-warm-1: the first-run warm-up stays inside the boot budget and never holds the loading screen.</summary>
    public sealed class FirstRunWarmTests
    {
        [Test]
        public void TheWarmUpFitsTheBootBudgetAndTheScreenNeverWaitsLonger()
        {
            Assert.LessOrEqual(FirstRunWarm.BudgetSeconds, 1.5f, "boot time growth under 1.5 s");
            Assert.LessOrEqual(FirstRunWarm.BudgetSeconds, LoadingScreen.WarmUpWaitSeconds, "the screen's own bound covers it");
            Assert.GreaterOrEqual(FirstRunWarm.WideFrames, 1);
            Assert.GreaterOrEqual(FirstRunWarm.FxFrames, 1);
            Assert.IsTrue(FirstRunWarm.Done, "no warm-up in progress outside a match load (EditMode: none installed)");
            Assert.AreEqual(1f, FirstRunWarm.Progress, 1e-6f);
            Assert.AreEqual(-1, FirstRunWarm.StatesAfterStart, "counter off");
            // first-run-warm-2: nothing runs, so nothing holds the sim or the frame governor; the hold has its own cap.
            Assert.IsFalse(FirstRunWarm.Running, "no warm-up running outside a match load");
            Assert.IsFalse(FirstRunWarm.HoldsSim, "the sim is never held without a warm-up");
            Assert.LessOrEqual(FirstRunWarm.HoldCapSeconds, LoadingScreen.WarmUpWaitSeconds + 1f, "the sim hold is bounded");
            Assert.Greater(FirstRunWarm.HoldCapSeconds, FirstRunWarm.BudgetSeconds, "the hold outlasts the warm-up's own cap");
            bool rec = FirstRunWarm.RecordingSupported;   // never throws, whatever the device says
            Assert.That(rec || !rec);
            Assert.GreaterOrEqual(ShaderWarmup.MinRecordingBytes, 283L, "the Air's empty 282-byte trace counts as no recording");
        }

        /// <summary>
        /// b12-x1: the match-load hitch (400 to 500 ms at 0.3 s into match 1 on smoke runs) was the scene-load frame, counted
        /// because the automated recording began during the title's fade, before the load. An automated load now counts as
        /// "running" (no recording, no render-scale move) until the new match world is warmed, with its own cap; the sim
        /// hold stays the warm-up's alone. The one "[CM.Load] first-second" line names what ran in the slowest frame.
        /// </summary>
        [Test]
        public void AnAutomatedLoadIsNotAMatchFrameAndTheFirstSecondLineNamesTheCause()
        {
            Assert.IsFalse(FirstRunWarm.LoadPendingNow, "no load pending outside a player (EditMode: none installed)");
            Debug.Log($"[b12-x1] load-pending cap {FirstRunWarm.PendingCapSeconds} s (limit 30), first-second window {FirstRunWarm.ProbeAfterTickSeconds} s " +
                      $"after the first tick, capped at {FirstRunWarm.ProbeCapSeconds} s");
            Assert.LessOrEqual(FirstRunWarm.PendingCapSeconds, 30f, "an automated run never waits forever for its recording");
            Assert.Greater(FirstRunWarm.PendingCapSeconds, FirstRunWarm.HoldCapSeconds, "the pending cap outlasts a warm-up");
            Assert.AreEqual(1f, FirstRunWarm.ProbeAfterTickSeconds, 1e-6f, "the line covers the first second of the match");

            (bool load, string warm, bool ticks, bool build, string want)[] cases =
            {
                (true, "warm-wide", true, true, FirstRunWarm.CauseSceneLoad),
                (false, "warm-wide", true, true, "warm-wide"),
                (false, null, true, true, FirstRunWarm.CauseFirstTicks),
                (false, null, false, true, FirstRunWarm.CauseWorldBuild),
                (false, null, false, false, FirstRunWarm.CauseFrame),
            };
            foreach (var c in cases)
            {
                string got = FirstRunWarm.CauseOf(c.load, c.warm, c.ticks, c.build);
                Debug.Log($"[b12-x1] cause(load={c.load}, warm={c.warm ?? "none"}, firstTicks={c.ticks}, building={c.build}) = {got} (want {c.want})");
                Assert.AreEqual(c.want, got);
            }

            string line = FirstRunWarm.FormatFirstSecond(437.4f, 0.29f, FirstRunWarm.CauseSceneLoad, false, 41.2f, 0.12f, FirstRunWarm.CauseFirstTicks, false, 3.4f);
            string never = FirstRunWarm.FormatFirstSecond(120f, 2f, FirstRunWarm.CauseWorldBuild, true, -1f, 0f, null, false, -1f);
            Debug.Log("[b12-x1] sample line: " + line);
            Debug.Log("[b12-x1] sample line, no first tick: " + never);
            StringAssert.StartsWith("[CM.Load] first-second worst=437ms at=0.29s cause=scene-load covered=no", line);
            StringAssert.Contains("after the first tick worst=41ms at=0.12s cause=first-ticks", line);
            StringAssert.Contains("covered=loading-screen", never);
            StringAssert.Contains("did not come", never);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b12-t: the capture and logging tools the batch 12 Air list depends on: the CM.* frame-cost scopes are in the
    /// recorder's list and reach perf.json and the match-end lines with the clearance counters; GoreShots stages the
    /// aftermath scene at +20 s and +60 s; KaijuKoShots plans its three stills; the Kaiju ownership line has every field.
    /// Each case prints what it measured, the value and the limit.
    /// </summary>
    [TestFixture]
    public sealed class CaptureToolsTests
    {
        static readonly string[] Five = { "CM.KaijuHill", "CM.KaijuClear", "CM.HudEdge", "CM.Pace", "CM.OnlineView" };

        static void Say(string what, object value, object limit) =>
            Console.WriteLine("[b12-t capture] " + what + " value=" + Convert.ToString(value, CultureInfo.InvariantCulture)
                + " limit=" + Convert.ToString(limit, CultureInfo.InvariantCulture));

        [Test]
        public void TheRecordedListHasAllFiveCmScopes()
        {
            foreach (string n in Five)
            {
                bool listed = PerfOverlay.RecordedMarkers.Contains(n), scoped = Array.IndexOf(PerfOverlay.CmScopes, n) >= 0;
                Say(n + " in the recorded list / in CmScopes", listed + "/" + scoped, "True/True");
                Assert.IsTrue(listed, n + " recorded");
                Assert.IsTrue(scoped, n + " kept per frame");
            }
            Say("CmScopes count", PerfOverlay.CmScopes.Length, 5);
            Assert.AreEqual(5, PerfOverlay.CmScopes.Length);
        }

        [Test]
        public void ScopeStatsAreMeanP95AndMaxPerFrame()
        {
            var f = new float[100];
            for (int i = 0; i < f.Length; i++) f[i] = i + 1;
            PerfOverlay.ScopeStat s = PerfOverlay.ScopeOf("CM.KaijuHill", f, f.Length);
            Say("mean of 1..100", s.Mean, 50.5);
            Say("p95 of 1..100 (nearest rank)", s.P95, 95);
            Say("max of 1..100", s.Max, 100);
            Assert.IsTrue(s.Recorded);
            Assert.AreEqual(50.5, s.Mean, 1e-9);
            Assert.AreEqual(95, s.P95, 1e-9);
            Assert.AreEqual(100, s.Max, 1e-9);
            Assert.AreEqual(100, s.Frames);
            PerfOverlay.ScopeStat none = PerfOverlay.ScopeOf("CM.Pace", null, 0);
            Say("no frames is not recorded", none.Recorded, false);
            Assert.IsFalse(none.Recorded);
        }

        [Test]
        public void ToJsonAndTheMatchEndLinesCarryEveryScopeAndCounter()
        {
            PerfOverlay.Report r = PerfOverlay.Analyse(new List<float> { 16f, 17f, 18f });
            var f = new float[] { 0.1f, 0.2f, 0.4f };
            for (int i = 0; i < Five.Length; i++)
                r.Scopes.Add(i == Five.Length - 1 ? new PerfOverlay.ScopeStat { Name = Five[i] } : PerfOverlay.ScopeOf(Five[i], f, f.Length));
            r.ClearanceFull = 3;
            r.ClearancePatches = 40;
            r.ClearanceRegions = 5;
            r.SimSeconds = 120;
            string json = PerfOverlay.ToJson(r, new (string, string)[] { ("result", "\"MATCH END\"") });
            Console.WriteLine("[b12-t capture] perf json:\n" + json);
            string[] keys = { "\"cm_scopes\": {", "\"clearance\": {", "\"full\": 3", "\"patches\": 40", "\"regions\": 5", "\"per_min\": 21.5" };
            foreach (string n in Five)
            {
                bool has = json.Contains("\"" + n + "\": ");
                Say("json has " + n, has, true);
                Assert.IsTrue(has, n);
            }
            Assert.IsTrue(json.Contains("\"CM.OnlineView\": null"), "an unrecorded scope is null, never 0");
            for (int i = 0; i < keys.Length; i++)
            {
                bool has = json.Contains(keys[i]);
                Say("json has " + keys[i], has, true);
                Assert.IsTrue(has, keys[i]);
            }
            int open = 0, close = 0;
            foreach (char ch in json) { if (ch == '{') open++; else if (ch == '}') close++; }
            Say("json braces open/close", open + "/" + close, "equal");
            Assert.AreEqual(open, close);

            List<string> lines = r.PerfLines();
            foreach (string l in lines) Console.WriteLine("[b12-t capture] line: " + l);
            Say("match-end lines", lines.Count, 6);
            Assert.AreEqual(6, lines.Count);
            StringAssert.StartsWith("[CM.Perf] scope=CM.KaijuHill mean=0.2333 p95=0.4000 max=0.4000 frames=3", lines[0]);
            StringAssert.Contains("scope=CM.OnlineView not recorded", lines[4]);
            Assert.AreEqual("[CM.Perf] clearance full=3 patches=40 regions=5 perMin=21.50", lines[5]);
        }

        [Test]
        public void GoreShotsStagesTheAftermathAtTwentyAndSixtySeconds()
        {
            bool has = GoreShots.SceneList.Contains("aftermath");
            Say("GoreShots scenes", string.Join(",", GoreShots.SceneList), "contains aftermath");
            Assert.IsTrue(has);
            Assert.AreEqual("hill-fight", GoreShots.SceneList[GoreShots.SceneList.IndexOf("aftermath") - 1], "aftermath follows the hill fight");
            Say("aftermath offsets s", string.Join(",", GoreShots.AftermathSeconds), "20,60");
            CollectionAssert.AreEqual(new double[] { 20, 60 }, GoreShots.AftermathSeconds);
        }

        [Test]
        public void KaijuKoShotsPlansThreeStills()
        {
            KaijuKoShots.Still[] p = KaijuKoShots.Plan;
            foreach (KaijuKoShots.Still s in p)
                Say("still " + s.Name + " at s (after " + (s.AfterRecover ? "recover" : "knock-out") + ")", s.Seconds, s.Expect);
            Assert.AreEqual(3, p.Length);
            Assert.AreEqual(0.5, p[0].Seconds, 1e-9);
            Assert.IsFalse(p[0].AfterRecover);
            Assert.AreEqual(2.0, p[1].Seconds, 1e-9);
            Assert.IsFalse(p[1].AfterRecover);
            Assert.IsTrue(p[2].AfterRecover);
            Assert.AreEqual(KaijuKoShots.Expect.Down, p[1].Expect);
            // the REACHED test against the batch 11 down band: 60 deg root pitch, chin at 10 % of a 5 u body
            bool down = KaijuKoShots.Reached(KaijuKoShots.Expect.Down, 1f, 60f, 0.5f, 5f);
            bool upright = KaijuKoShots.Reached(KaijuKoShots.Expect.Down, 1f, 10f, 2.5f, 5f);
            bool standing = KaijuKoShots.Reached(KaijuKoShots.Expect.Up, 0f, 0f, float.NaN, 5f);
            Say("down pose 60 deg chin 0.5 of 5 u reached", down, true);
            Say("upright 10 deg chin 2.5 reached as down", upright, false);
            Say("standing reached as up", standing, true);
            Assert.IsTrue(down);
            Assert.IsFalse(upright);
            Assert.IsTrue(standing);
        }

        [Test]
        public void TheKaijuOwnershipLineHasEveryField()
        {
            const double hz = 30;
            var t = new SimProfile.KaijuOwnTally();
            void Feed(KaijuState s, int seconds) { for (int i = 0; i < seconds * 30; i++) t.Sample(s); }
            Feed(KaijuState.Egg, 30);
            Feed(KaijuState.Neutral, 10);
            Feed(KaijuState.Azure, 50);
            Feed(KaijuState.Berserk, 5);
            Feed(KaijuState.Crimson, 20);
            string line = KaijuOwnershipLog.Line(t, hz);
            Say("line", line, "egg 30 neutral 10 azure 50 crimson 20 berserk 5, 3 switches, first owned 40, longest azure 50");
            Assert.AreEqual("[CM.KaijuOwn] egg=30.0s neutral=10.0s azure=50.0s crimson=20.0s berserk=5.0s switches=3 firstOwned=40.0s longestHold=50.0s(azure)", line);
            string json = KaijuOwnershipLog.Json(t, hz);
            Console.WriteLine("[b12-t capture] kaiju_own json: " + json);
            StringAssert.Contains("\"switches\": 3", json);
            StringAssert.Contains("\"longest_hold_team\": \"azure\"", json);
        }
    }
}

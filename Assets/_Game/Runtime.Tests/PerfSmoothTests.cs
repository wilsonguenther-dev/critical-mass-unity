using System;
using System.Collections;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// perf-smooth-1: the loading budget (fake clock), the catch-up cap, zero allocation on the hottest HUD paths, the
    /// recorder's file names, and that the view-side build work never changes the sim.
    /// </summary>
    public sealed class PerfSmoothTests
    {
        // ---------------------------------------------------------------- allocation measure

        /// <summary>
        /// Bytes allocated by action. This Mono (Boehm GC) reports 0 from GC.GetAllocatedBytesForCurrentThread
        /// (integration batch 1), and Unity's Is.Not.AllocatingGCMemory constraint gave contradictory answers on the CI
        /// runner (it missed a new int[64] and flagged code that allocates nothing), so each counter is first proved on a
        /// known allocation: the per-thread counter, then the managed heap size (GC.GetTotalMemory without a collection,
        /// retried when a collection ran in between). When neither sees the probe, the test is Inconclusive, never a
        /// silent pass. The action should loop (hundreds of calls) so even a small per-call allocation is visible.
        /// </summary>
        internal static void AssertNoAllocation(Action action, string what)
        {
            string how;
            long bytes = Measure(action, out how);
            Assert.LessOrEqual(bytes, Slack(how), what + " allocated (" + how + "): " + bytes + " bytes");
        }

        // The heap-size counter moves in small blocks (Boehm hands out memory a block at a time), so a few KB of noise
        // is allowed there; a per-call allocation of 16 bytes over 1000 calls is 16 KB and fails.
        static long Slack(string how) => how == HeapCounter ? 4096 : 0;

        const string ThreadCounter = "GC.GetAllocatedBytesForCurrentThread", HeapCounter = "GC.GetTotalMemory delta";
        static object _probeKeep;

        static long ProbeAllocation()
        {
            var keep = new object[1000];
            for (int i = 0; i < keep.Length; i++) keep[i] = new int[16];   // about 100 KB in small objects
            _probeKeep = keep;
            return 1;
        }

        internal static long Measure(Action action, out string how)
        {
            long t0 = GC.GetAllocatedBytesForCurrentThread();
            ProbeAllocation();
            if (GC.GetAllocatedBytesForCurrentThread() - t0 >= 64 * 1024)
            {
                how = ThreadCounter;
                long before = GC.GetAllocatedBytesForCurrentThread();
                action();
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            how = HeapCounter;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                int gc0 = GC.CollectionCount(0);
                long p0 = GC.GetTotalMemory(false);
                ProbeAllocation();
                long probe = GC.GetTotalMemory(false) - p0;
                long a0 = GC.GetTotalMemory(false);
                action();
                long used = GC.GetTotalMemory(false) - a0;
                _probeKeep = null;
                if (GC.CollectionCount(0) != gc0) continue;   // a collection in between: the deltas mean nothing, retry
                if (probe < 64 * 1024) break;                   // this counter cannot see allocations here
                return Math.Max(0, used);
            }
            Assert.Inconclusive("no allocation counter on this runner sees a known 100 KB allocation");
            return 0;
        }

        [Test]
        public void TheAllocationMeasureCatchesAnAllocation()
        {
            // the measure itself must see a real allocation (a zero test that cannot fail verifies nothing)
            object keep = null;
            long bytes = Measure(() => keep = new int[64], out string how);
            Assert.Greater(bytes, 0, "a new int[64] was not seen by " + how);
            Assert.IsNotNull(keep);
        }

        static World NewWorld()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            return World.CreateWorld(new WorldOptions { Seed = 1337, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        static void Run(World w, int ticks)
        {
            InputFrame scratch = SimInput.EmptyInput();
            for (int i = 0; i < ticks; i++) MatchTick.Advance(w, null, scratch);
        }

        [Test]
        public void HudBinderReadAllocatesNothingInSteadyPlay()
        {
            World w = NewWorld();
            Run(w, 600);
            var s = new HudState();
            HudBinder.Read(w, HudBinder.LocalActor, s);
            HudBinder.Read(w, HudBinder.LocalActor, s);
            AssertNoAllocation(() => { for (int i = 0; i < 1000; i++) HudBinder.Read(w, HudBinder.LocalActor, s); }, "1000 HudBinder.Read");
        }

        [Test]
        public void GameHudRefreshAllocatesNothingWhileNumbersChange()
        {
            World w = NewWorld();
            Run(w, 600);
            var go = new GameObject("HUD alloc test");
            try
            {
                GameHud hud = go.AddComponent<GameHud>();
                hud.Build();
                // warm every number the loop below shows (cached strings are made once, on first use)
                for (int i = 0; i < 40; i++)
                {
                    w.Actors[0].Hp = w.Actors[0].MaxHp * (1.0 - i / 50.0);
                    hud.Refresh(w, 0.016f);
                }
                AssertNoAllocation(() =>
                {
                    for (int i = 0; i < 120; i++)
                    {
                        w.Actors[0].Hp = w.Actors[0].MaxHp * (1.0 - (i % 40) / 50.0);
                        hud.Refresh(w, 0.016f);
                    }
                }, "120 GameHud.Refresh with health changing");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void NumberCacheAndLoadingLinesAllocateNothingAfterFirstUse()
        {
            for (int i = 0; i < 1000; i++) NumberCache.Int(i);
            AssertNoAllocation(() =>
            {
                for (int i = 0; i < 1000; i++) NumberCache.Int(i);
                for (int f = 0; f < 200; f++) LoadingScreen.StepLine(LoadStep.Dressing, f / 60f);
            }, "cached numbers and the loading step line");
        }

        [Test]
        public void FrameCostSectionsAllocateNothing()
        {
            AssertNoAllocation(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    FrameCost.Mark m = FrameCost.Enter();
                    FrameCost.Exit(FrameCost.Slot.Hud, m);
                }
                FrameCost.ClearFrame();
            }, "1000 FrameCost sections");
        }

        // ---------------------------------------------------------------- loading budget

        /// <summary>A job of n steps, each advancing the fake clock by stepMs.</summary>
        static IEnumerator FakeJob(int n, double stepMs, double[] clock, List<int> ran)
        {
            for (int i = 0; i < n; i++)
            {
                clock[0] += stepMs;
                ran.Add(i);
                yield return null;
            }
        }

        [Test]
        public void TheLoadingBudgetNeverStartsAStepPastTheBudget()
        {
            var clock = new double[1];
            var ran = new List<int>();
            IEnumerator job = FakeJob(100, 1.5, clock, ran);
            const double budget = 6.0;
            int frames = 0;
            bool done = false;
            while (!done && frames < 1000)
            {
                double t0 = clock[0];
                done = FrameBudget.Run(job, budget, () => clock[0]);
                double used = clock[0] - t0;
                frames++;
                // a frame's work is under the budget plus at most one step (the step that crossed it)
                Assert.LessOrEqual(used, budget + 1.5 + 1e-9, "frame " + frames + " used " + used + " ms");
                if (!done) Assert.Greater(used, 0.0, "every frame before the end makes progress");
            }
            Assert.IsTrue(done, "the job finished");
            Assert.AreEqual(100, ran.Count, "every step ran exactly once");
            Assert.AreEqual(26, frames, "100 steps of 1.5 ms at 6 ms a frame: 4 steps a frame for 25 frames, then the frame that sees the end");
        }

        [Test]
        public void TheLoadingBudgetRunsOneStepEvenWhenAStepIsLong()
        {
            var clock = new double[1];
            var ran = new List<int>();
            IEnumerator job = FakeJob(3, 20.0, clock, ran);
            Assert.IsFalse(FrameBudget.Run(job, 6.0, () => clock[0]));
            Assert.AreEqual(1, ran.Count, "a 20 ms step runs alone in its frame");
            Assert.IsFalse(FrameBudget.Run(job, 6.0, () => clock[0]));
            Assert.IsFalse(FrameBudget.Run(job, 6.0, () => clock[0]));
            Assert.IsTrue(FrameBudget.Run(job, 6.0, () => clock[0]), "finished");
            Assert.IsTrue(FrameBudget.Run(null, 6.0, () => clock[0]), "no job is a finished job");
        }

        [Test]
        public void TheLoadingScreenMovesEveryFewFrames()
        {
            var seen = new HashSet<string>();
            for (int f = 0; f < 90; f++) seen.Add(LoadingScreen.StepLine(LoadStep.World, f / 60f));
            Assert.AreEqual(4, seen.Count, "the step line shows 0 to 3 dots, a new one every third of a second (all four within 1.5 s)");
            Assert.AreEqual(0f, LoadingScreen.TipAlpha(0f));
            Assert.AreEqual(1f, LoadingScreen.TipAlpha(0.6f));
            Assert.That(LoadingScreen.TipAlpha(0.3f), Is.InRange(0.3f, 0.7f));
            Assert.AreEqual(LoadingScreen.StepLabels[3], LoadingScreen.StepLine(LoadStep.WarmUp, 0f));
        }

        [Test]
        public void TheBuildBudgetIsSixMilliseconds()
        {
            Assert.AreEqual(6.0, WorldView.BuildBudgetMs, 1e-9);
            Assert.IsFalse(WorldView.Building, "no build runs in EditMode");
        }

        // ---------------------------------------------------------------- catch-up cap

        [Test]
        public void AfterAHitchTheClockRunsAtMostTheCapAndDropsTheRest()
        {
            var clock = new SimClock();
            double dt = 1.0 / 30;
            // a 1 s hitch at the game's pace: the input clamp (0.25 s) then the tick cap
            int n = clock.Advance(1.0, 1.0, dt, false);
            Assert.LessOrEqual(n, SimClock.MaxTicksPerFrame);
            Assert.Less(clock.Accumulator, dt, "the backlog past the cap is dropped, not carried");
            Assert.Greater(clock.DroppedTicks, 0);
            // the next normal frames run at most one tick each: no spiral
            for (int f = 0; f < 60; f++)
            {
                int k = clock.Advance(1.0 / 60, 1.0, dt, false);
                Assert.LessOrEqual(k, 1, "frame " + f + " after the hitch");
                double a = clock.Alpha(dt);
                Assert.That(a, Is.InRange(0.0, 1.0), "interpolation alpha stays in 0..1");
            }
        }

        [Test]
        public void AtSixtyHertzTheClockAlternatesZeroAndOneTicks()
        {
            var clock = new SimClock();
            double dt = 1.0 / 30;
            int ticks = 0, maxPerFrame = 0;
            for (int f = 0; f < 600; f++)
            {
                int k = clock.Advance(1.0 / 60, 1.0, dt, false);
                ticks += k;
                maxPerFrame = Math.Max(maxPerFrame, k);
            }
            Assert.That(ticks, Is.InRange(299, 300), "10 s at 1x: 300 ticks");
            Assert.AreEqual(1, maxPerFrame, "never two ticks in one 60 Hz frame");
        }

        // ---------------------------------------------------------------- determinism

        [Test]
        public void TheViewSideBuildWorkNeverChangesTheSim()
        {
            World a = NewWorld(), b = NewWorld();
            Run(a, 300);
            Run(b, 300);
            // everything the time-sliced world build runs off the sim's data: the plan and the field (on a worker thread)
            var cat = WorldKitCatalog.Load();
            WorldPlan plan = WorldDresser.Plan(b.Map, BasesFile.Load(), cat != null ? cat.Centroid : (Func<string, Vector3>)null,
                cat != null ? cat.Body : (Func<string, Vector3[]>)null);
            var task = System.Threading.Tasks.Task.Run(() => WorldField.Compute(plan, out _, out _));
            task.Wait();
            Run(a, 600);
            Run(b, 600);
            Assert.AreEqual(AutoplayArgs.WorldHash(a), AutoplayArgs.WorldHash(b), "the sim hash is unchanged by the build work");
        }

        [Test]
        public void TheFieldComputedOnAWorkerEqualsTheMainThreadOne()
        {
            World w = NewWorld();
            var cat = WorldKitCatalog.Load();
            WorldPlan plan = WorldDresser.Plan(w.Map, BasesFile.Load(), cat != null ? cat.Centroid : (Func<string, Vector3>)null,
                cat != null ? cat.Body : (Func<string, Vector3[]>)null);
            Color[] main = WorldField.Compute(plan, out int c0, out int r0);
            Color[] worker = System.Threading.Tasks.Task.Run(() => WorldField.Compute(plan, out _, out _)).Result;
            Assert.AreEqual(main.Length, worker.Length);
            for (int i = 0; i < main.Length; i++)
                if (main[i] != worker[i]) Assert.Fail("texel " + i + " differs: " + main[i] + " vs " + worker[i]);
            Assert.AreEqual(c0 * r0, main.Length);
        }

        // ---------------------------------------------------------------- recorder

        [Test]
        public void TheRecorderWritesOneCsvPerMatchBesideItsPerfFile()
        {
            Assert.AreEqual(System.IO.Path.Combine("out", "frames.csv"), FrameRecorder.CsvPathFor(System.IO.Path.Combine("out", "perf.json")));
            Assert.AreEqual(System.IO.Path.Combine("out", "frames-m2.csv"), FrameRecorder.CsvPathFor(System.IO.Path.Combine("out", "perf-m2.json")));
            Assert.IsNull(FrameRecorder.CsvPathFor(null));
            Assert.IsFalse(FrameRecorder.Active, "never records in a normal run");
        }
    }
}

using System;
using System.Diagnostics;
using CriticalMass.Net;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b14 sim cost: the HEAVY TICK guard. The median tick hides what a player feels as a hitch: the occasional tick that
    /// rebuilds flow fields and proves routes. Measured on the same job (ci/b14-simcost, runs 44189936888 and later), the
    /// candidate f4d9f2a2 kept its median (27.5 us) but its trimmed mean rose 1.27x over a72d2a5e because an expired
    /// ladder or bridge whose lift the Kaiju route guard refused retried EVERY tick, each retry paying two Dijkstra fields and
    /// the guard's searches. These tests pin (1) the deterministic work: route proofs worked out in full per whole match, and
    /// that the verdict memo gives bit-identical matches; (2) the p99 / max / trimmed-mean tick cost, in units of one hill
    /// flow field on the same machine in the same process, so the limit travels across runners; (3) 0 B per tick on the memo.
    /// </summary>
    [TestFixture]
    public sealed class SimTickCostTests
    {
        // Pinned from the printed measurement on the fix branch (fix/b14-sim-cost); see the PR. Each limit = measured x ~1.25.
        const int MaxRouteProofsPerMatch4242 = 400;
        const double MaxP99InFields = 6.0, MaxMaxInFields = 60.0, MaxTrimmedInFields = 0.6;

        static World Make(int seed, int team) =>
            World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = team, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault() });

        /// <summary>The whole match on (seed, team): hashes every 600 ticks, and the route-proof counters.</summary>
        static ulong[] PlayWhole(int seed, int team, bool memo, out int proofs, out int hits, out int ticks)
        {
            bool prev = SimProfile.NoRouteMemo;
            SimProfile.NoRouteMemo = !memo;
            try
            {
                World w = Make(seed, team);
                var scratch = SimInput.EmptyInput();
                var h = new ulong[25];
                int k = 0;
                SimProfile.ResetCounters();
                int t = 0;
                for (; t < 14400 && w.Match.Phase == MatchPhase.Playing; t++)
                {
                    MatchTick.Advance(w, null, scratch);
                    if (t % 600 == 599 && k < h.Length) h[k++] = StateHash.Of(w);
                }
                if (k < h.Length) h[k] = StateHash.Of(w);
                proofs = SimProfile.RouteProofs;
                hits = SimProfile.RouteMemoHits;
                ticks = t;
                return h;
            }
            finally
            {
                SimProfile.NoRouteMemo = prev;
            }
        }

        [TestCase(4242, 16)]
        [TestCase(1337, 16)]
        [TestCase(42, 5)]
        public void RouteVerdictMemoGivesTheSameMatchAndCutsTheProofs(int seed, int team)
        {
            ulong[] off = PlayWhole(seed, team, false, out int pOff, out int hOff, out int tOff);
            ulong[] on = PlayWhole(seed, team, true, out int pOn, out int hOn, out int tOn);
            int diff = -1;
            for (int i = 0; i < on.Length && diff < 0; i++) if (on[i] != off[i]) diff = i;
            Console.WriteLine($"[sim tick cost] seed {seed} {team}v{team} whole match ({tOn} ticks): route proofs in full {pOn} with the memo (+{hOn} answered by it) vs {pOff} without; hashes every 600 ticks {(diff < 0 ? "IDENTICAL" : "DIFFER at sample " + diff)}; end hash {on[on.Length - 1]:X16} (limit: identical, proofs with the memo <= without)");
            Assert.AreEqual(tOff, tOn, "the memo must not change the match length");
            Assert.AreEqual(-1, diff, "the route verdict memo must not change the match");
            Assert.LessOrEqual(pOn, pOff);
            Assert.AreEqual(pOff, pOn + hOn, "every proof is either worked out or answered by the memo");
            if (seed == 4242) Assert.LessOrEqual(pOn, MaxRouteProofsPerMatch4242, "route proofs in full per whole 16v16 match (seed 4242): heavy ticks creep back when this grows");
        }

        /// <summary>One hill flow field on the fresh map, median of 41 after warm-up: this machine's unit of heavy work.</summary>
        static double FieldUnitUs()
        {
            World w = Make(4242, 16);
            var goal = new[] { new PointXZ(w.Map.Hill.X, w.Map.Hill.Z) };
            var f = FlowField.ComputeFlowField(w.Map, goal);
            for (int i = 0; i < 10; i++) FlowField.ComputeFlowField(w.Map, goal, f);
            var s = new long[41];
            for (int i = 0; i < s.Length; i++)
            {
                long c0 = Stopwatch.GetTimestamp();
                FlowField.ComputeFlowField(w.Map, goal, f);
                s[i] = Stopwatch.GetTimestamp() - c0;
            }
            Array.Sort(s);
            return s[s.Length / 2] * 1e6 / Stopwatch.Frequency;
        }

        [Test]
        public void GridReachAnswersExactlyWhatTheFlowFieldAnswers()
        {
            // the shipped map with random holes punched (seeded LCG, no shared RNG): every pad / goal pairing, 400 grids
            World w = Make(4242, 16);
            MapDef map = w.Map;
            byte[] keep = (byte[])map.Cells.Clone();
            var hill = new[] { new PointXZ(map.Hill.X, map.Hill.Z) };
            var shrine = new[] { new PointXZ(map.Shrine.X, map.Shrine.Z), new PointXZ(map.Shrine.X + 2.5, map.Shrine.Z), new PointXZ(-3, 4) };
            uint s = 12345;
            int cases = 0, mismatches = 0, reachedA = 0, cutA = 0;
            try
            {
                for (int g = 0; g < 400; g++)
                {
                    Array.Copy(keep, map.Cells, keep.Length);
                    int holes = g % 40 * 25;
                    for (int h = 0; h < holes; h++)
                    {
                        s = s * 1664525u + 1013904223u;
                        int i = (int)(s % (uint)map.Cells.Length);
                        if (MapGrid.IsWalkableType(map.Cells[i])) map.Cells[i] = Cell.Void;
                    }
                    PointXZ[] goals = g % 2 == 0 ? hill : shrine;
                    PadDef a = map.Pads[0], c = map.Pads[1];
                    s = s * 1664525u + 1013904223u;
                    double qx = s % (uint)map.Width + 0.5, qz = (s >> 8) % (uint)map.Height + 0.5;
                    FlowField f = FlowField.ComputeFlowField(map, goals);
                    bool fa = double.IsFinite(f.Dist[JsMath.FloorToInt(a.Z) * map.Width + JsMath.FloorToInt(a.X)]);
                    bool fq = double.IsFinite(f.Dist[JsMath.FloorToInt(qz) * map.Width + JsMath.FloorToInt(qx)]);
                    int r = GridReach.Reached(map, goals, a.X, a.Z, qx, qz);
                    cases++;
                    if (fa != ((r & 1) != 0) || fq != ((r & 2) != 0)) mismatches++;
                    if (fa) reachedA++; else cutA++;
                }
            }
            finally
            {
                Array.Copy(keep, map.Cells, keep.Length);
            }
            Console.WriteLine($"[sim tick cost] GridReach vs ComputeFlowField: {cases} grids, {mismatches} answers differ (limit 0); pad reached in {reachedA}, cut off in {cutA}");
            Assert.AreEqual(0, mismatches);
            Assert.Greater(cutA, 0, "some grids cut the pad off (the test covers both answers)");
        }

        [Test]
        public void HeavyTicksStayWithinTheirBudgetInFlowFieldUnits()
        {
            double unit = FieldUnitUs();
            int[] seeds = { 4242, 1337, 7 };
            var per = new long[seeds.Length * 14400];
            int n = 0;
            foreach (int seed in seeds)
            {
                World w = Make(seed, 16);
                var scratch = SimInput.EmptyInput();
                for (int t = 0; t < 300; t++) MatchTick.Advance(w, null, scratch);
                for (int t = 300; t < 14400 && w.Match.Phase == MatchPhase.Playing; t++)
                {
                    long c0 = Stopwatch.GetTimestamp();
                    MatchTick.Advance(w, null, scratch);
                    per[n++] = Stopwatch.GetTimestamp() - c0;
                }
            }
            double us = 1e6 / Stopwatch.Frequency;
            Array.Sort(per, 0, n);
            int keep = n - n / 50;
            long sum = 0;
            for (int i = 0; i < keep; i++) sum += per[i];
            double med = per[n / 2] * us, p99 = per[Math.Min(n - 1, n * 99 / 100)] * us, mx = per[n - 1] * us, trim = sum * us / keep;
            Console.WriteLine($"[sim tick cost] 16v16 Full seeds 4242 1337 7, {n} ticks: median {med:0.0} us, p99 {p99:0.0} us, max {mx:0.0} us, trimmed mean {trim:0.0} us; one hill flow field {unit:0.0} us; in field units p99 {p99 / unit:0.00} (limit {MaxP99InFields}), max {mx / unit:0.00} (limit {MaxMaxInFields}), trimmed {trim / unit:0.000} (limit {MaxTrimmedInFields})");
            Assert.LessOrEqual(p99 / unit, MaxP99InFields, "p99 tick cost in hill-flow-field units");
            Assert.LessOrEqual(mx / unit, MaxMaxInFields, "heaviest tick in hill-flow-field units");
            Assert.LessOrEqual(trim / unit, MaxTrimmedInFields, "trimmed mean tick cost in hill-flow-field units");
        }

        [Test]
        public void RouteVerdictMemoAllocatesNothingPerTick()
        {
            World w = Make(4242, 16);
            var scratch = SimInput.EmptyInput();
            for (int t = 0; t < 600; t++) MatchTick.Advance(w, null, scratch);
            // the memo path directly: the same refused-or-allowed proof asked again and again, and a fresh one every few calls
            int cx = JsMath.FloorToInt(w.Map.Hill.X) + 6, cz = JsMath.FloorToInt(w.Map.Hill.Z);
            Terraform.DigKeepsRoutes(w, cz * w.Map.Width + cx);
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 300; i++) Terraform.DigKeepsRoutes(w, cz * w.Map.Width + cx + (i % 3));
            long memoBytes = GC.GetAllocatedBytesForCurrentThread() - b0;
            SimProfile.ResetCounters();
            long b1 = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 300; t++) MatchTick.Advance(w, null, scratch);
            long stepBytes = GC.GetAllocatedBytesForCurrentThread() - b1;
            Console.WriteLine($"[sim tick cost] allocation: 300 route proofs through the memo {memoBytes} B (limit 0 B); 300 whole ticks 16v16 seed 4242 Full after 600 warm-up {stepBytes} B (route proofs {SimProfile.RouteProofs}, memo hits {SimProfile.RouteMemoHits})");
            Assert.AreEqual(0, memoBytes, "the route verdict memo allocates nothing");
            Assert.AreEqual(0, stepBytes, "a whole tick allocates nothing after warm-up");
        }
    }
}

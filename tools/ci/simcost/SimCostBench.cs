using System;
using System.Diagnostics;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b14 sim-cost harness, CI only (tools/ci/simcost; never merged): the same benchmark compiled into every arm's tree and
    /// run alternately in one job. Default (Full) config, 16v16 seeds 4242 1337 7 99 2024 31337 and 5v5 seed 42, whole match
    /// (cap 14400 ticks), 300 untimed warm-up ticks per world, only MatchTick.Advance timed. Prints the per-tick histogram
    /// (median, p95, p99, p99.9, max, trimmed 2% mean). In a probed tree (probe.py --patch) it also prints, for the heaviest
    /// 1% of ticks, each system's share of their time and the nested probes' time and calls per heavy tick.
    /// </summary>
    [TestFixture]
    public sealed class SimCostBench
    {
        [Test]
        public void SimCostPerTick()
        {
            string tag = Environment.GetEnvironmentVariable("CM_COST_TAG") ?? "?";
            int[] seeds16 = { 4242, 1337, 7, 99, 2024, 31337 };
            int cap = seeds16.Length * 14400 + 14400;
            var per = new long[cap];
            var grid = new byte[cap];
            int nt = SimCostProbe.TopNames.Length, nn = SimCostProbe.NestNames.Length;
            bool probed = nt > 0;
            var top = probed ? new float[(long)cap * nt] : null;
            var nest = probed ? new float[(long)cap * nn] : null;
            var calls = probed ? new short[(long)cap * nn] : null;
            int n = 0;
            long total = 0;
            for (int k = 0; k <= seeds16.Length; k++)
            {
                bool five = k == seeds16.Length;
                var w = World.CreateWorld(new WorldOptions { Seed = five ? 42 : seeds16[k], TeamSize = five ? 5 : 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault() });
                var scratch = SimInput.EmptyInput();
                for (int t = 0; t < 300; t++) { MatchTick.Advance(w, null, scratch); SimCostProbe.Close(); }
                SimCostProbe.Clear();
                for (int t = 300; t < 14400 && w.Match.Phase == MatchPhase.Playing; t++)
                {
                    int gv = w.GridVersion;
                    long c0 = Stopwatch.GetTimestamp();
                    MatchTick.Advance(w, null, scratch);
                    if (probed) SimCostProbe.Close();
                    long dt = Stopwatch.GetTimestamp() - c0;
                    grid[n] = (byte)Math.Min(255, w.GridVersion - gv);
                    if (probed)
                    {
                        for (int j = 0; j < nt; j++) top[(long)n * nt + j] = SimCostProbe.Top[j];
                        for (int j = 0; j < nn; j++) { nest[(long)n * nn + j] = SimCostProbe.Nest[j]; calls[(long)n * nn + j] = (short)Math.Min(32000, SimCostProbe.NestCalls[j]); }
                        SimCostProbe.Clear();
                    }
                    per[n++] = dt; total += dt;
                }
            }
            double us = 1e6 / Stopwatch.Frequency;
            var s = new long[n]; Array.Copy(per, s, n); Array.Sort(s);
            int keep = n - n / 50; long sum = 0;
            for (int i = 0; i < keep; i++) sum += s[i];
            double P(double q) => s[Math.Min(n - 1, (int)(q * n))] * us;
            int gticks = 0; for (int i = 0; i < n; i++) if (grid[i] > 0) gticks++;
            Console.WriteLine($"[simcost] {tag}: {sum * us / keep:0.00} us per tick (trimmed 2%), raw mean {total * us / Math.Max(1, n):0.00} us, median {P(0.5):0.00} us, p95 {P(0.95):0.00} us, p99 {P(0.99):0.00} us, p99.9 {P(0.999):0.00} us, max {s[n - 1] * us:0.00} us, {n} ticks timed, grid-change ticks {gticks}");
            // The heaviest 1% of ticks: how many changed the grid.
            long thr = s[n - Math.Max(1, n / 100)];
            int heavy = 0, heavyGrid = 0; double heavySum = 0;
            for (int i = 0; i < n; i++) if (per[i] >= thr) { heavy++; heavySum += per[i]; if (grid[i] > 0) heavyGrid++; }
            Console.WriteLine($"[simcost] {tag}: heaviest 1%: {heavy} ticks >= {thr * us:0.0} us, mean {heavySum * us / heavy:0.0} us, {heavyGrid} of them changed the grid");
            if (!probed) return;
            var tsum = new double[nt]; var tall = new double[nt];
            var nsum = new double[nn]; var nall = new double[nn]; var csum = new double[nn]; var call = new double[nn];
            for (int i = 0; i < n; i++)
            {
                bool h = per[i] >= thr;
                for (int j = 0; j < nt; j++) { double v = top[(long)i * nt + j]; tall[j] += v; if (h) tsum[j] += v; }
                for (int j = 0; j < nn; j++) { double v = nest[(long)i * nn + j]; nall[j] += v; csum[j] += h ? calls[(long)i * nn + j] : 0; call[j] += calls[(long)i * nn + j]; if (h) nsum[j] += v; }
            }
            double hs = 0; for (int j = 0; j < nt; j++) hs += tsum[j];
            var order = new int[nt]; for (int j = 0; j < nt; j++) order[j] = j;
            Array.Sort(order, (a, b) => tsum[b].CompareTo(tsum[a]));
            foreach (int j in order)
                Console.WriteLine($"[simprobe] {tag}: TOP {SimCostProbe.TopNames[j]}: heavy share {100 * tsum[j] / hs:0.0}%, {tsum[j] * us / heavy:0.0} us per heavy tick, {tall[j] * us / n:0.00} us per tick overall");
            var o2 = new int[nn]; for (int j = 0; j < nn; j++) o2[j] = j;
            Array.Sort(o2, (a, b) => nsum[b].CompareTo(nsum[a]));
            foreach (int j in o2)
                Console.WriteLine($"[simprobe] {tag}: NEST {SimCostProbe.NestNames[j]}: heavy share {100 * nsum[j] / hs:0.0}%, {nsum[j] * us / heavy:0.0} us and {csum[j] / heavy:0.00} calls per heavy tick, {nall[j] * us / n:0.00} us and {call[j] / n:0.000} calls per tick overall");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/bots/bots.test.ts (09e0904) describe "bots: CM-21 heading (blended flow + turn-rate limit)", 3 cases.
    /// Every actor (human slot included) is on bots, exactly like the browser tickOnce (MatchTick.Advance with no human frame).
    /// </summary>
    [TestFixture]
    public sealed class BotsHeadingTests
    {
        private const string HeadingBaseline = "Assets/_Game/Sim.Tests/Data/heading-before.json";

        private static readonly InputFrame Frame = SimInput.EmptyInput();

        private int[] _before = new int[0];
        private int[] _after = new int[0];

        private delegate void VelFn(World w, int id, out double x, out double z);

        private static World MakeWorld(int seed, int teamSize)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = seed,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            });
        }

        /// <summary>One SimDriver step with every actor (human included) on bots.</summary>
        private static void TickOnce(World w)
        {
            MatchTick.Advance(w, null, Frame);
        }

        /// <summary>Port of V8's Math.hypot for two arguments (max-normalised Kahan sum, then sqrt times max).</summary>
        private static double Hypot(double a, double b)
        {
            bool oneArgIsNaN = false;
            double max = 0;
            double absA = 0, absB = 0;
            if (double.IsNaN(a)) oneArgIsNaN = true;
            else
            {
                absA = Math.Abs(a);
                if (absA > max) max = absA;
            }
            if (double.IsNaN(b)) oneArgIsNaN = true;
            else
            {
                absB = Math.Abs(b);
                if (absB > max) max = absB;
            }
            if (max == double.PositiveInfinity) return double.PositiveInfinity;
            if (oneArgIsNaN) return double.NaN;
            if (max == 0) return 0;
            double sum = 0;
            double compensation = 0;
            double n = absA / max;
            double summand = n * n - compensation;
            double preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
            n = absB / max;
            summand = n * n - compensation;
            preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
            return Math.Sqrt(sum) * max;
        }

        /// <summary>CM-21 (2): per-actor heading jump counts over ticks sim steps of a 5v5 match, velocity read by vel.</summary>
        private static int[] HeadingJumps(int seed, int ticks, VelFn vel)
        {
            World w = MakeWorld(seed, 5);
            int n = w.ActorCount;
            var heading = new List<List<double>>();
            var sampled = new List<List<bool>>();
            for (int i = 0; i < n; i++)
            {
                heading.Add(new List<double> { 0 });
                sampled.Add(new List<bool> { false });
            }
            for (int t = 1; t <= ticks; t++)
            {
                TickOnce(w);
                for (int i = 0; i < n; i++)
                {
                    Actor a = w.Actors[i];
                    vel(w, i, out double vx, out double vz);
                    double sp = Hypot(vx, vz);
                    heading[i].Add(JsMath.Atan2(vz, vx));
                    sampled[i].Add(a.Alive && sp >= HeadingMetric.HeadingMinSpeedFraction * w.Config.Class(a.Cls).MoveSpeed);
                }
            }
            var jumps = new int[n];
            for (int i = 0; i < n; i++) jumps[i] = HeadingMetric.CountHeadingJumps(heading[i], sampled[i]);
            return jumps;
        }

        /// <summary>Hand parser for the fixed baseline shape: the integers of the one array, "perBot".</summary>
        private static int[] ReadPerBot(string text)
        {
            int key = text.IndexOf("\"perBot\"", StringComparison.Ordinal);
            Assert.GreaterOrEqual(key, 0, "perBot missing in " + HeadingBaseline);
            int open = text.IndexOf('[', key);
            int close = text.IndexOf(']', open);
            string body = text.Substring(open + 1, close - open - 1).Trim();
            if (body.Length == 0) return new int[0];
            string[] parts = body.Split(',');
            var values = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) values[i] = int.Parse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
            return values;
        }

        [OneTimeSetUp]
        public void BeforeAll()
        {
            Assert.IsTrue(File.Exists(HeadingBaseline), "baseline missing: " + Path.GetFullPath(HeadingBaseline));
            _before = ReadPerBot(File.ReadAllText(HeadingBaseline));
            _after = HeadingJumps(1, 300, (World w, int id, out double x, out double z) =>
            {
                x = w.Vx[id];
                z = w.Vz[id];
            });
            TestContext.Progress.WriteLine("heading jumps per bot (seed 1 5v5, 300 ticks): before " + string.Join(", ", _before)
                + " | after " + string.Join(", ", _after));
        }

        [Test]
        public void HeadingEveryBotHasLe3HeadingJumpsGt30DegPer3TickWindowIn300TicksOfASeed15v5Match()
        {
            Assert.AreEqual(10, _after.Length);
            for (int i = 0; i < _after.Length; i++) Assert.LessOrEqual(_after[i], 3, "bot " + i);
        }

        [Test]
        public void HeadingEveryBotHasLeTheJumpsRecordedOnMainE2eBaselinesHeadingBeforeJson()
        {
            Assert.AreEqual(_after.Length, _before.Length);
            for (int i = 0; i < _after.Length; i++) Assert.LessOrEqual(_after[i], _before[i], "bot " + i);
        }

        [Test]
        public void HeadingBotsStillMoveMeanSpeedOverTheRunGt1US()
        {
            World w = MakeWorld(1, 5);
            double dt = SimStep.Dt(w.Config);
            double sum = 0;
            int samples = 0;
            for (int t = 0; t < 300; t++)
            {
                TickOnce(w);
                for (int i = 0; i < w.ActorCount; i++)
                {
                    sum += Hypot(w.X[i] - w.PrevX[i], w.Z[i] - w.PrevZ[i]) / dt;
                    samples++;
                }
            }
            Assert.Greater(sum / samples, 1);
        }
    }
}

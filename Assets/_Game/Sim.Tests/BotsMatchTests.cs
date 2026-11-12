using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/bots/bots.test.ts (09e0904) describes "bots (a)", "bots (b)/(d)", "bots (c)" and "bots (f): perf".</summary>
    [TestFixture]
    public sealed class BotsMatchTests
    {
        /// <summary>The one scratch frame every bot-driven tick fills (browser module-level `frame`).</summary>
        private static readonly InputFrame Frame = SimInput.EmptyInput();

        // (a): 5v5 seed 1, 120 s.
        private static World _worldA;
        private static int _samplesTotal;
        private static int _samplesAzure;
        private static int _samplesCrimson;
        private static int _flapWorstA;

        // (b)/(d): 16v16 seed 2, 240 s.
        private static World _worldB;
        private static int _owned;
        private static int _ticks;
        private static double _dist;
        private static int _aliveSteps;
        private static double _worstStill;
        private static int _flapWorstB;
        private static HashSet<AiState> _seen;

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
        private static void TickOnce(World world)
        {
            MatchTick.Advance(world, null, Frame);
        }

        private static void Run(World world, double seconds, Action<World> onTick)
        {
            int steps = (int)JsMath.Round(seconds * world.Config.SimHz);
            for (int s = 0; s < steps; s++)
            {
                TickOnce(world);
                onTick?.Invoke(world);
            }
        }

        /// <summary>Flapping tracker: aiState change times per actor; reports the worst count inside any 10 s window.</summary>
        private sealed class FlapTracker
        {
            private readonly AiState[] _last;
            private readonly List<double>[] _times;

            public FlapTracker(World world)
            {
                int n = world.Actors.Length;
                _last = new AiState[n];
                _times = new List<double>[n];
                for (int i = 0; i < n; i++)
                {
                    _last[i] = world.Actors[i].AiState;
                    _times[i] = new List<double>();
                }
            }

            public void Sample(World w)
            {
                for (int i = 0; i < w.ActorCount; i++)
                {
                    AiState s = w.Actors[i].AiState;
                    if (s != _last[i])
                    {
                        _times[i].Add(w.Match.Time);
                        _last[i] = s;
                    }
                }
            }

            public int Worst(double windowSeconds)
            {
                int worst = 0;
                foreach (List<double> t in _times)
                {
                    int lo = 0;
                    for (int hi = 0; hi < t.Count; hi++)
                    {
                        while (t[hi] - t[lo] > windowSeconds) lo++;
                        worst = Math.Max(worst, hi - lo + 1);
                    }
                }
                return worst;
            }
        }

        private static bool HillPresence(World w, TeamId team)
        {
            HillDef h = w.Map.Hill;
            double r2 = w.Config.Hill.Radius * w.Config.Hill.Radius;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.Team != team) continue;
                double dx = w.X[i] - h.X;
                double dz = w.Z[i] - h.Z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        private static double[] Head(double[] values, int count)
        {
            var r = new double[count];
            Array.Copy(values, r, count);
            return r;
        }

        [OneTimeSetUp]
        public void ComputeRuns()
        {
            // (a) 5v5 seed 1, 120 s.
            _samplesTotal = 0;
            _samplesAzure = 0;
            _samplesCrimson = 0;
            _worldA = MakeWorld(1, 5);
            var flA = new FlapTracker(_worldA);
            int k = 0;
            Run(_worldA, 120, w =>
            {
                flA.Sample(w);
                if (++k % 15 != 0 || w.Match.Time <= 20) return;
                _samplesTotal++;
                if (HillPresence(w, TeamId.Azure)) _samplesAzure++;
                if (HillPresence(w, TeamId.Crimson)) _samplesCrimson++;
            });
            _flapWorstA = flA.Worst(10);

            // (b)/(d) 16v16 seed 2, 240 s.
            _owned = 0;
            _ticks = 0;
            _dist = 0;
            _aliveSteps = 0;
            _worstStill = 0;
            _seen = new HashSet<AiState>();
            _worldB = MakeWorld(2, 16);
            var flB = new FlapTracker(_worldB);
            int n = _worldB.ActorCount;
            var refX = new double[n];
            var refZ = new double[n];
            var stillSince = new double[n];
            for (int i = 0; i < n; i++) stillSince[i] = -1;
            Run(_worldB, 240, w =>
            {
                flB.Sample(w);
                _ticks++;
                if (w.Match.HillState == HillState.Azure || w.Match.HillState == HillState.Crimson) _owned++;
                double t = w.Match.Time;
                for (int i = 0; i < n; i++)
                {
                    Actor a = w.Actors[i];
                    _seen.Add(a.AiState);
                    if (!a.Alive)
                    {
                        stillSince[i] = -1;
                        continue;
                    }
                    double dx = w.X[i] - w.PrevX[i];
                    double dz = w.Z[i] - w.PrevZ[i];
                    double d = Math.Sqrt(dx * dx + dz * dz);
                    if (d < 1)
                    {
                        // > 1 unit in one step is a respawn teleport, not movement.
                        _dist += d;
                        _aliveSteps++;
                    }
                    if (a.AiState == AiState.HoldHill)
                    {
                        stillSince[i] = -1;
                        continue;
                    }
                    if (stillSince[i] < 0 || Math.Abs(w.X[i] - refX[i]) > 0.05 || Math.Abs(w.Z[i] - refZ[i]) > 0.05)
                    {
                        refX[i] = w.X[i];
                        refZ[i] = w.Z[i];
                        stillSince[i] = t;
                    }
                    else
                    {
                        _worstStill = Math.Max(_worstStill, t - stillSince[i]);
                    }
                }
            });
            _flapWorstB = flB.Worst(10);
        }

        [Test]
        public void AEachTeamHasABotInsideTheHillRadiusOnGe70PercentOfSamplesAfter20SAndBothTeamsDeliveredBiomass()
        {
            Assert.Greater(_samplesTotal, 150);
            Assert.GreaterOrEqual((double)_samplesAzure / _samplesTotal, 0.7);
            Assert.GreaterOrEqual((double)_samplesCrimson / _samplesTotal, 0.7);
            Assert.Greater(_worldA.Match.Delivered[(int)TeamId.Azure].Biomass, 0);
            Assert.Greater(_worldA.Match.Delivered[(int)TeamId.Crimson].Biomass, 0);
        }

        [Test]
        public void D5v5NoBotChangesAiStateMoreThan12TimesInAny10SWindow()
        {
            Assert.LessOrEqual(_flapWorstA, 12);
        }

        [Test]
        public void BBothTeamsDeliveredBiomass()
        {
            Assert.Greater(_worldB.Match.Delivered[(int)TeamId.Azure].Biomass, 0);
            Assert.Greater(_worldB.Match.Delivered[(int)TeamId.Crimson].Biomass, 0);
        }

        [Test]
        public void BTheHillIsOwnedAzureOrCrimsonNotContestedGe50PercentOfTheTime()
        {
            Assert.GreaterOrEqual((double)_owned / _ticks, 0.5);
        }

        [Test]
        public void BMeanBotSpeedGt1USAndNoBotSitsStillPlusMinus005ForGt6SOutsideHoldHill()
        {
            Assert.Greater(_dist / (_aliveSteps * SimStep.Dt(_worldB.Config)), 1);
            Assert.LessOrEqual(_worstStill, 6);
        }

        [Test]
        public void D16v16NoBotChangesAiStateMoreThan12TimesInAny10SWindowTheFSMUsesItsStates()
        {
            Assert.LessOrEqual(_flapWorstB, 12);
            foreach (AiState s in new[] { AiState.ToHill, AiState.HoldHill, AiState.Fight, AiState.Gather, AiState.Deliver })
                Assert.IsTrue(_seen.Contains(s), s.ToString());
        }

        [Test]
        public void CTheSameSeedGivesIdenticalWorldXAfter60SDifferentSeedsDiffer()
        {
            World a = MakeWorld(7, 5);
            World b = MakeWorld(7, 5);
            World c = MakeWorld(8, 5);
            Run(a, 60, null);
            Run(b, 60, null);
            Run(c, 60, null);
            CollectionAssert.AreEqual(Head(b.X, b.ActorCount), Head(a.X, a.ActorCount));
            CollectionAssert.AreEqual(Head(b.Z, b.ActorCount), Head(a.Z, a.ActorCount));
            CollectionAssert.AreNotEqual(Head(c.X, c.ActorCount), Head(a.X, a.ActorCount));
        }

        [Test]
        public void PersonalityWeightsAreDrawnOnceFromTheBotRng0812AndDifferBySeed()
        {
            BotsState a = BotBrain.GetBotsState(MakeWorld(1, 16));
            BotsState b = BotBrain.GetBotsState(MakeWorld(2, 16));
            double[] pa = Head(a.Aggression, 32);
            var all = new List<double>(pa);
            all.AddRange(Head(a.Greed, 32));
            foreach (double v in all)
            {
                Assert.GreaterOrEqual(v, 0.8 - 1e-6);
                Assert.LessOrEqual(v, 1.2 + 1e-6);
            }
            CollectionAssert.AreNotEqual(Head(b.Aggression, 32), pa);
            var distinct = new HashSet<string>();
            foreach (double v in pa) distinct.Add(v.ToString("F3", CultureInfo.InvariantCulture));
            Assert.Greater(distinct.Count, 20);
        }

        [Test, Explicit("perf"), Category("Perf")]
        public void FPerfBotsThinkAt16v16AveragesLt15MsOver600TicksWithGe10BarricadesPlacedAndExpiring()
        {
            World w = MakeWorld(9, 16);
            int width = w.Map.Width;
            // Barricade churn on bridge cells the bots walk (B1/B3/B5 both sides): place every 20 ticks, expire 40 later.
            var spots = new List<(int Cx, int Cz)>();
            for (int cz = 0; cz < w.Map.Height; cz++)
                for (int cx = 0; cx < width; cx++)
                    if (w.Map.Cells[cz * width + cx] == Cell.Bridge && (cx + cz) % 7 == 0) spots.Add((cx, cz));
            Assert.Greater(spots.Count, 20);
            var live = new Queue<(int Cx, int Cz, int At)>();
            int placed = 0;
            int expired = 0;
            double total = 0;
            InputFrame f = SimInput.EmptyInput();
            double dt = SimStep.Dt(w.Config);
            var sw = new Stopwatch();
            for (int s = 0; s < 600; s++)
            {
                if (s % 20 == 0)
                {
                    (int cx, int cz) = spots[(s / 20) % spots.Count];
                    if (w.Map.Cells[cz * width + cx] == Cell.Bridge)
                    {
                        MapGrid.SetCell(w, cx, cz, Cell.Barricade);
                        live.Enqueue((cx, cz, s));
                        placed++;
                    }
                }
                while (live.Count > 0 && s - live.Peek().At >= 40)
                {
                    (int Cx, int Cz, int At) bar = live.Dequeue();
                    MapGrid.SetCell(w, bar.Cx, bar.Cz, Cell.Bridge);
                    expired++;
                }
                BotBrain.FillBotInput(w, 0, f);
                SimInput.ApplyInput(w, 0, f);
                sw.Restart();
                BotBrain.BotsThink(w, dt);
                sw.Stop();
                total += sw.Elapsed.TotalMilliseconds;
                for (int id = 1; id < w.ActorCount; id++)
                {
                    BotBrain.FillBotInput(w, id, f);
                    SimInput.ApplyInput(w, id, f);
                }
                SimStep.Step(w);
            }
            Assert.GreaterOrEqual(placed, 10);
            Assert.GreaterOrEqual(expired, 10);
            double avg = total / 600;
            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "botsThink 16v16 avg {0:F4} ms over 600 ticks; barricades placed {1}, expired {2}", avg, placed, expired));
            Assert.Less(avg, 1.5);
        }
    }
}

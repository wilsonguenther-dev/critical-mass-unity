using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// DG-24 soak: whole 16v16 matches with the new classes on both teams (the default ClassMix keeps them at 0, U-060),
    /// every bot on its brain and actor 0 on autopilot, at Normal and Hard. Every tick: no NaN, no living actor inside solid
    /// ground, no travelling bot still for more than StuckTicks, every unbroken piece on the map still Bridge under it. Every
    /// 10 s both pads reach the hill. At the end: both teams reached the hill and the owned-hill share is inside batch B's
    /// band. Prints one JSON line per run with the per-class numbers. Time is counted in sim ticks only.
    /// </summary>
    [TestFixture]
    public sealed class BuilderHealerSoakTests
    {
        private const int TeamSize = 16;
        private const int StuckTicks = 90;
        private const double StuckRadius = 0.5;
        private const int SamplePeriod = 300;

        /// <summary>16 per team: 6 Warriors, 4 Farmers, 3 Miners, 3 Builders.</summary>
        internal static double[] Mix() => new[] { 0.35, 0.25, 0.2, 0, 0.2 };

        internal static GameConfig MixConfig()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            cfg.Bots.ClassMix = Mix();
            return cfg;
        }

        private static bool Travelling(AiState s) => s == AiState.ToHill || s == AiState.Deliver || s == AiState.Retreat || s == AiState.Unstick;

        internal static string Soak(int seed, BotDifficulty d, List<string> failures, out double ownedShare, out World world)
        {
            GameConfig cfg = MixConfig();
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = seed, TeamSize = TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
                BotDifficulty = d, AllyBotDifficulty = d,
            });
            InputFrame scratch = SimInput.EmptyInput();
            int n = w.ActorCount;
            var anchorX = new double[n];
            var anchorZ = new double[n];
            var anchorTick = new int[n];
            var anchorState = new AiState[n];
            int ownSamples = 0, owned = 0, worstStill = 0;
            int hz = (int)cfg.SimHz;
            int max = (int)(cfg.MatchSeconds * cfg.SimHz) + hz;
            for (int s = 0; s < max && w.Match.Phase != MatchPhase.Ended; s++)
            {
                MatchTick.Advance(w, null, scratch);
                int tick = w.Tick;
                for (int id = 0; id < n; id++)
                {
                    Actor a = w.Actors[id];
                    double x = w.X[id];
                    double z = w.Z[id];
                    if (!double.IsFinite(x) || !double.IsFinite(z)) failures.Add($"{d} tick {tick}: actor {id} NaN position");
                    if (!a.Alive)
                    {
                        anchorState[id] = AiState.None;
                        continue;
                    }
                    if (!Terraform.WalkableAt(w.Map, x, z))
                    {
                        int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
                        bool overVoid = MapGrid.InBounds(w.Map, cx, cz) && w.Map.Cells[cz * w.Map.Width + cx] == Cell.Void;
                        bool carried = a.AirLen > 0 || Collide.DeckSpanAt(w, x, z, Collide.ActorRadius(w, id)) != null;
                        if (!(overVoid && carried)) failures.Add($"{d} tick {tick}: actor {id} ({a.Cls}) at ({x:F2}, {z:F2}) inside cell type {(MapGrid.InBounds(w.Map, cx, cz) ? w.Map.Cells[cz * w.Map.Width + cx] : -1)}");
                    }
                    bool windingUp = a.AiState == AiState.Deliver && a.DeliverTicks != 0;
                    bool travelling = a.StandUp <= 0 && Travelling(a.AiState) && !windingUp;
                    double dx = x - anchorX[id];
                    double dz = z - anchorZ[id];
                    if (!travelling || a.AiState != anchorState[id] || dx * dx + dz * dz >= StuckRadius * StuckRadius)
                    {
                        anchorX[id] = x;
                        anchorZ[id] = z;
                        anchorTick[id] = tick;
                        anchorState[id] = travelling ? a.AiState : AiState.None;
                        continue;
                    }
                    int still = tick - anchorTick[id];
                    if (still > worstStill) worstStill = still;
                    if (still == StuckTicks + 1) failures.Add($"{d} tick {tick}: bot {id} ({a.Cls}, {a.AiState}) stationary > {StuckTicks} ticks at ({x:F2}, {z:F2})");
                }
                foreach (BuiltPiece p in w.Builder.Pieces)
                {
                    if (!p.Active || p.Carried || p.Broken) continue;
                    for (int q = 0; q < p.Count; q++)
                    {
                        byte t = w.Map.Cells[p.Cells[q]];
                        if (t != Cell.Bridge && t != Cell.Barricade) failures.Add($"{d} tick {tick}: an unbroken piece has cell type {t}");
                    }
                }
                if (failures.Count > 20) break;
                if (tick % SamplePeriod == 0 && !Terraform.PadsReachHill(w.Map)) failures.Add($"{d} tick {tick}: a pad cannot reach the hill");
                if (tick % hz == 0 && w.Match.Time > OutcomeRunner.OutcomeSampleFrom + 1e-6)
                {
                    ownSamples++;
                    if (w.Match.HillState == HillState.Azure || w.Match.HillState == HillState.Crimson) owned++;
                }
            }
            ownedShare = ownSamples > 0 ? (double)owned / ownSamples : 0;
            world = w;
            BuilderStats b = w.Builder.Stats;
            return $"{{\"item\":\"DG-24/40\",\"difficulty\":\"{d}\",\"seed\":{seed},\"ticks\":{w.Tick},\"winner\":\"{w.Match.Winner}\",\"hillA\":{w.Match.HillSeconds.Azure:F1},\"hillC\":{w.Match.HillSeconds.Crimson:F1},\"ownedShare\":{ownedShare:F3},\"worstStill\":{worstStill},"
                + PerClass(w)
                + $"\"ladders\":{b.Ladders},\"bridges\":{b.Bridges},\"refused\":{b.Refused},\"pickedUp\":{b.PickedUp},\"expired\":{b.Expired},\"destroyed\":{b.Destroyed},\"deferred\":{b.Deferred},\"routeDeferred\":{b.RouteDeferred},\"biomassSpent\":" + b.BiomassSpent.ToString("F0") + "}";
        }

        /// <summary>Knock-outs and deaths per class (both teams pooled).</summary>
        internal static string PerClass(World w)
        {
            string s = "";
            foreach (ClassId c in ClassIds.All)
            {
                int k = 0, dd = 0, cnt = 0;
                for (int i = 0; i < w.ActorCount; i++)
                {
                    if (w.Actors[i].Cls != c) continue;
                    cnt++;
                    k += w.Actors[i].Kills;
                    dd += w.Actors[i].Deaths;
                }
                if (cnt > 0) s += $"\"{c}\":{{\"n\":{cnt},\"ko\":{k},\"deaths\":{dd}}},";
            }
            return s;
        }

        [Test, Category("Slow")]
        public void DG24Soak16v16NewClassesOnBothTeamsNoStuckNobodyInSolidGroundPiecesNeverSealABaseBandsHold()
        {
            var failures = new List<string>();
            foreach (BotDifficulty d in new[] { BotDifficulty.Normal, BotDifficulty.Hard })
            {
                string line = Soak(101, d, failures, out double ownedShare, out World w);
                Console.WriteLine(line);
                Assert.Greater(w.Match.HillSeconds.Azure, 0, d + ": Azure bots reached the hill");
                Assert.Greater(w.Match.HillSeconds.Crimson, 0, d + ": Crimson bots reached the hill");
                Assert.GreaterOrEqual(ownedShare, OutcomeThresholds.OwnedShareMin, d + ": batch B's per-match owned-share band");
                Assert.Greater(w.Builder.Stats.Ladders + w.Builder.Stats.Bridges, 0, d + ": the Builder bots built something");
            }
            CollectionAssert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>Determinism with the new classes on both teams: same seed, same hash; another seed differs.</summary>
        [Test]
        public void DG24SameSeedSameStateHashIn16v16WithTheNewClasses()
        {
            Action<GameConfig> mix = c => c.Bots.ClassMix = Mix();
            ulong a = MatchHash.Run(4242, 16, 2700, ClassId.Builder, null, mix);
            Assert.AreEqual(a, MatchHash.Run(4242, 16, 2700, ClassId.Builder, null, mix));
            Assert.AreNotEqual(a, MatchHash.Run(4243, 16, 2700, ClassId.Builder, null, mix));
        }
    }
}

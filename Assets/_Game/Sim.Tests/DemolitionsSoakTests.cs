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
    /// DG-22 soak (port of src/sim/demolitions.slow.test.ts on 09e0904, widened): whole 16v16 matches with three Demolitions
    /// Experts per team (the oracle's 3 of 16; the default ClassMix keeps 0, U-059), every bot on its brain and actor 0 on
    /// autopilot, run once per difficulty (both teams at it). Every tick: no NaN, no living actor inside solid ground (the
    /// DG-09 rule), no travelling bot still for more than StuckTicks. Every 10 s both pads reach the hill. At the end: both
    /// teams reached the hill, the owned-hill share is inside batch B's band, and the Demolitions bots used both tools.
    /// Also a 16v16 Hard vs Easy band with Demolitions in the mix, and determinism. Time is counted in sim ticks only.
    /// </summary>
    [TestFixture]
    public sealed class DemolitionsSoakTests
    {
        private const int TeamSize = 16;
        private const int StuckTicks = 90;
        private const double StuckRadius = 0.5;
        private const int SamplePeriod = 300;

        /// <summary>16 per team: 6 Warriors, 4 Farmers, 3 Miners, 3 Demolitions (largest remainder of these shares).</summary>
        internal static GameConfig DemoMix()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            cfg.Bots.ClassMix = new[] { 0.35, 0.25, 0.2, 0.2, 0, 0 };
            return cfg;
        }

        private static bool Travelling(AiState s) => s == AiState.ToHill || s == AiState.Deliver || s == AiState.Retreat || s == AiState.Unstick;

        private static string Soak(int seed, BotDifficulty d, List<string> failures, out DemolitionsStats stats, out double ownedShare, out World world)
        {
            GameConfig cfg = DemoMix();
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = seed, TeamSize = TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
                BotDifficulty = d, AllyBotDifficulty = d,
            });
            int demos = 0;
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Cls == ClassId.Demolitions) demos++;
            Assert.AreEqual(6, demos, "three Demolitions Experts per team");
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
                        if (!(overVoid && carried)) failures.Add($"{d} tick {tick}: actor {id} at ({x:F2}, {z:F2}) inside cell type {(MapGrid.InBounds(w.Map, cx, cz) ? w.Map.Cells[cz * w.Map.Width + cx] : -1)}");
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
                if (failures.Count > 20) break;
                if (tick % SamplePeriod == 0 && !Terraform.PadsReachHill(w.Map)) failures.Add($"{d} tick {tick}: a pad cannot reach the hill");
                if (tick % hz == 0 && w.Match.Time > OutcomeRunner.OutcomeSampleFrom + 1e-6)
                {
                    ownSamples++;
                    if (w.Match.HillState == HillState.Azure || w.Match.HillState == HillState.Crimson) owned++;
                }
            }
            ownedShare = ownSamples > 0 ? (double)owned / ownSamples : 0;
            stats = w.Demolitions.Stats;
            world = w;
            int dk = 0, dd = 0;
            for (int i = 0; i < n; i++)
            {
                if (w.Actors[i].Cls != ClassId.Demolitions) continue;
                dk += w.Actors[i].Kills;
                dd += w.Actors[i].Deaths;
            }
            return $"{{\"item\":\"DG-22\",\"difficulty\":\"{d}\",\"seed\":{seed},\"ticks\":{w.Tick},\"winner\":\"{w.Match.Winner}\",\"hillA\":{w.Match.HillSeconds.Azure:F1},\"hillC\":{w.Match.HillSeconds.Crimson:F1},\"ownedShare\":{ownedShare:F3},\"worstStill\":{worstStill},"
                + $"\"demoKills\":{dk},\"demoDeaths\":{dd},\"minesPlaced\":{stats.MinesPlaced},\"minesTriggered\":{stats.MinesTriggered},\"tntThrown\":{stats.TntThrown},\"tntBlasts\":{stats.TntBlasts},\"blastKills\":{stats.Kills},\"damage\":{stats.Damage:F0},"
                + $"\"walls\":{stats.WallsBroken},\"carved\":{stats.CellsCarved},\"routeRefusals\":{stats.RouteRefusals},\"rateRefusals\":{stats.RateRefusals}}}";
        }

        [Test, Category("Slow")]
        public void DG22Soak16v16DemolitionsOnBothTeamsAtEveryDifficultyNoStuckNobodyInSolidGroundBandsHoldBothToolsUsed()
        {
            var failures = new List<string>();
            foreach (BotDifficulty d in new[] { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard })
            {
                string line = Soak(101, d, failures, out DemolitionsStats st, out double ownedShare, out World w);
                Console.WriteLine(line);
                Assert.Greater(w.Match.HillSeconds.Azure, 0, d + ": Azure bots reached the hill");
                Assert.Greater(w.Match.HillSeconds.Crimson, 0, d + ": Crimson bots reached the hill");
                Assert.GreaterOrEqual(ownedShare, OutcomeThresholds.OwnedShareMin, d + ": batch B's per-match owned-share band");
                Assert.Greater(st.TntThrown, 0, d + ": TNT thrown");
                Assert.Greater(st.TntBlasts, 0, d + ": TNT blew");
                Assert.Greater(st.MinesPlaced, 0, d + ": mines laid");
                if (d != BotDifficulty.Easy) Assert.Greater(st.MinesTriggered, 0, d + ": a mine caught an enemy");
                if (d != BotDifficulty.Easy) Assert.Greater(st.Kills, 0, d + ": the blasts knocked someone out");
                Assert.AreEqual(0, st.PoolRefusals, d + ": bomb pool never full");
            }
            CollectionAssert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>16v16 with Demolitions in the mix: Hard beats Easy in a clear majority, both sides of the map.</summary>
        [Test, Category("Slow")]
        public void DG22HardBeatsEasyAt16v16WithDemolitionsInTheMix()
        {
            const int seeds = 6;
            int hardA = 0, hardC = 0;
            for (int s = 1; s <= seeds; s++)
            {
                if (Play(s, BotDifficulty.Hard, BotDifficulty.Easy) == MatchResult.Azure) hardA++;
                if (Play(s, BotDifficulty.Easy, BotDifficulty.Hard) == MatchResult.Crimson) hardC++;
            }
            Console.WriteLine("{\"item\":\"DG-22\",\"16v16HardWinsAsAzure\":" + hardA + ",\"asCrimson\":" + hardC + ",\"of\":" + seeds + "}");
            // Pooled clear majority (at least 9 of 12, 75%) and a majority on each side (4 of 6). Measured at hand-in: 6 + 4 = 10.
            Assert.GreaterOrEqual(hardA + hardC, 9, "Hard wins pooled over both sides");
            Assert.GreaterOrEqual(hardA, 4, "Hard as Azure");
            Assert.GreaterOrEqual(hardC, 4, "Hard as Crimson");
        }

        private static MatchResult Play(int seed, BotDifficulty azure, BotDifficulty crimson)
        {
            GameConfig cfg = DemoMix();
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = seed, TeamSize = TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Demolitions, Config = cfg,
                AllyBotDifficulty = azure, BotDifficulty = crimson,
            });
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++) MatchTick.Advance(w, null, scratch);
            return w.Match.Winner;
        }

        /// <summary>Determinism: the same seed with Demolitions on both teams (mines, TNT and craters live) gives the same hash; another seed does not.</summary>
        [Test]
        public void DG22SameSeedSameStateHashIn16v16WithMinesTntAndCraters()
        {
            Action<GameConfig> mix = c => c.Bots.ClassMix = new[] { 0.35, 0.25, 0.2, 0.2, 0, 0 };
            ulong a = MatchHash.Run(1337, 16, 2700, ClassId.Demolitions, null, mix);
            Assert.AreEqual(a, MatchHash.Run(1337, 16, 2700, ClassId.Demolitions, null, mix));
            Assert.AreNotEqual(a, MatchHash.Run(1338, 16, 2700, ClassId.Demolitions, null, mix));
            Action<WorldOptions> hard = o => { o.BotDifficulty = BotDifficulty.Hard; o.AllyBotDifficulty = BotDifficulty.Hard; };
            ulong h = MatchHash.Run(1337, 16, 2700, ClassId.Demolitions, hard, mix);
            Assert.AreEqual(h, MatchHash.Run(1337, 16, 2700, ClassId.Demolitions, hard, mix));
        }
    }
}

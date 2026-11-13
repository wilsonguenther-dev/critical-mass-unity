using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Sim-physics lane soaks: 16v16 at the shipped pace (GameConfig.CreateDefault, pace 1 since the owner's 2026-11-12 call), all six classes, at Easy, Normal and
    /// Hard: no NaN, nobody inside solid ground or inside the Kaiju, no travelling bot stuck, pads reach the hill, bands hold;
    /// and the before (PhysicsConfig.Classic) / after balance table the PR quotes. Also new golden hashes for the new default.
    /// </summary>
    [TestFixture]
    public sealed class SimPhysicsSoakTests
    {
        private const int TeamSize = 16;
        private const int StuckTicks = 90;
        private const double StuckRadius = 0.5;

        internal static GameConfig SixClassMix(bool classic)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            cfg.Bots.ClassMix = new[] { 0.3, 0.2, 0.15, 0.1, 0.15, 0.1 };
            cfg.Physics.Classic = classic;
            return cfg;
        }

        private static bool Travelling(AiState s) => s == AiState.ToHill || s == AiState.Deliver || s == AiState.Retreat || s == AiState.Unstick;

        internal sealed class Run
        {
            public MatchResult Winner;
            public double HillA, HillC, OwnedShare;
            public int Ticks, WorstStill, Hatched;
            public readonly int[] Ko = new int[6];
            public readonly int[] Deaths = new int[6];
            public readonly int[] Count = new int[6];
        }

        internal static Run Soak(int seed, BotDifficulty d, bool classic, List<string> failures)
        {
            GameConfig cfg = SixClassMix(classic);
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
            var run = new Run();
            int ownSamples = 0, owned = 0;
            int hz = (int)cfg.SimHz;
            int max = (int)(cfg.MatchSeconds * cfg.SimHz) + hz;
            double body = cfg.Physics.KaijuBodyRadius;
            string tag = (classic ? "classic " : "physics ") + d + " seed " + seed;
            for (int s = 0; s < max && w.Match.Phase != MatchPhase.Ended; s++)
            {
                MatchTick.Advance(w, null, scratch);
                int tick = w.Tick;
                if (tick % hz == 0 && w.Match.Time > OutcomeRunner.OutcomeSampleFrom + 1e-6)
                {
                    ownSamples++;
                    if (w.Match.HillState == HillState.Azure || w.Match.HillState == HillState.Crimson) owned++;
                }
                if (failures == null) continue;
                Kaiju k = w.Kaiju;
                for (int id = 0; id < n; id++)
                {
                    Actor a = w.Actors[id];
                    double x = w.X[id];
                    double z = w.Z[id];
                    if (!double.IsFinite(x) || !double.IsFinite(z)) failures.Add($"{tag} tick {tick}: actor {id} NaN position");
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
                        if (!(overVoid && carried)) failures.Add($"{tag} tick {tick}: actor {id} at ({x:F2}, {z:F2}) inside solid ground");
                    }
                    if (!classic && k.State != KaijuState.Egg && a.AirLen == 0)
                    {
                        double kx = x - k.X, kz = z - k.Z;
                        if (kx * kx + kz * kz < body * body) failures.Add($"{tag} tick {tick}: actor {id} inside the Kaiju");
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
                    if (still > run.WorstStill) run.WorstStill = still;
                    if (still == StuckTicks + 1) failures.Add($"{tag} tick {tick}: bot {id} ({a.Cls}, {a.AiState}) stationary > {StuckTicks} ticks at ({x:F2}, {z:F2})");
                }
                if (failures.Count > 30) break;
                if (tick % 300 == 0 && !Terraform.PadsReachHill(w.Map)) failures.Add($"{tag} tick {tick}: a pad cannot reach the hill");
            }
            run.Winner = w.Match.Winner;
            run.HillA = w.Match.HillSeconds.Azure;
            run.HillC = w.Match.HillSeconds.Crimson;
            run.OwnedShare = ownSamples > 0 ? (double)owned / ownSamples : 0;
            run.Ticks = w.Tick;
            run.Hatched = w.Match.Hatched ? 1 : 0;
            for (int i = 0; i < n; i++)
            {
                int c = (int)w.Actors[i].Cls;
                run.Count[c]++;
                run.Ko[c] += w.Actors[i].Kills;
                run.Deaths[c] += w.Actors[i].Deaths;
            }
            return run;
        }

        [Test, Category("Slow")]
        public void PhysicsSoak16v16AtEveryDifficultyNoStuckNobodyInSolidGroundOrInTheKaijuBandsHold()
        {
            var failures = new List<string>();
            foreach (BotDifficulty d in new[] { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard })
            {
                Run r = Soak(101, d, false, failures);
                Console.WriteLine($"{{\"item\":\"sim-physics soak\",\"difficulty\":\"{d}\",\"winner\":\"{r.Winner}\",\"hillA\":{r.HillA.ToString("F1", CultureInfo.InvariantCulture)},\"hillC\":{r.HillC.ToString("F1", CultureInfo.InvariantCulture)},\"ownedShare\":{r.OwnedShare.ToString("F3", CultureInfo.InvariantCulture)},\"worstStill\":{r.WorstStill},\"ticks\":{r.Ticks}}}");
                if (r.OwnedShare < 0.25) failures.Add($"{d}: owned share {r.OwnedShare:F3} under the 0.25 band");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>The before / after table: 6 seeds per difficulty per arm, both sides at the same difficulty.</summary>
        [Test, Category("Slow")]
        public void BalanceTableBeforeClassicAndAfterPhysics()
        {
            var sb = new StringBuilder();
            int[] seeds = { 11, 12, 13, 14, 15, 16 };
            foreach (BotDifficulty d in new[] { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard })
            {
                foreach (bool classic in new[] { true, false })
                {
                    int az = 0, cr = 0, draw = 0;
                    double owned = 0, hill = 0, ticks = 0;
                    var ko = new double[6];
                    var deaths = new double[6];
                    var count = new double[6];
                    foreach (int s in seeds)
                    {
                        Run r = Soak(s, d, classic, null);
                        if (r.Winner == MatchResult.Azure) az++;
                        else if (r.Winner == MatchResult.Crimson) cr++;
                        else draw++;
                        owned += r.OwnedShare;
                        hill += r.HillA + r.HillC;
                        ticks += r.Ticks;
                        for (int c = 0; c < 6; c++)
                        {
                            ko[c] += r.Ko[c];
                            deaths[c] += r.Deaths[c];
                            count[c] += r.Count[c];
                        }
                    }
                    int m = seeds.Length;
                    var per = new StringBuilder();
                    for (int c = 0; c < 6; c++)
                    {
                        double kpa = count[c] > 0 ? ko[c] / count[c] : 0;
                        double dpa = count[c] > 0 ? deaths[c] / count[c] : 0;
                        per.Append($",\"{(ClassId)c}\":{{\"koPerFighter\":{kpa.ToString("F2", CultureInfo.InvariantCulture)},\"deathsPerFighter\":{dpa.ToString("F2", CultureInfo.InvariantCulture)}}}");
                    }
                    string line = $"{{\"item\":\"sim-physics balance\",\"arm\":\"{(classic ? "before (Classic)" : "after (physics)")}\",\"difficulty\":\"{d}\",\"matches\":{m},\"azureWins\":{az},\"crimsonWins\":{cr},\"draws\":{draw},"
                        + $"\"ownedShare\":{(owned / m).ToString("F3", CultureInfo.InvariantCulture)},\"hillSecondsHeld\":{(hill / m).ToString("F1", CultureInfo.InvariantCulture)},\"matchTicks\":{(ticks / m).ToString("F0", CultureInfo.InvariantCulture)}{per}}}";
                    Console.WriteLine(line);
                    sb.AppendLine(line);
                }
            }
            Assert.IsNotEmpty(sb.ToString());
        }

        [Test]
        public void GoldenHashesOfTheNewDefaultAreStable()
        {
            // Rules-combined (U-069): one pair of goldens per rules profile, all from CreateBrowser5v5 at pace 1 unless named.
            // Classic: browser parity (no physics, no depth, pace 2x): must equal the DG-21 golden bit for bit.
            // PhysicsOnly: the physics lane on, depth off (the test config). Re-pinned 2026-11-12 for U-071 (allies pass head-on).
            // Full: everything on (the game's default rules). Re-pinned 2026-11-13 for U-074..U-078, then for bridge collapse off by
            // default (Classic and PhysicsOnly unchanged both times: depth rules are off in them).
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly, RulesProfile.Full })
            {
                Action<GameConfig> t = c => c.ApplyRules(p);
                ulong a = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, t);
                ulong b = MatchHash.Run(42, 5, 14500, ClassId.Warrior, null, t);
                Console.WriteLine("{\"item\":\"rules-combined golden\",\"profile\":\"" + p + "\",\"16v16seed1\":\"0x" + a.ToString("X16") + "\",\"5v5seed42\":\"0x" + b.ToString("X16") + "\"}");
                Assert.AreEqual(a, MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, t), p + ": same seed, same hash");
                Assert.AreEqual(Golden[(int)p, 0], a, p + ": 16v16 seed 1, 2:00");
                Assert.AreEqual(Golden[(int)p, 1], b, p + ": 5v5 seed 42, whole match");
            }
            Assert.AreEqual(MatchHash.Run(1, 16, 3600, ClassId.Warrior), Golden[(int)RulesProfile.PhysicsOnly, 0], "CreateBrowser5v5 is the PhysicsOnly profile");
        }

        /// <summary>[profile, 0] = 16v16 seed 1 at 2:00, [profile, 1] = 5v5 seed 42 whole match. Classic = the DG-21 goldens.</summary>
        private static readonly ulong[,] Golden =
        {
            { 0x5C912AEED16BC302UL, 0x2980068FA0457CC2UL }, // Classic (DG-21, unchanged since the browser port)
            { 0x2BE070768E23B442UL, 0x8BCA67D3B8845532UL }, // PhysicsOnly (re-pinned 2026-11-12: U-071; 5v5 re-pinned 2026-11-13: fix/kaiju-alive-1 Kaiju hits stagger, throw at KaijuKnockScale 0.5, topple)
            { 0xAA728D5DAF5451F4UL, 0xBF82793EEE8C78C8UL }, // Full (re-pinned 2026-11-13 integration batch 7: fix/bridge-holes MapEventsConfig.BridgeCollapse default false (16v16 and 5v5) plus fix/kaiju-alive-1 Kaiju hits stagger, throw at KaijuKnockScale 0.5, topple (5v5); before: 0x70C7D9F992CAC3CC / 0x8F3F947AE72E7815, U-074..U-078)
        };
    }
}

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

        /// <summary>
        /// b12-c: what holds a stationary bot (printed with its failure line): its own swing, stagger, health, velocity and
        /// the walkable cells around it, and every actor within 2.5 u (id, class, team, state, distance, swing pending).
        /// </summary>
        internal static string StuckContext(World w, int id)
        {
            Actor a = w.Actors[id];
            var sb = new System.Text.StringBuilder();
            sb.Append(" [hp ").Append(a.Hp.ToString("F0")).Append('/').Append(a.MaxHp.ToString("F0")).Append(", swingPending ").Append(a.SwingPending)
              .Append(", attackCd ").Append(a.AttackCd.ToString("F2")).Append(", hitStun ").Append(a.HitStunTicks).Append(", v (")
              .Append(w.Vx[id].ToString("F2")).Append(", ").Append(w.Vz[id].ToString("F2")).Append("), knock (").Append(a.KVx.ToString("F2")).Append(", ")
              .Append(a.KVz.ToString("F2")).Append("), walkable N/E/S/W ");
            int cx = JsMath.FloorToInt(w.X[id]), cz = JsMath.FloorToInt(w.Z[id]);
            sb.Append(MapGrid.IsWalkable(w.Map, cx, cz + 1) ? 1 : 0).Append(MapGrid.IsWalkable(w.Map, cx + 1, cz) ? 1 : 0)
              .Append(MapGrid.IsWalkable(w.Map, cx, cz - 1) ? 1 : 0).Append(MapGrid.IsWalkable(w.Map, cx - 1, cz) ? 1 : 0).Append("; near:");
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == id || !w.Actors[j].Alive) continue;
                double dx = w.X[j] - w.X[id], dz = w.Z[j] - w.Z[id], d = Math.Sqrt(dx * dx + dz * dz);
                if (d > 2.5) continue;
                Actor o = w.Actors[j];
                sb.Append(' ').Append(j).Append(' ').Append(o.Cls).Append(' ').Append(o.Team).Append(' ').Append(o.AiState).Append(" d=").Append(d.ToString("F2"))
                  .Append(o.SwingPending ? " swinging" : "");
            }
            return sb.Append(']').ToString();
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
                    if (still == StuckTicks + 1) failures.Add($"{tag} tick {tick}: bot {id} ({a.Cls}, {a.AiState}) stationary > {StuckTicks} ticks at ({x:F2}, {z:F2})" + StuckContext(w, id));
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
            var wrong = new System.Collections.Generic.List<string>();
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly, RulesProfile.Full })
            {
                Action<GameConfig> t = c => c.ApplyRules(p);
                ulong a = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, t);
                ulong b = MatchHash.Run(42, 5, 14500, ClassId.Warrior, null, t);
                Console.WriteLine("{\"item\":\"rules-combined golden\",\"profile\":\"" + p + "\",\"16v16seed1\":\"0x" + a.ToString("X16") + "\",\"5v5seed42\":\"0x" + b.ToString("X16") + "\"}");
                Assert.AreEqual(a, MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, t), p + ": same seed, same hash");
                // integration batch 10: every profile prints its line before any golden fails, so one run re-pins every row
                if (Golden[(int)p, 0] != a) wrong.Add(p + ": 16v16 seed 1, 2:00: expected 0x" + Golden[(int)p, 0].ToString("X16") + " but was 0x" + a.ToString("X16"));
                if (Golden[(int)p, 1] != b) wrong.Add(p + ": 5v5 seed 42, whole match: expected 0x" + Golden[(int)p, 1].ToString("X16") + " but was 0x" + b.ToString("X16"));
            }
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
            Assert.AreEqual(MatchHash.Run(1, 16, 3600, ClassId.Warrior), Golden[(int)RulesProfile.PhysicsOnly, 0], "CreateBrowser5v5 is the PhysicsOnly profile");
        }

        /// <summary>[profile, 0] = 16v16 seed 1 at 2:00, [profile, 1] = 5v5 seed 42 whole match. Classic = the DG-21 goldens.</summary>
        private static readonly ulong[,] Golden =
        {
            { 0x5C912AEED16BC302UL, 0x2980068FA0457CC2UL }, // Classic (DG-21, unchanged since the browser port)
            { 0x0836DA5FD21C4854UL, 0x780B92B03E6FFEAEUL }, // PhysicsOnly (5v5 re-pinned 2026-11-15 candidate 9 for fix/kaiju-hill-1 rounds 3+4 (run 43713330248; Classic, Full and every 16v16 row unchanged); earlier: (re-pinned 2026-11-14 integration batch 10 for fix/movement-shiver-1..4 + feat/kaiju-bridges-1+2 (5v5 only; run 43639049337), shiver-4 from run 43633158753 (after -3: run 43630865907); after -2: 0x233E437F9CFAECD7 / 0x490A1BF7AA50C0A2; shiver-1 alone: 0xBFA1133FB889F15F / 0x8809B67103F4E49C; before: 0x2BE070768E23B442 / 0x8BCA67D3B8845532; earlier: (re-pinned 2026-11-12: U-071; 5v5 re-pinned 2026-11-13: fix/kaiju-alive-1 Kaiju hits stagger, throw at KaijuKnockScale 0.5, topple)
            // feat/light-weapons-1 (2026-11-14): this Full row MUST be re-pinned ON PURPOSE in the one combined compile. The light
            // weapons (DepthConfig.LightWeapons) change every Full match: Demolitions and Healer bots now swing at close range and
            // every Builder / Demolitions / Healer hit shoves. Classic and PhysicsOnly do not move (depth rules are off there).
            // Re-pin: copy the "rules-combined golden" Full line this test prints into the two values below.
            // fix/movement-shiver-1 (2026-11-14): the PhysicsOnly AND Full rows MUST be re-pinned ON PURPOSE again in the combined
            // compile. PhysicsConfig.KaijuNavGuard (the Kaiju holds at the last cell its body fits in instead of shaking at a
            // bridge mouth or wall end, gives up an unreachable target, claws and slams only what it can see, keeps its target
            // with a margin) and PhysicsConfig.BotWideLine (bots walk straight at a fight target only when the body fits along
            // the line; no swinging at a target behind a wall) change Kaiju and bot decisions. Classic does not move (Phys.On is
            // off there). Copy the PhysicsOnly and Full "rules-combined golden" lines this test prints into the rows.
            // b12-c2 (RULE CHANGE, merged last): this Full row is EXPECTED to move (both values) with DepthConfig.WeaponReach on; the
            // integrator pastes the final printed Full "rules-combined golden" line once. Classic and PhysicsOnly must not move.
            { 0xC8739A69D4C704AAUL, 0x4CF5DD7DE4AB3BA1UL }, // Full (re-pinned 2026-11-14 integration batch 10 for fix/movement-shiver-1..4 + feat/kaiju-bridges-1+2 (5v5 only; run 43639049337), shiver-4 from run 43633158753 (after -3: run 43630865907); after -2: 0x214EE5D55012D0DB / 0x5D9F4BDCB5B33D08; before: 0xF20646D083F6DF6D / 0x12D502EBB7470305; earlier re-pinned 2026-11-14 integration batch 9 for feat/light-weapons-1, from run 43611800851; PRE-light-weapons: 0xAA728D5DAF5451F4 / 0xBF82793EEE8C78C8; re-pinned 2026-11-13 integration batch 7: fix/bridge-holes MapEventsConfig.BridgeCollapse default false (16v16 and 5v5) plus fix/kaiju-alive-1 Kaiju hits stagger, throw at KaijuKnockScale 0.5, topple (5v5); before: 0x70C7D9F992CAC3CC / 0x8F3F947AE72E7815, U-074..U-078)
        };
    }
}

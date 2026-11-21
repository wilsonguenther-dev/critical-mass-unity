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
    /// <summary>b15 fix round: diagnosis probes (probe branch only; category Probe never runs in the gates).</summary>
    [TestFixture, Category("Probe")]
    public sealed class B15FixProbeTests
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.00", Inv);

        static char CellChar(byte t)
        {
            switch (t)
            {
                case Cell.Void: return ' ';
                case Cell.Floor: return '.';
                case Cell.Wall: return '#';
                case Cell.BreakableIntact: return 'b';
                case Cell.BreakableCracked: return 'c';
                case Cell.BreakableDestroyed: return ',';
                case Cell.Water: return '~';
                case Cell.Bridge: return '=';
                case Cell.Barricade: return 'B';
                case Cell.LowWall: return 'w';
                default: return '?';
            }
        }

        static void DumpArea(World w, string label, int x0, int x1, int z0, int z1)
        {
            MapDef map = w.Map;
            var sb = new StringBuilder();
            sb.Append("[area] " + label + " x " + x0 + ".." + x1 + " z " + z0 + ".." + z1 + " (cell | lift)\n");
            for (int z = z0; z <= z1; z++)
            {
                sb.Append(z.ToString("00", Inv)).Append(' ');
                for (int x = x0; x <= x1; x++) sb.Append(MapGrid.InBounds(map, x, z) ? CellChar(map.Cells[z * map.Width + x]) : '!');
                sb.Append("  |  ");
                for (int x = x0; x <= x1; x++)
                {
                    if (!MapGrid.InBounds(map, x, z) || map.Lift == null) { sb.Append('-'); continue; }
                    int l = map.Lift[z * map.Width + x];
                    sb.Append(l == 0 ? '.' : l < 0 ? (char)('a' - 1 - l) : (char)('0' + Math.Min(9, l)));
                }
                sb.Append('\n');
            }
            Console.Write(sb.ToString());
        }

        sealed class Ep
        {
            public int Seed, Tick, Id, Cell;
            public string Text;
            public string Key;
        }

        /// <summary>Item 1: where the stuck episodes of the balance metric happen and what the bots there are doing.</summary>
        [Test]
        public void StuckEpisodesWhereAndWhat()
        {
            GameConfig cfg = BalanceRunner.Config(RulesProfile.Full, new string[0]);
            var eps = new List<Ep>();
            int seeds = 0;
            World last = null;
            foreach (int seed in WinShare.Seeds16)
            {
                seeds++;
                World w = World.CreateWorld(BalanceRunner.Options(cfg, seed, 16));
                last = w;
                BotsState st = BotBrain.GetBotsState(w);
                int n = w.ActorCount;
                var held = new int[n];
                var ax = new double[n];
                var az = new double[n];
                InputFrame scratch = SimInput.EmptyInput();
                int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz;
                for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
                {
                    MatchTick.Advance(w, null, scratch);
                    for (int i = 0; i < n; i++)
                    {
                        Actor a = w.Actors[i];
                        InputFrame inp = w.Inputs[i];
                        bool h = a.Alive && a.StandUp <= 0 && inp.MoveX * inp.MoveX + inp.MoveZ * inp.MoveZ > 0.25 && !EnemyTouching(w, i);
                        if (!h) { held[i] = 0; continue; }
                        if (held[i] == 0) { ax[i] = w.X[i]; az[i] = w.Z[i]; }
                        if (++held[i] < BalanceProbe.StuckWindow) continue;
                        held[i] = 0;
                        double ddx = w.X[i] - ax[i], ddz = w.Z[i] - az[i];
                        if (ddx * ddx + ddz * ddz >= BalanceProbe.StuckDistance * BalanceProbe.StuckDistance) continue;
                        eps.Add(Describe(w, st, seed, i, inp));
                    }
                }
            }
            Console.WriteLine("[stuck] seeds " + seeds + " episodes " + eps.Count + " median-ish per match " + F(eps.Count / (double)seeds));
            var byCell = new Dictionary<int, List<Ep>>();
            foreach (Ep e in eps)
            {
                if (!byCell.TryGetValue(e.Cell, out var l)) byCell[e.Cell] = l = new List<Ep>();
                l.Add(e);
            }
            var cells = new List<KeyValuePair<int, List<Ep>>>(byCell);
            cells.Sort((p, q) => q.Value.Count != p.Value.Count ? q.Value.Count.CompareTo(p.Value.Count) : p.Key.CompareTo(q.Key));
            int W = last.Map.Width;
            var byKey = new Dictionary<string, int>();
            foreach (Ep e in eps) { byKey.TryGetValue(e.Key, out int c); byKey[e.Key] = c + 1; }
            var keys = new List<KeyValuePair<string, int>>(byKey);
            keys.Sort((p, q) => q.Value.CompareTo(p.Value));
            for (int k = 0; k < Math.Min(25, keys.Count); k++) Console.WriteLine("[stuck kind] " + keys[k].Value + " x " + keys[k].Key);
            for (int k = 0; k < Math.Min(10, cells.Count); k++)
            {
                int cell = cells[k].Key;
                List<Ep> l = cells[k].Value;
                var kinds = new Dictionary<string, int>();
                var bodies = new Dictionary<string, int>();
                foreach (Ep e in l)
                {
                    kinds.TryGetValue(e.Key, out int c); kinds[e.Key] = c + 1;
                    string b = e.Seed + "/" + e.Id;
                    bodies.TryGetValue(b, out int d); bodies[b] = d + 1;
                }
                var ks = new StringBuilder();
                foreach (var kv in kinds) ks.Append(kv.Value + "x{" + kv.Key + "} ");
                var bs = new StringBuilder();
                foreach (var kv in bodies) bs.Append(kv.Key + ":" + kv.Value + " ");
                Console.WriteLine("[stuck cell] (" + (cell % W) + ", " + (cell / W) + ") episodes " + l.Count + " kinds " + ks + " bodies(seed/id:n) " + bs);
                for (int s = 0; s < Math.Min(8, l.Count); s++) Console.WriteLine("    " + l[s].Text);
            }
            for (int k = 0; k < Math.Min(4, cells.Count); k++)
            {
                int cx = cells[k].Key % W, cz = cells[k].Key / W;
                DumpArea(last, "around hot cell " + (k + 1) + " (" + cx + ", " + cz + ") at the end of the last match", cx - 7, cx + 7, cz - 6, cz + 6);
            }
        }

        static Ep Describe(World w, BotsState st, int seed, int i, InputFrame inp)
        {
            Actor a = w.Actors[i];
            MapDef map = w.Map;
            int cx = (int)Math.Floor(w.X[i]), cz = (int)Math.Floor(w.Z[i]);
            int cell = cz * map.Width + cx;
            double ml = Math.Sqrt(inp.MoveX * inp.MoveX + inp.MoveZ * inp.MoveZ);
            double mx = inp.MoveX / ml, mz = inp.MoveZ / ml;
            // what is ahead along the move: the next two cells' type and lift, and allies in the way
            var ahead = new StringBuilder();
            for (int q = 1; q <= 2; q++)
            {
                int nx = (int)Math.Floor(w.X[i] + mx * 0.6 * q), nz = (int)Math.Floor(w.Z[i] + mz * 0.6 * q);
                if (!MapGrid.InBounds(map, nx, nz)) { ahead.Append("oob "); continue; }
                int ni = nz * map.Width + nx;
                ahead.Append(CellChar(map.Cells[ni])).Append(map.Lift != null ? map.Lift[ni].ToString(Inv) : "-").Append(' ');
            }
            int allyAhead = 0, allyNear = 0, allyAheadStill = 0;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == i || !w.Actors[j].Alive || w.Actors[j].Team != a.Team) continue;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i], d2 = dx * dx + dz * dz;
                if (d2 < 1.2 * 1.2)
                {
                    allyNear++;
                    if (dx * mx + dz * mz > 0.2) { allyAhead++; if (w.Vx[j] * w.Vx[j] + w.Vz[j] * w.Vz[j] < 0.05) allyAheadStill++; }
                }
            }
            byte job = st.Job != null ? st.Job[i] : (byte)255;
            byte rally = st.Rally != null ? st.Rally[i] : (byte)255;
            HillDef hill = map.Hill;
            double hd = Math.Sqrt((w.X[i] - hill.X) * (w.X[i] - hill.X) + (w.Z[i] - hill.Z) * (w.Z[i] - hill.Z));
            string key = a.Cls + " " + a.AiState + " job" + job + " rally" + rally + " carry" + (a.CarryAmount > 0 ? 1 : 0) + " allyAhead" + Math.Min(allyAhead, 2);
            string text = "seed " + seed + " t " + F(w.Match.Time) + " id " + i + " " + a.Team + " " + a.Cls + " ai " + a.AiState + " role " + st.Role[i] + " job " + job + " rally " + rally +
                          " pos (" + F(w.X[i]) + ", " + F(w.Z[i]) + ") hillDist " + F(hd) + " move (" + F(mx) + ", " + F(mz) + ") v (" + F(w.Vx[i]) + ", " + F(w.Vz[i]) + ") air " + a.AirLen +
                          " carry " + a.CarryAmount + " ahead [" + ahead + "] allyNear " + allyNear + " allyAhead " + allyAhead + " (still " + allyAheadStill + ")" +
                          " target " + st.Target[i] + " task " + st.TaskKind[i] + " unstickUntil " + F(st.UnstickUntil[i]) + " want " + st.WantMove[i];
            return new Ep { Seed = seed, Tick = w.Tick, Id = i, Cell = cell, Text = text, Key = key };
        }

        static bool EnemyTouching(World w, int i)
        {
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == i || !w.Actors[j].Alive || w.Actors[j].Team == w.Actors[i].Team) continue;
                double rr = Collide.ActorRadius(w, i) + Collide.ActorRadius(w, j) + 0.2;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i];
                if (dx * dx + dz * dz < rr * rr) return true;
            }
            return false;
        }

        /// <summary>Item 2: 16v16 seed 38 (the route-guard scan): what the Kaiju stands in front of after the hatch.</summary>
        [Test]
        public void KaijuSeed38()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions { Seed = 38, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            DumpArea(w, "seed 38 tick 0", 44, 68, 2, 30);
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + 30;
            bool dumped = false;
            int head = w.EventHead;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                for (; head < w.EventHead; head++)
                {
                    SimEvent e = w.Events[head % World.EventCapacity];
                    if (e.X >= 44 && e.X <= 68 && e.Z >= 2 && e.Z <= 30 && e.Type != SimEventType.Hit && e.Type != SimEventType.AttackStart && e.Type != SimEventType.Knock &&
                        e.Type != SimEventType.HitStun && e.Type != SimEventType.Death && e.Type != SimEventType.AttackCancel && w.Match.Time < 270)
                        if (e.Type.ToString().IndexOf("Gather", StringComparison.Ordinal) < 0 && e.Type.ToString().IndexOf("Pickup", StringComparison.Ordinal) < 0 &&
                            e.Type.ToString().IndexOf("Respawn", StringComparison.Ordinal) < 0 && e.Type.ToString().IndexOf("Deliver", StringComparison.Ordinal) < 0)
                            Console.WriteLine("[k38 event] t " + F(w.Match.Time) + " " + e.Type + " a " + e.A + " b " + e.B + " at (" + F(e.X) + ", " + F(e.Z) + ") v " + F(e.Value));
                }
                Kaiju k = w.Kaiju;
                if (!w.Match.Hatched || w.Match.Time > 275) continue;
                if (w.Tick % 30 == 0)
                {
                    int foes = 0, near = 0;
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        Actor a = w.Actors[i];
                        if (!a.Alive) continue;
                        bool foe = k.State == KaijuState.Berserk || (k.State == KaijuState.Azure) == (a.Team == TeamId.Crimson);
                        if (foe) foes++;
                        double dx = w.X[i] - k.X, dz = w.Z[i] - k.Z;
                        if (dx * dx + dz * dz < 100) near++;
                    }
                    Console.WriteLine("[k38] t " + F(w.Match.Time) + " kaiju (" + F(k.X) + ", " + F(k.Z) + ") state " + k.State + " target " + k.TargetId + " ko " + F(k.KoIn) +
                                      " reachesHill " + Kaiju.BodyReachesHillFrom(w, k.X, k.Z) + " foes " + foes + " bodiesWithin10 " + near);
                }
                if (!dumped && w.Match.Time > 250)
                {
                    dumped = true;
                    DumpArea(w, "seed 38 at " + F(w.Match.Time) + " s", 44, 68, 2, 30);
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        Actor a = w.Actors[i];
                        double dx = w.X[i] - k.X, dz = w.Z[i] - k.Z;
                        if (a.Alive && dx * dx + dz * dz < 400) Console.WriteLine("[k38 body] id " + i + " " + a.Team + " " + a.Cls + " (" + F(w.X[i]) + ", " + F(w.Z[i]) + ") ai " + a.AiState);
                    }
                }
            }
        }

        /// <summary>Item 3: the 5v5 idle-player sweep (D-030 seeds 201..400) per comeback lever, plus the all-bot reference.</summary>
        [Test]
        public void IdlePlayerSweepByLever()
        {
            var arms = new[]
            {
                "", "Depth.Comeback.Enabled=false", "Depth.Comeback.RespawnCurveOn=false", "Depth.Comeback.ForwardReturnOn=false",
                "Depth.Comeback.RallyOn=false", "Depth.Comeback.HoldFatigueOn=false", "Depth.BotBrain.Enabled=false", "Depth.HutChoices.Enabled=false",
            };
            foreach (string arm in arms)
            {
                string[] sets = arm.Length == 0 ? new string[0] : arm.Split(';');
                Action<GameConfig> tweak = c => { c.ApplyRules(RulesProfile.Full); ConfigOverrides.Apply(c, sets); };
                int wins = 0, losses = 0, draws = 0;
                double hillShare = 0;
                for (int s = 201; s < 401; s++)
                {
                    MatchOutcome o = OutcomeRunner.RunOutcomeMatch(s, 5, HumanMode.Passive, tweak);
                    if (o.Winner == MatchResult.Azure) wins++; else if (o.Winner == MatchResult.Crimson) losses++; else draws++;
                    double tot = o.HillSeconds.Azure + o.HillSeconds.Crimson;
                    hillShare += tot > 0 ? o.HillSeconds.Azure / tot : 0.5;
                }
                Console.WriteLine("[idle 5v5] arm '" + (arm.Length == 0 ? "game" : arm) + "': idle team wins " + wins + " / losses " + losses + " / draws " + draws + " of 200; idle team hill share " + F(hillShare / 200));
            }
            int botWins = 0;
            Action<GameConfig> full = c => c.ApplyRules(RulesProfile.Full);
            for (int s = 201; s < 401; s++) if (OutcomeRunner.RunOutcomeMatch(s, 5, HumanMode.Bot, full).Winner == MatchResult.Azure) botWins++;
            Console.WriteLine("[idle 5v5] all-bot reference (actor 0 on autopilot): Azure wins " + botWins + " of 200");
        }

        static string Matrix(DuelRunner.Cell[,] d)
        {
            var sb = new StringBuilder();
            string[] names = { "War", "Far", "Min", "Dem", "Bui", "Hea" };
            int unbeaten = 0;
            for (int a = 0; a < 6; a++)
            {
                sb.Append(names[a]).Append(':');
                bool all = true;
                for (int b = 0; b < 6; b++)
                {
                    DuelRunner.Cell c = d[a, b];
                    sb.Append(' ').Append(c.Wins).Append('/').Append(c.Losses).Append('/').Append(c.Draws);
                    if (a != b && !(c.Wins * 2 > c.Reps)) all = false;
                }
                if (all) { unbeaten++; sb.Append("  UNBEATEN"); }
                sb.Append(" | ");
            }
            return "unbeaten " + unbeaten + " :: " + sb;
        }

        /// <summary>Item 4: the duel table (8 reps, the balance report's) per lever: the shipped rows and the three levers.</summary>
        [Test]
        public void MinerDuelLevers()
        {
            var arms = new[]
            {
                "", "Depth.CombatFeel.CancelRefundsCooldown=true", "Depth.CombatFeel.LightHitsKeepWindup=true", "Depth.CombatFeel.Classes[2].AttackRange=1.3",
                "Depth.CombatFeel.CancelRefundsCooldown=true;Depth.CombatFeel.LightHitsKeepWindup=true",
            };
            foreach (string arm in arms)
            {
                string[] sets = arm.Length == 0 ? new string[0] : arm.Split(';');
                Console.WriteLine("[duel] arm '" + (arm.Length == 0 ? "game" : arm) + "' (row W/L/D vs W F M D B H): " + Matrix(DuelRunner.Duels(RulesProfile.Full, sets, 8)));
            }
        }
    }
}

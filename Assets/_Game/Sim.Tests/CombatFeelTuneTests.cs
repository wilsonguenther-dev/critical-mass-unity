using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 combat feel, second pass: the measuring tools behind the tuning numbers in CombatFeelConfig.
    /// (1) A small deterministic coordinate-descent search over the combat numbers that scores a candidate on the 36-duel
    ///     matrix, the 15 fight-backs, a 16v16 damage-spread probe, the Demolitions knock-out-share gate and the two-minute
    ///     shiver gate, and prints every step and the best set (the numbers in CombatFeelConfig come from its output).
    /// (2) Stuck episodes with their cause (state, wall or crowd, target) at the hot cells.
    /// (3) Per-system tick cost on the same seeds, rule off against rule on, through a step replica proven equal to SimStep.
    /// (4) The whole-match shiver soak with the switch off against on (is the soak red outside the rule?).
    /// </summary>
    [TestFixture]
    public sealed class CombatFeelTuneTests
    {
        private static readonly ClassId[] Six = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        private sealed class Knob
        {
            public string Name;
            public Action<CombatFeelConfig, double> Set;
            public double[] Values;   // [0] = the shipped value
        }

        private static ClassConfig Row(CombatFeelConfig f, ClassId c) => f.Classes[(int)c];

        // [0] of every list is the shipped value in CombatFeelConfig (the search starts there).
        // [0] of every list is the shipped value in CombatFeelConfig (the search starts there).
        private static readonly Knob[] Knobs =
        {
            new Knob { Name = "Warrior.AttackDamage", Set = (f, v) => Row(f, ClassId.Warrior).AttackDamage = v, Values = new[] { 21.0, 22 } },
            new Knob { Name = "Warrior.MaxHp", Set = (f, v) => Row(f, ClassId.Warrior).MaxHp = v, Values = new[] { 140.0, 135, 130 } },
            new Knob { Name = "Warrior.AttackCooldown", Set = (f, v) => Row(f, ClassId.Warrior).AttackCooldown = v, Values = new[] { 0.9, 0.85 } },
            new Knob { Name = "Healer.AttackDamage", Set = (f, v) => Row(f, ClassId.Healer).AttackDamage = v, Values = new[] { 15.0, 17, 19 } },
            new Knob { Name = "Healer.AttackCooldown", Set = (f, v) => Row(f, ClassId.Healer).AttackCooldown = v, Values = new[] { 0.8, 0.7 } },
            new Knob { Name = "Farmer.AttackDamage", Set = (f, v) => Row(f, ClassId.Farmer).AttackDamage = v, Values = new[] { 20.0, 23 } },
            new Knob { Name = "Miner.AttackDamage", Set = (f, v) => Row(f, ClassId.Miner).AttackDamage = v, Values = new[] { 14.0, 16 } },
            new Knob { Name = "Miner.MaxHp", Set = (f, v) => Row(f, ClassId.Miner).MaxHp = v, Values = new[] { 95.0, 105 } },
            new Knob { Name = "Demolitions.AttackDamage", Set = (f, v) => Row(f, ClassId.Demolitions).AttackDamage = v, Values = new[] { 12.0, 14 } },
            new Knob { Name = "Demolitions.AttackCooldown", Set = (f, v) => Row(f, ClassId.Demolitions).AttackCooldown = v, Values = new[] { 0.7, 0.8 } },
            new Knob { Name = "TntDamage", Set = (f, v) => f.TntDamage = v, Values = new[] { 55.0, 46 } },
            new Knob { Name = "TntCooldownTicks", Set = (f, v) => f.TntCooldownTicks = v, Values = new[] { 120.0, 150 } },
            new Knob { Name = "Builder.AttackDamage", Set = (f, v) => Row(f, ClassId.Builder).AttackDamage = v, Values = new[] { 26.0, 22 } },
            new Knob { Name = "MinerHeavyPierce", Set = (f, v) => f.MinerHeavyPierce = v, Values = new[] { 1.4 } },
            new Knob { Name = "SunderDamageTaken", Set = (f, v) => f.SunderDamageTaken = v, Values = new[] { 0.15 } },
            new Knob { Name = "Miner.AttackCooldown", Set = (f, v) => Row(f, ClassId.Miner).AttackCooldown = v, Values = new[] { 0.65 } },
        };

        private static void Apply(GameConfig c, double[] vals)
        {
            for (int k = 0; k < Knobs.Length; k++) Knobs[k].Set(c.Depth.CombatFeel, vals[k]);
        }

        private static GameConfig FullWith(double[] vals, bool quiet)
        {
            GameConfig c = CombatFeelTests.Full(true, quiet);
            Apply(c, vals);
            return c;
        }

        private sealed class Score
        {
            public double Duel, Spread, Demo, Shiver;
            public double Top10, WarriorShare, DemoKillShare, KillsPerMin, DemoGateShare;
            public int ShiverHard, ShiverKaiju, ShiverMild, Nevers, AllWins, MaxLock;
            public List<string> Flags;
            public double Total => Duel + Spread + Demo + Shiver;
        }

        // ------------------------------------------------------------------------------------------- the duel part

        private static double DuelCost(double[] vals, Score s, StringBuilder matrix)
        {
            double cost = 0;
            GameConfig cfg = FullWith(vals, true);
            foreach (ClassId atk in Six)
            {
                matrix?.Append("[tuned ttk] ").Append(atk).Append(": ");
                foreach (ClassId def in Six)
                {
                    CombatFeelTests.DuelResult r = CombatFeelTests.RunDuel(atk, def, true, false, 30, cfg);
                    double hi = atk == ClassId.Healer ? 7 : 6, lo = 3;
                    if (double.IsNaN(r.Seconds)) { cost += 10; s.Nevers++; }
                    else cost += Math.Max(0, lo - r.Seconds) + Math.Max(0, r.Seconds - hi);
                    matrix?.Append(def).Append('=').Append(double.IsNaN(r.Seconds) ? "never" : r.Seconds.ToString("0.00")).Append(' ');
                }
                matrix?.Append('\n');
            }
            var wins = new int[6];
            for (int i = 0; i < 6; i++)
                for (int j = i + 1; j < 6; j++)
                {
                    CombatFeelTests.DuelResult r = CombatFeelTests.RunDuel(Six[i], Six[j], true, true, 30, cfg);
                    if (r.Winner == 0) wins[i]++;
                    else if (r.Winner == 1) wins[j]++;
                    else { cost += 1; s.Nevers++; }
                    matrix?.Append("[tuned fight] ").Append(Six[i]).Append(" v ").Append(Six[j]).Append(": ")
                          .Append(r.Winner == 0 ? Six[i].ToString() : r.Winner == 1 ? Six[j].ToString() : "none")
                          .Append(" in ").Append(double.IsNaN(r.Seconds) ? "never" : r.Seconds.ToString("0.00")).Append(" s\n");
                }
            for (int i = 0; i < 6; i++)
            {
                if (wins[i] >= 5) { cost += 4; s.AllWins++; }
                matrix?.Append("[tuned wins] ").Append(Six[i]).Append(' ').Append(wins[i]).Append(" of 5\n");
            }
            s.Duel = cost;
            return cost;
        }

        // ------------------------------------------------------------------------------------------- the 16v16 part

        private static readonly int[] ProbeSeeds = { 1337, 4242, 7, 99 };

        private static void SpreadCost(double[] vals, Score s)
        {
            double top = 0, total = 0, wDmg = 0, kills = 0, demoKills = 0, minutes = 0;
            foreach (int seed in ProbeSeeds)
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
                Apply(cfg, vals);
                World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                InputFrame scratch = SimInput.EmptyInput();
                int n = w.ActorCount, head = w.EventHead;
                var dealt = new double[n];
                var lk = new int[n];
                double matchDmg = 0;
                while (w.Match.Phase != MatchPhase.Ended && w.Tick < 15000)
                {
                    MatchTick.Advance(w, null, scratch);
                    for (int i = 0; i < n; i++)
                    {
                        Actor a = w.Actors[i];
                        lk[i] = a.Alive && a.HitStunTicks > 0 && a.LastCause == DamageCause.Melee ? lk[i] + 1 : 0;
                        if (lk[i] > s.MaxLock) s.MaxLock = lk[i];
                    }
                    for (; head < w.EventHead; head++)
                    {
                        SimEvent e = w.Events[head % World.EventCapacity];
                        if (e.Type == SimEventType.Hit && e.B >= 0 && e.B < n)
                        {
                            dealt[e.B] += e.Value;
                            matchDmg += e.Value;
                            if (w.Actors[e.B].Cls == ClassId.Warrior) wDmg += e.Value;
                        }
                        else if (e.Type == SimEventType.Death)
                        {
                            kills++;
                            if (e.B >= 0 && e.B < n && w.Actors[e.B].Cls == ClassId.Demolitions) demoKills++;
                        }
                    }
                }
                Array.Sort(dealt);
                for (int i = 0; i < 10 && i < n; i++) top += dealt[n - 1 - i];
                total += matchDmg;
                minutes += w.Match.Time / 60;
            }
            s.Top10 = total > 0 ? top / total : 0;
            s.WarriorShare = total > 0 ? wDmg / total : 0;
            s.DemoKillShare = kills > 0 ? demoKills / kills : 0;
            s.KillsPerMin = minutes > 0 ? kills / minutes : 0;
            s.Spread = 30 * Math.Max(0, s.Top10 - 0.50) + 30 * Math.Max(0, s.WarriorShare - 0.35) + 30 * Math.Max(0, s.DemoKillShare - 0.25) +
                       0.5 * Math.Max(0, 34 - s.KillsPerMin) + 0.5 * Math.Max(0, s.MaxLock - 15);
        }

        /// <summary>DemolitionsAttackThrowTests.SameSeedBalanceHerKillShareBeforeVersusAfter's rule-on arm (limit 0.25).</summary>
        private static void DemoGateCost(double[] vals, Score s)
        {
            double sum = 0;
            foreach (int seed in new[] { 1, 7, 42, 1337 })
            {
                GameConfig cfg = GameConfig.CreateDefault();
                Apply(cfg, vals);
                cfg.Abilities.DemolitionsTnt.ThrowOnAttack = true;
                World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Demolitions, Config = cfg });
                InputFrame scratch = SimInput.EmptyInput();
                int max = (int)(150 * cfg.SimHz);
                for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++) MatchTick.Advance(w, null, scratch);
                int all = 0, demo = 0;
                for (int i = 0; i < w.ActorCount; i++)
                {
                    all += w.Actors[i].Kills;
                    if (w.Actors[i].Cls == ClassId.Demolitions) demo += w.Actors[i].Kills;
                }
                sum += all > 0 ? (double)demo / all : 0;
            }
            s.DemoGateShare = sum / 4;
            s.Demo = 30 * Math.Max(0, s.DemoGateShare - 0.235);
        }

        /// <summary>MovementShiverTests.NoShiverInTwoMinutesOfA16v16BotMatchWithTheKaijuOut, counted the gate's way.</summary>
        internal static void ShiverGate(GameConfig cfg, out int kaiju, out int hard, out int mild, List<string> lines)
        {
            cfg.HatchSeconds = 30;
            World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            var watch = new ShiverWatch();
            InputFrame scratch = SimInput.EmptyInput();
            for (int t = 0; t < 3600 && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                watch.Observe(w);
            }
            kaiju = 0; hard = 0; mild = 0;
            var lastPressTick = new Dictionary<int, int>();
            var pressRun = new Dictionary<int, double>();
            int windowTicks = (int)Math.Round(watch.PressSeconds * 30);
            foreach (ShiverFlag f in watch.Flags)
            {
                lines?.Add(ShiverWatch.Format(f));
                if (f.Actor == ShiverWatch.KaijuId) { kaiju++; continue; }
                if (f.Kind == ShiverKind.Reversals || (f.Kind == ShiverKind.Dither && f.Reversals >= 4)) { hard++; continue; }
                if (f.Kind == ShiverKind.Pressed)
                {
                    bool chained = lastPressTick.TryGetValue(f.Actor, out int last) && f.Tick - last <= windowTicks + 1;
                    double run = (chained ? pressRun[f.Actor] : 0) + watch.PressSeconds;
                    lastPressTick[f.Actor] = f.Tick;
                    pressRun[f.Actor] = run;
                    if (run >= 4.0 - 1e-9) { hard++; continue; }
                }
                mild++;
            }
        }

        private static void ShiverCost(double[] vals, Score s)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            Apply(cfg, vals);
            s.Flags = new List<string>();
            ShiverGate(cfg, out s.ShiverKaiju, out s.ShiverHard, out s.ShiverMild, s.Flags);
            s.Shiver = 5 * (s.ShiverKaiju + s.ShiverHard) + Math.Max(0, s.ShiverMild - 6);
        }

        private static Score Evaluate(double[] vals, double bound)
        {
            var s = new Score();
            if (DuelCost(vals, s, null) >= bound) { s.Spread = s.Demo = s.Shiver = double.PositiveInfinity; return s; }
            ShiverCost(vals, s);
            if (s.Duel + s.Shiver >= bound) { s.Spread = s.Demo = double.PositiveInfinity; return s; }
            DemoGateCost(vals, s);
            if (s.Duel + s.Shiver + s.Demo >= bound) { s.Spread = double.PositiveInfinity; return s; }
            SpreadCost(vals, s);
            return s;
        }

        private static string Line(string what, double[] vals, Score s)
        {
            var sb = new StringBuilder("{\"item\":\"b15 tune\",\"what\":\"").Append(what).Append("\",\"total\":").Append(Fmt(s.Total))
                .Append(",\"duel\":").Append(Fmt(s.Duel)).Append(",\"shiver\":").Append(Fmt(s.Shiver)).Append(",\"demo\":").Append(Fmt(s.Demo))
                .Append(",\"spread\":").Append(Fmt(s.Spread)).Append(",\"nevers\":").Append(s.Nevers).Append(",\"allWins\":").Append(s.AllWins)
                .Append(",\"shiverHard\":").Append(s.ShiverHard).Append(",\"shiverKaiju\":").Append(s.ShiverKaiju).Append(",\"shiverMild\":").Append(s.ShiverMild)
                .Append(",\"demoGate\":").Append(s.DemoGateShare.ToString("0.000")).Append(",\"top10\":").Append(s.Top10.ToString("0.000"))
                .Append(",\"warrior\":").Append(s.WarriorShare.ToString("0.000")).Append(",\"demoKill\":").Append(s.DemoKillShare.ToString("0.000"))
                .Append(",\"kpm\":").Append(s.KillsPerMin.ToString("0.00")).Append(",\"maxLockTicks\":").Append(s.MaxLock).Append(",\"set\":{");
            for (int k = 0; k < Knobs.Length; k++) sb.Append(k > 0 ? "," : "").Append('"').Append(Knobs[k].Name).Append("\":").Append(vals[k].ToString("0.###"));
            return sb.Append("}}").ToString();
        }

        private static string Fmt(double v) => double.IsInfinity(v) ? "\"skipped\"" : v.ToString("0.000");

        /// <summary>
        /// The search: start from the shipped numbers, then two passes of coordinate descent (each knob through its candidate
        /// values, keep the best total; a candidate whose cheap parts already reach the best total is cut before its 16v16 part).
        /// Deterministic (fixed seeds, fixed order). Prints every candidate, the best set and its duel matrix.
        /// </summary>
        [Test, Category("Slow")]
        public void CoordinateSearchOverTheCombatNumbersPrintsTheBestSetAndItsMatrix()
        {
            var sw = Stopwatch.StartNew();
            double[] best = new double[Knobs.Length];
            for (int k = 0; k < Knobs.Length; k++) best[k] = Knobs[k].Values[0];
            Score bestScore = Evaluate(best, double.PositiveInfinity);
            Console.WriteLine(Line("start", best, bestScore));
            int shownFlags = 0;
            if (bestScore.Flags != null) foreach (string fl in bestScore.Flags) Console.WriteLine("[tune shiver flag start] " + fl);
            for (int pass = 0; pass < 2 && bestScore.Total > 0; pass++)
                for (int k = 0; k < Knobs.Length && bestScore.Total > 0; k++)
                    foreach (double v in Knobs[k].Values)
                    {
                        if (v == best[k]) continue;
                        if (sw.Elapsed.TotalMinutes > 14) break;
                        var cand = (double[])best.Clone();
                        cand[k] = v;
                        Score s = Evaluate(cand, bestScore.Total);
                        Console.WriteLine(Line("pass " + pass + " " + Knobs[k].Name + "=" + v.ToString("0.###"), cand, s));
                        if (s.ShiverHard + s.ShiverKaiju > 0 && shownFlags++ < 4 && s.Flags != null)
                            foreach (string fl in s.Flags) Console.WriteLine("[tune shiver flag] " + fl);
                        if (s.Total < bestScore.Total) { best = cand; bestScore = s; }
                    }
            var matrix = new StringBuilder();
            DuelCost(best, new Score(), matrix);
            Console.WriteLine(Line("BEST", best, bestScore));
            Console.Write(matrix.ToString());
            Console.WriteLine("{\"item\":\"b15 tune time\",\"seconds\":" + sw.Elapsed.TotalSeconds.ToString("0") + "}");
            Assert.IsFalse(double.IsInfinity(bestScore.Total));
        }

        // ------------------------------------------------------------------------------------------- stuck causes

        /// <summary>
        /// Stuck episodes (the 24-seed measure: pressing to move, under 0.4 u in 3 s) on four 16v16 seeds, each with its cause:
        /// the bot state, how long in it, the bot's own stuck detector clock, wall or crowd in the pressed direction, the target.
        /// </summary>
        [Test]
        public void StuckEpisodesPrintTheirCause()
        {
            var byState = new Dictionary<string, int>();
            var byCell = new Dictionary<int, int>();
            int total = 0, shown = 0;
            foreach (bool feel in new[] { true, false })
                foreach (int seed in ProbeSeeds)
                {
                    GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
                    cfg.Depth.CombatFeel.Enabled = feel;
                    World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                    InputFrame scratch = SimInput.EmptyInput();
                    int n = w.ActorCount, window = 90, count = 0;
                    var ax = new double[n];
                    var az = new double[n];
                    var at = new int[n];
                    for (int i = 0; i < n; i++) at[i] = -1;
                    BotsState st = BotBrain.GetBotsState(w);
                    while (w.Match.Phase != MatchPhase.Ended && w.Tick < 15000)
                    {
                        MatchTick.Advance(w, null, scratch);
                        for (int i = 0; i < n; i++)
                        {
                            Actor a = w.Actors[i];
                            InputFrame f = w.Inputs[i];
                            bool pressing = a.Alive && a.StandUp <= 0 && f.MoveX * f.MoveX + f.MoveZ * f.MoveZ > 0.01;
                            double dx = w.X[i] - ax[i], dz = w.Z[i] - az[i];
                            if (!pressing || at[i] < 0 || dx * dx + dz * dz >= 0.16)
                            {
                                ax[i] = w.X[i];
                                az[i] = w.Z[i];
                                at[i] = pressing ? w.Tick : -1;
                                continue;
                            }
                            if (w.Tick - at[i] < window) continue;
                            at[i] = w.Tick;
                            count++;
                            if (!feel) continue;
                            total++;
                            string state = a.AiState.ToString();
                            byState.TryGetValue(state, out int c0);
                            byState[state] = c0 + 1;
                            int cell = (int)Math.Floor(w.X[i]) * 1000 + (int)Math.Floor(w.Z[i]);
                            byCell.TryGetValue(cell, out int c1);
                            byCell[cell] = c1 + 1;
                            double ml = Math.Sqrt(f.MoveX * f.MoveX + f.MoveZ * f.MoveZ);
                            double px = w.X[i] + f.MoveX / ml * 0.7, pz = w.Z[i] + f.MoveZ / ml * 0.7;
                            bool wall = !MapGrid.IsWalkable(w.Map, (int)Math.Floor(px), (int)Math.Floor(pz));
                            int near = -1;
                            double nd = 99;
                            for (int j = 0; j < n; j++)
                            {
                                if (j == i || !w.Actors[j].Alive) continue;
                                double ex = w.X[j] - w.X[i], ez = w.Z[j] - w.Z[i];
                                double d = Math.Sqrt(ex * ex + ez * ez);
                                if (d < nd) { nd = d; near = j; }
                            }
                            int t = st.Target[i];
                            double td = t >= 0 && t < n ? Math.Sqrt((w.X[t] - w.X[i]) * (w.X[t] - w.X[i]) + (w.Z[t] - w.Z[i]) * (w.Z[t] - w.Z[i])) : -1;
                            if (shown++ < 120)
                                Console.WriteLine("{\"item\":\"b15 stuck\",\"seed\":" + seed + ",\"t\":" + w.Match.Time.ToString("0.0") + ",\"id\":" + i + ",\"cls\":\"" + a.Cls +
                                                  "\",\"state\":\"" + state + "\",\"inState\":" + (w.Match.Time - st.StateSince[i]).ToString("0.0") +
                                                  ",\"anchorAge\":" + (w.Match.Time - st.AnchorTime[i]).ToString("0.0") + ",\"wantMove\":" + st.WantMove[i] +
                                                  ",\"at\":\"" + w.X[i].ToString("0.0") + "," + w.Z[i].ToString("0.0") + "\",\"move\":\"" + f.MoveX.ToString("0.00") + "," + f.MoveZ.ToString("0.00") +
                                                  "\",\"wallAhead\":" + (wall ? "true" : "false") + ",\"nearest\":" + nd.ToString("0.00") + ",\"nearestTeamMate\":" +
                                                  (near >= 0 && w.Actors[near].Team == a.Team ? "true" : "false") + ",\"target\":" + t + ",\"targetDist\":" + td.ToString("0.0") +
                                                  ",\"kaiju\":\"" + w.Kaiju.X.ToString("0.0") + "," + w.Kaiju.Z.ToString("0.0") + "\",\"telegraph\":" + (w.Kaiju.TelegraphIn > 0 || w.Kaiju.MeleeWindupIn > 0 ? "true" : "false") +
                                                  ",\"stun\":" + a.HitStunTicks + ",\"kv\":" + (a.KVx != 0 || a.KVz != 0 ? "true" : "false") + "}");
                        }
                    }
                    Console.WriteLine("{\"item\":\"b15 stuck match\",\"feel\":" + (feel ? "true" : "false") + ",\"seed\":" + seed + ",\"episodes\":" + count + ",\"unjams\":" + st.Unjams + "}");
                }
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in byState) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            var cells = new List<KeyValuePair<int, int>>(byCell);
            cells.Sort((p, q) => q.Value != p.Value ? q.Value.CompareTo(p.Value) : p.Key.CompareTo(q.Key));
            sb.Append("| cells ");
            for (int k = 0; k < cells.Count && k < 10; k++) sb.Append(cells[k].Key / 1000).Append(',').Append(cells[k].Key % 1000).Append('x').Append(cells[k].Value).Append(' ');
            Console.WriteLine("[b15 stuck] " + total + " episodes on " + ProbeSeeds.Length + " seeds, by state: " + sb);
            Assert.Pass();
        }

        // ------------------------------------------------------------------------------------------- per-system cost

        private static readonly string[] SysNames =
        {
            "bot0+reflex", "botsThink", "botsSteer", "abilities", "movement", "hash", "combat", "pickups", "events", "topple", "pieces", "barricades", "walls",
            "resources", "corruption", "handoff", "sources", "hut", "kaiju", "station", "ping", "respawn", "heal", "hill", "match", "endstep",
        };

        private static void TimedTick(World w, InputFrame scratch, long[] acc, Stopwatch sw)
        {
            long t0 = sw.ElapsedTicks;
            int k = 0;
            void Lap() { long t1 = sw.ElapsedTicks; acc[k++] += t1 - t0; t0 = t1; }
            BotReflex.BeginFills(w);
            BotBrain.FillBotInput(w, 0, scratch);
            SimInput.ApplyInput(w, 0, scratch);
            Lap();
            BotBrain.BotsThink(w, SimStep.Dt(w.Config));
            Lap();
            for (int id = 1; id < w.ActorCount; id++)
            {
                BotBrain.FillBotInput(w, id, scratch);
                SimInput.ApplyInput(w, id, scratch);
            }
            BotReflex.EndFills(w);
            Lap();
            if (w.Match.Phase == MatchPhase.Ended) return;
            double dt = SimStep.Dt(w.Config);
            w.Tick++;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                w.PrevX[i] = w.X[i];
                w.PrevZ[i] = w.Z[i];
            }
            Kaiju kj = w.Kaiju;
            kj.PrevX = kj.X;
            kj.PrevZ = kj.Z;
            Abilities.AbilitiesSystem(w, dt); Lap();
            Movement.MovementSystem(w, dt); Lap();
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, n); Lap();
            Combat.CombatSystem(w, dt); Lap();
            Pickups.PickupsSystem(w); Lap();
            MapEvents.EventsSystem(w); Lap();
            Topple.ToppleSystem(w); Lap();
            BuiltPieces.PiecesSystem(w); Lap();
            Barricades.BarricadesSystem(w, dt); Lap();
            BuilderWalls.WallsSystem(w); Lap();
            SimResources.ResourcesSystem(w, dt); Lap();
            Corruption.CorruptionSystem(w, dt); Lap();
            Roles.HandoffSystem(w); Lap();
            Sources.SourcesSystem(w, dt); Lap();
            Blacksmith.HutSystem(w); Lap();
            Kaiju.KaijuSystem(w, dt); Lap();
            Stations.StationSystem(w); Lap();
            PingRules.PingSystem(w); Lap();
            Respawn.RespawnSystem(w, dt); Lap();
            Heal.HealSystem(w); Lap();
            Hill.HillSystem(w, dt); Lap();
            Match.MatchSystem(w, dt); Lap();
            DepthState.EndStep(w); Lap();
            w.Match.Time += dt;
        }

        private static ulong Hash(World w)
        {
            ulong h = 1469598103934665603UL;
            void Mix(double v) { h ^= (ulong)BitConverter.DoubleToInt64Bits(v); h *= 1099511628211UL; }
            Mix(w.Tick);
            Mix(w.Match.Time);
            for (int i = 0; i < w.ActorCount; i++) { Mix(w.X[i]); Mix(w.Z[i]); Mix(w.Actors[i].Hp); Mix(w.Actors[i].Alive ? 1 : 0); }
            Mix(w.Kaiju.X);
            Mix(w.Kaiju.Z);
            Mix(w.Kaiju.Hp);
            return h;
        }

        /// <summary>
        /// Per-system cost, rule off against rule on, on the same seeds (4 x 16v16 whole matches each, arms interleaved, twice):
        /// microseconds per tick of every system and of the whole tick, plus the actors alive and swings per tick that drive
        /// them. The replica of SimStep used to time the systems is first proven equal to SimStep (same state hash after 900
        /// ticks). Prints one line per system; asserts only the replica (cost is judged from the lines).
        /// </summary>
        [Test, Category("Slow")]
        public void PerSystemTickCostRuleOffAgainstOnOnTheSameSeeds()
        {
            {
                GameConfig c1 = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full), c2 = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
                World a = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c1 });
                World b = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c2 });
                InputFrame s1 = SimInput.EmptyInput(), s2 = SimInput.EmptyInput();
                var acc0 = new long[SysNames.Length];
                var sw0 = Stopwatch.StartNew();
                for (int t = 0; t < 900; t++)
                {
                    MatchTick.Advance(a, null, s1);
                    TimedTick(b, s2, acc0, sw0);
                }
                Assert.AreEqual(Hash(a), Hash(b), "the timing replica is SimStep (state hash after 900 ticks)");
            }
            var acc = new long[2][];
            var ticks = new long[2];
            var alive = new double[2];
            for (int arm = 0; arm < 2; arm++) acc[arm] = new long[SysNames.Length];
            var sw = Stopwatch.StartNew();
            for (int rep = 0; rep < 2; rep++)
                foreach (int seed in ProbeSeeds)
                    for (int arm = 0; arm < 2; arm++)
                    {
                        GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
                        cfg.Depth.CombatFeel.Enabled = arm == 1;
                        World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                        InputFrame scratch = SimInput.EmptyInput();
                        for (int t = 0; t < 600; t++) MatchTick.Advance(w, null, scratch); // warm-up, not timed
                        while (w.Match.Phase != MatchPhase.Ended && w.Tick < 15000)
                        {
                            TimedTick(w, scratch, acc[arm], sw);
                            ticks[arm]++;
                            int al = 0;
                            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Alive) al++;
                            alive[arm] += al;
                        }
                    }
            double us = 1e6 / Stopwatch.Frequency;
            double whole0 = 0, whole1 = 0;
            for (int k = 0; k < SysNames.Length; k++)
            {
                double off = acc[0][k] * us / Math.Max(1, ticks[0]), on = acc[1][k] * us / Math.Max(1, ticks[1]);
                whole0 += off;
                whole1 += on;
                Console.WriteLine("{\"item\":\"b15 system cost\",\"system\":\"" + SysNames[k] + "\",\"offUs\":" + off.ToString("0.000") + ",\"onUs\":" + on.ToString("0.000") +
                                  ",\"deltaUs\":" + (on - off).ToString("0.000") + "}");
            }
            Console.WriteLine("{\"item\":\"b15 system cost whole tick\",\"offUs\":" + whole0.ToString("0.00") + ",\"onUs\":" + whole1.ToString("0.00") + ",\"ratio\":" +
                              (whole1 / Math.Max(1e-9, whole0)).ToString("0.000") + ",\"ticksOff\":" + ticks[0] + ",\"ticksOn\":" + ticks[1] +
                              ",\"aliveOff\":" + (alive[0] / Math.Max(1, ticks[0])).ToString("0.0") + ",\"aliveOn\":" + (alive[1] / Math.Max(1, ticks[1])).ToString("0.0") + ",\"limit\":\"<= 1.08\"}");
        }

        // ------------------------------------------------------------------------------------------- the shiver soak, switch off

        /// <summary>
        /// MovementShiverTests.NoActorOrKaijuShiversInWholeBotMatches' guarded arm (seeds 1, 7, 1337; hatch 240 and 60) with
        /// CombatFeel.Enabled off and on: if the switch-off count is far from 0 the soak is red outside this rule. Prints only.
        /// </summary>
        [Test, Category("Slow")]
        public void ShiverSoakWithTheSwitchOffAgainstOn()
        {
            var totals = new int[2];
            for (int arm = 0; arm < 2; arm++)
                foreach (int seed in new[] { 1, 7, 1337 })
                    foreach (double hatch in new[] { 240.0, 60.0 })
                    {
                        GameConfig cfg = GameConfig.CreateDefault();
                        cfg.Depth.CombatFeel.Enabled = arm == 1;
                        cfg.Physics.KaijuNavGuard = true;
                        cfg.Physics.BotWideLine = true;
                        cfg.Physics.BotCalmSteer = true;
                        cfg.Physics.KaijuBridgeSqueeze = true;
                        cfg.HatchSeconds = hatch;
                        World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                        var watch = new ShiverWatch();
                        InputFrame scratch = SimInput.EmptyInput();
                        for (int t = 0; t < 14400 && w.Match.Phase != MatchPhase.Ended; t++)
                        {
                            MatchTick.Advance(w, null, scratch);
                            watch.Observe(w);
                        }
                        totals[arm] += watch.Count;
                        Console.WriteLine("{\"item\":\"b15 shiver soak\",\"feel\":" + (arm == 1 ? "true" : "false") + ",\"seed\":" + seed + ",\"hatch\":" + hatch +
                                          ",\"flags\":" + watch.Count + ",\"dither\":" + watch.CountByKind[0] + ",\"reversals\":" + watch.CountByKind[1] +
                                          ",\"pressed\":" + watch.CountByKind[2] + "}");
                    }
            Console.WriteLine("{\"item\":\"b15 shiver soak total\",\"switchOff\":" + totals[0] + ",\"switchOn\":" + totals[1] + ",\"gate\":0}");
            Assert.Pass();
        }
    }
}

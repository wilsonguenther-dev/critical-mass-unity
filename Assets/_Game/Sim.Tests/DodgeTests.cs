using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// CM b13 item J (RULE CHANGE): dodge for every class (Sim/B13/Dodge.cs, numbers in DodgeConfig) and jump over short
    /// walls. Staged fields drive Movement.MovementSystem directly (one call = one tick); every case PRINTS one line. The
    /// balance arm plays whole bot matches with Dodge.On against the same-seed control (Dodge.On false, nothing else).
    /// </summary>
    [TestFixture]
    public sealed class DodgeTests
    {
        private const int Me = 0;
        private const double X0 = 56.5, Z0 = 40.5;

        private static World Field(GameConfig cfg, out double dt)
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 41, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg ?? GameConfig.CreateDefault() });
            for (int j = 1; j < w.ActorCount; j++) { w.Actors[j].Alive = false; w.Actors[j].RespawnIn = 1e9; }
            Actor a = w.Actors[Me];
            a.Alive = true; a.Hp = a.MaxHp; a.SpawnProtect = 0; a.StandUp = 0; a.AirLen = 0; a.HitStunTicks = 0; a.KVx = 0; a.KVz = 0; a.Facing = 0;
            // a clear 9 x 7 floor patch around the start
            for (int cz = (int)Z0 - 3; cz <= (int)Z0 + 3; cz++)
                for (int cx = (int)X0 - 4; cx <= (int)X0 + 5; cx++) w.Map.Cells[cz * w.Map.Width + cx] = Cell.Floor;
            w.X[Me] = X0; w.Z[Me] = Z0; w.PrevX[Me] = X0; w.PrevZ[Me] = Z0; w.Vx[Me] = 0; w.Vz[Me] = 0;
            w.Tick = 200;
            dt = SimStep.Dt(w.Config);
            return w;
        }

        private static void Tick(World w, double dt, bool dodge, double mx, double mz)
        {
            w.Tick++;
            InputFrame f = w.Inputs[Me];
            f.MoveX = mx; f.MoveZ = mz; f.Dodge = dodge; f.Jump = false; f.Attack = false;
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            Movement.MovementSystem(w, dt);
        }

        private static void Wall(World w, int cx, byte cell)
        {
            for (int cz = (int)Z0 - 3; cz <= (int)Z0 + 3; cz++) w.Map.Cells[cz * w.Map.Width + cx] = cell;
        }

        [Test]
        public void ADodgeCoversItsDistanceInItsTicksThenWalksOn()
        {
            World w = Field(null, out double dt);
            DodgeConfig dc = w.Config.Depth.Dodge;
            Assert.IsTrue(Dodge.On(w), "Full profile: on");
            double sum = 0;
            for (int k = 1; k <= dc.DashTicks; k++) sum += Dodge.Share(k, dc.DashTicks);
            Console.WriteLine("{\"item\":\"b13-jd shares\",\"sum\":" + sum.ToString("F12") + "}");
            Assert.AreEqual(1.0, sum, 1e-12);
            Tick(w, dt, true, 1, 0);
            for (int k = 1; k < dc.DashTicks; k++)
            {
                Console.WriteLine("{\"item\":\"b13-jd dash\",\"tick\":" + k + ",\"x\":" + (w.X[Me] - X0).ToString("F3") + ",\"active\":" + (Dodge.Active(w, Me) ? "true" : "false") + "}");
                Tick(w, dt, false, 1, 0);
            }
            double moved = w.X[Me] - X0;
            Console.WriteLine("{\"item\":\"b13-jd distance\",\"moved\":" + moved.ToString("F3") + ",\"want\":" + dc.Distance + ",\"ticks\":" + w.Dodges.Len[Me] + ",\"exitVx\":" + w.Vx[Me].ToString("F2") + "}");
            Assert.AreEqual(dc.Distance, moved, 0.02, "3.0 u over the dash");
            Assert.AreEqual(dc.DashTicks, w.Dodges.Len[Me]);
            Assert.AreEqual(w.Config.Class(ClassId.Warrior).MoveSpeed, w.Vx[Me], 0.01, "leaves at the walk speed");
            Tick(w, dt, false, 1, 0);
            Assert.IsFalse(Dodge.Active(w, Me), "dash over after its ticks");
            Assert.AreEqual(1, w.Dodges.Count);
        }

        [Test]
        public void InvulnerableOnDashTicksOneToFiveOnlyAndNeverToFalls()
        {
            foreach (int hitAt in new[] { 0, 1, 5, 6 })
            {
                World w = Field(null, out double dt);
                Tick(w, dt, true, 1, 0);
                for (int k = 0; k < hitAt; k++) Tick(w, dt, false, 1, 0);
                double dmg = Damage.ApplyDamage(w, Me, 10, Damage.KaijuSlamSource, w.X[Me] + 1, w.Z[Me], DamageCause.KaijuSlam);
                bool immune = hitAt >= 1 && hitAt <= 5;
                Console.WriteLine("{\"item\":\"b13-jd iframes\",\"dashTick\":" + hitAt + ",\"damage\":" + dmg.ToString("F1") + ",\"wantImmune\":" + (immune ? "true" : "false") +
                                  ",\"fallImmune\":" + (Dodge.Immune(w, Me, DamageCause.Fall) ? "true" : "false") + "}");
                Assert.AreEqual(immune, dmg <= 0, "dash tick " + hitAt);
                Assert.IsFalse(Dodge.Immune(w, Me, DamageCause.Fall), "never immune to a fall");
            }
        }

        [Test]
        public void CooldownAndEveryRefusal()
        {
            World w = Field(null, out double dt);
            Tick(w, dt, true, 1, 0);
            for (int k = 0; k < 30; k++) Tick(w, dt, false, 0, 0);
            int cd = Dodge.CooldownTicks(w, Me);
            Tick(w, dt, true, 1, 0);
            Console.WriteLine("{\"item\":\"b13-jd cooldown\",\"cooldownLeftAt30\":" + cd + ",\"dodges\":" + w.Dodges.Count + "}");
            Assert.AreEqual(1, w.Dodges.Count, "refused on cooldown");
            for (int k = 0; k < 60; k++) Tick(w, dt, false, 0, 0);
            Tick(w, dt, true, 1, 0);
            Assert.AreEqual(2, w.Dodges.Count, "ready again after 90 ticks");

            string[] names = { "airborne", "standing up", "staggered", "knocked", "swing wind-up", "crystal carrier" };
            for (int c = 0; c < names.Length; c++)
            {
                World f = Field(null, out double d2);
                Actor a = f.Actors[Me];
                if (c == 0) a.AirLen = 5;
                if (c == 1) a.StandUp = 1;
                if (c == 2) a.HitStunTicks = 4;
                if (c == 3) a.KVx = 2;
                if (c == 4) { a.SwingPending = true; a.SwingTick = f.Tick; a.SwingWindup = 6; }
                if (c == 5) a.Carry = CarryKind.Corruption;
                bool can = Dodge.CanStart(f, Me);
                Tick(f, d2, true, 1, 0);
                Console.WriteLine("{\"item\":\"b13-jd refusal\",\"case\":\"" + names[c] + "\",\"canStart\":" + (can ? "true" : "false") + ",\"dodges\":" + f.Dodges.Count + "}");
                Assert.IsFalse(can, names[c]);
                Assert.AreEqual(0, f.Dodges.Count, names[c] + ": no dodge");
            }
        }

        [Test]
        public void StopsAtWallsAndShortWallsAndNeverEndsInAHole()
        {
            byte[] cells = { Cell.Wall, Cell.LowWall, Cell.Void };
            string[] names = { "wall", "short wall", "hole" };
            for (int c = 0; c < cells.Length; c++)
            {
                World w = Field(null, out double dt);
                Wall(w, (int)X0 + 2, cells[c]);
                if (cells[c] == Cell.Void) Wall(w, (int)X0 + 3, Cell.Void);
                Tick(w, dt, true, 1, 0);
                for (int k = 0; k < 8; k++) Tick(w, dt, false, 0, 0);
                double end = w.X[Me];
                bool clear = Collide.FootprintClear(w, Me, w.X[Me], w.Z[Me]);
                Console.WriteLine("{\"item\":\"b13-jd blocked\",\"case\":\"" + names[c] + "\",\"endX\":" + end.ToString("F3") + ",\"cellEdge\":" + ((int)X0 + 2) + ",\"ticks\":" + w.Dodges.Len[Me] +
                                  ",\"footprintClear\":" + (clear ? "true" : "false") + ",\"dodges\":" + w.Dodges.Count + "}");
                Assert.Less(end, (int)X0 + 2, names[c] + ": never past the cell edge");
                Assert.IsTrue(clear, names[c] + ": ends on clear ground");
            }
            World r = Field(null, out double d3);
            Wall(r, (int)X0 + 1, Cell.Wall); // the wall right in front: fewer than 2 clear ticks
            Tick(r, d3, true, 1, 0);
            Console.WriteLine("{\"item\":\"b13-jd refusal\",\"case\":\"wall in front\",\"dodges\":" + r.Dodges.Count + "}");
            Assert.AreEqual(0, r.Dodges.Count, "refused against a wall in front");
        }

        [Test]
        public void ClassicAndPhysicsOnlyIgnoreTheDodgeInput()
        {
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                World w = Field(GameConfig.CreateDefault().ApplyRules(p), out double dt);
                Tick(w, dt, true, 0, 0);
                for (int k = 0; k < 6; k++) Tick(w, dt, false, 0, 0);
                Console.WriteLine("{\"item\":\"b13-jd profile\",\"profile\":\"" + p + "\",\"on\":" + (Dodge.On(w) ? "true" : "false") + ",\"moved\":" + (w.X[Me] - X0).ToString("F3") + ",\"dodges\":" + w.Dodges.Count + "}");
                Assert.IsFalse(Dodge.On(w));
                Assert.AreEqual(0, w.Dodges.Count);
                Assert.AreEqual(X0, w.X[Me], 1e-9, "the press does nothing");
            }
        }

        [Test]
        public void AJumpClearsAShortWallNeverLandsOnOneAndATallWallStopsIt()
        {
            string[] names = { "short wall, full speed", "short wall under the landing", "tall wall (barricade)" };
            for (int c = 0; c < 3; c++)
            {
                World w = Field(null, out double dt);
                double speed = w.Config.Class(ClassId.Warrior).MoveSpeed;
                if (c == 0) Wall(w, (int)X0 + 1, Cell.LowWall);
                if (c == 1) { Wall(w, (int)X0 + 2, Cell.LowWall); Wall(w, (int)X0 + 3, Cell.LowWall); }
                if (c == 2) Wall(w, (int)X0 + 1, Cell.Barricade);
                w.X[Me] = X0 - 0.2;
                w.Vx[Me] = speed;
                int k = Jump.PlanJump(w, Me, speed, 0, dt);
                double landX = k > 0 ? Jump_LandX(w, Me, speed, dt, k) : double.NaN;
                int landCell = k > 0 ? w.Map.Cells[(int)Z0 * w.Map.Width + JsMath.FloorToInt(landX)] : -1;
                Console.WriteLine("{\"item\":\"b13-jd jump\",\"case\":\"" + names[c] + "\",\"airTicks\":" + k + ",\"landX\":" + landX.ToString("F2") + ",\"landCell\":" + landCell + "}");
                if (c == 0) { Assert.Greater(k, 0); Assert.Greater(landX, (int)X0 + 2, "lands past the short wall"); }
                if (c == 1) Assert.IsTrue(k == 0 || (landCell != Cell.LowWall && Collide.FootprintClear(w, Me, landX, Z0)), "never lands on a short wall");
                if (c == 2) Assert.IsTrue(k == 0 || landX < (int)X0 + 1, "a tall wall stops the flight");
            }
        }

        /// <summary>Replays the planned flight (as Jump.PlanJump) to its k-th tick end x.</summary>
        private static double Jump_LandX(World w, int id, double vx, double dt, int k)
        {
            double x = w.X[id], z = w.Z[id];
            for (int t = 1; t <= k; t++) Collide.SweepActor(w, id, x, z, vx * dt, 0, true, out x, out z);
            return x;
        }

        // ------------------------------------------------------------------ balance, determinism, allocation

        private sealed class Arm
        {
            public readonly int[] KillsByClass = new int[6];
            public int Kills, LeadChanges, Dodges, Matches, BlastKills;
            // b13 r3 evidence (BotReflex / Dodge counters, summed over the arm's matches)
            public readonly int[] Seen = new int[8], Decided = new int[8], Pressed = new int[8];
            public int StartedOnHill, LeftHill, HillTurned, HillHeld, HillLethalOut, OpenTurned;
            public double Minutes, BotMinutes, SlamDamage, AllDamage;
            public readonly int[] Wins = new int[3];
            public readonly List<double> FirstKo = new List<double>();
        }

        private static GameConfig MatchConfig(int size, bool on)
        {
            GameConfig c = size >= 16 ? GameConfig.CreateDefault() : GameConfig.CreateBrowser5v5();
            c.Depth.Classic = false;
            c.Depth.Dodge.On = on;
            return c;
        }

        private static void PlayMatch(int seed, int size, bool on, Arm arm)
        {
            GameConfig cfg = MatchConfig(size, on);
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = size, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz, lastHead = 0, lastLeader = 0;
            double firstKo = -1;
            var teamDodges = new int[2];
            int kaijuAzure = 0, kaijuCrimson = 0;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                if (w.Kaiju.State == KaijuState.Azure) kaijuAzure++;
                else if (w.Kaiju.State == KaijuState.Crimson) kaijuCrimson++;
                int from = Math.Max(w.EventHead >= lastHead ? lastHead : 0, w.EventHead - World.EventCapacity);
                for (int q = from; q < w.EventHead; q++)
                {
                    SimEvent ev = w.Events[q % World.EventCapacity];
                    if (ev.Type == SimEventType.Dodge && ev.A >= 0 && ev.A < w.ActorCount && !w.Actors[ev.A].IsHuman) { arm.Dodges++; teamDodges[w.Actors[ev.A].Team == TeamId.Azure ? 0 : 1]++; }
                    if ((ev.Type == SimEventType.Hit || ev.Type == SimEventType.Death) && ev.A >= 0 && ev.A < w.ActorCount && ev.Value > 0)
                    {
                        arm.AllDamage += ev.Value;
                        if (ev.Cause == DamageCause.KaijuSlam) arm.SlamDamage += ev.Value;
                    }
                    if (ev.Type != SimEventType.Death || ev.A < 0 || ev.A >= w.ActorCount) continue;
                    if (firstKo < 0) firstKo = w.Match.Time;
                    arm.Kills++;
                    if (ev.B >= 0 && ev.B < w.ActorCount && w.Actors[ev.B].Team != w.Actors[ev.A].Team)
                    {
                        arm.KillsByClass[(int)w.Actors[ev.B].Cls]++;
                        if (ev.Cause == DamageCause.Explosion && w.Actors[ev.B].Cls == ClassId.Demolitions) arm.BlastKills++;
                    }
                }
                lastHead = w.EventHead;
                double lead = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                int leader = lead > 1e-9 ? 1 : lead < -1e-9 ? -1 : 0;
                if (leader != 0) { if (lastLeader != 0 && leader != lastLeader) arm.LeadChanges++; lastLeader = leader; }
            }
            arm.Matches++;
            DodgeState ds = w.Dodges;
            for (int k = 0; k < 8; k++) { arm.Seen[k] += ds.SeenByKind[k]; arm.Decided[k] += ds.DecidedByKind[k]; arm.Pressed[k] += ds.PressedByKind[k]; }
            arm.StartedOnHill += ds.StartedOnHill; arm.LeftHill += ds.LeftHill; arm.HillTurned += ds.HillTurned; arm.HillHeld += ds.HillHeld; arm.HillLethalOut += ds.HillLethalOut; arm.OpenTurned += ds.OpenTurned;
            arm.Minutes += w.Match.Time / 60.0;
            int bots = 0;
            for (int i = 0; i < w.ActorCount; i++) if (!w.Actors[i].IsHuman) bots++;
            arm.BotMinutes += bots * w.Match.Time / 60.0;
            if (firstKo >= 0) arm.FirstKo.Add(firstKo);
            arm.Wins[w.Match.Winner == MatchResult.Azure ? 0 : w.Match.Winner == MatchResult.Crimson ? 1 : 2]++;
            Console.WriteLine("{\"item\":\"b13-jd balance match\",\"size\":" + size + ",\"seed\":" + seed + ",\"dodge\":\"" + (on ? "on" : "off (control)") + "\",\"winner\":\"" + w.Match.Winner +
                              "\",\"firstKoAt\":" + firstKo.ToString("F1") + ",\"seconds\":" + w.Match.Time.ToString("F0") +
                              ",\"kaijuMostlyWith\":\"" + (kaijuAzure > kaijuCrimson ? "Azure" : kaijuCrimson > kaijuAzure ? "Crimson" : "none") + "\",\"kaijuAzureS\":" + (kaijuAzure / cfg.SimHz).ToString("F0") + ",\"kaijuCrimsonS\":" + (kaijuCrimson / cfg.SimHz).ToString("F0") +
                              ",\"botDodgesAzure\":" + teamDodges[0] + ",\"botDodgesCrimson\":" + teamDodges[1] + ",\"hillAzureS\":" + w.Match.HillSeconds.Azure.ToString("F0") + ",\"hillCrimsonS\":" + w.Match.HillSeconds.Crimson.ToString("F0") + "}");
        }

        private static double Median(List<double> v)
        {
            if (v.Count == 0) return -1;
            v.Sort();
            return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        private static void Limit(List<string> failures, string what, double on, double control, bool pass, string limit)
        {
            string line = what + ": on " + on.ToString("F3") + ", control " + control.ToString("F3") + ", limit " + limit + " -> " + (pass ? "PASS" : "FAIL");
            Console.WriteLine("{\"item\":\"b13-jd limit\",\"line\":\"" + line + "\"}");
            if (!pass) failures.Add(line);
        }

        /// <summary>
        /// BALANCE vs control (Dodge.On false; 5v5 Full 1 to 20 and 16v16 1337, 4242, 7, 99, 2024, 31337). Limits: win share
        /// within 0.15 (5v5, and all 26 together); median first knock-out -10% / +20%; kills per minute -25% / +10%; class kill
        /// shares within 0.08; hill lead changes +-30%; dodges per bot-minute (Normal) between 1 and 4; Kaiju slam damage share
        /// printed (expected to fall).
        /// </summary>
        [Test]
        public void DodgeKeepsTheControlsBalanceOnTheSameSeeds()
        {
            var failures = new List<string>();
            var all = new[] { new Arm(), new Arm() };
            foreach (int size in new[] { 5, 16 })
            {
                int[] seeds = size == 16 ? new[] { 1337, 4242, 7, 99, 2024, 31337 } : new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
                var arms = new[] { new Arm(), new Arm() };
                foreach (int seed in seeds) for (int a = 0; a < 2; a++) PlayMatch(seed, size, a == 0, arms[a]);
                for (int a = 0; a < 2; a++) { all[a].Matches += arms[a].Matches; for (int t = 0; t < 3; t++) all[a].Wins[t] += arms[a].Wins[t]; }
                string tag = size + "v" + size;
                double kOn = arms[0].Kills / Math.Max(1e-9, arms[0].Minutes), kOff = arms[1].Kills / Math.Max(1e-9, arms[1].Minutes);
                double koOn = Median(arms[0].FirstKo), koOff = Median(arms[1].FirstKo);
                Limit(failures, tag + " median first knock-out (s)", koOn, koOff, koOff > 0 && koOn >= koOff * 0.90 - 1e-9 && koOn <= koOff * 1.20 + 1e-9, "-10% / +20%");
                Limit(failures, tag + " kills per minute", kOn, kOff, kOff > 0 && kOn >= kOff * 0.75 - 1e-9 && kOn <= kOff * 1.10 + 1e-9, "-25% / +10%");
                Limit(failures, tag + " hill lead changes", arms[0].LeadChanges, arms[1].LeadChanges, Math.Abs(arms[0].LeadChanges - arms[1].LeadChanges) <= 0.30 * Math.Max(1, arms[1].LeadChanges) + 1e-9, "+-30%");
                double dpm = arms[0].Dodges / Math.Max(1e-9, arms[0].BotMinutes);
                Limit(failures, tag + " dodges per bot-minute (Normal)", dpm, arms[1].Dodges / Math.Max(1e-9, arms[1].BotMinutes), dpm >= 1 && dpm <= 4, "1 to 4");
                string[] kn = { "", "slam", "tnt", "claw", "melee" };
                for (int k = 1; k <= 4; k++)
                    Console.WriteLine("{\"item\":\"b13-jd threats\",\"size\":" + size + ",\"threat\":\"" + kn[k] + "\",\"seenPerBotMin\":" + (arms[0].Seen[k] / Math.Max(1e-9, arms[0].BotMinutes)).ToString("F2") +
                                      ",\"decidedPerBotMin\":" + (arms[0].Decided[k] / Math.Max(1e-9, arms[0].BotMinutes)).ToString("F2") + ",\"pressedPerBotMin\":" + (arms[0].Pressed[k] / Math.Max(1e-9, arms[0].BotMinutes)).ToString("F2") +
                                      ",\"seen\":" + arms[0].Seen[k] + ",\"decided\":" + arms[0].Decided[k] + ",\"pressed\":" + arms[0].Pressed[k] + "}");
                Console.WriteLine("{\"item\":\"b13-jd hill dodges\",\"size\":" + size + ",\"startedOnHill\":" + arms[0].StartedOnHill + ",\"endedOffHill\":" + arms[0].LeftHill + ",\"turnedToStay\":" + arms[0].HillTurned +
                                  ",\"heldNoDodge\":" + arms[0].HillHeld + ",\"lethalLeft\":" + arms[0].HillLethalOut + ",\"offHillTurnedToOpenGround\":" + arms[0].OpenTurned + ",\"leadChangesOn\":" + arms[0].LeadChanges + ",\"leadChangesControl\":" + arms[1].LeadChanges + "}");
                Console.WriteLine("{\"item\":\"b13-jd blast kills\",\"size\":" + size + ",\"demolitionsBlastKillsOn\":" + arms[0].BlastKills + ",\"control\":" + arms[1].BlastKills + ",\"killsOn\":" + arms[0].Kills + ",\"killsControl\":" + arms[1].Kills + "}");
                Console.WriteLine("{\"item\":\"b13-jd slam damage share\",\"size\":" + size + ",\"on\":" + (arms[0].SlamDamage / Math.Max(1e-9, arms[0].AllDamage)).ToString("F3") +
                                  ",\"control\":" + (arms[1].SlamDamage / Math.Max(1e-9, arms[1].AllDamage)).ToString("F3") + "}");
                int sOn = 0, sOff = 0;
                for (int c = 0; c < 6; c++) { sOn += arms[0].KillsByClass[c]; sOff += arms[1].KillsByClass[c]; }
                for (int c = 0; c < 6; c++)
                {
                    double on = sOn > 0 ? (double)arms[0].KillsByClass[c] / sOn : 0, off = sOff > 0 ? (double)arms[1].KillsByClass[c] / sOff : 0;
                    Limit(failures, tag + " " + (ClassId)c + " kill share", on, off, Math.Abs(on - off) <= 0.08 + 1e-9, "within 0.08");
                }
                for (int t = 0; t < 2; t++)
                {
                    double on = (double)arms[0].Wins[t] / seeds.Length, off = (double)arms[1].Wins[t] / seeds.Length;
                    if (size == 5) Limit(failures, "5v5 " + (t == 0 ? "Azure" : "Crimson") + " win share", on, off, Math.Abs(on - off) <= 0.15 + 1e-9, "within 0.15");
                    else Console.WriteLine("{\"item\":\"b13-jd limit\",\"line\":\"16v16 " + (t == 0 ? "Azure" : "Crimson") + " win share (printed only, 6 matches): on " + on.ToString("F3") + ", control " + off.ToString("F3") + "\"}");
                }
            }
            for (int t = 0; t < 2; t++)
            {
                double on = (double)all[0].Wins[t] / Math.Max(1, all[0].Matches), off = (double)all[1].Wins[t] / Math.Max(1, all[1].Matches);
                Limit(failures, "all matches " + (t == 0 ? "Azure" : "Crimson") + " win share", on, off, Math.Abs(on - off) <= 0.15 + 1e-9, "within 0.15");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void TheSameSeedTwiceIsIdenticalWithBotsDodging()
        {
            var x = new double[2];
            var dodges = new int[2];
            for (int run = 0; run < 2; run++)
            {
                World w = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = MatchConfig(16, true) });
                InputFrame scratch = SimInput.EmptyInput();
                for (int t = 0; t < 3600; t++) MatchTick.Advance(w, null, scratch);
                double s = 0;
                for (int i = 0; i < w.ActorCount; i++) s += w.X[i] * (i + 1) + w.Z[i] * (i + 7) + w.Actors[i].Hp * 0.001;
                x[run] = s;
                dodges[run] = w.Dodges.Count;
            }
            Console.WriteLine("{\"item\":\"b13-jd determinism\",\"fingerprintA\":" + x[0].ToString("R") + ",\"fingerprintB\":" + x[1].ToString("R") + ",\"dodgesA\":" + dodges[0] + ",\"dodgesB\":" + dodges[1] + "}");
            Assert.AreEqual(x[0], x[1], 0.0);
            Assert.AreEqual(dodges[0], dodges[1]);
        }

        [Test]
        public void NoAllocationOver300TicksOfA16v16WithBotsDodging()
        {
            const int Warm = 600, Ticks = 300;
            var alloc = new long[2];
            var clock = new long[2];
            long own = 0;
            int dodgesSeen = 0;
            // b13 r3: both arms are built and warmed first, then measured tick by tick in turn (on, control, on, ...), so
            // JIT tiering and runner clock drift fall on both arms alike (round 1 printed on 41.0 vs control 74.1 us, round 3's
            // first run 58.4 vs 40.1 us, with the arm measured first always the odd one)
            var worlds = new World[2];
            var scratches = new InputFrame[2];
            for (int arm = 0; arm < 2; arm++)
            {
                worlds[arm] = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = MatchConfig(16, arm == 0) });
                scratches[arm] = SimInput.EmptyInput();
                for (int t = 0; t < Warm; t++) MatchTick.Advance(worlds[arm], null, scratches[arm]);
            }
            {
                World w = worlds[0];
                // the rule's own calls run once in the warm-up too, so a first call's one-time set-up (static tables, JIT) is
                // not counted as a per-tick allocation (round 1 printed 74416 B with no dodge in the window)
                for (int id = 0; id < w.ActorCount; id++)
                {
                    Dodge.Active(w, id); Dodge.Immune(w, id, DamageCause.Melee); Dodge.Phase(w, id, 0.5); Dodge.CooldownTicks(w, id);
                    BotReflex.FindThreat(w, null, w.Config.Depth.Dodge, id, out int _, out int _, out double _, out double _);
                }
            }
            int d0 = worlds[0].Dodges.Count;
            for (int t = 0; t < Ticks; t++)
            {
                for (int arm = 0; arm < 2; arm++)
                {
                    World w = worlds[arm];
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    long c0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    MatchTick.Advance(w, null, scratches[arm]);
                    clock[arm] += System.Diagnostics.Stopwatch.GetTimestamp() - c0;
                    alloc[arm] += GC.GetAllocatedBytesForCurrentThread() - a0;
                    if (arm != 0) continue;
                    long o0 = GC.GetAllocatedBytesForCurrentThread();
                    for (int id = 0; id < w.ActorCount; id++)
                    {
                        Dodge.Active(w, id); Dodge.Immune(w, id, DamageCause.Melee); Dodge.Phase(w, id, 0.5); Dodge.CooldownTicks(w, id);
                        BotReflex.FindThreat(w, null, w.Config.Depth.Dodge, id, out int _, out int _, out double _, out double _);
                    }
                    own += GC.GetAllocatedBytesForCurrentThread() - o0;
                }
            }
            dodgesSeen = worlds[0].Dodges.Count - d0;
            double usOn = clock[0] * 1e6 / System.Diagnostics.Stopwatch.Frequency / Ticks, usOff = clock[1] * 1e6 / System.Diagnostics.Stopwatch.Frequency / Ticks;
            Console.WriteLine("{\"item\":\"b13-jd allocation\",\"ruleOwnCallsBytes\":" + own + ",\"wholeTickBytesOn\":" + alloc[0] + ",\"wholeTickBytesControl\":" + alloc[1] + ",\"dodgesInWindow\":" + dodgesSeen + "}");
            Console.WriteLine("{\"item\":\"b13-jd sim cost\",\"usPerTickOn\":" + usOn.ToString("F1") + ",\"usPerTickControl\":" + usOff.ToString("F1") + ",\"limit\":1.10,\"verdict\":\"" + (usOn <= usOff * 1.10 ? "PASS" : "FAIL") + "\"}");
            Assert.AreEqual(0, own, "the dodge rule's own calls allocate nothing");
            Assert.LessOrEqual(alloc[0], alloc[1], "dodging adds no allocation to the tick");
        }
    }
}

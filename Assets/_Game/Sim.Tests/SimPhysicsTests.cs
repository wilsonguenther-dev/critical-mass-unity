using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// The sim-physics lane (PHYSICS-BUILD-LIST DG-67..DG-100): one rule test per acceptance check. 5v5 browser roster
    /// (azure 0 warrior human, 1 warrior, 2 farmer, 3 farmer, 4 miner; crimson 5 warrior, 6 warrior, 7 farmer, 8 farmer,
    /// 9 miner), fights staged on open hill ground around (56, 40). Pace 1 unless a test says otherwise.
    /// </summary>
    [TestFixture]
    public sealed class SimPhysicsTests
    {
        internal const double HillX = 56;
        internal const double HillZ = 40;

        /// <summary>The browser 5v5 config with PhysicsConfig.Classic on: ported browser tests that pin press-tick damage use it.</summary>
        public static GameConfig ClassicBrowser()
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            c.Physics.Classic = true;
            return c;
        }

        internal static World Make(Action<GameConfig> tweak = null, TeamId human = TeamId.Azure, double pace = 0, int seed = 7)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            cfg.Physics.Knockback = true; // the lane's rule tests stage the knockback feature (shipped default-off, see PR)
            tweak?.Invoke(cfg);
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 5, HumanTeam = human, HumanClass = ClassId.Warrior, Config = cfg, Pace = pace });
            // Park everyone far apart on their pads so only the staged actors meet.
            return w;
        }

        internal static void Place(World w, int id, double x, double z)
        {
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
            w.PrevX[id] = w.X[id];
            w.PrevZ[id] = w.Z[id];
            w.Actors[id].SpawnProtect = 0;
            w.Actors[id].StandUp = 0;
        }

        /// <summary>Move every actor except the listed ones to its own pad corner, out of the way, with spawn protection kept off.</summary>
        internal static void ParkOthers(World w, params int[] keep)
        {
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (Array.IndexOf(keep, i) >= 0) continue;
                PadDef pad = w.Map.Pads[(int)w.Actors[i].Team];
                Place(w, i, pad.X, pad.Z);
                w.Actors[i].SpawnProtect = 1000;
            }
        }

        internal static void Input(World w, int id, double aimX, double aimZ, bool attack, double moveX = 0, double moveZ = 0)
        {
            InputFrame f = SimInput.EmptyInput();
            f.AimX = aimX;
            f.AimZ = aimZ;
            f.Attack = attack;
            f.MoveX = moveX;
            f.MoveZ = moveZ;
            SimInput.ApplyInput(w, id, f);
        }

        internal static void Idle(World w)
        {
            for (int i = 0; i < w.ActorCount; i++) SimInput.ApplyInput(w, i, SimInput.EmptyInput());
        }

        internal static List<SimEvent> Events(World w, SimEventType type, int fromHead = 0)
        {
            var output = new List<SimEvent>();
            int from = Math.Max(fromHead, w.EventHead - World.EventCapacity);
            for (int i = from; i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type != type) continue;
                output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value, Cause = e.Cause, Radius = e.Radius });
            }
            return output;
        }

        // ------------------------------------------------------------------ DG-67 fair trades

        private static void StageTrade(World w, int a, int b, double ax, double bx)
        {
            ParkOthers(w, a, b);
            Place(w, a, ax, HillZ);
            Place(w, b, bx, HillZ);
            w.Actors[a].Hp = 1;
            w.Actors[b].Hp = 1;
        }

        [Test]
        public void DG67TwoLethalSwingsPressedOnOneTickBothKill()
        {
            World w = Make(c => c.Physics.AttackWindup = false);
            StageTrade(w, 0, 5, HillX - 0.6, HillX + 0.6);
            Idle(w);
            Input(w, 0, HillX + 0.6, HillZ, true);
            Input(w, 5, HillX - 0.6, HillZ, true);
            SimStep.Step(w);
            Assert.IsFalse(w.Actors[0].Alive, "the lower id dies too");
            Assert.IsFalse(w.Actors[5].Alive, "the higher id dies");
        }

        [Test]
        public void DG67ClassicKeepsTheBrowserIdOrderTheLowerIdWins()
        {
            World w = Make(c => c.Physics.Classic = true);
            StageTrade(w, 0, 5, HillX - 0.6, HillX + 0.6);
            Idle(w);
            Input(w, 0, HillX + 0.6, HillZ, true);
            Input(w, 5, HillX - 0.6, HillZ, true);
            SimStep.Step(w);
            Assert.IsTrue(w.Actors[0].Alive);
            Assert.IsFalse(w.Actors[5].Alive);
        }

        [Test]
        public void DG67MirrorTheTeamHoldingTheLowIdsGivesTheMirroredResult()
        {
            // Azure human: azure ids 0..4. Crimson human: crimson ids 0..4. Same staging mirrored in x: same outcome per side.
            World az = Make(c => c.Physics.AttackWindup = false, TeamId.Azure);
            World cr = Make(c => c.Physics.AttackWindup = false, TeamId.Crimson);
            StageTrade(az, 0, 5, HillX - 0.6, HillX + 0.6);
            StageTrade(cr, 5, 0, HillX - 0.6, HillX + 0.6);
            cr.Actors[0].Hp = 30;
            az.Actors[5].Hp = 30;
            Idle(az);
            Idle(cr);
            Input(az, 0, HillX + 0.6, HillZ, true);
            Input(az, 5, HillX - 0.6, HillZ, true);
            Input(cr, 5, HillX + 0.6, HillZ, true);
            Input(cr, 0, HillX - 0.6, HillZ, true);
            SimStep.Step(az);
            SimStep.Step(cr);
            Assert.AreEqual(az.Actors[0].Alive, cr.Actors[5].Alive);
            Assert.AreEqual(az.Actors[5].Alive, cr.Actors[0].Alive);
            Assert.AreEqual(az.Actors[5].Hp, cr.Actors[0].Hp, 1e-9);
        }

        [Test]
        public void DG67ReplayIsIdentical()
        {
            ulong a = MatchHash.Run(3, 5, 2400, ClassId.Warrior);
            Assert.AreEqual(a, MatchHash.Run(3, 5, 2400, ClassId.Warrior));
        }
        // ------------------------------------------------------------------ DG-68 one timing unit

        /// <summary>The Sim source folder (tests run in the checkout, as BoundaryTests does).</summary>
        internal static string SimDir()
        {
            string d = Directory.GetCurrentDirectory();
            for (int i = 0; i < 6 && d != null; i++, d = Path.GetDirectoryName(d))
            {
                string p = Path.Combine(d, "Assets", "_Game", "Sim");
                if (Directory.Exists(p)) return p;
            }
            Assert.Ignore("Sim sources not found from the working directory");
            return null;
        }

        [Test]
        public void DG68NoConfigTicksFieldIsReadWithoutTheConverter()
        {
            // Every double field named *Ticks on a config group is a 30 Hz count: each read must pass through Durations.
            var names = new HashSet<string>();
            foreach (Type t in typeof(GameConfig).Assembly.GetTypes())
            {
                if (t.Namespace != "CriticalMass.Sim.Config" || !t.Name.EndsWith("Config")) continue;
                foreach (FieldInfo f in t.GetFields()) if (f.Name.EndsWith("Ticks") && f.FieldType == typeof(double)) names.Add(f.Name);
            }
            Assert.Greater(names.Count, 10);
            var rx = new Regex(@"\.(" + string.Join("|", names) + @")\b(?!\s*\()"); // a field read, not a method call (Jump.AirTicks(c))
            var bad = new List<string>();
            foreach (string file in Directory.GetFiles(SimDir(), "*.cs", SearchOption.AllDirectories))
            {
                string fn = Path.GetFileName(file);
                if (fn == "GameConfig.cs" || fn == "Durations.cs") continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i].Trim();
                    if (l.StartsWith("//")) continue;
                    if (rx.IsMatch(l) && !l.Contains("Durations.")) bad.Add(fn + ":" + (i + 1) + " " + l);
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void DG68TheOnScreenTimingTableAtTheDefaultPaceMatchesTheCheckedInTable()
        {
            GameConfig c = GameConfig.CreateDefault();
            // Owner 2026-11-12: the default pace is 1x; 2x is the "Fast" match option (WorldOptions.Pace = 2).
            World w1 = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c });
            Assert.AreEqual(1, w1.Dur.Pace);
            Assert.AreEqual(14, w1.Dur.KaijuClawWindup, "0.45 s at 1x");
            Assert.AreEqual(36, w1.Dur.SlamTelegraph, "the 1.2 s sim telegraph already beats 0.8 s at 1x");
            Assert.AreEqual(4, w1.Dur.AttackWindup[(int)ClassId.Warrior], "0.12 s at 1x");
            Assert.AreEqual(3, w1.Dur.AttackWindup[(int)ClassId.Farmer], "0.10 s at 1x");
            World w = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c, Pace = 2 });
            double pace = w.Dur.Pace;
            Assert.AreEqual(2, pace);
            // name, ticks, checked-in on-screen seconds at pace 2
            var table = new (string, int, double)[]
            {
                ("kaiju claw wind-up", w.Dur.KaijuClawWindup, TableClaw),
                ("kaiju slam telegraph", w.Dur.SlamTelegraph, TableSlam),
                ("warrior wind-up", w.Dur.AttackWindup[(int)ClassId.Warrior], TableWarriorWindup),
                ("farmer wind-up", w.Dur.AttackWindup[(int)ClassId.Farmer], TableFarmerWindup),
                ("jump", Jump.AirTicks(c), 0.3),
                ("tnt fuse", Durations.T30(c, c.Abilities.DemolitionsTnt.FuseTicks), 0.75),
                ("mine arm", Durations.T30(c, c.Abilities.DemolitionsMine.ArmTicks), 0.25),
                ("pillar fall", Durations.T30(c, c.Abilities.Topple.FallTicks), 0.3),
                ("delivery wind-up", Delivery.WindupTicks(c), 0.2),
            };
            foreach (var (name, ticks, want) in table)
            {
                Console.WriteLine("{\"item\":\"DG-68\",\"window\":\"" + name + "\",\"ticks\":" + ticks + ",\"onScreen\":" + Durations.OnScreen(c, ticks, pace).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "}");
                Assert.AreEqual(want, Durations.OnScreen(c, ticks, pace), 0.0085, name);
            }
        }

        // The checked-in on-screen seconds at the Fast pace (2x) of the windows later items change.
        private const double TableClaw = 0.45;
        private const double TableSlam = 0.8;
        private const double TableWarriorWindup = 0.12;
        private const double TableFarmerWindup = 0.10;

        [Test]
        public void DG68TicksAt30HzRescaleWithSimHzAndFloorsHoldAtAnyPace()
        {
            GameConfig c = GameConfig.CreateDefault();
            Assert.AreEqual(45, Durations.T30(c, 45));
            c.SimHz = 60;
            Assert.AreEqual(90, Durations.T30(c, 45), "a 60 Hz sim keeps the same seconds");
            c.SimHz = 30;
            Assert.AreEqual(27, Durations.Floor(c, 11, 0.45, 2));
            Assert.AreEqual(14, Durations.Floor(c, 11, 0.45, 1));
            Assert.AreEqual(54, Durations.Floor(c, 11, 0.45, 4));
            Assert.AreEqual(40, Durations.Floor(c, 40, 0.45, 2), "a longer window is kept");
        }
        // ------------------------------------------------------------------ DG-69 attack wind-up

        private static World Duel(out int windup, double pace = 0, Action<GameConfig> tweak = null)
        {
            World w = Make(c => { c.Physics.Knockback = false; tweak?.Invoke(c); }, pace: pace);
            ParkOthers(w, 0, 5);
            Place(w, 0, HillX - 0.75, HillZ);
            Place(w, 5, HillX + 0.75, HillZ);
            windup = w.Dur.AttackWindup[(int)ClassId.Warrior];
            Idle(w);
            Input(w, 5, HillX - 0.75, HillZ, false);
            return w;
        }

        [Test]
        public void DG69APressAtTDoesNoDamageUntilTPlusWThenFullDamage()
        {
            World w = Duel(out int windup);
            Assert.AreEqual(4, windup, "0.12 s at pace 1 = 4 ticks");
            double hp0 = w.Actors[5].Hp;
            Input(w, 0, HillX + 0.75, HillZ, true);
            SimStep.Step(w);
            int t = w.Tick;
            Input(w, 0, HillX + 0.75, HillZ, false);
            while (w.Tick < t + windup - 1)
            {
                Assert.AreEqual(hp0, w.Actors[5].Hp, "no damage during the wind-up (tick " + w.Tick + ")");
                SimStep.Step(w);
            }
            Assert.AreEqual(hp0, w.Actors[5].Hp, "no damage on T+W-1");
            SimStep.Step(w);
            Assert.AreEqual(t + windup, w.Tick);
            Assert.AreEqual(hp0 - w.Config.Class(ClassId.Warrior).AttackDamage, w.Actors[5].Hp, 1e-9, "full damage on T+W");
        }

        [Test]
        public void DG69AVictimThatLeavesReachDuringTheWindupTakesNothing()
        {
            World w = Duel(out int windup);
            Place(w, 5, HillX + 1.25, HillZ); // 2.0 apart: inside the 2.15 reach at the press
            double hp0 = w.Actors[5].Hp;
            Input(w, 0, HillX + 1.25, HillZ, true);
            Input(w, 5, HillX + 10, HillZ, false, 1, 0); // and stepping away
            SimStep.Step(w);
            Input(w, 0, HillX + 1.25, HillZ, false);
            for (int k = 0; k < windup + 2; k++) SimStep.Step(w);
            Assert.AreEqual(hp0, w.Actors[5].Hp);
        }

        [Test]
        public void DG69AttackStartOnThePressTickAndAttackContactOnTPlusWEvenOnAWhiff()
        {
            World w = Duel(out int windup);
            Place(w, 5, HillX + 8, HillZ); // out of reach: a whiff
            int head = w.EventHead;
            Input(w, 0, HillX + 8, HillZ, true);
            SimStep.Step(w);
            int t = w.Tick;
            List<SimEvent> starts = Events(w, SimEventType.AttackStart, head);
            Assert.AreEqual(1, starts.Count);
            Assert.AreEqual(0, starts[0].A);
            Assert.AreEqual(Combat.KindAttack, starts[0].B);
            SimEvent raw = null;
            for (int i = head; i < w.EventHead; i++) if (w.Events[i % World.EventCapacity].Type == SimEventType.AttackStart) raw = w.Events[i % World.EventCapacity];
            Assert.AreEqual(windup, raw.Windup);
            Assert.AreEqual(t, w.Actors[0].SwingTick);
            Assert.AreEqual(windup, w.Actors[0].SwingWindup);
            Input(w, 0, HillX + 8, HillZ, false);
            for (int k = 0; k < windup - 1; k++) SimStep.Step(w);
            Assert.AreEqual(0, Events(w, SimEventType.AttackContact, head).Count, "no contact before T+W");
            SimStep.Step(w);
            List<SimEvent> contacts = Events(w, SimEventType.AttackContact, head);
            Assert.AreEqual(1, contacts.Count, "contact on T+W");
            Assert.AreEqual(0, contacts[0].Value, "a whiff hits nobody");
        }

        [Test]
        public void DG69AttackersSlowTo40PercentWhileSwinging()
        {
            // The rule, staged: the shipped default is 1.0 (no slowdown) because 0.4 broke the hill-presence and CM-64 bands.
            World w = Duel(out int windup, tweak: c => c.Physics.SwingMoveMultiplier = 0.4);
            Place(w, 5, HillX + 8, HillZ);
            for (int k = 0; k < 20; k++) { Input(w, 0, HillX + 8, HillZ, false, 0, 1); SimStep.Step(w); }
            double free = w.Z[0] - w.PrevZ[0];
            Input(w, 0, HillX + 8, HillZ, true, 0, 1);
            SimStep.Step(w);
            Input(w, 0, HillX + 8, HillZ, false, 0, 1);
            for (int k = 0; k < 6; k++) SimStep.Step(w);
            double slowed = w.Z[0] - w.PrevZ[0];
            Assert.AreEqual(0.4, slowed / free, 0.02);
        }

        [Test]
        public void DG69WindupZeroIsTheBrowserPressTickHit()
        {
            World w = Duel(out int windup, 0, c => c.Physics.AttackWindup = false);
            Assert.AreEqual(0, windup);
            double hp0 = w.Actors[5].Hp;
            Input(w, 0, HillX + 0.75, HillZ, true);
            SimStep.Step(w);
            Assert.Less(w.Actors[5].Hp, hp0);
        }

        [Test]
        public void DG69WindupIsRealSecondsAtTheMatchPace()
        {
            World p2 = Make(null, pace: 2);
            Assert.AreEqual(7, p2.Dur.AttackWindup[(int)ClassId.Warrior], "0.12 s at pace 2");
            Assert.AreEqual(6, p2.Dur.AttackWindup[(int)ClassId.Farmer], "0.10 s at pace 2");
        }

        [Test]
        public void DG69InputReadinessFuzzReadyEqualsSimAcceptanceOnEveryTick()
        {
            World w = Duel(out int windup);
            var rng = new Random(11);
            int agree = 0;
            for (int k = 0; k < 600; k++)
            {
                bool press = rng.NextDouble() < 0.5;
                bool ready = Combat.AttackReady(w, 0);
                Input(w, 0, HillX + 0.75, HillZ, press);
                Input(w, 5, HillX - 0.75, HillZ, rng.NextDouble() < 0.3);
                int head = w.EventHead;
                SimStep.Step(w);
                bool accepted = false;
                foreach (SimEvent e in Events(w, SimEventType.AttackStart, head)) if (e.A == 0) accepted = true;
                if (press) Assert.AreEqual(ready, accepted, "tick " + w.Tick);
                if (press) agree++;
                if (!w.Actors[0].Alive || !w.Actors[5].Alive) break;
            }
            Assert.Greater(agree, 50);
        }
        // ------------------------------------------------------------------ DG-70 knockback, hit-stun, weight

        /// <summary>Warrior 0 swings once along +x at victim v standing 1.2 u ahead; returns the victim's x travel 8 ticks after contact.</summary>
        private static double KnockTravel(World w, int v, out int stunAfterHit)
        {
            ParkOthers(w, 0, v);
            Place(w, 0, HillX - 1.2, HillZ);
            Place(w, v, HillX, HillZ);
            Idle(w);
            Input(w, 0, HillX, HillZ, true);
            SimStep.Step(w);
            Input(w, 0, HillX, HillZ, false);
            int windup = w.Actors[0].SwingWindup;
            for (int k = 0; k < windup; k++) SimStep.Step(w);
            Assert.Less(w.Actors[v].Hp, w.Actors[v].MaxHp, "the hit landed");
            stunAfterHit = w.Actors[v].HitStunTicks;
            double x0 = HillX;
            for (int k = 0; k < 8; k++) SimStep.Step(w);
            return w.X[v] - x0;
        }

        [Test]
        public void DG70AWarriorHitPushesAStandingFarmerAtLeast06AndStaggersIt()
        {
            World w = Make();
            double d = KnockTravel(w, 7, out int stun); // 7 = crimson farmer
            Assert.GreaterOrEqual(d, 0.6);
            Assert.Greater(stun, 0, "22 damage beats the Farmer's poise 15: a stagger");
        }

        [Test]
        public void DG70TheSameHitOnAWarriorMovesItAtMostHalfAsFar()
        {
            double farmer = KnockTravel(Make(), 7, out _);
            double warrior = KnockTravel(Make(), 5, out int stun);
            Assert.LessOrEqual(warrior, farmer * 0.5 + 1e-4);
            Assert.Greater(warrior, 0);
            Assert.AreEqual(0, stun, "22 is under the Warrior's poise 25");
        }

        /// <summary>A Wall cell whose two west neighbours on the same row and the row cells above / below them are walkable.</summary>
        internal static bool FindWallFace(World w, out int cx, out int cz)
        {
            MapDef m = w.Map;
            for (cz = 2; cz < m.Height - 2; cz++)
            {
                for (cx = 4; cx < m.Width - 1; cx++)
                {
                    if (m.Cells[cz * m.Width + cx] != Cell.Wall) continue;
                    bool ok = true;
                    for (int dx = 1; dx <= 3 && ok; dx++) ok = MapGrid.IsWalkable(m, cx - dx, cz);
                    if (ok) return true;
                }
            }
            cx = cz = -1;
            return false;
        }

        [Test]
        public void DG70AVictimPushedIntoAWallStopsAtTheWallAndNeverEntersABlockedCellOver600Ticks()
        {
            World w = Make(c => c.Physics.WallsBlockHits = false);
            Assert.IsTrue(FindWallFace(w, out int cx, out int cz));
            ParkOthers(w, 0, 7);
            double r = w.Config.Class(ClassId.Farmer).Radius;
            Place(w, 7, cx - r - 0.01, cz + 0.5);
            Place(w, 0, cx - r - 1.3, cz + 0.5);
            for (int k = 0; k < 600; k++)
            {
                Idle(w);
                w.Actors[7].Hp = w.Actors[7].MaxHp; // keep it alive
                Input(w, 0, w.X[7], w.Z[7], true);
                SimStep.Step(w);
                Assert.IsTrue(Collide.FootprintClear(w, 7, w.X[7], w.Z[7]), "tick " + k);
                Assert.LessOrEqual(w.X[7], cx - r + 1e-6, "tick " + k);
            }
        }

        [Test]
        public void DG70AStaggerDuringTheWindupCancelsTheSwing()
        {
            World w = Make();
            ParkOthers(w, 0, 7);
            Place(w, 0, HillX - 1.0, HillZ);
            Place(w, 7, HillX, HillZ);
            Idle(w);
            int head = w.EventHead;
            Input(w, 0, HillX, HillZ, true); // warrior presses first (wind-up 4)
            SimStep.Step(w);
            Input(w, 0, HillX, HillZ, false);
            SimStep.Step(w);
            Input(w, 7, HillX - 1.0, HillZ, true); // farmer presses 1 tick later (wind-up 3): its contact would come after the warrior's
            SimStep.Step(w);
            Input(w, 7, HillX - 1.0, HillZ, false);
            for (int k = 0; k < 6; k++) SimStep.Step(w);
            Assert.AreEqual(w.Actors[0].MaxHp, w.Actors[0].Hp, "the cancelled farmer swing did no damage");
            Assert.AreEqual(1, Events(w, SimEventType.AttackCancel, head).Count);
            Assert.AreEqual(1, Events(w, SimEventType.HitStun, head).Count);
            Assert.GreaterOrEqual(Events(w, SimEventType.Knock, head).Count, 1);
        }

        [Test]
        public void DG70HitEventsCarryDirectionImpulseAndTier()
        {
            World w = Make();
            int head = w.EventHead;
            KnockTravel(w, 7, out _);
            SimEvent hit = null;
            for (int i = head; i < w.EventHead; i++) if (w.Events[i % World.EventCapacity].Type == SimEventType.Hit) hit = w.Events[i % World.EventCapacity];
            Assert.IsNotNull(hit);
            Assert.AreEqual(1, hit.DirX, 1e-6);
            Assert.AreEqual(0, hit.DirZ, 1e-6);
            Assert.AreEqual(0.66 / 0.8, hit.Impulse, 1e-6);
            Assert.AreEqual(1, hit.Tier, "22 damage = heavy");
        }

        [Test]
        public void DG70StaggerBlocksAttackAbilityAndJumpAndReadinessSaysSo()
        {
            World w = Make();
            KnockTravel(w, 7, out int stun);
            Place(w, 7, HillX, HillZ);
            w.Actors[7].HitStunTicks = 3;
            Assert.IsFalse(Combat.AttackReady(w, 7));
            int head = w.EventHead;
            InputFrame f = SimInput.EmptyInput();
            f.Attack = true;
            f.Ability = true;
            f.Jump = true;
            f.AimX = HillX + 3;
            f.AimZ = HillZ;
            SimInput.ApplyInput(w, 7, f);
            SimStep.Step(w);
            foreach (SimEvent e in Events(w, SimEventType.AttackStart, head)) Assert.AreNotEqual(7, e.A);
            Assert.AreEqual(0, w.Actors[7].AirLen);
            Assert.AreEqual(0, w.Actors[7].AbilityCd);
        }

        [Test]
        public void DG70ReplayWithKnockbackIsIdentical()
        {
            Assert.AreEqual(MatchHash.Run(5, 16, 1800, ClassId.Farmer), MatchHash.Run(5, 16, 1800, ClassId.Farmer));
        }
        // ------------------------------------------------------------------ DG-83 walls block hits

        private static int SwingsAcrossCell(byte cellType)
        {
            World w = Make(c => c.Physics.Knockback = false);
            Assert.IsTrue(FindWallFace(w, out int cx, out int cz));
            MapDef m = w.Map;
            // A one-cell wall at (cx - 2, cz) with walkable ground both sides (the face's west cells are walkable).
            int wx = cx - 2;
            MapGrid.SetCell(w, wx, cz, cellType);
            ParkOthers(w, 0, 5);
            Place(w, 0, wx - 0.475, cz + 0.5);
            Place(w, 5, wx + 1.475, cz + 0.5);
            Assert.AreEqual(1.95, w.X[5] - w.X[0], 1e-4);
            double hp0 = w.Actors[5].Hp;
            for (int k = 0; k < 300; k++)
            {
                Idle(w);
                Place(w, 0, wx - 0.475, cz + 0.5);
                Place(w, 5, wx + 1.475, cz + 0.5);
                Input(w, 0, w.X[5], w.Z[5], true);
                SimStep.Step(w);
                if (!w.Actors[5].Alive) break;
            }
            return (int)Math.Round(hp0 - w.Actors[5].Hp);
        }

        [Test]
        public void DG83TwoEnemies195ApartAcrossAOneCellWall300TicksOfSwingsNoDamage()
        {
            Assert.AreEqual(0, SwingsAcrossCell(Cell.Wall));
            Assert.AreEqual(0, SwingsAcrossCell(Cell.BreakableIntact));
        }

        [Test]
        public void DG83TheSamePairWithFloorBetweenTakesDamage()
        {
            Assert.Greater(SwingsAcrossCell(Cell.Floor), 0);
        }

        [Test]
        public void DG83SegmentClearBlocksOnWallsAndPassesVoidAndWater()
        {
            World w = Make();
            Assert.IsTrue(FindWallFace(w, out int cx, out int cz));
            Assert.IsFalse(SegmentClear.Clear(w.Map, cx - 1.5, cz + 0.5, cx + 0.5, cz + 0.5));
            Assert.IsTrue(SegmentClear.Clear(w.Map, cx - 1.5, cz + 0.5, cx + 0.5, cz + 0.5, cx, cz), "the target cell is skipped");
            MapGrid.SetCell(w, cx - 2, cz, Cell.Water);
            Assert.IsTrue(SegmentClear.Clear(w.Map, cx - 2.5, cz + 0.5, cx - 1.5, cz + 0.5));
            Assert.IsTrue(SegmentClear.Blocks(Cell.Barricade));
            Assert.IsFalse(SegmentClear.Blocks(Cell.Void));
        }
        // ------------------------------------------------------------------ DG-84 Kaiju claw you can dodge

        /// <summary>Hatch now, make the Kaiju berserk (hostile to all) on the hill with no slam or new swing of its own.</summary>
        internal static Kaiju HatchedKaiju(World w)
        {
            w.ForceHatch = true;
            Idle(w);
            SimStep.Step(w);
            Assert.IsTrue(w.Match.Hatched);
            Kaiju k = w.Kaiju;
            k.State = KaijuState.Berserk;
            k.X = HillX;
            k.Z = HillZ;
            k.PrevX = HillX;
            k.PrevZ = HillZ;
            k.SlamCd = 1000;
            k.MeleeCd = 1000;
            return k;
        }

        private static void LockClaw(World w, double facing)
        {
            Kaiju k = w.Kaiju;
            k.MeleeWindupIn = w.Dur.KaijuClawWindup;
            k.MeleeWindupLen = w.Dur.KaijuClawWindup;
            k.ClawX = k.X;
            k.ClawZ = k.Z;
            k.ClawFacing = facing;
            k.Facing = facing;
            k.MeleeTargetId = 5;
        }

        [Test]
        public void DG84ATargetThatLeavesTheConeDuringTheWindupTakesNothingASecondHostileInTheConeIsHit()
        {
            World w = Make();
            Kaiju k = HatchedKaiju(w);
            ParkOthers(w, 5, 6);
            Place(w, 5, HillX + 2, HillZ);
            Place(w, 6, HillX + 1.6, HillZ + 0.8);
            LockClaw(w, 0);
            double hp5 = w.Actors[5].Hp, hp6 = w.Actors[6].Hp;
            int windup = k.MeleeWindupIn;
            for (int t = 0; t < windup + 1; t++)
            {
                Idle(w);
                if (t < windup - 2) Place(w, 5, HillX - 2, HillZ); // the locked target circles behind the Kaiju
                SimStep.Step(w);
            }
            Assert.AreEqual(hp5, w.Actors[5].Hp, "behind the Kaiju: out of the shown cone");
            Assert.Less(w.Actors[6].Hp, hp6, "the second hostile in the cone is hit");
            Assert.Greater(w.X[6], HillX + 1.6, "and pushed back");
        }

        [Test]
        public void DG84ClawWindupIsAtLeast045SecondsOnScreenAtTheDefaultPaceAndAtPace1()
        {
            GameConfig c = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c });
            Assert.GreaterOrEqual(Durations.OnScreen(c, w.Dur.KaijuClawWindup, w.Dur.Pace), 0.45 - 1e-9);
            World p1 = Make(null, pace: 1);
            Assert.GreaterOrEqual(Durations.OnScreen(p1.Config, p1.Dur.KaijuClawWindup, 1), 0.45 - 1e-9);
            World classic = Make(cc => cc.Physics.Classic = true, pace: 2);
            Assert.AreEqual(11, classic.Dur.KaijuClawWindup, "Classic keeps the browser's 11 ticks");
        }

        [Test]
        public void DG84TheWindupEventCarriesTheCone()
        {
            World w = Make();
            Kaiju k = HatchedKaiju(w);
            ParkOthers(w, 5);
            Place(w, 5, HillX + 2, HillZ);
            k.MeleeCd = 0;
            k.TargetId = 5;
            w.KaijuNav.RetargetIn = 100;
            int head = w.EventHead;
            Idle(w);
            SimStep.Step(w);
            SimEvent e = null;
            for (int i = head; i < w.EventHead; i++) if (w.Events[i % World.EventCapacity].Type == SimEventType.KaijuMeleeWindup) e = w.Events[i % World.EventCapacity];
            Assert.IsNotNull(e, "the claw started");
            Assert.AreEqual(w.Dur.KaijuClawWindup, e.Windup);
            Assert.AreEqual(w.Config.Kaiju.MeleeRange + Kaiju.MeleeLeash, e.Radius, 1e-9);
            Assert.AreEqual(1, e.DirX, 1e-6);
            Assert.AreEqual(k.X, k.ClawX);
        }
        // ------------------------------------------------------------------ DG-85 Kaiju slam shockwave

        private static void SlamNow(World w, double x, double z)
        {
            Kaiju k = w.Kaiju;
            k.TelegraphX = x;
            k.TelegraphZ = z;
            k.TelegraphIn = 0.5 / w.Config.SimHz;
            Idle(w);
            SimStep.Step(w);
            Assert.AreEqual(0, k.TelegraphIn);
        }

        [Test]
        public void DG85FullDamageAtTheCentreLessAtTheEdgeAndMorePushAtTheCentre()
        {
            World w = Make(c => c.Physics.SolidKaiju = false); // d = 1 is inside the solid body: the list's check runs without it
            HatchedKaiju(w);
            ParkOthers(w, 5, 6);
            Place(w, 5, HillX + 1, HillZ);
            Place(w, 6, HillX - 4.5, HillZ);
            double h5 = w.Actors[5].Hp, h6 = w.Actors[6].Hp;
            SlamNow(w, HillX, HillZ);
            Assert.AreEqual(45, h5 - w.Actors[5].Hp, 1e-9, "d = 1");
            Assert.AreEqual(45 * 0.38, w.Config.Kaiju.SlamDamage * w.Config.Physics.SlamEdgeDamageShare, 1e-9);
            Assert.LessOrEqual(h6 - w.Actors[6].Hp, 22, "d = 4.5");
            Assert.Greater(h6 - w.Actors[6].Hp, 0);
            for (int t = 0; t < 30; t++) { Idle(w); SimStep.Step(w); }
            double push5 = w.X[5] - (HillX + 1);
            double push6 = (HillX - 4.5) - w.X[6];
            Assert.Greater(push5, push6, "outward push at d = 1 beats d = 4.5");
            Assert.Greater(push6, 0);
        }

        [Test]
        public void DG85WithTheSolidBodyAFighterTouchingItTakesTheFullSlam()
        {
            World w = Make();
            HatchedKaiju(w);
            ParkOthers(w, 2);
            Place(w, 2, HillX + 1.7, HillZ);
            double h = w.Actors[2].Hp;
            SlamNow(w, HillX, HillZ);
            Assert.AreEqual(45, h - w.Actors[2].Hp, 1e-9);
        }

        [Test]
        public void DG85BehindAWallAndInTheAirTakeNothing()
        {
            World w = Make();
            HatchedKaiju(w);
            Assert.IsTrue(FindWallFace(w, out int cx, out int cz));
            MapGrid.SetCell(w, cx - 2, cz, Cell.Wall);
            ParkOthers(w, 5, 6);
            Place(w, 5, cx - 0.5, cz + 0.5); // behind the wall cell from the slam point
            Place(w, 6, cx - 2.5, cz - 1.5);
            Actor air = w.Actors[6];
            air.AirLen = 12;
            air.AirTick = 2;
            air.AirFromX = air.AirToX = w.X[6];
            air.AirFromZ = air.AirToZ = w.Z[6];
            w.Kaiju.X = cx - 2.5;
            w.Kaiju.Z = cz + 0.5;
            double h5 = w.Actors[5].Hp, h6 = air.Hp;
            SlamNow(w, cx - 2.5, cz + 0.5);
            Assert.AreEqual(h5, w.Actors[5].Hp, "behind a wall");
            Assert.AreEqual(h6, air.Hp, "airborne");
        }

        [Test]
        public void DG85TelegraphIsAtLeast08SecondsOnScreen()
        {
            GameConfig c = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c });
            Assert.GreaterOrEqual(Durations.OnScreen(c, w.Dur.SlamTelegraph, w.Dur.Pace), 0.8 - 1e-9);
            World fast = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c, Pace = 2 });
            Assert.GreaterOrEqual(Durations.OnScreen(c, fast.Dur.SlamTelegraph, 2), 0.8 - 1e-9);
            Assert.AreEqual(36, Make().Dur.SlamTelegraph, "pace 1: the 1.2 s sim telegraph is already longer");
        }
        // ------------------------------------------------------------------ DG-86 the Kaiju is solid

        [Test]
        public void DG86AnActorPlaced05FromTheKaijuCentreIsOutsideItsBodyAfterOneTick()
        {
            World w = Make();
            Kaiju k = HatchedKaiju(w);
            ParkOthers(w, 2);
            Place(w, 2, HillX + 0.5, HillZ);
            int head = w.EventHead;
            Idle(w);
            SimStep.Step(w);
            double d = Math.Sqrt((w.X[2] - k.X) * (w.X[2] - k.X) + (w.Z[2] - k.Z) * (w.Z[2] - k.Z));
            Assert.GreaterOrEqual(d, w.Config.Physics.KaijuBodyRadius + w.Config.Class(ClassId.Farmer).Radius - 1e-3);
            Assert.AreEqual(1, Events(w, SimEventType.BodyShove, head).Count);
            Assert.AreEqual(w.Actors[2].MaxHp, w.Actors[2].Hp, "no trample damage");
        }

        [Test]
        public void U074AnActorStandingWhereTheKaijuHatchesIsOutsideItsBodyOnTheHatchTick()
        {
            World probe = Make();
            probe.ForceHatch = true;
            SimStep.Step(probe);
            double hx = probe.Kaiju.X, hz = probe.Kaiju.Z;
            World w = Make();
            ParkOthers(w, 2);
            Place(w, 2, hx + 0.3, hz);
            Idle(w);
            w.ForceHatch = true;
            SimStep.Step(w); // the hatching step itself
            Assert.IsTrue(w.Match.Hatched);
            Kaiju k = w.Kaiju;
            double d = Math.Sqrt((w.X[2] - k.X) * (w.X[2] - k.X) + (w.Z[2] - k.Z) * (w.Z[2] - k.Z));
            Assert.GreaterOrEqual(d, w.Config.Physics.KaijuBodyRadius - 1e-6, "pushed out on the hatch tick, not one tick later");
        }

        /// <summary>True when the Kaiju's wall collider circle overlaps no blocked cell.</summary>
        internal static bool KaijuClearOfWalls(World w)
        {
            Kaiju k = w.Kaiju;
            double r = Kaiju.GridR(w) - 1e-3;
            for (int cz = JsMath.FloorToInt(k.Z - r); cz <= JsMath.FloorToInt(k.Z + r); cz++)
            {
                for (int cx = JsMath.FloorToInt(k.X - r); cx <= JsMath.FloorToInt(k.X + r); cx++)
                {
                    // The list's check: Wall cells (a barricade dropped or a cell dug beside a standing Kaiju is a separate rule).
                    if (!MapGrid.InBounds(w.Map, cx, cz) || w.Map.Cells[cz * w.Map.Width + cx] != Cell.Wall) continue;
                    double nx = Math.Max(cx, Math.Min(k.X, cx + 1));
                    double nz = Math.Max(cz, Math.Min(k.Z, cz + 1));
                    if ((k.X - nx) * (k.X - nx) + (k.Z - nz) * (k.Z - nz) < r * r) return false;
                }
            }
            return true;
        }

        [Test]
        public void DG86Soak1800TicksNoLivingActorInsideTheKaijuBodyAndItsColliderNeverOverAWall()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            World w = World.CreateWorld(new WorldOptions { Seed = 21, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            w.ForceHatch = true;
            MatchTick.Advance(w, null, scratch);
            w.Kaiju.State = KaijuState.Berserk;
            double body = cfg.Physics.KaijuBodyRadius;
            int worst = 0;
            for (int t = 0; t < 1800 && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                Kaiju k = w.Kaiju;
                for (int i = 0; i < w.ActorCount; i++)
                {
                    if (!w.Actors[i].Alive || w.Actors[i].AirLen > 0) continue;
                    double dx = w.X[i] - k.X, dz = w.Z[i] - k.Z;
                    if (dx * dx + dz * dz < body * body)
                    {
                        worst++;
                        Console.WriteLine("inside: t " + t + " id " + i + " d " + Math.Sqrt(dx * dx + dz * dz) + " standUp " + w.Actors[i].StandUp + " k " + k.X + "," + k.Z + " a " + w.X[i] + "," + w.Z[i] + " ko " + k.KoIn);
                    }
                }
                Assert.IsTrue(KaijuClearOfWalls(w), "tick " + t + " kaiju at " + k.X + "," + k.Z);
            }
            Assert.AreEqual(0, worst, "living actor centres inside the Kaiju body");
        }
        // ------------------------------------------------------------------ DG-97 crowds that settle

        private static World Crowd(Action<GameConfig> tweak = null)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            tweak?.Invoke(cfg);
            World w = World.CreateWorld(new WorldOptions { Seed = 9, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            for (int i = 0; i < w.ActorCount; i++)
            {
                double a = i * 2.39996, rad = 2.0 * Math.Sqrt((i + 0.5) / w.ActorCount);
                Place(w, i, HillX + rad * Math.Cos(a), HillZ + rad * Math.Sin(a));
                w.Actors[i].SpawnProtect = 1000; // no fights
            }
            return w;
        }

        private static double WorstOverlap(World w)
        {
            double worst = 0;
            for (int i = 0; i < w.ActorCount; i++)
                for (int j = i + 1; j < w.ActorCount; j++)
                {
                    double rr = Collide.ActorRadius(w, i) + Collide.ActorRadius(w, j);
                    double d = Math.Sqrt((w.X[i] - w.X[j]) * (w.X[i] - w.X[j]) + (w.Z[i] - w.Z[j]) * (w.Z[i] - w.Z[j]));
                    worst = Math.Max(worst, rr - d);
                }
            return worst;
        }

        [Test]
        public void DG97ThirtyTwoActorsInA4UnitDiscSettleUnder005OverlapIn30TicksWithNoSeparationMoveOver02()
        {
            World w = Crowd();
            double start = WorstOverlap(w);
            Assert.Greater(start, 0.3, "the disc starts crowded");
            double biggest = 0;
            for (int t = 0; t < 30; t++)
            {
                Idle(w);
                SimStep.Step(w);
                for (int i = 0; i < w.ActorCount; i++)
                    biggest = Math.Max(biggest, Math.Sqrt((w.X[i] - w.PrevX[i]) * (w.X[i] - w.PrevX[i]) + (w.Z[i] - w.PrevZ[i]) * (w.Z[i] - w.PrevZ[i])));
            }
            Console.WriteLine("{\"item\":\"DG-97\",\"startOverlap\":" + start.ToString("0.000") + ",\"after30\":" + WorstOverlap(w).ToString("0.0000") + ",\"biggestStep\":" + biggest.ToString("0.000") + "}");
            Assert.Less(WorstOverlap(w), 0.05);
            Assert.LessOrEqual(biggest, 0.2);
        }

        [Test]
        public void DG97ShufflingWhichIdStandsWhereGivesTheSamePositions()
        {
            World a = Crowd();
            World b = Crowd();
            // Swap the spots of same-team, same-class pairs in b: the physics is identical, only the id order differs.
            var swapped = new int[a.ActorCount];
            for (int i = 0; i < a.ActorCount; i++) swapped[i] = i;
            for (int i = 0; i < a.ActorCount; i++)
            {
                if (swapped[i] != i) continue;
                for (int j = a.ActorCount - 1; j > i; j--)
                {
                    if (swapped[j] != j || a.Actors[j].Team != a.Actors[i].Team || a.Actors[j].Cls != a.Actors[i].Cls) continue;
                    swapped[i] = j;
                    swapped[j] = i;
                    break;
                }
            }
            for (int i = 0; i < a.ActorCount; i++) Place(b, i, a.X[swapped[i]], a.Z[swapped[i]]);
            for (int i = 0; i < b.ActorCount; i++) b.Actors[i].SpawnProtect = 1000;
            for (int t = 0; t < 30; t++)
            {
                Idle(a);
                Idle(b);
                SimStep.Step(a);
                SimStep.Step(b);
            }
            for (int i = 0; i < a.ActorCount; i++)
            {
                Assert.AreEqual(a.X[swapped[i]], b.X[i], 1e-4, "x of " + i);
                Assert.AreEqual(a.Z[swapped[i]], b.Z[i], 1e-4, "z of " + i);
            }
        }

        [Test]
        public void DG97AnActorClippingALoneWallCornerBy01SlidesPastItAndOneOverlapping05Stops()
        {
            foreach (double clip in new[] { 0.1, 0.5 })
            {
                World w = Make();
                ParkOthers(w, 2);
                int cx = (int)HillX + 3, cz = (int)HillZ;
                MapGrid.SetCell(w, cx, cz, Cell.Wall);
                double r = w.Config.Class(ClassId.Farmer).Radius;
                Place(w, 2, cx - 3, cz - r + clip);
                double x0 = w.X[2];
                int passed = -1;
                for (int t = 0; t < 60; t++)
                {
                    Input(w, 2, double.NaN, double.NaN, false, 1, 0);
                    SimStep.Step(w);
                    if (passed < 0 && w.X[2] - r > cx + 1) passed = t;
                }
                double free = w.Config.Class(ClassId.Farmer).MoveSpeed * 60 / w.Config.SimHz;
                if (clip < 0.25)
                {
                    Assert.GreaterOrEqual(passed, 0, "it passed the corner");
                    Assert.Greater(w.X[2] - x0, 0.75 * free * 0.9, "little x progress lost");
                }
                else Assert.Less(w.X[2] + r, cx + 1e-3, "a 0.5 overlap still stops");
            }
        }
        // ------------------------------------------------------------------ DG-98 blasts carry force

        private static Bomb DropTnt(World w, int slot, int owner, double x, double z, int blastTick)
        {
            Bomb b = w.Demolitions.Bombs[slot];
            b.Active = true;
            b.Owner = owner;
            b.Team = w.Actors[owner].Team;
            b.X = x;
            b.Z = z;
            b.FromX = x;
            b.FromZ = z;
            b.ThrownTick = w.Tick;
            b.LandTick = w.Tick;
            b.Landed = true;
            b.BlastTick = blastTick;
            return b;
        }

        [Test]
        public void DG98CentreVictimFullDamageEdgeVictim35PercentPushedOutward()
        {
            World w = Make();
            ParkOthers(w, 0, 5, 6);
            Place(w, 0, HillX - 6, HillZ);
            Place(w, 5, HillX, HillZ);
            Place(w, 6, HillX, HillZ + 2.0);
            double h5 = w.Actors[5].Hp, h6 = w.Actors[6].Hp;
            DropTnt(w, 0, 0, HillX, HillZ, w.Tick + 1);
            Idle(w);
            SimStep.Step(w);
            double dmg = w.Config.Abilities.DemolitionsTnt.Damage;
            Assert.AreEqual(dmg, h5 - w.Actors[5].Hp, 1e-9, "centre");
            Assert.AreEqual(dmg * 0.35, h6 - w.Actors[6].Hp, 1e-6, "edge");
            for (int t = 0; t < 20; t++) { Idle(w); SimStep.Step(w); }
            Assert.Greater(w.Z[6], HillZ + 2.0, "thrown outward");
        }

        [Test]
        public void DG98BehindAWallTakesNothingAndAirborneIsPushedButNotHurt()
        {
            World w = Make();
            Assert.IsTrue(FindWallFace(w, out int cx, out int cz));
            MapGrid.SetCell(w, cx - 2, cz, Cell.Wall);
            ParkOthers(w, 0, 5, 6);
            Place(w, 5, cx - 0.5, cz + 0.5);
            Place(w, 6, cx - 2.5, cz - 0.6);
            Actor air = w.Actors[6];
            air.AirLen = 12;
            air.AirTick = 2;
            air.AirFromX = air.AirToX = w.X[6];
            air.AirFromZ = air.AirToZ = w.Z[6];
            double h5 = w.Actors[5].Hp, h6 = air.Hp;
            DropTnt(w, 0, 0, cx - 2.5, cz + 0.5, w.Tick + 1);
            Idle(w);
            SimStep.Step(w);
            Assert.AreEqual(h5, w.Actors[5].Hp, "behind a wall");
            Assert.AreEqual(h6, air.Hp, "airborne: no damage");
            Assert.AreNotEqual(0, air.KVz, "airborne: pushed");
        }

        [Test]
        public void DG98TwoTntOneAndAHalfApartDetonateOneTickApart()
        {
            World w = Make();
            ParkOthers(w, 0);
            Place(w, 0, HillX - 8, HillZ);
            int head = w.EventHead;
            DropTnt(w, 0, 0, HillX, HillZ, w.Tick + 1);
            DropTnt(w, 1, 0, HillX + 1.5, HillZ, w.Tick + 40);
            var ticks = new List<int>();
            for (int t = 0; t < 5; t++)
            {
                int h = w.EventHead;
                Idle(w);
                SimStep.Step(w);
                if (Events(w, SimEventType.Explosion, h).Count > 0) ticks.Add(w.Tick);
            }
            Assert.AreEqual(2, ticks.Count);
            Assert.AreEqual(ticks[0] + 1, ticks[1]);
        }
        // ------------------------------------------------------------------ DG-99 pillars land when they land

        [Test]
        public void DG99CellsChangeOnLandingAndAnActorUnderABlockIsCrushedAndPushedClearNeverIntoAWall()
        {
            World w = Make();
            Assert.IsNotNull(w.Map.Decorations);
            int fall = Durations.T30(w.Config, w.Config.Abilities.Topple.FallTicks);
            var buf = new int[Topple.FootprintMax(w.Config)];
            for (int k = 0; k < w.Map.Decorations.Length; k++)
            {
                World t = Make();
                DecorationDef d = t.Map.Decorations[k];
                int n = Topple.Footprint(t.Config, t.Map, d, Topple.Dir(d, Topple.Block), buf);
                int cell = -1;
                for (int j = 0; j < n; j++) if (Topple.ToppledType(t.Map.Cells[buf[j]], Topple.Block) == Cell.Wall && MapGrid.IsWalkableType(t.Map.Cells[buf[j]])) { cell = buf[j]; break; }
                if (cell < 0) continue;
                ParkOthers(t, 2);
                int cx = cell % t.Map.Width, cz = cell / t.Map.Width;
                Place(t, 2, cx + 0.5, cz + 0.5);
                byte before = t.Map.Cells[cell];
                int res = Topple.ToppleDecoration(t, k, Topple.Block, -1);
                if (res != Topple.Block) continue; // the route guard refused this piece's block: try the next
                int fallTick = t.Tick;
                double hp0 = t.Actors[2].Hp;
                while (t.Tick < fallTick + fall - 1)
                {
                    Idle(t);
                    Place(t, 2, cx + 0.5, cz + 0.5);
                    SimStep.Step(t);
                }
                Assert.AreEqual(before, t.Map.Cells[cell], "unchanged at FallTick + FallTicks - 1");
                Idle(t);
                SimStep.Step(t);
                Assert.AreEqual(Cell.Wall, t.Map.Cells[cell], "changed at FallTick + FallTicks");
                Assert.AreEqual(hp0 - t.Config.Physics.PillarCrushDamage, t.Actors[2].Hp, 1e-9, "crush damage");
                Assert.IsTrue(Collide.FootprintClear(t, 2, t.X[2], t.Z[2]), "outside the strip, never in a wall");
                for (int s = 0; s < 30; s++) { Idle(t); SimStep.Step(t); Assert.IsTrue(Collide.FootprintClear(t, 2, t.X[2], t.Z[2])); }
                return;
            }
            Assert.Fail("no decoration could be toppled as a block onto a standing actor");
        }

        [Test]
        public void DG99TheFinishingSwingIsNotRefusedByPeopleStandingThere()
        {
            World w = Make();
            var buf = new int[Topple.FootprintMax(w.Config)];
            int accepted = 0;
            for (int k = 0; k < w.Map.Decorations.Length; k++)
            {
                World t = Make();
                DecorationDef d = t.Map.Decorations[k];
                int n = Topple.Footprint(t.Config, t.Map, d, Topple.Dir(d, Topple.Block), buf);
                for (int j = 0; j < n; j++)
                {
                    if (!MapGrid.IsWalkableType(t.Map.Cells[buf[j]])) continue;
                    ParkOthers(t, 2);
                    Place(t, 2, buf[j] % t.Map.Width + 0.5, buf[j] / t.Map.Width + 0.5);
                    break;
                }
                int res = Topple.ToppleDecoration(t, k, Topple.Block, -1);
                Assert.AreNotEqual(Topple.RefusedOccupied, res);
                if (res == Topple.Block) accepted++;
            }
            Assert.Greater(accepted, 0);
        }
        // ------------------------------------------------------------------ DG-100 sim polish

        private static bool SwingHitsKaijuAt(double offDeg)
        {
            World w = Make(c => c.Physics.Knockback = false);
            Kaiju k = HatchedKaiju(w);
            ParkOthers(w, 2);
            double a = offDeg * Math.PI / 180;
            // Farmer 2 stands 2 u from the Kaiju centre, facing +x; the Kaiju sits offDeg off that facing.
            Place(w, 2, HillX - 2 * Math.Cos(a), HillZ - 2 * Math.Sin(a));
            w.Actors[2].Facing = 0;
            double hp = k.Hp;
            Idle(w);
            Input(w, 2, w.X[2] + 5, w.Z[2], true);
            for (int t = 0; t < 6; t++) { SimStep.Step(w); Input(w, 2, w.X[2] + 5, w.Z[2], false); }
            return k.Hp < hp;
        }

        [Test]
        public void DG100ASwingAtTheKaijusFlankConnectsAt70DegreesAndMissesAt120()
        {
            Assert.IsTrue(SwingHitsKaijuAt(70));
            Assert.IsFalse(SwingHitsKaijuAt(120));
        }

        [Test]
        public void DG100ARevertedLandingResetsPrevX()
        {
            World w = Make();
            ParkOthers(w, 2);
            Place(w, 2, HillX, HillZ);
            Actor a = w.Actors[2];
            a.AirLen = 5;
            a.AirTick = 5;
            a.AirFromX = HillX - 2;
            a.AirFromZ = HillZ;
            w.X[2] = -5; // the landing spot is off the map: the guard reverts
            Jump.LandJump(w, 2);
            Assert.AreEqual(HillX - 2, w.X[2]);
            Assert.AreEqual(w.X[2], w.PrevX[2]);
            Assert.AreEqual(w.Z[2], w.PrevZ[2]);
        }

        [Test]
        public void DG100TwoDeathsOnOnePointGiveTwoOrbsAtLeast05ApartOnWalkableCells()
        {
            World w = Make();
            ParkOthers(w, 5, 6);
            Place(w, 5, HillX, HillZ);
            Place(w, 6, HillX, HillZ);
            Damage.ApplyDamage(w, 5, 1000, -1, HillX, HillZ);
            Damage.ApplyDamage(w, 6, 1000, -1, HillX, HillZ);
            Combat.DropCorpseMeat(w);
            var orbs = new List<LooseOrb>();
            foreach (LooseOrb o in w.Orbs) if (o.Active) orbs.Add(o);
            Assert.AreEqual(2, orbs.Count);
            double d = Math.Sqrt((orbs[0].X - orbs[1].X) * (orbs[0].X - orbs[1].X) + (orbs[0].Z - orbs[1].Z) * (orbs[0].Z - orbs[1].Z));
            Assert.GreaterOrEqual(d, 0.5);
            foreach (LooseOrb o in orbs) Assert.IsTrue(MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(o.X), JsMath.FloorToInt(o.Z)));
        }

        [Test]
        public void DG100KaijuPositionsAreFloat32()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            World w = World.CreateWorld(new WorldOptions { Seed = 4, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            w.ForceHatch = true;
            for (int t = 0; t < 400; t++)
            {
                MatchTick.Advance(w, null, scratch);
                if (w.Kaiju.State == KaijuState.Egg) continue;
                Assert.AreEqual(JsMath.F32(w.Kaiju.X), w.Kaiju.X, "tick " + t);
                Assert.AreEqual(JsMath.F32(w.Kaiju.Z), w.Kaiju.Z, "tick " + t);
            }
        }
    }
}

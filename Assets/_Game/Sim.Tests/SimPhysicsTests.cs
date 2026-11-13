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

        internal static World Make(Action<GameConfig> tweak = null, TeamId human = TeamId.Azure, double pace = 0, int seed = 7)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
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
            var rx = new Regex(@"\.(" + string.Join("|", names) + @")\b");
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
            World w = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c });
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

        // The checked-in on-screen seconds (pace 2) of the windows later items change.
        private const double TableClaw = 0.183;
        private const double TableSlam = 0.6;
        private const double TableWarriorWindup = 0;
        private const double TableFarmerWindup = 0;

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
    }
}

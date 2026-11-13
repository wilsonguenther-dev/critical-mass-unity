using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
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
    }
}

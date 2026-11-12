using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of the respawn / class-change cases of src/sim/combat.test.ts lines 282-375 (09e0904).</summary>
    [TestFixture]
    public sealed class CombatRespawnTests
    {
        /// <summary>5v5, human azure warrior, seed 7 (browser OPTS).</summary>
        private static World NewWorld()
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = 7,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            });
        }

        /// <summary>Browser RESPAWN_STEPS.</summary>
        private static int RespawnSteps(GameConfig c)
        {
            return (int)JsMath.Round(c.Respawn.DelaySeconds * c.SimHz);
        }

        /// <summary>Teleport an actor (no interpolation streak) and drop its spawn protection.</summary>
        private static void Place(World w, int id, double x, double z)
        {
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
            w.PrevX[id] = JsMath.F32(x);
            w.PrevZ[id] = JsMath.F32(z);
            w.Actors[id].SpawnProtect = 0;
        }

        private static void StepN(World w, int n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
        }

        [Test]
        public void RespawnsAfter5SOfStepsAtItsOwnPadWithFullHpAndSpawnProtection()
        {
            World w = NewWorld();
            GameConfig cfg = w.Config;
            Place(w, 5, 60, 40);
            Damage.ApplyDamage(w, 5, 1000, 0, 58, 40);
            StepN(w, RespawnSteps(cfg) - 1);
            Assert.AreEqual(false, w.Actors[5].Alive);
            int head = w.EventHead;
            SimStep.Step(w);
            Actor a = w.Actors[5];
            Assert.AreEqual(true, a.Alive);
            Assert.AreEqual(a.MaxHp, a.Hp);
            Assert.AreEqual(cfg.Class(ClassId.Warrior).MaxHp, a.Hp);
            Assert.AreEqual(cfg.Respawn.ProtectionSeconds, a.SpawnProtect);
            Assert.AreEqual(CarryKind.None, a.Carry);
            PadDef pad = w.Map.Pads[(int)TeamId.Crimson];
            double dx = w.X[5] - pad.X;
            double dz = w.Z[5] - pad.Z;
            Assert.LessOrEqual(Math.Sqrt(dx * dx + dz * dz), pad.R);
            Assert.AreEqual(w.X[5], w.PrevX[5], 1e-4);
            Assert.AreEqual(w.Z[5], w.PrevZ[5], 1e-4);
            int respawns = 0;
            for (int i = head; i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == SimEventType.Respawn && e.A == 5) respawns++;
            }
            Assert.AreEqual(1, respawns);
        }

        [Test]
        public void RespawnedActorsTakeFreeSpawnSlotsNoTwoRespawnsOnTheSamePoint()
        {
            World w = NewWorld();
            for (int id = 5; id < 10; id++)
            {
                Place(w, id, 60 + (id - 5) * 0.2, 36 + (id - 5) * 1.0);
                Damage.ApplyDamage(w, id, 1000, -1, 56, 30);
            }
            StepN(w, RespawnSteps(w.Config));
            for (int i = 5; i < 10; i++)
            {
                Assert.AreEqual(true, w.Actors[i].Alive);
                for (int j = i + 1; j < 10; j++)
                {
                    double dx = w.X[i] - w.X[j];
                    double dz = w.Z[i] - w.Z[j];
                    Assert.Greater(Math.Sqrt(dx * dx + dz * dz), 0.5);
                }
            }
        }

        [Test]
        public void RequestClassChangeWhileDeadGivesRespawnsAsTheNewClassWithThatClassSMaxHp()
        {
            World w = NewWorld();
            GameConfig cfg = w.Config;
            Place(w, 5, 60, 40);
            Damage.ApplyDamage(w, 5, 1000, 0, 58, 40);
            Assert.AreEqual(true, Respawn.RequestClassChange(w, 5, ClassId.Farmer));
            Assert.AreEqual((ClassId?)ClassId.Farmer, w.Actors[5].PendingClass);
            StepN(w, RespawnSteps(cfg));
            Actor a = w.Actors[5];
            Assert.AreEqual(true, a.Alive);
            Assert.AreEqual(ClassId.Farmer, a.Cls);
            Assert.AreEqual(cfg.Class(ClassId.Farmer).MaxHp, a.MaxHp);
            Assert.AreEqual(cfg.Class(ClassId.Farmer).MaxHp, a.Hp);
            Assert.IsNull(a.PendingClass);
        }

        [Test]
        public void RequestClassChangeWhileAliveIsIgnored()
        {
            World w = NewWorld();
            Assert.AreEqual(false, Respawn.RequestClassChange(w, 5, ClassId.Miner));
            Assert.IsNull(w.Actors[5].PendingClass);
            SimStep.Step(w);
            Assert.AreEqual(ClassId.Warrior, w.Actors[5].Cls);
        }

        [Test]
        public void ClassPickThroughApplyInputOnADeadActorItSetsPendingClassOnALivingActorItIsIgnored()
        {
            World w = NewWorld();
            GameConfig cfg = w.Config;
            Place(w, 5, 60, 40);
            Damage.ApplyDamage(w, 5, 1000, 0, 58, 40);
            InputFrame dead = SimInput.EmptyInput();
            dead.ClassPick = 1; // ClassIds.All[1] = farmer
            SimInput.ApplyInput(w, 5, dead);
            SimStep.Step(w);
            Assert.AreEqual((ClassId?)ClassId.Farmer, w.Actors[5].PendingClass);

            InputFrame living = SimInput.EmptyInput();
            living.ClassPick = 2; // miner
            SimInput.ApplyInput(w, 6, living);
            SimStep.Step(w);
            Assert.AreEqual(true, w.Actors[6].Alive);
            Assert.IsNull(w.Actors[6].PendingClass);
            // The ignored pick is consumed: dying later does not resurrect it.
            Place(w, 6, 62, 40);
            Damage.ApplyDamage(w, 6, 1000, 0, 61, 40);
            StepN(w, RespawnSteps(cfg));
            Assert.AreEqual(true, w.Actors[6].Alive);
            Assert.AreEqual(ClassId.Warrior, w.Actors[6].Cls);

            StepN(w, RespawnSteps(cfg));
            Assert.AreEqual(true, w.Actors[5].Alive);
            Assert.AreEqual(ClassId.Farmer, w.Actors[5].Cls);
            Assert.AreEqual(cfg.Class(ClassId.Farmer).MaxHp, w.Actors[5].MaxHp);
        }
    }
}

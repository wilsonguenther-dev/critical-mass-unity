using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of the CM-28 death clarity cases of src/sim/clarity.test.ts lines 60-140 (09e0904).</summary>
    [TestFixture]
    public sealed class ClarityDeathTests
    {
        private static World NewWorld(ClassId cls = ClassId.Warrior, int seed = 11)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = seed,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = cls,
                Config = GameConfig.CreateBrowser5v5(),
            });
        }

        /// <summary>Browser DT.</summary>
        private static double Dt(GameConfig c)
        {
            return 1.0 / c.SimHz;
        }

        private static void Place(World w, int id, double x, double z)
        {
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
            w.PrevX[id] = JsMath.F32(x);
            w.PrevZ[id] = JsMath.F32(z);
            w.Actors[id].SpawnProtect = 0;
        }

        /// <summary>Copies of every event of type still in the ring, oldest first.</summary>
        private static List<SimEvent> Events(World w, SimEventType type)
        {
            var output = new List<SimEvent>();
            for (int h = Math.Max(0, w.EventHead - World.EventCapacity); h < w.EventHead; h++)
            {
                SimEvent e = w.Events[h % World.EventCapacity];
                if (e.Type == type)
                {
                    output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value });
                }
            }
            return output;
        }

        /// <summary>A crimson actor id (5..9 in a 5v5 with the human on azure).</summary>
        private static int Crimson(World w, ClassId cls)
        {
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Team == TeamId.Crimson && w.Actors[i].Cls == cls) return i;
            throw new InvalidOperationException("no crimson " + cls);
        }

        [Test]
        public void LastAttackerHoldsTheKillingActorSIdWhileTheRespawnCardShowsAndIsClearedAtRespawn()
        {
            World w = NewWorld();
            GameConfig cfg = w.Config;
            double dt = Dt(cfg);
            int killer = Crimson(w, ClassId.Warrior);
            Place(w, 0, 30, 30);
            Place(w, killer, 31, 30);
            Damage.ApplyDamage(w, 0, 9999, killer, w.X[killer], w.Z[killer]);
            Actor a = w.Actors[0];
            Assert.AreEqual(false, a.Alive);
            Assert.AreEqual(killer, a.LastAttacker);
            List<SimEvent> death = Events(w, SimEventType.Death);
            Assert.AreEqual(1, death.Count);
            Assert.AreEqual(killer, death[0].B);
            // Still the killer on every step of the respawn delay (the card reads it).
            int delayTicks = (int)JsMath.Round(cfg.Respawn.DelaySeconds * cfg.SimHz);
            for (int t = 0; t < delayTicks - 2; t++)
            {
                SimStep.Step(w, dt);
                Assert.AreEqual(false, a.Alive);
                Assert.AreEqual(killer, a.LastAttacker);
            }
            for (int t = 0; t < 6 && !a.Alive; t++) SimStep.Step(w, dt);
            Assert.AreEqual(true, a.Alive);
            Assert.AreEqual(-1, a.LastAttacker);
        }

        [Test]
        public void LastAttackerKeepsKAIJUSOURCE2ForAKaijuMeleeKillAnd1ForASourcelessKill()
        {
            World w = NewWorld();
            Place(w, 0, 30, 30);
            Damage.ApplyDamage(w, 0, 9999, Damage.KaijuSource, 32, 30);
            Assert.AreEqual(Damage.KaijuSource, w.Actors[0].LastAttacker);
            World v = NewWorld();
            Place(v, 0, 30, 30);
            Damage.ApplyDamage(v, 0, 9999, -1, 30, 30);
            Assert.AreEqual(-1, v.Actors[0].LastAttacker);
        }

        [Test]
        public void LostCargoRecordsKindAndAmountOfTheCargoDestroyedAtDeathCargoItselfIsStillDestroyed()
        {
            World w = NewWorld(ClassId.Farmer);
            int killer = Crimson(w, ClassId.Warrior);
            Actor a = w.Actors[0];
            Place(w, 0, 30, 30);
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 2;
            Assert.AreEqual(CarryKind.None, a.LostCargo.Kind);
            Assert.AreEqual(0, a.LostCargo.Amount);
            Damage.ApplyDamage(w, 0, 9999, killer, 31, 30);
            Assert.AreEqual(CarryKind.None, a.Carry);
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(CarryKind.Biomass, a.LostCargo.Kind);
            Assert.AreEqual(2, a.LostCargo.Amount);
        }

        [Test]
        public void LostCargoIsNone0AfterADeathWithEmptyHandsClearedAtRespawnAndOnResetWorld()
        {
            World w = NewWorld();
            double dt = Dt(w.Config);
            Actor a = w.Actors[0];
            LostCargo lc = a.LostCargo;
            Place(w, 0, 30, 30);
            a.Carry = CarryKind.Corruption;
            a.CarryAmount = 1;
            Damage.ApplyDamage(w, 0, 9999, -1, 30, 30);
            Assert.AreEqual(CarryKind.Corruption, a.LostCargo.Kind);
            Assert.AreEqual(1, a.LostCargo.Amount);
            for (int t = 0; t < 200 && !a.Alive; t++) SimStep.Step(w, dt);
            Assert.AreEqual(true, a.Alive);
            Assert.AreEqual(CarryKind.None, a.LostCargo.Kind);
            Assert.AreEqual(0, a.LostCargo.Amount);
            // A death with empty hands overwrites any stale record.
            a.LostCargo.Kind = CarryKind.Biomass;
            a.LostCargo.Amount = 3;
            a.SpawnProtect = 0;
            Damage.ApplyDamage(w, 0, 9999, -1, w.X[0], w.Z[0]);
            Assert.AreEqual(CarryKind.None, a.LostCargo.Kind);
            Assert.AreEqual(0, a.LostCargo.Amount);
            a.LostCargo.Kind = CarryKind.Biomass;
            a.LostCargo.Amount = 3;
            World.ResetWorld(w, new WorldOptions
            {
                Seed = 11,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = w.Config,
            });
            Assert.AreEqual(0, w.Actors[0].LostCargo.Amount);
            // Mutated in place, never reallocated (no allocation in the sim step).
            Assert.AreSame(lc, w.Actors[0].LostCargo);
        }
    }
}

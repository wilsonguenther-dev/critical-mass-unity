using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/hill.test.ts (09e0904).</summary>
    [TestFixture]
    public sealed class HillTests
    {
        /// <summary>F-05i behaviour note: after n steps hill seconds equal n * dt within 1e-9.</summary>
        private const double HillSecondsTol = 1e-9;

        private static WorldOptions Opts()
        {
            return new WorldOptions
            {
                Seed = 1,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Miner,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        private static double Dt(World w) => SimStep.Dt(w.Config);

        /// <summary>Park every actor far from the hill, then return the world. Azure ids 0..4, crimson ids 5..9.</summary>
        private static World Parked()
        {
            World w = World.CreateWorld(Opts());
            for (int i = 0; i < w.ActorCount; i++)
            {
                w.X[i] = 2;
                w.Z[i] = 2;
                w.PrevX[i] = 2;
                w.PrevZ[i] = 2;
            }
            return w;
        }

        private static void Place(World w, int id, double dx = 0, double dz = 0)
        {
            w.X[id] = w.Map.Hill.X + dx;
            w.Z[id] = w.Map.Hill.Z + dz;
            w.PrevX[id] = w.X[id];
            w.PrevZ[id] = w.Z[id];
        }

        private static int CountEvents(World w, SimEventType type)
        {
            int n = 0;
            int from = Math.Max(0, w.EventHead - w.Events.Length);
            for (int h = from; h < w.EventHead; h++) if (w.Events[h % w.Events.Length].Type == type) n++;
            return n;
        }

        private static int LastEventA(World w)
        {
            return w.Events[(w.EventHead - 1) % w.Events.Length].A;
        }

        [Test]
        public void Case2AzureVs1CrimsonInsideGivesAzureControlsAndGainsExactlyStepsXDT()
        {
            World w = Parked();
            Place(w, 0, 1, 0);
            Place(w, 1, -1, 0);
            Place(w, 5, 0, 1);
            int steps = 90;
            for (int i = 0; i < steps; i++) Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(HillState.Azure, w.Match.HillState);
            Assert.AreEqual(2.0, w.Match.HillCount.Azure);
            Assert.AreEqual(1.0, w.Match.HillCount.Crimson);
            Assert.AreEqual(steps * Dt(w), w.Match.HillSeconds.Azure, HillSecondsTol);
            Assert.AreEqual(0.0, w.Match.HillSeconds.Crimson);
            Assert.AreEqual(steps * Dt(w), w.Actors[0].HillSeconds, HillSecondsTol);
            Assert.AreEqual(steps * Dt(w), w.Actors[1].HillSeconds, HillSecondsTol);
            Assert.AreEqual(0.0, w.Actors[5].HillSeconds);
        }

        [Test]
        public void CrimsonMajorityGivesCrimsonControls()
        {
            World w = Parked();
            Place(w, 0);
            Place(w, 5);
            Place(w, 6);
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(HillState.Crimson, w.Match.HillState);
            Assert.AreEqual(Dt(w), w.Match.HillSeconds.Crimson, HillSecondsTol);
            Assert.AreEqual(0.0, w.Match.HillSeconds.Azure);
        }

        [Test]
        public void Case2Vs2GivesContestedNeitherIncreases()
        {
            World w = Parked();
            Place(w, 0);
            Place(w, 1);
            Place(w, 5);
            Place(w, 6);
            for (int i = 0; i < 30; i++) Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(HillState.Contested, w.Match.HillState);
            Assert.AreEqual(0.0, w.Match.HillSeconds.Azure);
            Assert.AreEqual(0.0, w.Match.HillSeconds.Crimson);
            Assert.AreEqual(0.0, w.Actors[0].HillSeconds);
        }

        [Test]
        public void Case0Vs0GivesNeutral()
        {
            World w = Parked();
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(HillState.Neutral, w.Match.HillState);
            Assert.AreEqual(0.0, w.Match.HillCount.Azure);
            Assert.AreEqual(0.0, w.Match.HillCount.Crimson);
        }

        [Test]
        public void ADeadAzureActorInsideDoesNotCount1LivingPlus1DeadAzureVs1CrimsonGivesContested()
        {
            World w = Parked();
            Place(w, 0);
            Place(w, 1);
            Place(w, 5);
            w.Actors[1].Alive = false;
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(1.0, w.Match.HillCount.Azure);
            Assert.AreEqual(HillState.Contested, w.Match.HillState);
        }

        [Test]
        public void AnActorExactlyAtDistanceRIsOutsideJustInsideCounts()
        {
            World w = Parked();
            Place(w, 0, w.Config.Hill.Radius, 0);
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(0.0, w.Match.HillCount.Azure);
            Assert.AreEqual(HillState.Neutral, w.Match.HillState);
            Place(w, 0, w.Config.Hill.Radius - 0.25, 0);
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(1.0, w.Match.HillCount.Azure);
            Assert.AreEqual(HillState.Azure, w.Match.HillState);
        }

        [Test]
        public void HillOwnerChangeIsPushedOncePerChangeWithA0123()
        {
            World w = Parked();
            int baseHead = w.EventHead;
            Hill.HillSystem(w, Dt(w));
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(baseHead, w.EventHead); // neutral -> neutral: no change
            Place(w, 0);
            Hill.HillSystem(w, Dt(w));
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(1, CountEvents(w, SimEventType.HillOwnerChange));
            Assert.AreEqual(1, LastEventA(w));
            Place(w, 5);
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(3, LastEventA(w));
            Place(w, 6);
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(2, LastEventA(w));
            w.X[5] = 2;
            w.Z[5] = 2;
            w.X[6] = 2;
            w.Z[6] = 2;
            w.X[0] = 2;
            w.Z[0] = 2;
            Hill.HillSystem(w, Dt(w));
            Assert.AreEqual(0, LastEventA(w));
            Assert.AreEqual(4, CountEvents(w, SimEventType.HillOwnerChange));
        }

        [Test]
        public void StepRunsHillSystemBeforeTheClockAdvances()
        {
            World w = Parked();
            Place(w, 0);
            SimStep.Step(w);
            Assert.AreEqual(Dt(w), w.Match.HillSeconds.Azure, 0.5 * Math.Pow(10, -6));
        }
    }
}

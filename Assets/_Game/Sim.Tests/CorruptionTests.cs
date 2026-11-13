using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/corruption.test.ts (09e0904, CM-85, D-068): every biomass unit delivered to the egg, plant or meat,
    /// adds KaijuFeed.BiomassInstabilityPerUnit instability on top of corruption-node runs (CM-34 rate unchanged).
    /// 5v5, human azure warrior: azure 2 is a farmer, crimson 7 a farmer.
    /// </summary>
    [TestFixture]
    public sealed class CorruptionTests
    {
        private const double ShrineX = 56;
        private const double ShrineZ = 8;
        /// <summary>Main's corruption-node rate (CM-34 landed value).</summary>
        private const double MainCorruptionInstabilityPerUnit = 9;

        private static World MakeWorld()
        {
            return World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateBrowser5v5() });
        }

        private static void Place(World w, int id, double x, double z)
        {
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
            w.PrevX[id] = w.X[id];
            w.PrevZ[id] = w.Z[id];
            w.Actors[id].SpawnProtect = 0;
        }

        private static void Hold(World w, int id, bool interact)
        {
            InputFrame f = SimInput.EmptyInput();
            f.AimX = w.X[id];
            f.AimZ = w.Z[id];
            f.Interact = interact;
            SimInput.ApplyInput(w, id, f);
        }

        private static void StepN(World w, int n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
        }

        [Test]
        public void BiomassInstabilityPerUnitComesFromGameConfigIsPositiveAndBelowTheCorruptionRate()
        {
            KaijuFeedConfig feed = GameConfig.CreateBrowser5v5().KaijuFeed;
            Assert.Greater(feed.BiomassInstabilityPerUnit, 0);
            Assert.Less(feed.BiomassInstabilityPerUnit, feed.CorruptionInstabilityPerUnit);
            Assert.AreEqual(0.125, feed.BiomassInstabilityPerUnit);
            Assert.AreEqual(MainCorruptionInstabilityPerUnit, feed.CorruptionInstabilityPerUnit);
        }

        [Test]
        public void DerivedBoundWhileBiomassStaysUnder152UnitsBerserkStillNeedsMains10CorruptionUnits()
        {
            KaijuFeedConfig feed = GameConfig.CreateBrowser5v5().KaijuFeed;
            double b = feed.BiomassInstabilityPerUnit;
            double c = feed.CorruptionInstabilityPerUnit;
            Assert.Less(9 * c + 151 * b, feed.BerserkThreshold);
            Assert.Less(68 * b, feed.BerserkThreshold / 10);
        }

        [Test]
        public void DeliveringNPlantUnitsAddsExactlyNTimesTheRateInOneLoadOrUnitByUnit()
        {
            foreach (int n in new[] { 1, 2, 3, 6, 40 })
            {
                World one = MakeWorld();
                double per = one.Config.KaijuFeed.BiomassInstabilityPerUnit;
                SimResources.ApplyDelivery(one, TeamId.Azure, CarryKind.Biomass, n, -1, BiomassType.Plant);
                Assert.AreEqual(n * per, one.Match.Instability, n + " plant in one load");
                World each = MakeWorld();
                for (int i = 0; i < n; i++) SimResources.ApplyDelivery(each, TeamId.Crimson, CarryKind.Biomass, 1, -1, BiomassType.Plant);
                Assert.AreEqual(n * per, each.Match.Instability, n + " plant unit by unit");
                Assert.IsFalse(one.Match.BerserkLocked);
            }
        }

        [Test]
        public void MeatBiomassCorruptsAtTheSamePerUnitShareAsPlant()
        {
            World w = MakeWorld();
            double per = w.Config.KaijuFeed.BiomassInstabilityPerUnit;
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Biomass, 2, -1, BiomassType.Meat);
            Assert.AreEqual(2 * per, w.Match.Instability);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 3, -1, BiomassType.Plant);
            Assert.AreEqual(5 * per, w.Match.Instability);
        }

        [Test]
        public void ACorruptionNodeDeliveryAddsTheSameInstabilityAsOnMainAndBiomassStacksOnTop()
        {
            World w = MakeWorld();
            double per = w.Config.KaijuFeed.BiomassInstabilityPerUnit;
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 3, -1);
            Assert.AreEqual(3 * MainCorruptionInstabilityPerUnit, w.Match.Instability);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 4, -1, BiomassType.Plant);
            Assert.AreEqual(3 * MainCorruptionInstabilityPerUnit + 4 * per, w.Match.Instability);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 1, -1);
            Assert.AreEqual(4 * MainCorruptionInstabilityPerUnit + 4 * per, w.Match.Instability);
        }

        [Test]
        public void BiomassAllegianceIsUnchangedOnlyInstabilityGainsTheShare()
        {
            World w = MakeWorld();
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 3, -1, BiomassType.Plant);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 2, -1, BiomassType.Meat);
            Assert.AreEqual(3 * feed.BiomassAllegiancePerUnit + 2 * feed.MeatAllegiancePerUnit, w.Match.Allegiance.Azure);
            Assert.AreEqual(0, w.Match.Allegiance.Crimson);
        }

        [Test]
        public void BiomassThatTipsInstabilityOverTheThresholdLocksBerserkOnce()
        {
            World w = MakeWorld();
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            int units = (int)Math.Ceiling(feed.BerserkThreshold / feed.CorruptionInstabilityPerUnit) - 1;
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, units, -1);
            Assert.AreEqual(99, w.Match.Instability);
            Assert.IsFalse(w.Match.BerserkLocked);
            int need = (int)Math.Ceiling((feed.BerserkThreshold - w.Match.Instability) / feed.BiomassInstabilityPerUnit);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, need - 1, -1, BiomassType.Plant);
            Assert.IsFalse(w.Match.BerserkLocked);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1, -1, BiomassType.Plant);
            Assert.GreaterOrEqual(w.Match.Instability, feed.BerserkThreshold);
            Assert.IsTrue(w.Match.BerserkLocked);
        }

        [Test]
        public void DoesNothingAfterTheHatch()
        {
            World w = MakeWorld();
            w.ForceHatch = true;
            SimStep.Step(w);
            Assert.IsTrue(w.Match.Hatched);
            double before = w.Match.Instability;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 5, -1, BiomassType.Plant);
            Assert.AreEqual(before, w.Match.Instability);
        }

        [Test]
        public void EAtTheShrineWithPlantCargoAndAMeatLoadAddsPlantPlusMeatTimesTheRate()
        {
            World w = MakeWorld();
            Actor a = w.Actors[2];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 3;
            a.CarryMeat = 2;
            Place(w, 2, ShrineX, ShrineZ + 2.3);
            Hold(w, 2, true);
            StepN(w, BiomassTests.Windup(w));
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(0, a.CarryMeat);
            Assert.AreEqual(5, w.Match.Delivered[(int)TeamId.Azure].Biomass);
            Assert.AreEqual(5 * w.Config.KaijuFeed.BiomassInstabilityPerUnit, w.Match.Instability);
        }

        [Test]
        public void EAtTheShrineWithCorruptionCargoAddsMainsCorruptionInstabilityOnly()
        {
            World w = MakeWorld();
            Actor a = w.Actors[7];
            a.Carry = CarryKind.Corruption;
            a.CarryAmount = 2;
            Place(w, 7, ShrineX + 1.5, ShrineZ + 2);
            Hold(w, 7, true);
            StepN(w, BiomassTests.Windup(w));
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(2 * MainCorruptionInstabilityPerUnit, w.Match.Instability);
        }
    }
}

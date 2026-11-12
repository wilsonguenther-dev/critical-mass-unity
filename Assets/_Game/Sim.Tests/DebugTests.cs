using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/debug.test.ts (09e0904): describe "debugAddFeed".</summary>
    [TestFixture]
    public sealed class DebugTests
    {
        private static WorldOptions Opts()
        {
            return new WorldOptions
            {
                Seed = 4,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Farmer,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        /// <summary>A real delivery: actor id stands on the shrine with units of kind, holds E, and ResourcesSystem runs once.</summary>
        private static void RealDelivery(World w, int id, CarryKind kind, int units)
        {
            Actor a = w.Actors[id];
            w.X[id] = JsMath.F32(w.Map.Shrine.X + 1);
            w.Z[id] = JsMath.F32(w.Map.Shrine.Z + 1);
            a.Carry = kind;
            a.CarryAmount = units;
            w.Inputs[id].Interact = true;
            SimResources.ResourcesSystem(w, 1.0 / 30);
            w.Inputs[id].Interact = false;
            Assert.AreEqual(0, a.CarryAmount);
        }

        /// <summary>Snapshot of the feed state: allegiance, instability, berserk lock, delivered tallies.</summary>
        private sealed class FeedState
        {
            public double AllegianceAzure;
            public double AllegianceCrimson;
            public double Instability;
            public bool BerserkLocked;
            public double DeliveredAzureBiomass;
            public double DeliveredAzureCorruption;
            public double DeliveredCrimsonBiomass;
            public double DeliveredCrimsonCorruption;
        }

        private static FeedState Snapshot(World w)
        {
            MatchState m = w.Match;
            return new FeedState
            {
                AllegianceAzure = m.Allegiance.Azure,
                AllegianceCrimson = m.Allegiance.Crimson,
                Instability = m.Instability,
                BerserkLocked = m.BerserkLocked,
                DeliveredAzureBiomass = m.Delivered[(int)TeamId.Azure].Biomass,
                DeliveredAzureCorruption = m.Delivered[(int)TeamId.Azure].Corruption,
                DeliveredCrimsonBiomass = m.Delivered[(int)TeamId.Crimson].Biomass,
                DeliveredCrimsonCorruption = m.Delivered[(int)TeamId.Crimson].Corruption,
            };
        }

        /// <summary>toEqual on the feed state: one assertion per field.</summary>
        private static void AssertSameFeed(FeedState expected, FeedState actual)
        {
            Assert.AreEqual(expected.AllegianceAzure, actual.AllegianceAzure, "allegiance.azure");
            Assert.AreEqual(expected.AllegianceCrimson, actual.AllegianceCrimson, "allegiance.crimson");
            Assert.AreEqual(expected.Instability, actual.Instability, "instability");
            Assert.AreEqual(expected.BerserkLocked, actual.BerserkLocked, "berserkLocked");
            Assert.AreEqual(expected.DeliveredAzureBiomass, actual.DeliveredAzureBiomass, "delivered.azure.biomass");
            Assert.AreEqual(expected.DeliveredAzureCorruption, actual.DeliveredAzureCorruption, "delivered.azure.corruption");
            Assert.AreEqual(expected.DeliveredCrimsonBiomass, actual.DeliveredCrimsonBiomass, "delivered.crimson.biomass");
            Assert.AreEqual(expected.DeliveredCrimsonCorruption, actual.DeliveredCrimsonCorruption, "delivered.crimson.corruption");
        }

        private static int FirstOfTeam(World w, TeamId team)
        {
            for (int i = 0; i < w.Actors.Length; i++)
                if (i < w.ActorCount && w.Actors[i].Team == team) return i;
            return -1;
        }

        [Test]
        public void MatchesTheRealDeliveryMathForBothTeamsAndBothKinds()
        {
            World real = World.CreateWorld(Opts());
            World dbg = World.CreateWorld(Opts());
            KaijuFeedConfig feed = real.Config.KaijuFeed;
            int azureId = FirstOfTeam(real, TeamId.Azure);
            int crimsonId = FirstOfTeam(real, TeamId.Crimson);
            Assert.GreaterOrEqual(azureId, 0);
            Assert.GreaterOrEqual(crimsonId, 0);

            RealDelivery(real, azureId, CarryKind.Biomass, 3);
            RealDelivery(real, azureId, CarryKind.Corruption, 2);
            RealDelivery(real, crimsonId, CarryKind.Biomass, 4);
            SimDebug.DebugAddFeed(dbg, TeamId.Azure, 3, 2);
            SimDebug.DebugAddFeed(dbg, TeamId.Crimson, 4, 0);

            AssertSameFeed(Snapshot(real), Snapshot(dbg));
            Assert.AreEqual(3 * feed.BiomassAllegiancePerUnit + 2 * feed.CorruptionAllegiancePerUnit, dbg.Match.Allegiance.Azure);
            Assert.AreEqual(2 * feed.CorruptionInstabilityPerUnit, dbg.Match.Instability); // ITEM CM-85: restores "+ 7 * BiomassInstabilityPerUnit"
        }

        [Test]
        public void PushesTheSameDeliveryEventsWithActorId1()
        {
            World w = World.CreateWorld(Opts());
            int head = w.EventHead;
            SimDebug.DebugAddFeed(w, TeamId.Crimson, 1, 1);
            Assert.AreEqual(head + 2, w.EventHead);
            SimEvent e0 = w.Events[head % w.Events.Length];
            SimEvent e1 = w.Events[(head + 1) % w.Events.Length];
            Assert.AreEqual(SimEventType.DeliverBiomass, e0.Type);
            Assert.AreEqual(-1, e0.A);
            Assert.AreEqual(1, e0.B);
            Assert.AreEqual(SimEventType.DeliverCorruption, e1.Type);
            Assert.AreEqual(1, e1.Value);
        }

        [Test]
        public void TeamNullAddsInstabilityOnlyNoAllegianceAndCrossingTheThresholdLocksBerserk()
        {
            World w = World.CreateWorld(Opts());
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            double units = System.Math.Ceiling(feed.BerserkThreshold / feed.CorruptionInstabilityPerUnit);
            SimDebug.DebugAddFeed(w, null, 0, units - 1);
            Assert.AreEqual(0, w.Match.Allegiance.Azure);
            Assert.AreEqual(0, w.Match.Allegiance.Crimson);
            Assert.AreEqual((units - 1) * feed.CorruptionInstabilityPerUnit, w.Match.Instability);
            Assert.AreEqual(false, w.Match.BerserkLocked);
            SimDebug.DebugAddFeed(w, null, 0, 1);
            Assert.AreEqual(units * feed.CorruptionInstabilityPerUnit, w.Match.Instability);
            Assert.AreEqual(true, w.Match.BerserkLocked);
            Assert.AreEqual(0, w.Match.Delivered[(int)TeamId.Azure].Corruption + w.Match.Delivered[(int)TeamId.Crimson].Corruption);
        }

        [Test]
        public void DoesNothingAfterTheHatchAndReturnsFalse()
        {
            World w = World.CreateWorld(Opts());
            w.Match.Hatched = true;
            w.Kaiju.State = KaijuState.Neutral;
            FeedState before = Snapshot(w);
            Assert.AreEqual(false, SimDebug.DebugAddFeed(w, TeamId.Azure, 5, 5));
            Assert.AreEqual(false, SimDebug.DebugAddFeed(w, null, 0, 5));
            AssertSameFeed(before, Snapshot(w));
        }

        [Test]
        public void ReturnsTrueWhenItFedTheEggAndIgnoresNonPositiveUnits()
        {
            World w = World.CreateWorld(Opts());
            Assert.AreEqual(true, SimDebug.DebugAddFeed(w, TeamId.Azure, 1, 0));
            FeedState before = Snapshot(w);
            Assert.AreEqual(false, SimDebug.DebugAddFeed(w, TeamId.Azure, 0, 0));
            Assert.AreEqual(false, SimDebug.DebugAddFeed(w, TeamId.Azure, -2, double.NaN));
            AssertSameFeed(before, Snapshot(w));
        }
    }
}

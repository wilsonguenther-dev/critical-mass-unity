using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of the resource cases of src/sim/kaiju.test.ts lines 96-263 (09e0904): describe "resources: gather" and "resources: delivery".</summary>
    /// <remarks>
    /// 5v5, human azure warrior. Roster (CM-01 largest remainder):
    ///   azure   0 warrior (human), 1 warrior, 2 farmer, 3 farmer, 4 miner
    ///   crimson 5 warrior,         6 warrior, 7 farmer, 8 farmer, 9 miner
    /// </remarks>
    [TestFixture]
    public sealed class KaijuResourceTests
    {
        private const double ShrineX = 56;
        private const double ShrineZ = 8;

        private static World MakeWorld(int teamSize = 5)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = 7,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            });
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

        private static void Hold(World w, int id, bool interact, bool attack = false, double aimX = double.NaN, double aimZ = double.NaN)
        {
            InputFrame f = SimInput.EmptyInput();
            f.Interact = interact;
            f.Attack = attack;
            f.AimX = aimX;
            f.AimZ = aimZ;
            SimInput.ApplyInput(w, id, f);
        }

        private static List<SimEvent> Events(World w, SimEventType type)
        {
            var output = new List<SimEvent>();
            int from = System.Math.Max(0, w.EventHead - World.EventCapacity);
            for (int i = from; i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type)
                    output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value });
            }
            return output;
        }

        private static void StepN(World w, double n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
        }

        /// <summary>Force-hatch now (same code path as the natural hatch) and return after the hatching step.</summary>
        private static void ForceHatch(World w)
        {
            w.ForceHatch = true;
            SimStep.Step(w);
            Assert.AreEqual(true, w.Match.Hatched);
        }

        [Test]
        public void FarmerGathers3BiomassIn3SAWarriorGathers1In2SAndCannotExceedCarry1()
        {
            World w = MakeWorld();
            double hz = w.Config.SimHz;
            ResourceNode node = w.Nodes[0]; // west biomass (24, 16)
            Assert.AreEqual(NodeKind.Biomass, node.Kind);
            Assert.AreEqual(w.Config.Resources.BiomassNodeCharges, (double)node.Charges);
            Place(w, 2, node.X + 1, node.Z); // farmer
            Hold(w, 2, true);
            StepN(w, 3 * hz - 1);
            Assert.AreEqual(2, w.Actors[2].CarryAmount);
            SimStep.Step(w);
            Assert.AreEqual(CarryKind.Biomass, w.Actors[2].Carry);
            Assert.AreEqual(3, w.Actors[2].CarryAmount);
            Assert.AreEqual(3, node.Charges);
            Assert.AreEqual(3, Events(w, SimEventType.Gather).Count);
            StepN(w, 2 * hz);
            Assert.AreEqual(3, w.Actors[2].CarryAmount); // farmer capacity 3

            World w2 = MakeWorld();
            ResourceNode n2 = w2.Nodes[0];
            Place(w2, 1, n2.X + 1, n2.Z); // warrior
            Hold(w2, 1, true);
            StepN(w2, 2 * hz - 1);
            Assert.AreEqual(0, w2.Actors[1].CarryAmount);
            SimStep.Step(w2);
            Assert.AreEqual(CarryKind.Biomass, w2.Actors[1].Carry);
            Assert.AreEqual(1, w2.Actors[1].CarryAmount);
            StepN(w2, 6 * hz);
            Assert.AreEqual(1, w2.Actors[1].CarryAmount);
            Assert.AreEqual(w2.Config.Resources.BiomassNodeCharges - 1, (double)n2.Charges);
        }

        [Test]
        public void ReleasingEOrMovingOutOfRangeResetsGatherProgressANodeRegrowsOneChargePerRegrowPeriod()
        {
            World w = MakeWorld();
            double hz = w.Config.SimHz;
            ResourceNode node = w.Nodes[0];
            Place(w, 1, node.X + 1, node.Z);
            Hold(w, 1, true);
            StepN(w, hz);
            Assert.Greater(w.Actors[1].GatherProgress, 0.9);
            Hold(w, 1, false);
            SimStep.Step(w);
            Assert.AreEqual(0, w.Actors[1].GatherProgress);
            Hold(w, 1, true);
            StepN(w, hz);
            Place(w, 1, node.X + 4, node.Z); // 4 > 1.8 + 0.45
            SimStep.Step(w);
            Assert.AreEqual(0, w.Actors[1].GatherProgress);
            Assert.AreEqual(0, w.Actors[1].CarryAmount);

            // Regrow: take one charge, it comes back after biomassRegrowSeconds.
            Place(w, 1, node.X + 1, node.Z);
            StepN(w, 2 * hz);
            Assert.AreEqual(5, node.Charges);
            Hold(w, 1, false);
            StepN(w, w.Config.Resources.BiomassRegrowSeconds * hz - 5);
            Assert.AreEqual(5, node.Charges);
            StepN(w, 10);
            Assert.AreEqual(6, node.Charges);
        }

        [Test]
        public void CannotGatherCorruptionWhileCarryingBiomass()
        {
            World w = MakeWorld();
            double hz = w.Config.SimHz;
            ResourceNode cnode = null;
            for (int i = 0; i < w.Nodes.Length; i++)
            {
                if (w.Nodes[i].Kind == NodeKind.Corruption)
                {
                    cnode = w.Nodes[i];
                    break;
                }
            }
            Assert.IsNotNull(cnode);
            Assert.AreEqual(w.Config.Resources.CorruptionNodeCharges, (double)cnode.Charges);
            Actor a = w.Actors[2]; // farmer, capacity 3
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 1;
            Place(w, 2, cnode.X + 1, cnode.Z);
            Hold(w, 2, true);
            StepN(w, 4 * hz);
            Assert.AreEqual(CarryKind.Biomass, a.Carry);
            Assert.AreEqual(1, a.CarryAmount);
            Assert.AreEqual(w.Config.Resources.CorruptionNodeCharges, (double)cnode.Charges);
            Assert.AreEqual(0, Events(w, SimEventType.Gather).Count);
        }

        [Test]
        public void TakingDamageResetsGatherProgress()
        {
            World w = MakeWorld();
            double hz = w.Config.SimHz;
            ResourceNode node = w.Nodes[0];
            Place(w, 1, node.X + 1, node.Z);
            Place(w, 5, node.X + 2.2, node.Z);
            Hold(w, 1, true);
            StepN(w, hz);
            Assert.Greater(w.Actors[1].GatherProgress, 0.9);
            Hold(w, 5, false, true, w.X[1], w.Z[1]); // crimson warrior swings at 1
            SimStep.Step(w);
            Assert.Less(w.Actors[1].Hp, w.Actors[1].MaxHp);
            Assert.Less(w.Actors[1].GatherProgress, 0.05);
        }

        [Test]
        public void Delivering3BiomassAddsExactly30AllegianceAndEmptiesCarryASecondEPressAddsNothing()
        {
            World w = MakeWorld();
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            Actor a = w.Actors[2];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 3;
            Place(w, 2, ShrineX, ShrineZ + 2.3);
            Hold(w, 2, true);
            SimStep.Step(w);
            Assert.AreEqual(3 * feed.BiomassAllegiancePerUnit, w.Match.Allegiance.Azure);
            Assert.AreEqual(30, w.Match.Allegiance.Azure);
            Assert.AreEqual(3, w.Match.Delivered[(int)TeamId.Azure].Biomass);
            Assert.AreEqual(CarryKind.None, a.Carry);
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(1, Events(w, SimEventType.DeliverBiomass).Count);
            Hold(w, 2, false);
            SimStep.Step(w);
            Hold(w, 2, true);
            StepN(w, 5);
            Assert.AreEqual(30, w.Match.Allegiance.Azure);
            Assert.AreEqual(3, w.Match.Delivered[(int)TeamId.Azure].Biomass);
            Assert.AreEqual(1, Events(w, SimEventType.DeliverBiomass).Count);
            Assert.AreEqual(0, w.Match.Allegiance.Crimson);
        }

        [Test]
        public void DeliveryOutOfRange35FromShrineDoesNothing()
        {
            World w = MakeWorld();
            Actor a = w.Actors[2];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 3;
            Place(w, 2, ShrineX, ShrineZ + 3.5);
            Hold(w, 2, true);
            StepN(w, 10);
            Assert.AreEqual(0, w.Match.Allegiance.Azure);
            Assert.AreEqual(3, a.CarryAmount);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverBiomass).Count);
        }

        [Test]
        public void CorruptionDeliveryAdds4AllegiancePlusCorruptionInstabilityPerUnit9CM34Instability()
        {
            World w = MakeWorld();
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            Actor a = w.Actors[7]; // crimson farmer
            a.Carry = CarryKind.Corruption;
            a.CarryAmount = 1;
            Place(w, 7, ShrineX + 1.5, ShrineZ + 2);
            Hold(w, 7, true);
            SimStep.Step(w);
            Assert.AreEqual(feed.CorruptionAllegiancePerUnit, w.Match.Allegiance.Crimson);
            Assert.AreEqual(4, w.Match.Allegiance.Crimson);
            Assert.AreEqual(feed.CorruptionInstabilityPerUnit, w.Match.Instability);
            Assert.AreEqual(9, w.Match.Instability);
            Assert.AreEqual(1, w.Match.Delivered[(int)TeamId.Crimson].Corruption);
            Assert.AreEqual(CarryKind.None, a.Carry);
            Assert.AreEqual(1, Events(w, SimEventType.DeliverCorruption).Count);
        }

        [Test]
        public void ApplyDeliveryHoldsTheDeliveryMathCM10ReusesItAndDoesNothingAfterHatch()
        {
            World w = MakeWorld();
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 2);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 3);
            Assert.AreEqual(20, w.Match.Allegiance.Azure);
            Assert.AreEqual(12, w.Match.Allegiance.Crimson);
            Assert.AreEqual(3 * feed.CorruptionInstabilityPerUnit, w.Match.Instability); // ITEM CM-85: restores "+ 2 * BiomassInstabilityPerUnit"
            ForceHatch(w);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 5);
            Assert.AreEqual(20, w.Match.Allegiance.Azure);
            // E at the shrine after hatch does nothing either.
            Actor a = w.Actors[2];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 1;
            Place(w, 2, ShrineX, ShrineZ + 2.3);
            Hold(w, 2, true);
            SimStep.Step(w);
            Assert.AreEqual(1, a.CarryAmount);
            Assert.AreEqual(20, w.Match.Allegiance.Azure);
        }
    }
}

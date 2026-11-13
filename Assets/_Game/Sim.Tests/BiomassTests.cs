using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/biomass.test.ts (09e0904, CM-35): biomass types. Plant from map nodes, meat from killed bodies as a
    /// loose orb picked up by walking over it, each paying its own rate, every DeliverBiomass tagged with its type.
    /// 5v5, human azure warrior: azure 0 warrior (human), 1 warrior, 2 farmer, 3 farmer, 4 miner; crimson 5..9 the same.
    /// Kills are staged inside the hill ring around (56, 40). DG-16b: deliveries take the CM-64 wind-up
    /// (<see cref="Windup"/> passes), as the CM-64 branch edited this file.
    /// </summary>
    [TestFixture]
    public sealed class BiomassTests
    {
        private const double HillX = 56;
        private const double HillZ = 40;

        private static World MakeWorld()
        {
            return World.CreateWorld(Opts());
        }

        private static WorldOptions Opts()
        {
            return new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateBrowser5v5() };
        }

        /// <summary>ResourcesSystem passes a delivery needs (1 before CM-64; KaijuFeed.DeliverWindupTicks after).</summary>
        internal static int Windup(World w) => Delivery.WindupTicks(w.Config);

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

        private static List<SimEvent> Events(World w, SimEventType type)
        {
            var output = new List<SimEvent>();
            for (int i = Math.Max(0, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type) output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value, Bio = e.Bio, Cause = e.Cause });
            }
            return output;
        }

        private static List<LooseOrb> ActiveOrbs(World w)
        {
            var output = new List<LooseOrb>();
            foreach (LooseOrb o in w.Orbs) if (o.Active) output.Add(o);
            return output;
        }

        private static void Kill(World w, int target, int killer)
        {
            Place(w, target, HillX, HillZ);
            Place(w, killer, HillX - 4, HillZ);
            Damage.ApplyDamage(w, target, 1e6, killer, w.X[killer], w.Z[killer]);
            Assert.IsFalse(w.Actors[target].Alive);
        }

        private static void StepN(World w, int n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
        }

        private static void Resources(World w, int passes)
        {
            double dt = SimStep.Dt(w.Config);
            for (int i = 0; i < passes; i++) SimResources.ResourcesSystem(w, dt);
        }

        [Test]
        public void EachBiomassTypeHasItsOwnGameConfigValue()
        {
            World w = MakeWorld();
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            ResourcesConfig res = w.Config.Resources;
            Assert.Greater(feed.BiomassAllegiancePerUnit, 0);
            Assert.Greater(feed.MeatAllegiancePerUnit, 0);
            Assert.Greater(res.MeatUnitsPerKill, 0);
            Assert.Greater(res.MeatPickupRange, 0);
            Assert.AreEqual(res.MeatUnitsPerKill, res.MeatCarryCapacity);
            Assert.AreEqual(res.BiomassRegrowSeconds * w.Config.SimHz, res.MeatOrbLifetimeTicks);
            Assert.AreEqual(Math.Floor(res.MeatOrbLifetimeTicks), res.MeatOrbLifetimeTicks);

            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1, -1, BiomassType.Plant);
            Assert.AreEqual(feed.BiomassAllegiancePerUnit, w.Match.Allegiance.Azure, 1e-9);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Biomass, 1, -1, BiomassType.Meat);
            Assert.AreEqual(feed.MeatAllegiancePerUnit, w.Match.Allegiance.Crimson, 1e-9);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 2);
            Assert.AreEqual(3, w.Match.Delivered[(int)TeamId.Azure].Plant);
            Assert.AreEqual(0, w.Match.Delivered[(int)TeamId.Azure].Meat);
            Assert.AreEqual(1, w.Match.Delivered[(int)TeamId.Crimson].Meat);
            Assert.AreEqual(3, w.Match.Delivered[(int)TeamId.Azure].Biomass);
            Assert.AreEqual(1, w.Match.Delivered[(int)TeamId.Crimson].Biomass);
        }

        [Test]
        public void GatheringAMapBiomassNodeYieldsPlantCargoAndAPlantTaggedDelivery()
        {
            World w = MakeWorld();
            ResourceNode node = Array.Find(w.Nodes, n => n.Kind == NodeKind.Biomass);
            Assert.IsNotNull(node);
            int id = 2;
            Place(w, id, node.X, node.Z);
            Hold(w, id, true);
            int need = (int)Math.Ceiling(w.Config.Class(ClassId.Farmer).GatherSeconds * w.Config.SimHz) + 1;
            Resources(w, need);
            Actor a = w.Actors[id];
            Assert.AreEqual(CarryKind.Biomass, a.Carry);
            Assert.GreaterOrEqual(a.CarryAmount, 1);
            Assert.AreEqual(0, a.CarryMeat);
            Assert.GreaterOrEqual(Events(w, SimEventType.Gather).Count, 1);

            int units = a.CarryAmount;
            Place(w, id, w.Map.Shrine.X, w.Map.Shrine.Z);
            Resources(w, Windup(w));
            List<SimEvent> del = Events(w, SimEventType.DeliverBiomass);
            Assert.AreEqual(1, del.Count);
            Assert.AreEqual(BiomassType.Plant, del[0].Bio);
            Assert.AreEqual(units, del[0].Value);
            Assert.AreEqual(id, del[0].A);
            Assert.AreEqual(units, w.Match.Delivered[(int)TeamId.Azure].Plant);
            Assert.AreEqual(0, w.Match.Delivered[(int)TeamId.Azure].Meat);
            Assert.AreEqual(units * w.Config.KaijuFeed.BiomassAllegiancePerUnit, w.Match.Allegiance.Azure, 1e-9);
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(0, a.CarryMeat);
        }

        [Test]
        public void AKillDropsExactlyOneMeatOrbAtTheCorpseOncePerDeath()
        {
            World w = MakeWorld();
            ResourcesConfig res = w.Config.Resources;
            Kill(w, 5, 1);
            Assert.AreEqual(0, ActiveOrbs(w).Count);
            SimStep.Step(w);
            List<LooseOrb> orbs = ActiveOrbs(w);
            Assert.AreEqual(1, orbs.Count);
            Assert.AreEqual(BiomassType.Meat, orbs[0].Kind);
            Assert.AreEqual(5, orbs[0].Source);
            Assert.AreEqual(res.MeatUnitsPerKill, orbs[0].Units);
            Assert.AreEqual(HillX, orbs[0].X, 1e-4);
            Assert.AreEqual(HillZ, orbs[0].Z, 1e-4);
            Assert.AreEqual(res.MeatOrbLifetimeTicks - 1, orbs[0].Ttl);
            Place(w, 1, HillX - 10, HillZ);
            StepN(w, 30);
            Assert.AreEqual(1, ActiveOrbs(w).Count);
            Combat.DropCorpseMeat(w);
            Assert.AreEqual(1, ActiveOrbs(w).Count);
        }

        [Test]
        public void AnOrbRotsAfterExactlyMeatOrbLifetimeTicksSimTicks()
        {
            World w = MakeWorld();
            int life = (int)w.Config.Resources.MeatOrbLifetimeTicks;
            Kill(w, 5, 1);
            Place(w, 1, HillX - 10, HillZ);
            SimStep.Step(w);
            StepN(w, life - 2);
            Assert.AreEqual(1, ActiveOrbs(w).Count);
            Assert.AreEqual(1, ActiveOrbs(w)[0].Ttl);
            SimStep.Step(w);
            Assert.AreEqual(0, ActiveOrbs(w).Count);
        }

        [Test]
        public void WalkingOverTheOrbPicksTheMeatUpAndDeliveryPaysTheMeatRateWithAMeatTaggedEvent()
        {
            World w = MakeWorld();
            ResourcesConfig res = w.Config.Resources;
            Kill(w, 5, 1);
            Place(w, 1, HillX - 10, HillZ);
            SimStep.Step(w);
            int picker = 2;
            Place(w, picker, HillX + 0.3, HillZ);
            Hold(w, picker, false);
            SimStep.Step(w);
            Actor a = w.Actors[picker];
            int units = (int)Math.Min(res.MeatUnitsPerKill, res.MeatCarryCapacity);
            Assert.AreEqual(CarryKind.None, a.Carry);
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(units, a.CarryMeat);
            if (units == (int)res.MeatUnitsPerKill) Assert.AreEqual(0, ActiveOrbs(w).Count);

            Place(w, picker, w.Map.Shrine.X, w.Map.Shrine.Z);
            Hold(w, picker, true);
            Resources(w, Windup(w));
            List<SimEvent> del = Events(w, SimEventType.DeliverBiomass);
            Assert.AreEqual(1, del.Count);
            Assert.AreEqual(BiomassType.Meat, del[0].Bio);
            Assert.AreEqual(units, del[0].Value);
            Assert.AreEqual(0, del[0].B);
            Assert.AreEqual(units, w.Match.Delivered[(int)TeamId.Azure].Meat);
            Assert.AreEqual(0, w.Match.Delivered[(int)TeamId.Azure].Plant);
            Assert.AreEqual(units, w.Match.Delivered[(int)TeamId.Azure].Biomass);
            Assert.AreEqual(units * w.Config.KaijuFeed.MeatAllegiancePerUnit, w.Match.Allegiance.Azure, 1e-9);
            Assert.AreEqual(CarryKind.None, a.Carry);
            Assert.AreEqual(0, a.CarryMeat);
        }

        [Test]
        public void PickupRespectsMeatCarryCapacityAndNeverTouchesTheClassCargo()
        {
            World w = MakeWorld();
            ResourcesConfig res = w.Config.Resources;
            Kill(w, 5, 1);
            Place(w, 1, HillX - 10, HillZ);
            SimStep.Step(w);
            int picker = 0;
            Actor a = w.Actors[picker];
            int cargoCap = (int)w.Config.Class(ClassId.Warrior).CarryCapacity;
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = cargoCap;
            World.SpawnLooseOrb(w, HillX, HillZ, (int)res.MeatUnitsPerKill, (int)res.MeatOrbLifetimeTicks, 6);
            Place(w, picker, HillX - 0.3, HillZ);
            Hold(w, picker, false);
            SimStep.Step(w);
            Assert.AreEqual(CarryKind.Biomass, a.Carry);
            Assert.AreEqual(cargoCap, a.CarryAmount);
            Assert.AreEqual(res.MeatCarryCapacity, a.CarryMeat);
            int left = (int)(2 * res.MeatUnitsPerKill - res.MeatCarryCapacity);
            int sum = 0;
            foreach (LooseOrb o in ActiveOrbs(w)) sum += o.Units;
            Assert.AreEqual(left, sum);
        }

        [Test]
        public void CorruptionCargoNeverPicksUpMeat()
        {
            World w = MakeWorld();
            Kill(w, 5, 1);
            Place(w, 1, HillX - 10, HillZ);
            SimStep.Step(w);
            int picker = 2;
            w.Actors[picker].Carry = CarryKind.Corruption;
            w.Actors[picker].CarryAmount = 1;
            Place(w, picker, HillX + 0.3, HillZ);
            SimStep.Step(w);
            Assert.AreEqual(CarryKind.Corruption, w.Actors[picker].Carry);
            Assert.AreEqual(0, w.Actors[picker].CarryMeat);
            Assert.AreEqual(w.Config.Resources.MeatUnitsPerKill, ActiveOrbs(w)[0].Units);
        }

        [Test]
        public void AMixedLoadRaisesOneDeliverBiomassPerTypeEachAtItsOwnRate()
        {
            World w = MakeWorld();
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            int id = 2;
            Actor a = w.Actors[id];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 2;
            a.CarryMeat = 1;
            Place(w, id, w.Map.Shrine.X, w.Map.Shrine.Z);
            Hold(w, id, true);
            Resources(w, Windup(w));
            List<SimEvent> del = Events(w, SimEventType.DeliverBiomass);
            Assert.AreEqual(2, del.Count);
            Assert.AreEqual(BiomassType.Plant, del[0].Bio);
            Assert.AreEqual(2, del[0].Value);
            Assert.AreEqual(BiomassType.Meat, del[1].Bio);
            Assert.AreEqual(1, del[1].Value);
            Assert.AreEqual(2 * feed.BiomassAllegiancePerUnit + feed.MeatAllegiancePerUnit, w.Match.Allegiance.Azure, 1e-9);
            Assert.AreEqual(2, w.Match.Delivered[(int)TeamId.Azure].Plant);
            Assert.AreEqual(1, w.Match.Delivered[(int)TeamId.Azure].Meat);
            Assert.AreEqual(3, w.Match.Delivered[(int)TeamId.Azure].Biomass);
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(0, a.CarryMeat);
            w.Actors[5].SpawnProtect = 0;
            Damage.ApplyDamage(w, 5, 1, 1, w.X[1], w.Z[1]);
            List<SimEvent> hits = Events(w, SimEventType.Hit);
            Assert.AreEqual(1, hits.Count);
            Assert.IsNull(hits[0].Bio);
        }

        [Test]
        public void MeatPickedUpBeforeTakingCorruptionRidesAlongWithTheCorruptionDelivery()
        {
            World w = MakeWorld();
            int cap = (int)w.Config.Resources.MeatCarryCapacity;
            int id = 3;
            Actor a = w.Actors[id];
            a.CarryMeat = cap;
            a.Carry = CarryKind.Corruption;
            a.CarryAmount = 1;
            Place(w, id, w.Map.Shrine.X, w.Map.Shrine.Z);
            Hold(w, id, true);
            Resources(w, Windup(w));
            Assert.AreEqual(1, Events(w, SimEventType.DeliverCorruption).Count);
            List<SimEvent> del = Events(w, SimEventType.DeliverBiomass);
            Assert.AreEqual(1, del.Count);
            Assert.AreEqual(BiomassType.Meat, del[0].Bio);
            Assert.AreEqual(cap, del[0].Value);
            Assert.AreEqual(cap, w.Match.Delivered[(int)TeamId.Azure].Meat);
            Assert.AreEqual(1, w.Match.Delivered[(int)TeamId.Azure].Corruption);
            Assert.AreEqual(0, a.CarryMeat);
            Assert.AreEqual(CarryKind.None, a.Carry);
        }

        [Test]
        public void DyingOrTheHatchEmptiesTheMeatLoad()
        {
            World w = MakeWorld();
            w.Actors[2].CarryMeat = 2;
            w.Actors[3].CarryMeat = 2;
            Place(w, 2, HillX, HillZ);
            Damage.ApplyDamage(w, 2, 1e6, 5, HillX + 1, HillZ);
            Resources(w, 1);
            Assert.AreEqual(0, w.Actors[2].CarryMeat);
            Assert.AreEqual(2, w.Actors[3].CarryMeat);
            w.Match.Hatched = true;
            w.Kaiju.State = KaijuState.Neutral;
            Resources(w, 1);
            Assert.AreEqual(0, w.Actors[3].CarryMeat);
        }

        [Test]
        public void NoOrbDropsAfterTheHatch()
        {
            World w = MakeWorld();
            w.Match.Hatched = true;
            w.Kaiju.State = KaijuState.Neutral;
            Kill(w, 5, 1);
            Combat.DropCorpseMeat(w);
            Assert.AreEqual(0, ActiveOrbs(w).Count);
        }

        [Test]
        public void AFullPoolRecyclesTheOrbWithTheLeastTimeLeftAndCountsTheOverflow()
        {
            World w = MakeWorld();
            int cap = World.OrbCapacity(w.Config);
            for (int i = 0; i < cap; i++) World.SpawnLooseOrb(w, 10, 10, 1, 100 + i, -1);
            Assert.AreEqual(0, w.OrbOverflows);
            int slot = World.SpawnLooseOrb(w, 20, 20, 2, 600, 3);
            Assert.AreEqual(0, slot);
            Assert.AreEqual(1, w.OrbOverflows);
            Assert.AreEqual(2, w.Orbs[0].Units);
            Assert.AreEqual(3, w.Orbs[0].Source);
            Assert.AreEqual(cap, w.Orbs.Length);
        }

        [Test]
        public void ResetWorldClearsEveryOrbTheDropFlagsAndThePerTypeTalliesInPlace()
        {
            World w = MakeWorld();
            LooseOrb[] orbsRef = w.Orbs;
            Kill(w, 5, 1);
            SimStep.Step(w);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1, -1, BiomassType.Meat);
            World.ResetWorld(w, Opts());
            Assert.AreSame(orbsRef, w.Orbs);
            Assert.AreEqual(0, ActiveOrbs(w).Count);
            foreach (byte v in w.MeatDropped) Assert.AreEqual(0, v);
            Assert.AreEqual(0, w.Match.Delivered[(int)TeamId.Azure].Meat);
            Assert.AreEqual(0, w.Match.Delivered[(int)TeamId.Azure].Plant);
            foreach (Actor a in w.Actors) Assert.AreEqual(0, a.CarryMeat);
        }

        [Test]
        public void NoNewBotFieldIsAllocatedOnAKill()
        {
            World w = MakeWorld();
            BotsState st = BotBrain.GetBotsState(w);
            BotFields f = st.Fields;
            int count = f.Count;
            double[][] distRefs = (double[][])f.Dist.Clone();
            int[] fieldOfNode = f.FieldOfNode;
            int nodeCount = w.Nodes.Length;
            double dt = SimStep.Dt(w.Config);
            for (int t = 0; t < 3; t++)
            {
                BotBrain.BotsThink(w, dt);
                SimStep.Step(w);
            }
            Kill(w, 5, 1);
            Kill(w, 6, 0);
            for (int t = 0; t < 60; t++)
            {
                BotBrain.BotsThink(w, dt);
                SimStep.Step(w);
            }
            int carriers = 0;
            foreach (Actor a in w.Actors) if (a.CarryMeat > 0) carriers++;
            Assert.Greater(ActiveOrbs(w).Count + carriers, 0);
            BotsState st2 = BotBrain.GetBotsState(w);
            Assert.AreSame(st, st2);
            Assert.AreSame(f, st2.Fields);
            Assert.AreEqual(count, f.Count);
            Assert.AreEqual(distRefs.Length, f.Dist.Length);
            for (int i = 0; i < distRefs.Length; i++) Assert.AreSame(distRefs[i], f.Dist[i]);
            Assert.AreSame(fieldOfNode, f.FieldOfNode);
            Assert.AreEqual(nodeCount, w.Nodes.Length);
        }

        [Test]
        public void OnlyKaijuRoleBotsAndTheHumanSlotWalkPickMeat()
        {
            World w = MakeWorld();
            ResourcesConfig res = w.Config.Resources;
            BotsState st = BotBrain.GetBotsState(w);
            BotBrain.BotsThink(w, SimStep.Dt(w.Config));
            Assert.IsTrue(w.Actors[0].MeatPickup);
            for (int id = 1; id < w.ActorCount; id++) Assert.AreEqual(st.Role[id] == BotsState.RoleKaiju, w.Actors[id].MeatPickup, "bot " + id);
            int hillBot = -1;
            int feeder = -1;
            for (int id = 1; id < w.ActorCount; id++)
            {
                if (hillBot < 0 && st.Role[id] == BotsState.RoleHill) hillBot = id;
                if (feeder < 0 && st.Role[id] == BotsState.RoleKaiju) feeder = id;
            }
            Assert.Greater(hillBot, 0);
            Assert.Greater(feeder, 0);

            Place(w, hillBot, HillX, HillZ);
            World.SpawnLooseOrb(w, HillX, HillZ, (int)res.MeatUnitsPerKill, (int)res.MeatOrbLifetimeTicks, 5);
            Resources(w, 1);
            Assert.AreEqual(0, w.Actors[hillBot].CarryMeat);
            Assert.AreEqual(CarryKind.None, w.Actors[hillBot].Carry);
            Assert.AreEqual(1, ActiveOrbs(w).Count);

            Place(w, feeder, HillX, HillZ + 8);
            World.SpawnLooseOrb(w, HillX, HillZ + 8, (int)res.MeatUnitsPerKill, (int)res.MeatOrbLifetimeTicks, 6);
            Resources(w, 1);
            Assert.Greater(w.Actors[feeder].CarryMeat, 0);
            Assert.AreEqual(CarryKind.None, w.Actors[feeder].Carry);
            Assert.AreEqual(0, w.Actors[feeder].CarryAmount);
            Assert.AreEqual(1, ActiveOrbs(w).Count);
        }

        [Test]
        public void AFeedersPlantCargoIsTheSameWithOrWithoutMeatOnItsBack()
        {
            World w = MakeWorld();
            ResourcesConfig res = w.Config.Resources;
            int id = 2;
            Actor a = w.Actors[id];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 1;
            Place(w, id, HillX, HillZ);
            World.SpawnLooseOrb(w, HillX, HillZ, (int)res.MeatUnitsPerKill, (int)res.MeatOrbLifetimeTicks, 5);
            Hold(w, id, false);
            Resources(w, 1);
            Assert.AreEqual((int)Math.Min(res.MeatUnitsPerKill, res.MeatCarryCapacity), a.CarryMeat);
            Assert.AreEqual(CarryKind.Biomass, a.Carry);
            Assert.AreEqual(1, a.CarryAmount);
        }

        [Test]
        public void ResetWorldPutsMeatPickupBackOnForEveryActor()
        {
            World w = MakeWorld();
            for (int id = 0; id < w.ActorCount; id++) w.Actors[id].MeatPickup = false;
            World.ResetWorld(w, Opts());
            for (int id = 0; id < w.ActorCount; id++) Assert.IsTrue(w.Actors[id].MeatPickup);
        }
    }
}

using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>U-063 class roles (our rule): only Farmers feed the egg, only raiders run corruption, carriers hand off, bots obey.</summary>
    [TestFixture]
    public sealed class RolesTests
    {
        private static GameConfig Cfg => DepthKit.Cfg();

        private static World AtShrine(ClassId cls, CarryKind kind, int amount, int meat = 0)
        {
            GameConfig cfg = Cfg;
            World w = DepthKit.Field(cfg, cls);
            DepthKit.Put(w, 0, w.Map.Shrine.X, w.Map.Shrine.Z + 2.2);
            Actor a = w.Actors[0];
            a.Carry = kind;
            a.CarryAmount = amount;
            a.CarryMeat = meat;
            w.Inputs[0].Interact = true;
            return w;
        }

        [Test]
        public void U063ClassicLeavesEveryClassFreeToFeedAndCorrupt()
        {
            World w = DepthKit.Field(GameConfig.CreateBrowser5v5(), ClassId.Warrior);
            Assert.IsFalse(Roles.On(w));
            foreach (ClassId c in ClassIds.All)
            {
                Assert.IsTrue(Roles.MayFeed(w, c));
                Assert.IsTrue(Roles.MayCorrupt(w, c));
            }
            Assert.IsTrue(GameConfig.CreateBrowser5v5().Depth.Classic, "the browser config is the classic setting");
            Assert.IsFalse(GameConfig.CreateDefault().Depth.Classic, "the game runs the rules");
        }

        [Test]
        public void U063AWarriorWithBiomassIsRefusedAtTheShrineKeepsItsLoadAndGetsOneEventPerLock()
        {
            World w = AtShrine(ClassId.Warrior, CarryKind.Biomass, 1);
            int head = w.EventHead;
            DepthKit.Steps(w, 40);
            Assert.AreEqual(0, w.Match.Delivered[0].Biomass);
            Assert.AreEqual(1, w.Actors[0].CarryAmount);
            Assert.AreEqual(0, w.Actors[0].DeliverTicks, "no wind-up starts");
            List<SimEvent> ev = DepthKit.Since(w, head, SimEventType.DeliverRefused);
            Assert.AreEqual(1, ev.Count, "one event inside the 45-tick lock");
            Assert.AreEqual(0, ev[0].A);
            Assert.AreEqual(Roles.RefusedNotFeeder, ev[0].B);
            Assert.AreEqual(w.Map.Shrine.X, ev[0].X);
            Assert.AreEqual(1, ev[0].Value);
            DepthKit.Steps(w, 20);
            Assert.AreEqual(2, DepthKit.Since(w, head, SimEventType.DeliverRefused).Count, "a second one after the lock");
        }

        [Test]
        public void U063AFarmerFeedsPlantAndMeatAfterTheWindup()
        {
            World w = AtShrine(ClassId.Farmer, CarryKind.Biomass, 3, 2);
            DepthKit.Steps(w, Delivery.WindupTicks(w.Config) + 1);
            Assert.AreEqual(5, w.Match.Delivered[0].Biomass);
            Assert.AreEqual(3, w.Match.Delivered[0].Plant);
            Assert.AreEqual(2, w.Match.Delivered[0].Meat);
            Assert.AreEqual(0, w.Actors[0].CarryAmount);
            Assert.AreEqual(0, w.Actors[0].CarryMeat);
        }

        [Test]
        public void U063RaidersDeliverCorruptionAndTheirMeatStaysWithThem()
        {
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Miner, ClassId.Demolitions })
            {
                World w = AtShrine(c, CarryKind.Corruption, 1, 2);
                DepthKit.Steps(w, Delivery.WindupTicks(w.Config) + 1);
                Assert.AreEqual(1, w.Match.Delivered[0].Corruption, c.ToString());
                Assert.AreEqual(0, w.Match.Delivered[0].Biomass, c.ToString());
                Assert.AreEqual(0, w.Actors[0].CarryAmount);
                Assert.AreEqual(2, w.Actors[0].CarryMeat, "meat is biomass: a raider cannot feed it");
            }
        }

        [Test]
        public void U063FarmerBuilderAndHealerNeitherDeliverNorGatherCorruption()
        {
            foreach (ClassId c in new[] { ClassId.Farmer, ClassId.Builder, ClassId.Healer })
            {
                World w = AtShrine(c, CarryKind.Corruption, 1);
                int head = w.EventHead;
                DepthKit.Steps(w, 30);
                Assert.AreEqual(0, w.Match.Delivered[0].Corruption, c.ToString());
                Assert.AreEqual(Roles.RefusedNotRaider, DepthKit.Since(w, head, SimEventType.DeliverRefused)[0].B);

                World g = DepthKit.Field(Cfg, c);
                ResourceNode node = null;
                foreach (ResourceNode n in g.Nodes) if (n.Kind == NodeKind.Corruption) { node = n; break; }
                DepthKit.Put(g, 0, node.X + 1, node.Z);
                g.Inputs[0].Interact = true;
                DepthKit.Steps(g, 150);
                Assert.AreEqual(0, g.Actors[0].CarryAmount, c + " takes no corruption from a node");
            }
            World m = DepthKit.Field(Cfg, ClassId.Miner);
            ResourceNode cn = null;
            foreach (ResourceNode n in m.Nodes) if (n.Kind == NodeKind.Corruption) { cn = n; break; }
            DepthKit.Put(m, 0, cn.X + 1, cn.Z);
            m.Inputs[0].Interact = true;
            DepthKit.Steps(m, 150);
            Assert.AreEqual(CarryKind.Corruption, m.Actors[0].Carry, "a Miner does");
        }

        [Test]
        public void U063ACarrierHandsItsBiomassAndMeatToAFarmerBesideItNotToAWarriorNorAnEnemy()
        {
            World w = DepthKit.Field(Cfg, ClassId.Warrior);
            int farmer = DepthKit.Ally(w, 0), other = DepthKit.Ally(w, 1), enemy = DepthKit.Enemy(w, 0);
            DepthKit.Become(w, farmer, ClassId.Farmer);
            DepthKit.Become(w, other, ClassId.Warrior);
            DepthKit.Become(w, enemy, ClassId.Farmer);
            DepthKit.Put(w, other, 56.9, 40);
            DepthKit.Put(w, enemy, 56, 40.9);
            DepthKit.Put(w, farmer, 57.5, 41);
            Actor a = w.Actors[0];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 1;
            a.CarryMeat = 2;
            w.Actors[farmer].Carry = CarryKind.Biomass;
            w.Actors[farmer].CarryAmount = 1;
            int head = w.EventHead;
            DepthKit.Steps(w, 1);
            Assert.AreEqual(1, a.CarryAmount, "no interact, no hand-off");
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, 1);
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(CarryKind.None, a.Carry);
            Assert.AreEqual(0, a.CarryMeat);
            Assert.AreEqual(2, w.Actors[farmer].CarryAmount);
            Assert.AreEqual(2, w.Actors[farmer].CarryMeat);
            Assert.AreEqual(0, w.Actors[other].CarryAmount);
            Assert.AreEqual(0, w.Actors[enemy].CarryAmount);
            List<SimEvent> ev = DepthKit.Since(w, head, SimEventType.Handoff);
            Assert.AreEqual(2, ev.Count);
            Assert.AreEqual(farmer, ev[0].B);
            Assert.AreEqual(BiomassType.Plant, ev[0].Bio);
            Assert.AreEqual(BiomassType.Meat, ev[1].Bio);
            Assert.AreEqual(w.X[farmer], ev[0].X);
            Assert.AreEqual(3, w.Depth.HandoffUnits);
        }

        [Test]
        public void U063AFullFarmerTakesOnlyWhatFitsAndAFarAwayOneNothing()
        {
            World w = DepthKit.Field(Cfg, ClassId.Miner);
            int farmer = DepthKit.Ally(w, 0);
            DepthKit.Become(w, farmer, ClassId.Farmer);
            DepthKit.Put(w, farmer, 60.5, 40);
            w.Actors[0].Carry = CarryKind.Biomass;
            w.Actors[0].CarryAmount = 1;
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, 3);
            Assert.AreEqual(1, w.Actors[0].CarryAmount, "4.5 units away is out of reach");
            DepthKit.Put(w, farmer, 57.2, 40);
            w.Actors[farmer].Carry = CarryKind.Biomass;
            w.Actors[farmer].CarryAmount = (int)w.Config.Class(ClassId.Farmer).CarryCapacity;
            DepthKit.Steps(w, 3);
            Assert.AreEqual(1, w.Actors[0].CarryAmount, "a full Farmer takes nothing");
        }

        [Test]
        public void U063TheDepthLineupHasAllSixClassesAndTheClassicOneIsUnchanged()
        {
            var roster = new ClassId[16];
            World.BuildRoster(Cfg, 16, null, roster);
            var n = new int[6];
            foreach (ClassId c in roster) n[(int)c]++;
            CollectionAssert.AreEqual(new[] { 5, 4, 2, 2, 1, 2 }, n);
            World.BuildRoster(GameConfig.CreateBrowser5v5(), 16, null, roster);
            n = new int[6];
            foreach (ClassId c in roster) n[(int)c]++;
            Assert.AreEqual(0, n[3] + n[4] + n[5]);
            var five = new ClassId[5];
            World.BuildRoster(Cfg, 5, ClassId.Warrior, five);
            n = new int[6];
            foreach (ClassId c in five) n[(int)c]++;
            CollectionAssert.AreEqual(new[] { 2, 1, 1, 0, 0, 1 }, n, "a small team fields more fighters (SmallTeamClassMix)");
        }

        [Test]
        public void U063BotsObeyOnlyFarmersFeedOnlyRaidersCorruptAndEscortsWalkWithCarriers()
        {
            World w = DepthKit.Make(Cfg, ClassId.Warrior, 7, 16);
            int fed = 0, escortTicks = 0;
            DepthKit.RunBots(w, 5400, e =>
            {
                if (e.Type == SimEventType.DeliverBiomass && e.A >= 0)
                {
                    fed++;
                    Assert.AreEqual(ClassId.Farmer, w.Actors[e.A].Cls, "a " + w.Actors[e.A].Cls + " fed the egg");
                }
                if (e.Type == SimEventType.DeliverCorruption && e.A >= 0) Assert.IsTrue(Roles.MayCorrupt(w, w.Actors[e.A].Cls));
                if (e.Type == SimEventType.Gather)
                {
                    bool corruption = w.Nodes[e.B].Kind == NodeKind.Corruption;
                    ClassId c = w.Actors[e.A].Cls;
                    Assert.IsTrue(corruption ? Roles.MayCorrupt(w, c) : Roles.MayFeed(w, c), c + " gathered " + w.Nodes[e.B].Kind);
                }
                if (e.Type == SimEventType.Respawn)
                    for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].AiState == AiState.Escort) escortTicks++;
            });
            Assert.Greater(fed, 0, "Farmers fed the egg");
            Assert.LessOrEqual(w.Depth.Handoffs, 4, "a supply bot may pass wood or water to a Farmer it meets (U-064); nobody else hands off");
            System.Console.WriteLine("{\"item\":\"U-063\",\"fedEvents\":" + fed + ",\"escortSamples\":" + escortTicks + ",\"deliveredAzure\":" + w.Match.Delivered[0].Biomass + ",\"deliveredCrimson\":" + w.Match.Delivered[1].Biomass + ",\"corruption\":" + (w.Match.Delivered[0].Corruption + w.Match.Delivered[1].Corruption) + "}");
        }

        [Test]
        public void U063TheSameSeedGivesTheSameWorldWithTheRulesOn()
        {
            ulong a = MatchHash.Run(11, 16, 1500, ClassId.Farmer, null, c => c.Depth.Classic = false);
            Assert.AreEqual(a, MatchHash.Run(11, 16, 1500, ClassId.Farmer, null, c => c.Depth.Classic = false));
            Assert.AreNotEqual(a, MatchHash.Run(11, 16, 1500, ClassId.Farmer));
        }
    }
}

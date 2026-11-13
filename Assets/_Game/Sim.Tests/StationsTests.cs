using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>U-065 base stations (CM-84 markers): live class switching (CM-54 cooldown and codes) and class upgrades (CM-39 ladder) at the base.</summary>
    [TestFixture]
    public sealed class StationsTests
    {
        private static GameConfig Cfg => DepthKit.Cfg();

        private static World AtBase(ClassId cls, GameConfig cfg = null)
        {
            World w = DepthKit.Field(cfg ?? Cfg, cls);
            PadDef pad = w.Map.Pads[0];
            DepthKit.Put(w, 0, pad.X + 1.5, pad.Z);
            return w;
        }

        private static void Pick(World w, ClassId cls, int steps = 1)
        {
            w.Inputs[0].ClassPick = (int)cls;
            DepthKit.Steps(w, steps);
        }

        [Test]
        public void U065ALivingActorSwitchesClassAtItsStationKeepsItsHealthShareAndWaitsOutTheCooldown()
        {
            World w = AtBase(ClassId.Warrior);
            Actor a = w.Actors[0];
            a.Hp = 80; // half of 160
            int head = w.EventHead;
            Pick(w, ClassId.Builder);
            Assert.AreEqual(ClassId.Builder, a.Cls);
            Assert.AreEqual(w.Config.Class(ClassId.Builder).MaxHp, a.MaxHp);
            Assert.AreEqual(60, a.Hp, 1.01, "half of 120 (the fountain may add a tick)");
            Assert.IsTrue(a.Alive);
            Assert.AreEqual(1, a.ClassSwitches);
            List<SimEvent> ev = DepthKit.Since(w, head, SimEventType.ClassSwitch);
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(Respawn.ClassSwitchAccepted, ev[0].B);
            Assert.AreEqual((int)ClassId.Builder, ev[0].Value);
            Assert.AreEqual(w.X[0], ev[0].X);
            Assert.GreaterOrEqual(a.AbilityCd, w.Config.Depth.Stations.SwitchAbilityLockSeconds - 0.1);

            Pick(w, ClassId.Healer);
            Assert.AreEqual(ClassId.Builder, a.Cls, "inside the 150-tick cooldown");
            Assert.AreEqual(Respawn.ClassSwitchCooldown, DepthKit.Since(w, head, SimEventType.ClassSwitch)[1].B);
            DepthKit.Steps(w, (int)w.Config.Respawn.ClassSwitchCooldownTicks);
            Pick(w, ClassId.Healer);
            Assert.AreEqual(ClassId.Healer, a.Cls);
            Assert.AreEqual(2, a.ClassSwitches);
            Assert.AreEqual(2, 0 + a.ClassSwitches);
            Assert.AreEqual(0, Respawn.TeamClassSwitches(w, TeamId.Azure), "the human's switches are not the bots'");
        }

        [Test]
        public void U065AwayFromTheStationALivingPickIsRefusedAndClassicDropsItSilently()
        {
            World w = DepthKit.Field(Cfg, ClassId.Warrior, 56, 40);
            int head = w.EventHead;
            Pick(w, ClassId.Miner);
            Assert.AreEqual(ClassId.Warrior, w.Actors[0].Cls);
            List<SimEvent> ev = DepthKit.Since(w, head, SimEventType.ClassSwitch);
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(Respawn.ClassSwitchAlive, ev[0].B);
            Assert.AreEqual(-1, w.Inputs[0].ClassPick, "one-shot");

            World c = AtBase(ClassId.Warrior, GameConfig.CreateBrowser5v5());
            head = c.EventHead;
            Pick(c, ClassId.Miner);
            Assert.AreEqual(ClassId.Warrior, c.Actors[0].Cls);
            Assert.AreEqual(0, DepthKit.Since(c, head, SimEventType.ClassSwitch).Count);
            Assert.AreEqual(0, c.Actors[0].ClassSwitchCd);
        }

        [Test]
        public void U065SwitchingBanksTheLoadAndDropsCorruption()
        {
            World w = AtBase(ClassId.Miner);
            w.Actors[0].Carry = CarryKind.Wood;
            w.Actors[0].CarryAmount = 1;
            w.Terraform.Carried[0] = 2;
            Pick(w, ClassId.Farmer);
            Assert.AreEqual(ClassId.Farmer, w.Actors[0].Cls);
            Assert.AreEqual(0, w.Actors[0].CarryAmount);
            Assert.AreEqual(1, Sources.Stock(w, TeamId.Azure, Sources.StockWood));
            Assert.AreEqual(0, w.Terraform.Carried[0], "dug blocks do not follow into another class");

            World v = AtBase(ClassId.Warrior);
            v.Actors[0].Carry = CarryKind.Corruption;
            v.Actors[0].CarryAmount = 1;
            Pick(v, ClassId.Farmer);
            Assert.AreEqual(CarryKind.None, v.Actors[0].Carry, "a Farmer never holds corruption");
            Assert.AreEqual(0, v.Depth.Banked);
        }

        [Test]
        public void U065ADeadPickQueuesForTheRespawnWithTheCm54Cooldown()
        {
            World w = AtBase(ClassId.Warrior);
            Actor a = w.Actors[0];
            a.Alive = false;
            a.RespawnIn = 3;
            int head = w.EventHead;
            Pick(w, ClassId.Farmer);
            Assert.AreEqual(ClassId.Farmer, a.PendingClass);
            Pick(w, ClassId.Miner);
            Assert.AreEqual(ClassId.Farmer, a.PendingClass, "the second pick is inside the cooldown");
            List<SimEvent> ev = DepthKit.Since(w, head, SimEventType.ClassSwitch);
            Assert.AreEqual(Respawn.ClassSwitchAccepted, ev[0].B);
            Assert.AreEqual(Respawn.ClassSwitchCooldown, ev[1].B);
            DepthKit.Steps(w, 100);
            Assert.IsTrue(a.Alive);
            Assert.AreEqual(ClassId.Farmer, a.Cls);
        }

        [Test]
        public void U065ABuilderBuysAClassLevelForItsWholeTeamPaidInWoodAndWater()
        {
            World w = AtBase(ClassId.Builder);
            int ally = DepthKit.Ally(w, 0), enemy = DepthKit.Enemy(w, 0);
            DepthKit.Become(w, ally, ClassId.Warrior);
            DepthKit.Become(w, enemy, ClassId.Warrior);
            DepthKit.Put(w, ally, 56, 40);
            DepthKit.Put(w, enemy, 60, 44);
            w.Actors[ally].Hp = 100;
            w.Depth.Stock[Sources.StockWood] = 9;
            w.Depth.Stock[Sources.StockWater] = 3;
            int head = w.EventHead;
            w.Inputs[0].StationBuy = (int)ClassId.Warrior + 1;
            DepthKit.Steps(w, 1);
            Assert.AreEqual(0, w.Inputs[0].StationBuy, "one-shot");
            Assert.AreEqual(2, Stations.Level(w, TeamId.Azure, ClassId.Warrior));
            Assert.AreEqual(1, Stations.Level(w, TeamId.Crimson, ClassId.Warrior));
            Assert.AreEqual(184, w.Actors[ally].MaxHp, "160 x 1.15");
            Assert.AreEqual(124, w.Actors[ally].Hp, "healed by the gain");
            Assert.AreEqual(160, w.Actors[enemy].MaxHp);
            Assert.AreEqual(6, Sources.Stock(w, TeamId.Azure, Sources.StockWood));
            Assert.AreEqual(2, Sources.Stock(w, TeamId.Azure, Sources.StockWater));
            SimEvent e = DepthKit.Since(w, head, SimEventType.Upgrade)[0];
            Assert.AreEqual(0, e.A);
            Assert.AreEqual((int)ClassId.Warrior, e.B);
            Assert.AreEqual(2, e.Value);
            Assert.AreEqual(w.Map.Pads[0].X, e.X);

            w.Inputs[0].StationBuy = (int)ClassId.Warrior + 1;
            DepthKit.Steps(w, 1);
            Assert.AreEqual(3, Stations.Level(w, TeamId.Azure, ClassId.Warrior));
            Assert.AreEqual(208, w.Actors[ally].MaxHp, "160 x 1.30");
            Assert.AreEqual(1, Sources.Stock(w, TeamId.Azure, Sources.StockWood));
            w.Depth.Stock[Sources.StockWood] = 20;
            w.Depth.Stock[Sources.StockWater] = 20;
            Assert.AreEqual(Stations.RefusedMaxLevel, Stations.TryUpgrade(w, 0, (int)ClassId.Warrior), "the ladder has three levels");
            Assert.AreEqual(3, Stations.MaxLevel(w.Config));

            // A Warrior that respawns later starts at the bought level.
            Actor al = w.Actors[ally];
            al.Alive = false;
            al.RespawnIn = 0.01;
            DepthKit.Steps(w, 2);
            Assert.IsTrue(al.Alive);
            Assert.AreEqual(208, al.MaxHp);
            Assert.AreEqual(208, al.Hp);
        }

        [Test]
        public void U065OnlyABuilderInItsStationWithEnoughStockMayBuy()
        {
            World w = AtBase(ClassId.Warrior);
            w.Depth.Stock[Sources.StockWood] = 9;
            w.Depth.Stock[Sources.StockWater] = 3;
            Assert.AreEqual(Stations.RefusedNotBuilder, Stations.TryUpgrade(w, 0, 0));
            DepthKit.Become(w, 0, ClassId.Builder);
            DepthKit.Put(w, 0, 56, 40);
            Assert.AreEqual(Stations.RefusedNotAtStation, Stations.TryUpgrade(w, 0, 0));
            PadDef pad = w.Map.Pads[0];
            DepthKit.Put(w, 0, pad.X + 1, pad.Z);
            w.Depth.Stock[Sources.StockWater] = 0;
            int head = w.EventHead;
            Assert.AreEqual(Stations.RefusedNoStock, Stations.TryUpgrade(w, 0, 0));
            Assert.AreEqual(Stations.RefusedNoStock, DepthKit.Since(w, head, SimEventType.Upgrade)[0].Value);
            Assert.AreEqual(9, Sources.Stock(w, TeamId.Azure, Sources.StockWood), "a refusal spends nothing");
            Assert.AreEqual(1, Stations.Level(w, TeamId.Azure, ClassId.Warrior));

            World c = AtBase(ClassId.Builder, GameConfig.CreateBrowser5v5());
            c.Inputs[0].StationBuy = 1;
            head = c.EventHead;
            DepthKit.Steps(c, 1);
            Assert.AreEqual(0, DepthKit.Since(c, head, SimEventType.Upgrade).Count, "classic has no station");
            Assert.AreEqual(160, Stations.MaxHp(c, TeamId.Azure, ClassId.Warrior));
        }

        [Test]
        public void U065BuilderBotsBuyUpgradesFromTheStockTheSupplyRunsFill()
        {
            World w = DepthKit.Make(Cfg, ClassId.Warrior, 7, 16);
            int bought = 0;
            DepthKit.RunBots(w, 7000, e =>
            {
                if (e.Type != SimEventType.Upgrade || e.Value < 2) return;
                bought++;
                Assert.AreEqual(ClassId.Builder, w.Actors[e.A].Cls);
            });
            DepthState d = w.Depth;
            Console.WriteLine("{\"item\":\"U-065\",\"upgrades\":" + bought + ",\"wood\":" + d.Gathered[0] + ",\"water\":" + d.Gathered[1] + ",\"stockAzure\":[" + d.Stock[0] + "," + d.Stock[1] + "],\"stockCrimson\":[" + d.Stock[3] + "," + d.Stock[4] + "]}");
            Assert.GreaterOrEqual(bought, 1);
            Assert.AreEqual(bought, d.UpgradesBought);
        }
    }
}

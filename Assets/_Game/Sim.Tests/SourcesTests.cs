using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>U-064 biomass sources (DG-42, our rule): wood groves and water springs to harvest, carry, feed or bank, and supply bots.</summary>
    [TestFixture]
    public sealed class SourcesTests
    {
        private static GameConfig Cfg => DepthKit.Cfg();

        private static SourceNode First(World w, BiomassType kind)
        {
            foreach (SourceNode s in w.Depth.Sources) if (s.Kind == kind) return s;
            return null;
        }

        private static World At(ClassId cls, BiomassType kind, out SourceNode s)
        {
            World w = DepthKit.Field(Cfg, cls);
            s = First(w, kind);
            DepthKit.Put(w, 0, s.X + 1.2, s.Z);
            w.Inputs[0].Interact = true;
            return w;
        }

        [Test]
        public void U064SourcesSitOnOpenFloorMirroredAndBothPadsCanWalkToThem()
        {
            World w = DepthKit.Field(Cfg, ClassId.Warrior);
            SourceNode[] list = w.Depth.Sources;
            int wood = 0, water = 0;
            var sb = new System.Text.StringBuilder();
            foreach (SourceNode s in list)
            {
                if (s.Kind == BiomassType.Wood) wood++; else water++;
                sb.Append(s.Kind).Append('@').Append(s.X).Append(',').Append(s.Z).Append(' ');
                Assert.AreEqual(Cell.Floor, w.Map.Cells[(int)s.Z * w.Map.Width + (int)s.X]);
                bool mirrored = false;
                foreach (SourceNode o in list) if (o.Kind == s.Kind && Math.Abs(o.X - (w.Map.Width - s.X)) < 1e-9 && o.Z == s.Z) mirrored = true;
                Assert.IsTrue(mirrored || Math.Abs(s.X - w.Map.Width / 2.0) < 1.5, "source " + s.Id + " has no mirror");
                foreach (PadDef p in w.Map.Pads) Assert.IsTrue(double.IsFinite(Sources.FieldDist(w, s.Id, p.X, p.Z)), "pad cannot reach source " + s.Id);
                foreach (ResourceNode n in w.Nodes) Assert.Greater(Math.Abs(n.X - s.X) + Math.Abs(n.Z - s.Z), 3.0);
            }
            Console.WriteLine("{\"item\":\"U-064\",\"sources\":\"" + sb + "\"}");
            Assert.AreEqual(6, wood);
            Assert.AreEqual(5, water);
        }

        [Test]
        public void U064ABuilderChopsAUnitInHalfTheTimeAWarriorNeedsAndAHitRestartsIt()
        {
            World w = At(ClassId.Builder, BiomassType.Wood, out SourceNode s);
            int head = w.EventHead;
            DepthKit.Steps(w, 29);
            Assert.AreEqual(0, w.Actors[0].CarryAmount);
            DepthKit.Steps(w, 1);
            Assert.AreEqual(1, w.Actors[0].CarryAmount, "2 s x 0.5 = 30 ticks");
            Assert.AreEqual(CarryKind.Wood, w.Actors[0].Carry);
            Assert.AreEqual(3, s.Charges);
            SimEvent e = DepthKit.Since(w, head, SimEventType.SourceGather)[0];
            Assert.AreEqual(s.Id, e.B);
            Assert.AreEqual(s.X, e.X);
            Assert.AreEqual(BiomassType.Wood, e.Bio);

            World v = At(ClassId.Warrior, BiomassType.Wood, out SourceNode s2);
            DepthKit.Steps(v, 59);
            Assert.AreEqual(0, v.Actors[0].CarryAmount);
            v.Actors[0].Hp -= 5; // a hit
            DepthKit.Steps(v, 59);
            Assert.AreEqual(0, v.Actors[0].CarryAmount, "the hit restarted the chop");
            DepthKit.Steps(v, 2);
            Assert.AreEqual(1, v.Actors[0].CarryAmount);
            Assert.AreEqual((int)v.Config.Class(ClassId.Warrior).CarryCapacity, 1);
            DepthKit.Steps(v, 100);
            Assert.AreEqual(1, v.Actors[0].CarryAmount, "full hands gather no more");
            Assert.AreEqual(3, s2.Charges);
        }

        [Test]
        public void U064ChargesGrowBackAndKindsDoNotMix()
        {
            World w = At(ClassId.Farmer, BiomassType.Water, out SourceNode s);
            DepthKit.Steps(w, 200);
            Assert.AreEqual(CarryKind.Water, w.Actors[0].Carry);
            Assert.AreEqual(3, w.Actors[0].CarryAmount);
            Assert.AreEqual(0, s.Charges);
            w.Inputs[0].Interact = false;
            DepthKit.Steps(w, (int)w.Config.Depth.Sources.WaterRegrowTicks + 1);
            Assert.GreaterOrEqual(s.Charges, 1);
            SourceNode wood = First(w, BiomassType.Wood);
            w.Actors[0].CarryAmount = 1;
            DepthKit.Put(w, 0, wood.X + 1.2, wood.Z);
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, 200);
            Assert.AreEqual(CarryKind.Water, w.Actors[0].Carry);
            Assert.AreEqual(1, w.Actors[0].CarryAmount, "a water carrier chops no wood");
        }

        [Test]
        public void U064AFarmerFeedsWoodAndWaterAndWaterCoolsTheEggButNotAfterTheLock()
        {
            World w = DepthKit.Field(Cfg, ClassId.Farmer);
            SourcesConfig S = w.Config.Depth.Sources;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 2, 0, BiomassType.Wood);
            Assert.AreEqual(2 * S.WoodAllegiancePerUnit, w.Match.Allegiance.Azure);
            Assert.AreEqual(2, w.Match.Delivered[0].Wood);
            Assert.AreEqual(2, w.Match.Delivered[0].Biomass);
            w.Match.Instability = 10;
            int head = w.EventHead;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 2, 0, BiomassType.Water);
            Assert.AreEqual(10 - 2 * S.WaterCoolPerUnit, w.Match.Instability, 1e-9);
            Assert.AreEqual(2 * S.WoodAllegiancePerUnit + 2 * S.WaterAllegiancePerUnit, w.Match.Allegiance.Azure);
            Assert.AreEqual(BiomassType.Water, DepthKit.Since(w, head, SimEventType.DeliverBiomass)[0].Bio);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 9, 0, BiomassType.Water);
            Assert.AreEqual(0, w.Match.Instability, "never below zero");
            w.Match.Instability = 120;
            w.Match.BerserkLocked = true;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 3, 0, BiomassType.Water);
            Assert.AreEqual(120, w.Match.Instability, "the lock is final");

            World f = DepthKit.Field(Cfg, ClassId.Farmer);
            DepthKit.Put(f, 0, f.Map.Shrine.X, f.Map.Shrine.Z + 2.2);
            f.Actors[0].Carry = CarryKind.Water;
            f.Actors[0].CarryAmount = 3;
            f.Inputs[0].Interact = true;
            DepthKit.Steps(f, Delivery.WindupTicks(f.Config) + 1);
            Assert.AreEqual(3, f.Match.Delivered[0].Water, "carried water goes in as water");
            Assert.AreEqual(0, f.Actors[0].CarryAmount);
        }

        [Test]
        public void U064APressAtTheBaseBanksTheLoadOnceAndWoodPaysTheBuilderFirst()
        {
            World w = DepthKit.Field(Cfg, ClassId.Miner);
            PadDef pad = w.Map.Pads[0];
            DepthKit.Put(w, 0, pad.X + 2, pad.Z);
            Actor a = w.Actors[0];
            a.Carry = CarryKind.Wood;
            a.CarryAmount = 1;
            a.CarryMeat = 2;
            double before = BuiltPieces.Bank(w, TeamId.Azure);
            int head = w.EventHead;
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, 5);
            Assert.AreEqual(1, Sources.Stock(w, TeamId.Azure, Sources.StockWood));
            Assert.AreEqual(2, Sources.Stock(w, TeamId.Azure, Sources.StockFood));
            Assert.AreEqual(0, Sources.Stock(w, TeamId.Crimson, Sources.StockWood));
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(0, a.CarryMeat);
            List<SimEvent> ev = DepthKit.Since(w, head, SimEventType.BankDeposit);
            Assert.AreEqual(2, ev.Count);
            Assert.AreEqual(BiomassType.Wood, ev[0].Bio);
            Assert.AreEqual(pad.X, ev[0].X);
            Assert.AreEqual(before + 1, BuiltPieces.Bank(w, TeamId.Azure), "banked wood is in the Builder's bank");
            Assert.AreEqual(1, Sources.SpendUpTo(w, TeamId.Azure, Sources.StockWood, 3));
            Assert.IsFalse(Sources.TrySpend(w, TeamId.Azure, 1, 0));
            // Outside the base nothing banks.
            a.Carry = CarryKind.Water;
            a.CarryAmount = 1;
            w.Inputs[0].Interact = false;
            DepthKit.Steps(w, 1);
            DepthKit.Put(w, 0, pad.X + 9, pad.Z);
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, 3);
            Assert.AreEqual(1, a.CarryAmount);
            Assert.AreEqual(0, Sources.Stock(w, TeamId.Azure, Sources.StockWater));
        }

        [Test]
        public void U064AFarmerTakesFoodBackOutOfTheStockAndAWarriorCannot()
        {
            World w = DepthKit.Field(Cfg, ClassId.Farmer);
            PadDef pad = w.Map.Pads[0];
            DepthKit.Put(w, 0, pad.X + 2, pad.Z);
            w.Depth.Stock[Sources.StockFood] = 4;
            int head = w.EventHead;
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, 4);
            Assert.AreEqual(3, w.Actors[0].CarryAmount, "a Farmer carries 3");
            Assert.AreEqual(CarryKind.Biomass, w.Actors[0].Carry);
            Assert.AreEqual(1, Sources.Stock(w, TeamId.Azure, Sources.StockFood), "held interact does not put it straight back");
            Assert.AreEqual(1, DepthKit.Since(w, head, SimEventType.BankWithdraw).Count);

            World v = DepthKit.Field(Cfg, ClassId.Warrior);
            DepthKit.Put(v, 0, pad.X + 2, pad.Z);
            v.Depth.Stock[Sources.StockFood] = 4;
            v.Inputs[0].Interact = true;
            DepthKit.Steps(v, 4);
            Assert.AreEqual(0, v.Actors[0].CarryAmount);
        }

        [Test]
        public void U064ClassicHasNoStockAndNoGathering()
        {
            World w = DepthKit.Field(GameConfig.CreateBrowser5v5(), ClassId.Builder);
            SourceNode s = First(w, BiomassType.Wood);
            DepthKit.Put(w, 0, s.X + 1.2, s.Z);
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, 120);
            Assert.AreEqual(0, w.Actors[0].CarryAmount);
            Assert.IsFalse(Sources.On(w));
        }

        [Test]
        public void U064SupplyBotsHarvestAndBankAndTwoPerTeamRunIt()
        {
            World w = DepthKit.Make(Cfg, ClassId.Warrior, 7, 16);
            int maxSupply = 0;
            DepthKit.RunBots(w, 5400, e =>
            {
                if (e.Type != SimEventType.SourceGather) return;
                BotsState st = BotBrain.GetBotsState(w);
                for (int t = 0; t < 2; t++)
                {
                    int c = 0;
                    for (int i = 0; i < w.ActorCount; i++) if ((int)w.Actors[i].Team == t && st.Supply[i] != 0) c++;
                    if (c > maxSupply) maxSupply = c;
                }
                ClassId cls = w.Actors[e.A].Cls;
                Assert.IsTrue(cls == ClassId.Builder || cls == ClassId.Miner || cls == ClassId.Demolitions, cls + " ran supply");
            });
            DepthState d = w.Depth;
            Console.WriteLine("{\"item\":\"U-064\",\"wood\":" + d.Gathered[0] + ",\"water\":" + d.Gathered[1] + ",\"banked\":" + d.Banked + ",\"stockAzure\":[" + d.Stock[0] + "," + d.Stock[1] + "," + d.Stock[2] + "],\"stockCrimson\":[" + d.Stock[3] + "," + d.Stock[4] + "," + d.Stock[5] + "],\"maxSupplyPerTeam\":" + maxSupply + "}");
            Assert.Greater(d.Gathered[0] + d.Gathered[1], 8);
            Assert.Greater(d.Banked, 4);
            Assert.LessOrEqual(maxSupply, 2);
            Assert.GreaterOrEqual(maxSupply, 1);
        }
    }
}

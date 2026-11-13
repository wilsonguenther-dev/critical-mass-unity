using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// U-070 (owner 2026-11-12, supersedes U-063's Farmers-only feeding): every class feeds the egg at every team size; the
    /// Farmer is the best at it (browser carry 3 and gather 1 s, plus BestFeederBonus on its deliveries); a hand-off to a
    /// Farmer or a trip through the team stock pays PassBonus once, on delivery.
    /// </summary>
    [TestFixture]
    public sealed class FeedRuleTests
    {
        private static World AtShrine(ClassId cls, int amount, GameConfig cfg = null)
        {
            World w = DepthKit.Field(cfg ?? DepthKit.Cfg(), cls);
            DepthKit.Put(w, 0, w.Map.Shrine.X, w.Map.Shrine.Z + 2.2);
            Actor a = w.Actors[0];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = amount;
            w.Inputs[0].Interact = true;
            return w;
        }

        [Test]
        public void U070EveryClassFeedsTheEggWithTheDefaultRulesAndNothingIsRefused()
        {
            foreach (ClassId c in ClassIds.All)
            {
                World w = AtShrine(c, 1);
                int head = w.EventHead;
                DepthKit.Steps(w, Delivery.WindupTicks(w.Config) + 1);
                Assert.AreEqual(1, w.Match.Delivered[0].Biomass, c.ToString());
                Assert.IsEmpty(DepthKit.Since(w, head, SimEventType.DeliverRefused), c.ToString());
                Assert.IsTrue(Roles.MayFeed(w, c));
            }
            Assert.IsTrue(GameConfig.CreateDefault().Depth.Roles.MayFeed[(int)ClassId.Warrior], "the game's default: everyone feeds");
        }

        [Test]
        public void U070AFarmersUnitCountsTheBonusAndAnyOtherClassCountsOne()
        {
            World f = AtShrine(ClassId.Farmer, 2);
            World v = AtShrine(ClassId.Warrior, 2);
            double per = f.Config.KaijuFeed.BiomassAllegiancePerUnit * MapEvents.FeedScale(f);
            DepthKit.Steps(f, Delivery.WindupTicks(f.Config) + 1);
            DepthKit.Steps(v, Delivery.WindupTicks(v.Config) + 1);
            Assert.AreEqual(2 * per * (1 + f.Config.Depth.Roles.BestFeederBonus), f.Match.Allegiance.Azure, 1e-9);
            Assert.AreEqual(2 * per, v.Match.Allegiance.Azure, 1e-9);
            Assert.Greater(f.Config.Depth.Roles.BestFeederBonus, 0);
            Assert.IsTrue(Roles.IsBestFeeder(f, ClassId.Farmer));
            Assert.IsFalse(Roles.IsBestFeeder(f, ClassId.Warrior));
        }

        [Test]
        public void U070AHandOffToAFarmerIsOptionalAndPaysThePassBonusOnceOnDelivery()
        {
            World w = DepthKit.Field(DepthKit.Cfg(), ClassId.Warrior);
            int farmer = DepthKit.Ally(w, 0);
            DepthKit.Become(w, farmer, ClassId.Farmer);
            DepthKit.Put(w, 0, 40, 40);
            DepthKit.Put(w, farmer, 41, 40);
            w.Actors[0].Carry = CarryKind.Biomass;
            w.Actors[0].CarryAmount = 1;
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, 1);
            Assert.AreEqual(0, w.Actors[0].CarryAmount, "the Warrior handed it over");
            Assert.AreEqual(1, w.Actors[farmer].CarryAmount);
            Assert.AreEqual(1, w.Depth.PassUnits[farmer]);
            double per = w.Config.KaijuFeed.BiomassAllegiancePerUnit * MapEvents.FeedScale(w);
            RolesConfig R = w.Config.Depth.Roles;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1, farmer);
            Assert.AreEqual(per * (1 + R.BestFeederBonus + R.PassBonus), w.Match.Allegiance.Azure, 1e-9);
            Assert.AreEqual(0, w.Depth.PassUnits[farmer], "paid once");
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1, farmer);
            Assert.AreEqual(per * (2 + 2 * R.BestFeederBonus + R.PassBonus), w.Match.Allegiance.Azure, 1e-9);
        }

        [Test]
        public void U070BankingThenWithdrawingPaysThePassBonusOnlyOnTheDeliveryNeverOnADeposit()
        {
            World w = DepthKit.Field(DepthKit.Cfg(), ClassId.Warrior);
            PadDef pad = w.Map.Pads[0];
            DepthKit.Put(w, 0, pad.X + 2, pad.Z);
            Actor a = w.Actors[0];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 1;
            Assert.IsTrue(Sources.Bank(w, 0));
            Assert.AreEqual(0, w.Match.Allegiance.Azure, "a deposit pays nothing");
            Assert.AreEqual(0, w.Depth.PassUnits[0]);
            Assert.IsTrue(Sources.Bank(w, 0), "any class takes food back out (U-070)");
            Assert.AreEqual(1, a.CarryAmount);
            Assert.AreEqual(1, w.Depth.PassUnits[0]);
            Assert.IsTrue(Sources.Bank(w, 0), "putting it back");
            Assert.AreEqual(0, w.Depth.PassUnits[0], "a bank-and-withdraw loop earns nothing");
            Assert.AreEqual(0, w.Match.Allegiance.Azure);
        }

        [Test]
        public void U070ClassicAndTheBonusesAtZeroScaleAllegianceByExactlyOne()
        {
            World c = DepthKit.Field(GameConfig.CreateBrowser5v5(), ClassId.Farmer);
            Assert.AreEqual(1.0, Roles.AllegianceScale(c, 0, 3));
            World z = DepthKit.Field(DepthKit.Cfg(d => { d.Roles.BestFeederBonus = 0; d.Roles.PassBonus = 0; }), ClassId.Farmer);
            z.Depth.PassUnits[0] = 3;
            Assert.AreEqual(1.0, Roles.AllegianceScale(z, 0, 3));
        }
    }
}

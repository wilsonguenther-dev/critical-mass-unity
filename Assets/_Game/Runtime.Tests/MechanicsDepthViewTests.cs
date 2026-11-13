using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Mechanics;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// feat/mechanics-depth-view (rules PR #58, U-063 to U-068): the event-to-visual table, the "your job now" line per class
    /// and state, the station panel's buy and switch rules, the ping and purchase frame writing, and the power-up timers.
    /// Pure: no scene. Passes with CM_SIM_MECHANICS off (the frame checks then assert the "no field" answer) and on.
    /// </summary>
    public sealed class MechanicsDepthViewTests
    {
        [TearDown]
        public void Clean()
        {
            DepthInputLatch.Clear();
            DepthInputLatch.SuppressButtons = false;
        }

        // ---------------------------------------------------------------- event-to-visual table

        [Test]
        public void EverySpecEventMapsToAVisualOrAnExplicitNone()
        {
            Assert.AreEqual(25, DepthEvents.SpecNames.Length);
            CollectionAssert.AllItemsAreUnique(DepthEvents.SpecNames);
            int[] bs = { -2, -1, 0, 1, 2, 3 };
            double[] vs = { -5, -4, -3, -2, -1, 0, 1, 2, 3, 180 };
            foreach (string name in DepthEvents.SpecNames)
                foreach (int b in bs)
                    foreach (double v in vs)
                        Assert.AreNotEqual(DepthVisual.Unmapped, DepthEvents.VisualForName(name, b, v), name + " b=" + b + " v=" + v);
            Assert.AreEqual(DepthVisual.Unmapped, DepthEvents.VisualFor(-1, 0, 0));
            Assert.AreEqual(DepthVisual.Unmapped, DepthEvents.VisualForName("NotAnEvent", 0, 0));
        }

        [Test]
        public void TheTableTellsSuccessFromRefusal()
        {
            Assert.AreEqual(DepthVisual.SwitchFlash, DepthEvents.VisualForName("ClassSwitch", 0, 2));
            Assert.AreEqual(DepthVisual.SwitchRefusal, DepthEvents.VisualForName("ClassSwitch", 1, 2));
            Assert.AreEqual(DepthVisual.SwitchRefusal, DepthEvents.VisualForName("ClassSwitch", 2, 2));
            Assert.AreEqual(DepthVisual.UpgradeBanner, DepthEvents.VisualForName("Upgrade", 0, 2));
            Assert.AreEqual(DepthVisual.UpgradeBanner, DepthEvents.VisualForName("Upgrade", 0, 3));
            Assert.AreEqual(DepthVisual.UpgradeRefusal, DepthEvents.VisualForName("Upgrade", 0, -3));
            Assert.AreEqual(DepthVisual.CoinScatter, DepthEvents.VisualForName("PickupSpawned", 0, 5));
            Assert.AreEqual(DepthVisual.PowerBeam, DepthEvents.VisualForName("PickupSpawned", 1, 600));
            Assert.AreEqual(DepthVisual.CoinPop, DepthEvents.VisualForName("PickupTaken", 0, 5));
            Assert.AreEqual(DepthVisual.PowerAura, DepthEvents.VisualForName("PickupTaken", 3, 450));
            Assert.AreEqual(DepthVisual.WardShatter, DepthEvents.VisualForName("PowerUpEnded", 3, 1));
            Assert.AreEqual(DepthVisual.AuraEnd, DepthEvents.VisualForName("PowerUpEnded", 1, 0));
            Assert.AreEqual(DepthVisual.BridgeRepair, DepthEvents.VisualForName("BridgeRepaired", 0, 2));
            Assert.AreEqual(DepthVisual.None, DepthEvents.VisualForName("BridgeRepaired", -1, -1), "a called-off collapse draws nothing");
            Assert.AreEqual(DepthVisual.RepairRefusal, DepthEvents.VisualForName("BridgeRepaired", 0, -2));
            Assert.AreEqual(DepthVisual.GatherChop, DepthEvents.VisualForName("SourceGather", 0, 1, -1));
            if (DepthEvents.BioWater >= 0)
                Assert.AreEqual(DepthVisual.GatherSplash, DepthEvents.VisualForName("SourceGather", 0, 1, DepthEvents.BioWater));
            Assert.AreEqual(DepthVisual.None, DepthEvents.DeliveryVisual(null));
            Assert.AreEqual(DepthVisual.None, DepthEvents.DeliveryVisual(BiomassType.Plant));
        }

        [Test]
        public void EveryDepthVisualIsReachable()
        {
            var seen = new HashSet<DepthVisual>();
            int[] bs = { -1, 0, 1, 2, 3 };
            double[] vs = { -2, -1, 0, 1, 2, 3 };
            for (int ix = 0; ix < DepthEvents.SpecNames.Length; ix++)
                foreach (int b in bs)
                    foreach (double v in vs)
                    {
                        seen.Add(DepthEvents.VisualFor(ix, b, v));
                        seen.Add(DepthEvents.VisualFor(ix, b, v, Math.Max(0, DepthEvents.BioWater)));
                    }
            foreach (DepthVisual d in Enum.GetValues(typeof(DepthVisual)))
            {
                if (d == DepthVisual.Unmapped || d == DepthVisual.CoolPulse) continue;
                if (d == DepthVisual.GatherSplash && DepthEvents.BioWater < 0) continue;
                Assert.IsTrue(seen.Contains(d), d + " has no event");
            }
        }

        [Test]
        public void EventCodesAreFoundByNameOrAreAbsentTogether()
        {
            int present = 0;
            foreach (int c in DepthEvents.Codes) if (c >= 0) present++;
            Assert.IsTrue(present == 0 || present == DepthEvents.Codes.Length, "a half-merged rules lane: " + present + " of " + DepthEvents.Codes.Length);
            if (DepthRead.Compiled) Assert.AreEqual(DepthEvents.Codes.Length, present, "CM_SIM_MECHANICS is on but the events are missing");
            for (int i = 0; i < DepthEvents.Codes.Length; i++)
                if (DepthEvents.Codes[i] >= 0) Assert.AreEqual(i, DepthEvents.IndexOf(DepthEvents.Codes[i]));
        }

        // ---------------------------------------------------------------- the "your job now" line

        static ObjectiveFacts Facts(ClassId cls, bool rules = true) => new ObjectiveFacts { Cls = cls, Alive = true, RulesOn = rules, MayFeed = true };

        [Test]
        public void ObjectiveWhenDownIsToPickAClass()
        {
            ObjectiveFacts f = Facts(ClassId.Warrior);
            f.Alive = false;
            Assert.AreEqual(ObjectiveId.Respawn, Objective.For(f));
        }

        [Test]
        public void AnyClassMayFeedAndTheFarmerIsJustBest()
        {
            foreach (ClassId c in ClassIds.All)
            {
                ObjectiveFacts f = Facts(c);
                f.Carry = CarryKind.Biomass;
                f.Units = 2;
                ObjectiveId id = Objective.For(f);
                Assert.AreEqual(c == ClassId.Farmer ? ObjectiveId.FeedEgg : ObjectiveId.FeedOrPass, id, c.ToString());
                string words = Objective.Words(id, c);
                StringAssert.DoesNotContain("cannot", words.ToLowerInvariant());
                StringAssert.DoesNotContain("only farmers", words.ToLowerInvariant());
                StringAssert.Contains("Feed the egg", words);
            }
            ObjectiveFacts meat = Facts(ClassId.Miner);
            meat.Meat = 1;
            Assert.AreEqual(ObjectiveId.FeedOrPass, Objective.For(meat));
        }

        [Test]
        public void WithoutTheRulesEveryCarrierFeeds()
        {
            ObjectiveFacts f = Facts(ClassId.Warrior, false);
            f.Carry = CarryKind.Biomass;
            f.Units = 1;
            Assert.AreEqual(ObjectiveId.FeedEgg, Objective.For(f));
        }

        [Test]
        public void ASimRoleFlagThatRefusesFeedingSuggestsPassingNeverForbids()
        {
            ObjectiveFacts f = Facts(ClassId.Warrior);
            f.Carry = CarryKind.Biomass;
            f.Units = 1;
            f.MayFeed = false;
            Assert.AreEqual(ObjectiveId.PassOrBank, Objective.For(f));
            StringAssert.DoesNotContain("cannot", Objective.Words(ObjectiveId.PassOrBank, ClassId.Warrior).ToLowerInvariant());
        }

        [Test]
        public void ObjectiveCoversEveryState()
        {
            ObjectiveFacts f = Facts(ClassId.Farmer);
            f.Carry = CarryKind.Biomass; f.Units = 1; f.EggExposed = true;
            Assert.AreEqual(ObjectiveId.FeedExposed, Objective.For(f));

            f = Facts(ClassId.Warrior);
            f.Carry = CarryKind.Corruption; f.Units = 1;
            Assert.AreEqual(ObjectiveId.Corruption, Objective.For(f));

            f = Facts(ClassId.Builder);
            f.NearBrokenBridge = true; f.WoodStock = 2;
            Assert.AreEqual(ObjectiveId.Repair, Objective.For(f));
            f.WoodStock = 1;
            Assert.AreEqual(ObjectiveId.RepairWood, Objective.For(f));

            f = Facts(ClassId.Builder);
            f.InStation = true; f.BuilderCanBuy = true;
            Assert.AreEqual(ObjectiveId.BuyUpgrade, Objective.For(f));

            f = Facts(ClassId.Farmer);
            f.InBase = true; f.FoodStock = 3;
            Assert.AreEqual(ObjectiveId.TakeFood, Objective.For(f));

            f = Facts(ClassId.Healer);
            f.DropActive = true;
            Assert.AreEqual(ObjectiveId.ClaimDrop, Objective.For(f));

            foreach (ClassId c in ClassIds.All)
            {
                ObjectiveFacts idle = Facts(c);
                Assert.AreEqual(ObjectiveId.ClassIdle, Objective.For(idle), c.ToString());
                Assert.IsNotEmpty(Objective.Words(ObjectiveId.ClassIdle, c));
                idle.Hatched = true;
                Assert.AreEqual(ObjectiveId.ClassHatched, Objective.For(idle), c.ToString());
                Assert.IsNotEmpty(Objective.Words(ObjectiveId.ClassHatched, c));
            }
            // rules off: no station, stock or map-event objectives
            f = Facts(ClassId.Builder, false);
            f.InStation = true; f.BuilderCanBuy = true; f.DropActive = true;
            Assert.AreEqual(ObjectiveId.ClassIdle, Objective.For(f));
        }

        [Test]
        public void CarryingWoodOrWaterSaysWhereItGoes()
        {
            if (DepthEvents.CarryWood < 0) Assert.Ignore("This base has no CarryKind.Wood (CM_SIM_MECHANICS lane not merged).");
            ObjectiveFacts f = Facts(ClassId.Miner);
            f.Carry = (CarryKind)DepthEvents.CarryWood; f.Units = 1;
            Assert.AreEqual(ObjectiveId.BankWood, Objective.For(f));
            f.Carry = (CarryKind)DepthEvents.CarryWater;
            Assert.AreEqual(ObjectiveId.Water, Objective.For(f));
        }

        [Test]
        public void ObjectiveFactsFromARealWorldNeverThrow()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 3, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = GameConfig.CreateDefault() });
            ObjectiveFacts f = Objective.Facts(w, 0);
            Assert.AreEqual(ClassId.Builder, f.Cls);
            Assert.AreEqual(DepthRead.On(w), f.RulesOn);
            Assert.IsNotEmpty(Objective.Words(Objective.For(f), f.Cls));
            Assert.AreEqual(default(ObjectiveFacts).Cls, Objective.Facts(w, -1).Cls);
        }

        // ---------------------------------------------------------------- station panel rules

        [Test]
        public void OnlyABuilderInTheStationWithStockMayBuy()
        {
            Assert.AreEqual(StationRules.Buy.Ok, StationRules.CanBuy(true, true, true, true, 3, 1, 3, 1));
            Assert.AreEqual(StationRules.Buy.NotBuilder, StationRules.CanBuy(true, false, true, true, 9, 9, 3, 1));
            Assert.AreEqual(StationRules.Buy.NotBuilder, StationRules.CanBuy(false, true, true, true, 9, 9, 3, 1), "a knocked-out Builder");
            Assert.AreEqual(StationRules.Buy.NotAtStation, StationRules.CanBuy(true, true, false, true, 9, 9, 3, 1));
            Assert.AreEqual(StationRules.Buy.MaxLevel, StationRules.CanBuy(true, true, true, false, 9, 9, 0, 0));
            Assert.AreEqual(StationRules.Buy.NeedStock, StationRules.CanBuy(true, true, true, true, 2, 1, 3, 1));
            Assert.AreEqual(StationRules.Buy.NeedStock, StationRules.CanBuy(true, true, true, true, 3, 0, 3, 1));
            // the sim's codes (Stations.Refused*) map onto the same states
            Assert.AreEqual(StationRules.Buy.MaxLevel, StationRules.FromSimCode(-2));
            Assert.AreEqual(StationRules.Buy.NeedStock, StationRules.FromSimCode(-3));
            Assert.AreEqual(StationRules.Buy.NotBuilder, StationRules.FromSimCode(-4));
            Assert.AreEqual(StationRules.Buy.NotAtStation, StationRules.FromSimCode(-5));
            Assert.IsNull(StationRules.Words(StationRules.Buy.Ok));
            foreach (StationRules.Buy b in new[] { StationRules.Buy.NotBuilder, StationRules.Buy.NotAtStation, StationRules.Buy.MaxLevel, StationRules.Buy.NeedStock })
                Assert.IsNotEmpty(StationRules.Words(b), b.ToString());
        }

        [Test]
        public void SwitchingClassNeedsTheStationAndRespectsTheLimit()
        {
            Assert.AreEqual(StationRules.Switch.Ok, StationRules.CanSwitch(true, true, ClassId.Warrior, ClassId.Builder, 0));
            Assert.AreEqual(StationRules.Switch.Current, StationRules.CanSwitch(true, true, ClassId.Warrior, ClassId.Warrior, 0));
            Assert.AreEqual(StationRules.Switch.Cooldown, StationRules.CanSwitch(true, true, ClassId.Warrior, ClassId.Builder, 30));
            Assert.AreEqual(StationRules.Switch.NotAtStation, StationRules.CanSwitch(true, false, ClassId.Warrior, ClassId.Builder, 0));
            Assert.AreEqual(StationRules.Switch.Ok, StationRules.CanSwitch(false, false, ClassId.Warrior, ClassId.Builder, 30), "down: the respawn pick");
            GameConfig c = GameConfig.CreateDefault();
            Assert.AreEqual(5, StationRules.Seconds((int)c.Respawn.ClassSwitchCooldownTicks, c.SimHz), "the 5 s limit on screen");
            Assert.AreEqual(1, StationRules.Seconds(1, 30));
            Assert.AreEqual(0, StationRules.Seconds(0, 30));
        }

        [Test]
        public void PanelFocusWalksTilesThenRows()
        {
            Assert.AreEqual(1, StationPanel.Step(0, Vector2.right));
            Assert.AreEqual(2, StationPanel.Step(2, Vector2.right));
            Assert.AreEqual(3, StationPanel.Step(0, Vector2.down));
            Assert.AreEqual(6, StationPanel.Step(4, Vector2.down));
            Assert.AreEqual(7, StationPanel.Step(6, Vector2.down));
            Assert.AreEqual(11, StationPanel.Step(11, Vector2.down));
            Assert.AreEqual(3, StationPanel.Step(6, Vector2.up));
            Assert.AreEqual(0, StationPanel.Step(3, Vector2.up));
        }

        // ---------------------------------------------------------------- ping and purchase frame writing

        [Test]
        public void APingIsWrittenForExactlyOneTick()
        {
            Assert.IsTrue(DepthInputLatch.QueuePing(3, 12.5, 40.25));
            Assert.IsTrue(DepthInputLatch.PingPending);
            InputFrame f = SimInput.EmptyInput();
            DepthInputLatch.WriteInto(f);
            Assert.IsFalse(DepthInputLatch.PingPending);
            bool compiled = DepthRead.ReadInput(f, out int buy, out int kind, out double x, out double z);
            Assert.AreEqual(DepthRead.Compiled, compiled);
            if (compiled)
            {
                Assert.AreEqual(4, kind, "PingKind = call + 1");
                Assert.AreEqual(12.5, x, 1e-9);
                Assert.AreEqual(40.25, z, 1e-9);
                Assert.AreEqual(0, buy);
            }
            InputFrame next = SimInput.EmptyInput();
            DepthInputLatch.WriteInto(next);
            DepthRead.ReadInput(next, out _, out int kind2, out _, out _);
            Assert.AreEqual(0, kind2, "the next tick carries no ping");
        }

        [Test]
        public void BadPingsAndPurchasesAreRefusedAtTheLatch()
        {
            Assert.IsFalse(DepthInputLatch.QueuePing(4, 0, 0));
            Assert.IsFalse(DepthInputLatch.QueuePing(-1, 0, 0));
            Assert.IsFalse(DepthInputLatch.QueuePing(0, double.NaN, 0));
            Assert.IsFalse(DepthInputLatch.QueueStationBuy(-1));
            Assert.IsFalse(DepthInputLatch.QueueStationBuy(ClassIds.All.Count));
            Assert.IsFalse(DepthInputLatch.PingPending);
            Assert.AreEqual(0, DepthInputLatch.StationBuyPending);
        }

        [Test]
        public void APurchaseIsClassIndexPlusOneForOneTick()
        {
            Assert.IsTrue(DepthInputLatch.QueueStationBuy(4));
            Assert.AreEqual(5, DepthInputLatch.StationBuyPending);
            DepthInputLatch.Take(out int buy, out int kind, out _, out _);
            Assert.AreEqual(5, buy);
            Assert.AreEqual(0, kind);
            DepthInputLatch.Take(out int buy2, out _, out _, out _);
            Assert.AreEqual(0, buy2);
        }

        [Test]
        public void HumanFillCarriesTheLatchForOneTick()
        {
            var s = new HumanInputState();
            InputFrame f = SimInput.EmptyInput();
            DepthInputLatch.QueuePing(0, 5, 6);
            DepthInputLatch.QueueStationBuy(1);
            s.Fill(f);
            Assert.IsFalse(DepthInputLatch.PingPending, "Fill consumed the ping");
            Assert.AreEqual(0, DepthInputLatch.StationBuyPending, "Fill consumed the purchase");
            if (DepthRead.ReadInput(f, out int buy, out int kind, out _, out _)) { Assert.AreEqual(1, kind); Assert.AreEqual(2, buy); }
            s.Fill(f);
            if (DepthRead.ReadInput(f, out int buy2, out int kind2, out _, out _)) { Assert.AreEqual(0, kind2); Assert.AreEqual(0, buy2); }
        }

        [Test]
        public void TheStationPanelHoldsTheFaceButtons()
        {
            InputFrame f = SimInput.EmptyInput();
            f.Attack = true; f.Ability = true; f.Jump = true; f.Interact = true;
            DepthInputLatch.SuppressButtons = true;
            DepthInputLatch.WriteInto(f);
            Assert.IsFalse(f.Attack);
            Assert.IsFalse(f.Ability);
            Assert.IsFalse(f.Jump);
            Assert.IsTrue(f.Interact, "interact still banks and hands off");
        }

        // ---------------------------------------------------------------- power-up timers

        [Test]
        public void PowerUpTimersCountDownAndEnd()
        {
            Assert.AreEqual(1f, PowerTimers.Left01(100, 0, 400, 300), 1e-5f);
            Assert.AreEqual(0.5f, PowerTimers.Left01(250, 0, 400, 300), 1e-5f);
            Assert.AreEqual(0f, PowerTimers.Left01(450, 0, 400, 300), 1e-5f);
            Assert.AreEqual(0f, PowerTimers.Left01(100, 0, 400, 0));
            Assert.Less(PowerTimers.Left01(250, 0.5, 400, 300), PowerTimers.Left01(250, 0, 400, 300));
            Assert.AreEqual(10, PowerTimers.SecondsLeft(100, 400, 30));
            Assert.AreEqual(1, PowerTimers.SecondsLeft(399, 400, 30));
            Assert.AreEqual(0, PowerTimers.SecondsLeft(400, 400, 30));
            Assert.IsTrue(PowerTimers.Active(1, 100, 101));
            Assert.IsFalse(PowerTimers.Active(1, 101, 101));
            Assert.IsFalse(PowerTimers.Active(0, 0, 500), "0 = no power-up");
            Assert.IsFalse(PowerTimers.Active(4, 0, 500));
            Assert.AreEqual(0.5f, PowerTimers.WardShell01(30, 60), 1e-5f);
            Assert.AreEqual(0f, PowerTimers.WardShell01(-5, 60));
            Assert.AreEqual(1f, PowerTimers.Fly01(1f, 0.5f));
            Assert.AreEqual(0f, PowerTimers.Fly01(0f, 0.5f));
            Assert.AreEqual(4, PowerTimers.Icon.Length);
            Assert.AreEqual(4, PowerTimers.Colour.Length);
        }

        [Test]
        public void AuraColoursStayOutsideBothTeamHues()
        {
            Color az = Hud.Style.CMTokens.Azure.Color, cr = Hud.Style.CMTokens.Crimson.Color;
            for (int k = 1; k < 4; k++)
            {
                Color c = PowerTimers.Colour[k];
                Assert.Greater(Dist(c, az), 0.25f, "kind " + k + " too close to Azure");
                Assert.Greater(Dist(c, cr), 0.25f, "kind " + k + " too close to Crimson");
            }
        }

        static float Dist(Color a, Color b) => Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

        // ---------------------------------------------------------------- words

        [Test]
        public void EveryWordComesFromTheOneTableAndThePackCanRenameIt()
        {
            string before = DepthWords.ObjFeedEgg, beforeHaste = DepthWords.PickupName[1];
            try
            {
                int n = DepthWords.ApplyPack("{\"words\":[{\"id\":\"ObjFeedEgg\",\"text\":\"Feed it\"},{\"id\":\"PickupName.1\",\"text\":\"ZOOM\"},{\"id\":\"Nope\",\"text\":\"x\"}]}");
                Assert.AreEqual(2, n);
                Assert.AreEqual("Feed it", Objective.Words(ObjectiveId.FeedEgg, ClassId.Farmer));
                Assert.AreEqual("ZOOM", DepthWords.Pickup(1));
            }
            finally
            {
                DepthWords.ObjFeedEgg = before;
                DepthWords.PickupName[1] = beforeHaste;
            }
            Assert.AreEqual(6, DepthWords.RoleLine.Length);
            Assert.AreEqual(4, DepthWords.PickupName.Length);
            Assert.AreEqual(4, DepthWords.CallShort.Length);
            StringAssert.Contains(DepthWords.Role(ClassId.Farmer), DepthWords.WithRoleLine("Gatherer", ClassId.Farmer));
        }

        [Test]
        public void FeedLinesNameTheCallerOrSayYou()
        {
            DepthNames.Clear();
            Assert.AreEqual("You: Help!", DepthLines.Call(9001, 0, 0, 3, "WARRIOR"));
            Assert.AreEqual("Warrior: On my way", DepthLines.Call(9001, 4, 0, 1, "WARRIOR"));
            Assert.AreEqual("YOU", DepthNames.RowLabel(0, true, "YOU", "WARRIOR"));
            Assert.AreEqual("MINER", DepthNames.RowLabel(3, false, "YOU", "MINER"), "no names bound: the class word");
        }
    }
}

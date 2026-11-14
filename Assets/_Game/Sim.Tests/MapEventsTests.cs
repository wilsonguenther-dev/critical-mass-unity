using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>U-068 map events: supply drop, egg exposed, bridge collapse and repair.</summary>
    [TestFixture]
    public sealed class MapEventsTests
    {
        private static GameConfig Cfg => DepthKit.Cfg();

        /// <summary>The U-068 rotation with bridge collapse switched back on (it is off by default since 2026-11-13).</summary>
        private static GameConfig WithCollapse => DepthKit.Cfg(d => d.Events.BridgeCollapse = true);
        private static GameConfig NoSchedule => DepthKit.Cfg(d => d.Events.FirstEventTicks = 1e8);

        [Test]
        public void U068TheScheduleIsFixedByTheSeedAndShowsAllThreeKinds()
        {
            string Run(int seed)
            {
                World w = DepthKit.Field(WithCollapse, ClassId.Warrior, 20, 40, seed);
                var sb = new System.Text.StringBuilder();
                int head = w.EventHead;
                for (int s = 0; s < 3; s++)
                {
                    DepthKit.Steps(w, s == 0 ? 1401 : 1800);
                    for (int i = head; i < w.EventHead; i++)
                    {
                        SimEvent e = w.Events[i % World.EventCapacity];
                        if (e.Type == SimEventType.SupplyDropIncoming || e.Type == SimEventType.EggExposed || e.Type == SimEventType.BridgeWarning)
                            sb.Append(e.Type).Append('@').Append(w.Tick - (s == 0 ? 0 : 0)).Append(' ');
                    }
                    head = w.EventHead;
                }
                return sb.ToString();
            }
            string a = Run(63), b = Run(63);
            Console.WriteLine("{\"item\":\"U-068\",\"schedule\":\"" + a + "\"}");
            Assert.AreEqual(a, b);
            StringAssert.Contains("SupplyDropIncoming", a);
            StringAssert.Contains("EggExposed", a);
            StringAssert.Contains("BridgeWarning", a);
        }

        [Test]
        public void U068ASupplyDropIsAnnouncedLandsAndIsClaimedByStandingOnItUncontested()
        {
            World w = DepthKit.Field(NoSchedule, ClassId.Warrior, 20, 40);
            MapEventsConfig E = w.Config.Depth.Events;
            int head = w.EventHead;
            MapEvents.Start(w, MapEvents.KindSupplyDrop, 0);
            SimEvent inc = DepthKit.Since(w, head, SimEventType.SupplyDropIncoming)[0];
            Assert.AreEqual(w.Map.Width / 2.0, inc.X, "on the centre line");
            Assert.AreEqual(w.Tick + E.DropWarnTicks, inc.Value);
            int enemy = DepthKit.Enemy(w, 0);
            DepthKit.Put(w, 0, inc.X - 1, inc.Z);
            DepthKit.Put(w, enemy, inc.X + 1, inc.Z);
            DepthKit.Steps(w, (int)E.DropWarnTicks + 1);
            Assert.AreEqual(1, DepthKit.Since(w, head, SimEventType.SupplyDropLanded).Count);
            DepthKit.Steps(w, 200);
            Assert.AreEqual(MapEvents.ContestBoth, w.Depth.DropContest);
            Assert.AreEqual(0, w.Depth.DropsClaimed, "a contested drop ticks for nobody");
            DepthKit.Put(w, enemy, 90, 40);
            DepthKit.Steps(w, (int)E.DropClaimTicks - 1);
            Assert.AreEqual(0, w.Depth.DropsClaimed);
            DepthKit.Steps(w, 2);
            SimEvent cl = DepthKit.Since(w, head, SimEventType.SupplyDropClaimed)[0];
            Assert.AreEqual(0, cl.A);
            Assert.AreEqual(0, cl.B);
            Assert.AreEqual(4, Sources.Stock(w, TeamId.Azure, Sources.StockWood));
            Assert.AreEqual(2, Sources.Stock(w, TeamId.Azure, Sources.StockWater));
            Assert.AreEqual(0, Sources.Stock(w, TeamId.Crimson, Sources.StockWood));
            Assert.AreEqual(10, w.Actors[0].CoinsPicked);
            List<SimEvent> co = DepthKit.Since(w, head, SimEventType.SupplyDropContest);
            Assert.AreEqual(MapEvents.ContestBoth, co[0].Value);
            Assert.AreEqual(MapEvents.ContestAzure, co[1].Value);
            DepthKit.Steps(w, (int)E.DropPowerUpDelayTicks + 2);
            Assert.AreNotEqual(PickupKind.Coin, Pickups.PowerOf(w, 0), "the power-up on the spot");

            World v = DepthKit.Field(NoSchedule, ClassId.Warrior, 20, 40);
            head = v.EventHead;
            MapEvents.Start(v, MapEvents.KindSupplyDrop, 0);
            DepthKit.Steps(v, (int)(E.DropWarnTicks + E.DropLifetimeTicks) + 3);
            Assert.AreEqual(1, DepthKit.Since(v, head, SimEventType.SupplyDropExpired).Count);
            Assert.AreEqual(MapEvents.DropNone, v.Depth.DropState);
        }

        [Test]
        public void U068WhileTheEggIsExposedEveryDeliveryCountsDouble()
        {
            World w = DepthKit.Field(NoSchedule, ClassId.Farmer, 20, 40);
            MapEventsConfig E = w.Config.Depth.Events;
            double per = w.Config.KaijuFeed.BiomassAllegiancePerUnit;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            Assert.AreEqual(per, w.Match.Allegiance.Azure);
            int head = w.EventHead;
            MapEvents.Start(w, MapEvents.KindEgg, 0);
            SimEvent e = DepthKit.Since(w, head, SimEventType.EggExposed)[0];
            Assert.AreEqual(w.Map.Shrine.X, e.X);
            Assert.AreEqual(w.Tick + E.EggWindowTicks, e.Value);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            Assert.AreEqual(3 * per, w.Match.Allegiance.Azure);
            DepthKit.Steps(w, (int)E.EggWindowTicks + 1);
            Assert.AreEqual(1, DepthKit.Since(w, head, SimEventType.EggExposedEnded).Count);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            Assert.AreEqual(4 * per, w.Match.Allegiance.Azure);
        }

        [Test]
        public void U068ABridgeCollapsesAfterItsWarningNeverCutsABaseOffAndABuilderRebuildsItForWood()
        {
            World w = DepthKit.Field(NoSchedule, ClassId.Builder, 20, 40);
            MapEventsConfig E = w.Config.Depth.Events;
            DepthState d = w.Depth;
            int head = w.EventHead;
            MapEvents.Start(w, MapEvents.KindBridge, 0);
            SimEvent warn = DepthKit.Since(w, head, SimEventType.BridgeWarning)[0];
            Assert.AreEqual(d.BrokenSpan, warn.B);
            Assert.AreEqual(w.Tick + E.BridgeWarnTicks, warn.Value);
            Assert.AreEqual(Cell.Bridge, w.Map.Cells[d.BrokenIdx[0]]);
            int cell = d.BrokenIdx[d.BrokenN / 2];
            DepthKit.Put(w, 0, cell % w.Map.Width + 0.5, cell / w.Map.Width + 0.5);
            DepthKit.Steps(w, (int)E.BridgeWarnTicks + 1);
            SimEvent down = DepthKit.Since(w, head, SimEventType.BridgeCollapsed)[0];
            Assert.AreEqual(d.BrokenN, down.Radius);
            for (int i = 0; i < d.BrokenN; i++) Assert.AreEqual(Cell.Void, w.Map.Cells[d.BrokenIdx[i]]);
            Assert.IsTrue(Terraform.PadsReachHill(w.Map), "both bases still reach the hill");
            DepthKit.Steps(w, 30);
            Assert.GreaterOrEqual(DepthKit.Since(w, head, SimEventType.Fall).Count, 1, "who stood on it fell and climbed out");
            Assert.IsTrue(w.Actors[0].Alive);
            Assert.AreNotEqual(Cell.Void, w.Map.Cells[(int)w.Z[0] * w.Map.Width + (int)w.X[0]]);
            Console.WriteLine("{\"item\":\"U-068\",\"span\":" + d.BrokenSpan + ",\"cells\":" + d.BrokenN + ",\"at\":\"" + down.X + "," + down.Z + "\"}");

            // No wood: refused. With wood: 3 s of interact at a broken end.
            BridgeSpan s = BridgeSpans.All[d.BrokenSpan];
            DepthKit.Steps(w, 60);
            DepthKit.Put(w, 0, d.BridgeAx - s.Ux * 1.2, d.BridgeAz - s.Uz * 1.2);
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, (int)E.BridgeRepairTicks + 5);
            Assert.AreEqual(MapEvents.BridgeBroken, d.BridgeState);
            List<SimEvent> rep = DepthKit.Since(w, head, SimEventType.BridgeRepaired);
            Assert.AreEqual(1, rep.Count);
            Assert.AreEqual(-2, rep[0].Value);
            w.Depth.Stock[Sources.StockWood] = 3;
            DepthKit.Steps(w, (int)E.BridgeRepairTicks - 1);
            Assert.AreEqual(MapEvents.BridgeBroken, d.BridgeState);
            DepthKit.Steps(w, 2);
            Assert.AreEqual(MapEvents.BridgeNone, d.BridgeState);
            rep = DepthKit.Since(w, head, SimEventType.BridgeRepaired);
            Assert.AreEqual(0, rep[1].A);
            Assert.AreEqual(2, rep[1].Value);
            Assert.AreEqual(1, Sources.Stock(w, TeamId.Azure, Sources.StockWood));
            for (int i = 0; i < d.BrokenN; i++) Assert.AreEqual(Cell.Bridge, w.Map.Cells[d.BrokenIdx[i]]);

            // Nobody repairs: it rebuilds itself.
            World v = DepthKit.Field(NoSchedule, ClassId.Warrior, 20, 40);
            head = v.EventHead;
            MapEvents.Start(v, MapEvents.KindBridge, 0);
            DepthKit.Steps(v, (int)(E.BridgeWarnTicks + E.BridgeAutoRepairTicks) + 3);
            List<SimEvent> auto = DepthKit.Since(v, head, SimEventType.BridgeRepaired);
            Assert.AreEqual(1, auto.Count);
            Assert.AreEqual(-1, auto[0].A);
            Assert.AreEqual(1, v.Depth.BridgesRepaired);
        }

        [Test]
        public void U068EverySpanTheCollapseMayPickKeepsBothBasesConnected()
        {
            int ok = 0;
            for (int k = 0; k < 16; k++)
            {
                World w = DepthKit.Field(NoSchedule, ClassId.Warrior, 20, 40);
                MapEvents.Start(w, MapEvents.KindBridge, k);
                if (w.Depth.BridgeState != MapEvents.BridgeWarned) continue;
                DepthKit.Steps(w, (int)w.Config.Depth.Events.BridgeWarnTicks + 1);
                Assert.IsTrue(Terraform.PadsReachHill(w.Map), "span " + w.Depth.BrokenSpan);
                ok++;
            }
            Assert.AreEqual(16, ok, "every event number found a span it may drop");
        }

        [Test]
        public void U068ClassicHasNoMapEvents()
        {
            World w = DepthKit.Field(GameConfig.CreateBrowser5v5(), ClassId.Warrior, 20, 40);
            int head = w.EventHead;
            DepthKit.Steps(w, 3500);
            Assert.AreEqual(0, w.Depth.EventIndex);
            Assert.AreEqual(0, DepthKit.Since(w, head, SimEventType.SupplyDropIncoming).Count + DepthKit.Since(w, head, SimEventType.BridgeWarning).Count + DepthKit.Since(w, head, SimEventType.EggExposed).Count);
            Assert.AreEqual(1, MapEvents.FeedScale(w));
        }

        [Test]
        public void U068InABotMatchTheEventsRunAndBotsClaimDrops()
        {
            World w = DepthKit.Make(Cfg, ClassId.Warrior, 7, 16);
            int falls = 0;
            DepthKit.RunBots(w, 7200, e =>
            {
                if (e.Type == SimEventType.Fall) falls++;
            });
            DepthState d = w.Depth;
            Console.WriteLine("{\"item\":\"U-068\",\"events\":" + d.EventIndex + ",\"dropsClaimed\":" + d.DropsClaimed + ",\"eggWindows\":" + d.EggWindows + ",\"bridgesDown\":" + d.BridgesCollapsed + ",\"bridgesBack\":" + d.BridgesRepaired + ",\"falls\":" + falls + "}");
            Assert.GreaterOrEqual(d.EventIndex, 3);
            Assert.IsTrue(Terraform.PadsReachHill(w.Map));
        }

        /// <summary>
        /// 2026-11-13 ("the bridges have random holes"): by default no scheduled event breaks a bridge. A 16v16 bot match of
        /// 4 minutes (four events, so the bridge turn comes up) has no BridgeWarning or BridgeCollapsed, no span is
        /// warned or broken at the end (digs and blasts may still change bridge cells; that is not an event); the
        /// bridge turn is a quiet minute, the other kinds still run. Start(KindBridge) still works for the rule's tests.
        /// </summary>
        [Test]
        public void ByDefaultNoScheduledEventBreaksABridgeTheBridgeTurnIsAQuietMinute()
        {
            Assert.IsFalse(GameConfig.CreateDefault().Depth.Events.BridgeCollapse, "off in the game's default config");
            World w = DepthKit.Make(Cfg, ClassId.Warrior, 7, 16);
            int warn = 0, down = 0, drops = 0;
            DepthKit.RunBots(w, 7200, e =>
            {
                if (e.Type == SimEventType.BridgeWarning) warn++;
                if (e.Type == SimEventType.BridgeCollapsed) down++;
                if (e.Type == SimEventType.SupplyDropIncoming) drops++;
            });
            Assert.GreaterOrEqual(w.Depth.EventIndex, 4, "the schedule ran (four events cover the three-kind rotation)");
            Assert.AreEqual(0, warn, "no bridge warning");
            Assert.AreEqual(0, down, "no bridge collapse");
            Assert.AreEqual(0, w.Depth.BridgesCollapsed);
            Assert.AreEqual(MapEvents.BridgeNone, w.Depth.BridgeState, "no span warned or broken at the end");
            Assert.Greater(drops, 0, "the other kinds still run (a supply drop came)");
        }
    }
}

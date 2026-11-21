using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 item 4 (systems audit defect 5): the views of the five systems that do nothing in the default game are hidden
    /// behind named flags (DeadSystemViews), come back when their flag is set or when the rule that makes them live is on,
    /// and the sim is not touched (the checksum rows are the sim gate's job; here: the flags read the config only).
    /// </summary>
    [TestFixture]
    public sealed class DeadSystemViewsTests
    {
        static World Make(System.Action<GameConfig> tweak = null)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            tweak?.Invoke(cfg);
            return World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        [TearDown]
        public void Reset()
        {
            DeadSystemViews.ShowCoins = DeadSystemViews.ShowBankedWater = DeadSystemViews.ShowStationLadder = false;
            DeadSystemViews.ShowBridgeRepair = DeadSystemViews.ShowMinerBarricades = false;
        }

        [Test]
        public void InTheDefaultGameEveryDeadSystemIsHidden()
        {
            World w = Make();
            Assert.IsTrue(Blacksmith.On(w), "the hut rule is on by default (the reason three of them are dead)");
            Assert.IsFalse(w.Config.Depth.Events.BridgeCollapse, "bridge collapse is off by default");
            Assert.IsFalse(DeadSystemViews.CoinsShown(), "coins");
            Assert.IsFalse(DeadSystemViews.BankedWaterShown(w), "banked water");
            Assert.IsFalse(DeadSystemViews.StationLadderShown(w), "station ladder");
            Assert.IsFalse(DeadSystemViews.BridgeRepairShown(w), "bridge repair");
            Assert.IsFalse(DeadSystemViews.MinerBarricadesShown(w), "miner barricades");
            Assert.IsFalse(DeadSystemViews.MinerBarricadesShown(), "miner barricades in the menus (default rules)");
        }

        [Test]
        public void EachViewComesBackWhenItsRuleIsLiveOrItsFlagIsSet()
        {
            World noHut = Make(c => c.Depth.Hut.On = false);
            Assert.IsTrue(DeadSystemViews.BankedWaterShown(noHut), "hut off: water pays the station ladder");
            Assert.IsTrue(DeadSystemViews.StationLadderShown(noHut));
            Assert.IsTrue(DeadSystemViews.MinerBarricadesShown(noHut));
            Assert.IsTrue(DeadSystemViews.BridgeRepairShown(Make(c => c.Depth.Events.BridgeCollapse = true)));
            Assert.IsFalse(DeadSystemViews.CoinsShown(), "coins have no sink in any config");

            World w = Make();
            DeadSystemViews.ShowCoins = true;
            DeadSystemViews.ShowBankedWater = true;
            DeadSystemViews.ShowStationLadder = true;
            DeadSystemViews.ShowBridgeRepair = true;
            DeadSystemViews.ShowMinerBarricades = true;
            Assert.IsTrue(DeadSystemViews.CoinsShown());
            Assert.IsTrue(DeadSystemViews.BankedWaterShown(w));
            Assert.IsTrue(DeadSystemViews.StationLadderShown(w));
            Assert.IsTrue(DeadSystemViews.BridgeRepairShown(w));
            Assert.IsTrue(DeadSystemViews.MinerBarricadesShown(w));
        }

        [Test]
        public void TheMinersSetupLineNamesHaulNotBarricade()
        {
            string line = FlowWords.AbilityLine(ClassId.Miner);
            Debug.Log("[b15 dead systems] Miner setup line: " + line);
            StringAssert.DoesNotContain("Barricade", line);
            StringAssert.Contains("Haul", line);
            DeadSystemViews.ShowMinerBarricades = true;
            StringAssert.DoesNotContain("Haul", FlowWords.AbilityLine(ClassId.Miner), "the flag brings the pack's word back");
        }

        [Test]
        public void TheRewardsPanelDropsTheCoinColumnAndCoinOnlyLines()
        {
            var input = new MatchStatsInput { KnockOuts = 3, HillSeconds = 40, CoinsPicked = 12, Completed = true, Outcome = MatchOutcome.Win };
            MatchRewards r = RewardRules.Compute(input);
            int coinOnly = 0;
            foreach (RewardLine l in r.Lines) if (l.Coins > 0 && l.SkillPoints <= 0) coinOnly++;
            var go = new GameObject("Rewards Under Test", typeof(RectTransform));
            try
            {
                RewardsPanel p = RewardsPanel.Create(go.transform, 0f, 0f);
                p.Show(r);
                Assert.IsFalse(p.CoinColumnShown, "no coin column while coins buy nothing");
                Debug.Log($"[b15 dead systems] rewards lines {r.Lines.Count}, coin-only lines hidden {coinOnly}");
                DeadSystemViews.ShowCoins = true;
                p.Show(r);
                Assert.IsTrue(p.CoinColumnShown, "the flag brings the coin column back");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

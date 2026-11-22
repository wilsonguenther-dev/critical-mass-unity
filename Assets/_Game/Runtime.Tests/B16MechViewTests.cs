using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Mechanics;
using CriticalMass.Runtime.Online;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>b16 mechanics fixes (was B16ViewTests on feat/b16-mechanics-fixes; renamed at the batch 16 combine, the physics branch owns B16ViewTests), the view and meta side (items 2, 4, 7, 8d, 9 of the batch 16 mechanics brief).</summary>
    public sealed class B16MechViewTests
    {
        static MatchStatsInput Done(MatchOutcome o) => new MatchStatsInput { KnockOuts = 3, Outcome = o, Completed = true };

        [Test]
        public void ALeaveIsALossAndPracticeIsCountedApartByOneRule()
        {
            var p = new PlayerProfile();
            RewardRules.Apply(RewardRules.Compute(Done(MatchOutcome.Win)), p);
            MatchStatsInput practice = Done(MatchOutcome.Win);
            practice.Practice = true;
            RewardRules.Apply(RewardRules.Compute(practice), p);
            var left = new MatchStatsInput { KnockOuts = 9, Outcome = MatchOutcome.Win, Left = true };
            MatchRewards lr = RewardRules.Compute(left);
            Assert.AreEqual(0, lr.Coins + lr.SkillPoints, "a leave pays nothing");
            RewardRules.Apply(lr, p);
            Assert.AreEqual(2, p.MatchesPlayed, "the win and the leave; practice apart");
            Assert.AreEqual(1, p.Wins);
            Assert.AreEqual(1, p.Losses, "the leave is a loss even while winning");
            Assert.AreEqual(1, p.PracticeMatches);
            Assert.IsTrue(MatchCounting.Counts(left) && !MatchCounting.Counts(practice));
            Assert.AreEqual(MatchOutcome.Loss, MatchCounting.Outcome(left));
            var quit = new MatchStatsInput();
            Assert.IsFalse(MatchCounting.Counts(quit), "an unfinished match that was not left (host left) counts nowhere");

            // the class career and the history follow the same rule
            MetaRecorder.RecordLeave(p, ClassId.Miner, TeamId.Azure, new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc));
            ClassMastery m = p.MasteryOf(ClassId.Miner);
            Assert.AreEqual(1, m.Matches);
            Assert.AreEqual(1, m.Losses);
            MatchRecord rec = p.History[p.History.Length - 1];
            Assert.IsTrue(rec.Left);
            Assert.AreEqual(0, rec.Result, "a loss row");
            Assert.AreEqual(0, rec.MasteryXp, "no mastery for a leave");
        }

        [Test]
        public void TheHostLeftScreenSaysNoResult()
        {
            StringAssert.Contains("NO RESULT", OnlineMatchOverlay.HostLeftTitle);
            StringAssert.Contains("neither a win nor a loss", OnlineMatchOverlay.HostLeftBody);
        }

        [Test]
        public void TheMinerGoalNamesTheHutCardsNotAFlatBonus()
        {
            string miner = MatchWords.ClassGoals[(int)ClassId.Miner];
            StringAssert.DoesNotContain("whole team stronger", miner);
            StringAssert.Contains("votes for a perk card", miner);
        }

        [Test]
        public void TheHillCountReadsCountedOverCap()
        {
            Assert.AreEqual("HILL 7/10", GameHud.CapWord(7, 10));
            Assert.AreEqual("HILL 10/10", GameHud.CapWord(14, 10), "past the cap still reads the cap");
            Assert.AreEqual("HILL 3/5", GameHud.CapWord(3, 5));
            Assert.AreEqual("5+ ON HILL", GameHud.CapWord(9, 0), "no cap: today's words");
        }

        [Test]
        public void TheHillPipsAgreeWithTheHillNumber()
        {
            // b16 finish (Editor review: "7/10" with 4 pips): one pip per counted point, a Warrior fills two
            Assert.AreEqual(10, GameHud.Pips);
            Assert.AreEqual(7, GameHud.PipsLit(4, 7, 10), "3 Warriors + 1: 4 bodies, 7 points, 7 pips");
            Assert.AreEqual(8, GameHud.PipsLit(4, 8, 10), "4 Warriors: 8 pips");
            Assert.AreEqual(10, GameHud.PipsLit(11, 10, 10), "capped: the whole row");
            Assert.AreEqual(3, GameHud.PipsLit(3, 3, 5));
            Assert.AreEqual(5, GameHud.PipsShown(5), "a cap of 5 draws 5 pips");
            Assert.AreEqual(10, GameHud.PipsShown(10));
            Assert.AreEqual(10, GameHud.PipsShown(0));
            Assert.AreEqual(6, GameHud.PipsLit(3, 3, 0), "no cap: two pips a body");
            Assert.AreEqual(10, GameHud.PipsLit(9, 9, 0), "no cap: 5+ fills the row, as the words say");
            for (int counted = 0; counted <= 14; counted++)
                StringAssert.StartsWith("HILL " + GameHud.PipsLit(0, counted, 10) + "/", GameHud.CapWord(counted, 10), "pips == number at " + counted);
        }

        [Test]
        public void TheIdlePanelAndTheTipsHaveTheirWords()
        {
            // b16 finish: the idle panel and the AUTOPILOT banner use one wording
            Assert.AreEqual(CriticalMass.Runtime.Spectator.PlayerPresence.BannerWhat(false), B16Words.AfkHead);
            StringAssert.Contains("COMPUTER IS PLAYING YOUR FIGHTER", B16Words.AfkHead);
            StringAssert.Contains("TO PLAY", B16Words.AfkLine);
            Assert.AreEqual(6, B16Words.Ids.Length, "own Kaiju on the circle, rally, hut vote, forward respawn, hill cap, why you died");
            foreach (string id in B16Words.Ids) Assert.IsFalse(string.IsNullOrEmpty(B16Words.Head(id)), id);
            StringAssert.Contains("can't build", CriticalMass.Runtime.View.Affordance.AffordanceScan.HillKeepoutWord);
        }

        [Test]
        public void WaterAndCoinCountersStayHidden()
        {
            Assert.IsFalse(DeadSystemViews.ShowCoins, "coins have no sink: hidden (state kept)");
            Assert.IsFalse(DeadSystemViews.ShowBankedWater, "banked water has no spender with the hut rule: hidden");
        }
    }
}

using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// U-063..U-068 soak: the outcome bands again with the mechanics-depth rules ON (the classic bands in OutcomesSoakTests
    /// run with them off, bit for bit as before). Soak lane only; prints every number.
    /// </summary>
    [TestFixture, Category("Slow")]
    public sealed class DepthSoakTests
    {
        private static void DepthOn(GameConfig c) => c.Depth.Classic = false;

        private static string Line(string name, List<MatchOutcome> list)
        {
            int azure = 0, crimson = 0, draw = 0, changes = 0;
            double owned = 0, minOwned = 1;
            foreach (MatchOutcome o in list)
            {
                if (o.Winner == MatchResult.Azure) azure++;
                else if (o.Winner == MatchResult.Crimson) crimson++;
                else draw++;
                owned += o.OwnedShare;
                if (o.OwnedShare < minOwned) minOwned = o.OwnedShare;
                changes += o.OwnerChanges;
            }
            return "{\"item\":\"depth-soak\",\"batch\":\"" + name + "\",\"matches\":" + list.Count + ",\"azure\":" + azure + ",\"crimson\":" + crimson + ",\"draw\":" + draw
                + ",\"ownedShareMean\":" + (owned / list.Count).ToString("F3") + ",\"ownedShareMin\":" + minOwned.ToString("F3") + ",\"ownerChangesMean\":" + ((double)changes / list.Count).ToString("F1") + "}";
        }

        [Test, Category("Slow")]
        public void DepthB16v16TheFourSeedsKeepTheOwnedShareBand()
        {
            var list = new List<MatchOutcome>();
            for (int seed = 101; seed <= 104; seed++) list.Add(OutcomeRunner.RunOutcomeMatch(seed, 16, HumanMode.Bot, DepthOn));
            Console.WriteLine(Line("B-16v16-depth", list));
            foreach (MatchOutcome o in list) Assert.GreaterOrEqual(o.OwnedShare, OutcomeThresholds.OwnedShareMin, "seed " + o.Seed);
        }

        [Test, Category("Slow")]
        public void DepthA5v5TheTwentySeedsStayDecisiveSwingyAndBalanced()
        {
            var list = new List<MatchOutcome>();
            for (int seed = 1; seed <= 20; seed++) list.Add(OutcomeRunner.RunOutcomeMatch(seed, 5, HumanMode.Bot, DepthOn));
            Console.WriteLine(Line("A-5v5-depth", list));
            int decisive = 0, azure = 0, crimson = 0;
            double owned = 0;
            foreach (MatchOutcome o in list)
            {
                if (o.Winner != MatchResult.Draw) decisive++;
                if (o.Winner == MatchResult.Azure) azure++;
                if (o.Winner == MatchResult.Crimson) crimson++;
                owned += o.OwnedShare;
            }
            Assert.GreaterOrEqual((double)decisive / list.Count, OutcomeThresholds.Decisive);
            Assert.GreaterOrEqual(owned / list.Count, OutcomeThresholds.OwnedShare);
            Assert.LessOrEqual(Math.Max(azure, crimson), OutcomeThresholds.MaxTeamWins);
        }

        [Test, Category("Slow")]
        public void DepthPassiveAHumanWhoStandsStillStillWinsAtLeast15PercentOf200Seeds()
        {
            int wins = 0;
            for (int seed = 201; seed < 401; seed++)
                if (OutcomeRunner.RunOutcomeMatch(seed, 5, HumanMode.Passive, DepthOn).Winner == MatchResult.Azure) wins++;
            Console.WriteLine("{\"item\":\"depth-soak\",\"batch\":\"passive-200-depth\",\"passiveTeamWins\":" + wins + ",\"of\":200}");
            Assert.GreaterOrEqual(wins / 200.0, 0.15);
        }
    }
}

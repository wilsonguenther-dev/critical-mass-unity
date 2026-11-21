using System.Collections.Generic;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 view polish, Editor review item 5: the profile header said 66 matches (the account counter, practice and legacy
    /// matches included) while CAREER said 1 (the class's career), and the stats line repeated KNOCK-OUTS and HILL TIME for
    /// the Warrior. One source (the class careers), the CAREER line names its class, each stat shows once.
    /// </summary>
    public sealed class ProfileCountsTests
    {
        static PlayerProfile Reviewers()
        {
            // the Editor profile the reviewer saw: an old account counter far above the class careers
            var p = new PlayerProfile { PlayerName = "BOOB", MatchesPlayed = 66, Wins = 29, Draws = 0, Losses = 37 };
            ClassMastery w = p.MasteryOf(ClassId.Warrior);
            w.Matches = 1; w.Wins = 1; w.KnockOuts = 14; w.HillSeconds = 112;
            ClassMastery f = p.MasteryOf(ClassId.Farmer);
            f.Matches = 2; f.Wins = 0; f.Losses = 2;
            return p;
        }

        [Test]
        public void HeaderAndCareerReadOneSource()
        {
            PlayerProfile p = Reviewers();
            string head = ProfileMenuScreen.HeaderLine(p);
            string career = ProfileMenuScreen.CareerHead(ClassId.Warrior, p.MasteryOf(ClassId.Warrior));
            Debug.Log($"[b15 profile] header '{head}' | career '{career}'");
            StringAssert.Contains("3 matches", head, "the header sums the class careers");
            StringAssert.DoesNotContain("66 matches", head, "not the legacy account counter");
            StringAssert.StartsWith("WARRIOR " + MetaWords.Career, career, "the CAREER line names its class");
            StringAssert.Contains("1 matches", career);
            ProfileMenuScreen.CareerTotals(p, out int n, out int wins, out int d, out int l);
            Assert.AreEqual(n, wins + d + l, "every career match has one result");
        }

        [Test]
        public void TheStatsLineNamesEachStatOnce()
        {
            foreach (ClassId cls in ClassIds.All)
            {
                RoleStat[] stats = ProfileMenuScreen.CareerStats(cls);
                var seen = new HashSet<RoleStat>();
                foreach (RoleStat r in stats) Assert.IsTrue(seen.Add(r), cls + " repeats " + r);
                Assert.IsTrue(seen.Contains(RoleStat.KnockOuts) && seen.Contains(RoleStat.HillSeconds), cls + " keeps knock-outs and hill time");
            }
            PlayerProfile p = Reviewers();
            string line = ProfileMenuScreen.CareerStatsLine(ClassId.Warrior, p.MasteryOf(ClassId.Warrior));
            Debug.Log($"[b15 profile] warrior stats '{line}' (limit: each stat once)");
            string ko = MetaWords.StatValue(RoleStat.KnockOuts, 14);
            Assert.AreEqual(line.IndexOf(ko, System.StringComparison.Ordinal), line.LastIndexOf(ko, System.StringComparison.Ordinal), "knock-outs once");
        }
    }
}

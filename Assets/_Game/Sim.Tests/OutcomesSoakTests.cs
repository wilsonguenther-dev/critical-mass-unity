using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/bots/outcomes.slow.test.ts (09e0904): do bots produce a real match? Soak lane only (U-034: reported, never a gate).</summary>
    [TestFixture, Category("Slow")]
    public sealed class OutcomesSoakTests
    {
        /// <summary>D-030 passive sweep, sturdier form: 200 passive 5v5 seeds from 201, share floor 0.15 (was 11 of 50, outcomes.slow.test.ts line 150).</summary>
        private const int PassiveSweepFirstSeed = 201;
        private const int PassiveSweepSeeds = 200;
        private const double PassiveSweepShareMin = 0.15;

        private static IReadOnlyList<MatchOutcome> A => SoakCache.Batch("A", HumanMode.Bot);
        private static IReadOnlyList<MatchOutcome> B => SoakCache.Batch("B", HumanMode.Bot);
        private static IReadOnlyList<MatchOutcome> C => SoakCache.Batch("C", HumanMode.Passive);
        private static IReadOnlyList<MatchOutcome> AfkBot => SoakCache.Batch("AFK", HumanMode.Bot);
        private static IReadOnlyList<MatchOutcome> AfkPassive => SoakCache.Batch("AFK", HumanMode.Passive);
        private static IReadOnlyList<MatchOutcome> AfkWander => SoakCache.Batch("AFK", HumanMode.Wander);

        private static double Mean(IReadOnlyList<MatchOutcome> list, Func<MatchOutcome, double> f)
        {
            if (list.Count == 0) return 0;
            double s = 0;
            for (int i = 0; i < list.Count; i++) s += f(list[i]);
            return s / list.Count;
        }

        /// <summary>Matches of the first n entries of list won by team (n &lt; 0: all).</summary>
        private static int Wins(IReadOnlyList<MatchOutcome> list, MatchResult team, int n = -1)
        {
            int end = n < 0 ? list.Count : Math.Min(n, list.Count);
            int c = 0;
            for (int i = 0; i < end; i++)
                if (list[i].Winner == team) c++;
            return c;
        }

        private static bool HatchedAllied(MatchOutcome o)
        {
            return o.Hatch == KaijuState.Azure || o.Hatch == KaijuState.Crimson;
        }

        private static List<MatchOutcome> AllAB()
        {
            var all = new List<MatchOutcome>(A);
            all.AddRange(B);
            return all;
        }

        [Test, Category("Slow")]
        public void ADecisiveGe80PercentOfMatchesAreNotADraw()
        {
            IReadOnlyList<MatchOutcome> a = A;
            Assert.AreEqual(OutcomeBatches.A.Count, a.Count);
            int n = 0;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Winner == MatchResult.Azure || a[i].Winner == MatchResult.Crimson) n++;
            double decisive = (double)n / a.Count;
            Assert.GreaterOrEqual(decisive, OutcomeThresholds.Decisive);
        }

        [Test, Category("Slow")]
        public void AOwnedShareMeanShareOfSamplesOwnedByATeamContestedAndNeutralExcludedGe04()
        {
            Assert.GreaterOrEqual(Mean(A, o => o.OwnedShare), OutcomeThresholds.OwnedShare);
        }

        [Test, Category("Slow")]
        public void AOwnedShareEverySingleMatchGe025()
        {
            foreach (MatchOutcome o in A)
                Assert.GreaterOrEqual(o.OwnedShare, OutcomeThresholds.OwnedShareMin, "seed " + o.Seed);
        }

        [Test, Category("Slow")]
        public void ASwingyMeanHillOwnerChangesPerMatchGe4()
        {
            Assert.GreaterOrEqual(Mean(A, o => o.OwnerChanges), OutcomeThresholds.OwnerChangesMean);
        }

        [Test, Category("Slow")]
        public void ABalancedNeitherTeamWinsMoreThan14Of20()
        {
            int most = Math.Max(Wins(A, MatchResult.Azure), Wins(A, MatchResult.Crimson));
            Assert.LessOrEqual(most, OutcomeThresholds.MaxTeamWins);
        }

        [Test, Category("Slow")]
        public void AKaijuDutyMeanBotsPerTeamInGatherDeliverCorruptDuringT20240Ge15()
        {
            Assert.GreaterOrEqual(Mean(A, o => o.KaijuDuty), OutcomeThresholds.KaijuDutyMean);
        }

        [Test, Category("Slow")]
        public void BOwnedShareEverySingleMatchGe025()
        {
            IReadOnlyList<MatchOutcome> b = B;
            Assert.AreEqual(OutcomeBatches.B.Count, b.Count);
            foreach (MatchOutcome o in b)
                Assert.GreaterOrEqual(o.OwnedShare, OutcomeThresholds.OwnedShareMin, "seed " + o.Seed);
        }

        [Test, Category("Slow")]
        public void APlusBDifferentGe2DistinctNaturalHatchOutcomes()
        {
            List<MatchOutcome> all = AllAB();
            Assert.AreEqual(OutcomeBatches.A.Count + OutcomeBatches.B.Count, all.Count);
            var distinct = new HashSet<KaijuState>();
            foreach (MatchOutcome o in all)
                if (o.Hatch != null) distinct.Add(o.Hatch.Value);
            Assert.GreaterOrEqual(distinct.Count, OutcomeThresholds.DistinctHatchOutcomes);
        }

        [Test, Category("Slow")]
        public void APlusBTheKaijuSwingsFightsButDoesNotDecideThemAnAlliedHatchSTeamWinsLe85PercentOfThoseMatches()
        {
            int allied = 0;
            int alliedWins = 0;
            foreach (MatchOutcome o in AllAB())
            {
                if (!HatchedAllied(o)) continue;
                allied++;
                if (SimEnumMap.TeamOf(o.Winner) == SimEnumMap.TeamOf(o.Hatch.Value)) alliedWins++;
            }
            double share = allied > 0 ? (double)alliedWins / allied : 0;
            Assert.LessOrEqual(share, OutcomeThresholds.AlliedWinShareMax);
        }

        [Test, Category("Slow")]
        public void CRunsIts10PassiveSeedsWinShareAssertedOnThe50SeedPassiveSweepD030()
        {
            Assert.AreEqual(OutcomeBatches.C.Count, C.Count);
        }

        [Test, Category("Slow")]
        public void AFKThePassiveBatchReplaysBatchCExactlySameSeedsSameOutcomes()
        {
            IReadOnlyList<MatchOutcome> passive = AfkPassive;
            IReadOnlyList<MatchOutcome> c = C;
            Assert.AreEqual(OutcomeBatches.AFK.Count, passive.Count);
            Assert.AreEqual(OutcomeBatches.C.Count, c.Count);
            for (int i = 0; i < OutcomeBatches.C.Count; i++)
                Assert.AreEqual(c[i].Winner, passive[i].Winner, "seed " + c[i].Seed);
        }

        /// <summary>
        /// D-030, sturdier form (DG-24/40 PR, proposed in the DG-21/22 PR): the 50-seed sweep's floor of 11 wins had no margin
        /// (p = 0.22 over 50 seeds: one standard deviation is 2.9 wins, so a rule change flipping one seed turned it red). Now
        /// 200 passive 5v5 seeds (201..400) pooled, and the passive human's team must win at least PassiveSweepShareMin of
        /// them: about 2.4 standard deviations under the measured share. Prints the count.
        /// </summary>
        [Test, Category("Slow")]
        public void CD030APassiveHumanSTeamStillWinsAtLeast15PercentOf200PassiveSeeds201400()
        {
            int wins = 0;
            for (int seed = PassiveSweepFirstSeed; seed < PassiveSweepFirstSeed + PassiveSweepSeeds; seed++)
            {
                if (OutcomeRunner.RunOutcomeMatch(seed, 5, HumanMode.Passive).Winner == MatchResult.Azure) wins++;
            }
            Console.WriteLine("{\"item\":\"D-030\",\"passiveTeamWins\":" + wins + ",\"of\":" + PassiveSweepSeeds + "}");
            Assert.GreaterOrEqual((double)wins / PassiveSweepSeeds, PassiveSweepShareMin);
        }

        [Test, Category("Slow")]
        public void AFKOnBatchCSSeedsANonContributingActor0PassiveOrWanderingNeverWinsMoreThanACompetentOne()
        {
            int n = OutcomeBatches.C.Count;
            int played = Wins(AfkBot, MatchResult.Azure, n);
            Assert.LessOrEqual(Wins(AfkPassive, MatchResult.Azure, n) - played, OutcomeThresholds.AfkWinsOverPlayingMax);
            Assert.LessOrEqual(Wins(AfkWander, MatchResult.Azure, n) - played, OutcomeThresholds.AfkWinsOverPlayingMax);
        }

        [Test, Category("Slow")]
        public void AFKOnAll50SeedsANonContributingActor0PassiveOrWanderingNeverWinsMoreThanACompetentOne()
        {
            int played = Wins(AfkBot, MatchResult.Azure);
            Assert.LessOrEqual(Wins(AfkPassive, MatchResult.Azure) - played, OutcomeThresholds.AfkWinsOverPlayingMax);
            Assert.LessOrEqual(Wins(AfkWander, MatchResult.Azure) - played, OutcomeThresholds.AfkWinsOverPlayingMax);
        }
    }
}

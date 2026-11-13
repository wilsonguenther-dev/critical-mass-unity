using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// DG-16b slow batches: ports of src/sim/delivery.slow.test.ts (cm/CM-64) and cleanse.slow.test.ts (cm/CM-36), with
    /// the DG-16b row's own gate (intercepts in at least 80 of 100 seeds). 5v5, seeds 1..100, every slot on bots (actor 0 on
    /// autopilot), shipped config, full matches for the delivery batch, to the lock or hatch for the cleanse batch.
    /// </summary>
    [TestFixture, Category("Slow")]
    public sealed class RulesDg16bSoakTests
    {
        private const int Seeds = 100;
        private const double ShareMin = 0.1;
        private const double ShareMax = 0.5;
        /// <summary>DG-16b acceptance: seeds (of 100) with at least one intercepted delivery.</summary>
        private const int InterceptSeedsMin = 80;

        private static readonly InputFrame Frame = SimInput.EmptyInput();

        private sealed class DeliveryRun
        {
            public int Seed;
            public double InterceptedAzure, InterceptedCrimson, Delivered;
            public KaijuState? Hatch;
            public bool BerserkLocked;
            public MatchResult Winner;
        }

        private static List<DeliveryRun> _runs;

        private static List<DeliveryRun> Runs()
        {
            if (_runs != null) return _runs;
            var runs = new List<DeliveryRun>();
            for (int seed = 1; seed <= Seeds; seed++)
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5();
                World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz;
                KaijuState? hatch = null;
                for (int s = 0; s < max && w.Match.Phase != MatchPhase.Ended; s++)
                {
                    MatchTick.Advance(w, null, Frame);
                    if (hatch == null && w.Match.Hatched) hatch = w.Kaiju.State;
                }
                MatchState m = w.Match;
                DeliveredTally a = m.Delivered[(int)TeamId.Azure];
                DeliveredTally c = m.Delivered[(int)TeamId.Crimson];
                runs.Add(new DeliveryRun
                {
                    Seed = seed,
                    InterceptedAzure = m.Intercepted.Azure,
                    InterceptedCrimson = m.Intercepted.Crimson,
                    Delivered = a.Biomass + a.Corruption + c.Biomass + c.Corruption,
                    Hatch = hatch,
                    BerserkLocked = m.BerserkLocked,
                    Winner = m.Winner,
                });
            }
            int withI = 0, berserk = 0, allied = 0, alliedWins = 0;
            double ia = 0, ic = 0;
            var hatchKinds = new HashSet<KaijuState>();
            foreach (DeliveryRun r in runs)
            {
                ia += r.InterceptedAzure;
                ic += r.InterceptedCrimson;
                if (r.InterceptedAzure + r.InterceptedCrimson > 0) withI++;
                if (r.BerserkLocked) berserk++;
                if (r.Hatch != null) hatchKinds.Add(r.Hatch.Value);
            }
            Console.WriteLine("{\"item\":\"CM-64\",\"seeds\":" + Seeds + ",\"interceptedAzure\":" + ia + ",\"interceptedCrimson\":" + ic + ",\"matchesWithInterception\":" + withI
                + ",\"berserkShare\":" + (double)berserk / Seeds + ",\"distinctHatch\":" + hatchKinds.Count + "}");
            _runs = runs;
            return runs;
        }

        [Test, Category("Slow")]
        public void CM64RanEverySeedAndSomethingWasDeliveredInEveryMatch()
        {
            List<DeliveryRun> runs = Runs();
            Assert.AreEqual(Seeds, runs.Count);
            foreach (DeliveryRun r in runs) Assert.Greater(r.Delivered, 0, "seed " + r.Seed);
        }

        [Test, Category("Slow")]
        public void CM64InterceptedDeliveriesForEachTeamAndInAtLeast80Of100Seeds()
        {
            double ia = 0, ic = 0;
            int withI = 0;
            foreach (DeliveryRun r in Runs())
            {
                ia += r.InterceptedAzure;
                ic += r.InterceptedCrimson;
                if (r.InterceptedAzure + r.InterceptedCrimson > 0) withI++;
            }
            Assert.Greater(ia, 0);
            Assert.Greater(ic, 0);
            Assert.GreaterOrEqual(withI, InterceptSeedsMin);
        }

        [Test, Category("Slow")]
        public void CM64TheCM19HatchThresholdsHoldDistinctOutcomesAndAlliedWinShare()
        {
            var kinds = new HashSet<KaijuState>();
            int allied = 0, wins = 0;
            foreach (DeliveryRun r in Runs())
            {
                if (r.Hatch == null) continue;
                kinds.Add(r.Hatch.Value);
                TeamId? t = SimEnumMap.TeamOf(r.Hatch.Value);
                if (t == null) continue;
                allied++;
                if (SimEnumMap.TeamOf(r.Winner) == t) wins++;
            }
            Assert.GreaterOrEqual(kinds.Count, OutcomeThresholds.DistinctHatchOutcomes);
            Assert.LessOrEqual(allied > 0 ? (double)wins / allied : 0, OutcomeThresholds.AlliedWinShareMax);
        }

        [Test, Category("Slow")]
        public void CM64TheBerserkShareStaysInTheCM34Band()
        {
            int berserk = 0;
            foreach (DeliveryRun r in Runs()) if (r.BerserkLocked) berserk++;
            double share = (double)berserk / Seeds;
            Assert.GreaterOrEqual(share, ShareMin);
            Assert.LessOrEqual(share, ShareMax);
        }

        [Test, Category("Slow")]
        public void CM36TheAutopilotPlayerUsesTheCleanseAndTheBerserkShareStaysInTheBand()
        {
            int cleanses = 0, crossed = 0;
            double removed = 0;
            for (int seed = 1; seed <= Seeds; seed++)
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5();
                World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz;
                for (int s = 0; s < max && w.Match.Phase != MatchPhase.Ended; s++)
                {
                    MatchTick.Advance(w, null, Frame);
                    if (w.Match.BerserkLocked || w.Match.Hatched) break;
                }
                if (w.Match.BerserkLocked) crossed++;
                cleanses += w.Cleanse.Count;
                removed += w.Cleanse.Removed;
            }
            double share = (double)crossed / Seeds;
            Console.WriteLine("{\"item\":\"CM-36\",\"cleanses\":" + cleanses + ",\"instabilityRemoved\":" + removed + ",\"berserkShare\":" + share + "}");
            Assert.AreEqual(9, GameConfig.CreateBrowser5v5().KaijuFeed.CorruptionInstabilityPerUnit);
            Assert.Greater(cleanses, 0, "the autopilot player never cleansed");
            Assert.GreaterOrEqual(share, ShareMin);
            Assert.LessOrEqual(share, ShareMax);
        }
    }
}

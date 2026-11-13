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
    /// DG-16a slow batches, ports of src/sim/heal.slow.test.ts (CM-37), biomass.slow.test.ts (CM-35) and the CM-85 part of
    /// corruption.slow.test.ts (09e0904). 5v5, seeds 1..100, every slot on bots (actor 0 on autopilot, SimDriver's order).
    /// Config swaps are per world (a cloned GameConfig), so nothing global is touched. Time is sim ticks.
    /// </summary>
    [TestFixture, Category("Slow")]
    public sealed class RulesDg16aSoakTests
    {
        private const int Seeds = 100;
        private const int TeamSize = 5;
        /// <summary>CM-34's band for the 5v5 berserk share.</summary>
        private const double ShareMin = 0.1;
        private const double ShareMax = 0.5;

        private static readonly InputFrame Frame = SimInput.EmptyInput();

        private static World NewWorld(int seed, GameConfig cfg)
        {
            return World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        private static int MaxSteps(GameConfig c) => (int)Math.Ceiling(c.MatchSeconds * c.SimHz) + (int)c.SimHz;

        // ------------------------------------------------------------------ CM-37 fountain batch

        private sealed class FountainMatch
        {
            public int Seed, Heals, RetreatHeals, LowHeals, DeathsAzure, DeathsCrimson;
            public double HpHealed;
        }

        private static FountainMatch RunFountain(int seed, GameConfig cfg)
        {
            World w = NewWorld(seed, cfg);
            var r = new FountainMatch { Seed = seed };
            int head = w.EventHead;
            int max = MaxSteps(cfg);
            for (int s = 0; s < max && w.Match.Phase != MatchPhase.Ended; s++)
            {
                MatchTick.Advance(w, null, Frame);
                Assert.LessOrEqual(w.EventHead - head, World.EventCapacity, "event ring overflow at seed " + seed);
                for (; head < w.EventHead; head++)
                {
                    SimEvent e = w.Events[head % World.EventCapacity];
                    if (e.Type != SimEventType.Heal) continue;
                    r.Heals++;
                    r.HpHealed += e.Value;
                    Actor a = w.Actors[e.A];
                    if (a.AiState == AiState.Retreat) r.RetreatHeals++;
                    if (a.MaxHp > 0 && (a.Hp - e.Value) / a.MaxHp < cfg.Heal.SeekHpShare) r.LowHeals++;
                }
            }
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Team == TeamId.Azure) r.DeathsAzure += w.Actors[i].Deaths;
                else r.DeathsCrimson += w.Actors[i].Deaths;
            }
            return r;
        }

        [Test, Category("Slow")]
        public void CM37BotsUseTheFountainDeathsPerMatchMoveOffMainEveryTeamStillDiesEveryMatch()
        {
            GameConfig on = GameConfig.CreateBrowser5v5();
            GameConfig off = GameConfig.CreateBrowser5v5();
            off.Heal.HpPerTick = 0;
            var onRuns = new List<FountainMatch>();
            var offRuns = new List<FountainMatch>();
            for (int k = 1; k <= Seeds; k++)
            {
                onRuns.Add(RunFountain(k, on));
                offRuns.Add(RunFountain(k, off));
            }
            double onAvg = 0, offAvg = 0, hp = 0;
            int heals = 0, low = 0, retreat = 0, offHeals = 0, withHeals = 0;
            foreach (FountainMatch m in onRuns)
            {
                onAvg += m.DeathsAzure + m.DeathsCrimson;
                heals += m.Heals;
                low += m.LowHeals;
                retreat += m.RetreatHeals;
                hp += m.HpHealed;
                if (m.Heals > 0) withHeals++;
            }
            foreach (FountainMatch m in offRuns)
            {
                offAvg += m.DeathsAzure + m.DeathsCrimson;
                offHeals += m.Heals;
            }
            onAvg /= Seeds;
            offAvg /= Seeds;
            Console.WriteLine("{\"item\":\"CM-37\",\"avgDeathsFountain\":" + onAvg + ",\"avgDeathsMain\":" + offAvg + ",\"heals\":" + heals
                + ",\"retreatHeals\":" + retreat + ",\"lowHeals\":" + low + ",\"matchesWithHeals\":" + withHeals + ",\"hpHealedPerMatch\":" + Math.Round(hp / Seeds, 1) + "}");
            Assert.AreEqual(0, offHeals, "fountain off must heal nobody");
            Assert.Greater(heals, 0, "bots never healed at a fountain");
            Assert.Greater(low, 0, "no bot below SeekHpShare healed at its fountain");
            Assert.AreNotEqual(offAvg, onAvg, "average deaths per match equal main's");
            foreach (FountainMatch m in onRuns)
            {
                Assert.GreaterOrEqual(m.DeathsAzure, 1, "seed " + m.Seed + ": azure never died");
                Assert.GreaterOrEqual(m.DeathsCrimson, 1, "seed " + m.Seed + ": crimson never died");
            }
        }

        // ------------------------------------------------------------------ CM-35 biomass batch

        [Test, Category("Slow")]
        public void CM35DeliveriesByTypeAreReportedAndBothPlantAndMeatAreNonZero()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            double plant = 0, meat = 0;
            int overflows = 0, seedsWithMeat = 0;
            for (int seed = 1; seed <= Seeds; seed++)
            {
                World w = NewWorld(seed, cfg);
                int max = MaxSteps(cfg);
                int seen = 0, meatEvents = 0, plantEvents = 0;
                while (w.Tick < max && w.Match.Phase != MatchPhase.Ended && !w.Match.Hatched)
                {
                    MatchTick.Advance(w, null, Frame);
                    for (; seen < w.EventHead; seen++)
                    {
                        SimEvent e = w.Events[seen % World.EventCapacity];
                        if (e.Type != SimEventType.DeliverBiomass) continue;
                        if (e.Bio == BiomassType.Meat) meatEvents++;
                        else if (e.Bio == BiomassType.Plant) plantEvents++;
                    }
                }
                DeliveredTally a = w.Match.Delivered[(int)TeamId.Azure];
                DeliveredTally c = w.Match.Delivered[(int)TeamId.Crimson];
                double p = a.Plant + c.Plant;
                double m = a.Meat + c.Meat;
                plant += p;
                meat += m;
                overflows += w.OrbOverflows;
                if (m > 0) seedsWithMeat++;
                Assert.AreEqual(p > 0, plantEvents > 0, "seed " + seed + " plant events");
                Assert.AreEqual(m > 0, meatEvents > 0, "seed " + seed + " meat events");
            }
            Console.WriteLine("{\"batch\":\"CM-35\",\"plant\":" + plant + ",\"meat\":" + meat + ",\"seedsWithMeat\":" + seedsWithMeat + ",\"orbOverflows\":" + overflows + "}");
            Assert.Greater(plant, 0);
            Assert.Greater(meat, 0);
        }

        [Test, Category("Slow")]
        public void CM35TheCM19OutcomeThresholdsAreUnchanged()
        {
            Assert.AreEqual(0.8, OutcomeThresholds.Decisive);
            Assert.AreEqual(0.4, OutcomeThresholds.OwnedShare);
            Assert.AreEqual(0.25, OutcomeThresholds.OwnedShareMin);
            Assert.AreEqual(4, OutcomeThresholds.OwnerChangesMean);
            Assert.AreEqual(14, OutcomeThresholds.MaxTeamWins);
            Assert.AreEqual(2, OutcomeThresholds.DistinctHatchOutcomes);
            Assert.AreEqual(0.85, OutcomeThresholds.AlliedWinShareMax);
            Assert.AreEqual(1.5, OutcomeThresholds.KaijuDutyMean);
            Assert.AreEqual(3, OutcomeThresholds.PassiveHumanTeamWins);
            Assert.AreEqual(0, OutcomeThresholds.AfkWinsOverPlayingMax);
        }

        // ------------------------------------------------------------------ CM-85 corruption batch

        /// <summary>World.Tick right after the step that locked berserk (-1 = never).</summary>
        internal static int[] CrossTicks(double corruptionPerUnit, double biomassPerUnit, out double share)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            // U-057: the sandwich isolates the instability rule (the browser's CM-85 predates the CM-36 cleanse), so the autopilot
            // cleanse is off here; it would remove instability in one world and not the other. CM-36's own soak measures it on.
            cfg.KaijuFeed.Cleanse.BotNetUnits = double.PositiveInfinity;
            cfg.KaijuFeed.CorruptionInstabilityPerUnit = corruptionPerUnit;
            cfg.KaijuFeed.BiomassInstabilityPerUnit = biomassPerUnit;
            var cross = new int[Seeds];
            int crossed = 0;
            for (int k = 0; k < Seeds; k++)
            {
                World w = NewWorld(k + 1, cfg);
                int max = MaxSteps(cfg);
                cross[k] = -1;
                for (int s = 0; s < max && w.Match.Phase != MatchPhase.Ended; s++)
                {
                    MatchTick.Advance(w, null, Frame);
                    if (w.Match.BerserkLocked)
                    {
                        cross[k] = w.Tick;
                        crossed++;
                        break;
                    }
                    if (w.Match.Hatched) break;
                }
            }
            share = (double)crossed / Seeds;
            return cross;
        }

        [Test, Category("Slow")]
        public void CM85TheBerserkShareStaysInTheCM34BandAndTheDerivedSandwichHolds()
        {
            GameConfig shipped = GameConfig.CreateBrowser5v5();
            double rate = shipped.KaijuFeed.CorruptionInstabilityPerUnit;
            int[] main = CrossTicks(10, 0, out double mainShare);
            int[] cm34 = CrossTicks(rate, 0, out double cm34Share);
            int[] bio = CrossTicks(rate, shipped.KaijuFeed.BiomassInstabilityPerUnit, out double bioShare);
            var crossed = new List<int>();
            foreach (int t in bio) if (t >= 0) crossed.Add(t);
            crossed.Sort();
            double median = crossed.Count == 0 ? -1 : crossed.Count % 2 == 1 ? crossed[crossed.Count / 2] : (crossed[crossed.Count / 2 - 1] + crossed[crossed.Count / 2]) / 2.0;
            Console.WriteLine("{\"item\":\"CM-85\",\"mainShare\":" + mainShare + ",\"cm34Share\":" + cm34Share + ",\"berserkShare\":" + bioShare + ",\"medianFirstCrossTick\":" + median + "}");
            Assert.GreaterOrEqual(bioShare, ShareMin);
            Assert.LessOrEqual(bioShare, ShareMax);
            Assert.Greater(median, 0, "no 5v5 seed crossed the threshold with biomass corruption");
            Assert.LessOrEqual(median, Math.Ceiling(shipped.HatchSeconds * shipped.SimHz) + shipped.SimHz);
            for (int k = 0; k < Seeds; k++)
            {
                if (cm34[k] >= 0)
                {
                    Assert.GreaterOrEqual(bio[k], 0, "seed " + (k + 1) + ": locked with CM-34 alone");
                    Assert.LessOrEqual(bio[k], cm34[k], "seed " + (k + 1));
                }
                if (bio[k] >= 0)
                {
                    Assert.GreaterOrEqual(main[k], 0, "seed " + (k + 1) + ": CM-85 locked, main did not");
                    Assert.GreaterOrEqual(bio[k], main[k], "seed " + (k + 1));
                }
            }
        }
    }
}

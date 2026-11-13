using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>DG-41 (U-062): the random berserk line on Travis's y = sqrt(x) curve. Off by default (KaijuFeed.RandomBerserk).</summary>
    [TestFixture]
    public sealed class BerserkCurveTests
    {
        private static World Make(int seed, bool random, double curveMax = 800)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            cfg.KaijuFeed.RandomBerserk = random;
            cfg.KaijuFeed.BerserkCurveMax = curveMax;
            return World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        [Test]
        public void DG41OffTheLineIsTheFixedThresholdOnItIsFixedPerSeedAndBelowTheCurveTop()
        {
            Assert.IsFalse(GameConfig.CreateDefault().KaijuFeed.RandomBerserk, "off by default: Normal stays the browser rule");
            Assert.AreEqual(100, SimResources.BerserkLine(Make(7, false)), 1e-12);
            World w = Make(7, true);
            double line = SimResources.BerserkLine(w);
            Assert.AreEqual(line, SimResources.BerserkLine(Make(7, true)), 0, "same seed, same line");
            Assert.GreaterOrEqual(line, 0);
            Assert.Less(line, 800);
            World.ResetWorld(w, new WorldOptions { Seed = 8, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = w.Config });
            Assert.AreEqual(SimResources.BerserkLine(Make(8, true)), SimResources.BerserkLine(w), 0, "a reset with a new seed draws a new line");
        }

        [Test]
        public void DG41TheChanceThatInstabilityIHasLockedBerserkIsTheSquareRootOfIOverTheCurveTop()
        {
            const int n = 20000;
            const double max = 160;
            foreach (double i in new[] { 10.0, 40.0, 90.0, 140.0 })
            {
                int locked = 0;
                for (int seed = 1; seed <= n; seed++)
                {
                    double u = SimResources.SeedUniform(seed);
                    if (u * u * max <= i) locked++;
                }
                double expected = Math.Sqrt(i / max);
                Assert.AreEqual(expected, (double)locked / n, 0.015, "I = " + i);
            }
        }

        [Test]
        public void DG41ADeliveryThatCrossesTheRandomLineLocksBerserkOneBelowItDoesNot()
        {
            int seed = 1;
            while (SimResources.SeedUniform(seed) < 0.5) seed++;
            World w = Make(seed, true);
            double line = SimResources.BerserkLine(w);
            w.Match.Instability = line - 5;
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Biomass, 1, 1, BiomassType.Plant);
            Assert.IsFalse(w.Match.BerserkLocked);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 1, 1);
            Assert.IsTrue(w.Match.BerserkLocked, "9 instability carried it over the line " + line);
        }

        /// <summary>
        /// Berserk share over 100 whole 5v5 matches (seeds 1..100, shipped config otherwise) with the fixed threshold and with the
        /// random line at the shipped curve top and at 1200. Measurement for the owner; asserts only that the shipped curve top keeps the share in
        /// the CM-34 band the fixed rule is held to.
        /// </summary>
        [Test, Category("Slow")]
        public void DG41BerserkShareWithTheRandomLineAgainstTheFixedThreshold()
        {
            double off = Share(false, 160);
            double shipped = GameConfig.CreateBrowser5v5().KaijuFeed.BerserkCurveMax;
            double onShipped = Share(true, shipped);
            double on1200 = Share(true, 1200);
            Console.WriteLine("{\"item\":\"DG-41\",\"seeds\":100,\"fixedShare\":" + off + ",\"randomShareTop" + shipped + "\":" + onShipped + ",\"randomShareTop1200\":" + on1200 + "}");
            Assert.GreaterOrEqual(onShipped, 0.1, "the CM-34 band (RulesDg16aSoakTests ShareMin)");
            Assert.LessOrEqual(onShipped, 0.5, "the CM-34 band (ShareMax)");
        }

        private static double Share(bool random, double curveMax)
        {
            int locked = 0;
            InputFrame frame = SimInput.EmptyInput();
            for (int seed = 1; seed <= 100; seed++)
            {
                World w = Make(seed, random, curveMax);
                int max = (int)(w.Config.MatchSeconds * w.Config.SimHz) + 30;
                for (int s = 0; s < max && w.Match.Phase != MatchPhase.Ended && !w.Match.Hatched; s++) MatchTick.Advance(w, null, frame);
                if (w.Match.BerserkLocked) locked++;
            }
            return locked / 100.0;
        }
    }
}

using System;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.View;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>DG-17 acceptance: banner text matches the sim outcome, the 18-tick spotlight, crack stages, callouts.</summary>
    [TestFixture]
    public sealed class KaijuMomentsTests
    {
        static World NewWorld(int seed)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
            });
        }

        /// <summary>What the banner must say for the sim's own outcome, worked out here without the tracker.</summary>
        static string Expected(World w)
        {
            MatchState m = w.Match;
            switch (w.Kaiju.State)
            {
                case KaijuState.Berserk: return "BERSERK: corruption won";
                case KaijuState.Azure: return "THE KAIJU SIDES WITH AZURE: fed " + Whole(m.Allegiance.Azure, m.Allegiance.Crimson) + " to " + Whole(m.Allegiance.Crimson, m.Allegiance.Azure);
                case KaijuState.Crimson: return "THE KAIJU SIDES WITH CRIMSON: fed " + Whole(m.Allegiance.Crimson, m.Allegiance.Azure) + " to " + Whole(m.Allegiance.Azure, m.Allegiance.Crimson);
                default: return "THE KAIJU SIDES WITH NOBODY: dead even at " + Math.Round(m.Allegiance.Azure).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        static string Whole(double v, double other)
        {
            bool fine = Math.Round(v) == Math.Round(other) && v != other;
            return fine ? v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : Math.Round(v).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        [Test]
        public void BannerExamplesReadAsWritten()
        {
            Assert.AreEqual("THE KAIJU SIDES WITH AZURE: fed 64 to 41", KaijuMomentTracker.BannerText(KaijuState.Azure, 64, 41));
            Assert.AreEqual("THE KAIJU SIDES WITH CRIMSON: fed 52 to 30", KaijuMomentTracker.BannerText(KaijuState.Crimson, 30, 52));
            Assert.AreEqual("BERSERK: corruption won", KaijuMomentTracker.BannerText(KaijuState.Berserk, 90, 10));
            Assert.AreEqual("THE KAIJU SIDES WITH NOBODY: dead even at 50", KaijuMomentTracker.BannerText(KaijuState.Neutral, 50, 50));
            // two tallies that round alike show one decimal, so the winner never reads as a tie
            Assert.AreEqual("THE KAIJU SIDES WITH AZURE: fed 64.4 to 64.2", KaijuMomentTracker.BannerText(KaijuState.Azure, 64.4, 64.2));
        }

        [Test]
        public void HatchBannersCarryAnIconAsWellAsColour()
        {
            foreach (KaijuState s in new[] { KaijuState.Azure, KaijuState.Crimson, KaijuState.Berserk, KaijuState.Neutral })
            {
                BannerSpec b = KaijuMomentTracker.HatchBanner(s, 10, 5);
                Assert.IsFalse(string.IsNullOrEmpty(b.Icon), s + " icon");
                Assert.IsFalse(string.IsNullOrEmpty(b.Headline), s + " headline");
                Assert.IsFalse(string.IsNullOrEmpty(b.Reason), s + " reason");
            }
            Assert.AreEqual(BannerStyle.Corruption, KaijuMomentTracker.HatchBanner(KaijuState.Berserk, 0, 0).Style);
            Assert.AreEqual(UiIcons.Adult[3], KaijuMomentTracker.HatchBanner(KaijuState.Berserk, 0, 0).Icon);
            Assert.AreEqual(BannerStyle.Corruption, KaijuMomentTracker.BerserkLockBanner().Style);
        }

        /// <summary>20 seeds, random feeding (some past the berserk threshold, some ties), a forced hatch, observed
        /// every 4 ticks (8x at 60 fps): the banner text matches the sim's outcome every time.</summary>
        [Test]
        public void BannerTextMatchesTheSimOutcomeOverTwentySeeds()
        {
            int berserk = 0, sided = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                World w = NewWorld(seed);
                var rng = new System.Random(seed);
                double a = rng.Next(0, 9), c = seed % 5 == 0 ? a : rng.Next(0, 9);
                SimDebug.DebugAddFeed(w, TeamId.Azure, a, seed % 4 == 0 ? 7 : 0);
                SimDebug.DebugAddFeed(w, TeamId.Crimson, c, seed % 4 == 0 ? 6 : 0);
                var t = new KaijuMomentTracker();
                var scratch = new InputFrame();
                t.Observe(w);
                w.ForceHatch = true;
                for (int f = 0; f < 3 && !t.HatchSeen; f++)
                {
                    for (int k = 0; k < 4; k++) MatchTick.AdvanceNoHuman(w, scratch);
                    t.Observe(w);
                }
                Assert.IsTrue(t.HatchSeen, "seed " + seed + ": hatch not seen");
                Assert.AreEqual(w.Kaiju.State, t.HatchState, "seed " + seed + ": state");
                Assert.AreEqual(Expected(w), KaijuMomentTracker.BannerText(t.HatchState, t.HatchAzure, t.HatchCrimson), "seed " + seed);
                if (w.Kaiju.State == KaijuState.Berserk) berserk++;
                if (w.Kaiju.State == KaijuState.Azure || w.Kaiju.State == KaijuState.Crimson) sided++;
            }
            Assert.Greater(berserk, 0, "the seeds should include a berserk hatch");
            Assert.Greater(sided, 0, "the seeds should include an allegiance hatch");
        }

        /// <summary>The same check on the natural hatch with bots feeding (about 7,200 ticks a seed).</summary>
        [Test, Category("Slow")]
        public void BannerTextMatchesTheNaturalHatchOverTwentySeeds()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                World w = NewWorld(seed * 7919);
                var t = new KaijuMomentTracker();
                var scratch = new InputFrame();
                int guard = (int)((w.Config.HatchSeconds + 2) * w.Config.SimHz);
                for (int i = 0; i < guard && !t.HatchSeen; i += 4)
                {
                    for (int k = 0; k < 4; k++) MatchTick.AdvanceNoHuman(w, scratch);
                    t.Observe(w);
                }
                Assert.IsTrue(t.HatchSeen, "seed " + seed);
                Assert.AreEqual(Expected(w), KaijuMomentTracker.BannerText(t.HatchState, t.HatchAzure, t.HatchCrimson), "seed " + seed);
            }
        }

        [Test]
        public void TheSpotlightLastsEighteenTicksAtThirtyHertz()
        {
            World w = NewWorld(1337);
            Assert.AreEqual(30, w.Config.SimHz, 1e-9);
            var t = new KaijuMomentTracker();
            var scratch = new InputFrame();
            t.Observe(w);
            w.ForceHatch = true;
            int lit = 0;
            for (int i = 0; i < 60; i++)
            {
                MatchTick.AdvanceNoHuman(w, scratch);
                t.Observe(w);
                if (t.SpotlightActive(w.Tick)) lit++;
            }
            Assert.AreEqual(KaijuMomentTracker.SpotlightTicks, lit);
            Assert.AreEqual(18, lit);
        }

        [Test]
        public void TheSpotlightStartsOnTheHatchTickEvenWhenFramesSkipTicks()
        {
            World w = NewWorld(42);
            var t = new KaijuMomentTracker();
            var scratch = new InputFrame();
            t.Observe(w);
            // natural hatch: run to just before it, then 4 ticks per frame
            w.Match.Time = w.Config.HatchSeconds - 2.0 / w.Config.SimHz;
            int hatchTick = -1;
            for (int f = 0; f < 4; f++)
            {
                for (int k = 0; k < 4; k++)
                {
                    MatchTick.AdvanceNoHuman(w, scratch);
                    if (hatchTick < 0 && w.Match.Hatched) hatchTick = w.Tick;
                }
                t.Observe(w);
            }
            Assert.IsTrue(t.HatchSeen);
            Assert.AreEqual(hatchTick, t.HatchTick, 1, "within one tick of the real hatch tick");
        }

        [TestCase(120.0, false, 0)]
        [TestCase(60.0, false, 1)]
        [TestCase(45.0, false, 1)]
        [TestCase(30.0, false, 2)]
        [TestCase(10.0, false, 3)]
        [TestCase(0.5, false, 3)]
        [TestCase(0.0, true, 0)]
        public void CrackStages(double untilHatch, bool hatched, int stage)
        {
            Assert.AreEqual(stage, KaijuMomentTracker.CrackStage(untilHatch, hatched));
        }

        [Test]
        public void TheHatchSoonCalloutFiresOnceAtThirtySeconds()
        {
            World w = NewWorld(7);
            var t = new KaijuMomentTracker();
            w.Match.Time = w.Config.HatchSeconds - 31;
            t.Observe(w);
            Assert.AreEqual(0, Count(t, KaijuMomentKind.HatchSoon));
            w.Match.Time = w.Config.HatchSeconds - 30;
            t.Observe(w);
            Assert.AreEqual(1, Count(t, KaijuMomentKind.HatchSoon));
            w.Match.Time = w.Config.HatchSeconds - 29;
            t.Observe(w);
            Assert.AreEqual(0, Count(t, KaijuMomentKind.HatchSoon));
        }

        [Test]
        public void TheBerserkLockIsAMoment()
        {
            World w = NewWorld(9);
            var t = new KaijuMomentTracker();
            t.Observe(w);
            SimDebug.DebugAddFeed(w, null, 0, w.Config.KaijuFeed.BerserkThreshold);
            t.Observe(w);
            Assert.AreEqual(1, Count(t, KaijuMomentKind.BerserkLocked));
            Assert.IsTrue(t.BerserkLocked);
        }

        [Test]
        public void ANewMatchStartsTheStoryOver()
        {
            World w = NewWorld(3);
            var t = new KaijuMomentTracker();
            var scratch = new InputFrame();
            t.Observe(w);
            w.ForceHatch = true;
            MatchTick.AdvanceNoHuman(w, scratch);
            t.Observe(w);
            Assert.IsTrue(t.HatchSeen);
            World.ResetWorld(w, w.Opts);
            t.Observe(w);
            Assert.IsFalse(t.HatchSeen);
            Assert.AreEqual(-1, t.HatchTick);
        }

        static int Count(KaijuMomentTracker t, KaijuMomentKind k)
        {
            int n = 0;
            for (int i = 0; i < t.PendingCount; i++) if (t.Pending(i) == k) n++;
            return n;
        }
    }
}

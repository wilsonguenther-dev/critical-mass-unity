using System.IO;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>Big moments: queue, priorities, cooldowns, rarity, settings, triggers and missing-art fallback (pure logic).</summary>
    public sealed class BigMomentsTests
    {
        [SetUp]
        public void SetUp()
        {
            BigMomentsSettings.ClearOverrides();
            MomentArt.ResetLoader();
        }

        [TearDown]
        public void TearDown()
        {
            BigMomentsSettings.ClearOverrides();
            MomentArt.ResetLoader();
        }

        static MomentRequest Req(MomentId id, bool player = false) =>
            new MomentRequest { Id = id, Actor = player ? 0 : 5, Victim = -1, PlayerOwned = player, Team = TeamId.Azure };

        static World NewWorld() => World.CreateWorld(new WorldOptions
        {
            Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
        });

        static int Enemy(World w, int nth = 0)
        {
            for (int i = 1; i < w.ActorCount; i++)
                if (w.Actors[i].Team != w.Actors[0].Team && nth-- == 0) return i;
            return -1;
        }

        // ------------------------------------------------------------ recipes

        [Test]
        public void EveryMomentHasARecipeWithinTheDurationRules()
        {
            foreach (MomentId id in MomentRecipes.Gallery())
            {
                MomentRecipe r = MomentRecipes.Get(id);
                Assert.IsNotNull(r, id.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(r.Title), id + " title");
                float s = MomentQueue.TakeoverSeconds(r, false), sp = MomentQueue.TakeoverSeconds(r, true);
                Assert.That(s, Is.InRange(0.6f, 1.4f), id + " takeover length");
                Assert.That(sp, Is.InRange(0.6f, 1.4f), id + " shortened length");
                Assert.LessOrEqual(sp, s);
            }
            Assert.AreEqual(18, MomentRecipes.Gallery().Length);   // batch 3: BLAST RADIUS and LIFELINE
            Assert.AreEqual(MomentScale.Stinger, MomentRecipes.Get(MomentId.TwoDown).Scale, "streaks are corner stingers");
            Assert.AreEqual(MomentScale.Stinger, MomentRecipes.Get(MomentId.ThreeDown).Scale);
            Assert.AreEqual(MomentScale.Stinger, MomentRecipes.Get(MomentId.Wildfire).Scale);
        }

        [Test]
        public void TitlesComeFromWordsAndCanBeRenamed()
        {
            Assert.AreEqual("QUAKE", MomentWords.Title(MomentId.Quake));
            MomentWords.Set(MomentId.Quake, "TREMOR");
            Assert.AreEqual("TREMOR", MomentWords.Title(MomentId.Quake));
            MomentWords.Set(MomentId.Quake, null);
            Assert.AreEqual("QUAKE", MomentWords.Title(MomentId.Quake));
        }

        // ------------------------------------------------------------ queue

        [Test]
        public void TakeoverPlaysForItsLengthThenEnds()
        {
            var q = new MomentQueue();
            Assert.AreEqual(OfferResult.Queued, q.Offer(Req(MomentId.OpeningStrike), 0, BigMomentsMode.Full, false));
            q.Update(0, false);
            Assert.IsTrue(q.Playing);
            Assert.AreEqual(MomentId.OpeningStrike, q.Current.Request.Id);
            float len = q.Current.Seconds;
            q.Update(len * 0.5, false);
            Assert.AreEqual(0.5f, q.Progress(len * 0.5), 1e-3f);
            q.Update(len + 0.01, false);
            Assert.IsFalse(q.Playing);
        }

        [Test]
        public void HigherPriorityPlaysFirst()
        {
            var q = new MomentQueue();
            q.Offer(Req(MomentId.Hatch), 0, BigMomentsMode.Full, false);
            q.Offer(Req(MomentId.Finisher), 0, BigMomentsMode.Full, false);
            q.Update(0, false);
            Assert.AreEqual(MomentId.Finisher, q.Current.Request.Id);
            q.Update(q.Current.Seconds + 0.01, false);
            Assert.AreEqual(MomentId.Hatch, q.Current.Request.Id, "the hatch never goes stale and plays next");
        }

        [Test]
        public void PerTypeCooldownDropsRepeats()
        {
            var q = new MomentQueue();
            Assert.AreEqual(OfferResult.QueuedAsStinger, q.Offer(Req(MomentId.TwoDown, true), 0, BigMomentsMode.Full, false));
            Assert.AreEqual(OfferResult.Cooldown, q.Offer(Req(MomentId.TwoDown, true), 3, BigMomentsMode.Full, false));
            Assert.AreEqual(OfferResult.QueuedAsStinger, q.Offer(Req(MomentId.TwoDown, true), 6.5, BigMomentsMode.Full, false));
            Assert.AreEqual(OfferResult.Queued, q.Offer(Req(MomentId.OpeningStrike), 10, BigMomentsMode.Full, false));
            Assert.AreEqual(OfferResult.Cooldown, q.Offer(Req(MomentId.OpeningStrike), 5000, BigMomentsMode.Full, false), "once a match");
        }

        [Test]
        public void AtMostOneFullTakeoverPerTwentySeconds()
        {
            var q = new MomentQueue();
            Assert.AreEqual(OfferResult.Queued, q.Offer(Req(MomentId.HillCleared), 0, BigMomentsMode.Full, false));
            Assert.AreEqual(OfferResult.QueuedAsStinger, q.Offer(Req(MomentId.BatteringRam), 10, BigMomentsMode.Full, false), "inside the gap: corner only");
            Assert.AreEqual(OfferResult.Queued, q.Offer(Req(MomentId.Quake), 20.5, BigMomentsMode.Full, false));
            Assert.AreEqual(OfferResult.Queued, q.Offer(Req(MomentId.Hatch), 21, BigMomentsMode.Full, false), "the hatch ignores the gap");
            Assert.AreEqual(MomentQueue.TakeoverGapSeconds, 20.0);
        }

        [Test]
        public void TightFightDemotesOthersAndShortensThePlayersOwn()
        {
            var q = new MomentQueue();
            Assert.AreEqual(OfferResult.QueuedAsStinger, q.Offer(Req(MomentId.HillCleared), 0, BigMomentsMode.Full, true));
            Assert.AreEqual(OfferResult.Queued, q.Offer(Req(MomentId.BatteringRam, true), 30, BigMomentsMode.Full, true));
            q.Update(30, false);
            Assert.IsTrue(q.Current.Shortened);
            Assert.AreEqual(Mathf.Clamp(1.0f * MomentQueue.PlayerShorten, 0.6f, 1.4f), q.Current.Seconds, 1e-4f);
        }

        [Test]
        public void BannersAndTakeoversNeverStack()
        {
            var q = new MomentQueue();
            q.Offer(Req(MomentId.HillCleared), 0, BigMomentsMode.Full, false);
            q.Update(0.1, true);
            Assert.IsFalse(q.Playing, "an ordinary takeover waits for the banner");
            Assert.IsFalse(q.HasWaitingPreempt);
            q.Update(0.2, false);
            Assert.IsTrue(q.Playing, "it starts once the banner is gone");

            var k = new MomentQueue();
            k.Offer(Req(MomentId.Hatch), 0, BigMomentsMode.Full, false);
            Assert.IsTrue(k.HasWaitingPreempt, "the director holds the banner for a Kaiju takeover");
            k.Update(0, true);
            Assert.IsTrue(k.Playing, "a Kaiju takeover plays over the banner (which is held)");
        }

        [Test]
        public void WaitingMomentsGoStale()
        {
            var q = new MomentQueue();
            q.Offer(Req(MomentId.HillCleared), 0, BigMomentsMode.Full, false);
            q.Update(MomentQueue.WaitTimeout + 0.1, true);
            Assert.AreEqual(0, q.WaitingCount);
            q.Update(MomentQueue.WaitTimeout + 0.2, false);
            Assert.IsFalse(q.Playing);
        }

        [Test]
        public void StingersWaitForTheTakeover()
        {
            var q = new MomentQueue();
            q.Offer(Req(MomentId.OpeningStrike), 0, BigMomentsMode.Full, false);
            q.Offer(Req(MomentId.TwoDown, true), 0, BigMomentsMode.Full, false);
            q.Update(0, false);
            Assert.IsTrue(q.Playing);
            Assert.IsFalse(q.StingerPlaying);
            q.Update(q.Current.Seconds + 0.01, false);
            Assert.IsTrue(q.StingerPlaying);
            Assert.AreEqual(MomentId.TwoDown, q.CurrentStinger.Request.Id);
        }

        [Test]
        public void ForceIgnoresTheRules()
        {
            var q = new MomentQueue();
            q.Force(Req(MomentId.Quake), MomentScale.Takeover, 0);
            Assert.IsTrue(q.Playing);
            q.Force(Req(MomentId.Quake), MomentScale.Takeover, 0.1);
            Assert.AreEqual(0.1, q.Current.StartedAt, 1e-9);
        }

        // ------------------------------------------------------------ settings

        [Test]
        public void SettingsOffReducedFull()
        {
            var q = new MomentQueue();
            Assert.AreEqual(OfferResult.Off, q.Offer(Req(MomentId.Hatch), 0, BigMomentsMode.Off, false));
            Assert.AreEqual(OfferResult.QueuedAsStinger, q.Offer(Req(MomentId.Hatch), 0, BigMomentsMode.Reduced, false), "reduced = corner stingers only");
            Assert.AreEqual(BigMomentsMode.Reduced, BigMomentsSettings.Step(BigMomentsMode.Full, 1));
            Assert.AreEqual(BigMomentsMode.Off, BigMomentsSettings.Step(BigMomentsMode.Full, -1));
            Assert.AreEqual(BigMomentsMode.Full, BigMomentsSettings.Step(BigMomentsMode.Off, 1));
            Assert.AreEqual("REDUCED", BigMomentsSettings.Label(BigMomentsMode.Reduced));
            BigMomentsSettings.ModeOverride = BigMomentsMode.Off;
            Assert.AreEqual(BigMomentsMode.Off, BigMomentsSettings.Mode);
        }

        [Test]
        public void AccessibilitySettingsToneEveryLayerDown()
        {
            MomentLook full = MomentLook.Resolve(false, false, false);
            Assert.IsTrue(full.Motion);
            Assert.IsTrue(full.Invert);
            Assert.IsFalse(full.Shake, "camera shake stays off");
            Assert.IsFalse(full.SlowMotion, "slow motion defaults off");

            MomentLook flash = MomentLook.Resolve(false, true, false);
            Assert.IsFalse(flash.Invert, "no invert frame with reduce flashing");
            Assert.LessOrEqual(flash.FlashAlpha, 0.25f);
            Assert.GreaterOrEqual(flash.FlashSeconds, 0.3f, "a slow fade, not a strobe");

            MomentLook motion = MomentLook.Resolve(true, false, true);
            Assert.IsFalse(motion.Motion);
            Assert.IsFalse(motion.Invert);
            Assert.IsFalse(motion.SlowMotion);
            Assert.Less(motion.SpeedLineAlpha, full.SpeedLineAlpha);
            Assert.Less(motion.FlashAlpha, full.FlashAlpha);

            BigMomentsSettings.ReducedMotionOverride = true;
            BigMomentsSettings.ReduceFlashingOverride = true;
            Assert.IsFalse(BigMomentsSettings.Look().Invert);
            Assert.IsFalse(BigMomentsSettings.Look().Motion);
        }

        [Test]
        public void NoMoreThanThreeFlashesASecond()
        {
            var f = new FlashLimiter();
            Assert.IsTrue(f.TryFlash(0.0));
            Assert.IsTrue(f.TryFlash(0.1));
            Assert.IsTrue(f.TryFlash(0.2));
            Assert.IsFalse(f.TryFlash(0.3));
            Assert.IsFalse(f.TryFlash(0.99));
            Assert.IsTrue(f.TryFlash(1.01));
        }

        // ------------------------------------------------------------ art slots

        [Test]
        public void MissingArtFallsBackInOrder()
        {
            MomentArt.Loader = _ => null;
            Assert.AreEqual(PortraitSource.LiveModel, MomentArt.ResolvePortrait(ClassId.Warrior, TeamId.Azure, true, true, out _, out _));
            Assert.AreEqual(PortraitSource.HudIcon, MomentArt.ResolvePortrait(ClassId.Builder, TeamId.Azure, false, true, out _, out _));
            Assert.AreEqual(PortraitSource.Shape, MomentArt.ResolvePortrait(ClassId.Builder, TeamId.Azure, false, false, out Texture2D img, out _));
            Assert.IsNull(img);
            Assert.AreEqual(PortraitSource.LiveModel, MomentArt.ResolveKaiju(KaijuState.Berserk, true, true, out _));
            Assert.AreEqual(PortraitSource.Shape, MomentArt.ResolveKaiju(KaijuState.Berserk, false, false, out _));

            var tex = new Texture2D(4, 4);
            try
            {
                MomentArt.Loader = n => n == "bm-portrait-miner" || n == "bm-portrait-miner-teammask" ? tex : null;
                Assert.AreEqual(PortraitSource.TintedImage, MomentArt.ResolvePortrait(ClassId.Miner, TeamId.Crimson, true, true, out Texture2D a, out Texture2D m));
                Assert.AreSame(tex, a);
                Assert.AreSame(tex, m);
                MomentArt.Loader = n => n == "bm-portrait-miner-crimson" ? tex : null;
                Assert.AreEqual(PortraitSource.TeamImage, MomentArt.ResolvePortrait(ClassId.Miner, TeamId.Crimson, true, true, out _, out _));
                MomentArt.Loader = n => n == "bm-kaiju-azure" ? tex : null;
                Assert.AreEqual(PortraitSource.TeamImage, MomentArt.ResolveKaiju(KaijuState.Azure, true, true, out _));
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void ThePaintedStoryCutInsFillThePortraitSlotBeforeTheLiveModel()
        {
            // Integration batch 4: the seven painted cut-ins of PR #62 (Resources/Story/cutin_*.jpg).
            string[] files = { "cutin_warrior_azure", "cutin_warrior_crimson", "cutin_miner_azure", "cutin_miner_crimson",
                "cutin_farmer_azure", "cutin_farmer_crimson", "cutin_kaiju" };
            foreach (string f in files) Assert.IsTrue(File.Exists("Assets/_Game/Resources/Story/" + f + ".jpg"), f);
            Assert.AreEqual("story:cutin_warrior_azure", MomentArt.PaintedName(ClassId.Warrior, TeamId.Azure));
            Assert.AreEqual("story:cutin_kaiju", MomentArt.PaintedKaiju);

            var tex = new Texture2D(1920, 1072);
            try
            {
                MomentArt.Loader = n => n.StartsWith(MomentArt.StoryPrefix) && n != "story:cutin_builder_azure" ? tex : null;
                Assert.AreEqual(PortraitSource.Painted, MomentArt.ResolvePortrait(ClassId.Warrior, TeamId.Azure, true, true, out Texture2D a, out _));
                Assert.AreSame(tex, a);
                Assert.AreEqual(PortraitSource.Painted, MomentArt.ResolveKaiju(KaijuState.Berserk, true, true, out _));
                MomentArt.Loader = n => n == "bm-portrait-miner-crimson" || n.StartsWith(MomentArt.StoryPrefix) ? tex : null;
                Assert.AreEqual(PortraitSource.TeamImage, MomentArt.ResolvePortrait(ClassId.Miner, TeamId.Crimson, true, true, out _, out _), "a bm-portrait image still wins");
                Rect crop = MomentArt.PaintedCrop(tex);
                Assert.AreEqual(1072f / 1920f, crop.width, 1e-4f, "a square of the 16:9 painting");
                Assert.GreaterOrEqual(crop.x, 0f);
                Assert.LessOrEqual(crop.xMax, 1f + 1e-5f);
            }
            finally
            {
                MomentArt.ResetLoader();
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void ATakeoverClaimsTheCentreLane()
        {
            HudZones.Clear();
            HudZones.FrameOverride = 100;
            try
            {
                HudZones.Hold(HudZone.Callout, HudPriority.Takeover);
                Assert.IsTrue(HudZones.Blocked(HudZone.Callout, HudPriority.Banner), "banners wait for a takeover");
                Assert.IsTrue(HudZones.Blocked(HudZone.Callout, HudPriority.Autopilot), "the autopilot line waits too");
                Assert.IsTrue(HudZones.Blocked(HudZone.Callout, HudPriority.Guide), "the depth event timer waits");
                HudZones.FrameOverride = 102;
                Assert.IsFalse(HudZones.Blocked(HudZone.Callout, HudPriority.Banner), "the lane frees the frame after the next");
            }
            finally
            {
                HudZones.FrameOverride = -1;
                HudZones.Clear();
            }
        }

        [Test]
        public void ArtNamesMatchTheBrief()
        {
            Assert.AreEqual("bm-portrait-warrior-azure", MomentArt.PortraitName(ClassId.Warrior, TeamId.Azure));
            Assert.AreEqual("bm-portrait-demolitions-crimson", MomentArt.PortraitName(ClassId.Demolitions, TeamId.Crimson));
            Assert.AreEqual("bm-portrait-farmer-teammask", MomentArt.TeamMaskName(ClassId.Farmer));
            Assert.AreEqual("bm-kaiju-berserk", MomentArt.KaijuName(KaijuState.Berserk));
            Assert.AreEqual("bm-title-plate", MomentArt.PlateName(PlateKind.Takeover));
            Assert.AreEqual("bm-stinger-plate", MomentArt.PlateName(PlateKind.Stinger));
        }

        [Test]
        public void DeliveredSheetsAndPlatesAreOnDisk()
        {
            const string dir = "Assets/_Game/Resources/UI/BigMoments/";
            foreach (string n in MomentArt.Sheets) Assert.IsTrue(File.Exists(dir + n + ".png"), n);
            foreach (string n in MomentArt.Plates) Assert.IsTrue(File.Exists(dir + n + ".png"), n);
        }

        [Test]
        public void ProceduralSheetsCoverEveryMissingSheet()
        {
            MomentArt.Loader = _ => null;
            foreach (string n in MomentArt.Sheets)
            {
                Texture2D t = ProceduralSheets.SheetOrFallback(n);
                Assert.IsNotNull(t, n);
                Assert.Greater(t.width, 8, n);
            }
        }

        // ------------------------------------------------------------ triggers

        [Test]
        public void OptionalEventsResolveByName()
        {
            Assert.AreEqual((int)SimEventType.Hit, MomentTriggers.Code("Hit"));
            Assert.AreEqual(-1, MomentTriggers.Code("NoSuchEventAnywhere"));
        }

        [Test]
        public void FirstKillAndThePlayersStreakRaiseMoments()
        {
            World w = NewWorld();
            var t = new MomentTriggers();
            t.Observe(w);
            int e0 = Enemy(w, 0), e1 = Enemy(w, 1), e2 = Enemy(w, 2);
            World.PushEvent(w, SimEventType.Death, e0, 0, -500, -500, 30);
            t.Observe(w);
            Assert.AreEqual(1, t.Count);
            Assert.AreEqual(MomentId.OpeningStrike, t.Get(0).Id);
            Assert.IsTrue(t.Get(0).PlayerOwned);

            World.PushEvent(w, SimEventType.Death, e1, 0, -500, -500, 30);
            t.Observe(w);
            Assert.AreEqual(MomentId.TwoDown, t.Get(0).Id);
            World.PushEvent(w, SimEventType.Death, e2, 0, -500, -500, 30);
            t.Observe(w);
            Assert.AreEqual(MomentId.ThreeDown, t.Get(0).Id);
            w.Actors[0].Alive = true;   // test world only: the tight-fight rule needs a living player
            Assert.IsTrue(t.TightFight(w), "killing counts as a fight");
        }

        [Test]
        public void SwingThatHitsThreeIsABatteringRam()
        {
            World w = NewWorld();
            var t = new MomentTriggers();
            t.Observe(w);
            for (int i = 0; i < 3; i++) World.PushEvent(w, SimEventType.Hit, Enemy(w, i), 0, 0, 0, 10);
            t.Observe(w);
            Assert.AreEqual(1, t.Count);
            Assert.AreEqual(MomentId.BatteringRam, t.Get(0).Id);
        }

        [Test]
        public void SlamKillsQuakeAndKaijuEventsRaiseKaijuMoments()
        {
            World w = NewWorld();
            var t = new MomentTriggers();
            t.Observe(w);
            World.PushEvent(w, SimEventType.Death, Enemy(w, 0), -1, 10, 10, 45);
            World.PushEvent(w, SimEventType.Death, Enemy(w, 1), -1, 11, 10, 45);
            World.PushEvent(w, SimEventType.KaijuSlam, -1, 2, 10, 10, 4);
            t.Observe(w);
            Assert.AreEqual(MomentId.Quake, t.Get(0).Id);
            Assert.AreEqual(2, t.Get(0).Count);

            World.PushEvent(w, SimEventType.Hatch, -1, -1, 0, 0, 3);
            World.PushEvent(w, SimEventType.KaijuKO, -1, 0, 0, 0, 0);
            t.Observe(w);
            Assert.AreEqual(MomentId.Hatch, t.Get(0).Id);
            Assert.AreEqual(KaijuState.Berserk, t.Get(0).Kaiju);
            Assert.AreEqual(MomentId.KaijuDown, t.Get(1).Id);
            Assert.IsTrue(t.Get(1).PlayerOwned);
        }

        [Test]
        public void EndFrameChoosesWireTurnaroundOrFinisher()
        {
            Assert.AreEqual(MomentId.AtTheWire, MomentTriggers.EndMoment(TeamId.Azure, 480, 475, 0));
            Assert.AreEqual(MomentId.Turnaround, MomentTriggers.EndMoment(TeamId.Azure, 480, 300, 45));
            Assert.AreEqual(MomentId.Finisher, MomentTriggers.EndMoment(TeamId.Azure, 480, 100, 5));
        }

        [Test]
        public void NewMatchStartsTheTriggersOver()
        {
            World w = NewWorld();
            var t = new MomentTriggers();
            t.Observe(w);
            World.PushEvent(w, SimEventType.Death, Enemy(w, 0), 0, -500, -500, 30);
            t.Observe(w);
            Assert.AreEqual(MomentId.OpeningStrike, t.Get(0).Id);
            World w2 = NewWorld();
            w2.Epoch = w.Epoch + 1;
            t.Observe(w2);
            World.PushEvent(w2, SimEventType.Death, Enemy(w2, 0), 0, -500, -500, 30);
            t.Observe(w2);
            Assert.AreEqual(MomentId.OpeningStrike, t.Get(0).Id, "first blood again in the new match");
        }

        // ------------------------------------------------------------ banner hold

        [Test]
        public void BannerPostponeRestartsTheEntrance()
        {
            var b = new BannerQueue();
            b.Show(new BannerSpec { Headline = "X", Priority = 1 }, 0);
            Assert.AreEqual(1f, b.Alpha(1.0), 1e-4f);
            b.Postpone(2.0);
            Assert.AreEqual(0f, b.Alpha(2.0), 1e-4f, "held: enters fresh");
            b.Update(2.0 + BannerQueue.InSeconds + 1.0);
            Assert.IsTrue(b.HasCurrent, "the full hold runs after the takeover");
        }
    }
}

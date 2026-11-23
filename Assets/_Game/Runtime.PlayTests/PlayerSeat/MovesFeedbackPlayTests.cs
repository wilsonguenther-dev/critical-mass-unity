// Class moves build (2026-11-22), lane 6 (T1-7): the player-facing side of the new verbs, tested.
// Plain tests: the pure rules (words, slot words, the charge glow, T1-6 cast distance, slide pose, parry hold, help tables).
// Tier F: the Boot scene with real devices (PlayerSeatFixture); every check samples AFTER the end of the rendered frame, so
// "on screen" means drawn on that frame, not merely queued.
using System;
using System.Collections;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Player;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.View.B17;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CameraView = CriticalMass.Runtime.Camera.CameraView;

namespace CriticalMass.Runtime.PlayTests.PlayerSeat
{
    /// <summary>Lane 6 pure rules (no scene).</summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class MovesFeedbackWordsTests
    {
        [Test]
        public void MovesWordsSayWhatHappenedToTheRightViewer()
        {
            Assert.AreEqual(ActionTags.GuardBreakWord, ActionTags.MovesWords(SimEventType.GuardBreak, 0, 0, true, false));
            Assert.AreEqual(ActionTags.GuardBrokenWord, ActionTags.MovesWords(SimEventType.GuardBreak, 0, 0, false, true));
            Assert.IsNull(ActionTags.MovesWords(SimEventType.GuardBreak, 0, 0, false, false), "a bystander sees no guard-break word");
            Assert.AreEqual(ActionTags.BaitedWord, ActionTags.MovesWords(SimEventType.FeintBaited, 0, 0, false, true));
            Assert.AreEqual(ActionTags.LungeWord, ActionTags.MovesWords(SimEventType.Lunge, 0, 0, true, false));
            Assert.AreEqual(ActionTags.ChargedWord, ActionTags.MovesWords(SimEventType.HeavyRelease, 1.0, 0, true, false));
            Assert.IsNull(ActionTags.MovesWords(SimEventType.HeavyRelease, 0.6, 0, true, false), "a part charge is not CHARGED");
            Assert.AreEqual("x2", ActionTags.MovesWords(SimEventType.ComboStep, 2, 0, true, false));
            Assert.AreEqual("x3", ActionTags.MovesWords(SimEventType.ComboStep, 3, 0, true, false));
            Assert.AreEqual(ActionTags.FinisherWord, ActionTags.MovesWords(SimEventType.ComboStep, 3, 1, true, false));
            Assert.AreEqual(ActionTags.TrippedWord, ActionTags.MovesWords(SimEventType.Slide, 1, 0, true, false));
            Assert.IsNull(ActionTags.MovesWords(SimEventType.Slide, 0, 0, true, false), "a slide that trips nobody says nothing");
            Assert.AreEqual(ActionTags.SecondWindWord, ActionTags.MovesWords(SimEventType.SecondWind, 0, 0, false, true));
            Assert.IsNull(ActionTags.MovesWords(SimEventType.Hit, 5, 0, true, false), "old events keep their own words");
        }

        [Test]
        public void SlotWordsFollowTheChargeTheChainAndTravel()
        {
            Assert.AreEqual(MovesRead.ChargeWord, MovesRead.AttackSlotWord(true, 0.4f, 0));
            Assert.AreEqual(MovesRead.ChargedWord, MovesRead.AttackSlotWord(true, 1f, 0));
            Assert.AreEqual(MovesRead.ChargeWord, MovesRead.AttackSlotWord(true, 0.4f, 2), "the hold decides: a charge wins over a chain");
            Assert.AreEqual("x2", MovesRead.AttackSlotWord(false, 0f, 2));
            Assert.AreEqual("x3", MovesRead.AttackSlotWord(false, 0f, 3));
            Assert.IsNull(MovesRead.AttackSlotWord(false, 0f, 0), "nothing armed: the slot keeps its own word");
            Assert.AreEqual("SLIDE Shift", MovesRead.DodgeSlotWord(true, "Shift"));
            Assert.AreEqual(MovesRead.SlideWord, MovesRead.DodgeSlotWord(true, null));
            Assert.IsNull(MovesRead.DodgeSlotWord(false, "Shift"));
            Assert.AreEqual("", MovesRead.ComboWord(1));
        }

        [Test]
        public void ChargeGlowRisesInThreeStepsAndRampsWithReduceFlashing()
        {
            Assert.AreEqual(0, MovesRead.ChargeStep(0f));
            Assert.AreEqual(1, MovesRead.ChargeStep(0.2f));
            Assert.AreEqual(2, MovesRead.ChargeStep(0.6f));
            Assert.AreEqual(3, MovesRead.ChargeStep(1f));
            float prevStep = 0f, prevRamp = 0f;
            int changes = 0;
            for (int k = 1; k <= 100; k++)
            {
                float s = k / 100f;
                float a = MovesViews.GlowAlpha(s, false), r = MovesViews.GlowAlpha(s, true);
                Assert.GreaterOrEqual(a, prevStep, "the glow never dims during a charge (share " + s + ")");
                Assert.GreaterOrEqual(r, prevRamp, "the reduced glow never dims (share " + s + ")");
                Assert.Less(r - prevRamp, 0.13f, "reduce flashing: no jump in the glow (share " + s + ")");
                if (a != prevStep) changes++;
                prevStep = a;
                prevRamp = r;
            }
            Assert.LessOrEqual(changes, 3, "at most three glow steps in one charge");
            Assert.AreEqual(0f, MovesViews.GlowAlpha(0f, true));
        }

        [Test]
        public void StickCastDistanceFollowsTheDeflection()   // T1-6
        {
            Assert.AreEqual(AimResolver.MovesCastMin, AimResolver.MovesStickCastDistance(AimResolver.MovesStickMin, 8), 1e-9);
            Assert.AreEqual(8, AimResolver.MovesStickCastDistance(1.0, 8), 1e-9);
            Assert.AreEqual(3, AimResolver.MovesStickCastDistance(0.1, 8), 1e-9, "a resting stick casts 3 u ahead");
            Assert.AreEqual(2, AimResolver.MovesStickCastDistance(0.1, 2), 1e-9, "never past the range");
            double prev = 0;
            for (int k = 30; k <= 100; k++)
            {
                double d = AimResolver.MovesStickCastDistance(k / 100.0, 8);
                Assert.GreaterOrEqual(d, prev - 1e-12, "a deeper stick never casts nearer (" + k + "%)");
                prev = d;
            }
        }

        [Test]
        public void KeysCastLandsOnTheAssistedEnemyElseThreeAhead()   // T1-6
        {
            double[] ex = { 5, 0, 10 }, ez = { 0, 5, 0 };
            AimResolver.MovesKeysCastPoint(0, 0, 1, 0, 8, ex, ez, 3, out double ax, out double az, out int target);
            Assert.AreEqual(0, target, "the enemy straight ahead at 5 u");
            Assert.AreEqual(5, ax, 1e-9);
            Assert.AreEqual(0, az, 1e-9);
            AimResolver.MovesKeysCastPoint(0, 0, 1, 0, 4, ex, ez, 3, out ax, out az, out target);
            Assert.LessOrEqual(Math.Sqrt(ax * ax + az * az), 4 + 1e-9, "capped at the range");
            AimResolver.MovesKeysCastPoint(0, 0, 0, -1, 8, ex, ez, 3, out ax, out az, out target);
            Assert.AreEqual(-1, target, "nobody inside 30 degrees");
            Assert.AreEqual(0, ax, 1e-9);
            Assert.AreEqual(-3, az, 1e-9, "3 u ahead");
        }

        [Test]
        public void SlidePoseAndParryHold()
        {
            MovesPoses.Slide(-1f, out float lean, out float squash);
            Assert.AreEqual(0f, lean);
            Assert.AreEqual(1f, squash);
            MovesPoses.Slide(0.5f, out lean, out squash);
            Assert.AreEqual(MovesPoses.SlideLeanDeg, lean, 1e-5);
            Assert.AreEqual(MovesPoses.SlideSquashY, squash, 1e-5);
            MovesPoses.Slide(1f, out lean, out squash);
            Assert.AreEqual(0f, lean, 1e-5);
            Assert.AreEqual(1f, squash, 1e-5);
            MovesPoses.ClearHolds();
            MovesPoses.Hold(3, 10.0, MovesPoses.ParryHoldSeconds);
            Assert.IsTrue(MovesPoses.Holding(3, 10.05));
            Assert.IsFalse(MovesPoses.Holding(3, 10.09), "80 ms, not longer");
            Assert.IsFalse(MovesPoses.Holding(4, 10.05));
            MovesPoses.ClearHolds();
        }

        [Test]
        public void WordRanksPutParryOnTop()   // readability item 2
        {
            Assert.Greater(WorldWords.Rank(ActionTags.ParryWord), WorldWords.Rank(ActionTags.GuardBreakWord));
            Assert.Greater(WorldWords.Rank(ActionTags.GuardBreakWord), WorldWords.Rank(ActionTags.FinisherWord));
            Assert.Greater(WorldWords.Rank(ActionTags.FinisherWord), WorldWords.Rank("BLOCKED"));
            Assert.Greater(WorldWords.Rank("BLOCKED"), WorldWords.Rank(ActionTags.SecondWindWord));
            Assert.AreEqual(WorldWords.Rank("STUNNED"), WorldWords.Rank("PERFECT"), "the rest share the lowest rank");
            Assert.AreEqual(1, WorldWords.MaxPerAnchor, "readability-3: one word per body");
        }

        [Test]
        public void CrowdAndEdgeRules()   // readability-4 items 1, 2, 4, 5
        {
            Assert.IsTrue(CrowdTags.Pick(5, 0, 5, -1, false, 400.0), "my opponent is named");
            Assert.IsTrue(CrowdTags.Pick(6, 0, -1, 6, false, 400.0), "the enemy under my aim is named");
            Assert.IsFalse(CrowdTags.Pick(7, 0, 5, 6, false, 1.0), "another enemy: bar only");
            Assert.IsTrue(CrowdTags.Pick(8, 0, -1, -1, true, 5.9 * 5.9), "an ally within 6 u");
            Assert.IsFalse(CrowdTags.Pick(9, 0, -1, -1, true, 6.1 * 6.1), "an ally further away: bar only");
            var bars = new[] { new Rect(0f, 0f, 50f, 10f), new Rect(10f, 4f, 50f, 10f), new Rect(20f, 2f, 50f, 10f) };
            HealthBars.SeparateAll(bars, 3);
            for (int i = 0; i < 3; i++) for (int j = i + 1; j < 3; j++) Assert.IsFalse(bars[i].Overlaps(bars[j]), "bars " + i + " and " + j + " de-overlap");
            Assert.IsTrue(ViewHintGate.Rule(0, 500.0) && ViewHintGate.Rule(1, 500.0), "first two matches: always");
            Assert.IsTrue(ViewHintGate.Rule(2, 30.0), "third match: the first minute");
            Assert.IsFalse(ViewHintGate.Rule(2, 61.0), "third match after a minute: hidden");
            Assert.AreEqual("SPRINTING: SHIFT TO SLIDE", MovesHelp.HintText(MovesHelp.Verb.Slide, "Shift"));
            bool was = WorldWords.MovesCap;
            try
            {
                WorldWords.MovesCap = false;
                EdgeSpread.Reset();
                Assert.AreEqual(new Vector2(1900f, 500f), EdgeSpread.Spread(1, new Vector2(1900f, 500f), new Vector2(44f, 60f), 1920f, 1080f, 10f, out bool off), "off: unchanged");
                Assert.IsTrue(off);
            }
            finally { WorldWords.MovesCap = was; EdgeSpread.Reset(); }
        }

        [Test]
        public void OneCalloutBudgetRules()   // readability-3 items 1, 2
        {
            Assert.AreEqual(WorldWords.Group("COUNTERED"), WorldWords.Group("INTERRUPTED"), "near-duplicates merge");
            Assert.AreEqual(WorldWords.Group("COUNTER!"), WorldWords.Group("PUNISH!"));
            Assert.AreNotEqual(WorldWords.Group("COUNTER!"), WorldWords.Group("COUNTERED"), "mine and his are different words");
            Assert.AreEqual(0, WorldWords.Group("BLOCKED"));
            Assert.IsTrue(WorldWords.ShownInThirdPerson(3, 3, -1), "over me");
            Assert.IsTrue(WorldWords.ShownInThirdPerson(7, 3, 7), "over my opponent");
            Assert.IsFalse(WorldWords.ShownInThirdPerson(9, 3, 7), "someone else's fight stays quiet");
            Assert.IsTrue(WorldWords.ShownInThirdPerson(-1, 3, 7), "a word on a point");
            var feed = WorldWords.RightAnchored(new Rect(1500f, 136f, 400f, 230f), 1920f, 1080f);
            Vector2 p = new Vector2(1600f, 1080f - 200f);
            Assert.IsTrue(WorldWords.Clear(ref p, 40f, feed), "a word in the kill feed column moves");
            Assert.LessOrEqual(p.y + WorldWords.PillH * 0.5f, feed.yMin, "under the panel");
            Assert.AreEqual(DamageNumbers.Kind.Crit, DamageNumbers.Stronger(DamageNumbers.Kind.Hit, DamageNumbers.Kind.Crit));
            Assert.AreEqual(DamageNumbers.Kind.Heavy, DamageNumbers.Stronger(DamageNumbers.Kind.Heavy, DamageNumbers.Kind.Hit));
            Assert.AreEqual(0.25f, DamageNumbers.MergeWindow);
        }

        [Test]
        public void WordsStayBelowTheTopHudRows()   // readability-2 item 3
        {
            float h = 1080f;
            float noBanner = WorldWords.TopReserve(false), banner = WorldWords.TopReserve(true);
            Assert.GreaterOrEqual(banner, noBanner, "the objective banner lane reserves at least the top band");
            float top = WorldWords.TopLimit(h, banner, 1), low = WorldWords.TopLimit(h, banner, 0);
            Assert.LessOrEqual(top + WorldWords.PillH * 0.5f, h - banner, "the top word's pill ends under the banner lane");
            Assert.AreEqual(WorldWords.PillH + 4f, top - low, 1e-4, "the two words keep their stagger at the limit");
        }

        [Test]
        public void PredictedGuardShowsAtOnceAndFadesWhenRefused()   // readability item 1
        {
            Assert.AreEqual(1f, MovesViews.GuardPrediction(true, false, 0f), "the press frame itself");
            Assert.AreEqual(1f, MovesViews.GuardPrediction(true, false, MovesViews.GuardPredictWait), "waiting for the sim's tick");
            Assert.AreEqual(1f, MovesViews.GuardPrediction(true, true, 5f), "the sim holds it");
            Assert.AreEqual(0f, MovesViews.GuardPrediction(true, false, MovesViews.GuardPredictWait + MovesViews.GuardPredictFade), 1e-6, "refused: gone within 0.12 s");
            Assert.AreEqual(0f, MovesViews.GuardPrediction(false, false, 0f), "released before the sim took it");
            float mid = MovesViews.GuardPrediction(true, false, MovesViews.GuardPredictWait + MovesViews.GuardPredictFade * 0.5f);
            Assert.That(mid, Is.InRange(0.4f, 0.6f));
        }

        [Test]
        public void EveryHelpLineHasAButtonAndEveryHintAKey()
        {
            foreach (string line in MovesHelp.CardLines) Assert.IsNotNull(MovesHelp.CardRow(line), line);
            for (int v = 0; v < (int)MovesHelp.Verb.Count; v++)
            {
                var verb = (MovesHelp.Verb)v;
                Assert.IsNotNull(MovesHelp.HintAction(verb), verb.ToString());
                StringAssert.Contains("Q", MovesHelp.HintText(verb, "q"), verb + ": the hint names the button");
            }
        }
    }

    /// <summary>Lane 6 tier F: each verb's feedback is drawn on the rendered frame it is due.</summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class MovesFeedbackPlayTests : PlayerSeatFixture
    {
        readonly int?[] _hintPrefs = new int?[(int)MovesHelp.Verb.Count];

        public override void Setup()
        {
            base.Setup();
            for (int v = 0; v < _hintPrefs.Length; v++)
            {
                string k = MovesHelp.PrefKey((MovesHelp.Verb)v);
                _hintPrefs[v] = CmPrefs.HasKey(k) ? CmPrefs.GetInt(k) : (int?)null;
            }
        }

        public override void TearDown()
        {
            for (int v = 0; v < _hintPrefs.Length; v++)
            {
                string k = MovesHelp.PrefKey((MovesHelp.Verb)v);
                if (_hintPrefs[v].HasValue) CmPrefs.SetInt(k, _hintPrefs[v].Value);
                else CmPrefs.DeleteKey(k);
            }
            MovesPoses.ClearHolds();
            base.TearDown();
        }

        /// <summary>Every class-moves switch on in the loaded match (a test write; the shipped game decides at world creation).</summary>
        void MovesOn()
        {
            MovesConfig.PlayBuildAllOn(W.Config.Depth.Moves);
            MovesState m = MovesState.Create(W.Config);
            if (m == null) Assert.Ignore("this match is not the Full rules: the class moves cannot exist here");
            W.Moves = m;
            m.Init(W);
        }

        static MovesViews Views
        {
            get
            {
                Assert.IsNotNull(MovesViews.Current, "MovesViews is not in the Boot scene (DodgeView.Awake adds it)");
                return MovesViews.Current;
            }
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator SwitchesOffShowNothingNew()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            if (MovesRead.On(W)) Assert.Ignore("this build turns the class moves on (CM_MOVES_ON)");
            Isolate(ClassId.Farmer, 2.0, 1, 0);
            double x = W.X[Enemy], z = W.Z[Enemy];
            PressAttack(CameraView.TopDown);
            for (int t = 0; t < 30; t++) yield return TickFrame(() => Pin(Enemy, x, z));
            ReleaseAttack();
            yield return EndOfFrame();   // fix/px-view-2: WaitForEndOfFrame never resumes under -nographics (shard hang)
            MovesViews v = Views;
            Assert.AreEqual(0, v.GuardArcsShown);
            Assert.AreEqual(0, v.ChargeShapesShown);
            Assert.AreEqual(0, v.GlideRingsShown);
            Assert.AreEqual(0, v.PuffsAlive);
            Assert.IsNull(v.LastWord);
            var anim = S.Views.AnimatorOf(0);
            Assert.IsNotNull(anim);
            Assert.AreEqual(0f, anim.MovesGuardWeight);
            Assert.AreEqual(0f, anim.MovesChargeWeight);
            Assert.AreEqual(0f, anim.MovesGlideWeight);
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator GuardArcOnTheFirstFrameAndPoseWithinTwoFrames()
        {
            yield return Load(ClassId.Builder, CameraView.TopDown);
            MovesOn();
            Isolate(ClassId.Warrior, 6.0, 1, 0);
            yield return null;
            DefenceMovesState d = W.Moves.Defence;
            MovesConfig m = W.Config.Depth.Moves;
            d.GuardOn[0] = 1;
            d.GuardSince[0] = W.Tick - m.GuardRaiseTicks;
            d.GuardMeter[0] = m.GuardMeter[(int)ClassId.Builder] * 0.5;
            Assert.IsTrue(MovesRead.GuardBlocking(W, 0), "the staged guard blocks");
            float t0 = Time.unscaledTime;
            yield return EndOfFrame();   // fix/px-view-2: WaitForEndOfFrame never resumes under -nographics (shard hang)
            Assert.GreaterOrEqual(Views.GuardArcsShown, 1, "the guard meter arc is drawn on the first frame of the guard");
            Assert.GreaterOrEqual(S.Views.AnimatorOf(0).MovesGuardWeight, MovesPoses.GuardFirstFrame, "readability-2: the guard pose is at 0.8 on its first frame, not on the 33 ms edge");
            while (Time.unscaledTime - t0 < (float)MovesPoses.GuardRiseSeconds + 0.02f) yield return null;
            yield return EndOfFrame();   // fix/px-view-2: WaitForEndOfFrame never resumes under -nographics (shard hang)
            Assert.GreaterOrEqual(S.Views.AnimatorOf(0).MovesGuardWeight, 0.99f, "the guard pose is full within 33 ms");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator HoldingAttackChargesAHeavyOnScreenAndReleaseSaysCharged()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            MovesOn();
            Isolate(ClassId.Farmer, 7.0, 1, 0);
            double x = W.X[Enemy], z = W.Z[Enemy];
            MovesConfig m = W.Config.Depth.Moves;
            yield return null;
            PressAttack(CameraView.TopDown);
            int firstCharge = -1;
            for (int t = 0; t < m.ChargeFullTick + 4; t++)
            {
                yield return TickFrame(() => Pin(Enemy, x, z));
                if (firstCharge < 0 && MovesRead.Charging(W, 0, out _)) firstCharge = t;
            }
            yield return EndOfFrame();   // fix/px-view-2: WaitForEndOfFrame never resumes under -nographics (shard hang)
            Assert.GreaterOrEqual(firstCharge, 0, "holding the attack button charged a heavy (Heavy switch, lane 4)");
            Assert.IsTrue(MovesRead.Charging(W, 0, out float share));
            Assert.GreaterOrEqual(share, 0.999f, "full charge after ChargeFullTick");
            Assert.GreaterOrEqual(Views.ChargeShapesShown, 1, "the heavy's ground telegraph is drawn while charging");
            Assert.Greater(S.Views.AnimatorOf(0).MovesChargeWeight, 0.5f, "the charge pose holds the wind-up");
            Assert.AreEqual(MovesRead.ChargedWord, MovesRead.AttackSlotWord(W, 0), "the attack slot says CHARGED");
            ReleaseAttack();
            int said = -1;
            for (int f = 0; f < 6 && said < 0; f++)
            {
                yield return TickFrame(() => Pin(Enemy, x, z));
                yield return EndOfFrame();   // fix/px-view-2: WaitForEndOfFrame never resumes under -nographics (shard hang)
                if (Views.LastWord == ActionTags.ChargedWord) said = f;
            }
            Assert.GreaterOrEqual(said, 0, "CHARGED shows after a full-charge release");
            Assert.LessOrEqual(said, 1, "CHARGED shows on the release tick's frame or the next");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator EventWordsShowOnTheNextRenderedFrame()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            MovesOn();
            int e = Isolate(ClassId.Farmer, 2.0, 1, 0);
            yield return null;
            yield return null;   // MovesViews' event cursor meets this world

            yield return Expect(() => World.PushEvent(W, SimEventType.SecondWind, 0, -1, W.X[0], W.Z[0], 0), ActionTags.SecondWindWord);
            yield return Expect(() => World.PushEvent(W, SimEventType.ComboStep, 0, -1, W.X[e], W.Z[e], 2), "x2");
            // readability-3: one word per body by rank, so the low-ranked TRIPPED goes before GUARD BREAK (rank 5) on the same body
            int puffs = Views.PuffsAlive;
            yield return Expect(() => World.PushEvent(W, SimEventType.Slide, 0, e, W.X[e], W.Z[e], 0), ActionTags.TrippedWord);
            Assert.Greater(Views.PuffsAlive, puffs, "the trip throws dust (a trip with no damage too)");
            yield return Expect(() => World.PushEvent(W, SimEventType.GuardBreak, e, 0, W.X[e], W.Z[e], 0), ActionTags.GuardBreakWord);
            yield return Expect(() => World.PushEvent(W, SimEventType.FeintBaited, 0, e, W.X[0], W.Z[0], 0), ActionTags.BaitedWord);

            yield return Expect(() => World.PushEvent(W, SimEventType.HitBlocked, e, 0, W.X[e], W.Z[e], PlayerReasons.BlockedParried), ActionTags.ParryWord);
            double now = Time.unscaledTimeAsDouble;
            Assert.IsTrue(MovesPoses.Holding(e, now), "parry hold (80 ms) latched on the parrier, same frame");
            Assert.IsTrue(MovesPoses.Holding(0, now), "parry hold (80 ms) latched on the attacker, same frame");
        }

        IEnumerator Expect(Action push, string word)
        {
            // fix/px-view-2 (review: SECOND WIND drawn at frame 32, expected 33): the event is pushed in frame N's coroutine
            // step, before that frame's LateUpdate, so the word is drawn on frame N itself, the first rendered frame after the
            // event, just as a sim event decoded in a frame's Update is drawn in that frame's LateUpdate. The old rig read the
            // frame number after a yield (N + 1).
            int pushed = Time.frameCount;
            push();
            yield return null;
            yield return EndOfFrame();
            Assert.AreEqual(word, Views.LastWord, "the word for the event just pushed");
            Assert.That(Views.LastWordFrame, Is.InRange(pushed, pushed + 1), word + " is drawn on the first rendered frame after its event (pushed on frame " + pushed + ", drawn on " + Views.LastWordFrame + ")");
        }
    }
}

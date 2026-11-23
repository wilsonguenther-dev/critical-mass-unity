// Class moves build (2026-11-22), lane 6 (T1-7): the player-facing side of the new verbs, tested.
// Plain tests: the pure rules (words, slot words, the charge glow, T1-6 cast distance, slide pose, parry hold, help tables).
// Tier F: the Boot scene with real devices (PlayerSeatFixture); every check samples AFTER the end of the rendered frame, so
// "on screen" means drawn on that frame, not merely queued.
using System;
using System.Collections;
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
                _hintPrefs[v] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
            }
        }

        public override void TearDown()
        {
            for (int v = 0; v < _hintPrefs.Length; v++)
            {
                string k = MovesHelp.PrefKey((MovesHelp.Verb)v);
                if (_hintPrefs[v].HasValue) PlayerPrefs.SetInt(k, _hintPrefs[v].Value);
                else PlayerPrefs.DeleteKey(k);
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

        [UnityTest]
        public IEnumerator SwitchesOffShowNothingNew()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            if (MovesRead.On(W)) Assert.Ignore("this build turns the class moves on (CM_MOVES_ON)");
            Isolate(ClassId.Farmer, 2.0, 1, 0);
            double x = W.X[Enemy], z = W.Z[Enemy];
            PressAttack(CameraView.TopDown);
            for (int t = 0; t < 30; t++) yield return TickFrame(() => Pin(Enemy, x, z));
            ReleaseAttack();
            yield return new WaitForEndOfFrame();
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

        [UnityTest]
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
            yield return new WaitForEndOfFrame();
            Assert.GreaterOrEqual(Views.GuardArcsShown, 1, "the guard meter arc is drawn on the first frame of the guard");
            while (Time.unscaledTime - t0 < (float)MovesPoses.GuardRiseSeconds + 0.02f) yield return null;
            yield return new WaitForEndOfFrame();
            Assert.GreaterOrEqual(S.Views.AnimatorOf(0).MovesGuardWeight, 0.99f, "the guard pose is full within 33 ms");
        }

        [UnityTest]
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
            yield return new WaitForEndOfFrame();
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
                yield return new WaitForEndOfFrame();
                if (Views.LastWord == ActionTags.ChargedWord) said = f;
            }
            Assert.GreaterOrEqual(said, 0, "CHARGED shows after a full-charge release");
            Assert.LessOrEqual(said, 1, "CHARGED shows on the release tick's frame or the next");
        }

        [UnityTest]
        public IEnumerator EventWordsShowOnTheNextRenderedFrame()
        {
            yield return Load(ClassId.Warrior, CameraView.TopDown);
            MovesOn();
            int e = Isolate(ClassId.Farmer, 2.0, 1, 0);
            yield return null;
            yield return null;   // MovesViews' event cursor meets this world

            yield return Expect(() => World.PushEvent(W, SimEventType.SecondWind, 0, -1, W.X[0], W.Z[0], 0), ActionTags.SecondWindWord);
            yield return Expect(() => World.PushEvent(W, SimEventType.ComboStep, 0, -1, W.X[e], W.Z[e], 2), "x2");
            yield return Expect(() => World.PushEvent(W, SimEventType.GuardBreak, e, 0, W.X[e], W.Z[e], 0), ActionTags.GuardBreakWord);
            yield return Expect(() => World.PushEvent(W, SimEventType.FeintBaited, 0, e, W.X[0], W.Z[0], 0), ActionTags.BaitedWord);

            int puffs = Views.PuffsAlive;
            yield return Expect(() => World.PushEvent(W, SimEventType.Slide, 0, e, W.X[e], W.Z[e], 1), ActionTags.TrippedWord);
            Assert.Greater(Views.PuffsAlive, puffs, "the trip throws dust");

            yield return Expect(() => World.PushEvent(W, SimEventType.HitBlocked, e, 0, W.X[e], W.Z[e], PlayerReasons.BlockedParried), ActionTags.ParryWord);
            double now = Time.unscaledTimeAsDouble;
            Assert.IsTrue(MovesPoses.Holding(e, now), "parry hold (80 ms) latched on the parrier, same frame");
            Assert.IsTrue(MovesPoses.Holding(0, now), "parry hold (80 ms) latched on the attacker, same frame");
        }

        IEnumerator Expect(Action push, string word)
        {
            push();
            yield return null;
            int frame = Time.frameCount;
            yield return new WaitForEndOfFrame();
            Assert.AreEqual(word, Views.LastWord, "the word for the event just pushed");
            Assert.AreEqual(frame, Views.LastWordFrame, word + " is drawn on the first frame after its event");
        }
    }
}

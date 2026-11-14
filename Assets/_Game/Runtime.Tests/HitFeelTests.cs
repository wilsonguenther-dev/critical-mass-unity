using System;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// feat/hit-feel-1: the swing's contact frame on the damage tick, the attacker facing and stepping into its target, the
    /// victim reacting away from the blow, hit-stop on both fighters, the camera nudge and the capped pain / effort voice.
    /// </summary>
    public sealed class HitFeelTests
    {
        static readonly ClassId[] All = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        static World NewWorld(ClassId cls) => World.CreateWorld(new WorldOptions
        {
            Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = GameConfig.CreateDefault(),
        });

        static int Enemy(World w)
        {
            for (int i = 1; i < w.ActorCount; i++) if (w.Actors[i].Team != w.Actors[0].Team && w.Actors[i].Alive) return i;
            return -1;
        }

        /// <summary>Puts actor 0 in a swing pressed `age` ticks ago with wind-up W, facing +x, and an enemy at (dx, dz).</summary>
        static World Swinging(ClassId cls, int windup, int age, double dx, double dz, out int enemy)
        {
            World w = NewWorld(cls);
            w.Tick = 200;
            enemy = Enemy(w);
            Actor a = w.Actors[0];
            a.SwingTick = w.Tick - age;
            a.SwingWindup = windup;
            a.SwingFacing = 0;
            a.Facing = 0;
            for (int i = 1; i < w.ActorCount; i++) { w.X[i] = w.X[0] + 40 + i; w.Z[i] = w.Z[0] + 40; }   // everyone else far away
            w.X[enemy] = w.X[0] + dx;
            w.Z[enemy] = w.Z[0] + dz;
            return w;
        }

        // ---------------------------------------------------------------- timing

        [Test]
        public void EveryClassHitsItsContactFrameOnTheDamageTick()
        {
            const double hz = 30;
            foreach (ClassId cls in All)
                for (int windup = 1; windup <= 14; windup++)
                {
                    string clip = ArtMapping.ClassClips(cls).Attack;
                    if (cls == ClassId.Healer || cls == ClassId.Demolitions || cls == ClassId.Builder) clip = clip ?? ArtMapping.Attack;
                    double c = ClipFacts.Contact(clip, 0.567f), len = ClipFacts.Get(clip) != null ? ClipFacts.Get(clip).length : 1.067;
                    double w = windup / hz;
                    Assert.AreEqual(c, AttackTiming.ClipTime(w, c, len, w), 1e-9, $"{cls} W={windup}: contact frame on the damage tick");
                    Assert.Less(AttackTiming.ClipTime(w - 1 / hz, c, len, w), c, $"{cls}: anticipation before contact");
                    Assert.Greater(AttackTiming.ClipTime(w + 1 / hz, c, len, w), c, $"{cls}: follow-through after contact");
                    Assert.AreEqual(w, AttackTiming.ContactAge(c, w), 1e-9, $"{cls}: 0 ms offset");
                }
        }

        // ---------------------------------------------------------------- facing and lunge

        [Test]
        public void TheAttackerTurnsToTheEnemyItSwingsAt()
        {
            World w = Swinging(ClassId.Warrior, 4, 2, 1.0, 0.4, out int e);
            Assert.AreEqual(e, HitFeel.SwingTarget(w, 0));
            Assert.AreEqual(Math.Atan2(0.4, 1.0), HitFeel.Facing(w, 0, 0), 1e-9, "faces the target, not just the press direction");

            World behind = Swinging(ClassId.Warrior, 4, 2, -1.0, 0, out _);
            Assert.AreEqual(-1, HitFeel.SwingTarget(behind, 0), "an enemy behind is not the target");
            Assert.AreEqual(0, HitFeel.Facing(behind, 0, 0), 1e-9, "falls back to the sim's swing facing");

            World late = Swinging(ClassId.Warrior, 4, 4 + 30, 1.0, 0.4, out _);
            late.Actors[0].Facing = 1.0;
            Assert.AreEqual(1.0, HitFeel.Facing(late, 0, 0), 1e-9, "after the follow-through the sim's facing rules again");
        }

        [Test]
        public void TheLungePeaksOnContactAndIsAbsorbed()
        {
            const double W = 0.2;
            Assert.AreEqual(0, HitFeel.LungeShape(0, W), 1e-9);
            Assert.Less(HitFeel.LungeShape(W * 0.3, W), 0, "a small lean back first (anticipation)");
            Assert.AreEqual(1, HitFeel.LungeShape(W, W), 1e-9, "peak on the damage tick");
            Assert.AreEqual(0, HitFeel.LungeShape(W + HitFeel.LungeReturn, W), 1e-9, "back to the sim position");
            Assert.AreEqual(0, HitFeel.LungeShape(-0.1, W), 1e-9);
            Assert.AreEqual(0, HitFeel.LungeShape(HitFeel.LungeReturn, 0), 1e-9, "classic rules: follow-through only");

            World w = Swinging(ClassId.Builder, 6, 6, 1.4, 0, out _);
            Vector3 l = HitFeel.Lunge(w, 0, 0);
            Assert.Greater(l.x, 0f, "toward the target (+x)");
            Assert.LessOrEqual(l.magnitude, (float)HitFeel.LungeMax + 1e-5f);
            World close = Swinging(ClassId.Builder, 6, 6, 0.9, 0, out _);
            Assert.AreEqual(0f, HitFeel.Lunge(close, 0, 0).magnitude, 1e-5f, "bodies already touching: no step into the target");
        }

        // ---------------------------------------------------------------- reaction

        [Test]
        public void TheVictimReactsAwayFromTheBlow()
        {
            // Victim faces +z (Unity). A blow travelling +x came from its left (-x): the chest twists right (negative).
            Assert.AreEqual(HitSide.Left, HitReactions.Side(0, 1, 1, 0));
            Assert.Less(HitReactions.Pick(10, false, false, HitSide.Left).TwistDeg, 0f);
            Assert.Greater(HitReactions.Pick(10, false, false, HitSide.Right).TwistDeg, 0f);
            Assert.AreEqual(HitSide.Front, HitReactions.Side(0, 1, 0, -1), "a blow travelling against the facing is frontal");
            Assert.AreEqual(ArtMapping.Hit, HitReactions.Pick(10, false, false, HitSide.Front).Clip, "light: the flinch");
            Assert.AreEqual(ArtMapping.HitHeavy, HitReactions.Pick(25, false, false, HitSide.Front).Clip, "big hit: the stagger");
            Assert.AreEqual(ReactionLayer.FullBody, HitReactions.Pick(60, true, false, HitSide.Front).Layer, "knock-out: full body");
        }

        // ---------------------------------------------------------------- hit-stop and camera

        [Test]
        public void HitStopIsFortyToSeventyMillisecondsAndOffUnderReduceMotion()
        {
            for (byte t = 0; t <= 2; t++)
            {
                double s = HitFeel.HoldWallSeconds(t, false);
                Assert.GreaterOrEqual(s, 0.04);
                Assert.LessOrEqual(s, 0.07);
                Assert.AreEqual(0, HitFeel.HoldWallSeconds(t, true));
            }
            Assert.Greater(HitFeel.Nudge(true, false), 0f);
            Assert.Greater(HitFeel.Nudge(false, true), 0f);
            Assert.AreEqual(0f, HitFeel.Nudge(false, false), "nobody else's hit moves the camera");
        }

        // ---------------------------------------------------------------- voice

        [Test]
        public void PainAndEffortVoicesExistAndAreCapped()
        {
            int pain = SoundTable.IndexOf("pain"), effort = SoundTable.IndexOf("effort");
            Assert.GreaterOrEqual(pain, 0);
            Assert.GreaterOrEqual(effort, 0);
            Assert.LessOrEqual(SoundTable.Cues[pain].MaxVoices, 3, "a brawl is a few voices, not a wall");
            Assert.LessOrEqual(SoundTable.Cues[effort].MaxVoices, 2);
            Assert.Greater(SoundTable.Cues[pain].PitchJitter, 0f, "each grunt a little different");
            Assert.AreEqual(3, SoundTable.Cues[pain].Synth.Length, "three pain shapes");
            foreach (string s in new[] { "pain_a", "pain_b", "pain_c", "effort_a", "effort_b" })
                Assert.IsNotNull(SynthRecipes.Find(s), s + " is synthesised in code");
            int on = 0;
            for (int t = 0; t < 100; t++) if (SoundFeed.EffortOn(3, t)) on++;
            Assert.AreEqual(50, on, "about every other swing");
        }

        [Test]
        public void TheCaptureDummyStandsInsideTheReach()
        {
            GameConfig c = GameConfig.CreateDefault();
            foreach (ClassId cls in All)
            {
                double r = LightWeaponView.Range(c, cls), d = HitFeelCapture.DummyAhead(r);
                Assert.Less(d, r + 0.3, cls + ": the swing can land");
            }
        }
            // ---------------------------------------------------------------- feat/hit-feel-2

        [Test]
        public void TheWindupSnapsToItsExtremeHoldsThenStrikesOnTheTick()
        {
            const double hz = 30;
            foreach (ClassId cls in All)
                for (int windup = 2; windup <= 8; windup++)
                {
                    string clip = ArtMapping.ClassClips(cls).Attack ?? ArtMapping.Attack;
                    double c = ClipFacts.Contact(clip, 0.567f), w = windup / hz, e = AttackTiming.Extreme(c);
                    double snap = Math.Min(AttackTiming.AnticipationSeconds, w * 0.5), strike = Math.Min(AttackTiming.StrikeSeconds, w * 0.4);
                    Assert.LessOrEqual(snap, 2.0 / 30 + 1e-9, "anticipation in 2 frames at most");
                    Assert.AreEqual(e, AttackTiming.CraftTime(snap, c, w), 1e-9, $"{cls} W={windup}: at the wind-up extreme");
                    Assert.AreEqual(e, AttackTiming.CraftTime(w - strike, c, w), 1e-9, $"{cls}: extreme held until the strike");
                    Assert.AreEqual(c, AttackTiming.ClipTime(w, c, 2, w), 1e-9, $"{cls}: contact on the damage tick");
                    Assert.AreEqual(c + 0.1, AttackTiming.ClipTime(w + 0.1, c, 2, w), 1e-9, $"{cls}: follow-through at normal speed");
                    Assert.AreEqual(1f, AttackTiming.Envelope(snap, AttackTiming.CraftTime(snap, c, w), 2, w), 1e-6f, "the pose is fully in for the extreme");
                }
            Assert.AreEqual(4.0 / 30 - AttackTiming.StrikeSeconds, HitFeel.SmearDelay(4.0 / 30), 1e-9, "the smear starts with the strike");
            Assert.AreEqual(0, HitFeel.SmearDelay(0.01), 1e-9);
        }

        [Test]
        public void TheStepInClosesTheReachGapButNeverEntersTheBody()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            foreach (ClassId cls in All)
            {
                double range = LightWeaponView.Range(cfg, cls), reach = HitFeel.ViewReach(cls), rA = 0.45, rT = 0.45;
                for (double d = rA + rT + 0.05; d <= range + rT; d += 0.05)
                {
                    double step = HitFeel.StepPeak(d, reach, rA, rT);
                    Assert.LessOrEqual(step, d - rA - rT - HitFeel.BodyGap + 1e-9, $"{cls} at {d:0.00}: never into the body");
                    Assert.LessOrEqual(step, HitFeel.ReachStepMax + 1e-9);
                    if (d - rT - reach <= HitFeel.ReachStepMax - 0.05 && d - rA - rT - HitFeel.BodyGap >= d - rT - reach + 0.05)
                        Assert.GreaterOrEqual(reach + step, d - rT - 1e-9, $"{cls} at {d:0.00}: the weapon reaches the body");
                }
            }
        }

        [Test]
        public void FlinchesAreBigEnoughToSeeAndScaleWithDamage()
        {
            Assert.AreEqual(8, HitFeel.FlinchLeanDeg(0), 1e-9);
            Assert.AreEqual(24, HitFeel.FlinchLeanDeg(30), 1e-9);
            Assert.Greater(HitFeel.FlinchLeanDeg(16), HitFeel.FlinchLeanDeg(8), "more damage, more lean");
            Assert.GreaterOrEqual(HitFeel.FlinchLeanDeg(8), 12, "even the lantern's 8 leans 12 degrees");
            Assert.Greater(HitFeel.FlinchShift(22), HitFeel.FlinchShift(9));
            Assert.AreEqual(0, HitFeel.StaggerStep(0.1, 9), 1e-9, "a light tap does not stagger");
            Assert.Greater(HitFeel.StaggerStep(0.1, 16), 0.15, "a mid hit staggers a visible step");
            Assert.Greater(HitFeel.StaggerStep(0.1, 30), HitFeel.StaggerStep(0.1, 16));
            Assert.AreEqual(0, HitFeel.StaggerStep(0.45, 30), 1e-9, "absorbed by 0.45 s");
        }

        [Test]
        public void TheLocalHitNudgeIsOnByDefaultAndRespectsTheSettingAndReduceMotion()
        {
            Assert.AreEqual(CriticalMass.Runtime.Camera.CameraShake.DefaultNudgeScale, CriticalMass.Runtime.Camera.CameraShake.NudgeScale(false, false, 0f), "fresh install: on, low");
            Assert.AreEqual(0f, CriticalMass.Runtime.Camera.CameraShake.NudgeScale(false, true, 0f), "the player turned shake off: off");
            Assert.AreEqual(0.5f, CriticalMass.Runtime.Camera.CameraShake.NudgeScale(false, true, 0.5f));
            Assert.AreEqual(0f, CriticalMass.Runtime.Camera.CameraShake.NudgeScale(true, false, 1f), "reduce motion: off");
            float t = HitFeel.NudgeOnLocal * CriticalMass.Runtime.Camera.CameraShake.DefaultNudgeScale;
            Assert.Less(t * t * CriticalMass.Runtime.Camera.CameraShake.MaxOffset, 0.05f, "low: a few centimetres");
        }
    }
}

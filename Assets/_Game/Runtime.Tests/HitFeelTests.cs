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
            World w = Swinging(ClassId.Warrior, 4, 2, 1.0, 0.3, out int e);
            Assert.AreEqual(e, HitFeel.SwingTarget(w, 0));
            // px lane C (2026-11-22): the body faces the sim's LIVE SwingFacing, never a view-chosen target (a turn toward
            // an enemy the swing then misses was the lie the owner saw).
            Assert.AreEqual(0, HitFeel.Facing(w, 0, 0), 1e-9, "faces the live swing direction");
            w.Actors[0].SwingFacing = 0.25;
            Assert.AreEqual(0.25, HitFeel.Facing(w, 0, 0), 1e-9, "and follows it when the sim tracks the aim");

            World behind = Swinging(ClassId.Warrior, 4, 2, -1.0, 0, out _);
            Assert.AreEqual(-1, HitFeel.SwingTarget(behind, 0), "an enemy behind is not the target");
            Assert.AreEqual(0, HitFeel.Facing(behind, 0, 0), 1e-9, "falls back to the sim's swing facing");

            World late = Swinging(ClassId.Warrior, 4, 4 + 30, 1.0, 0.4, out _);
            late.Actors[0].Facing = 1.0;
            Assert.AreEqual(1.0, HitFeel.Facing(late, 0, 0), 1e-9, "after the follow-through the sim's facing rules again");
        }

        /// <summary>fix/hit-feel-3: the swing turn never fights the movement facing (FacingSanityPlayTests allows 30 degrees).</summary>
        [Test]
        public void TheSwingTurnStaysInsideAConeOfTheSimFacing()
        {
            Assert.LessOrEqual(HitFeel.FaceConeDeg, 25, "inside the facing test's 30 degrees with room for the leg yaw");
            World wide = Swinging(ClassId.Warrior, 4, 2, 1.0, 0.7, out int e);   // 35 degrees off the sim facing
            Assert.AreEqual(e, HitFeel.SwingTarget(wide, 0), "still the swing target (inside the arc)");
            Assert.AreEqual(0, HitFeel.Facing(wide, 0, 0), 1e-9, "outside the cone: keep the sim facing");

            World moving = Swinging(ClassId.Warrior, 4, 2, 1.0, 0.0, out _);
            moving.Actors[0].Facing = Math.PI;   // moving away from the swing (backing off): the movement facing rules
            Assert.AreEqual(Math.PI, HitFeel.Facing(moving, 0, 0), 1e-9, "never turned around against the sim facing");
            for (double ang = -Math.PI; ang <= Math.PI; ang += 0.1)
            {
                World s = Swinging(ClassId.Demolitions, 4, 2, Math.Cos(ang) * 1.0, Math.Sin(ang) * 1.0, out _);
                s.Actors[0].Facing = 0.2;
                double d = HitFeel.Facing(s, 0, 0) - 0.2;
                d = Math.Atan2(Math.Sin(d), Math.Cos(d));
                Assert.LessOrEqual(Math.Abs(d) * 180 / Math.PI, HitFeel.FaceConeDeg + 1e-6, $"target at {ang:0.0} rad");
            }
        }

        /// <summary>fix/hit-feel-3: step-in, flinch and stagger never draw the body more than MaxViewOffset off its sim spot.</summary>
        [Test]
        public void TheDrawnBodyStaysNearItsSimPosition()
        {
            Assert.LessOrEqual(HitFeel.MaxViewOffset, 0.25f + 1e-6f, "the placement test allows 0.35 u; the ring sits on the sim spot");
            Vector3 big = HitFeel.ClampOffset(new Vector3(0.4f, 0.3f, -0.3f));
            Assert.AreEqual(HitFeel.MaxViewOffset, new Vector2(big.x, big.z).magnitude, 1e-5f, "clamped horizontally");
            Assert.AreEqual(0.3f, big.y, 1e-6f, "vertical untouched");
            Vector3 small = new Vector3(0.1f, 0, 0.05f);
            Assert.AreEqual(small, HitFeel.ClampOffset(small), "a small offset is kept");
            Assert.LessOrEqual(HitFeel.ReachStepMax, HitFeel.MaxViewOffset + 1e-9);
            for (double dmg = 0; dmg <= 60; dmg += 2)
                for (double t = 0; t < 0.6; t += 0.02)
                    Assert.LessOrEqual(HitFeel.FlinchShift(dmg) + HitFeel.StaggerStep(t, dmg), 0.32, $"dmg {dmg} at {t:0.00} s (before the clamp)");
        }

        // ---------------------------------------------------------------- fix/hit-feel-5

        [Test]
        public void TheStrikeLeanClosesTheGapUpTo18Degrees()
        {
            Assert.AreEqual(0, HitPose.StrikeLeanDeg(0, 0.5), 1e-9, "no gap, no lean");
            Assert.AreEqual(0, HitPose.StrikeLeanDeg(-0.2, 0.5), 1e-9, "tip already in the body");
            double deg = HitPose.StrikeLeanDeg(0.1, 0.5);
            Assert.AreEqual(0.1, HitPose.LeanGain(deg, 0.5), 1e-9, "the lean covers exactly the gap");
            Assert.AreEqual(HitPose.MaxStrikeLeanDeg, HitPose.StrikeLeanDeg(5, 0.5), 1e-9, "capped");
            Assert.LessOrEqual(HitPose.MaxStrikeLeanDeg, 25, "below the upright limit even if it were on the root (it is not)");
        }

        [Test]
        public void TheMeasuredTipReachReplacesTheEstimate()
        {
            try
            {
                Assert.AreEqual(HitFeel.EstimatedReach(ClassId.Warrior), HitFeel.ViewReach(ClassId.Warrior), 1e-9);
                HitFeel.SetMeasured(ClassId.Warrior, 1.42, 0.6);
                Assert.AreEqual(1.42, HitFeel.ViewReach(ClassId.Warrior), 1e-9, "the rig's own number wins");
                HitFeel.SetMeasured(ClassId.Miner, 0.01, 0.6);
                Assert.AreEqual(HitFeel.EstimatedReach(ClassId.Miner), HitFeel.ViewReach(ClassId.Miner), 1e-9, "a failed measure is ignored");
            }
            finally { Array.Clear(HitFeel.MeasuredReach, 0, HitFeel.MeasuredReach.Length); Array.Clear(HitFeel.MeasuredLever, 0, HitFeel.MeasuredLever.Length); }
        }

        [Test]
        public void TheReactionReadsOnTheUpperBodyForEveryTier()
        {
            foreach (HitTier t in new[] { HitTier.Light, HitTier.Heavy, HitTier.Critical })
            {
                Assert.GreaterOrEqual(HitPose.RecoilDeg(t), 12f, t + ": a visible recoil even for the lightest hit");
                Assert.LessOrEqual(HitPose.RecoilDeg(t) + HitPose.HeadSnapDeg(t), 55f, t + ": the head stays well above the hips");
            }
            Assert.Greater(HitPose.RecoilDeg(HitTier.Critical), HitPose.RecoilDeg(HitTier.Light));
            Assert.AreEqual(0f, HitPose.RecoilEnv(-0.01));
            Assert.AreEqual(1f, HitPose.RecoilEnv(HitPose.RecoilOut), 1e-5f, "out in 50 ms");
            Assert.AreEqual(0f, HitPose.RecoilEnv(HitPose.RecoilBack), "back by 0.3 s");
            Assert.Greater(HitPose.Squash(0.04), 0.06f);
            Assert.LessOrEqual(HitPose.Squash(0.04), (float)HitPose.SquashMax + 1e-6f, "inside the 0.7 to 1.4 height test");
            Assert.AreEqual(0f, HitPose.Squash(HitPose.SquashSeconds));
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
        public void HitStopIsSixtyToOneHundredTenMillisecondsAndOffUnderReduceMotion()
        {
            for (byte t = 0; t <= 2; t++)
            {
                double s = HitFeel.HoldWallSeconds(t, false);
                Assert.GreaterOrEqual(s, 0.06);   // px lane C: 60 / 80 / 110 ms by tier
                Assert.LessOrEqual(s, 0.11);
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

        /// <summary>
        /// fix/hit-feel-3: ease into the extreme, hold it, and play the WHOLE strike (extreme to contact) in the last sim tick
        /// before the damage tick, so the hand is fastest on that tick (hit-feel-2's 2-frame snap peaked it 4 to 5 ticks early).
        /// </summary>
        [Test]
        public void TheWindupHoldsItsExtremeThenStrikesOverFiveFrames()
        {
            const double hz = 30;
            foreach (ClassId cls in All)
                for (int windup = 3; windup <= 8; windup++)
                    foreach (double measured in new[] { -1.0, 0.05 })
                    {
                        string clip = ArtMapping.ClassClips(cls).Attack ?? ArtMapping.Attack;
                        double c = ClipFacts.Contact(clip, 0.567f), w = windup / hz;
                        double e = measured >= 0 && measured <= c ? measured : AttackTiming.Extreme(c);
                        // px lane C: the strike is 5 frames of 60 fps (at most 0.4 W), not one sim tick (the "half motion")
                        Assert.AreEqual(5.0 / 60, AttackTiming.StrikeSeconds, 1e-12, "the strike is five frames");
                        double strike = AttackTiming.StrikeFor(w);
                        double holdEnd = w - strike, ease = Math.Min(AttackTiming.AnticipationSeconds, holdEnd);
                        Assert.AreEqual(e, AttackTiming.CraftTime(ease, c, w, measured), 1e-9, $"{cls} W={windup}: at the extreme");
                        Assert.AreEqual(e, AttackTiming.CraftTime(holdEnd, c, w, measured), 1e-9, $"{cls}: extreme held until the strike");
                        Assert.AreEqual(c, AttackTiming.ClipTime(w, c, 2, w, measured), 1e-9, $"{cls}: contact on the damage tick");
                        Assert.AreEqual(c + 0.1, AttackTiming.ClipTime(w + 0.1, c, 2, w, measured), 1e-9, $"{cls}: follow-through at normal speed");
                        // per-tick clip advance: the last wind-up tick (the strike) must move at least as much clip as any
                        // earlier tick's share of the ease, so the hand's fastest tick is the damage tick.
                        double lastTick = c - AttackTiming.ClipTime(w - 1 / hz, c, 2, w, measured);
                        double firstTick = AttackTiming.ClipTime(holdEnd + 1 / hz, c, 2, w, measured) - e;
                        Assert.Greater(lastTick, firstTick, $"{cls}: ease-in, the hand fastest on the damage tick");
                        Assert.AreEqual(0f, AttackTiming.Envelope(0, 0, 2, w), 1e-6f, "fades in from nothing");
                        Assert.Greater(AttackTiming.Envelope(holdEnd, e, 2, w), 0.7f, "mostly in for the strike");
                    }
            Assert.AreEqual(4.0 / 30 - AttackTiming.StrikeFor(4.0 / 30), HitFeel.SmearDelay(4.0 / 30), 1e-9, "the smear starts with the strike");
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
            Assert.AreEqual(6, HitFeel.FlinchLeanDeg(0), 1e-9);
            // fix/hit-feel-3: at most 13 degrees (hit-feel-1's value): 24 tipped the body's bounds to 1.5x its height.
            Assert.AreEqual(13, HitFeel.FlinchLeanDeg(30), 1e-9);
            Assert.LessOrEqual(HitFeel.FlinchLeanDeg(100), 13 + 1e-9);
            Assert.Greater(HitFeel.FlinchLeanDeg(16), HitFeel.FlinchLeanDeg(8), "more damage, more lean");
            Assert.Greater(HitFeel.FlinchShift(22), HitFeel.FlinchShift(9));
            Assert.AreEqual(0, HitFeel.StaggerStep(0.1, 9), 1e-9, "a light tap does not stagger");
            Assert.Greater(HitFeel.StaggerStep(0.1, 16), 0.1, "a mid hit staggers a visible step");
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

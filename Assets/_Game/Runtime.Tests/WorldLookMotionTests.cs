using System;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 world look, motion (item 4): per-class stride from each class's own measured clips, the facing spring's settle
    /// time, the exact swing direction on contact, and the match-end interpolation hold. Pure, no scene.
    /// </summary>
    public sealed class WorldLookMotionTests
    {
        static readonly ClassId[] All = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        [Test]
        public void EveryClassStridesOnItsOwnMeasuredClips()
        {
            var cfg = new GameConfig();
            foreach (ClassId c in All)
            {
                ModelSpec spec = ArtMapping.Spec(ArtMapping.ClassModel(c));
                Assert.IsNotNull(spec, c + " has a model");
                string src = ClassStrides.SourceOf(spec.ClipSource);
                Assert.IsTrue(ClassStrides.Covers(src), $"{c}: {src} has measured Run and Walk rows");
                foreach (string clip in new[] { ArtMapping.Run, ArtMapping.StrafeL, ArtMapping.StrafeR, ArtMapping.Walk, ArtMapping.WalkBack })
                {
                    Assert.IsTrue(ClassStrides.TryGet(src, clip, out ClassStrides.Row r), $"{c} {clip} measured");
                    Assert.Greater(r.GroundSpeed, 0.3f);
                    Assert.That(r.LeftPlant, Is.InRange(0f, 1f));
                    Assert.AreEqual(r.GroundSpeed, ActorAnimator.StrideSpeed(src, true, clip), 1e-6, "the animator strides on the measured row");
                    Assert.AreEqual(r.LeftPlant, ActorAnimator.Plant(src, clip), 1e-6, "and syncs on its plant");
                }
                // At the class's own run speed the run clip plays inside the rate clamp, so the planted foot holds
                // (outside it the clamp lets the foot slide).
                ClassStrides.TryGet(src, ArtMapping.Run, out ClassStrides.Row run);
                double speed = cfg.Class(c).MoveSpeed, rate = speed / run.GroundSpeed;
                double knight = ClipFacts.GroundSpeed(ArtMapping.Run, 4.57f) * spec.StrideScale;
                Debug.Log($"[b15 stride] {c}: run {speed:0.00} u/s, clip ground speed {run.GroundSpeed:0.000} (Knight x StrideScale was {knight:0.000}, " +
                          $"{(knight - run.GroundSpeed) / run.GroundSpeed:+0%;-0%} slide), playback rate {rate:0.00}, toe lift {run.Lift:0.000} u");
                Assert.That(rate, Is.InRange(Locomotion.MinRate, Locomotion.MaxRate), $"{c} run rate inside the clamp");
                // One distance of the class's stride advances the phase by exactly one cycle (no slide).
                double stride = run.GroundSpeed * run.Length;
                double p = Locomotion.Advance(0.0, stride * 0.25, stride, run.Length, 0.25 * run.Length / Math.Max(rate, 1e-6) );
                Assert.AreEqual(0.25, p, 1e-6, $"{c}: a quarter stride is a quarter cycle");
            }
        }

        [Test]
        public void TheFallbackStillScalesTheKnight()
        {
            Assert.IsFalse(ClassStrides.Covers("KayKit_Clips.fbx"));
            Assert.AreEqual(ClipFacts.GroundSpeed(ArtMapping.Run, 4.57f), ActorAnimator.StrideSpeed("KayKit_Clips.fbx", false, ArtMapping.Run), 1e-6);
        }

        static double Err(double a, double b)
        {
            double d = (a - b) % 360.0;
            if (d > 180) d -= 360;
            else if (d < -180) d += 360;
            return Math.Abs(d);
        }

        [Test]
        public void TheFacingSettlesInAFifthOfTheOldTimeAndWithin012Seconds()
        {
            foreach (double fps in new[] { 60.0, 120.0, 144.0 })
                foreach (double turn in new[] { 30.0, 90.0, 180.0 })
                {
                    double yaw = 0, vel = 0, t = 0, dt = 1.0 / fps, settled = -1;
                    while (t < 1.0)
                    {
                        ActorAnimator.StepFacing(ref yaw, ref vel, turn, dt);
                        t += dt;
                        if (Err(yaw, turn) < 2.0 && Math.Abs(vel) < 60.0) { settled = t; break; }
                    }
                    Debug.Log($"[b15 facing] {fps:0} fps, {turn:0} degree turn: settled in {settled:0.000} s (limit 0.12)");
                    Assert.That(settled, Is.InRange(0.0, 0.12 + 1e-9), $"{turn} degrees at {fps} fps");
                }
        }

        [Test]
        public void TheSwingSnapsToItsTargetOnceAtContact()
        {
            double snapped = double.NaN, start = 10.0, windup = 0.12, dt = 1.0 / 60.0;
            int hits = 0;
            for (double age = 0; age < 0.5; age += dt)
                if (ActorAnimator.ContactSnap(age, windup, dt, ref snapped, start)) { hits++; Assert.GreaterOrEqual(age + 1e-9, windup); Assert.Less(age, windup + 2 * dt); }
            Assert.AreEqual(1, hits, "exactly one snap per swing, on the contact frame");
            Assert.IsTrue(ActorAnimator.ContactSnap(windup, windup, dt, ref snapped, 11.0), "a new swing snaps again");
        }

        [Test]
        public void InterpolationHoldsStillOnceTheMatchHasEnded()
        {
            Assert.AreEqual(0.37, ViewAlpha.Hold(0.37, false), 1e-12, "playing: the driver's alpha");
            Assert.AreEqual(1.0, ViewAlpha.Hold(0.37, true), 1e-12, "ended: the last state, every frame (no cycling between the last two ticks)");
            Assert.AreEqual(1.0, ViewAlpha.Hold(0.0, true), 1e-12);
        }
    }
}

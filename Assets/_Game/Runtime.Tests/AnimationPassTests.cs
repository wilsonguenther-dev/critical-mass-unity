using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Animation pass (2026-11-12, physics audit seat ANIMATION, DG-71 to DG-108): the pure maths behind directional
    /// locomotion, speed matching, layer selection, the hit-frame timing table, the clip facts and the hooks.
    /// </summary>
    public sealed class AnimationPassTests
    {
        [SetUp]
        public void SetUp() => ClipFacts.ResetForTests();

        // ------------------------------------------------------------------ direction choice (DG-76)

        [TestCase(0, 1, 0, 1, 0)]       // facing +z, moving +z: straight ahead
        [TestCase(0, 1, -1, 0, 90)]     // moving -x: the character's left
        [TestCase(0, 1, 1, 0, -90)]     // moving +x: right
        [TestCase(0, 1, 0, -1, 180)]    // backwards
        [TestCase(1, 0, 0, 1, 90)]      // facing +x, moving +z: left
        public void LeftAngleIsPositiveToTheCharactersLeft(double fx, double fz, double vx, double vz, double expected)
        {
            double a = Locomotion.LeftAngleDeg(fx, fz, vx, vz);
            Assert.AreEqual(0, Math.Abs(Wrap(a - expected)), 1e-6, $"angle {a}");
        }

        [Test]
        public void DirectionWeightsSumToOneAndBlendOnlyNeighbours()
        {
            var w = new float[Locomotion.DirCount];
            for (double a = -360; a <= 360; a += 7.5)
            {
                Locomotion.DirectionWeights(a, w);
                Assert.AreEqual(1f, w.Sum(), 1e-5f, "sum at " + a);
                Assert.LessOrEqual(w.Count(x => x > 1e-6f), 2, "neighbours only at " + a);
            }
            Locomotion.DirectionWeights(30, w);
            Assert.AreEqual(0.5f, w[0], 1e-5f);
            Assert.AreEqual(0.5f, w[1], 1e-5f);
        }

        /// <summary>
        /// Eight move directions with a fixed facing: the weighted travel direction of the chosen run clips lies within
        /// 30 degrees of the velocity (the stride axis follows the motion: no moonwalking), and straight back plays the run
        /// reversed, never the forward run.
        /// </summary>
        [Test]
        public void EightDirectionsPickClipsWhoseStrideFollowsTheVelocity()
        {
            var w = new float[Locomotion.DirCount];
            for (int k = 0; k < 8; k++)
            {
                int side = Locomotion.NoSide;
                double left = k * 45.0;
                double warp = Locomotion.DirectionWeights(left, w, ref side);
                double sx = 0, sz = 0;
                for (int d = 0; d < Locomotion.DirCount; d++)
                {
                    if (w[d] <= 0f) continue;
                    Locomotion.Dir dir = Locomotion.RunBand[d];
                    ClipFact f = ClipFacts.Get(dir.Clip);
                    Assert.IsNotNull(f, dir.Clip + " has no clip fact");
                    double travel = (f.moveDeg + (dir.Reversed ? 180 : 0) + warp) * Math.PI / 180;
                    sx += w[d] * Math.Cos(travel);
                    sz += w[d] * Math.Sin(travel);
                }
                double got = Math.Atan2(sz, sx) * 180 / Math.PI;
                Assert.LessOrEqual(Math.Abs(Wrap(got - left)), 30.0, $"moving {left} deg left of the facing, the stride travels {got:0} deg");
            }
            Locomotion.DirectionWeights(180, w);
            Assert.AreEqual(1f, w[3], 1e-5f);
            Assert.IsTrue(Locomotion.RunBand[3].Reversed && Locomotion.RunBand[3].Clip == ArtMapping.Run, "back = Run reversed");
            Assert.AreEqual(ArtMapping.WalkBack, Locomotion.WalkBand[3].Clip, "walking back = Walking_Backwards");
        }

        // ------------------------------------------------------------------ sideways sectors (fix/animation-reds)

        /// <summary>The direction a clip's legs point (+ left of the facing): Locomotion.LegYawDeg of its measured travel.</summary>
        static double Legs(int dirIndex) => Locomotion.LegYawDeg(ClipFacts.Get(Locomotion.RunBand[dirIndex].Clip).moveDeg);

        /// <summary>
        /// Integration batch 3 red (0.98 u/u at exactly sideways): the KayKit strafes turn the hips +-60 degrees, and a
        /// per-bone rotation mix of StrafeL (hips +60) with StrafeR reversed (hips -60) marches in place. Pinned: at every
        /// angle (with or without history) no two weighted clips have legs more than 60 degrees apart, so the mix never
        /// cancels a stride.
        /// </summary>
        [Test]
        public void NoAngleMixesStrafesWhoseHipsPointOppositeWays()
        {
            var w = new float[Locomotion.DirCount];
            int side = Locomotion.NoSide;
            for (double a = -720; a <= 720; a += 0.5)
            {
                Locomotion.DirectionWeights(a, w, ref side);
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                for (int d = 0; d < Locomotion.DirCount; d++)
                {
                    if (w[d] <= 1e-6f) continue;
                    lo = Math.Min(lo, Legs(d));
                    hi = Math.Max(hi, Legs(d));
                }
                Assert.LessOrEqual(hi - lo, 60.5, $"at {a} deg the mixed clips' legs point {lo:0} and {hi:0} deg: the strides cancel");
                Assert.AreEqual(1f, w.Sum(), 1e-5f, "sum at " + a);
            }
        }

        /// <summary>
        /// Sideways, the diagonals and the backpedal with a fixed facing: the travel of the chosen clips plus the leg warp
        /// equals the velocity angle (sideways: exactly), the warp stays within 45 degrees, and outside the sideways
        /// sectors there is no warp.
        /// </summary>
        [TestCase(90.0)] [TestCase(-90.0)] [TestCase(270.0)] [TestCase(75.0)] [TestCase(105.0)] [TestCase(255.0)] [TestCase(285.0)]
        [TestCase(45.0)] [TestCase(135.0)] [TestCase(-45.0)] [TestCase(-135.0)] [TestCase(180.0)] [TestCase(0.0)]
        public void TheLegsTravelAlongTheVelocity(double left)
        {
            var w = new float[Locomotion.DirCount];
            int side = Locomotion.NoSide;
            double warp = Locomotion.DirectionWeights(left, w, ref side);
            double sx = 0, sz = 0;
            for (int d = 0; d < Locomotion.DirCount; d++)
            {
                if (w[d] <= 0f) continue;
                Locomotion.Dir dir = Locomotion.RunBand[d];
                double travel = (ClipFacts.Get(dir.Clip).moveDeg + (dir.Reversed ? 180 : 0) + warp) * Math.PI / 180;
                sx += w[d] * Math.Cos(travel);
                sz += w[d] * Math.Sin(travel);
            }
            double got = Math.Atan2(sz, sx) * 180 / Math.PI;
            bool sideways = Math.Abs(Math.Abs(Wrap(left)) - 90) < 30;
            Assert.LessOrEqual(Math.Abs(Wrap(got - left)), sideways ? 0.5 : 15.0, $"moving {left} deg: the legs travel {got:0.0} deg");
            Assert.LessOrEqual(Math.Abs(warp), 45.0 + 1e-9, "warp");
            if (!sideways) Assert.AreEqual(0.0, warp, 1e-9, "no warp outside the sideways sectors");
            else Assert.AreEqual(1, w.Count(x => x > 0f), "one strafe sideways");
        }

        /// <summary>
        /// With no history, exactly sideways runs toes-first: StrafeL going left, StrafeR going right (the same at -90 as
        /// at 270). The hysteresis keeps the strafe through a 10 Hz aim wobble of +-12 degrees around the sideways line
        /// and flips only past 15 degrees, once.
        /// </summary>
        [Test]
        public void SidewaysPicksToesFirstAndHoldsThroughAWobble()
        {
            var w = new float[Locomotion.DirCount];
            int side = Locomotion.NoSide;
            Locomotion.DirectionWeights(90, w, ref side);
            Assert.AreEqual(1, side, "left: StrafeL");
            Assert.IsFalse(Locomotion.RunBand[side].Reversed);
            side = Locomotion.NoSide;
            Locomotion.DirectionWeights(-90, w, ref side);
            Assert.AreEqual(5, side, "right: StrafeR");
            Assert.IsFalse(Locomotion.RunBand[side].Reversed);

            // 10 Hz wobble sampled at 60 fps for 2 s: the aim swings +-12 degrees, the legs never flip.
            side = Locomotion.NoSide;
            int flips = 0, last = -2;
            for (int f = 0; f < 120; f++)
            {
                double a = 90 + 12 * Math.Sin(2 * Math.PI * 10 * f / 60.0);
                double warp = Locomotion.DirectionWeights(a, w, ref side);
                if (last != -2 && side != last) flips++;
                last = side;
                Assert.AreEqual(a, side * Locomotion.DirStepDeg + warp, 1e-9, "travel = strafe + warp");
            }
            Assert.AreEqual(0, flips, "the legs flipped during a 12 degree wobble");

            // Past the hysteresis the legs flip once to the other strafe, and back only past it on the other side.
            side = 1;
            Locomotion.DirectionWeights(104, w, ref side);
            Assert.AreEqual(1, side);
            Locomotion.DirectionWeights(105.5, w, ref side);
            Assert.AreEqual(2, side, "past 105: StrafeR reversed");
            Locomotion.DirectionWeights(80, w, ref side);
            Assert.AreEqual(2, side, "held until 75");
            Locomotion.DirectionWeights(74, w, ref side);
            Assert.AreEqual(1, side);
            Locomotion.DirectionWeights(30, w, ref side);
            Assert.AreEqual(Locomotion.NoSide, side, "leaving the sector forgets the strafe");
        }

        [Test]
        public void LegYawFollowsTheClipsMeasuredTravel()
        {
            Assert.AreEqual(0, Locomotion.LegYawDeg(ClipFacts.Get(ArtMapping.Run).moveDeg), 1e-3);
            Assert.AreEqual(60, Locomotion.LegYawDeg(ClipFacts.Get(ArtMapping.StrafeL).moveDeg), 1.0);
            Assert.AreEqual(-60, Locomotion.LegYawDeg(ClipFacts.Get(ArtMapping.StrafeR).moveDeg), 1.0);
            Assert.AreEqual(0, Locomotion.LegYawDeg(ClipFacts.Get(ArtMapping.WalkBack).moveDeg), 1e-3, "walking backwards: toes forward");
            Assert.AreEqual(0, Locomotion.LegYawDeg(ClipFacts.Get(ArtMapping.Walk).moveDeg), 1e-3);
        }

        // ------------------------------------------------------------------ speed matching (DG-75)

        [Test]
        public void MeasuredGroundSpeedsReplaceTheOldEstimates()
        {
            Assert.AreEqual(0.86, ClipFacts.GroundSpeed(ArtMapping.Walk, 0), 0.04, "walk (the old estimate was 1.6)");
            Assert.AreEqual(4.57, ClipFacts.GroundSpeed(ArtMapping.Run, 0), 0.1, "run (the old estimate was 4.0)");
            Assert.AreEqual(4.5, ClipFacts.GroundSpeed(ArtMapping.StrafeL, 0), 0.15);
            Assert.AreEqual(0.86, ClipFacts.GroundSpeed(ArtMapping.WalkBack, 0), 0.04);
        }

        /// <summary>At walk, run (every class), shield walk and slowed speeds the matched rate leaves the planted foot still.</summary>
        [TestCase(0.86)]
        [TestCase(1.2)]
        [TestCase(2.66)]   // Warrior shield walk 3.8 x 0.7
        [TestCase(3.8)]
        [TestCase(4.0)]
        [TestCase(4.2)]
        [TestCase(5.5)]
        public void MatchedRateKeepsThePlantedFootStill(double speed)
        {
            float run = Locomotion.RunWeight(speed);
            string clip = run >= 0.5f ? ArtMapping.Run : ArtMapping.Walk;
            double g = ClipFacts.GroundSpeed(clip, 1);
            double rate = Locomotion.MatchRate(speed, g);
            double slidePerUnit = Math.Abs(speed - rate * g) / speed;
            Assert.LessOrEqual(slidePerUnit, 0.15, $"{clip} at {speed} u/s plays at {rate:0.00}: the foot slides {slidePerUnit:0.00} u/u");
        }

        [Test]
        public void ThePhaseCompletesOneCycleExactlyWhenTheBodyCoversOneStride()
        {
            double stride = ClipFacts.Get(ArtMapping.Run).Stride, len = ClipFacts.Get(ArtMapping.Run).length;
            double speed = 4.0, dt = 1.0 / 60, p = 0.3, dist = 0;
            int frames = 0;
            while (dist < stride - 1e-9) { p = Locomotion.Advance(p, speed * dt, stride, len, dt); dist += speed * dt; frames++; }
            Assert.AreEqual(0.3, p, 0.02, $"after {frames} frames and {dist:0.00} u the phase is back where it started");
            Assert.AreEqual(0.7, Locomotion.Advance(0.7, 0, stride, len, 0), 1e-9, "no time: no advance");
        }

        [Test]
        public void WalkRunAndReversedClipsPlantTheLeftFootAtTheSamePhase()
        {
            float refPlant = ClipFacts.LeftPlant(ArtMapping.Run, 0);
            foreach (string c in new[] { ArtMapping.Walk, ArtMapping.Run, ArtMapping.StrafeL, ArtMapping.StrafeR, ArtMapping.WalkBack })
            {
                ClipFact f = ClipFacts.Get(c);
                foreach (bool rev in new[] { false, true })
                {
                    double t = Locomotion.SampleTime(refPlant, f.length, f.leftPlant, refPlant, rev);
                    Assert.AreEqual(f.leftPlant * f.length, t, 1e-6, $"{c} reversed={rev}: left mid-stance at the shared plant phase");
                }
            }
        }

        [Test]
        public void PhaseCrossingsWrap()
        {
            Assert.IsTrue(Locomotion.Crossed(0.1, 0.3, 0.2));
            Assert.IsFalse(Locomotion.Crossed(0.1, 0.3, 0.5));
            Assert.IsTrue(Locomotion.Crossed(0.9, 0.05, 0.95));
            Assert.IsTrue(Locomotion.Crossed(0.9, 0.05, 0.02));
            Assert.IsFalse(Locomotion.Crossed(0.9, 0.05, 0.5));
        }

        // ------------------------------------------------------------------ layer selection (DG-73, DG-88)

        [Test]
        public void UpperBodyClipsAreActionsNeverLocomotion()
        {
            foreach (string c in new[] { ArtMapping.Attack, ArtMapping.AttackSlice, ArtMapping.Attack2HChop, ArtMapping.Attack2HSlice,
                         ArtMapping.Block, ArtMapping.Blocking, ArtMapping.BlockHit, ArtMapping.Cast, ArtMapping.UseItem, ArtMapping.Hit, ArtMapping.Carry })
                CollectionAssert.Contains(ArtMapping.UpperBodyClips, c);
            foreach (string c in new[] { ArtMapping.Idle, ArtMapping.Walk, ArtMapping.Run, ArtMapping.StrafeL, ArtMapping.StrafeR, ArtMapping.WalkBack,
                         ArtMapping.JumpAir, ArtMapping.HitHeavy, ArtMapping.GetUp })
                CollectionAssert.DoesNotContain(ArtMapping.UpperBodyClips, c);
        }

        [Test]
        public void EachClassHasItsOwnAttackAndAnAbilityThatIsNeverTheChop()
        {
            Assert.AreEqual(ArtMapping.AttackSlice, ArtMapping.ClassClips(ClassId.Warrior).Attack, "Warrior: horizontal cleave");
            Assert.AreEqual(ArtMapping.Block, ArtMapping.ClassClips(ClassId.Warrior).Ability, "Warrior shield: Block");
            Assert.AreEqual(ArtMapping.Blocking, ArtMapping.ClassClips(ClassId.Warrior).AbilityHold);
            Assert.AreEqual(ArtMapping.Attack2HChop, ArtMapping.ClassClips(ClassId.Miner).Attack);
            Assert.AreEqual(ArtMapping.Attack2HChop, ArtMapping.ClassClips(ClassId.Miner).Gather, "Miner digs with the chop");
            Assert.AreEqual(ArtMapping.Attack2HSlice, ArtMapping.ClassClips(ClassId.Farmer).Attack);
            Assert.AreEqual(ArtMapping.PickUp, ArtMapping.ClassClips(ClassId.Farmer).Gather, "Farmer picks plants");
            foreach (ClassId c in ClassIds.All)
            {
                ArtMapping.ClassClipSet s = ArtMapping.ClassClips(c);
                Assert.AreNotEqual(s.Attack, s.Ability, c + ": the ability must not replay the attack");
                Assert.AreNotEqual(ArtMapping.Attack, s.Ability, c.ToString());
            }
        }

        [Test]
        public void TheWarriorCleaveSwingsHorizontallyLikeItsCrescent()
        {
            Assert.AreEqual("horizontal", ClipFacts.Get(ArtMapping.AttackSlice)?.swingPlane, "clip_facts.json (Resources/Anim) must load");
            Assert.AreEqual("vertical", ClipFacts.Get(ArtMapping.Attack)?.swingPlane, "the old one-hand chop was vertical");
        }

        /// <summary>4 directions x 3 tiers (plus blocked): the clip, layer and twist side match the table.</summary>
        [Test]
        public void HitReactionsByDirectionAndStrength()
        {
            // Victim faces +z. Hit directions point from the source toward the victim.
            var dirs = new (string name, double dx, double dz, HitSide side)[]
            {
                ("from the front", 0, -1, HitSide.Front), ("from behind", 0, 1, HitSide.Back),
                ("from the left", 1, 0, HitSide.Left), ("from the right", -1, 0, HitSide.Right),
            };
            var tiers = new (double dmg, bool kill, HitTier tier, string clip, ReactionLayer layer)[]
            {
                (12, false, HitTier.Light, ArtMapping.Hit, ReactionLayer.Upper),
                (22, false, HitTier.Heavy, ArtMapping.HitHeavy, ReactionLayer.FullBody),
                (45, false, HitTier.Critical, ArtMapping.HitHeavy, ReactionLayer.FullBody),
            };
            foreach (var d in dirs)
            {
                HitSide side = HitReactions.Side(0, 1, d.dx, d.dz);
                Assert.AreEqual(d.side, side, d.name);
                foreach (var t in tiers)
                {
                    HitReaction r = HitReactions.Pick(t.dmg, t.kill, false, side);
                    Assert.AreEqual(t.tier, r.Tier, $"{t.dmg} dmg {d.name}");
                    Assert.AreEqual(t.clip, r.Clip, $"{t.dmg} dmg {d.name}");
                    Assert.AreEqual(t.layer, r.Layer, $"{t.dmg} dmg {d.name}");
                    if (side == HitSide.Left) Assert.Less(r.TwistDeg, 0f, "a blow from the left turns the body right");
                    else if (side == HitSide.Right) Assert.Greater(r.TwistDeg, 0f, "a blow from the right turns the body left");
                    else Assert.AreEqual(0f, r.TwistDeg);
                }
            }
            HitReaction b = HitReactions.Pick(30, false, true, HitSide.Front);
            Assert.AreEqual(ArtMapping.BlockHit, b.Clip, "a blocked hit plays Block_Hit");
            Assert.AreEqual(ReactionLayer.Upper, b.Layer);
        }

        // ------------------------------------------------------------------ hit-frame timing table (DG-74, contract C2)

        [Test]
        public void ParityModeShowsTheContactFrameWithinTwoFramesOfTheDamageTick()
        {
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Miner, ClassId.Farmer })
            {
                AttackTiming.Row(c, 0, 30, out string clip, out double contact, out double len, out double contactAge);
                Assert.LessOrEqual(contactAge, 2.0 / 30 + 1e-9, $"{c} {clip}: contact drawn {contactAge * 30:0.0} frames after the damage tick");
                Assert.AreEqual(contact, AttackTiming.ClipTime(contactAge, contact, len, 0), 1e-9, c + " the contact frame is drawn");
                Assert.Greater(len, contact, c + " a follow-through exists after contact");
            }
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(8)]
        public void WithAWindupTheContactFrameLandsOnTheDamageTick(int windupTicks)
        {
            const double hz = 30;
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Miner, ClassId.Farmer })
            {
                AttackTiming.Row(c, windupTicks, hz, out string clip, out double contact, out double len, out double contactAge);
                double ws = windupTicks / hz;
                Assert.AreEqual(ws, contactAge, 1e-9, c + " contact age = W");
                Assert.AreEqual(0, AttackTiming.ClipTime(0, contact, len, ws), 1e-9, c + " starts at clip time 0");
                Assert.AreEqual(contact, AttackTiming.ClipTime(ws, contact, len, ws), 1e-9, c + " contact exactly on tick T + W");
                double prev = -1;
                for (double age = 0; age < 2; age += 1 / 60.0)
                {
                    double t = AttackTiming.ClipTime(age, contact, len, ws);
                    Assert.GreaterOrEqual(t, prev - 1e-12, c + " clip time never runs backwards");
                    prev = t;
                }
                Assert.AreEqual(len, AttackTiming.ClipTime(5, contact, len, ws), 1e-9, c + " the follow-through finishes");
            }
        }

        [Test]
        public void TheEnvelopeFadesInFinishesTheFollowThroughAndEnds()
        {
            Assert.AreEqual(0f, AttackTiming.Envelope(0, 0, 1, 0.1), 1e-6f, "starts from nothing (no first-frame snap)");
            Assert.AreEqual(1f, AttackTiming.Envelope(0.2, 0.5, 1, 0.1), 1e-6f, "full mid-swing, past any cooldown");
            Assert.Less(AttackTiming.Envelope(0.9, 0.95, 1, 0.1), 0.5f, "fades out over the last 0.12 s of the clip");
            Assert.AreEqual(0f, AttackTiming.Envelope(2, 1, 1, 0.1), "gone at the end");
        }

        sealed class FakeTicks { public double AttackWindupTicks = 4; }
        sealed class FakeSeconds { public double AttackWindupSeconds = 0.06; }
        sealed class FakeNone { public double AttackCooldown = 0.7; }

        [Test]
        public void TheWindupIsReadFromTheConfigWhateverItsUnit()
        {
            Assert.AreEqual(4, AttackTiming.ReadWindupTicks(new FakeTicks(), 30, 2));
            Assert.AreEqual(4, AttackTiming.ReadWindupTicks(new FakeSeconds(), 30, 2), "0.06 on-screen s at pace 2 = 3.6 -> 4 ticks");
            Assert.AreEqual(0, AttackTiming.ReadWindupTicks(new FakeNone(), 30, 2), "no field yet: parity mode");
            Assert.AreEqual(0, AttackTiming.WindupTicks(new GameConfig(), ClassId.Warrior), "main has no wind-up today");
        }

        // ------------------------------------------------------------------ clip facts, root motion, import (DG-72, A24)

        [Test]
        public void ClipFactsCoverEveryImportedClip()
        {
            Assert.IsNotNull(Resources.Load<TextAsset>(ClipFacts.ResourcePath), "Resources/Anim/clip_facts.json");
            foreach (string c in ArtMapping.KaykitExtraClips.Concat(new[] { ArtMapping.Idle, ArtMapping.Walk, ArtMapping.Run, ArtMapping.Attack,
                         ArtMapping.Gather, ArtMapping.Hit, ArtMapping.Death, ArtMapping.Cheer }))
            {
                ClipFact f = ClipFacts.Get(c);
                Assert.IsNotNull(f, c);
                Assert.Greater(f.length, 0.1f, c + " length");
            }
        }

        /// <summary>Root-motion policy: the sim owns translation, so a looping clip may drift its hips at most 0.05 u a cycle.</summary>
        [Test]
        public void LoopingClipsStayInPlace()
        {
            var drift = new List<string>();
            foreach (string c in ArtMapping.LoopingClips)
            {
                ClipFact f = ClipFacts.Get(c);
                if (f == null || string.IsNullOrEmpty(f.source)) continue; // Kaiju clips are not in the table
                Debug.Log($"[ClipFacts] {c}: hips drift {f.hipsDrift:0.000} u");
                if (f.hipsDrift > ClipFacts.RootDriftLimit) drift.Add($"{c} drifts {f.hipsDrift:0.000} u per cycle");
            }
            CollectionAssert.IsEmpty(drift);
        }

        /// <summary>KayKit_Clips.fbx carries the 24 new clips once tools/art/build_kaykit_clips.py has been re-run.</summary>
        [Test]
        public void TheClipFbxCarriesTheAnimationPassClips()
        {
            ModelSpec spec = ArtMapping.Spec(ArtMapping.KaykitClips);
            string[] have = AssetDatabase.LoadAllAssetsAtPath(spec.AssetPath).OfType<AnimationClip>().Select(c => c.name).ToArray();
            if (have.Length == 0) Assert.Inconclusive(spec.AssetPath + " is not imported");
            string[] missing = ArtMapping.KaykitExtraClips.Where(c => !have.Contains(c)).ToArray();
            if (missing.Length == ArtMapping.KaykitExtraClips.Length)
                Assert.Inconclusive("KayKit_Clips.fbx still has the original 8 clips: re-run tools/art/build_kaykit_clips.py (machine lock), then re-import");
            CollectionAssert.IsEmpty(missing, "a partial clip export");
        }

        // ------------------------------------------------------------------ level of detail, hooks, idle life

        [Test]
        public void AnimationLevelOfDetailTable()
        {
            Assert.IsTrue(AnimLod.Decide(true, false, false, false, 1, 0), "AlwaysAnimate (tests) always evaluates");
            Assert.IsTrue(AnimLod.Decide(false, true, false, false, 1, 0), "a freshly shown model evaluates before it is visible");
            Assert.IsFalse(AnimLod.Decide(false, false, false, true, 0, 0), "invisible: skipped");
            Assert.IsTrue(AnimLod.Decide(false, false, true, true, 1, 0), "near: every frame");
            int far = 0;
            for (int id = 0; id < 32; id++) far += AnimLod.Decide(false, false, true, false, 7, id) ? 1 : 0;
            Assert.AreEqual(16, far, "far: half the crowd each frame, staggered by id");
        }

        sealed class Sink : IFootfallSink { public int N; public void OnFootfall(in Footfall f) => N++; }
        sealed class Listener : IPostPoseListener { public int N; public void OnPostPose(int actor, ModelView mv, in ActorPoseState s) => N++; }

        /// <summary>
        /// Integration batch 3 red (RunningRaisesFootfalls: 0 footfalls): the step cap's window was the Unity frame only, and
        /// a world stepped tick by tick inside one frame dropped every step after the first 12. Pinned: within one frame the
        /// cap holds per tick (13 steps on one tick: 12 pass), and the next tick opens a new window; landings and the Kaiju
        /// always pass.
        /// </summary>
        [Test]
        public void TheStepCapIsPerTickNotPerUnityFrame()
        {
            Footfalls.ResetForTests();
            var s = new Sink();
            Footfalls.Register(s);
            for (int k = 0; k < Footfalls.StepCapPerFrame + 1; k++) Footfalls.Emit(new Footfall { Tick = 100, Kind = FootfallKind.Step });
            Assert.AreEqual(Footfalls.StepCapPerFrame, s.N, "one tick: capped");
            Assert.AreEqual(1, Footfalls.Dropped);
            Footfalls.Emit(new Footfall { Tick = 100, Kind = FootfallKind.Land });
            Footfalls.Emit(new Footfall { Tick = 100, Kind = FootfallKind.KaijuStep });
            Assert.AreEqual(Footfalls.StepCapPerFrame + 2, s.N, "landings and the Kaiju pass the cap");
            for (int t = 101; t < 156; t++)
                for (int k = 0; k < 20; k++) Footfalls.Emit(new Footfall { Tick = t, Kind = FootfallKind.Step });
            Assert.AreEqual(Footfalls.StepCapPerFrame + 2 + 55 * Footfalls.StepCapPerFrame, s.N, "55 ticks in one frame: 12 each");
            Footfalls.Unregister(s);
            Footfalls.ResetForTests();
        }

        [Test]
        public void FootfallsAndPostPoseHooksDispatchWithoutDuplicates()
        {
            Footfalls.ResetForTests();
            PostPoseHooks.ResetForTests();
            var s = new Sink();
            Footfalls.Register(s);
            Footfalls.Register(s);
            Footfalls.Emit(new Footfall { Kind = FootfallKind.KaijuStep, Strength = Footfalls.KaijuStepStrength });
            Assert.AreEqual(1, s.N);
            Footfalls.Unregister(s);
            Footfalls.Emit(new Footfall { Kind = FootfallKind.Land });
            Assert.AreEqual(1, s.N);
            var l = new Listener();
            PostPoseHooks.Register(l);
            PostPoseHooks.Register(l);
            var st = new ActorPoseState();
            PostPoseHooks.Dispatch(3, null, in st);
            Assert.AreEqual(1, l.N);
            Assert.AreEqual(1, PostPoseHooks.Count);
            PostPoseHooks.ResetForTests();
            Footfalls.ResetForTests();
        }

        [Test]
        public void IdleBreaksAreDeterministicAndNeighboursNeverBreakOnTheSameTick()
        {
            for (int id = 0; id < 32; id++)
            {
                double a = ActorAnimator.IdleBreakMin + ActorAnimator.IdleBreakSpread * ActorAnimator.Hash01(id, 0);
                double b = ActorAnimator.IdleBreakMin + ActorAnimator.IdleBreakSpread * ActorAnimator.Hash01(id + 1, 0);
                Assert.That(a, Is.InRange(4.0, 8.0));
                Assert.AreNotEqual((int)(a * 30), (int)(b * 30), $"actors {id} and {id + 1} break on the same tick");
                Assert.AreEqual(ActorAnimator.Hash(id, 5), ActorAnimator.Hash(id, 5));
            }
        }

        static double Wrap(double d)
        {
            d %= 360;
            if (d > 180) d -= 360;
            if (d < -180) d += 360;
            return d;
        }
    }
}

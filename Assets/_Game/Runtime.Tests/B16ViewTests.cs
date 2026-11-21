using System;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.B16;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b16 physics fixes, view half (physics audit 2026-11-21): the facing spring's timing [10], the refused-jump cue [11],
    /// speed-tied footstep dust and the death fling's fallback direction (audit missing list). Each case prints one evidence
    /// line. The ladder foot [14] is LadderClimbTests; the Kaiju x-ray rings [9] are judged by eye (UnderRings).
    /// </summary>
    [TestFixture]
    public sealed class B16ViewTests
    {
        /// <summary>[10] The drawn fighter yaw settles a 90 degree sim turn to within 10% in 60 to 90 ms; the swing snaps exact on contact.</summary>
        [Test]
        public void FighterFacingSpringSettlesIn60To90MsAndTheSwingContactIsExact()
        {
            double yaw = 0, vel = 0, dt = 1.0 / 240, t = 0;
            double settle = -1;
            for (int k = 0; k < 240 && settle < 0; k++)
            {
                ActorAnimator.StepFacing(ref yaw, ref vel, 90, dt);
                t += dt;
                if (Math.Abs(90 - yaw) <= 9) settle = t;
            }
            Debug.Log($"[b16 facing] 90 deg sim turn: drawn yaw within 10% after {settle * 1000:F0} ms (target 60..90; omega {ActorAnimator.YawOmega})");
            Assert.That(settle, Is.InRange(0.06, 0.09));
            double snapped = double.NaN;
            Assert.IsTrue(ActorAnimator.ContactSnap(0.2, 0.2, 1.0 / 60, ref snapped, 1.0), "the contact frame snaps to the swing direction");
            Assert.IsFalse(ActorAnimator.ContactSnap(0.21, 0.2, 1.0 / 60, ref snapped, 1.0), "once per swing");
        }

        [Test]
        public void FootstepDustRisesWithSpeed()
        {
            float walk = FxScene.FootDust(0.5 * 3.8, 3.8), jog = FxScene.FootDust(0.8 * 3.8, 3.8), run = FxScene.FootDust(3.8, 3.8), dash = FxScene.FootDust(8, 3.8);
            Debug.Log($"[b16 dust] footstep dust scale at 50% / 80% / 100% / dash speed: {walk:F2} / {jog:F2} / {run:F2} / {dash:F2} (was sprint-only)");
            Assert.AreEqual(0f, walk);
            Assert.That(jog, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(1f, run, 1e-5f);
            Assert.That(dash, Is.GreaterThan(1f).And.LessThanOrEqualTo(1.3f));
        }

        static World NewWorld()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            return World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 4, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        [Test]
        public void ADeathWithNoDirectionFlingsAwayFromTheKiller()
        {
            World w = NewWorld();
            w.X[1] = 10; w.Z[1] = 10;
            CombatFx.KillerDir(w, 1, 13, 14, out double dx, out double dz);
            Debug.Log($"[b16 death fling] killer at (10, 10), victim at (13, 14): fling direction ({dx:F2}, {dz:F2}) (target (0.60, 0.80))");
            Assert.AreEqual(0.6, dx, 1e-6);
            Assert.AreEqual(0.8, dz, 1e-6);
            CombatFx.KillerDir(w, -1, 13, 14, out dx, out dz);
            Assert.AreEqual(0.0, dx);
            Assert.AreEqual(0.0, dz);
        }

        [Test]
        public void ARefusedJumpStumblesAndHighlightsTheEdgeBriefly()
        {
            World w = NewWorld();
            JumpRefusedView.Scan(w);
            w.Actors[0].Facing = 0;
            World.PushEvent(w, SimEventType.JumpRefused, 0, -1, 20, 20, 1);
            w.Tick++;
            Vector3 shake = JumpRefusedView.Offset(w, 0, 0.1);
            bool edge = JumpRefusedView.Edge(w, 0, 0.3, out double ex, out double ez, out float fade);
            Debug.Log($"[b16 refused jump] stumble offset {shake.magnitude:F3} u, edge highlight {edge} at ({ex:F2}, {ez:F2}) fade {fade:F2}");
            Assert.Greater(shake.magnitude, 0.005f, "the body stumbles");
            Assert.IsTrue(edge, "the edge in front lights up");
            Assert.AreEqual(20 + JumpRefusedView.EdgeAhead, ex, 1e-4);
            w.Tick += 30;
            Assert.AreEqual(Vector3.zero, JumpRefusedView.Offset(w, 0, 0), "the stumble is over within a second");
            Assert.IsFalse(JumpRefusedView.Edge(w, 0, 0, out _, out _, out _), "and the highlight");
        }
    }
}

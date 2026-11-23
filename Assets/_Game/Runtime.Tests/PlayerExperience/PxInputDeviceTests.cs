using System;
using CriticalMass.Runtime.Input;
using NUnit.Framework;
using Mode = CriticalMass.Runtime.Input.AimResolver.Mode;

namespace CriticalMass.Runtime.Tests.PlayerExperience
{
    /// <summary>
    /// Tier F31 / F34 (gate run 3f0ed276): the aiming device follows the most recent real input both ways, a resting
    /// cursor never keeps the aim from a keys player, and with no device input the frames sent never change the aim
    /// relative to the body (35 s, a moving camera and a pushed fighter).
    /// </summary>
    public class PxInputDeviceTests
    {
        const double Dt = 1.0 / 60.0;

        [Test]
        public void KeysOnlyWithTheMouseNeverMovedAimWithTheKeys()
        {
            var a = new AimDeviceArbiter();
            Mode m = Mode.Pointer;   // a fresh match on the keys-and-mouse scheme
            Assert.AreEqual(Mode.Pointer, a.Decide(0, m), "nothing used yet: keep the scheme's device");
            a.NoteKeys(0.1);
            m = a.Decide(0.1, m);
            Assert.AreEqual(Mode.Keys, m, "the first move key takes the aim from a cursor that never moved");
            // Clicking to attack with the mouse at rest is not aim input: no call reaches the arbiter, the keys keep it.
            for (double t = 0.2; t < 10; t += Dt) m = a.Decide(t, m);
            Assert.AreEqual(Mode.Keys, m);
        }

        /// <summary>Fix round 7 (owner's call): walk with the keys for 3 s with the cursor resting (it moved once at the start),
        /// then click: the click hands the aim to the cursor at once, past PointerHoldSeconds, with no move.</summary>
        [Test]
        public void AClickAfterWalkingWithTheKeysAimsAtTheRestingCursor()
        {
            var a = new AimDeviceArbiter();
            Mode m = Mode.Pointer;
            a.NotePointerMoved(0);
            m = a.Decide(0, m);
            double t = 0;
            for (; t < 3.0; t += Dt) { a.NoteKeys(t); m = a.Decide(t, m); }
            Assert.AreEqual(Mode.Keys, m, "3 s of walking with a resting cursor: the keys aim (past the hold)");
            a.NotePointerPressed(t);
            a.NoteKeys(t);   // still walking on the click frame
            m = a.Decide(t, m);
            Assert.AreEqual(Mode.Pointer, m, "the click aims at the cursor on its own frame");
            double u = t + Dt;
            for (; u < t + 0.25; u += Dt) { a.NoteKeys(u); m = a.Decide(u, m); }
            Assert.AreEqual(Mode.Pointer, m, "and after it, while the player keeps walking");
            a.NoteKeys(t + 0.3, true);
            m = a.Decide(t + 0.3, m);
            Assert.AreEqual(Mode.Pointer, m, "a keyboard attack right after the click does not snatch it back");
            double later = t + AimDeviceArbiter.KeyAttackHoldSeconds + 0.1;
            a.NoteKeys(later, true);
            Assert.AreEqual(Mode.Keys, a.Decide(later, m), "a keyboard attack once the cursor has rested takes it, as before");
        }

        [Test]
        public void TheHandOverGoesBothWays()
        {
            var a = new AimDeviceArbiter();
            Mode m = Mode.Pointer;
            a.NotePointerMoved(1.0);
            m = a.Decide(1.0, m);
            Assert.AreEqual(Mode.Pointer, m, "a mouse move aims with the cursor");
            // Walking with the keys while the cursor rests: the cursor keeps the aim for the hold, then the keys take it.
            double t = 1.0;
            for (; t < 1.0 + AimDeviceArbiter.PointerHoldSeconds - 0.05; t += Dt) { a.NoteKeys(t); m = a.Decide(t, m); }
            Assert.AreEqual(Mode.Pointer, m, "inside the hold the resting cursor still aims");
            for (; t < 1.0 + AimDeviceArbiter.PointerHoldSeconds + 0.1; t += Dt) { a.NoteKeys(t); m = a.Decide(t, m); }
            Assert.AreEqual(Mode.Keys, m, "after the hold a stationary cursor never overrides the keys");
            // Keys -> mouse: one real move hands it back at once.
            a.NotePointerMoved(t);
            m = a.Decide(t, m);
            Assert.AreEqual(Mode.Pointer, m, "a mouse move takes the aim back at once");
            // Mouse -> stick -> keys.
            t += 0.5;
            a.NoteStick(t);
            m = a.Decide(t, m);
            Assert.AreEqual(Mode.Stick, m, "a deflected stick aims");
            t += 0.5;
            a.NoteKeys(t);
            m = a.Decide(t, m);
            Assert.AreEqual(Mode.Pointer, m, "the cursor moved 1 s ago (inside the hold): back to the cursor, not the keys");
            t += AimDeviceArbiter.PointerHoldSeconds;
            a.NoteKeys(t);
            m = a.Decide(t, m);
            Assert.AreEqual(Mode.Keys, m, "with the cursor still past the hold the keys aim");
        }

        [Test]
        public void AKeyboardAttackTakesTheAimAfterTheShortRest()
        {
            var a = new AimDeviceArbiter();
            Mode m = Mode.Pointer;
            a.NotePointerMoved(0);
            a.NoteKeys(0.2, attack: true);
            Assert.AreEqual(Mode.Pointer, a.Decide(0.2, m), "too soon after the mouse moved");
            a.NoteKeys(AimDeviceArbiter.KeyAttackHoldSeconds + 0.1, attack: true);
            Assert.AreEqual(Mode.Keys, a.Decide(AimDeviceArbiter.KeyAttackHoldSeconds + 0.1, m), "a keyboard attack with the mouse resting");
        }

        [Test]
        public void SmallMouseJitterIsNotAimInput()
        {
            Assert.IsFalse(AimDeviceArbiter.PointerMoved(1f, 1f));
            Assert.IsTrue(AimDeviceArbiter.PointerMoved(3f, 0f));
        }

        [Test]
        public void ThirtyFiveSecondsUntouchedChangesNoAimInTheFramesSent()
        {
            var hold = new AimHold();
            double px = 0, pz = 0;
            // The first frame: the player last aimed 2 u ahead.
            bool has = true;
            double ax = 2, az = 0;
            hold.Apply(true, px, pz, ref has, ref ax, ref az);
            double rx0 = ax - px, rz0 = az - pz;
            double maxMove = 0;
            for (double t = 0; t < 35; t += Dt)
            {
                // The fighter is pushed and the camera slides (follow, lean, shake): the cursor's ground point moves.
                px += 0.01 * Math.Sin(t);
                pz += 0.004;
                bool h = true;
                double cx = 2 + 1.5 * Math.Sin(0.7 * t) + px * 0.5, cz = 0.8 * Math.Cos(1.3 * t);
                hold.Apply(false, px, pz, ref h, ref cx, ref cz);
                Assert.IsTrue(h);
                double rx = cx - px, rz = cz - pz;
                maxMove = Math.Max(maxMove, Math.Sqrt((rx - rx0) * (rx - rx0) + (rz - rz0) * (rz - rz0)));
            }
            Assert.Less(maxMove, 1e-9, "the aim relative to the body never changed without device input");
            // A touched frame takes the new raw aim at once.
            bool h2 = true;
            double nx = px + 5, nz = pz;
            hold.Apply(true, px, pz, ref h2, ref nx, ref nz);
            Assert.AreEqual(px + 5, nx, 1e-12);
        }

        [Test]
        public void AnUntouchedPointerOffTheGroundKeepsTheLastAim()
        {
            var hold = new AimHold();
            bool has = true;
            double ax = 1, az = 1;
            hold.Apply(true, 0, 0, ref has, ref ax, ref az);
            bool h = false;   // the camera moved and the resting cursor now misses the ground
            double x = 0, z = 0;
            hold.Apply(false, 0, 0, ref h, ref x, ref z);
            Assert.IsTrue(h);
            Assert.AreEqual(1, x, 1e-12);
            Assert.AreEqual(1, z, 1e-12);
        }
    }
}

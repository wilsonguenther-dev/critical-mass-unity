using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Input;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// CameraFollow: CameraRig.tsx 418-529 at 09e0904 (review advisory R20261112T1918-2-2) plus the U-005 amendment of
    /// 2026-11-12 (2 u screen-up offset, 3 u look-ahead cap, aim look-ahead toward the cursor).
    /// </summary>
    [TestFixture]
    public sealed class CameraFollowTests
    {
        private const double Dt = 1.0 / 60;
        private static double UpX => HumanInputState.ScreenUpX;
        private static double UpZ => HumanInputState.ScreenUpZ;

        private static CameraFollowInput Frame(double x, double z, double y = 0)
        {
            return new CameraFollowInput
            {
                Dt = Dt, HumanX = x, HumanZ = z, HumanY = y,
                MinX = -100, MaxX = 100, MinZ = -100, MaxZ = 100, Epoch = 0,
            };
        }

        [Test]
        public void TheFirstFrameFramesTheHumanTwoUnitsTowardScreenUp()
        {
            var f = new CameraFollow(UpX, UpZ);
            f.Step(Frame(10, 20));
            Assert.AreEqual(10 + 2 * UpX, f.SX.X, 1e-9);
            Assert.AreEqual(20 + 2 * UpZ, f.SZ.X, 1e-9);
        }

        [Test]
        public void LookAheadUsesTheRenderVelocityOfTheInterpolatedHuman()
        {
            // 3.8 u/s along +x for 3 s: the eased look-ahead settles at 3.8 x 0.25 = 0.95 u (browser), not the sim velocity.
            var f = new CameraFollow(UpX, UpZ);
            double x = 0;
            for (int i = 0; i < 180; i++)
            {
                f.Step(Frame(x, 0));
                x += 3.8 * Dt;
            }
            Assert.AreEqual(0.95, f.LaX, 1e-3);
            Assert.AreEqual(0, f.LaZ, 1e-9);
        }

        [Test]
        public void ATeleportOnlyZeroesTheLookAheadVelocityAndTheSpringFliesHome()
        {
            var f = new CameraFollow(UpX, UpZ);
            for (int i = 0; i < 30; i++) f.Step(Frame(0, 0));
            f.Step(Frame(30, 0)); // 30 u in one frame = 1800 u/s, over TeleportSpeed
            Assert.AreEqual(0, f.LaX, 1e-9, "the jump adds no look-ahead");
            Assert.Less(f.SX.X, 15, "the spring did not snap: it is on its way");
            Assert.Greater(f.SX.V, 0);
            for (int i = 0; i < 120; i++) f.Step(Frame(30, 0));
            Assert.AreEqual(30 + 2 * UpX, f.SX.X, 1e-3);
        }

        [Test]
        public void ANewMatchEpochSnaps()
        {
            var f = new CameraFollow(UpX, UpZ);
            f.Step(Frame(0, 0));
            CameraFollowInput g = Frame(40, 10);
            g.Epoch = 1;
            f.Step(g);
            Assert.AreEqual(40 + 2 * UpX, f.SX.X, 1e-9);
            Assert.AreEqual(10 + 2 * UpZ, f.SZ.X, 1e-9);
        }

        [Test]
        public void TheSpringIsClampedIntoTheBoundsAfterItsStepWithItsVelocityZeroed()
        {
            var f = new CameraFollow(UpX, UpZ);
            CameraFollowInput a = Frame(0, 0);
            a.MaxX = 5;
            f.Step(a);
            // Running hard toward +x: the goal is clamped at 5 and the spring's overshoot is clamped too.
            f.SX.X = 4.9;
            f.SX.V = 600; // one step would carry it to about 13.4
            f.Step(a);
            Assert.LessOrEqual(f.SX.X, 5);
            Assert.AreEqual(0, f.SX.V, 1e-12);
        }

        [Test]
        public void HeightSpringsTowardTheHumansGroundHeight()
        {
            var f = new CameraFollow(UpX, UpZ);
            f.Step(Frame(0, 0, 0));
            for (int i = 0; i < 3; i++) f.Step(Frame(0, 0, 0.75));
            Assert.Greater(f.SY.X, 0);
            Assert.Less(f.SY.X, 0.75);
            for (int i = 0; i < 120; i++) f.Step(Frame(0, 0, 0.75));
            Assert.AreEqual(0.75, f.SY.X, 1e-3);
        }

        [Test]
        public void AimLookAheadLeansTowardTheCursorAndIsCappedAtThreeUnits()
        {
            var f = new CameraFollow(UpX, UpZ);
            for (int i = 0; i < 120; i++)
            {
                CameraFollowInput a = Frame(0, 0);
                a.HasAim = true;
                a.AimX = 4; // 4 u x 0.25 = 1 u
                f.Step(a);
            }
            Assert.AreEqual(1.0, f.LaX, 1e-3);

            for (int i = 0; i < 120; i++)
            {
                CameraFollowInput a = Frame(0, 0);
                a.HasAim = true;
                a.AimX = 30;
                a.AimZ = 30;
                f.Step(a);
            }
            Assert.AreEqual(GameplayCamera.LookAheadMax, Math.Sqrt(f.LaX * f.LaX + f.LaZ * f.LaZ), 1e-3);
        }

        [Test]
        public void AnOverrideFramesItsTargetAtOnceWithItsGroundHeight()
        {
            var f = new CameraFollow(UpX, UpZ);
            f.Step(Frame(0, 0));
            CameraFollowInput o = Frame(0, 0);
            o.Override = true;
            o.OverrideX = 50;
            o.OverrideZ = -20;
            o.OverrideY = 1.5;
            f.Step(o);
            Assert.AreEqual(50, f.SX.X, 1e-9);
            Assert.AreEqual(-20, f.SZ.X, 1e-9);
            Assert.AreEqual(1.5, f.SY.X, 1e-9);

            // A second override target while one is on also frames at once when asked (capture tool shots).
            f.SnapToNextOverride();
            o.OverrideX = -30;
            o.OverrideZ = 12;
            f.Step(o);
            Assert.AreEqual(-30, f.SX.X, 1e-9);
            Assert.AreEqual(12, f.SZ.X, 1e-9);
        }
    }
}

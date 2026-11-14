using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// feat/follow-camera-1 (2026-11-14): the third- and first-person follow views' pure parts. The yaw settles behind
    /// the character in a set time with no overshoot and ignores quick taps; camera-relative input in all three views
    /// gives the world-space move the sim already takes, and holds its basis while a move is held; collision pulls the
    /// third-person camera in; first person sits at the eyes; the view cycle and the camera settings persist.
    /// </summary>
    public sealed class FollowCameraTests
    {
        const double Dt = 1 / 60.0;

        // ---------------------------------------------------------------- yaw follow

        /// <summary>Steps the follow toward want for seconds; returns the first time it was within tol of want and stayed,
        /// and the worst overshoot past want (degrees, in the direction of travel).</summary>
        static double Settle(YawFollow f, double want, double seconds, double tol, out double overshoot, out bool monotonic)
        {
            double start = GroundCamera.DeltaAngle(f.Yaw, want);
            double sign = start >= 0 ? 1 : -1;
            double settled = double.NaN, last = Math.Abs(start);
            overshoot = 0;
            monotonic = true;
            int n = (int)Math.Round(seconds / Dt);
            for (int i = 1; i <= n; i++)
            {
                f.Step(Dt, want, false);
                double err = GroundCamera.DeltaAngle(f.Yaw, want) * sign;   // > 0: still short of want, < 0: past it
                if (-err > overshoot) overshoot = -err;
                if (Math.Abs(err) > last + 1e-9) monotonic = false;
                last = Math.Abs(err);
                if (Math.Abs(err) <= tol) { if (double.IsNaN(settled)) settled = i * Dt; }
                else settled = double.NaN;
            }
            return settled;
        }

        [Test]
        public void TheYawSettlesBehindA90DegreeTurnInTimeWithoutOvershoot()
        {
            var f = new YawFollow();
            f.Reset(0);
            double t = Settle(f, 90, 4, 2, out double over, out bool mono);
            Assert.IsFalse(double.IsNaN(t), "settles within 2 degrees");
            Assert.Less(t, 2.0, "behind the character within 2 s (0.6 s delay for a big turn, then the spring)");
            Assert.Greater(t, YawFollow.BigTurnDelay, "never snaps: the delay comes first");
            Assert.Less(over, 3.0, "no overshoot beyond a few degrees");
            Assert.IsTrue(mono, "no jitter: the gap only ever closes");
        }

        [Test]
        public void TheYawSwingsRoundANearHalfTurnAndASmallCorrectionAtOnce()
        {
            // 170 degrees (an exact 180 has no shorter side; the spring then picks one, which is as good).
            var f = new YawFollow();
            f.Reset(10);
            double t = Settle(f, 180, 4, 2, out double over, out bool mono);
            Assert.Less(t, 2.2);
            Assert.Less(over, 3.0);
            Assert.IsTrue(mono);
            var g = new YawFollow();
            g.Reset(0);
            double s = Settle(g, 10, 2, 0.5, out double o2, out _);
            Assert.Less(s, 1.0, "a 10-degree drift is followed with no delay");
            Assert.Less(o2, 0.5);
        }

        [Test]
        public void AQuickTapDoesNotSwingTheView()
        {
            var f = new YawFollow();
            f.Reset(0);
            for (int i = 0; i < 6; i++) f.Step(Dt, 90, false);       // 0.1 s sideways
            for (int i = 0; i < 60; i++) f.Step(Dt, 0, false);       // then forward again
            Assert.Less(Math.Abs(GroundCamera.DeltaAngle(f.Yaw, 0)), 0.5);
            var g = new YawFollow();
            g.Reset(0);
            for (int i = 0; i < 6; i++) g.Step(Dt, 40, false);       // a 40-degree flick for 0.1 s (delay 0.15 s)
            for (int i = 0; i < 60; i++) g.Step(Dt, 0, false);
            Assert.Less(Math.Abs(GroundCamera.DeltaAngle(g.Yaw, 0)), 0.5);
        }

        [Test]
        public void ReduceMotionSettlesFasterAndRecentreHasNoDelay()
        {
            var f = new YawFollow();
            f.Reset(0);
            f.Recenter(120);
            int i = 0;
            for (; i < 240 && Math.Abs(GroundCamera.DeltaAngle(f.Yaw, 120)) > 2; i++) f.Step(Dt, 120, true);
            Assert.Less(i * Dt, 0.8, "re-centre starts at once; reduce motion uses the quick spring");
            double held = f.Yaw;
            f.Step(0, 0, false);
            Assert.AreEqual(held, f.Yaw, 1e-12, "a zero step (paused) changes nothing");
        }

        // ---------------------------------------------------------------- camera-relative input, all three views

        [Test]
        public void ForwardMovesAwayFromTheCameraInEveryView()
        {
            // Top-down: the high view's own basis (HumanInputState) equals the ground basis at the heading's Unity yaw.
            double hy = CameraHeading.UnityYaw(CameraHeading.Active);
            GroundCamera.CameraRelativeMove(0, 1, hy, out double tx, out double tz);
            Assert.AreEqual(HumanInputState.ScreenUpX, tx, 1e-6);
            Assert.AreEqual(HumanInputState.ScreenUpZ, tz, 1e-6);
            GroundCamera.CameraRelativeMove(1, 0, hy, out double trx, out double trz);
            Assert.AreEqual(HumanInputState.ScreenRightX, trx, 1e-6);
            Assert.AreEqual(HumanInputState.ScreenRightZ, trz, 1e-6);

            // Third and first person: from the follow's camera, forward points from the camera to the character and beyond.
            MapDef m = Flat(40, 40);
            foreach (GroundRig rig in new[] { FollowCamera.ThirdRig, FollowCamera.FirstRig })
                for (int y = 0; y < 360; y += 30)
                {
                    var f = new GroundFollow();
                    f.SetYaw(y);
                    f.SetPitch(rig.FirstPerson ? FollowCamera.FirstPitch : FollowCamera.ThirdPitch, rig.MinPitch, rig.MaxPitch);
                    f.Step(new GroundFollowInput { Dt = Dt, TargetX = 20.5, TargetZ = 20.5, Map = m, Rig = rig, Snap = true });
                    GroundCamera.CameraRelativeMove(0, 1, f.Yaw, out double mx, out double mz);
                    double ax = 20.5 + mx - f.CamX, az = 20.5 + mz - f.CamZ;     // one step forward, seen from the camera
                    double bx = 20.5 - f.CamX, bz = 20.5 - f.CamZ;
                    Assert.Greater(ax * ax + az * az, bx * bx + bz * bz - 1e-9, $"yaw {y}: forward moves away from the camera");
                    Assert.Greater(mx * Math.Sin(y * Math.PI / 180) - mz * Math.Cos(y * Math.PI / 180), 0.999, "forward is the view's facing");
                    GroundCamera.CameraRelativeMove(1, 0, f.Yaw, out double rx, out double rz);
                    Assert.AreEqual(0, rx * mx + rz * mz, 1e-9, "right is square to forward");
                }
        }

        [Test]
        public void TheInputBasisHoldsWhileAMoveIsHeldSoARunTowardTheCameraKeepsGoing()
        {
            var l = new InputLatch();
            Assert.AreEqual(90, l.Step(false, false, 90), 1e-9);
            Assert.AreEqual(90, l.Step(true, false, 90), 1e-9, "the move starts on the camera's yaw");
            Assert.AreEqual(90, l.Step(true, false, 180), 1e-9, "held: the camera swinging behind does not turn the run");
            Assert.AreEqual(180, l.Step(true, true, 180), 1e-9, "manual look takes the camera's yaw at once");
            Assert.AreEqual(270, l.Step(false, false, 270), 1e-9, "released: back on the camera's yaw");
        }

        [Test]
        public void TheLookAheadLeadsTheTravelAndStopsUnderReduceMotion()
        {
            FollowCamera.Lead(4, 0, false, out double lx, out double lz);
            Assert.AreEqual(FollowCamera.LeadMax, lx, 1e-9, "capped");
            Assert.AreEqual(0, lz, 1e-9);
            FollowCamera.Lead(1, 1, false, out lx, out lz);
            Assert.AreEqual(FollowCamera.LeadSeconds, lx, 1e-9);
            FollowCamera.Lead(4, 4, true, out lx, out lz);
            Assert.AreEqual(0, lx * lx + lz * lz, 1e-12);
        }

        // ---------------------------------------------------------------- collision, first person, corridor

        static MapDef Flat(int w, int h)
        {
            var m = new MapDef { Width = w, Height = h, Cells = new byte[w * h], Height01 = new double[w * h] };
            for (int i = 0; i < w * h; i++) m.Cells[i] = Cell.Floor;
            m.Bounds = new BoundsDef { MinX = 0, MinZ = 0, MaxX = w, MaxZ = h };
            return m;
        }

        [Test]
        public void AHighTerraceBehindPullsTheThirdPersonCameraInAndItEasesBackOut()
        {
            // Arena walls are 1.5 u (the third-person camera rides over them, as it should); a terrace 5 u up from z 18
            // stands behind the camera instead.
            MapDef m = Flat(30, 30);
            for (int z = 18; z < 30; z++)
                for (int x = 0; x < 30; x++) m.Height01[z * 30 + x] = 5;
            var f = new GroundFollow();
            // Yaw 0 looks along sim -z, so the camera sits behind at +z: toward the wall.
            f.SetYaw(0);
            f.SetPitch(FollowCamera.ThirdPitch, FollowCamera.ThirdMinPitch, FollowCamera.ThirdMaxPitch);
            var input = new GroundFollowInput { Dt = Dt, TargetX = 15.5, TargetZ = 16.5, Map = m, Rig = FollowCamera.ThirdRig, Snap = true };
            f.Step(input);
            Assert.Less(f.Dist, FollowCamera.ThirdDistance - 1, "pulled in front of the terrace");
            Assert.IsFalse(GroundCollision.Inside(m, f.CamX, f.CamY, f.CamZ, GroundCollision.Radius, null, 0), "never inside it");
            // Away from the wall: the distance eases back out (no jump), to the full third-person distance.
            input.Snap = false;
            input.TargetZ = 8.5;
            double prev = f.Dist;
            bool jumped = false;
            for (int i = 0; i < 180; i++)
            {
                f.Step(input);
                if (f.Dist - prev > 0.5) jumped = true;
                prev = f.Dist;
            }
            Assert.IsFalse(jumped, "a smooth return");
            Assert.AreEqual(FollowCamera.ThirdDistance, f.Dist, 0.05);
            Assert.GreaterOrEqual(f.CamY, FollowCamera.ThirdPivotHeight, "above the head: pitch down from over the shoulders");
        }

        [Test]
        public void FirstPersonSitsAtTheEyesAndThirdPersonPitchIsLimited()
        {
            MapDef m = Flat(20, 20);
            var f = new GroundFollow();
            f.SetYaw(90);
            f.Step(new GroundFollowInput { Dt = Dt, TargetX = 10, TargetZ = 10, GroundY = 0, Map = m, Rig = FollowCamera.FirstRig, Snap = true });
            Assert.AreEqual(FollowCamera.EyeHeight, f.CamY, 1e-6);
            Assert.AreEqual(10 + FollowCamera.EyeForward, f.CamX, 1e-6, "a little ahead along the facing (yaw 90 = sim +x)");
            Assert.AreEqual(0, f.Dist, 1e-9);
            var g = new GroundFollow();
            g.SetPitch(200, FollowCamera.ThirdMinPitch, FollowCamera.ThirdMaxPitch);
            Assert.AreEqual(FollowCamera.ThirdMaxPitch, g.Pitch, 1e-9);
            g.Step(new GroundFollowInput { Dt = Dt, TargetX = 10, TargetZ = 10, Map = m, Rig = FollowCamera.ThirdRig, OrbitPitch = -500, Snap = true });
            Assert.AreEqual(FollowCamera.ThirdMinPitch, g.Pitch, 1e-9);
        }

        [Test]
        public void AFighterOnTheLineToThePlayerIsInTheCorridorAndOneBesideItIsNot()
        {
            var cam = new Vector3(0, 3, 0);
            var me = new Vector3(0, 0, 4.4f);
            Assert.IsTrue(ViewCull.InCorridor(cam, me, new Vector3(0.3f, 0, 2f), 0.7f));
            Assert.IsFalse(ViewCull.InCorridor(cam, me, new Vector3(1.5f, 0, 2f), 0.7f));
            Assert.IsFalse(ViewCull.InCorridor(cam, me, new Vector3(0, 0, 4.4f), 0.7f), "the player himself is not");
            Assert.IsFalse(ViewCull.InCorridor(cam, me, new Vector3(0, 0, 6f), 0.7f), "beyond the player is not");
        }

        // ---------------------------------------------------------------- view cycle and settings

        static readonly string[] Keys =
        {
            GameSettings.GroundCameraKey, GameSettings.CameraViewKey, GameSettings.CameraManualKey, GameSettings.CameraSensKey,
            GameSettings.CameraFovKey, GameSettings.CameraInvertYKey, "cm.camera.heading",
        };

        [Test]
        public void TheViewCyclesAndEveryCameraSettingPersists()
        {
            var had = new bool[Keys.Length];
            var old = new int[Keys.Length];
            for (int i = 0; i < Keys.Length; i++) { had[i] = PlayerPrefs.HasKey(Keys[i]); old[i] = PlayerPrefs.GetInt(Keys[i], 0); }
            try
            {
                foreach (string k in Keys) PlayerPrefs.DeleteKey(k);
                Assert.AreEqual(CameraView.TopDown, GameSettings.CameraView, "top-down by default");
                Assert.IsFalse(GameSettings.CameraManual, "auto follow by default");
                Assert.AreEqual(1f, GameSettings.CameraSensitivity, 1e-6);
                Assert.AreEqual(FollowCamera.FovDefault, GameSettings.CameraFov);

                Assert.AreEqual(CameraView.ThirdPerson, FollowCamera.Next(CameraView.TopDown));
                Assert.AreEqual(CameraView.FirstPerson, FollowCamera.Next(CameraView.ThirdPerson));
                Assert.AreEqual(CameraView.TopDown, FollowCamera.Next(CameraView.FirstPerson));

                GameSettings.CameraView = CameraView.FirstPerson;
                Assert.AreEqual(CameraView.FirstPerson, GameSettings.CameraView);
                Assert.IsTrue(GameSettings.GroundCamera, "a low view is on");
                GameSettings.GroundCamera = false;
                Assert.AreEqual(CameraView.TopDown, GameSettings.CameraView);
                GameSettings.GroundCamera = true;
                Assert.AreEqual(CameraView.FirstPerson, GameSettings.CameraView, "the old switch brings back the last low view");
                PlayerPrefs.DeleteKey(GameSettings.CameraViewKey);
                Assert.AreEqual(CameraView.ThirdPerson, GameSettings.CameraView, "an older save with only GROUND on reads as third person");

                GameSettings.CameraManual = true;
                GameSettings.CameraInvertY = true;
                GameSettings.CameraSensitivity = 9f;
                GameSettings.CameraFov = 10;
                Assert.IsTrue(GameSettings.CameraManual);
                Assert.IsTrue(GameSettings.CameraInvertY);
                Assert.AreEqual(3f, GameSettings.CameraSensitivity, 1e-6, "clamped");
                Assert.AreEqual(FollowCamera.FovMin, GameSettings.CameraFov, "clamped");
                Assert.AreEqual(1, PlayerPrefs.GetInt(GameSettings.CameraManualKey, 0), "stored in the prefs");

                // The Settings line cycles all four choices and back.
                GameSettings.CameraView = CameraView.TopDown;
                string first = PauseMenu.CameraText();
                var seen = new System.Collections.Generic.HashSet<string> { first };
                for (int i = 0; i < 3; i++) { PauseMenu.StepCamera(1); seen.Add(PauseMenu.CameraText()); }
                Assert.AreEqual(4, seen.Count);
                Assert.AreEqual(CameraView.FirstPerson, GameSettings.CameraView);
                PauseMenu.StepCamera(1);
                Assert.AreEqual(CameraView.TopDown, GameSettings.CameraView);
                PauseMenu.StepFollow(-1);
                Assert.IsTrue(GameSettings.CameraManual && !GameSettings.CameraInvertY, "MANUAL, INVERT Y back one is MANUAL");
                PauseMenu.StepFollow(-1);
                Assert.IsFalse(GameSettings.CameraManual, "and back one more is AUTO");
            }
            finally
            {
                for (int i = 0; i < Keys.Length; i++)
                {
                    if (had[i]) PlayerPrefs.SetInt(Keys[i], old[i]);
                    else PlayerPrefs.DeleteKey(Keys[i]);
                }
            }
        }
    }
}

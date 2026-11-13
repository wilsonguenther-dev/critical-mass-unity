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
    /// feat/ground-camera (2026-11-13): the ground view's pure parts. Camera-relative input becomes the world-space move
    /// and aim the sim already takes (every direction at several yaws); the high view's input is unchanged by default;
    /// collision pulls the camera in front of walls, terraces and cylinders; target cycling and the auto-director; the
    /// CAMERA setting persists.
    /// </summary>
    public sealed class GroundCameraTests
    {
        const double Eps = 1e-9;

        // ---------------------------------------------------------------- camera-relative input

        [Test]
        public void TheBasisAtYaw315IsTheHighViewsFixedBasis()
        {
            // The high view is Euler(55, 360 - 45, 0): its screen basis is HumanInputState's static one.
            GroundCamera.Basis(315, out double ux, out double uz, out double rx, out double rz);
            Assert.AreEqual(HumanInputState.ScreenUpX, ux, 1e-12);
            Assert.AreEqual(HumanInputState.ScreenUpZ, uz, 1e-12);
            Assert.AreEqual(HumanInputState.ScreenRightX, rx, 1e-12);
            Assert.AreEqual(HumanInputState.ScreenRightZ, rz, 1e-12);
        }

        [Test]
        public void EveryDirectionAtSeveralYawsGivesTheExpectedWorldVector()
        {
            // Unity yaw psi looks along Unity (sin psi, 0, cos psi) = sim (sin psi, -cos psi) (SimSpace negates z).
            double[] yaws = { 0, 45, 90, 135, 180, 225, 270, 315, 17.5, 333 };
            for (int y = 0; y < yaws.Length; y++)
            {
                Vector3 fwdU = Quaternion.Euler(0f, (float)yaws[y], 0f) * Vector3.forward;
                Vector3 rightU = Quaternion.Euler(0f, (float)yaws[y], 0f) * Vector3.right;
                for (int d = 0; d < 8; d++)
                {
                    double a = d * Math.PI / 4;
                    double right = Math.Round(Math.Sin(a), 12), up = Math.Round(Math.Cos(a), 12);
                    GroundCamera.CameraRelativeMove(right, up, yaws[y], out double x, out double z);
                    Vector3 wantU = fwdU * (float)up + rightU * (float)right;
                    SimSpace.ToSim(wantU, out double ex, out double ez);
                    double el = Math.Sqrt(ex * ex + ez * ez);
                    Assert.AreEqual(ex / el, x, 1e-5, $"yaw {yaws[y]} dir {d} x");
                    Assert.AreEqual(ez / el, z, 1e-5, $"yaw {yaws[y]} dir {d} z");
                    Assert.AreEqual(1.0, Math.Sqrt(x * x + z * z), 1e-6, "full stick is full speed, never more");
                }
            }
            // Up is the camera's forward, right is its right, a diagonal stays length 1.
            GroundCamera.CameraRelativeMove(0, 1, 90, out double fx, out double fz);
            Assert.AreEqual(1, fx, Eps);
            Assert.AreEqual(0, fz, Eps);
            GroundCamera.CameraRelativeMove(1, 1, 0, out double qx, out double qz);
            Assert.AreEqual(1, Math.Sqrt(qx * qx + qz * qz), Eps);
            GroundCamera.CameraRelativeMove(0, 0, 123, out double zx, out double zz);
            Assert.AreEqual(0, zx, Eps);
            Assert.AreEqual(0, zz, Eps);
        }

        [Test]
        public void CameraBasisFeedsTheSimTheSameKindOfWorldMove()
        {
            var s = new HumanInputState();
            InputFrame f = SimInput.EmptyInput();
            foreach (double yaw in new[] { 0.0, 90, 200, 315 })
            {
                GroundCamera.Basis(yaw, out double ux, out double uz, out double rx, out double rz);
                s.SetCameraBasis(ux, uz, rx, rz);
                s.SetMove(0, 1, true);
                s.Fill(f);
                Assert.AreEqual(ux, f.MoveX, 1e-9, $"W walks along the camera at yaw {yaw}");
                Assert.AreEqual(uz, f.MoveZ, 1e-9);
                s.SetMove(1, 0, false);
                s.Fill(f);
                Assert.AreEqual(rx, f.MoveX, 1e-9, $"right strafes right at yaw {yaw}");
                Assert.AreEqual(rz, f.MoveZ, 1e-9);
            }
            // Cleared: back to the fixed high-view basis, byte for byte.
            s.ClearCameraBasis();
            Assert.IsFalse(s.CameraBasisActive);
            s.SetMove(0, 1, false);
            s.Fill(f);
            Assert.AreEqual(HumanInputState.ScreenUpX, f.MoveX, 1e-12);
            Assert.AreEqual(HumanInputState.ScreenUpZ, f.MoveZ, 1e-12);
        }

        [Test]
        public void TheHighViewIsUnchangedByDefault()
        {
            var s = new HumanInputState();
            Assert.IsFalse(s.CameraBasisActive, "no camera basis unless ground play sets one");
            InputFrame f = SimInput.EmptyInput();
            s.SetMove(1, 0, false);
            s.Fill(f);
            Assert.AreEqual(HumanInputState.ScreenRightX, f.MoveX, 1e-12);
            Assert.AreEqual(HumanInputState.ScreenRightZ, f.MoveZ, 1e-12);
            Assert.AreEqual(315.0, GameplayCamera.Rotation(GameConfig.CreateDefault()).eulerAngles.y, 1e-3, "the high camera still turns 45");
            Assert.IsFalse(GroundView.IsActive, "no ground view without a match");
        }

        [Test]
        public void AimMapsAlongTheCameraFacingWithinItsLimits()
        {
            GroundCamera.AimPoint(10, 20, 90, 9, out double ax, out double az);   // yaw 90 looks along sim +x
            Assert.AreEqual(19, ax, 1e-9);
            Assert.AreEqual(20, az, 1e-9);
            GroundCamera.AimPoint(10, 20, 180, 4, out ax, out az);                // yaw 180 looks along sim +z
            Assert.AreEqual(10, ax, 1e-9);
            Assert.AreEqual(24, az, 1e-9);
            GroundCamera.AimPoint(0, 0, 0, 0.5, out ax, out az);                  // never nearer than MinAim
            Assert.AreEqual(-GroundCamera.MinAim, az, 1e-9);
            GroundCamera.AimPoint(0, 0, 270, 999, out ax, out az);                // never farther than MaxAim
            Assert.AreEqual(-GroundCamera.MaxAim, ax, 1e-9);
            GroundCamera.AimPoint(0, 0, 0, double.NaN, out ax, out az);
            Assert.IsFalse(double.IsNaN(ax) || double.IsNaN(az), "a NaN distance never reaches the sim");
            // YawToward is the inverse of Basis.
            foreach (double y in new[] { 0.0, 33, 90, 181, 300 })
            {
                GroundCamera.Basis(y, out double ux, out double uz, out _, out _);
                Assert.AreEqual(0, GroundCamera.DeltaAngle(y, GroundCamera.YawToward(1, 1, 1 + ux, 1 + uz, -1)), 1e-6);
            }
            Assert.AreEqual(GroundCamera.Normalize(SimSpace.YawDeg(0.7)), GroundCamera.YawOfFacing(0.7), 1e-4);
        }

        // ---------------------------------------------------------------- collision

        static MapDef Flat(int w, int h, double height = 0)
        {
            var m = new MapDef { Width = w, Height = h, Cells = new byte[w * h], Height01 = new double[w * h] };
            for (int i = 0; i < w * h; i++)
            {
                m.Cells[i] = Cell.Floor;
                m.Height01[i] = height;
            }
            m.Bounds = new BoundsDef { MinX = 0, MinZ = 0, MaxX = w, MaxZ = h };
            return m;
        }

        [Test]
        public void OpenGroundLetsTheCameraSitAtFullDistance()
        {
            MapDef m = Flat(20, 20);
            double d = GroundCollision.Resolve(m, 10, 1.3, 10, 0, 0.12, 1, GroundCamera.Distance, null, 0);
            Assert.AreEqual(GroundCamera.Distance, d, Eps);
        }

        [Test]
        public void AWallBehindPullsTheCameraInFrontOfIt()
        {
            MapDef m = Flat(20, 20);
            for (int x = 0; x < 20; x++) m.Cells[12 * 20 + x] = Cell.Wall;   // a wall row at z 12..13
            // Pivot at z 10.5, camera wanted 3 u toward +z: the wall face is at z 12.
            double d = GroundCollision.Resolve(m, 10.5, 1.3, 10.5, 0, 0, 1, 3, null, 0);
            Assert.Less(d, 1.5, "the camera stops before the wall face");
            Assert.GreaterOrEqual(d, GroundCollision.MinDistance);
            Assert.IsFalse(GroundCollision.Inside(m, 10.5, 1.3, 10.5 + d, GroundCollision.Radius, null, 0), "the camera sphere is outside the wall");
            // Above the wall (a high pitch) there is nothing to hit.
            double up = GroundCollision.Resolve(m, 10.5, 4.5, 10.5, 0, 0, 1, 3, null, 0);
            Assert.AreEqual(3, up, Eps);
        }

        [Test]
        public void AHigherTerraceAndACylinderPullInTooButTheVoidDoesNot()
        {
            MapDef m = Flat(20, 20);
            for (int z = 13; z < 20; z++)
                for (int x = 0; x < 20; x++) m.Height01[z * 20 + x] = 3;   // a terrace 3 u up from z 13
            double d = GroundCollision.Resolve(m, 10.5, 1.3, 10.5, 0, 0.05, 1, 3.5, null, 0);
            Assert.Less(d, 2.5);
            var cyl = new[] { new SeeThroughCylinder { X = 10.5, Z = 8.5, R = 0.6, Y0 = 0, Y1 = 5 } };
            double c = GroundCollision.Resolve(Flat(20, 20), 10.5, 1.3, 10.5, 0, 0, -1, 3, cyl, 1);
            Assert.Less(c, 1.3, "a tree behind the camera pulls it in");
            MapDef v = Flat(20, 20);
            for (int z = 12; z < 20; z++)
                for (int x = 0; x < 20; x++) v.Cells[z * 20 + x] = Cell.Void;   // the island ends at z 12: a cliff
            Assert.AreEqual(3, GroundCollision.Resolve(v, 10.5, 1.3, 10.5, 0, 0, 1, 3, null, 0), Eps, "out over the void is open air");
        }

        [Test]
        public void TheFollowNeverPutsTheCameraInsideGeometry()
        {
            MapDef m = Flat(30, 30);
            for (int x = 0; x < 30; x++) m.Cells[18 * 30 + x] = Cell.Wall;
            var f = new GroundFollow();
            var input = new GroundFollowInput { Dt = 1 / 60.0, TargetX = 15.5, TargetZ = 16.5, GroundY = 0, Map = m, AutoYaw = true, Snap = true };
            // Walk the yaw round in 10-degree steps with a facing that puts the wall behind at some point.
            for (int i = 0; i < 72; i++)
            {
                input.WantYaw = i * 10;
                input.Snap = true;
                f.Step(input);
                Assert.IsFalse(GroundCollision.Inside(m, f.CamX, f.CamY, f.CamZ, GroundCollision.Radius * 0.9, null, 0),
                    $"yaw {i * 10}: camera at ({f.CamX:F2}, {f.CamY:F2}, {f.CamZ:F2}) is inside the wall");
            }
        }

        [Test]
        public void PullInIsImmediateAndLettingOutEases()
        {
            MapDef open = Flat(30, 30), walled = Flat(30, 30);
            for (int x = 0; x < 30; x++) walled.Cells[17 * 30 + x] = Cell.Wall;
            var f = new GroundFollow();
            var input = new GroundFollowInput { Dt = 1 / 60.0, TargetX = 15.5, TargetZ = 15.5, Map = open, AutoYaw = true, WantYaw = 0, Snap = true };
            f.Step(input);   // yaw 0 looks along sim -z: the camera sits toward +z
            Assert.AreEqual(GroundCamera.Distance, f.Dist, 1e-6);
            input.Snap = false;
            input.Map = walled;
            f.Step(input);
            Assert.Less(f.Dist, GroundCamera.Distance - 0.5, "pull-in happens in the same frame");
            double pulled = f.Dist;
            input.Map = open;
            f.Step(input);
            Assert.Greater(f.Dist, pulled);
            Assert.Less(f.Dist, GroundCamera.Distance, "letting out eases (OutTau)");
        }

        // ---------------------------------------------------------------- targets

        static World NewWorld()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            return World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        [Test]
        public void TargetCyclingWrapsAndSkipsTheKnockedOut()
        {
            World w = NewWorld();
            int n = w.ActorCount;
            Assert.AreEqual(1, GroundCamera.NextAlive(w, 0, 1));
            Assert.AreEqual(n - 1, GroundCamera.NextAlive(w, 0, -1), "previous from the first wraps to the last");
            Assert.AreEqual(0, GroundCamera.NextAlive(w, n - 1, 1), "next from the last wraps to the first");
            w.Actors[1].Alive = false;
            w.Actors[2].Alive = false;
            Assert.AreEqual(3, GroundCamera.NextAlive(w, 0, 1), "the knocked out are skipped");
            Assert.AreEqual(0, GroundCamera.NextAlive(w, 3, -1));
            for (int i = 0; i < n; i++) w.Actors[i].Alive = false;
            Assert.AreEqual(-1, GroundCamera.NextAlive(w, 0, 1), "nobody standing: no target");
            Assert.AreEqual(-1, GroundCamera.NextAlive(null, 0, 1));
        }

        [Test]
        public void TheDirectorHoldsAShotThenCutsToTheBusiestFighter()
        {
            World w = NewWorld();
            var d = new GroundDirector();
            Assert.IsTrue(d.Step(w, 0.016), "the first frame picks someone");
            int first = d.Current;
            Assert.IsTrue(GroundDirector.Valid(w, first));
            // Make a duel on the hill for another fighter: an enemy right beside him.
            int hero = first == 2 ? 3 : 2;
            int foe = -1;
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Team != w.Actors[hero].Team) { foe = i; break; }
            HillDef hill = w.Map.Hill;
            w.X[hero] = hill.X; w.Z[hero] = hill.Z;
            w.X[foe] = hill.X + 1; w.Z[foe] = hill.Z;
            Assert.Greater(GroundDirector.Score(w, hero), GroundDirector.Score(w, first) * GroundDirector.SwitchMargin);
            Assert.IsFalse(d.Step(w, GroundDirector.MinShot * 0.5), "no cut before the minimum shot length");
            bool cut = d.Step(w, GroundDirector.MinShot);
            Assert.IsTrue(cut, "after the minimum shot the duel on the hill wins");
            Assert.IsTrue(d.Current == hero || d.Current == foe, "the shot is on the duel");
            // The followed fighter goes down: the next frame cuts away at once.
            w.Actors[d.Current].Alive = false;
            Assert.IsTrue(d.Step(w, 0.016));
            Assert.IsTrue(GroundDirector.Valid(w, d.Current));
            Assert.AreEqual(-1, GroundDirector.Score(w, GroundDirector.KaijuTarget), "an unhatched Kaiju is no target");
        }

        // ---------------------------------------------------------------- setting

        [Test]
        public void TheCameraSettingPersistsAndDefaultsToHigh()
        {
            bool had = PlayerPrefs.HasKey(GameSettings.GroundCameraKey);
            int old = PlayerPrefs.GetInt(GameSettings.GroundCameraKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(GameSettings.GroundCameraKey);
                Assert.IsFalse(GameSettings.GroundCamera, "HIGH by default");
                GameSettings.GroundCamera = true;
                Assert.AreEqual(1, PlayerPrefs.GetInt(GameSettings.GroundCameraKey, 0), "stored in PlayerPrefs (kept between launches)");
                Assert.IsTrue(GameSettings.GroundCamera);
                GameSettings.GroundCamera = false;
                Assert.IsFalse(GameSettings.GroundCamera);
                CollectionAssert.Contains(GameSettings.AllKeys, GameSettings.GroundCameraKey, "reset and the test backups know the key");
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(GameSettings.GroundCameraKey, old);
                else PlayerPrefs.DeleteKey(GameSettings.GroundCameraKey);
            }
        }

        [Test]
        public void TheGroundFlagsNeedAnAutomationFlag()
        {
            Assert.IsTrue(CriticalMass.Runtime.Game.AutoplayArgs.HasAutomationFlag(new[] { "x", "-autoplay", "-groundView" }, "-groundview"));
            Assert.IsFalse(CriticalMass.Runtime.Game.AutoplayArgs.HasAutomationFlag(new[] { "x", "-groundView" }, "-groundview"));
            var o = CriticalMass.Runtime.Game.AutoplayArgs.Parse(new[] { "-autoplay", "-shots", "-shotTick", "4800", "-seed", "1337" });
            Assert.AreEqual(4800, o.ShotTick);
            Assert.AreEqual(1337, o.Seed);
        }
    }
}

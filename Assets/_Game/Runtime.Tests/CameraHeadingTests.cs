using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// feat/camera-creator-angle: the creator's angle (heading 135) puts the Crimson base bottom-left and the Azure base
    /// top-right on screen; up on the stick is up on the screen; the minimap uses the same heading; the classic 45 stays.
    /// </summary>
    public sealed class CameraHeadingTests
    {
        double? _old;
        [SetUp] public void SetUp() { _old = CameraHeading.Pin; CameraHeading.Pin = CameraHeading.Creator; }
        [TearDown] public void TearDown() => CameraHeading.Pin = _old;

        static void Screen(double heading, double simX, double simZ, out float sx, out float sy)
        {
            // The real camera rotation (GameplayCamera.Rotation): Euler(55, 360 - heading, 0); a point's screen offset from
            // the map centre along the camera's right and its ground-projected forward.
            Quaternion r = Quaternion.Euler(55f, (float)(360 - heading), 0f);
            Vector3 right = r * Vector3.right, fwd = r * Vector3.forward;
            fwd.y = 0; fwd.Normalize();
            Vector3 p = SimSpace.ToWorld(simX, 0, simZ);
            sx = Vector3.Dot(p, right);
            sy = Vector3.Dot(p, fwd);
        }

        [Test]
        public void TheCreatorAnglePutsCrimsonBottomLeftAndAzureTopRight()
        {
            MapDef map = MinimapGeometry.BakedMap;
            PadDef azure = map.Pads[(int)TeamId.Azure], crimson = map.Pads[(int)TeamId.Crimson];   // pads are indexed by team
            Assert.IsNotNull(azure); Assert.IsNotNull(crimson);
            double cx = map.Hill.X, cz = map.Hill.Z;
            Screen(CameraHeading.Creator, crimson.X - cx, crimson.Z - cz, out float rx, out float ry);
            Screen(CameraHeading.Creator, azure.X - cx, azure.Z - cz, out float bx, out float by);
            Assert.Less(rx, 0, "Crimson left"); Assert.Less(ry, 0, "Crimson bottom");
            Assert.Greater(bx, 0, "Azure right"); Assert.Greater(by, 0, "Azure top");
            // The classic diamond: Azure top-left, Crimson bottom-right.
            Screen(CameraHeading.Classic, crimson.X - cx, crimson.Z - cz, out rx, out ry);
            Assert.Greater(rx, 0); Assert.Less(ry, 0);
        }

        [Test]
        public void UpOnTheStickIsUpOnTheScreenAtTheCreatorAngle()
        {
            Quaternion r = Quaternion.Euler(55f, (float)(360 - CameraHeading.Creator), 0f);
            Vector3 fwd = r * Vector3.forward; fwd.y = 0; fwd.Normalize();
            SimSpace.ToSim(fwd, out double fx, out double fz);
            Assert.AreEqual(fx, HumanInputState.ScreenUpX, 1e-5);
            Assert.AreEqual(fz, HumanInputState.ScreenUpZ, 1e-5);
            // Right is up turned clockwise on screen.
            Vector3 right = r * Vector3.right; right.y = 0; right.Normalize();
            SimSpace.ToSim(right, out double rx, out double rz);
            Assert.AreEqual(rx, HumanInputState.ScreenRightX, 1e-5);
            Assert.AreEqual(rz, HumanInputState.ScreenRightZ, 1e-5);
        }

        [Test]
        public void TheMinimapUsesTheHeadingOnScreenAndTheClassicStaysAvailable()
        {
            Assert.AreEqual(CameraHeading.Creator, MinimapGeometry.Projection.YawDeg, 1e-9);
            CameraHeading.Pin = CameraHeading.Classic;
            Assert.AreEqual(CameraHeading.Classic, MinimapGeometry.Projection.YawDeg, 1e-9);
            Assert.AreEqual(45d, MinimapGeometry.BakedYaw, 1e-9, "the baked images stay the classic heading");
        }

        [Test]
        public void TheSettingDefaultFollowsTheFlipLineAndBothHeadingsStaySelectable()
        {
            CameraHeading.Pin = null;
            bool had = PlayerPrefs.HasKey(CameraHeading.Key);
            int old = PlayerPrefs.GetInt(CameraHeading.Key, 0);
            try
            {
                PlayerPrefs.DeleteKey(CameraHeading.Key);
                Assert.AreEqual(CameraHeading.CreatorAngleIsDefault ? CameraHeading.Creator : CameraHeading.Classic, CameraHeading.Chosen);
                CameraHeading.ClassicChosen = true;
                Assert.AreEqual(CameraHeading.Classic, CameraHeading.Chosen);
                CameraHeading.ClassicChosen = false;
                Assert.AreEqual(CameraHeading.Creator, CameraHeading.Chosen);
                Assert.Contains(CameraHeading.Key, CriticalMass.Runtime.Menu.GameSettings.AllKeys);
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(CameraHeading.Key, old); else PlayerPrefs.DeleteKey(CameraHeading.Key);
            }
        }
    }
}

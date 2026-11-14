using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// fix/follow-camera-3 (2026-11-14): effects at the lens and on the sight line, the on-screen size cap, fighter LOD by
    /// camera distance in the low views, first person turning off a wall and never poking into one, and the own-fighter rim.
    /// </summary>
    public sealed class FollowCamera3Tests
    {
        GameObject _camGo;

        UnityEngine.Camera LowCamera()
        {
            _camGo = new GameObject("Follow3 Test Camera");
            var cam = _camGo.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.aspect = 16f / 9f;
            _camGo.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(14f, 0f, 0f));   // looks along +z
            return cam;
        }

        [TearDown]
        public void TearDown()
        {
            ViewCull.SetFollowLine(Vector3.zero, false);
            ViewCull.SetView(null, false);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        [Test]
        public void AnEffectAtTheLensIsDroppedAndOneNearItShrinks()
        {
            ViewCull.SetView(LowCamera(), true);
            int near0 = ViewCull.FxNearCulled, shrunk0 = ViewCull.FxShrunk;
            Assert.AreEqual(0f, ViewCull.FxScale(new Vector3(0f, 2.9f, 0.9f), 0.3f, true), "burst touching the lens: not drawn");
            float k = ViewCull.FxScale(new Vector3(0f, 2.5f, 3f), 0.5f, true);
            Assert.Greater(k, 0f);
            Assert.Less(k, 1f, "inside NearFade: shrunk");
            Assert.AreEqual(1f, ViewCull.FxScale(new Vector3(0f, 1f, 12f), 0.5f, true), "small and away from the lens: full size");
            Assert.AreEqual(1f, ViewCull.FxScale(new Vector3(0f, 2.9f, 0.9f), 0.3f, false), "ground marks keep only the view test");
            Assert.AreEqual(1, ViewCull.FxNearCulled - near0);
            Assert.AreEqual(1, ViewCull.FxShrunk - shrunk0);
            ViewCull.SetView(null, false);
            Assert.AreEqual(1f, ViewCull.FxScale(new Vector3(0f, 3f, 0.2f), 0.5f, true), "top-down: untouched");
        }

        [Test]
        public void BigBillboardsAreCappedOnScreen()
        {
            ViewCull.SetView(LowCamera(), true);
            var c = new Vector3(0f, 1f, 20f);
            float d = (c - new Vector3(0f, 3f, 0f)).magnitude;
            float k = ViewCull.FxScale(c, 8f, true);
            Assert.Greater(k, 0f);
            Assert.LessOrEqual(8f * k, ViewCull.MaxScreenTan * d + 1e-3f, "radius at most MaxScreenTan of the distance");
        }

        [Test]
        public void EffectsOnTheSightLineToThePlayerAreDropped()
        {
            ViewCull.SetView(LowCamera(), true);
            ViewCull.SetFollowLine(new Vector3(0f, 1.6f, 4.4f), true);
            var mid = new Vector3(0f, 2.3f, 2.2f);
            Assert.AreEqual(0f, ViewCull.FxScale(mid, 0.3f, true), "smoke between the camera and the player: not drawn");
            Assert.Greater(ViewCull.FxScale(new Vector3(1.5f, 2.3f, 4f), 0.3f, true), 0f, "to the side of the line: drawn");
            Assert.Greater(ViewCull.FxScale(new Vector3(0f, 1.6f, 9f), 0.3f, true), 0f, "beyond the player: drawn");
            ViewCull.SetFollowLine(Vector3.zero, false);
            Assert.Greater(ViewCull.FxScale(mid, 0.3f, true), 0f, "first person (no line): drawn");
        }

        [Test]
        public void ANearFadeBuilderDropsTheLensBurstAndAPlainOneDoesNot()
        {
            ViewCull.SetView(LowCamera(), true);
            var air = new FxMeshBuilder("Follow3 Air", 64, 192) { ViewCulled = true, NearFade = true };
            var ground = new FxMeshBuilder("Follow3 Ground", 64, 192) { ViewCulled = true };
            try
            {
                air.Quad(new Vector3(0f, 2.9f, 0.9f), Vector3.right * 0.2f, Vector3.up * 0.2f, Color.white, Color.white);
                Assert.AreEqual(0, air.VertCount, "air burst at the lens: not built");
                air.Quad(new Vector3(0f, 1f, 10f), Vector3.right * 0.2f, Vector3.up * 0.2f, Color.white, Color.white);
                Assert.AreEqual(4, air.VertCount, "in view, away from the lens: built");
                ground.Quad(new Vector3(0f, 2.9f, 0.9f), Vector3.right * 0.2f, Vector3.up * 0.2f, Color.white, Color.white);
                Assert.AreEqual(4, ground.VertCount, "telegraphs and ground marks keep only the view test");
            }
            finally
            {
                Object.DestroyImmediate(air.Mesh);
                Object.DestroyImmediate(ground.Mesh);
            }
        }

        [Test]
        public void LowViewLodIsByCameraDistanceWithTheStatedThreshold()
        {
            float inf = float.PositiveInfinity;
            Assert.AreEqual(9f, FollowCamera.LowLod0Metres);
            Assert.AreEqual(6, FollowCamera.LowLod0Budget);
            Assert.AreEqual(0, FollowCamera.LowViewLod(true, 50f, 50f, 0f), "your own fighter: always full detail");
            Assert.AreEqual(0, FollowCamera.LowViewLod(false, 5f, 3f, inf));
            Assert.AreEqual(1, FollowCamera.LowViewLod(false, 9.5f, 3f, inf), "past 9 u from the camera: LOD1");
            Assert.AreEqual(1, FollowCamera.LowViewLod(false, 5f, 6f, 4f), "outside the crowd budget: LOD1");
        }

        static MapDef WallAhead(out double eyeY)
        {
            int w = 20, h = 20;
            var m = new MapDef { Width = w, Height = h, Cells = new byte[w * h], Height01 = new double[w * h] };
            for (int i = 0; i < w * h; i++) m.Cells[i] = Cell.Floor;
            for (int x = 0; x < w; x++) m.Cells[11 * w + x] = Cell.Wall;   // a wall row at sim z 11..12
            m.Bounds = new BoundsDef { MinX = 0, MinZ = 0, MaxX = w, MaxZ = h };
            eyeY = FollowCamera.EyeHeight;
            return m;
        }

        [Test]
        public void FirstPersonTurnsOffAWallRightInFrontAndNotOffOpenGround()
        {
            MapDef m = WallAhead(out double y);
            double toWall = GroundCamera.YawToward(0, 0, 0, 1, 0), away = GroundCamera.YawToward(0, 0, 0, -1, 0);
            Assert.Less(FollowCamera.Ahead(m, 10.5, y, 10.2, toWall, null, 0), FollowCamera.WallNear, "the setup faces a wall 0.8 u away");
            double turned = FollowCamera.WallYaw(m, 10.5, y, 10.2, toWall, null, 0);
            Assert.Greater(Math.Abs(GroundCamera.DeltaAngle(toWall, turned)), 1.0, "turned off the wall");
            Assert.GreaterOrEqual(FollowCamera.Ahead(m, 10.5, y, 10.2, turned, null, 0), FollowCamera.WallNear, "the turned view looks clear");
            Assert.LessOrEqual(Math.Abs(GroundCamera.DeltaAngle(toWall, turned)), 75.0 + 1e-6, "never more than 75 degrees off");
            Assert.AreEqual(away, FollowCamera.WallYaw(m, 10.5, y, 10.2, away, null, 0), 1e-9, "open ground: untouched");
            Assert.AreEqual(toWall, FollowCamera.WallYaw(null, 10.5, y, 10.2, toWall, null, 0), 1e-9);
        }

        [Test]
        public void FirstPersonEyesNeverPokeIntoAWall()
        {
            MapDef m = WallAhead(out double y);
            GroundCamera.Basis(GroundCamera.YawToward(0, 0, 0, 1, 0), out double ux, out double uz, out _, out _);
            Assert.AreEqual(FollowCamera.EyeForward, FollowCamera.EyeOffset(m, 10.5, y, 8.0, ux, uz, null, 0), 1e-9, "open: a little ahead");
            Assert.AreEqual(0.0, FollowCamera.EyeOffset(m, 10.5, y, 10.85, ux, uz, null, 0), 1e-9, "pressed to the wall: at the pivot");
            Assert.AreEqual(FollowCamera.EyeBack, FollowCamera.EyeOffset(m, 10.5, y, 11.2, ux, uz, null, 0), 1e-9, "pivot touching it: behind");
        }

        [Test]
        public void TheOwnFighterRimIsLightAndWithinTheOutlineRange()
        {
            Assert.Greater(ActorViews.SelfRimPx, 3f, "a touch wider than the 3 px character outline");
            Assert.LessOrEqual(ActorViews.SelfRimPx, 8f, "inside the shader's range");
            Assert.Greater(ActorViews.SelfRimColor.grayscale, 0.8f, "light, against the navy outlines everyone else has");
        }
    }
}

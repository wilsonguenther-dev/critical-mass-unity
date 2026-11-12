using CriticalMass.Runtime;
using CriticalMass.Runtime.Camera;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// GroundHeight (review finding R20261112T1918-2-1): picking.ts islandHeightAt with the cell clamped to the map, and
    /// the aim plane at the human's ground height (humanInput.ts pickHumanAim). Map: SkybreakBasinMap.BuildSkybreakBasin
    /// (GameConfig.CreateBrowser5v5()); cell (56, 40) is the centre of the central hill island, H 0.75.
    /// </summary>
    [TestFixture]
    public sealed class GroundHeightTests
    {
        private const int HillCx = 56, HillCz = 40;
        private GameConfig _config;
        private MapDef _map;

        [SetUp]
        public void SetUp()
        {
            _config = GameConfig.CreateBrowser5v5();
            _map = SkybreakBasinMap.BuildSkybreakBasin(_config);
        }

        [Test]
        public void IslandHeightAtReadsTheCellUnderThePointAndClampsOffTheMap()
        {
            Assert.AreEqual(0.75, _map.Height01[HillCz * _map.Width + HillCx], 1e-12, "fixture: the hill island centre cell");
            Assert.AreEqual(0.75, GroundHeight.IslandHeightAt(_map, HillCx + 0.99, HillCz + 0.01), 1e-12);
            int w = _map.Width, h = _map.Height;
            Assert.AreEqual(_map.Height01[0], GroundHeight.IslandHeightAt(_map, -5, -3), 1e-12, "below the map: cell (0, 0)");
            Assert.AreEqual(_map.Height01[0], GroundHeight.IslandHeightAt(_map, double.NaN, double.NaN), 1e-12, "NaN: cell (0, 0)");
            Assert.AreEqual(_map.Height01[(h - 1) * w + (w - 1)], GroundHeight.IslandHeightAt(_map, w + 7, h + 2), 1e-12,
                "past the map: the last cell");
            Assert.AreEqual(_map.Height01[HillCz * w + (w - 1)], GroundHeight.IslandHeightAt(_map, w + 0.5, HillCz + 0.5), 1e-12,
                "past one edge: clamped on that axis only");
        }

        [Test]
        public void ARayThroughAWorldPointAboveAHillCellReturnsThatCellsSimPoint()
        {
            double sx = HillCx + 0.5, sz = HillCz + 0.5;
            Vector3 target = SimSpace.ToWorld(sx, 0.75, sz);
            Vector3 forward = SimSpace.CameraRotation(_config) * Vector3.forward;
            var ray = new Ray(target - forward * 30f, forward);

            Assert.IsTrue(GroundHeight.TryAimPoint(ray, _map, sx, sz, out double ax, out double az));
            Assert.AreEqual(sx, ax, 1e-3);
            Assert.AreEqual(sz, az, 1e-3);

            // The old y = 0 plane misses that point by h / tan(pitch) = 0.75 / tan(55 deg), about 0.53 u (U-005 amendment
            // 2026-11-12; it was 0.96 u at the old 38 degrees). The centre ray of the perspective camera has the pitch.
            Assert.IsTrue(GroundHeight.TryAimPoint(ray, null, 0, 0, out double zx, out double zz));
            double miss = System.Math.Sqrt((zx - sx) * (zx - sx) + (zz - sz) * (zz - sz));
            Assert.AreEqual(0.75 / System.Math.Tan(GameplayCamera.PitchDeg * System.Math.PI / 180), miss, 1e-3);
            Assert.AreEqual(0.525, miss, 2e-3);
        }

        [Test]
        public void PerspectiveRaysFromTheGameplayCameraHitTheHillCellTheyPassThrough()
        {
            // Under perspective every pointer ray starts at the camera and points through its pixel; it is not parallel
            // to the camera forward. A ray from the camera position through a hill-top point off screen centre must
            // still return that point's sim position.
            double sx = HillCx + 0.5, sz = HillCz + 0.5;
            Vector3 lookTarget = SimSpace.ToWorld(sx + 4, 0.75, sz - 3);
            Vector3 camPos = GameplayCamera.PositionFor(_config, lookTarget);
            Vector3 p = SimSpace.ToWorld(sx, 0.75, sz);
            var ray = new Ray(camPos, (p - camPos).normalized);
            Assert.IsTrue(GroundHeight.TryAimPoint(ray, _map, sx, sz, out double ax, out double az));
            Assert.AreEqual(sx, ax, 1e-3);
            Assert.AreEqual(sz, az, 1e-3);
        }

        [Test]
        public void EveryViewportCornerRayMeetsTheGroundAndTheFootprintMatchesTheArtDirection()
        {
            // Art direction 2.1: about 26 u across at screen centre, 7.4 u below the target and 11.6 u above it at 16:9,
            // and the horizon never in frame (the top-edge ray still points down).
            Vector3 target = Vector3.zero;
            double aspect = 16.0 / 9.0;
            foreach (var (vx, vy) in new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) })
            {
                Ray r = GameplayCamera.ViewportRay(_config, target, vx, vy, aspect);
                Assert.Less(r.direction.y, -0.5f, "corner " + vx + "," + vy + " points down");
                Assert.IsTrue(GroundHeight.TryAimPoint(r, null, 0, 0, out _, out _));
            }
            Vector3 up = SimSpace.CameraRotation(_config) * Vector3.forward;
            up.y = 0;
            up.Normalize();
            Assert.AreEqual(-7.38, Vector3.Dot(Hit(0.5, 0.0, aspect) - target, up), 0.05, "ground below the target");
            Assert.AreEqual(11.56, Vector3.Dot(Hit(0.5, 1.0, aspect) - target, up), 0.05, "ground above the target");
            Assert.AreEqual(26.2, Vector3.Distance(Hit(0.0, 0.5, aspect), Hit(1.0, 0.5, aspect)), 0.1, "width at centre");
        }

        [Test]
        public void ATwoUnitCharacterAtScreenCentreIs90PixelsTallAt1080p()
        {
            // Art direction 2.1 says 84 px; that is the orthographic estimate 2 cos(55) / (2 x 23.4 tan(17.5)) x 1080.
            // Under perspective the head is 2 sin(55) = 1.64 u nearer the lens than the feet, so the true feet-to-head
            // height is 90.3 px, at the top of acceptance check 2's 78 to 90 px band (reported in the PR).
            Quaternion inv = Quaternion.Inverse(SimSpace.CameraRotation(_config));
            Vector3 cam = GameplayCamera.PositionFor(_config, Vector3.zero);
            double t = System.Math.Tan(GameplayCamera.FovDeg * System.Math.PI / 360);
            double ScreenY(Vector3 w)
            {
                Vector3 l = inv * (w - cam);
                return l.y / (l.z * t) * 540.0;
            }
            double px = ScreenY(new Vector3(0, (float)_config.Render.CharacterHeight, 0)) - ScreenY(Vector3.zero);
            Assert.AreEqual(90.3, px, 0.3);
            Assert.AreEqual(84, px * (23.4 - 2 * System.Math.Sin(55 * System.Math.PI / 180)) / 23.4, 0.5, "the orthographic estimate");
        }

        private Vector3 Hit(double vx, double vy, double aspect)
        {
            Ray r = GameplayCamera.ViewportRay(_config, Vector3.zero, vx, vy, aspect);
            Assert.IsTrue(GroundHeight.PlaneAt(0).Raycast(r, out float d));
            return r.GetPoint(d);
        }

        [Test]
        public void PlaneAtUsesUnitysNegatedDistance()
        {
            Plane p = GroundHeight.PlaneAt(0.75);
            Assert.AreEqual(0f, p.GetDistanceToPoint(new Vector3(3f, 0.75f, -2f)), 1e-6f);
            Assert.AreEqual(-0.75f, p.distance, 1e-6f);
        }
    }
}

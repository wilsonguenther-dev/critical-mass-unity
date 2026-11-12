using CriticalMass.Runtime;
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

            // The old y = 0 plane misses that point by h / tan(pitch) = 0.75 / tan(38 deg), about 0.96 u.
            Assert.IsTrue(GroundHeight.TryAimPoint(ray, null, 0, 0, out double zx, out double zz));
            double miss = System.Math.Sqrt((zx - sx) * (zx - sx) + (zz - sz) * (zz - sz));
            Assert.AreEqual(0.75 / System.Math.Tan(_config.Render.CameraPitchDeg * System.Math.PI / 180), miss, 1e-3);
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

using System;
using CriticalMass.Runtime;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// SimSpace axis conversion (F-08a, O-13 ruling). New, no vitest twin: SimDriver.tsx has no test file. Every map comes
    /// from SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateBrowser5v5()).
    /// </summary>
    [TestFixture]
    public sealed class SimSpaceTests
    {
        private GameConfig _config;
        private MapDef _map;

        [SetUp]
        public void SetUp()
        {
            _config = GameConfig.CreateBrowser5v5();
            _map = SkybreakBasinMap.BuildSkybreakBasin(_config);
        }

        [Test]
        public void TheAzurePadIsOnTheLeftOfTheScreen()
        {
            Vector3 right = SimSpace.CameraRotation(_config) * Vector3.right;
            Vector3 hill = SimSpace.ToWorld(_map.Hill.X, 0, _map.Hill.Z);
            PadDef azure = _map.Pads[(int)TeamId.Azure];
            PadDef crimson = _map.Pads[(int)TeamId.Crimson];
            Vector3 azureWorld = SimSpace.ToWorld(azure.X, 0, azure.Z);
            Vector3 crimsonWorld = SimSpace.ToWorld(crimson.X, 0, crimson.Z);

            Assert.Less(Vector3.Dot(azureWorld - hill, right), 0f);
            Assert.Greater(Vector3.Dot(crimsonWorld - hill, right), 0f);
        }

        [Test]
        public void ScreenUpIsSimMinus1Minus1OverSqrt2()
        {
            Vector3 f = SimSpace.CameraRotation(_config) * Vector3.forward;
            f.y = 0f;
            f.Normalize();
            SimSpace.ToSim(f, out double sx, out double sz);

            double expected = -1 / Math.Sqrt(2);
            Assert.AreEqual(expected, sx, 1e-6);
            Assert.AreEqual(expected, sz, 1e-6);
        }

        [Test]
        public void ToSimOfToWorldIsExact()
        {
            double[,] points =
            {
                { 0, 0 },
                { 12.5, 36.5 },
                { 111.75, 71.25 },
                { -3.5, 0.125 },
            };
            for (int i = 0; i < points.GetLength(0); i++)
            {
                double x = points[i, 0];
                double z = points[i, 1];
                SimSpace.ToSim(SimSpace.ToWorld(x, 0, z), out double sx, out double sz);
                Assert.AreEqual(x, sx);
                Assert.AreEqual(z, sz);
            }
        }
    }
}

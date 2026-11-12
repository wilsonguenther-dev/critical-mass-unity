using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/flowfield.test.ts.</summary>
    [TestFixture]
    public sealed class FlowFieldTests
    {
        private static MapDef BuildMap()
        {
            return SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateBrowser5v5());
        }

        [Test]
        public void DijkstraCosts1OrthogonalAndSQRT2DiagonalUnreachableIsInfinity()
        {
            MapDef map = BuildMap();
            FlowField f = FlowField.ComputeFlowField(map, new[] { new PointXZ(56.5, 40.5) });
            int W = map.Width;
            Assert.AreEqual(0.0, f.Dist[40 * W + 56]);
            Assert.AreEqual(1.0, f.Dist[40 * W + 57], 0.5e-6);
            Assert.AreEqual(Math.Sqrt(2), f.Dist[41 * W + 57], 0.5e-6);
            Assert.IsTrue(double.IsPositiveInfinity(f.Dist[0]), "corner is VOID"); // corner is VOID
            for (int i = 0; i < map.Cells.Length; i++)
            {
                byte c = map.Cells[i];
                if (c != Cell.Floor && c != Cell.Bridge && c != Cell.BreakableDestroyed)
                    Assert.IsTrue(double.IsPositiveInfinity(f.Dist[i]), "cell " + i + " (type " + c + ") should be Infinity");
            }
        }

        [Test]
        public void ReusesTheOutFieldAndSamplesAUnitDirectionTowardTheGoal()
        {
            MapDef map = BuildMap();
            FlowField f = FlowField.ComputeFlowField(map, new[] { new PointXZ(56.5, 40.5) });
            FlowField again = FlowField.ComputeFlowField(map, new[] { new PointXZ(56.5, 40.5) }, f);
            Assert.AreSame(f, again);
            FlowField.SampleFlowDir(f, map, 50.5, 40.5, out double dx, out double dz);
            Assert.AreEqual(1.0, Math.Sqrt(dx * dx + dz * dz), 0.5e-6);
            Assert.Greater(dx, 0.5);
        }

        [Test]
        public void NeverCutsACornerBetweenTwoBlockedOrthogonalCells()
        {
            // Tiny synthetic map: walkable diagonal pair separated by two walls.
            MapDef map = BuildMap();
            MapDef tiny = map.WithCells(new byte[] { Cell.Floor, Cell.Wall, Cell.Wall, Cell.Floor });
            tiny.Width = 2;
            tiny.Height = 2;
            FlowField f = FlowField.ComputeFlowField(tiny, new[] { new PointXZ(0.5, 0.5) });
            Assert.IsTrue(double.IsPositiveInfinity(f.Dist[3]));
        }
    }
}

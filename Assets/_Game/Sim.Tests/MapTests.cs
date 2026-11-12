using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of the 5 grid-only cases of src/sim/map.test.ts (the 4 flow-field cases are F-04).</summary>
    [TestFixture]
    public sealed class MapTests
    {
        private const int W = 112;
        private const int H = 72;

        private static MapDef Build()
        {
            return SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateBrowser5v5());
        }

        private static int CellOf(double x, double z)
        {
            return JsMath.FloorToInt(z) * W + JsMath.FloorToInt(x);
        }

        [Test]
        public void Is112X72WithPadsHillAndShrineWhereTheSpecPutsThem()
        {
            GameConfig config = GameConfig.CreateBrowser5v5();
            MapDef map = SkybreakBasinMap.BuildSkybreakBasin(config);
            Assert.AreEqual(W, map.Width);
            Assert.AreEqual(H, map.Height);
            Assert.AreEqual(W * H, map.Cells.Length);
            Assert.AreEqual(W * H, map.Height01.Length);
            Assert.AreEqual(W * H, map.WallHp.Length);
            PadDef azure = map.Pads[(int)TeamId.Azure];
            Assert.AreEqual(8.0, azure.X);
            Assert.AreEqual(36.0, azure.Z);
            Assert.AreEqual(3.0, azure.R);
            PadDef crimson = map.Pads[(int)TeamId.Crimson];
            Assert.AreEqual(104.0, crimson.X);
            Assert.AreEqual(36.0, crimson.Z);
            Assert.AreEqual(3.0, crimson.R);
            Assert.AreEqual(56.0, map.Hill.X);
            Assert.AreEqual(40.0, map.Hill.Z);
            Assert.AreEqual(config.Hill.Radius, map.Hill.R);
            Assert.AreEqual(56.0, map.Shrine.X);
            Assert.AreEqual(8.0, map.Shrine.Z);
            Assert.AreEqual(1.5, map.Shrine.SolidR);
            Assert.AreEqual(0.0, map.Bounds.MinX);
            Assert.AreEqual(0.0, map.Bounds.MinZ);
            Assert.AreEqual((double)W, map.Bounds.MaxX);
            Assert.AreEqual((double)H, map.Bounds.MaxZ);
            Assert.IsTrue(MapGrid.IsWalkable(map, 8, 36));
            Assert.IsTrue(MapGrid.IsWalkable(map, 104, 36));
            Assert.IsTrue(MapGrid.IsWalkable(map, 56, 40));
            Assert.AreEqual(Cell.Wall, map.Cells[CellOf(56, 8)]); // shrine solid
        }

        [Test]
        public void IsDeterministicTwoBuildsAreIdentical()
        {
            MapDef map = Build();
            MapDef b = Build();
            CollectionAssert.AreEqual(map.Cells, b.Cells);
            CollectionAssert.AreEqual(map.Height01, b.Height01);
            CollectionAssert.AreEqual(map.WallHp, b.WallHp);
        }

        [Test]
        public void HasTheShrineYardGapToTheSouthAndBreakableShortcutsWith2Hp()
        {
            MapDef map = Build();
            // B7 lands in the gap at 90 degrees (due south of the shrine), radius 4..5.
            Assert.IsTrue(MapGrid.IsWalkable(map, 56, 12));
            Assert.IsTrue(MapGrid.IsWalkable(map, 55, 12));
            // East of the gap, at the same radius, is wall.
            Assert.AreEqual(Cell.Wall, map.Cells[CellOf(60.5, 8.5)]);
            int breakables = 0;
            for (int i = 0; i < map.Cells.Length; i++)
            {
                if (map.Cells[i] == Cell.BreakableIntact)
                {
                    breakables++;
                    Assert.AreEqual(2, (int)map.WallHp[i]);
                }
                else Assert.AreEqual(0, (int)map.WallHp[i]);
            }
            Assert.GreaterOrEqual(breakables, 8);
        }

        [Test]
        public void HasAll8BiomassAnd3CorruptionNodesOnWalkableCells()
        {
            MapDef map = Build();
            Assert.AreEqual(8, map.BiomassNodes.Length);
            Assert.AreEqual(3, map.CorruptionNodes.Length);
            foreach (PointXZ n in map.BiomassNodes)
                Assert.IsTrue(MapGrid.IsWalkable(map, JsMath.FloorToInt(n.X), JsMath.FloorToInt(n.Z)));
            foreach (PointXZ n in map.CorruptionNodes)
                Assert.IsTrue(MapGrid.IsWalkable(map, JsMath.FloorToInt(n.X), JsMath.FloorToInt(n.Z)));
        }

        [Test]
        public void IsMirrorSymmetricInCellTypeAcrossX56()
        {
            MapDef map = Build();
            int asym = 0;
            for (int cz = 0; cz < H; cz++)
                for (int cx = 0; cx < W; cx++)
                    if (map.Cells[cz * W + cx] != map.Cells[cz * W + (W - 1 - cx)]) asym++;
            Assert.AreEqual(0, asym);
        }
    }
}

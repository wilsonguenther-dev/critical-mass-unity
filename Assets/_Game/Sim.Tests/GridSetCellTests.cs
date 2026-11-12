using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of the setCell case of src/sim/grid.test.ts (09e0904, line 7).</summary>
    [TestFixture]
    public sealed class GridSetCellTests
    {
        [Test]
        public void SetCellBumpsGridVersionOnEveryChangeAndWallVersionOnlyForNonBarricadeChanges()
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 1,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Miner,
                Config = GameConfig.CreateBrowser5v5(),
            });
            int g0 = w.GridVersion;
            int v0 = w.WallVersion;
            // Barricade placed on FLOOR and removed again: grid only.
            MapGrid.SetCell(w, 56, 40, Cell.Barricade);
            Assert.AreEqual(false, MapGrid.IsWalkable(w.Map, 56, 40));
            MapGrid.SetCell(w, 56, 40, Cell.Floor);
            Assert.AreEqual(g0 + 2, w.GridVersion);
            Assert.AreEqual(v0, w.WallVersion);
            // A breakable wall changing state: both.
            int bx = -1;
            int bz = -1;
            for (int i = 0; i < w.Map.Cells.Length && bx < 0; i++)
                if (w.Map.Cells[i] == Cell.BreakableIntact)
                {
                    bx = i % w.Map.Width;
                    bz = (i - bx) / w.Map.Width; // exact: i - bx is a multiple of Width
                }
            MapGrid.SetCell(w, bx, bz, Cell.BreakableCracked);
            Assert.AreEqual(g0 + 3, w.GridVersion);
            Assert.AreEqual(v0 + 1, w.WallVersion);
            // Same type again: no-op.
            MapGrid.SetCell(w, bx, bz, Cell.BreakableCracked);
            Assert.AreEqual(g0 + 3, w.GridVersion);
            MapGrid.SetCell(w, bx, bz, Cell.BreakableDestroyed);
            Assert.AreEqual(true, MapGrid.IsWalkable(w.Map, bx, bz));
            Assert.AreEqual(v0 + 2, w.WallVersion);
        }
    }
}

using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of the grid-only case of src/sim/grid.test.ts (the setCell case is F-02b).</summary>
    [TestFixture]
    public sealed class GridTests
    {
        [Test]
        public void CellAtWorldToCellClearanceOk()
        {
            MapDef map = SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateBrowser5v5());
            Assert.AreEqual((int)Cell.Floor, MapGrid.CellAt(map, 56.2, 40.9));
            Assert.AreEqual((int)Cell.Void, MapGrid.CellAt(map, -1, 5));
            Assert.IsTrue(MapGrid.WorldToCell(map, 10.7, 3.2, out int cx, out int cz));
            Assert.AreEqual(10, cx);
            Assert.AreEqual(3, cz);
            Assert.IsFalse(MapGrid.WorldToCell(map, 200, 3, out cx, out cz));
            Assert.IsTrue(MapGrid.ClearanceOk(map, 56, 40, 1.6)); // open hill centre
            Assert.IsFalse(MapGrid.ClearanceOk(map, 0, 0, 0.5)); // VOID
        }
    }
}

using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// F-06c: steer.ts has no vitest file of its own (its oracle is the bot fixtures of F-06a, F-06b, F-06e, F-06f).
    /// This case is new, no vitest twin: it pins FirstBlockedCell, worked out by hand from steer.ts lines 74-110.
    /// </summary>
    [TestFixture]
    public sealed class BotSteerTests
    {
        [Test]
        public void FirstBlockedCellWalksTheGridToTheFirstNonWalkableCell()
        {
            var cells = new byte[64];
            for (int i = 0; i < cells.Length; i++) cells[i] = Cell.Floor;
            var map = new MapDef { Width = 8, Height = 8, Cells = cells };
            map.Cells[2 * 8 + 4] = Cell.Wall;

            // East along row 2 from cell (1, 2): cells 18, 19 are floor, 20 is the wall.
            Assert.AreEqual(20, BotSteer.FirstBlockedCell(map, 1.5, 2.5, 6.5, 2.5));
            // The end cell (3, 2) is reached before any blocked cell.
            Assert.AreEqual(-1, BotSteer.FirstBlockedCell(map, 1.5, 2.5, 3.5, 2.5));
            // From (6, 2) east: cell 23 is floor, then cx = 8 is off the map: cz * W + cx = 2 * 8 + 8.
            Assert.AreEqual(24, BotSteer.FirstBlockedCell(map, 6.5, 2.5, 9.5, 2.5));
        }
    }
}

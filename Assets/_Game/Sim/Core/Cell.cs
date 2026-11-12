namespace CriticalMass.Sim.Core
{
    /// <summary>Grid cell types (browser CELL). Walkable = Floor | BreakableDestroyed | Bridge. CellType is byte everywhere.</summary>
    public static class Cell
    {
        public const byte Void = 0, Floor = 1, Wall = 2, BreakableIntact = 3, BreakableCracked = 4,
            BreakableDestroyed = 5, Water = 6, Bridge = 7, Barricade = 8;
    }
}

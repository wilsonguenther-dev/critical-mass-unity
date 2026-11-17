namespace CriticalMass.Sim.Core
{
    /// <summary>Grid cell types (browser CELL). Walkable = Floor | BreakableDestroyed | Bridge. CellType is byte everywhere.</summary>
    public static class Cell
    {
        public const byte Void = 0, Floor = 1, Wall = 2, BreakableIntact = 3, BreakableCracked = 4,
            BreakableDestroyed = 5, Water = 6, Bridge = 7, Barricade = 8,
            /// <summary>CM b13 [B]: a short wall cell. Blocks walking and bot paths; an AIRBORNE actor passes over it like in-map
            /// void; every hit, blast, crush and clearance test that treats Barricade as a breakable obstacle treats it the same.</summary>
            LowWall = 9;
    }
}

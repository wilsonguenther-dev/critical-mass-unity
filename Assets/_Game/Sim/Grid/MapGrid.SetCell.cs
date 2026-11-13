using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Grid
{
    public static partial class MapGrid
    {
        /// <summary>Change one cell: GridVersion bumps on every change, WallVersion (and WallChangeTick, CM-30) unless the change is a barricade swap.</summary>
        public static void SetCell(World w, int cx, int cz, byte type)
        {
            MapDef map = w.Map;
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return;
            int i = cz * map.Width + cx;
            byte old = map.Cells[i];
            if (old == type) return;
            map.Cells[i] = type;
            w.GridVersion++;
            if (old != Cell.Barricade && type != Cell.Barricade)
            {
                w.WallVersion++;
                w.WallChangeTick = w.Tick; // CM-30: terraform's rate cap keys on the last field-invalidating change
            }
        }
    }
}

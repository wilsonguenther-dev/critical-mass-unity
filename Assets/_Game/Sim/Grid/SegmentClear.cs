using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Grid
{
    /// <summary>
    /// DG-83 (S3, OD-4, not in the browser): line of sight on the grid. A DDA walk (one grid cell at a time along the segment,
    /// Amanatides-Woo) that fails on the first Wall, Barricade, intact or cracked Breakable cell, or a cell off the map.
    /// Void, Water, Floor, Bridge and destroyed breakables let a hit through. The cell (ignoreCx, ignoreCz) is skipped (the
    /// target cell of a wall break or barricade chip). Allocation-free, deterministic (ties step x first).
    /// </summary>
    public static class SegmentClear
    {
        /// <summary>True when cell type t stops a hit.</summary>
        public static bool Blocks(byte t)
        {
            return t == Cell.Wall || t == Cell.Barricade || t == Cell.BreakableIntact || t == Cell.BreakableCracked;
        }

        /// <summary>True when nothing blocks the segment (x0, z0) to (x1, z1), the cell (ignoreCx, ignoreCz) excepted.</summary>
        public static bool Clear(MapDef map, double x0, double z0, double x1, double z1, int ignoreCx = int.MinValue, int ignoreCz = int.MinValue)
        {
            int cx = JsMath.FloorToInt(x0);
            int cz = JsMath.FloorToInt(z0);
            int ex = JsMath.FloorToInt(x1);
            int ez = JsMath.FloorToInt(z1);
            double dx = x1 - x0;
            double dz = z1 - z0;
            int sx = dx > 0 ? 1 : dx < 0 ? -1 : 0;
            int sz = dz > 0 ? 1 : dz < 0 ? -1 : 0;
            double tdx = sx != 0 ? Math.Abs(1 / dx) : double.PositiveInfinity;
            double tdz = sz != 0 ? Math.Abs(1 / dz) : double.PositiveInfinity;
            double tmx = sx > 0 ? (cx + 1 - x0) * tdx : sx < 0 ? (x0 - cx) * tdx : double.PositiveInfinity;
            double tmz = sz > 0 ? (cz + 1 - z0) * tdz : sz < 0 ? (z0 - cz) * tdz : double.PositiveInfinity;
            int guard = Math.Abs(ex - cx) + Math.Abs(ez - cz) + 2;
            for (int step = 0; step <= guard; step++)
            {
                if (!(cx == ignoreCx && cz == ignoreCz))
                {
                    if (!MapGrid.InBounds(map, cx, cz)) return false;
                    if (Blocks(map.Cells[cz * map.Width + cx])) return false;
                }
                if (cx == ex && cz == ez) return true;
                if (tmx <= tmz)
                {
                    tmx += tdx;
                    cx += sx;
                }
                else
                {
                    tmz += tdz;
                    cz += sz;
                }
            }
            return true;
        }
    }
}

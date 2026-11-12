using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Grid
{
    /// <summary>Grid helpers over a MapDef (browser grid.ts). Out-of-bounds cells are never walkable.</summary>
    public static partial class MapGrid
    {
        /// <summary>True when (cx, cz) is a cell of the map.</summary>
        public static bool InBounds(MapDef map, int cx, int cz)
        {
            return cx >= 0 && cz >= 0 && cx < map.Width && cz < map.Height;
        }

        /// <summary>Walkable = FLOOR | BREAKABLE_DESTROYED | BRIDGE (everything else blocks).</summary>
        public static bool IsWalkableType(int t)
        {
            return t == Cell.Floor || t == Cell.BreakableDestroyed || t == Cell.Bridge;
        }

        /// <summary>True when (cx, cz) is in bounds and its cell type is walkable.</summary>
        public static bool IsWalkable(MapDef map, int cx, int cz)
        {
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return false;
            return IsWalkableType(map.Cells[cz * map.Width + cx]);
        }

        /// <summary>Cell type at a world point; VOID outside the map.</summary>
        public static int CellAt(MapDef map, double x, double z)
        {
            int cx = JsMath.FloorToInt(x);
            int cz = JsMath.FloorToInt(z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return Cell.Void;
            return map.Cells[cz * map.Width + cx];
        }

        /// <summary>World point to cell coordinates. Returns false when the point is outside the map.</summary>
        public static bool WorldToCell(MapDef map, double x, double z, out int cx, out int cz)
        {
            cx = JsMath.FloorToInt(x);
            cz = JsMath.FloorToInt(z);
            return InBounds(map, cx, cz);
        }

        /// <summary>Flat cell index, or -1 when out of bounds.</summary>
        public static int CellIndex(MapDef map, int cx, int cz)
        {
            return InBounds(map, cx, cz) ? cz * map.Width + cx : -1;
        }

        /// <summary>True when a circle of radius centred on cell (cx, cz)'s centre overlaps no non-walkable cell rectangle.</summary>
        public static bool ClearanceOk(MapDef map, int cx, int cz, double radius)
        {
            if (!IsWalkable(map, cx, cz)) return false;
            double x = cx + 0.5;
            double z = cz + 0.5;
            double r2 = radius * radius;
            int x0 = JsMath.FloorToInt(x - radius);
            int x1 = JsMath.FloorToInt(x + radius);
            int z0 = JsMath.FloorToInt(z - radius);
            int z1 = JsMath.FloorToInt(z + radius);
            for (int gz = z0; gz <= z1; gz++)
            {
                for (int gx = x0; gx <= x1; gx++)
                {
                    if (IsWalkable(map, gx, gz)) continue;
                    double nx = x < gx ? gx : x > gx + 1 ? gx + 1 : x;
                    double nz = z < gz ? gz : z > gz + 1 ? gz + 1 : z;
                    double ddx = x - nx;
                    double ddz = z - nz;
                    if (ddx * ddx + ddz * ddz < r2) return false;
                }
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Grid
{
    /// <summary>
    /// b14 sim cost: "does this cell reach that goal" without a distance field. The route guard (Terraform.CellsKeepRoutes)
    /// only ever asked whether a team pad's cell got a FINITE distance in FlowField.ComputeFlowField; a full Dijkstra over
    /// the map paid ~650 us for that yes/no, up to four times per proof, and a TNT crater proves every carved cell. A cell
    /// has a finite flow-field distance exactly when it is connected to a valid goal cell through walkable cells by the
    /// field's own steps: orthogonal steps onto walkable cells, and diagonal steps only when BOTH orthogonal cells are
    /// walkable (no corner cutting). Such a diagonal step can always be replaced by its two orthogonal steps, so the answer
    /// is plain 4-connectivity over MapGrid.IsWalkableType, from the same goals (in the map and walkable; others ignored).
    /// Breadth-first, stops as soon as every asked cell is found. Allocation-free after the first map (static scratch).
    /// Same answers as the field, so no Sim result moves.
    /// </summary>
    public static class GridReach
    {
        private static int[] Queue = new int[0];
        private static int[] Seen = new int[0];
        private static int Stamp;

        /// <summary>Bit 1: the cell under (ax, az) would get a finite ComputeFlowField distance from goals; bit 2: the cell under (bx, bz).</summary>
        public static int Reached(MapDef map, IReadOnlyList<PointXZ> goals, double ax, double az, double bx, double bz)
        {
            int W = map.Width, H = map.Height, n = W * H;
            byte[] cells = map.Cells;
            if (Queue.Length < n)
            {
                Queue = new int[n];
                Seen = new int[n];
                Stamp = 0;
            }
            if (Stamp == int.MaxValue)
            {
                Array.Fill(Seen, 0);
                Stamp = 0;
            }
            int stamp = ++Stamp;
            int ta = CellOf(map, ax, az), tb = CellOf(map, bx, bz);
            int want = (ta >= 0 ? 1 : 0) | (tb >= 0 ? 2 : 0), got = 0;
            if (want == 0) return 0;
            int head = 0, tail = 0;
            for (int g = 0; g < goals.Count; g++)
            {
                int gx = JsMath.FloorToInt(goals[g].X), gz = JsMath.FloorToInt(goals[g].Z);
                if (gx < 0 || gz < 0 || gx >= W || gz >= H) continue;
                int i = gz * W + gx;
                if (!MapGrid.IsWalkableType(cells[i]) || Seen[i] == stamp) continue;
                Seen[i] = stamp;
                Queue[tail++] = i;
            }
            while (head < tail)
            {
                int c = Queue[head++];
                if (c == ta) got |= 1;
                if (c == tb) got |= 2;
                if (got == want) return got;
                int cx = c % W, cz = c / W;
                if (cx + 1 < W) Visit(cells, c + 1, stamp, ref tail);
                if (cx > 0) Visit(cells, c - 1, stamp, ref tail);
                if (cz + 1 < H) Visit(cells, c + W, stamp, ref tail);
                if (cz > 0) Visit(cells, c - W, stamp, ref tail);
            }
            return got;
        }

        private static void Visit(byte[] cells, int i, int stamp, ref int tail)
        {
            if (Seen[i] == stamp || !MapGrid.IsWalkableType(cells[i])) return;
            Seen[i] = stamp;
            Queue[tail++] = i;
        }

        private static int CellOf(MapDef map, double x, double z)
        {
            int cx = JsMath.FloorToInt(x), cz = JsMath.FloorToInt(z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return -1;
            return cz * map.Width + cx;
        }
    }
}

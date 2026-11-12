using System;
using System.Collections.Generic;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Grid
{
    /// <summary>Uniform spatial hash, cell size 2 units, fixed-capacity arrays; Rebuild is a counting sort, QueryCircle an exact distance check (browser spatialHash.ts).</summary>
    public sealed class SpatialHash
    {
        public double CellSize;
        public int Cols;
        public int Rows;
        public int Capacity;

        /// <summary>CellStart[c] .. CellStart[c + 1] - 1 index into Items for cell c. Length Cols * Rows + 1.</summary>
        public int[] CellStart;

        /// <summary>Scratch per-cell counters during rebuild.</summary>
        public int[] CellFill;

        /// <summary>Actor ids sorted by cell.</summary>
        public int[] Items;

        /// <summary>Cell of each id from the last rebuild (-1 = not inserted).</summary>
        public int[] ItemCell;

        /// <summary>Positions referenced by the last rebuild (not copied).</summary>
        public double[] Xs;

        public double[] Zs;
        public int Count;

        public const double HashCellSize = 2;

        public static SpatialHash CreateSpatialHash(int width, int height, int capacity)
        {
            int cols = Math.Max(1, (int)Math.Ceiling(width / HashCellSize));
            int rows = Math.Max(1, (int)Math.Ceiling(height / HashCellSize));
            int[] itemCell = new int[capacity];
            Array.Fill(itemCell, -1);
            return new SpatialHash
            {
                CellSize = HashCellSize,
                Cols = cols,
                Rows = rows,
                Capacity = capacity,
                CellStart = new int[cols * rows + 1],
                CellFill = new int[cols * rows],
                Items = new int[capacity],
                ItemCell = itemCell,
                Xs = new double[0],
                Zs = new double[0],
                Count = 0,
            };
        }

        private static int CellOf(SpatialHash h, double x, double z)
        {
            int cx = JsMath.FloorToInt(x / h.CellSize);
            int cz = JsMath.FloorToInt(z / h.CellSize);
            if (cx < 0) cx = 0;
            else if (cx >= h.Cols) cx = h.Cols - 1;
            if (cz < 0) cz = 0;
            else if (cz >= h.Rows) cz = h.Rows - 1;
            return cz * h.Cols + cx;
        }

        /// <summary>Re-bucket ids 0..count-1 whose Alive flag is set. Positions outside the grid clamp to the edge cells.</summary>
        public static void Rebuild(SpatialHash h, double[] xs, double[] zs, IReadOnlyList<Actor> alive, int count)
        {
            int n = count > h.Capacity ? h.Capacity : count;
            h.Xs = xs;
            h.Zs = zs;
            h.Count = n;
            int cells = h.Cols * h.Rows;
            int[] start = h.CellStart;
            int[] fill = h.CellFill;
            Array.Fill(start, 0);
            for (int i = 0; i < n; i++)
            {
                if (!alive[i].Alive)
                {
                    h.ItemCell[i] = -1;
                    continue;
                }
                int c = CellOf(h, xs[i], zs[i]);
                h.ItemCell[i] = c;
                start[c + 1]++;
            }
            for (int c = 0; c < cells; c++) start[c + 1] += start[c];
            for (int c = 0; c < cells; c++) fill[c] = start[c];
            for (int i = 0; i < n; i++)
            {
                int c = h.ItemCell[i];
                if (c < 0) continue;
                h.Items[fill[c]++] = i;
            }
            for (int i = n; i < h.Capacity; i++) h.ItemCell[i] = -1;
        }

        /// <summary>Writes ids within distance r of (x, z) into output (up to output.Length). Returns the count written.</summary>
        public static int QueryCircle(SpatialHash h, double x, double z, double r, int[] output)
        {
            double cs = h.CellSize;
            int c0 = JsMath.FloorToInt((x - r) / cs);
            int c1 = JsMath.FloorToInt((x + r) / cs);
            int r0 = JsMath.FloorToInt((z - r) / cs);
            int r1 = JsMath.FloorToInt((z + r) / cs);
            if (c0 < 0) c0 = 0;
            if (r0 < 0) r0 = 0;
            if (c1 >= h.Cols) c1 = h.Cols - 1;
            if (r1 >= h.Rows) r1 = h.Rows - 1;
            // Points outside the grid were clamped into edge cells, so a query outside the grid still scans those edges.
            if (c0 >= h.Cols) c0 = h.Cols - 1;
            if (r0 >= h.Rows) r0 = h.Rows - 1;
            if (c1 < 0) c1 = 0;
            if (r1 < 0) r1 = 0;
            double r2 = r * r;
            int max = output.Length;
            int n = 0;
            for (int cz = r0; cz <= r1; cz++)
            {
                for (int cx = c0; cx <= c1; cx++)
                {
                    int c = cz * h.Cols + cx;
                    int end = h.CellStart[c + 1];
                    for (int k = h.CellStart[c]; k < end; k++)
                    {
                        int id = h.Items[k];
                        double dx = h.Xs[id] - x;
                        double dz = h.Zs[id] - z;
                        if (dx * dx + dz * dz <= r2)
                        {
                            if (n >= max) return n;
                            output[n++] = id;
                        }
                    }
                }
            }
            return n;
        }
    }
}

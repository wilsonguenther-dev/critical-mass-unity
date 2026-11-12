using System;
using System.Collections.Generic;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Grid
{
    /// <summary>Flow field: multi-source Dijkstra distance (in cells) from goal cells over walkable cells (browser flowfield.ts).</summary>
    public sealed class FlowField
    {
        /// <summary>Distance per cell; f32-rounded on store (browser Float32Array). Unreachable = +Infinity.</summary>
        public double[] Dist;

        /// <summary>Caller-defined version stamp (e.g. world.WallVersion it was built for).</summary>
        public int Version;

        /// <summary>Incremented once at the top of every ComputeFlowField call (read by F-05e's memo test).</summary>
        public static int ComputeCalls;

        // Neighbour offsets: 4 orthogonal then 4 diagonal.
        private static readonly int[] Ndx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Ndz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private static readonly double Sqrt2 = Math.Sqrt(2);
        private static readonly double Sqrt1_2 = Math.Sqrt(0.5);

        // Module-scope scratch, grown only when a bigger map appears.
        private static int[] Heap = new int[0]; // cell indices
        private static int[] HeapPos = new int[0]; // position of a cell in heap, -1 = not in heap
        private static double[] HeapKey = new double[0]; // tentative distance (double precision while relaxing)
        private static byte[] Closed = new byte[0];

        private static int HeapSize = 0;

        private static void EnsureScratch(int n)
        {
            if (Heap.Length >= n) return;
            Heap = new int[n];
            HeapPos = new int[n];
            HeapKey = new double[n];
            Closed = new byte[n];
        }

        private static void SiftUp(int p)
        {
            int c = Heap[p];
            double k = HeapKey[c];
            while (p > 0)
            {
                int parent = (p - 1) >> 1;
                int pc = Heap[parent];
                if (HeapKey[pc] < k || (HeapKey[pc] == k && pc < c)) break;
                Heap[p] = pc;
                HeapPos[pc] = p;
                p = parent;
            }
            Heap[p] = c;
            HeapPos[c] = p;
        }

        private static void SiftDown(int p)
        {
            int c = Heap[p];
            double k = HeapKey[c];
            for (;;)
            {
                int l = 2 * p + 1;
                if (l >= HeapSize) break;
                int r = l + 1;
                if (r < HeapSize)
                {
                    int lc = Heap[l];
                    int rc = Heap[r];
                    if (HeapKey[rc] < HeapKey[lc] || (HeapKey[rc] == HeapKey[lc] && rc < lc)) l = r;
                }
                int best = Heap[l];
                if (HeapKey[best] > k || (HeapKey[best] == k && best > c)) break;
                Heap[p] = best;
                HeapPos[best] = p;
                p = l;
            }
            Heap[p] = c;
            HeapPos[c] = p;
        }

        private static void PushOrDecrease(int c, double key)
        {
            int pos = HeapPos[c];
            if (pos < 0)
            {
                HeapKey[c] = key;
                Heap[HeapSize] = c;
                HeapPos[c] = HeapSize;
                HeapSize++;
                SiftUp(HeapSize - 1);
            }
            else if (key < HeapKey[c])
            {
                HeapKey[c] = key;
                SiftUp(pos);
            }
        }

        private static int PopMin()
        {
            int top = Heap[0];
            HeapPos[top] = -1;
            HeapSize--;
            if (HeapSize > 0)
            {
                Heap[0] = Heap[HeapSize];
                HeapPos[Heap[0]] = 0;
                SiftDown(0);
            }
            return top;
        }

        /// <summary>Dijkstra distance from the nearest goal to every walkable cell; unwalkable or out-of-map goals are ignored; reuses output when its size matches.</summary>
        public static FlowField ComputeFlowField(MapDef map, IReadOnlyList<PointXZ> goals, FlowField output = null, int version = 0)
        {
            ComputeCalls++;
            int W = map.Width;
            int H = map.Height;
            int n = W * H;
            byte[] cells = map.Cells;
            FlowField field = output != null && output.Dist.Length == n ? output : new FlowField { Dist = new double[n], Version = 0 };
            field.Version = version;
            double[] dist = field.Dist;
            Array.Fill(dist, double.PositiveInfinity);
            EnsureScratch(n);
            Array.Fill(HeapPos, -1, 0, n);
            Array.Fill(Closed, (byte)0, 0, n);
            HeapSize = 0;

            for (int g = 0; g < goals.Count; g++)
            {
                int gx = JsMath.FloorToInt(goals[g].X);
                int gz = JsMath.FloorToInt(goals[g].Z);
                if (gx < 0 || gz < 0 || gx >= W || gz >= H) continue;
                int i = gz * W + gx;
                if (!MapGrid.IsWalkableType(cells[i])) continue;
                PushOrDecrease(i, 0);
            }

            while (HeapSize > 0)
            {
                int c = PopMin();
                Closed[c] = 1;
                double d = HeapKey[c];
                dist[c] = JsMath.F32(d);
                int cx = c % W;
                int cz = (c - cx) / W; // exact: c - cx is a multiple of W
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Ndx[k];
                    int nz = cz + Ndz[k];
                    if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                    int ni = nz * W + nx;
                    if (Closed[ni] != 0 || !MapGrid.IsWalkableType(cells[ni])) continue;
                    double step = 1;
                    if (k >= 4)
                    {
                        // No corner cutting: both orthogonal cells must be walkable.
                        if (!MapGrid.IsWalkableType(cells[cz * W + nx]) || !MapGrid.IsWalkableType(cells[nz * W + cx])) continue;
                        step = Sqrt2;
                    }
                    PushOrDecrease(ni, d + step);
                }
            }
            return field;
        }

        /// <summary>Unit direction from the cell containing (x, z) toward its lowest-distance neighbour; (0, 0) and false at a goal or when no neighbour is closer.</summary>
        public static bool SampleFlowDir(FlowField field, MapDef map, double x, double z, out double dx, out double dz)
        {
            int W = map.Width;
            int H = map.Height;
            byte[] cells = map.Cells;
            int cx = JsMath.FloorToInt(x);
            int cz = JsMath.FloorToInt(z);
            dx = 0;
            dz = 0;
            if (cx < 0 || cz < 0 || cx >= W || cz >= H) return false;
            double here = field.Dist[cz * W + cx];
            double best = here;
            int bk = -1;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + Ndx[k];
                int nz = cz + Ndz[k];
                if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                double d = field.Dist[nz * W + nx];
                if (!(d < best)) continue;
                if (k >= 4 && (!MapGrid.IsWalkableType(cells[cz * W + nx]) || !MapGrid.IsWalkableType(cells[nz * W + cx]))) continue;
                best = d;
                bk = k;
            }
            if (bk < 0) return false;
            if (bk < 4)
            {
                dx = Ndx[bk];
                dz = Ndz[bk];
            }
            else
            {
                dx = Ndx[bk] * Sqrt1_2;
                dz = Ndz[bk] * Sqrt1_2;
            }
            return true;
        }
    }
}

using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Bots
{
    /// <summary>Shared bot flow fields (CM-09): one Dijkstra distance field per goal, rebuilt on WallVersion change only, at most one per MaintainFields call. Port of src/bots/fields.ts.</summary>
    public sealed class BotFields
    {
        public const int FieldHill = 0, FieldMinerHill = 1, FieldShrine = 2, FieldPadAzure = 3, FieldPadCrimson = 4, FieldCorruption = 5, FieldNode0 = 6;

        /// <summary>Extra cost (cells) for a miner to path through one BREAKABLE (intact or cracked) cell (CM-09 spec).</summary>
        public const double MinerBreakableCost = 6;

        /// <summary>Shrine goal: walkable cells whose centre is within this of the shrine centre (inside shrineInteractRange 3).</summary>
        public const double ShrineGoalRadius = 2.5;

        /// <summary>Node goal: cells whose centre is within this of the node point (the four cells touching it, nodes sit on corners).</summary>
        private const double NodeGoalRadius = 1.2;

        private const int MaxGoals = 256;

        public int Count;
        public int Width;
        public int Height;

        /// <summary>Distance in cells to the nearest goal cell; +Infinity = unreachable. One array per field, f32-rounded on store (browser Float32Array).</summary>
        public double[][] Dist;

        /// <summary>world.WallVersion each field was built for (-1 = never built).</summary>
        public int[] Version;

        /// <summary>Goal cell indices per field (computed at reset; a goal on a non-passable cell is skipped at build time).</summary>
        public int[][] Goals;

        public int[] GoalCount;

        /// <summary>1 when the field lets BREAKABLE cells through at +MinerBreakableCost (minerHill only).</summary>
        public byte[] Miner;

        /// <summary>Field index for world node id (biomass node -> its own field, corruption node -> FieldCorruption).</summary>
        public int[] FieldOfNode;

        /// <summary>Round-robin cursor for spreading rebuilds.</summary>
        public int Cursor;

        /// <summary>Total Dijkstra builds since creation (tests read it).</summary>
        public int Builds;

        /// <summary>world.Tick of the last single rebuild done by MaintainFields (-1 = none).</summary>
        public int LastRebuildTick;

        /// <summary>The hill radius the bots aim for when holding (kept inside hill.r so the actor counts for hillSystem).</summary>
        public static double HoldRadius(GameConfig c) => c.Hill.Radius - 1.5;

        public static BotFields CreateFields(World w)
        {
            MapDef map = w.Map;
            int n = map.Width * map.Height;
            int biomass = 0;
            foreach (ResourceNode node in w.Nodes) if (node.Kind == NodeKind.Biomass) biomass++;
            int count = FieldNode0 + biomass;
            double[][] dist = new double[count][];
            int[][] goals = new int[count][];
            for (int f = 0; f < count; f++)
            {
                double[] d = new double[n];
                Array.Fill(d, double.PositiveInfinity);
                dist[f] = d;
                goals[f] = new int[MaxGoals];
            }
            byte[] miner = new byte[count];
            miner[FieldMinerHill] = 1;
            int[] version = new int[count];
            Array.Fill(version, -1);
            int[] fieldOfNode = new int[w.Nodes.Length];
            Array.Fill(fieldOfNode, -1);
            return new BotFields
            {
                Count = count,
                Width = map.Width,
                Height = map.Height,
                Dist = dist,
                Version = version,
                Goals = goals,
                GoalCount = new int[count],
                Miner = miner,
                FieldOfNode = fieldOfNode,
                Cursor = 0,
                Builds = 0,
                LastRebuildTick = -1,
            };
        }

        private static void AddDiscGoals(BotFields f, int field, MapDef map, double x, double z, double r)
        {
            int W = map.Width;
            double r2 = r * r;
            for (int cz = JsMath.FloorToInt(z - r); cz <= JsMath.FloorToInt(z + r); cz++)
            {
                if (cz < 0 || cz >= map.Height) continue;
                for (int cx = JsMath.FloorToInt(x - r); cx <= JsMath.FloorToInt(x + r); cx++)
                {
                    if (cx < 0 || cx >= W) continue;
                    double dx = cx + 0.5 - x;
                    double dz = cz + 0.5 - z;
                    if (dx * dx + dz * dz > r2) continue;
                    int k = f.GoalCount[field];
                    if (k >= MaxGoals) return;
                    f.Goals[field][k] = cz * W + cx;
                    f.GoalCount[field] = k + 1;
                }
            }
        }

        /// <summary>Recompute every goal list (the geometry is static per match) and mark every field stale. Allocation-free.</summary>
        public static void ResetFields(BotFields f, World w)
        {
            MapDef map = w.Map;
            Array.Fill(f.GoalCount, 0);
            Array.Fill(f.Version, -1);
            f.Cursor = 0;
            f.LastRebuildTick = -1;
            double hr = Math.Max(0.5, map.Hill.R - 1);
            AddDiscGoals(f, FieldHill, map, map.Hill.X, map.Hill.Z, hr);
            AddDiscGoals(f, FieldMinerHill, map, map.Hill.X, map.Hill.Z, hr);
            AddDiscGoals(f, FieldShrine, map, map.Shrine.X, map.Shrine.Z, ShrineGoalRadius);
            PadDef azure = map.Pads[(int)TeamId.Azure];
            PadDef crimson = map.Pads[(int)TeamId.Crimson];
            AddDiscGoals(f, FieldPadAzure, map, azure.X, azure.Z, azure.R);
            AddDiscGoals(f, FieldPadCrimson, map, crimson.X, crimson.Z, crimson.R);
            int bio = 0;
            for (int i = 0; i < w.Nodes.Length; i++)
            {
                ResourceNode node = w.Nodes[i];
                if (node.Kind == NodeKind.Biomass)
                {
                    int field = FieldNode0 + bio++;
                    if (field >= f.Count)
                    {
                        f.FieldOfNode[i] = -1;
                        continue;
                    }
                    f.FieldOfNode[i] = field;
                    AddDiscGoals(f, field, map, node.X, node.Z, NodeGoalRadius);
                }
                else
                {
                    f.FieldOfNode[i] = FieldCorruption;
                    AddDiscGoals(f, FieldCorruption, map, node.X, node.Z, NodeGoalRadius);
                }
            }
        }

        /// <summary>Passable for bot pathing: walkable, or BARRICADE (avoided locally), or BREAKABLE for a miner field.</summary>
        private static bool Passable(byte t, bool miner)
        {
            return t == Cell.Floor
                || t == Cell.BreakableDestroyed
                || t == Cell.Bridge
                || t == Cell.Barricade
                || (miner && (t == Cell.BreakableIntact || t == Cell.BreakableCracked));
        }

        private static bool IsBreakable(byte t)
        {
            return t == Cell.BreakableIntact || t == Cell.BreakableCracked;
        }

        private static readonly int[] Ndx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Ndz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        // Module-scope Dijkstra scratch (indexed binary heap), grown only for a bigger map.
        private static int[] Heap = new int[0];
        private static int[] HeapPos = new int[0];
        private static double[] HeapKey = new double[0];
        private static int HeapSize = 0;

        private static void EnsureScratch(int n)
        {
            if (Heap.Length >= n) return;
            Heap = new int[n];
            HeapPos = new int[n];
            HeapKey = new double[n];
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

        private static void Relax(int c, double key)
        {
            int pos = HeapPos[c];
            if (pos == -1)
            {
                HeapKey[c] = key;
                Heap[HeapSize] = c;
                HeapPos[c] = HeapSize;
                HeapSize++;
                SiftUp(HeapSize - 1);
            }
            else if (pos >= 0 && key < HeapKey[c])
            {
                HeapKey[c] = key;
                SiftUp(pos);
            }
        }

        private static int PopMin()
        {
            int top = Heap[0];
            HeapPos[top] = -2; // closed
            HeapSize--;
            if (HeapSize > 0)
            {
                Heap[0] = Heap[HeapSize];
                HeapPos[Heap[0]] = 0;
                SiftDown(0);
            }
            return top;
        }

        /// <summary>Full Dijkstra for one field on the current grid, stamped with world.WallVersion. Allocation-free.</summary>
        public static void BuildField(BotFields f, World w, int field)
        {
            MapDef map = w.Map;
            int W = map.Width;
            int H = map.Height;
            int n = W * H;
            byte[] cells = map.Cells;
            bool miner = f.Miner[field] == 1;
            double[] dist = f.Dist[field];
            Array.Fill(dist, double.PositiveInfinity);
            EnsureScratch(n);
            Array.Fill(HeapPos, -1, 0, n);
            HeapSize = 0;
            int[] goals = f.Goals[field];
            for (int g = 0; g < f.GoalCount[field]; g++)
            {
                int i = goals[g];
                if (Passable(cells[i], miner)) Relax(i, 0);
            }
            while (HeapSize > 0)
            {
                int c = PopMin();
                double d = HeapKey[c];
                dist[c] = JsMath.F32(d);
                int cx = c % W;
                int cz = (int)((double)(c - cx) / W);
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Ndx[k];
                    int nz = cz + Ndz[k];
                    if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                    int ni = nz * W + nx;
                    if (HeapPos[ni] == -2) continue;
                    byte t = cells[ni];
                    if (!Passable(t, miner)) continue;
                    double step = 1;
                    if (k >= 4)
                    {
                        if (!Passable(cells[cz * W + nx], miner) || !Passable(cells[nz * W + cx], miner)) continue;
                        step = Math.Sqrt(2.0);
                    }
                    // Cost is paid for leaving a breakable cell toward the goal (equals paying it on entry along any path).
                    if (miner && IsBreakable(cells[c])) step += MinerBreakableCost;
                    Relax(ni, d + step);
                }
            }
            f.Version[field] = w.WallVersion;
            f.Builds++;
        }

        /// <summary>Build every field now (used once at reset, when nothing is usable yet).</summary>
        public static void BuildAllFields(BotFields f, World w)
        {
            for (int k = 0; k < f.Count; k++) BuildField(f, w, k);
        }

        /// <summary>Rebuild at most ONE stale field (Version != world.WallVersion), round-robin from the cursor. Returns the rebuilt field index or -1.</summary>
        public static int MaintainFields(BotFields f, World w)
        {
            int wv = w.WallVersion;
            for (int s = 0; s < f.Count; s++)
            {
                int k = (f.Cursor + s) % f.Count;
                if (f.Version[k] == wv) continue;
                BuildField(f, w, k);
                f.Cursor = (k + 1) % f.Count;
                f.LastRebuildTick = w.Tick;
                return k;
            }
            return -1;
        }

        /// <summary>Number of fields whose version differs from world.WallVersion.</summary>
        public static int StaleFieldCount(BotFields f, World w)
        {
            int s = 0;
            for (int k = 0; k < f.Count; k++) if (f.Version[k] != w.WallVersion) s++;
            return s;
        }

        /// <summary>Distance of field <c>field</c> at the cell containing (x, z); +Infinity outside the map or unreachable.</summary>
        public static double FieldDistAt(BotFields f, int field, double x, double z)
        {
            int cx = JsMath.FloorToInt(x);
            int cz = JsMath.FloorToInt(z);
            if (cx < 0 || cz < 0 || cx >= f.Width || cz >= f.Height) return double.PositiveInfinity;
            return f.Dist[field][cz * f.Width + cx];
        }

        /// <summary>Next step down field <c>field</c> from (x, z): the lowest-distance neighbour (no corner cutting), aimed at its centre. False at a goal or a local minimum (step is then (0, 0, -1)).</summary>
        public static bool FlowStepAt(BotFields f, World w, int field, double x, double z, out FlowStep step)
        {
            step.X = 0;
            step.Z = 0;
            step.Cell = -1;
            int W = f.Width;
            int H = f.Height;
            int cx = JsMath.FloorToInt(x);
            int cz = JsMath.FloorToInt(z);
            if (cx < 0 || cz < 0 || cx >= W || cz >= H) return false;
            double[] dist = f.Dist[field];
            byte[] cells = w.Map.Cells;
            bool miner = f.Miner[field] == 1;
            double here = dist[cz * W + cx];
            double best = here;
            int bk = -1;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + Ndx[k];
                int nz = cz + Ndz[k];
                if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                double d = dist[nz * W + nx];
                if (!(d < best)) continue;
                if (k >= 4 && (!Passable(cells[cz * W + nx], miner) || !Passable(cells[nz * W + cx], miner))) continue;
                best = d;
                bk = k;
            }
            if (bk < 0) return false;
            int bx = cx + Ndx[bk];
            int bz = cz + Ndz[bk];
            double dx = bx + 0.5 - x;
            double dz = bz + 0.5 - z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6)
            {
                dx = Ndx[bk];
                dz = Ndz[bk];
            }
            double inv = 1 / Math.Sqrt(dx * dx + dz * dz);
            step.X = dx * inv;
            step.Z = dz * inv;
            step.Cell = bz * W + bx;
            return true;
        }
    }

    /// <summary>One step down a bot field: unit direction toward the next cell's centre, and that cell's index (-1 when none).</summary>
    public struct FlowStep
    {
        public double X;
        public double Z;
        public int Cell;
    }
}

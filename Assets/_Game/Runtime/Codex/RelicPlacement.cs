using System.Collections.Generic;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.Codex
{
    /// <summary>
    /// The deterministic placement of the 8 Basin Relics (DG-25), computed from the sim map alone, and the check the
    /// acceptance row asks for. Read-only on the map; the result depends on nothing but the cells.
    /// <para><b>Main routes</b> are every cell on a shortest walkable path (4-connected breadth-first search, fixed
    /// neighbour order) from each spawn pad and from the hill to the hill, the shrine, both pads and every resource
    /// node; every bridge cell; and the objective areas (pads + 1, hill + 1, shrine yard, 2.5 around each node).</para>
    /// <para>A relic cell must be FLOOR, reachable on foot from the Azure pad, at least MinRouteDistance cells from any
    /// route cell (Chebyshev distance, so no route cell in the 5x5 square around it) and clear of blocking cells by
    /// ClearanceRadius. Relics come in mirrored pairs (the map is mirrored across x = 56): four western picks by
    /// farthest-point sampling (first the cell farthest from a route, then each next the cell farthest from every pick
    /// and its mirror; ties: farther from a route, then lower cell index), each followed by its mirror.</para>
    /// <para>The secret Crown of the Hill (DG-26) sits on the Azure overlook ledge, which no walk reaches (TryCrownSpot).</para>
    /// </summary>
    public static class RelicPlacement
    {
        public const int Count = 8;
        public const int MinRouteDistance = 3;
        public const double ClearanceRadius = 1.0;
        /// <summary>Centre of the Azure overlook ledge (SkybreakBasinMap.Overlook).</summary>
        public const double OverlookX = 11, OverlookZ = 18;

        public struct Spot
        {
            public int Cx, Cz;
            public double X => Cx + 0.5;
            public double Z => Cz + 0.5;
            public override string ToString() => "(" + Cx + ", " + Cz + ")";
        }

        static readonly int[] Dx4 = { 1, -1, 0, 0 };
        static readonly int[] Dz4 = { 0, 0, 1, -1 };

        /// <summary>The 8 relic cells, pairs (west, mirror) in order; fewer only if the map has too few candidates.</summary>
        public static Spot[] Place(MapDef map)
        {
            bool[] route = RouteCells(map);
            int[] dist = RouteDistance(map, route);
            int[] reach = Reach(map, StartCell(map, 0));
            int w = map.Width;
            var west = new List<int>();
            for (int i = 0; i < map.Cells.Length; i++)
            {
                int cx = i % w, cz = i / w;
                if (cx >= w / 2) continue;
                if (Candidate(map, dist, reach, i) && Candidate(map, dist, reach, Mirror(map, i))) west.Add(i);
            }
            var chosen = new List<int>();
            var result = new List<Spot>();
            for (int k = 0; k < Count / 2 && west.Count > 0; k++)
            {
                int best = -1;
                long bestMin = -1;
                int bestDist = -1;
                for (int n = 0; n < west.Count; n++)
                {
                    int i = west[n];
                    if (chosen.Contains(i)) continue;
                    long md = long.MaxValue;
                    if (chosen.Count == 0) md = 0;
                    for (int c = 0; c < chosen.Count; c++)
                    {
                        md = System.Math.Min(md, D2(map, i, chosen[c]));
                        md = System.Math.Min(md, D2(map, i, Mirror(map, chosen[c])));
                    }
                    if (chosen.Count > 0) md = System.Math.Min(md, D2(map, i, Mirror(map, i)));
                    bool better = md > bestMin || (md == bestMin && dist[i] > bestDist) || (md == bestMin && dist[i] == bestDist && i < best);
                    if (best < 0 || better)
                    {
                        best = i;
                        bestMin = md;
                        bestDist = dist[i];
                    }
                }
                if (best < 0) break;
                chosen.Add(best);
                result.Add(ToSpot(map, best));
                result.Add(ToSpot(map, Mirror(map, best)));
            }
            return result.ToArray();
        }

        /// <summary>The acceptance check: one message per broken rule (empty = every relic is placed right).</summary>
        public static List<string> Check(MapDef map, Spot[] spots)
        {
            var errors = new List<string>();
            if (spots == null || spots.Length != Count)
            {
                errors.Add("expected " + Count + " relics, got " + (spots == null ? 0 : spots.Length));
                if (spots == null) return errors;
            }
            bool[] route = RouteCells(map);
            int[] dist = RouteDistance(map, route);
            int[] reach = Reach(map, StartCell(map, 0));
            var seen = new HashSet<int>();
            foreach (Spot s in spots)
            {
                int i = MapGrid.CellIndex(map, s.Cx, s.Cz);
                if (i < 0) { errors.Add(s + " is off the map"); continue; }
                if (!seen.Add(i)) errors.Add(s + " is used twice");
                if (!MapGrid.IsWalkable(map, s.Cx, s.Cz)) errors.Add(s + " is not walkable");
                else if (map.Cells[i] != Cell.Floor) errors.Add(s + " is not plain floor (cell type " + map.Cells[i] + ")");
                if (reach[i] < 0) errors.Add(s + " cannot be reached on foot from the Azure pad");
                if (dist[i] < MinRouteDistance) errors.Add(s + " is " + dist[i] + " cells from a main route (needs " + MinRouteDistance + ")");
                if (!MapGrid.ClearanceOk(map, s.Cx, s.Cz, ClearanceRadius)) errors.Add(s + " is too close to a wall or an edge");
            }
            return errors;
        }

        /// <summary>The overlook-ledge cell for the Crown of the Hill: floor no walk from the Azure pad reaches. False when there is none.</summary>
        public static bool TryCrownSpot(MapDef map, out Spot spot)
        {
            spot = default;
            int[] reach = Reach(map, StartCell(map, 0));
            double best = double.MaxValue;
            bool found = false;
            for (int cz = 0; cz < map.Height; cz++)
            {
                for (int cx = 0; cx < map.Width; cx++)
                {
                    int i = cz * map.Width + cx;
                    if (map.Cells[i] != Cell.Floor || reach[i] >= 0) continue;
                    double dx = cx + 0.5 - OverlookX, dz = cz + 0.5 - OverlookZ;
                    double d = dx * dx + dz * dz;
                    if (d > 4.0 || d >= best || !MapGrid.ClearanceOk(map, cx, cz, 0.75)) continue;
                    best = d;
                    spot = new Spot { Cx = cx, Cz = cz };
                    found = true;
                }
            }
            return found;
        }

        /// <summary>True on main-route cells (see the class notes).</summary>
        public static bool[] RouteCells(MapDef map)
        {
            int n = map.Cells.Length, w = map.Width;
            var route = new bool[n];
            var targets = new List<PointXZ> { new PointXZ(map.Hill.X, map.Hill.Z), new PointXZ(map.Shrine.X, map.Shrine.Z) };
            if (map.Pads != null) foreach (PadDef p in map.Pads) targets.Add(new PointXZ(p.X, p.Z));
            if (map.BiomassNodes != null) targets.AddRange(map.BiomassNodes);
            if (map.CorruptionNodes != null) targets.AddRange(map.CorruptionNodes);

            var sources = new List<int>();
            if (map.Pads != null) for (int t = 0; t < map.Pads.Length; t++) sources.Add(StartCell(map, t));
            sources.Add(CellOf(map, map.Hill.X, map.Hill.Z));
            foreach (int s in sources)
            {
                if (s < 0) continue;
                int[] parent = Reach(map, s);
                foreach (PointXZ t in targets)
                {
                    int best = -1;
                    double bd = double.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (parent[i] < 0) continue;
                        double dx = i % w + 0.5 - t.X, dz = i / w + 0.5 - t.Z;
                        double d = dx * dx + dz * dz;
                        if (d < bd) { bd = d; best = i; }
                    }
                    // Trace the whole path back to this source (paths from other sources may share cells).
                    for (int i = best; i >= 0; i = parent[i] == i ? -1 : parent[i]) route[i] = true;
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (map.Cells[i] == Cell.Bridge) { route[i] = true; continue; }
                double x = i % w + 0.5, z = i / w + 0.5;
                if (Near(x, z, map.Hill.X, map.Hill.Z, map.Hill.R + 1)) route[i] = true;
                else if (Near(x, z, map.Shrine.X, map.Shrine.Z, 6)) route[i] = true;
                else if (map.Pads != null && (Near(x, z, map.Pads[0].X, map.Pads[0].Z, map.Pads[0].R + 1) ||
                                              (map.Pads.Length > 1 && Near(x, z, map.Pads[1].X, map.Pads[1].Z, map.Pads[1].R + 1)))) route[i] = true;
                else if (NearAny(x, z, map.BiomassNodes, 2.5) || NearAny(x, z, map.CorruptionNodes, 2.5)) route[i] = true;
            }
            return route;
        }

        /// <summary>Chebyshev distance in cells from every cell to the nearest route cell (int.MaxValue when there is none).</summary>
        public static int[] RouteDistance(MapDef map, bool[] route)
        {
            int n = map.Cells.Length, w = map.Width, h = map.Height;
            var dist = new int[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                dist[i] = route[i] ? 0 : int.MaxValue;
                if (route[i]) queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, z = i / w;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        int j = nz * w + nx;
                        if (dist[j] > dist[i] + 1)
                        {
                            dist[j] = dist[i] + 1;
                            queue.Enqueue(j);
                        }
                    }
                }
            }
            return dist;
        }

        /// <summary>Breadth-first search over walkable cells from <paramref name="start"/>: parent index per cell (the start is its own parent), -1 = unreached.</summary>
        public static int[] Reach(MapDef map, int start)
        {
            int n = map.Cells.Length, w = map.Width;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = -1;
            if (start < 0 || !MapGrid.IsWalkable(map, start % w, start / w)) return parent;
            var queue = new Queue<int>();
            parent[start] = start;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, z = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx4[k], nz = z + Dz4[k];
                    if (!MapGrid.IsWalkable(map, nx, nz)) continue;
                    int j = nz * w + nx;
                    if (parent[j] >= 0) continue;
                    parent[j] = i;
                    queue.Enqueue(j);
                }
            }
            return parent;
        }

        /// <summary>The cell of team <paramref name="team"/>'s pad centre (-1 when the map has no such pad).</summary>
        public static int StartCell(MapDef map, int team)
        {
            if (map.Pads == null || team < 0 || team >= map.Pads.Length) return -1;
            return CellOf(map, map.Pads[team].X, map.Pads[team].Z);
        }

        static int CellOf(MapDef map, double x, double z)
        {
            return MapGrid.WorldToCell(map, x, z, out int cx, out int cz) ? cz * map.Width + cx : -1;
        }

        static bool Candidate(MapDef map, int[] dist, int[] reach, int i)
        {
            if (i < 0 || i >= map.Cells.Length) return false;
            if (map.Cells[i] != Cell.Floor || reach[i] < 0 || dist[i] < MinRouteDistance) return false;
            return MapGrid.ClearanceOk(map, i % map.Width, i / map.Width, ClearanceRadius);
        }

        static int Mirror(MapDef map, int i) => (i / map.Width) * map.Width + (map.Width - 1 - i % map.Width);

        static long D2(MapDef map, int a, int b)
        {
            long dx = a % map.Width - b % map.Width, dz = a / map.Width - b / map.Width;
            return dx * dx + dz * dz;
        }

        static Spot ToSpot(MapDef map, int i) => new Spot { Cx = i % map.Width, Cz = i / map.Width };

        static bool Near(double x, double z, double px, double pz, double r)
        {
            double dx = x - px, dz = z - pz;
            return dx * dx + dz * dz <= r * r;
        }

        static bool NearAny(double x, double z, PointXZ[] pts, double r)
        {
            if (pts == null) return false;
            for (int k = 0; k < pts.Length; k++) if (Near(x, z, pts[k].X, pts[k].Z, r)) return true;
            return false;
        }
    }
}

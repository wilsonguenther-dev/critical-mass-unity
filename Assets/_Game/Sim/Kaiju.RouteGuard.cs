using System;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// b14-c item 2 (the Kaiju idle bug): the KAIJU ROUTE GUARD. The CM-30 route guard (Terraform.CellsKeepRoutes: every dig,
    /// TNT crater, topple, built-piece change and bridge collapse) proved that the PADS still reach the hill and the shrine on
    /// the actors' grid, never that the Kaiju's BODY can still walk from its hatch spot (or from where it stands) to the hill.
    /// Measured (40-seed scan, diag 44156825103 / 44157934512): on 16v16 seeds 1 and 25 bot TNT craters at the shrine
    /// bridge's two mouths (cells (55..57, 15..16) and (53..54, 25..26), the bridge cell (54, 25) blown) left the actors a
    /// one-cell lane and the Kaiju none: it hatched into a pocket region with no fighter and no barricade to claw, PickSiege
    /// fell back to its own cell, and it stood with no target for the whole 240 s it was owned. Seed 19: craters round the
    /// hill split it from the hill for 23 s. The guard now also refuses a change that would newly split the Kaiju's clearance
    /// region (crush rule: barricades and short walls pass) from the hill's. Full profile only; a pure function of the grid
    /// (its own shadow copy, never the Kaiju's nav state, so a view calling the route proof cannot move the Sim); allocation-free
    /// after the first call; it runs only when a change makes a cell impassable for the body (a hole), so walls cost nothing.
    /// </summary>
    public sealed partial class Kaiju
    {
        private sealed class RouteShadow
        {
            public byte[] Built, Cells, Sq, Tent, TentSq;
            public int[] Visit, Queue;
            public int Stamp;
            public bool Valid;
            public MapDef Map;
            public double R, Sr;
            public int Halo;
            public bool Squeeze;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, RouteShadow> RouteShadows =
            new System.Runtime.CompilerServices.ConditionalWeakTable<World, RouteShadow>();

        /// <summary>The Kaiju route guard applies: Full profile, the nav guard and the hill-siege crush rule on.</summary>
        internal static bool RouteGuardOn(World w) =>
            DepthRules.On(w, true) && Phys.On(w, w.Config.Physics.KaijuNavGuard) && Phys.On(w, w.Config.Physics.KaijuHillSiege);

        private static bool PassableType(byte t) => MapGrid.IsWalkableType(t) || t == Cell.Barricade || t == Cell.LowWall;

        private static RouteShadow ShadowFor(World w)
        {
            int n = w.Map.Width * w.Map.Height;
            if (!RouteShadows.TryGetValue(w, out RouteShadow s) || s.Built.Length != n)
            {
                if (s != null) RouteShadows.Remove(w);
                s = new RouteShadow { Built = new byte[n], Cells = new byte[n], Sq = new byte[n], Tent = new byte[n], TentSq = new byte[n], Visit = new int[n], Queue = new int[n] };
                RouteShadows.Add(w, s);
            }
            return s;
        }

        /// <summary>Brings the guard's own shadow clearance grid (s.Cells) up to the real grid (the RefreshClearance rule, own arrays).</summary>
        private static void SyncShadow(World w, RouteShadow s, double r, double sr, int halo, bool squeeze)
        {
            MapDef map = w.Map;
            byte[] real = map.Cells;
            int W = map.Width, H = map.Height, n = W * H;
            if (!s.Valid || !ReferenceEquals(s.Map, map) || s.R != r || s.Sr != sr || s.Halo != halo || s.Squeeze != squeeze)
            {
                for (int cz = 0; cz < H; cz++)
                    for (int cx = 0; cx < W; cx++)
                        ClearCellAt(w, map, cx, cz, r, true, squeeze, sr, halo, s.Cells, s.Sq);
                Array.Copy(real, s.Built, n);
                s.Map = map; s.R = r; s.Sr = sr; s.Halo = halo; s.Squeeze = squeeze; s.Valid = true;
                return;
            }
            int reach = ClearReach(r, sr, halo);
            for (int i = 0; i < n; i++)
            {
                if (s.Built[i] == real[i]) continue;
                int ccx = i % W, ccz = i / W;
                for (int cz = Math.Max(0, ccz - reach); cz <= Math.Min(H - 1, ccz + reach); cz++)
                    for (int cx = Math.Max(0, ccx - reach); cx <= Math.Min(W - 1, ccx + reach); cx++)
                        ClearCellAt(w, map, cx, cz, r, true, squeeze, sr, halo, s.Cells, s.Sq);
                s.Built[i] = real[i];
            }
        }

        /// <summary>Nearest walkable cell of `cells` to (x, z) within GoalSnapRings (first minimum wins); -1 none.</summary>
        private static int Snap(MapDef map, byte[] cells, double x, double z)
        {
            int ox = JsMath.FloorToInt(x), oz = JsMath.FloorToInt(z), W = map.Width;
            for (int ring = 0; ring <= GoalSnapRings; ring++)
            {
                double best = double.PositiveInfinity;
                int bi = -1;
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                        int cx = ox + dx, cz = oz + dz;
                        if (cx < 0 || cz < 0 || cx >= W || cz >= map.Height || !MapGrid.IsWalkableType(cells[cz * W + cx])) continue;
                        double ex = cx + 0.5 - x, ez = cz + 0.5 - z, d2 = ex * ex + ez * ez;
                        if (d2 < best) { best = d2; bi = cz * W + cx; }
                    }
                if (bi >= 0) return bi;
            }
            return -1;
        }

        private static bool Clear(MapDef map, byte[] cells, int cx, int cz) =>
            cx >= 0 && cz >= 0 && cx < map.Width && cz < map.Height && MapGrid.IsWalkableType(cells[cz * map.Width + cx]);

        /// <summary>Breadth-first over `cells` (8-connected, no corner cutting: BuildRegions' rule) from cell a; true when it reaches b.</summary>
        private static bool Connected(MapDef map, RouteShadow s, byte[] cells, int a, int b)
        {
            if (a < 0 || b < 0) return false;
            if (a == b) return true;
            if (s.Stamp == int.MaxValue) { Array.Fill(s.Visit, 0); s.Stamp = 0; }
            int stamp = ++s.Stamp, W = map.Width, head = 0, tail = 0;
            s.Queue[tail++] = a;
            s.Visit[a] = stamp;
            while (head < tail)
            {
                int c = s.Queue[head++], cx = c % W, cz = c / W;
                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + Ndx[i], nz = cz + Ndz[i];
                    if (!Clear(map, cells, nx, nz)) continue;
                    int ni = nz * W + nx;
                    if (s.Visit[ni] == stamp) continue;
                    if (i >= 4 && (!Clear(map, cells, nx, cz) || !Clear(map, cells, cx, nz))) continue;
                    if (ni == b) return true;
                    s.Visit[ni] = stamp;
                    s.Queue[tail++] = ni;
                }
            }
            return false;
        }

        /// <summary>
        /// Called by Terraform.CellsKeepRoutes with the tentative cells ALREADY written into map.Cells (idx[k] = next[k], k &lt; n)
        /// and their previous values in old[]: false = the change would newly cut the Kaiju's body off from the hill (from where
        /// it stands once hatched, from its hatch spot before). A body already cut off is not this change's doing (true).
        /// </summary>
        internal static bool TentativeKeepsKaijuRoute(World w, int[] idx, byte[] next, byte[] old, int n)
        {
            if (!RouteGuardOn(w)) return true;
            bool narrows = false;
            for (int k = 0; k < n && !narrows; k++) if (PassableType(old[k]) && !PassableType(next[k])) narrows = true;
            if (!narrows) return true;
            MapDef map = w.Map;
            byte[] cells = map.Cells;
            RouteShadow s = ShadowFor(w);
            double r = GridR(w), sr = w.Config.Physics.KaijuSqueezeRadius;
            int halo = w.Config.Physics.KaijuSqueezeHalo;
            bool squeeze = SqueezeOn(w);
            // The shadow BEFORE the change: sync it with the tentative cells put back.
            for (int k = 0; k < n; k++) cells[idx[k]] = old[k];
            SyncShadow(w, s, r, sr, halo, squeeze);
            for (int k = 0; k < n; k++) cells[idx[k]] = next[k];
            Kaiju kj = w.Kaiju;
            bool out_ = w.Match.Hatched && kj.State != KaijuState.Egg;
            double sx = out_ ? kj.X : map.Shrine.X, sz = out_ ? kj.Z : map.Shrine.Z + HatchOffsetZ;
            HillDef hill = map.Hill;
            if (!Connected(map, s, s.Cells, Snap(map, s.Cells, sx, sz), Snap(map, s.Cells, hill.X, hill.Z))) return true;
            // The shadow AFTER: a copy patched around the changed cells from the tentative grid.
            int N = s.Cells.Length, W = map.Width, H = map.Height;
            Array.Copy(s.Cells, s.Tent, N);
            Array.Copy(s.Sq, s.TentSq, N);
            int reach = ClearReach(r, sr, halo);
            for (int k = 0; k < n; k++)
            {
                int ccx = idx[k] % W, ccz = idx[k] / W;
                for (int cz = Math.Max(0, ccz - reach); cz <= Math.Min(H - 1, ccz + reach); cz++)
                    for (int cx = Math.Max(0, ccx - reach); cx <= Math.Min(W - 1, ccx + reach); cx++)
                        ClearCellAt(w, map, cx, cz, r, true, squeeze, sr, halo, s.Tent, s.TentSq);
            }
            return Connected(map, s, s.Tent, Snap(map, s.Tent, sx, sz), Snap(map, s.Tent, hill.X, hill.Z));
        }

        /// <summary>
        /// b14-c (tests and the scan): the Kaiju's body can walk from (x, z) to the hill on the current grid (the guard's own
        /// shadow, crush rule). Pure: touches no Sim state.
        /// </summary>
        public static bool BodyReachesHillFrom(World w, double x, double z)
        {
            RouteShadow s = ShadowFor(w);
            SyncShadow(w, s, GridR(w), w.Config.Physics.KaijuSqueezeRadius, w.Config.Physics.KaijuSqueezeHalo, SqueezeOn(w));
            MapDef map = w.Map;
            return Connected(map, s, s.Cells, Snap(map, s.Cells, x, z), Snap(map, s.Cells, map.Hill.X, map.Hill.Z));
        }
    }
}

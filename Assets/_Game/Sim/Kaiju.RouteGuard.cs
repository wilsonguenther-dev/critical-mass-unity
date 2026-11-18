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
    /// (the Kaiju's own clearance grid, refreshed first: RefreshClearance is byte-identical whenever it runs); allocation-free
    /// (static scratch sized for the map at type load); it runs only when a change makes a cell impassable for the body (a hole), so walls cost nothing.
    /// </summary>
    public sealed partial class Kaiju
    {
        // Scratch for the tentative clearance grid and the breadth-first search. Static, sized for the shipped map at type load
        // (grown only for a bigger map), so no call allocates; the contents never outlive a call (a pure function of the grid).
        private static byte[] Tent = new byte[CriticalMass.Sim.Maps.SkybreakBasinMap.MapWidth * CriticalMass.Sim.Maps.SkybreakBasinMap.MapHeight];
        private static byte[] TentSq = new byte[Tent.Length];
        private static int[] Visit = new int[Tent.Length];
        private static int[] Queue = new int[Tent.Length];
        private static int Stamp;

        /// <summary>
        /// b14 sim cost (Terraform's route verdict memo): bumped every time RefreshClearance rebuilds or patches the clearance
        /// grid of any World, so a remembered start cell is only compared on the very clearance bytes it was taken from.
        /// Never Sim state.
        /// </summary>
        internal static long ClearEpoch;

        /// <summary>
        /// b14 sim cost: what a TentativeKeepsKaijuRoute verdict read about the Kaiju: the body point (x, z) it snapped from,
        /// the start cell that gave on the clearance grid before the change, and the box of cells the tentative change can
        /// alter on the clearance grid (every changed cell +- ClearReach).
        /// </summary>
        internal struct GuardKey
        {
            public bool Used;
            public int Start;
            public double X, Z;
            public int Bx0, Bx1, Bz0, Bz1;
        }

        /// <summary>b14 sim cost: the key of the last TentativeKeepsKaijuRoute call (Used false = it never read the Kaiju).</summary>
        internal static GuardKey LastGuard;

        private static bool SnapAreaOutside(double x, double z, in GuardKey g)
        {
            int ox = JsMath.FloorToInt(x), oz = JsMath.FloorToInt(z);
            return ox + GoalSnapRings < g.Bx0 || ox - GoalSnapRings > g.Bx1 || oz + GoalSnapRings < g.Bz0 || oz - GoalSnapRings > g.Bz1;
        }

        /// <summary>
        /// b14 sim cost: would TentativeKeepsKaijuRoute read the same about the Kaiju now as it did for key g? Only on a
        /// clearance grid that is already current (the caller also checks it is the very same build: ClearEpoch); no refresh
        /// here, so nothing the Kaiju step reads is touched. Same when the body point is the same, or when the start cell is
        /// the same and neither point's snap search reaches the cells the change can alter (then the search after the change
        /// reads the same bytes as the one before, and both give the same start cell).
        /// </summary>
        internal static bool RouteMemoSameKaiju(World w, in GuardKey g)
        {
            KaijuNav nav = w.KaijuNav;
            if (nav.ClearGridVersion != w.GridVersion) return false;
            MapDef map = w.Map;
            Kaiju kj = w.Kaiju;
            bool out_ = w.Match.Hatched && kj.State != KaijuState.Egg;
            double sx = out_ ? kj.X : map.Shrine.X, sz = out_ ? kj.Z : map.Shrine.Z + HatchOffsetZ;
            if (sx == g.X && sz == g.Z) return true;
            if (!SnapAreaOutside(sx, sz, g) || !SnapAreaOutside(g.X, g.Z, g)) return false;
            int start = Snap(map, nav.Clear.Cells, sx, sz);
            return (start < 0 ? int.MinValue : start) == g.Start;
        }

        /// <summary>The Kaiju route guard applies: Full profile, the nav guard and the hill-siege crush rule on.</summary>
        internal static bool RouteGuardOn(World w) =>
            DepthRules.On(w, true) && Phys.On(w, w.Config.Physics.KaijuNavGuard) && Phys.On(w, w.Config.Physics.KaijuHillSiege);

        /// <summary>
        /// World creation: allocate the per-world clearance build state and (b14 sim cost) BUILD the Kaiju clearance grid now,
        /// while the match loads. Before, the first full build (~7 ms of whole-map clearance tests plus the region labels) landed
        /// on whichever tick first needed it: the hatch tick, or, with the route guard, the first pre-hatch dig or TNT crater,
        /// stacked on that crater's own route proofs (a 20 ms tick measured on 16v16 seed 4242). The grid is a pure function of
        /// the map (RefreshClearance is byte-identical whenever it runs) and every reader refreshes it first, so building it
        /// early moves no Sim result; later grid changes are then patches.
        /// </summary>
        internal static void WarmClearanceBuild(World w)
        {
            ClearBuildFor(w);
            RefreshClearance(w);
        }

        private static bool PassableType(byte t) => MapGrid.IsWalkableType(t) || t == Cell.Barricade || t == Cell.LowWall;

        private static void Fit(int n)
        {
            if (Tent.Length >= n) return;
            Tent = new byte[n]; TentSq = new byte[n]; Visit = new int[n]; Queue = new int[n]; Stamp = 0;
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
        private static bool Connected(MapDef map, byte[] cells, int a, int b)
        {
            if (a < 0 || b < 0) return false;
            if (a == b) return true;
            if (Stamp == int.MaxValue) { Array.Fill(Visit, 0); Stamp = 0; }
            int stamp = ++Stamp, W = map.Width, head = 0, tail = 0;
            Queue[tail++] = a;
            Visit[a] = stamp;
            while (head < tail)
            {
                int c = Queue[head++], cx = c % W, cz = c / W;
                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + Ndx[i], nz = cz + Ndz[i];
                    if (!Clear(map, cells, nx, nz)) continue;
                    int ni = nz * W + nx;
                    if (Visit[ni] == stamp) continue;
                    if (i >= 4 && (!Clear(map, cells, nx, cz) || !Clear(map, cells, cx, nz))) continue;
                    if (ni == b) return true;
                    Visit[ni] = stamp;
                    Queue[tail++] = ni;
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
            // The clearance BEFORE the change: the Kaiju's own shadow grid, brought up to date with the tentative cells put back
            // (RefreshClearance is a pure function of the grid, byte-identical whenever it runs; the Kaiju step would run it next).
            for (int k = 0; k < n; k++) cells[idx[k]] = old[k];
            RefreshClearance(w);
            for (int k = 0; k < n; k++) cells[idx[k]] = next[k];
            KaijuNav nav = w.KaijuNav;
            byte[] before = nav.Clear.Cells;
            Kaiju kj = w.Kaiju;
            bool out_ = w.Match.Hatched && kj.State != KaijuState.Egg;
            double sx = out_ ? kj.X : map.Shrine.X, sz = out_ ? kj.Z : map.Shrine.Z + HatchOffsetZ;
            HillDef hill = map.Hill;
            int start = Snap(map, before, sx, sz);
            int W = map.Width, H = map.Height;
            double r = GridR(w), sr = w.Config.Physics.KaijuSqueezeRadius;
            int halo = w.Config.Physics.KaijuSqueezeHalo;
            bool squeeze = SqueezeOn(w);
            int reach = ClearReach(r, sr, halo);
            // b14 sim cost: from here the verdict depends on the Kaiju; remember what it read (Terraform's route verdict memo)
            LastGuard.Used = true;
            LastGuard.Start = start < 0 ? int.MinValue : start;
            LastGuard.X = sx;
            LastGuard.Z = sz;
            LastGuard.Bx0 = int.MaxValue; LastGuard.Bx1 = int.MinValue; LastGuard.Bz0 = int.MaxValue; LastGuard.Bz1 = int.MinValue;
            for (int k = 0; k < n; k++)
            {
                int ccx = idx[k] % W, ccz = idx[k] / W;
                LastGuard.Bx0 = Math.Min(LastGuard.Bx0, ccx - reach); LastGuard.Bx1 = Math.Max(LastGuard.Bx1, ccx + reach);
                LastGuard.Bz0 = Math.Min(LastGuard.Bz0, ccz - reach); LastGuard.Bz1 = Math.Max(LastGuard.Bz1, ccz + reach);
            }
            if (!Connected(map, before, start, Snap(map, before, hill.X, hill.Z))) return true;
            // The clearance AFTER: a scratch copy patched around the changed cells from the tentative grid.
            int N = before.Length;
            Fit(N);
            Array.Copy(before, Tent, N);
            Array.Copy(nav.Squeeze, TentSq, N);
            for (int k = 0; k < n; k++)
            {
                int ccx = idx[k] % W, ccz = idx[k] / W;
                for (int cz = Math.Max(0, ccz - reach); cz <= Math.Min(H - 1, ccz + reach); cz++)
                    for (int cx = Math.Max(0, ccx - reach); cx <= Math.Min(W - 1, ccx + reach); cx++)
                        ClearCellAt(w, map, cx, cz, r, true, squeeze, sr, halo, Tent, TentSq);
            }
            return Connected(map, Tent, Snap(map, Tent, sx, sz), Snap(map, Tent, hill.X, hill.Z));
        }

        /// <summary>
        /// b14-c (tests and the scan): the Kaiju's body can walk from (x, z) to the hill on the current grid (its own clearance
        /// grid, crush rule; brought up to date first, a pure function of the grid).
        /// </summary>
        public static bool BodyReachesHillFrom(World w, double x, double z)
        {
            RefreshClearance(w);
            MapDef map = w.Map;
            byte[] c = w.KaijuNav.Clear.Cells;
            return Connected(map, c, Snap(map, c, x, z), Snap(map, c, map.Hill.X, map.Hill.Z));
        }
    }
}

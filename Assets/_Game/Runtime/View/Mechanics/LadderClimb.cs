using System;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// b14 ladder fix (view only, never the sim). The sim is flat: a LADDER turns a 1 to 2 cell run of rock-wall cells walkable
    /// (BuiltPieces), so a fighter crosses the wall at ground speed. This class is the one shape every ladder view agrees on:
    /// one leaning ladder up the near face whose feet stand on the ground in front of the wall and whose rails rest on the wall
    /// lip, a walk along the wall top, one ladder down the far face. StagedBuildView draws that, ActorViews lifts a fighter
    /// along it (and turns him to face the rungs while he is on a ladder), ActorAnimator plays the climb pose there.
    /// <para>Owner review 2026-11-18: the old A-frame rose 1.6 u to the middle of the wall, but the hill's rock wall (the
    /// authored WorldBase Hill art) stands 3.0 u over its ground, so the ladders and the climbing fighter were inside the rock.
    /// Measured in engine (mesh down-casts): hill ring top 3.0 u over the ground (lip bevel 2.3 u), the shrine-yard wall 1.5 u
    /// (= the grid's wall box, SeeThroughProbe.WallHeight).</para>
    /// <para>Lanes: on Skybreak the rock walls are 8-connected circles, so a 3-lane plan converts the centre lane and at most
    /// part of a side lane (measured: all 248 ladder spots convert 1 or 2 cells, none 3). A ladder is drawn and a fighter is
    /// lifted only over lanes that really were converted; the rest of the rectangle is still rock.</para>
    /// Pure, allocation-free, O(pieces x cells).
    /// </summary>
    public static class LadderClimb
    {
        /// <summary>Rock top over the ground of the hill ring (authored WorldBase Hill art, measured 2026-11-18).</summary>
        public const float HillRockTop = 3.0f;
        /// <summary>Rock top elsewhere (the shrine yard art and the grid's wall box).</summary>
        public const float WallBoxTop = 1.5f;
        /// <summary>Cells from the hill centre inside which a wall cell is the tall hill rock (ring radius 10.5 on Skybreak).</summary>
        public const double HillRockRadius = 13.0;
        /// <summary>Horizontal run of a ladder per unit of rise (tan of its lean from vertical, about 20 degrees).</summary>
        public const float Lean = 0.36f;
        /// <summary>Gap between the wall face (the run's cell edge) and the ladder's top contact, so the rails touch the lip and
        /// never cut the face (the authored rock stands up to 0.1 u proud of its cells).</summary>
        public const double Standoff = 0.12;
        /// <summary>Half a lane across, plus a margin for the fighter's own width on the rungs.</summary>
        public const double LaneHalf = 0.55;

        /// <summary>Kept for callers of the old A-frame (the ladder's height: now per wall, see RockTop).</summary>
        public const float Rise = HillRockTop;

        /// <summary>Rock top over the ground for a ladder crossing a wall at sim (x, z) (HillRockTop near the hill, else WallBoxTop).</summary>
        public static float RockTop(MapDef map, double x, double z)
        {
            if (map == null || map.Hill == null) return WallBoxTop;
            double dx = x - map.Hill.X, dz = z - map.Hill.Z;
            return dx * dx + dz * dz <= HillRockRadius * HillRockRadius ? HillRockTop : WallBoxTop;
        }

        /// <summary>Distance from a ladder's centre to its top contact (the face plus the standoff).</summary>
        public static double FaceDist(double halfLen) => halfLen + Standoff;

        /// <summary>Distance from a ladder's centre to its feet.</summary>
        public static double FootDist(double halfLen, float top) => FaceDist(halfLen) + top * Lean;

        /// <summary>Height of the climb profile `along` metres from a ladder's centre (pure; tests): 0 beyond the feet, rising
        /// linearly up the ladder to `top` at the face, `top` across the wall.</summary>
        public static float Profile(double along, double halfLen, float top)
        {
            double u = Math.Abs(along), face = FaceDist(halfLen), foot = FootDist(halfLen, top);
            if (u >= foot) return 0f;
            if (u <= face) return top;
            return (float)(top * (foot - u) / Math.Max(1e-6, foot - face));
        }

        /// <summary>Old two-argument form (the hill rock top).</summary>
        public static float Profile(double along, double halfLen) => Profile(along, halfLen, HillRockTop);

        /// <summary>Across offsets (in lanes, -1..1 relative to the piece's centre line) that hold at least one converted cell.</summary>
        public static int LaneMask(MapDef map, BuiltPiece p)
        {
            int mask = 0;
            for (int k = 0; k < p.Count; k++)
            {
                int i = p.Cells[k];
                double cx = i % map.Width + 0.5 - p.X, cz = i / map.Width + 0.5 - p.Z;
                int o = (int)Math.Round(-cx * p.Dz + cz * p.Dx);
                if (o >= -1 && o <= 1) mask |= 1 << (o + 1);
            }
            return mask == 0 ? 2 : mask;
        }

        /// <summary>Lane mask of a planned (ghost) piece from its cells, centre (cx, cz) and axis (dx, dz).</summary>
        public static int LaneMask(MapDef map, int[] cells, int count, double cx, double cz, int dx, int dz)
        {
            int mask = 0;
            for (int k = 0; k < count; k++)
            {
                int i = cells[k];
                double ox = i % map.Width + 0.5 - cx, oz = i / map.Width + 0.5 - cz;
                int o = (int)Math.Round(-ox * dz + oz * dx);
                if (o >= -1 && o <= 1) mask |= 1 << (o + 1);
            }
            return mask == 0 ? 2 : mask;
        }

        /// <summary>1 inside a converted lane, fading to 0 over LaneFade beyond its edge (a fighter stepping sideways onto the
        /// rungs rises smoothly instead of popping), 0 elsewhere.</summary>
        static double LaneWeight(int mask, double across)
        {
            double best = 0;
            for (int o = -1; o <= 1; o++)
            {
                if ((mask & (1 << (o + 1))) == 0) continue;
                double d = Math.Abs(across - o) - LaneHalf;
                double wgt = d <= 0 ? 1 : d >= LaneFade ? 0 : 1 - d / LaneFade;
                if (wgt > best) best = wgt;
            }
            return best;
        }

        /// <summary>Across distance beyond a lane edge over which the lift fades out.</summary>
        public const double LaneFade = 0.4;

        /// <summary>
        /// Where sim point (x, z) is on the highest active laid ladder over it: the lift, whether it is on a ladder (not on the
        /// wall top), and the sim facing that looks at the rungs (toward the wall). False when no ladder covers the point.
        /// </summary>
        public static bool Sample(World w, double x, double z, out float lift, out bool onRungs, out double faceRungs)
        {
            lift = 0f;
            onRungs = false;
            faceRungs = 0;
            BuiltPiece[] ps = w != null && w.Builder != null ? w.Builder.Pieces : null;
            if (ps == null) return false;
            bool any = false;
            for (int k = 0; k < ps.Length; k++)
            {
                BuiltPiece p = ps[k];
                if (p == null || !p.Active || p.Carried || p.Kind != BuiltPieces.Ladder) continue;
                double ox = x - p.X, oz = z - p.Z;
                double along = ox * p.Dx + oz * p.Dz;
                double across = -ox * p.Dz + oz * p.Dx;
                if (Math.Abs(across) > 1.0 + LaneHalf + LaneFade) continue;
                double lw = LaneWeight(LaneMask(w.Map, p), across);
                if (lw <= 0) continue;
                float top = RockTop(w.Map, p.X, p.Z);
                float h = (float)(Profile(along, p.HalfLen, top) * lw);
                if (h <= 0f || h < lift) continue;
                any = true;
                lift = h;
                double u = Math.Abs(along);
                onRungs = u > FaceDist(p.HalfLen) && u < FootDist(p.HalfLen, top);
                double s = along >= 0 ? -1.0 : 1.0;   // toward the centre: the wall
                faceRungs = Math.Atan2(p.Dz * s, p.Dx * s);
            }
            return any;
        }

        /// <summary>The climb height under sim point (x, z): the highest active, laid ladder covering it, else 0.</summary>
        public static float Lift(World w, double x, double z) => Sample(w, x, z, out float h, out _, out _) ? h : 0f;

        /// <summary>True while sim point (x, z) is on a ladder's rungs (between its feet and the wall lip).</summary>
        public static bool OnRungs(World w, double x, double z) => Sample(w, x, z, out _, out bool r, out _) && r;

        // ------------------------------------------------------------------------------------------------ shortcut

        static int[] _q = new int[0];
        static int[] _mark = new int[0];
        static int _stamp;

        /// <summary>
        /// Walking steps (4-neighbour, walkable cells, the plan's own cells excluded) from cell (ax, az) to cell (bx, bz),
        /// searched within a square of radius `radius` cells; int.MaxValue when not found there. A ladder is a SHORTCUT
        /// when this is well over the straight span; at the end of a wall it is a step or two (a ladder to nowhere).
        /// </summary>
        public static int WalkSteps(MapDef map, int ax, int az, int bx, int bz, int[] planCells, int planCount, int radius)
        {
            int n = map.Width * map.Height;
            if (_mark.Length < n) { _mark = new int[n]; _q = new int[n]; _stamp = 0; }
            if (++_stamp == int.MaxValue) { Array.Clear(_mark, 0, _mark.Length); _stamp = 1; }
            int stamp = _stamp;
            for (int k = 0; k < planCount; k++) _mark[planCells[k]] = stamp;   // never walk the planned cells
            int start = az * map.Width + ax, goal = bz * map.Width + bx;
            int head = 0, tail = 0, depth = 0;
            _q[tail++] = start;
            _mark[start] = stamp;
            int levelEnd = tail;
            while (head < tail)
            {
                int i = _q[head++];
                if (i == goal) return depth;
                int cx = i % map.Width, cz = i / map.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = cz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= map.Width || nz >= map.Height) continue;
                    if (Math.Abs(nx - ax) > radius || Math.Abs(nz - az) > radius) continue;
                    int j = nz * map.Width + nx;
                    if (_mark[j] == stamp || !MapGrid.IsWalkable(map, nx, nz)) continue;
                    _mark[j] = stamp;
                    _q[tail++] = j;
                }
                if (head == levelEnd) { depth++; levelEnd = tail; }
            }
            return int.MaxValue;
        }

        /// <summary>Extra walking steps a ladder must save over its own span to count as a shortcut.</summary>
        public const int MinSavedSteps = 4;

        /// <summary>
        /// True when the last successful BuiltPieces.Plan (a LADDER along (dx, dz) for actor id) saves at least MinSavedSteps
        /// walking steps: the landing is not a short walk round the wall's end. Reads Plan's scratch; call right after Plan.
        /// </summary>
        public static bool LastPlanIsShortcut(World w, int id, int dx, int dz)
        {
            if (BuiltPieces.LastPlanKind != BuiltPieces.Ladder) return true;
            BuiltPieces.LastPlanLanding(w, id, dx, dz, out double lx, out double lz, out int cells);
            int n = BuiltPieces.LastPlanCount;
            if (_plan.Length < n) _plan = new int[n];
            for (int k = 0; k < n; k++) _plan[k] = BuiltPieces.LastPlanCell(k);
            int ax = (int)Math.Floor(w.X[id]), az = (int)Math.Floor(w.Z[id]);
            int steps = WalkSteps(w.Map, ax, az, (int)Math.Floor(lx), (int)Math.Floor(lz), _plan, n, cells + MinSavedSteps + 2);
            return steps >= cells + MinSavedSteps;
        }

        static int[] _plan = new int[16];

        /// <summary>True when the planned ladder's landing is nearer the hill centre than the builder (it climbs onto the hill).</summary>
        public static bool LastPlanToHill(World w, int id, int dx, int dz)
        {
            HillDef h = w.Map.Hill;
            if (h == null) return false;
            BuiltPieces.LastPlanLanding(w, id, dx, dz, out double lx, out double lz, out _);
            double d0 = (w.X[id] - h.X) * (w.X[id] - h.X) + (w.Z[id] - h.Z) * (w.Z[id] - h.Z);
            double d1 = (lx - h.X) * (lx - h.X) + (lz - h.Z) * (lz - h.Z);
            return d1 < d0;
        }
    }
}

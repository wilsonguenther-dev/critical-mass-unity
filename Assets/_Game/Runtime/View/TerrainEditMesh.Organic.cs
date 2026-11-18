using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// b15 island geometry, natural pits (owner 2026-11-17: "I don't know why you have holes being like a straight block, it
    /// could be something more realistic"). A dug area is ONE pit shape, not a grid of square holes:
    /// <list type="bullet">
    /// <item>RIM: follows the merged dug cells. Each rim corner of the dug area is cut round (CornerCut along the diagonal,
    /// under the 0.15 u rule) and every rim point is pushed in by a small deterministic jitter, so the outline wobbles; walls
    /// between two dug cells are never drawn (adjacent cells share their corner points exactly, so the shape is seamless
    /// across cells and chunks).</item>
    /// <item>WALLS: slope in from the rim to the floor (Inset(depth): 0.3 u for a dug cell's 0.9 u, less for shallow pits)
    /// with a grass lip, then topsoil, clay and rock bands whose boundaries wobble per point.</item>
    /// <item>FLOOR: one height per grid corner (the mean of the dug cells around it), so a dug area deepens smoothly toward its
    /// middle (no step walls), with two stones per cell, now and then a root on a wall and a spoil heap on one rim side.</item>
    /// </list>
    /// Depth is a parameter: every point takes its floor from Depth(cx, cz) (the ring depths of a crater, or the true depth of
    /// a walkable lift pit from the terrain-height rule), and the wall profile scales to it. Same mesh, same material, vertex
    /// colours; all noise comes from cell and corner positions (same on every client). Cap: MaxTrisPerPitCell.
    /// </summary>
    public sealed partial class TerrainEditMesh
    {
        /// <summary>Natural pits on (off: the b14 square pits, kept for the before/after measurement).</summary>
        public static bool OrganicPits = true;
        /// <summary>How far a convex rim corner of a dug area is cut in along its diagonal (u; at most 0.15).</summary>
        public const float CornerCut = 0.15f;
        /// <summary>Largest inward jitter of a rim point (u).</summary>
        public const float RimJitter = 0.05f;
        /// <summary>Triangle cap per dug cell (a lone cell, the worst case, is about 170).</summary>
        public const int MaxTrisPerPitCell = 180;

        static readonly Color32 Clay = C(186, 135, 90), Root = C(92, 64, 42);

        /// <summary>Wall run (u) from the rim in to the floor for a pit `depth` deep.</summary>
        public static double Inset(double depth) => System.Math.Min(0.3, 0.33 * depth);

        bool IsPit(int cx, int cz) => KindAt(cx, cz) == TerrainEdits.Kind.Pit;

        /// <summary>
        /// The 2 x 2 cells around grid corner (vx, vz) that are dug and joined to cell (ax, az) through that block (the cell,
        /// its dug side neighbours, and the diagonal cell only through a dug side neighbour). Returns their count; dir is the
        /// unit direction from the corner into them (into the pit).
        /// </summary>
        int Joined(int vx, int vz, int ax, int az, out double dx, out double dz)
        {
            int ox = ax == vx ? vx - 1 : vx, oz = az == vz ? vz - 1 : vz;   // the block's other column and row
            bool side1 = IsPit(ox, az), side2 = IsPit(ax, oz), diag = (side1 || side2) && IsPit(ox, oz);
            double sx = ax + 0.5 - vx, sz = az + 0.5 - vz;
            int n = 1;
            if (side1) { sx += ox + 0.5 - vx; sz += az + 0.5 - vz; n++; }
            if (side2) { sx += ax + 0.5 - vx; sz += oz + 0.5 - vz; n++; }
            if (diag) { sx += ox + 0.5 - vx; sz += oz + 0.5 - vz; n++; }
            double l = System.Math.Sqrt(sx * sx + sz * sz);
            if (l < 1e-9) { dx = dz = 0; return n; }
            dx = sx / l; dz = sz / l;
            return n;
        }

        /// <summary>Floor height at grid corner (vx, vz): the mean of (ground - depth) over the dug cells around it.</summary>
        double CornerFloor(int vx, int vz)
        {
            double sum = 0;
            int n = 0;
            for (int dz = -1; dz <= 0; dz++)
            for (int dx = -1; dx <= 0; dx++)
            {
                int x = vx + dx, z = vz + dz;
                if (!IsPit(x, z)) continue;
                sum += H(x, z) - Depth(x, z);
                n++;
            }
            return n > 0 ? sum / n : H(vx, vz);
        }

        /// <summary>Floor height at any sim point: bilinear over the corner floors of the cell that contains it (a function of
        /// the point alone, so two cells sharing a floor vertex agree exactly).</summary>
        double FloorAt(double x, double z)
        {
            int cx = (int)System.Math.Floor(x), cz = (int)System.Math.Floor(z);
            double fx = x - cx, fz = z - cz;
            double a = CornerFloor(cx, cz), b = CornerFloor(cx + 1, cz), c = CornerFloor(cx + 1, cz + 1), d = CornerFloor(cx, cz + 1);
            return (a * (1 - fx) + b * fx) * (1 - fz) + (d * (1 - fx) + c * fx) * fz;
        }

        static double VJit(double x, double z, int salt) =>
            (WorldDresser.H32((long)System.Math.Round(x * 8), (long)System.Math.Round(z * 8), 4100 + salt) % 1000u) / 1000.0;

        // Per-cell scratch: the rim polyline of one side (4 points) and its rows.
        readonly double[] _rx = new double[4], _rz = new double[4], _ex = new double[4], _ez = new double[4], _ix = new double[4], _iz = new double[4], _bx = new double[4], _bz = new double[4], _by = new double[4];
        readonly double[] _fx = new double[12], _fz = new double[12];

        /// <summary>The natural pit for dug cell (cx, cz).</summary>
        void OrganicPit(int cx, int cz)
        {
            PitCells++;
            double h = H(cx, cz);
            double D = Depth(cx, cz);
            double yf = h - D;
            MinFloorY = Mathf.Min(MinFloorY, (float)yf);
            MaxFloorY = Mathf.Max(MaxFloorY, (float)yf);
            int nf = 0;   // floor polygon points
            int bestSide = -1;
            double bestHash = -1;
            // Corners in order (sim): v0 (cx, cz), v1 (cx+1, cz), v2 (cx+1, cz+1), v3 (cx, cz+1); side s runs from corner s to
            // corner s+1: 0 north (z = cz), 1 east (x = cx+1), 2 south (z = cz+1), 3 west (x = cx).
            for (int s = 0; s < 4; s++)
            {
                int vax = s == 1 || s == 2 ? cx + 1 : cx, vaz = s >= 2 ? cz + 1 : cz;
                int vbx = s == 0 || s == 1 ? cx + 1 : cx, vbz = s == 1 || s == 2 ? cz + 1 : cz;
                int nx = cx + (s == 1 ? 1 : s == 3 ? -1 : 0), nz = cz + (s == 0 ? -1 : s == 2 ? 1 : 0);
                double inx = -(nx - cx), inz = -(nz - cz);   // into this cell, away from the side's neighbour
                bool rim = !IsPit(nx, nz);
                // The side's start corner: its floor point (the rim walls end there, or the cell edge where it is interior).
                CornerPoint(vax, vaz, cx, cz, out double rax, out double raz, out double bax, out double baz, out _);
                _fx[nf] = bax; _fz[nf] = baz; nf++;
                if (!rim) continue;
                // Rim polyline: start corner, two points along the side, end corner.
                CornerPoint(vbx, vbz, cx, cz, out double rbx, out double rbz, out double bbx, out double bbz, out _);
                _ex[0] = vax; _ez[0] = vaz; _rx[0] = rax; _rz[0] = raz; _bx[0] = bax; _bz[0] = baz;
                _ex[3] = vbx; _ez[3] = vbz; _rx[3] = rbx; _rz[3] = rbz; _bx[3] = bbx; _bz[3] = bbz;
                for (int k = 1; k <= 2; k++)
                {
                    double u = k == 1 ? 0.35 : 0.65;
                    double px = vax + (vbx - vax) * u, pz = vaz + (vbz - vaz) * u;
                    double j = RimJitter * VJit(px, pz, 1);
                    double ins = Inset(h - FloorAt(px + inx * 0.3, pz + inz * 0.3));
                    _ex[k] = px; _ez[k] = pz;
                    _rx[k] = px + inx * j; _rz[k] = pz + inz * j;
                    _bx[k] = px + inx * (j + ins); _bz[k] = pz + inz * (j + ins);
                }
                for (int k = 0; k < 4; k++) _by[k] = FloorAt(_bx[k], _bz[k]);
                for (int k = 1; k <= 2; k++) { _fx[nf] = _bx[k]; _fz[nf] = _bz[k]; nf++; }
                RimSide(cx, cz, s, h, inx, inz);
                double sh = Hash01(cx, cz, 900 + s);
                if (sh > bestHash) { bestHash = sh; bestSide = s; }
            }
            // Floor: a fan from the cell centre over the floor polygon (corner floor points and the rim sides' wall feet).
            Color32 floor = Jit(FloorDirt, cx, cz, 11);
            double ccx = cx + 0.5, ccz = cz + 0.5, ccy = FloorAt(ccx, ccz);
            for (int k = 0; k < nf; k++)
            {
                int k1 = (k + 1) % nf;
                TriSim(ccx, ccy, ccz, _fx[k], FloorAt(_fx[k], _fz[k]), _fz[k], _fx[k1], FloorAt(_fx[k1], _fz[k1]), _fz[k1], 0, 1, 0, floor);
            }
            PitFloorFaces++;
            // Two stones on the floor, near the middle (inside the wall feet of a lone cell).
            for (int k = 0; k < 2; k++)
            {
                double px = cx + 0.38 + 0.24 * Hash01(cx, cz, 21 + k), pz = cz + 0.38 + 0.24 * Hash01(cx, cz, 31 + k);
                double sz = 0.045 + 0.035 * Hash01(cx, cz, 41 + k);
                Lump(px, FloorAt(px, pz), pz, sz, 0.04 + 0.05 * Hash01(cx, cz, 51 + k), Jit(Pebble, cx, cz, 61 + k));
                Pebbles++;
            }
            // One spoil heap, on the rim side with the highest hash, if the ground there is untouched floor.
            if (bestSide >= 0 && Hash01(cx, cz, 111) >= 0.25)
            {
                int dx = bestSide == 1 ? 1 : bestSide == 3 ? -1 : 0, dz = bestSide == 0 ? -1 : bestSide == 2 ? 1 : 0;
                int nx = cx + dx, nz = cz + dz;
                byte nc = Cur(nx, nz);
                if ((nc == Cell.Floor || nc == Cell.BreakableDestroyed) && KindAt(nx, nz) == TerrainEdits.Kind.None)
                {
                    double along = (Hash01(cx, cz, 71) - 0.5) * 0.4;
                    double mx = cx + 0.5 + dx * 0.76 + (dx == 0 ? along : 0), mz = cz + 0.5 + dz * 0.76 + (dz == 0 ? along : 0);
                    double len = 0.28 + 0.1 * Hash01(cx, cz, 81), wid = 0.16 + 0.05 * Hash01(cx, cz, 121);
                    Mound(mx, H(nx, nz) - 0.015, mz, dx == 0 ? len : wid, dx == 0 ? wid : len, 0.08 + 0.05 * Hash01(cx, cz, 91), cx, cz, 101);
                    SpoilLumps++;
                }
            }
        }

        /// <summary>
        /// Rim and floor points of grid corner (vx, vz) for dug cell (ax, az): r = where the rim passes (the corner itself on a
        /// straight edge, cut in along the diagonal at a convex corner of the dug area, jittered in), b = the wall's foot (r
        /// pushed in by the wall run; at a corner by the run times the diagonal factor), rim = the corner is on the rim.
        /// </summary>
        void CornerPoint(int vx, int vz, int ax, int az, out double rx, out double rz, out double bx, out double bz, out bool rim)
        {
            int n = Joined(vx, vz, ax, az, out double dx, out double dz);
            rim = n < 4;
            if (!rim) { rx = bx = vx; rz = bz = vz; return; }
            double j = RimJitter * VJit(vx, vz, 2);
            bool diagonal = n != 2;   // 1 (convex corner) or 3 (inner corner): the direction is diagonal
            double cut = n == 1 ? CornerCut : 0;
            double ins = Inset(H(ax, az) - CornerFloor(vx, vz));
            double foot = n == 1 ? System.Math.Max(ins * 1.41421356, cut + ins) : diagonal ? ins * 1.41421356 : ins;
            rx = vx + dx * (cut + j); rz = vz + dz * (cut + j);
            bx = vx + dx * (cut + j + foot - cut); bz = vz + dz * (cut + j + foot - cut);
            if (n == 1) { bx = vx + dx * (foot + j); bz = vz + dz * (foot + j); }
        }

        /// <summary>One rim side's lip top, lip front and earth bands (the polyline in _e / _r / _b, feet heights in _by).</summary>
        void RimSide(int cx, int cz, int side, double h, double inx, double inz)
        {
            PitWallFaces++;
            Color32 grass = Jit(Grass, cx, cz, 701), grassDark = Jit(GrassDark, cx, cz, 702);
            for (int k = 0; k < 3; k++)
            {
                int k1 = k + 1;
                // Lip top: the strip between the cell edge and the rim (covers the cut corners), at ground height.
                QuadSim(_ex[k], h, _ez[k], _ex[k1], h, _ez[k1], _rx[k1], h, _rz[k1], _rx[k], h, _rz[k], 0, 1, 0, grass, 0f, default);
                // Band rows: rim (h), lip foot, topsoil foot, clay foot, floor.
                double prevAx = _rx[k], prevAz = _rz[k], prevBx = _rx[k1], prevBz = _rz[k1];
                for (int band = 0; band < 4; band++)
                {
                    WallRow(k, band, h, out double ax, out double ay, out double az);
                    WallRow(k1, band, h, out double bx, out double by, out double bz);
                    double ya = band == 0 ? h : RowY(k, band - 1, h), yb = band == 0 ? h : RowY(k1, band - 1, h);
                    Color32 c = band == 0 ? grassDark : band == 1 ? Jit(Topsoil, cx, cz, 710 + side) : band == 2 ? Jit(Clay, cx, cz, 720 + side) : Jit(RockLight, cx, cz, 730 + side);
                    // Shading normal: the face's own, leaned toward up (PitNormalUp) so every wall of a pit takes the lit band.
                    double fnx = inx * (1 - PitNormalUp), fny = PitNormalUp, fnz = inz * (1 - PitNormalUp);
                    QuadSim(prevAx, ya, prevAz, prevBx, yb, prevBz, bx, by, bz, ax, ay, az, fnx, fny, fnz, c, 0f, default);
                    prevAx = ax; prevAz = az; prevBx = bx; prevBz = bz;
                }
            }
            // Now and then a root crossing the topsoil band of the middle segment.
            if (Hash01(cx, cz, 760 + side) < 0.35)
            {
                WallRow(1, 0, h, out double ax, out double ay, out double az);
                WallRow(2, 1, h, out double bx, out double by, out double bz);
                double ox = inx * 0.012, oz = inz * 0.012;
                QuadSim(ax + ox, ay - 0.02, az + oz, bx + ox, by + 0.03, bz + oz, bx + ox, by - 0.0, bz + oz, ax + ox, ay - 0.05, az + oz,
                    inx * (1 - PitNormalUp), PitNormalUp, inz * (1 - PitNormalUp), Jit(Root, cx, cz, 770 + side), 0f, default);
            }
        }

        /// <summary>Height of band row `band` (0 lip foot, 1 topsoil foot, 2 clay foot, 3 floor) at polyline point k.</summary>
        double RowY(int k, int band, double h)
        {
            double dTot = System.Math.Max(0.05, h - _by[k]);
            switch (band)
            {
                case 0: return h - System.Math.Min(LipDrop, dTot * 0.3);
                case 1: return h - System.Math.Min(dTot * 0.45, 0.18 + 0.06 * VJit(_ex[k], _ez[k], 11));
                case 2: return h - System.Math.Min(dTot * 0.8, System.Math.Max(dTot * 0.45 + 0.05, dTot * (0.5 + 0.12 * VJit(_ex[k], _ez[k], 12))));
                default: return _by[k];
            }
        }

        /// <summary>Point of band row `band` at polyline point k: from the rim toward the wall's foot by the row's depth share,
        /// bowed (a dug slope is steeper at the top), with a small per-point wobble.</summary>
        void WallRow(int k, int band, double h, out double x, out double y, out double z)
        {
            y = RowY(k, band, h);
            double dTot = System.Math.Max(0.05, h - _by[k]);
            double t = System.Math.Min(1.0, (h - y) / dTot);
            double bow = band == 3 ? 1.0 : System.Math.Pow(t, 1.35) + (band == 0 ? 0.04 : 0.03 * (VJit(_ex[k], _ez[k], 20 + band) - 0.5));
            x = _rx[k] + (_bx[k] - _rx[k]) * bow;
            z = _rz[k] + (_bz[k] - _rz[k]) * bow;
        }

        /// <summary>One triangle (floor fan) with a fixed normal and colour, no outline.</summary>
        void TriSim(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz, double nx, double ny, double nz, Color32 col)
        {
            Vector3 a = SimSpace.ToWorld(ax, ay, az), b = SimSpace.ToWorld(bx, by, bz), c = SimSpace.ToWorld(cx, cy, cz);
            Vector3 n = SimSpace.ToWorld(nx, ny, nz);
            int i0 = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c);
            for (int k = 0; k < 3; k++) { Normals.Add(n); Colors.Add(col); Weights.Add(Vector2.zero); Outline.Add(n); }
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0) { Tris.Add(i0); Tris.Add(i0 + 1); Tris.Add(i0 + 2); }
            else { Tris.Add(i0); Tris.Add(i0 + 2); Tris.Add(i0 + 1); }
        }

        // ------------------------------------------------------------------------------------------------ irregular stones

        /// <summary>
        /// b15 placed blocks: a short wall / block piece's top is not a perfect box. Each top corner drops 0 to StoneTopJitter
        /// and pulls in up to StoneInJitter by its corner hash (a corner shared by two blocks of a run differs per block, so a
        /// run shows separate stones), and the courses' mortar line and joint move per cell (Side).
        /// </summary>
        public const float StoneTopJitter = 0.035f, StoneInJitter = 0.025f;

        void BevelTopStone(int cx, int cz, double top, double bevel, Color32 cTop, Color32 cCham, Vector3 centre)
        {
            double y1 = top - bevel;
            // Corner order: (x0, z0), (x1, z0), (x1, z1), (x0, z1).
            double t0 = top - StoneTopJitter * Hash01(cx * 2, cz * 2, 811), t1 = top - StoneTopJitter * Hash01(cx * 2 + 1, cz * 2, 811);
            double t2 = top - StoneTopJitter * Hash01(cx * 2 + 1, cz * 2 + 1, 811), t3 = top - StoneTopJitter * Hash01(cx * 2, cz * 2 + 1, 811);
            // The highest corner stays on the grid height (the block's top is where the Sim stands actors on it).
            double lift = top - System.Math.Max(System.Math.Max(t0, t1), System.Math.Max(t2, t3));
            t0 += lift; t1 += lift; t2 += lift; t3 += lift;
            double j0 = StoneInJitter * Hash01(cx, cz, 821), j1 = StoneInJitter * Hash01(cx, cz, 822), j2 = StoneInJitter * Hash01(cx, cz, 823), j3 = StoneInJitter * Hash01(cx, cz, 824);
            double ax = cx + bevel + j0, az = cz + bevel + j0, bx = cx + 1 - bevel - j1, bz = cz + bevel + j1;
            double qx = cx + 1 - bevel - j2, qz = cz + 1 - bevel - j2, dx = cx + bevel + j3, dz = cz + 1 - bevel - j3;
            QuadSim(ax, t0, az, bx, t1, bz, qx, t2, qz, dx, t3, dz, 0, 1, 0, cTop, 1f, centre);
            const double k = 0.70710678;
            QuadSim(cx, y1, cz, cx, y1, cz + 1, dx, t3, dz, ax, t0, az, -k, k, 0, cCham, 1f, centre);              // -x
            QuadSim(cx + 1, y1, cz, bx, t1, bz, qx, t2, qz, cx + 1, y1, cz + 1, k, k, 0, cCham, 1f, centre);      // +x
            QuadSim(cx, y1, cz, ax, t0, az, bx, t1, bz, cx + 1, y1, cz, 0, k, -k, cCham, 1f, centre);              // -z
            QuadSim(cx, y1, cz + 1, cx + 1, y1, cz + 1, qx, t2, qz, dx, t3, dz, 0, k, k, cCham, 1f, centre);      // +z
        }

        /// <summary>Mortar line height share (0.42 to 0.58) and joint offset (+-0.08 to +-0.22) of a stone side, per cell.</summary>
        static double StoneMortar(int cx, int cz, int s) => 0.42 + 0.16 * Hash01(cx, cz, 830 + s);
        static double StoneJoint(int cx, int cz, int s) => (((cx + cz + s) & 1) == 0 ? 1 : -1) * (0.08 + 0.14 * Hash01(cx, cz, 840 + s));
    }
}

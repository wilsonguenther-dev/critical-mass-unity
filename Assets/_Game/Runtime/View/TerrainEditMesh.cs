using System.Collections.Generic;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// b14 terrain look: the geometry of changed terrain for one 16 x 16 chunk (GridChunkMeshView owns one per chunk with
    /// edits). View only; a pure builder over plain arrays, so EditMode tests build it without a scene.
    /// <list type="bullet">
    /// <item>PIT (TerrainEdits.Kind.Pit): inner walls on every side whose neighbour is not a pit (earth strata from the cut
    /// rim down: a grass lip, dark topsoil, soil with a ragged lower edge, light rock, dark rock), each wall tapered 0.07 u
    /// inward at the bottom (a dug look; the corners of two walls share one edge, so there is no gap), a packed-dirt floor
    /// with two pebbles, a step wall down to a deeper neighbouring pit, and a spoil lump on the untouched ground at each
    /// walled rim. Depth grows toward the middle of a dug area: 0.9 u for a cell touching unbroken ground, 1.35 u one ring
    /// in, 1.8 u from two rings in (deeper digs show taller walls). The floor closes the hole: the island's underside is
    /// 6 u down, so no pit reaches it and none is see-through.</item>
    /// <item>PLACED / FILLED: an earth block, top at the cell's height with a 0.06 u bevel all round (a V seam between
    /// neighbouring blocks and around a fill), a grass cap, strata on every side that shows (sides only toward the void;
    /// toward a pit the pit's wall is drawn), a dark rock bottom 1.0 u down for a block placed out into the void.</item>
    /// <item>SHORT WALL / BLOCK piece (Cell.LowWall): a dressed-stone block standing on the cell, top exactly WallView.ShortTop
    /// above the ground, bevelled top, two courses with a mortar line, a dark footing band, vertical seams at each cell end,
    /// faces between two wall cells left out (they never show), so a run of blocks reads as stacked stones with seams.</item>
    /// <item>b15 RAISED (TerrainEdits.Kind.Raised, a lift above 0): stacked dressed-stone courses, one per level (StepU each,
    /// a mortar line and a dark footing per course, vertical seams at the cell ends), a bevelled stone cap on top; sides only
    /// where the neighbour's top is lower, down to that top (to the cell's own ground minus PlacedDepth toward void). The
    /// chunk view keeps drawing the ground box under it. b15 lift pits (a lift below 0) are PITs at their TRUE depth
    /// (-lift x StepU: 0.45 / 0.9 u), so the floor is where the characters stand; void craters keep the ring depths.</item>
    /// </list>
    /// One material (CM/Toon in vertex-colour mode, the kit's own colour path; outline weight per vertex from UV4.x: pit
    /// faces carry no hull, blocks and stones the 2 px structure line from their centre via UV3). Colours are the terrain
    /// kit's (grass mid #A3C150, cliff rock #5B5E58) plus earth tones; every face takes a small per-cell value jitter.
    /// Allocation-free after warm-up (lists and scratch arrays are reused).
    /// </summary>
    public sealed class TerrainEditMesh
    {
        public const float PitDepth1 = 0.9f, PitDepthStep = 0.45f, PitDepthMax = 1.8f;
        public const float Taper = 0.07f;
        public const float PlacedDepth = 1.0f;
        public const float EarthBevel = 0.06f, StoneBevel = 0.07f;
        /// <summary>A short wall's top above its ground (the ghost and WallView's team cap use the same number).</summary>
        public const float ShortTop = B13.WallView.ShortTop;
        /// <summary>A short wall's footing below its ground (hidden by the ground, closes any seam with the ground art).</summary>
        public const float ShortSink = 0.02f;

        // Palette (sRGB, alpha 0 = CMToon mask code 0, a fixed colour).
        static readonly Color32 Grass = C(163, 193, 80), GrassDark = C(128, 156, 62);
        static readonly Color32 Topsoil = C(94, 66, 43), Soil = C(122, 88, 56);
        static readonly Color32 RockLight = C(106, 108, 101), RockDark = C(76, 79, 75);
        static readonly Color32 FloorDirt = C(112, 84, 56), Pebble = C(140, 142, 134), Spoil = C(128, 94, 62);
        static readonly Color32 StoneTop = C(166, 167, 157), StoneSide = C(130, 132, 123), StoneFoot = C(100, 103, 96), Seam = C(70, 72, 68);

        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 0);

        public readonly List<Vector3> Verts = new List<Vector3>(4096);
        public readonly List<Vector3> Normals = new List<Vector3>(4096);
        public readonly List<Vector3> Outline = new List<Vector3>(4096);
        public readonly List<Color32> Colors = new List<Color32>(4096);
        public readonly List<Vector2> Weights = new List<Vector2>(4096);
        public readonly List<int> Tris = new List<int>(6144);

        // Stats of the last Build (tests and the capture print them).
        public int PitCells, PitWallFaces, PitFloorFaces, StepWallFaces, Pebbles, SpoilLumps;
        public int BlockCells, BlockSideFaces, BlockBottomFaces, ShortWallCells, ShortWallSideFaces;
        /// <summary>b15: raised (stacked) cells and stone courses drawn in the last Build.</summary>
        public int RaisedCells, RaisedCourses;

        /// <summary>b15 terrain height: the lift layer (MapDef.Lift, or the chunk view's copy; null = no layer) and its step.</summary>
        public sbyte[] Lift;
        public double StepU = 0.45;
        public float MinFloorY, MaxFloorY;

        int _w, _h;
        byte[] _cells, _pr;
        bool[] _tc;
        double[] _h01;
        // Per-build caches (grown, never shrunk, so a warm rebuild allocates nothing): the kind of every cell within 3 of the
        // chunk (a depth reads 2 rings around a neighbour) and the pit depth of every cell within 1.
        byte[] _kind = new byte[0];
        double[] _depth = new double[0];
        int _kx0, _kz0, _kw, _kh, _dx0, _dz0, _dw, _dh;
        readonly bool[] _rim = new bool[4];
        readonly double[] _step = new double[4];
        static readonly int[] DX = { -1, 1, 0, 0 }, DZ = { 0, 0, -1, 1 };

        public void Clear()
        {
            Verts.Clear(); Normals.Clear(); Outline.Clear(); Colors.Clear(); Weights.Clear(); Tris.Clear();
            PitCells = PitWallFaces = PitFloorFaces = StepWallFaces = Pebbles = SpoilLumps = 0;
            BlockCells = BlockSideFaces = BlockBottomFaces = ShortWallCells = ShortWallSideFaces = 0;
            RaisedCells = RaisedCourses = 0;
            MinFloorY = float.MaxValue; MaxFloorY = float.MinValue;
        }

        /// <summary>True when cell (current type, kind) is drawn by this builder (the chunk view then skips its own box).</summary>
        public static bool Owns(byte current, TerrainEdits.Kind kind) =>
            (kind != TerrainEdits.Kind.None && kind != TerrainEdits.Kind.Raised) || current == Cell.LowWall;   // b15: under a stack the ground box stays

        /// <summary>
        /// Build cells [x0, x1) x [z0, z1) of a width x height map. pristine / touched may be null (no edits: only short walls).
        /// </summary>
        public void Build(int width, int height, byte[] cells, double[] h01, byte[] pristine, bool[] touched, int x0, int z0, int x1, int z1)
        {
            Clear();
            _w = width; _h = height; _cells = cells; _h01 = h01; _pr = pristine; _tc = touched;
            _kw = _kh = _dw = _dh = 0;   // caches off while they fill
            int kx0 = x0 - 3, kz0 = z0 - 3, kw = x1 - x0 + 6, kh = z1 - z0 + 6;
            if (_kind.Length < kw * kh) _kind = new byte[kw * kh];
            for (int z = 0; z < kh; z++)
            for (int x = 0; x < kw; x++)
                _kind[z * kw + x] = (byte)KindAt(kx0 + x, kz0 + z);
            _kx0 = kx0; _kz0 = kz0; _kw = kw; _kh = kh;
            int dx0 = x0 - 1, dz0 = z0 - 1, dw = x1 - x0 + 2, dh = z1 - z0 + 2;
            if (_depth.Length < dw * dh) _depth = new double[dw * dh];
            for (int k = 0; k < dw * dh; k++) _depth[k] = -1;
            _dx0 = dx0; _dz0 = dz0; _dw = dw; _dh = dh;
            for (int cz = z0; cz < z1; cz++)
            for (int cx = x0; cx < x1; cx++)
            {
                int i = cz * _w + cx;
                byte cur = _cells[i];
                TerrainEdits.Kind k = KindAt(cx, cz);
                if (k == TerrainEdits.Kind.Pit) { Pit(cx, cz); continue; }
                if (k == TerrainEdits.Kind.Raised) { Raised(cx, cz); continue; }
                if (k == TerrainEdits.Kind.Placed || k == TerrainEdits.Kind.Filled) Earth(cx, cz, k == TerrainEdits.Kind.Placed);
                if (cur == Cell.LowWall) ShortWall(cx, cz);
            }
        }

        /// <summary>Upload into a mesh (32-bit indices when needed). False when there is nothing to draw.</summary>
        public bool Upload(Mesh mesh)
        {
            mesh.Clear();
            if (Tris.Count == 0) return false;
            mesh.indexFormat = Verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetColors(Colors);
            mesh.SetUVs(3, Outline);
            mesh.SetUVs(4, Weights);
            mesh.SetTriangles(Tris, 0, false);
            mesh.RecalculateBounds();
            return true;
        }

        static Material _material;

        /// <summary>The one shared material: CM/Toon, vertex colours, outline from UV3 weighted by UV4.x, 2 px.</summary>
        public static Material Material()
        {
            if (_material != null) return _material;
            _material = new Material(ToonMaterials.Structure("#808080")) { name = "CMToon terrain edits" };
            _material.SetFloat("_ColorMode", 1f);
            _material.SetFloat("_OutlineSource", ToonMaterials.OutlineFromUv3);
            _material.SetFloat("_OutlineWeightUV4", 1f);
            _material.SetFloat("_OutlinePx", ToonMaterials.StructureOutlinePx);
            return _material;
        }

        // ------------------------------------------------------------------------------------------------ cell queries

        bool In(int cx, int cz) => cx >= 0 && cz >= 0 && cx < _w && cz < _h;

        byte Cur(int cx, int cz) => In(cx, cz) ? _cells[cz * _w + cx] : Cell.Void;

        TerrainEdits.Kind KindAt(int cx, int cz)
        {
            int kx = cx - _kx0, kz = cz - _kz0;
            if (kx >= 0 && kz >= 0 && kx < _kw && kz < _kh) return (TerrainEdits.Kind)_kind[kz * _kw + kx];
            if (!In(cx, cz)) return TerrainEdits.Kind.None;
            int i = cz * _w + cx;
            byte p = _pr != null ? _pr[i] : _cells[i];
            return TerrainEdits.Classify(p, _cells[i], _tc != null && _tc[i], Lift != null ? Lift[i] : (sbyte)0);
        }

        double H(int cx, int cz)
        {
            if (!In(cx, cz)) return 0;
            return _h01 != null ? _h01[cz * _w + cx] : 0;
        }

        /// <summary>PitDepth through the per-build cache.</summary>
        double Depth(int cx, int cz)
        {
            int dx = cx - _dx0, dz = cz - _dz0;
            if (dx < 0 || dz < 0 || dx >= _dw || dz >= _dh) return PitDepth(cx, cz);
            int k = dz * _dw + dx;
            if (_depth[k] < 0) _depth[k] = PitDepth(cx, cz);
            return _depth[k];
        }

        /// <summary>Pit depth of cell (cx, cz): by the Chebyshev ring to the nearest cell that is not a pit.</summary>
        public double PitDepth(int cx, int cz)
        {
            int l = LiftOf(cx, cz);
            if (l < 0) return -l * StepU;   // b15: a walkable dug pit is drawn at its true depth

            for (int r = 1; r <= 2; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != r) continue;
                    if (KindAt(cx + dx, cz + dz) != TerrainEdits.Kind.Pit) return PitDepth1 + PitDepthStep * (r - 1);
                }
            }
            return PitDepthMax;
        }

        /// <summary>b15: the lift of a lift-type cell (0 off the map, without the layer, or for any other cell type).</summary>
        int LiftOf(int cx, int cz)
        {
            if (Lift == null || !In(cx, cz)) return 0;
            int i = cz * _w + cx;
            if (_cells[i] == Cell.Bridge) return Lift[i] > 0 ? Lift[i] : 0;   // a height ladder on a stack: the stack under it
            return CriticalMass.Sim.Core.TerrainHeight.IsLiftType(_cells[i]) ? Lift[i] : 0;
        }

        static float Hash01(int cx, int cz, int salt) => (WorldDresser.H32(cx, cz, salt) % 1000u) / 1000f;

        static Color32 Jit(Color32 c, int cx, int cz, int salt)
        {
            float k = 0.94f + 0.12f * Hash01(cx, cz, salt);
            return new Color32((byte)Mathf.Min(255, c.r * k), (byte)Mathf.Min(255, c.g * k), (byte)Mathf.Min(255, c.b * k), 0);
        }

        // ------------------------------------------------------------------------------------------------ pit

        void Pit(int cx, int cz)
        {
            PitCells++;
            double h = H(cx, cz);
            double yf = h - Depth(cx, cz);
            for (int s = 0; s < 4; s++)
            {
                int nx = cx + DX[s], nz = cz + DZ[s];
                _step[s] = double.NaN;
                if (KindAt(nx, nz) == TerrainEdits.Kind.Pit)
                {
                    _rim[s] = false;
                    double yfn = H(nx, nz) - Depth(nx, nz);
                    if (yfn > yf + 0.01) _step[s] = yfn;
                }
                else _rim[s] = true;
            }
            double tW = _rim[0] ? Taper : 0, tE = _rim[1] ? Taper : 0, tS = _rim[2] ? Taper : 0, tN = _rim[3] ? Taper : 0;

            // Floor: packed dirt, inset where a rim wall tapers in.
            Color32 floor = Jit(FloorDirt, cx, cz, 11);
            QuadSim(cx + tW, yf, cz + tS, cx + 1 - tE, yf, cz + tS, cx + 1 - tE, yf, cz + 1 - tN, cx + tW, yf, cz + 1 - tN, 0, 1, 0, floor, 0f, default);
            PitFloorFaces++;
            MinFloorY = Mathf.Min(MinFloorY, (float)yf);
            MaxFloorY = Mathf.Max(MaxFloorY, (float)yf);

            if (_rim[0]) RimWall(cx, cz, h, yf, cx, cz, cx, cz + 1, 1, 0, tS, tN, 0);
            if (_rim[1]) RimWall(cx, cz, h, yf, cx + 1, cz, cx + 1, cz + 1, -1, 0, tS, tN, 1);
            if (_rim[2]) RimWall(cx, cz, h, yf, cx, cz, cx + 1, cz, 0, 1, tW, tE, 2);
            if (_rim[3]) RimWall(cx, cz, h, yf, cx, cz + 1, cx + 1, cz + 1, 0, -1, tW, tE, 3);
            for (int s = 0; s < 4; s++) if (!double.IsNaN(_step[s])) StepWall(cx, cz, s, _step[s], yf);

            // Two pebbles on the floor.
            for (int k = 0; k < 2; k++)
            {
                double px = cx + 0.25 + 0.5 * Hash01(cx, cz, 21 + k), pz = cz + 0.25 + 0.5 * Hash01(cx, cz, 31 + k);
                double sz = 0.05 + 0.04 * Hash01(cx, cz, 41 + k);
                Lump(px, yf, pz, sz, 0.05 + 0.05 * Hash01(cx, cz, 51 + k), Jit(Pebble, cx, cz, 61 + k));
                Pebbles++;
            }
            // A spoil lump on the untouched ground just outside each walled rim.
            for (int s = 0; s < 4; s++)
            {
                if (!_rim[s]) continue;
                int nx = cx + DX[s], nz = cz + DZ[s];
                byte nc = Cur(nx, nz);
                if ((nc != Cell.Floor && nc != Cell.BreakableDestroyed) || KindAt(nx, nz) != TerrainEdits.Kind.None) continue;
                double along = (Hash01(cx, cz, 71 + s) - 0.5) * 0.5;
                double mx = cx + 0.5 + DX[s] * 0.68 + (DX[s] == 0 ? along : 0);
                double mz = cz + 0.5 + DZ[s] * 0.68 + (DZ[s] == 0 ? along : 0);
                Lump(mx, H(nx, nz) - 0.01, mz, 0.11 + 0.04 * Hash01(cx, cz, 81 + s), 0.08 + 0.05 * Hash01(cx, cz, 91 + s), Jit(Spoil, cx, cz, 101 + s));
                SpoilLumps++;
            }
        }

        /// <summary>
        /// One rim wall: the top edge (ax, az)-(bx, bz) on the cell boundary at height h, the bottom edge tapered Taper inward
        /// (ix, iz) at the floor yf and inset ta / tb along the edge at its ends (so it meets the neighbouring walls' corners).
        /// Earth strata bands, two segments along the wall with their own ragged soil depth.
        /// </summary>
        void RimWall(int cx, int cz, double h, double yf, double ax, double az, double bx, double bz, int ix, int iz, double ta, double tb, int side)
        {
            double dh = h - yf;
            double ux = bx - ax, uz = bz - az;   // unit along the edge
            double b0x = ax + ix * Taper + ux * ta, b0z = az + iz * Taper + uz * ta;
            double b1x = bx + ix * Taper - ux * tb, b1z = bz + iz * Taper - uz * tb;
            double nl = System.Math.Sqrt(dh * dh + Taper * Taper);
            double nx = ix * dh / nl, ny = Taper / nl, nz = iz * dh / nl;
            for (int seg = 0; seg < 2; seg++)
            {
                double u0 = seg * 0.5, u1 = u0 + 0.5;
                double soil = System.Math.Min(dh * 0.72, 0.46 + 0.2 * Hash01(cx * 4 + side, cz, 7 + seg));
                double rockSplit = soil + (dh - soil) * 0.5;
                double d0 = 0;
                for (int band = 0; band < 5; band++)
                {
                    double d1 = band == 0 ? 0.05 : band == 1 ? 0.24 : band == 2 ? soil : band == 3 ? rockSplit : dh;
                    if (d1 > dh) d1 = dh;
                    if (d1 <= d0 + 1e-4) continue;
                    Color32 c = band == 0 ? GrassDark : band == 1 ? Topsoil : band == 2 ? Soil : band == 3 ? RockLight : RockDark;
                    c = Jit(c, cx, cz, 200 + side * 10 + band);
                    WallPoint(ax, az, bx, bz, b0x, b0z, b1x, b1z, h, dh, u0, d0, out double p0x, out double p0y, out double p0z);
                    WallPoint(ax, az, bx, bz, b0x, b0z, b1x, b1z, h, dh, u1, d0, out double p1x, out double p1y, out double p1z);
                    WallPoint(ax, az, bx, bz, b0x, b0z, b1x, b1z, h, dh, u1, d1, out double p2x, out double p2y, out double p2z);
                    WallPoint(ax, az, bx, bz, b0x, b0z, b1x, b1z, h, dh, u0, d1, out double p3x, out double p3y, out double p3z);
                    QuadSim(p0x, p0y, p0z, p1x, p1y, p1z, p2x, p2y, p2z, p3x, p3y, p3z, nx, ny, nz, c, 0f, default);
                    d0 = d1;
                }
            }
            PitWallFaces++;
        }

        static void WallPoint(double ax, double az, double bx, double bz, double b0x, double b0z, double b1x, double b1z,
            double h, double dh, double u, double depth, out double x, out double y, out double z)
        {
            double v = dh > 0 ? depth / dh : 0;
            double tx = ax + (bx - ax) * u, tz = az + (bz - az) * u;
            double qx = b0x + (b1x - b0x) * u, qz = b0z + (b1z - b0z) * u;
            x = tx + (qx - tx) * v;
            z = tz + (qz - tz) * v;
            y = h - depth;
        }

        /// <summary>A vertical rock step on side s from a shallower neighbouring pit's floor (yTop) down to this floor.</summary>
        void StepWall(int cx, int cz, int s, double yTop, double yf)
        {
            double ax, az, bx, bz;
            int ix = -DX[s], iz = -DZ[s];
            if (s == 0) { ax = cx; az = cz; bx = cx; bz = cz + 1; }
            else if (s == 1) { ax = cx + 1; az = cz; bx = cx + 1; bz = cz + 1; }
            else if (s == 2) { ax = cx; az = cz; bx = cx + 1; bz = cz; }
            else { ax = cx; az = cz + 1; bx = cx + 1; bz = cz + 1; }
            double mid = (yTop + yf) * 0.5;
            QuadSim(ax, yTop, az, bx, yTop, bz, bx, mid, bz, ax, mid, az, ix, 0, iz, Jit(RockLight, cx, cz, 300 + s), 0f, default);
            QuadSim(ax, mid, az, bx, mid, bz, bx, yf, bz, ax, yf, az, ix, 0, iz, Jit(RockDark, cx, cz, 310 + s), 0f, default);
            StepWallFaces++;
        }

        // ------------------------------------------------------------------------------------------------ blocks

        /// <summary>A placed (out into the void) or filled (back into a pit) earth block.</summary>
        void Earth(int cx, int cz, bool placed)
        {
            BlockCells++;
            double top = H(cx, cz);
            double bottom = top - PlacedDepth;
            Vector3 centre = SimSpace.ToWorld(cx + 0.5, (top + bottom) * 0.5, cz + 0.5);
            BevelTop(cx, cz, top, EarthBevel, Jit(Grass, cx, cz, 401), Jit(GrassDark, cx, cz, 402), centre);
            for (int s = 0; s < 4; s++)
            {
                int nx = cx + DX[s], nz = cz + DZ[s];
                if (TerrainEdits.Land(Cur(nx, nz)) || KindAt(nx, nz) == TerrainEdits.Kind.Pit) continue;   // hidden, or the pit's wall
                Side(cx, cz, s, top - EarthBevel, bottom, false, centre);
                BlockSideFaces++;
            }
            if (placed)
            {
                QuadSim(cx, bottom, cz, cx + 1, bottom, cz, cx + 1, bottom, cz + 1, cx, bottom, cz + 1, 0, -1, 0, Jit(RockDark, cx, cz, 403), 1f, centre);
                BlockBottomFaces++;
            }
        }

        /// <summary>
        /// b15: a raised cell: lift courses of dressed stone (StepU each) with a bevelled cap; a side shows only where the
        /// neighbour's top is lower, each course with its mortar line, footing and end seams, so a stack reads as N blocks.
        /// </summary>
        void Raised(int cx, int cz)
        {
            RaisedCells++;
            int lift = LiftOf(cx, cz);
            double g = H(cx, cz);
            double top = g + lift * StepU;
            Vector3 centre = SimSpace.ToWorld(cx + 0.5, (top + g) * 0.5, cz + 0.5);
            BevelTop(cx, cz, top, StoneBevel, Jit(StoneTop, cx, cz, 701), Jit(StoneSide, cx, cz, 702), centre);
            for (int s = 0; s < 4; s++)
            {
                int nx = cx + DX[s], nz = cz + DZ[s];
                double nb;
                if (KindAt(nx, nz) == TerrainEdits.Kind.Pit) nb = H(nx, nz);                      // the pit's rim wall goes on from there
                else if (TerrainEdits.Land(Cur(nx, nz))) nb = H(nx, nz) + System.Math.Max(0, LiftOf(nx, nz)) * StepU;
                else nb = g - PlacedDepth;                                                         // out over the void: show the block's earth
                if (nb >= top - StoneBevel - 1e-4) continue;
                for (int k = lift; k >= 1; k--)
                {
                    double c0 = g + k * StepU, c1 = g + (k - 1) * StepU;
                    if (k == lift) c0 = top - StoneBevel;
                    if (c1 < nb) c1 = nb;
                    if (c0 <= c1 + 1e-4) continue;
                    Side(cx, cz, s, c0, c1, true, centre);
                    RaisedCourses++;
                }
                if (nb < g - 1e-4) { Side(cx, cz, s, g, nb, false, centre); BlockSideFaces++; }
            }
        }

        /// <summary>A short wall / block piece: a dressed stone block on the cell, top ShortTop above its ground.</summary>
        void ShortWall(int cx, int cz)
        {
            ShortWallCells++;
            double g = H(cx, cz);
            double top = g + ShortTop, bottom = g - ShortSink;
            Vector3 centre = SimSpace.ToWorld(cx + 0.5, (top + bottom) * 0.5, cz + 0.5);
            BevelTop(cx, cz, top, StoneBevel, Jit(StoneTop, cx, cz, 501), Jit(StoneSide, cx, cz, 502), centre);
            for (int s = 0; s < 4; s++)
            {
                int nx = cx + DX[s], nz = cz + DZ[s];
                if (Cur(nx, nz) == Cell.LowWall) continue;   // the face between two wall cells never shows
                double sb = KindAt(nx, nz) == TerrainEdits.Kind.Pit ? g : bottom;   // the pit's rim wall starts at g
                Side(cx, cz, s, top - StoneBevel, sb, true, centre);
                ShortWallSideFaces++;
            }
        }

        /// <summary>Top face inset by `bevel` and four chamfers down to the cell boundary at top - bevel.</summary>
        void BevelTop(int cx, int cz, double top, double bevel, Color32 cTop, Color32 cCham, Vector3 centre)
        {
            double y1 = top - bevel;
            double i0x = cx + bevel, i1x = cx + 1 - bevel, i0z = cz + bevel, i1z = cz + 1 - bevel;
            QuadSim(i0x, top, i0z, i1x, top, i0z, i1x, top, i1z, i0x, top, i1z, 0, 1, 0, cTop, 1f, centre);
            const double k = 0.70710678;
            QuadSim(cx, y1, cz, cx, y1, cz + 1, i0x, top, i1z, i0x, top, i0z, -k, k, 0, cCham, 1f, centre);              // -x
            QuadSim(cx + 1, y1, cz, i1x, top, i0z, i1x, top, i1z, cx + 1, y1, cz + 1, k, k, 0, cCham, 1f, centre);      // +x
            QuadSim(cx, y1, cz, i0x, top, i0z, i1x, top, i0z, cx + 1, y1, cz, 0, k, -k, cCham, 1f, centre);              // -z
            QuadSim(cx, y1, cz + 1, cx + 1, y1, cz + 1, i1x, top, i1z, i0x, top, i1z, 0, k, k, cCham, 1f, centre);      // +z
        }

        /// <summary>
        /// One outer side (s: 0 -x, 1 +x, 2 -z, 3 +z) from y0 down to y1: horizontal bands (earth strata, or stone courses with
        /// a mortar line and a dark footing), each split in three along the side so both ends carry a dark vertical seam.
        /// </summary>
        void Side(int cx, int cz, int s, double y0, double y1, bool stone, Vector3 centre)
        {
            if (y0 <= y1) return;
            double ax, az, bx, bz;
            if (s == 0) { ax = cx; az = cz + 1; bx = cx; bz = cz; }
            else if (s == 1) { ax = cx + 1; az = cz; bx = cx + 1; bz = cz + 1; }
            else if (s == 2) { ax = cx; az = cz; bx = cx + 1; bz = cz; }
            else { ax = cx + 1; az = cz + 1; bx = cx; bz = cz + 1; }
            double nx = DX[s], nz = DZ[s];
            double hgt = y0 - y1;
            double d0 = 0;
            for (int band = 0; band < 5; band++)
            {
                double d1;
                Color32 c;
                if (stone)
                {
                    double mortar = hgt * 0.5;
                    d1 = band == 0 ? mortar - 0.0125 : band == 1 ? mortar + 0.0125 : band == 2 ? hgt - 0.08 : hgt;
                    c = band == 1 ? Seam : band == 3 ? StoneFoot : StoneSide;
                    if (band == 4) break;
                }
                else
                {
                    d1 = band == 0 ? 0.22 : band == 1 ? 0.5 : band == 2 ? 0.75 : hgt;
                    c = band == 0 ? Topsoil : band == 1 ? Soil : band == 2 ? RockLight : RockDark;
                    if (band == 4) break;
                }
                if (d1 > hgt) d1 = hgt;
                if (d1 <= d0 + 1e-4) { if (d1 >= hgt) break; continue; }
                c = Jit(c, cx, cz, 600 + s * 10 + band);
                double ya = y0 - d0, yb = y0 - d1;
                SeamStrip(ax, az, bx, bz, 0.0, 0.035, ya, yb, nx, nz, Seam, centre);
                SeamStrip(ax, az, bx, bz, 0.035, 0.965, ya, yb, nx, nz, c, centre);
                SeamStrip(ax, az, bx, bz, 0.965, 1.0, ya, yb, nx, nz, Seam, centre);
                d0 = d1;
                if (d1 >= hgt) break;
            }
        }

        void SeamStrip(double ax, double az, double bx, double bz, double u0, double u1, double ya, double yb, double nx, double nz, Color32 c, Vector3 centre)
        {
            double x0 = ax + (bx - ax) * u0, z0 = az + (bz - az) * u0, x1 = ax + (bx - ax) * u1, z1 = az + (bz - az) * u1;
            QuadSim(x0, ya, z0, x1, ya, z1, x1, yb, z1, x0, yb, z0, nx, 0, nz, c, 1f, centre);
        }

        /// <summary>A low truncated pyramid (pebble, spoil lump) at sim (x, z) standing on y: half size s, height t.</summary>
        void Lump(double x, double y, double z, double s, double t, Color32 c)
        {
            double r = s * 0.55;
            Vector3 centre = SimSpace.ToWorld(x, y + t * 0.3, z);
            double yt = y + t;
            QuadSim(x - r, yt, z - r, x + r, yt, z - r, x + r, yt, z + r, x - r, yt, z + r, 0, 1, 0, c, 1f, centre);
            double sl = s - r, nl = System.Math.Sqrt(sl * sl + t * t), ny = sl / nl, nh = t / nl;
            QuadSim(x - s, y, z - s, x - s, y, z + s, x - r, yt, z + r, x - r, yt, z - r, -nh, ny, 0, c, 1f, centre);
            QuadSim(x + s, y, z - s, x + r, yt, z - r, x + r, yt, z + r, x + s, y, z + s, nh, ny, 0, c, 1f, centre);
            QuadSim(x - s, y, z - s, x - r, yt, z - r, x + r, yt, z - r, x + s, y, z - s, 0, ny, -nh, c, 1f, centre);
            QuadSim(x - s, y, z + s, x + s, y, z + s, x + r, yt, z + r, x - r, yt, z + r, 0, ny, nh, c, 1f, centre);
        }

        // ------------------------------------------------------------------------------------------------ quads

        /// <summary>
        /// One quad from four sim corners in cyclic order with a sim normal; winding picked after the conversion so the front
        /// faces along the normal. Outline weight w (UV4.x): 0 = no hull; the hull direction (UV3) points away from `centre`.
        /// </summary>
        void QuadSim(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz,
            double dx, double dy, double dz, double nx, double ny, double nz, Color32 col, float w, Vector3 centre)
        {
            Vector3 a = SimSpace.ToWorld(ax, ay, az), b = SimSpace.ToWorld(bx, by, bz), c = SimSpace.ToWorld(cx, cy, cz), d = SimSpace.ToWorld(dx, dy, dz);
            Vector3 n = SimSpace.ToWorld(nx, ny, nz);
            int i0 = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c); Verts.Add(d);
            Normals.Add(n); Normals.Add(n); Normals.Add(n); Normals.Add(n);
            Colors.Add(col); Colors.Add(col); Colors.Add(col); Colors.Add(col);
            var wv = new Vector2(w, 0f);
            Weights.Add(wv); Weights.Add(wv); Weights.Add(wv); Weights.Add(wv);
            if (w > 0f)
            {
                Outline.Add(Dir(a - centre, n)); Outline.Add(Dir(b - centre, n)); Outline.Add(Dir(c - centre, n)); Outline.Add(Dir(d - centre, n));
            }
            else
            {
                Outline.Add(n); Outline.Add(n); Outline.Add(n); Outline.Add(n);
            }
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0)
            {
                Tris.Add(i0); Tris.Add(i0 + 1); Tris.Add(i0 + 2);
                Tris.Add(i0); Tris.Add(i0 + 2); Tris.Add(i0 + 3);
            }
            else
            {
                Tris.Add(i0); Tris.Add(i0 + 2); Tris.Add(i0 + 1);
                Tris.Add(i0); Tris.Add(i0 + 3); Tris.Add(i0 + 2);
            }
        }

        static Vector3 Dir(Vector3 v, Vector3 fallback)
        {
            float m = v.magnitude;
            return m > 1e-5f ? v / m : fallback;
        }
    }
}

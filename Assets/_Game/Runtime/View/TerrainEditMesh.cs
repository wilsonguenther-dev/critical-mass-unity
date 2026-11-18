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
    /// rim down: an overhanging grass lip, topsoil, soil with a ragged lower edge, light rock, dark rock; shading normals
    /// leaned toward up so all four walls take the same lit band), each wall tapered 0.07 u
    /// inward at the bottom (a dug look; the corners of two walls share one edge, so there is no gap), a packed-dirt floor
    /// with a trodden patch and two pebbles, a step wall down to a deeper neighbouring pit, and a low six-sided spoil heap on
    /// the untouched ground at about two walled rims in three. Depth grows toward the middle of a dug area: 0.9 u for a cell touching unbroken ground, 1.35 u one ring
    /// in, 1.8 u from two rings in (deeper digs show taller walls). The floor closes the hole: the island's underside is
    /// 6 u down, so no pit reaches it and none is see-through.</item>
    /// <item>PLACED / FILLED: an earth block, top at the cell's height with a 0.06 u bevel all round (a V seam between
    /// neighbouring blocks and around a fill), a grass cap, strata on every side that shows (sides only toward the void;
    /// toward a pit the pit's wall is drawn) in the island cliff's rock colours, and for a block placed out into the void a
    /// body 1.0 u deep whose underside tapers in to a rock keel (UnderInset / UnderDepth) like the island's own underside.</item>
    /// <item>SHORT WALL / BLOCK piece (Cell.LowWall): a dressed-stone block standing on the cell, top exactly WallView.ShortTop
    /// above the ground, 0.1 u bevelled top (WallView's team inlay sits inside it), two courses with a mortar line and a
    /// staggered joint in the top course, a dark footing band, vertical seams at each cell end,
    /// faces between two wall cells left out (they never show), so a run of blocks reads as stacked stones with seams.</item>
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
        public const float EarthBevel = 0.06f, StoneBevel = 0.1f;
        /// <summary>A short wall's top above its ground (the ghost and WallView's team cap use the same number).</summary>
        public const float ShortTop = B13.WallView.ShortTop;
        /// <summary>A short wall's footing below its ground (hidden by the ground, closes any seam with the ground art).</summary>
        public const float ShortSink = 0.02f;

        // Palette (sRGB, alpha 0 = CMToon mask code 0, a fixed colour). Review 2026-11-17 (in-engine, night seed 1337): the
        // first palette read near-black inside a pit and on every block side; earth tones are lifted and warmed so the strata
        // separate at the gameplay camera, and the stone matches the tall-wall kit's blue-grey instead of slate.
        static readonly Color32 Grass = C(163, 193, 80), GrassDark = C(128, 156, 62);
        static readonly Color32 Topsoil = C(116, 80, 50), Soil = C(158, 114, 72);
        static readonly Color32 RockLight = C(150, 146, 134), RockDark = C(112, 110, 102);
        static readonly Color32 FloorDirt = C(140, 104, 68), FloorClod = C(118, 86, 56), Pebble = C(168, 168, 158), Spoil = C(156, 114, 72), SpoilTop = C(176, 132, 86);
        static readonly Color32 StoneTop = C(196, 198, 200), StoneSide = C(152, 156, 166), StoneFoot = C(118, 122, 132), Seam = C(84, 86, 96);
        static readonly Color32 CliffRock = C(128, 130, 122), CliffDark = C(96, 98, 92);
        /// <summary>Pit faces lean their shading normal this far toward up (0 = true wall normal): every side of a pit then
        /// takes the same lit band instead of one wall lit and three in the shadow band (they read as a black hole).</summary>
        public const float PitNormalUp = 0.75f;
        /// <summary>The grass lip that overhangs a pit's cut edge (sim units, inward from the cell boundary / its drop).</summary>
        public const float LipWidth = 0.08f, LipDrop = 0.07f;

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
            MinFloorY = float.MaxValue; MaxFloorY = float.MinValue;
        }

        /// <summary>True when cell (current type, kind) is drawn by this builder (the chunk view then skips its own box).</summary>
        public static bool Owns(byte current, TerrainEdits.Kind kind) => kind != TerrainEdits.Kind.None || current == Cell.LowWall;

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
            return TerrainEdits.Classify(p, _cells[i], _tc != null && _tc[i]);
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
            // A trodden darker patch on the floor (irregular quad, 1 cm up) so the floor is not one flat fill.
            {
                double fx = cx + 0.3 + 0.25 * Hash01(cx, cz, 12), fz = cz + 0.3 + 0.25 * Hash01(cx, cz, 13);
                double r0 = 0.16 + 0.08 * Hash01(cx, cz, 14), r1 = 0.12 + 0.08 * Hash01(cx, cz, 15);
                double yc = yf + 0.01;
                QuadSim(fx - r0, yc, fz - r1 * 0.6, fx + r1, yc, fz - r0, fx + r0 * 0.8, yc, fz + r1, fx - r1 * 0.7, yc, fz + r0 * 0.9, 0, 1, 0, Jit(FloorClod, cx, cz, 16), 0f, default);
            }
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
                // Not every rim gets a heap (a ring of identical heaps reads as a pattern): about two in three.
                if (Hash01(cx, cz, 111 + s) < 0.3) continue;
                double along = (Hash01(cx, cz, 71 + s) - 0.5) * 0.4;
                double mx = cx + 0.5 + DX[s] * 0.74 + (DX[s] == 0 ? along : 0);
                double mz = cz + 0.5 + DZ[s] * 0.74 + (DZ[s] == 0 ? along : 0);
                double len = 0.26 + 0.08 * Hash01(cx, cz, 81 + s), wid = 0.15 + 0.04 * Hash01(cx, cz, 121 + s);
                Mound(mx, H(nx, nz) - 0.015, mz, DX[s] == 0 ? len : wid, DX[s] == 0 ? wid : len, 0.08 + 0.04 * Hash01(cx, cz, 91 + s), cx, cz, 101 + s * 7);
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
            // Shading normal leaned toward up (PitNormalUp), so all four walls share one band.
            nx *= 1 - PitNormalUp; nz *= 1 - PitNormalUp; ny = ny * (1 - PitNormalUp) + PitNormalUp;
            double nn = System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= nn; ny /= nn; nz /= nn;
            Lip(cx, cz, h, ax, az, bx, bz, ix, iz, ta > 0, tb > 0);
            for (int seg = 0; seg < 2; seg++)
            {
                double u0 = seg * 0.5, u1 = u0 + 0.5;
                double soil = System.Math.Min(dh * 0.62, 0.36 + 0.18 * Hash01(cx * 4 + side, cz, 7 + seg));
                double rockSplit = soil + (dh - soil) * 0.55;
                double d0 = LipDrop;
                for (int band = 1; band < 5; band++)
                {
                    double d1 = band == 1 ? 0.2 + 0.05 * Hash01(cx * 4 + side, cz, 17 + seg) : band == 2 ? soil : band == 3 ? rockSplit : dh;
                    if (d1 > dh) d1 = dh;
                    if (d1 <= d0 + 1e-4) continue;
                    Color32 c = band == 1 ? Topsoil : band == 2 ? Soil : band == 3 ? RockLight : RockDark;
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

        /// <summary>
        /// The grass lip over a rim wall's top: a top strip LipWidth wide (mitred where the neighbouring side is a rim too, so
        /// two lips never overlap), its front face LipDrop deep, and the underside back to the wall. A lit grass line round
        /// the cut edge is what makes the hole read as dug from the gameplay camera.
        /// </summary>
        void Lip(int cx, int cz, double h, double ax, double az, double bx, double bz, int ix, int iz, bool mitreA, bool mitreB)
        {
            double ux = bx - ax, uz = bz - az;
            double y = h, yb = h - LipDrop;
            double ia = mitreA ? LipWidth : 0, ib = mitreB ? LipWidth : 0;
            double a2x = ax + ix * LipWidth + ux * ia, a2z = az + iz * LipWidth + uz * ia;
            double b2x = bx + ix * LipWidth - ux * ib, b2z = bz + iz * LipWidth - uz * ib;
            const double up = 0.6;
            Color32 g = Jit(Grass, cx, cz, 701);
            QuadSim(ax, y, az, bx, y, bz, b2x, y, b2z, a2x, y, a2z, 0, 1, 0, g, 0f, default);
            QuadSim(a2x, y, a2z, b2x, y, b2z, b2x, yb, b2z, a2x, yb, a2z, ix * (1 - up), up, iz * (1 - up), Jit(GrassDark, cx, cz, 702), 0f, default);
            double wa = 0.005;
            QuadSim(ax + ix * wa, yb, az + iz * wa, bx + ix * wa, yb, bz + iz * wa, b2x, yb, b2z, a2x, yb, a2z, ix * 0.3, -0.95, iz * 0.3, Jit(Topsoil, cx, cz, 703), 0f, default);
        }

        /// <summary>
        /// A low spoil mound at sim (x, z) on ground y: an octagon of half-extents (rx, rz) rising t to a smaller top, lighter
        /// on top. Reads as a heap of soil, not a box (the first lumps were truncated cubes: "chocolate bricks").
        /// </summary>
        void Mound(double x, double y, double z, double rx, double rz, double t, int cx, int cz, int salt)
        {
            Vector3 centre = SimSpace.ToWorld(x, y - 0.05, z);
            Color32 side = Jit(Spoil, cx, cz, salt), top = Jit(SpoilTop, cx, cz, salt + 1);
            const double k = 0.5;   // top ring scale
            double yt = y + t;
            for (int s = 0; s < 6; s++)
            {
                int s1 = (s + 1) % 6;
                double j0 = 0.85 + 0.3 * Hash01(cx * 8 + s, cz, salt + 3), j1 = 0.85 + 0.3 * Hash01(cx * 8 + s1, cz, salt + 3);
                double px0 = x + HexC[s] * rx * j0, pz0 = z + HexS[s] * rz * j0, px1 = x + HexC[s1] * rx * j1, pz1 = z + HexS[s1] * rz * j1;
                _hx[s] = x + HexC[s] * rx * k * j0; _hz[s] = z + HexS[s] * rz * k * j0;
                double qx1 = x + HexC[s1] * rx * k * j1, qz1 = z + HexS[s1] * rz * k * j1;
                double nx = HexMC[s] * t, nz = HexMS[s] * t, ny = (rx + rz) * 0.5 * (1 - k);
                QuadSim(px0, y, pz0, px1, y, pz1, qx1, yt, qz1, _hx[s], yt, _hz[s], nx, ny, nz, side, 1f, centre);
            }
            // the top hexagon as two quads
            QuadSim(_hx[0], yt, _hz[0], _hx[1], yt, _hz[1], _hx[2], yt, _hz[2], _hx[3], yt, _hz[3], 0, 1, 0, top, 1f, centre);
            QuadSim(_hx[0], yt, _hz[0], _hx[3], yt, _hz[3], _hx[4], yt, _hz[4], _hx[5], yt, _hz[5], 0, 1, 0, top, 1f, centre);
        }

        static readonly double[] HexC = new double[6], HexS = new double[6], HexMC = new double[6], HexMS = new double[6];
        readonly double[] _hx = new double[6], _hz = new double[6];
        static TerrainEditMesh()
        {
            for (int s = 0; s < 6; s++)
            {
                double a = (s + 0.5) * System.Math.PI / 3, am = (s + 1.0) * System.Math.PI / 3;
                HexC[s] = System.Math.Cos(a); HexS[s] = System.Math.Sin(a); HexMC[s] = System.Math.Cos(am); HexMS[s] = System.Math.Sin(am);
            }
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
            double lx = ix * (1 - PitNormalUp), ly = PitNormalUp, lz = iz * (1 - PitNormalUp);
            QuadSim(ax, yTop, az, bx, yTop, bz, bx, mid, bz, ax, mid, az, lx, ly, lz, Jit(RockLight, cx, cz, 300 + s), 0f, default);
            QuadSim(ax, mid, az, bx, mid, bz, bx, yf, bz, ax, yf, az, lx, ly, lz, Jit(RockDark, cx, cz, 310 + s), 0f, default);
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
            bool any = false;
            for (int s = 0; s < 4; s++)
            {
                int nx = cx + DX[s], nz = cz + DZ[s];
                _rim[s] = !(TerrainEdits.Land(Cur(nx, nz)) || KindAt(nx, nz) == TerrainEdits.Kind.Pit);   // else hidden, or the pit's wall
                if (!_rim[s]) continue;
                Side(cx, cz, s, top - EarthBevel, bottom, false, centre);
                BlockSideFaces++;
                any = true;
            }
            if (placed)
            {
                // Review 2026-11-17: a flat bottom 1 u down read as a floating slab ("a big brick"). The underside now tapers
                // in on every exposed side to a rock keel, like the island's own underside.
                double ins = any ? UnderInset : 0, tip = bottom - (any ? UnderDepth : 0);
                double x0 = cx + (_rim[0] ? ins : 0), x1 = cx + 1 - (_rim[1] ? ins : 0);
                double z0 = cz + (_rim[2] ? ins : 0), z1 = cz + 1 - (_rim[3] ? ins : 0);
                Color32 ud = Jit(CliffDark, cx, cz, 404);
                double sl = UnderDepth, sn = System.Math.Sqrt(sl * sl + ins * ins);
                double hn = ins > 0 ? sl / sn : 1, vn = ins > 0 ? -ins / sn : 0;
                if (_rim[0]) QuadSim(cx, bottom, cz, cx, bottom, cz + 1, x0, tip, z1, x0, tip, z0, -hn, vn, 0, ud, 1f, centre);
                if (_rim[1]) QuadSim(cx + 1, bottom, cz, x1, tip, z0, x1, tip, z1, cx + 1, bottom, cz + 1, hn, vn, 0, ud, 1f, centre);
                if (_rim[2]) QuadSim(cx, bottom, cz, x0, tip, z0, x1, tip, z0, cx + 1, bottom, cz, 0, vn, -hn, ud, 1f, centre);
                if (_rim[3]) QuadSim(cx, bottom, cz + 1, cx + 1, bottom, cz + 1, x1, tip, z1, x0, tip, z1, 0, vn, hn, ud, 1f, centre);
                QuadSim(x0, tip, z0, x1, tip, z0, x1, tip, z1, x0, tip, z1, 0, -1, 0, Jit(RockDark, cx, cz, 403), 1f, centre);
                BlockBottomFaces++;
            }
        }

        /// <summary>A placed block's underside: inset on each exposed side over this depth below its 1 u body.</summary>
        public const float UnderInset = 0.3f, UnderDepth = 0.5f;

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
                    // Earth sides match the island's own cliff: a grass lip, topsoil, then cliff rock (the first bands
                    // were brown-on-brown and read as a brick of chocolate next to the grey island cliffs).
                    d1 = band == 0 ? 0.06 : band == 1 ? 0.26 : band == 2 ? 0.62 : hgt;
                    c = band == 0 ? GrassDark : band == 1 ? Topsoil : band == 2 ? CliffRock : CliffDark;
                    if (band == 4) break;
                }
                if (d1 > hgt) d1 = hgt;
                if (d1 <= d0 + 1e-4) { if (d1 >= hgt) break; continue; }
                c = Jit(c, cx, cz, 600 + s * 10 + band);
                double ya = y0 - d0, yb = y0 - d1;
                double e = stone ? 0.03 : 0.02;
                SeamStrip(ax, az, bx, bz, 0.0, e, ya, yb, nx, nz, Seam, centre);
                if (stone && band == 0)
                {
                    // The top course is two stones: a vertical joint off-centre (staggered by cell), so a row of blocks
                    // reads as laid masonry, not one long box.
                    double uj = 0.5 + (((cx + cz + s) & 1) == 0 ? 0.14 : -0.14);
                    SeamStrip(ax, az, bx, bz, e, uj - 0.015, ya, yb, nx, nz, c, centre);
                    SeamStrip(ax, az, bx, bz, uj - 0.015, uj + 0.015, ya, yb, nx, nz, Seam, centre);
                    SeamStrip(ax, az, bx, bz, uj + 0.015, 1 - e, ya, yb, nx, nz, Jit(c, cx + 7, cz, 650 + s), centre);
                }
                else SeamStrip(ax, az, bx, bz, e, 1 - e, ya, yb, nx, nz, c, centre);
                SeamStrip(ax, az, bx, bz, 1 - e, 1.0, ya, yb, nx, nz, Seam, centre);
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

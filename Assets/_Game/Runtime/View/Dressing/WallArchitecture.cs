using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Dressing
{
    /// <summary>
    /// World horizon 2 (2026-11-14): per-quality budget of the wall architecture. View only: the Sim's wall cells, the
    /// 1.5 u gameplay height, collision and wall damage are untouched (this dresses only static Wall cells, never a
    /// breakable, so the #98 damage stages read exactly as before). ONE mesh on ONE material (CM/WallDress), no texture,
    /// no collider, no shadow caster. Low (and VeryLow) keep only the keeps and the hill (pillars and beacon).
    /// </summary>
    public readonly struct WallArchBudget
    {
        public readonly bool Merlons, Courses, Buttresses, Gates, BridgeRopes, KeepDetail;
        public readonly int Towers, Banners, Torches;

        public WallArchBudget(bool merlons, bool courses, bool buttresses, bool gates, bool ropes, bool keepDetail,
            int towers, int banners, int torches)
        {
            Merlons = merlons; Courses = courses; Buttresses = buttresses; Gates = gates; BridgeRopes = ropes; KeepDetail = keepDetail;
            Towers = towers; Banners = banners; Torches = torches;
        }

        public static WallArchBudget For(int level)
        {
            if (level <= FramePolicy.LevelLow) return new WallArchBudget(false, false, false, false, false, false, 0, 0, 0);
            if (level == FramePolicy.LevelMedium) return new WallArchBudget(true, true, false, true, true, true, 80, 24, 24);
            return new WallArchBudget(true, true, true, true, true, true, 160, 40, 40);
        }

        /// <summary>Draw calls on every level: one lit pass (plus its depth prepass where the pipeline runs one). No ink
        /// outline pass: the stone reads by its toon bands and coping colour, and one pass keeps the cost flat.</summary>
        public const int Draws = 1;
        public const int TextureBytes = 0;
        /// <summary>Ceilings the tests hold: triangles per static Wall cell (the kit) plus a fixed part (keeps, hill, gates,
        /// ropes). A straight cell open on both sides costs 2 + 4 coping, 8 courses, 20 merlons = 34.</summary>
        public const int MaxPerCellLow = 0, MaxPerCellMedium = 44, MaxPerCellHigh = 52, MaxFixed = 5000;
        public static int MaxPerCell(int level) => level <= FramePolicy.LevelLow ? MaxPerCellLow : level == FramePolicy.LevelMedium ? MaxPerCellMedium : MaxPerCellHigh;

        /// <summary>The extra height the battlements, towers and keeps' tops gain in the low (eye-level) views, at most.
        /// From the top-down camera they gain nothing, so the gameplay view and its readability stay as they are.</summary>
        public const float MaxRise = 1.0f;
        /// <summary>Over a walkable cell, nothing of the dressing (top-down) stands more than this inside the cell from a wall face.</summary>
        public const float MaxIntrusion = 0.2f;
    }

    /// <summary>
    /// The architecture geometry. COLOR rgb tone (sRGB). UV0 x: rise (u) at full low view; y: flag (0 stone, 1 cloth that
    /// sways, 2 flame (self-lit, flickers), 3 low-only: folds into UV1.xyz from the top-down camera). UV1 xyz: the fold
    /// offset of a low-only vertex. UV2 x: sway weight, y: phase.
    /// </summary>
    public sealed class ArchMesh
    {
        public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<Vector2> U0 = new List<Vector2>(), U2 = new List<Vector2>();
        public readonly List<Vector4> U1 = new List<Vector4>();
        public readonly List<int> T = new List<int>();
        public int Tris => T.Count / 3;

        /// <summary>A vertex at sim (x, y, z) (x east, z south), sim normal; stored in Unity space.</summary>
        public int Add(double x, double y, double z, Vector3 simN, Color c, float rise = 0f, float flag = 0f)
        {
            V.Add(new Vector3((float)x, (float)y, (float)-z));
            N.Add(new Vector3(simN.x, simN.y, -simN.z));
            C.Add(c); U0.Add(new Vector2(rise, flag)); U1.Add(Vector4.zero); U2.Add(Vector2.zero);
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        public void Fold(int i, Vector3 unityOffset) => U1[i] = new Vector4(unityOffset.x, unityOffset.y, unityOffset.z, 0f);
        public void Sway(int i, float w, float phase) => U2[i] = new Vector2(w, phase);
    }

    /// <summary>What the layout made, for the report, the shots and the tests.</summary>
    public sealed class WallArchReport
    {
        public int WallCells, Coping, Merlons, Courses, Buttresses, Towers, Gates, Banners, Torches, Keeps, Pillars, RopeCells, Triangles;
        /// <summary>Sim points for eye-level shots: x, z, yaw (0 north, 90 east), degrees looking down.</summary>
        public readonly List<(string name, float x, float z, float yaw, float down)> Views = new List<(string, float, float, float, float)>();
    }

    /// <summary>
    /// A small kit of wall pieces chosen per Wall cell by its four neighbours (open sides get a coping lip, a merlon and
    /// stone courses; corners, ends and lone posts get a round tower; straight runs get a buttress now and then; a gap of
    /// 1 to 3 cells between two wall ends gets a gate arch high over the heads), varied by a cell hash (broken or missing
    /// merlons, tone), so it tiles without visible repetition. Each base gets a keep with its own silhouette off the
    /// island behind it (Azure a square spire keep, Crimson a round crown keep), banners and torches near the bases, a ring
    /// of broken beacon pillars and a floating sky-glass beacon at the hill, rope rails on the bridges.
    /// </summary>
    public static class WallArchitectureLayout
    {
        public const float WallHeight = 1.5f;   // the Sim's and the grid view's wall block (SeeThroughProbe.WallHeight)

        static readonly Color Stone = BeyondHex("#A9A396"), StoneDark = BeyondHex("#7E7A72"), Coping = BeyondHex("#C9C2B2"),
            Moss = BeyondHex("#6F7D5A"), Slate = BeyondHex("#4E5868"), Wood = BeyondHex("#7A5A3C"), Rope = BeyondHex("#C8B48A"),
            Flame = BeyondHex("#FFB347"), Azure = BeyondHex("#16C7D9"), AzureDark = BeyondHex("#0F7F8C"), Crimson = BeyondHex("#D9473B"),
            CrimsonDark = BeyondHex("#8C2A24"), Pale = BeyondHex("#EDE6D6"), Glass = BeyondHex("#A8EEF5"), Ember = BeyondHex("#FF8A3D"),
            Rock = BeyondHex("#6C7A8E"), RockDark = BeyondHex("#4F596B");

        static Color BeyondHex(string h) { ColorUtility.TryParseHtmlString(h, out Color c); return c; }

        static bool WallLike(byte t) => t == Cell.Wall || t == Cell.BreakableIntact || t == Cell.BreakableCracked;
        static bool Walkable(byte t) => t == Cell.Floor || t == Cell.BreakableDestroyed || t == Cell.Bridge;

        static uint Hash(int x, int z, uint salt)
        {
            unchecked
            {
                uint h = (uint)x * 73856093u ^ (uint)z * 19349663u ^ salt * 83492791u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return h;
            }
        }

        static float H01(int x, int z, uint salt) => (Hash(x, z, salt) & 0xFFFF) / 65535f;

        static readonly int[] Dx = { 0, 1, 0, -1 }, Dz = { -1, 0, 1, 0 };   // N, E, S, W in sim (z south)

        public static WallArchReport Build(ArchMesh m, MapDef map, in WallArchBudget b)
        {
            var rep = new WallArchReport();
            int W = map.Width, H = map.Height;
            byte T(int x, int z) => x >= 0 && z >= 0 && x < W && z < H ? map.Cells[z * W + x] : Cell.Void;
            double Y(int x, int z) => map.Height01[Mathf.Clamp(z, 0, H - 1) * W + Mathf.Clamp(x, 0, W - 1)];
            PadDef azure = map.Pads != null && map.Pads.Length > 0 ? map.Pads[(int)TeamId.Azure] : null;
            PadDef crimson = map.Pads != null && map.Pads.Length > 1 ? map.Pads[(int)TeamId.Crimson] : null;
            int team(double x, double z)
            {
                if (azure != null && Sq(x - azure.X, z - azure.Z) < 26 * 26) return 1;
                if (crimson != null && Sq(x - crimson.X, z - crimson.Z) < 26 * 26) return 2;
                return 0;
            }

            int towers = 0, banners = 0, torches = 0;
            int bestRun = 0; (float x, float z, float yaw) runView = (0, 0, 0);
            // cand4-reds-1: Skybreak's walls are the hill ring and the shrine yard, circles on the grid with their gaps at the
            // cardinal points, so they have no straight cell and the eye-level "wall-run" shot never existed. Fallback: the
            // first chained wall cell (two wall-like neighbours) with open ground beside it, seen from 3 u off that side.
            bool fbFound = false; (float x, float z, float yaw) fbView = (0, 0, 0);
            for (int cz = 0; cz < H; cz++)
            for (int cx = 0; cx < W; cx++)
            {
                if (T(cx, cz) != Cell.Wall) continue;
                rep.WallCells++;
                double y0 = Y(cx, cz), top = y0 + WallHeight;
                int mask = 0, nb = 0;
                for (int d = 0; d < 4; d++) if (WallLike(T(cx + Dx[d], cz + Dz[d]))) { mask |= 1 << d; nb++; }
                bool straight = mask == 0b0101 || mask == 0b1010;
                bool corner = nb == 2 && !straight;
                bool tower = b.Towers > 0 && (nb <= 1 || corner) && !NearBreakable(T, cx, cz) && towers < b.Towers && (nb <= 1 || (Hash(cx, cz, 7) % 2u) == 0u || Near(team(cx, cz)));
                float tone = 0.9f + 0.2f * H01(cx, cz, 1);
                int tm = team(cx + 0.5, cz + 0.5);

                if (b.Merlons)
                {
                    CopingSlab(m, cx, cz, top, mask, Mul(Coping, tone));
                    rep.Coping++;
                }
                if (tower)
                {
                    // cand4-reds-1: a tower's 0.62 u ring reaches into the neighbouring cells; next to a lower cell its roof
                    // stood more than 0.5 u over THAT cell's wall top in the top-down view. Cap the roof to the lowest
                    // neighbour (never below this wall's own top).
                    double minN = y0;
                    for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) minN = Math.Min(minN, Y(cx + i, cz + j));
                    Tower(m, cx + 0.5, cz + 0.5, y0, top, tm, H01(cx, cz, 9), minN + WallHeight + 0.5);
                    towers++;
                    rep.Towers++;
                }
                for (int d = 0; d < 4; d++)
                {
                    if ((mask & (1 << d)) != 0) continue;
                    byte n = T(cx + Dx[d], cz + Dz[d]);
                    if (b.Courses)
                    {
                        Courses(m, cx, cz, d, y0, Mul(Stone, tone), Mul(Moss, tone));
                        rep.Courses++;
                    }
                    if (b.Merlons && !tower)
                    {
                        float hz = H01(cx, cz, (uint)(20 + d));
                        if (hz > 0.05f)
                        {
                            Merlon(m, cx, cz, d, top, hz < 0.15f, Mul(hz < 0.15f ? StoneDark : Stone, tone));
                            rep.Merlons++;
                        }
                    }
                    if (b.Buttresses && straight && (Hash(cx, cz, 31) % 5u) == 0u && Walkable(n))
                    {
                        Buttress(m, cx, cz, d, y0, Mul(StoneDark, tone));
                        rep.Buttresses++;
                    }
                    if (!Walkable(n)) continue;
                    if (tm != 0 && banners < b.Banners && (Hash(cx, cz, (uint)(40 + d)) % 3u) == 0u && !tower)
                    {
                        Banner(m, cx, cz, d, top, tm, H01(cx, cz, 41) * 6.28f);
                        banners++;
                        rep.Banners++;
                    }
                    else if ((tm != 0 || NearHill(map, cx, cz)) && torches < b.Torches && (Hash(cx, cz, (uint)(50 + d)) % 6u) == 0u)
                    {
                        Torch(m, cx, cz, d, y0, H01(cx, cz, 51) * 6.28f);
                        torches++;
                        rep.Torches++;
                    }
                }
                if (!fbFound && nb == 2)
                    for (int d = 0; d < 4 && !fbFound; d++)
                    {
                        if (!Walkable(T(cx + Dx[d], cz + Dz[d])) || !Walkable(T(cx + Dx[d] * 3, cz + Dz[d] * 3))) continue;
                        float yawDeg = Mathf.Repeat(Mathf.Atan2(-Dx[d], Dz[d]) * Mathf.Rad2Deg, 360f);
                        fbView = (cx + 0.5f + Dx[d] * 3f, cz + 0.5f + Dz[d] * 3f, yawDeg);
                        fbFound = true;
                    }
                // A long straight run makes the eye-level wall shot: stand 3 u off its open side, look along it.
                if (straight && rep.Views.Count == 0)
                {
                    int run = 0;
                    bool alongX = mask == 0b1010;
                    for (int k = 1; k < 40 && T(cx + (alongX ? k : 0), cz + (alongX ? 0 : k)) == Cell.Wall; k++) run++;
                    if (run > bestRun)
                    {
                        bestRun = run;
                        int side = alongX ? (Walkable(T(cx, cz + 1)) ? 2 : 0) : (Walkable(T(cx + 1, cz)) ? 1 : 3);
                        float vx = cx + 0.5f + Dx[side] * 3f - (alongX ? 2f : 0f), vz = cz + 0.5f + Dz[side] * 3f - (alongX ? 0f : 2f);
                        float yaw = alongX ? (side == 2 ? 75f : 105f) : (side == 1 ? 195f : 165f);
                        runView = (vx, vz, yaw);
                    }
                }
            }
            if (bestRun > 0) rep.Views.Add(("wall-run", runView.x, runView.z, runView.yaw, 4f));
            else if (fbFound) rep.Views.Add(("wall-run", fbView.x, fbView.z, fbView.yaw, 4f));

            if (b.Gates) Gates(m, map, T, Y, rep);
            if (b.BridgeRopes) Ropes(m, map, T, Y, rep);
            Hill(m, map, T, Y, rep);
            if (azure != null) Keep(m, azure, true, Y((int)azure.X, (int)azure.Z), b.KeepDetail, W, rep);
            if (crimson != null) Keep(m, crimson, false, Y((int)crimson.X, (int)crimson.Z), b.KeepDetail, W, rep);
            rep.Triangles = m.Tris;
            return rep;
        }

        static bool Near(int team) => team != 0;

        /// <summary>A tower's round body spills about 0.15 u past its cell: never next to a breakable (its damage stages stay clean).</summary>
        static bool NearBreakable(Func<int, int, byte> T, int cx, int cz)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                byte t = T(cx + dx, cz + dz);
                if (t == Cell.BreakableIntact || t == Cell.BreakableCracked || t == Cell.BreakableDestroyed) return true;
            }
            return false;
        }
        static double Sq(double v, double w) => v * v + w * w;
        static Color Mul(Color c, float k) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);

        static bool NearHill(MapDef map, int cx, int cz) =>
            map.Hill != null && Sq(cx + 0.5 - map.Hill.X, cz + 0.5 - map.Hill.Z) < 14 * 14;

        // ---------------------------------------------------------------------------------------------- primitives

        /// <summary>A sim-space box (x0..x1, y0..y1, z0..z1) without its bottom; top vertices rise by rise.</summary>
        static void Box(ArchMesh m, double x0, double x1, double y0, double y1, double z0, double z1, Color c, float rise, float riseBottom = 0f, float flag = 0f)
        {
            Color side = Mul(c, 0.92f);
            int a = m.Add(x0, y1, z0, Vector3.up, c, rise, flag), bb = m.Add(x1, y1, z0, Vector3.up, c, rise, flag);
            int cc = m.Add(x1, y1, z1, Vector3.up, c, rise, flag), d = m.Add(x0, y1, z1, Vector3.up, c, rise, flag);
            m.Quad(a, d, cc, bb);   // top (wound for Unity's flipped z)
            Face(m, x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1, new Vector3(0, 0, 1), side, rise, riseBottom, flag);   // south (+z sim)
            Face(m, x1, y0, z0, x0, y0, z0, x0, y1, z0, x1, y1, z0, new Vector3(0, 0, -1), side, rise, riseBottom, flag);   // north
            Face(m, x1, y0, z1, x1, y0, z0, x1, y1, z0, x1, y1, z1, new Vector3(1, 0, 0), side, rise, riseBottom, flag);   // east
            Face(m, x0, y0, z0, x0, y0, z1, x0, y1, z1, x0, y1, z0, new Vector3(-1, 0, 0), side, rise, riseBottom, flag);   // west
        }

        /// <summary>A quad from two bottom and two top corners (counter-clockwise seen from outside in sim space).</summary>
        static void Face(ArchMesh m, double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz,
            double dx, double dy, double dz, Vector3 n, Color c, float riseTop, float riseBottom, float flag = 0f)
        {
            int a = m.Add(ax, ay, az, n, c, riseBottom, flag), b = m.Add(bx, by, bz, n, c, riseBottom, flag);
            int cc = m.Add(cx, cy, cz, n, c, riseTop, flag), d = m.Add(dx, dy, dz, n, c, riseTop, flag);
            // Sim space is left-handed against Unity (z flips), so the sim counter-clockwise order is Unity clockwise.
            m.Quad(a, d, cc, b);
        }

        /// <summary>The outward edge of side d of a cell: its centre, the along-edge unit and the outward normal.</summary>
        static void Edge(int cx, int cz, int d, out double ex, out double ez, out double ux, out double uz)
        {
            ex = cx + 0.5 + Dx[d] * 0.5; ez = cz + 0.5 + Dz[d] * 0.5;
            ux = -Dz[d]; uz = Dx[d];
        }

        // ---------------------------------------------------------------------------------------------- wall kit

        /// <summary>The coping: a pale cap slab 0.06 u proud on the open sides; faces only where they can be seen (2 + 2 per open side).</summary>
        static void CopingSlab(ArchMesh m, int cx, int cz, double top, int mask, Color c)
        {
            const double o = 0.06;
            double x0 = cx - ((mask & 8) == 0 ? o : 0), x1 = cx + 1 + ((mask & 2) == 0 ? o : 0);
            double z0 = cz - ((mask & 1) == 0 ? o : 0), z1 = cz + 1 + ((mask & 4) == 0 ? o : 0);
            double y0 = top - 0.04, y1 = top + 0.08;
            int a = m.Add(x0, y1, z0, Vector3.up, c), b = m.Add(x1, y1, z0, Vector3.up, c), cc = m.Add(x1, y1, z1, Vector3.up, c), d = m.Add(x0, y1, z1, Vector3.up, c);
            m.Quad(a, d, cc, b);
            Color side = Mul(c, 0.92f);
            if ((mask & 4) == 0) Face(m, x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1, new Vector3(0, 0, 1), side, 0f, 0f);
            if ((mask & 1) == 0) Face(m, x1, y0, z0, x0, y0, z0, x0, y1, z0, x1, y1, z0, new Vector3(0, 0, -1), side, 0f, 0f);
            if ((mask & 2) == 0) Face(m, x1, y0, z1, x1, y0, z0, x1, y1, z0, x1, y1, z1, new Vector3(1, 0, 0), side, 0f, 0f);
            if ((mask & 8) == 0) Face(m, x0, y0, z0, x0, y0, z1, x0, y1, z1, x0, y1, z0, new Vector3(-1, 0, 0), side, 0f, 0f);
        }

        /// <summary>One merlon centred on an open side; from the top-down camera it is a 0.14 u lip, in low views 0.64 u.</summary>
        static void Merlon(ArchMesh m, int cx, int cz, int d, double top, bool broken, Color c)
        {
            Edge(cx, cz, d, out double ex, out double ez, out double ux, out double uz);
            double nx = Dx[d], nz = Dz[d];
            double hw = 0.21, dep = 0.24;
            double ox = ex + nx * 0.06 - nx * dep, oz = ez + nz * 0.06 - nz * dep;   // inner face centre
            double x0 = Math.Min(ox - ux * hw, ox + ux * hw + nx * dep), x1 = Math.Max(ox - ux * hw, ox + ux * hw + nx * dep);
            double z0 = Math.Min(oz - uz * hw, oz + uz * hw + nz * dep), z1 = Math.Max(oz - uz * hw, oz + uz * hw + nz * dep);
            Box(m, x0, x1, top + 0.08, top + (broken ? 0.12 : 0.22), z0, z1, c, broken ? 0.22f : 0.5f);
        }

        /// <summary>Stone courses on an open face: a band at mid height and a mossy plinth, each a face a few cm proud (4 triangles).</summary>
        static void Courses(ArchMesh m, int cx, int cz, int d, double y0, Color stone, Color moss)
        {
            Edge(cx, cz, d, out double ex, out double ez, out double ux, out double uz);
            Band(m, ex, ez, ux, uz, Dx[d], Dz[d], y0 + 0.72, y0 + 0.8, 0.05, Mul(stone, 1.08f));
            Band(m, ex, ez, ux, uz, Dx[d], Dz[d], y0 - 0.05, y0 + 0.24, 0.08, moss);
        }

        static void Band(ArchMesh m, double ex, double ez, double ux, double uz, double nx, double nz, double ya, double yb, double outw, Color c)
        {
            double fx = ex + nx * outw, fz = ez + nz * outw;
            var n = new Vector3((float)nx, 0, (float)nz);
            Face(m, fx - ux * 0.5, ya, fz - uz * 0.5, fx + ux * 0.5, ya, fz + uz * 0.5, fx + ux * 0.5, yb, fz + uz * 0.5, fx - ux * 0.5, yb, fz - uz * 0.5, n, c, 0f, 0f);
        }

        /// <summary>A sloped buttress on an open face of a straight run (base 0.18 u out, top flush at 1.25 u).</summary>
        static void Buttress(ArchMesh m, int cx, int cz, int d, double y0, Color c)
        {
            Edge(cx, cz, d, out double ex, out double ez, out double ux, out double uz);
            double nx = Dx[d], nz = Dz[d], hw = 0.16, outB = 0.18, yt = y0 + 1.25;
            var slope = new Vector3((float)nx, 0.15f, (float)nz).normalized;
            Face(m, ex + nx * outB - ux * hw, y0 - 0.05, ez + nz * outB - uz * hw, ex + nx * outB + ux * hw, y0 - 0.05, ez + nz * outB + uz * hw,
                ex + ux * hw, yt, ez + uz * hw, ex - ux * hw, yt, ez - uz * hw, slope, c, 0f, 0f);
            var su = new Vector3((float)ux, 0, (float)uz);
            Face(m, ex + ux * hw, y0 - 0.05, ez + uz * hw, ex + nx * outB + ux * hw, y0 - 0.05, ez + nz * outB + uz * hw,
                ex + ux * hw, yt, ez + uz * hw, ex + ux * hw, yt, ez + uz * hw, su, Mul(c, 0.9f), 0f, 0f);
            Face(m, ex + nx * outB - ux * hw, y0 - 0.05, ez + nz * outB - uz * hw, ex - ux * hw, y0 - 0.05, ez - uz * hw,
                ex - ux * hw, yt, ez - uz * hw, ex - ux * hw, yt, ez - uz * hw, -su, Mul(c, 0.9f), 0f, 0f);
        }

        /// <summary>A round (octagonal) corner tower: 0.12 u proud of the wall, cone roof that grows in low views (24 tris).</summary>
        static void Tower(ArchMesh m, double x, double z, double y0, double top, int team, float h, double cap = double.MaxValue)
        {
            const int n = 8;
            double r = 0.62, yt = Math.Max(top + 0.02, Math.Min(top + 0.25, cap - 0.14));   // apex = yt + 0.12 stays under cap
            Color body = Mul(Stone, 0.95f + 0.1f * h), roof = team == 1 ? Azure : team == 2 ? Crimson : Slate;
            for (int i = 0; i < n; i++)
            {
                double a = (i + 0.5) * Math.PI * 2 / n, a2 = (i + 1.5) * Math.PI * 2 / n;
                var nrm = new Vector3((float)Math.Cos(a + Math.PI / n), 0, (float)Math.Sin(a + Math.PI / n));
                Face(m, x + Math.Cos(a) * r, y0 - 0.1, z + Math.Sin(a) * r, x + Math.Cos(a2) * r, y0 - 0.1, z + Math.Sin(a2) * r,
                    x + Math.Cos(a2) * r, yt, z + Math.Sin(a2) * r, x + Math.Cos(a) * r, yt, z + Math.Sin(a) * r, nrm, body, 0.6f, 0f);
            }
            int apex = m.Add(x, yt + 0.12, z, Vector3.up, roof, 1.0f);
            for (int i = 0; i < n; i++)
            {
                double a = (i + 0.5) * Math.PI * 2 / n, a2 = (i + 1.5) * Math.PI * 2 / n;
                var nrm = new Vector3((float)Math.Cos(a + Math.PI / n), 0.7f, (float)Math.Sin(a + Math.PI / n)).normalized;
                int p = m.Add(x + Math.Cos(a) * (r + 0.08), yt, z + Math.Sin(a) * (r + 0.08), nrm, roof, 0.6f);
                int q = m.Add(x + Math.Cos(a2) * (r + 0.08), yt, z + Math.Sin(a2) * (r + 0.08), nrm, roof, 0.6f);
                m.Tri(apex, p, q);
            }
        }

        /// <summary>A team banner hanging from the coping on an open face, two-sided, swaying (14 triangles a side).</summary>
        static void Banner(ArchMesh m, int cx, int cz, int d, double top, int team, float phase)
        {
            Edge(cx, cz, d, out double ex, out double ez, out double ux, out double uz);
            double nx = Dx[d], nz = Dz[d];
            Color main = team == 1 ? Azure : Crimson, dark = team == 1 ? AzureDark : CrimsonDark;
            double[] ys = { 0.04, -0.34, -0.46, -0.86 };
            Color[] cs = { dark, main, Pale, main };
            for (int s = 0; s < 1; s++)   // one sheet: CM/WallDress is two-sided
            {
                double off = 0.11;
                var n = new Vector3((float)(nx * (s == 0 ? 1 : -1)), 0, (float)(nz * (s == 0 ? 1 : -1)));
                var col = new int[ys.Length * 2];
                for (int k = 0; k < ys.Length; k++)
                {
                    float w = (float)(-ys[k] / 0.86);
                    for (int j = 0; j < 2; j++)
                    {
                        double u = j == 0 ? -0.18 : 0.18;
                        int v = m.Add(ex + nx * off + ux * u, top + ys[k], ez + nz * off + uz * u, n, cs[k], 0f, 1f);
                        m.Sway(v, w * 0.12f, phase);
                        col[k * 2 + j] = v;
                    }
                }
                int tip = m.Add(ex + nx * off, top - 1.0, ez + nz * off, n, main, 0f, 1f);
                m.Sway(tip, 0.14f, phase);
                for (int k = 0; k < ys.Length - 1; k++)
                {
                    int a = col[k * 2], b = col[k * 2 + 1], c = col[k * 2 + 3], dd = col[k * 2 + 2];
                    if (s == 0) m.Quad(a, dd, c, b); else m.Quad(a, b, c, dd);
                }
                int l = col[(ys.Length - 1) * 2], r = col[(ys.Length - 1) * 2 + 1];
                if (s == 0) m.Tri(l, tip, r); else m.Tri(l, r, tip);
            }
        }

        /// <summary>A torch bracket on an open face at 1.1 u, its flame self-lit and flickering (16 triangles).</summary>
        static void Torch(ArchMesh m, int cx, int cz, int d, double y0, float phase)
        {
            Edge(cx, cz, d, out double ex, out double ez, out double ux, out double uz);
            double nx = Dx[d], nz = Dz[d], y = y0 + 1.05;
            double x0 = Math.Min(ex, ex + nx * 0.14) - 0.03 * Math.Abs(ux) - 0.03 * Math.Abs(nz), x1 = Math.Max(ex, ex + nx * 0.14) + 0.03 * Math.Abs(ux) + 0.03 * Math.Abs(nz);
            double z0 = Math.Min(ez, ez + nz * 0.14) - 0.03 * Math.Abs(uz) - 0.03 * Math.Abs(nx), z1 = Math.Max(ez, ez + nz * 0.14) + 0.03 * Math.Abs(uz) + 0.03 * Math.Abs(nx);
            Box(m, x0, x1, y - 0.05, y + 0.03, z0, z1, Wood, 0f);
            double fx = ex + nx * 0.12, fz = ez + nz * 0.12;
            for (int k = 0; k < 2; k++)
            {
                double ax = k == 0 ? ux : nx, az = k == 0 ? uz : nz;
                for (int s = 0; s < 1; s++)
                {
                    var n = new Vector3((float)(-az), 0, (float)ax);
                    int a = m.Add(fx - ax * 0.06, y + 0.03, fz - az * 0.06, n, Flame, 0f, 2f), b = m.Add(fx + ax * 0.06, y + 0.03, fz + az * 0.06, n, Flame, 0f, 2f);
                    int c = m.Add(fx, y + 0.27, fz, n, Pale, 0f, 2f);
                    m.Sway(a, 0f, phase); m.Sway(b, 0f, phase); m.Sway(c, 0.03f, phase);
                    if (s == 0) m.Tri(a, c, b); else m.Tri(a, b, c);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------- gates

        /// <summary>A gap of 1 to 3 walkable cells between two wall ends in a line gets an arch 2.25 to 2.85 u up (over
        /// every head), shown only in low views: from the top-down camera it folds into the two end posts.</summary>
        static void Gates(ArchMesh m, MapDef map, Func<int, int, byte> T, Func<int, int, double> Y, WallArchReport rep)
        {
            int W = map.Width, H = map.Height;
            for (int axis = 0; axis < 2; axis++)
            for (int cz = 0; cz < H; cz++)
            for (int cx = 0; cx < W; cx++)
            {
                int sx = axis == 0 ? 1 : 0, sz = axis == 0 ? 0 : 1;
                if (T(cx, cz) != Cell.Wall || !WallLike(T(cx - sx, cz - sz))) continue;
                int k = 1;
                while (k <= 4 && Walkable(T(cx + sx * k, cz + sz * k))) k++;
                if (k < 2 || k > 4) continue;
                int bx = cx + sx * k, bz = cz + sz * k;
                if (T(bx, bz) != Cell.Wall || !WallLike(T(bx + sx, bz + sz))) continue;
                double y = Math.Max(Y(cx, cz), Y(bx, bz));
                double a0 = axis == 0 ? cx + 0.5 : cz + 0.5, a1 = axis == 0 ? bx + 0.5 : bz + 0.5, mid = (a0 + a1) * 0.5;
                double c = axis == 0 ? cz + 0.5 : cx + 0.5;
                Arch(m, axis, a0, a1, c, y + 2.25, y + 2.85, 0.36, y + WallHeight);
                rep.Gates++;
                if (rep.Gates == 1)
                {
                    double gx = axis == 0 ? mid : c + 4.0, gz = axis == 0 ? c + 4.0 : mid;
                    rep.Views.Add(("gate", (float)gx, (float)gz, axis == 0 ? 0f : 270f, 2f));
                }
            }
        }

        static void Arch(ArchMesh m, int axis, double a0, double a1, double c, double yb, double yt, double hd, double yWall)
        {
            double sp = 0.32;
            double[] us = { a0, a0 + sp, a1 - sp, a1 };
            double[] lows = { yb, yb + 0.35, yb + 0.35, yb };
            Color col = Coping, under = StoneDark;
            int V(double a, double y, double cc, Vector3 simN, Color co)
            {
                int i = axis == 0 ? m.Add(a, y, cc, simN, co, 0f, 3f) : m.Add(cc, y, a, simN, co, 0f, 3f);
                // Fold every vertex onto one point on the first post's top: zero area from the top-down camera.
                float da = (float)(a0 - a), dc = (float)(c - cc), dy = (float)(yWall - y);
                m.Fold(i, axis == 0 ? new Vector3(da, dy, -dc) : new Vector3(dc, dy, -da));
                return i;
            }
            Vector3 front = axis == 0 ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0);
            for (int s = 0; s < 2; s++)
            {
                double cc = c + (s == 0 ? hd : -hd);
                Vector3 n = s == 0 ? front : -front;
                var lo = new int[4]; var hi = new int[4];
                for (int i = 0; i < 4; i++) { lo[i] = V(us[i], lows[i], cc, n, col); hi[i] = V(us[i], yt, cc, n, col); }
                for (int i = 0; i < 3; i++)
                {
                    bool flip = (s == 0) ^ (axis == 1);
                    if (flip) m.Quad(lo[i], hi[i], hi[i + 1], lo[i + 1]); else m.Quad(lo[i], lo[i + 1], hi[i + 1], hi[i]);
                }
            }
            // Top and underside strips.
            for (int i = 0; i < 3; i++)
            {
                int t0 = V(us[i], yt, c - hd, Vector3.up, col), t1 = V(us[i + 1], yt, c - hd, Vector3.up, col);
                int t2 = V(us[i + 1], yt, c + hd, Vector3.up, col), t3 = V(us[i], yt, c + hd, Vector3.up, col);
                m.Quad(t0, t1, t2, t3);
                int u0 = V(us[i], lows[i], c - hd, Vector3.down, under), u1 = V(us[i + 1], lows[i + 1], c - hd, Vector3.down, under);
                int u2 = V(us[i + 1], lows[i + 1], c + hd, Vector3.down, under), u3 = V(us[i], lows[i], c + hd, Vector3.down, under);
                m.Quad(u0, u1, u2, u3);
            }
        }

        // ---------------------------------------------------------------------------------------------- bridges

        /// <summary>Rope rails on the bridges: a post every second cell along each open side, a sagging rope per cell.</summary>
        static void Ropes(ArchMesh m, MapDef map, Func<int, int, byte> T, Func<int, int, double> Y, WallArchReport rep)
        {
            for (int cz = 0; cz < map.Height; cz++)
            for (int cx = 0; cx < map.Width; cx++)
            {
                if (T(cx, cz) != Cell.Bridge) continue;
                double y = Y(cx, cz);
                for (int d = 0; d < 4; d++)
                {
                    if (T(cx + Dx[d], cz + Dz[d]) != Cell.Void) continue;
                    Edge(cx, cz, d, out double ex, out double ez, out double ux, out double uz);
                    double px = ex - Dx[d] * 0.08, pz = ez - Dz[d] * 0.08;
                    if (((cx + cz) & 1) == 0)
                        Box(m, px - 0.05, px + 0.05, y - 0.1, y + 0.95, pz - 0.05, pz + 0.05, Wood, 0f);
                    for (int s = 0; s < 1; s++)
                    {
                        var n = new Vector3(Dx[d], 0, Dz[d]);
                        int a = m.Add(px - ux * 0.5, y + 0.78, pz - uz * 0.5, n, Rope), b = m.Add(px, y + 0.71, pz, n, Rope);
                        int c = m.Add(px + ux * 0.5, y + 0.78, pz + uz * 0.5, n, Rope);
                        int a2 = m.Add(px - ux * 0.5, y + 0.82, pz - uz * 0.5, n, Rope), b2 = m.Add(px, y + 0.75, pz, n, Rope);
                        int c2 = m.Add(px + ux * 0.5, y + 0.82, pz + uz * 0.5, n, Rope);
                        if (s == 0) { m.Quad(a, a2, b2, b); m.Quad(b, b2, c2, c); }
                        else { m.Quad(a, b, b2, a2); m.Quad(b, c, c2, b2); }
                    }
                    rep.RopeCells++;
                }
            }
        }

        // ---------------------------------------------------------------------------------------------- hill

        /// <summary>The hill landmark: six broken beacon pillars on the hill ring's wall cells (two carry braziers) and a
        /// floating sky-glass beacon over the centre, shown only in low views (it folds to a point under the ground from the top-down camera).</summary>
        static void Hill(ArchMesh m, MapDef map, Func<int, int, byte> T, Func<int, int, double> Y, WallArchReport rep)
        {
            if (map.Hill == null) return;
            double hx = map.Hill.X, hz = map.Hill.Z;
            var best = new (int x, int z, double d)[6];
            for (int i = 0; i < 6; i++) best[i] = (-1, -1, 1e9);
            for (int cz = 0; cz < map.Height; cz++)
            for (int cx = 0; cx < map.Width; cx++)
            {
                if (T(cx, cz) != Cell.Wall) continue;
                double dx = cx + 0.5 - hx, dz = cz + 0.5 - hz, r = Math.Sqrt(dx * dx + dz * dz);
                if (r > 12.0 || r < 3.0) continue;
                double a = Math.Atan2(dz, dx) + Math.PI;
                int s = Mathf.Clamp((int)(a / (Math.PI * 2) * 6), 0, 5);
                double target = (s + 0.5) * Math.PI * 2 / 6, off = Math.Abs(Math.IEEERemainder(a - target, Math.PI * 2));
                if (off < best[s].d) best[s] = (cx, cz, off);
            }
            for (int s = 0; s < 6; s++)
            {
                if (best[s].x < 0) continue;
                double x = best[s].x + 0.5, z = best[s].z + 0.5, top = Y(best[s].x, best[s].z) + WallHeight;
                Pillar(m, x, z, top, s % 3 == 0, (float)s);
                rep.Pillars++;
            }
            // The beacon: a sky-glass octahedron 7.5 u over the hill centre, folded to a point from the top-down camera.
            double cy = Y((int)hx, (int)hz) + 7.5;
            int[] p = new int[6];
            double[,] o = { { 0, 1.3, 0 }, { 0, -1.6, 0 }, { 0.8, 0, 0 }, { -0.8, 0, 0 }, { 0, 0, 0.8 }, { 0, 0, -0.8 } };
            for (int i = 0; i < 6; i++)
            {
                p[i] = m.Add(hx + o[i, 0], cy + o[i, 1], hz + o[i, 2], new Vector3((float)o[i, 0], (float)o[i, 1], (float)o[i, 2]).normalized, Glass, 0f, 3f);
                m.Fold(p[i], new Vector3(-(float)o[i, 0], -8.5f - (float)o[i, 1], (float)o[i, 2]));   // to a point 1 u under the hill
            }
            int[,] f = { { 0, 2, 4 }, { 0, 4, 3 }, { 0, 3, 5 }, { 0, 5, 2 }, { 1, 4, 2 }, { 1, 3, 4 }, { 1, 5, 3 }, { 1, 2, 5 } };
            for (int i = 0; i < 8; i++) m.Tri(p[f[i, 0]], p[f[i, 2]], p[f[i, 1]]);
            if (map.Hill != null) rep.Views.Add(("hill-beacon", (float)hx + 9f, (float)hz + 9f, 315f, -15f));
        }

        static void Pillar(ArchMesh m, double x, double z, double top, bool brazier, float seed)
        {
            const int n = 6;
            double r = 0.36, yt = top + 0.15;
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n, a2 = (i + 1) * Math.PI * 2 / n;
                var nrm = new Vector3((float)Math.Cos(a + Math.PI / n), 0, (float)Math.Sin(a + Math.PI / n));
                float riseA = 1.0f - 0.35f * (float)Math.Abs(Math.Sin(a + seed)), riseB = 1.0f - 0.35f * (float)Math.Abs(Math.Sin(a2 + seed));   // broken, uneven tops
                int b0 = m.Add(x + Math.Cos(a) * r, top - 0.1, z + Math.Sin(a) * r, nrm, Pale), b1 = m.Add(x + Math.Cos(a2) * r, top - 0.1, z + Math.Sin(a2) * r, nrm, Pale);
                int t1 = m.Add(x + Math.Cos(a2) * r, yt, z + Math.Sin(a2) * r, nrm, Mul(Pale, 0.9f), riseB), t0 = m.Add(x + Math.Cos(a) * r, yt, z + Math.Sin(a) * r, nrm, Mul(Pale, 0.9f), riseA);
                m.Quad(b0, t0, t1, b1);
            }
            if (!brazier) return;
            for (int k = 0; k < 2; k++)
                for (int s = 0; s < 1; s++)
                {
                    double ax = k == 0 ? 1 : 0, az = k == 0 ? 0 : 1;
                    var nn = new Vector3((float)-az, 0, (float)ax);
                    int a = m.Add(x - ax * 0.25, yt + 0.02, z - az * 0.25, nn, Ember, 0.65f, 2f), b = m.Add(x + ax * 0.25, yt + 0.02, z + az * 0.25, nn, Ember, 0.65f, 2f);
                    int c = m.Add(x, yt + 0.32, z, nn, Flame, 0.65f, 2f);
                    m.Sway(c, 0.05f, seed);
                    if (s == 0) m.Tri(a, c, b); else m.Tri(a, b, c);
                }
        }

        // ---------------------------------------------------------------------------------------------- keeps

        /// <summary>
        /// Each base's keep stands off the island behind its pad, on its own floating rock, tied back by a snapped chain:
        /// Azure a square spire keep (corner turrets, teal roofs, a tall spire), Crimson a round crown keep (a ring of
        /// horn merlons, ember windows, two flanking drums). Outside every walkable cell, so it never covers a fighter.
        /// </summary>
        static void Keep(ArchMesh m, PadDef pad, bool azure, double y0, bool detail, int mapW, WallArchReport rep)
        {
            double x = azure ? -6.0 : mapW + 6.0, z = pad.Z, gy = y0 - 0.5;
            // Foundation: a hanging rock (inverted octagonal cone).
            FloatingRock(m, x, z, gy, azure ? 3.4 : 3.6, 7.0);
            if (azure)
            {
                Box(m, x - 1.6, x + 1.6, gy, gy + 6.2, z - 1.6, z + 1.6, Pale, 0f);
                Crenels(m, x, z, gy + 6.2, 1.6, 3, Pale);
                for (int i = 0; i < 4; i++)
                {
                    double tx = x + (i % 2 == 0 ? -1.6 : 1.6), tz = z + (i < 2 ? -1.6 : 1.6);
                    Turret(m, tx, tz, gy, gy + 7.2, 0.6, Pale, Azure, 1.6);
                }
                Cone(m, x, z, gy + 6.2, 1.2, 4.6, 8, Azure);
                if (detail)
                {
                    Window(m, x + 1.61, z, gy + 4.4, true, Flame);
                    Window(m, x + 1.61, z - 0.8, gy + 2.6, true, Flame);
                    Window(m, x + 1.61, z + 0.8, gy + 2.6, true, Flame);
                    BigBanner(m, x + 1.65, z - 0.9, gy + 5.6, Azure, AzureDark);
                    BigBanner(m, x + 1.65, z + 0.9, gy + 5.6, Azure, AzureDark);
                }
            }
            else
            {
                Drum(m, x, z, gy, gy + 5.4, 2.0, 10, CrimsonDarkStone(), 0f);
                Drum(m, x, z, gy + 5.4, gy + 6.0, 2.35, 10, StoneDark, 0f);
                Horns(m, x, z, gy + 6.0, 2.2, 10, Crimson);
                Cone(m, x, z, gy + 6.0, 1.6, 2.2, 10, Crimson);
                Drum(m, x, z - 2.6, gy, gy + 3.6, 1.0, 8, CrimsonDarkStone(), 0f);
                Drum(m, x, z + 2.6, gy, gy + 3.6, 1.0, 8, CrimsonDarkStone(), 0f);
                Cone(m, x, z - 2.6, gy + 3.6, 1.15, 1.4, 8, CrimsonDark);
                Cone(m, x, z + 2.6, gy + 3.6, 1.15, 1.4, 8, CrimsonDark);
                if (detail)
                {
                    Window(m, x - 2.01, z, gy + 4.0, false, Ember);
                    Window(m, x - 1.9, z - 0.7, gy + 2.4, false, Ember);
                    Window(m, x - 1.9, z + 0.7, gy + 2.4, false, Ember);
                    BigBanner(m, x - 2.05, z, gy + 5.2, Crimson, CrimsonDark);
                }
            }
            if (detail) Chain(m, azure ? x + 2.0 : x - 2.0, z + 1.0, gy + 1.0, azure ? 0.6 : mapW - 0.6, z + 3.0, y0 - 0.3);
            rep.Keeps++;
            float vx = azure ? 3.0f : mapW - 3.0f, vz = (float)z + 7f;
            float yaw = Mathf.Repeat(Mathf.Atan2((float)(x - vx), (float)(vz - z)) * Mathf.Rad2Deg, 360f);   // 0 north, 90 east
            rep.Views.Add((azure ? "keep-azure" : "keep-crimson", vx, vz, yaw, -6f));
        }

        static Color CrimsonDarkStone() => BeyondHex("#8E7F78");

        static void FloatingRock(ArchMesh m, double x, double z, double y, double r, double depth)
        {
            const int n = 8;
            int tip = m.Add(x + 0.4, y - depth, z - 0.3, Vector3.down, RockDark);
            var top = new int[n];
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n, rr = r * (0.85 + 0.25 * Math.Sin(i * 2.3));
                top[i] = m.Add(x + Math.Cos(a) * rr, y, z + Math.Sin(a) * rr, new Vector3((float)Math.Cos(a), -0.4f, (float)Math.Sin(a)).normalized, Rock);
            }
            int c = m.Add(x, y + 0.02, z, Vector3.up, Moss);
            for (int i = 0; i < n; i++)
            {
                int a = top[i], b = top[(i + 1) % n];
                m.Tri(tip, a, b);
                m.Tri(c, b, a);
            }
        }

        static void Crenels(ArchMesh m, double x, double z, double y, double half, int perSide, Color c)
        {
            for (int s = 0; s < 4; s++)
                for (int k = 0; k < perSide; k++)
                {
                    double t = -half + (k + 0.5) * (2 * half / perSide);
                    double cx = s < 2 ? x + t : x + (s == 2 ? -half : half) - (s == 2 ? -0.2 : 0.2);
                    double cz = s < 2 ? z + (s == 0 ? -half : half) - (s == 0 ? -0.2 : 0.2) : z + t;
                    Box(m, cx - 0.22, cx + 0.22, y, y + 0.32, cz - 0.22, cz + 0.22, c, 0.4f);
                }
        }

        static void Turret(ArchMesh m, double x, double z, double y0, double y1, double r, Color body, Color roof, double roofH)
        {
            Drum(m, x, z, y0, y1, r, 6, body, 0f);
            Cone(m, x, z, y1, r + 0.12, roofH, 6, roof);
        }

        static void Drum(ArchMesh m, double x, double z, double y0, double y1, double r, int n, Color c, float rise)
        {
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n, a2 = (i + 1) * Math.PI * 2 / n;
                var nrm = new Vector3((float)Math.Cos(a + Math.PI / n), 0, (float)Math.Sin(a + Math.PI / n));
                Face(m, x + Math.Cos(a) * r, y0, z + Math.Sin(a) * r, x + Math.Cos(a2) * r, y0, z + Math.Sin(a2) * r,
                    x + Math.Cos(a2) * r, y1, z + Math.Sin(a2) * r, x + Math.Cos(a) * r, y1, z + Math.Sin(a) * r, nrm, Mul(c, i % 2 == 0 ? 1f : 0.94f), rise, 0f);
            }
            int ctr = m.Add(x, y1, z, Vector3.up, c, rise);
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n, a2 = (i + 1) * Math.PI * 2 / n;
                int p = m.Add(x + Math.Cos(a) * r, y1, z + Math.Sin(a) * r, Vector3.up, c, rise), q = m.Add(x + Math.Cos(a2) * r, y1, z + Math.Sin(a2) * r, Vector3.up, c, rise);
                m.Tri(ctr, q, p);
            }
        }

        static void Cone(ArchMesh m, double x, double z, double y, double r, double h, int n, Color c)
        {
            int apex = m.Add(x, y + h, z, Vector3.up, c);
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n, a2 = (i + 1) * Math.PI * 2 / n;
                var nrm = new Vector3((float)Math.Cos(a + Math.PI / n), (float)(r / h), (float)Math.Sin(a + Math.PI / n)).normalized;
                int p = m.Add(x + Math.Cos(a) * r, y, z + Math.Sin(a) * r, nrm, Mul(c, i % 2 == 0 ? 1f : 0.9f));
                int q = m.Add(x + Math.Cos(a2) * r, y, z + Math.Sin(a2) * r, nrm, Mul(c, i % 2 == 0 ? 1f : 0.9f));
                m.Tri(apex, q, p);
            }
        }

        /// <summary>The crown keep's ring of horn merlons: tall triangles leaning out (crimson, 2 tris each).</summary>
        static void Horns(ArchMesh m, double x, double z, double y, double r, int n, Color c)
        {
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n, w = 0.32;
                double px = x + Math.Cos(a) * r, pz = z + Math.Sin(a) * r, tx = -Math.Sin(a), tz = Math.Cos(a);
                var nrm = new Vector3((float)Math.Cos(a), 0.2f, (float)Math.Sin(a));
                int b0 = m.Add(px - tx * w, y, pz - tz * w, nrm, c), b1 = m.Add(px + tx * w, y, pz + tz * w, nrm, c);
                int tip = m.Add(px + Math.Cos(a) * 0.45, y + 1.1, pz + Math.Sin(a) * 0.45, nrm, Mul(c, 1.1f), 0.6f);
                m.Tri(b0, tip, b1);
            }
        }

        static void Window(ArchMesh m, double x, double z, double y, bool eastFacing, Color c)
        {
            double s = 0.22;
            var n = new Vector3(eastFacing ? 1 : -1, 0, 0);
            int a = m.Add(x, y - s, z - s * 0.6, n, c, 0f, 2f), b = m.Add(x, y - s, z + s * 0.6, n, c, 0f, 2f);
            int cc = m.Add(x, y + s, z + s * 0.6, n, c, 0f, 2f), d = m.Add(x, y + s, z - s * 0.6, n, c, 0f, 2f);
            if (eastFacing) m.Quad(a, d, cc, b); else m.Quad(a, b, cc, d);
        }

        static void BigBanner(ArchMesh m, double x, double z, double y, Color main, Color dark)
        {
            for (int s = 0; s < 1; s++)
            {
                var n = new Vector3(x > 0 ? -1 : 1, 0, 0);
                int a = m.Add(x, y, z - 0.45, n, dark, 0f, 1f), b = m.Add(x, y, z + 0.45, n, dark, 0f, 1f);
                int c = m.Add(x, y - 2.4, z + 0.45, n, main, 0f, 1f), d = m.Add(x, y - 2.4, z - 0.45, n, main, 0f, 1f);
                int tip = m.Add(x, y - 2.9, z, n, main, 0f, 1f);
                m.Sway(c, 0.25f, (float)z); m.Sway(d, 0.25f, (float)z); m.Sway(tip, 0.3f, (float)z);
                if (s == 0) { m.Quad(a, d, c, b); m.Tri(d, tip, c); } else { m.Quad(a, b, c, d); m.Tri(d, c, tip); }
            }
        }

        /// <summary>A snapped iron chain (WORLD.md "chains and roots") from the keep back toward the island: a sagging ribbon.</summary>
        static void Chain(ArchMesh m, double x0, double z0, double y0, double x1, double z1, double y1)
        {
            const int seg = 6;
            Color iron = BeyondHex("#3E4450");
            int pa = -1, pb = -1;
            for (int i = 0; i <= seg; i++)
            {
                double t = i / (double)seg;
                double x = x0 + (x1 - x0) * t, z = z0 + (z1 - z0) * t, y = y0 + (y1 - y0) * t - Math.Sin(t * Math.PI) * 1.2;
                if (i == seg) y -= 0.8;   // snapped: the last link hangs short of the island
                int a = m.Add(x, y - 0.06, z, Vector3.up, iron), b = m.Add(x, y + 0.06, z, Vector3.up, iron);
                if (pa >= 0) m.Quad(pa, pb, b, a);
                pa = a; pb = b;
            }
        }

        // ---------------------------------------------------------------------------------------------- mesh

        public static Mesh ToMesh(ArchMesh a)
        {
            var mesh = new Mesh { name = "Wall Architecture", indexFormat = a.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(a.V); mesh.SetNormals(a.N); mesh.SetColors(a.C);
            mesh.SetUVs(0, a.U0); mesh.SetUVs(1, a.U1); mesh.SetUVs(2, a.U2);
            mesh.SetTriangles(a.T, 0, true);
            Bounds bb = mesh.bounds;
            bb.Expand(new Vector3(2f, 2f * WallArchBudget.MaxRise + 4f, 2f));   // rise and banner sway happen in the shader
            mesh.bounds = bb;
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}

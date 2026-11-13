using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Dressing;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Atmosphere
{
    /// <summary>
    /// Per-quality budget of the world around the arena (world around arena 1, 2026-11-13). Pure; EditMode tests hold it.
    /// Low gets the cheap version: the six landmarks only, coarse, no roots, no far ring, no ink pass (one draw).
    /// </summary>
    public readonly struct WorldBeyondBudget
    {
        /// <summary>Rock segments around an island; how many plain islets per edge band; far silhouette islets.</summary>
        public readonly int Segments, NearIslets, FarIslets;
        /// <summary>Roots per islet; share (percent) of the play island's rim edges that get a hanging root.</summary>
        public readonly int RootsPerIslet, RimRootPercent;
        /// <summary>The far homeland massifs (only wide cameras see them), trees and underside crystals on the islets, the ink pass.</summary>
        public readonly bool Massifs, Trees, UnderCrystals, Ink;

        public WorldBeyondBudget(int segments, int nearIslets, int farIslets, int rootsPerIslet, int rimRootPercent, bool massifs, bool trees, bool underCrystals, bool ink)
        {
            Segments = segments; NearIslets = nearIslets; FarIslets = farIslets; RootsPerIslet = rootsPerIslet; RimRootPercent = rimRootPercent;
            Massifs = massifs; Trees = trees; UnderCrystals = underCrystals; Ink = ink;
        }

        public static WorldBeyondBudget For(int level)
        {
            if (level <= FramePolicy.LevelLow) return new WorldBeyondBudget(6, 0, 0, 0, 0, false, false, false, false);
            if (level == FramePolicy.LevelMedium) return new WorldBeyondBudget(8, 6, 8, 2, 18, false, true, false, true);
            return new WorldBeyondBudget(10, 10, 18, 4, 40, true, true, true, true);
        }

        /// <summary>Draw calls: the fill, plus the ink line when on.</summary>
        public int Draws => Ink ? 2 : 1;
    }

    /// <summary>
    /// The geometry kit of the world around the arena: islands, roots, towers, crystals, bones, chains and waterfalls,
    /// built by code as plain triangles with vertex colour (sRGB; alpha 1 = self-lit glow) and per-vertex data for
    /// CM/WorldBeyond: UV0 (water ribbon u, v), UV1 (depth band 0..1, team 0 none 1 Azure 2 Crimson, flag 0 solid 1 water
    /// 2 root, sway weight 0..1), UV2 (the smoothed outline normal; the ink hull follows it, so boxes do not tear).
    /// Original shapes in the art bible's toon language; no other game's designs. Pure: tests count and bound it.
    /// </summary>
    public sealed class BeyondMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<Vector2> U0 = new List<Vector2>();
        public readonly List<Vector4> U1 = new List<Vector4>();
        public readonly List<Vector3> U2 = new List<Vector3>();
        public readonly List<int> T = new List<int>();
        public int Tris => T.Count / 3;

        /// <summary>Depth band, team and flag for what is added next.</summary>
        public float Band, Team;

        public int Add(Vector3 v, Vector3 n, Color c, float flag = 0f, float sway = 0f, Vector2 uv = default, Vector3? outline = null)
        {
            Vector3 nn = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up;
            V.Add(v); N.Add(nn); C.Add(c); U0.Add(uv); U1.Add(new Vector4(Band, Team, flag, sway));
            Vector3 o = outline ?? nn;
            U2.Add(o.sqrMagnitude > 1e-8f ? o.normalized : nn);
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        public static Color Hex(string h, float glow = 0f)
        {
            ColorUtility.TryParseHtmlString(h, out Color c);
            c.a = glow;
            return c;
        }

        // ---- palette (bible environment swatches; sRGB) ----
        public static readonly Color Grass = Hex("#A3C150"), GrassDark = Hex("#6F9A3E"), Earth = Hex("#B98A5E"), Rock = Hex("#9C9480"),
            RockBand = Hex("#857D6B"), RockDark = Hex("#5F625B"), Leaf = Hex("#5E9A45"), LeafDark = Hex("#4C8239"), Wood = Hex("#7A5634"),
            WoodDark = Hex("#633F22"), WallStone = Hex("#B0A58C"), WallCap = Hex("#C9BFA6"), Window = Hex("#3C4152"), Bone = Hex("#E2D2B0"),
            BoneShade = Hex("#C4B28E"), DeadCrystal = Hex("#8FA3A8"), SkyGlass = Hex("#A8EEF5", 1f), Water = Hex("#6FC0EC"),
            Basalt = Hex("#6A6366"), BasaltCap = Hex("#857C7C"), AzureRoof = Hex("#4E9CA6"), AzureGlow = Hex("#7FE6F0", 1f),
            Ember = Hex("#FFB066", 1f), EmberLeaf = Hex("#E8823C", 1f), CrimsonRoof = Hex("#B86A5E"), Iron = Hex("#6E6A72");

        /// <summary>
        /// A rock body turned around a vertical axis: ring i at height ys[i] (relative to top) and radius rs[i] x the
        /// irregular silhouette (one jitter per spoke, kept across rings so the shape reads as one chunk). Optional dome
        /// cap on top and a point at the bottom. Returns the index of the first ring vertex.
        /// </summary>
        public void Lathe(Vector3 top, float[] ys, float[] rs, Color[] cols, int seg, uint seed, float jitter, bool cap, float tipY, Color tipCol, float squash = 1f, float yaw = 0f)
        {
            int rings = ys.Length;
            var spoke = new float[seg];
            uint s = seed | 1u;
            for (int j = 0; j < seg; j++) spoke[j] = 1f + (WorldBeyondLayout.R(ref s) - 0.5f) * 2f * jitter;
            var idx = new int[rings, seg];
            for (int i = 0; i < rings; i++)
            {
                float yPrev = i > 0 ? ys[i - 1] : ys[i] + 0.5f, yNext = i < rings - 1 ? ys[i + 1] : tipY;
                float rPrev = i > 0 ? rs[i - 1] : 0f, rNext = i < rings - 1 ? rs[i + 1] : 0f;
                for (int j = 0; j < seg; j++)
                {
                    float a = yaw + j * Mathf.PI * 2f / seg;
                    var rad = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * squash);
                    float r = rs[i] * spoke[j] * (1f + (WorldBeyondLayout.R(ref s) - 0.5f) * jitter * 0.4f);
                    // Normal from the profile slope: outward by the height span, up by the radius change.
                    Vector3 n = rad * (yPrev - yNext) + Vector3.up * (rNext - rPrev);
                    if (i == 0 && cap) n = Vector3.Lerp(n.normalized, Vector3.up, 0.6f);
                    idx[i, j] = Add(top + rad * r + Vector3.up * ys[i], n, cols[Mathf.Min(i, cols.Length - 1)], 0f, 0f, default, rad + Vector3.up * 0.2f);
                }
            }
            for (int i = 0; i < rings - 1; i++)
                for (int j = 0; j < seg; j++)
                {
                    int jn = (j + 1) % seg;
                    Quad(idx[i, j], idx[i, jn], idx[i + 1, jn], idx[i + 1, j]);
                }
            if (cap)
            {
                int c = Add(top + Vector3.up * (ys[0] + 0.08f), Vector3.up, cols[0]);
                for (int j = 0; j < seg; j++) Tri(c, idx[0, (j + 1) % seg], idx[0, j]);
            }
            int tip = Add(top + Vector3.up * tipY, Vector3.down, tipCol);
            for (int j = 0; j < seg; j++) Tri(tip, idx[rings - 1, j], idx[rings - 1, (j + 1) % seg]);
        }

        /// <summary>A tube of 'sides' sides along a path (roots, ribs, chains' bars), tapering to a point at the end.</summary>
        public void Tube(IList<Vector3> path, float r0, float r1, Color col, int sides, float flag, float sway0, float sway1)
        {
            int n = path.Count;
            var ring = new int[n - 1, sides];
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 f = (path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(f, Mathf.Abs(f.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, f).normalized;
                float t = (float)i / (n - 1), r = Mathf.Lerp(r0, r1, t), w = Mathf.Lerp(sway0, sway1, t);
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides;
                    Vector3 d = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    ring[i, k] = Add(path[i] + d * r, d, col, flag, w);
                }
            }
            for (int i = 0; i < n - 2; i++)
                for (int k = 0; k < sides; k++)
                {
                    int kn = (k + 1) % sides;
                    Quad(ring[i, k], ring[i + 1, k], ring[i + 1, kn], ring[i, kn]);
                }
            Vector3 fl = (path[n - 1] - path[n - 2]).normalized;
            int tip = Add(path[n - 1], fl, col, flag, sway1);
            for (int k = 0; k < sides; k++) Tri(ring[n - 2, k], tip, ring[n - 2, (k + 1) % sides]);
        }

        /// <summary>A box (yaw in degrees) with flat faces; top face colour separate (the top one tone lighter, bible).</summary>
        public void Box(Vector3 centre, Vector3 size, float yawDeg, Color side, Color top, float tiltDeg = 0f)
        {
            Quaternion q = Quaternion.Euler(tiltDeg, yawDeg, 0f);
            Vector3 h = size * 0.5f;
            Vector3[] fn = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            foreach (Vector3 n in fn)
            {
                Vector3 a = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 b = Vector3.Cross(n, a);
                Vector3 c0 = Vector3.Scale(n, h);
                Vector3 ea = Vector3.Scale(a, h), eb = Vector3.Scale(b, h);
                Color col = n.y > 0.5f ? top : side;
                int i0 = Add(centre + q * (c0 - ea - eb), q * n, col, 0f, 0f, default, q * (c0 - ea - eb));
                int i1 = Add(centre + q * (c0 + ea - eb), q * n, col, 0f, 0f, default, q * (c0 + ea - eb));
                int i2 = Add(centre + q * (c0 + ea + eb), q * n, col, 0f, 0f, default, q * (c0 + ea + eb));
                int i3 = Add(centre + q * (c0 - ea + eb), q * n, col, 0f, 0f, default, q * (c0 - ea + eb));
                Quad(i0, i3, i2, i1);
            }
        }

        /// <summary>A cone (roof, tree layer): base ring at 'base', apex 'height' up the axis.</summary>
        public void Cone(Vector3 base0, float radius, float height, int seg, Color col, Color baseCol, Vector3? axis = null, float glowTip = -1f)
        {
            Vector3 ax = (axis ?? Vector3.up).normalized;
            Vector3 s1 = Vector3.Cross(ax, Mathf.Abs(ax.y) > 0.9f ? Vector3.right : Vector3.up).normalized, s2 = Vector3.Cross(ax, s1);
            var ring = new int[seg];
            for (int j = 0; j < seg; j++)
            {
                float a = j * Mathf.PI * 2f / seg;
                Vector3 d = s1 * Mathf.Cos(a) + s2 * Mathf.Sin(a);
                ring[j] = Add(base0 + d * radius, d * height + ax * radius, col);
            }
            Color tc = glowTip >= 0f ? new Color(col.r, col.g, col.b, glowTip) : col;
            int apex = Add(base0 + ax * height, ax, tc);
            int bc = Add(base0, -ax, baseCol);
            for (int j = 0; j < seg; j++)
            {
                int jn = (j + 1) % seg;
                Tri(apex, ring[jn], ring[j]);
                Tri(bc, ring[j], ring[jn]);
            }
        }

        /// <summary>A crystal: a four-sided double point along 'dir' (glow colour carries alpha 1).</summary>
        public void Spike(Vector3 base0, Vector3 dir, float length, float width, Color col)
        {
            Vector3 f = dir.normalized;
            Vector3 s1 = Vector3.Cross(f, Mathf.Abs(f.y) > 0.9f ? Vector3.right : Vector3.up).normalized, s2 = Vector3.Cross(f, s1);
            Vector3 mid = base0 + f * length * 0.28f;
            int tip = Add(base0 + f * length, f, col), root = Add(base0 - f * length * 0.08f, -f, col);
            var r = new int[4];
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f + 0.4f;
                Vector3 d = s1 * Mathf.Cos(a) + s2 * Mathf.Sin(a);
                r[k] = Add(mid + d * width, d, col);
            }
            for (int k = 0; k < 4; k++)
            {
                int kn = (k + 1) % 4;
                Tri(tip, r[kn], r[k]);
                Tri(root, r[k], r[kn]);
            }
        }

        /// <summary>One chain link: a flat ring of 'seg' x 3 around the axis 'normal' (radius a x b), optionally open.</summary>
        public void Link(Vector3 centre, Vector3 normal, Vector3 along, float a, float b, float thick, Color col, int seg, bool open)
        {
            Vector3 n = normal.normalized, u = along.normalized, v = Vector3.Cross(n, u);
            int points = open ? seg - 1 : seg;   // an open link leaves out one segment of the ring
            var ring = new int[points, 3];
            for (int i = 0; i < points; i++)
            {
                float t = i * Mathf.PI * 2f / seg;
                Vector3 c = centre + u * Mathf.Cos(t) * a + v * Mathf.Sin(t) * b;
                Vector3 out0 = (u * Mathf.Cos(t) * b + v * Mathf.Sin(t) * a).normalized;
                for (int k = 0; k < 3; k++)
                {
                    float p = k * Mathf.PI * 2f / 3f;
                    Vector3 d = out0 * Mathf.Cos(p) + n * Mathf.Sin(p);
                    ring[i, k] = Add(c + d * thick, d, col);
                }
            }
            int quads = open ? points - 1 : points;
            for (int i = 0; i < quads; i++)
            {
                int i1 = (i + 1) % points;
                for (int k = 0; k < 3; k++)
                {
                    int kn = (k + 1) % 3;
                    Quad(ring[i, k], ring[i1, k], ring[i1, kn], ring[i, kn]);
                }
            }
        }

        /// <summary>A waterfall ribbon from 'lip' down to 'bottomY', bowing out along 'outward' (flag 1: scrolling water).</summary>
        public void Fall(Vector3 lip, Vector3 outward, Vector3 across, float width, float bottomY, int rows)
        {
            Vector3 o = outward.normalized, x = across.normalized;
            Vector3 n = o;
            var l = new int[rows + 1]; var r = new int[rows + 1];
            float drop = lip.y - bottomY;
            for (int i = 0; i <= rows; i++)
            {
                float t = (float)i / rows;
                Vector3 c = lip + o * (Mathf.Sqrt(t) * 1.4f) + Vector3.down * (t * drop);
                float w = width * (1f + t * 0.5f);
                l[i] = Add(c - x * w * 0.5f, n, Water, 1f, 0f, new Vector2(0f, t * drop));
                r[i] = Add(c + x * w * 0.5f, n, Water, 1f, 0f, new Vector2(1f, t * drop));
            }
            for (int i = 0; i < rows; i++) Quad(l[i], r[i], r[i + 1], l[i + 1]);
        }

        /// <summary>
        /// A floating island: a grass cap with a dark fringe, then earth and rock strata tapering to a hanging point
        /// (radius r, depth below the top 'depth'). Optional roots from the underside and glowing crystals under it
        /// (a proposal of the world note: sky-glass grows on the undersides and is what holds the land up).
        /// </summary>
        public void Islet(Vector3 top, float r, float depth, int seg, uint seed, int roots, bool underCrystals, Color? capCol = null, float squash = 1f)
        {
            Color cap = capCol ?? Grass;
            float[] ys = { 0f, -0.2f, -0.5f, -depth * 0.34f, -depth * 0.58f, -depth * 0.8f };
            float[] rs = { r, r * 1.04f, r * 0.97f, r * 0.84f, r * 0.6f, r * 0.33f };
            Color[] cs = { cap, GrassDark, Earth, Rock, RockBand, Rock };
            if (seg <= 6)
            {
                ys = new[] { 0f, -0.25f, -depth * 0.45f, -depth * 0.75f };
                rs = new[] { r, r * 1.03f, r * 0.72f, r * 0.36f };
                cs = new[] { cap, GrassDark, Rock, RockBand };
            }
            Lathe(top, ys, rs, cs, seg, seed, 0.16f, true, -depth, RockDark, squash);
            uint s = seed * 2654435761u + 7u;
            for (int k = 0; k < roots; k++)
            {
                float a = WorldBeyondLayout.R(ref s) * Mathf.PI * 2f;
                float rr = r * (0.55f + 0.3f * WorldBeyondLayout.R(ref s));
                Vector3 at = top + new Vector3(Mathf.Cos(a) * rr, -depth * (0.3f + 0.25f * WorldBeyondLayout.R(ref s)), Mathf.Sin(a) * rr * squash);
                Root(at, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), 1.4f + 2.4f * WorldBeyondLayout.R(ref s), 0.09f + 0.05f * WorldBeyondLayout.R(ref s), s);
            }
            if (underCrystals)
                for (int k = 0; k < 2; k++)
                {
                    float a = WorldBeyondLayout.R(ref s) * Mathf.PI * 2f;
                    Vector3 at = top + new Vector3(Mathf.Cos(a) * r * 0.4f, -depth * 0.6f, Mathf.Sin(a) * r * 0.4f * squash);
                    Spike(at, new Vector3(Mathf.Cos(a) * 0.5f, -1f, Mathf.Sin(a) * 0.5f), 0.9f + 0.6f * WorldBeyondLayout.R(ref s), 0.16f, SkyGlass);
                }
        }

        /// <summary>A hanging root: three-sided, four segments, curling out then down; the tip sways (flag 2).</summary>
        public Vector3 Root(Vector3 at, Vector3 outward, float length, float radius, uint seed)
        {
            uint s = seed | 1u;
            var path = new List<Vector3>(5);
            Vector3 o = outward.normalized;
            float curl = 0.25f + 0.35f * WorldBeyondLayout.R(ref s);
            for (int i = 0; i <= 4; i++)
            {
                float t = i / 4f;
                path.Add(at + o * (Mathf.Sin(t * 1.6f) * curl * length * 0.3f) + Vector3.down * (t * length) +
                         new Vector3(WorldBeyondLayout.R(ref s) - 0.5f, 0f, WorldBeyondLayout.R(ref s) - 0.5f) * 0.12f * t);
            }
            Tube(path, radius, radius * 0.25f, WorldBeyondLayout.R(ref s) < 0.5f ? Wood : WoodDark, 3, 2f, 0f, 1f);
            return path[4];
        }

        /// <summary>Two stacked cones on a short trunk: a round toon tree seen from far.</summary>
        public void Tree(Vector3 at, float h, Color leaf, Color leafDark, float glowTip = -1f)
        {
            Box(at + Vector3.up * h * 0.15f, new Vector3(h * 0.12f, h * 0.3f, h * 0.12f), 0f, WoodDark, WoodDark);
            Cone(at + Vector3.up * h * 0.25f, h * 0.36f, h * 0.5f, 6, leafDark, leafDark, null, glowTip);
            Cone(at + Vector3.up * h * 0.52f, h * 0.27f, h * 0.5f, 6, leaf, leafDark, null, glowTip);
        }

        /// <summary>A round stone tower with a cornice, an optional cone roof, a broken (jagged) top when roofless.</summary>
        public void Tower(Vector3 at, float r, float h, int seg, uint seed, Color wall, Color cap, Color? roof, bool broken, Color window, bool glowWindows)
        {
            uint s = seed | 1u;
            float[] ys = { h, h * 0.9f, h * 0.88f, h * 0.55f, 0.15f, 0f };
            float[] rs = { r * 1.12f, r * 1.12f, r, r * 1.02f, r * 1.1f, r * 1.14f };
            Color[] cs = { cap, cap, wall, wall, wall, RockDark };
            int first = V.Count;
            Lathe(at, ys, rs, cs, seg, seed, 0.03f, !broken && roof == null, 0f, RockDark);
            // Lathe heights are relative to 'at' and descend; the first ring is the top. Broken: notch alternate top spokes down.
            if (broken)
                for (int j = 0; j < seg; j++)
                    if ((j % 2) == 1 || WorldBeyondLayout.R(ref s) < 0.3f) V[first + j] += Vector3.down * h * (0.12f + 0.18f * WorldBeyondLayout.R(ref s));
            if (roof.HasValue) Cone(at + Vector3.up * h, r * 1.45f, r * 2.1f, seg, roof.Value, cap);
            int windows = Mathf.Max(2, seg / 3);
            for (int k = 0; k < windows; k++)
            {
                float a = (k + 0.5f) * Mathf.PI * 2f / windows + WorldBeyondLayout.R(ref s) * 0.3f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Color wc = glowWindows ? new Color(window.r, window.g, window.b, 1f) : window;
                Box(at + d * r * 0.98f + Vector3.up * h * (0.62f + 0.12f * (k % 2)), new Vector3(r * 0.32f, h * 0.13f, r * 0.12f), -a * Mathf.Rad2Deg + 90f, wc, wc);
            }
        }
    }

    /// <summary>One placed thing of the world around the arena (pure data, for tests and the report).</summary>
    public struct BeyondFeature
    {
        public string Name;
        public Vector3 Centre;
        public float Radius;
        public int Band;   // 0 near, 1 mid, 2 far, 3 the play island's own underside
    }

    /// <summary>
    /// Where everything goes, in Unity world space (x east, z = -sim z, island tops at y 0 and up): deterministic from
    /// the map size, the plan's rim and our own seed, never the Sim's random stream. The gameplay camera (pitch 55,
    /// FOV 35, about 19 u above its target) sees the void only about 10 to 16 u past an island edge, and mostly past the
    /// north and west edges (screen up is north-west) and the east edge (screen right), so the three depth bands are
    /// packed close: near 3 to 9 u out with tops at y -2 to -4, mid 9 to 16 u out at -4 to -7, far 16 to 30 u out at
    /// -7 to -9.5 (above the cloud floor at -11). The homeland massifs, 30 to 70 u out, are for wide cameras only.
    /// Nothing stands over the play surface: every island centre lies outside the map rectangle by more than its radius.
    /// </summary>
    public static class WorldBeyondLayout
    {
        public const int Seed = 0x0B11D;

        public static float R(ref uint s)
        {
            unchecked { s ^= s << 13; s ^= s >> 17; s ^= s << 5; }
            return (s & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Builds the whole world for one map into 'm'; returns what it placed. Tips of hanging roots (for the drip
        /// sprites) and waterfall feet (for the mist) are appended when the lists are given.</summary>
        public static List<BeyondFeature> Build(BeyondMesh m, in WorldBeyondBudget b, int mapW, int mapH, IList<WorldEdge> rim,
                                                List<Vector3> rootTips = null, List<Vector3> fallFeet = null)
        {
            var f = new List<BeyondFeature>();
            float W = mapW, H = mapH;
            int seg = b.Segments;
            bool low = seg <= 6;
            uint s = Seed;

            // ---------------- near band (0): the landmarks that tell the story ----------------
            m.Band = 0.0f; m.Team = 0f;
            // 1. The old watchtower: a ruined round tower on an islet off the north-west corner, a fallen roof beside it,
            //    and a broken chain hanging from the islet's side toward the play island (the islands were once tied together).
            {
                Vector3 c = new Vector3(-4f, -4.2f, 3.5f);
                m.Islet(c, 4.2f, 6.5f, seg, 11u, low ? 0 : b.RootsPerIslet + 1, b.UnderCrystals);
                m.Tower(c + new Vector3(0.6f, 0f, 0.4f), 1.25f, 4.6f, low ? 6 : 8, 12u, BeyondMesh.WallStone, BeyondMesh.WallCap, null, true, BeyondMesh.Window, false);
                m.Cone(c + new Vector3(-1.9f, 0.35f, -1.2f), 1.5f, 1.9f, low ? 5 : 7, BeyondMesh.Hex("#8C7A6A"), BeyondMesh.WallCap, new Vector3(0.9f, 0.35f, 0.3f));
                m.Box(c + new Vector3(2.2f, 0.25f, -1.6f), new Vector3(0.9f, 0.5f, 0.7f), 20f, BeyondMesh.WallStone, BeyondMesh.WallCap);
                m.Box(c + new Vector3(1.4f, 0.2f, -2.5f), new Vector3(0.7f, 0.4f, 0.6f), -35f, BeyondMesh.WallStone, BeyondMesh.WallCap, 12f);
                if (!low)
                {
                    // Chain: six links from an iron ring on the islet's south-east flank, sagging toward the arena, the last one open.
                    Vector3 p = c + new Vector3(3.1f, -1.1f, -2.6f);
                    Vector3 dir = new Vector3(0.62f, -0.55f, -0.56f).normalized;
                    for (int k = 0; k < 6; k++)
                    {
                        Vector3 at = p + dir * (k * 0.62f) + Vector3.down * (0.05f * k * k);
                        Vector3 nrm = (k % 2 == 0) ? Vector3.Cross(dir, Vector3.up) : Vector3.up;
                        m.Link(at, nrm, dir, 0.42f, 0.24f, 0.075f, BeyondMesh.Iron, 8, k == 5);
                    }
                }
                f.Add(new BeyondFeature { Name = "watchtower", Centre = c, Radius = 4.4f, Band = 0 });
            }
            // 2. The waterfall islet north of the shrine: a spring falls off its south lip into the cloud sea.
            {
                Vector3 c = new Vector3(W * 0.5f - 13f, -3.2f, 9.5f);
                m.Islet(c, 3.6f, 5.5f, seg, 21u, low ? 0 : b.RootsPerIslet, b.UnderCrystals);
                m.Box(c + new Vector3(0.2f, 0.08f, 0.6f), new Vector3(2.2f, 0.12f, 1.6f), 15f, BeyondMesh.Water, BeyondMesh.Water);
                Vector3 lip = c + new Vector3(0.9f, -0.1f, -3.45f);
                m.Fall(lip, new Vector3(0.15f, 0f, -1f), Vector3.right, 1.1f, -10.6f, low ? 3 : 6);
                if (fallFeet != null) fallFeet.Add(new Vector3(lip.x + 0.2f, -10.2f, lip.z - 1.6f));
                if (b.Trees) { m.Tree(c + new Vector3(-1.6f, 0f, 1.2f), 2.2f, BeyondMesh.Leaf, BeyondMesh.LeafDark); m.Tree(c + new Vector3(1.8f, 0f, 1.5f), 1.7f, BeyondMesh.Leaf, BeyondMesh.LeafDark); }
                f.Add(new BeyondFeature { Name = "waterfall", Centre = c, Radius = 3.8f, Band = 0 });
            }
            // 3. The sky-glass outcrop off the south-west: a crown of pale crystals that glows at night.
            {
                Vector3 c = new Vector3(-8f, -3.4f, -H * 0.72f);
                m.Islet(c, 3.0f, 5.0f, seg, 31u, low ? 0 : b.RootsPerIslet, true);
                uint cs = 33u;
                int n = low ? 4 : 7;
                for (int k = 0; k < n; k++)
                {
                    float a = k * 2.3f;
                    float d = k == 0 ? 0f : 0.7f + 1.1f * R(ref cs);
                    Vector3 at = c + new Vector3(Mathf.Cos(a) * d, 0.05f, Mathf.Sin(a) * d);
                    Vector3 dir = new Vector3(Mathf.Cos(a) * d * 0.25f, 1f, Mathf.Sin(a) * d * 0.25f);
                    m.Spike(at, dir, k == 0 ? 3.6f : 1.2f + 1.6f * R(ref cs), k == 0 ? 0.55f : 0.3f, BeyondMesh.SkyGlass);
                }
                f.Add(new BeyondFeature { Name = "crystal outcrop", Centre = c, Radius = 3.2f, Band = 0 });
            }

            // ---------------- mid band (1): each banner's homeland outpost behind its base, the old Kaiju ----------------
            m.Band = 0.35f;
            // 4. Azure: the Shieldholds (proposal): a terraced plateau of pale stone with round towers under teal roofs and a
            //    standing shield-disc gate, behind the Azure base (west, the top-left of the screen), in a cool team-tinted light.
            {
                m.Team = 1f;
                Vector3 c = new Vector3(-10f, -4.0f, -H * 0.5f + 11f);
                m.Islet(c, 5.4f, 7.5f, seg, 41u, low ? 0 : b.RootsPerIslet, b.UnderCrystals, BeyondMesh.Hex("#9FB98A"), 0.8f);
                m.Lathe(c + new Vector3(0.6f, 1.3f, 0.4f), new[] { 0f, -1.3f }, new[] { 3.4f, 3.7f }, new[] { BeyondMesh.WallCap, BeyondMesh.WallStone }, seg, 42u, 0.05f, true, -1.35f, BeyondMesh.WallStone);
                m.Tower(c + new Vector3(-1.4f, 1.3f, 1.2f), 0.75f, 4.4f, low ? 6 : 8, 43u, BeyondMesh.WallStone, BeyondMesh.WallCap, BeyondMesh.AzureRoof, false, BeyondMesh.AzureGlow, true);
                m.Tower(c + new Vector3(1.9f, 1.3f, -0.8f), 0.6f, 3.2f, low ? 6 : 8, 44u, BeyondMesh.WallStone, BeyondMesh.WallCap, BeyondMesh.AzureRoof, false, BeyondMesh.AzureGlow, true);
                if (!low) m.Tower(c + new Vector3(0.4f, 1.3f, 2.4f), 0.5f, 2.4f, 7, 45u, BeyondMesh.WallStone, BeyondMesh.WallCap, BeyondMesh.AzureRoof, false, BeyondMesh.AzureGlow, true);
                // The shield gate: a stone disc standing on edge with a teal boss, facing the arena (east).
                m.Cone(c + new Vector3(3.3f, 2.9f, 0.6f), 1.5f, 0.35f, low ? 8 : 12, BeyondMesh.WallStone, BeyondMesh.WallStone, Vector3.right);
                m.Cone(c + new Vector3(3.6f, 2.9f, 0.6f), 0.55f, 0.3f, 8, BeyondMesh.AzureGlow, BeyondMesh.AzureGlow, Vector3.right);
                m.Box(c + new Vector3(3.3f, 1.55f, 0.6f), new Vector3(0.5f, 0.5f, 1.6f), 0f, BeyondMesh.WallStone, BeyondMesh.WallCap);
                f.Add(new BeyondFeature { Name = "azure homeland outpost", Centre = c, Radius = 5.6f, Band = 1 });
                m.Team = 0f;
            }
            // 5. Crimson: the Ember Orchards (proposal): basalt columns, ember braziers and a grove of trees that burn without
            //    burning down (the relic "a road to an orchard of burning trees, past the edge"). The camera's yaw puts the east void
            //    at the bottom-right of the screen, where it is never seen, so the orchard stands off the north-east (the top-right of
            //    the screen from the Crimson half of the map); the Crimson massifs proper lie east, for wide cameras.
            {
                m.Team = 2f;
                Vector3 c = new Vector3(W - 8f, -3.8f, 12.5f);
                m.Islet(c, 6.0f, 7.5f, seg, 51u, low ? 0 : b.RootsPerIslet, b.UnderCrystals, BeyondMesh.Hex("#A9A57C"), 0.8f);
                uint cs = 52u;
                int cols = low ? 4 : 7;
                for (int k = 0; k < cols; k++)
                {
                    float a = k * 2.4f + 0.3f, d = 0.6f + 2.6f * R(ref cs);
                    Vector3 at = c + new Vector3(Mathf.Cos(a) * d - 1.2f, 0f, Mathf.Sin(a) * d * 0.8f);
                    float h = 2.2f + 3.6f * R(ref cs) * (1f - d / 4f);
                    m.Lathe(at, new[] { h, h - 0.25f, 0f }, new[] { 0.55f + 0.35f * R(ref cs), 0.7f, 0.75f }, new[] { BeyondMesh.BasaltCap, BeyondMesh.Basalt, BeyondMesh.Basalt },
                            6, 53u + (uint)k, 0.02f, true, 0f, BeyondMesh.Basalt);
                    if (k % 2 == 0) m.Spike(at + Vector3.up * (h + 0.05f), Vector3.up, 0.6f, 0.22f, BeyondMesh.Ember);
                }
                int trees = low ? 2 : 5;
                for (int k = 0; k < trees; k++)
                {
                    Vector3 at = c + new Vector3(1.6f + (k % 3) * 1.1f, 0f, -1.8f + (k / 3) * 1.6f + R(ref cs) * 0.6f);
                    m.Tree(at, 1.5f + 0.6f * R(ref cs), BeyondMesh.EmberLeaf, BeyondMesh.Hex("#9C4A2E"), 1f);
                }
                if (!low) m.Tower(c + new Vector3(-0.6f, 0f, 2.6f), 0.7f, 3.8f, 6, 54u, BeyondMesh.Basalt, BeyondMesh.BasaltCap, BeyondMesh.CrimsonRoof, false, BeyondMesh.Ember, true);
                f.Add(new BeyondFeature { Name = "crimson homeland outpost", Centre = c, Radius = 6.2f, Band = 1 });
                m.Team = 0f;
            }
            // 6. The old Kaiju: a far island north of the shrine carries the ribs, spine and flat underbite skull of a Kaiju
            //    much older than the egg (bone, with a dead crystal ridge), the reason the Basin knows what a Kaiju is.
            {
                Vector3 c = new Vector3(W * 0.5f + 4f, -4.6f, 13f);
                m.Islet(c, 5.4f, 6.5f, seg, 61u, low ? 0 : b.RootsPerIslet, b.UnderCrystals, BeyondMesh.Hex("#B5B784"));
                Vector3 axis = new Vector3(1f, 0f, -0.35f).normalized, side = Vector3.Cross(Vector3.up, axis);
                int ribs = low ? 3 : 5;
                for (int k = 0; k < ribs; k++)
                {
                    float along = -2.2f + k * 1.0f;
                    float span = 1.9f - Mathf.Abs(k - 2) * 0.22f, rise = 2.6f - Mathf.Abs(k - 2) * 0.35f;
                    foreach (float sd in new[] { 1f, -1f })
                    {
                        if (k == 4 && sd < 0) continue;   // one rib broken off
                        var path = new List<Vector3>();
                        int steps = low ? 3 : 5;
                        float end = (k == 1 && sd > 0) ? 0.55f : 0.92f;   // and one snapped short
                        for (int i = 0; i <= steps; i++)
                        {
                            float t = i / (float)steps * end;
                            float ang = t * Mathf.PI * 0.62f;
                            path.Add(c + axis * along + side * sd * (Mathf.Sin(ang) * span * 0.6f + 0.25f) + Vector3.up * (Mathf.Sin(ang + 0.35f) * rise) + Vector3.down * 0.3f);
                        }
                        path.Reverse();
                        m.Tube(path, 0.08f, 0.17f, BeyondMesh.Bone, low ? 3 : 4, 0f, 0f, 0f);
                    }
                }
                for (int k = 0; k < (low ? 3 : 7); k++)
                {
                    Vector3 at = c + axis * (-2.6f + k * 0.8f) + Vector3.up * (0.25f + 0.1f * Mathf.Sin(k));
                    m.Box(at, new Vector3(0.5f, 0.32f, 0.42f), Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg, BeyondMesh.BoneShade, BeyondMesh.Bone);
                    if (k % 2 == 1) m.Spike(at + Vector3.up * 0.12f, Vector3.up, 0.55f, 0.16f, BeyondMesh.DeadCrystal);
                }
                // Skull: wide and flat, with the underbite jaw (our Kaiju's head), half sunk in the turf, facing the arena.
                Vector3 head = c + axis * 3.4f + Vector3.up * 0.35f;
                float yaw = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg;
                m.Box(head, new Vector3(2.0f, 0.8f, 1.4f), yaw, BeyondMesh.BoneShade, BeyondMesh.Bone, -8f);
                m.Box(head + axis * 0.35f + Vector3.down * 0.4f, new Vector3(2.2f, 0.4f, 1.1f), yaw, BeyondMesh.BoneShade, BeyondMesh.Bone, 6f);
                foreach (float sd in new[] { 1f, -1f })
                {
                    m.Box(head + side * sd * 0.5f + Vector3.up * 0.38f + axis * 0.2f, new Vector3(0.36f, 0.12f, 0.3f), yaw, BeyondMesh.Window, BeyondMesh.Window);
                    m.Spike(head + side * sd * 0.8f + axis * 0.75f + Vector3.down * 0.3f, axis + Vector3.up * 0.9f, 0.45f, 0.08f, BeyondMesh.Bone);
                }
                f.Add(new BeyondFeature { Name = "old kaiju remains", Centre = c, Radius = 5.6f, Band = 1 });
            }

            // ---------------- plain islets: near and mid band along the north, west and east edges ----------------
            if (b.NearIslets > 0)
            {
                // Spots chosen to leave the landmarks clear: (x, z, band) with x, z in map fractions past the edges.
                Vector3[] spots =
                {
                    new Vector3(-0.05f, 0.05f, 0), new Vector3(-0.09f, 0.62f, 1), new Vector3(0.22f, -0.08f, 0), new Vector3(0.36f, -0.20f, 1),
                    new Vector3(0.74f, -0.10f, 0), new Vector3(0.95f, -0.40f, 1), new Vector3(1.06f, 0.12f, 0), new Vector3(1.10f, 0.70f, 1),
                    new Vector3(-0.07f, 0.88f, 0), new Vector3(0.52f, -0.30f, 1),
                };
                for (int k = 0; k < b.NearIslets && k < spots.Length; k++)
                {
                    int band = (int)spots[k].z;
                    m.Band = band == 0 ? 0f : 0.35f;
                    float r = band == 0 ? 1.8f + 1.2f * R(ref s) : 2.4f + 1.6f * R(ref s);
                    float x = spots[k].x * W, z = -spots[k].y * H;
                    // Push out so the whole islet stays off the map rectangle.
                    if (x < 0) x -= r + 2f; else if (x > W) x += r + 2f;
                    if (z > 0) z += r + 2f; else if (z < -H) z -= r + 2f;
                    float y = band == 0 ? -2.2f - 1.6f * R(ref s) : -4.2f - 2.2f * R(ref s);
                    var c = new Vector3(x, y, z);
                    m.Islet(c, r, r * 1.7f, seg, 100u + (uint)k, b.RootsPerIslet, b.UnderCrystals && k % 3 == 0);
                    if (b.Trees && k % 2 == 0) m.Tree(c + new Vector3(r * 0.2f, 0f, r * 0.1f), 1.4f + R(ref s), BeyondMesh.Leaf, BeyondMesh.LeafDark);
                    if (k % 4 == 1) m.Box(c + new Vector3(-r * 0.3f, 0.3f, 0f), new Vector3(0.6f, 0.6f, 0.6f), 30f * k, BeyondMesh.WallStone, BeyondMesh.WallCap, 18f);
                    f.Add(new BeyondFeature { Name = "islet", Centre = c, Radius = r * 1.05f, Band = band });
                }
            }

            // ---------------- far band (2): silhouette islets almost fogged into the cloud sea ----------------
            m.Band = 0.75f;
            for (int k = 0; k < b.FarIslets; k++)
            {
                // Around the north, west and east, 16 to 30 u out.
                float u = (k + R(ref s) * 0.6f) / Mathf.Max(1, b.FarIslets);
                float x, z;
                float out0 = 16f + 14f * R(ref s);
                if (u < 0.3f) { x = -out0; z = -H * (u / 0.3f) * 0.95f; }
                else if (u < 0.7f) { x = W * ((u - 0.3f) / 0.4f); z = out0; }
                else { x = W + out0; z = -H * ((u - 0.7f) / 0.3f) * 0.95f; }
                float r = 1.6f + 2.4f * R(ref s);
                var c = new Vector3(x, -7f - 2.5f * R(ref s), z);
                m.Islet(c, r, r * 1.6f, 6, 200u + (uint)k, 0, false);
                f.Add(new BeyondFeature { Name = "far islet", Centre = c, Radius = r, Band = 2 });
            }

            // ---------------- the homeland massifs (wide cameras only) ----------------
            if (b.Massifs)
            {
                m.Band = 0.85f;
                m.Team = 1f;
                for (int k = 0; k < 4; k++)
                {
                    var c = new Vector3(-42f - 9f * k + 3f * R(ref s), -5f - k * 0.6f, -H * 0.25f - 12f * k + 6f * R(ref s));
                    float r = 7f + 3f * R(ref s);
                    m.Islet(c, r, 9f, 8, 300u + (uint)k, 0, false, BeyondMesh.Hex("#9FB98A"));
                    m.Tower(c + new Vector3(0f, 0f, 0f), 1.1f, 6f + 3f * R(ref s), 8, 310u + (uint)k, BeyondMesh.WallStone, BeyondMesh.WallCap, BeyondMesh.AzureRoof, false, BeyondMesh.AzureGlow, true);
                    m.Tower(c + new Vector3(2.4f, 0f, 1.6f), 0.8f, 4f, 7, 320u + (uint)k, BeyondMesh.WallStone, BeyondMesh.WallCap, BeyondMesh.AzureRoof, false, BeyondMesh.AzureGlow, true);
                    f.Add(new BeyondFeature { Name = "azure massif", Centre = c, Radius = r, Band = 2 });
                }
                m.Team = 2f;
                for (int k = 0; k < 4; k++)
                {
                    var c = new Vector3(W + 42f + 9f * k + 3f * R(ref s), -5f - k * 0.6f, -H * 0.25f - 12f * k + 6f * R(ref s));
                    float r = 7f + 3f * R(ref s);
                    m.Islet(c, r, 9f, 8, 400u + (uint)k, 0, false, BeyondMesh.Hex("#A9A57C"));
                    for (int j = 0; j < 4; j++)
                    {
                        Vector3 at = c + new Vector3(-2f + j * 1.4f, 0f, (j % 2) * 1.5f - 0.7f);
                        float h = 4f + 5f * R(ref s);
                        m.Lathe(at, new[] { h, h - 0.3f, 0f }, new[] { 0.9f, 1.05f, 1.15f }, new[] { BeyondMesh.BasaltCap, BeyondMesh.Basalt, BeyondMesh.Basalt }, 6, 410u + (uint)(k * 4 + j), 0.02f, true, 0f, BeyondMesh.Basalt);
                        if (j % 2 == 0) m.Spike(at + Vector3.up * h, Vector3.up, 0.9f, 0.3f, BeyondMesh.Ember);
                    }
                    f.Add(new BeyondFeature { Name = "crimson massif", Centre = c, Radius = r, Band = 2 });
                }
                m.Team = 0f;
            }

            // ---------------- the play island's own underside: roots hanging off the rim (band 3) ----------------
            m.Band = 0f;
            if (rim != null && b.RimRootPercent > 0)
            {
                int placed = 0;
                for (int i = 0; i < rim.Count; i++)
                {
                    WorldEdge e = rim[i];
                    if (e.Bridge) continue;
                    uint h = (uint)(i * 2654435761u) ^ 0x9E3779B9u;
                    if ((int)((R(ref h)) * 100f) >= b.RimRootPercent) continue;
                    float t = 0.2f + 0.6f * R(ref h);
                    float sx = (float)(e.Px + (e.Qx - e.Px) * t), sz = (float)(e.Pz + (e.Qz - e.Pz) * t);
                    var n = new Vector3((float)e.Nx, 0f, (float)-e.Nz);
                    if (n.sqrMagnitude < 1e-6f) continue;
                    n.Normalize();
                    var at = new Vector3(sx, (float)e.H - 0.35f - 0.9f * R(ref h), -sz) + n * 0.1f;
                    Vector3 tip = m.Root(at, n, 1.2f + 2.6f * R(ref h), 0.07f + 0.05f * R(ref h), h);
                    if (rootTips != null && placed % 5 == 0) rootTips.Add(tip);
                    placed++;
                }
                f.Add(new BeyondFeature { Name = "rim roots x" + placed, Centre = new Vector3(W * 0.5f, -1.5f, -H * 0.5f), Radius = 0f, Band = 3 });
            }
            // Drips also from the near islets' roots: their tips are not tracked one by one; a few points under the landmarks.
            if (rootTips != null && !low)
                foreach (BeyondFeature x in f)
                    if (x.Band == 0 && x.Radius > 0f) rootTips.Add(x.Centre + new Vector3(x.Radius * 0.3f, -x.Radius * 1.3f, -x.Radius * 0.2f));
            return f;
        }
    }

    /// <summary>
    /// Draws the world around the arena (world around arena 1): every island, landmark, root and waterfall of
    /// WorldBeyondLayout in ONE combined mesh with CM/WorldBeyond (one draw for the fill, one for the ink line; Low
    /// drops the ink). Rebuilt only when the world or the quality level changes; no CPU per frame (root sway and water
    /// scroll in the shader). Visual only: reads the map size and WorldView.Plan's rim, never writes the Sim; its
    /// placements use its own seed. Captures may hide it (Shown = false) for before / after frames.
    /// </summary>
    [GameOverlay(Order = 77, Scope = OverlayScope.Always)]
    public sealed class WorldBeyond : MonoBehaviour
    {
        public static WorldBeyond Current { get; private set; }
        /// <summary>False hides the world around the arena and the ambient extras that belong to it (before / after captures).</summary>
        public static bool Shown = true;
        public static event Action ShownChanged;

        public static void SetShown(bool on)
        {
            if (Shown == on) return;
            Shown = on;
            ShownChanged?.Invoke();
        }

        SimDriverBehaviour _driver;
        WorldView _view;
        CriticalMass.Sim.Core.World _builtWorld, _waitWorld;
        int _builtLevel = -1, _waitFrames;
        GameObject _root;
        Mesh _mesh;
        Material _mat;

        public int Triangles { get; private set; }
        public int Draws { get; private set; }
        public int Features { get; private set; }
        public int Builds { get; private set; }
        public IReadOnlyList<BeyondFeature> LastFeatures => _features;
        readonly List<BeyondFeature> _features = new List<BeyondFeature>();

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        void OnDestroy()
        {
            Clear();
            if (_mat != null) Destroy(_mat);
        }

        void LateUpdate()
        {
            if (_driver == null)
            {
                _driver = GetComponent<SimDriverBehaviour>();
                if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
                if (_driver == null) return;
            }
            if (_view == null) _view = GetComponent<WorldView>();
            if (_driver.World == null || _driver.World.Map == null) return;
            if (_driver.World != _waitWorld) { _waitWorld = _driver.World; _waitFrames = 0; }
            if (_view != null && _view.Plan == null && ++_waitFrames < 120) return;
            if (_driver.World != _builtWorld || RenderTier.Level != _builtLevel) Build(_driver.World, _view != null ? _view.Plan : null, RenderTier.Level);
            if (_root != null && _root.activeSelf != Shown) _root.SetActive(Shown);
        }

        void Clear()
        {
            if (_mesh != null) Destroy(_mesh);
            _mesh = null;
            if (_root != null) Destroy(_root);
            _root = null;
        }

        void Build(CriticalMass.Sim.Core.World w, WorldPlan plan, int level)
        {
            Clear();
            _builtWorld = w;
            _builtLevel = level;
            Builds++;
            WorldBeyondBudget b = WorldBeyondBudget.For(level);
            if (_mat == null)
            {
                Shader sh = Resources.Load<Shader>("CMWorldBeyond");
                if (sh == null) sh = Shader.Find("CM/WorldBeyond");
                if (sh == null) return;
                _mat = new Material(sh) { name = "World Beyond" };
            }
            _mat.SetShaderPassEnabled("SRPDefaultUnlit", b.Ink);
            var m = new BeyondMesh();
            _features.Clear();
            _features.AddRange(WorldBeyondLayout.Build(m, b, w.Map.Width, w.Map.Height, plan != null ? plan.Rim : null));
            Features = _features.Count;
            _mesh = ToMesh(m);
            _root = new GameObject("World Beyond");
            _root.transform.SetParent(transform, false);
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _root.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            Triangles = m.Tris * (b.Ink ? 2 : 1);
            Draws = b.Draws;
            _root.SetActive(Shown);
        }

        public static Mesh ToMesh(BeyondMesh m)
        {
            var mesh = new Mesh { name = "World Beyond", indexFormat = m.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(m.V); mesh.SetNormals(m.N); mesh.SetColors(m.C);
            mesh.SetUVs(0, m.U0); mesh.SetUVs(1, m.U1); mesh.SetUVs(2, m.U2);
            mesh.SetTriangles(m.T, 0, true);
            // Roots sway a few centimetres; the bounds from the vertices plus a margin cover it.
            Bounds bb = mesh.bounds;
            bb.Expand(1f);
            mesh.bounds = bb;
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}

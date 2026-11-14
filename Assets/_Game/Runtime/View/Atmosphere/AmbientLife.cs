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
    /// Per-quality budget of the ambient life (arena night life 1). Pure; EditMode tests hold it. Everything is off or
    /// reduced on Low (the other agent's frame-cost work must never pay for scenery): no creatures, birds, fireflies or
    /// mist on Low, eight cloud wisps and half the lantern lights. Triangles per item are the meshes AmbientMeshes builds.
    /// </summary>
    public readonly struct AmbientBudget
    {
        public readonly int Skyrays, Loafers, Flocks, BirdsPerFlock, Clouds, Fireflies, MistPerFall, MaxFalls;
        /// <summary>1 every lantern gets a halo and a pool, 2 every second one, 0 none.</summary>
        public readonly int LanternStride;
        /// <summary>World around arena 1: night gliders (they replace the day birds after dusk), cloud-sea banks with a
        /// moonlit rim, wind streaks, far lightning in the cloud sea (night only), water drips from hanging roots.</summary>
        public readonly int Gliders, Banks, Streaks, Bolts, Drips;

        public AmbientBudget(int skyrays, int loafers, int flocks, int birdsPerFlock, int clouds, int fireflies, int mistPerFall, int maxFalls, int lanternStride,
                             int gliders = 0, int banks = 0, int streaks = 0, int bolts = 0, int drips = 0)
        {
            Skyrays = skyrays; Loafers = loafers; Flocks = flocks; BirdsPerFlock = birdsPerFlock; Clouds = clouds;
            Fireflies = fireflies; MistPerFall = mistPerFall; MaxFalls = maxFalls; LanternStride = lanternStride;
            Gliders = gliders; Banks = banks; Streaks = streaks; Bolts = bolts; Drips = drips;
        }

        public const int MaxLanterns = 160;

        public static AmbientBudget For(int level)
        {
            if (level <= FramePolicy.LevelLow) return new AmbientBudget(0, 0, 0, 0, 8, 0, 0, 0, 2, 0, 4, 0, 0, 0);
            if (level == FramePolicy.LevelMedium) return new AmbientBudget(3, 1, 2, 7, 16, 96, 3, 10, 1, 5, 6, 8, 3, 12);
            return new AmbientBudget(5, 2, 4, 7, 26, 192, 5, 14, 1, 10, 10, 14, 5, 24);
        }

        /// <summary>The same budget without the world-around-arena extras (before / after captures).</summary>
        public AmbientBudget WithoutWorldExtras() =>
            new AmbientBudget(Skyrays, Loafers, Flocks, BirdsPerFlock, Clouds, Fireflies, MistPerFall, MaxFalls, LanternStride);

        public int Birds => Flocks * BirdsPerFlock;
        public int Lanterns(int available) => LanternStride <= 0 ? 0 : Math.Min(MaxLanterns, (available + LanternStride - 1) / LanternStride);
        /// <summary>Mist puffs at the foot of the world's own waterfall (one fall).</summary>
        public int FallMist => Banks > 0 ? MistPerFall : 0;

        /// <summary>Triangles drawn at most (creature fill + ink hull, sprites x 2 per quad, two per lantern).</summary>
        public int Triangles(int lanternsAvailable) =>
            2 * (Skyrays * AmbientMeshes.SkyrayTris + Loafers * AmbientMeshes.LoaferTris + Birds * AmbientMeshes.BirdTris + Gliders * AmbientMeshes.GliderTris) +
            2 * (Clouds + Fireflies + MistPerFall * MaxFalls + 2 * Lanterns(lanternsAvailable) + Banks + Streaks + Bolts + Drips + FallMist);

        /// <summary>Draw calls: creature fill and ink (when any), the alpha sprites, the additive sprites (when any).</summary>
        public int Draws(int lanternsAvailable) =>
            (Skyrays + Loafers + Birds + Gliders > 0 ? 2 : 0) + (Clouds + MistPerFall * MaxFalls + Banks + Streaks + Drips > 0 ? 1 : 0) +
            (Fireflies + Lanterns(lanternsAvailable) + Bolts > 0 ? 1 : 0);
    }

    /// <summary>
    /// The creature meshes, built by code (original designs, simple toon shapes): the SKYRAY, a broad diamond glider with
    /// a whip tail, two short head horns and a row of glow spots down its spine; the CLOUD-LOAFER, a long, slow, round
    /// grazer with four paddle fins, a fan tail and glow spots along its flanks; and a two-wing BIRD chevron. Authored in
    /// the creature's frame (+z forward, +x right, y up, 1 unit = 1 m before scale) with smooth normals (the ink hull
    /// follows them) and vertex colour (sRGB; alpha 1 = glow spot). Pure: tests count the triangles.
    /// </summary>
    public static class AmbientMeshes
    {
        public const int SkyrayTris = 55, LoaferTris = 120, BirdTris = 4, GliderTris = 12;

        public sealed class Part
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();
            public int Tris => T.Count / 3;

            public int Add(Vector3 v, Vector3 n, Color c)
            {
                V.Add(v); N.Add(n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up); C.Add(c);
                return V.Count - 1;
            }

            public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
        }

        static Color Hex(string h, float glow = 0f)
        {
            ColorUtility.TryParseHtmlString(h, out Color c);
            c.a = glow;
            return c;
        }

        /// <summary>Skyray: wingspan 3.6, length 2.2 plus a 1.6 tail. Slate-teal back, cream belly, gold glow spots.</summary>
        public static Part Skyray()
        {
            var p = new Part();
            Color back = Hex("#4F6F86"), edge = Hex("#3C566C"), belly = Hex("#D9DCCB"), spot = Hex("#F2E6B8", 1f);
            // Outline of the wing (right half, z forward), mirrored: nose, horn base, shoulder, tip, trailing, tail root.
            Vector2[] rim = { new Vector2(0f, 1.15f), new Vector2(0.38f, 0.85f), new Vector2(1.05f, 0.35f), new Vector2(1.8f, -0.15f),
                              new Vector2(1.05f, -0.55f), new Vector2(0.32f, -0.95f) };
            // Top surface: a fan from the raised spine centre (0, 0.16, 0) and a spine point at the rear.
            int cTop = p.Add(new Vector3(0f, 0.16f, 0.05f), Vector3.up, back);
            int cBot = p.Add(new Vector3(0f, -0.10f, 0.05f), Vector3.down, belly);
            var topR = new int[rim.Length]; var topL = new int[rim.Length]; var botR = new int[rim.Length]; var botL = new int[rim.Length];
            for (int i = 0; i < rim.Length; i++)
            {
                Vector2 q = rim[i];
                float y = -0.02f * q.x * q.x;
                Color tc = i == 3 ? edge : back;
                topR[i] = p.Add(new Vector3(q.x, y + 0.03f, q.y), new Vector3(q.x * 0.3f, 1f, q.y * 0.2f), tc);
                topL[i] = i == 0 ? topR[0] : p.Add(new Vector3(-q.x, y + 0.03f, q.y), new Vector3(-q.x * 0.3f, 1f, q.y * 0.2f), tc);
                botR[i] = p.Add(new Vector3(q.x, y - 0.03f, q.y), new Vector3(q.x * 0.3f, -1f, q.y * 0.2f), belly);
                botL[i] = i == 0 ? botR[0] : p.Add(new Vector3(-q.x, y - 0.03f, q.y), new Vector3(-q.x * 0.3f, -1f, q.y * 0.2f), belly);
            }
            for (int i = 0; i < rim.Length - 1; i++)
            {
                p.Tri(cTop, topR[i], topR[i + 1]); p.Tri(cTop, topL[i + 1], topL[i]);
                p.Tri(cBot, botR[i + 1], botR[i]); p.Tri(cBot, botL[i], botL[i + 1]);
            }
            // Close the rear between the two tail roots (top and bottom).
            p.Tri(cTop, topR[rim.Length - 1], topL[rim.Length - 1]);
            p.Tri(cBot, botL[rim.Length - 1], botR[rim.Length - 1]);
            // Rim strip joining top and bottom (so the hull has a closed edge).
            for (int i = 0; i < rim.Length - 1; i++)
            {
                p.Tri(topR[i], botR[i], botR[i + 1]); p.Tri(topR[i], botR[i + 1], topR[i + 1]);
                p.Tri(topL[i], botL[i + 1], botL[i]); p.Tri(topL[i], topL[i + 1], botL[i + 1]);
            }
            // Whip tail: a thin pyramid from the rear.
            int t0 = p.Add(new Vector3(0.08f, 0.02f, -0.95f), new Vector3(1f, 0.2f, 0f), edge);
            int t1 = p.Add(new Vector3(-0.08f, 0.02f, -0.95f), new Vector3(-1f, 0.2f, 0f), edge);
            int t2 = p.Add(new Vector3(0f, 0.10f, -0.95f), Vector3.up, edge);
            int tip = p.Add(new Vector3(0f, 0.04f, -2.55f), Vector3.back, edge);
            p.Tri(t0, tip, t2); p.Tri(t2, tip, t1); p.Tri(t1, tip, t0);
            // Head horns: two small spikes forward.
            foreach (float sx in new[] { 1f, -1f })
            {
                int h0 = p.Add(new Vector3(0.30f * sx, 0.06f, 0.88f), new Vector3(sx, 0.4f, 0.2f), edge);
                int h1 = p.Add(new Vector3(0.18f * sx, 0.06f, 0.98f), new Vector3(sx * 0.2f, 0.4f, 1f), edge);
                int h2 = p.Add(new Vector3(0.36f * sx, 0.04f, 1.32f), Vector3.forward, edge);
                if (sx > 0) p.Tri(h0, h1, h2); else p.Tri(h1, h0, h2);
            }
            // Spine glow spots: four small diamonds lifted off the back.
            for (int k = 0; k < 4; k++)
            {
                float z = 0.55f - k * 0.38f, s = 0.09f - k * 0.012f, y = 0.175f - k * 0.01f;
                int a = p.Add(new Vector3(0f, y, z + s), Vector3.up, spot), b = p.Add(new Vector3(s, y, z), Vector3.up, spot);
                int c = p.Add(new Vector3(0f, y, z - s), Vector3.up, spot), d = p.Add(new Vector3(-s, y, z), Vector3.up, spot);
                p.Tri(a, b, c); p.Tri(a, c, d);
            }
            return p;
        }

        /// <summary>Cloud-loafer: length 9, body radius 1.1. Dusty lavender back, pale belly, gold glow spots on the flanks.</summary>
        public static Part Loafer()
        {
            var p = new Part();
            Color back = Hex("#8C87AA"), belly = Hex("#DCD6E4"), fin = Hex("#726E93"), spot = Hex("#F2E6B8", 1f);
            const int rings = 8, seg = 6;
            float[] z = { 4.4f, 3.7f, 2.6f, 1.2f, -0.3f, -1.8f, -3.1f, -4.1f };
            float[] r = { 0.25f, 0.75f, 1.05f, 1.12f, 1.05f, 0.82f, 0.50f, 0.22f };
            var idx = new int[rings, seg];
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    float a = j * Mathf.PI * 2f / seg;
                    Vector3 n = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.9f, 0f);
                    Vector3 v = new Vector3(n.x * r[i], n.y * r[i] * 0.85f, z[i]);
                    bool spotHere = (i == 3 || i == 4) && (j == 0 || j == 3);
                    idx[i, j] = p.Add(v, n, spotHere ? spot : (n.y > -0.2f ? back : belly));
                }
            int nose = p.Add(new Vector3(0f, 0f, 4.75f), Vector3.forward, back);
            int tail = p.Add(new Vector3(0f, 0.05f, -4.4f), Vector3.back, back);
            for (int j = 0; j < seg; j++)
            {
                int jn = (j + 1) % seg;
                p.Tri(nose, idx[0, jn], idx[0, j]);
                p.Tri(tail, idx[rings - 1, j], idx[rings - 1, jn]);
                for (int i = 0; i < rings - 1; i++)
                {
                    p.Tri(idx[i, j], idx[i, jn], idx[i + 1, jn]);
                    p.Tri(idx[i, j], idx[i + 1, jn], idx[i + 1, j]);
                }
            }
            // Four paddle fins (two front, two rear) and a fan tail; each fin is two triangles, both faces.
            void Fin(Vector3 root, Vector3 tipOff, Vector3 back2, Color c)
            {
                Vector3 n = Vector3.Cross(tipOff, back2).normalized;
                int a = p.Add(root, n, c), b = p.Add(root + tipOff, n, c), d = p.Add(root + back2, n, c), e = p.Add(root + tipOff + back2 * 0.6f, n, c);
                p.Tri(a, b, e); p.Tri(a, e, d);
                int a2 = p.Add(root, -n, c), b2 = p.Add(root + tipOff, -n, c), d2 = p.Add(root + back2, -n, c), e2 = p.Add(root + tipOff + back2 * 0.6f, -n, c);
                p.Tri(a2, e2, b2); p.Tri(a2, d2, e2);
            }
            foreach (float sx in new[] { 1f, -1f })
            {
                Fin(new Vector3(0.95f * sx, -0.25f, 1.8f), new Vector3(1.5f * sx, -0.35f, -0.4f), new Vector3(0f, 0f, -0.9f), fin);
                Fin(new Vector3(0.75f * sx, -0.2f, -1.9f), new Vector3(1.0f * sx, -0.25f, -0.3f), new Vector3(0f, 0f, -0.6f), fin);
            }
            Fin(new Vector3(0f, 0.05f, -4.2f), new Vector3(1.5f, 0f, -1.1f), new Vector3(-1.5f, 0f, 0f), fin);
            Fin(new Vector3(0f, 0.05f, -4.2f), new Vector3(-1.5f, 0f, -1.1f), new Vector3(1.5f, 0f, 0f), fin);
            return p;
        }

        /// <summary>
        /// Lantern glider (world around arena 1, original): the night flier that replaces the day birds. A swept crescent
        /// wing of dusk blue, two long tail streamers and a warm lantern under the body (glow spot, lit at night), so at
        /// night it reads as a drifting light with a dark wing over it. Wingspan 1.5, length 1.1. Both faces authored.
        /// </summary>
        public static Part Glider()
        {
            var p = new Part();
            Color wing = Hex("#3E4A6E"), edge = Hex("#55638F"), lamp = Hex("#FFE3A0", 1f);
            foreach (float up in new[] { 1f, -1f })
            {
                Vector3 n = up > 0 ? Vector3.up : Vector3.down;
                int nose = p.Add(new Vector3(0f, 0.02f, 0.28f), n, wing), root = p.Add(new Vector3(0f, 0f, -0.18f), n, wing);
                int rt = p.Add(new Vector3(0.75f, 0.06f, -0.36f), n, edge), ri = p.Add(new Vector3(0.26f, 0f, -0.12f), n, wing);
                int lt = p.Add(new Vector3(-0.75f, 0.06f, -0.36f), n, edge), li = p.Add(new Vector3(-0.26f, 0f, -0.12f), n, wing);
                if (up > 0) { p.Tri(nose, rt, ri); p.Tri(nose, ri, root); p.Tri(nose, root, li); p.Tri(nose, li, lt); }
                else { p.Tri(nose, ri, rt); p.Tri(nose, root, ri); p.Tri(nose, li, root); p.Tri(nose, lt, li); }
                int s0 = p.Add(new Vector3(0.05f, 0f, -0.16f), n, edge), s1 = p.Add(new Vector3(-0.05f, 0f, -0.16f), n, edge), st = p.Add(new Vector3(0f, -0.02f, -0.85f), n, edge);
                if (up > 0) p.Tri(s0, s1, st); else p.Tri(s1, s0, st);
            }
            int a = p.Add(new Vector3(0f, -0.07f, 0.08f), Vector3.down, lamp), b = p.Add(new Vector3(0.08f, -0.07f, -0.03f), Vector3.down, lamp);
            int c = p.Add(new Vector3(0f, -0.07f, -0.14f), Vector3.down, lamp), d = p.Add(new Vector3(-0.08f, -0.07f, -0.03f), Vector3.down, lamp);
            p.Tri(a, c, b); p.Tri(a, d, c);
            return p;
        }

        /// <summary>Bird: a chevron of two wings (0.7 wingspan), dark slate so it reads as a silhouette.</summary>
        public static Part Bird()
        {
            var p = new Part();
            Color c = Hex("#3B4258");
            int nose = p.Add(new Vector3(0f, 0f, 0.18f), Vector3.up, c), tail = p.Add(new Vector3(0f, 0f, -0.12f), Vector3.up, c);
            int rt = p.Add(new Vector3(0.36f, 0.02f, -0.08f), Vector3.up, c), lt = p.Add(new Vector3(-0.36f, 0.02f, -0.08f), Vector3.up, c);
            p.Tri(nose, rt, tail); p.Tri(nose, tail, lt);
            int nose2 = p.Add(new Vector3(0f, 0f, 0.18f), Vector3.down, c), tail2 = p.Add(new Vector3(0f, 0f, -0.12f), Vector3.down, c);
            int rt2 = p.Add(new Vector3(0.36f, 0.02f, -0.08f), Vector3.down, c), lt2 = p.Add(new Vector3(-0.36f, 0.02f, -0.08f), Vector3.down, c);
            p.Tri(nose2, tail2, rt2); p.Tri(nose2, lt2, tail2);
            return p;
        }
    }

    /// <summary>One ambient sprite (pure data; AmbientLife packs them into a mesh).</summary>
    public struct AmbientSprite
    {
        public int Kind;
        public Vector3 Anchor;
        public float Size, Seed;
        public Vector4 Params;
    }

    /// <summary>One background creature on its loop (pure data).</summary>
    public struct AmbientCreature
    {
        public int Kind;
        public float Seed, CentreX, CentreZ, RadiusX, RadiusZ, Height, Speed, Phase, Scale;
        public Vector2 Offset;
    }

    /// <summary>
    /// Where everything goes (pure; deterministic from the map size, the plan's anchors and our OWN seed, never the Sim's
    /// random stream). Creatures circle far below and around the islands (skyrays 6.5 to 8.5 u down, loafers 10 u down,
    /// birds 2 to 3.5 u down outside the island ring), so they show only in the void between and around the islands and
    /// never over the play surface; clouds drift 5 to 7.5 u down. Fireflies sit around edge trees, mist under waterfall
    /// lips, halos and pools at lanterns and torches.
    /// </summary>
    public static class AmbientLayout
    {
        public const int Seed = 0x5EB1;

        static float R(ref uint s)
        {
            unchecked { s ^= s << 13; s ^= s >> 17; s ^= s << 5; }
            return (s & 0xFFFFFF) / 16777216f;
        }

        /// <summary>The CPU mirror of CM/AmbientCreature's path (bob and wing motion left out): Unity world position of a
        /// creature's centre at ambient clock t.</summary>
        public static Vector3 PositionAt(in AmbientCreature c, float t)
        {
            float a = c.Phase + t * c.Speed;
            return new Vector3(c.CentreX + Mathf.Cos(a) * c.RadiusX, c.Height, c.CentreZ + Mathf.Sin(a) * c.RadiusZ);
        }

        /// <summary>mapW, mapH: sim size (x east, z south; Unity z = -sim z).</summary>
        public static List<AmbientCreature> Creatures(in AmbientBudget b, int mapW, int mapH)
        {
            var o = new List<AmbientCreature>();
            uint s = (uint)Seed;
            float cx = mapW * 0.5f, cz = -mapH * 0.5f;
            for (int k = 0; k < b.Skyrays; k++)
            {
                float rx = mapW * (0.42f + 0.30f * R(ref s)), rz = mapH * (0.48f + 0.35f * R(ref s));
                float lin = 1.9f + 0.8f * R(ref s);
                o.Add(new AmbientCreature
                {
                    Kind = 0, Seed = R(ref s), CentreX = cx + (R(ref s) - 0.5f) * 10f, CentreZ = cz + (R(ref s) - 0.5f) * 8f, RadiusX = rx, RadiusZ = rz,
                    Height = -6.5f - 2f * R(ref s), Speed = (k % 2 == 0 ? 1f : -1f) * lin / ((rx + rz) * 0.5f), Phase = R(ref s) * 6.2832f,
                    Scale = 0.9f + 0.4f * R(ref s),
                });
            }
            for (int k = 0; k < b.Loafers; k++)
            {
                float rx = mapW * (0.55f + 0.25f * R(ref s)), rz = mapH * (0.65f + 0.25f * R(ref s));
                o.Add(new AmbientCreature
                {
                    Kind = 1, Seed = R(ref s), CentreX = cx, CentreZ = cz, RadiusX = rx, RadiusZ = rz, Height = -10f - R(ref s),
                    Speed = (k % 2 == 0 ? -1f : 1f) * 0.9f / ((rx + rz) * 0.5f), Phase = R(ref s) * 6.2832f, Scale = 1f + 0.25f * R(ref s),
                });
            }
            for (int f = 0; f < b.Flocks; f++)
            {
                float rx = mapW * (0.62f + 0.15f * R(ref s)), rz = mapH * (0.78f + 0.2f * R(ref s));
                float speed = (f % 2 == 0 ? 1f : -1f) * 3.6f / ((rx + rz) * 0.5f), phase = R(ref s) * 6.2832f, h = -2f - 1.5f * R(ref s);
                for (int i = 0; i < b.BirdsPerFlock; i++)
                {
                    // A loose V: the leader at the front, the others back and out on alternate sides.
                    int rank = (i + 1) / 2;
                    float side = i == 0 ? 0f : (i % 2 == 1 ? 1f : -1f);
                    o.Add(new AmbientCreature
                    {
                        Kind = 2, Seed = R(ref s), CentreX = cx, CentreZ = cz, RadiusX = rx, RadiusZ = rz, Height = h + (R(ref s) - 0.5f) * 0.3f,
                        Speed = speed, Phase = phase, Scale = 1.1f, Offset = new Vector2(side * rank * 0.75f, -rank * 0.65f + (R(ref s) - 0.5f) * 0.2f),
                    });
                }
            }
            // Night gliders: loose strings of five drifting slower than the birds, on the same outside ring.
            int groups = (b.Gliders + 4) / 5;
            for (int g = 0, made = 0; g < groups; g++)
            {
                float rx = mapW * (0.6f + 0.18f * R(ref s)), rz = mapH * (0.75f + 0.22f * R(ref s));
                float speed = (g % 2 == 0 ? -1f : 1f) * 2.2f / ((rx + rz) * 0.5f), phase = R(ref s) * 6.2832f, h = -1.8f - 1.4f * R(ref s);
                for (int i = 0; i < 5 && made < b.Gliders; i++, made++)
                    o.Add(new AmbientCreature
                    {
                        Kind = 3, Seed = R(ref s), CentreX = cx, CentreZ = cz, RadiusX = rx, RadiusZ = rz, Height = h + (R(ref s) - 0.5f) * 0.8f,
                        Speed = speed, Phase = phase, Scale = 0.9f + 0.3f * R(ref s),
                        Offset = new Vector2((R(ref s) - 0.5f) * 2.4f, -i * 1.3f),
                    });
            }
            return o;
        }

        /// <summary>World around arena 1: cloud-sea banks (kind 5), wind streaks (7) and far lightning (6), all in the void
        /// below the island tops; drips (8) under hanging-root tips; mist (1) at the foot of the world's waterfall.</summary>
        public static List<AmbientSprite> WorldExtras(in AmbientBudget b, int mapW, int mapH, IList<Vector3> rootTips, IList<Vector3> fallFeet)
        {
            var o = new List<AmbientSprite>();
            uint s = (uint)Seed ^ 0xC10Du;
            float minX = -60f, range = mapW + 120f;
            for (int k = 0; k < b.Banks; k++)
                o.Add(new AmbientSprite
                {
                    Kind = 5, Seed = R(ref s), Size = 9f + 8f * R(ref s),
                    Anchor = new Vector3(minX + range * R(ref s), -9.2f - 1.1f * R(ref s), -(-45f + (mapH + 90f) * R(ref s))),
                    Params = new Vector4(minX, range, 0.12f + 0.14f * R(ref s), 0f),
                });
            for (int k = 0; k < b.Streaks; k++)
                o.Add(new AmbientSprite
                {
                    Kind = 7, Seed = R(ref s), Size = 2.5f + 3f * R(ref s),
                    Anchor = new Vector3(minX + range * R(ref s), -1.8f - 4f * R(ref s), -(-30f + (mapH + 60f) * R(ref s))),
                    Params = new Vector4(minX, range, 2.2f + 1.4f * R(ref s), 0f),
                });
            for (int k = 0; k < b.Bolts; k++)
            {
                // Far out in the cloud sea past the north, west and east edges.
                float u = R(ref s);
                float x = u < 0.33f ? -24f - 20f * R(ref s) : u < 0.66f ? mapW * R(ref s) : mapW + 24f + 20f * R(ref s);
                float z = u < 0.33f || u >= 0.66f ? -mapH * R(ref s) : 24f + 18f * R(ref s);
                o.Add(new AmbientSprite { Kind = 6, Seed = R(ref s), Size = 7f + 5f * R(ref s), Anchor = new Vector3(x, -9.7f, z) });
            }
            if (rootTips != null)
                for (int k = 0; k < rootTips.Count && k < b.Drips; k++)
                    o.Add(new AmbientSprite { Kind = 8, Seed = R(ref s), Size = 0.06f, Anchor = rootTips[k] });
            if (fallFeet != null)
                for (int f = 0; f < fallFeet.Count; f++)
                    for (int k = 0; k < b.FallMist; k++)
                        o.Add(new AmbientSprite { Kind = 1, Seed = (k + 0.5f) / Mathf.Max(1, b.FallMist), Size = 1.6f + 0.8f * R(ref s), Anchor = fallFeet[f] + new Vector3((R(ref s) - 0.5f) * 1.2f, 0f, (R(ref s) - 0.5f) * 1.2f) });
            return o;
        }

        public static List<AmbientSprite> Clouds(in AmbientBudget b, int mapW, int mapH)
        {
            var o = new List<AmbientSprite>();
            uint s = (uint)Seed ^ 0x9E37u;
            float minX = -40f, range = mapW + 80f;
            for (int k = 0; k < b.Clouds; k++)
                o.Add(new AmbientSprite
                {
                    Kind = 0, Seed = R(ref s), Size = 4.5f + 4.5f * R(ref s),
                    Anchor = new Vector3(minX + range * R(ref s), -5f - 2.5f * R(ref s), -(-25f + (mapH + 50f) * R(ref s))),
                    Params = new Vector4(minX, range, 0.35f + 0.45f * R(ref s), 0f),
                });
            return o;
        }

        /// <summary>Sprites from the plan's anchors (world positions): trees (fireflies), waterfalls (mist), lights.</summary>
        public static List<AmbientSprite> Anchored(in AmbientBudget b, IList<Vector3> trees, IList<Vector3> falls, IList<Vector3> lightHeads, IList<Vector3> lightFeet)
        {
            var o = new List<AmbientSprite>();
            uint s = (uint)Seed ^ 0x51F1u;
            if (trees != null && trees.Count > 0)
                for (int k = 0; k < b.Fireflies; k++)
                {
                    Vector3 t = trees[(int)(R(ref s) * trees.Count) % trees.Count];
                    float a = R(ref s) * 6.2832f, d = 0.8f + 1.4f * R(ref s);
                    o.Add(new AmbientSprite { Kind = 2, Seed = R(ref s), Size = 0.11f, Anchor = t + new Vector3(Mathf.Cos(a) * d, 0.7f + 1.7f * R(ref s), Mathf.Sin(a) * d) });
                }
            if (falls != null)
                for (int f = 0; f < falls.Count && f < b.MaxFalls; f++)
                    for (int k = 0; k < b.MistPerFall; k++)
                        o.Add(new AmbientSprite { Kind = 1, Seed = (k + 0.5f) / b.MistPerFall, Size = 0.9f + 0.5f * R(ref s), Anchor = falls[f] + new Vector3((R(ref s) - 0.5f) * 0.8f, -2.6f - 1.2f * R(ref s), (R(ref s) - 0.5f) * 0.8f) });
            if (lightHeads != null && b.LanternStride > 0)
            {
                int n = 0;
                for (int i = 0; i < lightHeads.Count && n < AmbientBudget.MaxLanterns; i += b.LanternStride, n++)
                {
                    float seed = R(ref s);
                    o.Add(new AmbientSprite { Kind = 3, Seed = seed, Size = 0.55f, Anchor = lightHeads[i] });
                    if (lightFeet != null && i < lightFeet.Count) o.Add(new AmbientSprite { Kind = 4, Seed = seed, Size = 1.9f, Anchor = lightFeet[i] });
                }
            }
            return o;
        }
    }

    /// <summary>
    /// Draws the ambient life (arena night life 1): background creatures, drifting cloud wisps, waterfall mist, fireflies,
    /// lantern halos and light pools, each layer one combined mesh animated in its shader (CM/AmbientCreature,
    /// CM/AmbientSprite), so the per-frame CPU cost is nothing and the GPU cost is AmbientBudget's few draws. Rebuilt only
    /// when the world or the quality level changes. Visual only: reads WorldView.Plan and the map size, never writes the
    /// Sim; its placements use its own seed. Night-only sprites collapse to nothing in the shader when their intensity is 0.
    /// </summary>
    [GameOverlay(Order = 76, Scope = OverlayScope.Always)]
    public sealed class AmbientLife : MonoBehaviour
    {
        public static AmbientLife Current { get; private set; }

        SimDriverBehaviour _driver;
        WorldView _view;
        CriticalMass.Sim.Core.World _builtWorld;
        int _builtLevel = -1;
        CriticalMass.Sim.Core.World _waitWorld;
        int _waitFrames;
        bool _builtShown = true;
        GameObject _root;
        Material _creatureMat, _alphaMat, _addMat;
        readonly List<Mesh> _meshes = new List<Mesh>();

        /// <summary>What the last build made (tests and the capture report).</summary>
        public int Creatures { get; private set; }
        public int Sprites { get; private set; }
        public int Triangles { get; private set; }
        public int Draws { get; private set; }
        public int LanternsFound { get; private set; }
        public int Builds { get; private set; }

        /// <summary>The ambient clock the shaders animate by (_CMAmbientTime): real seconds, x 0.35 under reduce motion.
        /// Captures may pin it (ClockOverride) to place a creature in frame.</summary>
        public static float Clock { get; private set; }
        public static float? ClockOverride;
        public static readonly int ClockId = Shader.PropertyToID("_CMAmbientTime");
        /// <summary>The creatures of the last build (the capture frames one; tests check their loops stay off the islands).</summary>
        public IReadOnlyList<AmbientCreature> LastCreatures => _lastCreatures;
        readonly List<AmbientCreature> _lastCreatures = new List<AmbientCreature>();

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        void OnDestroy()
        {
            Clear();
            foreach (Material m in new[] { _creatureMat, _alphaMat, _addMat }) if (m != null) Destroy(m);
        }

        void LateUpdate()
        {
            Clock = ClockOverride ?? Clock + Time.unscaledDeltaTime * (CriticalMass.Runtime.Menu.GameSettings.ReducedMotion ? 0.35f : 1f);
            Shader.SetGlobalFloat(ClockId, Clock);
            if (_driver == null)
            {
                _driver = GetComponent<SimDriverBehaviour>();
                if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
                if (_driver == null) return;
            }
            if (_view == null) _view = GetComponent<WorldView>();
            if (_driver.World == null || _driver.World.Map == null) return;
            // Wait for the dressed world (anchors come from its plan); without the kit (no plan after about two seconds)
            // the sky still gets its life.
            if (_driver.World != _waitWorld) { _waitWorld = _driver.World; _waitFrames = 0; }
            if (_view != null && _view.Plan == null && ++_waitFrames < 120) return;
            if (_driver.World == _builtWorld && RenderTier.Level == _builtLevel && WorldBeyond.Shown == _builtShown) return;
            Build(_driver.World, _view != null ? _view.Plan : null, RenderTier.Level);
        }

        void Clear()
        {
            foreach (Mesh m in _meshes) if (m != null) Destroy(m);
            _meshes.Clear();
            if (_root != null) Destroy(_root);
            _root = null;
        }

        Material Make(string shader, bool additive)
        {
            Shader sh = Resources.Load<Shader>(shader);
            if (sh == null) sh = Shader.Find(shader == "CMAmbientCreature" ? "CM/AmbientCreature" : "CM/AmbientSprite");
            if (sh == null) return null;
            var m = new Material(sh) { name = shader + (additive ? " (add)" : "") };
            if (shader == "CMAmbientSprite")
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.renderQueue = additive ? 2960 : 2950;
            }
            return m;
        }

        void Build(CriticalMass.Sim.Core.World w, WorldPlan plan, int level)
        {
            Clear();
            _builtWorld = w;
            _builtLevel = level;
            Builds++;
            if (_creatureMat == null) _creatureMat = Make("CMAmbientCreature", false);
            if (_alphaMat == null) _alphaMat = Make("CMAmbientSprite", false);
            if (_addMat == null) _addMat = Make("CMAmbientSprite", true);
            _builtShown = WorldBeyond.Shown;
            AmbientBudget b = AmbientBudget.For(level);
            if (!_builtShown) b = b.WithoutWorldExtras();
            int mapW = w.Map.Width, mapH = w.Map.Height;

            var trees = new List<Vector3>();
            var falls = new List<Vector3>();
            var heads = new List<Vector3>();
            var feet = new List<Vector3>();
            if (plan != null) CollectAnchors(plan, trees, falls, heads, feet);
            LanternsFound = heads.Count;

            _root = new GameObject("Ambient Life");
            _root.transform.SetParent(transform, false);
            Creatures = Sprites = Triangles = Draws = 0;

            List<AmbientCreature> cs = AmbientLayout.Creatures(b, mapW, mapH);
            _lastCreatures.Clear();
            _lastCreatures.AddRange(cs);
            if (cs.Count > 0 && _creatureMat != null) Layer("Creatures", CreatureMesh(cs), _creatureMat);
            Creatures = cs.Count;

            var alpha = AmbientLayout.Clouds(b, mapW, mapH);
            var anchored = AmbientLayout.Anchored(b, trees, falls, heads, feet);
            if (b.Banks + b.Streaks + b.Bolts + b.Drips > 0)
            {
                // The drip and mist anchors come from the world around the arena (same deterministic layout, scratch mesh).
                var tips = new List<Vector3>(); var feetOfFalls = new List<Vector3>();
                WorldBeyondLayout.Build(new BeyondMesh(), WorldBeyondBudget.For(level), mapW, mapH, plan != null ? plan.Rim : null, tips, feetOfFalls);
                anchored.AddRange(AmbientLayout.WorldExtras(b, mapW, mapH, tips, feetOfFalls));
            }
            var add = new List<AmbientSprite>();
            foreach (AmbientSprite s in anchored) (s.Kind == 1 || s.Kind == 5 || s.Kind == 7 || s.Kind == 8 ? alpha : add).Add(s);
            if (alpha.Count > 0 && _alphaMat != null) Layer("Clouds and Mist", SpriteMesh(alpha), _alphaMat);
            if (add.Count > 0 && _addMat != null) Layer("Night Lights", SpriteMesh(add), _addMat);
            Sprites = alpha.Count + add.Count;
        }

        void Layer(string name, Mesh mesh, Material mat)
        {
            _meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            // Index count from the submesh descriptor: the mesh is no longer readable after UploadMeshData(true).
            Triangles += (int)mesh.GetIndexCount(0) / 3 * (mat == _creatureMat ? 2 : 1);
            Draws += mat == _creatureMat ? 2 : 1;
        }

        static readonly Bounds Huge = new Bounds(new Vector3(56f, -4f, -36f), new Vector3(600f, 80f, 600f));

        static Mesh CreatureMesh(List<AmbientCreature> cs)
        {
            AmbientMeshes.Part skyray = AmbientMeshes.Skyray(), loafer = AmbientMeshes.Loafer(), bird = AmbientMeshes.Bird(), glider = AmbientMeshes.Glider();
            var v = new List<Vector3>(); var n = new List<Vector3>(); var col = new List<Color>(); var t = new List<int>();
            var u1 = new List<Vector4>(); var u2 = new List<Vector4>(); var u3 = new List<Vector4>();
            foreach (AmbientCreature c in cs)
            {
                AmbientMeshes.Part p = c.Kind == 0 ? skyray : c.Kind == 1 ? loafer : c.Kind == 3 ? glider : bird;
                int base0 = v.Count;
                for (int i = 0; i < p.V.Count; i++)
                {
                    v.Add(p.V[i]); n.Add(p.N[i]); col.Add(p.C[i]);
                    u1.Add(new Vector4(c.Seed, c.Kind, c.Offset.x, c.Offset.y));
                    u2.Add(new Vector4(c.CentreX, c.CentreZ, c.RadiusX, c.RadiusZ));
                    u3.Add(new Vector4(c.Height, c.Speed, c.Phase, c.Scale));
                }
                foreach (int i in p.T) t.Add(base0 + i);
            }
            var m = new Mesh { name = "Ambient Creatures", indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(col);
            m.SetUVs(1, u1); m.SetUVs(2, u2); m.SetUVs(3, u3);
            m.SetTriangles(t, 0, false);
            m.bounds = Huge;
            m.UploadMeshData(true);
            return m;
        }

        static Mesh SpriteMesh(List<AmbientSprite> ss)
        {
            var v = new List<Vector3>(ss.Count * 4); var t = new List<int>(ss.Count * 6);
            var u1 = new List<Vector4>(ss.Count * 4); var u2 = new List<Vector4>(ss.Count * 4); var u3 = new List<Vector4>(ss.Count * 4);
            Vector3[] q = { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) };
            foreach (AmbientSprite s in ss)
            {
                int b0 = v.Count;
                for (int i = 0; i < 4; i++)
                {
                    v.Add(q[i]);
                    u1.Add(new Vector4(s.Seed, s.Kind, 0f, 0f));
                    u2.Add(new Vector4(s.Anchor.x, s.Anchor.y, s.Anchor.z, s.Size));
                    u3.Add(s.Params);
                }
                t.Add(b0); t.Add(b0 + 2); t.Add(b0 + 1); t.Add(b0); t.Add(b0 + 3); t.Add(b0 + 2);
            }
            var m = new Mesh { name = "Ambient Sprites", indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetUVs(1, u1); m.SetUVs(2, u2); m.SetUVs(3, u3);
            m.SetTriangles(t, 0, false);
            m.bounds = Huge;
            m.UploadMeshData(true);
            return m;
        }

        /// <summary>Edge trees, waterfall lips and light heads / feet from the plan (world positions).</summary>
        static void CollectAnchors(WorldPlan plan, List<Vector3> trees, List<Vector3> falls, List<Vector3> heads, List<Vector3> feet)
        {
            WorldKitCatalog cat = WorldKitCatalog.Load();
            var tops = new Dictionary<string, float>();
            foreach (WorldPlacement p in plan.Placements)
            {
                if (p.Piece == null) continue;
                Vector3 pos = p.Matrix.GetColumn(3);
                if (p.Cat == "tree") { trees.Add(pos); continue; }
                if (p.Piece == "env_waterfall") { falls.Add(pos); continue; }
                bool light = p.Cat == "light" || p.Piece.Contains("lantern") || p.Piece.Contains("torch") || p.Piece.Contains("brazier");
                if (!light) continue;
                if (!tops.TryGetValue(p.Piece, out float top)) tops[p.Piece] = top = LightHeadY(cat, p.Piece);
                float sy = p.Matrix.lossyScale.y;
                heads.Add(pos + Vector3.up * top * (sy > 0.01f ? sy : 1f));
                // The pool sits on the ground: hanging lanterns hang 0.69 u above their deck tile (WorldDresser).
                feet.Add(new Vector3(pos.x, p.Piece == "env_lantern_hang" ? pos.y - 0.69f : pos.y, pos.z));
            }
        }

        /// <summary>The height of a light's glowing head above its pivot: the upper part of its model's bounds.</summary>
        static float LightHeadY(WorldKitCatalog cat, string piece)
        {
            GameObject model = cat != null ? cat.Find(piece) : null;
            if (model == null) return 1.2f;
            bool any = false;
            Bounds b = default;
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                Vector3 lo = m.MultiplyPoint3x4(mb.min), hi = m.MultiplyPoint3x4(mb.max);
                var wb = new Bounds((lo + hi) * 0.5f, Vector3.zero);
                wb.Encapsulate(lo); wb.Encapsulate(hi);
                if (!any) { b = wb; any = true; } else b.Encapsulate(wb);
            }
            if (!any) return 1.2f;
            return piece == "env_lantern_hang" ? b.center.y : b.min.y + b.size.y * 0.82f;
        }
    }
}

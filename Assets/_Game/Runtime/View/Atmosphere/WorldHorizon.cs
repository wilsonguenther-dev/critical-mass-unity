using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Atmosphere
{
    /// <summary>
    /// Per-quality budget of the eye-level horizon (world horizon 1, 2026-11-14). Pure; EditMode tests hold it.
    /// Everything is ONE mesh drawn in ONE call with ONE material and no texture: the sky dome, the cloud sea, the
    /// cloud banks and the far silhouettes in up to three depth bands. Low keeps the dome and one band.
    /// </summary>
    public readonly struct WorldHorizonBudget
    {
        /// <summary>Segments around the sky dome; silhouettes in the near, mid and far band; cloud cards per near/mid band.</summary>
        public readonly int DomeSegments, Near, Mid, Far, CloudsPerBand;

        public WorldHorizonBudget(int domeSegments, int near, int mid, int far, int cloudsPerBand)
        {
            DomeSegments = domeSegments; Near = near; Mid = mid; Far = far; CloudsPerBand = cloudsPerBand;
        }

        public static WorldHorizonBudget For(int level)
        {
            if (level <= FramePolicy.LevelLow) return new WorldHorizonBudget(12, 0, 10, 0, 0);
            if (level == FramePolicy.LevelMedium) return new WorldHorizonBudget(16, 12, 0, 14, 6);
            return new WorldHorizonBudget(20, 14, 16, 20, 8);
        }

        /// <summary>Bands that carry silhouettes.</summary>
        public int Bands => (Near > 0 ? 1 : 0) + (Mid > 0 ? 1 : 0) + (Far > 0 ? 1 : 0);

        /// <summary>The horizon costs one draw call at every tier (and nothing else: no texture, no shadow, no collider).</summary>
        public const int Draws = 1;
        /// <summary>New texture memory: none. Colour is vertex colour plus the shader's sky maths.</summary>
        public const int TextureBytes = 0;
        /// <summary>The ceiling the tests hold every tier to, and the ceiling for one silhouette.</summary>
        public const int MaxTriangles = 2000, MaxPieceTriangles = 100;
    }

    /// <summary>
    /// The horizon geometry: positions are offsets from the camera (CM/WorldHorizon adds the camera position in the vertex
    /// shader and pins depth to the far plane, so it is a skybox that never clips and never covers the arena). COLOR: rgb
    /// the tone (sRGB), a the air in front of it (0 none .. 1 all horizon colour). UV0: x how much the piece follows the
    /// camera (1 = infinitely far; 0.9 = slides a tenth of the camera's travel: parallax), y kind (0 dome, 1 silhouette,
    /// 2 cloud card, 3 self-lit glow). Index order is draw order (no depth write): dome, far band, mid band, near band.
    /// </summary>
    public sealed class HorizonMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<Vector2> U = new List<Vector2>();
        public readonly List<int> T = new List<int>();
        public int Tris => T.Count / 3;

        public int Add(Vector3 p, Color c, float follow, float kind)
        {
            V.Add(p); C.Add(c); U.Add(new Vector2(follow, kind));
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }
    }

    /// <summary>One far piece (pure data, for tests and the report).</summary>
    public struct HorizonPiece
    {
        public int Band;          // 0 near, 1 mid, 2 far
        public float AzimuthDeg;  // from east toward north
        public string Kind;       // island, spire, tower, columns, falls, shard, cloud
        public int Triangles;
    }

    /// <summary>
    /// Where the far silhouettes go. Fixed layout (own seed), the same for every map. Azimuth from east (+x) toward north
    /// (+z). Each direction carries its own accent so a player knows which way they face even from the horizon: east
    /// (Crimson's side) basalt columns with ember tips, north (past the shrine) islands with falls, west (Azure's side)
    /// pale towers under teal roofs, south islands with sky-glass shards. The near band slides a tenth of the camera's
    /// travel, the mid band a twentieth, the far band almost nothing: slight parallax, no per-frame CPU.
    /// </summary>
    public static class WorldHorizonLayout
    {
        public const uint Seed = 0x40F1u;

        struct BandSpec
        {
            public float Follow, Dist, Haze, TopMin, TopMax, WidthMin, WidthMax, DepthMul;
            public Color Grass, Rock, RockDark;
        }

        static readonly BandSpec[] Bands =
        {
            new BandSpec { Follow = 0.90f, Dist = 260f, Haze = 0.34f, TopMin = 0.6f, TopMax = 3.2f, WidthMin = 5f, WidthMax = 11f, DepthMul = 0.55f,
                           Grass = BeyondMesh.Hex("#8EA67C"), Rock = BeyondMesh.Hex("#6C7A8E"), RockDark = BeyondMesh.Hex("#56627A") },
            new BandSpec { Follow = 0.95f, Dist = 330f, Haze = 0.55f, TopMin = 0.3f, TopMax = 2.0f, WidthMin = 6f, WidthMax = 13f, DepthMul = 0.45f,
                           Grass = BeyondMesh.Hex("#9BB09A"), Rock = BeyondMesh.Hex("#7A889E"), RockDark = BeyondMesh.Hex("#66738C") },
            new BandSpec { Follow = 0.985f, Dist = 400f, Haze = 0.74f, TopMin = 0.1f, TopMax = 1.1f, WidthMin = 8f, WidthMax = 16f, DepthMul = 0.32f,
                           Grass = BeyondMesh.Hex("#A4B6AA"), Rock = BeyondMesh.Hex("#8794A8"), RockDark = BeyondMesh.Hex("#7582A0") },
        };

        static readonly Color Basalt = BeyondMesh.Hex("#5E5A63"), Ember = BeyondMesh.Hex("#FFB066"), Foam = BeyondMesh.Hex("#EAF4FA"),
            Pale = BeyondMesh.Hex("#C9BFA6"), Teal = BeyondMesh.Hex("#4E9CA6"), Glass = BeyondMesh.Hex("#A8EEF5");

        /// <summary>Dome rings (elevation, degrees): dense near the horizon, where the sky and the cloud sea turn.</summary>
        static readonly float[] DomeRings = { -89f, -30f, -12f, -5f, -2f, -0.5f, 0.5f, 3f, 8f, 18f, 35f, 60f, 89f };

        public static List<HorizonPiece> Build(HorizonMesh m, in WorldHorizonBudget b)
        {
            var pieces = new List<HorizonPiece>();
            Dome(m, b.DomeSegments);
            uint s = Seed;
            // Back to front: far, mid, near (no depth write; the index order is the paint order).
            int[] counts = { b.Near, b.Mid, b.Far };
            for (int band = 2; band >= 0; band--)
            {
                int n = counts[band];
                if (n <= 0) continue;
                BandSpec bs = Bands[band];
                float phase = R(ref s) * 360f;
                var done = new bool[4];
                for (int k = 0; k < n; k++)
                {
                    float az = phase + (k + 0.25f + 0.5f * R(ref s)) * 360f / n;
                    az = Mathf.Repeat(az, 360f);
                    int t0 = m.Tris;
                    // One piece in three carries its direction's accent, and every direction gets at least one per band.
                    int sector = Sector(az);
                    string kind = Piece(m, bs, band, az, k, k % 3 == 0 || !done[sector], ref s);
                    if (kind != "island" && kind != "spire") done[sector] = true;
                    pieces.Add(new HorizonPiece { Band = band, AzimuthDeg = az, Kind = kind, Triangles = m.Tris - t0 });
                }
                if (band < 2)
                    for (int k = 0; k < b.CloudsPerBand; k++)
                    {
                        float az = Mathf.Repeat(phase + 17f + (k + R(ref s)) * 360f / Mathf.Max(1, b.CloudsPerBand), 360f);
                        int t0 = m.Tris;
                        Cloud(m, bs, az, 2.2f + 2.6f * R(ref s), -0.4f - 0.9f * R(ref s));
                        pieces.Add(new HorizonPiece { Band = band, AzimuthDeg = az, Kind = "cloud", Triangles = m.Tris - t0 });
                    }
            }
            return pieces;
        }

        public static float R(ref uint s) => WorldBeyondLayout.R(ref s);

        static void Dome(HorizonMesh m, int seg)
        {
            int first = m.V.Count;
            foreach (float e in DomeRings)
            {
                float er = e * Mathf.Deg2Rad;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j * Mathf.PI * 2f / seg;
                    m.Add(new Vector3(Mathf.Cos(er) * Mathf.Cos(a), Mathf.Sin(er), Mathf.Cos(er) * Mathf.Sin(a)) * 1000f, Color.white, 1f, 0f);
                }
            }
            int row = seg + 1;
            for (int r = 0; r < DomeRings.Length - 1; r++)
                for (int j = 0; j < seg; j++)
                {
                    int a = first + r * row + j;
                    m.Quad(a, a + 1, a + row + 1, a + row);
                }
        }

        /// <summary>Which accent a direction carries (azimuth from east toward north, degrees).</summary>
        static int Sector(float az)
        {
            az = Mathf.Repeat(az, 360f);
            return az < 45f || az >= 315f ? 0 : az < 135f ? 1 : az < 225f ? 2 : 3;
        }

        public static string Accent(float az)
        {
            az = Mathf.Repeat(az, 360f);
            if (az < 45f || az >= 315f) return "columns";
            if (az < 135f) return "falls";
            if (az < 225f) return "tower";
            return "shard";
        }

        static string Piece(HorizonMesh m, BandSpec bs, int band, float az, int k, bool accentWanted, ref uint s)
        {
            float a = az * Mathf.Deg2Rad;
            var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var right = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            float D = bs.Dist;
            float top = Mathf.Lerp(bs.TopMin, bs.TopMax, R(ref s));
            float wDeg = Mathf.Lerp(bs.WidthMin, bs.WidthMax, R(ref s));
            float w = D * wDeg * Mathf.Deg2Rad;
            float yTop = D * Mathf.Tan(top * Mathf.Deg2Rad);
            float depth = w * bs.DepthMul * (0.7f + 0.5f * R(ref s));
            float haze = bs.Haze;
            // Every fifth piece of the near and mid bands is a lone floating rock needle instead of an island.
            if (band < 2 && k % 5 == 4)
            {
                Spire(m, bs, radial * D, right, w * 0.22f, yTop + w * (0.5f + 0.4f * R(ref s)), yTop - depth * 0.8f, haze);
                return "spire";
            }
            float t = Mathf.Max(0.6f, w * 0.035f);
            Island(m, bs, radial * D, right, w, yTop, t, depth, haze, ref s);
            // The far band only takes towers and columns (they still read at 400 u); falls and shards are near and mid.
            if (!accentWanted) return "island";
            string accent = Accent(az);
            float x = (R(ref s) - 0.5f) * w * 0.4f;
            Vector3 foot = radial * D + right * x + Vector3.up * (yTop - t * 0.5f);
            switch (accent)
            {
                case "tower":
                    Rect(m, bs.Follow, foot, right, w * 0.07f, w * 0.38f, Pale, haze, 1f);
                    TriUp(m, bs.Follow, foot + Vector3.up * (w * 0.38f), right, w * 0.11f, w * 0.16f, Teal, haze, 1f);
                    break;
                case "columns":
                    for (int c = 0; c < 3; c++)
                    {
                        float h = w * (0.18f + 0.22f * R(ref s));
                        Vector3 cf = foot + right * ((c - 1) * w * 0.08f);
                        Rect(m, bs.Follow, cf, right, w * 0.05f, h, Basalt, haze, 1f);
                        TriUp(m, bs.Follow, cf + Vector3.up * h, right, w * 0.03f, w * 0.05f, Ember, haze * 0.5f, 3f);
                    }
                    break;
                case "falls":
                    if (band == 2) return "island";
                    // A white ribbon from the lip down into the cloud sea, a little below the horizon.
                    Rect(m, bs.Follow, foot + Vector3.down * (yTop + D * 0.03f), right, w * 0.035f, yTop + D * 0.03f, Foam, haze * 0.8f, 1f);
                    break;
                default:
                    if (band == 2) return "island";
                    TriUp(m, bs.Follow, foot, right, w * 0.05f, w * 0.45f, Glass, haze * 0.6f, 3f);
                    break;
            }
            return accent;
        }

        /// <summary>A flat-topped island card: a thin grass strip on top, a hanging jagged rock underside (21 triangles).</summary>
        static void Island(HorizonMesh m, BandSpec bs, Vector3 centre, Vector3 right, float w, float yTop, float t, float depth, float haze, ref uint s)
        {
            const int n = 6, nb = 5;
            float f = bs.Follow;
            Color grass = new Color(bs.Grass.r, bs.Grass.g, bs.Grass.b, haze);
            Color rock = new Color(bs.Rock.r, bs.Rock.g, bs.Rock.b, haze);
            Color dark = new Color(bs.RockDark.r, bs.RockDark.g, bs.RockDark.b, haze);
            var g0 = new int[n]; var g1 = new int[n]; var r1 = new int[n];
            for (int i = 0; i < n; i++)
            {
                float k = i / (n - 1f) * 2f - 1f;
                float bump = R(ref s) * w * 0.025f * (1f - k * k);
                float shoulder = k * k * k * k * w * 0.04f;
                Vector3 p = centre + right * (k * w * 0.5f) + Vector3.up * (yTop + bump - shoulder);
                g0[i] = m.Add(p, grass, f, 1f);
                g1[i] = m.Add(p + Vector3.down * t, grass, f, 1f);
                r1[i] = m.Add(p + Vector3.down * t, rock, f, 1f);
            }
            for (int i = 0; i < n - 1; i++) m.Quad(g0[i], g0[i + 1], g1[i + 1], g1[i]);
            int c = m.Add(centre + Vector3.up * (yTop - t - depth * 0.3f), rock, f, 1f);
            var bot = new int[nb];
            for (int j = 0; j < nb; j++)
            {
                float k = 0.85f - j * (1.7f / (nb - 1));   // right shoulder to left shoulder
                float shape = Mathf.Pow(1f - Mathf.Abs(k), 0.8f) * (0.75f + 0.5f * R(ref s));
                bot[j] = m.Add(centre + right * (k * w * 0.42f) + Vector3.up * (yTop - t - depth * Mathf.Max(0.15f, shape)), dark, f, 1f);
            }
            int prev = r1[0];
            for (int i = 1; i < n; i++) { m.Tri(c, prev, r1[i]); prev = r1[i]; }
            for (int j = 0; j < nb; j++) { m.Tri(c, prev, bot[j]); prev = bot[j]; }
            m.Tri(c, prev, r1[0]);
        }

        /// <summary>A lone floating rock needle: six points, four triangles.</summary>
        static void Spire(HorizonMesh m, BandSpec bs, Vector3 centre, Vector3 right, float halfW, float yTip, float yBottom, float haze)
        {
            float f = bs.Follow;
            Color rock = new Color(bs.Rock.r, bs.Rock.g, bs.Rock.b, haze), dark = new Color(bs.RockDark.r, bs.RockDark.g, bs.RockDark.b, haze);
            float yMid = Mathf.Lerp(yBottom, yTip, 0.38f);
            int tip = m.Add(centre + Vector3.up * yTip + right * halfW * 0.15f, rock, f, 1f);
            int l1 = m.Add(centre + right * -halfW * 0.55f + Vector3.up * Mathf.Lerp(yMid, yTip, 0.5f), rock, f, 1f);
            int l0 = m.Add(centre + right * -halfW + Vector3.up * yMid, rock, f, 1f);
            int b0 = m.Add(centre + right * halfW * -0.1f + Vector3.up * yBottom, dark, f, 1f);
            int r0 = m.Add(centre + right * halfW * 0.9f + Vector3.up * yMid, rock, f, 1f);
            int r1 = m.Add(centre + right * halfW * 0.4f + Vector3.up * Mathf.Lerp(yMid, yTip, 0.6f), rock, f, 1f);
            m.Tri(tip, l1, r1);
            m.Tri(l1, l0, r0);
            m.Tri(l1, r0, r1);
            m.Tri(l0, b0, r0);
        }

        static void Rect(HorizonMesh m, float follow, Vector3 foot, Vector3 right, float halfW, float h, Color col, float haze, float kind)
        {
            var c = new Color(col.r, col.g, col.b, haze);
            int a = m.Add(foot - right * halfW, c, follow, kind), b = m.Add(foot + right * halfW, c, follow, kind);
            int d = m.Add(foot - right * halfW + Vector3.up * h, c, follow, kind), e = m.Add(foot + right * halfW + Vector3.up * h, c, follow, kind);
            m.Quad(a, b, e, d);
        }

        static void TriUp(HorizonMesh m, float follow, Vector3 foot, Vector3 right, float halfW, float h, Color col, float haze, float kind)
        {
            var c = new Color(col.r, col.g, col.b, haze);
            m.Tri(m.Add(foot - right * halfW, c, follow, kind), m.Add(foot + right * halfW, c, follow, kind), m.Add(foot + Vector3.up * h, c, follow, kind));
        }

        /// <summary>A cloud bank card on the cloud sea in front of the island feet: a half puff, six triangles. Red channel:
        /// 1 sunlit top, 0 shaded underside (the shader picks the time-of-day cloud tones).</summary>
        static void Cloud(HorizonMesh m, BandSpec bs, float az, float radiusDeg, float centreDeg)
        {
            float a = az * Mathf.Deg2Rad, D = bs.Dist * 0.97f;
            var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var right = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            float r = D * radiusDeg * Mathf.Deg2Rad, y = D * Mathf.Tan(centreDeg * Mathf.Deg2Rad);
            Vector3 c0 = radial * D + Vector3.up * y;
            float haze = bs.Haze * 0.8f;
            int c = m.Add(c0 + Vector3.down * r * 0.15f, new Color(0.55f, 0f, 0f, haze), bs.Follow, 2f);
            var ring = new int[6];
            for (int j = 0; j < 6; j++)
            {
                float t = j / 5f * Mathf.PI;
                float lump = 1f + 0.18f * Mathf.Sin(j * 2.1f + az);
                Vector3 p = c0 + right * (Mathf.Cos(t) * r) + Vector3.up * (Mathf.Sin(t) * r * 0.45f * lump);
                ring[j] = m.Add(p, new Color(j == 0 || j == 5 ? 0.35f : 1f, 0f, 0f, haze), bs.Follow, 2f);
            }
            for (int j = 0; j < 5; j++) m.Tri(c, ring[j], ring[j + 1]);
            m.Tri(c, ring[5], ring[0]);
        }

        /// <summary>The mesh: bounds set huge (the shader places it around the camera), uploaded and made no longer readable.</summary>
        public static Mesh ToMesh(HorizonMesh h)
        {
            var mesh = new Mesh { name = "World Horizon", indexFormat = h.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(h.V);
            mesh.SetColors(h.C);
            mesh.SetUVs(0, h.U);
            mesh.SetTriangles(h.T, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}

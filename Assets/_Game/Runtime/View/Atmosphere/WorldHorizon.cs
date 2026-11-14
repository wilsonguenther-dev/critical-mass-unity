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
        /// <summary>World horizon 2: bird / lantern-glider flocks, distant lights, high cloud streaks, hanging vines and
        /// chains under the near and mid islands, and the one far creature (the Kaiju's kin over the island of the bones).</summary>
        public readonly int Flocks, Lights, HighClouds;
        public readonly bool Vines, Creature;

        public WorldHorizonBudget(int domeSegments, int near, int mid, int far, int cloudsPerBand)
            : this(domeSegments, near, mid, far, cloudsPerBand, 0, 0, 0, false, false) { }

        public WorldHorizonBudget(int domeSegments, int near, int mid, int far, int cloudsPerBand, int flocks, int lights, int highClouds,
            bool vines, bool creature)
        {
            DomeSegments = domeSegments; Near = near; Mid = mid; Far = far; CloudsPerBand = cloudsPerBand;
            Flocks = flocks; Lights = lights; HighClouds = highClouds; Vines = vines; Creature = creature;
        }

        public static WorldHorizonBudget For(int level)
        {
            if (level <= FramePolicy.LevelLow) return new WorldHorizonBudget(12, 0, 10, 0, 0, 0, 0, 0, false, true);
            if (level == FramePolicy.LevelMedium) return new WorldHorizonBudget(16, 12, 0, 14, 6, 2, 10, 4, true, true);
            return new WorldHorizonBudget(20, 14, 16, 20, 8, 3, 16, 6, true, true);
        }

        /// <summary>Bands that carry silhouettes.</summary>
        public int Bands => (Near > 0 ? 1 : 0) + (Mid > 0 ? 1 : 0) + (Far > 0 ? 1 : 0);

        /// <summary>The horizon costs one draw call at every tier (and nothing else: no texture, no shadow, no collider).</summary>
        public const int Draws = 1;
        /// <summary>New texture memory: none. Colour is vertex colour plus the shader's sky maths.</summary>
        public const int TextureBytes = 0;
        /// <summary>The ceiling the tests hold every tier to, and the ceiling for one silhouette.</summary>
        public const int MaxTriangles = 3000, MaxPieceTriangles = 100;
        /// <summary>The far creature is the one piece over 100 triangles.</summary>
        public const int MaxCreatureTriangles = 300;
    }

    /// <summary>
    /// The horizon geometry: positions are offsets from the camera (CM/WorldHorizon adds the camera position in the vertex
    /// shader and pins depth to the far plane, so it is a skybox that never clips and never covers the arena). COLOR: rgb
    /// the tone (sRGB), a the air in front of it (0 none .. 1 all horizon colour). UV0: x how much the piece follows the
    /// camera (1 = infinitely far; 0.9 = slides a tenth of the camera's travel: parallax), y kind (0 dome, 1 silhouette,
    /// 2 cloud card, 3 self-lit glow; world horizon 2: 4 waterfall (UV1.x 0 at the lip .. 1 at the foot, foam bands scroll
    /// down), 5 vine or chain (sways by UV2.x, phase UV1.w), 6 distant light (dark by day, lit at dusk and night, twinkle
    /// phase UV1.w), 7 flyer, 8 the far creature (both circle the pivot UV1.xyz at UV2.y radians a second, wing vertices
    /// flap by UV2.x at UV2.z radians a second with phase UV1.w; UV2.w 0 day only (birds roost), 1 night only (lantern
    /// gliders, self-lit), 2 always). Index order is draw order (no depth write): dome, high streaks, far band, the island
    /// of the bones and its creature, mid band, near band, flocks.
    /// </summary>
    public sealed class HorizonMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<Vector2> U = new List<Vector2>();
        public readonly List<Vector4> U1 = new List<Vector4>(), U2 = new List<Vector4>();
        public readonly List<int> T = new List<int>();
        public int Tris => T.Count / 3;

        public int Add(Vector3 p, Color c, float follow, float kind) => Add(p, c, follow, kind, Vector4.zero, Vector4.zero);

        public int Add(Vector3 p, Color c, float follow, float kind, Vector4 u1, Vector4 u2)
        {
            V.Add(p); C.Add(c); U.Add(new Vector2(follow, kind)); U1.Add(u1); U2.Add(u2);
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
        public string Kind;       // island, spire, tower, columns, falls, shard, cloud, streak, bones, creature, flock
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
            uint ws = Seed ^ 0x9E37u;   // world horizon 2 draws from its own stream: the pass 1 layout stays as it was
            for (int k = 0; k < b.HighClouds; k++)
            {
                float az = Mathf.Repeat(40f + k * 360f / b.HighClouds + 25f * R(ref ws), 360f);
                int t0 = m.Tris;
                Streak(m, az, 11f + 14f * R(ref ws), 14f + 16f * R(ref ws));
                pieces.Add(new HorizonPiece { Band = 2, AzimuthDeg = az, Kind = "streak", Triangles = m.Tris - t0 });
            }
            int lights = b.Lights;
            // Back to front: far, mid, near (no depth write; the index order is the paint order).
            int[] counts = { b.Near, b.Mid, b.Far };
            for (int band = 2; band >= 0; band--)
            {
                if (band == 1)
                {
                    // After the far band, before the mid band (paint order): the island of the old Kaiju's bones (north of
                    // the shrine, WORLD.md) and the creature circling it. On every level.
                    int t0 = m.Tris;
                    Bones(m, Bands[2], BonesAzimuth);
                    pieces.Add(new HorizonPiece { Band = 2, AzimuthDeg = BonesAzimuth, Kind = "bones", Triangles = m.Tris - t0 });
                    if (b.Creature)
                    {
                        t0 = m.Tris;
                        Creature(m, Bands[2].Follow, BonesAzimuth);
                        pieces.Add(new HorizonPiece { Band = 2, AzimuthDeg = BonesAzimuth, Kind = "creature", Triangles = m.Tris - t0 });
                    }
                }
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
                    string kind = Piece(m, bs, band, az, k, k % 3 == 0 || !done[sector], ref s, b.Vines, ref lights, ref ws);
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
            for (int k = 0; k < b.Flocks; k++)
            {
                float az = Mathf.Repeat(70f + k * 137.5f + 20f * R(ref ws), 360f);
                int t0 = m.Tris;
                Flock(m, az, k % 2 == 1, ref ws);
                pieces.Add(new HorizonPiece { Band = 0, AzimuthDeg = az, Kind = "flock", Triangles = m.Tris - t0 });
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

        static string Piece(HorizonMesh m, BandSpec bs, int band, float az, int k, bool accentWanted, ref uint s, bool vines, ref int lights, ref uint ws)
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
            // World horizon 2 variants (own stream): twin needles in the near and mid bands, thick-cliffed mesas far out.
            if (band < 2 && k % 7 == 3)
            {
                Spire(m, bs, radial * D - right * w * 0.12f, right, w * 0.16f, yTop + w * 0.55f, yTop - depth * 0.7f, haze);
                Spire(m, bs, radial * D + right * w * 0.14f, right, w * 0.12f, yTop + w * 0.38f, yTop - depth * 0.6f, haze);
                return "spire";
            }
            float t = Mathf.Max(0.6f, w * (band == 2 && k % 4 == 1 ? 0.11f : 0.035f));
            Island(m, bs, radial * D, right, w, yTop, t, depth, haze, ref s);
            if (vines && band < 2 && R(ref ws) < 0.6f) Vines(m, bs, radial * D, right, w, yTop - t, depth, haze, R(ref ws) < 0.3f, ref ws);
            if (lights > 0 && band < 2 && (k % 2 == 0 || Accent(az) == "tower"))
            {
                for (int j = 0; j < 2 && lights > 0; j++, lights--)
                    Lamp(m, bs.Follow, radial * D + right * ((R(ref ws) - 0.5f) * w * 0.5f) + Vector3.up * (yTop + D * 0.002f + j * D * 0.004f),
                        right, D * 0.0035f, haze * 0.6f, R(ref ws) * 6.28f);
            }
            // The far band only takes towers and columns (they still read at 400 u); falls and shards are near and mid.
            if (!accentWanted)
            {
                // A natural rock arch on some plain near-band islands.
                if (band == 0 && R(ref ws) < 0.35f)
                {
                    Vector3 af = radial * D + right * ((R(ref ws) - 0.5f) * w * 0.3f) + Vector3.up * (yTop - t * 0.3f);
                    Color rk = bs.Rock;
                    Rect(m, bs.Follow, af - right * w * 0.09f, right, w * 0.025f, w * 0.16f, rk, haze, 1f);
                    Rect(m, bs.Follow, af + right * w * 0.09f, right, w * 0.025f, w * 0.13f, rk, haze, 1f);
                    Rect(m, bs.Follow, af - right * w * 0.005f + Vector3.up * w * 0.12f, right, w * 0.12f, w * 0.045f, rk, haze, 1f);
                }
                return "island";
            }
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
                    // A white ribbon from the lip down into the cloud sea, a little below the horizon, foam bands running down.
                    Falls(m, bs.Follow, foot, right, w * 0.035f, yTop + D * 0.03f, haze * 0.8f);
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

        // ------------------------------------------------------------------------------------------- world horizon 2

        /// <summary>Azimuth of the island of the bones (from east toward north): north of the shrine, a little west.</summary>
        public const float BonesAzimuth = 97f;
        /// <summary>The creature: distance of its orbit's centre, orbit radius, height, wingspan (u), seconds a turn and a beat.</summary>
        public const float CreatureDist = 410f, CreatureOrbit = 50f, CreatureHeight = 66f, CreatureSpan = 40f, CreatureTurnS = 90f, CreatureBeatS = 6f;

        static readonly Color Bone = BeyondMesh.Hex("#D8CFBC"), Vine = BeyondMesh.Hex("#5E7A4A"), Iron = BeyondMesh.Hex("#3E4450"),
            Lantern = BeyondMesh.Hex("#FFC36B"), Dusk = BeyondMesh.Hex("#3C4F7A"), Bird = BeyondMesh.Hex("#3A3F4C"),
            Hide = BeyondMesh.Hex("#2F3442"), HideDark = BeyondMesh.Hex("#232733"), Wing = BeyondMesh.Hex("#3B3448");

        /// <summary>The waterfall: a ribbon in two drops that widens into mist (4 triangles), UV1.x runs down it.</summary>
        static void Falls(HorizonMesh m, float follow, Vector3 lip, Vector3 right, float halfW, float h, float haze)
        {
            var c = new Color(Foam.r, Foam.g, Foam.b, haze);
            float[] ys = { 0f, 0.55f, 1f };
            float[] ws = { 1f, 1.25f, 2.2f };
            var l = new int[3]; var r = new int[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = lip + Vector3.down * (h * ys[i]);
                var u1 = new Vector4(ys[i] * 4f, 0f, 0f, 0f);
                l[i] = m.Add(p - right * halfW * ws[i], c, follow, 4f, u1, Vector4.zero);
                r[i] = m.Add(p + right * halfW * ws[i], c, follow, 4f, u1, Vector4.zero);
            }
            for (int i = 0; i < 2; i++) m.Quad(l[i], r[i], r[i + 1], l[i + 1]);
        }

        /// <summary>Two or three hanging strands (roots, or a snapped chain one time in three) under an island (4 triangles each).</summary>
        static void Vines(HorizonMesh m, BandSpec bs, Vector3 centre, Vector3 right, float w, float yUnder, float depth, float haze, bool chain, ref uint ws)
        {
            int n = 2 + (R(ref ws) < 0.5f ? 1 : 0);
            Color col = chain ? Iron : Vine;
            var c = new Color(col.r, col.g, col.b, haze);
            for (int k = 0; k < n; k++)
            {
                float x = (R(ref ws) - 0.5f) * w * 0.55f, len = depth * (0.6f + 0.8f * R(ref ws)), hw = Mathf.Max(0.25f, w * 0.006f) * (chain ? 1.4f : 1f);
                float ph = R(ref ws) * 6.28f;
                Vector3 top = centre + right * x + Vector3.up * (yUnder - depth * 0.15f * (1f - Mathf.Abs(x) / (w * 0.3f)));
                int pl = -1, pr = -1;
                for (int j = 0; j <= 2; j++)
                {
                    float f = j / 2f;
                    var u2 = new Vector4(len * 0.06f * f * f, 0f, 0f, 0f);
                    var u1 = new Vector4(0f, 0f, 0f, ph);
                    Vector3 p = top + Vector3.down * (len * f);
                    float hh = hw * (1f - 0.5f * f);
                    int a = m.Add(p - right * hh, c, bs.Follow, 5f, u1, u2), b = m.Add(p + right * hh, c, bs.Follow, 5f, u1, u2);
                    if (pl >= 0) m.Quad(pl, pr, b, a);
                    pl = a; pr = b;
                }
            }
        }

        /// <summary>A distant light (a window, a hamlet's lamp): a small diamond, dark by day, lit at dusk and night (2 triangles).</summary>
        static void Lamp(HorizonMesh m, float follow, Vector3 p, Vector3 right, float r, float haze, float phase)
        {
            var c = new Color(Lantern.r, Lantern.g, Lantern.b, haze);
            var u1 = new Vector4(0f, 0f, 0f, phase);
            int a = m.Add(p - right * r, c, follow, 6f, u1, Vector4.zero), b = m.Add(p + Vector3.up * r * 1.3f, c, follow, 6f, u1, Vector4.zero);
            int d = m.Add(p + right * r, c, follow, 6f, u1, Vector4.zero), e = m.Add(p + Vector3.down * r * 1.3f, c, follow, 6f, u1, Vector4.zero);
            m.Quad(a, b, d, e);
        }

        /// <summary>A high, thin cloud streak (a lens, 4 triangles) that takes the time-of-day cloud tones and drifts with the banks.</summary>
        static void Streak(HorizonMesh m, float az, float elevDeg, float widthDeg)
        {
            float a = az * Mathf.Deg2Rad, D = 900f;
            var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var right = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 c0 = radial * (D * Mathf.Cos(elevDeg * Mathf.Deg2Rad)) + Vector3.up * (D * Mathf.Sin(elevDeg * Mathf.Deg2Rad));
            float hw = D * widthDeg * Mathf.Deg2Rad * 0.5f, hh = hw * 0.06f;
            const float haze = 0.45f;
            int l = m.Add(c0 - right * hw, new Color(0.6f, 0f, 0f, haze), 1f, 2f), r = m.Add(c0 + right * hw, new Color(0.6f, 0f, 0f, haze), 1f, 2f);
            int t0 = m.Add(c0 - right * hw * 0.3f + Vector3.up * hh, new Color(1f, 0f, 0f, haze), 1f, 2f);
            int t1 = m.Add(c0 + right * hw * 0.35f + Vector3.up * hh * 0.8f, new Color(1f, 0f, 0f, haze), 1f, 2f);
            int b0 = m.Add(c0 + Vector3.down * hh * 0.5f, new Color(0.3f, 0f, 0f, haze), 1f, 2f);
            m.Tri(l, t0, b0); m.Tri(t0, t1, b0); m.Tri(t1, r, b0); m.Tri(l, b0, r);
        }

        /// <summary>The island of the bones (WORLD.md): a far island with five rib arcs, a spine and the flat underbite skull.</summary>
        static void Bones(HorizonMesh m, BandSpec bs, float az)
        {
            float a = az * Mathf.Deg2Rad, D = CreatureDist;
            var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var right = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            float w = D * 12f * Mathf.Deg2Rad, yTop = D * Mathf.Tan(0.7f * Mathf.Deg2Rad);
            uint s = Seed ^ 0xB0E5u;
            Island(m, bs, radial * D, right, w, yTop, w * 0.05f, w * 0.35f, bs.Haze, ref s);
            Vector3 foot = radial * D + Vector3.up * yTop;
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2.4f) * w * 0.07f, h = w * (0.16f - 0.018f * Mathf.Abs(i - 1.6f));
                TriUp(m, bs.Follow, foot + right * x, right, w * 0.012f, h, Bone, bs.Haze, 1f);
                TriUp(m, bs.Follow, foot + right * (x + w * 0.03f), right, w * 0.008f, h * 0.8f, Bone, bs.Haze, 1f);
            }
            Rect(m, bs.Follow, foot + right * (-w * 0.05f), right, w * 0.2f, w * 0.012f, Bone, bs.Haze, 1f);
            // The skull: a flat wedge with the lower jaw jutting past the upper (the underbite), lying on its side.
            Vector3 sk = foot + right * (w * 0.24f);
            var c = new Color(Bone.r, Bone.g, Bone.b, bs.Haze);
            int s0 = m.Add(sk, c, bs.Follow, 1f), s1 = m.Add(sk + right * w * 0.09f, c, bs.Follow, 1f), s2 = m.Add(sk + right * w * 0.02f + Vector3.up * w * 0.06f, c, bs.Follow, 1f);
            int j0 = m.Add(sk + right * w * 0.02f, c, bs.Follow, 1f), j1 = m.Add(sk + right * w * 0.115f + Vector3.up * w * 0.012f, c, bs.Follow, 1f);
            m.Tri(s0, s2, s1); m.Tri(j0, s1, j1);
        }

        /// <summary>
        /// The creature (an original design: the Kaiju's kin, WORLD.md): a winged beast with a long neck, a wedge head with
        /// the family's underbite, wide fingered wings with a scalloped trailing edge, a long tail with a forked sky-glass
        /// tip and a sky-glass ridge down its back that glows faintly at night. It circles the island of the bones far out
        /// (CreatureOrbit u round a centre CreatureDist u away, one turn in CreatureTurnS s, a slow beat every CreatureBeatS
        /// s), banked into the turn, a hazed dark silhouette. Never interacts: it is a part of the sky. 82 triangles.
        /// </summary>
        static void Creature(HorizonMesh m, float follow, float az)
        {
            float a = az * Mathf.Deg2Rad;
            Vector3 pivot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * CreatureDist + Vector3.up * CreatureHeight;
            // At rest (time 0) it sits on the near side of its orbit, flying across the view.
            Vector3 radial = -new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 fwd = new Vector3(-radial.z, 0f, radial.x), rightW = Vector3.Cross(Vector3.up, fwd);
            Vector3 c0 = pivot + radial * CreatureOrbit;
            float k = CreatureSpan / 40f, bank = 28f * Mathf.Deg2Rad, cb = Mathf.Cos(bank), sb = Mathf.Sin(bank);
            float speed = 2f * Mathf.PI / CreatureTurnS, beat = 2f * Mathf.PI / CreatureBeatS;
            const float haze = 0.6f;
            int P(float x, float y, float z, Color col, float flap)
            {
                // Banked into the turn: local +x points away from the orbit's centre, so the inner (-x) wing drops.
                Vector3 p = c0 + rightW * (x * k * cb) + Vector3.up * ((y + x * sb) * k) + fwd * (z * k);
                return m.Add(p, new Color(col.r, col.g, col.b, haze), follow, 8f, new Vector4(pivot.x, pivot.y, pivot.z, 0f),
                    new Vector4(flap * 6f * k, speed, beat, 2f));
            }
            for (int side = -1; side <= 1; side += 2)
            {
                float sd = side;
                float Fl(float x) => Mathf.Clamp01((Mathf.Abs(x) - 2f) / 18f);
                // Membrane, a fan from the wrist (star-shaped, so the fan is exact).
                float[,] e = { { 2.2f, 3f }, { 9f, 5f }, { 14f, 3f }, { 20f, 1.5f }, { 18f, -1f }, { 17.5f, -4f }, { 15f, -4.5f }, { 13f, -7f }, { 7f, -3.5f }, { 2f, -2f } };
                var idx = new int[10];
                for (int i = 0; i < 10; i++) idx[i] = P(sd * e[i, 0], 0f, e[i, 1], Wing, Fl(e[i, 0]));
                int wrist = idx[2];
                for (int i = 3; i < 10; i++) m.Tri(wrist, idx[i], i + 1 < 10 ? idx[i + 1] : idx[0]);
                m.Tri(wrist, idx[0], idx[1]);
                // Arm and finger bones: thin dark spars over the membrane (a hair above it).
                float[,] bones = { { 2.2f, 3f, 9f, 5f }, { 9f, 5f, 14f, 3f }, { 14f, 3f, 20f, 1.5f }, { 14f, 3f, 17.5f, -4f }, { 14f, 3f, 13f, -7f } };
                for (int i = 0; i < 5; i++)
                {
                    float x0 = bones[i, 0], z0 = bones[i, 1], x1 = bones[i, 2], z1 = bones[i, 3], hw = i < 2 ? 0.45f : 0.25f;
                    float dx = x1 - x0, dz = z1 - z0, l = Mathf.Sqrt(dx * dx + dz * dz), nx = -dz / l * hw, nz = dx / l * hw;
                    int b0 = P(sd * (x0 + nx), 0.05f, z0 + nz, HideDark, Fl(x0)), b1 = P(sd * (x0 - nx), 0.05f, z0 - nz, HideDark, Fl(x0));
                    int b2 = P(sd * (x1 - nx), 0.05f, z1 - nz, HideDark, Fl(x1)), b3 = P(sd * (x1 + nx), 0.05f, z1 + nz, HideDark, Fl(x1));
                    m.Quad(b0, b1, b2, b3);
                }
                // A horn swept back from the head, a tucked hind leg.
                m.Tri(P(sd * 0.9f, 0.2f, 13.2f, HideDark, 0f), P(sd * 1.1f, 0.4f, 13.8f, HideDark, 0f), P(sd * 2.0f, 0.6f, 11.4f, HideDark, 0f));
                m.Tri(P(sd * 1.2f, -0.2f, -4.5f, HideDark, 0f), P(sd * 1.6f, -0.3f, -3.0f, HideDark, 0f), P(sd * 1.3f, -0.8f, -7.5f, HideDark, 0f));
            }
            // Body: a fan round its centre.
            float[,] body = { { 0f, 6f }, { 1.6f, 4.5f }, { 2.4f, 1f }, { 2.2f, -2.5f }, { 1.3f, -5f }, { 0f, -6f }, { -1.3f, -5f }, { -2.2f, -2.5f }, { -2.4f, 1f }, { -1.6f, 4.5f } };
            int bc = P(0f, 0.3f, 0f, Hide, 0f);
            for (int i = 0; i < 10; i++) m.Tri(bc, P(body[i, 0], 0f, body[i, 1], Hide, 0f), P(body[(i + 1) % 10, 0], 0f, body[(i + 1) % 10, 1], Hide, 0f));
            // Neck (3 segments, a gentle S), head, and the jutting lower jaw.
            float[] nz2 = { 6f, 8.5f, 11f, 13f }, nxo = { 0f, 0.4f, 0.2f, -0.1f }, nw = { 1.1f, 0.85f, 0.7f, 0.75f };
            for (int i = 0; i < 3; i++)
                m.Quad(P(nxo[i] - nw[i], 0f, nz2[i], Hide, 0f), P(nxo[i] + nw[i], 0f, nz2[i], Hide, 0f),
                    P(nxo[i + 1] + nw[i + 1], 0f, nz2[i + 1], Hide, 0f), P(nxo[i + 1] - nw[i + 1], 0f, nz2[i + 1], Hide, 0f));
            m.Quad(P(-1.1f, 0.1f, 13f, Hide, 0f), P(1.0f, 0.1f, 13f, Hide, 0f), P(0.5f, 0.1f, 15.8f, Hide, 0f), P(-0.6f, 0.1f, 15.8f, Hide, 0f));
            m.Quad(P(-0.9f, -0.4f, 13.3f, HideDark, 0f), P(0.8f, -0.4f, 13.3f, HideDark, 0f), P(0.75f, -0.5f, 16.7f, HideDark, 0f), P(-0.85f, -0.5f, 16.7f, HideDark, 0f));
            // Tail: five segments tapering, swinging a little, then the forked sky-glass tip.
            float[] tz = { -6f, -9f, -12f, -15f, -18f, -20.5f }, tx = { 0f, 0.5f, 1.1f, 1.2f, 0.6f, -0.3f }, tw = { 1.1f, 0.9f, 0.7f, 0.5f, 0.35f, 0.25f };
            for (int i = 0; i < 5; i++)
                m.Quad(P(tx[i] - tw[i], 0f, tz[i], Hide, 0f), P(tx[i] + tw[i], 0f, tz[i], Hide, 0f),
                    P(tx[i + 1] + tw[i + 1], 0f, tz[i + 1], Hide, 0f), P(tx[i + 1] - tw[i + 1], 0f, tz[i + 1], Hide, 0f));
            m.Tri(P(-0.55f, 0f, -20.5f, Glass, 0f), P(-1.6f, 0f, -23.2f, Glass, 0f), P(-0.1f, 0f, -21.5f, Glass, 0f));
            m.Tri(P(-0.05f, 0f, -20.5f, Glass, 0f), P(0.9f, 0f, -23.0f, Glass, 0f), P(-0.3f, 0f, -21.8f, Glass, 0f));
            // The sky-glass ridge: a row of upright shards from the crown to the tail (the family's crystal back).
            float[] rz = { 12.5f, 10f, 7.5f, 4.5f, 1.5f, -1.5f, -4.5f, -8f, -11.5f, -15f }, rh = { 1.0f, 1.2f, 1.5f, 2.0f, 2.2f, 2.0f, 1.7f, 1.3f, 1.0f, 0.7f };
            for (int i = 0; i < rz.Length; i++)
            {
                float x = i < 3 ? nxo[Mathf.Min(i + 1, 3)] * 0.5f : (i >= 7 ? tx[Mathf.Min(i - 6, 5)] : 0f);
                m.Tri(P(x, 0.2f, rz[i] + 0.9f, Glass, 0f), P(x, 0.2f, rz[i] - 0.9f, Glass, 0f), P(x, 0.2f + rh[i], rz[i] - 0.5f, Glass, 0f));
            }
        }

        /// <summary>A flock: six small fliers circling a point 160 to 220 u out (2 triangles each). By day birds (dark,
        /// roost at night); a night flock is lantern gliders, dusk-blue with a warm light under each (WORLD.md).</summary>
        static void Flock(HorizonMesh m, float az, bool lanterns, ref uint ws)
        {
            float a = az * Mathf.Deg2Rad, D = 160f + 60f * R(ref ws);
            Vector3 pivot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * D + Vector3.up * (D * Mathf.Tan((6f + 6f * R(ref ws)) * Mathf.Deg2Rad));
            float speed = (R(ref ws) < 0.5f ? 1f : -1f) * (2f * Mathf.PI / (40f + 30f * R(ref ws)));
            const float haze = 0.35f, follow = 0.9f;
            for (int i = 0; i < 6; i++)
            {
                float oa = R(ref ws) * Mathf.PI * 2f, orad = 6f + 10f * R(ref ws);
                Vector3 c0 = pivot + new Vector3(Mathf.Cos(oa) * orad, (R(ref ws) - 0.5f) * 4f, Mathf.Sin(oa) * orad);
                Vector3 f = new Vector3(-Mathf.Sin(oa), 0f, Mathf.Cos(oa)) * Mathf.Sign(speed), rt = Vector3.Cross(Vector3.up, f);
                float span = lanterns ? 1.6f : 1.3f, ph = R(ref ws) * 6.28f;
                Color body = lanterns ? Lantern : Bird, tip = lanterns ? Dusk : Bird;
                var u1 = new Vector4(pivot.x, pivot.y, pivot.z, ph);
                var still = new Vector4(0f, speed, 7f, lanterns ? 1f : 0f);
                var flap = new Vector4(0.7f * span, speed, 7f, lanterns ? 1f : 0f);
                if (lanterns) { flap.z = 3f; still.z = 3f; }
                int nose = m.Add(c0 + f * 0.5f, new Color(body.r, body.g, body.b, haze), follow, 7f, u1, still);
                int tail = m.Add(c0 - f * 0.4f, new Color(body.r, body.g, body.b, haze), follow, 7f, u1, still);
                int lt = m.Add(c0 - rt * span - f * 0.2f, new Color(tip.r, tip.g, tip.b, haze), follow, 7f, u1, flap);
                int rtp = m.Add(c0 + rt * span - f * 0.2f, new Color(tip.r, tip.g, tip.b, haze), follow, 7f, u1, flap);
                m.Tri(nose, tail, lt);
                m.Tri(nose, rtp, tail);
            }
        }

        /// <summary>The mesh: bounds set huge (the shader places it around the camera), uploaded and made no longer readable.</summary>
        public static Mesh ToMesh(HorizonMesh h)
        {
            var mesh = new Mesh { name = "World Horizon", indexFormat = h.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(h.V);
            mesh.SetColors(h.C);
            mesh.SetUVs(0, h.U);
            mesh.SetUVs(1, h.U1);
            mesh.SetUVs(2, h.U2);
            mesh.SetTriangles(h.T, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}

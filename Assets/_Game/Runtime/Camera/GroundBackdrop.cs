using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// Ground view backdrop (feat/ground-camera): from the high camera the world is ground and void; from a low camera it
    /// is also a horizon, and the solid clear colour reads as an empty blue wall. While ground view is on this draws, behind
    /// everything (render queue 1000 to 1002, no depth write, no shadows): a sky dome (SkyTop at the zenith to SkyHorizon at
    /// eye level, then a cloud sea below the horizon), a band of cloud puffs on the horizon and a ring of hazy far floating
    /// islands (flat tops, rock undersides, a few with a tower), all vertex-coloured on CM/FxUnlit (the combat effects'
    /// shader, already in every build). Centred on the camera every frame, so it is a skybox: at 250 to 400 u the missing
    /// parallax is invisible. Deterministic (fixed island table). Uses what exists; the world-around-arena pass
    /// (feat/world-around-arena-1, not on integration/batch-6 yet) replaces the islands when it lands.
    /// </summary>
    public sealed class GroundBackdrop : MonoBehaviour
    {
        public const float DomeRadius = 420f;
        const int DomeSegments = 48, DomeRings = 18;
        /// <summary>Lowest elevation of the dome (degrees): the cloud sea fills everything under the island rims.</summary>
        const float DomeBottomDeg = -60f;

        /// <summary>Far islands: azimuth (deg), distance, elevation of the top (deg), top radius, underside depth, tower.</summary>
        static readonly float[][] Islands =
        {
            new[] { 12f, 300f, 1.6f, 34f, 46f, 1f }, new[] { 48f, 360f, 3.4f, 22f, 30f, 0f }, new[] { 83f, 260f, 0.4f, 40f, 52f, 0f },
            new[] { 121f, 340f, 4.2f, 26f, 36f, 1f }, new[] { 158f, 290f, 1.0f, 30f, 44f, 0f }, new[] { 196f, 380f, 2.6f, 20f, 28f, 0f },
            new[] { 228f, 270f, -0.6f, 38f, 50f, 1f }, new[] { 262f, 350f, 3.0f, 24f, 34f, 0f }, new[] { 297f, 310f, 0.9f, 32f, 42f, 0f },
            new[] { 331f, 390f, 4.6f, 18f, 26f, 1f }, new[] { 70f, 395f, 6.0f, 14f, 20f, 0f }, new[] { 244f, 398f, 5.4f, 16f, 22f, 0f },
        };

        Material _dome, _clouds, _islands;
        readonly List<GameObject> _parts = new List<GameObject>();
        bool _built;

        /// <summary>Builds the meshes once (first show).</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;
            Shader sh = Resources.Load<Shader>("CMFxUnlit");
            if (sh == null) sh = Shader.Find("CM/FxUnlit");
            if (sh == null) return;
            _dome = Mat(sh, "Ground Backdrop Dome", 1000);
            _clouds = Mat(sh, "Ground Backdrop Clouds", 1001);
            _islands = Mat(sh, "Ground Backdrop Islands", 1002);
            Part("Sky Dome", DomeMesh(), _dome);
            Part("Far Islands", IslandMesh(), _islands);
            Part("Horizon Clouds", CloudMesh(), _clouds);
        }

        static Material Mat(Shader sh, string name, int queue)
        {
            var m = new Material(sh) { name = name, renderQueue = queue, hideFlags = HideFlags.DontSave };
            m.SetFloat("_Cull", 0);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_OffsetUnits", 0);
            return m;
        }

        void Part(string n, Mesh mesh, Material m)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            _parts.Add(go);
        }

        void OnDestroy()
        {
            foreach (GameObject p in _parts)
                if (p != null)
                {
                    MeshFilter f = p.GetComponent<MeshFilter>();
                    if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
                }
            if (_dome != null) Destroy(_dome);
            if (_clouds != null) Destroy(_clouds);
            if (_islands != null) Destroy(_islands);
        }

        /// <summary>Follows the camera (position only: the dome is a skybox). follow-camera-2: every HorizonCheckFrames it
        /// looks for the world's own eye-level horizon (feat/world-horizon-1's "World Horizon" under WorldBeyond); while that
        /// is present this backdrop's sky, clouds and far islands stay off, so the two never draw over each other.</summary>
        public void Place(Vector3 cameraPos)
        {
            transform.SetPositionAndRotation(cameraPos, Quaternion.identity);
            if (Time.frameCount < _nextCheck) return;
            _nextCheck = Time.frameCount + HorizonCheckFrames;
            var wb = FindAnyObjectByType<CriticalMass.Runtime.View.Atmosphere.WorldBeyond>();
            SetOwnSky(!HorizonPresent(wb != null ? wb.transform : null));
        }

        public const int HorizonCheckFrames = 60;
        public const string HorizonName = "World Horizon";
        int _nextCheck;

        /// <summary>True while this backdrop draws its own sky (no world horizon found).</summary>
        public bool OwnSky { get; private set; } = true;

        public void SetOwnSky(bool on)
        {
            if (on == OwnSky) return;
            OwnSky = on;
            foreach (GameObject p in _parts)
                if (p != null && p.activeSelf != on) p.SetActive(on);
        }

        /// <summary>The world's horizon is built and showing somewhere under root (pure over the hierarchy; tests).</summary>
        public static bool HorizonPresent(Transform root)
        {
            if (root == null) return false;
            if (root.name == HorizonName) return root.gameObject.activeInHierarchy;
            for (int i = 0; i < root.childCount; i++)
                if (HorizonPresent(root.GetChild(i))) return true;
            return false;
        }

        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out Color c) ? c : Color.magenta;

        static readonly Color Top = Hex(Palette.SkyTop), Horizon = Hex(Palette.SkyHorizon);
        static readonly Color Sea = new Color(0.86f, 0.93f, 0.96f), SeaDeep = new Color(0.66f, 0.80f, 0.90f);
        static readonly Color IsleTop = new Color(0.55f, 0.72f, 0.62f), IsleRock = new Color(0.50f, 0.58f, 0.70f), IsleDark = new Color(0.40f, 0.47f, 0.62f);

        /// <summary>Sky colour at an elevation (degrees): the dome's gradient, also the islands' haze target.</summary>
        public static Color SkyAt(float elevDeg)
        {
            if (elevDeg >= 0f)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Sin(elevDeg * Mathf.Deg2Rad) / 0.55f));
                return Color.Lerp(Horizon, Top, t);
            }
            float u = Mathf.Clamp01(-elevDeg / 4f);
            Color c = Color.Lerp(Horizon, Sea, u);
            float v = Mathf.Clamp01((-elevDeg - 4f) / 40f);
            return Color.Lerp(c, SeaDeep, v);
        }

        static Mesh DomeMesh()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            // Elevation rings, denser near the horizon where the gradient turns.
            var elev = new List<float>();
            for (int i = 0; i <= DomeRings; i++)
            {
                float k = (float)i / DomeRings;
                elev.Add(Mathf.Lerp(DomeBottomDeg, 90f, k * k * 0.35f + k * 0.65f));
            }
            elev.Insert(1, -2f);
            elev.Insert(1, -8f);
            elev.Sort();
            for (int r = 0; r < elev.Count; r++)
            {
                float e = elev[r] * Mathf.Deg2Rad;
                Color col = SkyAt(elev[r]);
                for (int s = 0; s <= DomeSegments; s++)
                {
                    float a = (float)s / DomeSegments * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(e) * Mathf.Sin(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Cos(a)) * DomeRadius);
                    c.Add(col);
                }
            }
            int row = DomeSegments + 1;
            for (int r = 0; r < elev.Count - 1; r++)
                for (int s = 0; s < DomeSegments; s++)
                {
                    int a = r * row + s, b = a + row;
                    t.Add(a); t.Add(b); t.Add(a + 1);
                    t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
            return Make("Sky Dome", v, c, t);
        }

        /// <summary>One floating island: an elliptical top with a lit rim, a rock cone hanging under it, maybe a tower.</summary>
        static void Island(List<Vector3> v, List<Color> c, List<int> t, float azDeg, float dist, float elevDeg, float r, float depth, bool tower)
        {
            float az = azDeg * Mathf.Deg2Rad;
            var centre = new Vector3(Mathf.Sin(az) * dist, Mathf.Tan(elevDeg * Mathf.Deg2Rad) * dist, Mathf.Cos(az) * dist);
            // Atmospheric haze: far and high islands sit closer to the sky colour (ink at 400 u is mostly air).
            float haze = Mathf.Clamp01(0.42f + (dist - 250f) / 400f);
            Color sky = SkyAt(elevDeg);
            Color top = Color.Lerp(IsleTop, sky, haze), rim = Color.Lerp(IsleTop * 1.12f, sky, haze * 0.9f);
            Color rock = Color.Lerp(IsleRock, sky, haze), dark = Color.Lerp(IsleDark, sky, haze * 0.85f);
            const int n = 14;
            var right = new Vector3(Mathf.Cos(az), 0f, -Mathf.Sin(az));
            var fwd = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az));
            int seed = Mathf.RoundToInt(azDeg * 7f + dist);
            // Top: a fan with a slight dome; rim ring a little lower; then rock rings tapering to a point.
            int centreIdx = v.Count;
            v.Add(centre + Vector3.up * r * 0.08f);
            c.Add(top);
            int rimStart = v.Count;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float wob = 1f + 0.12f * Mathf.Sin(a * 3f + seed) + 0.06f * Mathf.Sin(a * 5f + seed * 0.37f);
                v.Add(centre + (right * Mathf.Cos(a) * r + fwd * Mathf.Sin(a) * r * 0.55f) * wob);
                c.Add(rim);
            }
            for (int i = 0; i < n; i++) { t.Add(centreIdx); t.Add(rimStart + (i + 1) % n); t.Add(rimStart + i); }
            int prev = rimStart;
            float[] ringDepth = { 0.18f, 0.5f, 0.82f };
            float[] ringScale = { 0.92f, 0.62f, 0.28f };
            for (int k = 0; k < ringDepth.Length; k++)
            {
                int start = v.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = v[rimStart + i] - centre;
                    float jag = 1f + 0.10f * Mathf.Sin(i * 2.3f + seed + k);
                    v.Add(centre + p * ringScale[k] * jag - Vector3.up * depth * ringDepth[k]);
                    c.Add(Color.Lerp(rock, dark, ringDepth[k]));
                }
                for (int i = 0; i < n; i++)
                {
                    int a = prev + i, b = prev + (i + 1) % n, cc = start + i, d = start + (i + 1) % n;
                    t.Add(a); t.Add(b); t.Add(cc);
                    t.Add(b); t.Add(d); t.Add(cc);
                }
                prev = start;
            }
            int tip = v.Count;
            v.Add(centre - Vector3.up * depth);
            c.Add(dark);
            for (int i = 0; i < n; i++) { t.Add(prev + i); t.Add(prev + (i + 1) % n); t.Add(tip); }
            if (!tower) return;
            // A watchtower silhouette: a tapered box with a cap, near the island's left third.
            Vector3 b0 = centre - right * r * 0.35f + Vector3.up * r * 0.05f;
            float w = r * 0.07f, h = r * 0.55f;
            Color tw = Color.Lerp(new Color(0.62f, 0.56f, 0.52f), sky, haze);
            Color cap = Color.Lerp(new Color(0.55f, 0.36f, 0.30f), sky, haze);
            Quad(v, c, t, b0 - right * w, b0 + right * w, b0 + right * w * 0.8f + Vector3.up * h, b0 - right * w * 0.8f + Vector3.up * h, tw);
            Vector3 c0 = b0 + Vector3.up * h;
            int ci = v.Count;
            v.Add(c0 - right * w * 1.5f); v.Add(c0 + right * w * 1.5f); v.Add(c0 + Vector3.up * w * 2.2f);
            c.Add(cap); c.Add(cap); c.Add(cap);
            t.Add(ci); t.Add(ci + 1); t.Add(ci + 2);
        }

        static void Quad(List<Vector3> v, List<Color> c, List<int> t, Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        static Mesh IslandMesh()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            foreach (float[] i in Islands) Island(v, c, t, i[0], i[1], i[2], i[3], i[4], i[5] > 0.5f);
            return Make("Far Islands", v, c, t);
        }

        /// <summary>Soft cloud puffs on the horizon: flattened fans, opaque core fading to the edge (vertex alpha).</summary>
        static Mesh CloudMesh()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            const int puffs = 46, n = 12;
            for (int p = 0; p < puffs; p++)
            {
                float az = (p * 137.5f) % 360f * Mathf.Deg2Rad;
                float dist = 360f + (p * 53 % 40);
                float elev = -3.5f + (p * 29 % 9) * 0.55f;
                float w = 30f + (p * 31 % 26), h = w * 0.28f;
                var centre = new Vector3(Mathf.Sin(az) * dist, Mathf.Tan(elev * Mathf.Deg2Rad) * dist, Mathf.Cos(az) * dist);
                var right = new Vector3(Mathf.Cos(az), 0f, -Mathf.Sin(az));
                Color core = new Color(0.97f, 0.99f, 1f, 0.9f), edge = new Color(0.93f, 0.97f, 1f, 0f);
                int ci = v.Count;
                v.Add(centre);
                c.Add(core);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    float bump = 1f + 0.2f * Mathf.Sin(a * 3f + p);
                    float up = Mathf.Sin(a);
                    v.Add(centre + right * Mathf.Cos(a) * w * bump + Vector3.up * (up > 0 ? up * h * bump : up * h * 0.35f));
                    c.Add(edge);
                }
                for (int i = 0; i < n; i++) { t.Add(ci); t.Add(ci + 1 + i); t.Add(ci + 1 + (i + 1) % n); }
            }
            return Make("Horizon Clouds", v, c, t);
        }

        static Mesh Make(string name, List<Vector3> v, List<Color> c, List<int> t)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(t, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * DomeRadius * 2.2f);
            return m;
        }
    }
}

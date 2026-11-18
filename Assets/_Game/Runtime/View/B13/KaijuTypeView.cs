// CM b13 item K: the SHROUD cloud in the world (view only; reads KaijuTypes, writes nothing to the sim).
// b14 polish: plus the Kaiju's type halo on the ground (a soft contact shadow and a glow ring whose SHAPE names the type).
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.B13
{
    /// <summary>
    /// CM b13 item K: MIRE's SHROUD as a pooled soft ground disc plus a low band of puffs (two meshes built once, two draw calls
    /// at most, CM/FxUnlit, no texture). Both are unit-radius meshes scaled to the cloud's radius and moved to its centre (it
    /// follows the Kaiju). It fades in over the 15-tick puff and out over the cloud's last 15 ticks. An affected local viewer
    /// (KaijuTypes.Affected) sees a dense cloud; an unaffected one a thin haze. Fading writes a preallocated colour array (no
    /// per-frame allocation). Triangles: disc 2 x Segments (96), band Puffs x 4 (96): 192 in all.
    /// </summary>
    public sealed class KaijuTypeView : MonoBehaviour
    {
        public const int Segments = 48, Puffs = 24;
        /// <summary>Alpha of the disc centre and of the puffs, for an affected / unaffected viewer.</summary>
        public const float DenseDisc = 0.78f, DenseBand = 0.62f, HazeDisc = 0.22f, HazeBand = 0.14f;
        /// <summary>Slate teal-grey cloud colour (sRGB).</summary>
        static readonly Color32 CloudCol = new Color32(92, 116, 122, 255), CloudCore = new Color32(70, 90, 98, 255);
        const float BandTop = 1.3f, PuffSize = 0.22f, FadeTicks = 15f;

        // b14 polish: the type halo. One unit-radius polar mesh per type (built once in Start: 5 x 2,400 vertices), one
        // draw call, same material as the cloud. Shape per type (colour-blind safe): GROVE eight leaf petals, BRUTE ten
        // spikes, TIMBER three concentric bark rings, MIRE twelve mist dots, ELDER a solid ring with four diamonds. Under it a
        // dark soft contact shadow so the body lifts off the night ground.
        public const int HaloRings = 20, HaloSegments = 120;
        /// <summary>Halo radius in u at type scale 1 (the body's hit radius is 1.64 u; the ring sits just outside the feet).</summary>
        public const float HaloRadius = 2.9f, HaloGlowAlpha = 0.62f, HaloShadowAlpha = 0.50f;
        static readonly Color32 HaloShadow = new Color32(24, 35, 59, 255);
        GameObject _haloGo;
        MeshFilter _haloFilter;
        readonly Mesh[] _halo = new Mesh[5];
        int _haloType = -1;
        /// <summary>The halo is drawn this frame, and for which type (tests, capture tool).</summary>
        public bool HaloDrawn { get; private set; }
        public KaijuType HaloType => (KaijuType)Mathf.Max(0, _haloType);

        SimDriverBehaviour _driver;
        Material _mat;
        GameObject _discGo, _bandGo;
        Mesh _disc, _band;
        Color32[] _discBase, _discWork, _bandBase, _bandWork;
        float _lastAlpha = -1f;
        bool _lastDense;

        /// <summary>The cloud is drawn this frame (for the capture tool and tests).</summary>
        public bool Drawn { get; private set; }
        /// <summary>The drawn opacity scale (0..1) and whether it is the dense (affected) look.</summary>
        public float Alpha => _lastAlpha < 0 ? 0 : _lastAlpha;
        public bool Dense => _lastDense;
        public float Radius { get; private set; }

        void Start()
        {
            _driver = GetComponentInParent<SimDriverBehaviour>();
            Shader sh = Resources.Load<Shader>("CMFxUnlit");
            if (sh == null) sh = Shader.Find("CM/FxUnlit");
            if (_driver == null || sh == null) { enabled = false; return; }
            _mat = new Material(sh) { name = "Kaiju Shroud", renderQueue = 3090 };
            _mat.SetFloat("_Cull", 0);
            _mat.SetFloat("_ZWrite", 0);
            _mat.SetFloat("_OffsetUnits", -1);
            BuildDisc();
            BuildBand();
            _discGo = Layer("Shroud Disc", _disc);
            _bandGo = Layer("Shroud Band", _band);
            _discGo.SetActive(false);
            _bandGo.SetActive(false);
            for (int t = 0; t < _halo.Length; t++) _halo[t] = BuildHalo((KaijuType)t);
            _haloGo = Layer("Kaiju Type Halo", _halo[0]);
            _haloFilter = _haloGo.GetComponent<MeshFilter>();
            _haloGo.SetActive(false);
        }

        void OnDestroy()
        {
            Release(_discGo); Release(_bandGo); Release(_haloGo); Release(_disc); Release(_band); Release(_mat);
            for (int t = 0; t < _halo.Length; t++) Release(_halo[t]);
        }

        static void Release(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        GameObject Layer(string n, Mesh mesh)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>A unit disc: centre ring at full alpha to 0.7, then a soft rim to alpha 0 at radius 1.</summary>
        void BuildDisc()
        {
            int n = Segments;
            var v = new Vector3[1 + 2 * n];
            _discBase = new Color32[v.Length];
            var t = new int[n * 3 + n * 6];
            v[0] = Vector3.zero;
            _discBase[0] = CloudCore;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[1 + i] = new Vector3(c * 0.7f, 0, s * 0.7f);
                v[1 + n + i] = new Vector3(c, 0, s);
                _discBase[1 + i] = CloudCol;
                _discBase[1 + n + i] = new Color32(CloudCol.r, CloudCol.g, CloudCol.b, 0);
            }
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                t[k++] = 0; t[k++] = 1 + j; t[k++] = 1 + i;
                t[k++] = 1 + i; t[k++] = 1 + j; t[k++] = 1 + n + j;
                t[k++] = 1 + i; t[k++] = 1 + n + j; t[k++] = 1 + n + i;
            }
            _disc = new Mesh { name = "Shroud Disc" };
            _disc.MarkDynamic();
            _disc.vertices = v;
            _discWork = (Color32[])_discBase.Clone();
            _disc.colors32 = _discWork;
            _disc.triangles = t;
            _disc.bounds = new Bounds(Vector3.zero, new Vector3(2, 1, 2));
        }

        /// <summary>Puffs: two crossed vertical quads each (they read from above and from the side), golden-angle spread in the disc.</summary>
        void BuildBand()
        {
            int n = Puffs;
            var v = new Vector3[n * 8];
            _bandBase = new Color32[v.Length];
            var t = new int[n * 12];
            for (int i = 0; i < n; i++)
            {
                float rr = Mathf.Sqrt((i + 0.5f) / n) * 0.92f;
                float a = i * 2.39996f;
                float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
                float y0 = 0.05f, y1 = BandTop * (0.55f + 0.45f * ((i * 7) % 5) / 4f);
                float hs = PuffSize * (0.8f + 0.4f * ((i * 3) % 4) / 3f);
                int b = i * 8;
                // quad 1 along x, quad 2 along z (world height in u: the band does not scale with the radius in y)
                v[b + 0] = new Vector3(x - hs, y0, z); v[b + 1] = new Vector3(x + hs, y0, z);
                v[b + 2] = new Vector3(x + hs, y1, z); v[b + 3] = new Vector3(x - hs, y1, z);
                v[b + 4] = new Vector3(x, y0, z - hs); v[b + 5] = new Vector3(x, y0, z + hs);
                v[b + 6] = new Vector3(x, y1, z + hs); v[b + 7] = new Vector3(x, y1, z - hs);
                for (int q = 0; q < 8; q++)
                {
                    bool top = q == 2 || q == 3 || q == 6 || q == 7;
                    _bandBase[b + q] = top ? new Color32(CloudCol.r, CloudCol.g, CloudCol.b, 0) : CloudCol;
                }
                int k = i * 12;
                t[k++] = b; t[k++] = b + 1; t[k++] = b + 2; t[k++] = b; t[k++] = b + 2; t[k++] = b + 3;
                t[k++] = b + 4; t[k++] = b + 5; t[k++] = b + 6; t[k++] = b + 4; t[k++] = b + 6; t[k++] = b + 7;
            }
            _band = new Mesh { name = "Shroud Band" };
            _band.MarkDynamic();
            _band.vertices = v;
            _bandWork = (Color32[])_bandBase.Clone();
            _band.colors32 = _bandWork;
            _band.triangles = t;
            _band.bounds = new Bounds(new Vector3(0, BandTop / 2, 0), new Vector3(2, BandTop, 2));
        }

        static float Band(float r, float a, float b) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a - 0.04f, a + 0.02f, r)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(b - 0.02f, b + 0.04f, r)));

        /// <summary>The halo glow share (0..1) at polar point (r 0..1, angle a) for a type: the shape cue.</summary>
        public static float HaloPattern(KaijuType t, float r, float a)
        {
            const float Tau = Mathf.PI * 2f;
            switch (t)
            {
                case KaijuType.Grove:
                    return Band(r, 0.55f, 0.70f + 0.28f * Mathf.Pow(Mathf.Abs(Mathf.Cos(4f * a)), 0.6f));
                case KaijuType.Brute:
                {
                    float f = Mathf.Repeat(a * 10f / Tau, 1f);
                    float tri = 1f - 2f * Mathf.Abs(f - 0.5f);
                    return Band(r, 0.55f, 0.66f + 0.33f * tri * tri);
                }
                case KaijuType.Timber:
                    return Mathf.Max(Band(r, 0.52f, 0.60f), Mathf.Max(Band(r, 0.70f, 0.78f), Band(r, 0.88f, 0.96f)));
                case KaijuType.Mire:
                {
                    float best = 9f;
                    float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                    for (int i = 0; i < 12; i++)
                    {
                        float da = i * Tau / 12f;
                        float dx = x - Mathf.Cos(da) * 0.80f, dz = z - Mathf.Sin(da) * 0.80f;
                        best = Mathf.Min(best, dx * dx + dz * dz);
                    }
                    return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.09f, 0.15f, Mathf.Sqrt(best)));
                }
                default:
                {
                    float ring = Band(r, 0.78f, 0.92f);
                    float q = Mathf.Repeat(a + Mathf.PI / 4f, Mathf.PI / 2f) - Mathf.PI / 4f;   // angle from the nearest quarter
                    float dia = Mathf.Abs(q) * r / 0.16f + Mathf.Abs(r - 0.85f) / 0.15f;
                    return Mathf.Max(ring, dia < 1f ? 1f : 0f);
                }
            }
        }

        Mesh BuildHalo(KaijuType type)
        {
            Color32 glow = ColorUtility.TryParseHtmlString(KaijuTypeLook.GlowHex(type), out Color gc) ? (Color32)gc : new Color32(255, 210, 74, 255);
            int nr = HaloRings, na = HaloSegments;
            var v = new Vector3[1 + nr * na];
            var c = new Color32[v.Length];
            var t = new int[na * 3 + (nr - 1) * na * 6];
            v[0] = Vector3.zero;
            c[0] = new Color32(HaloShadow.r, HaloShadow.g, HaloShadow.b, (byte)(HaloShadowAlpha * 255));
            for (int ri = 0; ri < nr; ri++)
            {
                float r = (ri + 1) / (float)nr;
                for (int ai = 0; ai < na; ai++)
                {
                    float a = ai * Mathf.PI * 2f / na;
                    int idx = 1 + ri * na + ai;
                    v[idx] = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                    float g = Mathf.Clamp01(HaloPattern(type, r, a));
                    float sh = HaloShadowAlpha * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.70f, r)));
                    float alpha = g * HaloGlowAlpha + sh * (1f - g);
                    float wg = alpha > 1e-4f ? g * HaloGlowAlpha / alpha : 0f;
                    c[idx] = new Color32((byte)Mathf.Lerp(HaloShadow.r, glow.r, wg), (byte)Mathf.Lerp(HaloShadow.g, glow.g, wg),
                        (byte)Mathf.Lerp(HaloShadow.b, glow.b, wg), (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
                }
            }
            int k = 0;
            for (int ai = 0; ai < na; ai++)
            {
                int aj = (ai + 1) % na;
                t[k++] = 0; t[k++] = 1 + aj; t[k++] = 1 + ai;
            }
            for (int ri = 0; ri < nr - 1; ri++)
                for (int ai = 0; ai < na; ai++)
                {
                    int aj = (ai + 1) % na;
                    int i0 = 1 + ri * na + ai, i1 = 1 + ri * na + aj, o0 = 1 + (ri + 1) * na + ai, o1 = 1 + (ri + 1) * na + aj;
                    t[k++] = i0; t[k++] = i1; t[k++] = o1;
                    t[k++] = i0; t[k++] = o1; t[k++] = o0;
                }
            var m = new Mesh { name = "Kaiju Halo " + type };
            if (v.Length > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = v;
            m.colors32 = c;
            m.triangles = t;
            m.bounds = new Bounds(Vector3.zero, new Vector3(2, 0.2f, 2));
            m.UploadMeshData(true);
            return m;
        }

        void Halo(World w)
        {
            if (_haloGo == null) return;
            if (!w.Match.Hatched || w.Kaiju.State == KaijuState.Egg)
            {
                HaloDrawn = false;
                if (_haloGo.activeSelf) _haloGo.SetActive(false);
                return;
            }
            KaijuType type = KaijuTypes.Type(w);
            int ti = Mathf.Clamp((int)type, 0, _halo.Length - 1);
            if (ti != _haloType) { _haloType = ti; _haloFilter.sharedMesh = _halo[ti]; }
            float h = (float)GroundHeight.At(w.Map, w.Kaiju.X, w.Kaiju.Z);
            float rad = HaloRadius * KaijuTypeLook.Scale(type);
            _haloGo.transform.localPosition = SimSpace.ToWorld(w.Kaiju.X, h + 0.03, w.Kaiju.Z);
            _haloGo.transform.localScale = new Vector3(rad, 1f, rad);
            _haloGo.transform.localRotation = Quaternion.Euler(0, Time.time * 9f % 360f, 0);
            if (!_haloGo.activeSelf) _haloGo.SetActive(true);
            HaloDrawn = true;
        }

        void LateUpdate()
        {
            World w = _driver != null ? _driver.World : null;
            if (w == null || w.ActorCount == 0) { Hide(); HaloDrawn = false; if (_haloGo != null && _haloGo.activeSelf) _haloGo.SetActive(false); return; }
            Halo(w);
            KaijuTypeState s = w.KaijuKind;
            double r, x, z;
            float fade;
            if (KaijuTypes.CloudActive(w, out x, out z, out r))
                fade = Mathf.Clamp01(s.CloudIn / FadeTicks);
            else if (s.PuffIn > 0 && s.Type == KaijuType.Mire && s.Level >= 1)
            {
                // the puff telegraph: the cloud swells in from a third of its radius
                float p = 1f - s.PuffIn / Mathf.Max(1f, w.Config.Depth.KaijuTypes.ShroudPuffTicks);
                r = w.Config.Depth.KaijuTypes.ShroudRadius[s.Level - 1] * (0.33 + 0.67 * p);
                x = w.Kaiju.X;
                z = w.Kaiju.Z;
                fade = p;
            }
            else { Hide(); return; }

            int me = LocalSeat.Actor;
            bool dense = me >= 0 && me < w.ActorCount && KaijuTypes.Affected(w, me);
            float h = (float)GroundHeight.At(w.Map, x, z);
            Vector3 pos = SimSpace.ToWorld(x, h + 0.04, z);   // O-13: every sim-to-Unity position goes through SimSpace
            float rf = (float)r;
            _discGo.transform.localPosition = pos;
            _discGo.transform.localScale = new Vector3(rf, 1f, rf);
            _bandGo.transform.localPosition = pos;
            _bandGo.transform.localScale = new Vector3(rf, 1f, rf);
            _bandGo.transform.localRotation = Quaternion.Euler(0, Time.time * 6f % 360f, 0);
            if (!_discGo.activeSelf) _discGo.SetActive(true);
            if (!_bandGo.activeSelf) _bandGo.SetActive(true);
            float a = Mathf.Round(fade * 32f) / 32f;   // rewrite colours only on a visible step
            if (a != _lastAlpha || dense != _lastDense)
            {
                _lastAlpha = a;
                _lastDense = dense;
                Tint(_disc, _discBase, _discWork, a * (dense ? DenseDisc : HazeDisc));
                Tint(_band, _bandBase, _bandWork, a * (dense ? DenseBand : HazeBand));
            }
            Radius = rf;
            Drawn = true;
        }

        static void Tint(Mesh m, Color32[] src, Color32[] dst, float alpha)
        {
            for (int i = 0; i < src.Length; i++)
            {
                Color32 c = src[i];
                c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * alpha), 0, 255);
                dst[i] = c;
            }
            m.colors32 = dst;
        }

        void Hide()
        {
            Drawn = false;
            Radius = 0;
            if (_discGo != null && _discGo.activeSelf) _discGo.SetActive(false);
            if (_bandGo != null && _bandGo.activeSelf) _bandGo.SetActive(false);
            _lastAlpha = -1f;
        }
    }
}

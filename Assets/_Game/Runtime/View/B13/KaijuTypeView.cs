// CM b13 item K: the SHROUD cloud in the world (view only; reads KaijuTypes, writes nothing to the sim).
using CriticalMass.Runtime.Game;
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
        }

        void OnDestroy()
        {
            Release(_discGo); Release(_bandGo); Release(_disc); Release(_band); Release(_mat);
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

        void LateUpdate()
        {
            World w = _driver != null ? _driver.World : null;
            if (w == null || w.ActorCount == 0) { Hide(); return; }
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

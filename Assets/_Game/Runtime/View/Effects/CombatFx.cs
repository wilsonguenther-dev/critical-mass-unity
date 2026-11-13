using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>What ActorViews applies to one actor's model this frame (all view-only).</summary>
    public struct ActorPoseFx
    {
        /// <summary>Dead, but the body is still shown (Death clip, then the pop).</summary>
        public bool Corpse;
        public double CorpseX, CorpseZ;
        /// <summary>Sim seconds since the death (Death clip time).</summary>
        public double DeathT;
        /// <summary>Sim seconds since the last hit (Hit clip time), large when none.</summary>
        public double HitT;
        /// <summary>The 2-3 frame white flash.</summary>
        public bool Flash;
        /// <summary>Animation hold (fake hit-stop): one-shot clip times are shifted back by HoldOffset; loops do not advance while Holding.</summary>
        public bool Holding;
        public double HoldOffset;
        /// <summary>Knock-back lean (world rotation applied on top of the facing) and nudge (world offset).</summary>
        public Quaternion Lean;
        public Vector3 Nudge;
        /// <summary>Uniform scale (the corpse pop: 1 -> 1.2 -> 0).</summary>
        public float Scale;
    }

    /// <summary>The cm.blood setting (PlayerPrefs int, default 1 = on). The settings page writes it; effects re-read it each second.</summary>
    public static class BloodSetting
    {
        public const string Key = "cm.blood";
        public static bool Enabled => PlayerPrefs.GetInt(Key, 1) != 0;
    }

    /// <summary>
    /// Combat feel, view only: hit bursts, blood, deaths, swing trails, Kaiju telegraph / slam / roar / berserk, pickups,
    /// debris, the local player's damage vignette, the animation hold and flash ActorViews applies, and the sounds.
    /// Subscribes to SimDriverBehaviour.Ticked and decodes each tick's events once (CombatFeed); never writes the sim.
    /// All effect timing is sim time ((Tick + Alpha) / SimHz), so frames after the same ticks look the same.
    /// Three dynamic meshes (ground, air, solid) = three draw calls for every effect together.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class CombatFx : MonoBehaviour, IFxSink
    {
        public const int GroundVerts = 40000, AirVerts = 30000, SolidVerts = 6400;

        private SimDriverBehaviour _driver;
        private CombatFeed _feed;
        private FxScene _scene;
        private CombatAudio _audio;
        private FxMeshBuilder _ground, _air, _solid;
        private GameObject _groundGo, _airGo, _solidGo;
        private Material _groundMat, _airMat, _solidMat;
        private readonly Dictionary<Material, Material> _flash = new Dictionary<Material, Material>();
        private int _frame;
        private double _vignetteAt = double.NegativeInfinity, _vignetteAmount;
        private RawImage _vignette;
        private Canvas _vignetteCanvas;
        private double _builtAt = double.NaN;

        /// <summary>Feed and scene (tests, capture tool).</summary>
        public CombatFeed Feed => _feed;
        public FxScene Scene => _scene;
        public CombatAudio Audio => _audio;

        /// <summary>Animation hold for heavy hits, sim seconds (about 60 ms wall at the default speed).</summary>
        public double HoldSeconds => _scene != null ? _scene.S(0.06) : 0.12;
        /// <summary>White flash length, sim seconds (about 2.5 frames at 60 fps).</summary>
        public double FlashSeconds => _scene != null ? _scene.S(0.045) : 0.09;

        private void Awake()
        {
            _driver = GetComponent<SimDriverBehaviour>();
        }

        private void Start()
        {
            if (_driver == null || _driver.World == null) { enabled = false; return; }
            World w = _driver.World;
            GameConfig cfg = _driver.Config;
            _feed = new CombatFeed(World.ActorCapacity(cfg));
            _scene = new FxScene(cfg, w.Map) { Blood = BloodSetting.Enabled };
            _audio = new CombatAudio(transform, cfg);
            _ground = new FxMeshBuilder("Fx Ground", GroundVerts, GroundVerts * 3);
            _air = new FxMeshBuilder("Fx Air", AirVerts, AirVerts * 3);
            _solid = new FxMeshBuilder("Fx Solid", SolidVerts, SolidVerts * 2);
            Shader sh = Resources.Load<Shader>("CMFxUnlit");
            if (sh == null) sh = Shader.Find("CM/FxUnlit");
            _groundMat = Mat(sh, "Fx Ground", 0, 0, -2, 2990);
            _airMat = Mat(sh, "Fx Air", 0, 0, 0, 3100);
            _solidMat = Mat(sh, "Fx Solid", 2, 1, 0, 2460);
            _groundGo = Layer("Fx Ground", _ground, _groundMat);
            _airGo = Layer("Fx Air", _air, _airMat);
            _solidGo = Layer("Fx Solid", _solid, _solidMat);
            BuildVignette();
            _driver.Ticked -= OnTicked;
            _driver.Ticked += OnTicked;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void OnDestroy()
        {
            if (_driver != null) _driver.Ticked -= OnTicked;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            foreach (Material m in _flash.Values) if (m != null) Destroy(m);
            _audio?.Dispose();
        }

        private void OnDisable()
        {
            if (_groundGo != null) { _groundGo.SetActive(false); _airGo.SetActive(false); _solidGo.SetActive(false); }
            if (_vignetteCanvas != null) _vignetteCanvas.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (_groundGo != null) { _groundGo.SetActive(true); _airGo.SetActive(true); _solidGo.SetActive(true); }
            if (_vignetteCanvas != null) _vignetteCanvas.gameObject.SetActive(true);
        }

        private static Material Mat(Shader sh, string name, int cull, int zwrite, int offset, int queue)
        {
            var m = new Material(sh) { name = name, renderQueue = queue };
            m.SetFloat("_Cull", cull);
            m.SetFloat("_ZWrite", zwrite);
            m.SetFloat("_OffsetUnits", offset);
            return m;
        }

        private GameObject Layer(string n, FxMeshBuilder b, Material m)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>One sim tick happened: decode it and spawn (sinks: scene, audio, this for the vignette).</summary>
        private void OnTicked(World w)
        {
            if (!enabled) return;
            _feed.OnTick(w, _scene, _audio, this);
        }

        public void OnFx(ref FxEvent e, World w)
        {
            if ((e.Kind == FxKind.Hit || e.Kind == FxKind.Death || e.Kind == FxKind.Block) && e.A == 0)
            {
                _vignetteAt = e.Tick / w.Config.SimHz;
                _vignetteAmount = e.Kind == FxKind.Block ? 0.25 : Mathf.Clamp((float)(e.Value / 40.0), 0.3f, 0.95f);
            }
        }

        private double SimNow()
        {
            World w = _driver.World;
            return (w.Tick + _driver.Alpha) / _driver.Config.SimHz;
        }

        private void LateUpdate()
        {
            if (_scene == null) return;
            if (++_frame % 60 == 0) _scene.Blood = BloodSetting.Enabled;
            double now = SimNow();
            _audio.Update(now, _driver.World);
            UpdateVignette(now);
        }

        private void OnBeginCamera(ScriptableRenderContext ctx, UnityEngine.Camera cam)
        {
            if (_scene == null || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            Rebuild(cam.transform.right, cam.transform.up);
        }

        /// <summary>Advance and rebuild the effect meshes for a camera basis (also called by tests and the capture tool).</summary>
        public void Rebuild(Vector3 camRight, Vector3 camUp)
        {
            double now = SimNow();
            _scene.Advance(now);
            _scene.Build(now, _driver.World, camRight, camUp, _ground, _air, _solid);
            _ground.Upload();
            _air.Upload();
            _solid.Upload();
            _builtAt = now;
        }

        /// <summary>Vertices written in the last rebuild (budget report).</summary>
        public int LastVerts => _ground == null ? 0 : _ground.VertCount + _air.VertCount + _solid.VertCount;
        public int LastDropped => _ground == null ? 0 : _ground.Dropped + _air.Dropped + _solid.Dropped;

        // ---- pose effects for ActorViews ----

        /// <summary>The view-only pose adjustments for actor i at the current sim time; false when there is nothing to apply.</summary>
        public bool Pose(int i, out ActorPoseFx fx)
        {
            fx = default;
            fx.Lean = Quaternion.identity;
            fx.Scale = 1f;
            fx.HitT = 1e9;
            if (_feed == null || i < 0 || i >= _feed.LastHitTick.Length) return false;
            World w = _driver.World;
            double hz = _driver.Config.SimHz;
            double now = SimNow();
            bool alive = i < w.ActorCount && w.Actors[i].Alive;
            if (!alive)
            {
                double dt = now - _feed.DeathTick[i] / hz;
                double total = _scene.CorpseSeconds + _scene.PopSeconds;
                if (dt < 0 || dt >= total) return false;
                fx.Corpse = true;
                fx.DeathT = dt;
                fx.CorpseX = _feed.DeathX[i];
                fx.CorpseZ = _feed.DeathZ[i];
                fx.Flash = dt < FlashSeconds;
                if (dt > _scene.CorpseSeconds)
                {
                    float p = (float)((dt - _scene.CorpseSeconds) / _scene.PopSeconds);
                    fx.Scale = p < 0.3f ? 1f + 0.2f * (p / 0.3f) : 1.2f * (1f - (p - 0.3f) / 0.7f);
                }
                return true;
            }
            double hitT = now - _feed.LastHitTick[i] / hz;
            double holdT = now - _feed.HoldTick[i] / hz;
            bool any = false;
            if (hitT >= 0 && hitT < 2.0)
            {
                fx.HitT = hitT;
                fx.Flash = hitT < FlashSeconds;
                double up = _scene.S(0.035), down = _scene.S(0.22);
                double env = hitT < up ? hitT / up : System.Math.Max(0, 1 - (hitT - up) / down);
                if (env > 0)
                {
                    float k = (float)(env * (0.55 + 0.45 * System.Math.Min(1, _feed.LastHitDmg[i] / 30)));
                    Vector3 d = SimSpace.ToWorld(_feed.HitDirX[i], 0, _feed.HitDirZ[i]);
                    Vector3 axis = Vector3.Cross(Vector3.up, d);
                    if (axis.sqrMagnitude > 1e-6f) fx.Lean = Quaternion.AngleAxis(13f * k, axis.normalized);
                    fx.Nudge = d * (0.11f * k);
                }
                any = true;
            }
            if (holdT >= 0 && holdT < 2.0)
            {
                fx.Holding = holdT < HoldSeconds;
                fx.HoldOffset = System.Math.Min(holdT, HoldSeconds);
                any = true;
            }
            return any;
        }

        /// <summary>The Attack clip time for actor i, held during a heavy hit that happened after the swing began.</summary>
        public double AttackTime(int i, double animAction, in ActorPoseFx fx)
        {
            if (_feed == null) return animAction;
            double holdT = SimNow() - _feed.HoldTick[i] / _driver.Config.SimHz;
            return holdT <= animAction + 1e-6 ? animAction - fx.HoldOffset : animAction;
        }

        /// <summary>A flat white copy of a CM/Toon material (outline kept), cached per source.</summary>
        public Material FlashMaterial(Material source)
        {
            if (source == null) return null;
            if (_flash.TryGetValue(source, out Material m)) return m;
            m = new Material(source) { name = source.name + " (Flash)" };
            Color white = new Color(1f, 0.98f, 0.94f, 1f);
            foreach (string p in FlashColours) if (m.HasProperty(p)) m.SetColor(p, white);
            if (m.HasProperty("_ColorMode")) m.SetFloat("_ColorMode", 0);
            if (m.HasProperty("_MidMultiplier")) m.SetFloat("_MidMultiplier", 1);
            if (m.HasProperty("_ShadowMultiplier")) m.SetFloat("_ShadowMultiplier", 0.86f);
            if (m.HasProperty("_ShadowTintAmount")) m.SetFloat("_ShadowTintAmount", 0);
            if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0);
            _flash[source] = m;
            return m;
        }

        private static readonly string[] FlashColours =
        {
            "_BaseColor", "_TeamColor", "_TeamCloth", "_TeamGlow", "_TeamDark", "_DefaultCloth", "_DefaultGlow", "_DefaultDark",
            "_TeamMarker", "_TeamMarkerDark",
        };

        // ---- local player damage vignette ----

        private void BuildVignette()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Fx Vignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u * 0.8f + v * v * 1.1f);
                // Two hard steps (toon): an outer dark-red band and a thinner inner band.
                float a = r > 1.02f ? 0.85f : r > 0.86f ? 0.55f : r > 0.76f ? 0.22f : 0f;
                Color32 c = r > 1.02f ? FxScene.BloodDark : FxScene.BloodMid;
                c.a = (byte)(a * 255);
                px[y * n + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var go = new GameObject("Fx Vignette");
            go.transform.SetParent(transform, false);
            _vignetteCanvas = go.AddComponent<Canvas>();
            _vignetteCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _vignetteCanvas.worldCamera = UnityEngine.Camera.main;
            _vignetteCanvas.planeDistance = 0.6f;
            _vignetteCanvas.sortingOrder = -50;
            var img = new GameObject("Vignette");
            img.transform.SetParent(go.transform, false);
            _vignette = img.AddComponent<RawImage>();
            _vignette.texture = tex;
            _vignette.raycastTarget = false;
            var rt = _vignette.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _vignette.color = new Color(1, 1, 1, 0);
            _vignette.enabled = false;
        }

        private void UpdateVignette(double now)
        {
            if (_vignette == null) return;
            if (_vignetteCanvas.worldCamera == null) _vignetteCanvas.worldCamera = UnityEngine.Camera.main;
            double t = now - _vignetteAt;
            double life = _scene.S(0.32);
            float a = t >= 0 && t < life ? (float)(_vignetteAmount * (1 - t / life) * (1 - t / life)) : 0f;
            bool on = a > 0.01f;
            if (_vignette.enabled != on) _vignette.enabled = on;
            if (on) _vignette.color = new Color(1, 1, 1, a);
        }

        /// <summary>Current vignette alpha (tests and captures).</summary>
        public float VignetteAlpha => _vignette != null && _vignette.enabled ? _vignette.color.a : 0f;
    }
}

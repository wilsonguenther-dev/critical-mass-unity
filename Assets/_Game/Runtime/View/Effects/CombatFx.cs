using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Dressing;
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
    /// <para>v2: per-tier pose holds, a per-tick world pass (FxScene.OnTickState), explosions and splashes injected through
    /// the static Effects API, screen shake (only with cm.shake on: the camera is offset for its own render and restored
    /// right after, so no camera script ever sees it), the foliage mask from the dressed world, the _CMFxImpulses shader
    /// hook, weapon blood tints, and the F6 effects gallery in development builds.</para>
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class CombatFx : MonoBehaviour, IFxSink
    {
        // v1: 40000 / 30000 / 6400. v2 sizes cover FxScene.WorstCaseVerts per layer (a test holds them).
        public const int GroundVerts = 58000, AirVerts = 56000, SolidVerts = 10800;
        /// <summary>Shader global: up to 8 world impulses (xyz, strength 0..1) for foliage sway; count in _CMFxImpulseCount.</summary>
        public static readonly int ImpulsesId = Shader.PropertyToID("_CMFxImpulses"), ImpulseCountId = Shader.PropertyToID("_CMFxImpulseCount");
        /// <summary>Largest camera shake offset in world units, at trauma 1 (the offset is trauma squared times this).</summary>
        public const float ShakeMax = 0.32f;

        private SimDriverBehaviour _driver;
        private CombatFeed _feed;
        private FxScene _scene;
        private CombatAudio _audio;
        private FxMeshBuilder _ground, _air, _solid;
        private GameObject _groundGo, _airGo, _solidGo;
        private Material _groundMat, _airMat, _solidMat;
        private readonly Dictionary<Material, Material> _flash = new Dictionary<Material, Material>();
        /// <summary>Reduce flashing (M11, DG-106): the hit flash is a 30% tint toward white, cached per source like the flash.</summary>
        private readonly Dictionary<Material, Material> _tint = new Dictionary<Material, Material>();
        private int _frame;
        private double _vignetteAt = double.NegativeInfinity, _vignetteAmount;
        private RawImage _vignette;
        private Canvas _vignetteCanvas;
        private double _builtAt = double.NaN;
        private readonly Vector4[] _impulses = new Vector4[8];
        private bool _shakeOn;
        private Transform _shaken;
        private Vector3 _shakeRestore;
        private int _foliageTries;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private FxGallery _gallery;
        /// <summary>The gallery (development builds and the editor).</summary>
        public FxGallery Gallery => _gallery;
#endif

        /// <summary>Feed and scene (tests, capture tool).</summary>
        public CombatFeed Feed => _feed;
        public FxScene Scene => _scene;
        public CombatAudio Audio => _audio;

        /// <summary>Animation hold for heavy hits, sim seconds (about 65 ms wall at the default speed).</summary>
        public double HoldSeconds => HoldFor(1);

        /// <summary>
        /// Pose hold per hit tier, sim seconds: light 30 ms wall (victim only), heavy 65 ms, critical or finishing 110 ms
        /// (victim and attacker). Pose time only; the sim never pauses.
        /// </summary>
        public double HoldFor(byte tier)
        {
            double wall = tier == 0 ? 0.03 : tier == 1 ? 0.065 : 0.11;
            return _scene != null ? _scene.S(wall) : wall * 2;
        }
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
            PrewarmFlash();
            _shakeOn = Effects.ShakeEnabled;
            Effects.Active = this;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _gallery = new FxGallery(_scene, _audio);
#endif
            // M11: the camera shake (scaled by the setting, off with reduce motion) and its start-of-frame reset.
            if (GetComponent<CriticalMass.Runtime.Camera.CameraShake>() == null) gameObject.AddComponent<CriticalMass.Runtime.Camera.CameraShake>();
            if (GetComponent<CriticalMass.Runtime.Camera.CameraShakeReset>() == null) gameObject.AddComponent<CriticalMass.Runtime.Camera.CameraShakeReset>();
            _driver.Ticked -= OnTicked;
            _driver.Ticked += OnTicked;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        private void OnDestroy()
        {
            if (_driver != null) _driver.Ticked -= OnTicked;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            RestoreShake();
            if (Effects.Active == this) Effects.Active = null;
            foreach (Material m in _flash.Values) if (m != null) Destroy(m);
            foreach (Material m in _tint.Values) if (m != null) Destroy(m);
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
            _scene.OnTickState(w);
        }

        /// <summary>Spawn an effect outside the sim's events (Effects.Explode / Splash), stamped with the current tick.</summary>
        internal bool Inject(FxKind kind, double x, double z, double value, int b, int a)
        {
            if (_scene == null || !enabled) return false;
            World w = _driver.World;
            var e = new FxEvent { Kind = kind, Tick = w.Tick, A = a, B = b, X = x, Z = z, Value = value, Team = TeamId.Azure, Cls = ClassId.Warrior };
            if (a >= 0 && a < w.ActorCount) { e.Team = w.Actors[a].Team; e.Cls = w.Actors[a].Cls; e.Local = a == 0; }
            _scene.OnFx(ref e, w);
            _audio?.OnFx(ref e, w);
            return true;
        }

        internal bool InjectLand(int actor)
        {
            World w = _driver != null ? _driver.World : null;
            if (w == null || actor < 0 || actor >= w.ActorCount) return false;
            return Inject(FxKind.Land, w.X[actor], w.Z[actor], w.Actors[actor].Cls == ClassId.Warrior ? 1 : 0, -1, actor);
        }

        internal bool PlayGallery()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_gallery == null) return false;
            _gallery.Begin(SimNow(), _driver.World);
            return true;
#else
            return false;
#endif
        }

        /// <summary>Weapon blood tint 0..1 for actor i: 1 when it lands a hit, fading over 2.5 s wall; 0 with blood off.
        /// Hook for the character view (ActorViews owner): lerp the weapon material's colour toward BloodMid by this.</summary>
        public float WeaponBlood(int i)
        {
            if (_feed == null || !_scene.Blood || i < 0 || i >= _feed.LastDealtTick.Length) return 0f;
            double age = SimNow() - _feed.LastDealtTick[i] / _driver.Config.SimHz;
            return age < 0 ? 0f : Mathf.Clamp01(1f - (float)(age / _scene.S(2.5)));
        }

        /// <summary>The Kaiju's fist blood tint 0..1 (same fade as WeaponBlood).</summary>
        public float KaijuFistBlood
        {
            get
            {
                if (_feed == null || !_scene.Blood) return 0f;
                double age = SimNow() - _feed.KaijuDealtTick / _driver.Config.SimHz;
                return age < 0 ? 0f : Mathf.Clamp01(1f - (float)(age / _scene.S(2.5)));
            }
        }

        public void OnFx(ref FxEvent e, World w)
        {
            if ((e.Kind == FxKind.Hit || e.Kind == FxKind.Death || e.Kind == FxKind.Block) && e.A == 0)
            {
                _vignetteAt = e.Tick / w.Config.SimHz;
                _vignetteAmount = e.Kind == FxKind.Block ? 0.25 : Mathf.Clamp((float)(e.Value / 40.0), 0.3f, 0.95f);
            }
            ShakeFor(ref e, w);
        }

        /// <summary>
        /// Camera shake impulses (M11, gated by FeelGate: amount x the shake setting, nothing with reduce motion or shake
        /// off, which is the default). Only what lands on or near the local player shakes the view.
        /// </summary>
        private static void ShakeFor(ref FxEvent e, World w)
        {
            if (w.ActorCount == 0 || FeelGate.ShakeScale <= 0f) return;
            float amount = ShakeAmount(e.Kind, e.A == 0, e.Value, DistanceToLocal(w, e.X, e.Z), w.Config.Kaiju.SlamRadius);
            if (amount > 0f) CriticalMass.Runtime.Camera.CameraShake.Impulse(amount);
        }

        static double DistanceToLocal(World w, double x, double z)
        {
            double dx = w.X[0] - x, dz = w.Z[0] - z;
            return System.Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Shake trauma 0..1 an effect adds (pure; tests). 0 for anything that is not big or not near you.</summary>
        public static float ShakeAmount(FxKind kind, bool local, double value, double distance, double slamRadius)
        {
            switch (kind)
            {
                case FxKind.KaijuSlam: return distance <= slamRadius * 1.6 ? 0.6f : 0f;
                case FxKind.Hit: return local && value >= 25 ? 0.25f : 0f;
                case FxKind.Death: return local ? 0.4f : 0f;
                case FxKind.Hatch: return distance <= 20 ? 0.45f : 0f;
                case FxKind.BerserkLock: return 0.35f;
                case FxKind.KaijuKO: return distance <= 12 ? 0.3f : 0f;
                case FxKind.WallBreak: return distance <= 6 ? 0.2f : 0f;
                default: return 0f;
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
            double now = SimNow();
            bool galleryRunning = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f6Key.wasPressedThisFrame) PlayGallery();
            galleryRunning = _gallery != null && _gallery.Update(now, _driver.World);
#endif
            if (++_frame % 60 == 0)
            {
                if (!galleryRunning) _scene.Blood = BloodSetting.Enabled;
                _shakeOn = Effects.ShakeEnabled;
                if (!_scene.HasFoliage && _foliageTries < 20) FindFoliage();
            }
            _audio.Update(now, _driver.World);
            UpdateVignette(now);
            int n = _scene.Impulses(now, _impulses);
            Shader.SetGlobalVectorArray(ImpulsesId, _impulses);
            Shader.SetGlobalFloat(ImpulseCountId, n);
        }

        private void OnBeginCamera(ScriptableRenderContext ctx, UnityEngine.Camera cam)
        {
            if (_scene == null || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            Rebuild(cam.transform.right, cam.transform.up);
            ApplyShake(cam);
        }

        private void OnEndCamera(ScriptableRenderContext ctx, UnityEngine.Camera cam)
        {
            if (_shaken != null && cam != null && cam.transform == _shaken) RestoreShake();
        }

        /// <summary>
        /// Trauma-based shake (Eiserloh): offset = trauma^2 x ShakeMax along the camera's right and up, from a sum of sines
        /// of sim time (a replay shakes the same way). Only the gameplay camera, only with cm.shake on, only for the render.
        /// </summary>
        private void ApplyShake(UnityEngine.Camera cam)
        {
            Effects.ShakeOffset = Vector3.zero;
            if (!_shakeOn || cam.cameraType != CameraType.Game || cam != UnityEngine.Camera.main) return;
            World w = _driver.World;
            if (w.ActorCount == 0) return;
            double now = SimNow();
            float tr = _scene.Trauma(now, w.X[0], w.Z[0]);
            if (tr <= 0.001f) return;
            // The ACCESS tab's CAMERA SHAKE amount (0..100) scales it; the bare on flag with no amount set is full strength.
            float amount = CriticalMass.Runtime.Menu.SettingsExtras.Shake01;
            float s = tr * tr * ShakeMax * (amount > 0f ? amount : 1f);
            float tt = (float)now;
            float ox = Mathf.Sin(tt * 47.3f) * 0.6f + Mathf.Sin(tt * 91.7f + 1.3f) * 0.4f;
            float oy = Mathf.Sin(tt * 53.9f + 0.7f) * 0.6f + Mathf.Sin(tt * 83.1f + 2.1f) * 0.4f;
            Transform t = cam.transform;
            Vector3 off = (t.right * ox + t.up * oy) * s;
            RestoreShake();
            _shaken = t;
            _shakeRestore = t.position;
            t.position += off;
            Effects.ShakeOffset = off;
        }

        private void RestoreShake()
        {
            if (_shaken == null) return;
            _shaken.position = _shakeRestore;
            _shaken = null;
        }

        /// <summary>Read grass and bushes from the dressed world's plan (once it exists), for the rustle reaction.</summary>
        private void FindFoliage()
        {
            _foliageTries++;
            WorldView view = GetComponent<WorldView>();
            if (view == null) view = FindAnyObjectByType<WorldView>();
            if (view == null || view.Plan == null) return;
            _scene.SetFoliage(FxScene.FoliageMask(view.Plan, _driver.World.Map));
        }

        /// <summary>Advance and rebuild the effect meshes for a camera basis (also called by tests and the capture tool).</summary>
        public void Rebuild(Vector3 camRight, Vector3 camUp)
        {
            double now = SimNow();
            _scene.Alpha = _driver.Alpha;
            _scene.Pace = _driver.TimeScale > 0 ? _driver.TimeScale : 0;
            _scene.BlobShadows = CriticalMass.Runtime.Game.QualityTable.Current.BlobShadows;
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
                double hold = HoldFor(_feed.HoldTier[i]);
                fx.Holding = holdT < hold;
                fx.HoldOffset = System.Math.Min(holdT, hold);
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
            if (FeelGate.ReduceFlashing) return TintMaterial(source);
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

        /// <summary>Reduce flashing: a copy of a CM/Toon material with its colours 30% toward white (no white frame).</summary>
        public Material TintMaterial(Material source)
        {
            if (source == null) return null;
            if (_tint.TryGetValue(source, out Material m)) return m;
            m = new Material(source) { name = source.name + " (Tint)" };
            Color white = new Color(1f, 0.98f, 0.94f, 1f);
            foreach (string p in FlashColours)
                if (m.HasProperty(p)) m.SetColor(p, Color.Lerp(m.GetColor(p), white, FeelGate.ReducedFlashTint));
            _tint[source] = m;
            return m;
        }

        /// <summary>
        /// Create every character flash material up front (one per class model and team), so a hit never creates a
        /// material or any other GPU resource. A missing art material (setup not run) only skips the prewarm.
        /// </summary>
        private void PrewarmFlash()
        {
            foreach (ClassId cls in new[] { ClassId.Warrior, ClassId.Miner, ClassId.Farmer })
            {
                string key = ArtMapping.ClassModel(cls);
                ModelSpec spec = key != null ? ArtMapping.Spec(key) : null;
                if (spec == null) continue;
                try
                {
                    FlashMaterial(ArtMaterials.ForTeam(spec, TeamId.Azure));
                    FlashMaterial(ArtMaterials.ForTeam(spec, TeamId.Crimson));
                    TintMaterial(ArtMaterials.ForTeam(spec, TeamId.Azure));
                    TintMaterial(ArtMaterials.ForTeam(spec, TeamId.Crimson));
                }
                catch (System.InvalidOperationException)
                {
                    return;
                }
            }
        }

        /// <summary>Flash materials created (bounded: class models x 2 teams; all made in Start).</summary>
        public int FlashMaterialCount => _flash.Count + _tint.Count;

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
            float a;
            if (FeelGate.ReduceFlashing)
            {
                // M11: a steady low edge while you are being hit (no bright pulse on every hit), easing out at the end.
                double hold = _scene.S(0.8);
                a = t >= 0 && t < hold ? (float)(System.Math.Min(0.3, _vignetteAmount * 0.4) * System.Math.Min(1.0, (hold - t) / _scene.S(0.3))) : 0f;
            }
            else
            {
                double life = _scene.S(0.32);
                a = t >= 0 && t < life ? (float)(_vignetteAmount * (1 - t / life) * (1 - t / life)) : 0f;
            }
            bool on = a > 0.01f;
            if (_vignette.enabled != on) _vignette.enabled = on;
            if (on) _vignette.color = new Color(1, 1, 1, a);
        }

        /// <summary>Current vignette alpha (tests and captures).</summary>
        public float VignetteAlpha => _vignette != null && _vignette.enabled ? _vignette.color.a : 0f;
    }
}

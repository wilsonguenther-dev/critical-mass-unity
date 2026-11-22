using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Anim;
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
        // ---- px lane C (2026-11-22)
        /// <summary>The last hit involved the local player (attacker or victim): own-hit emphasis.</summary>
        public bool Own;
        /// <summary>The flash's peak strength (1 = full white; the safe cream tint share under reduce flashing) and whether it
        /// is an own-hit flash; FlashNew is true on the first frame of a new flash (the ledger's HitFlash report).</summary>
        public float FlashStrength;
        public bool FlashOwn, FlashNew;
        /// <summary>True on the first frame of a new hit-stop on this body, with its length in wall ms (HitStop report), and
        /// whether it is an own-hit hold.</summary>
        public bool HoldNew, HoldOwn;
        public float HoldMs;
    }

    /// <summary>Legacy blood on/off (cm.blood): on = any GORE level but Off. GoreGate is the one switch; CombatFx re-reads it live.</summary>
    public static class BloodSetting
    {
        public const string Key = "cm.blood";
        public static bool Enabled => GoreGate.Current != GoreLevel.Off;
    }

    /// <summary>
    /// Combat feel, view only: hit bursts, blood, deaths, swing trails, Kaiju telegraph / slam / roar / berserk, pickups,
    /// debris, the local player's damage vignette, the animation hold and flash ActorViews applies, and the sounds.
    /// Subscribes to SimDriverBehaviour.Ticked and decodes each tick's events once (CombatFeed); never writes the sim.
    /// All effect timing is sim time ((Tick + Alpha) / SimHz), so frames after the same ticks look the same.
    /// Three dynamic meshes (ground, air, solid) = three draw calls for every effect together.
    /// <para>v2: per-tier pose holds, a per-tick world pass (FxScene.OnTickState), explosions and splashes injected through
    /// the static Effects API, screen shake (only with cm.shake on: the camera is offset for its own render and restored
    /// right after, so no camera script ever sees it), the foliage mask from the dressed world, weapon blood tints, and
    /// the F6 effects gallery in development builds.</para>
    /// <para>World reactions (2026-11-12): owns the WorldForceBus (contract C1) and forwards every decoded and injected
    /// event to it (PR #30's _CMFxImpulses hook is retired: Reactions.ForceField publishes the bus slots); plans a death
    /// launch per knock-out (Anim.DeathLaunch: the corpse flies along the hit, tumbles, bounces and slides, and its landing
    /// is a scheduled force); rebuilds the effect meshes once per frame instead of once per camera.</para>
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class CombatFx : MonoBehaviour, IFxSink
    {
        // v1: 40000 / 30000 / 6400. v2 sizes cover FxScene.WorstCaseVerts per layer (a test holds them).
        public const int GroundVerts = 58000, AirVerts = 56000, SolidVerts = 10800;
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
        private WorldForceBus _bus;
        private LaunchPlan[] _launch;
        private int _rebuiltFrame = -1;
        private bool _shakeOn;
        private Transform _shaken;
        private Vector3 _shakeRestore;
        private int _foliageTries;
        // px lane C: per-victim flash rate limit (WCAG 2.3.1) and the first-frame flags for the ledger
        private double[] _flashStart;
        private bool[] _flashOwn, _flashPending;
        private int[] _flashHitSeen, _holdSeen;
        private readonly Dictionary<Material, Material> _tintOther = new Dictionary<Material, Material>();
        private MeshRenderer _airRenderer;
        /// <summary>The renderer that draws the air fx layer (bursts, trails): the ledger's source for ImpactBurst / WhiffCue.</summary>
        public Object FxSource => _airRenderer != null ? (Object)_airRenderer : this;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private FxGallery _gallery;
        /// <summary>The gallery (development builds and the editor).</summary>
        public FxGallery Gallery => _gallery;
#endif

        /// <summary>Feed and scene (tests, capture tool).</summary>
        public CombatFeed Feed => _feed;
        public FxScene Scene => _scene;
        public CombatAudio Audio => _audio;
        /// <summary>The world-force bus this CombatFx feeds (also WorldForceBus.Active while it runs).</summary>
        public WorldForceBus Bus => _bus;

        /// <summary>Animation hold for heavy hits, sim seconds (about 65 ms wall at the default speed).</summary>
        public double HoldSeconds => HoldFor(1);

        /// <summary>
        /// Pose hold per hit tier, sim seconds: light 30 ms wall (victim only), heavy 65 ms, critical or finishing 110 ms
        /// (victim and attacker). Pose time only; the sim never pauses.
        /// </summary>
        public double HoldFor(byte tier)
        {
            // feat/hit-feel-1: 45 / 60 / 70 ms on both fighters (was 30 victim-only / 65 / 110), none with reduce motion.
            double wall = Anim.HitFeel.HoldWallSeconds(tier, FeelGate.ReduceMotion);
            return _scene != null ? _scene.S(wall) : wall * 2;
        }
        /// <summary>White flash length, sim seconds: about 2.5 frames at 60 fps; at GORE Off the whole-silhouette flash is the
        /// main hit read, so it holds about 0.1 s (warm white: never a team hue, never the heal glow's green).</summary>
        public double FlashSeconds => FlashSecondsFor(false);

        /// <summary>px lane C: flash length (sim s) for an own-hit or other flash (HitFeelTuning: 120 / 100 ms wall).</summary>
        public double FlashSecondsFor(bool own)
        {
            double wall = HitFeelTuning.FlashSeconds(own, CriticalMass.Runtime.Camera.GroundView.PlayActive, FeelGate.ReduceFlashing);
            return _scene != null ? _scene.S(wall) : wall * 2;
        }

        private void Awake()
        {
            _driver = GetComponent<SimDriverBehaviour>();
        }

        // perf/start-hitch-1: timed for the scene-start report (LoadTimer).
        private void Start() { long t0 = CriticalMass.Runtime.Game.LoadTimer.Now(); StartBody(); CriticalMass.Runtime.Game.LoadTimer.Part("CombatFx.Start", t0); }

        private void StartBody()
        {
            if (_driver == null || _driver.World == null) { enabled = false; return; }
            World w = _driver.World;
            GameConfig cfg = _driver.Config;
            _feed = new CombatFeed(World.ActorCapacity(cfg));
            _scene = new FxScene(cfg, w.Map) { Gore = GoreGate.Current };
            _audio = new CombatAudio(transform, cfg);
            _bus = new WorldForceBus(cfg.SimHz) { SimPerWall = _scene.S(1.0) };
            WorldForceBus.Active = _bus;
            _bus.FootfallWorld = _driver != null ? _driver.World : null;
            CriticalMass.Runtime.View.Anim.Footfalls.Register(_bus);   // DG-93: measured footfalls feed the one force bus
            _launch = new LaunchPlan[World.ActorCapacity(cfg)];
            int cap = World.ActorCapacity(cfg);
            _flashStart = new double[cap];
            _flashOwn = new bool[cap];
            _flashPending = new bool[cap];
            _flashHitSeen = new int[cap];
            _holdSeen = new int[cap];
            for (int k = 0; k < cap; k++) { _flashStart[k] = double.NegativeInfinity; _flashHitSeen[k] = _holdSeen[k] = int.MinValue; }
            CriticalMass.Runtime.Camera.FightState.Reset();
            _scene.KaijuFootfall = OnKaijuFootfall;
            _ground = new FxMeshBuilder("Fx Ground", GroundVerts, GroundVerts * 3);
            _air = new FxMeshBuilder("Fx Air", AirVerts, AirVerts * 3);
            _solid = new FxMeshBuilder("Fx Solid", SolidVerts, SolidVerts * 2);
            _ground.ViewCulled = _air.ViewCulled = _solid.ViewCulled = true;   // follow-camera-2: low-view culling (gore after GoreGate too)
            _air.NearFade = _solid.NearFade = true;   // follow-camera-3: bursts, smoke and debris never fill the lens
            Shader sh = Resources.Load<Shader>("CMFxUnlit");
            if (sh == null) sh = Shader.Find("CM/FxUnlit");
            _groundMat = Mat(sh, "Fx Ground", 0, 0, -2, GroundFxQueue);
            _airMat = Mat(sh, "Fx Air", 0, 0, 0, 3100);
            _solidMat = Mat(sh, "Fx Solid", 2, 1, 0, 2460);
            _groundGo = Layer("Fx Ground", _ground, _groundMat);
            _airGo = Layer("Fx Air", _air, _airMat);
            _airRenderer = _airGo.GetComponent<MeshRenderer>();
            _solidGo = Layer("Fx Solid", _solid, _solidMat);
            BuildVignette();
            PrewarmFlash();
            if (GoreShots.Requested && GetComponent<GoreShots>() == null) gameObject.AddComponent<GoreShots>();   // fix/gore-proof-1
            if (KaijuBridgeShots.Requested && GetComponent<KaijuBridgeShots>() == null) gameObject.AddComponent<KaijuBridgeShots>();   // integration batch 10
            _shakeOn = false;   // integration batch 3: Camera.CameraShake (M11, FeelGate-scaled) is the one camera shake
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
            if (WorldForceBus.Active == _bus) WorldForceBus.Active = null;
            if (_bus != null) CriticalMass.Runtime.View.Anim.Footfalls.Unregister(_bus);
            foreach (Material m in _flash.Values) if (m != null) Destroy(m);
            foreach (Material m in _tintOther.Values) if (m != null) Destroy(m);
            foreach (Material m in _tint.Values) if (m != null) Destroy(m);
            if (_vignetteRed != null) Destroy(_vignetteRed);
            if (_vignetteNavy != null) Destroy(_vignetteNavy);
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

        /// <summary>b15 view polish (item 3): the ground fx layer (wind-up wedges, telegraphs, blob shadows) draws after the
        /// world decals (CM/WorldDecal, Transparent-50 = 2950) and before the air fx; it writes no depth and is depth-tested,
        /// so characters (opaque) stand over it.</summary>
        public const int GroundFxQueue = 2990;

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
            if (w.Tick < _seenTick || w.EventHead < _seenHead) ResetForNewWorld();
            _seenTick = w.Tick;
            _seenHead = w.EventHead;
            _feed.OnTick(w, _scene, _audio, this);
            CriticalMass.Runtime.Camera.FightState.Evaluate(w, Time.unscaledTimeAsDouble);   // px lane C
            _scene.OnTickState(w);
            _bus.FootfallWorld = w;
            _bus.OnTick(w);
        }

        private int _seenTick = int.MinValue, _seenHead;

        /// <summary>World resets seen (the sim clock went back: a rematch on the same scene, or a capture's restage).</summary>
        public int WorldResets { get; private set; }

        /// <summary>
        /// fix/gore-proof-1: a world reset drops what the old world left in the view. Before this, CombatFeed only rewound its
        /// event cursor: every fighter's last hit, hold and death tick survived the reset, so fighter i, hit at tick T in the
        /// old world, flashed white (and leaned, and held its pose) again when the new world's clock passed T, whatever it
        /// was doing then, for example swinging as an attacker. Capture tools restage with World.ResetWorld every shot, so the
        /// ghost flash landed on whoever had that index in the next staging. The live effects had the same fault (FxScene.Clear).
        /// </summary>
        private void ResetForNewWorld()
        {
            WorldResets++;
            _scene.Clear();
            FillTicks(_feed.LastHitTick);
            FillTicks(_feed.HoldTick);
            FillTicks(_feed.DeathTick);
            FillTicks(_feed.LastDealtTick);
            System.Array.Clear(_feed.HoldTier, 0, _feed.HoldTier.Length);
            _feed.KaijuDealtTick = int.MinValue / 2;
            _feed.ResetPx();
            if (_flashStart != null)
                for (int k = 0; k < _flashStart.Length; k++) { _flashStart[k] = double.NegativeInfinity; _flashPending[k] = false; _flashHitSeen[k] = _holdSeen[k] = int.MinValue; }
            CriticalMass.Runtime.View.WallDamage.StructureHits.Clear();
            CriticalMass.Runtime.Camera.FightState.Reset();
            if (_launch != null) System.Array.Clear(_launch, 0, _launch.Length);
            _vignetteAt = double.NegativeInfinity;
        }

        private static void FillTicks(int[] a)
        {
            for (int i = 0; i < a.Length; i++) a[i] = int.MinValue / 2;
        }

        private void OnKaijuFootfall(double x, double z)
        {
            if (_bus != null && _driver != null && _driver.World != null) _bus.PushKaijuStep(_driver.World, x, z);
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
            _bus?.OnFx(ref e, w);
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
            if (_feed == null || !GoreGate.Allows(GorePiece.WeaponBloodTint, _scene.Gore) || i < 0 || i >= _feed.LastDealtTick.Length) return 0f;
            double age = SimNow() - _feed.LastDealtTick[i] / _driver.Config.SimHz;
            return age < 0 ? 0f : Mathf.Clamp01(1f - (float)(age / _scene.S(2.5)));
        }

        /// <summary>The Kaiju's fist blood tint 0..1 (same fade as WeaponBlood).</summary>
        public float KaijuFistBlood
        {
            get
            {
                if (_feed == null || !GoreGate.Allows(GorePiece.KaijuFistBloodTint, _scene.Gore)) return 0f;
                double age = SimNow() - _feed.KaijuDealtTick / _driver.Config.SimHz;
                return age < 0 ? 0f : Mathf.Clamp01(1f - (float)(age / _scene.S(2.5)));
            }
        }

        public void OnFx(ref FxEvent e, World w)
        {
            _bus?.OnFx(ref e, w);
            if (e.Kind == FxKind.Death && _launch != null && e.A >= 0 && e.A < _launch.Length) PlanLaunch(ref e, w);
            if ((e.Kind == FxKind.Hit || e.Kind == FxKind.Death || e.Kind == FxKind.Block) && e.A == LocalSeat.Actor)
            {
                _vignetteAt = e.Tick / w.Config.SimHz;
                _vignetteAmount = e.Kind == FxKind.Block ? 0.25 : Mathf.Clamp((float)(e.Value / 40.0), 0.3f, 0.95f);
            }
            ShakeFor(ref e, w);
            PxImpact(ref e, w);
        }

        /// <summary>
        /// px lane C (2026-11-22): the camera and controller answer a hit by or on the local player (kick, rumble, fight
        /// state), the ledger hears the burst and the whiff the moment FxScene spawned them, and structure hits reach the
        /// wall views through StructureHits (no HP polling once the event flows).
        /// </summary>
        private void PxImpact(ref FxEvent e, World w)
        {
            int me = LocalSeat.Actor;
            switch (e.Kind)
            {
                case FxKind.Hit:
                case FxKind.Death:
                {
                    bool onMe = e.A == me, byMe = e.B == me && e.A != me;
                    if (!onMe && !byMe) return;
                    // lead's call 12: when the sim's crit rule is on, the CriticalHit event (FxKind.Critical below) drives the
                    // critical look; only with the rule off does the top tier that is not a kill stand in for it.
                    if (e.Kind == FxKind.Hit && e.Tier >= 2 && !CritRuleOn(w)) { OnCritical(e.A, e.B, 0); return; }
                    CriticalMass.Runtime.Camera.FightState.MarkCombat(Time.unscaledTimeAsDouble);
                    HitFeelTuning.KickKind k = e.Kind == FxKind.Death ? (onMe ? HitFeelTuning.KickKind.MyDeath : HitFeelTuning.KickKind.MyKill)
                        : onMe ? HitFeelTuning.KickKind.OnMe : HitFeelTuning.KickKind.ByMe;
                    CriticalMass.Runtime.Camera.CameraShake.Kick(k, SimSpace.ToWorld(e.DirX, 0, e.DirZ), e.A);
                    CriticalMass.Runtime.View.Reactions.CameraResponse.HitRumble(e.Tier >= 1 || e.Kind == FxKind.Death);
                    if (_scene != null && _scene.LastBurstSize > 0f) PlayerFeedback.Report(FeedbackChannel.ImpactBurst, e.A, _scene.LastBurstSize, FxSource);
                    return;
                }
                case FxKind.Critical:
                    if (e.A == me || (e.B == me && e.A != me)) OnCritical(e.A, e.B, (int)e.Value);
                    return;
                case FxKind.Contact:
                    if (e.A == me && e.Value <= 0) PlayerFeedback.Report(FeedbackChannel.WhiffCue, me, 1f, FxSource);
                    return;
                case FxKind.StructureHit:
                    CriticalMass.Runtime.View.WallDamage.StructureHits.Push(ref e);
                    if (e.A == me) CriticalMass.Runtime.Camera.FightState.MarkCombat(Time.unscaledTimeAsDouble);
                    return;
            }
        }

        /// <summary>
        /// CRITICAL HIT (lead's call 12, 2026-11-22), view only: kind 0 = the top-tier look (Tier 2, not a kill), 1 counter,
        /// 2 punish, 3 back hit (lane S1's SimEventType.CriticalHit Value). For hits by or on the local player: the two-stage
        /// burst, radial lines and the gold follow-through (FxScene.SpawnCritical), 140 ms hit-stop on both fighters
        /// (CombatFeed.CritHoldTick), the full-body Hit_B and a bigger recoil (CombatFeed.CritTick, read by ActorAnimator and
        /// ActorViews), a 1.6x camera kick plus a 2 deg FOV punch (zero under REDUCE MOTION) and the heavy rumble. Ledger:
        /// ImpactBurst (stage-2 size), CameraKick (in CameraShake.Kick), HitStop (ActorViews, 140 ms). Call it the tick of
        /// the Hit (after FxScene spawned it).
        /// </summary>
        /// <summary>True when the sim's earned-crit rule is live (Full combat feel and CritEnabled): the CriticalHit event exists.</summary>
        private static bool CritRuleOn(World w) => w != null && CombatFeel.On(w) && CombatFeel.Cfg(w).CritEnabled;

        public void OnCritical(int victim, int attacker, int kind)
        {
            World w = _driver != null ? _driver.World : null;
            if (w == null || _feed == null || victim < 0 || victim >= w.ActorCount) return;
            int me = LocalSeat.Actor;
            bool onMe = victim == me, byMe = attacker == me && victim != me;
            if (!onMe && !byMe) return;
            int tick = _feed.LastHitTick[victim];
            _feed.CritTick[victim] = tick;
            _feed.CritKind[victim] = (byte)Mathf.Clamp(kind, 0, 255);
            _feed.CritHoldTick[victim] = tick;
            if (attacker >= 0 && attacker < w.ActorCount) _feed.CritHoldTick[attacker] = tick;
            CriticalMass.Runtime.Camera.FightState.MarkCombat(Time.unscaledTimeAsDouble);
            CriticalMass.Runtime.Camera.CameraShake.Kick(onMe ? HitFeelTuning.KickKind.CritOnMe : HitFeelTuning.KickKind.CritByMe,
                SimSpace.ToWorld(_feed.HitDirX[victim], 0, _feed.HitDirZ[victim]), victim);
            CriticalMass.Runtime.Camera.CameraShake.FovPunch(HitFeelTuning.CritFovPunchDeg);
            CriticalMass.Runtime.View.Reactions.CameraResponse.HitRumble(true);
            if (_scene != null) PlayerFeedback.Report(FeedbackChannel.ImpactBurst, victim, _scene.SpawnCritical(attacker), FxSource);
        }

        /// <summary>
        /// Camera shake impulses (M11, gated by FeelGate: amount x the shake setting, nothing with reduce motion or shake
        /// off, which is the default). Only what lands on or near the local player shakes the view.
        /// </summary>
        private static void ShakeFor(ref FxEvent e, World w)
        {
            // feat/hit-feel-2: the hit nudge for the local player is on by default at a low strength (CameraShake.Nudge: the
            // shake setting once the player has set it, off with reduce motion).
            // px lane C: the hit nudge (trauma, x0.3 in third person: 0.004 u) is replaced by the directional hit kick
            // (CameraShake.Kick, PxImpact), which the shake slider does not scale.
            if (w.ActorCount == 0 || FeelGate.ShakeScale <= 0f) return;
            float amount = ShakeAmount(e.Kind, e.A == LocalSeat.Actor, e.Value, DistanceToLocal(w, e.X, e.Z), w.Config.Kaiju.SlamRadius);
            if (amount > 0f) CriticalMass.Runtime.Camera.CameraShake.Impulse(amount);
        }

        static double DistanceToLocal(World w, double x, double z)
        {
            double dx = w.X[LocalSeat.Actor] - x, dz = w.Z[LocalSeat.Actor] - z;
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

        /// <summary>The knock-out's flight (Anim.DeathLaunch) and its landing force, scheduled for the tick it lands.</summary>
        private void PlanLaunch(ref FxEvent e, World w)
        {
            float motion = Reactions.ReactionBudget.MotionScale;
            double dx = e.DirX, dz = e.DirZ;
            // b16 (audit missing list: death fling): a Death without a direction (the sim gives one only with knockback physics)
            // flings away from the killing blow's source: the killer's body, the Kaiju, else no fling (a fall, a blast in place).
            if (dx * dx + dz * dz < 1e-12) KillerDir(w, e.B, e.X, e.Z, out dx, out dz);
            LaunchPlan p = DeathLaunch.Plan(w.Map, e.X, e.Z, dx, dz, CombatFeed.TierOf(e.Value, false), e.B, motion);
            _launch[e.A] = p;
            if (p.Distance <= 0f || _bus == null || p.Falls) return;
            double lx = e.X + p.DirX * p.Distance, lz = e.Z + p.DirZ * p.Distance;
            WorldForce land = _bus.Make(w, ForceSource.Land, ForceShape.Landing, lx, lz, 0, 0, 0f, ExplosionKind.Tnt);
            land.Actor = e.A;
            land.Other = e.B;
            land.Team = e.Team;
            int ticks = (int)System.Math.Ceiling(p.Flight * _scene.S(1.0) * w.Config.SimHz);
            _bus.Schedule(land, e.Tick + System.Math.Max(1, ticks));
        }

        /// <summary>b16: the unit direction from the killing blow's source to the victim at (x, z), or zero (pure; tests).</summary>
        public static void KillerDir(World w, int source, double x, double z, out double dx, out double dz)
        {
            dx = 0; dz = 0;
            double sx, sz;
            if (source >= 0 && source < w.ActorCount) { sx = w.X[source]; sz = w.Z[source]; }
            else if ((source == CriticalMass.Sim.Core.Damage.KaijuSource || source == CriticalMass.Sim.Core.Damage.KaijuSlamSource) && w.Kaiju != null) { sx = w.Kaiju.X; sz = w.Kaiju.Z; }
            else return;
            double ex = x - sx, ez = z - sz, l = System.Math.Sqrt(ex * ex + ez * ez);
            if (l < 1e-4) return;
            dx = ex / l;
            dz = ez / l;
        }

        /// <summary>Actor i's planned death launch (tests, captures).</summary>
        public LaunchPlan Launch(int i) => _launch != null && i >= 0 && i < _launch.Length ? _launch[i] : default;

        private double SimNow()
        {
            World w = _driver.World;
            return (w.Tick + _driver.Alpha) / _driver.Config.SimHz;
        }

        private void LateUpdate() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.Vfx, cost); }

        void LateUpdateCost()
        {
            if (_scene == null) return;
            double now = SimNow();
            bool galleryRunning = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f6Key.wasPressedThisFrame) PlayGallery();
            galleryRunning = _gallery != null && _gallery.Update(now, _driver.World);
#endif
            // GORE applies live (a few frames after the menu changes it); the gallery pins its own levels while it runs.
            if (++_frame % 10 == 0 && !galleryRunning) ApplyGore(GoreGate.Current);
            if (_frame % 60 == 0)
            {
                _shakeOn = false;   // integration batch 3: Camera.CameraShake (M11, FeelGate-scaled) is the one camera shake
                if (!_scene.HasFoliage && _foliageTries < 20) FindFoliage();
            }
            _audio.Update(now, _driver.World);
            UpdateVignette(now);
        }

        /// <summary>
        /// Put a GORE level in force now: the scene gates every spawn and draw by it; dropping below Full wipes the blood
        /// already stamped into the ground paint (scorch and footprints there go too) and swaps the red damage vignette.
        /// </summary>
        public void ApplyGore(GoreLevel level)
        {
            if (_scene == null || level == _scene.Gore) return;
            bool leftFull = _scene.Gore == GoreLevel.Full && level != GoreLevel.Full;
            _scene.Gore = level;
            if (leftFull)
            {
                var paint = FindAnyObjectByType<CriticalMass.Runtime.View.Reactions.GroundPaint>();
                if (paint != null) paint.ClearAll();
            }
            if (_vignette != null) _vignette.texture = GoreGate.Allows(GorePiece.RedDamageVignette, level) ? _vignetteRed : _vignetteNavy;
        }

        private void OnBeginCamera(ScriptableRenderContext ctx, UnityEngine.Camera cam)
        {
            if (_scene == null || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            // Once per frame (audit finding 6): the gameplay camera always rebuilds (billboards face it); any other
            // camera only when nothing has built this frame yet.
            if (cam == UnityEngine.Camera.main || _rebuiltFrame != Time.frameCount)
            {
                Rebuild(cam.transform.right, cam.transform.up);
                _rebuiltFrame = Time.frameCount;
            }
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
            float tr = _scene.Trauma(now, w.X[LocalSeat.Actor], w.Z[LocalSeat.Actor]);
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

        /// <summary>smoke-look-1 capture: one view-only explosion ageSeconds old at sim (x, z), every other effect cleared.</summary>
        public void PreviewExplosion(double ageSeconds, double x, double z, float R, ExplosionKind kind)
        {
            if (_scene == null || _driver == null || _driver.World == null) return;
            _scene.PreviewExplosion(SimNow(), ageSeconds, x, z, R, kind, _driver.World);
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
                if (_launch != null && i < _launch.Length && _launch[i].Distance > 0f)
                {
                    // Death launch: along the hit, a tumble about cross(up, dir), one bounce and a slide (wall seconds).
                    LaunchPlan lp = _launch[i];
                    DeathLaunch.Evaluate(lp, (float)(dt / _scene.S(1.0)), out float along, out float lift, out float tumble);
                    fx.CorpseX += lp.DirX * along;
                    fx.CorpseZ += lp.DirZ * along;
                    fx.Nudge = Vector3.up * lift;
                    Vector3 d = SimSpace.ToWorld(lp.DirX, 0, lp.DirZ);
                    Vector3 axis = Vector3.Cross(Vector3.up, d);
                    if (tumble != 0f && axis.sqrMagnitude > 1e-6f) fx.Lean = Quaternion.AngleAxis(tumble, axis.normalized);
                }
                bool ownDeath = i == LocalSeat.Actor || (i < _feed.LastHitBy.Length && _feed.LastHitBy[i] == LocalSeat.Actor);
                fx.Flash = dt < FlashSecondsFor(ownDeath);
                fx.FlashOwn = ownDeath;
                fx.FlashStrength = HitFeelTuning.FlashStrength(FeelGate.ReduceFlashing, ownDeath);
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
                // px lane C: one flash per hit, at most one start per FlashMinGapSeconds per victim (under 3 a second), own
                // hits longer and (under reduce flashing) a stronger safe tint; FlashNew marks its first visible frame.
                int by = _feed.LastHitBy[i];
                bool own = i == LocalSeat.Actor || by == LocalSeat.Actor;
                fx.Own = own;
                int ht = _feed.LastHitTick[i];
                if (ht != _flashHitSeen[i])
                {
                    _flashHitSeen[i] = ht;
                    double t0 = ht / hz;
                    // fix round (defect 6): a shield-blocked hit never flashes the victim (the block look shows instead)
                    // fix round 6: a flash start "in the future" (a new world whose clock restarted without a reset seen) never
                    // blocks this one: the 3-a-second limit only counts starts at or before now
                    double sinceFlash = (t0 - _flashStart[i]) / _scene.S(1.0);
                    if (_feed.BlockTick[i] != ht && (sinceFlash < 0 || HitFeelTuning.FlashAllowed(sinceFlash)))
                    {
                        _flashStart[i] = t0;
                        _flashOwn[i] = own;
                        _flashPending[i] = true;
                    }
                }
                double ft = now - _flashStart[i];
                bool fOwn = _flashOwn[i];
                fx.Flash = ft >= 0 && ft < FlashSecondsFor(fOwn);
                fx.FlashOwn = fOwn;
                fx.FlashStrength = HitFeelTuning.FlashStrength(FeelGate.ReduceFlashing, fOwn);
                if (fx.Flash && _flashPending[i]) { fx.FlashNew = true; _flashPending[i] = false; }
                else if (!fx.Flash && ft >= 0) _flashPending[i] = false;
                double up = _scene.S(0.035), down = _scene.S(0.22);
                double env = hitT < up ? hitT / up : System.Math.Max(0, 1 - (hitT - up) / down);
                if (env > 0)
                {
                    // feat/hit-feel-2: lean 8 to 24 degrees and shift 0.08 to 0.22 u by damage (was 7 to 13 and 0.06 to 0.11:
                    // a few pixels at gameplay distance), plus a stagger step along the blow for mid and big hits.
                    double dmg = _feed.LastHitDmg[i];
                    Vector3 d = SimSpace.ToWorld(_feed.HitDirX[i], 0, _feed.HitDirZ[i]);
                    Vector3 axis = Vector3.Cross(Vector3.up, d);
                    if (axis.sqrMagnitude > 1e-6f) fx.Lean = Quaternion.AngleAxis((float)(Anim.HitFeel.FlinchLeanDeg(dmg) * env), axis.normalized);
                    fx.Nudge = d * (float)(Anim.HitFeel.FlinchShift(dmg) * env + Anim.HitFeel.StaggerStep(hitT / _scene.S(1.0), dmg));
                }
                any = true;
            }
            if (holdT >= 0 && holdT < 2.0)
            {
                bool crit = _feed.CritHoldTick[i] == _feed.HoldTick[i];   // a critical hit: 140 ms on both fighters
                double holdWall = crit ? HitFeelTuning.HoldCritWallSeconds(FeelGate.ReduceMotion) : Anim.HitFeel.HoldWallSeconds(_feed.HoldTier[i], FeelGate.ReduceMotion);
                double hold = crit ? (_scene != null ? _scene.S(holdWall) : holdWall * 2) : HoldFor(_feed.HoldTier[i]);
                fx.Holding = holdT < hold;
                fx.HoldOffset = System.Math.Min(holdT, hold);
                // px lane C: the ledger's HitStop for the victim and the attacker (own when either is the local player)
                int hk = _feed.HoldTick[i];
                if (hk != _holdSeen[i] && fx.Holding)
                {
                    _holdSeen[i] = hk;
                    fx.HoldNew = hold > 0;
                    fx.HoldMs = (float)(holdWall * 1000);
                    int me = LocalSeat.Actor;
                    fx.HoldOwn = OwnVictimAt(i, hk) || (_feed.LastHitBy[i] == me && _feed.LastHitTick[i] == hk);
                }
                any = true;
            }
            return any;
        }

        /// <summary>Round 3 (ledger): the sim time actor i's current flash started (NegativeInfinity none) and whether it is
        /// an own-hit flash; the tick of its current hold, its wall ms and whether it is own (ActorViews reports each once,
        /// keyed on these, instead of on the one-frame FlashNew / HoldNew latches).</summary>
        public double FlashStartOf(int i) => _flashStart != null && i >= 0 && i < _flashStart.Length ? _flashStart[i] : double.NegativeInfinity;

        public bool HoldOf(int i, out int tick, out float ms, out bool own)
        {
            tick = int.MinValue; ms = 0f; own = false;
            if (_feed == null || i < 0 || i >= _feed.HoldTick.Length) return false;
            tick = _feed.HoldTick[i];
            bool crit = _feed.CritHoldTick[i] == tick;
            double wall = crit ? HitFeelTuning.HoldCritWallSeconds(FeelGate.ReduceMotion) : Anim.HitFeel.HoldWallSeconds(_feed.HoldTier[i], FeelGate.ReduceMotion);
            ms = (float)(wall * 1000);
            int me = LocalSeat.Actor;
            own = OwnVictimAt(i, tick) || (_feed.LastHitBy[i] == me && _feed.LastHitTick[i] == tick);
            return ms > 0f;
        }

        /// <summary>px lane C: the hold of `attacker` at tick hk is an own hit: it is the local player, or it hit the local player then.</summary>
        private bool OwnVictimAt(int attacker, int hk)
        {
            int me = LocalSeat.Actor;
            if (attacker == me) return true;
            return me < _feed.LastHitBy.Length && _feed.LastHitBy[me] == attacker && _feed.LastHitTick[me] == hk;
        }

        /// <summary>
        /// px lane C: actor i's last sim knock for the drawn lead (ActorViews): age (WALL s), the knock (u), the sim spot it
        /// started from and its unit direction (sim). False when there is none younger than 1 s.
        /// </summary>
        public bool Knock(int i, out double ageWall, out double knock, out double x0, out double z0, out double dirX, out double dirZ)
        {
            ageWall = knock = x0 = z0 = dirX = dirZ = 0;
            if (_feed == null || i < 0 || i >= _feed.KnockTick.Length || _scene == null) return false;
            double hz = _driver.Config.SimHz;
            double age = (SimNow() - _feed.KnockTick[i] / hz) / _scene.S(1.0);
            if (!(age >= 0 && age < 1.0)) return false;
            ageWall = age;
            knock = _feed.KnockU[i];
            x0 = _feed.KnockX[i];
            z0 = _feed.KnockZ[i];
            dirX = _feed.KnockDirX[i];
            dirZ = _feed.KnockDirZ[i];
            return knock > 0;
        }

        /// <summary>The Attack clip time for actor i, held during a heavy hit that happened after the swing began.</summary>
        public double AttackTime(int i, double animAction, in ActorPoseFx fx)
        {
            if (_feed == null) return animAction;
            double holdT = SimNow() - _feed.HoldTick[i] / _driver.Config.SimHz;
            return holdT <= animAction + 1e-6 ? animAction - fx.HoldOffset : animAction;
        }

        /// <summary>A flat white copy of a CM/Toon material (outline kept), cached per source.</summary>
        public Material FlashMaterial(Material source) => FlashMaterial(source, true);

        /// <summary>px lane C: the flash material for an own-hit or other flash: full white, or under reduce flashing the safe
        /// cream fill + rim at HitFeelTuning.FlashSafeOwn / FlashSafeOther (was a 30% tint).</summary>
        public Material FlashMaterial(Material source, bool own)
        {
            if (source == null) return null;
            if (FeelGate.ReduceFlashing) return TintMaterial(source, own);
            if (_flash.TryGetValue(source, out Material m)) return m;
            m = new Material(source) { name = source.name + " (Flash)" };
            Color white = new Color(1f, 0.98f, 0.94f, 1f);
            foreach (string p in FlashColours) if (m.HasProperty(p)) m.SetColor(p, white);
            if (m.HasProperty("_ColorMode")) m.SetFloat("_ColorMode", 0);
            if (m.HasProperty("_MidMultiplier")) m.SetFloat("_MidMultiplier", 1);
            if (m.HasProperty("_ShadowMultiplier")) m.SetFloat("_ShadowMultiplier", 0.86f);
            if (m.HasProperty("_ShadowTintAmount")) m.SetFloat("_ShadowTintAmount", 0);
            if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0);
            // fix round 5: the unshaded flash term (CMToon _HitFlash) so the whole body reads white on the impact frame
            if (m.HasProperty(HitFlashId)) m.SetVector(HitFlashId, HitFeelTuning.HitFlashVector(false));
            _flash[source] = m;
            return m;
        }

        /// <summary>Reduce flashing: a copy of a CM/Toon material with its colours 30% toward white (no white frame).</summary>
        public Material TintMaterial(Material source) => TintMaterial(source, true);

        public Material TintMaterial(Material source, bool own)
        {
            if (source == null) return null;
            Dictionary<Material, Material> cache = own ? _tint : _tintOther;
            if (cache.TryGetValue(source, out Material m)) return m;
            m = new Material(source) { name = source.name + (own ? " (Tint)" : " (Tint other)") };
            Color cream = HitFeelTuning.FlashCream;
            // fix round 6 (advisory 4): the albedo moves only FlashSafeColourShare of the safe strength toward the cream (the
            // unshaded _HitFlash term carries the pulse), so the reduced flash is clearly gentler than the intense one
            float k = HitFeelTuning.FlashStrength(true, own) * HitFeelTuning.FlashSafeColourShare;
            foreach (string p in FlashColours)
                if (m.HasProperty(p)) m.SetColor(p, Color.Lerp(m.GetColor(p), cream, k));
            // a solid, team-neutral pulse: flatten the shading toward the cream and light the rim in it (no white frame)
            if (m.HasProperty("_ShadowTintAmount")) m.SetFloat("_ShadowTintAmount", m.GetFloat("_ShadowTintAmount") * (1f - k));
            if (m.HasProperty("_ShadowMultiplier")) m.SetFloat("_ShadowMultiplier", Mathf.Lerp(m.GetFloat("_ShadowMultiplier"), 0.9f, k));
            if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", cream);
            if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", Mathf.Max(m.GetFloat("_RimStrength"), 1f));
            if (m.HasProperty(HitFlashId)) m.SetVector(HitFlashId, HitFeelTuning.HitFlashVector(true));
            cache[source] = m;
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
                    TintMaterial(ArtMaterials.ForTeam(spec, TeamId.Azure), false);
                    TintMaterial(ArtMaterials.ForTeam(spec, TeamId.Crimson), false);
                }
                catch (System.InvalidOperationException)
                {
                    return;
                }
            }
        }

        /// <summary>Flash materials created (bounded: class models x 2 teams; all made in Start).</summary>
        public int FlashMaterialCount => _flash.Count + _tint.Count + _tintOther.Count;

        public static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");

        private static readonly string[] FlashColours =
        {
            "_BaseColor", "_TeamColor", "_TeamCloth", "_TeamGlow", "_TeamDark", "_DefaultCloth", "_DefaultGlow", "_DefaultDark",
            "_TeamMarker", "_TeamMarkerDark",
        };

        // ---- local player damage vignette ----

        private Texture2D _vignetteRed, _vignetteNavy;

        private void BuildVignette()
        {
            // Two 128 px textures made once (64 KB each): GORE Full and Reduced, Void Navy for Off (the default).
            // b15 safe defaults: the gore tone is ImpactPalette's violet-brown, never a team-like red (was FxScene.Blood*).
            _vignetteRed = VignetteTexture(ImpactPalette.GoreDark, ImpactPalette.GoreMid, "Fx Vignette");
            _vignetteNavy = VignetteTexture(FxScene.Navy, FxScene.GroundInkDark, "Fx Vignette Navy");
            Texture2D tex = GoreGate.Allows(GorePiece.RedDamageVignette, _scene.Gore) ? _vignetteRed : _vignetteNavy;
            BuildVignetteCanvas(tex);
        }

        private static Texture2D VignetteTexture(Color32 outer, Color32 inner, string name)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = name };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u * 0.8f + v * v * 1.1f);
                // Two hard steps (toon): an outer dark-red band and a thinner inner band.
                float a = r > 1.02f ? 0.85f : r > 0.86f ? 0.55f : r > 0.76f ? 0.22f : 0f;
                Color32 c = r > 1.02f ? outer : inner;
                c.a = (byte)(a * 255);
                px[y * n + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private void BuildVignetteCanvas(Texture2D tex)
        {
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
        /// <summary>fix/gore-proof-1: true while the damage edge wears the navy texture (GORE Reduced or Off).</summary>
        public bool VignetteNavy => _vignette != null && _vignetteNavy != null && _vignette.texture == _vignetteNavy;
        public float VignetteAlpha => _vignette != null && _vignette.enabled ? _vignette.color.a : 0f;
    }
}

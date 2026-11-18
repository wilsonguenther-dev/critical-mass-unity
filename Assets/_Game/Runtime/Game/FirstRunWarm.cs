using System;
using System.Diagnostics;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// first-run-warm-1 (smoke fact: the FIRST match of every freshly copied build had about 1.0 to 1.2% slow frames and the
    /// render scale dropped to 0.80; the second match about 0.13%). Cause read from the code: URP on Metal builds each
    /// shader variant's pipeline state the first time it is drawn, and nothing shipped warms them: ShaderWarmup only warms
    /// a recorded GraphicsStateCollection from StreamingAssets/ShaderWarmup/, and no recording was ever committed, so it does
    /// nothing; the loading screen's four warm-up frames draw only what the gameplay camera sees at the spawn. So the first
    /// sight of every other island, fighter class, team material, shadow caster and every explosion compiled mid-match.
    /// <para>This warm-up runs inside the loading screen's WarmUp step, behind the opaque screen, in a hard time budget
    /// (BudgetSeconds), one step per frame, each logged as "[CM.Warm] &lt;step&gt; &lt;ms&gt;":</para>
    /// <list type="number">
    /// <item>wide: WideFrames extra renders of the main camera into an off-screen texture, pulled back over the whole map
    /// (far clip, LOD bias and shadow distance raised for that render only, occlusion off), so every island, prop,
    /// fighter, team material and shadow caster is drawn once through the real URP passes and vertex layouts; nothing
    /// of it reaches the screen (the camera pose is put back right after each render);</item>
    /// <item>fx: one still of each explosion kind (CombatFx.PreviewExplosion) at the fireball age and one at the smoke age,
    /// drawn at the spawn, then every effect cleared (the effect meshes share a few materials, so this covers them all);</item>
    /// <item>audio: LoadAudioData for every loaded clip not yet in memory;</item>
    /// <item>glyphs: GlyphWarmup for every Text in the scene.</item>
    /// </list>
    /// <para>Proof counter (with -warmCount or -automation): a GraphicsStateCollection trace from boot (or ShaderWarmup's own
    /// -traceStates trace); at match start its state count is the base, and "[CM.Warm] statesAfterStart=N" every 30 s and on
    /// quit is how many graphics states were first met after the match started (target: near zero).</para>
    /// <para>first-run-warm-2 (the Air: a -traceStates match wrote a 282-byte file with every map empty, and the log had no
    /// "[CM.Warm]" line at all): graphics-state tracing only records where SystemInfo.supportsParallelPSOCreation is true,
    /// and that Mac's Metal player says false, so the recording route is now optional: "[CM.Warm] supportsParallelPSOCreation=X"
    /// is logged once at start, the counter and ShaderWarmup's recording are skipped cleanly when it is false (or the file
    /// is empty), and the warm-up above needs no recording. The automated match (-autoplay) never opens the loading
    /// screen, so the same warm-up now also starts by itself there, once per match, as soon as the world is built and
    /// before the first simulated tick (the sim is held while it runs: HoldsSim). Each run logs
    /// "[CM.Warm] begin origin=..." and "[CM.Warm] end ms=N items=M"; the time cap (BudgetSeconds) logs when it is hit,
    /// and HoldCapSeconds releases the sim whatever happens.</para>
    /// <para>b12-x1 (smoke: worst frame 400 to 500 ms at 0.3 s into match 1, and title idle CPU 37% against 35%). Two
    /// automation-only gaps: (1) the automated match's recording began on the frame the title was dismissed, during the
    /// 0.16 s fade, BEFORE the scene load, so the scene-load frame (the one a player sees under the loading screen) was
    /// counted as the match's worst frame; Running now also covers an automated load from the fade until the new match world
    /// is warmed (LoadPending, capped at PendingCapSeconds), the same rule as the warm-up frames themselves. (2) The
    /// automated warm-up also ran on the title's backdrop world (3 whole-map renders, inside the title's idle-CPU window);
    /// a player never warms the title, so backdrop worlds are skipped now. Every match load also logs one line,
    /// "[CM.Load] first-second worst=..ms at=..s cause=..." (load start to one second after the match's first tick), naming
    /// what ran in the slowest frame (scene-load, world-build, warm-wide/fx/audio/glyphs, first-ticks or frame) and whether
    /// the loading screen covered it. Players and automation alike; never in the editor.</para>
    /// </summary>
    public sealed class FirstRunWarm : MonoBehaviour
    {
        public const float BudgetSeconds = 1.2f;
        public const int WideFrames = 3, FxFrames = 2;
        public const float WideLodBias = 50f, WideShadowDistance = 400f;
        public const string CountFlag = "-warmCount";
        /// <summary>first-run-warm-2: the sim is never held longer than this (wall seconds), whatever a step costs.</summary>
        public const float HoldCapSeconds = 3f;
        /// <summary>b12-x1: an automated load never keeps the recording waiting longer than this (wall seconds).</summary>
        public const float PendingCapSeconds = 20f;
        /// <summary>b12-x1: the [CM.Load] first-second window: this long after the match's first tick (s), at most
        /// ProbeCapSeconds after the load began.</summary>
        public const float ProbeAfterTickSeconds = 1f, ProbeCapSeconds = 20f;
        public const string CauseSceneLoad = "scene-load", CauseWorldBuild = "world-build", CauseFirstTicks = "first-ticks", CauseFrame = "frame";
        static readonly string[] StageCause = { null, "warm-wide", "warm-fx", "warm-audio", "warm-glyphs", null };
        static readonly ExplosionKind[] Kinds = { ExplosionKind.Tnt, ExplosionKind.Mine, ExplosionKind.Barrel, ExplosionKind.WallBreak, ExplosionKind.KaijuSlam };
        static readonly double[] Ages = { 0.15, 1.0 };

        enum Stage { Idle, Wide, Fx, Audio, Glyphs, Done }

        static FirstRunWarm _instance;
        Stage _stage = Stage.Idle;
        int _frame, _items;
        long _stageStart, _allStart;
        string _origin = "loading";
        bool _auto;                 // automated run: begin by itself on each new match world
        World _warmedWorld;         // the match world last warmed (once per match)
        float _nextLook;
        SimDriverBehaviour _driver;
        CombatFx _fx;

        // counter
        // b12-x1: automated load pending (Running covers it) and the first-second probe
        bool _pendingLoad;
        long _pendingSince;
        long _probeLast, _probeT0, _probeTickT0;
        bool _probeSawLoad, _sceneArrived, _wasBuilding;
        string _lastCause;
        float _worstMs, _worstAt, _tickWorstMs, _tickWorstAt;
        string _worstCause, _tickWorstCause;
        bool _worstCovered, _tickWorstCovered;
        SimDriverBehaviour _probeDriver;

        GraphicsStateCollection _trace;
        bool _ownTrace, _counting, _matchStarted;
        int _base;
        float _nextLog;

        /// <summary>True when there is no warm-up in progress (finished, skipped or never begun): the loading screen may close.</summary>
        public static bool Done => _instance == null || _instance._stage == Stage.Idle || _instance._stage == Stage.Done;
        /// <summary>first-run-warm-2: a warm-up is running now. b12-x1: in automation also while a match load is under way
        /// (the fade, the load frame, the world build) until that world's warm-up has run: none of those are match frames.</summary>
        public static bool Running => _instance != null && (_instance.Active || _instance.LoadPending);
        /// <summary>first-run-warm-2: the sim waits while the warm-up runs (the match starts from the same state, later in
        /// wall time), so no simulated frame is shown before it ends; released after HoldCapSeconds in any case. b12-x1: the
        /// warm-up itself only, never a pending load.</summary>
        public static bool HoldsSim => _instance != null && _instance.Active && Ms(_instance._allStart) < HoldCapSeconds * 1000f;
        /// <summary>b12-x1: an automated match load is under way and its world is not warmed yet.</summary>
        public static bool LoadPendingNow => _instance != null && _instance.LoadPending;

        bool Active => _stage != Stage.Idle && _stage != Stage.Done;
        bool LoadPending => _auto && (FlowLoading() || (_pendingLoad && Ms(_pendingSince) < PendingCapSeconds * 1000f));

        /// <summary>The flow is fading out to load a scene (FlowDirector.FadeThenLoad).</summary>
        static bool FlowLoading()
        {
            Flow.FlowDirector d = Flow.FlowDirector.Current;
            return d != null && d.Loading;
        }

        /// <summary>b12-x1 (pure; tests): whether the automated warm-up runs on a world. Backdrop worlds (title, menus) never:
        /// a player never warms them, and their warm-up used to land inside the title's idle-CPU window.</summary>
        public static bool WarmsWorld(bool hasWorld, bool backdrop) => hasWorld && !backdrop;

        /// <summary>b12-x1 (pure; tests): the probe's cause for one frame, in priority order.</summary>
        public static string CauseOf(bool sceneLoaded, string warmStep, bool firstTicks, bool building) =>
            sceneLoaded ? CauseSceneLoad : warmStep ?? (firstTicks ? CauseFirstTicks : building ? CauseWorldBuild : CauseFrame);

        /// <summary>b12-x1 (pure; tests): the one "[CM.Load] first-second" line.</summary>
        public static string FormatFirstSecond(float worstMs, float atS, string cause, bool covered, float tickWorstMs, float tickAtS,
            string tickCause, bool tickCovered, float loadToTickS)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            string s = "[CM.Load] first-second worst=" + worstMs.ToString("0", c) + "ms at=" + atS.ToString("0.00", c) + "s cause=" + (cause ?? CauseFrame) +
                       " covered=" + (covered ? "loading-screen" : "no");
            if (loadToTickS < 0f) return s + "; the match's first tick did not come within " + ProbeCapSeconds.ToString("0", c) + " s";
            return s + "; after the first tick worst=" + tickWorstMs.ToString("0", c) + "ms at=" + tickAtS.ToString("0.00", c) + "s cause=" +
                   (tickCause ?? CauseFrame) + " covered=" + (tickCovered ? "loading-screen" : "no") + "; load start to first tick " +
                   loadToTickS.ToString("0.00", c) + "s";
        }
        /// <summary>first-run-warm-2: graphics-state tracing records anything only where parallel PSO creation is supported
        /// (Unity's BeginTrace docs); false on the Air's Metal player. Never throws.</summary>
        public static bool RecordingSupported
        {
            get { try { return SystemInfo.supportsParallelPSOCreation; } catch (Exception) { return false; } }
        }
        /// <summary>The warm-up's progress 0..1 for the loading bar.</summary>
        public static float Progress => _instance == null ? 1f : _instance.Fraction();
        /// <summary>States first met after the match started (counter on), else -1.</summary>
        public static int StatesAfterStart => _instance != null && _instance._matchStarted && _instance._trace != null ? _instance._trace.totalGraphicsStateCount - _instance._base : -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_instance != null || Application.isBatchMode) return;
            var go = new GameObject("CriticalMass FirstRunWarm");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FirstRunWarm>();
        }

        static long Now() => Stopwatch.GetTimestamp();
        static float Ms(long t0) => (float)((Now() - t0) * 1000.0 / Stopwatch.Frequency);
        static void Log(string s) => Debug.Log("[CM.Warm] " + s);

        /// <summary>Begins the warm-up for the match behind the loading screen (once per match load; no-op without a match).</summary>
        public static void Begin()
        {
            if (_instance == null) Install();
            if (_instance == null || (_instance._stage != Stage.Idle && _instance._stage != Stage.Done)) return;
            _instance.StartWarm("loading");
        }

        void OnEnable()
        {
            string[] args = Environment.GetCommandLineArgs();
            _counting = Array.IndexOf(args, CountFlag) >= 0 || Array.IndexOf(args, "-automation") >= 0;
            _auto = Array.IndexOf(args, "-automation") >= 0;
            try { _auto |= AutoplayArgs.Current.Autoplay; } catch (Exception) { }
            _auto &= !Application.isEditor;   // players only: editor play tests keep their tick timing
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        /// <summary>b12-x1: a scene arrived (this object lives across loads): the frame that ends now carried the load.</summary>
        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            _probeSawLoad = true;
            _sceneArrived = true;
            if (!_auto || Active) return;
            if (!_pendingLoad) _pendingSince = Now();
            _pendingLoad = true;
            _nextLook = 0f;
        }

        void Start()
        {
            Log("supportsParallelPSOCreation=" + RecordingSupported + " (" + SystemInfo.graphicsDeviceType + "; " +
                (RecordingSupported ? "graphics-state recording usable" : "no graphics-state recording here: the off-screen warm-up runs without it") + ")");
            if (!_counting) return;
            if (!RecordingSupported) { Log("counter off: graphics-state tracing records nothing without parallel PSO creation"); return; }
            try
            {
                _trace = ShaderWarmup.ActiveTrace;
                if (_trace == null)
                {
                    _trace = new GraphicsStateCollection();
                    _trace.BeginTrace();
                    _ownTrace = true;
                }
                Application.quitting += LogCount;
                Log("counter on (" + SystemInfo.graphicsDeviceType + ", " + (_ownTrace ? "own trace" : "-traceStates trace") + ")");
            }
            catch (Exception e)
            {
                _trace = null;
                Log("counter unavailable: " + e.GetType().Name + ": " + e.Message);
            }
        }

        void StartWarm(string origin)
        {
            _origin = origin;
            _items = 0;
            _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null || UnityEngine.Camera.main == null)
            {
                Log("skipped: no match or camera yet");
                _stage = Stage.Done;
                return;
            }
            _fx = _driver.GetComponent<CombatFx>() ?? FindAnyObjectByType<CombatFx>();
            _warmedWorld = _driver.World;
            _pendingLoad = false;   // b12-x1: the warm-up's own stage covers Running from here
            _allStart = Now();
            Log("begin origin=" + origin + " parallelPSO=" + RecordingSupported + " cap=" + (BudgetSeconds * 1000f).ToString("0") + "ms");
            _matchStarted = false;
            Enter(Stage.Wide);
        }

        void Enter(Stage s)
        {
            _stage = s;
            _frame = 0;
            _stageStart = Now();
        }

        float Fraction()
        {
            switch (_stage)
            {
                case Stage.Wide: return 0.5f * _frame / WideFrames;
                case Stage.Fx: return 0.5f + 0.4f * _frame / (Kinds.Length * FxFrames);
                case Stage.Audio: return 0.92f;
                case Stage.Glyphs: return 0.96f;
                default: return 1f;
            }
        }

        bool OverBudget() => Ms(_allStart) > BudgetSeconds * 1000f;

        void Update()
        {
            Probe();
            if (_auto && !_pendingLoad && !Active && FlowLoading())
            {
                _pendingLoad = true;   // b12-x1: the fade before an automated load: the recording waits
                _pendingSince = Now();
                _nextLook = 0f;
            }
            Stage before = _stage;
            if (_stage != Stage.Idle && _stage != Stage.Done) Step();
            else if (_auto) AutoBegin();
            _lastCause = StageCause[(int)before];   // the warm step that ran in this frame (null: none)
            UpdateCounter();
        }

        // ---- b12-x1: the [CM.Load] first-second probe (one Stopwatch read per frame; the line is built once per load) ----

        void Probe()
        {
            long now = Now(), last = _probeLast;
            _probeLast = now;
            if (Application.isEditor || last == 0) { _probeSawLoad = false; return; }
            bool sawLoad = _probeSawLoad;
            _probeSawLoad = false;
            if (_probeT0 == 0)
            {
                if (!sawLoad && !FlowLoading()) return;
                _probeT0 = last;
                _probeTickT0 = 0;
                _sceneArrived = sawLoad;
                _worstMs = _tickWorstMs = -1f;
                _worstCause = _tickWorstCause = null;
                _probeDriver = null;
            }
            if (_probeDriver == null) _probeDriver = FindAnyObjectByType<SimDriverBehaviour>();
            SimDriverBehaviour d = _probeDriver;
            bool backdrop = d != null && d.Request != null && d.Request.Backdrop;
            if (_sceneArrived && backdrop && !FlowLoading())
            {
                _probeT0 = 0;   // the load went to the title or a menu: no match, no line
                return;
            }
            bool building = View.Dressing.WorldView.Building;
            bool firstTicks = _probeTickT0 == 0 && _sceneArrived && d != null && d.World != null && !backdrop && d.World.Tick > 0;
            string cause = CauseOf(sawLoad, _lastCause, firstTicks, building || _wasBuilding);
            _wasBuilding = building;
            if (firstTicks) _probeTickT0 = last;
            bool covered = CriticalMass.Runtime.Menus.LoadingScreen.IsOpen;
            float ms = (float)((now - last) * 1000.0 / Stopwatch.Frequency);
            float at = (float)((last - _probeT0) / (double)Stopwatch.Frequency);
            if (ms > _worstMs) { _worstMs = ms; _worstAt = at; _worstCause = cause; _worstCovered = covered; }
            if (_probeTickT0 != 0 && ms > _tickWorstMs)
            {
                _tickWorstMs = ms;
                _tickWorstAt = (float)((last - _probeTickT0) / (double)Stopwatch.Frequency);
                _tickWorstCause = cause;
                _tickWorstCovered = covered;
            }
            bool done = _probeTickT0 != 0 && (now - _probeTickT0) >= (long)(ProbeAfterTickSeconds * Stopwatch.Frequency);
            bool capped = (now - _probeT0) >= (long)(ProbeCapSeconds * Stopwatch.Frequency);
            if (!done && !capped) return;
            float toTick = _probeTickT0 != 0 ? (float)((_probeTickT0 - _probeT0) / (double)Stopwatch.Frequency) : -1f;
            Debug.Log(FormatFirstSecond(_worstMs, _worstAt, _worstCause, _worstCovered, _tickWorstMs, _tickWorstAt, _tickWorstCause, _tickWorstCovered, toTick));
            _probeT0 = 0;
            _probeDriver = null;
        }

        /// <summary>first-run-warm-2: the automated match never opens the loading screen; begin on each new match world once
        /// its art is built (WorldView.Building off) and a camera exists, before the first tick (HoldsSim). Looks 4x a second.</summary>
        void AutoBegin()
        {
            if (!_pendingLoad && Time.unscaledTime < _nextLook) return;   // b12-x1: every frame while a load is pending
            _nextLook = Time.unscaledTime + 0.25f;
            if (CriticalMass.Runtime.Menus.LoadingScreen.IsOpen || View.Dressing.WorldView.Building || UnityEngine.Camera.main == null) return;
            SimDriverBehaviour d = FindAnyObjectByType<SimDriverBehaviour>();
            if (d == null || d.World == null || ReferenceEquals(d.World, _warmedWorld)) return;
            _nextLook = 0f;
            // b12-x1: the title's (or a menu's) backdrop world is never warmed: a player never warms it, and the automated
            // run's whole-map renders landed inside the title's idle-CPU window. The match world after it is warmed as before.
            if (!WarmsWorld(true, d.Request != null && d.Request.Backdrop))
            {
                _warmedWorld = d.World;
                if (!FlowLoading()) _pendingLoad = false;
                Log("skipped origin=automation: backdrop world (title or menu), not a match");
                return;
            }
            StartWarm("automation");
        }

        void Step()
        {
            if (_stage != Stage.Done && _stage != Stage.Glyphs && OverBudget())
            {
                Log("cap hit ms=" + Ms(_allStart).ToString("0") + " (cap " + (BudgetSeconds * 1000f).ToString("0") + ") at step " + _stage.ToString().ToLowerInvariant() + "; the rest is skipped");
                ClearFx();
                Finish();
                return;
            }
            switch (_stage)
            {
                case Stage.Wide:
                    if (_frame >= WideFrames)
                    {
                        Log("wide " + Ms(_stageStart).ToString("0") + " (" + WideFrames + " off-screen renders over the whole map)");
                        Enter(Stage.Fx);
                        return;
                    }
                    if (!RenderWide()) { Log("wide 0 (no camera)"); Enter(Stage.Fx); return; }
                    _frame++;
                    _items++;
                    break;
                case Stage.Fx:
                {
                    if (_fx == null || _driver == null || _driver.World == null) { Log("fx 0 (no effects view)"); Enter(Stage.Audio); return; }
                    int total = Kinds.Length * FxFrames;
                    if (_frame >= total)
                    {
                        ClearFx();
                        Log("fx " + Ms(_stageStart).ToString("0") + " (" + Kinds.Length + " explosion kinds x " + FxFrames + " ages)");
                        Enter(Stage.Audio);
                        return;
                    }
                    World w = _driver.World;
                    int me = w.ActorCount > 0 ? 0 : -1;
                    double x = me >= 0 ? w.X[me] + 2.0 : w.Map.Width * 0.5, z = me >= 0 ? w.Z[me] + 1.0 : w.Map.Height * 0.5;
                    try { _fx.PreviewExplosion(Ages[_frame % FxFrames], x, z, 2f, Kinds[_frame / FxFrames]); }
                    catch (Exception e) { Log("fx preview failed: " + e.Message); _frame = total; return; }
                    _frame++;
                    _items++;
                    break;
                }
                case Stage.Audio:
                {
                    int n = 0;
                    foreach (AudioClip c in Resources.FindObjectsOfTypeAll<AudioClip>())
                        if (c != null && c.loadState == AudioDataLoadState.Unloaded && c.LoadAudioData()) n++;
                    _items += n;
                    Log("audio " + Ms(_stageStart).ToString("0") + " (" + n + " clips loaded)");
                    Enter(Stage.Glyphs);
                    break;
                }
                case Stage.Glyphs:
                {
                    int n = CriticalMass.Runtime.Hud.Style.GlyphWarmup.WarmScene();
                    _items += n;
                    Log("glyphs " + Ms(_stageStart).ToString("0") + " (" + n + " font sizes)");
                    Finish();
                    break;
                }
            }
        }

        void Finish()
        {
            _stage = Stage.Done;
            Log("end ms=" + Ms(_allStart).ToString("0") + " items=" + _items + " origin=" + _origin);
        }

        void ClearFx()
        {
            try { if (_fx != null && _fx.Scene != null) _fx.Scene.Clear(); } catch (Exception) { }
        }

        // ---- the wide view: one off-screen render with the camera pulled back, everything put back after ----

        bool RenderWide()
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null || _driver == null || _driver.World == null) return false;
            CriticalMass.Sim.Grid.MapDef m = _driver.World.Map;
            Vector3 a = SimSpace.ToWorld(0, 0, 0), b = SimSpace.ToWorld(m.Width, 0, m.Height);
            Vector3 centre = (a + b) * 0.5f;
            float radius = 0.5f * Vector3.Distance(a, b) + 8f;
            float half = Mathf.Max(10f, cam.fieldOfView * 0.5f) * Mathf.Deg2Rad;
            float dist = radius / Mathf.Tan(half) * 1.05f;
            Transform t = cam.transform;
            Vector3 pos = t.position;
            float far = cam.farClipPlane, lod = QualitySettings.lodBias, shadow = 0f;
            bool occ = cam.useOcclusionCulling;
            RenderTexture old = cam.targetTexture;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            int w = Mathf.Max(64, cam.pixelWidth / 2), h = Mathf.Max(64, cam.pixelHeight / 2);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 24);
            CriticalMass.Runtime.View.ActorViews views = null;
            try
            {
                t.position = centre - t.forward * dist;
                cam.farClipPlane = Mathf.Max(far, dist + radius * 2f);
                cam.useOcclusionCulling = false;
                QualitySettings.lodBias = WideLodBias;
                if (urp != null) { shadow = urp.shadowDistance; urp.shadowDistance = Mathf.Max(shadow, WideShadowDistance); }
                cam.targetTexture = rt;
                // b15 world look: the Kaiju's later stages (inactive until the hatch) draw in this render too.
                if (views == null) views = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.View.ActorViews>();
                if (views != null) Log("kaiju stages warmed (" + views.PrewarmKaiju(true) + " materials)");
                cam.Render();
            }
            catch (Exception e) { Log("wide render failed: " + e.Message); }
            finally
            {
                try { if (views != null) views.PrewarmKaiju(false); } catch (Exception) { }
                cam.targetTexture = old;
                t.position = pos;
                cam.farClipPlane = far;
                cam.useOcclusionCulling = occ;
                QualitySettings.lodBias = lod;
                if (urp != null) urp.shadowDistance = shadow;
                RenderTexture.ReleaseTemporary(rt);
            }
            return true;
        }

        // ---- counter ----

        void UpdateCounter()
        {
            if (_trace == null) return;
            if (!_matchStarted)
            {
                if (_stage != Stage.Done || CriticalMass.Runtime.Menus.LoadingScreen.IsOpen) return;
                if (_driver == null || _driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing) return;
                _matchStarted = true;
                _base = _trace.totalGraphicsStateCount;
                _nextLog = Time.unscaledTime + 30f;
                Log("match start: " + _base + " graphics states met so far (" + _trace.variantCount + " variants)");
                return;
            }
            if (Time.unscaledTime < _nextLog) return;
            _nextLog = Time.unscaledTime + 30f;
            LogCount();
        }

        void LogCount()
        {
            if (_trace == null || !_matchStarted) return;
            try { Log("statesAfterStart=" + (_trace.totalGraphicsStateCount - _base) + " (total " + _trace.totalGraphicsStateCount + ", at start " + _base + ")"); }
            catch (Exception) { }
        }

        void OnDestroy()
        {
            if (_ownTrace && _trace != null) { try { _trace.EndTrace(); } catch (Exception) { } }
        }
    }
}

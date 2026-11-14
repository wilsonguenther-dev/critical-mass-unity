using System;
using System.Diagnostics;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
    /// </summary>
    public sealed class FirstRunWarm : MonoBehaviour
    {
        public const float BudgetSeconds = 1.2f;
        public const int WideFrames = 3, FxFrames = 2;
        public const float WideLodBias = 50f, WideShadowDistance = 400f;
        public const string CountFlag = "-warmCount";
        static readonly ExplosionKind[] Kinds = { ExplosionKind.Tnt, ExplosionKind.Mine, ExplosionKind.Barrel, ExplosionKind.WallBreak, ExplosionKind.KaijuSlam };
        static readonly double[] Ages = { 0.15, 1.0 };

        enum Stage { Idle, Wide, Fx, Audio, Glyphs, Done }

        static FirstRunWarm _instance;
        Stage _stage = Stage.Idle;
        int _frame;
        long _stageStart, _allStart;
        SimDriverBehaviour _driver;
        CombatFx _fx;

        // counter
        GraphicsStateCollection _trace;
        bool _ownTrace, _counting, _matchStarted;
        int _base;
        float _nextLog;

        /// <summary>True when there is no warm-up in progress (finished, skipped or never begun): the loading screen may close.</summary>
        public static bool Done => _instance == null || _instance._stage == Stage.Idle || _instance._stage == Stage.Done;
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
            _instance.StartWarm();
        }

        void OnEnable()
        {
            string[] args = Environment.GetCommandLineArgs();
            _counting = Array.IndexOf(args, CountFlag) >= 0 || Array.IndexOf(args, "-automation") >= 0;
        }

        void Start()
        {
            if (!_counting) return;
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

        void StartWarm()
        {
            _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null || UnityEngine.Camera.main == null)
            {
                Log("skipped: no match or camera yet");
                _stage = Stage.Done;
                return;
            }
            _fx = _driver.GetComponent<CombatFx>() ?? FindAnyObjectByType<CombatFx>();
            _allStart = Now();
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
            if (_stage != Stage.Idle && _stage != Stage.Done) Step();
            UpdateCounter();
        }

        void Step()
        {
            if (_stage != Stage.Done && _stage != Stage.Glyphs && OverBudget())
            {
                Log("budget " + (BudgetSeconds * 1000f).ToString("0") + " ms reached at step " + _stage.ToString().ToLowerInvariant() + "; the rest is skipped");
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
                    break;
                }
                case Stage.Audio:
                {
                    int n = 0;
                    foreach (AudioClip c in Resources.FindObjectsOfTypeAll<AudioClip>())
                        if (c != null && c.loadState == AudioDataLoadState.Unloaded && c.LoadAudioData()) n++;
                    Log("audio " + Ms(_stageStart).ToString("0") + " (" + n + " clips loaded)");
                    Enter(Stage.Glyphs);
                    break;
                }
                case Stage.Glyphs:
                {
                    int n = CriticalMass.Runtime.Hud.Style.GlyphWarmup.WarmScene();
                    Log("glyphs " + Ms(_stageStart).ToString("0") + " (" + n + " font sizes)");
                    Finish();
                    break;
                }
            }
        }

        void Finish()
        {
            _stage = Stage.Done;
            Log("total " + Ms(_allStart).ToString("0"));
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
            try
            {
                t.position = centre - t.forward * dist;
                cam.farClipPlane = Mathf.Max(far, dist + radius * 2f);
                cam.useOcclusionCulling = false;
                QualitySettings.lodBias = WideLodBias;
                if (urp != null) { shadow = urp.shadowDistance; urp.shadowDistance = Mathf.Max(shadow, WideShadowDistance); }
                cam.targetTexture = rt;
                cam.Render();
            }
            catch (Exception e) { Log("wide render failed: " + e.Message); }
            finally
            {
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

using System;
using System.IO;
using CriticalMass.Runtime.Hud;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-59 shader warm-up, so the first Kaiju hatch, explosion or berserk does not stutter.
    /// <para>Trace: an autoplay run with -traceStates records every shader variant and graphics state it meets into
    /// DATA/traces/&lt;api&gt;.graphicsstate (GraphicsStateCollection, Unity 6, experimental API) and saves it when the run
    /// quits. The integrator copies the files to Assets/StreamingAssets/ShaderWarmup/ (Metal, Direct3D11, Direct3D12).</para>
    /// <para>Warm-up: on the title screen (and any loading screen) the collection for the running API is warmed a few
    /// variants per frame (WarmUpProgressively), never in a match. Where the API has no parallel pipeline creation, Unity
    /// itself falls back to ShaderVariantCollection warm-up (documented remark). No file: nothing happens.</para>
    /// </summary>
    public sealed class ShaderWarmup : MonoBehaviour
    {
        public const string Folder = "ShaderWarmup";
        public const int PerFrame = 8;
        /// <summary>perf-smooth-1: behind an opaque loading screen nothing is drawn that a hitch could spoil: warm faster.</summary>
        public const int PerFrameLoading = 32;

        /// <summary>first-run-warm-1: the -traceStates trace while it runs (FirstRunWarm's counter reads it), else null.</summary>
        public static GraphicsStateCollection ActiveTrace { get; private set; }

        /// <summary>true once the shipped collection for this API is fully warmed (or there is none).</summary>
        public static bool Done { get; private set; }

        static GameObject _host;
        GraphicsStateCollection _warm, _trace;
        JobHandle _job;
        bool _tracing, _jobRunning;
        float _titleCheck;
        bool _title;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_host != null || Application.isEditor || Application.isBatchMode) return;
            _host = new GameObject("CriticalMass ShaderWarmup");
            DontDestroyOnLoad(_host);
            _host.AddComponent<ShaderWarmup>();
        }

        /// <summary>The file name for a graphics API (pure; tests).</summary>
        public static string FileFor(GraphicsDeviceType api) => api + ".graphicsstate";

        /// <summary>first-run-warm-2: a recording smaller than this holds no states (the Air's empty trace was 282 bytes).</summary>
        public const long MinRecordingBytes = 1024;

        // perf/start-hitch-1: timed for the scene-start report (LoadTimer).
        void Start() { long t0 = CriticalMass.Runtime.Game.LoadTimer.Now(); StartBody(); CriticalMass.Runtime.Game.LoadTimer.Part("ShaderWarmup.Start", t0); }

        void StartBody()
        {
            try
            {
                // first-run-warm-2: tracing and warming a recording need parallel PSO creation (Unity's BeginTrace docs); the
                // Air's Metal player says no and wrote an empty 282-byte trace. Skip both cleanly there; FirstRunWarm's
                // off-screen warm-up does not need them.
                bool usable = FirstRunWarm.RecordingSupported;
                if (GamePaths.HasFlag("-traceStates") && !usable)
                    Debug.Log("[CM.Warmup] -traceStates skipped: supportsParallelPSOCreation=false on " + SystemInfo.graphicsDeviceType);
                if (GamePaths.HasFlag("-traceStates") && usable)
                {
                    _trace = new GraphicsStateCollection();
                    _trace.BeginTrace();
                    _tracing = true;
                    ActiveTrace = _trace;
                    Application.quitting += SaveTrace;
                    Debug.Log("[CM.Warmup] tracing shader variants and graphics states for " + SystemInfo.graphicsDeviceType);
                }
                string path = Path.Combine(Application.streamingAssetsPath, Folder, FileFor(SystemInfo.graphicsDeviceType));
                if (File.Exists(path) && usable && new FileInfo(path).Length >= MinRecordingBytes)
                {
                    _warm = new GraphicsStateCollection(path);
                    if (_warm.totalGraphicsStateCount <= 0 && _warm.variantCount <= 0)
                    {
                        _warm = null;
                        Done = true;
                        Debug.Log($"[CM.Warmup] skipped: {path} holds no graphics states");
                    }
                    else Debug.Log($"[CM.Warmup] warming {path} on the title and loading screens");
                }
                else
                {
                    Done = true;
                    if (File.Exists(path)) Debug.Log($"[CM.Warmup] skipped: {path} " + (usable ? "is empty" : "needs parallel PSO creation, unsupported here"));
                }
            }
            catch (Exception e)
            {
                Done = true;   // an unsupported API must never stop the game
                Debug.LogWarning($"[CM.Warmup] skipped: {e.GetType().Name}: {e.Message}");
            }
        }

        void SaveTrace()
        {
            if (!_tracing || _trace == null) return;
            try
            {
                _trace.EndTrace();
                ActiveTrace = null;
                string dir = Path.Combine(GamePaths.DataDir, "traces");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, FileFor(SystemInfo.graphicsDeviceType));
                _trace.SaveToFile(path);
                Debug.Log("[CM.Warmup] trace saved: " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CM.Warmup] trace not saved: {e.Message}");
            }
            _tracing = false;
        }

        void Update()
        {
            if (Done || _warm == null) return;
            _titleCheck -= Time.unscaledDeltaTime;
            if (_titleCheck <= 0f)
            {
                _titleCheck = 0.25f;
                _title = FindAnyObjectByType<TitleScreen>() != null || Menus.LoadingScreen.IsOpen;
            }
            if (_jobRunning)
            {
                if (!_job.IsCompleted) return;
                _job.Complete();
                _jobRunning = false;
            }
            if (!_title) return;   // only on the title or loading screens, never mid-match
            try
            {
                if (_warm.isWarmedUp)
                {
                    Done = true;
                    Debug.Log("[CM.Warmup] done");
                    return;
                }
                _job = _warm.WarmUpProgressively(Menus.LoadingScreen.IsOpen ? PerFrameLoading : PerFrame);
                _jobRunning = true;
            }
            catch (Exception e)
            {
                Done = true;
                Debug.LogWarning($"[CM.Warmup] stopped: {e.Message}");
            }
        }

        void OnDestroy()
        {
            if (_jobRunning) _job.Complete();
        }
    }
}

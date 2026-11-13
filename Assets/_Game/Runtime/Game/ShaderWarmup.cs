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

        void Start()
        {
            try
            {
                if (GamePaths.HasFlag("-traceStates"))
                {
                    _trace = new GraphicsStateCollection();
                    _trace.BeginTrace();
                    _tracing = true;
                    Application.quitting += SaveTrace;
                    Debug.Log("[CM.Warmup] tracing shader variants and graphics states for " + SystemInfo.graphicsDeviceType);
                }
                string path = Path.Combine(Application.streamingAssetsPath, Folder, FileFor(SystemInfo.graphicsDeviceType));
                if (File.Exists(path))
                {
                    _warm = new GraphicsStateCollection(path);
                    Debug.Log($"[CM.Warmup] warming {path} on the title and loading screens");
                }
                else Done = true;
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

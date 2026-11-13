using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-05 command line, crash log and error-loop guard. Installed once per run on a DontDestroyOnLoad
    /// "CriticalMass Services" object (with PerfOverlay), so GameBootstrap is untouched; it finds the SimDriverBehaviour
    /// whenever one exists and only writes the driver's public fields (HumanAutopilot, TimeScale). Never writes Sim state.
    ///
    /// Flags (case-insensitive):
    ///   -autoplay                actor 0 plays as a bot; the title is dismissed after -autoplayTitleSeconds; recorded
    ///   -quitAfterMatch          quit after the last match: exit 0 clean, 3 when an exception was logged, 4 on timeout
    ///   -autoplayMatches N       back-to-back matches in one process (the Boot scene reloads between them), default 1
    ///   -autoplayTitleSeconds S  seconds on the title before match 1 (idle measurement), default 1
    ///   -autoplaySpeed X         sim seconds per wall second (default: the game's own, 2)
    ///   -autoplayTimeout S       per match: quit with exit 4 when it has not ended after S wall seconds (default 900)
    ///   -perfLog PATH            match 1's report as JSON; match n writes PATH with "-m{n}" before the extension
    ///                            (default persistentDataPath/perf-last.json)
    ///   -novsync                 no frame cap at all (FrameGovernor steps aside): measures the real frame cost
    ///   -safe                    lowest settings (read by FrameGovernor)
    ///   -dataDir PATH            the game's files go to PATH, not the player's folder (GamePaths; smoke and soak isolation)
    ///   -quality NAME|N          this quality level for the run (FrameGovernor; the perf report has quality_requested)
    ///   -debugHangSeconds N      TEST ONLY: the main thread sleeps N s once, 3 s after the title (proves the hang watchdog)
    ///   -seed N -simHashOut PATH sim-only run (no view): one 16v16 bot match on seed N, its end-state hash to PATH, quit (DG-66)
    ///   -stressHill              perf-60hz-32p: camera pinned on the hill, every living fighter drawn within 9 m of it (view only)
    ///   -shots                   screenshots shot-title.png, shot-start.png (3 s of match time), shot-mid.png and shot-kaiju-1..6.png (Kaiju walk strip after the hatch) into the data folder (Windows graphics run)
    ///   -traceStates             record shader states for warm-up (ShaderWarmup); -exportReport, -diag: SupportReport
    /// DG-63: the crash log is written by CrashLog (bounded queue, one background writer, 20 lines per second, repeat keys
    /// with digits and hex as "#"); Exceptions and Errors both count. A stack in the sim or the match driver ends the match
    /// to the menu with a notice instead of freezing it.
    /// Player builds append every exception and error to persistentDataPath/crash-log.txt with the build stamp (capped
    /// at 1 MB, the previous file kept as crash-log.1.txt). The same exception repeated RepeatLimit times stops the
    /// CriticalMass behaviour at the top of its stack (all instances disabled) and shows a small notice, so a per-frame
    /// error never spins; later repeats are counted, not written.
    /// </summary>
    public sealed class AutoplayArgs : MonoBehaviour
    {
        public const int ExitClean = 0;
        public const int ExitExceptions = 3;
        public const int ExitTimeout = 4;
        public const string LogTag = "[CM.Autoplay]";
        public const int RepeatLimit = 20;
        public const long CrashLogMaxBytes = 1024 * 1024;

        /// <summary>Parsed command line.</summary>
        public struct Options
        {
            public bool Autoplay;
            public bool QuitAfterMatch;
            /// <summary>0 = leave the driver's TimeScale as the game sets it.</summary>
            public double Speed;
            public double TimeoutSec;
            public string PerfLog;
            /// <summary>perf-smooth-1: -autoplayQualities A,B,C: the quality level of match 1, 2, 3 (match 1 also takes -quality).</summary>
            public string[] Qualities;
            /// <summary>perf-smooth-1 experiment: -autoplayPacing A,B,C: frame pacing of match 1, 2, 3 (FrameGovernor.PacingOverride:
            /// rule, display, timed60).</summary>
            public string[] Pacing;
            public bool NoVsync;
            public int Matches;
            public double TitleSeconds;
            public double DebugHangSeconds;
            public string SimHashOut;
            public int Seed;
            public bool Shots;
            /// <summary>perf-60hz-32p: -stressHill: the worst case for frame cost, all 32 fighters drawn on screen at once. The
            /// camera is pinned on the hill and every living fighter is DRAWN within 9 m of it (ActorViews moves only the
            /// models; the sim, its bots and its hash are untouched). Automation runs only.</summary>
            public bool StressHill;
        }

        /// <summary>Pure parser (tests). Unknown flags are ignored, so Unity's own (-screen-width, -logFile) pass through.</summary>
        public static Options Parse(string[] args)
        {
            var o = new Options { TimeoutSec = 900, Matches = 1, TitleSeconds = 1 };
            if (args == null) return o;
            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").Trim().ToLowerInvariant();
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "-autoplay": o.Autoplay = true; break;
                    case "-quitaftermatch": o.QuitAfterMatch = true; break;
                    case "-novsync": o.NoVsync = true; break;
                    case "-autoplayspeed":
                        if (TryNum(next, out double s) && s > 0) { o.Speed = s; i++; }
                        break;
                    case "-autoplaytimeout":
                        if (TryNum(next, out double t) && t > 0) { o.TimeoutSec = t; i++; }
                        break;
                    case "-autoplaymatches":
                        if (TryNum(next, out double m) && m >= 1) { o.Matches = (int)Math.Min(m, 50); i++; }
                        break;
                    case "-autoplaytitleseconds":
                        if (TryNum(next, out double ts) && ts >= 0) { o.TitleSeconds = ts; i++; }
                        break;
                    case "-debughangseconds":
                        if (TryNum(next, out double hs) && hs > 0) { o.DebugHangSeconds = Math.Min(hs, 120); i++; }
                        break;
                    case "-seed":
                        if (TryNum(next, out double sd)) { o.Seed = (int)sd; i++; }
                        break;
                    case "-simhashout":
                        if (!string.IsNullOrEmpty(next) && !next.StartsWith("-", StringComparison.Ordinal)) { o.SimHashOut = next; i++; }
                        break;
                    case "-shots": o.Shots = true; break;
                    case "-stresshill": o.StressHill = true; break;
                    case "-autoplaypacing":
                        if (!string.IsNullOrEmpty(next) && !next.StartsWith("-", StringComparison.Ordinal)) { o.Pacing = next.Split(','); i++; }
                        break;
                    case "-autoplayqualities":
                        if (!string.IsNullOrEmpty(next) && !next.StartsWith("-", StringComparison.Ordinal)) { o.Qualities = next.Split(','); i++; }
                        break;
                    case "-perflog":
                        if (!string.IsNullOrEmpty(next) && !next.StartsWith("-", StringComparison.Ordinal)) { o.PerfLog = next; i++; }
                        break;
                }
            }
            return o;
        }

        /// <summary>-cameraYaw DEGREES (camera-orientation grabs, 2026-11-13): the gameplay camera's yaw for this run, in place of
        /// RenderConfig.CameraYawDeg (45 = the map reads as a diamond, 0 = true north, bases left and right). Honoured only
        /// together with an automation flag (AutomationWindow.AutomationFlags), so a person's launch never changes the
        /// camera; normalised to [0, 360). Pure (EditMode tests: CameraYawFlagTests). The minimap stays baked at 45.</summary>
        public static bool TryCameraYaw(string[] args, out double yawDeg)
        {
            yawDeg = 0;
            if (args == null) return false;
            bool automation = false, found = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").Trim().ToLowerInvariant();
                if (Array.IndexOf(AutomationWindow.AutomationFlags, a) >= 0) automation = true;
                if (a == "-camerayaw" && i + 1 < args.Length && TryNum(args[i + 1], out double y))
                {
                    yawDeg = ((y % 360) + 360) % 360;
                    found = true;
                    i++;
                }
            }
            return found && automation;
        }

        /// <summary>Applies -cameraYaw from this process's command line to a match config (player and editor alike).</summary>
        public static void ApplyCameraYaw(GameConfig cfg)
        {
            if (cfg == null || !TryCameraYaw(Environment.GetCommandLineArgs(), out double y)) return;
            cfg.Render.CameraYawDeg = y;
            Debug.Log($"{LogTag} -cameraYaw: gameplay camera yaw {y.ToString("F1", CultureInfo.InvariantCulture)} deg (default 45)");
        }

        /// <summary>-shots, batch 7: 4 s after the Kaiju hatches the camera is held on it (CameraFollowTarget.SetOverride every
        /// frame) and 1.5 s later six shots 0.25 s apart are written as shot-kaiju-1..6.png, each logged with the Kaiju's sim
        /// position so a reader can tell it walked; then the camera goes back to the player.</summary>
        void KaijuStrip(double now)
        {
            World w = _driver.World;
            if (w.Kaiju == null) return;
            if (_kaijuHatchAt < 0)
            {
                if (w.Match.Hatched && w.Kaiju.State != KaijuState.Egg) _kaijuHatchAt = now;
                return;
            }
            if (now - _kaijuHatchAt < 4.0) return;
            if (_kaijuCam == null) _kaijuCam = FindAnyObjectByType<Camera.CameraFollowTarget>();
            if (_kaijuCam == null) { _kaijuShots = 6; return; }
            _kaijuCam.SetOverride(w.Kaiju.X, w.Kaiju.Z);
            if (now - _kaijuHatchAt < 5.5 || now < _kaijuNextShot) return;
            _kaijuShots++;
            _kaijuNextShot = now + 0.25;
            ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, "shot-kaiju-" + _kaijuShots + ".png"));
            Debug.Log($"{LogTag} shot-kaiju-{_kaijuShots}: tick {w.Tick}, kaiju {w.Kaiju.State} at " +
                      $"({w.Kaiju.X.ToString("F2", CultureInfo.InvariantCulture)}, {w.Kaiju.Z.ToString("F2", CultureInfo.InvariantCulture)})");
            if (_kaijuShots >= 6) _kaijuCam.ClearOverride();
        }

        static bool TryNum(string s, out double v) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>The report path for match n (1-based): "a/perf.json" -> "a/perf-m2.json".</summary>
        public static string PerfPathFor(string basePath, int match)
        {
            if (match <= 1) return basePath;
            string ext = Path.GetExtension(basePath);
            return basePath.Substring(0, basePath.Length - ext.Length) + "-m" + match + ext;
        }

        static Options? _current;

        /// <summary>The options this run was started with (parsed on first use).</summary>
        public static Options Current => (_current ?? (_current = Parse(Environment.GetCommandLineArgs()))).Value;

        /// <summary>Exceptions and errors logged since boot (any thread).</summary>
        public static int ExceptionCount => _exceptions;
        public static int ErrorCount => _errors;

        /// <summary>A short notice after the guard stopped a system (FrameGovernor shows it until StoppedNoticeUntil).</summary>
        public static string StoppedNotice { get; private set; }
        public static float StoppedNoticeUntil { get; private set; }

        static int _exceptions, _errors;
        static GameObject _host;
        static string _crashLogPath;
        static CrashLog _crash;
        static readonly object CrashLock = new object();
        static readonly Queue<string> ToStop = new Queue<string>();
        static double _processStartOffset = -1;   // seconds between process start and engine start, when the OS says

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_host != null) return;
            _host = new GameObject("CriticalMass Services");
            DontDestroyOnLoad(_host);
            _host.AddComponent<PerfOverlay>();
            _host.AddComponent<AutoplayArgs>();
        }

        // ── autoplay state ──
        SimDriverBehaviour _driver;
        TitleScreen _title;
        bool _titleSeen, _recording, _ended, _quitting, _timedOut, _hangDone, _shotTitle, _shotStart, _shotMid;
        // batch 7: Kaiju walk strip under -shots (camera held on the walking Kaiju, 6 shots 0.25 s apart).
        double _kaijuHatchAt = -1, _kaijuNextShot; int _kaijuShots; Camera.CameraFollowTarget _kaijuCam;
        double _titleAt = -1, _matchStart, _endAt, _firstTitleAt = -1;
        bool _costReset;
        int _exceptionsAtStart, _match = 1, _reloadFrame = -1;

        void Awake()
        {
            if (!Application.isEditor)
            {
                _crashLogPath = Path.Combine(GamePaths.DataDir, "crash-log.txt");
                if (_crash == null) _crash = new CrashLog(_crashLogPath, true, BuildInfo.Label);
                Application.quitting -= OnQuitFlush;
                Application.quitting += OnQuitFlush;
                Application.logMessageReceivedThreaded -= OnLog;
                Application.logMessageReceivedThreaded += OnLog;
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
                AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
                try
                {
                    DateTime start = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
                    _processStartOffset = Math.Max(0, (DateTime.UtcNow - start).TotalSeconds - Time.realtimeSinceStartupAsDouble);
                }
                catch (Exception)
                {
                    _processStartOffset = -1;
                }
                Debug.Log($"[CriticalMass] {BuildInfo.Label} built {BuildInfo.BuiltUtc}; Player.log {Application.consoleLogPath}; " +
                          $"crash log {_crashLogPath}; {SystemInfo.operatingSystem}; {SystemInfo.processorType} x{SystemInfo.processorCount}; " +
                          $"{SystemInfo.systemMemorySize} MB; {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceVersion})");
                if (SupportReport.HandleBootFlags()) { _quitting = true; return; }
            }
            Options o = Current;
            if (!string.IsNullOrEmpty(o.SimHashOut))
            {
                RunSimHash(o);
                return;
            }
            if (o.Autoplay)
            {
                Application.runInBackground = true;   // a background window must not pause the recorded match
                _matchStart = Time.realtimeSinceStartupAsDouble;
                Debug.Log($"{LogTag} START {BuildInfo.Label} matches={o.Matches} speed={(o.Speed > 0 ? o.Speed.ToString(CultureInfo.InvariantCulture) : "default")} " +
                          $"quitAfterMatch={o.QuitAfterMatch} novsync={o.NoVsync} titleSeconds={Num(o.TitleSeconds)} screen={Screen.width}x{Screen.height}");
            }
        }

        // ── crash log and repeat guard (any thread) ──

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception) Interlocked.Increment(ref _exceptions);
            else if (type == LogType.Error || type == LogType.Assert) Interlocked.Increment(ref _errors);
            else return;
            // DG-56: a lost graphics device (Windows TDR, a GPU reset) counts the session as unclean.
            if (condition != null && (condition.IndexOf("DEVICE_REMOVED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      condition.IndexOf("device removed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      condition.IndexOf("device lost", StringComparison.OrdinalIgnoreCase) >= 0))
                SessionKeeper.Flag("graphics-device-lost");
            CrashLog log = _crash;
            if (log == null) return;
            int n = log.Submit(type.ToString(), condition, stack);
            if (n == RepeatLimit && !string.IsNullOrEmpty(stack))
                lock (CrashLock) ToStop.Enqueue(stack);
        }

        static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Interlocked.Increment(ref _exceptions);
            _crash?.Submit("Unhandled", e.ExceptionObject?.ToString() ?? "unknown", "");
            _crash?.FlushNow();
        }

        static void OnQuitFlush()
        {
            SupportReport.OnQuitDiag();
            _crash?.Dispose();
        }

        /// <summary>
        /// Type names named by a Unity stack trace, top first. Handles both formats Unity writes:
        /// "Ns.Type.Method () (at file:line)" and "Ns.Type:Method()". Pure (tests).
        /// </summary>
        public static List<string> StackTypeNames(string stack)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(stack)) return names;
            foreach (string raw in stack.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int paren = line.IndexOf('(');
                string head = (paren >= 0 ? line.Substring(0, paren) : line).Trim();
                string type;
                int colon = head.IndexOf(':');
                if (colon > 0) type = head.Substring(0, colon);
                else
                {
                    int dot = head.LastIndexOf('.');
                    if (dot <= 0) continue;
                    type = head.Substring(0, dot);
                }
                type = type.Replace('/', '+').Trim();
                if (type.Length > 0 && !names.Contains(type)) names.Add(type);
            }
            return names;
        }

        /// <summary>true when a stack runs through the sim or the match driver (stopping it would freeze the match; pure; tests).</summary>
        public static bool IsMatchCritical(List<string> stackTypes)
        {
            foreach (string n in stackTypes)
                if (n.StartsWith("CriticalMass.Sim.", StringComparison.Ordinal) || n == typeof(SimDriverBehaviour).FullName) return true;
            return false;
        }

        static void StopOffender(string stack)
        {
            List<string> types = StackTypeNames(stack);
            if (IsMatchCritical(types))
            {
                EndMatchAfterError();
                return;
            }
            foreach (string name in types)
            {
                if (!name.StartsWith("CriticalMass.", StringComparison.Ordinal)) continue;
                Type t = null;
                foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.GetName().Name.StartsWith("CriticalMass", StringComparison.Ordinal)) continue;
                    t = asm.GetType(name, false);
                    if (t != null) break;
                }
                if (t == null || !typeof(Behaviour).IsAssignableFrom(t)) continue;
                if (t == typeof(AutoplayArgs) || t == typeof(FrameGovernor)) return;   // the guard never stops itself
                UnityEngine.Object[] found = FindObjectsByType(t, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (UnityEngine.Object o in found) ((Behaviour)o).enabled = false;
                StoppedNotice = $"{t.Name} stopped after repeated errors. Details are in the crash log.";
                StoppedNoticeUntil = Time.realtimeSinceStartup + 15f;
                Debug.LogWarning($"[CM.Guard] disabled {found.Length} {t.FullName} after {RepeatLimit} repeats of one exception");
                return;
            }
        }

        /// <summary>DG-63: the sim or driver keeps failing: leave the match for the menu with a notice, never a frozen match.</summary>
        static void EndMatchAfterError()
        {
            StoppedNotice = "The match stopped because of an error. Details are in the crash log.";
            StoppedNoticeUntil = Time.realtimeSinceStartup + 15f;
            Debug.LogWarning($"[CM.Guard] the match driver failed {RepeatLimit} times; ending the match");
            if (Current.Autoplay && Current.QuitAfterMatch)
            {
                Application.Quit(ExitExceptions);
                return;
            }
            SimDriverBehaviour d = FindAnyObjectByType<SimDriverBehaviour>();
            if (d != null) d.enabled = false;
            try
            {
                Flow.GameFlow.ReturnTo(Flow.FlowState.Menu);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CM.Guard] could not return to the menu: {e.Message}");
            }
        }

        // ── DG-66 sim-only hash run ──

        /// <summary>FNV-1a over the end state of a world (pure; the same on every machine for the same seed).</summary>
        public static ulong WorldHash(World w)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v)
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }
            Mix(w.Tick);
            Mix(BitConverter.DoubleToInt64Bits(w.Match.Time));
            Mix((long)w.Match.Winner);
            Mix(BitConverter.DoubleToInt64Bits(w.Match.HillSeconds.Azure));
            Mix(BitConverter.DoubleToInt64Bits(w.Match.HillSeconds.Crimson));
            for (int i = 0; i < w.ActorCount; i++)
            {
                Mix(BitConverter.DoubleToInt64Bits(w.X[i]));
                Mix(BitConverter.DoubleToInt64Bits(w.Z[i]));
                Mix(BitConverter.DoubleToInt64Bits(w.Vx[i]));
                Mix(BitConverter.DoubleToInt64Bits(w.Vz[i]));
                Mix(w.Actors[i].Alive ? 1 : 0);
            }
            Mix(w.GridVersion);
            return h;
        }

        /// <summary>One bot match on a seed, stepped to its end (or maxTicks); returns the world (pure sim; tests).</summary>
        public static World RunSimMatch(int seed, int maxTicks)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
            });
            InputFrame scratch = SimInput.EmptyInput();
            for (int t = 0; t < maxTicks && w.Match.Phase != MatchPhase.Ended; t++) MatchTick.Advance(w, null, scratch);
            return w;
        }

        void RunSimHash(Options o)
        {
            int code = ExitClean;
            try
            {
                World w = RunSimMatch(o.Seed != 0 ? o.Seed : 1337, 60000);
                string line = $"seed={(o.Seed != 0 ? o.Seed : 1337)} ticks={w.Tick} winner={w.Match.Winner} hash={WorldHash(w):x16}\n";
                File.WriteAllText(o.SimHashOut, line);
                Debug.Log($"{LogTag} SIMHASH {line.Trim()} arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            }
            catch (Exception e)
            {
                code = ExitExceptions;
                Debug.LogError($"{LogTag} SIMHASH failed: {e}");
            }
            _quitting = true;
            Application.Quit(code);
        }

        // ── per frame ──

        void Update()
        {
            string stop = null;
            lock (CrashLock)
            {
                if (ToStop.Count > 0) stop = ToStop.Dequeue();
            }
            if (stop != null) StopOffender(stop);

            double now = Time.realtimeSinceStartupAsDouble;
            if (!_titleSeen)
            {
                _title = FindAnyObjectByType<TitleScreen>();
                if (_title != null)
                {
                    _titleSeen = true;
                    _titleAt = now;
                    if (Current.Autoplay || Current.QuitAfterMatch) PerfOverlay.SetPhase(PerfOverlay.PhaseTitle);   // DG-111
                    if (!Application.isEditor && _firstTitleAt < 0)
                    {
                        _firstTitleAt = now;
                        string fromProcess = _processStartOffset >= 0
                            ? (now + _processStartOffset).ToString("F2", CultureInfo.InvariantCulture) : "unknown";
                        Debug.Log($"[CM.Boot] title on screen {now.ToString("F2", CultureInfo.InvariantCulture)} s after engine start, " +
                                  $"{fromProcess} s after process start, utc_ms={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
                    }
                }
            }

            Options o = Current;
            if (o.DebugHangSeconds > 0 && !_hangDone && _firstTitleAt >= 0 && now - _firstTitleAt > 3.0)
            {
                _hangDone = true;
                Debug.LogWarning($"{LogTag} -debugHangSeconds: the main thread sleeps {o.DebugHangSeconds} s (test only)");
                Thread.Sleep((int)(o.DebugHangSeconds * 1000));
            }
            if (o.Shots && !_shotTitle && _titleSeen && now - _titleAt > 2.0)
            {
                _shotTitle = true;
                ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, "shot-title.png"));
            }
            if (!o.Autoplay && !o.QuitAfterMatch) return;
            if (o.Shots && !_shotStart && _recording && _driver != null && _driver.World != null && _driver.World.Match.Time > 3)
            {
                // Match start: the moment the owner saw rings without bodies (2026-11-13); proves fighters draw from the first seconds.
                _shotStart = true;
                ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, "shot-start.png"));
            }
            if (o.Shots && _kaijuShots < 6 && _recording && _driver != null && _driver.World != null) KaijuStrip(now);
            if (o.Shots && !_shotMid && _recording && _driver != null && _driver.World != null && _driver.World.Match.Time > 60)
            {
                _shotMid = true;
                ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, "shot-mid.png"));
            }
            if (_driver == null)
            {
                _driver = FindAnyObjectByType<SimDriverBehaviour>();
                // After a reload GameBootstrap normally rebuilds the game from sceneLoaded; build it if it did not.
                if (_driver == null && _reloadFrame >= 0 && Time.frameCount > _reloadFrame + 2)
                {
                    GameObject g = GameBootstrap.Build(SceneManager.GetActiveScene());
                    _driver = g != null ? g.GetComponent<SimDriverBehaviour>() : null;
                }
                if (_driver != null && _reloadFrame >= 0)
                {
                    _reloadFrame = -1;
                    _matchStart = now;
                }
            }
            if (_driver == null || _driver.World == null || _quitting) return;
            SessionKeeper.MatchTick = _driver.World.Tick;

            if (o.Autoplay)
            {
                _driver.HumanAutopilot = true;
                if (o.Speed > 0) _driver.TimeScale = o.Speed;
                double hold = _match == 1 ? o.TitleSeconds : 1.0;
                if (_title != null && !_costReset && now - _titleAt >= 2.0)
                {
                    _costReset = true;
                    FrameCost.Reset();
                    if (_match == 1) FrameRecorder.Begin();   // perf-60hz-32p: the title's own frames (frames-title.csv)
                }
                if (_title != null && now - _titleAt >= hold)
                {
                    Debug.Log("[CM.Cost] title " + FrameCost.Report());
                    if (FrameRecorder.Active)
                    {
                        string perfBase = string.IsNullOrEmpty(Current.PerfLog) ? Path.Combine(Application.persistentDataPath, "perf-last.json") : Current.PerfLog;
                        string titleCsv = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(perfBase)) ?? "", "frames-title.csv");
                        Debug.Log("[CM.Frames] title " + FrameRecorder.End(titleCsv));
                    }
                    _title.Hide();
                    _title = null;
                }
                if (!_recording && !_ended && _title == null)
                {
                    _recording = true;
                    _exceptionsAtStart = _exceptions;
                    PerfOverlay.ClearPhase(PerfOverlay.PhaseMatch);
                    PerfOverlay.ClearPhase(PerfOverlay.PhaseResults);
                    PerfOverlay.SetPhase(PerfOverlay.PhaseMatch);
                    if (_match == 1 && o.Pacing != null && o.Pacing.Length > 0) FrameGovernor.PacingOverride = o.Pacing[0];
                    PerfOverlay.BeginRecording();
                    FrameRecorder.Begin();
                }
            }

            if (!_ended && _driver.World.Match.Phase == MatchPhase.Ended)
            {
                _ended = true;
                _endAt = now;
                Finish("MATCH END");
            }
            else if (!_ended && o.Autoplay && now - _matchStart > o.TimeoutSec)
            {
                _ended = true;
                _timedOut = true;
                _endAt = now;
                Finish("TIMEOUT");
            }
            if (!_ended || now - _endAt < 1.5) return;
            if (_pendingExtra != null) RewriteWithPhases();   // DG-111: the results phase is complete now

            if (!_timedOut && o.Autoplay && _match < o.Matches)
            {
                // Next match: reload Boot; the old game goes with the scene, GameBootstrap builds a fresh one.
                _match++;
                // perf-smooth-1: -autoplayQualities High,Medium,Low runs match n at the n-th level (one smoke, three levels).
                if (o.Qualities != null && _match - 1 < o.Qualities.Length) FrameGovernor.ForceRunQuality(o.Qualities[_match - 1]);
                if (o.Pacing != null && _match - 1 < o.Pacing.Length) FrameGovernor.PacingOverride = o.Pacing[_match - 1];
                _driver = null;
                _title = null;
                _titleSeen = false;
                _ended = false;
                _recording = false;
                _reloadFrame = Time.frameCount;
                SceneManager.LoadScene(GameBootstrap.BootScenePath);
                return;
            }
            if (o.QuitAfterMatch)
            {
                int code = _timedOut ? ExitTimeout : _exceptions > 0 ? ExitExceptions : ExitClean;
                Debug.Log($"{LogTag} QUIT exit={code} matches={_match} exceptions={_exceptions} errors={_errors}");
                _quitting = true;
                Application.Quit(code);
            }
        }

        void Finish(string why)
        {
            World w = _driver.World;
            PerfOverlay.Report r = _recording ? PerfOverlay.EndRecording() : null;
            _recording = false;
            PerfOverlay.SetPhase(PerfOverlay.PhaseResults);
            double wall = Time.realtimeSinceStartupAsDouble - _matchStart;
            const double Mb = 1024.0 * 1024.0;
            double allocated = Profiler.GetTotalAllocatedMemoryLong() / Mb;
            double reserved = Profiler.GetTotalReservedMemoryLong() / Mb;
            double mono = Profiler.GetMonoUsedSizeLong() / Mb;
            double gfx = Profiler.GetAllocatedMemoryForGraphicsDriver() / Mb;
            string assets = _match == 1 ? PerfOverlay.AssetReportJson() : "null";
            var extra = new[]
            {
                ("result", Q(why)),
                ("match", _match.ToString(CultureInfo.InvariantCulture)),
                ("matches", Current.Matches.ToString(CultureInfo.InvariantCulture)),
                ("winner", Q(w.Match.Winner.ToString())),
                ("sim_seconds", Num(w.Match.Time)),
                ("ticks", w.Tick.ToString(CultureInfo.InvariantCulture)),
                ("actors", w.ActorCount.ToString(CultureInfo.InvariantCulture)),
                ("wall_seconds", Num(wall)),
                ("boot_to_title_engine_s", _firstTitleAt >= 0 ? Num(_firstTitleAt) : "null"),
                ("boot_to_title_process_s", _firstTitleAt >= 0 && _processStartOffset >= 0 ? Num(_firstTitleAt + _processStartOffset) : "null"),
                ("exceptions", _exceptions.ToString(CultureInfo.InvariantCulture)),
                ("exceptions_during_match", (_exceptions - _exceptionsAtStart).ToString(CultureInfo.InvariantCulture)),
                ("errors", _errors.ToString(CultureInfo.InvariantCulture)),
                ("memory_mb", $"{{\"unity_allocated\": {Num(allocated)}, \"unity_reserved\": {Num(reserved)}, \"mono_used\": {Num(mono)}, \"graphics_driver\": {Num(gfx)}}}"),
                ("version", Q(BuildInfo.Version)),
                ("commit", Q(BuildInfo.Commit)),
                ("development", BuildInfo.Development ? "true" : "false"),
                ("speed", Num(_driver.TimeScale)),
                ("vsync", QualitySettings.vSyncCount.ToString(CultureInfo.InvariantCulture)),
                ("target_frame_rate", Application.targetFrameRate.ToString(CultureInfo.InvariantCulture)),
                ("frame_cap", FrameGovernor.AppliedCap.ToString(CultureInfo.InvariantCulture)),
                ("quality", Q(QualitySettings.names[QualitySettings.GetQualityLevel()])),
                ("quality_requested", Q(FrameGovernor.RequestedQuality)),
                ("pacing", Q(FrameGovernor.PacingOverride ?? "rule")),
                ("pacing_reason", Q(FrameGovernor.PacingReason ?? "")),
                ("window_hidden", AutomationWindow.Active ? "true" : "false"),
                ("stress_hill", Current.StressHill ? "true" : "false"),
                ("render_scale", Num(FrameGovernor.AppliedRenderScale)),
                ("render_pixels", ((long)(Screen.width * FrameGovernor.AppliedRenderScale) * (long)(Screen.height * FrameGovernor.AppliedRenderScale)).ToString(CultureInfo.InvariantCulture)),
                ("max_queued_frames", QualitySettings.maxQueuedFrames.ToString(CultureInfo.InvariantCulture)),
                ("window_mode", Q(Screen.fullScreenMode.ToString())),
                ("displays", Display.displays.Length.ToString(CultureInfo.InvariantCulture)),
                ("data_isolated", GamePaths.Isolated ? "true" : "false"),
                ("safe_mode", FrameGovernor.SafeMode ? "true" : "false"),
                ("previous_unclean", SessionKeeper.PreviousUnclean ? "true" : "false"),
                ("sim_dropped_seconds", Num(RuntimeCounters.SimDroppedSeconds)),
                ("sim_dropped_ticks", RuntimeCounters.SimDroppedTicks.ToString(CultureInfo.InvariantCulture)),
                ("hud_events_dropped", RuntimeCounters.HudEventsDropped.ToString(CultureInfo.InvariantCulture)),
                ("objects", ObjectCountsJson()),
                ("world_triangles", CriticalMass.Runtime.View.Dressing.WorldView.LastTriangles.ToString(CultureInfo.InvariantCulture)),
                ("render_tier", Q(RenderTier.Policy(RenderTier.Level).ToString())),
                ("screen", Q($"{Screen.width}x{Screen.height}")),
                ("assets", assets),
                ("phases", PerfOverlay.PhasesJson()),
            };
            {
                string perfBase = string.IsNullOrEmpty(Current.PerfLog) ? Path.Combine(Application.persistentDataPath, "perf-last.json") : Current.PerfLog;
                string recorder = FrameRecorder.End(FrameRecorder.CsvPathFor(PerfPathFor(perfBase, _match)));
                Array.Resize(ref extra, extra.Length + 1);
                extra[extra.Length - 1] = ("frame_recorder", recorder);
            }
            string json = PerfOverlay.ToJson(r, extra);
            _pendingExtra = extra;
            _pendingReport = r;
            string basePath = Current.PerfLog;
            if (string.IsNullOrEmpty(basePath)) basePath = Path.Combine(Application.persistentDataPath, "perf-last.json");
            string path = PerfPathFor(basePath, _match);
            _pendingPath = path;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                SafeFile.WriteAtomic(path, json, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} could not write {path}: {e.Message}");
            }
            string frames = r == null ? "frames=0" : r.Summary();
            Debug.Log($"{LogTag} {why} match={_match}/{Current.Matches} winner={w.Match.Winner} sim={Num(w.Match.Time)}s ticks={w.Tick} " +
                      $"wall={Num(wall)}s {frames} unity_mb={Num(allocated)} mono_mb={Num(mono)} exceptions={_exceptions} errors={_errors} perf={path}");
        }

        (string key, string jsonValue)[] _pendingExtra;
        PerfOverlay.Report _pendingReport;
        string _pendingPath;

        /// <summary>
        /// DG-111: perf.json is written at the match end (so a crash during results loses nothing) and written again when
        /// the results phase ends, with every phase's p50 and p95 (title, match, results).
        /// </summary>
        void RewriteWithPhases()
        {
            var extra = _pendingExtra;
            _pendingExtra = null;
            PerfOverlay.SetPhase(null);
            for (int i = 0; i < extra.Length; i++)
                if (extra[i].key == "phases") extra[i].jsonValue = PerfOverlay.PhasesJson();
            try
            {
                SafeFile.WriteAtomic(_pendingPath, PerfOverlay.ToJson(_pendingReport, extra), false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} could not rewrite {_pendingPath}: {e.Message}");
            }
        }

        /// <summary>DG-55/58: live object counts at a match end (the soak compares match 2 and match 3).</summary>
        static string ObjectCountsJson()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Format(c, "{{\"meshes\": {0}, \"materials\": {1}, \"textures\": {2}, \"game_objects\": {3}, \"audio_sources\": {4}, \"render_textures\": {5}}}",
                Resources.FindObjectsOfTypeAll<Mesh>().Length, Resources.FindObjectsOfTypeAll<Material>().Length,
                Resources.FindObjectsOfTypeAll<Texture>().Length,
                FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Resources.FindObjectsOfTypeAll<RenderTexture>().Length);
        }

        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

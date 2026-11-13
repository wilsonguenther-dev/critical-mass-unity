using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CriticalMass.Runtime.Hud;
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
            public bool NoVsync;
            public int Matches;
            public double TitleSeconds;
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
                    case "-perflog":
                        if (!string.IsNullOrEmpty(next) && !next.StartsWith("-", StringComparison.Ordinal)) { o.PerfLog = next; i++; }
                        break;
                }
            }
            return o;
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
        static readonly object CrashLock = new object();
        static readonly Dictionary<string, int> Repeats = new Dictionary<string, int>();
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
        bool _titleSeen, _recording, _ended, _quitting, _timedOut;
        double _titleAt = -1, _matchStart, _endAt, _firstTitleAt = -1;
        int _exceptionsAtStart, _match = 1, _reloadFrame = -1;

        void Awake()
        {
            if (!Application.isEditor)
            {
                _crashLogPath = Path.Combine(Application.persistentDataPath, "crash-log.txt");
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
                          $"crash log {_crashLogPath}");
            }
            Options o = Current;
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
            string key = type + "|" + condition + "|" + FirstLine(stack);
            int n;
            lock (CrashLock)
            {
                if (Repeats.Count > 2000) Repeats.Clear();   // messages with numbers in them must not grow this forever
                Repeats.TryGetValue(key, out n);
                Repeats[key] = ++n;
                if (n == RepeatLimit && type == LogType.Exception) ToStop.Enqueue(stack ?? "");
            }
            if (n < RepeatLimit) AppendCrash(type.ToString(), condition, stack);
            else if (n == RepeatLimit) AppendCrash(type.ToString(), condition + $" (repeated {RepeatLimit} times; further repeats are counted, not written)", stack);
        }

        static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Interlocked.Increment(ref _exceptions);
            AppendCrash("Unhandled", e.ExceptionObject?.ToString() ?? "unknown", "");
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            return nl >= 0 ? s.Substring(0, nl) : s;
        }

        static void AppendCrash(string kind, string condition, string stack)
        {
            if (_crashLogPath == null) return;
            try
            {
                lock (CrashLock)
                {
                    var fi = new FileInfo(_crashLogPath);
                    if (fi.Exists && fi.Length > CrashLogMaxBytes)
                    {
                        string old = Path.Combine(fi.DirectoryName ?? "", "crash-log.1.txt");
                        File.Copy(_crashLogPath, old, true);
                        File.Delete(_crashLogPath);
                    }
                    bool fresh = !File.Exists(_crashLogPath);
                    var sb = new StringBuilder();
                    if (fresh) sb.Append("# Critical Mass crash log (DG-05). Newest last; capped at 1 MB (older: crash-log.1.txt).\n");
                    sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
                      .Append(' ').Append(BuildInfo.Label).Append(' ').Append(kind).Append(": ").Append(condition).Append('\n');
                    if (!string.IsNullOrEmpty(stack)) sb.Append(stack.TrimEnd()).Append('\n');
                    File.AppendAllText(_crashLogPath, sb.ToString());
                }
            }
            catch (Exception)
            {
                // never throw from a log handler
            }
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

        static void StopOffender(string stack)
        {
            foreach (string name in StackTypeNames(stack))
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
            if (!o.Autoplay && !o.QuitAfterMatch) return;
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

            if (o.Autoplay)
            {
                _driver.HumanAutopilot = true;
                if (o.Speed > 0) _driver.TimeScale = o.Speed;
                double hold = _match == 1 ? o.TitleSeconds : 1.0;
                if (_title != null && now - _titleAt >= hold)
                {
                    _title.Hide();
                    _title = null;
                }
                if (!_recording && !_ended && _title == null)
                {
                    _recording = true;
                    _exceptionsAtStart = _exceptions;
                    PerfOverlay.BeginRecording();
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

            if (!_timedOut && o.Autoplay && _match < o.Matches)
            {
                // Next match: reload Boot; the old game goes with the scene, GameBootstrap builds a fresh one.
                _match++;
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
            double wall = Time.realtimeSinceStartupAsDouble - _matchStart;
            const double Mb = 1024.0 * 1024.0;
            double allocated = Profiler.GetTotalAllocatedMemoryLong() / Mb;
            double reserved = Profiler.GetTotalReservedMemoryLong() / Mb;
            double mono = Profiler.GetMonoUsedSizeLong() / Mb;
            double gfx = Profiler.GetAllocatedMemoryForGraphicsDriver() / Mb;
            string assets = _match == 1 ? PerfOverlay.AssetReportJson() : "null";
            string json = PerfOverlay.ToJson(r, new[]
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
                ("safe_mode", FrameGovernor.SafeMode ? "true" : "false"),
                ("screen", Q($"{Screen.width}x{Screen.height}")),
                ("assets", assets),
            });
            string basePath = Current.PerfLog;
            if (string.IsNullOrEmpty(basePath)) basePath = Path.Combine(Application.persistentDataPath, "perf-last.json");
            string path = PerfPathFor(basePath, _match);
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

        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-05 command line and crash log. Installed once per run on a DontDestroyOnLoad "CriticalMass Services" object
    /// (with PerfOverlay), so GameBootstrap is untouched; it finds the SimDriverBehaviour whenever one exists and only
    /// writes the driver's public fields (HumanAutopilot, TimeScale). Never writes Sim state.
    ///
    /// Flags (case-insensitive):
    ///   -autoplay               actor 0 plays as a bot; the title is dismissed after 1 s; the match is recorded
    ///   -quitAfterMatch         quit when the match ends: exit 0 clean, 3 when an exception was logged
    ///   -autoplaySpeed X        sim seconds per wall second (default: the game's own, 2)
    ///   -autoplayTimeout S      quit with exit 4 when the match has not ended after S wall seconds (default 900)
    ///   -perfLog PATH           write the match's frame-time report as JSON (default persistentDataPath/perf-last.json)
    ///   -novsync                vSyncCount 0 and no frame cap, to measure the real frame cost
    /// In player builds every exception and error is also appended to persistentDataPath/crash-log.txt with the build
    /// stamp; Unity's own Player.log (Application.consoleLogPath) carries everything else.
    /// </summary>
    public sealed class AutoplayArgs : MonoBehaviour
    {
        public const int ExitClean = 0;
        public const int ExitExceptions = 3;
        public const int ExitTimeout = 4;
        public const string LogTag = "[CM.Autoplay]";

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
        }

        /// <summary>Pure parser (tests). Unknown flags are ignored, so Unity's own (-screen-width, -logFile) pass through.</summary>
        public static Options Parse(string[] args)
        {
            var o = new Options { TimeoutSec = 900 };
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
                    case "-perflog":
                        if (!string.IsNullOrEmpty(next) && !next.StartsWith("-", StringComparison.Ordinal)) { o.PerfLog = next; i++; }
                        break;
                }
            }
            return o;
        }

        static bool TryNum(string s, out double v) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>The options this run was started with.</summary>
        public static Options Current { get; private set; }

        /// <summary>Exceptions and errors logged since boot (any thread).</summary>
        public static int ExceptionCount => _exceptions;
        public static int ErrorCount => _errors;

        static int _exceptions, _errors;
        static GameObject _host;
        static string _crashLogPath;
        static readonly object CrashLock = new object();
        static double _processStartOffset = -1;   // seconds between process start and engine start, when the OS says

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_host != null) return;
            Current = Parse(Environment.GetCommandLineArgs());
            _host = new GameObject("CriticalMass Services");
            DontDestroyOnLoad(_host);
            _host.AddComponent<PerfOverlay>();
            _host.AddComponent<AutoplayArgs>();
        }

        // ── autoplay state ──
        SimDriverBehaviour _driver;
        TitleScreen _title;
        bool _titleSeen, _recording, _ended;
        double _titleAt = -1, _autoplayStart, _endAt;
        int _exceptionsAtStart;

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
                if (o.NoVsync)
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = -1;
                }
                _autoplayStart = Time.realtimeSinceStartupAsDouble;
                Debug.Log($"{LogTag} START {BuildInfo.Label} speed={(o.Speed > 0 ? o.Speed.ToString(CultureInfo.InvariantCulture) : "default")} " +
                          $"quitAfterMatch={o.QuitAfterMatch} novsync={o.NoVsync} screen={Screen.width}x{Screen.height}");
            }
        }

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception) Interlocked.Increment(ref _exceptions);
            else if (type == LogType.Error || type == LogType.Assert) Interlocked.Increment(ref _errors);
            else return;
            AppendCrash(type.ToString(), condition, stack);
        }

        static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Interlocked.Increment(ref _exceptions);
            AppendCrash("Unhandled", e.ExceptionObject?.ToString() ?? "unknown", "");
        }

        static void AppendCrash(string kind, string condition, string stack)
        {
            if (_crashLogPath == null) return;
            try
            {
                lock (CrashLock)
                {
                    bool fresh = !File.Exists(_crashLogPath);
                    var sb = new StringBuilder();
                    if (fresh) sb.Append("# Critical Mass crash log (DG-05). Newest last.\n");
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

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_titleSeen)
            {
                _title = FindAnyObjectByType<TitleScreen>();
                if (_title != null)
                {
                    _titleSeen = true;
                    _titleAt = now;
                    if (!Application.isEditor)
                    {
                        string fromProcess = _processStartOffset >= 0
                            ? (now + _processStartOffset).ToString("F2", CultureInfo.InvariantCulture) : "unknown";
                        Debug.Log($"[CM.Boot] title on screen {now.ToString("F2", CultureInfo.InvariantCulture)} s after engine start, " +
                                  $"{fromProcess} s after process start, utc_ms={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
                    }
                }
            }

            Options o = Current;
            if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) return;
            if (!o.Autoplay && !o.QuitAfterMatch) return;

            if (o.Autoplay)
            {
                _driver.HumanAutopilot = true;
                if (o.Speed > 0) _driver.TimeScale = o.Speed;
                if (_title != null && now - _titleAt >= 1.0)
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
            else if (!_ended && o.Autoplay && now - _autoplayStart > o.TimeoutSec)
            {
                _ended = true;
                _endAt = now;
                Finish("TIMEOUT");
            }

            if (_ended && o.QuitAfterMatch && now - _endAt >= 1.5)
            {
                int code = _exceptions > 0 ? ExitExceptions : ExitClean;
                if (_driver.World.Match.Phase != MatchPhase.Ended) code = ExitTimeout;
                Debug.Log($"{LogTag} QUIT exit={code}");
                Application.Quit(code);
                enabled = false;
            }
        }

        void Finish(string why)
        {
            World w = _driver.World;
            PerfOverlay.Report r = _recording ? PerfOverlay.EndRecording() : null;
            _recording = false;
            double wall = Time.realtimeSinceStartupAsDouble - _autoplayStart;
            string json = PerfOverlay.ToJson(r, new[]
            {
                ("result", Q(why)),
                ("winner", Q(w.Match.Winner.ToString())),
                ("sim_seconds", Num(w.Match.Time)),
                ("ticks", w.Tick.ToString(CultureInfo.InvariantCulture)),
                ("actors", w.ActorCount.ToString(CultureInfo.InvariantCulture)),
                ("wall_seconds", Num(wall)),
                ("boot_to_title_engine_s", _titleAt >= 0 ? Num(_titleAt) : "null"),
                ("boot_to_title_process_s", _titleAt >= 0 && _processStartOffset >= 0 ? Num(_titleAt + _processStartOffset) : "null"),
                ("exceptions", _exceptions.ToString(CultureInfo.InvariantCulture)),
                ("exceptions_during_match", (_exceptions - _exceptionsAtStart).ToString(CultureInfo.InvariantCulture)),
                ("errors", _errors.ToString(CultureInfo.InvariantCulture)),
                ("version", Q(BuildInfo.Version)),
                ("commit", Q(BuildInfo.Commit)),
                ("development", BuildInfo.Development ? "true" : "false"),
                ("speed", Num(_driver.TimeScale)),
                ("vsync", QualitySettings.vSyncCount.ToString(CultureInfo.InvariantCulture)),
                ("screen", Q($"{Screen.width}x{Screen.height}")),
            });
            string path = Current.PerfLog;
            if (string.IsNullOrEmpty(path)) path = Path.Combine(Application.persistentDataPath, "perf-last.json");
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} could not write {path}: {e.Message}");
            }
            string frames = r == null ? "frames=0" : r.Summary();
            Debug.Log($"{LogTag} {why} winner={w.Match.Winner} sim={Num(w.Match.Time)}s ticks={w.Tick} wall={Num(wall)}s {frames} " +
                      $"exceptions={_exceptions} errors={_errors} perf={path}");
        }

        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

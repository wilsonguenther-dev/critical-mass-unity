using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-48 crash, hang and log keeping; DG-52 one copy only; DG-62 the safe-mode decision. Player builds only (the
    /// editor and its tests never take the lock, write the marker or start the watchdog). FrameGovernor calls
    /// EnsureStarted first thing and Tick every frame.
    /// <para>Boot: take an exclusive lock on DATA/instance.lock (an fcntl/LockFile lock the OS drops when the process
    /// dies); a second copy finds it held and quits (Windows also has forceSingleInstance). Because a live session always
    /// holds the lock, a session marker found while we hold it belongs to a dead process: that is an unclean end.</para>
    /// <para>Logs: Unity's previous Player log (Player-prev.log next to Application.consoleLogPath) is copied to
    /// DATA/logs/session-UTC.log, the last 10 kept; after an unclean end the newest OS crash report of the game is copied
    /// too (macOS ~/Library/Logs/DiagnosticReports/Critical Mass*.ips; Windows %TEMP%/Drivia/Critical Mass/Crashes).</para>
    /// <para>Heartbeat: every 5 s a ~300-byte DATA/heartbeat.txt (atomic) with build, frame p50/p95, CPU and GPU frame time,
    /// memory, render size, window mode, displays, focus, battery and quality.</para>
    /// <para>Hang watchdog: a background thread watches the frame counter. 5 s without a frame writes
    /// DATA/hang-UTC.txt (last 300 frame times, match tick, phase); 60 s writes a final record and calls
    /// Environment.FailFast so the OS writes a crash report and the next launch knows. Only frameless time in which frames
    /// were expected counts (HangWatchdogRule): paused, screen locked, display asleep, minimised, a debugger, declared
    /// LongWork, a machine sleep (the 0.5 s nap taking over 3 s by either clock) or a main thread that still answers the
    /// watchdog's run-loop probe hold the clock at zero, and it stays at zero for 15 s after the excuse ends. Tool runs
    /// (-exportReport, -simHashOut, -simScout) are never watched; the final record is also written to DATA/logs/.</para>
    /// </summary>
    public static class SessionKeeper
    {
        public const string MarkerFile = "session.running";
        public const string FlagFile = "session.flag";
        public const string HistoryFile = "session-history.txt";
        public const string HeartbeatFile = "heartbeat.txt";
        public const string LockFile = "instance.lock";
        public const int HistoryVersion = 1;
        public const int KeepSessionLogs = 10;
        public const double HangSeconds = HangWatchdogRule.HangSeconds;
        public const double KillSeconds = HangWatchdogRule.KillSeconds;
        public const double HeartbeatSeconds = 5;
        public const int RingFrames = 300;
        public const long MaxCopiedReportBytes = 4 * 1024 * 1024;

        public static bool Started { get; private set; }
        public static bool SecondInstance { get; private set; }
        public static bool PreviousUnclean { get; private set; }
        public static double PreviousRunSeconds { get; private set; } = -1;
        public static int UncleanStreak { get; private set; }
        public static bool SafeMode { get; private set; }
        public static string SafeReason { get; private set; } = "";

        /// <summary>A tool run (-exportReport, -simHashOut, -batchmode report): no lock, no marker, no watchdog.</summary>
        public static bool ToolRun { get; private set; }

        /// <summary>Main thread writes, the watchdog reads: the match tick and phase for the hang record.</summary>
        public static volatile int MatchTick;
        public static volatile string Phase = "boot";

        static FileStream _lock;
        static string _dir;
        static Thread _watchdog;
        static volatile bool _stopWatchdog;
        static int _frameCounter;
        static readonly float[] Ring = new float[RingFrames];
        static readonly float[] Sorted = new float[RingFrames];
        static int _ringHead, _ringCount;
        static float _heartbeatAge;
        static int _ignoreLongFramesUntil;
        static volatile string _pendingHeartbeat;
        static readonly FrameTiming[] Timings = new FrameTiming[1];
        static DateTime _startedUtc;
        static bool _diag;

        /// <summary>The latest CPU and GPU frame times from FrameTimingManager (ms; 0 when the platform has none).</summary>
        public static float CpuFrameMs { get; private set; }
        public static float GpuFrameMs { get; private set; }
        public static float CpuMainMs { get; private set; }
        /// <summary>perf-smooth-1: the main thread's wait for present and the render thread's time (ms; 0 when none).</summary>
        public static float CpuPresentWaitMs { get; private set; }
        public static float CpuRenderMs { get; private set; }

        /// <summary>Boot work (idempotent, main thread). Safe to call in the editor: it does nothing there.</summary>
        public static void EnsureStarted()
        {
            if (Started) return;
            Started = true;
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            // -simScout steps whole matches inside one Start (minutes, zero frames): a tool run, never watched (the
            // 2026-11-22 23:23 abort was a scout batch killed at 60 s; see HangWatchdogRule).
            ToolRun = GamePaths.HasFlag(args, "-exportReport") || GamePaths.HasFlag(args, "-simHashOut") ||
                      GamePaths.HasFlag(args, MatchVideo.ScoutFlag);
            _diag = GamePaths.HasFlag(args, "-diag");
            _dir = GamePaths.DataDir;
            _logsDir = GamePaths.LogsDir;
            _focused = Application.isFocused;
            _runInBackground = Application.runInBackground;
            _noFramesTestSeconds = ArgSeconds(args, "-cmNoFramesTest");
            _hangTestSeconds = ArgSeconds(args, "-cmHangTest");
            _startedUtc = DateTime.UtcNow;
            if (GamePaths.Translocated)
                Debug.LogWarning("[CM.Session] running from a translocated (quarantined) copy; move Critical Mass to Applications to keep it stable");
            if (ToolRun) return;

            if (!TakeInstanceLock())
            {
                SecondInstance = true;
                Debug.LogWarning("[CM.Session] another copy of Critical Mass is already running; this one exits");
                return;
            }
            SafeFile.CleanStale(_dir);
            ReadPrevious(args);
            ArchiveLogs();
            WriteMarker();
            SceneManager.sceneLoaded += (s, m) => _ignoreLongFramesUntil = Time.frameCount + 5;
            Application.quitting += OnQuitting;
            StartWatchdog();
            Debug.Log($"[CM.Session] data={_dir} previous={(PreviousUnclean ? "ended unexpectedly" : "clean")} " +
                      $"ran={PreviousRunSeconds:0}s streak={UncleanStreak} safe={SafeMode}{(SafeMode ? " (" + SafeReason + ")" : "")}");
        }

        // ── one copy only ──

        static bool TakeInstanceLock()
        {
            try
            {
                _lock = new FileStream(Path.Combine(_dir, LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                _lock.Lock(0, 1);   // fcntl record lock on macOS, LockFile on Windows; released by the OS when we die
                return true;
            }
            catch (IOException)
            {
                try { _lock?.Dispose(); } catch (Exception) { }
                _lock = null;
                return false;
            }
            catch (Exception e)
            {
                // FALLBACK: a file system without locks must not stop the game; run without the guard.
                Debug.LogWarning($"[CM.Session] no instance lock ({e.GetType().Name}); running without the one-copy guard");
                return true;
            }
        }

        // ── previous session ──

        static void ReadPrevious(string[] args)
        {
            string marker = Path.Combine(_dir, MarkerFile);
            string flag = Path.Combine(_dir, FlagFile);
            bool unclean = File.Exists(marker);
            bool flagged = File.Exists(flag);
            double ran = -1;
            if (unclean)
            {
                try
                {
                    var m = BuildInfo.Parse(File.ReadAllText(marker));
                    DateTime start = m.TryGetValue("started_utc", out string s) ? DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : File.GetCreationTimeUtc(marker);
                    DateTime last = File.Exists(Path.Combine(_dir, HeartbeatFile)) ? File.GetLastWriteTimeUtc(Path.Combine(_dir, HeartbeatFile)) : File.GetLastWriteTimeUtc(marker);
                    if (last < start) last = start;
                    ran = (last - start).TotalSeconds;
                }
                catch (Exception) { ran = -1; }
            }
            else if (flagged)
            {
                ran = 600;   // a clean quit after a GPU stall: unclean, but not a startup crash
            }
            PreviousUnclean = unclean || flagged;
            PreviousRunSeconds = ran;
            try { if (flagged) File.Delete(flag); } catch (Exception) { }

            string histPath = Path.Combine(_dir, HistoryFile);
            var h = BuildInfo.Parse(SafeFile.ReadVersioned(histPath, HistoryVersion, out _));
            int streak = h.TryGetValue("unclean_streak", out string st) && int.TryParse(st, out int sv) ? sv : 0;
            UncleanStreak = PreviousUnclean ? streak + 1 : 0;
            (SafeMode, SafeReason) = FramePolicy.SafeModeDecision(GamePaths.HasFlag(args, "-safe"), PreviousUnclean, ran, UncleanStreak);
            SafeFile.TryWriteAtomic(histPath, SafeFile.Versioned(HistoryVersion,
                "unclean_streak=" + UncleanStreak + "\nlast_start_utc=" + _startedUtc.ToString("o", CultureInfo.InvariantCulture) +
                "\nlast_previous_run_s=" + ran.ToString("0", CultureInfo.InvariantCulture) + "\n"), false, out _);
            if (PreviousUnclean)
                Debug.LogWarning($"[CM.Session] the previous session ended unexpectedly (ran about {ran:0} s; last heartbeat kept in logs/)");
        }

        static void WriteMarker()
        {
            int pid = 0;
            try { using (Process p = Process.GetCurrentProcess()) pid = p.Id; } catch (Exception) { }
            SafeFile.TryWriteAtomic(Path.Combine(_dir, MarkerFile),
                "started_utc=" + _startedUtc.ToString("o", CultureInfo.InvariantCulture) + "\npid=" + pid + "\nversion=" + BuildInfo.Label + "\n",
                false, out _);
        }

        /// <summary>Marks this session unclean even if it quits cleanly (a frame over 1.5 s, a lost graphics device).</summary>
        public static void Flag(string reason)
        {
            if (_dir == null || ToolRun) return;
            try
            {
                string p = Path.Combine(_dir, FlagFile);
                if (!File.Exists(p)) File.WriteAllText(p, reason + " " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\n");
            }
            catch (Exception) { }
        }

        static void OnQuitting()
        {
            _stopWatchdog = true;
            try
            {
                if (_dir != null) File.Delete(Path.Combine(_dir, MarkerFile));
            }
            catch (Exception) { }
            try { _lock?.Dispose(); } catch (Exception) { }
        }

        // ── log keeping ──

        /// <summary>The Player-prev.log Unity keeps next to the current log, or null (UNVERIFIED on 6000.3: falls back to nothing).</summary>
        static string PreviousPlayerLog()
        {
            try
            {
                string cur = Application.consoleLogPath;
                if (string.IsNullOrEmpty(cur)) return null;
                string prev = Path.Combine(Path.GetDirectoryName(cur) ?? "", "Player-prev.log");
                return File.Exists(prev) ? prev : null;
            }
            catch (Exception) { return null; }
        }

        static void ArchiveLogs()
        {
            try
            {
                string logs = GamePaths.LogsDir;
                Directory.CreateDirectory(logs);
                string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
                string prev = PreviousPlayerLog();
                if (prev != null) CopyCapped(prev, Path.Combine(logs, "session-" + stamp + ".log"), 5L * 1024 * 1024);
                string hb = Path.Combine(_dir, HeartbeatFile);
                if (PreviousUnclean && File.Exists(hb)) File.Copy(hb, Path.Combine(logs, "heartbeat-" + stamp + ".txt"), true);
                if (PreviousUnclean)
                {
                    string report = NewestOsCrashReport(_startedUtc.AddDays(-2));
                    if (report != null) CopyCapped(report, Path.Combine(logs, "os-crash-" + stamp + Path.GetExtension(report)), MaxCopiedReportBytes);
                }
                Prune(logs, "session-*.log", KeepSessionLogs);
                Prune(logs, "heartbeat-*.txt", KeepSessionLogs);
                Prune(logs, "os-crash-*", 5);
                Prune(_dir, "hang-*.txt", 10);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CM.Session] log keeping skipped: {e.GetType().Name}");
            }
        }

        static string NewestOsCrashReport(DateTime since)
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                var candidates = new List<string>();
                if (Application.platform == RuntimePlatform.OSXPlayer)
                {
                    string d = Path.Combine(home, "Library/Logs/DiagnosticReports");
                    if (Directory.Exists(d)) candidates.AddRange(Directory.GetFiles(d, "Critical Mass*"));
                }
                else if (Application.platform == RuntimePlatform.WindowsPlayer)
                {
                    string d = Path.Combine(Path.GetTempPath(), Application.companyName, Application.productName, "Crashes");
                    if (Directory.Exists(d))
                        foreach (string sub in Directory.GetDirectories(d))
                            foreach (string f in Directory.GetFiles(sub))
                                if (f.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                    candidates.Add(f);
                }
                string best = null;
                DateTime bestT = since;
                foreach (string c in candidates)
                {
                    DateTime t = File.GetLastWriteTimeUtc(c);
                    if (t > bestT) { bestT = t; best = c; }
                }
                return best;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Copies at most maxBytes (the tail when the file is larger); the source may be open for writing.</summary>
        public static void CopyCapped(string src, string dst, long maxBytes)
        {
            using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var o = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (i.Length > maxBytes) i.Seek(-maxBytes, SeekOrigin.End);
                i.CopyTo(o);
            }
        }

        /// <summary>Deletes the oldest files matching pattern past keep (by write time).</summary>
        public static void Prune(string dir, string pattern, int keep)
        {
            if (!Directory.Exists(dir)) return;
            string[] files = Directory.GetFiles(dir, pattern);
            if (files.Length <= keep) return;
            Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            for (int i = keep; i < files.Length; i++)
            {
                try { File.Delete(files[i]); } catch (Exception) { }
            }
        }

        // ── per frame (main thread) ──

        /// <summary>Called by FrameGovernor.Update: frame counter, frame-time ring, frame timings, long-frame flag, heartbeat.</summary>
        public static void Tick(float unscaledDt)
        {
            if (!WatchdogTestSkipsFrame()) Interlocked.Increment(ref _frameCounter);
            float ms = unscaledDt * 1000f;
            Ring[_ringHead] = ms;
            _ringHead = (_ringHead + 1) % RingFrames;
            if (_ringCount < RingFrames) _ringCount++;

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, Timings) > 0)
            {
                CpuFrameMs = (float)Timings[0].cpuFrameTime;
                GpuFrameMs = (float)Timings[0].gpuFrameTime;
                CpuMainMs = (float)Timings[0].cpuMainThreadFrameTime;
                CpuPresentWaitMs = (float)Timings[0].cpuMainThreadPresentWaitTime;
                CpuRenderMs = (float)Timings[0].cpuRenderThreadFrameTime;
            }

            if (_dir == null || ToolRun || SecondInstance) return;
            if (unscaledDt > FramePolicy.GpuStallSeconds && Time.frameCount > _ignoreLongFramesUntil && Time.frameCount > 30 && Phase == "match")
            {
                Flag("long-frame " + ms.ToString("0", CultureInfo.InvariantCulture) + "ms");
                Debug.LogWarning($"[CM.Session] a frame took {ms:0} ms (over {FramePolicy.GpuStallSeconds * 1000:0}); this session counts as unclean");
            }
            _heartbeatAge += unscaledDt;
            if (_heartbeatAge >= (_diag ? 1.0 : HeartbeatSeconds))
            {
                _heartbeatAge = 0f;
                _pendingHeartbeat = HeartbeatText();   // one small string every 5 s; the watchdog thread writes it
            }
        }

        /// <summary>p50 and p95 of the frame-time ring (no allocation).</summary>
        public static (float p50, float p95) RingPercentiles()
        {
            int n = _ringCount;
            if (n == 0) return (0, 0);
            Array.Copy(Ring, Sorted, n);
            Array.Sort(Sorted, 0, n);
            return (Sorted[Math.Min(n - 1, (int)Math.Ceiling(0.5 * n) - 1)], Sorted[Math.Min(n - 1, (int)Math.Ceiling(0.95 * n) - 1)]);
        }

        static string HeartbeatText()
        {
            (float p50, float p95) = RingPercentiles();
            long rssMb = -1;
            try { using (Process p = Process.GetCurrentProcess()) rssMb = p.WorkingSet64 / (1024 * 1024); } catch (Exception) { }
            long gfxMb = Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024);
            int q = QualitySettings.GetQualityLevel();
            var c = CultureInfo.InvariantCulture;
            return string.Format(c,
                "utc={0:o}\nbuild={1}\nup_s={2:0}\nframe_p50_ms={3:0.0}\nframe_p95_ms={4:0.0}\ncpu_ms={5:0.0}\ngpu_ms={6:0.0}\nrss_mb={7}\ngfx_mb={8}\n" +
                "render={9}x{10}@{11:0.00}\nmode={12}\ndisplays={13}\nfocus={14}\nbattery={15}\nquality={16}\nsafe={17}\ntick={18}\nphase={19}\n",
                DateTime.UtcNow, BuildInfo.Label, (DateTime.UtcNow - _startedUtc).TotalSeconds, p50, p95, CpuFrameMs, GpuFrameMs, rssMb, gfxMb,
                Screen.width, Screen.height, FrameGovernor.AppliedRenderScale, Screen.fullScreenMode, Display.displays.Length,
                Application.isFocused ? 1 : 0, SystemInfo.batteryStatus, q >= 0 && q < QualitySettings.names.Length ? QualitySettings.names[q] : "?",
                SafeMode ? 1 : 0, MatchTick, Phase);
        }

        // ── hang watchdog (background thread) ──

        static void StartWatchdog()
        {
            _stopWatchdog = false;
            _watchdog = new Thread(Watch) { IsBackground = true, Name = "CM hang watchdog" };
            _watchdog.Start();
        }

        static void Watch()
        {
            var clock = Stopwatch.StartNew();
            DateTime lastUtc = DateTime.UtcNow;
            double lastLoop = 0;
            var state = HangWatchdogRule.Start(0, Volatile.Read(ref _frameCounter));
            WatchdogVerdict was = WatchdogVerdict.Ok;
            while (!_stopWatchdog)
            {
                Thread.Sleep(500);
                double now = clock.Elapsed.TotalSeconds;
                DateTime utc = DateTime.UtcNow;
                double gap = Math.Max(now - lastLoop, (utc - lastUtc).TotalSeconds);
                lastLoop = now;
                lastUtc = utc;
                string hb = _pendingHeartbeat;
                if (hb != null)
                {
                    _pendingHeartbeat = null;
                    SafeFile.TryWriteAtomic(Path.Combine(_dir, HeartbeatFile), hb, false, out _);
                }
                int f = Volatile.Read(ref _frameCounter);
                bool pumped = WatchdogNative.MainThreadAnswered(now);
                // The excuses are only gathered when no frame came since the last look (the native queries cost a little).
                NoFrameExcuse why = f != state.SeenFrames ? NoFrameExcuse.None : Excuses(pumped);
                var sample = new WatchdogSample { Now = now, LoopGap = gap, Frames = f, Excuses = why };
                WatchdogVerdict v = HangWatchdogRule.Step(ref state, sample);
                LogTransition(was, v, state, now);
                was = v;
                if (v == WatchdogVerdict.HangRecord) WriteHang(state.Stalled, false, why, now);
                else if (v == WatchdogVerdict.Kill)
                {
                    WriteHang(state.Stalled, true, why, now);
                    Flag("hang");
                    Environment.FailFast("Critical Mass: the main thread did not finish a frame for " + KillSeconds +
                                         " s and did not answer the watchdog's probe (a real hang; hang record in " + (_logsDir ?? _dir) + ")");
                }
            }
        }

        /// <summary>What currently excuses missing frames (watchdog thread; every native query guarded inside WatchdogNative).</summary>
        static NoFrameExcuse Excuses(bool mainThreadPumped)
        {
            NoFrameExcuse e = NoFrameExcuse.None;
            if (mainThreadPumped) e |= NoFrameExcuse.MainThreadPumping;
            if (_paused || (!_focused && !_runInBackground)) e |= NoFrameExcuse.Paused;
            if (Volatile.Read(ref _longWork) > 0) e |= NoFrameExcuse.LongWork;
            try { if (Debugger.IsAttached) e |= NoFrameExcuse.Debugger; } catch (Exception) { }
            e |= WatchdogNative.SystemExcuses();
            return e;
        }

        static double _excusedSince = -1;

        /// <summary>One Player.log line when frames stop being expected and when the watchdog arms again (never per look).</summary>
        static void LogTransition(WatchdogVerdict was, WatchdogVerdict v, WatchdogState s, double now)
        {
            if (v == WatchdogVerdict.Excused && was != WatchdogVerdict.Excused)
            {
                _excusedSince = now;
                Debug.Log($"[CM.Session] watchdog: no frames, none expected ({HangWatchdogRule.Describe(s.LastExcuses)}); not counting");
            }
            else if (v == WatchdogVerdict.Grace && was == WatchdogVerdict.Excused)
                Debug.Log($"[CM.Session] watchdog: frames expected again after {now - _excusedSince:0} s (last excuse {HangWatchdogRule.Describe(s.LastExcuses)}); " +
                          $"armed again in {HangWatchdogRule.GraceSeconds:0} s");
        }

        // ── what the watchdog is told by the main thread ──

        static volatile bool _paused, _focused = true, _runInBackground = true;
        static int _longWork;
        static volatile string _longWorkWhat = "";
        static string _logsDir;

        /// <summary>FrameGovernor.OnApplicationFocus (main thread).</summary>
        public static void NoteFocus(bool focused)
        {
            _focused = focused;
            _runInBackground = Application.runInBackground;
        }

        /// <summary>FrameGovernor.OnApplicationPause (main thread).</summary>
        public static void NotePause(bool paused)
        {
            _paused = paused;
            _runInBackground = Application.runInBackground;
        }

        /// <summary>Ends a LongWork declaration (dispose once).</summary>
        public readonly struct LongWorkScope : IDisposable
        {
            public void Dispose() => Interlocked.Decrement(ref _longWork);
        }

        /// <summary>Declares a long synchronous job on the main thread (a blocking load, a batch of sim matches): the watchdog
        /// does not count the frameless time while any is open. <c>using (SessionKeeper.LongWork("what")) { ... }</c></summary>
        public static LongWorkScope LongWork(string what)
        {
            _longWorkWhat = what ?? "";
            Interlocked.Increment(ref _longWork);
            return default;
        }

        // ── reviewer proofs (player builds; inert unless the flag is given) ──

        /// <summary>Seconds after start before a test flag acts (the title is on screen by then).</summary>
        const double TestStartsAfter = 20;
        static double _noFramesTestSeconds, _hangTestSeconds, _noFramesUntil = -1;

        static double ArgSeconds(string[] args, string flag)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0)
                    return Math.Min(v, 3600);
            return 0;
        }

        /// <summary>
        /// -cmNoFramesTest S: from 20 s after start, the frame counter stops for S seconds while the game keeps running and
        /// pumping (what a locked screen or an occluded window looks like to the watchdog); the game must not abort.
        /// -cmHangTest S: at 20 s the main thread sleeps S seconds inside a frame (a real hang, nothing answers the probe):
        /// a hang record at 5 s and, for S over 60, the final record and the abort.
        /// </summary>
        static bool WatchdogTestSkipsFrame()
        {
            if (_noFramesTestSeconds <= 0 && _hangTestSeconds <= 0 && _noFramesUntil < 0) return false;
            double t = Time.realtimeSinceStartupAsDouble;
            if (t < TestStartsAfter) return false;
            if (_hangTestSeconds > 0)
            {
                double h = _hangTestSeconds;
                _hangTestSeconds = 0;
                Debug.LogWarning($"[CM.Session] -cmHangTest: the main thread now sleeps {h:0} s (expect a hang record at 5 s, the abort at 60 s)");
                Thread.Sleep((int)(h * 1000));
                Debug.Log("[CM.Session] -cmHangTest: the main thread is back");
            }
            if (_noFramesTestSeconds > 0)
            {
                _noFramesUntil = t + _noFramesTestSeconds;
                Debug.LogWarning($"[CM.Session] -cmNoFramesTest: the frame counter stops for {_noFramesTestSeconds:0} s while the game keeps pumping (expect no abort)");
                _noFramesTestSeconds = 0;
            }
            if (_noFramesUntil < 0) return false;
            if (t < _noFramesUntil) return true;
            _noFramesUntil = -1;
            Debug.Log("[CM.Session] -cmNoFramesTest: the frame counter runs again");
            return false;
        }

        /// <summary>
        /// The watchdog's OS queries, without a native plugin (P/Invoke into system libraries, like AutomationWindow). Every
        /// query is guarded: the first failure turns that query off for the run and the watchdog falls back to the rest.
        /// <para>macOS: the probe is dispatch_async_f onto the main dispatch queue (AppKit's main run loop drains it, Unity's
        /// player loop does not need to run); a stuck main thread never runs it. Screen lock from CGSessionCopyCurrentDictionary
        /// (kCGSSessionScreenIsLocked), display sleep from CGDisplayIsAsleep.</para>
        /// <para>Windows: the probe is SendMessageTimeout(WM_NULL, SMTO_ABORTIFHUNG) to the game window (answered only by a
        /// thread pumping messages); lock or switch-user from OpenInputDesktop failing; minimised or hidden from IsIconic /
        /// IsWindowVisible.</para>
        /// </summary>
        static class WatchdogNative
        {
            static bool _probeOff, _lockOff, _sleepOff, _windowOff;
            static int _answers, _inFlight;
            static int _lastAnswers;
            public static long Posted;
            public static double LastAnswerAt = -1;

            static void Off(ref bool off, string what, Exception e)
            {
                off = true;
                Debug.LogWarning($"[CM.Session] watchdog: {what} unavailable ({e.GetType().Name}); using the other signals");
            }

            /// <summary>true when the main thread answered a probe since the last look; posts the next probe.</summary>
            public static bool MainThreadAnswered(double now)
            {
                int a = Volatile.Read(ref _answers);
                bool answered = a != _lastAnswers;
                _lastAnswers = a;
                if (answered) LastAnswerAt = now;
                if (!_probeOff)
                {
                    try { Post(); }
                    catch (Exception e) { Off(ref _probeOff, "the main-thread probe", e); }
                }
                return answered;
            }

            public static string Describe(double now) =>
                $"probe={(_probeOff ? "off" : "on")} posted={Posted} answered={Volatile.Read(ref _answers)} in_flight={Volatile.Read(ref _inFlight)} " +
                $"last_answer_s_ago={(LastAnswerAt < 0 ? "never" : (now - LastAnswerAt).ToString("0.0", CultureInfo.InvariantCulture))} " +
                $"lock_query={(_lockOff ? "off" : "on")} sleep_query={(_sleepOff ? "off" : "on")} window_query={(_windowOff ? "off" : "on")}";

            public static NoFrameExcuse SystemExcuses()
            {
                NoFrameExcuse e = NoFrameExcuse.None;
                if (!_lockOff)
                {
                    try { if (ScreenLocked()) e |= NoFrameExcuse.ScreenLocked; }
                    catch (Exception x) { Off(ref _lockOff, "the screen-lock query", x); }
                }
                if (!_sleepOff)
                {
                    try { if (DisplayAsleep()) e |= NoFrameExcuse.DisplayAsleep; }
                    catch (Exception x) { Off(ref _sleepOff, "the display-sleep query", x); }
                }
                if (!_windowOff)
                {
                    try { if (Minimised()) e |= NoFrameExcuse.Minimised; }
                    catch (Exception x) { Off(ref _windowOff, "the window query", x); }
                }
                return e;
            }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
            const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
            const string LibSystem = "/usr/lib/libSystem.B.dylib";
            const uint Utf8 = 0x08000100;   // kCFStringEncodingUTF8
            static readonly IntPtr RtldDefault = new IntPtr(-2);

            [DllImport(CG)] static extern IntPtr CGSessionCopyCurrentDictionary();
            [DllImport(CG)] static extern uint CGMainDisplayID();
            [DllImport(CG)] static extern int CGDisplayIsAsleep(uint display);
            [DllImport(CF)] static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
            [DllImport(CF)] static extern IntPtr CFStringCreateWithCString(IntPtr alloc, [MarshalAs(UnmanagedType.LPStr)] string s, uint encoding);
            [DllImport(CF)] static extern byte CFBooleanGetValue(IntPtr b);
            [DllImport(CF)] static extern UIntPtr CFGetTypeID(IntPtr cf);
            [DllImport(CF)] static extern UIntPtr CFBooleanGetTypeID();
            [DllImport(CF)] static extern void CFRelease(IntPtr cf);
            [DllImport(LibSystem)] static extern IntPtr dlsym(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string name);
            [DllImport(LibSystem)] static extern void dispatch_async_f(IntPtr queue, IntPtr context, IntPtr work);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DispatchWork(IntPtr context);
            static readonly DispatchWork Work = OnMainThread;   // kept alive for the process lifetime
            static IntPtr _workPtr, _mainQueue, _lockedKey;

            [AOT.MonoPInvokeCallback(typeof(DispatchWork))]
            static void OnMainThread(IntPtr context)
            {
                Interlocked.Increment(ref _answers);
                Volatile.Write(ref _inFlight, 0);
            }

            static void Post()
            {
                if (_mainQueue == IntPtr.Zero)
                {
                    _mainQueue = dlsym(RtldDefault, "_dispatch_main_q");
                    if (_mainQueue == IntPtr.Zero) throw new EntryPointNotFoundException("_dispatch_main_q");
                    _workPtr = Marshal.GetFunctionPointerForDelegate(Work);
                }
                if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;   // one probe queued at a time
                Posted++;
                dispatch_async_f(_mainQueue, IntPtr.Zero, _workPtr);
            }

            static bool ScreenLocked()
            {
                IntPtr d = CGSessionCopyCurrentDictionary();
                if (d == IntPtr.Zero) return true;   // no window-server session for this process right now (switched-out user)
                try
                {
                    if (_lockedKey == IntPtr.Zero) _lockedKey = CFStringCreateWithCString(IntPtr.Zero, "CGSSessionScreenIsLocked", Utf8);
                    IntPtr v = CFDictionaryGetValue(d, _lockedKey);
                    return v != IntPtr.Zero && CFGetTypeID(v) == CFBooleanGetTypeID() && CFBooleanGetValue(v) != 0;
                }
                finally { CFRelease(d); }
            }

            static bool DisplayAsleep() => CGDisplayIsAsleep(CGMainDisplayID()) != 0;

            static bool Minimised() => false;   // AppKit window state is main-thread only; the probe covers minimised/occluded
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
            const string U32 = "user32.dll";
            const uint WmNull = 0x0000, SmtoAbortIfHung = 0x0002, SmtoErrorOnExit = 0x0020, DesktopSwitchDesktop = 0x0100;

            [DllImport(U32, CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
            [DllImport(U32)] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
            [DllImport(U32)] static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeoutMs, out IntPtr result);
            [DllImport(U32)] static extern bool IsIconic(IntPtr hwnd);
            [DllImport(U32)] static extern bool IsWindowVisible(IntPtr hwnd);
            [DllImport(U32)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
            [DllImport(U32)] static extern bool CloseDesktop(IntPtr desktop);

            static IntPtr _hwnd;
            static uint _pid;

            static IntPtr Window()
            {
                if (_hwnd != IntPtr.Zero) return _hwnd;
                if (_pid == 0) using (Process p = Process.GetCurrentProcess()) _pid = (uint)p.Id;
                IntPtr w = IntPtr.Zero;
                while ((w = FindWindowExW(IntPtr.Zero, w, "UnityWndClass", null)) != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(w, out uint pid);
                    if (pid == _pid) return _hwnd = w;
                }
                return IntPtr.Zero;
            }

            // Synchronous: the answer is known before this returns (at most 1 s; at once when Windows already counts the window hung).
            static void Post()
            {
                IntPtr w = Window();
                if (w == IntPtr.Zero) return;
                Posted++;
                if (SendMessageTimeoutW(w, WmNull, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung | SmtoErrorOnExit, 1000, out _) != IntPtr.Zero)
                    Interlocked.Increment(ref _answers);
            }

            static bool ScreenLocked()
            {
                IntPtr d = OpenInputDesktop(0, false, DesktopSwitchDesktop);
                if (d == IntPtr.Zero) return true;   // the secure desktop has the input: locked, switch user, a UAC prompt
                CloseDesktop(d);
                return false;
            }

            static bool DisplayAsleep() => false;   // Windows keeps presenting to a sleeping monitor; nothing to excuse

            static bool Minimised()
            {
                IntPtr w = Window();
                return w != IntPtr.Zero && (IsIconic(w) || !IsWindowVisible(w));
            }
#else
            static void Post() { _probeOff = true; Posted = 0; }   // the editor and other platforms: no probe
            static bool ScreenLocked() => false;
            static bool DisplayAsleep() => false;
            static bool Minimised() => false;
#endif
        }

        /// <summary>The hang record (pure text; tests).</summary>
        public static string HangText(double stalledSeconds, bool final, float[] ring, int ringHead, int ringCount, int tick, string phase)
        {
            var sb = new StringBuilder(4096);
            sb.Append("utc=").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append('\n')
              .Append("stalled_s=").Append(stalledSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append('\n')
              .Append("final=").Append(final ? 1 : 0).Append('\n')
              .Append("tick=").Append(tick).Append('\n')
              .Append("phase=").Append(phase ?? "").Append('\n')
              .Append("last_frames_ms=");
            int n = Math.Min(ringCount, ring.Length);
            for (int i = 0; i < n; i++)
            {
                int k = (ringHead - n + i + ring.Length) % ring.Length;
                if (i > 0) sb.Append(',');
                sb.Append(ring[k].ToString("0.0", CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>The watchdog's own view for a hang record (pure text; tests): what was checked and what the stacks are.</summary>
        public static string WatchdogText(NoFrameExcuse excusesNow, string probe, bool debugger, int longWork, string longWorkWhat, string platform)
        {
            var sb = new StringBuilder(1024);
            sb.Append("excuses_now=").Append(HangWatchdogRule.Describe(excusesNow)).Append('\n')
              .Append("watchdog=").Append(probe ?? "").Append('\n')
              .Append("debugger=").Append(debugger ? 1 : 0).Append('\n')
              .Append("long_work=").Append(longWork).Append(longWork > 0 ? " (" + (longWorkWhat ?? "") + ")" : "").Append('\n')
              .Append("platform=").Append(platform ?? "").Append('\n')
              .Append("rule=no frame for ").Append(KillSeconds.ToString("0", CultureInfo.InvariantCulture))
              .Append(" s while expected (not paused, locked, asleep, minimised, debugged, declared long work, machine sleep), and the main thread ")
              .Append("answered no probe; grace ").Append(HangWatchdogRule.GraceSeconds.ToString("0", CultureInfo.InvariantCulture)).Append(" s after any excuse\n")
              .Append("stacks=Mono cannot walk another thread's managed stack from managed code; the main thread's stack (thread 0, JIT frames ")
              .Append("under mono_runtime_invoke) is in the OS crash report the abort writes, copied to logs/os-crash-* on the next launch; ")
              .Append("Player.log holds the watchdog thread's FailFast stack. phase= above says where the main thread was.\n");
            return sb.ToString();
        }

        static void WriteHang(double stalled, bool final, NoFrameExcuse excusesNow, double now)
        {
            string text;
            try
            {
                bool dbg = false;
                try { dbg = Debugger.IsAttached; } catch (Exception) { }
                text = HangText(stalled, final, Ring, _ringHead, _ringCount, MatchTick, Phase) +
                       WatchdogText(excusesNow, WatchdogNative.Describe(now), dbg, Volatile.Read(ref _longWork), _longWorkWhat,
                                    Environment.OSVersion.ToString());
            }
            catch (Exception) { return; }
            string name = "hang-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + (final ? "-final" : "") + ".txt";
            try { File.WriteAllText(Path.Combine(_dir, name), text); } catch (Exception) { }   // SupportReport bundles DATA/hang-*.txt
            if (!final || _logsDir == null) return;
            try
            {
                Directory.CreateDirectory(_logsDir);
                File.WriteAllText(Path.Combine(_logsDir, name), text);   // the game's log folder keeps the final record too
            }
            catch (Exception) { }
        }

        /// <summary>Tests only: the frame counter the watchdog reads.</summary>
        public static int FrameCounter => Volatile.Read(ref _frameCounter);
    }
}

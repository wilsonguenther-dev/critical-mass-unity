using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    /// Environment.FailFast so the OS writes a crash report and the next launch knows. A machine sleep (the watchdog's own
    /// 0.5 s nap taking over 3 s) resets the clock, so waking a laptop never kills the game.</para>
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
        public const double HangSeconds = 5;
        public const double KillSeconds = 60;
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
            ToolRun = GamePaths.HasFlag(args, "-exportReport") || GamePaths.HasFlag(args, "-simHashOut");
            _diag = GamePaths.HasFlag(args, "-diag");
            _dir = GamePaths.DataDir;
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
            Interlocked.Increment(ref _frameCounter);
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
            int seen = Volatile.Read(ref _frameCounter);
            double lastProgress = 0, lastLoop = 0;
            bool hangWritten = false;
            while (!_stopWatchdog)
            {
                Thread.Sleep(500);
                double now = clock.Elapsed.TotalSeconds;
                if (now - lastLoop > 3.0) lastProgress = now;   // the machine slept: not a hang
                lastLoop = now;
                string hb = _pendingHeartbeat;
                if (hb != null)
                {
                    _pendingHeartbeat = null;
                    SafeFile.TryWriteAtomic(Path.Combine(_dir, HeartbeatFile), hb, false, out _);
                }
                int f = Volatile.Read(ref _frameCounter);
                if (f != seen)
                {
                    seen = f;
                    lastProgress = now;
                    hangWritten = false;
                    continue;
                }
                double stalled = now - lastProgress;
                if (stalled >= HangSeconds && !hangWritten)
                {
                    hangWritten = true;
                    WriteHang(stalled, false);
                }
                if (stalled >= KillSeconds)
                {
                    WriteHang(stalled, true);
                    Flag("hang");
                    Environment.FailFast("Critical Mass: the main thread did not finish a frame for " + KillSeconds + " s");
                }
            }
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

        static void WriteHang(double stalled, bool final)
        {
            try
            {
                string name = "hang-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + (final ? "-final" : "") + ".txt";
                File.WriteAllText(Path.Combine(_dir, name), HangText(stalled, final, Ring, _ringHead, _ringCount, MatchTick, Phase));
            }
            catch (Exception) { }
        }

        /// <summary>Tests only: the frame counter the watchdog reads.</summary>
        public static int FrameCounter => Volatile.Read(ref _frameCounter);
    }
}

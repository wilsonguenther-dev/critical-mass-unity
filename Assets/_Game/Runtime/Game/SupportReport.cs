using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-64 support kit. "Save a problem report" (Settings, Game tab), -exportReport (exits 0 when the zip was written,
    /// 1 otherwise; works while the game is running) and -diag (verbose session: 1 s heartbeat, a system dump at boot and
    /// a report at quit). The zip goes to the Desktop (else the data folder), capped at 10 MB, and holds crash-log.txt,
    /// hang files, the last 10 session logs, machine-profile.json, heartbeat.txt, session-history.txt, the current
    /// Player.log tail, the newest OS crash report copied at boot, and system.txt (build, OS, CPU, memory, GPU, graphics
    /// API, displays, settings). Nothing is uploaded: there is no third-party crash service (decision ledger).
    /// </summary>
    public static class SupportReport
    {
        public const long MaxBytes = 10L * 1024 * 1024;
        public const long MaxEntryBytes = 3L * 1024 * 1024;

        /// <summary>Writes the zip; returns its path, or null with the reason.</summary>
        public static string Export(string destDir, out string error)
        {
            error = null;
            try
            {
                string data = GamePaths.DataDir;
                string dir = destDir ?? Desktop() ?? data;
                Directory.CreateDirectory(dir);
                string zip = Path.Combine(dir, "CriticalMass-report-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip");
                var files = new List<(string path, string name)>();
                void Add(string p, string name)
                {
                    if (File.Exists(p)) files.Add((p, name));
                }
                Add(Path.Combine(data, "crash-log.txt"), "crash-log.txt");
                Add(Path.Combine(data, "crash-log.1.txt"), "crash-log.1.txt");
                Add(Path.Combine(data, FrameGovernor.ProfileFile), "machine-profile.json");
                Add(Path.Combine(data, SessionKeeper.HeartbeatFile), "heartbeat.txt");
                Add(Path.Combine(data, SessionKeeper.HistoryFile), "session-history.txt");
                foreach (string h in Newest(data, "hang-*.txt", 10)) Add(h, Path.GetFileName(h));
                string logs = GamePaths.LogsDir;
                foreach (string s in Newest(logs, "session-*.log", 10)) Add(s, "logs/" + Path.GetFileName(s));
                foreach (string s in Newest(logs, "heartbeat-*.txt", 5)) Add(s, "logs/" + Path.GetFileName(s));
                foreach (string s in Newest(logs, "os-crash-*", 2)) Add(s, "logs/" + Path.GetFileName(s));
                string player = Application.consoleLogPath;
                if (!string.IsNullOrEmpty(player)) Add(player, "Player.log");

                long total = 0;
                using (var fs = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    byte[] sys = Encoding.UTF8.GetBytes(SystemText());
                    WriteEntry(archive, "system.txt", sys);
                    total += sys.Length;
                    foreach ((string p, string name) in files)
                    {
                        if (total >= MaxBytes) break;
                        byte[] bytes = ReadTail(p, Math.Min(MaxEntryBytes, MaxBytes - total));
                        if (bytes == null) continue;
                        WriteEntry(archive, name, bytes);
                        total += bytes.Length;
                    }
                }
                if (new FileInfo(zip).Length > MaxBytes) Debug.LogWarning("[CM.Support] report is over 10 MB after compression headers");
                Debug.Log($"[CM.Support] problem report written: {zip} ({files.Count + 1} files)");
                return zip;
            }
            catch (Exception e)
            {
                error = SafeFile.Describe(e);
                Debug.LogWarning($"[CM.Support] problem report failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        static void WriteEntry(ZipArchive a, string name, byte[] bytes)
        {
            ZipArchiveEntry e = a.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using (Stream s = e.Open()) s.Write(bytes, 0, bytes.Length);
        }

        static byte[] ReadTail(string path, long max)
        {
            try
            {
                using (var i = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long n = Math.Min(i.Length, Math.Max(0, max));
                    if (i.Length > n) i.Seek(-n, SeekOrigin.End);
                    var buf = new byte[n];
                    int read = 0;
                    while (read < n)
                    {
                        int r = i.Read(buf, read, (int)(n - read));
                        if (r <= 0) break;
                        read += r;
                    }
                    if (read == n) return buf;
                    var cut = new byte[read];
                    Array.Copy(buf, cut, read);
                    return cut;
                }
            }
            catch (Exception) { return null; }
        }

        static IEnumerable<string> Newest(string dir, string pattern, int n)
        {
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            string[] f = Directory.GetFiles(dir, pattern);
            Array.Sort(f, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            if (f.Length > n) Array.Resize(ref f, n);
            return f;
        }

        static string Desktop()
        {
            try
            {
                string d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Desktop");
                return Directory.Exists(d) ? d : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Build and machine facts (also logged at boot with -diag).</summary>
        public static string SystemText()
        {
            var sb = new StringBuilder(2048);
            var c = CultureInfo.InvariantCulture;
            void L(string k, object v) => sb.Append(k).Append('=').Append(Convert.ToString(v, c)).Append('\n');
            L("utc", DateTime.UtcNow.ToString("o", c));
            L("build", BuildInfo.Label);
            L("built_utc", BuildInfo.BuiltUtc);
            L("unity", Application.unityVersion);
            L("platform", Application.platform);
            L("os", SystemInfo.operatingSystem);
            L("cpu", SystemInfo.processorType);
            L("cpu_cores", SystemInfo.processorCount);
            L("ram_mb", SystemInfo.systemMemorySize);
            L("gpu", SystemInfo.graphicsDeviceName);
            L("gpu_vendor", SystemInfo.graphicsDeviceVendor);
            L("gpu_driver", SystemInfo.graphicsDeviceVersion);
            L("gpu_api", SystemInfo.graphicsDeviceType);
            L("vram_mb", SystemInfo.graphicsMemorySize);
            L("battery", SystemInfo.batteryStatus);
            L("displays", Display.displays.Length);
            L("screen", Screen.width + "x" + Screen.height);
            L("display_native", Display.main.systemWidth + "x" + Display.main.systemHeight);
            L("refresh_hz", Screen.currentResolution.refreshRateRatio.value.ToString("0.##", c));
            L("window_mode", Screen.fullScreenMode);
            int q = QualitySettings.GetQualityLevel();
            L("quality", q >= 0 && q < QualitySettings.names.Length ? QualitySettings.names[q] : "?");
            L("render_scale", FrameGovernor.AppliedRenderScale.ToString("0.##", c));
            L("frame_cap", FrameGovernor.AppliedCap);
            L("vsync", QualitySettings.vSyncCount);
            L("safe_mode", SessionKeeper.SafeMode ? SessionKeeper.SafeReason : "no");
            L("previous_unclean", SessionKeeper.PreviousUnclean);
            L("data_dir", GamePaths.DataDir);
            L("app_dir", Application.dataPath);
            L("translocated", GamePaths.Translocated);
            L("player_log", Application.consoleLogPath);
            return sb.ToString();
        }

        /// <summary>Shows the file in Finder or Explorer (best effort; never throws).</summary>
        public static void Reveal(string path)
        {
            try
            {
                if (Application.platform == RuntimePlatform.OSXPlayer) Process.Start("/usr/bin/open", "-R \"" + path + "\"")?.Dispose();
                else if (Application.platform == RuntimePlatform.WindowsPlayer) Process.Start("explorer.exe", "/select,\"" + path + "\"")?.Dispose();
                else Application.OpenURL("file://" + Path.GetDirectoryName(path));
            }
            catch (Exception) { }
        }

        /// <summary>DG-64 reset: deletes the game's own data files (profile, history, heartbeat); logs and reports stay.</summary>
        public static void ResetDataFiles()
        {
            string d = GamePaths.DataDir;
            foreach (string f in new[] { FrameGovernor.ProfileFile, SessionKeeper.HistoryFile, SessionKeeper.HeartbeatFile })
            {
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                {
                    try { File.Delete(Path.Combine(d, f + suffix)); } catch (Exception) { }
                }
            }
            Debug.Log("[CM.Support] settings and data files reset");
        }

        /// <summary>Boot handling of -exportReport and -diag (called by AutoplayArgs.Awake). true: the run should quit now.</summary>
        public static bool HandleBootFlags()
        {
            if (Application.isEditor) return false;
            if (GamePaths.HasFlag("-diag")) Debug.Log("[CM.Diag]\n" + SystemText());
            if (!GamePaths.HasFlag("-exportReport")) return false;
            string zip = Export(null, out string error);
            Debug.Log(zip != null ? "[CM.Support] EXPORT OK " + zip : "[CM.Support] EXPORT FAILED " + error);
            Application.Quit(zip != null ? 0 : 1);
            return true;
        }

        /// <summary>-diag: a report at quit too.</summary>
        public static void OnQuitDiag()
        {
            if (!Application.isEditor && GamePaths.HasFlag("-diag")) Export(null, out _);
        }
    }
}

using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-53 small files that survive a crash or a power cut. Moved out of FrameGovernor.cs (DG-05) and hardened.
    /// <para>Write: the text goes to PATH.tmp through a FileStream that is flushed to the disk (Flush(true)), then
    /// File.Replace swaps it in with one rename and keeps the old file as PATH.bak (File.Move when there is no old file).
    /// An antivirus or indexer holding the file gets 3 tries, 50 ms apart; each retry is logged once and counted.</para>
    /// <para>Read: the first line must be "cm_format_version=N"; the file, then PATH.bak, then PATH.tmp are tried, and a
    /// file over 64 KB is rejected (a corrupt or hostile file can never make the game read megabytes at boot).</para>
    /// <para>Boot: CleanStale deletes a .tmp whose real file exists and promotes a valid .tmp that has none (a crash between
    /// the flush and the first rename).</para>
    /// A full disk or a read-only folder never throws past TryWriteAtomic: it returns false with the reason.
    /// </summary>
    public static class SafeFile
    {
        public const string VersionKey = "cm_format_version";
        public const int MaxReadBytes = 64 * 1024;
        public const int Attempts = 3;
        public const int RetryDelayMs = 50;

        /// <summary>Retries taken since boot (the antivirus case; tests read it).</summary>
        public static int RetryCount => _retries;
        static int _retries;

        /// <summary>The last write failure ("disk full", "no permission", ...), null after a good write.</summary>
        public static string LastError { get; private set; }

        /// <summary>Test-only fault injection: called with "tmp-written", "before-replace" and "after-replace".</summary>
        public static Action<string> FaultHook;

        public static string Versioned(int version, string body) => VersionKey + "=" + version + "\n" + body;

        /// <summary>Writes atomically; throws the final IOException after the retries (callers that cannot fail use TryWriteAtomic).</summary>
        public static void WriteAtomic(string path, string text, bool keepBak)
        {
            string tmp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(text ?? "");
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);   // to the disk, not just the OS cache: today's power cut is the case
                }
                FaultHook?.Invoke("tmp-written");
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        FaultHook?.Invoke("before-replace");
                        Swap(tmp, path, keepBak);
                        FaultHook?.Invoke("after-replace");
                        LastError = null;
                        return;
                    }
                    catch (IOException) when (attempt < Attempts)
                    {
                        Retry(path, attempt);
                    }
                    catch (UnauthorizedAccessException) when (attempt < Attempts)
                    {
                        Retry(path, attempt);
                    }
                }
            }
            catch (Exception e)
            {
                LastError = Describe(e);
                throw;
            }
        }

        /// <summary>WriteAtomic that never throws: false with the reason in error (and LastError).</summary>
        public static bool TryWriteAtomic(string path, string text, bool keepBak, out string error)
        {
            try
            {
                WriteAtomic(path, text, keepBak);
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = Describe(e);
                LastError = error;
                try
                {
                    if (File.Exists(path + ".tmp") && File.Exists(path)) File.Delete(path + ".tmp");
                }
                catch (Exception) { }
                return false;
            }
        }

        static void Retry(string path, int attempt)
        {
            Interlocked.Increment(ref _retries);
            Debug.LogWarning($"[CM.SafeFile] {Path.GetFileName(path)} is held by another program; retry {attempt} of {Attempts - 1} in {RetryDelayMs} ms");
            Thread.Sleep(RetryDelayMs);
        }

        static void Swap(string tmp, string target, bool keepBak)
        {
            if (!File.Exists(target))
            {
                File.Move(tmp, target);
                return;
            }
            try
            {
                // One rename on macOS and Windows; the old file becomes the .bak (or goes) in the same step.
                File.Replace(tmp, target, keepBak ? target + ".bak" : null);
            }
            catch (PlatformNotSupportedException)
            {
                // FALLBACK (no File.Replace on this runtime): keep a .bak first so a crash in between still reads back.
                File.Copy(target, target + ".bak", true);
                File.Delete(target);
                File.Move(tmp, target);
            }
        }

        /// <summary>A short, player-safe reason for a failed write.</summary>
        public static string Describe(Exception e)
        {
            if (e is UnauthorizedAccessException) return "no permission to write";
            if (e is DirectoryNotFoundException) return "folder missing";
            if (e is IOException io)
            {
                // ENOSPC (28) on macOS, ERROR_DISK_FULL (112) / ERROR_HANDLE_DISK_FULL (39) on Windows
                int h = io.HResult & 0xFFFF;
                if (h == 112 || h == 39 || h == 28 || (io.Message ?? "").IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "disk full";
                return "file in use or unreadable";
            }
            return e.GetType().Name;
        }

        /// <summary>The body of a versioned file, or null with the reason in why; tries PATH, PATH.bak, PATH.tmp.</summary>
        public static string ReadVersioned(string path, int version, out string why)
        {
            string body = TryRead(path, version, out why);
            if (body != null) return body;
            string bak = TryRead(path + ".bak", version, out _);
            if (bak != null)
            {
                why = null;
                return bak;
            }
            string tmp = TryRead(path + ".tmp", version, out _);
            if (tmp != null) why = null;
            return tmp;
        }

        /// <summary>Boot: a .tmp next to its real file is stale (deleted); a valid .tmp with no real file is promoted.</summary>
        public static int CleanStale(string dir)
        {
            int n = 0;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                foreach (string tmp in Directory.GetFiles(dir, "*.tmp"))
                {
                    string real = tmp.Substring(0, tmp.Length - 4);
                    try
                    {
                        if (!File.Exists(real) && HasVersionLine(tmp)) File.Move(tmp, real);
                        else File.Delete(tmp);
                        n++;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return n;
        }

        static bool HasVersionLine(string p)
        {
            var fi = new FileInfo(p);
            if (!fi.Exists || fi.Length == 0 || fi.Length > MaxReadBytes) return false;
            string t = File.ReadAllText(p);
            return t.StartsWith(VersionKey + "=", StringComparison.Ordinal);
        }

        static string TryRead(string path, int version, out string why)
        {
            why = null;
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists)
                {
                    why = "missing";
                    return null;
                }
                if (fi.Length > MaxReadBytes)
                {
                    why = "corrupt (over 64 KB)";
                    return null;
                }
                string t = File.ReadAllText(path);
                int nl = t.IndexOf('\n');
                string first = (nl >= 0 ? t.Substring(0, nl) : t).Trim();
                if (!first.StartsWith(VersionKey + "=", StringComparison.Ordinal))
                {
                    why = "corrupt (no version line)";
                    return null;
                }
                if (!int.TryParse(first.Substring(VersionKey.Length + 1), out int v) || v != version)
                {
                    why = "format version " + first.Substring(VersionKey.Length + 1) + ", expected " + version;
                    return null;
                }
                return nl >= 0 ? t.Substring(nl + 1) : "";
            }
            catch (Exception e)
            {
                why = "unreadable: " + e.GetType().Name;
                return null;
            }
        }
    }

    /// <summary>
    /// Where the game keeps its files. Application.persistentDataPath unless -dataDir PATH is given (smoke and soak runs
    /// use their own folder so they never touch the owner's files, DG-54). Resolved once on the main thread (the
    /// background writers read the cached value). A folder that cannot be written falls back to the temp folder.
    /// Never the app folder: a translocated or read-only .app (a download opened from Downloads) must still run.
    /// </summary>
    public static class GamePaths
    {
        static string _data;

        /// <summary>The data folder (creates it on first use).</summary>
        public static string DataDir
        {
            get
            {
                if (_data == null) _data = Init();
                return _data;
            }
        }

        public static string LogsDir => Path.Combine(DataDir, "logs");

        /// <summary>true when -dataDir moved the data folder (an isolated measurement run).</summary>
        public static bool Isolated { get; private set; }

        /// <summary>true when the app runs from a quarantined, translocated copy (macOS Gatekeeper); logged once at boot.</summary>
        public static bool Translocated => (Application.dataPath ?? "").IndexOf("/AppTranslocation/", StringComparison.Ordinal) >= 0;

        /// <summary>The -dataDir value in args, or null (pure; tests).</summary>
        public static string DataDirArg(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-dataDir", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(args[i + 1]) &&
                    !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    return args[i + 1];
            return HiddenOnlineDir(args);
        }

        /// <summary>
        /// online-rooms-4: a hidden online test peer (-onlineHost or -onlineJoin with -batchmode or -onlineHeadless) and no
        /// -dataDir gets its own folder, TEMP/cm-online-PID, so two copies on one machine never meet on the single-instance
        /// lock (SessionKeeper) and never touch the player's own saves. Null for every other run.
        /// </summary>
        public static string HiddenOnlineDir(string[] args)
        {
            bool online = false, hidden = false;
            foreach (string a in args)
            {
                string f = (a ?? "").TrimStart('-').ToLowerInvariant();
                if (f == "onlinehost" || f == "onlinejoin") online = true;
                if (f == "batchmode" || f == "onlineheadless") hidden = true;
            }
            if (!online || !hidden) return null;
            return Path.Combine(Path.GetTempPath(), "cm-online-" + System.Diagnostics.Process.GetCurrentProcess().Id);
        }

        /// <summary>true for a -dataDir run without touching the engine (safe from any thread after boot; tests can set it).</summary>
        public static bool IsolatedRun => _isolatedArg ?? (bool)(_isolatedArg = DataDirArg(Environment.GetCommandLineArgs()) != null);
        static bool? _isolatedArg;

        /// <summary>true when a flag is on the command line (case-insensitive).</summary>
        public static bool HasFlag(string flag) => HasFlag(Environment.GetCommandLineArgs(), flag);

        public static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (string a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>true in the Unity Editor (compile-time: a player build never takes the Editor branch).</summary>
#if UNITY_EDITOR
        public const bool InEditor = true;
#else
        public const bool InEditor = false;
#endif

        /// <summary>The name suffix of the Editor's own data folder ("Critical Mass" -> "Critical Mass (Editor)").</summary>
        public const string EditorSuffix = " (Editor)";

        static string _root, _playerRoot;

        /// <summary>
        /// The shipped game's data folder (Application.persistentDataPath: profile, replays, photos, logs). Only for
        /// comparisons (IsInsidePlayerRoot); the game writes through PersistentRoot or DataDir. Main thread on first use.
        /// </summary>
        public static string PlayerRoot => _playerRoot ?? (_playerRoot = Application.persistentDataPath);

        /// <summary>
        /// rough-spots pass 2: the root of every file the game keeps. A player build: Application.persistentDataPath,
        /// exactly as before. The Unity Editor: the sibling folder "NAME (Editor)" (EditorRoot), so opening the Editor or
        /// entering Play never reads, writes or prunes the owner's profile, replays, photos or logs. Main thread on first use.
        /// </summary>
        public static string PersistentRoot => _root ?? (_root = RootFor(PlayerRoot, InEditor));

        /// <summary>The data root for a player root (pure; tests): the player root itself, or EditorRoot in the Editor.</summary>
        public static string RootFor(string playerRoot, bool editor) => editor ? EditorRoot(playerRoot) : playerRoot;

        /// <summary>The Editor's sibling folder for a player root (pure; tests).</summary>
        public static string EditorRoot(string playerRoot)
        {
            string t = (playerRoot ?? "").TrimEnd('/', '\\');
            if (t.Length == 0) return Path.Combine(Path.GetTempPath(), "CriticalMass" + EditorSuffix);
            string parent = Path.GetDirectoryName(t);
            string name = Path.GetFileName(t) + EditorSuffix;
            return string.IsNullOrEmpty(parent) ? name : Path.Combine(parent, name);
        }

        /// <summary>true when path is the shipped game's data folder or inside it (pure on its arguments; tests).</summary>
        public static bool IsInside(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            string p, r;
            try { p = Path.GetFullPath(path).TrimEnd('/', '\\'); r = Path.GetFullPath(root).TrimEnd('/', '\\'); }
            catch (Exception) { return false; }
            return string.Equals(p, r, StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>true when path is inside the shipped game's own data folder (main thread on first use).</summary>
        public static bool IsInsidePlayerRoot(string path) => IsInside(path, PlayerRoot);

        static string Init()
        {
            string arg = DataDirArg(Environment.GetCommandLineArgs());
            Isolated = arg != null;
            string dir = arg != null ? Path.GetFullPath(arg) : PersistentRoot;
            if (Writable(dir)) return dir;
            string fallback = Path.Combine(Path.GetTempPath(), "CriticalMass");
            Debug.LogWarning($"[CM.Paths] {dir} cannot be written; using {fallback} for this session");
            return Writable(fallback) ? fallback : dir;
        }

        static bool Writable(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Tests only: forget the cached folder.</summary>
        public static void ResetForTests(string dir)
        {
            _data = dir;
            Isolated = dir != null;
        }
    }
}

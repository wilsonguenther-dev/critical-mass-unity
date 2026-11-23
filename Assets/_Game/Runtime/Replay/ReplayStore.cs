using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using CriticalMass.Net.Replay;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Replay
{
    /// <summary>One saved replay as the list screen shows it.</summary>
    public sealed class ReplayEntry
    {
        public string Path;
        public ReplayHeader Header;
        public ReplayCompatibility Compat;
        /// <summary>The plain reason it cannot be played ("" when it can).</summary>
        public string Why = "";
    }

    /// <summary>
    /// b13-replay: the Replays folder (GamePaths.DataDir/Replays: the platform's persistent data folder, or an automation
    /// run's own -dataDir). Files are named replay-yyyyMMdd-HHmmss-SEED.cmreplay from the UTC clock with the invariant
    /// culture (ASCII only, no colons: the same on macOS and Windows), so name order is age order. The newest Keep are
    /// kept; older ones are deleted after each save. Saving encodes, compresses and writes on a worker thread (temp file,
    /// then a move, so a crash never leaves half a replay under the real name).
    /// </summary>
    public static class ReplayStore
    {
        public const string FolderName = "Replays";
        public const string Extension = ".cmreplay";
        public const int Keep = 20;
        /// <summary>The list screen reads only this much of each file (the header is a few hundred bytes).</summary>
        public const int HeaderReadBytes = 16 * 1024;
        const string Tag = "[CM.Replay]";

        static string _folderOverride;

        /// <summary>Tests point the store at a temp folder (null = the real one).</summary>
        public static void SetFolderForTests(string dir) => _folderOverride = dir;

        /// <summary>Main thread only (GamePaths.DataDir reads Application.persistentDataPath).</summary>
        public static string Folder => _folderOverride ?? System.IO.Path.Combine(GamePaths.DataDir, FolderName);

        /// <summary>The file name for a replay recorded at utc on seed (pure; tests).</summary>
        public static string FileName(DateTime utc, int seed) =>
            "replay-" + utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
            Math.Abs((long)seed).ToString(CultureInfo.InvariantCulture) + Extension;

        /// <summary>The names to delete so only the newest keep remain (names sort by age; pure; tests).</summary>
        public static List<string> ToDelete(IEnumerable<string> names, int keep)
        {
            var all = new List<string>();
            foreach (string n in names) if (n != null && n.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) all.Add(n);
            all.Sort(StringComparer.Ordinal);
            var del = new List<string>();
            for (int i = 0; i < all.Count - Math.Max(0, keep); i++) del.Add(all[i]);
            return del;
        }

        /// <summary>Saves on a worker thread; done (optional) runs on that thread with the path or null.</summary>
        public static void SaveAsync(ReplayData d, Action<string> done = null)
        {
            if (d == null) return;
            string folder = Folder;
            if (GamePaths.InEditor) _ = GamePaths.PlayerRoot; // resolved here, on the main thread (EditorRefuses reads it)
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string path = SaveNow(d, folder);
                done?.Invoke(path);
            });
        }

        /// <summary>Encodes and writes now (any thread; folder resolved by the caller). Returns the path, or null on failure.</summary>
        public static string SaveNow(ReplayData d, string folder)
        {
            try
            {
                if (EditorRefuses(folder, "save")) return null;
                Directory.CreateDirectory(folder);
                string name = FileName(d.Header.RecordedUtc, d.Header.Start != null ? d.Header.Start.Seed : 0);
                string path = System.IO.Path.Combine(folder, name);
                for (int i = 2; File.Exists(path) && i < 100; i++)
                    path = System.IO.Path.Combine(folder, name.Substring(0, name.Length - Extension.Length) + "-" + i.ToString(CultureInfo.InvariantCulture) + Extension);
                byte[] bytes = ReplayFile.Write(d, true);
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                if (!GamePaths.InEditor || _folderOverride != null) Prune(folder, Keep);
                Debug.Log($"{Tag} saved {name}: {bytes.Length} bytes, {d.Header.Ticks} ticks, {d.FingerprintCount} fingerprints");
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} could not save the replay: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// rough-spots pass 2: in the Unity Editor the store never saves into or prunes the shipped game's own data folder
        /// (a session once deleted one of the owner's replays through the keep-20 limit). Editor saves go to the Editor's
        /// folder (GamePaths.PersistentRoot) and are never pruned there either unless a test pointed the store at its own
        /// folder. Always false in a player build.
        /// </summary>
        public static bool EditorRefuses(string folder, string what)
        {
            if (!GamePaths.InEditor || !GamePaths.IsInsidePlayerRoot(folder)) return false;
            Debug.LogWarning($"{Tag} Editor: refused to {what} in the player's own folder {folder}");
            return true;
        }

        /// <summary>Deletes all but the newest keep replays in folder (and stray .tmp files of earlier crashes).</summary>
        public static void Prune(string folder, int keep)
        {
            try
            {
                if (EditorRefuses(folder, "prune")) return;
                if (!Directory.Exists(folder)) return;
                var names = new List<string>();
                foreach (string f in Directory.GetFiles(folder))
                {
                    string n = System.IO.Path.GetFileName(f);
                    if (n.EndsWith(Extension + ".tmp", StringComparison.OrdinalIgnoreCase) && File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-1))
                        File.Delete(f);
                    else names.Add(n);
                }
                foreach (string n in ToDelete(names, keep)) File.Delete(System.IO.Path.Combine(folder, n));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} could not tidy the replays folder: {e.Message}");
            }
        }

        /// <summary>Every saved replay, newest first, with whether this build can play it (headers only).</summary>
        public static List<ReplayEntry> List()
        {
            var list = new List<ReplayEntry>();
            string folder = Folder;
            try
            {
                if (!Directory.Exists(folder)) return list;
                var buf = new byte[HeaderReadBytes];
                foreach (string f in Directory.GetFiles(folder, "*" + Extension))
                {
                    var e = new ReplayEntry { Path = f };
                    int n = 0;
                    try
                    {
                        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            int r;
                            while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
                        }
                    }
                    catch (Exception) { n = 0; }
                    ReplayReadStatus st = ReplayFile.TryReadHeader(buf, n, out e.Header, out string why);
                    if (st != ReplayReadStatus.Ok || e.Header == null)
                    {
                        e.Compat = st == ReplayReadStatus.NewerFormat ? ReplayCompatibility.NewerFormat : ReplayCompatibility.Damaged;
                        e.Why = why ?? ReplayCompat.Message(e.Compat, null);
                    }
                    else
                    {
                        e.Compat = ReplayCompat.Check(e.Header, BuildInfo.Version, BuildInfo.Commit);
                        e.Why = ReplayCompat.Message(e.Compat, e.Header);
                    }
                    list.Add(e);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{Tag} could not list the replays: {ex.Message}");
            }
            list.Sort((a, b) =>
            {
                long ta = a.Header != null ? a.Header.RecordedUtcTicks : 0, tb = b.Header != null ? b.Header.RecordedUtcTicks : 0;
                int c = tb.CompareTo(ta);
                return c != 0 ? c : string.CompareOrdinal(b.Path, a.Path);
            });
            return list;
        }

        /// <summary>Reads a whole replay; null with a plain reason when it cannot be played on this build.</summary>
        public static ReplayData Load(string path, out string why)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception) { why = "The replay file could not be opened."; return null; }
            ReplayReadStatus st = ReplayFile.TryRead(bytes, out ReplayData d, out why);
            if (st != ReplayReadStatus.Ok) return null;
            ReplayCompatibility c = ReplayCompat.Check(d.Header, BuildInfo.Version, BuildInfo.Commit);
            if (c != ReplayCompatibility.Ok)
            {
                why = ReplayCompat.Message(c, d.Header);
                return null;
            }
            why = null;
            return d;
        }
    }

    /// <summary>b13-replay: the list screen's words for one replay (pure; tests). Dates in the player's local time, invariant culture.</summary>
    public static class ReplayWords
    {
        public static string Length(double seconds)
        {
            if (!(seconds > 0) || double.IsInfinity(seconds)) seconds = 0;
            int s = (int)Math.Floor(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Result(ReplayHeader h)
        {
            if (h == null) return "";
            switch (h.Winner)
            {
                case MatchResult.Azure: return "AZURE WON";
                case MatchResult.Crimson: return "CRIMSON WON";
                case MatchResult.Draw: return "DRAW";
                default: return h.Finished ? "NO RESULT" : "LEFT EARLY";
            }
        }

        public static string Date(ReplayHeader h, bool local = true)
        {
            if (h == null || h.RecordedUtcTicks <= 0) return "";
            DateTime t = local ? h.RecordedUtc.ToLocalTime() : h.RecordedUtc;
            return t.ToString("ddd d MMM  HH:mm", CultureInfo.InvariantCulture).ToUpperInvariant();
        }

        /// <summary>"MON 16 NOV  17:44   SKYBREAK BASIN   FULL BATTLE 16 V 16   AZURE WON   10:00" (+ "   OTHER VERSION").</summary>
        public static string Row(ReplayEntry e, bool local = true)
        {
            if (e == null) return "";
            if (e.Header == null) return "DAMAGED REPLAY FILE";
            ReplayHeader h = e.Header;
            string mode = string.IsNullOrEmpty(h.ModeLabel) ? "MATCH" : h.ModeLabel.ToUpperInvariant();
            int size = h.Start != null ? h.Start.TeamSize : 0;
            string s = Date(h, local) + "   " + (string.IsNullOrEmpty(h.MapName) ? "" : h.MapName.ToUpperInvariant() + "   ") +
                       mode + " " + size.ToString(CultureInfo.InvariantCulture) + " V " + size.ToString(CultureInfo.InvariantCulture) + "   " +
                       Result(h) + "   " + Length(h.MatchTime);
            if (e.Compat == ReplayCompatibility.OtherVersion || e.Compat == ReplayCompatibility.NewerFormat) s += "   OTHER VERSION";
            else if (e.Compat == ReplayCompatibility.Damaged) s += "   DAMAGED";
            return s;
        }
    }
}

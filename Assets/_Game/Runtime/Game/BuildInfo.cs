using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-05 version stamp. Editor/Build.cs writes a short key=value text (version, commit, dirty, built_utc, target,
    /// development) to Resources/BuildInfo.txt for the length of one player build and deletes it after, so a built player
    /// knows exactly which commit it is and the tree stays clean. In the editor (no stamp) the version reads "dev".
    /// </summary>
    public static class BuildInfo
    {
        /// <summary>Resources path of the stamp Build.cs writes (Assets/_Game/Resources/BuildInfo.txt).</summary>
        public const string ResourcePath = "BuildInfo";

        static Dictionary<string, string> _fields;

        /// <summary>The parsed stamp; empty when the player was not built by Editor/Build.cs (editor, tests).</summary>
        public static IReadOnlyDictionary<string, string> Fields
        {
            get
            {
                if (_fields == null)
                {
                    TextAsset t = Resources.Load<TextAsset>(ResourcePath);
                    _fields = Parse(t != null ? t.text : null);
                }
                return _fields;
            }
        }

        /// <summary>"1.0.214" (bundleVersion plus the commit count), or "dev" without a stamp.</summary>
        public static string Version => Get("version", "dev");

        /// <summary>Short commit id the player was built from, "+dirty" when the tree had local changes; "" without a stamp.</summary>
        public static string Commit => Get("commit", "") + (Get("dirty", "0") == "1" ? "+dirty" : "");

        /// <summary>Build time, UTC ISO-8601; "" without a stamp.</summary>
        public static string BuiltUtc => Get("built_utc", "");

        /// <summary>true when the player is a development build (profiler markers available).</summary>
        public static bool Development => Get("development", "0") == "1";

        /// <summary>The one-line label on the title screen and the F3 overlay: "v1.0.214 (73d62bb)", or "v dev".</summary>
        public static string Label => Format(Version, Commit, Development);

        static string Get(string key, string fallback) =>
            Fields.TryGetValue(key, out string v) && !string.IsNullOrEmpty(v) ? v : fallback;

        /// <summary>The label for a version and commit (pure; tests).</summary>
        public static string Format(string version, string commit, bool development)
        {
            string s = version == "dev" ? "v dev" : "v" + version;
            if (!string.IsNullOrEmpty(commit)) s += " (" + commit + ")";
            if (development) s += " dev build";
            return s;
        }

        /// <summary>key=value lines; blank lines and lines starting with # are ignored; keys are lower-cased.</summary>
        public static Dictionary<string, string> Parse(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return d;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        /// <summary>The stamp text Build.cs writes (pure; the inverse of Parse).</summary>
        public static string Serialize(string version, string commit, bool dirty, string builtUtc, string target,
            bool development)
        {
            return "# Written by CriticalMass.Editor.Build for one player build, deleted after it (DG-05).\n" +
                   "version=" + version + "\n" +
                   "commit=" + commit + "\n" +
                   "dirty=" + (dirty ? "1" : "0") + "\n" +
                   "built_utc=" + builtUtc + "\n" +
                   "target=" + target + "\n" +
                   "development=" + (development ? "1" : "0") + "\n";
        }
    }
}

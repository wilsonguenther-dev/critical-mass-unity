using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>
    /// The words the flow screens show: class names, role lines and ability names (match setup), the result titles and
    /// lines and the Kaiju outro captions (results). Read from DG-04's words pack (Resources/Lore/classes.json and
    /// results.json) when it is present, with built-in fallbacks when it is absent or a line still carries a token this
    /// screen cannot fill. Lookup order: Resources "Lore/&lt;name&gt;" (a TextAsset, for player builds), then in the editor
    /// the file under Assets/_Game/Resources/Lore.
    /// </summary>
    public static class FlowWords
    {
        [Serializable]
        public sealed class AbilityWords
        {
            public string input;
            public string name;
        }

        [Serializable]
        public sealed class ClassWords
        {
            public string id;
            public string name;
            public string role;
            public int difficulty;
            public AbilityWords[] abilities;
        }

        [Serializable]
        sealed class ClassesFile
        {
            public ClassWords[] classes;
        }

        [Serializable]
        public sealed class ResultWords
        {
            public string id;
            public string title;
            public string line;
            public string reason;
        }

        [Serializable]
        public sealed class OutroWords
        {
            public string id;
            public string caption;
            public string reason;
        }

        [Serializable]
        sealed class ResultsFile
        {
            public ResultWords[] results;
            public OutroWords[] outros;
        }

        // ---- built-in fallbacks (same meaning as the words pack) ----

        static readonly string[] FallbackRole =
        {
            "Holds the hill.", "Feeds the egg.", "Opens the roads.", "Rewrites the map.", "Builds the way across.", "Keeps the line standing.",
        };

        static readonly string[,] FallbackAbility =
        {
            { "Cleave", "Shield Up" },
            { "Staff Hook", "Root Seeds" },
            { "Pick Swing", "Barricade" },
            { "Blast", "Mine" },
            { "Hammer", "Ladder" },
            { "Mend", "Healing Wings" },
        };

        static readonly int[] FallbackDifficulty = { 1, 2, 2, 3, 3, 2 };

        static ClassWords[] _classes;
        static ResultsFile _results;
        static bool _loaded;

        /// <summary>Drop what was read (tests).</summary>
        public static void ResetForTests()
        {
            _classes = null;
            _results = null;
            _loaded = false;
        }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _classes = ParseClasses(ReadText("classes"));
            _results = ParseResultsFile(ReadText("results"));
        }

        static string ReadText(string name)
        {
            try
            {
                TextAsset ta = Resources.Load<TextAsset>("Lore/" + name);
                if (ta != null) return ta.text;
#if UNITY_EDITOR
                string path = Path.Combine(Application.dataPath, "_Game", "Resources", "Lore", name + ".json");
                if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
#endif
            }
            catch (Exception)
            {
                // fall back to the built-in words
            }
            return null;
        }

        /// <summary>Parses a classes.json text; null when absent or malformed.</summary>
        public static ClassWords[] ParseClasses(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                ClassesFile f = JsonUtility.FromJson<ClassesFile>(json);
                return f != null && f.classes != null && f.classes.Length > 0 ? f.classes : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static ResultsFile ParseResultsFile(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<ResultsFile>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static ClassWords Find(ClassId c)
        {
            Load();
            if (_classes == null) return null;
            string id = c.ToString();
            for (int i = 0; i < _classes.Length; i++)
                if (_classes[i] != null && string.Equals(_classes[i].id, id, StringComparison.OrdinalIgnoreCase)) return _classes[i];
            return null;
        }

        static bool Clean(string s) => !string.IsNullOrWhiteSpace(s) && s.IndexOf('{') < 0;

        public static string ClassName(ClassId c)
        {
            ClassWords w = Find(c);
            return (w != null && Clean(w.name) ? w.name : c == ClassId.Demolitions ? "Demolitions" : c.ToString()).ToUpperInvariant();
        }

        public static string ClassRole(ClassId c)
        {
            ClassWords w = Find(c);
            int i = (int)c;
            return w != null && Clean(w.role) ? w.role : i >= 0 && i < FallbackRole.Length ? FallbackRole[i] : string.Empty;
        }

        /// <summary>Attack and ability names, "CLEAVE  ·  SHIELD UP".</summary>
        public static string AbilityLine(ClassId c)
        {
            int i = (int)c;
            string attack = i >= 0 && i < FallbackAbility.GetLength(0) ? FallbackAbility[i, 0] : "";
            string ability = i >= 0 && i < FallbackAbility.GetLength(0) ? FallbackAbility[i, 1] : "";
            ClassWords w = Find(c);
            if (w != null && w.abilities != null)
            {
                for (int k = 0; k < w.abilities.Length; k++)
                {
                    AbilityWords a = w.abilities[k];
                    if (a == null || !Clean(a.name)) continue;
                    if (a.input == "attack") attack = a.name;
                    else if (a.input == "ability") ability = a.name;
                }
            }
            return attack + " · " + ability;   // integration batch 4: title case, one space each side: "PICK-HAMMER · BARRICADE" did not fit a 230 px card at 16 px
        }

        /// <summary>Difficulty 1..3.</summary>
        public static int Difficulty(ClassId c)
        {
            ClassWords w = Find(c);
            int i = (int)c;
            int d = w != null && w.difficulty > 0 ? w.difficulty : i >= 0 && i < FallbackDifficulty.Length ? FallbackDifficulty[i] : 1;
            return Mathf.Clamp(d, 1, 3);
        }

        /// <summary>
        /// Replaces every {key} in template with values[key]; null when the template is empty or any token is left
        /// (the caller then uses its own words).
        /// </summary>
        public static string Fill(string template, IDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template)) return null;
            var sb = new StringBuilder(template.Length + 16);
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                if (open < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }
                int close = template.IndexOf('}', open + 1);
                if (close < 0) return null;
                sb.Append(template, i, open - i);
                string key = template.Substring(open + 1, close - open - 1);
                if (values == null || !values.TryGetValue(key, out string v)) return null;
                sb.Append(v);
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>The words pack's result entry ("victory", "defeat", "draw"), or null.</summary>
        public static ResultWords Result(string id)
        {
            Load();
            if (_results == null || _results.results == null) return null;
            for (int i = 0; i < _results.results.Length; i++)
                if (_results.results[i] != null && _results.results[i].id == id) return _results.results[i];
            return null;
        }

        /// <summary>The words pack's Kaiju outro ("azure", "crimson", "neutral", "berserk"), or null.</summary>
        public static OutroWords Outro(string id)
        {
            Load();
            if (_results == null || _results.outros == null) return null;
            for (int i = 0; i < _results.outros.Length; i++)
                if (_results.outros[i] != null && _results.outros[i].id == id) return _results.outros[i];
            return null;
        }
    }
}

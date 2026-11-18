using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>
    /// b15 tools (one tuning index): every tuning field reachable from GameConfig, found by reflection (nothing is moved or renamed),
    /// with its value in the game's config (GameConfig.CreateDefault), the file that declares it and the one-line meaning from its
    /// doc comment. docs/balance/TUNING-INDEX.md is generated from this; TuningIndexTests fails when a field has no entry there.
    /// </summary>
    public static class TuningIndex
    {
        public const string IndexPath = "docs/balance/TUNING-INDEX.md";
        public const string SourceRoot = "Assets/_Game/Sim";
        public const string Regenerate = "tools/balance/balance-report.sh tuning-index (or the balance-report workflow, mode tuning-index)";

        public sealed class Entry
        {
            public string Path = "", Type = "", Value = "", Owner = "", File = "", Meaning = "";
        }

        static readonly string ConfigNamespace = typeof(GameConfig).Namespace;

        public static List<Entry> Collect(GameConfig cfg, string sourceRoot = SourceRoot)
        {
            var docs = ScanSources(sourceRoot);
            var list = new List<Entry>();
            Walk(cfg, "", list, docs, 0);
            return list;
        }

        static bool IsConfigClass(Type t) => t != null && t.IsClass && t != typeof(string) && !t.IsArray && t.Namespace == ConfigNamespace;

        static void Walk(object obj, string prefix, List<Entry> list, Dictionary<string, KeyValuePair<string, string>> docs, int depth)
        {
            if (obj == null || depth > 8) return;
            Type t = obj.GetType();
            var fields = new List<FieldInfo>(t.GetFields(BindingFlags.Public | BindingFlags.Instance));
            fields.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            foreach (FieldInfo f in fields)
            {
                string path = prefix.Length > 0 ? prefix + "." + f.Name : f.Name;
                object v = f.GetValue(obj);
                Type ft = f.FieldType;
                if (IsConfigClass(ft) && v != null)
                {
                    Walk(v, path, list, docs, depth + 1);
                    continue;
                }
                if (ft.IsArray && IsConfigClass(ft.GetElementType()) && v is Array arr)
                {
                    for (int i = 0; i < arr.Length; i++) Walk(arr.GetValue(i), path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", list, docs, depth + 1);
                    continue;
                }
                docs.TryGetValue(t.Name + "." + f.Name, out KeyValuePair<string, string> doc);
                list.Add(new Entry
                {
                    Path = path,
                    Type = TypeName(ft),
                    Value = Format(v),
                    Owner = t.Name,
                    File = doc.Key ?? "",
                    Meaning = doc.Value ?? "",
                });
            }
        }

        static string TypeName(Type t)
        {
            if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
            if (t == typeof(double)) return "double";
            if (t == typeof(int)) return "int";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(float)) return "float";
            if (t == typeof(long)) return "long";
            return t.Name;
        }

        public static string Format(object v)
        {
            if (v == null) return "null";
            if (v is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            if (v is float fl) return fl.ToString("R", CultureInfo.InvariantCulture);
            if (v is bool b) return b ? "true" : "false";
            if (v is string s) return "\"" + s + "\"";
            if (v is Array a)
            {
                var sb = new StringBuilder("[");
                int shown = Math.Min(a.Length, 16);
                for (int i = 0; i < shown; i++) sb.Append(i > 0 ? ", " : "").Append(Format(a.GetValue(i)));
                if (a.Length > shown) sb.Append(", ... (" + a.Length + " items)");
                return sb.Append(']').ToString();
            }
            if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        static readonly Regex ClassRx = new Regex(@"\b(?:class|struct)\s+(\w+)", RegexOptions.Compiled);
        static readonly Regex FieldRx = new Regex(@"^\s*public\s+(?!static\b|const\b|override\b|sealed\b|class\b|struct\b|enum\b|abstract\b)(?:readonly\s+)?[\w\.\[\]<>,\? ]+?\s+(\w+)\s*(=|;)", RegexOptions.Compiled);
        static readonly Regex TagRx = new Regex(@"<see\s+cref=""([^""]+)""\s*/>|<[^>]+>", RegexOptions.Compiled);

        /// <summary>"Class.Field" to (file, meaning) from every .cs under root: the /// summary above a public field, else its // line comment.</summary>
        public static Dictionary<string, KeyValuePair<string, string>> ScanSources(string root)
        {
            var map = new Dictionary<string, KeyValuePair<string, string>>();
            if (!Directory.Exists(root)) return map;
            var files = new List<string>(Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories));
            files.Sort(StringComparer.Ordinal);
            foreach (string file in files)
            {
                string rel = file.Replace('\\', '/');
                string cls = "";
                var doc = new StringBuilder();
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("///", StringComparison.Ordinal))
                    {
                        doc.Append(' ').Append(line.Substring(3));
                        continue;
                    }
                    Match cm = ClassRx.Match(line);
                    if (cm.Success && !line.StartsWith("//", StringComparison.Ordinal) && (line.Contains(" class ") || line.StartsWith("class ", StringComparison.Ordinal) || line.Contains(" struct ")))
                    {
                        cls = cm.Groups[1].Value;
                        doc.Clear();
                        continue;
                    }
                    Match fm = FieldRx.Match(raw);
                    if (fm.Success && cls.Length > 0)
                    {
                        string meaning = Clean(doc.ToString());
                        int sl = raw.IndexOf("//", StringComparison.Ordinal);
                        if (meaning.Length == 0 && sl > 0) meaning = Clean(raw.Substring(sl + 2));
                        string key = cls + "." + fm.Groups[1].Value;
                        if (!map.ContainsKey(key)) map[key] = new KeyValuePair<string, string>(rel, meaning);
                        if (raw.IndexOf('{') < 0)
                        {
                            // "public double A = 1, B = 2;" declares B as well
                            foreach (Match extra in Regex.Matches(raw.Substring(fm.Index + fm.Length), @",\s*(\w+)\s*(=|;|,)"))
                            {
                                string k2 = cls + "." + extra.Groups[1].Value;
                                if (!map.ContainsKey(k2)) map[k2] = new KeyValuePair<string, string>(rel, meaning);
                            }
                        }
                    }
                    if (line.Length > 0 && !line.StartsWith("[", StringComparison.Ordinal)) doc.Clear();
                }
            }
            return map;
        }

        static string Clean(string s)
        {
            s = TagRx.Replace(s, m => m.Groups[1].Success ? m.Groups[1].Value.Replace("T:", "").Replace("F:", "") : " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length > 220)
            {
                int cut = s.IndexOf(". ", 60, StringComparison.Ordinal);
                s = cut > 0 && cut < 220 ? s.Substring(0, cut + 1) : s.Substring(0, 217) + "...";
            }
            return s;
        }

        public static string Markdown(List<Entry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("# Tuning index\n\n");
            sb.Append("Every tuning field the sim reads from `GameConfig`, found by reflection (`Assets/_Game/Sim.Tests/Harness/TuningIndex.cs`). ")
              .Append("Values are the game's config (`GameConfig.CreateDefault()`, the Full rules) when this file was generated; the meaning is the ")
              .Append("field's doc comment. Nothing was moved or renamed: the dials stay in their own config classes.\n\n");
            sb.Append("Regenerate: `").Append(Regenerate).Append("`. `TuningIndexTests` fails when a field has no row here. ")
              .Append("Paths work as `--set Path=Value` switches for the balance report.\n\n");
            sb.Append(entries.Count).Append(" fields.\n");
            string group = null;
            foreach (Entry e in entries)
            {
                int dot = e.Path.IndexOf('.');
                string g = dot > 0 ? e.Path.Substring(0, dot) : "(top level)";
                int br = g.IndexOf('[');
                if (br > 0) g = g.Substring(0, br);
                if (g != group)
                {
                    group = g;
                    sb.Append("\n## ").Append(g).Append("\n\n| Path | Value | Type | File | Meaning |\n|---|---|---|---|---|\n");
                }
                sb.Append("| `").Append(e.Path).Append("` | ").Append(BalanceFormat.Cell(e.Value)).Append(" | ").Append(e.Type).Append(" | ")
                  .Append(e.File.Length > 0 ? "`" + e.File + "`" : "").Append(" | ").Append(BalanceFormat.Cell(e.Meaning)).Append(" |\n");
            }
            return sb.ToString();
        }

        /// <summary>The paths listed in an index file (the first back-quoted cell of each table row).</summary>
        public static HashSet<string> PathsIn(string markdown)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(markdown, @"^\| `([^`]+)` \|", RegexOptions.Multiline)) set.Add(m.Groups[1].Value);
            return set;
        }
    }
}

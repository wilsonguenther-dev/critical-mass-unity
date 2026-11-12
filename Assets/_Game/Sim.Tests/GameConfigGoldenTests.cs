using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// F-01b (U-036): GameConfig.CreateBrowser5v5() checked against the browser file itself, through the golden file
    /// tools/golden/game-config-09e0904.json (every leaf of GAME_CONFIG in src/config/game.config.ts at 09e0904).
    /// New, no vitest twin. No tunable is typed into this test.
    /// </summary>
    [TestFixture]
    public sealed class GameConfigGoldenTests
    {
        /// <summary>Golden file, relative to the project root (the test runner's working directory).</summary>
        private const string GoldenPath = "tools/golden/game-config-09e0904.json";

        private static readonly Regex LeafRx = new Regex(
            @"""([^""]+)"":\s*(true|false|""[^""]*""|-?[0-9][0-9.eE+-]*)", RegexOptions.CultureInvariant);

        private static readonly Regex SegmentRx = new Regex(@"\[([0-9]+)\]|([^.\[\]]+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Whole-prefix rewrites (browser path, C# path) for every leaf whose C# name in F-01's Numbers table differs from
        /// the PascalCase rule. F-01's table (loop/items/F-01.md, "GameConfig field" against "Browser key") names every
        /// field as the PascalCase of its browser key, so no row differs and the list is empty.
        /// </summary>
        private static readonly (string ts, string cs)[] Renames = Array.Empty<(string ts, string cs)>();

        /// <summary>
        /// Golden paths F-01 states are not ported to GameConfig. F-01's Numbers section says "Every row of the table
        /// above" and its table lists every GAME_CONFIG group at 09e0904, so F-01 excludes no leaf and the list is empty.
        /// </summary>
        private static readonly string[] NotPorted = Array.Empty<string>();

        private static readonly string[] ClassNames = { "warrior", "farmer", "miner", "demolitions", "builder" };
        private static readonly string[] TeamNames = { "azure", "crimson" };

        // ---------------------------------------------------------------- golden file

        private static string ReadGolden()
        {
            Assert.IsTrue(File.Exists(GoldenPath), "golden file missing: " + Path.GetFullPath(GoldenPath));
            return File.ReadAllText(GoldenPath);
        }

        /// <summary>The members of the "leaves" object, in file order, as (path, golden value).</summary>
        private static List<KeyValuePair<string, object>> ParseLeaves(string text)
        {
            int at = text.IndexOf("\"leaves\":", StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, "golden file has no \"leaves\" object");
            string after = text.Substring(at + "\"leaves\":".Length);
            var leaves = new List<KeyValuePair<string, object>>();
            foreach (Match m in LeafRx.Matches(after))
            {
                leaves.Add(new KeyValuePair<string, object>(m.Groups[1].Value, ParseValue(m.Groups[2].Value)));
            }
            return leaves;
        }

        private static object ParseValue(string s)
        {
            if (s == "true") return true;
            if (s == "false") return false;
            if (s.Length >= 2 && s[0] == '"') return s.Substring(1, s.Length - 2);
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- resolver

        /// <summary>Applies the first Renames entry whose browser prefix covers the path; returns its index or -1.</summary>
        private static int ApplyRenames(string path, out string rewritten)
        {
            for (int r = 0; r < Renames.Length; r++)
            {
                string ts = Renames[r].ts;
                if (path == ts || path.StartsWith(ts + ".", StringComparison.Ordinal)
                    || path.StartsWith(ts + "[", StringComparison.Ordinal))
                {
                    rewritten = Renames[r].cs + path.Substring(ts.Length);
                    return r;
                }
            }
            rewritten = path;
            return -1;
        }

        private static string Pascal(string s)
        {
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>
        /// Walks a golden path down a GameConfig. On success value is the member's value and why is the C# path it was
        /// resolved through; on failure why says which segment could not be read.
        /// </summary>
        private static bool TryResolve(GameConfig c, string path, out object value, out string why)
        {
            value = null;
            ApplyRenames(path, out string p);
            object cur = c;
            var csPath = new StringBuilder("GameConfig");
            MatchCollection segs = SegmentRx.Matches(p);
            if (segs.Count == 0)
            {
                why = "empty path";
                return false;
            }
            foreach (Match seg in segs)
            {
                if (cur == null)
                {
                    why = "null at " + csPath;
                    return false;
                }
                if (seg.Groups[1].Success)
                {
                    int i = int.Parse(seg.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                    if (!(cur is IList list))
                    {
                        why = csPath + " is not a list (index [" + i + "])";
                        return false;
                    }
                    if (i >= list.Count)
                    {
                        why = csPath + " has " + list.Count + " elements (index [" + i + "])";
                        return false;
                    }
                    cur = list[i];
                    csPath.Append('[').Append(i).Append(']');
                    continue;
                }

                string name = seg.Groups[2].Value;
                int cls = Array.IndexOf(ClassNames, name);
                if (cls >= 0 && cur is IList classList)
                {
                    var id = (ClassId)Enum.Parse(typeof(ClassId), Pascal(name));
                    if ((int)id >= classList.Count)
                    {
                        why = csPath + " has " + classList.Count + " elements (class " + id + ")";
                        return false;
                    }
                    cur = classList[(int)id];
                    csPath.Append('[').Append(id).Append(']');
                    continue;
                }
                int team = Array.IndexOf(TeamNames, name);
                if (team >= 0 && cur is PerTeam perTeam)
                {
                    var id = (TeamId)Enum.Parse(typeof(TeamId), Pascal(name));
                    cur = perTeam[id];
                    csPath.Append('[').Append(id).Append(']');
                    continue;
                }

                string member = Pascal(name);
                Type t = cur.GetType();
                FieldInfo f = t.GetField(member, BindingFlags.Public | BindingFlags.Instance);
                if (f != null)
                {
                    cur = f.GetValue(cur);
                }
                else
                {
                    PropertyInfo prop = t.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
                    if (prop == null || prop.GetIndexParameters().Length != 0 || !prop.CanRead)
                    {
                        why = t.Name + " has no public field or property " + member + " (at " + csPath + ")";
                        return false;
                    }
                    cur = prop.GetValue(cur);
                }
                csPath.Append('.').Append(member);
            }
            value = cur;
            why = csPath.ToString();
            return true;
        }

        private static bool ValueEquals(object golden, object cs)
        {
            if (golden is double g)
            {
                if (cs is int i) return (double)i == g;
                if (cs is double d) return d == g;
                return false;
            }
            if (golden is bool gb) return cs is bool b && b == gb;
            if (golden is string gs) return cs is string s && s == gs;
            return false;
        }

        private static string Show(object v)
        {
            if (v == null) return "null";
            if (v is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture) + " (" + v.GetType().Name + ")";
            return v + " (" + v.GetType().Name + ")";
        }

        /// <summary>Every golden leaf not in NotPorted that does not resolve or does not equal its golden value.</summary>
        private static List<KeyValuePair<string, string>> Misses(GameConfig c, List<KeyValuePair<string, object>> leaves)
        {
            var misses = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, object> leaf in leaves)
            {
                if (Array.IndexOf(NotPorted, leaf.Key) >= 0) continue;
                if (!TryResolve(c, leaf.Key, out object value, out string why))
                {
                    misses.Add(new KeyValuePair<string, string>(leaf.Key,
                        leaf.Key + ": golden " + Show(leaf.Value) + ", C# unresolved (resolved as -: " + why + ")"));
                    continue;
                }
                if (!ValueEquals(leaf.Value, value))
                {
                    misses.Add(new KeyValuePair<string, string>(leaf.Key,
                        leaf.Key + ": golden " + Show(leaf.Value) + ", C# " + Show(value) + " (resolved as " + why + ")"));
                }
            }
            return misses;
        }

        private static string Join(List<KeyValuePair<string, string>> misses)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> m in misses) sb.Append('\n').Append(m.Value);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- tests

        [Test]
        public void EveryGoldenLeafMatchesCreateBrowser5v5()
        {
            List<KeyValuePair<string, object>> leaves = ParseLeaves(ReadGolden());
            Assert.Greater(leaves.Count, 0, "no golden leaves parsed");

            List<KeyValuePair<string, string>> misses = Misses(GameConfig.CreateBrowser5v5(), leaves);
            Assert.AreEqual(0, misses.Count, misses.Count + " golden leaf miss(es):" + Join(misses));

            var stale = new List<string>();
            foreach (string np in NotPorted)
            {
                if (TryResolve(GameConfig.CreateBrowser5v5(), np, out _, out string why)) stale.Add(np + " (resolves as " + why + ")");
            }
            Assert.AreEqual(0, stale.Count, "NotPorted paths that now resolve: " + string.Join(", ", stale));

            var used = new bool[Renames.Length];
            foreach (KeyValuePair<string, object> leaf in leaves)
            {
                int r = ApplyRenames(leaf.Key, out _);
                if (r >= 0) used[r] = true;
            }
            var unused = new List<string>();
            for (int r = 0; r < Renames.Length; r++)
            {
                if (!used[r]) unused.Add(Renames[r].ts + " -> " + Renames[r].cs);
            }
            Assert.AreEqual(0, unused.Count, "Renames entries no golden leaf uses: " + string.Join(", ", unused));
        }

        [Test]
        public void GoldenFileIsGameConfigTsAt09e0904With171Leaves()
        {
            string text = ReadGolden();
            int at = text.IndexOf("\"leaves\":", StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, "golden file has no \"leaves\" object");
            string head = text.Substring(0, at);

            Match commit = Regex.Match(head, @"""commit"":\s*""([^""]*)""");
            Assert.IsTrue(commit.Success, "no \"commit\" member");
            Assert.AreEqual("09e0904", commit.Groups[1].Value);

            Match source = Regex.Match(head, @"""source"":\s*""([^""]*)""");
            Assert.IsTrue(source.Success, "no \"source\" member");
            Assert.AreEqual("src/config/game.config.ts", source.Groups[1].Value);

            Match count = Regex.Match(head, @"""leafCount"":\s*([0-9]+)");
            Assert.IsTrue(count.Success, "no \"leafCount\" member");
            Assert.AreEqual(171, int.Parse(count.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture));

            Assert.AreEqual(171, ParseLeaves(text).Count);
        }

        [Test]
        public void TheResolverCatchesAWrongValue()
        {
            List<KeyValuePair<string, object>> leaves = ParseLeaves(ReadGolden());
            GameConfig c = GameConfig.CreateBrowser5v5();
            c.SimHz = 31;

            List<KeyValuePair<string, string>> misses = Misses(c, leaves);
            Assert.AreEqual(1, misses.Count, "expected exactly one miss:" + Join(misses));
            Assert.AreEqual("simHz", misses[0].Key, misses[0].Value);
        }
    }
}

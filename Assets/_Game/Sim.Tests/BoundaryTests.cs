using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/boundary.test.ts: every file under Assets/_Game/Sim (Bots included) stays pure C#, with no
    /// engine, editor, wall-clock, global-randomness or threading use. Paths are relative to the project root, which is
    /// the EditMode working directory.
    /// </summary>
    [TestFixture]
    public sealed class BoundaryTests
    {
        private const string SimDir = "Assets/_Game/Sim";
        private const string SimAsmdef = "Assets/_Game/Sim/CriticalMass.Sim.asmdef";

        private static readonly KeyValuePair<string, Regex>[] Banned =
        {
            new KeyValuePair<string, Regex>("using UnityEngine", new Regex(@"\busing\s+UnityEngine\b")),
            new KeyValuePair<string, Regex>("using UnityEditor", new Regex(@"\busing\s+UnityEditor\b")),
            new KeyValuePair<string, Regex>("UnityEngine.", new Regex(@"\bUnityEngine\s*\.")),
            new KeyValuePair<string, Regex>("System.Random", new Regex(@"\bSystem\s*\.\s*Random\b")),
            new KeyValuePair<string, Regex>("DateTime.Now", new Regex(@"\bDateTime\s*\.\s*Now\b")),
            new KeyValuePair<string, Regex>("DateTime.UtcNow", new Regex(@"\bDateTime\s*\.\s*UtcNow\b")),
            new KeyValuePair<string, Regex>("Stopwatch", new Regex(@"\bStopwatch\b")),
            new KeyValuePair<string, Regex>("Environment.TickCount", new Regex(@"\bEnvironment\s*\.\s*TickCount")),
            new KeyValuePair<string, Regex>("Guid.NewGuid", new Regex(@"\bGuid\s*\.\s*NewGuid\b")),
            new KeyValuePair<string, Regex>("System.Threading", new Regex(@"\bSystem\s*\.\s*Threading\b")),
        };

        private static readonly Regex BlockComment = new Regex(@"/\*[\s\S]*?\*/");
        private static readonly Regex LineComment = new Regex(@"(^|[^:""'`])//.*$", RegexOptions.Multiline);

        private static List<string> ListCs()
        {
            var files = new List<string>();
            if (!Directory.Exists(SimDir)) return files;
            foreach (string p in Directory.GetFiles(SimDir, "*.cs", SearchOption.AllDirectories))
                files.Add(p.Replace('\\', '/'));
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        private static string StripComments(string src)
        {
            return LineComment.Replace(BlockComment.Replace(src, " "), "$1");
        }

        private static List<string> Check(string rel, string source)
        {
            var violations = new List<string>();
            string code = StripComments(source);
            foreach (KeyValuePair<string, Regex> b in Banned)
                if (b.Value.IsMatch(code)) violations.Add(rel + ": uses " + b.Key);
            return violations;
        }

        [Test]
        public void ScansARealSetOfFilesNeverPassesVacuously()
        {
            Assert.GreaterOrEqual(ListCs().Count, 12);
        }

        [Test]
        public void NoSimOrBotsFileImportsReactThreeNextZustandGameUiOrUsesWallClockOrGlobalRandomness()
        {
            var violations = new List<string>();
            foreach (string f in ListCs()) violations.AddRange(Check(f, File.ReadAllText(f)));
            CollectionAssert.IsEmpty(violations, string.Join("\n", violations));

            Assert.IsTrue(File.Exists(SimAsmdef), SimAsmdef + " is missing");
            StringAssert.Contains("\"noEngineReferences\": true", File.ReadAllText(SimAsmdef));
        }

        [Test]
        public void TheGuardItselfCatchesAViolation()
        {
            List<string> violations = Check("inline", "var r = new System.Random();");
            Assert.AreEqual(1, violations.Count);
        }
    }
}

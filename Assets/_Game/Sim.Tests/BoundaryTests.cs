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

        // F-02c (U-032): transcendentals go through JsMath (fdlibm), never System.Math / MathF.
        private static readonly KeyValuePair<string, Regex>[] Transcendental =
        {
            new KeyValuePair<string, Regex>("Math.Sin/Cos/Atan2/Exp", new Regex(@"\bMath\.(Sin|Cos|Atan2|Exp)\s*\(")),
            new KeyValuePair<string, Regex>("MathF.Sin/Cos/Atan2/Exp", new Regex(@"\bMathF\.(Sin|Cos|Atan2|Exp)\s*\(")),
            new KeyValuePair<string, Regex>("using static System.Math", new Regex(@"using\s+static\s+System\.Math\b")),
            // U-049: fused multiply-add only at node's fused sites, inside Fdlibm.cs (its software Fma); never
            // System.Math's (absent at this API level) and never anywhere else in the Sim.
            new KeyValuePair<string, Regex>("FusedMultiplyAdd", new Regex(@"\bFusedMultiplyAdd\b")),
            new KeyValuePair<string, Regex>(FmaRule, new Regex(@"\bFma\s*\(")),
        };

        private const string FmaRule = "Fma( outside Fdlibm.cs";
        private const string FdlibmFile = "Assets/_Game/Sim/Core/Fdlibm.cs";

        // F-02c (review R20261112T1705-1-1): unqualified randomness, the clock, per-process hash codes.
        private static readonly KeyValuePair<string, Regex>[] Unqualified =
        {
            new KeyValuePair<string, Regex>("new Random(", new Regex(@"\bnew\s+Random\s*\(")),
            new KeyValuePair<string, Regex>("Random.Shared", new Regex(@"\bRandom\s*\.\s*Shared\b")),
            new KeyValuePair<string, Regex>("DateTimeOffset", new Regex(@"\bDateTimeOffset\b")),
            new KeyValuePair<string, Regex>("DateTime.Today", new Regex(@"\bDateTime\s*\.\s*Today\b")),
            new KeyValuePair<string, Regex>("GetHashCode(", new Regex(@"\bGetHashCode\s*\(")),
        };

        // F-02c (review R20261112T1705-1-6): raw team-enum casts; SimEnumMap is the one place that maps them.
        private const string SimEnumMapFile = "Assets/_Game/Sim/Core/SimEnumMap.cs";

        private static readonly KeyValuePair<string, Regex>[] TeamEnumCasts =
        {
            new KeyValuePair<string, Regex>("cast to a team enum", new Regex(@"\(\s*(MatchResult|HillState|KaijuState)\s*\)")),
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

        private static List<string> CheckRules(string rel, string source, KeyValuePair<string, Regex>[] rules)
        {
            var violations = new List<string>();
            if (rules == TeamEnumCasts && rel == SimEnumMapFile) return violations;
            string code = StripComments(source);
            foreach (KeyValuePair<string, Regex> b in rules)
            {
                if (b.Key == FmaRule && rel == FdlibmFile) continue;
                if (b.Value.IsMatch(code)) violations.Add(rel + ": uses " + b.Key);
            }
            return violations;
        }

        /// <summary>The F-02c guard: the three extended rule sets on one source.</summary>
        private static List<string> CheckExtended(string rel, string source)
        {
            var violations = new List<string>();
            violations.AddRange(CheckRules(rel, source, Transcendental));
            violations.AddRange(CheckRules(rel, source, Unqualified));
            violations.AddRange(CheckRules(rel, source, TeamEnumCasts));
            return violations;
        }

        private static void AssertNoSimFileBreaks(KeyValuePair<string, Regex>[] rules)
        {
            List<string> files = ListCs();
            Assert.GreaterOrEqual(files.Count, 12);
            var violations = new List<string>();
            foreach (string f in files) violations.AddRange(CheckRules(f, File.ReadAllText(f), rules));
            CollectionAssert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void NoSimFileCallsSystemMathSinCosAtan2Exp()
        {
            AssertNoSimFileBreaks(Transcendental);
        }

        [Test]
        public void NoSimFileUsesUnqualifiedRandomTheClockOrGetHashCode()
        {
            AssertNoSimFileBreaks(Unqualified);
        }

        [Test]
        public void NoSimFileCastsBetweenTeamEnums()
        {
            AssertNoSimFileBreaks(TeamEnumCasts);
        }

        [Test]
        public void TheExtendedGuardCatchesEachNewViolation()
        {
            string[] bad =
            {
                "var a = Math.Atan2(z, x);",
                "using static System.Math;",
                "var r = new Random(1);",
                "var r = Random.Shared;",
                "var t = DateTimeOffset.Now;",
                "var d = DateTime.Today;",
                "int h = o.GetHashCode();",
                "w.Match.Winner = (MatchResult)(int)team;",
                "var f = Math.FusedMultiplyAdd(a, b, c);",
                "var f = Fdlibm.Fma(a, b, c);",
            };
            foreach (string s in bad)
                Assert.AreEqual(1, CheckExtended("inline", s).Count, s);
            Assert.AreEqual(0, CheckExtended("inline", "var a = JsMath.Atan2(z, x);").Count);
            Assert.AreEqual(0, CheckExtended(FdlibmFile, "r = Fma(-fn, pio2_1, t);").Count, "Fdlibm.cs may call Fma");
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

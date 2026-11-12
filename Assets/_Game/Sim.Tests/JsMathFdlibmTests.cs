using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// JsMath.Sin, Cos, Atan2, Exp against node v24.11.1 bit for bit (U-032), over the committed golden file
    /// tools/golden/jsmath-fdlibm.json. Paths are relative to the project root, the EditMode working directory.
    /// New, no vitest twin.
    /// </summary>
    [TestFixture]
    public sealed class JsMathFdlibmTests
    {
        private const string GoldenPath = "tools/golden/jsmath-fdlibm.json";

        private static readonly Regex Vector =
            new Regex(@"\[""([0-9a-f]{16})"",""([0-9a-f]{16})""(?:,""([0-9a-f]{16})"")?\]");

        private static string ReadGolden()
        {
            Assert.IsTrue(File.Exists(GoldenPath), GoldenPath + " is missing");
            return File.ReadAllText(GoldenPath);
        }

        private static string Slice(string json, string fromKey, string toKey)
        {
            int start = json.IndexOf("\"" + fromKey + "\":", StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, "key " + fromKey + " missing");
            start += fromKey.Length + 3;
            if (toKey == null) return json.Substring(start);
            int end = json.IndexOf("\"" + toKey + "\":", start, StringComparison.Ordinal);
            Assert.GreaterOrEqual(end, 0, "key " + toKey + " missing");
            return json.Substring(start, end - start);
        }

        private static string SliceOf(string json, string fn)
        {
            switch (fn)
            {
                case "sin": return Slice(json, "sin", "cos");
                case "cos": return Slice(json, "cos", "exp");
                case "exp": return Slice(json, "exp", "atan2");
                case "atan2": return Slice(json, "atan2", null);
                default: throw new ArgumentException(fn);
            }
        }

        private static List<string[]> Vectors(string fn)
        {
            var list = new List<string[]>();
            foreach (System.Text.RegularExpressions.Match m in Vector.Matches(SliceOf(ReadGolden(), fn)))
            {
                list.Add(m.Groups[3].Success
                    ? new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value }
                    : new[] { m.Groups[1].Value, m.Groups[2].Value });
            }
            return list;
        }

        private static double FromHex(string h)
        {
            return BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64(h, 16)));
        }

        private static string ToHex(double d)
        {
            return unchecked((ulong)BitConverter.DoubleToInt64Bits(d)).ToString("x16");
        }

        private static void CheckUnary(string fn, Func<double, double> f)
        {
            List<string[]> vectors = Vectors(fn);
            Assert.Greater(vectors.Count, 0, "no " + fn + " vectors");
            var mismatches = new List<string>();
            foreach (string[] v in vectors)
            {
                Assert.AreEqual(2, v.Length, fn + " vector with a second input");
                double expected = FromHex(v[1]);
                double actual = f(FromHex(v[0]));
                bool ok = double.IsNaN(expected)
                    ? double.IsNaN(actual)
                    : BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual);
                if (!ok) mismatches.Add(v[0] + " -> " + v[1] + ", got " + ToHex(actual));
            }
            Report(fn, mismatches);
        }

        private static void Report(string fn, List<string> mismatches)
        {
            if (mismatches.Count == 0) return;
            var sb = new StringBuilder();
            sb.Append(fn).Append(": ").Append(mismatches.Count).Append(" mismatches; first 20:\n");
            for (int i = 0; i < mismatches.Count && i < 20; i++) sb.Append(mismatches[i]).Append('\n');
            Assert.Fail(sb.ToString());
        }

        [Test]
        public void SinMatchesNodeBitForBitOnEveryGoldenVector()
        {
            CheckUnary("sin", JsMath.Sin);
        }

        [Test]
        public void CosMatchesNodeBitForBitOnEveryGoldenVector()
        {
            CheckUnary("cos", JsMath.Cos);
        }

        [Test]
        public void Atan2MatchesNodeBitForBitOnEveryGoldenVector()
        {
            List<string[]> vectors = Vectors("atan2");
            Assert.Greater(vectors.Count, 0, "no atan2 vectors");
            var mismatches = new List<string>();
            foreach (string[] v in vectors)
            {
                Assert.AreEqual(3, v.Length, "atan2 vector without two inputs");
                double expected = FromHex(v[2]);
                double actual = JsMath.Atan2(FromHex(v[0]), FromHex(v[1]));
                bool ok = double.IsNaN(expected)
                    ? double.IsNaN(actual)
                    : BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual);
                if (!ok) mismatches.Add("(" + v[0] + ", " + v[1] + ") -> " + v[2] + ", got " + ToHex(actual));
            }
            Report("atan2", mismatches);
        }

        [Test]
        public void ExpMatchesNodeBitForBitOnEveryGoldenVector()
        {
            CheckUnary("exp", JsMath.Exp);
        }

        [Test]
        public void GoldenFileIsTheCommittedNodeRunAndCoversEveryFunction()
        {
            string json = ReadGolden();
            System.Text.RegularExpressions.Match node = Regex.Match(json, @"""node"":""([^""]*)""");
            System.Text.RegularExpressions.Match v8 = Regex.Match(json, @"""v8"":""([^""]*)""");
            Assert.IsTrue(node.Success, "node key missing");
            Assert.IsTrue(v8.Success, "v8 key missing");
            Assert.AreEqual("v24.11.1", node.Groups[1].Value);
            Assert.AreEqual("13.6.233.10-node.28", v8.Groups[1].Value);

            Assert.AreEqual(2038, Vectors("sin").Count, "sin vectors");
            Assert.AreEqual(2038, Vectors("cos").Count, "cos vectors");
            Assert.AreEqual(2029, Vectors("exp").Count, "exp vectors");
            Assert.AreEqual(2081, Vectors("atan2").Count, "atan2 vectors");
        }
    }
}

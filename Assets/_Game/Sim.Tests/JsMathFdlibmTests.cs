using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// JsMath.Sin, Cos, Atan2, Exp against node v24.11.1 bit for bit (U-032), over the committed golden file
    /// tools/golden/jsmath-fdlibm.json. Paths are relative to the project root, the EditMode working directory.
    /// New, no vitest twin. U-049: the oracle is node v24.11.1 arm64 (FMA-contracted fdlibm); the second, seeded set
    /// tools/golden/jsmath-fdlibm-large.json (1,003,479 vectors from tools/golden/gen-jsmath-large.mjs, committed as
    /// hashes) is rebuilt here input for input and checked by hash.
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

        private const string LargePath = "tools/golden/jsmath-fdlibm-large.json";

        [Test]
        public void LargeSeededSetMatchesNodeHashes()
        {
            Assert.IsTrue(File.Exists(LargePath), LargePath + " is missing");
            string json = File.ReadAllText(LargePath);
            Assert.AreEqual("v24.11.1", Field(json, "node"), "large set node");
            Assert.AreEqual("arm64", Field(json, "arch"), "large set arch");
            Assert.AreEqual(LargeSet.Seed.ToString(), Regex.Match(json, @"""seed"":\s*(\d+)").Groups[1].Value, "seed");

            var g = new LargeSet();
            double[] sinIn = g.SinInputs(), expIn = g.ExpInputs(), atan2In = g.Atan2Inputs();
            var problems = new List<string>();
            Expect(problems, json, "counts", "sin", sinIn.Length.ToString());
            Expect(problems, json, "counts", "cos", sinIn.Length.ToString());
            Expect(problems, json, "counts", "exp", expIn.Length.ToString());
            Expect(problems, json, "counts", "atan2", (atan2In.Length / 2).ToString());
            Expect(problems, json, "inputs", "sin", Sha(sinIn));
            Expect(problems, json, "inputs", "exp", Sha(expIn));
            Expect(problems, json, "inputs", "atan2", Sha(atan2In));
            Assert.IsEmpty(problems, "the C# generator drifted from gen-jsmath-large.mjs: " + string.Join("; ", problems));

            var sinOut = new double[sinIn.Length];
            var cosOut = new double[sinIn.Length];
            for (int i = 0; i < sinIn.Length; i++)
            {
                sinOut[i] = JsMath.Sin(sinIn[i]);
                cosOut[i] = JsMath.Cos(sinIn[i]);
            }
            var expOut = new double[expIn.Length];
            for (int i = 0; i < expIn.Length; i++) expOut[i] = JsMath.Exp(expIn[i]);
            var atan2Out = new double[atan2In.Length / 2];
            for (int i = 0; i < atan2Out.Length; i++) atan2Out[i] = JsMath.Atan2(atan2In[2 * i], atan2In[2 * i + 1]);
            Expect(problems, json, "outputs", "sin", Sha(sinOut));
            Expect(problems, json, "outputs", "cos", Sha(cosOut));
            Expect(problems, json, "outputs", "exp", Sha(expOut));
            Expect(problems, json, "outputs", "atan2", Sha(atan2Out));
            Assert.IsEmpty(problems, "output hash differs from node (rerun gen-jsmath-large.mjs --full and "
                + "tools/golden/fdlibm_replay.py --large to list the vectors): " + string.Join("; ", problems));
        }

        private static string Field(string json, string key)
        {
            return Regex.Match(json, "\"" + key + "\":\\s*\"([^\"]*)\"").Groups[1].Value;
        }

        private static void Expect(List<string> problems, string json, string section, string key, string actual)
        {
            System.Text.RegularExpressions.Match sec = Regex.Match(json, "\"" + section + "\":\\s*\\{([^}]*)\\}");
            System.Text.RegularExpressions.Match m =
                Regex.Match(sec.Groups[1].Value, "\"" + key + "\":\\s*\"?([0-9a-f]+)\"?");
            if (!sec.Success || !m.Success) problems.Add(section + "." + key + " missing");
            else if (m.Groups[1].Value != actual) problems.Add(section + "." + key + " " + m.Groups[1].Value + " != " + actual);
        }

        private static string Sha(double[] values)
        {
            var bytes = new byte[values.Length * 8];
            for (int i = 0; i < values.Length; i++)
            {
                ulong b = double.IsNaN(values[i])
                    ? 0x7FF8000000000000UL
                    : unchecked((ulong)BitConverter.DoubleToInt64Bits(values[i]));
                for (int k = 0; k < 8; k++) bytes[i * 8 + k] = (byte)(b >> (56 - 8 * k));
            }
            using (SHA256 sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte x in sha.ComputeHash(bytes)) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Input for input the generator of tools/golden/gen-jsmath-large.mjs (same mulberry32 stream, same
        /// order of draws, same IEEE-754 operations).</summary>
        private sealed class LargeSet
        {
            public const uint Seed = 0x5eed2026;
            private const double PI = 3.141592653589793;
            private uint s = Seed;

            private uint U32()
            {
                unchecked
                {
                    s += 0x6D2B79F5;
                    uint t = s;
                    t = (t ^ (t >> 15)) * (t | 1);
                    t ^= t + (t ^ (t >> 7)) * (t | 61);
                    return t ^ (t >> 14);
                }
            }

            private double Unit() { return U32() / 4294967296.0; }
            private double Uniform(double a) { return (Unit() * 2 - 1) * a; }
            private int Below(int n) { return (int)(U32() % (uint)n); }

            private static double Words(uint hi, uint lo)
            {
                return BitConverter.Int64BitsToDouble(unchecked((long)(((ulong)hi << 32) | lo)));
            }

            private double WithExp(int e)
            {
                uint sign = U32() & 1, mhi = U32() & 0xFFFFF, lo = U32();
                return Words((sign << 31) | ((uint)e << 20) | mhi, lo);
            }

            private double RandBits()
            {
                uint hi = U32(), lo = U32();
                return Words(hi, lo);
            }

            private static double OffsetUlps(double x, long d)
            {
                long b = BitConverter.DoubleToInt64Bits(x);
                long sign = b & long.MinValue, mag = b & long.MaxValue;
                long m = mag + d;
                if (m < 0) m = 0;
                if (m > 0x7FEFFFFFFFFFFFFFL) m = 0x7FEFFFFFFFFFFFFFL;
                return BitConverter.Int64BitsToDouble(sign | m);
            }

            private static double Bits(ulong b) { return BitConverter.Int64BitsToDouble(unchecked((long)b)); }

            private static readonly double[] Specials =
            {
                0.0, -0.0, double.PositiveInfinity, double.NegativeInfinity, double.NaN, Bits(0xFFF8000000000000UL),
                Bits(0x7FF0000000000001UL), 5e-324, -5e-324, 2.2250738585072014e-308, -2.2250738585072014e-308,
                2.225073858507201e-308, 1e-300, 1e-8, -1e-8, 1, -1, 0.5, -0.5, 2, 1e300, -1e300,
                1.7976931348623157e308, -1.7976931348623157e308,
            };

            private static readonly int[] SinThresholds =
            {
                0x3E400000, 0x3E500000, 0x3FD33333, 0x3FE90000, 0x3FE921FB, 0x3FF921FB, 0x4002D97C, 0x400921FB,
                0x4012D97C, 0x413921FB, 0x41500000, 0x7FEFFFFF,
            };

            private static readonly int[] Npio2Hw =
            {
                0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB, 0x402921FB,
                0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C, 0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB,
                0x403AB41B, 0x403C463A, 0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C,
                0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB, 0x404858EB, 0x404921FB,
            };

            public double[] SinInputs()
            {
                var o = new List<double>(Specials);
                o.AddRange(new[]
                {
                    PI, -PI, PI / 2, -PI / 2, PI / 4, -PI / 4, 3 * PI / 4, 2 * PI, 1e5, 1e6, 1e9, 1e15, 1e22,
                    4503599627370496.0, Bits(0x7FE0000000000000UL), 0.7853981633974483, 2.356194490192345,
                    3.9269908169872414, 5.497787143782138, 1.5707963267948966, 4.71238898038469, 823549.6, 823550,
                    1.4901161193847656e-8, 7.450580596923828e-9,
                });
                for (int i = 0; i < 20000; i++) o.Add(WithExp(Below(0x3E4)));
                for (int i = 0; i < 30000; i++) o.Add(WithExp(0x3E4 + Below(0x3FE - 0x3E4 + 1)));
                for (int i = 0; i < 40000; i++) o.Add(Uniform(8 * PI));
                for (int i = 0; i < 50000; i++)
                {
                    int k = (i & 1) != 0 ? Below(2001) - 1000 : Below(1048577) - 524288;
                    double x = k * (PI / 2);
                    o.Add(x == 0 ? WithExp(Below(0x3FF)) : OffsetUlps(x, Below(2049) - 1024));
                }
                for (int i = 0; i < 30000; i++) o.Add(Uniform(823549.6));
                for (int i = 0; i < 10000; i++) o.Add(WithExp(0x413 + Below(11)));
                for (int i = 0; i < 50000; i++) o.Add(WithExp(0x41E + Below(0x7FE - 0x41E + 1)));
                for (int i = 0; i < 12000; i++)
                {
                    int hw = SinThresholds[i % SinThresholds.Length] + Below(5) - 2;
                    uint lo = U32();
                    o.Add(Words(((U32() & 1) << 31) | (uint)hw, lo));
                }
                foreach (int hw in Npio2Hw)
                {
                    for (int i = 0; i < 300; i++)
                    {
                        uint sign = U32() & 1;
                        o.Add(Words((sign << 31) | (uint)hw, U32()));
                    }
                }
                for (int i = 0; i < 20000; i++) o.Add(RandBits());
                return o.ToArray();
            }

            public double[] ExpInputs()
            {
                var o = new List<double>(Specials);
                o.AddRange(new[]
                {
                    709.782712893384, 709.7827128933841, -745.1332191019411, -745.1332191019412, 0.6931471805599453,
                    -0.6931471805599453, 0.34657359027997264, 1.0397207708399179, 7.09782712893383973096e+02,
                    -7.45133219101941108420e+02, -708.3964185322641, -708.3964185322642, 1024 * 0.6931471805599453,
                });
                for (int i = 0; i < 20000; i++) o.Add(WithExp(Below(0x3E3)));
                for (int i = 0; i < 30000; i++) o.Add(Uniform(0.34657359027997264));
                for (int i = 0; i < 30000; i++)
                {
                    double x = 0.34657359027997264 + Unit() * (1.0397207708399179 - 0.34657359027997264);
                    o.Add((U32() & 1) != 0 ? -x : x);
                }
                for (int i = 0; i < 40000; i++) o.Add(Uniform(20));
                for (int i = 0; i < 50000; i++) o.Add(-745.2 + Unit() * (709.9 + 745.2));
                for (int i = 0; i < 10000; i++) o.Add(OffsetUlps(709.782712893384, Below(200001) - 100000));
                for (int i = 0; i < 10000; i++) o.Add(OffsetUlps(-745.1332191019411, Below(200001) - 100000));
                for (int i = 0; i < 20000; i++) o.Add(-708 - Unit() * 37.14);
                for (int i = 0; i < 20000; i++)
                {
                    int k = Below(2048) - 1024;
                    o.Add(OffsetUlps((k + 0.5) * 0.6931471805599453, Below(65) - 32));
                }
                for (int i = 0; i < 20000; i++) o.Add(RandBits());
                return o.ToArray();
            }

            /// <summary>Pairs flattened as y0, x0, y1, x1, ...</summary>
            public double[] Atan2Inputs()
            {
                double[] axis =
                {
                    0.0, -0.0, 1, -1, double.PositiveInfinity, double.NegativeInfinity, double.NaN, 5e-324, -5e-324,
                    1e300, -1e300, 2.2250738585072014e-308,
                };
                var o = new List<double>();
                foreach (double y in axis)
                foreach (double x in axis) { o.Add(y); o.Add(x); }
                for (int i = 0; i < 50000; i++) { double y = Uniform(120); o.Add(y); o.Add(Uniform(120)); }
                for (int i = 0; i < 40000; i++) { double y = RandBits(); o.Add(y); o.Add(RandBits()); }
                double[] bandLo = { 0, 0.4375, 0.6875, 1.1875, 2.4375, 1048576 };
                double[] bandHi = { 0.4375, 0.6875, 1.1875, 2.4375, 1048576, 7.378697629483821e19 };
                for (int i = 0; i < 60000; i++)
                {
                    double lo = bandLo[i % 6], hi = bandHi[i % 6];
                    double x = Uniform(1000);
                    if (x == 0) x = 1;
                    double r = lo + Unit() * (hi - lo);
                    o.Add((U32() & 1) != 0 ? -x * r : x * r);
                    o.Add(x);
                }
                for (int i = 0; i < 20000; i++)
                {
                    int e1 = 0x100 + Below(0x500), d = 55 + Below(12);
                    int e2 = (i & 1) != 0 ? e1 + d : e1 - d;
                    double y = WithExp(e2), x = WithExp(e1);
                    if ((i & 2) != 0) { o.Add(x); o.Add(y); }
                    else { o.Add(y); o.Add(x); }
                }
                for (int i = 0; i < 20000; i++) { o.Add((i & 1) != 0 ? RandBits() : Uniform(10)); o.Add(1); }
                for (int i = 0; i < 20000; i++)
                {
                    double y = WithExp(Below(0x31));
                    o.Add(y);
                    o.Add(WithExp(Below(0x31)));
                }
                return o.ToArray();
            }
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

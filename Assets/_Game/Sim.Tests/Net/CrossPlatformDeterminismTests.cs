using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CriticalMass.Net;
using CriticalMass.Net.Relay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// b12-w: what a Windows player and a Mac player must agree on before they can share a match, proven with no Unity
    /// and no network (runs in the no-Unity sim gate and in Unity's EditMode). A Windows PC may run in a Turkish, German
    /// or Arabic locale; the Mac in English. The same 600-tick match must give the same state fingerprint every 30 ticks
    /// and the same ConfigHash whatever the process's current culture is, and any text the online path parses must read
    /// the same with LF and CRLF line ends. Every case prints one line: what was measured, the value, the limit.
    /// The real two-machine proof is .github/workflows/crossplay.yml (Windows player against Mac player on GitHub).
    /// </summary>
    public sealed class CrossPlatformDeterminismTests
    {
        private const int Ticks = 600, Every = 30, Seed = 4242, TeamSize = 16;
        private static readonly string[] Cultures = { "", "tr-TR", "de-DE", "ar-SA" };   // "" = the invariant culture

        private static string Label(string name) => name.Length == 0 ? "invariant" : name;

        /// <summary>Runs `body` with the thread's current culture and UI culture set to `name`; false when this runtime has
        /// no such culture (a .NET process in invariant-globalization mode), so the caller can say so.</summary>
        private static bool InCulture(string name, Action body)
        {
            CultureInfo was = CultureInfo.CurrentCulture, wasUi = CultureInfo.CurrentUICulture;
            CultureInfo c;
            try { c = name.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(name); }
            catch (CultureNotFoundException) { return false; }
            // A runtime in invariant mode may hand back an invariant-behaving culture under the asked-for name: probe one
            // culture-specific fact (German and Turkish use a decimal comma, Arabic Saudi Arabia does not, so only check those).
            if ((name == "de-DE" || name == "tr-TR") && c.NumberFormat.NumberDecimalSeparator != ",") return false;
            try
            {
                CultureInfo.CurrentCulture = c;
                CultureInfo.CurrentUICulture = c;
                body();
            }
            finally
            {
                CultureInfo.CurrentCulture = was;
                CultureInfo.CurrentUICulture = wasUi;
            }
            return true;
        }

        /// <summary>The same match every time: seed 4242, 16 a side, the human actor driven by the autoplay pilot (as the
        /// hidden online peer does), fingerprints at tick 0 and every 30th tick.</summary>
        private static List<KeyValuePair<int, ulong>> PlayMatch(out ulong configHash)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            configHash = ConfigHash.Of(cfg);
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = Seed, TeamSize = TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
            });
            var pilot = new AutoplayPilot(101);
            InputFrame scratch = SimInput.EmptyInput();
            var hashes = new List<KeyValuePair<int, ulong>> { new KeyValuePair<int, ulong>(0, StateHash.Of(w)) };
            for (int t = 0; t < Ticks; t++)
            {
                InputFrame human = SimInput.EmptyInput();
                pilot.Fill(w, 0, w.Tick, human);
                MatchTick.Advance(w, human, scratch);
                if (w.Tick % Every == 0) hashes.Add(new KeyValuePair<int, ulong>(w.Tick, StateHash.Of(w)));
            }
            return hashes;
        }

        private static string Rows(List<KeyValuePair<int, ulong>> h)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<int, ulong> kv in h)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append(':').Append(kv.Value.ToString("X16", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        [Test]
        public void TheSameMatchHashesTheSameUnderEveryCulture()
        {
            List<KeyValuePair<int, ulong>> reference = null;
            ulong refConfig = 0;
            int ran = 0;
            var failures = new List<string>();
            foreach (string name in Cultures)
            {
                List<KeyValuePair<int, ulong>> got = null;
                ulong cfgHash = 0;
                bool ok = InCulture(name, () => got = PlayMatch(out cfgHash));
                if (!ok)
                {
                    Console.WriteLine($"[CM.CrossPlatform] culture {Label(name)}: not available on this runtime (invariant globalization), skipped");
                    continue;
                }
                ran++;
                Console.WriteLine($"[CM.CrossPlatform] culture {Label(name)} config 0x{cfgHash.ToString("X16", CultureInfo.InvariantCulture)} rows {got.Count.ToString(CultureInfo.InvariantCulture)} " +
                                  $"final 0x{got[got.Count - 1].Value.ToString("X16", CultureInfo.InvariantCulture)} hashes {Rows(got)}");
                if (reference == null) { reference = got; refConfig = cfgHash; continue; }
                if (cfgHash != refConfig) failures.Add($"culture {Label(name)} config 0x{cfgHash:X16} vs invariant 0x{refConfig:X16}");
                if (got.Count != reference.Count) { failures.Add($"culture {Label(name)} {got.Count} rows vs {reference.Count}"); continue; }
                for (int i = 0; i < got.Count; i++)
                {
                    if (got[i].Key == reference[i].Key && got[i].Value == reference[i].Value) continue;
                    failures.Add($"culture {Label(name)} first differing row tick {got[i].Key}: 0x{got[i].Value:X16} vs invariant 0x{reference[i].Value:X16}");
                    break;
                }
            }
            Console.WriteLine($"[CM.CrossPlatform] cultures run {ran} of {Cultures.Length}, rows each {(reference != null ? reference.Count : 0)} (limit: every row equal), failures {failures.Count}");
            Assert.That(reference, Is.Not.Null, "the invariant culture always runs");
            Assert.That(reference.Count, Is.EqualTo(Ticks / Every + 1), "tick 0 plus one row every 30 ticks");
            Assert.That(failures, Is.Empty, string.Join("; ", failures));
        }

        [Test]
        public void ConfigHashSeesOneChangedFieldAndIgnoresTheCulture()
        {
            GameConfig a = GameConfig.CreateDefault(), b = GameConfig.CreateDefault();
            ulong ha = ConfigHash.Of(a), hb = ConfigHash.Of(b);
            Console.WriteLine($"[CM.CrossPlatform] config same twice: 0x{ha:X16} 0x{hb:X16} (limit: equal)");
            Assert.AreEqual(ha, hb, "two default configs hash the same");

            GameConfig top = GameConfig.CreateDefault();
            top.MatchSeconds += 1;
            ulong hTop = ConfigHash.Of(top);
            Console.WriteLine($"[CM.CrossPlatform] config MatchSeconds +1: 0x{hTop:X16} vs 0x{ha:X16} (limit: differ)");
            Assert.AreNotEqual(ha, hTop, "a top-level field change moves the hash");

            GameConfig nested = GameConfig.CreateDefault();
            nested.Physics.KnockDecayPer30Hz = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(nested.Physics.KnockDecayPer30Hz) + 1);
            ulong hNested = ConfigHash.Of(nested);
            Console.WriteLine($"[CM.CrossPlatform] config Physics.KnockDecayPer30Hz one ulp up: 0x{hNested:X16} vs 0x{ha:X16} (limit: differ)");
            Assert.AreNotEqual(ha, hNested, "a one-bit change in a nested double moves the hash");

            foreach (string name in Cultures)
            {
                ulong same = 0, diff = 0;
                bool ok = InCulture(name, () =>
                {
                    same = ConfigHash.Of(GameConfig.CreateDefault());
                    GameConfig c = GameConfig.CreateDefault();
                    c.MatchSeconds += 1;
                    diff = ConfigHash.Of(c);
                });
                if (!ok) { Console.WriteLine($"[CM.CrossPlatform] config culture {Label(name)}: not available, skipped"); continue; }
                Console.WriteLine($"[CM.CrossPlatform] config culture {Label(name)}: default 0x{same:X16} changed 0x{diff:X16} (limit: default == 0x{ha:X16}, changed == 0x{hTop:X16})");
                Assert.AreEqual(ha, same, "culture " + Label(name) + " default config");
                Assert.AreEqual(hTop, diff, "culture " + Label(name) + " changed config");
            }
        }

        [Test]
        public void OnlineTextReadsTheSameWithLfAndCrlf()
        {
            // The room code is the one piece of text two machines exchange by hand (typed, pasted, or read from the
            // -onlineCodeOut file a harness wrote). LF and CRLF copies must read the same.
            string code = RoomCodes.Alphabet.Substring(0, RoomCodes.Length);
            string plain = RelayTransport.CleanCode(code), lf = RelayTransport.CleanCode(code + "\n"), crlf = RelayTransport.CleanCode(code + "\r\n");
            string spaced = RelayTransport.CleanCode(code.Substring(0, 3) + "-" + code.Substring(3));
            Console.WriteLine($"[CM.CrossPlatform] room code '{code}': plain '{plain}' LF '{lf ?? "null"}' CRLF '{crlf ?? "null"}' dashed '{spaced}' (limit: LF == CRLF; plain == dashed == code)");
            Assert.AreEqual(code, plain);
            Assert.AreEqual(code, spaced);
            Assert.AreEqual(lf, crlf, "LF and CRLF read alike");
            if (lf == null) Console.WriteLine("[CM.CrossPlatform] note: a trailing line end makes CleanCode refuse the code (both LF and CRLF); the harness trims; patch for RelayTransport.cs in docs/windows-crossplay.md");

            // The hash log the peers write and compare-hashes.py reads: "tick HEX\n". Same rows whatever the line end.
            string log = "0 00000000000000AA\n1 00000000000000BB\n30 00000000000000CC\n";
            Assert.AreEqual(ParseHashLog(log), ParseHashLog(log.Replace("\n", "\r\n")));
            Console.WriteLine($"[CM.CrossPlatform] hash log LF rows {ParseHashLog(log)} CRLF rows {ParseHashLog(log.Replace("\n", "\r\n"))} (limit: equal)");
        }

        private static string ParseHashLog(string text)
        {
            var sb = new StringBuilder();
            foreach (string line in text.Split('\n'))
            {
                string[] parts = line.Trim().Split(' ');
                if (parts.Length != 2) continue;
                int tick = int.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
                ulong h = ulong.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                sb.Append(tick.ToString(CultureInfo.InvariantCulture)).Append('=').Append(h.ToString("X16", CultureInfo.InvariantCulture)).Append(';');
            }
            return sb.ToString();
        }

        [Test]
        public void TheSimReadsNoTextAndItsSourcesArePinnedToLf()
        {
            // The Sim takes no text input at all (no files, resources, parsing or splitting), so a CRLF checkout or a
            // Windows locale cannot reach a rule through text. This guard keeps it that way, and checks that a Windows
            // checkout (git autocrlf) leaves C# sources LF, so no multi-line string literal changes between checkouts.
            string sim = Path.Combine("Assets", "_Game", "Sim");
            if (!Directory.Exists(sim)) { Assert.Inconclusive("run from the repository root (the Sim sources are not at " + sim + ")"); return; }
            var textApi = new Regex(@"\b(File|Directory|StreamReader|TextReader|Resources)\.|ReadAllText|ReadAllLines|TextAsset|\.Split\(|\b(int|long|double|float|decimal|ulong)\.(Try)?Parse\(|Convert\.ToDouble|Environment\.|DateTime\.|System\.Random\b|new Random\(", RegexOptions.CultureInvariant);
            var verbatimMultiLine = new Regex("@\"[^\"]*\n", RegexOptions.CultureInvariant);
            var hits = new List<string>();
            int files = 0, multiLine = 0;
            foreach (string f in Directory.GetFiles(sim, "*.cs", SearchOption.AllDirectories))
            {
                // SimProfile.cs holds wall-clock profiling counters (read by the frame-cost overlay, never by a rule).
                if (Path.GetFileName(f) == "SimProfile.cs") { Console.WriteLine("[CM.CrossPlatform] skipped SimProfile.cs (profiling counters, not rules)"); continue; }
                files++;
                string src = File.ReadAllText(f);
                if (verbatimMultiLine.IsMatch(src)) multiLine++;
                string[] lines = src.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i].TrimStart();
                    if (l.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (textApi.IsMatch(l)) hits.Add(Path.GetFileName(f) + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + " " + l.Trim());
                }
            }
            Console.WriteLine($"[CM.CrossPlatform] Sim sources scanned {files}, text/clock/environment calls {hits.Count} (limit 0), multi-line verbatim strings {multiLine} (limit 0)");
            foreach (string h in hits) Console.WriteLine("[CM.CrossPlatform]   " + h);
            Assert.That(files, Is.GreaterThan(20), "found the Sim sources");
            Assert.That(hits, Is.Empty, "the Sim reads no text, clock or environment");
            Assert.AreEqual(0, multiLine, "no multi-line verbatim strings in the Sim");

            string attrs = File.Exists(".gitattributes") ? File.ReadAllText(".gitattributes") : "";
            bool csLf = Regex.IsMatch(attrs, @"(?m)^\*\.cs\s+text\s+eol=lf\b");
            Console.WriteLine($"[CM.CrossPlatform] .gitattributes pins *.cs to LF: {csLf} (limit: true)");
            Assert.IsTrue(csLf, ".gitattributes keeps C# sources LF on a Windows checkout");
        }
    }
}

// b15 tools: the balance-report command (headless, .NET, no Unity). Read-only use of the sim: it plays bot matches or re-simulates
// saved replays, measures them from the outside (Sim.Tests/Harness/BalanceProbe.cs) and writes one report as Markdown, JSON and CSV.
//
//   balance-report [run] [--profile full|physics|classic] [--set Path=Value]... [--tuning FILE.json] [--seeds 24|72|N] [--seed-list a,b]
//                  [--team-size 16] [--replays DIR] [--record DIR] [--duel-reps 8] [--no-duels] [--targets FILE] [--out DIR]
//                  [--label TEXT] [--commit SHA]
//   balance-report compare --base SPEC --head SPEC [same options]     SPEC = a report .json (from another ref), or switches run here:
//                  "profile=full;Depth.Hut.On=false" ("" = the default game)
//   balance-report tuning-index [--out docs/balance/TUNING-INDEX.md]
//
// tools/balance/balance-report.sh builds this and resolves git refs for compare; run it from the repo root.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CriticalMass.Net;
using CriticalMass.Net.Replay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;

namespace CriticalMass.Balance
{
    public static class Program
    {
        sealed class Options
        {
            public string Command = "run";
            public RulesProfile Profile = RulesProfile.Full;
            public readonly List<string> Sets = new List<string>();
            public int Seeds = 24;
            public int[] SeedList;
            public int TeamSize = 16;
            public string Replays, Record;
            public int DuelReps = 8;
            public bool Duels = true;
            public string Targets = "docs/balance/TARGETS.json";
            public string Out;
            public string Label = "", Commit = "";
            public string Base, Head;
            public string Arms;
            public int Shard, Shards = 1;
        }

        public static int Main(string[] args)
        {
            try
            {
                Options o = ParseArgs(args);
                switch (o.Command)
                {
                    case "tuning-index": return TuningIndexCommand(o);
                    case "compare": return Compare(o);
                    case "duel-sweep": return DuelSweep(o);
                    default: return Run(o);
                }
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine("balance-report: " + e.Message);
                return 64;
            }
        }

        static Options ParseArgs(string[] a)
        {
            var o = new Options();
            int i = 0;
            if (a.Length > 0 && !a[0].StartsWith("--", StringComparison.Ordinal)) o.Command = a[i++];
            for (; i < a.Length; i++)
            {
                string k = a[i];
                string V()
                {
                    if (i + 1 >= a.Length) throw new ArgumentException(k + " needs a value");
                    return a[++i];
                }
                switch (k)
                {
                    case "--profile": o.Profile = ParseProfile(V()); break;
                    case "--set": o.Sets.Add(V()); break;
                    case "--tuning": o.Sets.AddRange(TuningFile(V())); break;
                    case "--seeds": o.Seeds = int.Parse(V(), CultureInfo.InvariantCulture); break;
                    case "--seed-list": o.SeedList = Array.ConvertAll(V().Split(','), s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)); break;
                    case "--team-size": o.TeamSize = int.Parse(V(), CultureInfo.InvariantCulture); break;
                    case "--replays": o.Replays = V(); break;
                    case "--record": o.Record = V(); break;
                    case "--duel-reps": o.DuelReps = int.Parse(V(), CultureInfo.InvariantCulture); break;
                    case "--no-duels": o.Duels = false; break;
                    case "--targets": o.Targets = V(); break;
                    case "--out": o.Out = V(); break;
                    case "--label": o.Label = V(); break;
                    case "--commit": o.Commit = V(); break;
                    case "--base": o.Base = V(); break;
                    case "--head": o.Head = V(); break;
                    case "--arms": o.Arms = V(); break;
                    case "--shard":
                    {
                        string[] kn = V().Split('/');
                        o.Shard = int.Parse(kn[0], CultureInfo.InvariantCulture);
                        o.Shards = int.Parse(kn[1], CultureInfo.InvariantCulture);
                        break;
                    }
                    default: throw new ArgumentException("unknown option " + k);
                }
            }
            return o;
        }

        static RulesProfile ParseProfile(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "full": return RulesProfile.Full;
                case "physics": case "physicsonly": return RulesProfile.PhysicsOnly;
                case "classic": return RulesProfile.Classic;
                default: throw new ArgumentException("profile '" + s + "': use full, physics or classic");
            }
        }

        /// <summary>A tuning file: {"Path": value, ...} (a flat JSON object; arrays become comma lists).</summary>
        static IEnumerable<string> TuningFile(string path)
        {
            if (!(MiniJson.Parse(File.ReadAllText(path)) is Dictionary<string, object> d)) throw new ArgumentException(path + ": expected a JSON object of Path: value");
            var list = new List<string>();
            foreach (var kv in d)
            {
                if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue; // "_about" and other notes
                list.Add(kv.Key + "=" + Text(kv.Value));
            }
            return list;
        }

        static string Text(object v)
        {
            if (v is List<object> l)
            {
                var parts = new List<string>();
                foreach (object x in l) parts.Add(Text(x));
                return string.Join(",", parts);
            }
            if (v is bool b) return b ? "true" : "false";
            if (v is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            return v?.ToString() ?? "";
        }

        static List<BalanceTarget> LoadTargets(Options o) =>
            o.Targets != null && File.Exists(o.Targets) ? BalanceTarget.FromJson(File.ReadAllText(o.Targets)) : new List<BalanceTarget>();

        static void Write(string dir, string stem, string md, string json, string csv)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, stem + ".md"), md);
            File.WriteAllText(Path.Combine(dir, stem + ".json"), json);
            File.WriteAllText(Path.Combine(dir, stem + ".csv"), csv);
            Console.WriteLine("[balance] wrote " + Path.Combine(dir, stem) + ".{md,json,csv}");
        }

        static int Run(Options o)
        {
            BalanceReport r = Measure(o, o.Profile, o.Sets, o.Label);
            List<BalanceTarget> targets = LoadTargets(o);
            Write(o.Out ?? "Temp/balance/report", "balance-report", BalanceWriter.Markdown(r, targets), BalanceWriter.Json(r), BalanceWriter.Csv(r));
            PrintTargets(r, targets);
            return 0;
        }

        static void PrintTargets(BalanceReport r, List<BalanceTarget> targets)
        {
            foreach (BalanceTarget t in targets)
            {
                BalanceRow row = r.Row(t.Row);
                string v = t.Verdict(row, out string note);
                Console.WriteLine("[target] " + t.Label + ": " + (row != null ? BalanceFormat.Value(row) + " [" + BalanceFormat.Ci(row) + "] n " + row.N : "n/a")
                                  + " vs " + t.Band() + " -> " + v + (note.Length > 0 ? " (" + note + ")" : ""));
            }
        }

        static BalanceReport Measure(Options o, RulesProfile profile, List<string> sets, string label)
        {
            var wall = Stopwatch.StartNew();
            var matches = new List<MatchRecord>();
            string source;
            GameConfig cfg = BalanceRunner.Config(profile, sets);
            if (o.Replays != null)
            {
                var files = new List<string>(Directory.GetFiles(o.Replays, "*.cmreplay", SearchOption.AllDirectories));
                files.Sort(StringComparer.Ordinal);
                foreach (string f in files)
                {
                    var sw = Stopwatch.StartNew();
                    if (ReplayFile.TryRead(File.ReadAllBytes(f), out ReplayData data, out string why) != ReplayReadStatus.Ok)
                    {
                        Console.WriteLine("[balance] skip " + f + ": " + why);
                        continue;
                    }
                    MatchRecord m = BalanceRunner.Replay(data, Path.GetFileName(f));
                    matches.Add(m);
                    Console.WriteLine("[balance] replay " + Path.GetFileName(f) + ": " + m.Winner + " " + BalanceFormat.Num(m.HillAzure) + "-" + BalanceFormat.Num(m.HillCrimson)
                                      + (m.Desynced ? " DESYNCED (a different build recorded it)" : "") + ", " + sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                }
                source = matches.Count + " replays re-simulated from " + o.Replays;
            }
            else
            {
                int[] seeds = o.SeedList ?? BalanceSeeds.Pool(o.Seeds);
                if (o.Record != null && sets.Count > 0) throw new ArgumentException("--record cannot be combined with --set: a replay rebuilds its config from the rules profile only");
                foreach (int seed in seeds)
                {
                    var sw = Stopwatch.StartNew();
                    ReplayRecorder rec = null;
                    MatchRecord m = BalanceRunner.BotMatch(BalanceRunner.Config(profile, sets), seed, o.TeamSize, 0,
                        w =>
                        {
                            if (o.Record == null) return;
                            rec = new ReplayRecorder();
                            ReplayStart s = ReplayStart.From(BalanceRunner.Options(w.Config, seed, o.TeamSize), ReplayKind.Offline, ConfigBasis.Browser5v5, (int)profile, 0, 0, 0, 0);
                            if (rec.Begin(w, new ReplayHeader { BuildVersion = "balance-report", BuildCommit = o.Commit, ModeLabel = "BOT SOAK", Start = s })) MatchTick.Tap = rec;
                            else rec = null;
                        },
                        w =>
                        {
                            if (rec == null) return;
                            MatchTick.Tap = null;
                            Directory.CreateDirectory(o.Record);
                            File.WriteAllBytes(Path.Combine(o.Record, "bot-" + seed.ToString(CultureInfo.InvariantCulture) + ".cmreplay"),
                                ReplayFile.Write(rec.Finish(w.Match.Phase == MatchPhase.Ended, DateTime.UtcNow.Ticks)));
                        });
                    matches.Add(m);
                    Console.WriteLine("[balance] seed " + seed + ": " + m.Winner + " " + BalanceFormat.Num(m.HillAzure) + "-" + BalanceFormat.Num(m.HillCrimson)
                                      + ", lead changes " + m.LeadChanges + ", " + sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                }
                source = seeds.Length + " bot matches " + o.TeamSize + "v" + o.TeamSize + ", seeds " + string.Join(",", seeds);
            }
            double matchWall = wall.Elapsed.TotalSeconds;
            DuelRunner.Cell[,] ttk = null, duels = null;
            DuelRunner.Cell[] esc = null, escDodge = null;
            var dw = Stopwatch.StartNew();
            if (o.Duels)
            {
                ttk = DuelRunner.Ttk(profile, sets, o.DuelReps);
                duels = DuelRunner.Duels(profile, sets, o.DuelReps);
                esc = DuelRunner.Escape(profile, sets, o.DuelReps, false);
                escDodge = DuelRunner.Escape(profile, sets, o.DuelReps, true);
            }
            double duelWall = dw.Elapsed.TotalSeconds;
            BalanceReport r = BalanceAggregate.Build(matches, ttk, duels, esc, escDodge);
            r.Meta.Add(new KeyValuePair<string, string>("label", label ?? ""));
            r.Meta.Add(new KeyValuePair<string, string>("commit", o.Commit));
            r.Meta.Add(new KeyValuePair<string, string>("source", source));
            r.Meta.Add(new KeyValuePair<string, string>("rules profile", profile.ToString()));
            r.Meta.Add(new KeyValuePair<string, string>("switches", sets.Count > 0 ? string.Join("; ", sets) : "none (the game's values)"));
            r.Meta.Add(new KeyValuePair<string, string>("config", "GameConfig.CreateBrowser5v5().ApplyRules(" + profile + ") + switches, the balance tests' config; ConfigHash " + ConfigHash.Of(cfg).ToString("x16", CultureInfo.InvariantCulture)));
            r.Meta.Add(new KeyValuePair<string, string>("actor 0", "on bot autopilot (every body is a bot)"));
            r.Meta.Add(new KeyValuePair<string, string>("wall time", matchWall.ToString("0.0", CultureInfo.InvariantCulture) + " s for the matches ("
                + (matches.Count > 0 ? (matchWall / matches.Count).ToString("0.0", CultureInfo.InvariantCulture) : "0") + " s each), "
                + duelWall.ToString("0.0", CultureInfo.InvariantCulture) + " s for the duels, one thread, " + Environment.ProcessorCount + " CPUs seen"));
            r.Meta.Add(new KeyValuePair<string, string>("generated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));
            Console.WriteLine("[balance] " + matches.Count + " matches in " + matchWall.ToString("0.0", CultureInfo.InvariantCulture) + " s, duels " + duelWall.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            return r;
        }

        static BalanceReport Side(Options o, string spec, string name)
        {
            if (spec != null && spec.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(spec))
            {
                Console.WriteLine("[balance] " + name + ": report " + spec);
                return BalanceWriter.FromJson(File.ReadAllText(spec));
            }
            RulesProfile profile = o.Profile;
            var sets = new List<string>(o.Sets);
            foreach (string tok in (spec ?? "").Split(';'))
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("profile=", StringComparison.OrdinalIgnoreCase)) profile = ParseProfile(t.Substring(8));
                else sets.Add(t);
            }
            Console.WriteLine("[balance] " + name + ": " + profile + (sets.Count > 0 ? " with " + string.Join("; ", sets) : ""));
            return Measure(o, profile, sets, name + " " + spec);
        }

        static int Compare(Options o)
        {
            if (o.Base == null || o.Head == null) throw new ArgumentException("compare needs --base and --head");
            BalanceReport b = Side(o, o.Base, "base");
            BalanceReport h = Side(o, o.Head, "head");
            List<BalanceTarget> targets = LoadTargets(o);
            string dir = o.Out ?? "Temp/balance/compare";
            Write(dir, "balance-compare", BalanceCompare.Markdown(b, h, targets), BalanceCompare.Json(b, h), BalanceCompare.Csv(b, h));
            File.WriteAllText(Path.Combine(dir, "base.json"), BalanceWriter.Json(b));
            File.WriteAllText(Path.Combine(dir, "head.json"), BalanceWriter.Json(h));
            int outside = 0;
            foreach (BalanceCompare.Line l in BalanceCompare.Lines(b, h))
                if (l.Verdict == "OUTSIDE noise")
                {
                    outside++;
                    Console.WriteLine("[compare] " + l.Label + ": " + BalanceFormat.Num(l.Base.Value) + " -> " + BalanceFormat.Num(l.Head.Value) + " (noise +-" + BalanceFormat.Num(l.Noise) + ")");
                }
            Console.WriteLine("[compare] " + outside + " rows outside the noise");
            return 0;
        }

        /// <summary>
        /// b16: duel-sweep --arms FILE [--shard k/n] [--duel-reps N] [--out FILE.csv]: the duel tables only (no matches) for every
        /// arm of FILE (one arm a line: name, a tab, switches "Path=Value;Path=Value"; # comments), this shard's share of
        /// them (line index mod n == k). One CSV line per arm: violations of the duel order, each class's wins, the matrix
        /// (row beats column, both seatings), the median time to kill and the escape rates.
        /// </summary>
        static int DuelSweep(Options o)
        {
            if (o.Arms == null) throw new ArgumentException("duel-sweep needs --arms FILE");
            string outPath = o.Out ?? "Temp/balance/duel-sweep.csv";
            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new System.Text.StringBuilder("arm,violations,why,wins_W,wins_F,wins_M,wins_D,wins_B,wins_H,beats,ttk_median,ttk_min,ttk_max,escape,escape_dodge,switches\n");
            int idx = -1, done = 0;
            var wall = Stopwatch.StartNew();
            foreach (string raw in File.ReadAllLines(o.Arms))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                idx++;
                if (o.Shards > 1 && idx % o.Shards != o.Shard) continue;
                int tab = line.IndexOf('\t');
                string name = tab >= 0 ? line.Substring(0, tab).Trim() : line;
                string spec = tab >= 0 ? line.Substring(tab + 1).Trim() : "";
                var sets = new List<string>(o.Sets);
                foreach (string tok in spec.Split(';'))
                    if (tok.Trim().Length > 0) sets.Add(tok.Trim());
                DuelRunner.Cell[,] duels = DuelRunner.Duels(o.Profile, sets, o.DuelReps);
                DuelRunner.Cell[,] ttk = DuelRunner.Ttk(o.Profile, sets, o.DuelReps);
                DuelRunner.Cell[] esc = DuelRunner.Escape(o.Profile, sets, o.DuelReps, false);
                DuelRunner.Cell[] escD = DuelRunner.Escape(o.Profile, sets, o.DuelReps, true);
                int bad = DuelRunner.OrderViolations(duels, out string why);
                var wins = new int[6];
                var beats = new List<string>();
                for (int a = 0; a < 6; a++)
                    for (int b = 0; b < 6; b++)
                        if (a != b && DuelRunner.Beats(duels, a, b))
                        {
                            wins[a]++;
                            beats.Add(BalanceAggregate.ClassNames[a].Substring(0, 2) + ">" + BalanceAggregate.ClassNames[b].Substring(0, 2));
                        }
                var meds = new List<double>();
                for (int a = 0; a < 6; a++)
                    for (int b = 0; b < 6; b++)
                    {
                        var st = BalanceStat.Sorted(ttk[a, b].Ttk);
                        if (st.Count > 0) meds.Add(BalanceStat.Quantile(st, 0.5));
                    }
                meds.Sort();
                double ttkMed = meds.Count > 0 ? BalanceStat.Quantile(meds, 0.5) : double.NaN;
                int ew = 0, er = 0, dwn = 0, dr = 0;
                for (int c = 1; c < 6; c++)
                {
                    ew += esc[c].Wins; er += esc[c].Reps;
                    dwn += escD[c].Wins; dr += escD[c].Reps;
                }
                string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
                sb.Append(name).Append(',').Append(bad).Append(",\"").Append(why.Replace("\"", "'")).Append("\",");
                for (int a = 0; a < 6; a++) sb.Append(wins[a]).Append(',');
                sb.Append(string.Join(" ", beats)).Append(',').Append(F(ttkMed)).Append(',').Append(meds.Count > 0 ? F(meds[0]) : "").Append(',')
                  .Append(meds.Count > 0 ? F(meds[meds.Count - 1]) : "").Append(',').Append(F(er > 0 ? (double)ew / er : double.NaN)).Append(',')
                  .Append(F(dr > 0 ? (double)dwn / dr : double.NaN)).Append(",\"").Append(spec).Append("\"\n");
                done++;
                if (done % 20 == 0) Console.WriteLine("[sweep] " + done + " arms, " + wall.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
            }
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine("[sweep] " + done + " arms -> " + outPath + " in " + wall.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
            return 0;
        }

        static int TuningIndexCommand(Options o)
        {
            List<TuningIndex.Entry> entries = TuningIndex.Collect(GameConfig.CreateDefault());
            string path = o.Out ?? TuningIndex.IndexPath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, TuningIndex.Markdown(entries));
            int documented = 0;
            foreach (TuningIndex.Entry e in entries)
                if (e.Meaning.Length > 0) documented++;
            Console.WriteLine("[tuning-index] " + entries.Count + " fields (" + documented + " with a doc comment) -> " + path);
            return 0;
        }
    }
}

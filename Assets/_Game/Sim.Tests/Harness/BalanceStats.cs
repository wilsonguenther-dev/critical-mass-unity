using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>b15 tools: small statistics for the balance report (95% intervals: Wilson for rates, t for means, ranks for medians).</summary>
    public static class BalanceStat
    {
        public const double Z = 1.959964;

        public static void Wilson(int k, int n, out double p, out double lo, out double hi)
        {
            if (n <= 0)
            {
                p = lo = hi = double.NaN;
                return;
            }
            p = (double)k / n;
            double z2 = Z * Z;
            double den = 1 + z2 / n;
            double centre = (p + z2 / (2 * n)) / den;
            double half = Z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / den;
            lo = Math.Max(0, centre - half);
            hi = Math.Min(1, centre + half);
        }

        /// <summary>Two-sided 95% t critical value for df degrees of freedom (table, then the normal value).</summary>
        public static double T95(int df)
        {
            double[] t = { double.NaN, 12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228, 2.201, 2.179, 2.160, 2.145, 2.131,
                2.120, 2.110, 2.101, 2.093, 2.086, 2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042 };
            if (df <= 0) return double.NaN;
            if (df < t.Length) return t[df];
            if (df < 60) return 2.00;
            return Z;
        }

        public static void Mean(IList<double> v, out double mean, out double lo, out double hi, out double sd)
        {
            int n = v.Count;
            mean = lo = hi = sd = double.NaN;
            if (n == 0) return;
            double s = 0;
            foreach (double x in v) s += x;
            mean = s / n;
            if (n == 1)
            {
                lo = hi = mean;
                sd = 0;
                return;
            }
            double ss = 0;
            foreach (double x in v) ss += (x - mean) * (x - mean);
            sd = Math.Sqrt(ss / (n - 1));
            double half = T95(n - 1) * sd / Math.Sqrt(n);
            lo = mean - half;
            hi = mean + half;
        }

        public static List<double> Sorted(IEnumerable<double> v)
        {
            var s = new List<double>();
            foreach (double x in v)
                if (!double.IsNaN(x)) s.Add(x);
            s.Sort();
            return s;
        }

        /// <summary>Linear-interpolated quantile of a sorted list.</summary>
        public static double Quantile(List<double> s, double q)
        {
            if (s.Count == 0) return double.NaN;
            double pos = q * (s.Count - 1);
            int i = (int)Math.Floor(pos);
            if (i >= s.Count - 1) return s[s.Count - 1];
            return s[i] + (pos - i) * (s[i + 1] - s[i]);
        }

        /// <summary>Median with a distribution-free 95% interval (order statistics n/2 -+ 0.98 sqrt(n)).</summary>
        public static void Median(List<double> s, out double med, out double lo, out double hi)
        {
            int n = s.Count;
            med = lo = hi = double.NaN;
            if (n == 0) return;
            med = Quantile(s, 0.5);
            double h = Z * Math.Sqrt(n) / 2;
            int j = Math.Max(1, (int)Math.Floor(n / 2.0 - h));
            int k = Math.Min(n, (int)Math.Ceiling(n / 2.0 + h + 1));
            lo = s[j - 1];
            hi = s[k - 1];
        }
    }

    /// <summary>One line of the report: a value, its 95% interval, the sample it rests on and (for distributions) the spread.</summary>
    public sealed class BalanceRow
    {
        public string Key = "", Group = "", Label = "", Kind = "", Unit = "", Note = "";
        public double Value = double.NaN, Lo = double.NaN, Hi = double.NaN, P25 = double.NaN, P75 = double.NaN;
        public int N;
        /// <summary>False: shown, never judged against noise (machine-dependent, or a count with no sampling model).</summary>
        public bool Comparable = true;

        public double Half => double.IsNaN(Lo) || double.IsNaN(Hi) ? double.NaN : (Hi - Lo) / 2;
    }

    public sealed class BalanceReport
    {
        public readonly List<KeyValuePair<string, string>> Meta = new List<KeyValuePair<string, string>>();
        public readonly List<BalanceRow> Rows = new List<BalanceRow>();
        public readonly List<string> Tables = new List<string>();
        public readonly List<string> NeedsCounter = new List<string>();
        public readonly List<MatchRecord> Matches = new List<MatchRecord>();
        public DuelRunner.Cell[,] Ttk, Duels;

        public BalanceRow Row(string key)
        {
            foreach (BalanceRow r in Rows)
                if (r.Key == key) return r;
            return null;
        }

        public string MetaValue(string key)
        {
            foreach (var kv in Meta)
                if (kv.Key == key) return kv.Value;
            return "";
        }
    }

    /// <summary>b15 tools: turns match records and duel tables into report rows (the definitions live in BalanceProbe).</summary>
    public static class BalanceAggregate
    {
        public static readonly string[] ClassNames = { "Warrior", "Farmer", "Miner", "Demolitions", "Builder", "Healer" };
        public static readonly string[] KaijuTypes = { "Elder", "Grove", "Brute", "Timber", "Mire" };
        public const double TtkMin = 3, TtkMax = 6;

        static BalanceRow Rate(BalanceReport r, string key, string group, string label, int k, int n, string note = "")
        {
            BalanceStat.Wilson(k, n, out double p, out double lo, out double hi);
            var row = new BalanceRow { Key = key, Group = group, Label = label, Kind = "rate", Value = p, Lo = lo, Hi = hi, N = n, Note = note };
            r.Rows.Add(row);
            return row;
        }

        static BalanceRow Mean(BalanceReport r, string key, string group, string label, List<double> v, string unit = "", string note = "")
        {
            var clean = BalanceStat.Sorted(v);
            BalanceStat.Mean(clean, out double m, out double lo, out double hi, out double _);
            var row = new BalanceRow
            {
                Key = key, Group = group, Label = label, Kind = "mean", Value = m, Lo = lo, Hi = hi, N = clean.Count, Unit = unit, Note = note,
                P25 = BalanceStat.Quantile(clean, 0.25), P75 = BalanceStat.Quantile(clean, 0.75),
            };
            r.Rows.Add(row);
            return row;
        }

        static BalanceRow Median(BalanceReport r, string key, string group, string label, List<double> v, string unit = "", string note = "")
        {
            var s = BalanceStat.Sorted(v);
            BalanceStat.Median(s, out double m, out double lo, out double hi);
            var row = new BalanceRow
            {
                Key = key, Group = group, Label = label, Kind = "median", Value = m, Lo = lo, Hi = hi, N = s.Count, Unit = unit, Note = note,
                P25 = BalanceStat.Quantile(s, 0.25), P75 = BalanceStat.Quantile(s, 0.75),
            };
            r.Rows.Add(row);
            return row;
        }

        static List<double> Each(List<MatchRecord> ms, Func<MatchRecord, double> f)
        {
            var v = new List<double>(ms.Count);
            foreach (MatchRecord m in ms) v.Add(f(m));
            return v;
        }

        static double Ratio(double a, double b) => b > 0 ? a / b : double.NaN;

        public static BalanceReport Build(List<MatchRecord> ms, DuelRunner.Cell[,] ttk, DuelRunner.Cell[,] duels)
        {
            var r = new BalanceReport { Ttk = ttk, Duels = duels };
            r.Matches.AddRange(ms);
            int n = ms.Count;
            const string O = "Outcome";
            int az = 0, cr = 0, dr = 0, blow = 0, halfN = 0, halfWon = 0;
            foreach (MatchRecord m in ms)
            {
                if (m.Winner == MatchResult.Azure) az++;
                else if (m.Winner == MatchResult.Crimson) cr++;
                else dr++;
                if (m.Blowout) blow++;
                bool decided = m.Winner == MatchResult.Azure || m.Winner == MatchResult.Crimson;
                if (m.LeaderAtHalf != 0 && decided)
                {
                    halfN++;
                    if ((m.LeaderAtHalf > 0) == (m.Winner == MatchResult.Azure)) halfWon++;
                }
            }
            Rate(r, "win.azure", O, "Azure win share", az, n);
            Rate(r, "win.crimson", O, "Crimson win share", cr, n);
            Rate(r, "win.draw", O, "Draw share", dr, n);
            Rate(r, "side.west", O, "West spawn (pad x=8) win share", az, n, "Azure always spawns west in this build: the same matches as the team row");
            Rate(r, "side.east", O, "East spawn (pad x=104) win share", cr, n, "Crimson always spawns east");
            Mean(r, "length", O, "Match length", Each(ms, m => m.Length), "s");
            Rate(r, "blowout", O, "Blowout share (winner's hill time at least 2x the loser's)", blow, n);
            Median(r, "lead.changes", O, "Hill lead changes per match (median; spread = p25 to p75)", Each(ms, m => m.LeadChanges));
            Mean(r, "lead.changes.mean", O, "Hill lead changes per match (mean)", Each(ms, m => m.LeadChanges));
            Median(r, "decided.at", O, "Result decided at (last lead change)", Each(ms, m => m.DecidedAt), "s");
            Mean(r, "decided.at.share", O, "Result decided at, share of the match", Each(ms, m => Ratio(m.DecidedAt, m.Length)));
            Rate(r, "leader.half.win", O, "Leader at half time wins", halfWon, halfN, "matches with a leader at 4:00 and a winner");
            Mean(r, "hill.owned", O, "Hill held (not contested, not neutral), share of samples after 0:30", Each(ms, m => m.OwnedShare));
            Median(r, "hill.longest", O, "Longest unbroken hill hold", Each(ms, m => m.LongestHold), "s");

            const string T = "Time per class (share of each body's match)";
            for (int c = 0; c <= MatchRecord.Classes; c++)
            {
                bool all = c == MatchRecord.Classes;
                string cn = all ? "all" : ClassNames[c];
                Func<MatchRecord, double[], double> share = (m, a) =>
                {
                    if (all)
                    {
                        double num = 0, den = 0;
                        for (int k = 0; k < MatchRecord.Classes; k++)
                        {
                            num += a[k];
                            den += m.ActorSec[k];
                        }
                        return Ratio(num, den);
                    }
                    return Ratio(a[c], m.ActorSec[c]);
                };
                Mean(r, "time.dead." + cn, T, cn + ": dead or getting up", Each(ms, m => share(m, m.DeadSec)));
                Mean(r, "time.walk." + cn, T, cn + ": walking (moving, not fighting, empty-handed, off the hill)", Each(ms, m => share(m, m.WalkSec)));
                Mean(r, "time.fight." + cn, T, cn + ": fighting (hit dealt or taken, or a swing, in the last 1 s)", Each(ms, m => share(m, m.FightSec)));
                Mean(r, "time.deadwalk." + cn, T, cn + ": dead or walking", Each(ms, m =>
                {
                    double[] sum = new double[MatchRecord.Classes];
                    for (int k = 0; k < MatchRecord.Classes; k++) sum[k] = m.DeadSec[k] + m.WalkSec[k];
                    return share(m, sum);
                }));
            }

            const string P = "Per class (per body per match)";
            for (int c = 0; c < MatchRecord.Classes; c++)
            {
                string cn = ClassNames[c];
                int cc = c;
                Mean(r, "dmg." + cn, P, cn + ": damage dealt to fighters", Each(ms, m => Ratio(m.Damage[cc], m.Body[cc])), "HP");
                Mean(r, "kills." + cn, P, cn + ": kills", Each(ms, m => Ratio(m.Kills[cc], m.Body[cc])));
                Mean(r, "deaths." + cn, P, cn + ": deaths", Each(ms, m => Ratio(m.Deaths[cc], m.Body[cc])));
                Mean(r, "heal." + cn, P, cn + ": healing done (Healer mends and pulses)", Each(ms, m => Ratio(m.Healing[cc], m.Body[cc])), "HP");
                Mean(r, "obj." + cn, P, cn + ": objective credit (hill seconds scored, Actor.HillSeconds)", Each(ms, m => Ratio(m.Objective[cc], m.Body[cc])), "s");
                Mean(r, "deliv." + cn, P, cn + ": units delivered to the egg", Each(ms, m => Ratio(m.Delivered[cc], m.Body[cc])));
                Mean(r, "kaijudmg." + cn, P, cn + ": damage dealt to the Kaiju", Each(ms, m => Ratio(m.KaijuDamage[cc], m.Body[cc])), "HP");
                Mean(r, "killshare." + cn, P, cn + ": share of all kills (whole class)", Each(ms, m =>
                {
                    double tot = 0;
                    for (int k = 0; k < MatchRecord.Classes; k++) tot += m.Kills[k];
                    return Ratio(m.Kills[cc], tot);
                }));
            }
            const string C = "Concentration";
            Mean(r, "dmg.top10", C, "Top-10 share of all fighter damage (10 of " + (n > 0 ? ms[0].Bodies : 0) + " bodies)", Each(ms, m => m.Top10DamageShare));
            Mean(r, "kills.top10", C, "Top-10 share of all kills", Each(ms, m => m.Top10KillShare));

            DuelRows(r, ttk, duels);

            const string K = "Kaiju";
            int hatched = 0, ownN = 0, ownWon = 0, berserk = 0;
            var typeCount = new int[KaijuTypes.Length];
            foreach (MatchRecord m in ms)
            {
                if (!m.Hatched) continue;
                hatched++;
                int ti = Array.IndexOf(KaijuTypes, m.KaijuType);
                if (ti >= 0) typeCount[ti]++;
                if (m.KaijuOwner == KaijuState.Berserk) berserk++;
                bool decided = m.Winner == MatchResult.Azure || m.Winner == MatchResult.Crimson;
                if (decided && (m.KaijuOwner == KaijuState.Azure || m.KaijuOwner == KaijuState.Crimson))
                {
                    ownN++;
                    if ((m.KaijuOwner == KaijuState.Azure) == (m.Winner == MatchResult.Azure)) ownWon++;
                }
            }
            Rate(r, "kaiju.hatched", K, "Matches where the egg hatched", hatched, n);
            for (int k = 0; k < KaijuTypes.Length; k++) Rate(r, "kaiju.type." + KaijuTypes[k], K, KaijuTypes[k] + " share of hatches", typeCount[k], hatched);
            Rate(r, "kaiju.owner.win", K, "Kaiju owner (side at the hatch) wins", ownWon, ownN, "hatched, owned by a team, with a winner");
            Rate(r, "kaiju.berserk", K, "Berserk share of hatches", berserk, hatched);

            const string H = "Blacksmith hut";
            for (int l = 0; l < 5; l++)
            {
                var times = new List<double>();
                foreach (MatchRecord m in ms)
                    for (int t = 0; t < 2; t++)
                        if (!double.IsNaN(m.HutAt[t, l])) times.Add(m.HutAt[t, l]);
                Median(r, "hut.L" + (l + 1), H, "Hut level " + (l + 1) + " reached at (team-matches that reached it)", times, "s");
            }
            int l5 = 0;
            foreach (MatchRecord m in ms)
                for (int t = 0; t < 2; t++)
                    if (!double.IsNaN(m.HutAt[t, 4])) l5++;
            Rate(r, "hut.L5.reach", H, "Team-matches reaching level 5", l5, 2 * n);

            const string S = "Structures (per match)";
            Mean(r, "struct.walls.built", S, "Builder walls built", Each(ms, m => m.WallsBuilt));
            Mean(r, "struct.walls.broken", S, "Builder wall cells broken or crumbled", Each(ms, m => m.WallsBroken));
            Mean(r, "struct.pieces.placed", S, "Ladders and bridges placed", Each(ms, m => m.PiecesPlaced));
            Mean(r, "struct.pieces.destroyed", S, "Ladders and bridges destroyed", Each(ms, m => m.PiecesDestroyed));
            Mean(r, "struct.barricades.placed", S, "Barricades placed", Each(ms, m => m.BarricadesPlaced));
            Mean(r, "struct.barricades.broken", S, "Barricades broken", Each(ms, m => m.BarricadesBroken));
            Mean(r, "struct.topples", S, "Decorations toppled", Each(ms, m => m.Topples));

            const string B = "Bots and cost";
            Median(r, "stuck", B, "Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching)", Each(ms, m => m.Stuck));
            Mean(r, "stuck.mean", B, "Stuck episodes per match (mean)", Each(ms, m => m.Stuck));
            BalanceRow tm = Mean(r, "tick.mean.us", B, "Sim tick cost, mean (wall clock, this machine)", Each(ms, m => m.TickMeanUs), "us");
            tm.Comparable = false;
            BalanceRow tp = Median(r, "tick.p99.us", B, "Sim tick cost, p99 within a match (median over matches)", Each(ms, m => m.TickP99Us), "us");
            tp.Comparable = false;
            int lost = 0, desync = 0;
            foreach (MatchRecord m in ms)
            {
                lost += m.EventsLost;
                if (m.Desynced) desync++;
            }
            r.Rows.Add(new BalanceRow { Key = "events.lost", Group = B, Label = "Sim events lost to the ring buffer (must be 0)", Kind = "count", Value = lost, N = n, Comparable = false });
            r.Rows.Add(new BalanceRow { Key = "replays.desynced", Group = B, Label = "Replays that stopped at a fingerprint mismatch", Kind = "count", Value = desync, N = n, Comparable = false });

            r.Tables.Add(StuckTable(ms));
            r.NeedsCounter.Add("Hut choice pick and win rates: this build has no hut choice (it lands with feat/b15-comeback-hut); the row appears once a HutChoice event or state exists.");
            return r;
        }

        static void DuelRows(BalanceReport r, DuelRunner.Cell[,] ttk, DuelRunner.Cell[,] duels)
        {
            const string D = "Duels (1v1, no bots)";
            if (ttk != null)
            {
                var cellMedians = new List<double>();
                int inBand = 0, finite = 0;
                for (int a = 0; a < 6; a++)
                    for (int b = 0; b < 6; b++)
                    {
                        DuelRunner.Cell c = ttk[a, b];
                        var s = BalanceStat.Sorted(c.Ttk);
                        BalanceStat.Median(s, out double med, out double lo, out double hi);
                        r.Rows.Add(new BalanceRow
                        {
                            Key = "ttk." + ClassNames[a] + "." + ClassNames[b], Group = D, Label = "Time to kill: " + ClassNames[a] + " on a passive " + ClassNames[b],
                            Kind = "median", Value = med, Lo = lo, Hi = hi, N = s.Count, Unit = "s",
                            Note = c.Kills < c.Reps ? "killed in " + c.Kills + " of " + c.Reps + " (30 s cap)" : "",
                        });
                        if (s.Count == 0) continue;
                        finite++;
                        cellMedians.Add(med);
                        if (med >= TtkMin - 1e-9 && med <= TtkMax + 1e-9) inBand++;
                    }
                Median(r, "ttk.median", D, "Time to kill, median over the matchups that can kill", cellMedians, "s", "n = matchups");
                r.Rows.Add(new BalanceRow { Key = "ttk.inband", Group = D, Label = "Matchups whose time to kill is within 3 to 6 s", Kind = "count", Value = inBand, N = finite, Comparable = false });
            }
            if (duels != null)
            {
                int unbeaten = 0;
                string who = "";
                for (int a = 0; a < 6; a++)
                {
                    bool beatsAll = true;
                    for (int b = 0; b < 6; b++)
                    {
                        DuelRunner.Cell c = duels[a, b];
                        if (a != b)
                        {
                            BalanceStat.Wilson(c.Wins, c.Reps, out double p, out double lo, out double hi);
                            r.Rows.Add(new BalanceRow
                            {
                                Key = "duel." + ClassNames[a] + "." + ClassNames[b], Group = D, Label = "Duel: " + ClassNames[a] + " beats " + ClassNames[b],
                                Kind = "rate", Value = p, Lo = lo, Hi = hi, N = c.Reps, Note = c.Draws > 0 ? c.Draws + " draws" : "",
                            });
                            if (!(c.Wins * 2 > c.Reps)) beatsAll = false;
                        }
                    }
                    if (beatsAll)
                    {
                        unbeaten++;
                        who += (who.Length > 0 ? ", " : "") + ClassNames[a];
                    }
                }
                r.Rows.Add(new BalanceRow { Key = "duel.unbeaten", Group = D, Label = "Classes that win every duel (more than half of each matchup)", Kind = "count", Value = unbeaten, N = 6, Note = who, Comparable = false });
            }
        }

        static string StuckTable(List<MatchRecord> ms)
        {
            var count = new Dictionary<int, int>();
            int width = ms.Count > 0 ? ms[0].MapWidth : 1;
            foreach (MatchRecord m in ms)
                foreach (int cell in m.StuckCells)
                    count[cell] = count.TryGetValue(cell, out int c) ? c + 1 : 1;
            var list = new List<KeyValuePair<int, int>>(count);
            list.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
            var sb = new StringBuilder();
            sb.Append("### Stuck episodes: top cells (all matches)\n\n| Cell (x, z) | Episodes |\n|---|---|\n");
            for (int i = 0; i < Math.Min(10, list.Count); i++)
                sb.Append("| (").Append(list[i].Key % Math.Max(1, width)).Append(", ").Append(list[i].Key / Math.Max(1, width)).Append(") | ").Append(list[i].Value).Append(" |\n");
            if (list.Count == 0) sb.Append("| none | 0 |\n");
            return sb.ToString();
        }
    }

    /// <summary>b15 tools: one target from docs/balance/TARGETS.json (a band on one report row).</summary>
    public sealed class BalanceTarget
    {
        public string Id = "", Row = "", Label = "", Source = "";
        public double Min = double.NaN, Max = double.NaN;
        /// <summary>True: the value must be strictly below Max (for example "stuck median under 5").</summary>
        public bool MaxExclusive;

        public string Band()
        {
            string lo = double.IsNaN(Min) ? "" : BalanceFormat.Num(Min);
            string hi = double.IsNaN(Max) ? "" : BalanceFormat.Num(Max);
            if (lo.Length > 0 && hi.Length > 0) return lo + " to " + hi;
            if (lo.Length > 0) return ">= " + lo;
            return (MaxExclusive ? "< " : "<= ") + hi;
        }

        public bool Meets(double v)
        {
            if (double.IsNaN(v)) return false;
            if (!double.IsNaN(Min) && v < Min - 1e-9) return false;
            if (!double.IsNaN(Max) && (MaxExclusive ? v >= Max - 1e-12 : v > Max + 1e-9)) return false;
            return true;
        }

        /// <summary>"met", "missed" or "no data"; note says when the 95% interval crosses a bound (the verdict could be noise).</summary>
        public string Verdict(BalanceRow row, out string note)
        {
            note = "";
            if (row == null || double.IsNaN(row.Value)) return "no data";
            bool ok = Meets(row.Value);
            if (!double.IsNaN(row.Lo) && !double.IsNaN(row.Hi) && (Meets(row.Lo) != ok || Meets(row.Hi) != ok)) note = "95% interval crosses the bound";
            return ok ? "met" : "missed";
        }

        public static List<BalanceTarget> FromJson(string json)
        {
            var list = new List<BalanceTarget>();
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root) || !(root.TryGetValue("targets", out object t) && t is List<object> items)) return list;
            foreach (object o in items)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                list.Add(new BalanceTarget
                {
                    Id = MiniJson.Str(d, "id"), Row = MiniJson.Str(d, "row"), Label = MiniJson.Str(d, "label"), Source = MiniJson.Str(d, "source"),
                    Min = MiniJson.Num(d, "min"), Max = MiniJson.Num(d, "max"), MaxExclusive = MiniJson.Bool(d, "maxExclusive"),
                });
            }
            return list;
        }
    }

    public static class BalanceFormat
    {
        public static string Num(double v)
        {
            if (double.IsNaN(v)) return "n/a";
            if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
            double a = Math.Abs(v);
            string f = a >= 1000 ? "0" : a >= 100 ? "0.0" : a >= 1 ? "0.00" : "0.000";
            return v.ToString(f, CultureInfo.InvariantCulture);
        }

        public static string Value(BalanceRow r) => (r.Kind == "count" && !double.IsNaN(r.Value) ? r.Value.ToString("0", CultureInfo.InvariantCulture) : Num(r.Value)) + (r.Unit.Length > 0 && !double.IsNaN(r.Value) ? " " + r.Unit : "");
        public static string Ci(BalanceRow r) => double.IsNaN(r.Lo) ? "" : Num(r.Lo) + " to " + Num(r.Hi);
        public static string Spread(BalanceRow r) => double.IsNaN(r.P25) ? "" : Num(r.P25) + " to " + Num(r.P75);
        public static string Cell(string s) => (s ?? "").Replace("|", "/").Replace("\n", " ");
        public static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        public static string R(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>b15 tools: the report as Markdown, CSV and JSON, and the compare table.</summary>
    public static class BalanceWriter
    {
        public static string Markdown(BalanceReport r, List<BalanceTarget> targets)
        {
            var sb = new StringBuilder();
            sb.Append("# Balance report\n\n");
            foreach (var kv in r.Meta) sb.Append("- ").Append(kv.Key).Append(": ").Append(BalanceFormat.Cell(kv.Value)).Append('\n');
            sb.Append("\nEvery number shows its sample size n. Rates carry a 95% Wilson interval, means a 95% t interval, medians a 95% rank interval; ")
              .Append("spread is p25 to p75 over matches. Definitions: Assets/_Game/Sim.Tests/Harness/BalanceProbe.cs.\n\n");
            if (targets != null && targets.Count > 0)
            {
                sb.Append("## Targets (docs/balance/TARGETS.json)\n\n| Target | Row | Value | 95% CI | n | Band | Verdict |\n|---|---|---|---|---|---|---|\n");
                foreach (BalanceTarget t in targets)
                {
                    BalanceRow row = r.Row(t.Row);
                    string v = t.Verdict(row, out string note);
                    sb.Append("| ").Append(BalanceFormat.Cell(t.Label)).Append(" | `").Append(t.Row).Append("` | ")
                      .Append(row != null ? BalanceFormat.Value(row) : "n/a").Append(" | ").Append(row != null ? BalanceFormat.Ci(row) : "").Append(" | ")
                      .Append(row != null ? row.N.ToString(CultureInfo.InvariantCulture) : "0").Append(" | ").Append(t.Band()).Append(" | **").Append(v).Append("**")
                      .Append(note.Length > 0 ? " (" + note + ")" : "").Append(" |\n");
                }
                sb.Append('\n');
            }
            string group = null;
            foreach (BalanceRow row in r.Rows)
            {
                if (row.Group.StartsWith("Duels", StringComparison.Ordinal)) continue;
                if (row.Group != group)
                {
                    group = row.Group;
                    sb.Append("\n## ").Append(group).Append("\n\n| Stat | Value | 95% CI | n | Spread | Note |\n|---|---|---|---|---|---|\n");
                }
                sb.Append("| ").Append(BalanceFormat.Cell(row.Label)).Append(" | ").Append(BalanceFormat.Value(row)).Append(" | ").Append(BalanceFormat.Ci(row))
                  .Append(" | ").Append(row.N).Append(" | ").Append(BalanceFormat.Spread(row)).Append(" | ").Append(BalanceFormat.Cell(row.Note)).Append(" |\n");
            }
            sb.Append('\n').Append(DuelMarkdown(r));
            foreach (string t in r.Tables) sb.Append('\n').Append(t);
            sb.Append("\n## Needs a sim counter (left out)\n\n");
            if (r.NeedsCounter.Count == 0) sb.Append("Nothing.\n");
            foreach (string s in r.NeedsCounter) sb.Append("- ").Append(s).Append('\n');
            sb.Append("\n## Matches\n\n| Source | Winner | Hill Azure | Hill Crimson | Lead changes | Decided at (s) | Blowout | Kaiju | Owner | Stuck | Tick mean (us) |\n|---|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (MatchRecord m in r.Matches)
                sb.Append("| ").Append(BalanceFormat.Cell(m.Source)).Append(" | ").Append(m.Winner).Append(" | ").Append(BalanceFormat.Num(m.HillAzure)).Append(" | ")
                  .Append(BalanceFormat.Num(m.HillCrimson)).Append(" | ").Append(m.LeadChanges).Append(" | ").Append(BalanceFormat.Num(m.DecidedAt)).Append(" | ")
                  .Append(m.Blowout ? "yes" : "no").Append(" | ").Append(m.Hatched ? m.KaijuType : "none").Append(" | ").Append(m.Hatched ? m.KaijuOwner.ToString() : "")
                  .Append(" | ").Append(m.Stuck).Append(" | ").Append(BalanceFormat.Num(m.TickMeanUs)).Append(m.Desynced ? " (desynced)" : "").Append(" |\n");
            return sb.ToString();
        }

        static string DuelMarkdown(BalanceReport r)
        {
            var sb = new StringBuilder();
            string[] cn = BalanceAggregate.ClassNames;
            if (r.Ttk != null || r.Duels != null)
            {
                sb.Append("## Duels (1v1, no bots)\n\nPlayed on an empty map at the hill, every other body removed; primary attack only (held, aimed, walking in to 0.8 x range); ")
                  .Append("no abilities, dodge or hut levels; ").Append(r.Ttk != null && r.Ttk[0, 0] != null ? r.Ttk[0, 0].Reps : 0)
                  .Append(" start angles per cell; 30 s cap. The sim is deterministic, so the angles are the only spread: n counts tries, not independent samples.\n\n");
                foreach (BalanceRow row in r.Rows)
                    if (row.Key == "ttk.median" || row.Key == "ttk.inband" || row.Key == "duel.unbeaten")
                        sb.Append("- ").Append(row.Label).Append(": ").Append(BalanceFormat.Value(row)).Append(row.Kind == "count" ? " of " + row.N : " (95% CI " + BalanceFormat.Ci(row) + ", n " + row.N + ")")
                          .Append(row.Note.Length > 0 ? " (" + row.Note + ")" : "").Append('\n');
                sb.Append('\n');
            }
            if (r.Ttk != null)
            {
                sb.Append("### Time to kill (s): row attacks a passive column (median over start angles; kills / tries)\n\n| attacker \\ target |");
                foreach (string c in cn) sb.Append(' ').Append(c).Append(" |");
                sb.Append("\n|---|---|---|---|---|---|---|\n");
                for (int a = 0; a < 6; a++)
                {
                    sb.Append("| ").Append(cn[a]).Append(" |");
                    for (int b = 0; b < 6; b++)
                    {
                        DuelRunner.Cell c = r.Ttk[a, b];
                        var s = BalanceStat.Sorted(c.Ttk);
                        double med = BalanceStat.Quantile(s, 0.5);
                        bool band = !double.IsNaN(med) && med >= BalanceAggregate.TtkMin - 1e-9 && med <= BalanceAggregate.TtkMax + 1e-9;
                        sb.Append(' ').Append(s.Count == 0 ? "no kill" : BalanceFormat.Num(med) + (band ? "" : " *")).Append(" (").Append(c.Kills).Append('/').Append(c.Reps).Append(") |");
                    }
                    sb.Append('\n');
                }
                sb.Append("\n`*` = outside 3 to 6 s.\n\n");
            }
            if (r.Duels != null)
            {
                sb.Append("### Duel win share: row beats column (wins / tries; draws are timeouts or trades; mirror matches not shown)\n\n| row \\ column |");
                foreach (string c in cn) sb.Append(' ').Append(c).Append(" |");
                sb.Append("\n|---|---|---|---|---|---|---|\n");
                for (int a = 0; a < 6; a++)
                {
                    sb.Append("| ").Append(cn[a]).Append(" |");
                    for (int b = 0; b < 6; b++)
                    {
                        DuelRunner.Cell c = r.Duels[a, b];
                        if (a == b)
                        {
                            sb.Append(" - |");
                            continue;
                        }
                        sb.Append(' ').Append(c.Reps > 0 ? BalanceFormat.Num((double)c.Wins / c.Reps) : "n/a").Append(" (").Append(c.Wins).Append('/').Append(c.Reps).Append(") |");
                    }
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        public static string Csv(BalanceReport r)
        {
            var sb = new StringBuilder("key,group,label,kind,value,ci_lo,ci_hi,n,p25,p75,unit,comparable,note\n");
            foreach (BalanceRow row in r.Rows)
                sb.Append(row.Key).Append(',').Append(BalanceFormat.Csv(row.Group)).Append(',').Append(BalanceFormat.Csv(row.Label)).Append(',').Append(row.Kind).Append(',')
                  .Append(BalanceFormat.R(row.Value)).Append(',').Append(BalanceFormat.R(row.Lo)).Append(',').Append(BalanceFormat.R(row.Hi)).Append(',').Append(row.N).Append(',')
                  .Append(BalanceFormat.R(row.P25)).Append(',').Append(BalanceFormat.R(row.P75)).Append(',').Append(row.Unit).Append(',').Append(row.Comparable ? "1" : "0").Append(',')
                  .Append(BalanceFormat.Csv(row.Note)).Append('\n');
            return sb.ToString();
        }

        public static string Json(BalanceReport r)
        {
            var j = new MiniJson.Writer();
            j.Open('{');
            j.Key("meta");
            j.Open('{');
            foreach (var kv in r.Meta) j.Prop(kv.Key, kv.Value);
            j.Close('}');
            j.Key("rows");
            j.Open('[');
            foreach (BalanceRow row in r.Rows)
            {
                j.Open('{');
                j.Prop("key", row.Key);
                j.Prop("group", row.Group);
                j.Prop("label", row.Label);
                j.Prop("kind", row.Kind);
                j.Prop("value", row.Value);
                j.Prop("lo", row.Lo);
                j.Prop("hi", row.Hi);
                j.Prop("n", row.N);
                j.Prop("p25", row.P25);
                j.Prop("p75", row.P75);
                j.Prop("unit", row.Unit);
                j.Prop("comparable", row.Comparable);
                j.Prop("note", row.Note);
                j.Close('}');
            }
            j.Close(']');
            j.Key("needsCounter");
            j.Open('[');
            foreach (string s in r.NeedsCounter) j.Item(s);
            j.Close(']');
            j.Key("matches");
            j.Open('[');
            foreach (MatchRecord m in r.Matches)
            {
                j.Open('{');
                j.Prop("source", m.Source);
                j.Prop("seed", m.Seed);
                j.Prop("teamSize", m.TeamSize);
                j.Prop("ticks", m.Ticks);
                j.Prop("length", m.Length);
                j.Prop("winner", m.Winner.ToString());
                j.Prop("hillAzure", m.HillAzure);
                j.Prop("hillCrimson", m.HillCrimson);
                j.Prop("leadChanges", m.LeadChanges);
                j.Prop("decidedAt", m.DecidedAt);
                j.Prop("leaderAtHalf", m.LeaderAtHalf);
                j.Prop("ownedShare", m.OwnedShare);
                j.Prop("longestHold", m.LongestHold);
                j.Prop("hatched", m.Hatched);
                j.Prop("kaijuType", m.KaijuType);
                j.Prop("kaijuOwner", m.Hatched ? m.KaijuOwner.ToString() : "");
                j.Prop("stuck", m.Stuck);
                j.Prop("top10DamageShare", m.Top10DamageShare);
                j.Prop("tickMeanUs", m.TickMeanUs);
                j.Prop("tickP99Us", m.TickP99Us);
                j.Prop("desynced", m.Desynced);
                j.Close('}');
            }
            j.Close(']');
            j.Close('}');
            return j.ToString();
        }

        /// <summary>Reads the rows and meta of a report JSON (the compare mode's input from another run).</summary>
        public static BalanceReport FromJson(string json)
        {
            var r = new BalanceReport();
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) throw new FormatException("not a balance report JSON");
            if (root.TryGetValue("meta", out object meta) && meta is Dictionary<string, object> md)
                foreach (var kv in md) r.Meta.Add(new KeyValuePair<string, string>(kv.Key, kv.Value as string ?? Convert.ToString(kv.Value, CultureInfo.InvariantCulture)));
            if (root.TryGetValue("rows", out object rows) && rows is List<object> list)
                foreach (object o in list)
                {
                    if (!(o is Dictionary<string, object> d)) continue;
                    r.Rows.Add(new BalanceRow
                    {
                        Key = MiniJson.Str(d, "key"), Group = MiniJson.Str(d, "group"), Label = MiniJson.Str(d, "label"), Kind = MiniJson.Str(d, "kind"),
                        Value = MiniJson.Num(d, "value"), Lo = MiniJson.Num(d, "lo"), Hi = MiniJson.Num(d, "hi"), N = (int)MiniJson.NumOr(d, "n", 0),
                        P25 = MiniJson.Num(d, "p25"), P75 = MiniJson.Num(d, "p75"), Unit = MiniJson.Str(d, "unit"), Comparable = MiniJson.Bool(d, "comparable"),
                        Note = MiniJson.Str(d, "note"),
                    });
                }
            if (root.TryGetValue("needsCounter", out object nc) && nc is List<object> ncl)
                foreach (object o in ncl)
                    if (o is string s) r.NeedsCounter.Add(s);
            return r;
        }
    }

    /// <summary>b15 tools: base vs head, row by row. A difference is outside the noise when it exceeds sqrt(h_base^2 + h_head^2), h = the
    /// rows' 95% half-widths (an unpaired, slightly conservative rule).</summary>
    public static class BalanceCompare
    {
        public sealed class Line
        {
            public string Key, Label;
            public BalanceRow Base, Head;
            public double Diff = double.NaN, Noise = double.NaN;
            public string Verdict = "";
        }

        public static List<Line> Lines(BalanceReport b, BalanceReport h)
        {
            var lines = new List<Line>();
            foreach (BalanceRow hr in h.Rows)
            {
                BalanceRow br = b.Row(hr.Key);
                var l = new Line { Key = hr.Key, Label = hr.Label, Base = br, Head = hr };
                if (br == null) l.Verdict = "head only";
                else
                {
                    l.Diff = hr.Value - br.Value;
                    double hb = br.Half, hh = hr.Half;
                    if (!(br.Comparable && hr.Comparable) || double.IsNaN(hb) || double.IsNaN(hh)) l.Verdict = double.IsNaN(l.Diff) ? "no data" : "not judged";
                    else
                    {
                        l.Noise = Math.Sqrt(hb * hb + hh * hh);
                        l.Verdict = double.IsNaN(l.Diff) ? "no data" : Math.Abs(l.Diff) > l.Noise + 1e-12 ? "OUTSIDE noise" : "within noise";
                    }
                }
                lines.Add(l);
            }
            foreach (BalanceRow br in b.Rows)
                if (h.Row(br.Key) == null) lines.Add(new Line { Key = br.Key, Label = br.Label, Base = br, Verdict = "base only" });
            return lines;
        }

        public static string Markdown(BalanceReport b, BalanceReport h, List<BalanceTarget> targets)
        {
            var sb = new StringBuilder("# Balance compare\n\n");
            sb.Append("- base: ").Append(BalanceFormat.Cell(b.MetaValue("label"))).Append(" (").Append(BalanceFormat.Cell(b.MetaValue("commit"))).Append(", ")
              .Append(BalanceFormat.Cell(b.MetaValue("switches"))).Append(")\n");
            sb.Append("- head: ").Append(BalanceFormat.Cell(h.MetaValue("label"))).Append(" (").Append(BalanceFormat.Cell(h.MetaValue("commit"))).Append(", ")
              .Append(BalanceFormat.Cell(h.MetaValue("switches"))).Append(")\n");
            sb.Append("- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.\n\n");
            var targetByRow = new Dictionary<string, BalanceTarget>();
            if (targets != null)
                foreach (BalanceTarget t in targets) targetByRow[t.Row] = t;
            List<Line> lines = Lines(b, h);
            int outside = 0;
            foreach (Line l in lines)
                if (l.Verdict == "OUTSIDE noise") outside++;
            sb.Append(outside).Append(" of ").Append(lines.Count).Append(" rows moved outside the noise.\n\n");
            sb.Append("| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |\n|---|---|---|---|---|---|---|\n");
            foreach (Line l in lines)
            {
                string tgt = "";
                if (targetByRow.TryGetValue(l.Key, out BalanceTarget t))
                    tgt = t.Band() + ": " + t.Verdict(l.Base, out string _) + " / " + t.Verdict(l.Head, out string _);
                sb.Append("| ").Append(BalanceFormat.Cell(l.Label)).Append(" | ").Append(Cell(l.Base)).Append(" | ").Append(Cell(l.Head)).Append(" | ")
                  .Append(BalanceFormat.Num(l.Diff)).Append(" | ").Append(double.IsNaN(l.Noise) ? "" : "+-" + BalanceFormat.Num(l.Noise)).Append(" | ")
                  .Append(l.Verdict == "OUTSIDE noise" ? "**OUTSIDE noise**" : l.Verdict).Append(" | ").Append(tgt).Append(" |\n");
            }
            return sb.ToString();
        }

        static string Cell(BalanceRow r) => r == null ? "" : BalanceFormat.Value(r) + (double.IsNaN(r.Lo) ? "" : " (" + BalanceFormat.Ci(r) + ")") + ", n " + r.N;

        public static string Csv(BalanceReport b, BalanceReport h)
        {
            var sb = new StringBuilder("key,label,base,base_lo,base_hi,base_n,head,head_lo,head_hi,head_n,diff,noise,verdict\n");
            foreach (Line l in Lines(b, h))
                sb.Append(l.Key).Append(',').Append(BalanceFormat.Csv(l.Label)).Append(',')
                  .Append(l.Base != null ? BalanceFormat.R(l.Base.Value) + "," + BalanceFormat.R(l.Base.Lo) + "," + BalanceFormat.R(l.Base.Hi) + "," + l.Base.N : ",,,").Append(',')
                  .Append(l.Head != null ? BalanceFormat.R(l.Head.Value) + "," + BalanceFormat.R(l.Head.Lo) + "," + BalanceFormat.R(l.Head.Hi) + "," + l.Head.N : ",,,").Append(',')
                  .Append(BalanceFormat.R(l.Diff)).Append(',').Append(BalanceFormat.R(l.Noise)).Append(',').Append(l.Verdict).Append('\n');
            return sb.ToString();
        }

        public static string Json(BalanceReport b, BalanceReport h)
        {
            var j = new MiniJson.Writer();
            j.Open('{');
            j.Prop("base", b.MetaValue("label") + " " + b.MetaValue("commit") + " " + b.MetaValue("switches"));
            j.Prop("head", h.MetaValue("label") + " " + h.MetaValue("commit") + " " + h.MetaValue("switches"));
            j.Key("lines");
            j.Open('[');
            foreach (Line l in Lines(b, h))
            {
                j.Open('{');
                j.Prop("key", l.Key);
                j.Prop("base", l.Base != null ? l.Base.Value : double.NaN);
                j.Prop("head", l.Head != null ? l.Head.Value : double.NaN);
                j.Prop("diff", l.Diff);
                j.Prop("noise", l.Noise);
                j.Prop("verdict", l.Verdict);
                j.Close('}');
            }
            j.Close(']');
            j.Close('}');
            return j.ToString();
        }
    }

    /// <summary>b15 tools: a minimal JSON reader and writer (no dependency: the Sim tests also compile inside Unity).</summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("JSON: trailing text at " + i);
            return v;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return d;
                }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("JSON: ':' expected at " + i);
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (i < s.Length && s[i] == '}')
                    {
                        i++;
                        return d;
                    }
                    throw new FormatException("JSON: ',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return l;
                }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (i < s.Length && s[i] == ']')
                    {
                        i++;
                        return l;
                    }
                    throw new FormatException("JSON: ',' or ']' expected at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0)
            {
                i += 4;
                return true;
            }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0)
            {
                i += 5;
                return false;
            }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0)
            {
                i += 4;
                return null;
            }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == st) throw new FormatException("JSON: unexpected '" + c + "' at " + i);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("JSON: string expected at " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            if (i >= s.Length) throw new FormatException("JSON: unterminated string");
            i++;
            return sb.ToString();
        }

        public static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) && v is string s ? s : "";
        public static double Num(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) && v is double x ? x : double.NaN;
        public static double NumOr(Dictionary<string, object> d, string k, double or) => d.TryGetValue(k, out object v) && v is double x ? x : or;
        public static bool Bool(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) && v is bool b && b;

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>A compact writer: NaN and infinities are written as null.</summary>
        public sealed class Writer
        {
            readonly StringBuilder _sb = new StringBuilder();
            readonly Stack<bool> _first = new Stack<bool>();
            bool _afterKey;

            void Sep()
            {
                if (_afterKey)
                {
                    _afterKey = false;
                    return;
                }
                if (_first.Count == 0) return;
                if (_first.Peek()) _first.Pop();
                else
                {
                    _sb.Append(',');
                    _first.Pop();
                }
                _first.Push(false);
            }

            public void Open(char c)
            {
                Sep();
                _sb.Append(c);
                _first.Push(true);
            }

            public void Close(char c)
            {
                _first.Pop();
                _sb.Append(c);
                if (c == '}' || c == ']') _sb.Append('\n');
            }

            public void Key(string k)
            {
                Sep();
                _sb.Append(Quote(k)).Append(':');
                _afterKey = true;
            }

            public void Item(string v)
            {
                Sep();
                _sb.Append(Quote(v));
            }

            public void Prop(string k, string v)
            {
                Key(k);
                Sep();
                _sb.Append(Quote(v));
            }

            public void Prop(string k, double v)
            {
                Key(k);
                Sep();
                _sb.Append(double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("R", CultureInfo.InvariantCulture));
            }

            public void Prop(string k, int v)
            {
                Key(k);
                Sep();
                _sb.Append(v.ToString(CultureInfo.InvariantCulture));
            }

            public void Prop(string k, bool v)
            {
                Key(k);
                Sep();
                _sb.Append(v ? "true" : "false");
            }

            public override string ToString() => _sb.ToString();
        }
    }
}

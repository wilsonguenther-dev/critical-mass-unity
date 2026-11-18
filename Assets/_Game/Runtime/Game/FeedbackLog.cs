using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// b15 safe defaults, item 4 (blind-spot audit T1/T3, owner-approved 2026-11-17): the match feedback file. One JSON object
    /// per line in <see cref="FileName"/> next to the replays (ReplayStore.Folder; its pruning only deletes *.cmreplay):
    /// <list type="bullet">
    /// <item>"match": one line per human match: build, class, team, result, length, seed, replay file, a frame-time summary
    /// and the optional card answers (fun 1..5, what felt bad, a one-line note) or skipped.</item>
    /// <item>"first_fun": time-to-first-fun markers for this launch (seconds from launch to the first match start, the first
    /// hit landed and the first objective credit; null until it happens).</item>
    /// </list>
    /// Nothing is ever sent anywhere: the player copies or sends the file. Pure apart from <see cref="Append"/>; FeedbackLogTests.
    /// </summary>
    public static class FeedbackLog
    {
        public const string FileName = "feedback.jsonl";
        public const int Version = 1;
        /// <summary>The note is one line, at most this many characters.</summary>
        public const int NoteMax = 120;

        /// <summary>The "what felt bad?" chips: the id written to the file and the words on the chip.</summary>
        public static readonly string[] ChipIds = { "fighting", "controls", "confusing", "performance", "bug", "other" };
        public static readonly string[] ChipWords = { "FIGHTING", "CONTROLS", "CONFUSING", "PERFORMANCE", "BUG", "OTHER" };

        /// <summary>The feedback file in a folder (the replay folder in the game).</summary>
        public static string PathIn(string folder) => Path.Combine(folder ?? "", FileName);

        /// <summary>The local player's result: win, loss, draw or unfinished (no winner yet: quit early).</summary>
        public static string Result(int winner, int team)
        {
            // MatchResult: 0 None, 1 Azure, 2 Crimson, 3 Draw; TeamId: 0 Azure, 1 Crimson.
            if (winner == 3) return "draw";
            if (winner != 1 && winner != 2) return "unfinished";
            return winner - 1 == team ? "win" : "loss";
        }

        /// <summary>A note as written to the file: one line, trimmed, at most NoteMax characters.</summary>
        public static string CleanNote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\n' || c == '\r' || c == '\t') b.Append(' ');
                else if (!char.IsControl(c)) b.Append(c);
            }
            string t = b.ToString().Trim();
            return t.Length > NoteMax ? t.Substring(0, NoteMax) : t;
        }

        /// <summary>A JSON string literal (quotes, backslashes and control characters escaped).</summary>
        public static string Quote(string s)
        {
            if (s == null) return "null";
            var b = new StringBuilder(s.Length + 2);
            b.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            return b.Append('"').ToString();
        }

        static string Num(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("0.###", CultureInfo.InvariantCulture);
        static string Num(double? v) => v.HasValue ? Num(v.Value) : "null";

        /// <summary>The "match" line (no trailing newline).</summary>
        public static string MatchLine(MatchFeedback m)
        {
            var b = new StringBuilder(512);
            b.Append("{\"type\":\"match\",\"v\":").Append(Version);
            b.Append(",\"utc\":").Append(Quote(m.Utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
            b.Append(",\"build\":").Append(Quote(m.Build ?? "")).Append(",\"commit\":").Append(Quote(m.Commit ?? ""));
            b.Append(",\"class\":").Append(Quote(m.Class ?? "")).Append(",\"team\":").Append(Quote(m.Team ?? ""));
            b.Append(",\"result\":").Append(Quote(m.Result ?? "unfinished"));
            b.Append(",\"match_seconds\":").Append(Num(m.MatchSeconds)).Append(",\"seed\":").Append(m.Seed.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"replay\":").Append(m.Replay != null ? Quote(m.Replay) : "null");
            FrameSummary f = m.Frames;
            b.Append(",\"frames\":{\"count\":").Append(f.Count.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"p50_ms\":").Append(Num(f.P50)).Append(",\"p95_ms\":").Append(Num(f.P95)).Append(",\"p99_ms\":").Append(Num(f.P99));
            b.Append(",\"max_ms\":").Append(Num(f.Max)).Append(",\"over_33ms\":").Append(f.Over33.ToString(CultureInfo.InvariantCulture)).Append('}');
            b.Append(",\"shown\":").Append(m.Shown ? "true" : "false").Append(",\"skipped\":").Append(m.Skipped ? "true" : "false");
            b.Append(",\"fun\":").Append(m.Fun >= 1 && m.Fun <= 5 ? m.Fun.ToString(CultureInfo.InvariantCulture) : "null");
            b.Append(",\"felt_bad\":[");
            int n = 0;
            if (m.FeltBad != null)
                foreach (string id in m.FeltBad)
                {
                    if (Array.IndexOf(ChipIds, id) < 0) continue;
                    if (n++ > 0) b.Append(',');
                    b.Append(Quote(id));
                }
            b.Append("],\"note\":").Append(Quote(CleanNote(m.Note)));
            return b.Append('}').ToString();
        }

        /// <summary>The "first_fun" line (no trailing newline).</summary>
        public static string FirstFunLine(FirstFun f, DateTime utc, string build)
        {
            var b = new StringBuilder(200);
            b.Append("{\"type\":\"first_fun\",\"v\":").Append(Version);
            b.Append(",\"utc\":").Append(Quote(utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
            b.Append(",\"build\":").Append(Quote(build ?? ""));
            b.Append(",\"to_first_match_s\":").Append(Num(f.FirstMatch));
            b.Append(",\"to_first_hit_s\":").Append(Num(f.FirstHit));
            b.Append(",\"to_first_objective_s\":").Append(Num(f.FirstObjective));
            return b.Append('}').ToString();
        }

        /// <summary>Appends one line to the file (creating the folder); false and a reason when the disk refuses.</summary>
        public static bool Append(string path, string line, out string error)
        {
            error = null;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }
    }

    /// <summary>One match's line in the feedback file.</summary>
    public sealed class MatchFeedback
    {
        public DateTime Utc;
        public string Build, Commit, Class, Team, Result, Replay, Note;
        public double MatchSeconds;
        public int Seed;
        public FrameSummary Frames;
        /// <summary>The card was on screen (false: the player went straight into another match).</summary>
        public bool Shown;
        public bool Skipped;
        /// <summary>Fun 1..5; 0 = not answered.</summary>
        public int Fun;
        public List<string> FeltBad = new List<string>();
    }

    /// <summary>Time-to-first-fun markers: seconds since launch, null until it happens.</summary>
    public struct FirstFun
    {
        public double? FirstMatch, FirstHit, FirstObjective;
        public bool Complete => FirstMatch.HasValue && FirstHit.HasValue && FirstObjective.HasValue;
    }

    /// <summary>Frame-time summary of one match (milliseconds).</summary>
    public struct FrameSummary
    {
        public int Count, Over33;
        public double P50, P95, P99, Max;
    }

    /// <summary>
    /// An allocation-free frame-time histogram: 0.5 ms buckets up to 250 ms, then one overflow bucket; percentiles report a
    /// bucket's upper edge (at most 0.5 ms high). Max is exact.
    /// </summary>
    public sealed class FrameHistogram
    {
        public const double BucketMs = 0.5;
        public const int Buckets = 500;
        readonly int[] _counts = new int[Buckets + 1];
        int _count, _over33;
        double _max;

        public int Count => _count;

        public void Clear()
        {
            Array.Clear(_counts, 0, _counts.Length);
            _count = 0;
            _over33 = 0;
            _max = 0;
        }

        public void Add(double ms)
        {
            if (double.IsNaN(ms) || ms < 0) return;
            int i = (int)(ms / BucketMs);
            _counts[i < Buckets ? i : Buckets]++;
            _count++;
            if (ms > 33.0) _over33++;
            if (ms > _max) _max = ms;
        }

        /// <summary>The q-th percentile (0..1), as the upper edge of its bucket (the exact max for the overflow bucket).</summary>
        public double Percentile(double q)
        {
            if (_count == 0) return double.NaN;
            int rank = Math.Max(1, (int)Math.Ceiling(q * _count));
            int seen = 0;
            for (int i = 0; i <= Buckets; i++)
            {
                seen += _counts[i];
                if (seen >= rank) return i == Buckets ? _max : Math.Min(_max, (i + 1) * BucketMs);
            }
            return _max;
        }

        public FrameSummary Summary() => new FrameSummary
        {
            Count = _count, Over33 = _over33, P50 = Percentile(0.50), P95 = Percentile(0.95), P99 = Percentile(0.99),
            Max = _count > 0 ? _max : double.NaN,
        };
    }
}

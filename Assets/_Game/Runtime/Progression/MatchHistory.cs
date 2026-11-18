using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>
    /// b15 profile: plug points for the match-summary item (feat/b15-match-summary), which owns the results graph and the
    /// support awards. Both are optional; with nothing plugged in the history row has no turning-point sentence and the
    /// role numbers come from RoleTally.
    /// </summary>
    public static class MetaHooks
    {
        /// <summary>One plain sentence on the match's turning point ("Crimson took the lead for good at 5:12."), or null.</summary>
        public static Func<World, string> TurningPoint;
        /// <summary>Replaces the role numbers of an actor (null result = keep RoleTally's).</summary>
        public static Func<World, int, RoleStats?> RoleStatsFor;
    }

    /// <summary>
    /// b15 profile: the match history (newest first, at most Keep) and the per-class career totals. Pure functions on the
    /// profile; MetaRecorder calls Record once per finished match.
    /// </summary>
    public static class MatchHistory
    {
        public const int Keep = 50;
        public const int PageSize = 8;

        public static int PageCount(int n) => Math.Max(1, (n + PageSize - 1) / PageSize);

        /// <summary>Adds a record at the front (newest first) and drops the oldest past Keep.</summary>
        public static void Add(PlayerProfile p, MatchRecord r)
        {
            if (p == null || r == null) return;
            var list = new List<MatchRecord>(Keep + 1) { r };
            if (p.History != null)
                foreach (MatchRecord old in p.History)
                {
                    if (list.Count >= Keep) break;
                    if (old != null) list.Add(old);
                }
            p.History = list.ToArray();
        }

        /// <summary>Adds one finished match to the class's career totals.</summary>
        public static void AddCareer(ClassMastery m, in RoleStats s, MatchOutcome outcome, int score)
        {
            if (m == null) return;
            m.Matches++;
            if (outcome == MatchOutcome.Win) m.Wins++;
            else if (outcome == MatchOutcome.Draw) m.Draws++;
            else m.Losses++;
            m.KnockOuts = Sum(m.KnockOuts, s.KnockOuts);
            m.Deaths = Sum(m.Deaths, s.Deaths);
            m.HillSeconds = Sum(m.HillSeconds, s.HillSeconds);
            m.Delivered = Sum(m.Delivered, s.Delivered);
            m.Healing = Sum(m.Healing, s.Healing);
            m.OreBanked = Sum(m.OreBanked, s.OreBanked);
            m.PiecesUsed = Sum(m.PiecesUsed, s.PiecesUsed);
            m.PiecesBuilt = Sum(m.PiecesBuilt, s.PiecesBuilt);
            m.ExplosiveHits = Sum(m.ExplosiveHits, s.ExplosiveHits);
            m.Demolished = Sum(m.Demolished, s.Demolished);
            m.Purified = Sum(m.Purified, s.Purified);
            if (score > m.BestScore) m.BestScore = score;
        }

        static int Sum(int a, int b) => (int)Math.Min((long)a + Math.Max(0, b), int.MaxValue);

        /// <summary>The ISO 8601 UTC stamp written into a record (system clock at save time).</summary>
        public static string Stamp(DateTime utc) => utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        /// <summary>The record's date for a row, local time ("2026-11-17 21:40"), or the unknown-date word.</summary>
        public static string Date(MatchRecord r, bool local = true)
        {
            if (r == null || !DateTime.TryParse(r.DateUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t))
                return MetaWords.DateUnknown;
            DateTime shown = local ? t.ToLocalTime() : t.ToUniversalTime();
            return shown.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>The class's main role number for a history row (its first job stat).</summary>
        public static string KeyStat(MatchRecord r)
        {
            if (r == null || !Enum.TryParse(r.Class, out ClassId cls)) return string.Empty;
            RoleStat s = MasteryRules.Job[(int)cls][0];
            return MetaWords.StatValue(s, r.Stats.Get(s));
        }

        /// <summary>One history row: date, map, class, result, key stat, score.</summary>
        public static string Row(MatchRecord r, bool local = true)
        {
            if (r == null) return string.Empty;
            string cls = Enum.TryParse(r.Class, out ClassId c) ? ProgressionWords.ClassNames[(int)c] : r.Class.ToUpperInvariant();
            return MetaWords.F(MetaWords.HistoryRow, Date(r, local), string.IsNullOrEmpty(r.Map) ? "-" : r.Map.ToUpperInvariant(), cls,
                MetaWords.Result(r.Result), KeyStat(r), r.Score.ToString("#,0", CultureInfo.InvariantCulture));
        }

        /// <summary>The records on page (0-based) of the history.</summary>
        public static List<MatchRecord> Page(PlayerProfile p, int page)
        {
            var list = new List<MatchRecord>(PageSize);
            if (p?.History == null) return list;
            int start = Math.Max(0, page) * PageSize;
            for (int i = start; i < p.History.Length && list.Count < PageSize; i++) list.Add(p.History[i]);
            return list;
        }
    }
}

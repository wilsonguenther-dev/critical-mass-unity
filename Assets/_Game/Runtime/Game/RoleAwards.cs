using System;

namespace CriticalMass.Runtime.Game
{
    /// <summary>The six results-screen awards, one per job on the map (index = MatchWords.AwardTitles).</summary>
    public enum AwardKind { HillAnchor = 0, Lifesaver = 1, MasterBuilder = 2, Prospector = 3, TopFeeder = 4, Demolisher = 5 }

    /// <summary>One award as shown: who earned it (-1 = nobody) and the two numbers its line names.</summary>
    public struct AwardResult
    {
        public AwardKind Kind;
        public int Actor;
        public double A, B;
    }

    /// <summary>The player's own best stat: the award kind where the player ranks highest among everyone in the match.</summary>
    public struct BestStat
    {
        /// <summary>False when the player scored nothing in any award stat (the knock-outs line is shown instead).</summary>
        public bool Has;
        public AwardKind Kind;
        public double Value;
        /// <summary>1 = the best in the match.</summary>
        public int Rank;
        /// <summary>Players compared.</summary>
        public int Of;
    }

    /// <summary>
    /// b15 item 2 (systems audit W1, defect 7): awards beyond knock-outs, so every class can earn credit. Every number comes
    /// from ActorStats, which MatchStats fills on the view side from sim events (HealLanded, WallBuilt, PiecePlaced,
    /// BankDeposit, HutLevel, Explosion, WallBroken, PieceDestroyed, DeliverBiomass) and the actors' own counters. Pure and
    /// deterministic: ties go to the higher second number, then fewer deaths, then the lower actor id.
    /// <para>Role points (added to the MVP points): they put a support player's job on the same scale as knock-outs. They
    /// are first-pass weights picked from the configs (a pulse heals 30 HP, a hut level costs 6 to 24 ore, a knock-out is
    /// 100), not from human match data, which does not exist yet; tune them here.</para>
    /// </summary>
    public static class RoleAwards
    {
        public const int Count = 6;
        public const int PointsPerHealHp = 1, PointsPerSave = 50, PointsPerStructureBuilt = 50, PointsPerOreBanked = 40,
            PointsPerHutLevel = 150, PointsPerStructureBlown = 75, PointsPerMultiHit = 50;

        /// <summary>The value an award ranks by.</summary>
        public static double Primary(ActorStats s, AwardKind k)
        {
            switch (k)
            {
                case AwardKind.HillAnchor: return Math.Floor(s.HillSeconds);
                case AwardKind.Lifesaver: return Math.Floor(s.HealingDone);
                case AwardKind.MasterBuilder: return s.StructuresBuilt;
                case AwardKind.Prospector: return s.OreBanked;
                case AwardKind.TopFeeder: return s.Biomass;
                case AwardKind.Demolisher: return s.StructuresBlown + s.MultiHits;
                default: return 0;
            }
        }

        /// <summary>The second number of an award's line (and the first tie-break).</summary>
        public static double Secondary(ActorStats s, AwardKind k)
        {
            switch (k)
            {
                case AwardKind.HillAnchor: return s.Kills;
                case AwardKind.Lifesaver: return s.Saves;
                case AwardKind.MasterBuilder: return s.WallCells;
                case AwardKind.Prospector: return s.HutLevelsFunded;
                case AwardKind.TopFeeder: return s.Handoffs;
                case AwardKind.Demolisher: return s.MultiHits;
                default: return 0;
            }
        }

        /// <summary>The two numbers the award line names ({a}, {b}).</summary>
        public static void LineNumbers(ActorStats s, AwardKind k, out double a, out double b)
        {
            a = Primary(s, k);
            b = Secondary(s, k);
            if (k == AwardKind.Demolisher) { a = s.StructuresBlown; b = s.MultiHits; }
        }

        /// <summary>True when a is ranked ahead of b for award k.</summary>
        static bool Ahead(ActorStats a, ActorStats b, AwardKind k)
        {
            double pa = Primary(a, k), pb = Primary(b, k);
            if (pa != pb) return pa > pb;
            double sa = Secondary(a, k), sb = Secondary(b, k);
            if (sa != sb) return sa > sb;
            if (a.Deaths != b.Deaths) return a.Deaths < b.Deaths;
            return a.Id < b.Id;
        }

        /// <summary>The winner of award k among the first count actors (-1 when nobody scored above zero).</summary>
        public static int Winner(ActorStats[] actors, int count, AwardKind k)
        {
            int best = -1;
            for (int i = 0; i < count && i < actors.Length; i++)
            {
                ActorStats s = actors[i];
                if (s == null || Primary(s, k) <= 0) continue;
                if (best < 0 || Ahead(s, actors[best], k)) best = i;
            }
            return best;
        }

        /// <summary>All six awards in AwardKind order.</summary>
        public static AwardResult[] Pick(ActorStats[] actors, int count)
        {
            var r = new AwardResult[Count];
            for (int k = 0; k < Count; k++)
            {
                var kind = (AwardKind)k;
                int w = Winner(actors, count, kind);
                r[k] = new AwardResult { Kind = kind, Actor = w };
                if (w >= 0) { LineNumbers(actors[w], kind, out r[k].A, out r[k].B); }
            }
            return r;
        }

        /// <summary>1-based rank of actor id for award k among everyone in the match (1 + how many are ahead).</summary>
        public static int Rank(ActorStats[] actors, int count, int id, AwardKind k)
        {
            ActorStats me = actors[id];
            int rank = 1;
            for (int i = 0; i < count && i < actors.Length; i++)
                if (i != id && actors[i] != null && Ahead(actors[i], me, k)) rank++;
            return rank;
        }

        /// <summary>
        /// The player's best stat: among the award stats where the player scored above zero, the one with the best rank;
        /// a tie in rank goes to the larger share of the match total, then to AwardKind order.
        /// </summary>
        public static BestStat BestOf(ActorStats[] actors, int count, int id)
        {
            var best = new BestStat { Has = false, Of = count };
            if (actors == null || id < 0 || id >= count || id >= actors.Length || actors[id] == null) return best;
            double bestShare = -1;
            for (int k = 0; k < Count; k++)
            {
                var kind = (AwardKind)k;
                double v = Primary(actors[id], kind);
                if (v <= 0) continue;
                double total = 0;
                for (int i = 0; i < count && i < actors.Length; i++) if (actors[i] != null) total += Primary(actors[i], kind);
                double share = total > 0 ? v / total : 0;
                int rank = Rank(actors, count, id, kind);
                if (!best.Has || rank < best.Rank || (rank == best.Rank && share > bestShare))
                {
                    best = new BestStat { Has = true, Kind = kind, Value = v, Rank = rank, Of = count };
                    bestShare = share;
                }
            }
            return best;
        }

        /// <summary>The job points added to an actor's MVP points (0 for a player who only fought).</summary>
        public static int RolePoints(ActorStats s) =>
            (int)Math.Round(s.HealingDone * PointsPerHealHp) + s.Saves * PointsPerSave + s.StructuresBuilt * PointsPerStructureBuilt +
            s.OreBanked * PointsPerOreBanked + s.HutLevelsFunded * PointsPerHutLevel + s.StructuresBlown * PointsPerStructureBlown +
            s.MultiHits * PointsPerMultiHit;

        /// <summary>The award line of a result, from the words table ("212 HP healed, 3 saves").</summary>
        public static string Line(AwardResult r) =>
            r.Actor < 0 ? MatchWords.AwardNobody
                : MatchWords.Fill(MatchWords.AwardLines[(int)r.Kind], "a", MatchWords.N(r.A), "b", MatchWords.N(r.B));

        /// <summary>The YOUR BEST line, from the words table ("212 HP healed, #1 of 32").</summary>
        public static string BestLine(BestStat b, ActorStats me)
        {
            if (!b.Has)
                return me != null && me.Kills > 0 ? MatchWords.Fill(MatchWords.BestKnockOuts, "a", MatchWords.N(me.Kills)) : MatchWords.BestNothing;
            string line = MatchWords.Fill(MatchWords.BestLines[(int)b.Kind], "a", MatchWords.N(b.Value));
            return MatchWords.Fill(MatchWords.BestRank, "line", line, "rank", b.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "n", b.Of.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

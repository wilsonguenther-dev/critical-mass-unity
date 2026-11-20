using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>The role numbers class mastery is earned from (b15 profile). Index = (int)RoleStat.</summary>
    public enum RoleStat
    {
        /// <summary>Knock-outs, at most RoleTally.KnockOutsPerVictim counted on the same victim.</summary>
        KnockOuts,
        /// <summary>Whole seconds on the hill.</summary>
        HillSeconds,
        /// <summary>Biomass and corruption units delivered to the egg.</summary>
        Delivered,
        /// <summary>HP a Healer restored on teammates (not herself).</summary>
        Healing,
        /// <summary>Ore banked at the team's Blacksmith hut.</summary>
        OreBanked,
        /// <summary>Built pieces a teammate picked up and used.</summary>
        PiecesUsed,
        /// <summary>Walls, blocks and pieces built, and bridges repaired.</summary>
        PiecesBuilt,
        /// <summary>Fighters hurt by this player's mines and TNT.</summary>
        ExplosiveHits,
        /// <summary>Cells carved and decorations brought down (structures destroyed).</summary>
        Demolished,
        /// <summary>Instability a Healer purified from the egg.</summary>
        Purified,
    }

    /// <summary>One player's role numbers from one match (b15 profile). Read from the sim's event ring by RoleTally.</summary>
    [Serializable]
    public struct RoleStats
    {
        public const int Count = 10;

        public int KnockOuts;
        public int HillSeconds;
        public int Delivered;
        public int Healing;
        public int OreBanked;
        public int PiecesUsed;
        public int PiecesBuilt;
        public int ExplosiveHits;
        public int Demolished;
        public int Purified;
        /// <summary>Not scored: shown in history only.</summary>
        public int Deaths;
        public int Dodges;

        public int Get(RoleStat s)
        {
            switch (s)
            {
                case RoleStat.KnockOuts: return KnockOuts;
                case RoleStat.HillSeconds: return HillSeconds;
                case RoleStat.Delivered: return Delivered;
                case RoleStat.Healing: return Healing;
                case RoleStat.OreBanked: return OreBanked;
                case RoleStat.PiecesUsed: return PiecesUsed;
                case RoleStat.PiecesBuilt: return PiecesBuilt;
                case RoleStat.ExplosiveHits: return ExplosiveHits;
                case RoleStat.Demolished: return Demolished;
                default: return Purified;
            }
        }

        /// <summary>True when the player did anything at all (an idle player earns no finish bonus).</summary>
        public bool AnyContribution
        {
            get
            {
                for (int i = 0; i < Count; i++) if (Get((RoleStat)i) > 0) return true;
                return false;
            }
        }
    }

    /// <summary>The match around the numbers: did it finish, who won, was anyone else human, which mode.</summary>
    public struct MasteryContext
    {
        public bool Completed;
        public MatchOutcome Outcome;
        /// <summary>No other human was in the match (bots only).</summary>
        public bool BotOnly;
        /// <summary>A practice mode (Training, Practice, Demo, Spectate, the guided match): no mastery at all.</summary>
        public bool PracticeMode;
        /// <summary>Local calendar day yyyymmdd (the system clock), for the bot-match daily allowance.</summary>
        public int Day;
    }

    /// <summary>One line of a mastery award: a stat, how many counted after its cap, the XP it gave.</summary>
    public struct MasteryLine
    {
        public RoleStat Stat;
        public int Counted;
        public int Xp;
    }

    /// <summary>What one match gave one class's mastery track.</summary>
    public sealed class MasteryAward
    {
        public ClassId Class;
        public readonly List<MasteryLine> Lines = new List<MasteryLine>(RoleStats.Count);
        /// <summary>Role XP plus the finish and result bonus, before the per-match cap.</summary>
        public int Raw;
        /// <summary>After the per-match cap.</summary>
        public int Capped;
        /// <summary>Percent kept (100, BotMatchPercent, or 0 in a practice mode or an unfinished match).</summary>
        public int Percent;
        /// <summary>What the track really gained (after the bot-match daily allowance in Apply).</summary>
        public int Xp;
        public int XpBefore, XpAfter;
        public int LevelBefore => MasteryRules.LevelFor(XpBefore);
        public int LevelAfter => MasteryRules.LevelFor(XpAfter);
        /// <summary>Why the award is smaller than the play (MasteryWhy), for the results strip.</summary>
        public MasteryWhy Why;
    }

    public enum MasteryWhy { Full, BotMatch, BotDailyAllowanceUsed, PracticeMode, NotFinished, MatchCap }

    /// <summary>
    /// b15 profile: class mastery maths. Pure integer arithmetic, so the same match always gives the same XP. Mastery is
    /// earned by doing the class's job (each class weights the role numbers differently, see Weight10) and pays cosmetics
    /// only (MasteryRewards): nothing here ever reaches the sim or changes a unit's strength.
    /// <para>Anti-farm: no XP for an unfinished match or a practice mode; each stat is capped per match (Cap); the whole
    /// match is capped (PerMatchCap); knock-outs on the same victim count at most RoleTally.KnockOutsPerVictim times; a
    /// bots-only match keeps BotMatchPercent and at most BotDailyAllowance XP per class per calendar day; an idle player
    /// gets no finish bonus.</para>
    /// </summary>
    public static class MasteryRules
    {
        public const int MaxLevel = CriticalMass.Net.CosmeticLook.MaxMastery;

        /// <summary>XP from level L to L + 1: 400 + 100 x (L - 1). Level 2 at 400, level 10 at 7,200, level 20 at 24,700.</summary>
        public static int XpToNext(int level) => level < 1 || level >= MaxLevel ? 0 : 400 + 100 * (level - 1);

        /// <summary>Total XP needed to reach a level (level 1 = 0).</summary>
        public static int TotalFor(int level)
        {
            int l = Math.Max(1, Math.Min(level, MaxLevel));
            // sum over k = 1 .. l - 1 of 400 + 100 (k - 1)
            int n = l - 1;
            return 400 * n + 100 * (n * (n - 1) / 2);
        }

        public static int MaxXp => TotalFor(MaxLevel);

        public static int LevelFor(int xp)
        {
            int level = 1;
            while (level < MaxLevel && xp >= TotalFor(level + 1)) level++;
            return level;
        }

        /// <summary>0..1 through the current level (1 at the top level).</summary>
        public static float Progress(int xp)
        {
            int l = LevelFor(xp);
            if (l >= MaxLevel) return 1f;
            int a = TotalFor(l), b = TotalFor(l + 1);
            return Math.Max(0f, Math.Min(1f, (xp - a) / (float)(b - a)));
        }

        public const int PerMatchCap = 1500;
        public const int BotMatchPercent = 50;
        public const int BotDailyAllowance = 2000;
        public const int FinishXp = 100;
        public const int WinXp = 100, DrawXp = 60, LossXp = 40;

        /// <summary>Most of each stat that counts in one match (index = RoleStat).</summary>
        public static readonly int[] Cap = { 15, 300, 30, 1500, 24, 15, 20, 25, 40, 400 };

        /// <summary>
        /// XP per ten units of each stat, per class (rows in ClassId order, columns in RoleStat order): the class's own job
        /// pays most; other jobs still pay a little so a Warrior who hauls ore is not wasting the match.
        /// </summary>
        public static readonly int[][] Weight10 =
        {
            //               KO  Hill Deliv Heal Ore  Used Built Expl Demol Purify
            /* Warrior */ new[] { 300, 20,  40,   0,  40,  60,   0,   0,   0,    0 },
            /* Farmer  */ new[] { 150, 10, 200,   0,  40,  60,   0,   0,   0,    0 },
            /* Miner   */ new[] { 150, 10,  40,   0, 300,  60,   0,   0,  20,    0 },
            /* Demol.  */ new[] { 200, 10,  40,   0,  40,  60,   0, 200,  40,    0 },
            /* Builder */ new[] { 150, 10,  40,   0,  40, 350,  80,   0,   0,    0 },
            /* Healer  */ new[] { 100, 15,  40,   6,  40,  60,   0,   0,   0,   10 },
        };

        /// <summary>The stats that are the class's job (shown first on the profile and the results strip).</summary>
        public static readonly RoleStat[][] Job =
        {
            new[] { RoleStat.KnockOuts, RoleStat.HillSeconds },
            new[] { RoleStat.Delivered, RoleStat.KnockOuts },
            new[] { RoleStat.OreBanked, RoleStat.Demolished },
            new[] { RoleStat.ExplosiveHits, RoleStat.Demolished },
            new[] { RoleStat.PiecesUsed, RoleStat.PiecesBuilt },
            new[] { RoleStat.Healing, RoleStat.Purified },
        };

        static int Clamp(int v, int max) => v < 0 ? 0 : v > max ? max : v;

        /// <summary>The award before the daily allowance (into is cleared and reused when given).</summary>
        public static MasteryAward Compute(ClassId cls, in RoleStats s, in MasteryContext ctx, MasteryAward into = null)
        {
            MasteryAward a = into ?? new MasteryAward();
            a.Class = cls;
            a.Lines.Clear();
            a.Raw = a.Capped = a.Xp = 0;
            a.Percent = 100;
            a.Why = MasteryWhy.Full;
            if (!ctx.Completed) { a.Percent = 0; a.Why = MasteryWhy.NotFinished; return a; }
            if (ctx.PracticeMode) { a.Percent = 0; a.Why = MasteryWhy.PracticeMode; return a; }
            int[] w = Weight10[Clamp((int)cls, Weight10.Length - 1)];
            int raw = 0;
            for (int i = 0; i < RoleStats.Count; i++)
            {
                int counted = Clamp(s.Get((RoleStat)i), Cap[i]);
                int xp = counted * w[i] / 10;
                if (counted <= 0) continue;
                a.Lines.Add(new MasteryLine { Stat = (RoleStat)i, Counted = counted, Xp = xp });
                raw += xp;
            }
            if (s.AnyContribution)
                raw += FinishXp + (ctx.Outcome == MatchOutcome.Win ? WinXp : ctx.Outcome == MatchOutcome.Draw ? DrawXp : LossXp);
            a.Raw = raw;
            a.Capped = Math.Min(raw, PerMatchCap);
            if (a.Capped < raw) a.Why = MasteryWhy.MatchCap;
            if (ctx.BotOnly)
            {
                a.Percent = BotMatchPercent;
                a.Why = MasteryWhy.BotMatch;
            }
            a.Xp = a.Capped * a.Percent / 100;
            return a;
        }

        /// <summary>
        /// Adds the award to the class's track (and its career totals, which Apply does not touch: MatchHistory does),
        /// holding a bots-only match to what is left of today's allowance. Records before / after on the award.
        /// </summary>
        public static void Apply(MasteryAward a, ClassMastery m, in MasteryContext ctx)
        {
            if (a == null || m == null) return;
            a.XpBefore = m.Xp;
            int gain = Math.Max(0, a.Xp);
            if (ctx.BotOnly && gain > 0)
            {
                if (m.BotDay != ctx.Day) { m.BotDay = ctx.Day; m.BotXpToday = 0; }
                int left = Math.Max(0, BotDailyAllowance - m.BotXpToday);
                if (gain > left)
                {
                    gain = left;
                    a.Why = MasteryWhy.BotDailyAllowanceUsed;
                }
                m.BotXpToday += gain;
            }
            m.Xp = (int)Math.Min((long)m.Xp + gain, MaxXp);
            a.Xp = m.Xp - a.XpBefore;
            a.XpAfter = m.Xp;
        }

        /// <summary>yyyymmdd of a local date.</summary>
        public static int DayOf(DateTime local) => local.Year * 10000 + local.Month * 100 + local.Day;
    }
}

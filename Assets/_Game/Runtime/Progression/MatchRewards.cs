using System;
using System.Collections.Generic;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>The local player's result.</summary>
    public enum MatchOutcome { Loss, Draw, Win }

    /// <summary>One source of reward, in the order the results list shows them.</summary>
    public enum RewardSource { KnockOuts, Biomass, Corruption, HillTime, KaijuKnockOut, Relics, CoinsPickedUp, Finish, Result }

    /// <summary>
    /// What the local player did in one match, read from stats the sim already exposes (Actor.Kills, Actor.HillSeconds,
    /// the delivery and Kaiju knock-out events, MatchState.Winner) plus view-side relics (DG-25) and, once the rules lane
    /// adds world pickups (DESIGN-progression-pickups section 7), Actor.CoinsPicked.
    /// </summary>
    public struct MatchStatsInput
    {
        public int KnockOuts;
        public int Deaths;
        public int BiomassDelivered;
        public int CorruptionDelivered;
        public double HillSeconds;
        public int KaijuKnockOuts;
        /// <summary>Relics found this match that the profile did not have yet.</summary>
        public int RelicsFound;
        public int CoinsPicked;
        public MatchOutcome Outcome;
        /// <summary>True only when the match ran to its end (no reward for a quit).</summary>
        public bool Completed;
    }

    /// <summary>One line of the results breakdown.</summary>
    public struct RewardLine
    {
        public RewardSource Source;
        /// <summary>How many (knock-outs, units, whole hill seconds, ...); 1 for Finish and Result.</summary>
        public int Count;
        public int Coins;
        public int SkillPoints;
    }

    /// <summary>The rewards of one match and where the profile stood before and after them.</summary>
    public sealed class MatchRewards
    {
        public readonly List<RewardLine> Lines = new List<RewardLine>(12);
        public MatchStatsInput Stats;
        public int Coins;
        public int SkillPoints;
        public int SkillPointsBefore;
        public int SkillPointsAfter;
        public int CoinsBefore;
        public int CoinsAfter;
        public int LevelBefore => Leveling.LevelFor(SkillPointsBefore);
        public int LevelAfter => Leveling.LevelFor(SkillPointsAfter);
        public int LevelsGained => LevelAfter - LevelBefore;

        public void Clear()
        {
            Lines.Clear();
            Stats = default;
            Coins = SkillPoints = SkillPointsBefore = SkillPointsAfter = CoinsBefore = CoinsAfter = 0;
        }
    }

    /// <summary>
    /// The reward maths (DESIGN-progression-pickups sections 1 and 3). Pure integer arithmetic on MatchStatsInput, so the
    /// same match always pays the same; caps stop a broken match from minting a fortune. LiveCoins is the same coin maths
    /// without the result bonus, for the HUD chip during play (allocation-free).
    /// </summary>
    public static class RewardRules
    {
        // coins
        public const int CoinsPerKnockOut = 5;
        public const int CoinsPerDeliveredUnit = 2;
        public const int HillSecondsPerCoin = 10;
        public const int CoinsPerKaijuKnockOut = 20;
        public const int CoinsPerRelic = 10;
        public const int CoinsWin = 25, CoinsDraw = 15, CoinsLoss = 10;

        // skill points
        public const int SpPerKnockOut = 25;
        public const int SpPerDeliveredUnit = 4;
        public const int HillSecondsPerSp = 2;
        public const int SpPerKaijuKnockOut = 100;
        public const int SpPerRelic = 20;
        public const int SpFinish = 50;
        public const int SpWin = 150, SpDraw = 100, SpLoss = 75;

        // caps on the counted amount of each source
        public const int MaxKnockOuts = 40;
        public const int MaxDeliveredUnits = 60;
        public const int MaxHillSeconds = 480;
        public const int MaxKaijuKnockOuts = 3;
        public const int MaxRelics = 8;
        public const int MaxCoinsPicked = 300;

        static int Cap(int v, int max) => v < 0 ? 0 : v > max ? max : v;

        static int HillWhole(double s)
        {
            if (!(s > 0) || double.IsNaN(s)) return 0;
            return s >= MaxHillSeconds ? MaxHillSeconds : (int)Math.Floor(s);
        }

        /// <summary>The breakdown and totals for one match (into is cleared and reused when given).</summary>
        public static MatchRewards Compute(in MatchStatsInput s, MatchRewards into = null)
        {
            MatchRewards r = into ?? new MatchRewards();
            r.Clear();
            r.Stats = s;
            if (!s.Completed) return r;
            int ko = Cap(s.KnockOuts, MaxKnockOuts);
            int bio = Cap(s.BiomassDelivered, MaxDeliveredUnits);
            int cor = Cap(s.CorruptionDelivered, MaxDeliveredUnits - bio);
            int hill = HillWhole(s.HillSeconds);
            int kaiju = Cap(s.KaijuKnockOuts, MaxKaijuKnockOuts);
            int relics = Cap(s.RelicsFound, MaxRelics);
            int picked = Cap(s.CoinsPicked, MaxCoinsPicked);

            Add(r, RewardSource.KnockOuts, ko, ko * CoinsPerKnockOut, ko * SpPerKnockOut);
            Add(r, RewardSource.Biomass, bio, bio * CoinsPerDeliveredUnit, bio * SpPerDeliveredUnit);
            Add(r, RewardSource.Corruption, cor, cor * CoinsPerDeliveredUnit, cor * SpPerDeliveredUnit);
            Add(r, RewardSource.HillTime, hill, hill / HillSecondsPerCoin, hill / HillSecondsPerSp);
            Add(r, RewardSource.KaijuKnockOut, kaiju, kaiju * CoinsPerKaijuKnockOut, kaiju * SpPerKaijuKnockOut);
            Add(r, RewardSource.Relics, relics, relics * CoinsPerRelic, relics * SpPerRelic);
            Add(r, RewardSource.CoinsPickedUp, picked, picked, 0);
            Add(r, RewardSource.Finish, 1, 0, SpFinish);
            int rc = s.Outcome == MatchOutcome.Win ? CoinsWin : s.Outcome == MatchOutcome.Draw ? CoinsDraw : CoinsLoss;
            int rs = s.Outcome == MatchOutcome.Win ? SpWin : s.Outcome == MatchOutcome.Draw ? SpDraw : SpLoss;
            Add(r, RewardSource.Result, 1, rc, rs);
            return r;
        }

        static void Add(MatchRewards r, RewardSource src, int count, int coins, int sp)
        {
            if (count <= 0) return;
            r.Lines.Add(new RewardLine { Source = src, Count = count, Coins = coins, SkillPoints = sp });
            r.Coins += coins;
            r.SkillPoints += sp;
        }

        /// <summary>Coins earned so far in a running match (no result bonus): the HUD chip. Allocation-free.</summary>
        public static int LiveCoins(in MatchStatsInput s)
        {
            int ko = Cap(s.KnockOuts, MaxKnockOuts);
            int bio = Cap(s.BiomassDelivered, MaxDeliveredUnits);
            int cor = Cap(s.CorruptionDelivered, MaxDeliveredUnits - bio);
            return ko * CoinsPerKnockOut + (bio + cor) * CoinsPerDeliveredUnit + HillWhole(s.HillSeconds) / HillSecondsPerCoin +
                   Cap(s.KaijuKnockOuts, MaxKaijuKnockOuts) * CoinsPerKaijuKnockOut + Cap(s.RelicsFound, MaxRelics) * CoinsPerRelic +
                   Cap(s.CoinsPicked, MaxCoinsPicked);
        }

        /// <summary>Adds the rewards to a profile and records before and after on the rewards (no save: Profile.Commit).</summary>
        public static void Apply(MatchRewards r, PlayerProfile p)
        {
            if (r == null || p == null) return;
            r.SkillPointsBefore = p.SkillPoints;
            r.CoinsBefore = p.Coins;
            if (r.Stats.Completed)
            {
                int capSp = Leveling.TotalFor(Leveling.MaxLevel);
                p.SkillPoints = (int)Math.Min((long)p.SkillPoints + r.SkillPoints, capSp);
                p.Coins = (int)Math.Min((long)p.Coins + r.Coins, int.MaxValue);
                p.MatchesPlayed++;
                if (r.Stats.Outcome == MatchOutcome.Win) p.Wins++;
                else if (r.Stats.Outcome == MatchOutcome.Draw) p.Draws++;
                else p.Losses++;
                p.KnockOuts += Math.Max(0, r.Stats.KnockOuts);
                if (r.SkillPoints > p.BestMatchSkillPoints) p.BestMatchSkillPoints = r.SkillPoints;
            }
            r.SkillPointsAfter = p.SkillPoints;
            r.CoinsAfter = p.Coins;
        }
    }
}

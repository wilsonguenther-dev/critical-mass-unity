using System;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>Bot match-quality thresholds (browser outcomes.thresholds.ts OUTCOME_THRESHOLDS), kept 1:1; never edited to make a batch pass.</summary>
    public static class OutcomeThresholds
    {
        /// <summary>Share of batch A matches that are not a draw.</summary>
        public const double Decisive = 0.8;
        /// <summary>Mean share of samples where hillState is azure or crimson (contested and neutral excluded), batch A.</summary>
        public const double OwnedShare = 0.4;
        /// <summary>The same share for every single match (batches A and B).</summary>
        public const double OwnedShareMin = 0.25;
        /// <summary>Mean number of hill owner changes per match, batch A.</summary>
        public const double OwnerChangesMean = 4;
        /// <summary>Neither team wins more than this many of the 20 batch A matches.</summary>
        public const int MaxTeamWins = 14;
        /// <summary>Distinct natural hatch outcomes (neutral / azure / crimson / berserk) across batches A + B.</summary>
        public const int DistinctHatchOutcomes = 2;
        /// <summary>Of the A + B matches whose Kaiju hatched allied to a team, that team wins at most this share.</summary>
        public const double AlliedWinShareMax = 0.85;
        /// <summary>Mean bots per team in gather / deliver / corrupt during t 20..240 s, batch A.</summary>
        public const double KaijuDutyMean = 1.5;
        /// <summary>The passive human's team wins at least this many of the 10 batch C matches (kept 1:1; no ported case reads it).</summary>
        public const int PassiveHumanTeamWins = 3;
        /// <summary>A non-contributing actor 0's team wins at most this many MORE matches than with actor 0 on bot control.</summary>
        public const int AfkWinsOverPlayingMax = 0;
    }

    /// <summary>One outcome batch (browser OUTCOME_BATCHES entry).</summary>
    public sealed class OutcomeBatch
    {
        public readonly string Name;
        public readonly int TeamSize;
        public readonly int FirstSeed;
        public readonly int Count;
        /// <summary>The batch's own actor-0 mode (browser driveHuman: true = Bot, false = Passive); AFK also runs Bot and Wander.</summary>
        public readonly HumanMode Human;

        public OutcomeBatch(string name, int teamSize, int firstSeed, int count, HumanMode human)
        {
            Name = name;
            TeamSize = teamSize;
            FirstSeed = firstSeed;
            Count = count;
            Human = human;
        }
    }

    /// <summary>The outcome batches (browser OUTCOME_BATCHES): A, B, C and AFK.</summary>
    public static class OutcomeBatches
    {
        /// <summary>5v5 seeds 1..20, every slot including actor 0 on bot control.</summary>
        public static readonly OutcomeBatch A = new OutcomeBatch("A", 5, 1, 20, HumanMode.Bot);
        /// <summary>16v16 seeds 101..104, every slot on bot control.</summary>
        public static readonly OutcomeBatch B = new OutcomeBatch("B", 16, 101, 4, HumanMode.Bot);
        /// <summary>5v5 seeds 201..210, actor 0 passive at its pad.</summary>
        public static readonly OutcomeBatch C = new OutcomeBatch("C", 5, 201, 10, HumanMode.Passive);
        /// <summary>5v5 seeds 201..250, each played with actor 0 Bot, Passive and Wander; its first 10 seeds are batch C's.</summary>
        public static readonly OutcomeBatch AFK = new OutcomeBatch("AFK", 5, 201, 50, HumanMode.Passive);

        /// <summary>The batch named name ("A", "B", "C" or "AFK").</summary>
        public static OutcomeBatch Get(string name)
        {
            switch (name)
            {
                case "A": return A;
                case "B": return B;
                case "C": return C;
                case "AFK": return AFK;
                default: throw new ArgumentException("unknown outcome batch: " + name, nameof(name));
            }
        }
    }
}

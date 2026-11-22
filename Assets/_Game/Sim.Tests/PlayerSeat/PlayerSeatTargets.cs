using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>
    /// Player-seat gate (2026-11-22): EVERY threshold of the tier S tests in one place. The lead tunes these; a fix builder
    /// never edits a test to make it pass, only the game (or, with the lead's sign-off, a number here, with the reason).
    /// Times are sim seconds (30 Hz: 1 tick = 33.3 ms). "Lag 6" = the human model aims where the target was 6 ticks (0.2 s) ago.
    /// </summary>
    public static class PlayerSeatTargets
    {
        // ---- the human model
        /// <summary>A normal human reaction / tracking delay in ticks (0.2 s at 30 Hz).</summary>
        public const int HumanLagTicks = 6;
        /// <summary>Start gap of a duel, centre to centre (DuelRunner.StartGap, the probe's value).</summary>
        public const double DuelStartGap = 3.0;
        /// <summary>Longest duel before it is a draw (DuelRunner.MaxSeconds).</summary>
        public const double DuelMaxSeconds = 30;
        /// <summary>Start angles per duel cell (the probe's 8).</summary>
        public const int Angles = 8;
        /// <summary>Start angles of the mirror and human-vs-human cells.</summary>
        public const int MirrorAngles = 16;

        // ---- S1 / S2 / S4: the primary on a passive target (rig controls)
        /// <summary>S1: the passive-target control must land at least this share (the probe's control landed 97-100%).</summary>
        public const double PassiveLandMin = 0.97;
        /// <summary>S1: the Hit's damage within this fraction of the class damage x modifiers.</summary>
        public const double DamageTolerance = 0.01;
        /// <summary>S1: Knock event Impulse at least this share of Strike.Knock / victim mass.</summary>
        public const double KnockShareMin = 0.9;
        /// <summary>S2: the in-face distance, centre to centre (bodies touching).</summary>
        public const double InFaceDistance = 0.9;
        /// <summary>S2: an in-face press deals at least this share of the class damage.</summary>
        public const double InFaceDamageShareMin = 0.9;
        /// <summary>S4: the kill lands within the computed full-landing time to kill plus this slack.</summary>
        public const double TtkSlackSeconds = 0.5;

        // ---- S3 / S35: land rate with a human's lag
        /// <summary>Share of a lag-6 human's swings that must land on a strafing target (S3) or a mirror human (S35).</summary>
        public const double LandRateMin = 0.70;
        /// <summary>S3: swings measured per class.</summary>
        public const int StrafeSwings = 40;
        /// <summary>S3: the strafer circles at this share of its class speed...</summary>
        public const double StraferSpeedShare = 0.6;
        /// <summary>... at this share of the human's reach (attack range + target radius), centre to centre.</summary>
        public const double StraferRadiusShare = 0.7;

        // ---- S5 / S43 / S44 / S45 / S35: duels against real bots (8 angles each unless stated)
        /// <summary>S5: a lag-6 human of a non-Healer class wins at least this share of 8 angles x 6 Normal-bot classes.</summary>
        public const double DuelWinShareMin = 0.40;
        /// <summary>S5 (lead): a lag-6 human Warrior wins at least this many of 8 against each non-Warrior Normal bot.</summary>
        public const int WarriorWinsOf8Min = 6;
        /// <summary>S5 (lead): the Warrior mirror (16 angles) win share lies in this band.</summary>
        public const double WarriorMirrorMin = 0.30, WarriorMirrorMax = 0.70;
        /// <summary>S35: in human vs human mirrors neither seat wins more than this share of 16 angles.</summary>
        public const double HumanMirrorMaxShare = 0.65;
        /// <summary>S43: the 50% Warrior (every other swing aimed off) wins at least this many of 8 against each other class.</summary>
        public const int Warrior50PctWinsOf8Min = 5;
        /// <summary>S43: ... with a median time to kill no longer than this.</summary>
        public const double WarriorTtkMaxSeconds = 15;
        /// <summary>S44: a lag-6 human Healer wins at most this many of 8 against each other class (she loses one on one).</summary>
        public const int HealerWinsOf8Max = 3;
        /// <summary>S44: every non-Healer lag-6 human wins at least this many of 8 against a Normal Healer bot.</summary>
        public const int BeatsHealerWinsOf8Min = 5;
        /// <summary>S45: each middle class (lag-6 human vs the other three as Normal bots) wins a share in this band.</summary>
        public const double MiddleShareMin = 0.30, MiddleShareMax = 0.70;

        /// <summary>PLAN P3: the share of all 30 non-mirror 1v1s a lag-6 human wins against Normal bots ("the deciding factor in a fight").</summary>
        public const double OverallDuelShareMin = 0.50;

        // ---- S6 / S41: healing against focus
        /// <summary>S6: a 50% human kills a target mended by one enemy Healer bot within this.</summary>
        public const double KillThroughHealerMaxSeconds = 20;
        /// <summary>S41: a target hit within this long does not out-heal pursuit at its fountain...</summary>
        public const double FountainRecentHitSeconds = 2;
        /// <summary>... measured as net HP over this window of a lag-6 Warrior hitting it (must be negative).</summary>
        public const double FountainWindowSeconds = 3;

        // ---- S11: chase
        /// <summary>S11: the runner flees at this share of its health.</summary>
        public const double ChaseRunnerHpShare = 0.29;
        /// <summary>S11: a lag-6 human Warrior lands the finishing hit within this...</summary>
        public const double ChaseSeconds = 4;
        /// <summary>... in a share of 8 angles in this band (above the top: no escape at all for the runner, against Travis's rule).</summary>
        public const double ChaseCatchShareMin = 0.40, ChaseCatchShareMax = 0.80;   // PLAN.md P3 (40 to 80%); the gate brief's first numbers were 0.50 / 0.90

        // ---- S42: bot fairness
        /// <summary>S42: a Normal bot's land rate against a lag-6 circle-strafing human lies in this band.</summary>
        public const double BotLandMin = 0.40, BotLandMax = 0.75;
        /// <summary>S42: bot swings measured per class.</summary>
        public const int BotSwings = 40;

        // ---- S33: never a silent press
        /// <summary>S33: a press is answered by an action or refusal event within this many ticks.</summary>
        public const int PressAnswerTicks = 2;

        // ---- S30: idle rule
        /// <summary>S30: a player who only moves the aim for this long is still at the controls.</summary>
        public const double AimOnlySeconds = 20;

        // ---- PlayerSeatImpactSoak (Slow + PlayerSeatSoak): one strong human moves the match
        /// <summary>Seeds per arm and team size of the soak.</summary>
        public const int SoakSeeds = 24;
        /// <summary>Team win share with the strong seat minus the all-Normal baseline, 5v5 and 16v16.</summary>
        public const double SoakUplift5 = 0.15, SoakUplift16 = 0.05;
        /// <summary>Seat 0's share of its team's kills: at least 1.5 seats' worth.</summary>
        public const double SoakKillSeats = 1.5;

        /// <summary>The six classes in roster order (TestCase arguments use the enum directly).</summary>
        public static readonly ClassId[] Classes = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };
    }
}

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
        /// <summary>px S4: the runner starts this far outside the Warrior's reach (range + runner radius), already in Retreat.</summary>
        public const double ChaseStartBeyondReach = 0.5;
        /// <summary>... in a share of 8 angles in this band (above the top: no escape at all for the runner, against Travis's rule).</summary>
        public const double ChaseCatchShareMin = 0.40, ChaseCatchShareMax = 0.80;   // PLAN.md P3 (40 to 80%); the gate brief's first numbers were 0.50 / 0.90

        // ---- S42: bot fairness
        /// <summary>S42 before px S4: the PLAN band on one mixed strafer; no longer asserted (split into S42a / S42b below).</summary>
        public const double BotLandMin = 0.40, BotLandMax = 0.75;
        /// <summary>px S4, lead's call 13: S42a, a Normal bot lands at most this on the dodging strafer (dodging pays).</summary>
        public const double BotLandDodgingMax = 0.60;
        /// <summary>px S4, lead's call 13: S42b, a Normal bot lands at least this on the close strafer who never steps out.</summary>
        public const double BotLandStandingMin = 0.40;
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

        // ---- S48: floor and ceiling (PLAN THE STANDARD rows 4, 5; start values)
        /// <summary>px S4: the first-match model's aim help (0 Off, 1 Low, 2 High). PLAN THE STANDARD row 4 says "aim help at its
        /// default"; the shipped default is High for keys and controller, Low for the mouse (Runtime/Input/AimAssistMath.cs,
        /// AimAssistTunables.DefaultFor). Low: the floor must hold for the device whose default helps least (the mouse). Lane I's
        /// measured help for that player (data/i-input/assist-s1-final.txt, duel rows): camyaw Off land 0.300 -> High 0.792,
        /// stick Off 0.667 -> High 0.797 (mouse Off 0.724 -> Low 0.749). S4 measured this floor at Low: land 0.59, win 0.48 vs
        /// Normal, gap 0.25; at High: land 0.70, win 0.60, gap 0.14 (data/s4-rig/tierS-s48-low.txt, tierS-s48-try1.txt).</summary>
        public const int FloorAimHelp = 1;
        /// <summary>First-match player: 0.3 s reactions (9 ticks), every other swing aimed off, never dodges or feints.</summary>
        public const int FloorLagTicks = 9;
        public const double FloorLandMin = 0.50, FloorWinVsNormalMin = 0.35, FloorWinVsEasyMin = 0.60;
        /// <summary>Strong player: 0.15 s reactions (4-5 ticks), feints, hit-cancels, dodges telegraphs read 6 ticks late, the Farmer keeps range.</summary>
        public const int CeilingLagTicks = 4;
        public const double CeilingWinVsNormalMin = 0.70, CeilingWinVsHardMin = 0.50;
        /// <summary>Strong minus first-match win share vs Normal bots.</summary>
        public const double SkillGapMin = 0.25;

        // ---- S49: never bored (PLAN THE STANDARD row 3; start values)
        public const int EngagementSeeds = 12;
        /// <summary>Meaningful = alive and an enemy within this, or on the hill, or busy with a verb, or near the egg / Kaiju.</summary>
        public const double EngageEnemyRadius = 8, EngageEggRadius = 6;
        public const double LongestIdleMedianMax = 20, LongestIdleWorstMax = 30, DeadOrWalkingShareMax = 0.25, RespawnToHitMedianMax = 10;

        // ---- S51-S60: defence and movement verbs (lane DM, 2026-11-22; data/dm-defence/VERBS.txt)
        /// <summary>A human's reaction: the dodge press comes this many ticks after the swing's wind-up starts (0.2 s).</summary>
        public const int DefenceReactTicks = 6;
        /// <summary>S51: a reactive dodge at 0.2 s avoids the swing of every class whose wind-up is long enough (measured 0.91-1.00).</summary>
        public const double ReactiveDodgeAvoidMin = 0.70;
        /// <summary>S52: share of avoided, threatened swings that raise HitBlocked(BlockedDodge) (measured 0.98-1.00).</summary>
        public const double DodgeReportedMin = 0.95;
        /// <summary>S53: a lag-6 human who side-dodges each telegraphed swing vs one who never dodges, 36 pairs x 8 vs Normal bots:
        /// win share 0.58 -> 0.63 and health left 0.24 -> 0.29 measured; the bar sits just under.</summary>
        public const double DodgeWinGainMin = 0.04, DodgeHpGainMin = 0.04;
        /// <summary>S54: dodge to the side, walk back in, swing: the first landed hit is a critical (measured 0.83 vs Normal bots,
        /// 0.89 vs a lag-6 human attacker with DodgePunishSeconds 0.8; 0.64 / 0.69 with the reward off). Owner bar: at least half.</summary>
        public const double DodgeChainCritMin = 0.75;
        /// <summary>S57: a reactive shield (Ability at +6 ticks) vs never, Warrior vs the six Normal bots x 8: mean health left
        /// 0.44 -> 0.59 measured.</summary>
        public const double ShieldHpGainMin = 0.12;
        /// <summary>S58: a walker is stuck when under 20% of walk speed for more than this many ticks with input held.</summary>
        public const int StuckTicksMax = 15;

        /// <summary>The six classes in roster order (TestCase arguments use the enum directly).</summary>
        public static readonly ClassId[] Classes = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };
    }
}

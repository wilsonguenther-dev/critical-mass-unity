using System.Globalization;
using CriticalMass.Sim.Config;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>
    /// Built-in screen words for rewards, levels, the Barracks and the character panel. The words pack
    /// (Content/Lore/*.json, branch feat/dg-03-04-rings-words) has no progression entries yet; when it gains a
    /// "progression" file its loader can overwrite these fields at boot (they are plain static strings on purpose).
    /// </summary>
    public static class ProgressionWords
    {
        public static string Rewards = "REWARDS";
        public static string Level = "LEVEL";
        public static string LevelUp = "LEVEL UP";
        public static string MaxLevel = "MAX LEVEL";
        public static string SkillPoints = "SKILL POINTS";
        public static string Sp = "SP";
        public static string Coins = "COINS";
        public static string Unlocks = "UNLOCKS AT LEVEL ";
        /// <summary>Kept for old callers; polish pass 1 leaves looks without a model out instead of showing this.</summary>
        public static string ComingSoon = "NOT AVAILABLE";
        public static string Equipped = "EQUIPPED";
        public static string Locked = "LOCKED";
        public static string NoHeadgear = "NONE";
        public static string TeamColoursLocked = "Team colours come from your side and never change.";
        public static string ChangeBetweenMatches = "Change your look in the Barracks between matches.";
        public static string NextUnlock = "NEXT LEVEL UNLOCKS";
        public static string NothingNext = "Every look is yours.";

        public static string Source(RewardSource s)
        {
            switch (s)
            {
                case RewardSource.KnockOuts: return "KNOCK-OUTS";
                case RewardSource.Biomass: return "BIOMASS DELIVERED";
                case RewardSource.Corruption: return "CORRUPTION DELIVERED";
                case RewardSource.HillTime: return "HILL TIME";
                case RewardSource.KaijuKnockOut: return "KAIJU KNOCK-OUT";
                case RewardSource.Relics: return "RELICS FOUND";
                case RewardSource.CoinsPickedUp: return "COINS PICKED UP";
                case RewardSource.Finish: return "MATCH FINISHED";
                default: return "RESULT";
            }
        }

        public static string Outcome(MatchOutcome o) => o == MatchOutcome.Win ? "VICTORY" : o == MatchOutcome.Draw ? "DRAW" : "DEFEAT";

        /// <summary>The count column of a line: "x3", "142 S", or "" for Finish and Result.</summary>
        public static string Count(RewardLine l)
        {
            if (l.Source == RewardSource.Finish || l.Source == RewardSource.Result) return string.Empty;
            string n = l.Count.ToString(CultureInfo.InvariantCulture);
            return l.Source == RewardSource.HillTime ? n + " S" : "x" + n;
        }

        public static readonly string[] ClassNames = { "WARRIOR", "FARMER", "MINER", "DEMOLITIONS", "BUILDER", "HEALER" };

        public static readonly string[] RoleLines =
        {
            "Front line. Holds the hill and turns blows aside with a shield.",
            "Harvester. Carries the most biomass and roots anyone who chases.",
            "Sapper. Breaks walls open and throws up barricades.",
            "Blast expert. Mines and TNT, no regular swing.",
            "Ladders and rope bridges for the whole team; anyone can carry them.",
            "Battle healer. Mends allies, heals in a burst and purifies the shrine. Never deals damage.",
        };

        public static readonly string[] AbilityNames = { "SHIELD", "ROOTS", "WALL BREAK", "MINE", "BUILD", "HEALING WINGS" };

        public static string ClassName(ClassId c) => ClassNames[(int)c];

        static string N(double v) => v.ToString(v == System.Math.Floor(v) ? "0" : "0.#", CultureInfo.InvariantCulture);
        static string Pct(double f) => N(System.Math.Round(f * 100)) + "%";

        /// <summary>What the class's attack does, with the numbers from the sim config.</summary>
        public static string AttackText(GameConfig cfg, ClassId c)
        {
            if (c == ClassId.Demolitions) return "No regular attack: the Demolitions Expert fights with mines and TNT.";
            ClassConfig k = cfg.Class(c);
            return N(k.AttackDamage) + " damage in a " + N(k.AttackArcDeg) + "° arc, reach " + N(k.AttackRange) + " m.";
        }

        /// <summary>What the class's ability does, with the numbers from the sim config.</summary>
        public static string AbilityText(GameConfig cfg, ClassId c)
        {
            AbilitiesConfig a = cfg.Abilities;
            switch (c)
            {
                case ClassId.Warrior:
                    return "Raise the shield for " + N(a.WarriorShield.DurationSeconds) + " s: hits from the front deal " +
                           Pct(a.WarriorShield.DamageReduction) + " less; you move at " + Pct(a.WarriorShield.MoveSpeedMultiplier) + " speed.";
                case ClassId.Farmer:
                    return "Throw a root seed up to " + N(a.FarmerRoots.CastRange) + " m: enemies within " + N(a.FarmerRoots.Radius) +
                           " m move at " + Pct(a.FarmerRoots.SlowMultiplier) + " for " + N(a.FarmerRoots.SlowSeconds) + " s.";
                case ClassId.Miner:
                    return "Each pick hit knocks a breakable wall down one stage (reach " + N(a.MinerWallBreak.Reach) +
                           " m). Barricades: up to " + N(a.MinerBarricade.MaxActivePerMiner) + ", " + N(a.MinerBarricade.MaxHp) +
                           " HP, " + N(a.MinerBarricade.LifetimeSeconds) + " s.";
                default:
                    return "Arrives in a later build.";
            }
        }

        public static string Seconds(double s) => N(s) + " S";
        public static string Number(double v) => N(v);
    }
}

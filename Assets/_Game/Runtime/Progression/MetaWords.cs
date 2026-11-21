using System.Globalization;
using CriticalMass.Sim.Config;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>
    /// b15 profile: the one string table for meta progression (the profile screen, class mastery, the reward table, match
    /// history, achievements progress and the results progress strip). Every word those screens show comes from here, as
    /// plain static strings, so a words pack can overwrite them at boot like ProgressionWords. Full templated sentences
    /// (never fragments glued around a number) so a translation can reorder them.
    /// </summary>
    public static class MetaWords
    {
        // ---- profile screen ----
        public static string ProfileTitle = "PROFILE";
        public static string TabMastery = "MASTERY";
        public static string TabHistory = "MATCH HISTORY";
        public static string TabAchievements = "ACHIEVEMENTS";
        public static string Back = "BACK";
        public static string Prev = "PREV";
        public static string Next = "NEXT";
        public static string HintKeyboard = "ARROWS  MOVE     ENTER  CHOOSE     ESC  BACK";
        public static string HintGamepad = "D-PAD  MOVE     A  CHOOSE     B  BACK";
        public static string MenuLabel = "PROFILE";

        // ---- mastery ----
        public static string Mastery = "MASTERY";
        public static string MasteryLevelOf = "MASTERY {0} OF {1}";
        public static string XpToNext = "{0} XP TO MASTERY {1}";
        public static string MaxMastery = "Top mastery reached. Every look for this class is yours.";
        public static string NextReward = "NEXT AT MASTERY {0}: {1}";
        public static string EarnedBy = "Earned by: {0}";
        public static string Career = "CAREER";
        public static string CareerLine = "{0} matches   {1} wins   {2} draws   {3} losses";
        public static string NoMatchesYet = "No matches with this class yet. Play one to start its mastery track.";
        public static string Loadout = "LOOK";
        public static string SlotTitle = "TITLE";
        public static string SlotFrame = "FRAME";
        public static string SlotTrim = "COLOURS";
        public static string SlotEmblem = "EMBLEM";
        public static string NoTitle = "NO TITLE";
        public static string PlainFrame = "PLAIN";
        public static string StockColours = "STOCK";
        public static string NoEmblem = "NONE";
        public static string LockedUntil = "LOCKED UNTIL MASTERY {0}";
        public static string TeamColoursStay = "Team colours never change. Looks change only the other colours.";

        // ---- reward kinds ----
        public static string KindTitle = "Title";
        public static string KindFrame = "Portrait frame";
        public static string KindTrim = "Colours";
        public static string KindEmblem = "Emblem";
        public static string RewardLine = "{0}: {1}";

        // ---- role stats (short labels; career and history) ----
        public static readonly string[] Stat =
        {
            "KNOCK-OUTS", "HILL TIME", "DELIVERED", "HEALING", "ORE BANKED", "BUILDS USED BY ALLIES", "BUILT", "EXPLOSIVE HITS",
            "DEMOLISHED", "PURIFIED",
        };

        /// <summary>The stat with its number and unit, for example "142 S HILL TIME" or "38 HEALING".</summary>
        public static string StatValue(RoleStat s, int v)
        {
            string n = v.ToString("#,0", CultureInfo.InvariantCulture);
            return s == RoleStat.HillSeconds ? string.Format(CultureInfo.InvariantCulture, "{0} S {1}", n, Stat[(int)s])
                : string.Format(CultureInfo.InvariantCulture, "{0} {1}", n, Stat[(int)s]);
        }

        /// <summary>What a class earns mastery from, in plain words.</summary>
        public static readonly string[] JobLine =
        {
            "knock-outs and hill time",
            "biomass delivered to the egg",
            "ore banked at your hut",
            "explosive hits and ground carved",
            "pieces your teammates use",
            "healing teammates and purifying the egg",
        };

        // ---- titles: index 0 unused (no title), 1..7 unlock at CosmeticLook.TitleLevels ----
        public static readonly string[][] Titles =
        {
            new[] { "", "SHIELD RECRUIT", "SHIELDBEARER", "LINE HOLDER", "HILL WARDEN", "VANGUARD", "BASIN CHAMPION", "MASTER WARRIOR" },
            new[] { "", "SEED CARRIER", "EGG TENDER", "FIELD RUNNER", "HARVEST KEEPER", "GROVE WARDEN", "KAIJU WHISPERER", "MASTER FARMER" },
            new[] { "", "PICK HAND", "VEIN FINDER", "ORE HAULER", "DEEP DELVER", "FORGE FRIEND", "HUT RAISER", "MASTER MINER" },
            new[] { "", "FUSE LIGHTER", "CRATER MAKER", "WALL BREAKER", "BLAST CALLER", "SIEGE HAND", "GROUND SHAKER", "MASTER DEMOLITIONIST" },
            new[] { "", "PLANK HAND", "WALL RAISER", "STAIR SETTER", "BRIDGE WRIGHT", "BULWARK MAKER", "BASIN ARCHITECT", "MASTER BUILDER" },
            new[] { "", "LANTERN BEARER", "MENDER", "FIELD MEDIC", "LIFESAVER", "EGG PURIFIER", "LAST LIGHT", "MASTER HEALER" },
        };

        public static readonly string[] Frames = { "PLAIN", "BRONZE", "SILVER", "GOLD", "BASIN CROWN" };
        public static readonly string[] Trims = { "STOCK", "MOSS", "SLATE", "BONE", "SOOT", "LICHEN" };
        public static readonly string[] Emblems = { "NONE", "ROUNDEL", "DIAMOND", "SHIELD", "HEXAGON", "CROWN" };

        public static string Title(ClassId cls, int id)
        {
            string[] row = Titles[(int)cls < 0 || (int)cls >= Titles.Length ? 0 : (int)cls];
            return id > 0 && id < row.Length ? row[id] : string.Empty;
        }

        // ---- match history ----
        public static string HistoryEmpty = "No matches yet. Your last 50 finished matches are kept here.";
        public static string PageOf = "PAGE {0} OF {1}";
        public static string Win = "WIN";
        public static string Draw = "DRAW";
        public static string Loss = "LOSS";
        public static string HistoryRow = "{0}   {1}   {2}   {3}   {4} PTS";
        public static string WatchReplay = "WATCH REPLAY";
        public static string ReplayGone = "Replay no longer kept (the newest 20 are kept).";
        public static string NoReplay = "No replay was saved for this match.";
        public static string BotMatchTag = "BOTS ONLY";
        public static string DateUnknown = "UNKNOWN DATE";

        public static string Result(int result) => result >= 2 ? Win : result == 1 ? Draw : Loss;

        // ---- achievements ----
        public static string AchievementsCount = "{0} OF {1} UNLOCKED";
        public static string Unlocked = "UNLOCKED";
        public static string BestOfTarget = "BEST {0} OF {1}";
        public static string NotYet = "NOT YET";

        // ---- results strip ----
        public static string StripGained = "{0} MASTERY  +{1} XP";
        public static string StripLevelUp = "{0} MASTERY {1}";
        public static string StripNone = "No mastery from this match: {0}";
        public static string StripNext = "NEXT: {1} AT MASTERY {0}";
        public static string StripUnlocked = "UNLOCKED: {0}";
        public static string WhyBotMatch = "Bots-only match: half mastery.";
        public static string WhyBotAllowance = "Today's mastery from bots-only matches is used up for this class.";
        public static string WhyPractice = "practice modes do not count.";
        public static string WhyNotFinished = "the match did not finish.";
        public static string WhyCap = "Most mastery one match can give.";

        public static string Why(MasteryWhy w)
        {
            switch (w)
            {
                case MasteryWhy.BotMatch: return WhyBotMatch;
                case MasteryWhy.BotDailyAllowanceUsed: return WhyBotAllowance;
                case MasteryWhy.PracticeMode: return WhyPractice;
                case MasteryWhy.NotFinished: return WhyNotFinished;
                case MasteryWhy.MatchCap: return WhyCap;
                default: return string.Empty;
            }
        }

        public static string F(string template, params object[] args) => string.Format(CultureInfo.InvariantCulture, template, args);
    }
}

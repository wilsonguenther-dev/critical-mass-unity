using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// b15 (systems audit W1, W2, W4): the ONE words table for the match story, the role awards, the first-match class goals
    /// and the honest-profile lines. Views read every player-facing string of those features from here and never hold a
    /// literal of their own, so a localisation loader can overwrite these plain static fields at boot (as ProgressionWords).
    /// Templates use {name} slots filled by <see cref="Fill"/>; word order is free per language. Sentence case for lines,
    /// upper case only where the screen's existing headings are upper case.
    /// </summary>
    public static class MatchWords
    {
        // ---------------------------------------------------------------- the match story (results, STORY page)

        public static string StoryTab = "MATCH STORY";
        public static string StoryHeading = "HILL TIME OVER THE MATCH";
        public static string AwardsHeading = "AWARDS";
        public static string AxisSeconds = "{s} s";
        public static string LegendAzure = "Azure";
        public static string LegendCrimson = "Crimson";
        public static string LegendHatch = "Kaiju hatch";
        public static string LegendHut = "Hut level";
        public static string LegendLead = "Lead change";
        public static string TeamAzure = "Azure";
        public static string TeamCrimson = "Crimson";

        /// <summary>The winner led from its first lead to the end.</summary>
        public static string TurnLedAndHeld = "{team} took the lead at {time} and never lost it.";
        /// <summary>The winner lost the lead at least once, then took it back for good.</summary>
        public static string TurnRetook = "{team} took the lead back at {time} and held it to the end.";
        /// <summary>The winner was well behind first.</summary>
        public static string TurnComeback = "{team} came back from {deficit} s down and took the lead for good at {time}.";
        /// <summary>The last seconds decided it (the winner's final lead came in the last 30 s).</summary>
        public static string TurnLate = "{team} took the lead at {time}, with the clock almost out.";
        public static string TurnDraw = "Neither team pulled clear: the hill time ended level.";
        public static string TurnNobody = "Nobody held the hill this match.";
        /// <summary>A short match (or a replay that started late) with no lead mark for the winner.</summary>
        public static string TurnNoMark = "{team} held the hill longer.";

        // ---------------------------------------------------------------- role awards

        public static string[] AwardTitles = { "HILL ANCHOR", "LIFESAVER", "MASTER BUILDER", "PROSPECTOR", "TOP FEEDER", "DEMOLISHER" };
        public static string[] AwardLines =
        {
            "{a} s holding the hill",
            "{a} HP healed, {b} saves",
            "{a} walls and pieces built",
            "{a} ore banked, {b} hut levels",
            "{a} biomass fed to the egg",
            "{a} structures blown, {b} multi-hits",
        };
        /// <summary>The same stats said about the player, for the YOUR BEST line.</summary>
        public static string[] BestLines =
        {
            "{a} s on the hill",
            "{a} HP healed",
            "{a} walls and pieces built",
            "{a} ore banked",
            "{a} biomass fed",
            "{a} structures blown",
        };
        public static string AwardNobody = "Nobody this match";
        public static string BestPrefix = "YOUR BEST";
        public static string BestRank = "{line}, #{rank} of {n}";
        /// <summary>The summary card's line: the prefix and the best line.</summary>
        public static string BestOnCard = "YOUR BEST: {line}";
        public static string BestKnockOuts = "{a} knock-outs";
        public static string BestNothing = "Play your role to earn an award";
        public static string You = "YOU";

        // ---------------------------------------------------------------- first-match class goals (match start, respawn)

        /// <summary>
        /// Indexed by ClassId (Warrior, Farmer, Miner, Demolitionist, Builder, Healer). The {Attack} {Interact} {DigPlace}
        /// {Switch} slots are TrialText tokens: they become the key or button bound in the control scheme in use.
        /// </summary>
        public static string[] ClassGoals =
        {
            "Warrior goal: knock out an enemy on the hill. Attack with {Attack}",
            "Farmer goal: bring food to the egg so the Kaiju sides with your team. Gather with {Interact}",
            "Miner goal: bank ore at your hut to make your whole team stronger. Mine with {DigPlace}",
            "Demolitionist goal: hit an enemy with TNT. Throw it with {DigPlace}",
            "Builder goal: put a ladder on the hill wall so your team gets in. Pick it with {Switch}, build with {DigPlace}",
            "Healer goal: heal a hurt teammate. Mend with {Attack}",
        };
        public static string GoalDone = "Goal done. Nice work.";

        // ---------------------------------------------------------------- honest profile (Barracks, character panel)

        /// <summary>The Miner's ability name on the match setup card while barricades are dead (item 4).</summary>
        public static string MinerAbility = "Haul";
        public static string MoreComing = "More looks are coming.";
        public static string TopLevelNothingNew = "Levels past {level} unlock nothing yet.";
        public static string NoLookYet = "NONE YET";
        public static string OutfitNote = "Choose a look for each class.";
        public static string OutfitNoteMore = "Choose a look for each class. New pieces unlock as you level up.";
        public static string HowSkill = "Knock-outs, deliveries to the egg, time on the hill and knocking out the Kaiju earn skill points at the end of every match. Levels unlock looks only, never strength.";
        public static string HowSkillCoins = "Knock-outs, deliveries to the egg, time on the hill and knocking out the Kaiju earn skill points and coins at the end of every match. Levels unlock looks only, never strength.";

        // ---------------------------------------------------------------- helpers

        public static string Team(TeamId t) => t == TeamId.Azure ? TeamAzure : TeamCrimson;

        /// <summary>m:ss of a match time (invariant culture).</summary>
        public static string Clock(double seconds)
        {
            int s = seconds <= 0 || double.IsNaN(seconds) ? 0 : (int)System.Math.Floor(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string N(double v) => System.Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

        /// <summary>Fills {name} slots from pairs: Fill(t, "team", "Azure", "time", "3:12"). Unknown slots stay as written.</summary>
        public static string Fill(string template, params string[] pairs)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            var map = new Dictionary<string, string>(pairs.Length / 2);
            for (int i = 0; i + 1 < pairs.Length; i += 2) map[pairs[i]] = pairs[i + 1];
            var sb = new System.Text.StringBuilder(template.Length + 16);
            int k = 0;
            while (k < template.Length)
            {
                int open = template.IndexOf('{', k);
                int close = open < 0 ? -1 : template.IndexOf('}', open + 1);
                if (open < 0 || close < 0)
                {
                    sb.Append(template, k, template.Length - k);
                    break;
                }
                sb.Append(template, k, open - k);
                string key = template.Substring(open + 1, close - open - 1);
                if (map.TryGetValue(key, out string v)) sb.Append(v);
                else sb.Append(template, open, close - open + 1);
                k = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>Every template in the table (tests: nothing empty, slots well formed).</summary>
        public static IEnumerable<string> All()
        {
            foreach (string s in new[]
            {
                StoryTab, StoryHeading, AwardsHeading, AxisSeconds, LegendAzure, LegendCrimson, LegendHatch, LegendHut, LegendLead,
                TeamAzure, TeamCrimson, TurnLedAndHeld, TurnRetook, TurnComeback, TurnLate, TurnDraw, TurnNobody, TurnNoMark,
                AwardNobody, BestPrefix, BestRank, BestOnCard, BestKnockOuts, BestNothing, You, GoalDone, MinerAbility, MoreComing, TopLevelNothingNew,
                NoLookYet, OutfitNote, OutfitNoteMore, HowSkill, HowSkillCoins,
            }) yield return s;
            foreach (string s in AwardTitles) yield return s;
            foreach (string s in AwardLines) yield return s;
            foreach (string s in BestLines) yield return s;
            foreach (string s in ClassGoals) yield return s;
        }
    }
}

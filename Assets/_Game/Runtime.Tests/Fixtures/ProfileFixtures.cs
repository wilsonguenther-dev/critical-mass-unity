namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 profile: REAL schema-1 profiles, copied byte for byte from a play-test Mac on 2026-11-17
    /// (~/Library/Application Support/Drivia/Critical Mass/profile/profile.json and its .bak, written by build 1.0.990-era
    /// players after 65 and 64 matches), plus the PlayerPrefs achievement keys that machine held ("cm.ach.*" = 1).
    /// The migration tests read these, never a hand-made file.
    /// </summary>
    public static class ProfileFixtures
    {
        /// <summary>profile.json (65 matches).</summary>
        public const string V1Main = @"{
    ""Version"": 1,
    ""PlayerName"": ""BOOB"",
    ""SkillPoints"": 33060,
    ""Coins"": 8318,
    ""MatchesPlayed"": 65,
    ""Wins"": 28,
    ""Draws"": 0,
    ""Losses"": 37,
    ""KnockOuts"": 1239,
    ""BestMatchSkillPoints"": 1219,
    ""RelicsFound"": [
        ""relic-05"",
        ""relic-07"",
        ""relic-03"",
        ""relic-08""
    ],
    ""Loadouts"": [
        {
            ""Class"": ""Warrior"",
            ""Head"": """",
            ""Emblem"": """",
            ""Emote"": ""Cheer"",
            ""Name"": """"
        },
        {
            ""Class"": ""Builder"",
            ""Head"": """",
            ""Emblem"": """",
            ""Emote"": """",
            ""Name"": """"
        }
    ]
}";

        /// <summary>profile.json.bak (one match earlier: 64 matches).</summary>
        public const string V1Backup = @"{
    ""Version"": 1,
    ""PlayerName"": ""BOOB"",
    ""SkillPoints"": 33060,
    ""Coins"": 8256,
    ""MatchesPlayed"": 64,
    ""Wins"": 28,
    ""Draws"": 0,
    ""Losses"": 36,
    ""KnockOuts"": 1230,
    ""BestMatchSkillPoints"": 1219,
    ""RelicsFound"": [
        ""relic-05"",
        ""relic-07"",
        ""relic-03"",
        ""relic-08""
    ],
    ""Loadouts"": [
        {
            ""Class"": ""Warrior"",
            ""Head"": """",
            ""Emblem"": """",
            ""Emote"": ""Cheer"",
            ""Name"": """"
        },
        {
            ""Class"": ""Builder"",
            ""Head"": """",
            ""Emblem"": """",
            ""Emote"": """",
            ""Name"": """"
        }
    ]
}";

        /// <summary>The "cm.ach." keys set to 1 in that machine's PlayerPrefs (com.drivia.criticalmass).</summary>
        public static readonly string[] V1PlayerPrefsAchievements = { "banner-raised", "first-feeding", "hill-holder", "kingmaker", "payback" };

        /// <summary>The main file cut off mid-write (the first 200 bytes): what a crash during a save leaves.</summary>
        public static string V1Truncated => V1Main.Substring(0, 200);
    }
}

using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>The cosmetic loadout of one class (Barracks, DG-27). Empty strings = the class default.</summary>
    [Serializable]
    public sealed class ClassLoadout
    {
        /// <summary>ClassId name ("Warrior"), stable across enum reorders.</summary>
        public string Class = string.Empty;
        /// <summary>Headgear item name (Head_&lt;Name&gt;.fbx), "" = the class's level-1 headgear.</summary>
        public string Head = string.Empty;
        /// <summary>Emblem item name (Emblem_&lt;Name&gt;.png), "" = the class's level-1 emblem.</summary>
        public string Emblem = string.Empty;
        /// <summary>Victory emote item name, "" = the class's level-1 emote.</summary>
        public string Emote = string.Empty;
        /// <summary>Reserved per-class display name; "" = the player name (owner choice 5 in DESIGN-progression-pickups).</summary>
        public string Name = string.Empty;
        /// <summary>b15 profile v2: the mastery look (ids into MasteryRewards; 0 = default). Badge = the procedural emblem.</summary>
        public int Title;
        public int Frame;
        public int Trim;
        public int Badge;
    }

    /// <summary>
    /// b15 profile v2: one class's mastery track and career totals. Xp gives the mastery level (MasteryRules); the totals
    /// are from every finished match with the class (MatchHistory.Record). Cosmetic progress only.
    /// </summary>
    [Serializable]
    public sealed class ClassMastery
    {
        /// <summary>ClassId name ("Healer").</summary>
        public string Class = string.Empty;
        public int Xp;
        /// <summary>Bots-only allowance: the local day (yyyymmdd) and the XP already taken from bots-only matches on it.</summary>
        public int BotDay;
        public int BotXpToday;
        public int Matches, Wins, Draws, Losses;
        public int KnockOuts, Deaths, HillSeconds, Delivered, Healing, OreBanked, PiecesUsed, PiecesBuilt, ExplosiveHits, Demolished, Purified;
        public int BestScore;

        public int Level => MasteryRules.LevelFor(Xp);

        public int Total(RoleStat s)
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

        internal void Clean()
        {
            Xp = Math.Max(0, Math.Min(Xp, MasteryRules.MaxXp));
            BotXpToday = Math.Max(0, BotXpToday);
            Matches = Math.Max(0, Matches); Wins = Math.Max(0, Wins); Draws = Math.Max(0, Draws); Losses = Math.Max(0, Losses);
            KnockOuts = Math.Max(0, KnockOuts); Deaths = Math.Max(0, Deaths); HillSeconds = Math.Max(0, HillSeconds);
            Delivered = Math.Max(0, Delivered); Healing = Math.Max(0, Healing); OreBanked = Math.Max(0, OreBanked);
            PiecesUsed = Math.Max(0, PiecesUsed); PiecesBuilt = Math.Max(0, PiecesBuilt); ExplosiveHits = Math.Max(0, ExplosiveHits);
            Demolished = Math.Max(0, Demolished); Purified = Math.Max(0, Purified); BestScore = Math.Max(0, BestScore);
        }
    }

    /// <summary>b15 profile v2: one achievement (moved in from PlayerPrefs): unlocked or not, and the best progress so far.</summary>
    [Serializable]
    public sealed class AchievementRecord
    {
        public string Id = string.Empty;
        public bool Unlocked;
        /// <summary>When it unlocked (UTC, ISO 8601 from the system clock); "" when migrated from PlayerPrefs or locked.</summary>
        public string UnlockedUtc = string.Empty;
        /// <summary>Best progress toward the target (AchievementProgress), for the honest progress bar.</summary>
        public int Best;
    }

    /// <summary>b15 profile v2: one finished match in the history (the newest MatchHistory.Keep are kept).</summary>
    [Serializable]
    public sealed class MatchRecord
    {
        /// <summary>UTC time from the system clock when it was saved, ISO 8601 ("o").</summary>
        public string DateUtc = string.Empty;
        public string Map = string.Empty;
        public int Mode;
        public string ModeLabel = string.Empty;
        public int TeamSize;
        public string Class = string.Empty;
        /// <summary>0 loss, 1 draw, 2 win.</summary>
        public int Result;
        public int Score;
        public int AzureHill, CrimsonHill;
        public bool Azure;
        public bool BotOnly;
        public int MasteryXp;
        public RoleStats Stats;
        /// <summary>One sentence on the turning point when the match summary gave one ("" otherwise).</summary>
        public string TurningPoint = string.Empty;
        /// <summary>The replay's file name in the replay folder ("" when none was saved).</summary>
        public string ReplayFile = string.Empty;
        public int Seed;
        /// <summary>b16: a practice match (shown apart; not in the careers or the profile's matches).</summary>
        public bool Practice;
        /// <summary>b16: the local player left before the end (recorded as a loss).</summary>
        public bool Left;
    }

    /// <summary>
    /// The saved player profile (DG-39 part 1): skill points (the level is derived), coins, match counts, relics found and
    /// the Barracks loadout per class. Between-match data only: nothing here ever reaches the sim, and nothing in it
    /// changes a unit's strength (levels unlock cosmetics only). Serialised with JsonUtility by ProfileStore.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        /// <summary>Schema version written by this build. Older files are migrated in ProfileStore.Migrate.</summary>
        public const int CurrentVersion = 2;   // b15 profile: v2 adds Mastery, Achievements, History and the loadout's look ids
        public const int MaxNameLength = 16;
        public const string DefaultName = "PLAYER";

        public int Version = CurrentVersion;
        public string PlayerName = DefaultName;
        public int SkillPoints;
        public int Coins;
        /// <summary>Completed and left matches outside practice (MatchCounting); practice is PracticeMatches.</summary>
        public int MatchesPlayed;
        /// <summary>b16: completed practice matches (Training, Practice, Spectate, the demo), shown apart. Older files read 0.</summary>
        public int PracticeMatches;
        public int Wins;
        public int Draws;
        public int Losses;
        public int KnockOuts;
        public int BestMatchSkillPoints;
        public string[] RelicsFound = new string[0];
        public ClassLoadout[] Loadouts = new ClassLoadout[0];
        /// <summary>
        /// b15 item 3: one bit per ClassId, set once the player has done that class's first-match goal (ClassGoals). Added
        /// inside schema 1 (kept by profile v2 as it is): an older file has no field and reads as 0 (no goal done), so no migration step is needed.
        /// </summary>
        public int ClassGoalsDone;
        /// <summary>v2: one track per class, created on first use.</summary>
        public ClassMastery[] Mastery = new ClassMastery[0];
        /// <summary>v2: the achievements (moved in from PlayerPrefs "cm.ach.*" by ProfileStore.Migrate; the keys stay).</summary>
        public AchievementRecord[] Achievements = new AchievementRecord[0];
        /// <summary>v2: the newest finished matches, newest first (at most MatchHistory.Keep).</summary>
        public MatchRecord[] History = new MatchRecord[0];

        /// <summary>The level the skill points reach (Leveling curve).</summary>
        public int Level => Leveling.LevelFor(SkillPoints);

        /// <summary>The loadout of a class, created (and kept) on first use.</summary>
        public ClassLoadout Loadout(ClassId cls)
        {
            string key = cls.ToString();
            if (Loadouts != null)
                for (int i = 0; i < Loadouts.Length; i++)
                    if (Loadouts[i] != null && Loadouts[i].Class == key) return Loadouts[i];
            var l = new ClassLoadout { Class = key };
            var list = new List<ClassLoadout>(Loadouts ?? new ClassLoadout[0]) { l };
            Loadouts = list.ToArray();
            return l;
        }

        /// <summary>The mastery track of a class, created (and kept) on first use.</summary>
        public ClassMastery MasteryOf(ClassId cls)
        {
            string key = cls.ToString();
            if (Mastery != null)
                for (int i = 0; i < Mastery.Length; i++)
                    if (Mastery[i] != null && Mastery[i].Class == key) return Mastery[i];
            var m = new ClassMastery { Class = key };
            var list = new List<ClassMastery>(Mastery ?? new ClassMastery[0]) { m };
            Mastery = list.ToArray();
            return m;
        }

        /// <summary>The record of an achievement id, created on first use.</summary>
        public AchievementRecord Achievement(string id)
        {
            if (Achievements != null)
                for (int i = 0; i < Achievements.Length; i++)
                    if (Achievements[i] != null && Achievements[i].Id == id) return Achievements[i];
            var a = new AchievementRecord { Id = id ?? string.Empty };
            var list = new List<AchievementRecord>(Achievements ?? new AchievementRecord[0]) { a };
            Achievements = list.ToArray();
            return a;
        }

        public bool HasAchievement(string id)
        {
            if (Achievements == null || string.IsNullOrEmpty(id)) return false;
            foreach (AchievementRecord a in Achievements) if (a != null && a.Id == id) return a.Unlocked;
            return false;
        }

        public bool HasRelic(string id)
        {
            if (string.IsNullOrEmpty(id) || RelicsFound == null) return false;
            return Array.IndexOf(RelicsFound, id) >= 0;
        }

        /// <summary>Adds a relic id once; false when it was already found.</summary>
        public bool AddRelic(string id)
        {
            if (string.IsNullOrEmpty(id) || HasRelic(id)) return false;
            var list = new List<string>(RelicsFound ?? new string[0]) { id };
            RelicsFound = list.ToArray();
            return true;
        }

        /// <summary>Repairs anything a hand edit or an old build could leave: nulls, negatives, bad names, duplicate loadouts.</summary>
        public void Sanitize()
        {
            Version = CurrentVersion;
            PlayerName = CleanName(PlayerName);
            SkillPoints = Math.Max(0, Math.Min(SkillPoints, Leveling.TotalFor(Leveling.MaxLevel)));
            Coins = Math.Max(0, Coins);
            MatchesPlayed = Math.Max(0, MatchesPlayed);
            Wins = Math.Max(0, Wins);
            Draws = Math.Max(0, Draws);
            Losses = Math.Max(0, Losses);
            KnockOuts = Math.Max(0, KnockOuts);
            BestMatchSkillPoints = Math.Max(0, BestMatchSkillPoints);
            ClassGoalsDone &= (1 << 6) - 1;   // b15: six classes, nothing else
            var relics = new List<string>();
            if (RelicsFound != null)
                foreach (string r in RelicsFound)
                    if (!string.IsNullOrEmpty(r) && !relics.Contains(r)) relics.Add(r);
            RelicsFound = relics.ToArray();
            SanitizeV2();
            var loadouts = new List<ClassLoadout>();
            var seen = new HashSet<string>();
            if (Loadouts != null)
                foreach (ClassLoadout l in Loadouts)
                {
                    if (l == null || string.IsNullOrEmpty(l.Class) || !Enum.TryParse(l.Class, out ClassId _) || !seen.Add(l.Class)) continue;
                    l.Head = l.Head ?? string.Empty;
                    l.Emblem = l.Emblem ?? string.Empty;
                    l.Emote = l.Emote ?? string.Empty;
                    l.Name = string.IsNullOrEmpty(l.Name) ? string.Empty : CleanName(l.Name);
                    // v2 look ids: out of range or no longer unlocked (a hand edit, a lowered track) = the default
                    int lvl = MasteryOf((ClassId)Enum.Parse(typeof(ClassId), l.Class)).Level;
                    l.Title = MasteryRewards.Unlocked(RewardKind.Title, l.Title, lvl) ? l.Title : 0;
                    l.Frame = MasteryRewards.Unlocked(RewardKind.Frame, l.Frame, lvl) ? l.Frame : 0;
                    l.Trim = MasteryRewards.Unlocked(RewardKind.Trim, l.Trim, lvl) ? l.Trim : 0;
                    l.Badge = MasteryRewards.Unlocked(RewardKind.Emblem, l.Badge, lvl) ? l.Badge : 0;
                    loadouts.Add(l);
                }
            Loadouts = loadouts.ToArray();
        }

        /// <summary>v2 parts: mastery one per known class, achievements one per id, history newest first and trimmed.</summary>
        void SanitizeV2()
        {
            var mastery = new List<ClassMastery>();
            var seen = new HashSet<string>();
            if (Mastery != null)
                foreach (ClassMastery m in Mastery)
                {
                    if (m == null || string.IsNullOrEmpty(m.Class) || !Enum.TryParse(m.Class, out ClassId _) || !seen.Add(m.Class)) continue;
                    m.Clean();
                    mastery.Add(m);
                }
            Mastery = mastery.ToArray();
            var ach = new List<AchievementRecord>();
            seen.Clear();
            if (Achievements != null)
                foreach (AchievementRecord a in Achievements)
                {
                    if (a == null || string.IsNullOrEmpty(a.Id) || !seen.Add(a.Id)) continue;
                    a.Best = Math.Max(0, a.Best);
                    a.UnlockedUtc = a.UnlockedUtc ?? string.Empty;
                    ach.Add(a);
                }
            Achievements = ach.ToArray();
            var hist = new List<MatchRecord>();
            if (History != null)
                foreach (MatchRecord r in History)
                {
                    if (r == null || string.IsNullOrEmpty(r.Class) || !Enum.TryParse(r.Class, out ClassId _)) continue;
                    r.DateUtc = r.DateUtc ?? string.Empty;
                    r.Map = r.Map ?? string.Empty;
                    r.ModeLabel = r.ModeLabel ?? string.Empty;
                    r.TurningPoint = r.TurningPoint ?? string.Empty;
                    r.ReplayFile = SafeFileName(r.ReplayFile);
                    r.Result = Math.Max(0, Math.Min(2, r.Result));
                    r.Score = Math.Max(0, r.Score);
                    hist.Add(r);
                    if (hist.Count >= MatchHistory.Keep) break;
                }
            History = hist.ToArray();
        }

        /// <summary>A bare file name (no folders, no "..") or "": a hand-edited path can never point outside the replay folder.</summary>
        public static string SafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            string n = System.IO.Path.GetFileName(s.Replace('\\', '/'));
            return n == null || n.Length == 0 || n.Contains("..") || n.IndexOf('/') >= 0 ? string.Empty : n;
        }

        /// <summary>Upper-case letters, digits, space, '-' and '_'; at most MaxNameLength; empty or a blocked word = DefaultName.</summary>
        public static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return DefaultName;
            var sb = new System.Text.StringBuilder(MaxNameLength);
            foreach (char c0 in s)
            {
                char c = char.ToUpperInvariant(c0);
                bool ok = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '-' || c == '_';
                if (!ok) continue;
                if (c == ' ' && (sb.Length == 0 || sb[sb.Length - 1] == ' ')) continue;
                sb.Append(c);
                if (sb.Length >= MaxNameLength) break;
            }
            string r = sb.ToString().TrimEnd();
            // online-rooms-2: the name other players see online, so it passes the same all-ages word filter as chat.
            return r.Length == 0 || CriticalMass.Net.ChatFilter.ContainsBlocked(r) ? DefaultName : r;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        public PlayerProfile Clone()
        {
            PlayerProfile p = JsonUtility.FromJson<PlayerProfile>(JsonUtility.ToJson(this));
            p.Sanitize();
            return p;
        }
    }
}

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
        public const int CurrentVersion = 1;
        public const int MaxNameLength = 16;
        public const string DefaultName = "PLAYER";

        public int Version = CurrentVersion;
        public string PlayerName = DefaultName;
        public int SkillPoints;
        public int Coins;
        public int MatchesPlayed;
        public int Wins;
        public int Draws;
        public int Losses;
        public int KnockOuts;
        public int BestMatchSkillPoints;
        public string[] RelicsFound = new string[0];
        public ClassLoadout[] Loadouts = new ClassLoadout[0];

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
            var relics = new List<string>();
            if (RelicsFound != null)
                foreach (string r in RelicsFound)
                    if (!string.IsNullOrEmpty(r) && !relics.Contains(r)) relics.Add(r);
            RelicsFound = relics.ToArray();
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
                    loadouts.Add(l);
                }
            Loadouts = loadouts.ToArray();
        }

        /// <summary>Upper-case letters, digits, space, '-' and '_'; at most MaxNameLength; empty = DefaultName.</summary>
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
            return r.Length == 0 ? DefaultName : r;
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

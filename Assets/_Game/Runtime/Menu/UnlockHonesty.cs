using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// b15 item 6 (systems audit defect 3, profile hygiene): the item table names 72 looks but only those whose art is in
    /// the build can be worn (audit: 0 of the 54 headgear and emblems, 1 of 3 emotes per class), and levels 11 to 30
    /// unlock nothing. Every unlock promise the menus make goes through here, so none names a look that does not exist:
    /// the next-unlock line names only real looks (else "Levels past N unlock nothing yet."), the level-up line lists only
    /// real looks, and the outfit page says "More looks are coming." once instead of a row per missing piece. No new art.
    /// </summary>
    public static class UnlockHonesty
    {
        /// <summary>True when an item's art is in the build (tests swap it; null restores the build's art check).</summary>
        public static Func<CosmeticItem, bool> HasArt = DefaultHasArt;
        /// <summary>Art files the table does not name (extras, ExtraItemLevel); tests swap it.</summary>
        public static Func<ClassId, CosmeticSlot, IEnumerable<string>> ArtIds = DefaultArtIds;

        public static readonly CosmeticSlot[] Slots = { CosmeticSlot.Head, CosmeticSlot.Emblem, CosmeticSlot.Emote };

        static bool DefaultHasArt(CosmeticItem it) => it != null && Cosmetics.Has(it.Class, it.Slot, it.Id);
        static IEnumerable<string> DefaultArtIds(ClassId c, CosmeticSlot s) => Cosmetics.ArtIds(c, s);

        public static void ResetProbes()
        {
            HasArt = DefaultHasArt;
            ArtIds = DefaultArtIds;
        }

        /// <summary>An item a player can actually wear once unlocked.</summary>
        public static bool Real(CosmeticItem it) => it != null && (HasArt ?? DefaultHasArt)(it);

        /// <summary>The wearable looks of one class slot, table and extras, in unlock order (a new list).</summary>
        public static List<CosmeticItem> RealItems(ClassId c, CosmeticSlot s)
        {
            List<CosmeticItem> all = CosmeticItems.WithExtras(c, s, (ArtIds ?? DefaultArtIds)(c, s));
            for (int i = all.Count - 1; i >= 0; i--) if (!Real(all[i])) all.RemoveAt(i);
            return all;
        }

        /// <summary>The highest level that unlocks a real look for a class (0: none at all).</summary>
        public static int LastRealLevel(ClassId c)
        {
            int last = 0;
            foreach (CosmeticSlot s in Slots)
                foreach (CosmeticItem it in RealItems(c, s))
                    if (it.UnlockLevel > last) last = it.UnlockLevel;
            return last;
        }

        /// <summary>"LEVEL 4: FLEX (emote)" for the next level above <paramref name="level"/> that unlocks a real look for the
        /// class, else "Levels past N unlock nothing yet." (N = the class's last real unlock, at least 1).</summary>
        public static string NextLine(ClassId c, int level, string[] slotNames)
        {
            var byLevel = new SortedDictionary<int, List<string>>();
            foreach (CosmeticSlot s in Slots)
                foreach (CosmeticItem it in RealItems(c, s))
                {
                    if (it.UnlockLevel <= level || it.UnlockLevel > Leveling.MaxLevel) continue;
                    if (!byLevel.TryGetValue(it.UnlockLevel, out List<string> names)) byLevel[it.UnlockLevel] = names = new List<string>();
                    string slot = slotNames != null && (int)s < slotNames.Length ? slotNames[(int)s].ToLowerInvariant() : s.ToString().ToLowerInvariant();
                    names.Add(it.DisplayName + " (" + slot + ")");
                }
            foreach (KeyValuePair<int, List<string>> kv in byLevel)
                return ProgressionWords.Level + " " + kv.Key.ToString(CultureInfo.InvariantCulture) + ": " + string.Join(", ", kv.Value);
            return MatchWords.Fill(MatchWords.TopLevelNothingNew, "level", Math.Max(1, LastRealLevel(c)).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>True when some level above <paramref name="level"/> still unlocks a real look for the class.</summary>
        public static bool MoreToUnlock(ClassId c, int level) => LastRealLevel(c) > level;

        /// <summary>"Unlocked: FLEX, ..." for the real looks of any class unlocked at levels from..to (at most 3 names; empty when none).</summary>
        public static string LevelUpLine(int from, int to)
        {
            var names = new List<string>();
            foreach (ClassId c in ClassIds.All)
                foreach (CosmeticSlot s in Slots)
                    foreach (CosmeticItem it in RealItems(c, s))
                        if (it.UnlockLevel >= from && it.UnlockLevel <= to && !names.Contains(it.DisplayName)) names.Add(it.DisplayName);
            if (names.Count == 0) return string.Empty;
            int more = names.Count - 3;
            string line = "Unlocked: " + string.Join(", ", names.GetRange(0, Math.Min(3, names.Count)));
            return more > 0 ? line + " and " + more.ToString(CultureInfo.InvariantCulture) + " more" : line;
        }

        /// <summary>The outfit page note: the level-up promise only while a real look is still to come, else "More looks are coming." once.</summary>
        public static string OutfitNote(ClassId c, int level) =>
            MoreToUnlock(c, level) ? MatchWords.OutfitNoteMore : MatchWords.OutfitNote + " " + MatchWords.MoreComing;
    }
}

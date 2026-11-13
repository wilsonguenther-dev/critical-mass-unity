using System;
using System.Collections.Generic;
using System.Text;
using CriticalMass.Sim.Config;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>A Barracks slot (DG-27). Team colours are not a slot: they stay locked to the side you play.</summary>
    public enum CosmeticSlot { Head, Emblem, Emote }

    /// <summary>One cosmetic: class, slot, id (the &lt;Name&gt; of its art file), screen name and the level that unlocks it.</summary>
    public sealed class CosmeticItem
    {
        public ClassId Class;
        public CosmeticSlot Slot;
        /// <summary>Head and Emblem: the &lt;Name&gt; part of Head_&lt;Name&gt;.fbx / Emblem_&lt;Name&gt;.png. Emote: the clip name.</summary>
        public string Id;
        public string DisplayName;
        public int UnlockLevel;
        /// <summary>True for an item that came from an art file not in the table (unlocks at ExtraItemLevel).</summary>
        public bool Extra;
    }

    /// <summary>
    /// The first cosmetic set per class (DESIGN-progression-pickups section 3, briefs/cosmetics.md): 3 headgear (levels
    /// 1, 3, 6), 6 emblems (1, 2, 4, 5, 7, 9) and 3 victory emotes from the Cheer family (1, 4, 8). The table only names
    /// and prices items; whether one can be worn is decided by the art present (Cosmetics.Has), so a missing file shows
    /// "coming soon" and a new file in the folder appears without code (as an extra at ExtraItemLevel).
    /// Levels unlock looks only, never strength.
    /// </summary>
    public static class CosmeticItems
    {
        public static readonly int[] HeadLevels = { 1, 3, 6 };
        public static readonly int[] EmblemLevels = { 1, 2, 4, 5, 7, 9 };
        public static readonly int[] EmoteLevels = { 1, 4, 8 };
        /// <summary>Unlock level of an art file the table does not name.</summary>
        public const int ExtraItemLevel = 10;

        static readonly string[][] HeadIds =
        {
            new[] { "IronCap", "HornedHelm", "CrestedHelm" },        // Warrior
            new[] { "StrawHat", "SunBonnet", "ScarecrowHat" },       // Farmer
            new[] { "LampCap", "DrillHelm", "CrystalCrown" },        // Miner
            new[] { "Goggles", "FuseBandana", "BlastHelm" },         // Demolitions
            new[] { "HardHat", "PencilCap", "ForemanHelm" },         // Builder
            new[] { "Circlet", "WingedHelm", "HaloCrown" },          // Healer (DG-40; art to come, resolves to nothing until then)
        };

        static readonly string[] EmblemIds = { "Crest", "Sunburst", "HillCrown", "EggSeam", "KaijuEye", "BasinStar" };

        /// <summary>Victory emote clip names (KayKit "Cheer" family) and their screen names.</summary>
        public static readonly string[] EmoteClips = { "Cheer", "Cheer_Flex", "Cheer_Dance" };
        static readonly string[] EmoteNames = { "CHEER", "FLEX", "VICTORY DANCE" };

        static Dictionary<int, List<CosmeticItem>> _table;

        static int Key(ClassId c, CosmeticSlot s) => (int)c * 8 + (int)s;

        static Dictionary<int, List<CosmeticItem>> AllItems
        {
            get
            {
                if (_table != null) return _table;
                var t = new Dictionary<int, List<CosmeticItem>>();
                foreach (ClassId c in ClassIds.All)
                {
                    var head = new List<CosmeticItem>();
                    string[] ids = HeadIds[(int)c];
                    for (int i = 0; i < ids.Length; i++) head.Add(Item(c, CosmeticSlot.Head, ids[i], DisplayName(ids[i]), HeadLevels[i]));
                    t[Key(c, CosmeticSlot.Head)] = head;
                    var emblem = new List<CosmeticItem>();
                    for (int i = 0; i < EmblemIds.Length; i++) emblem.Add(Item(c, CosmeticSlot.Emblem, EmblemIds[i], DisplayName(EmblemIds[i]), EmblemLevels[i]));
                    t[Key(c, CosmeticSlot.Emblem)] = emblem;
                    var emote = new List<CosmeticItem>();
                    for (int i = 0; i < EmoteClips.Length; i++) emote.Add(Item(c, CosmeticSlot.Emote, EmoteClips[i], EmoteNames[i], EmoteLevels[i]));
                    t[Key(c, CosmeticSlot.Emote)] = emote;
                }
                _table = t;
                return t;
            }
        }

        static CosmeticItem Item(ClassId c, CosmeticSlot s, string id, string name, int level) =>
            new CosmeticItem { Class = c, Slot = s, Id = id, DisplayName = name, UnlockLevel = level };

        /// <summary>The table items of a class slot, in unlock order (shared list: do not modify).</summary>
        public static IReadOnlyList<CosmeticItem> Table(ClassId cls, CosmeticSlot slot) => TableList(cls, slot);

        static List<CosmeticItem> TableList(ClassId cls, CosmeticSlot slot) =>
            AllItems.TryGetValue(Key(cls, slot), out List<CosmeticItem> l) ? l : new List<CosmeticItem>();

        /// <summary>The table items plus extra art ids not in the table (sorted, at ExtraItemLevel). A new list.</summary>
        public static List<CosmeticItem> WithExtras(ClassId cls, CosmeticSlot slot, IEnumerable<string> artIds)
        {
            var list = new List<CosmeticItem>(TableList(cls, slot));
            if (artIds == null) return list;
            var extra = new List<string>();
            foreach (string id in artIds)
            {
                if (string.IsNullOrEmpty(id) || extra.Contains(id)) continue;
                bool known = false;
                foreach (CosmeticItem it in list) known |= it.Id == id;
                if (!known) extra.Add(id);
            }
            extra.Sort(StringComparer.Ordinal);
            foreach (string id in extra)
                list.Add(new CosmeticItem { Class = cls, Slot = slot, Id = id, DisplayName = DisplayName(id), UnlockLevel = ExtraItemLevel, Extra = true });
            return list;
        }

        public static CosmeticItem Find(ClassId cls, CosmeticSlot slot, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (CosmeticItem it in TableList(cls, slot)) if (it.Id == id) return it;
            return null;
        }

        public static bool Unlocked(CosmeticItem item, int level) => item != null && level >= item.UnlockLevel;

        /// <summary>The level-1 item of a slot (what a fresh profile wears), or null.</summary>
        public static CosmeticItem Default(ClassId cls, CosmeticSlot slot)
        {
            List<CosmeticItem> l = TableList(cls, slot);
            return l.Count > 0 ? l[0] : null;
        }

        /// <summary>Every table item, all classes, that unlocks exactly at a level (for the "LEVEL UP" line).</summary>
        public static List<CosmeticItem> UnlockedAtLevel(int level)
        {
            var r = new List<CosmeticItem>();
            foreach (ClassId c in ClassIds.All)
                foreach (CosmeticSlot s in new[] { CosmeticSlot.Head, CosmeticSlot.Emblem, CosmeticSlot.Emote })
                    foreach (CosmeticItem it in TableList(c, s))
                        if (it.UnlockLevel == level) r.Add(it);
            return r;
        }

        /// <summary>"HornedHelm" or "horned_helm" to "HORNED HELM".</summary>
        public static string DisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            var sb = new StringBuilder(id.Length + 4);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (c == '_' || c == '-' || c == ' ')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                    continue;
                }
                bool boundary = i > 0 && char.IsUpper(c) && (char.IsLower(id[i - 1]) || char.IsDigit(id[i - 1]));
                if (boundary && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString().Trim();
        }
    }
}

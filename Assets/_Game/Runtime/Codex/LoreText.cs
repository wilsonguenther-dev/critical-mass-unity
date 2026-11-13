using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CriticalMass.Runtime.Codex
{
    /// <summary>
    /// Relic, easter egg and announcer text from the words pack (DG-04, Assets/_Game/Content/Lore/*.json) by id, with
    /// built-in fallbacks that carry the same words, so the game reads right whether or not the pack ships.
    /// <para>Where the pack is looked for, in order: Resources/Lore/&lt;name&gt; (a TextAsset), StreamingAssets/Lore/&lt;file&gt;,
    /// then (editor only) Assets/_Game/Content/Lore/&lt;file&gt;. The pack sits outside Resources today, so a player build
    /// uses the fallbacks until the words pack is copied into one of the first two places (see the PR notes).</para>
    /// </summary>
    public static class LoreText
    {
        public struct Relic
        {
            public string Id, Name, Line1, Line2;
        }

        [Serializable] sealed class RelicDto { public string id; public string name; public string line1; public string line2; }
        [Serializable] sealed class RelicsFile { public RelicDto[] relics; }
        [Serializable] sealed class EggDto { public string id; public string name; public string line; }
        [Serializable] sealed class EggsFile { public EggDto[] eggs; }
        [Serializable] sealed class LineDto { public string id; public string text; }
        [Serializable] sealed class AnnouncerFile { public LineDto[] lines; }

        static Dictionary<string, Relic> _relics;
        static Dictionary<string, string> _eggs;
        static Dictionary<string, string> _announcer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _relics = null;
            _eggs = null;
            _announcer = null;
        }

        /// <summary>The relic with this id ("relic-01".."relic-12"); a known fallback, or "?" lines for an unknown id.</summary>
        public static Relic GetRelic(string id)
        {
            LoadRelics();
            if (id != null && _relics.TryGetValue(id, out Relic r)) return r;
            return new Relic { Id = id, Name = "Basin Relic", Line1 = "Something old, left off the beaten path.", Line2 = "Keep it safe." };
        }

        /// <summary>An easter egg's on-screen line by id ("egg-01".."egg-05"); empty when the egg has none.</summary>
        public static string EggLine(string id)
        {
            LoadEggs();
            return id != null && _eggs.TryGetValue(id, out string s) ? s : string.Empty;
        }

        /// <summary>An announcer line by id ("an-01".."an-22"); empty when unknown.</summary>
        public static string Announcer(string id)
        {
            LoadAnnouncer();
            return id != null && _announcer.TryGetValue(id, out string s) ? s : string.Empty;
        }

        /// <summary>The pack file's text, or null when it cannot be found (then the fallbacks are used).</summary>
        public static string ReadPackFile(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            TextAsset ta = Resources.Load<TextAsset>("Lore/" + name);
            if (ta != null) return ta.text;
            try
            {
                string p = Path.Combine(Application.streamingAssetsPath, "Lore", file);
                if (File.Exists(p)) return File.ReadAllText(p);
#if UNITY_EDITOR
                p = Path.Combine(Application.dataPath, "_Game", "Resources", "Lore", file);
                if (File.Exists(p)) return File.ReadAllText(p);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LoreText] " + file + ": " + e.Message);
            }
            return null;
        }

        static void LoadRelics()
        {
            if (_relics != null) return;
            _relics = new Dictionary<string, Relic>();
            for (int i = 0; i < FallbackRelics.Length; i++)
            {
                string[] f = FallbackRelics[i];
                _relics[f[0]] = new Relic { Id = f[0], Name = f[1], Line1 = f[2], Line2 = f[3] };
            }
            string json = ReadPackFile("relics.json");
            if (json == null) return;
            try
            {
                RelicsFile file = JsonUtility.FromJson<RelicsFile>(json);
                if (file?.relics == null) return;
                foreach (RelicDto d in file.relics)
                {
                    if (d == null || string.IsNullOrEmpty(d.id) || string.IsNullOrEmpty(d.name) || string.IsNullOrEmpty(d.line1)) continue;
                    _relics[d.id] = new Relic { Id = d.id, Name = d.name, Line1 = d.line1, Line2 = d.line2 ?? string.Empty };
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LoreText] relics.json: " + e.Message);
            }
        }

        static void LoadEggs()
        {
            if (_eggs != null) return;
            _eggs = new Dictionary<string, string>();
            for (int i = 0; i < FallbackEggs.Length; i++) _eggs[FallbackEggs[i][0]] = FallbackEggs[i][1];
            string json = ReadPackFile("eastereggs.json");
            if (json == null) return;
            try
            {
                EggsFile file = JsonUtility.FromJson<EggsFile>(json);
                if (file?.eggs == null) return;
                foreach (EggDto d in file.eggs)
                    if (d != null && !string.IsNullOrEmpty(d.id) && !string.IsNullOrEmpty(d.line)) _eggs[d.id] = d.line;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LoreText] eastereggs.json: " + e.Message);
            }
        }

        static void LoadAnnouncer()
        {
            if (_announcer != null) return;
            _announcer = new Dictionary<string, string> { { "an-22", "Still up there on your own? Respect." } };
            string json = ReadPackFile("announcer.json");
            if (json == null) return;
            try
            {
                AnnouncerFile file = JsonUtility.FromJson<AnnouncerFile>(json);
                if (file?.lines == null) return;
                foreach (LineDto d in file.lines)
                    if (d != null && !string.IsNullOrEmpty(d.id) && !string.IsNullOrEmpty(d.text)) _announcer[d.id] = d.text;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LoreText] announcer.json: " + e.Message);
            }
        }

        /// <summary>The words pack's relics (DG-04, words pack section 5), id, name, line 1, line 2.</summary>
        public static readonly string[][] FallbackRelics =
        {
            new[] { "relic-01", "The First Banner", "Nobody remembers if it was Azure or Crimson.", "Both sides are certain." },
            new[] { "relic-02", "Sky-Stone Shard", "A piece of the night the sky split.", "Still warm, which is worrying." },
            new[] { "relic-03", "Shrine Keeper's Ladle", "Someone once fed the egg on purpose.", "Their last note reads: less violet." },
            new[] { "relic-04", "Waterfall Coin", "Thrown off the edge for luck.", "The cloud below has kept every one." },
            new[] { "relic-05", "Lamp Crystal", "Cut from the old roads under the hill.", "It glows brighter near anything hollow." },
            new[] { "relic-06", "Empty Seed Pouch", "A Farmer's first harvest went straight to the egg.", "The egg did not say thank you." },
            new[] { "relic-07", "Orchard Road Map", "A road to an orchard of burning trees, past the edge.", "Not this season." },
            new[] { "relic-08", "Tide Shell", "Hold it to your ear and you hear the sea.", "There is no sea. That is the strange part." },
            new[] { "relic-09", "Hatchling Tooth", "Lost by a very young Kaiju.", "Found by a very brave Miner." },
            new[] { "relic-10", "Bridge Knot", "Builders tie this knot first and trust it most.", "Nobody else has ever untied one." },
            new[] { "relic-11", "Burnt Fuse", "Left by the Basin's first Demolitions Expert.", "Found a long way from where it was lit." },
            new[] { "relic-12", "Crown of the Hill", "Whoever wore it held the hill from dawn to dusk.", "Then they came down for supper." },
        };

        /// <summary>The words pack's easter egg lines (eastereggs.json "line"), id, line.</summary>
        public static readonly string[][] FallbackEggs =
        {
            new[] { "egg-01", "Fine. Go on, then." },
            new[] { "egg-03", "Still up there on your own? Respect." },
            new[] { "egg-04", "The Kaiju chose no one, and sat down on the hill to think about it." },
            new[] { "egg-05", "No Kaiju were overfed in the making of this game. One was slightly corrupted." },
        };
    }
}

using System;
using UnityEngine;

namespace CriticalMass.Runtime.Codex
{
    /// <summary>
    /// Which Basin Relics the player has found, kept between launches (PlayerPrefs cm.codex.found, a bit mask: bits 0..7
    /// the eight placed relics, bit 8 the secret Crown of the Hill). View side only: the sim never sees it (DG-25 notes:
    /// relics move into the sim only if online play comes).
    /// </summary>
    public static class CodexProgress
    {
        public const string Key = "cm.codex.found";
        public const int RelicCount = RelicPlacement.Count;
        /// <summary>Index of the secret relic (Crown of the Hill, relic-12).</summary>
        public const int CrownIndex = 8;

        /// <summary>Raised with the relic index when a relic is found for the first time.</summary>
        public static event Action<int> Found;

        static readonly string[] Ids =
        {
            "relic-01", "relic-02", "relic-03", "relic-04", "relic-05", "relic-06", "relic-07", "relic-08", "relic-12",
        };

        /// <summary>Words pack id of relic <paramref name="index"/> (0..7 placed, 8 the crown).</summary>
        public static string IdOf(int index) => index >= 0 && index < Ids.Length ? Ids[index] : null;

        public static int Mask => PlayerPrefs.GetInt(Key, 0);

        public static bool IsFound(int index) => index >= 0 && index <= CrownIndex && (Mask & (1 << index)) != 0;

        /// <summary>Placed relics found, 0..8 (the crown is not counted: the Codex shows "n of 8").</summary>
        public static int FoundCount
        {
            get
            {
                int m = Mask, n = 0;
                for (int i = 0; i < RelicCount; i++) if ((m & (1 << i)) != 0) n++;
                return n;
            }
        }

        /// <summary>Marks a relic found; true only the first time.</summary>
        public static bool Mark(int index)
        {
            if (index < 0 || index > CrownIndex || IsFound(index)) return false;
            PlayerPrefs.SetInt(Key, Mask | (1 << index));
            PlayerPrefs.Save();
            Found?.Invoke(index);
            return true;
        }

        /// <summary>Forgets every relic (tests).</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        static readonly string[] CountText = { "0 OF 8", "1 OF 8", "2 OF 8", "3 OF 8", "4 OF 8", "5 OF 8", "6 OF 8", "7 OF 8", "8 OF 8" };

        /// <summary>"3 OF 8" (cached strings).</summary>
        public static string CountLabel(int n) => CountText[Mathf.Clamp(n, 0, CountText.Length - 1)];
    }
}

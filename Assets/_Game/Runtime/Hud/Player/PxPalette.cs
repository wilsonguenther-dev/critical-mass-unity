using CriticalMass.Runtime.Hud.Style;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Player-experience wave (2026-11-22), lane D: the colours and sizes of the local player's combat HUD (what the player
    /// is TOLD). One family for damage and hurt, chosen OUTSIDE both team hues (Azure #16C7D9 h186, Crimson #FF5A5F h358)
    /// and the colour-blind pair (#3D9BFF h211, #FFB000 h41), and away from Corruption (#8E3FD1 h272) and Biomass
    /// (#B7F238 h80): a hot magenta near h320. A shape cue always rides with the colour (enemy bars are chamfered with a
    /// dark outline, ally bars rounded), so colour is never the only signal. Sizes are reference px at 1080p (the HUD canvas
    /// scales uniformly from 1920 x 1080, HudLayout.Scale), written next to each constant.
    /// </summary>
    public static class PxPalette
    {
        /// <summary>Enemy health bars, my hit markers' kill variant, damage words. #F43FB0 (h 322).</summary>
        public static readonly Color Strike = CMTokens.ParseHex("#F43FB0");
        /// <summary>Light ink of the damage family for text on dark (heavy damage numbers, block words). #FFA3DC.</summary>
        public static readonly Color StrikeInk = CMTokens.ParseHex("#FFA3DC");
        /// <summary>Hurt (hits on ME): direction arcs, hurt vignette, low-health pulse, own bar when low. #E8288A (h 330).</summary>
        public static readonly Color Hurt = CMTokens.ParseHex("#E8288A");
        /// <summary>Healing (numbers, the bar's rise glow). #5CE38A (h 141): clear of Azure, Biomass and the hurt family.</summary>
        public static readonly Color Heal = CMTokens.ParseHex("#5CE38A");
        /// <summary>The "chunk just lost" segment of every bar.</summary>
        public static readonly Color Chunk = Color.white;
        /// <summary>Wall and barricade bars (structures are neither team nor damage).</summary>
        public static readonly Color Structure = CMTokens.ParseHex("#D9C7A3");
        /// <summary>Bar background (the meter's empty colour, slightly see-through).</summary>
        public static readonly Color BarBack = CMTokens.WithAlpha(CMTokens.MeterEmpty.Color, 0.88f);

        public static Color Cream => CMTokens.WarmCream.Color;
        public static Color Navy => CMTokens.VoidNavy.Color;
        public static Color Brass => CMTokens.ToolBrass.Color;
    }

    /// <summary>
    /// The one flash budget for every screen-wide pulse this lane draws (hurt vignette, low-health pulse): at most
    /// MaxPerSecond onsets in any one-second window (WCAG 2.3.1 three flashes). A pulse that would break the budget is
    /// merged into the one already showing (it holds, it does not re-flash). View only; no allocation.
    /// </summary>
    public static class PxFlashBudget
    {
        public const int MaxPerSecond = 3;
        static readonly float[] Onsets = new float[MaxPerSecond];
        static int _next;

        /// <summary>True (and the onset is counted) when a new flash may start at time now (unscaled seconds).</summary>
        public static bool TryOnset(float now)
        {
            float oldest = Onsets[_next];
            if (oldest > 0f && now - oldest < 1f) return false;
            Onsets[_next] = now <= 0f ? 1e-4f : now;
            _next = (_next + 1) % MaxPerSecond;
            return true;
        }

        /// <summary>Onsets in the last second (tests).</summary>
        public static int InLastSecond(float now)
        {
            int n = 0;
            for (int i = 0; i < MaxPerSecond; i++) if (Onsets[i] > 0f && now - Onsets[i] < 1f) n++;
            return n;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            for (int i = 0; i < MaxPerSecond; i++) Onsets[i] = 0f;
            _next = 0;
        }
    }
}

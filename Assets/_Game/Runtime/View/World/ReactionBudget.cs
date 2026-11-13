using CriticalMass.Runtime.Menu;
using UnityEngine;

namespace CriticalMass.Runtime.View.Reactions
{
    /// <summary>Feel tier: Low (Very Low, Low), Medium (Medium, High), High (Very High, Ultra) of QualitySettings.</summary>
    public enum ReactionTier : byte { Low, Medium, High }

    /// <summary>
    /// Caps of every world-reaction system per tier (physics audit seat "world reactions" findings 1, 4, 6 and 12; runtime
    /// seat section 4). Until PERF's FeelBudget (DG-81) lands this is the one table; swapping is the one line in Current.
    /// Reduced motion (cm.reducedMotion) scales every amplitude by 0.25 on top of the tier.
    /// </summary>
    public struct ReactionBudget
    {
        public ReactionTier Tier;
        /// <summary>Force texture side in texels (0 = no texture: shaders read the 16 force slots) and window side (u).</summary>
        public int ForceTexSize;
        public float ForceWindow;
        /// <summary>Walker brushes stamped per frame (fighters plus the Kaiju).</summary>
        public int Walkers;
        /// <summary>Forces per sim tick before weak ones drop (WorldForceBus.PerTickCap).</summary>
        public int PerTickCap;
        /// <summary>Ground paint side in texels and mark life in wall seconds; footprints stamped (High only).</summary>
        public int PaintSize;
        public float PaintSeconds;
        public bool Footprints;
        /// <summary>Spring bones: Kaiju always; fighters on screen (Medium) or all (High).</summary>
        public bool FighterSprings;
        public bool OffscreenSprings;
        /// <summary>Concurrent death launches (closed form, cost 0; the cap keeps the record array fixed).</summary>
        public int MaxLaunches;
        /// <summary>Water ripple rings at fighters.</summary>
        public bool Ripples;

        public static ReactionBudget For(ReactionTier t)
        {
            switch (t)
            {
                case ReactionTier.Low:
                    return new ReactionBudget
                    {
                        Tier = t, ForceTexSize = 0, ForceWindow = 0f, Walkers = 0, PerTickCap = 48, PaintSize = 512,
                        PaintSeconds = 8f, Footprints = false, FighterSprings = false, OffscreenSprings = false,
                        MaxLaunches = 32, Ripples = false,
                    };
                case ReactionTier.High:
                    return new ReactionBudget
                    {
                        Tier = t, ForceTexSize = 256, ForceWindow = 64f, Walkers = 33, PerTickCap = 128, PaintSize = 1024,
                        PaintSeconds = 40f, Footprints = true, FighterSprings = true, OffscreenSprings = true,
                        MaxLaunches = 32, Ripples = true,
                    };
                default:
                    return new ReactionBudget
                    {
                        Tier = ReactionTier.Medium, ForceTexSize = 128, ForceWindow = 48f, Walkers = 33, PerTickCap = 96,
                        PaintSize = 1024, PaintSeconds = 20f, Footprints = false, FighterSprings = true,
                        OffscreenSprings = false, MaxLaunches = 32, Ripples = true,
                    };
            }
        }

        /// <summary>Tests and captures force a tier here (null = from the quality level).</summary>
        public static ReactionTier? Override;

        public static ReactionTier FromQualityLevel(int level) =>
            level <= 1 ? ReactionTier.Low : level <= 3 ? ReactionTier.Medium : ReactionTier.High;

        public static ReactionTier CurrentTier => Override ?? FromQualityLevel(QualitySettings.GetQualityLevel());

        /// <summary>The live budget (the one line to point at FeelBudget when DG-81 merges).</summary>
        public static ReactionBudget Current => For(CurrentTier);

        /// <summary>Amplitude of every motion system: 0.25 with reduced motion on, else 1.</summary>
        public static float MotionScale => GameSettings.ReducedMotion ? 0.25f : 1f;
    }
}

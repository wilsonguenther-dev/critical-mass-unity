using System;
using CriticalMass.Runtime.Menu;

namespace CriticalMass.Runtime.View.Moments
{
    /// <summary>What one big-moment recipe flashes under a look (pure; PhotosensitivityTests print one line per recipe).</summary>
    public struct FlashMeasure
    {
        /// <summary>Flash starts one play of the recipe can make (white flash, invert frame): 0, 1 or 2.</summary>
        public int Starts;
        /// <summary>Share of the screen the largest flash covers (1 = the whole screen).</summary>
        public double ScreenShare;
        /// <summary>Worst-case change in relative luminance of that flash (0..1): the white overlay's alpha over a black
        /// picture, 1 for an invert frame.</summary>
        public double LuminanceStep;
        /// <summary>The flash is a "general flash" in the WCAG sense (luminance step of 10% or more over more than 25% of a
        /// 10 degree field) and so counts against the three-a-second limit.</summary>
        public bool CountsAsFlash;
        /// <summary>The recipe's slowest repeating pulse on screen (Hz; the edge glow), 0 when it has none.</summary>
        public double PulseHz;
        public bool Invert;
    }

    /// <summary>
    /// b15 safe defaults (blind-spot audit C1, owner-approved 2026-11-17): the photosensitivity rules every big flash obeys.
    /// <para>Source, WCAG 2.1 Success Criterion 2.3.1 "Three Flashes or Below Threshold" (Level A),
    /// https://www.w3.org/WAI/WCAG21/Understanding/three-flashes-or-below-threshold (read 2026-11-18): "Web pages do not
    /// contain anything that flashes more than three times in any one second period, or the flash is below the general flash
    /// and red flash thresholds." A general flash is "a pair of opposing changes in relative luminance of 10% or more of the
    /// maximum relative luminance (1.0) where the relative luminance of the darker image is below 0.80"; it is below the
    /// area threshold when "the combined area of flashes occurring concurrently occupies no more than a total of .006
    /// steradians within any 10 degree visual field on the screen (25% of any 10 degree visual field on the screen)", and "a
    /// 341 x 256 pixel rectangle anywhere on the displayed screen area when the content is viewed at 1024 x 768 pixels will
    /// provide a good estimate of a 10 degree visual field". Xbox Accessibility Guideline 118 asks the same of games.</para>
    /// <para>The rule here: by default (Settings > Access > INTENSE FLASHING off) every full-screen or edge flash the game
    /// starts (big-moment white flash, invert frame, impact edges) asks one SHARED budget, so all of them together start at
    /// most three in any one second; the invert frame never plays; the white flash is a short soft pulse (MomentLook,
    /// alpha 0.22 over 0.3 s). INTENSE FLASHING on is the opt-in old look (each system still keeps its own 3-a-second cap).
    /// View only: nothing here reaches the sim.</para>
    /// </summary>
    public static class FlashSafety
    {
        /// <summary>WCAG 2.3.1: no more than three flashes in any one-second period.</summary>
        public const int MaxFlashesPerSecond = 3;
        /// <summary>WCAG 2.3.1: a general flash changes relative luminance by 10% or more.</summary>
        public const double LuminanceStepLimit = 0.10;
        /// <summary>A 10 degree visual field as a share of the screen (341 x 256 px of 1024 x 768).</summary>
        public const double TenDegreeFieldShare = 341.0 * 256.0 / (1024.0 * 768.0);
        /// <summary>WCAG 2.3.1: flashes over at most 25% of any 10 degree field are below the area threshold.</summary>
        public const double AreaThresholdShare = 0.25 * TenDegreeFieldShare;
        /// <summary>The brightest a default white flash may be (overlay alpha): a pulse, not a white-out.</summary>
        public const float SafeFlashAlpha = 0.25f;
        /// <summary>A default flash takes at least this long to fade (a soft pulse rather than a strobe edge).</summary>
        public const float SafeFlashSeconds = 0.25f;
        /// <summary>The edge glow's breathing rate (BigMoments: 1.5 Hz, amplitude 0.08).</summary>
        public const double EdgeGlowHz = 1.5;
        /// <summary>Share of the screen one impact edge covers (CameraResponse: 16% deep).</summary>
        public const double ImpactEdgeShare = 0.16;

        /// <summary>Tests may pin "intense flashing allowed" (null = the player's setting).</summary>
        public static bool? IntenseOverride;

        /// <summary>The player opted into intense flashing (default off).</summary>
        public static bool IntenseAllowed => IntenseOverride ?? !GameSettings.ReduceFlashing;

        static readonly FlashLimiter Shared = new FlashLimiter();

        /// <summary>Forget the shared budget's history (tests; a new limiter allows three at once).</summary>
        public static void ResetShared() => Shared.Reset();

        /// <summary>True (and counted) when a full-screen or edge flash may start at wall time now (seconds, any monotonic
        /// clock shared by every caller). Always true with intense flashing allowed.</summary>
        public static bool TryShared(double now) => IntenseAllowed || Shared.TryFlash(now);

        /// <summary>
        /// The big-moment director's flash decision for one takeover start: the white flash and the invert frame each need
        /// the director's own limiter (3 a second, as before) and, by default, the shared budget. The invert also needs the
        /// look to allow it and a frozen picture to invert.
        /// </summary>
        public static void DecideMoment(MomentLayers layers, MomentLook look, FlashLimiter own, double clock, double wall,
            bool frozenPicture, out bool flash, out bool invert)
        {
            flash = (layers & MomentLayers.Flash) != 0 && own.TryFlash(clock) && TryShared(wall);
            invert = (layers & MomentLayers.Invert) != 0 && frozenPicture && look.Invert && own.TryFlash(clock) && TryShared(wall);
        }

        /// <summary>The impact-edge decision (CameraResponse): its own minimum gap, then the shared budget.</summary>
        public static bool EdgeFlashAllowed(double now, double lastFlashAt, double minGap) =>
            now - lastFlashAt >= minGap && TryShared(now);

        /// <summary>What one recipe flashes under a look (worst case: a black picture under the white overlay).</summary>
        public static FlashMeasure Measure(MomentRecipe r, MomentLook look)
        {
            var m = new FlashMeasure();
            if (r == null) return m;
            bool takeover = r.Scale == MomentScale.Takeover;
            if (takeover && (r.Layers & MomentLayers.Flash) != 0)
            {
                m.Starts++;
                m.ScreenShare = 1.0;
                m.LuminanceStep = Math.Max(m.LuminanceStep, look.FlashAlpha);
            }
            if (takeover && (r.Layers & MomentLayers.Invert) != 0 && look.Invert)
            {
                m.Starts++;
                m.ScreenShare = 1.0;
                m.LuminanceStep = 1.0;
                m.Invert = true;
            }
            if (takeover && (r.Layers & MomentLayers.EdgeGlow) != 0 && look.Motion) m.PulseHz = EdgeGlowHz;
            m.CountsAsFlash = m.Starts > 0 && m.LuminanceStep >= LuminanceStepLimit && m.ScreenShare > AreaThresholdShare;
            return m;
        }
    }
}

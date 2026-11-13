using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Style
{
    /// <summary>
    /// The one type scale, spacing scale and button sizes every screen built in code uses (menus, HUD panels, results),
    /// in 1920x1080 reference px (the canvases scale by min(w/1920, h/1080), so 1280x720 and 2560x1440 keep the layout).
    /// Added 2026-11-12 (UI rough spots) so screens stop picking sizes one by one; neighbouring files move onto it as they
    /// are touched. Text that can grow (class names, words-pack lines) goes through <see cref="FitsOneLine"/> in the
    /// EditMode layout tests and MenuUi.Fit at runtime, so nothing clips at any resolution or UI scale.
    /// </summary>
    public static class UiScale
    {
        // ---- type scale (ratio about 1.25, rounded to even px) ----
        /// <summary>Results headline (VICTORY / DEFEAT).</summary>
        public const int TypeHero = 104;
        /// <summary>Screen titles (PAUSED, MATCH SETUP, HIGHLIGHTS).</summary>
        public const int TypeTitle = 64;
        /// <summary>Card and panel headings.</summary>
        public const int TypeHeading = 30;
        /// <summary>Button labels (Display face).</summary>
        public const int TypeButton = 28;
        /// <summary>Body lines and stat rows.</summary>
        public const int TypeBody = 22;
        /// <summary>Secondary lines, key hints and tags.</summary>
        public const int TypeSmall = 18;
        /// <summary>The floor: nothing is drawn smaller than this at 1080p (11 px at 720p after scaling).</summary>
        public const int TypeMin = 16;

        // ---- spacing scale (4 px grid) ----
        public const float S1 = 4f, S2 = 8f, S3 = 12f, S4 = 16f, S5 = 24f, S6 = 32f, S7 = 48f, S8 = 64f;

        /// <summary>Title-safe margin on every edge (5 % of 1080 rounded to the grid): nothing focusable or read sits outside.</summary>
        public const float SafeMargin = 48f;
        /// <summary>Height of the solid band menus draw over the HUD's score and clock strip.</summary>
        public const float TitleBandH = 136f;

        // ---- the one button style ----
        public const float ButtonH = 72f, ButtonRowH = 84f, ListItemH = 58f, ButtonGap = 16f;
        public const float ButtonRadius = 16f;
        /// <summary>Pressed buttons shrink to this scale for one press (view only, under 100 ms).</summary>
        public const float PressedScale = 0.97f;

        /// <summary>The largest access UI scale (SettingsExtras.UiScaleOptions max) the layout tests check against.</summary>
        public const float LargestUserScale = 1.3f;

        /// <summary>The two resolutions every layout test checks (and the integrator captures).</summary>
        public static readonly Vector2Int[] CheckResolutions = { new Vector2Int(1280, 720), new Vector2Int(2560, 1440) };

        /// <summary>
        /// Width in reference px of <paramref name="text"/> in <paramref name="font"/> at <paramref name="size"/> on one line
        /// (TextGenerator, the same measure uGUI uses). Returns 0 when the font cannot be loaded.
        /// </summary>
        public static float PreferredWidth(Font font, string text, int size)
        {
            if (font == null || string.IsNullOrEmpty(text)) return 0f;
            var settings = new TextGenerationSettings
            {
                font = font,
                fontSize = size,
                fontStyle = FontStyle.Normal,
                scaleFactor = 1f,
                lineSpacing = 1f,
                richText = false,
                textAnchor = TextAnchor.MiddleLeft,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(10000f, 1000f),
                pivot = new Vector2(0.5f, 0.5f),
                updateBounds = true,
                color = Color.white,
                generateOutOfBounds = true,
            };
            var gen = new TextGenerator();
            return gen.GetPreferredWidth(text, settings);
        }

        /// <summary>
        /// The size best-fit settles on for one line of <paramref name="text"/> in a rect <paramref name="width"/> wide
        /// (outline included), from <paramref name="max"/> down to <paramref name="min"/>; -1 when even min does not fit.
        /// </summary>
        public static int FitSize(Font font, string text, int max, int min, float width, float outlinePx = 3f)
        {
            float room = width - outlinePx * 2f;
            for (int s = max; s >= min; s--)
                if (PreferredWidth(font, text, s) <= room) return s;
            return -1;
        }

        /// <summary>
        /// True when <paramref name="text"/> wrapped to <paramref name="width"/> fits <paramref name="height"/> at some size
        /// between min and max (what MenuUi.Fit's best-fit settles on at runtime).
        /// </summary>
        public static bool FitsWrapped(Font font, string text, int max, int min, float width, float height, float outlinePx = 2f)
        {
            if (font == null || string.IsNullOrEmpty(text)) return true;
            var gen = new TextGenerator();
            for (int s = max; s >= min; s--)
            {
                var settings = new TextGenerationSettings
                {
                    font = font, fontSize = s, fontStyle = FontStyle.Normal, scaleFactor = 1f, lineSpacing = 1f, richText = false,
                    textAnchor = TextAnchor.UpperLeft, horizontalOverflow = HorizontalWrapMode.Wrap,
                    verticalOverflow = VerticalWrapMode.Overflow, generationExtents = new Vector2(width - outlinePx * 2f, 10000f),
                    pivot = new Vector2(0.5f, 0.5f), updateBounds = true, color = Color.white, generateOutOfBounds = true,
                };
                if (gen.GetPreferredHeight(text, settings) <= height - outlinePx * 2f) return true;
            }
            return false;
        }

        /// <summary>True when one line fits at some size between min and max.</summary>
        public static bool FitsOneLine(Font font, string text, int max, int min, float width, float outlinePx = 3f) =>
            FitSize(font, text, max, min, width, outlinePx) > 0;
    }
}

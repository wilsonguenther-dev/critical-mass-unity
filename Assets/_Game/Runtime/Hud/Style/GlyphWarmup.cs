using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Style
{
    /// <summary>
    /// perf/fullscreen-cost-1: the HUD uses dynamic fonts (UI Text), which draw a letter into the font's texture the first
    /// time it is needed at a given pixel size and style. When the texture grows, every text that uses the font builds
    /// its mesh again. The playthrough walk saw this as a 70 ms canvas frame about 11 s into a match and 41 ms of canvas
    /// work in the 449 ms start frame. This asks each font, once per session for each (font, pixel size, style), for the
    /// printable ASCII set, at moments nobody watches the frame rate: while the title is idle and behind the loading screen.
    /// It warms every Text in the loaded scenes, shown or hidden. Visual only; it allocates only while it runs.
    /// </summary>
    public static class GlyphWarmup
    {
        public const string Chars =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~×·";

        static readonly HashSet<long> Done = new HashSet<long>();

        /// <summary>Combinations warmed so far this session.</summary>
        public static int Warmed => Done.Count;
        /// <summary>True once the idle title has had its pass.</summary>
        public static bool TitleDone { get; private set; }

        /// <summary>The cache key of one (font, pixel size, style) combination (pure; tests).</summary>
        public static long Key(int fontId, int pixelSize, FontStyle style) =>
            ((long)fontId << 20) ^ ((long)(pixelSize & 0xFFFF) << 4) ^ (long)style;

        /// <summary>The pixel size a Text draws at: its font size times the canvas scale; best-fit texts at their largest.</summary>
        public static int PixelSize(int fontSize, int bestFitMax, bool bestFit, float pixelsPerUnit)
        {
            int size = bestFit && bestFitMax > 0 ? bestFitMax : fontSize;
            float ppu = pixelsPerUnit > 0.01f && !float.IsNaN(pixelsPerUnit) ? pixelsPerUnit : 1f;
            return Mathf.Clamp(Mathf.RoundToInt(size * ppu), 1, 512);
        }

        /// <summary>Warms every Text in the loaded scenes; returns how many new combinations it asked for.</summary>
        public static int WarmScene(bool fromTitle = false)
        {
            if (fromTitle) TitleDone = true;
            Text[] texts = Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int n = 0;
            for (int i = 0; i < texts.Length; i++)
            {
                Text t = texts[i];
                if (t == null) continue;
                Font f = t.font;
                if (f == null || !f.dynamic) continue;
                int px = PixelSize(t.fontSize, t.resizeTextMaxSize, t.resizeTextForBestFit, t.pixelsPerUnit);
                if (!Done.Add(Key(f.GetInstanceID(), px, t.fontStyle))) continue;
                f.RequestCharactersInTexture(Chars, px, t.fontStyle);
                n++;
            }
            if (n > 0) Debug.Log($"[CM.Glyphs] warmed {n} font sizes ({Done.Count} this session){(fromTitle ? " at the title" : "")}");
            return n;
        }
    }
}

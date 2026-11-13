using UnityEngine;

namespace CriticalMass.Runtime.Hud.Style
{
    /// <summary>
    /// Polish pass 1: WCAG 2.x contrast of the colour constants, so "text on every panel reads at 4.5 : 1" is a number the
    /// tests hold, not a guess. <see cref="TextPairs"/> lists every text colour the screens draw on every panel colour.
    /// Disabled text (Stone at half alpha on a dimmed item) is exempt, as WCAG exempts inactive controls; it is listed in
    /// <see cref="ExemptPairs"/> so the exemption is explicit.
    /// </summary>
    public static class UiContrast
    {
        public const float TextMinimum = 4.5f;

        /// <summary>Relative luminance of an sRGB colour (alpha ignored).</summary>
        public static float Luminance(Color c)
        {
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        static float Lin(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        /// <summary>Contrast ratio 1..21 of two colours.</summary>
        public static float Ratio(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            if (la < lb) { float t = la; la = lb; lb = t; }
            return (la + 0.05f) / (lb + 0.05f);
        }

        /// <summary><paramref name="fg"/> at its alpha composited over an opaque <paramref name="bg"/>.</summary>
        public static Color Over(Color fg, Color bg)
        {
            float a = fg.a;
            return new Color(fg.r * a + bg.r * (1f - a), fg.g * a + bg.g * (1f - a), fg.b * a + bg.b * (1f - a), 1f);
        }

        /// <summary>The lockout warning's plate (HumanControlGuard).</summary>
        public static readonly Color WarningRed = new Color(0.62f, 0.08f, 0.08f, 1f);

        /// <summary>A text colour on a background, with where it is used.</summary>
        public readonly struct Pair
        {
            public readonly string Name;
            public readonly Color Text, Back;
            public Pair(string name, Color text, Color back) { Name = name; Text = text; Back = back; }
            public float Ratio => UiContrast.Ratio(Over(Text, Back), Back);
        }

        public static Pair[] TextPairs => new[]
        {
            new Pair("cream on panel", CMTokens.WarmCream.Color, CMTokens.PanelNavy.Color),
            new Pair("cream on navy", CMTokens.WarmCream.Color, CMTokens.VoidNavy.Color),
            new Pair("cream 80% on panel (scoreboard headings)", CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.8f), CMTokens.PanelNavy.Color),
            new Pair("brass on panel (tags, counts)", CMTokens.ToolBrass.Color, CMTokens.PanelNavy.Color),
            new Pair("brass on navy", CMTokens.ToolBrass.Color, CMTokens.VoidNavy.Color),
            new Pair("navy on brass (focused item)", CMTokens.VoidNavy.Color, CMTokens.ToolBrass.Color),
            new Pair("azure on panel (team text)", CMTokens.Azure.Color, CMTokens.PanelNavy.Color),
            new Pair("crimson ink on panel (team text)", CMTokens.CrimsonInk.Color, CMTokens.PanelNavy.Color),
            new Pair("crimson ink on navy", CMTokens.CrimsonInk.Color, CMTokens.VoidNavy.Color),
            new Pair("stone ink on panel (notes, empty states)", CMTokens.StoneInk.Color, CMTokens.PanelNavy.Color),
            new Pair("egg seam on panel (hatch callout)", CMTokens.EggSeam.Color, CMTokens.PanelNavy.Color),
            new Pair("meter orange on panel", CMTokens.MeterOrange.Color, CMTokens.PanelNavy.Color),
            new Pair("biomass on panel", CMTokens.Biomass.Color, CMTokens.PanelNavy.Color),
            new Pair("cream on the lockout warning", CMTokens.WarmCream.Color, WarningRed),
        };

        public static Pair[] ExemptPairs => new[]
        {
            new Pair("disabled item text (stone)", CMTokens.WithAlpha(CMTokens.Stone.Color, 0.9f), CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.5f)),
            new Pair("bible crimson as text (use CrimsonInk)", CMTokens.Crimson.Color, CMTokens.PanelNavy.Color),
        };
    }
}

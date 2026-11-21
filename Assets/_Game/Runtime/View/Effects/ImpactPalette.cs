using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// b15 safe defaults (blind-spot audit P5 / A2, owner-approved 2026-11-17): the colours a hit, a knock-out puff or an
    /// impact may use, so team colour stays reserved for teams (bible: "never colour alone"; team hues are Azure #16C7D9,
    /// Crimson #FF5A5F and the colour-blind pair #3D9BFF / #FFB000). Two families:
    /// <list type="bullet">
    /// <item>NEUTRAL (every gore level, and the whole look at the default GORE Off): pale warm-white and cream-yellow sparks
    /// and dust. Every one is low in saturation (under <see cref="NeutralMaxSaturation"/>), so none reads as a team.</item>
    /// <item>FULL GORE (opt-in only): a dark violet-brown instead of a saturated red, at least <see cref="MinTeamHueGapDeg"/>
    /// degrees of hue from every team colour, and never "blood red" or "Crimson-like" by GoreShots' own measure.</item>
    /// </list>
    /// For the hit-effect owner (feat/b15-combat-feel, FxScene): new hit sparks and impact dust take these constants;
    /// FxScene.BloodLight / BloodMid / BloodDark should become GoreLight / GoreMid / GoreDark (routed patch in the b15 safety
    /// PR). View only: nothing here reaches the sim. ImpactPaletteTests hold every colour to the rules.
    /// </summary>
    public static class ImpactPalette
    {
        /// <summary>Hot core of a hit spark (near white).</summary>
        public static readonly Color32 SparkHot = new Color32(255, 250, 236, 255);
        /// <summary>Body of a hit spark (pale cream-yellow).</summary>
        public static readonly Color32 SparkMid = new Color32(255, 240, 200, 255);
        /// <summary>Cooling edge of a spark (warm sand).</summary>
        public static readonly Color32 SparkEdge = new Color32(238, 222, 184, 255);
        /// <summary>Impact dust, light and dark tone.</summary>
        public static readonly Color32 DustLight = new Color32(200, 190, 172, 255), DustDark = new Color32(128, 120, 108, 255);

        /// <summary>FULL gore only (opt-in): dark violet-brown in three tone steps (light, mid, dark).</summary>
        public static readonly Color32 GoreLight = new Color32(128, 62, 100, 255), GoreMid = new Color32(96, 44, 74, 255),
            GoreDark = new Color32(62, 28, 48, 255);

        public static readonly Color32[] Neutral = { SparkHot, SparkMid, SparkEdge, DustLight, DustDark };
        public static readonly Color32[] FullGore = { GoreLight, GoreMid, GoreDark };

        /// <summary>A neutral colour stays under this HSV saturation (it reads as white, cream or grey, not a hue).</summary>
        public const double NeutralMaxSaturation = 0.25;
        /// <summary>A saturated impact colour keeps at least this much hue distance from every team colour.</summary>
        public const double MinTeamHueGapDeg = 30;

        /// <summary>The four team colours (normal and colour-blind pairs) the impact colours keep away from.</summary>
        public static readonly Color32[] TeamHues =
        {
            new Color32(0x16, 0xC7, 0xD9, 255), new Color32(0xFF, 0x5A, 0x5F, 255),
            new Color32(0x3D, 0x9B, 0xFF, 255), new Color32(0xFF, 0xB0, 0x00, 255),
        };

        /// <summary>Shortest distance between two hues in degrees (0..180).</summary>
        public static double HueGap(double a, double b)
        {
            double d = System.Math.Abs(a - b) % 360.0;
            return d > 180.0 ? 360.0 - d : d;
        }

        /// <summary>The smallest hue gap from c to any team colour (degrees).</summary>
        public static double NearestTeamHueGap(Color32 c)
        {
            GoreShots.Hsv(c, out double h, out _, out _);
            double best = 180;
            for (int i = 0; i < TeamHues.Length; i++)
            {
                GoreShots.Hsv(TeamHues[i], out double th, out _, out _);
                best = System.Math.Min(best, HueGap(h, th));
            }
            return best;
        }
    }

    /// <summary>
    /// b15 view polish (Editor review item 3): the fighter wind-up wedge's look (pure; FxScene.DrawSwingTelegraphs and a
    /// test read it). Neutral colours only (ImpactPalette), so a wedge never reads as a team. Quiet tier for every swing,
    /// a brighter tier when the local fighter is inside an enemy's wedge. With reduced flashing the bright edges are capped.
    /// </summary>
    public struct SwingTelegraph
    {
        /// <summary>Wedge inner radius (u) and the widths of the leading edge, the outer ink and the outer bright edge.</summary>
        public const float Inner = 0.15f, LeadW = 0.08f, InkW = 0.14f, EdgeW = 0.07f;
        /// <summary>Reduced flashing: no bright edge above this alpha.</summary>
        public const float ReducedEdgeCap = 0.6f;

        public Color32 Fill, Grow, Lead;
        public float BaseIn, BaseOut, GrowIn, GrowOut, LeadA, InkA, EdgeA;

        public static SwingTelegraph For(bool atMe, bool reduceFlashing)
        {
            float k = atMe ? 1f : 0.8f;
            var t = new SwingTelegraph
            {
                Fill = ImpactPalette.SparkEdge, Grow = ImpactPalette.SparkMid, Lead = ImpactPalette.SparkHot,
                BaseIn = 0.20f * k, BaseOut = 0.34f * k, GrowIn = 0.30f * k, GrowOut = 0.52f * k,
                LeadA = 0.95f * k, InkA = 0.75f * k, EdgeA = 0.92f * k,
            };
            if (reduceFlashing)
            {
                t.LeadA = Mathf.Min(t.LeadA, ReducedEdgeCap);
                t.EdgeA = Mathf.Min(t.EdgeA, ReducedEdgeCap);
            }
            return t;
        }
    }
}

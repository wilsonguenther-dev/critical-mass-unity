using System.IO;
using CriticalMass.Runtime.View.Effects;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 view polish, Editor review item 3: the fighter wind-up wedge was invisible at the gameplay camera (team-tinted
    /// fill at alpha 0.07..0.16 x 0.55). Pins the new look: neutral colours, at least three times the old opacity, a
    /// bright leading edge, the reduced-flashing cap, and the draw order (over the decals, under the characters).
    /// </summary>
    public sealed class SwingTelegraphLookTests
    {
        static float Sat(Color32 c)
        {
            Color.RGBToHSV(c, out _, out float s, out _);
            return s;
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        [Test]
        public void TheWedgeIsNeutralAndReadable()
        {
            foreach (bool atMe in new[] { false, true })
            {
                SwingTelegraph t = SwingTelegraph.For(atMe, false);
                foreach (Color32 c in new[] { t.Fill, t.Grow, t.Lead })
                {
                    Debug.Log($"[b15 wedge] colour {c}: saturation {Sat(c):0.000} (limit <= {ImpactPalette.NeutralMaxSaturation})");
                    Assert.LessOrEqual(Sat(c), (float)ImpactPalette.NeutralMaxSaturation, "a wedge never reads as a team");
                }
                // old quiet tier: base out 0.16 x 0.55 = 0.088, grow out 0.30 x 0.55 = 0.165
                float oldBase = atMe ? 0.16f : 0.088f, oldGrow = atMe ? 0.30f : 0.165f;
                Debug.Log($"[b15 wedge] atMe {atMe}: base {t.BaseOut:0.00} (old {oldBase:0.000}), grow {t.GrowOut:0.00} (old {oldGrow:0.000}), lead {t.LeadA:0.00}, edge {t.EdgeA:0.00}");
                if (!atMe)
                {
                    Assert.GreaterOrEqual(t.BaseOut, 3f * oldBase, "quiet fill at least 3x the old opacity");
                    Assert.GreaterOrEqual(t.GrowOut, 2.5f * oldGrow, "growing fill well above the old");
                }
                Assert.Greater(t.LeadA, t.GrowOut, "the leading edge is brighter than the fill");
                Assert.Greater(Luma(t.Lead), Luma(t.Fill), "the leading edge is the lightest tone");
                Assert.GreaterOrEqual(t.InkA, 0.6f, "the outer ink separates the edge from light paving");
            }
            Assert.Less(SwingTelegraph.Inner, 0.3f, "the wedge starts closer to the body (was 0.30 u)");
            Assert.Greater(SwingTelegraph.InkW, 0.07f, "thicker outer rim (was 0.07..0.09 u)");
        }

        [Test]
        public void ReducedFlashingCapsTheBrightEdges()
        {
            foreach (bool atMe in new[] { false, true })
            {
                SwingTelegraph t = SwingTelegraph.For(atMe, true);
                Assert.LessOrEqual(t.LeadA, SwingTelegraph.ReducedEdgeCap);
                Assert.LessOrEqual(t.EdgeA, SwingTelegraph.ReducedEdgeCap);
                Assert.Greater(t.LeadA, t.GrowOut * 0.9f, "still a visible edge");
            }
        }

        [Test]
        public void TheWedgeDrawsOverDecalsAndUnderCharacters()
        {
            string dir = Path.Combine(Application.dataPath, "_Game", "Art", "Shaders");
            StringAssert.Contains("\"Queue\" = \"Transparent-50\"", File.ReadAllText(Path.Combine(dir, "CMWorldDecal.shader")));
            Debug.Log($"[b15 wedge] ground fx queue {CombatFx.GroundFxQueue} (limit > 2950 decals, < 3000)");
            Assert.Greater(CombatFx.GroundFxQueue, 2950, "after the world decals");
            Assert.Less(CombatFx.GroundFxQueue, 3000, "in the ground band, before the air fx and the halo");
            string fx = File.ReadAllText(Path.Combine(Application.dataPath, "_Game", "Runtime", "View", "Effects", "FxScene.cs"));
            int a = fx.IndexOf("private void DrawSwingTelegraphs(", System.StringComparison.Ordinal);
            int b = fx.IndexOf("private double PaceNow", a, System.StringComparison.Ordinal);
            Assert.Greater(a, 0);
            string body = fx.Substring(a, b - a);
            StringAssert.DoesNotContain("AzureC", body, "no team colour in the wedge");
            StringAssert.Contains("SwingTelegraph.For(atMe, FeelGate.ReduceFlashing)", body);
        }
    }
}

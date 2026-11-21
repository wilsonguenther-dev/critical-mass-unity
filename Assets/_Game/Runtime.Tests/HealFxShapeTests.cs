using System.IO;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Mechanics;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 view polish, Editor review item 7: the heal FX against the bible row (soft mint motes rising, a ring on the
    /// ground, no hard quads). The old sparkle was a plus sign of two flat quads; the glow and the pulse fill were hard
    /// discs. Every case PRINTS what, value, limit.
    /// </summary>
    public sealed class HealFxShapeTests
    {
        [Test]
        public void MotesRiseAndFadeSoftly()
        {
            for (int m = 0; m < HealFx.Motes; m++)
            {
                float y0 = HealFx.MoteOffset(m, 0.05f).y, y1 = HealFx.MoteOffset(m, 0.95f).y;
                Debug.Log($"[b15 heal] mote {m}: y {y0:0.00} -> {y1:0.00} (limit rises >= 0.5 u), alpha end {HealFx.MoteAlpha(m, 0.999f):0.000}");
                Assert.Greater(y1 - y0, 0.5f, "motes rise");
                Assert.AreEqual(0f, HealFx.MoteAlpha(m, 1f), "gone at the end of the life");
                Assert.Less(HealFx.MoteAlpha(m, 0.999f), 0.05f, "faded out, no pop");
                Assert.Less(HealFx.MoteAlpha(m, m * 0.12f + 0.01f), 0.2f, "faded in, no pop");
            }
            Assert.GreaterOrEqual(HealFx.Motes, 3, "a few motes, not one sprite");
            Assert.Less(HealFx.MoteRadius(0.5f, 1f), 0.3f, "motes stay small");
        }

        [Test]
        public void AMoteHasNoHardEdge()
        {
            var b = new FxMeshBuilder("heal mote test", 256, 768);
            try
            {
                var c = new Color32(95, 224, 122, 230);
                b.SoftMote(Vector3.zero, Vector3.right, Vector3.up, 0.2f, HealFx.MoteSegments, c);
                int outerZero = 0, solid = 0;
                for (int i = 0; i < b.VertCount; i++)
                {
                    Color32 v = b.ColorAt(i);
                    if (v.a == 0) outerZero++;
                    else if (v.a == c.a) solid++;
                }
                Debug.Log($"[b15 heal] soft mote: {b.VertCount} verts, {outerZero} at alpha 0 on the rim, {solid} solid (limit rim >= {HealFx.MoteSegments})");
                Assert.GreaterOrEqual(outerZero, HealFx.MoteSegments, "the rim fades to alpha 0 all round");
                Assert.Greater(solid, 0, "a solid core");
                Assert.AreEqual(0, b.Dropped);
            }
            finally
            {
                Object.DestroyImmediate(b.Mesh);
            }
        }

        [Test]
        public void TheHealColourIsMint()
        {
            Color.RGBToHSV(MechanicsWorldView.HealMint, out float h, out float s, out float v);
            Debug.Log($"[b15 heal] mint hue {h * 360f:0} (limit 120..150), sat {s:0.00}, value {v:0.00}");
            Assert.That(h * 360f, Is.InRange(120f, 150f));
            Assert.AreEqual(0f, HealFx.RingInnerAlpha, "the ground ring fades in from its inner edge");
        }

        [Test]
        public void NoHardQuadsInTheHealDraws()
        {
            string rt = Path.Combine(Application.dataPath, "_Game", "Runtime", "View");
            string mech = File.ReadAllText(Path.Combine(rt, "Mechanics", "MechanicsWorldView.cs"));
            int a = mech.IndexOf("// heal motes (b15 view polish, item 7)", System.StringComparison.Ordinal);
            int z = mech.IndexOf("// wind-up glints", a, System.StringComparison.Ordinal);
            Assert.Greater(a, 0, "the heal motes block");
            string motes = mech.Substring(a, z - a);
            StringAssert.Contains("SoftMote(", motes);
            StringAssert.DoesNotContain(".Quad(", motes, "no flat quads in the heal motes");
            StringAssert.DoesNotContain("heal sparkles (0.6 s): a plus sign", mech, "the plus-sign sparkle is gone");
            string skill = File.ReadAllText(Path.Combine(rt, "Skills", "SkillFxCore.cs"));
            int g = skill.IndexOf("void DrawHealGlows(", System.StringComparison.Ordinal);
            int ge = skill.IndexOf("void DrawLifts(", g, System.StringComparison.Ordinal);
            string glow = skill.Substring(g, ge - g);
            StringAssert.Contains("SoftMote(", glow);
            StringAssert.DoesNotContain(".Blob(", glow, "no hard disc in the heal glow");
            StringAssert.Contains("healMotes=", File.ReadAllText(Path.Combine(rt, "Skills", "SkillCapture.cs")), "the skill capture logs the motes per shot");
        }
    }
}

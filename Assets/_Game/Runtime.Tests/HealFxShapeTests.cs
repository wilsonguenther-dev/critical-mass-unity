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
        }

        /// <summary>r2 (Editor review round 2): the round-1 motes (4 x 0.11..0.16 u, alpha 0.9) were lost at the gameplay
        /// camera in a fight. Fewer, larger, brighter, orbiting outside the torso, plus a ground ring pulse.</summary>
        [Test]
        public void MotesReadAtGameplayDistanceWithoutCoveringTheFighter()
        {
            float r = HealFx.MoteRadius(0.1f, 0f), rMax = HealFx.MoteRadius(0.5f, 1f);
            Debug.Log($"[b15 heal r2] motes {HealFx.Motes} (limit <= 3), radius {r:0.00}..{rMax:0.00} (limit 0.18..0.40, was 0.11..0.16), peak alpha {HealFx.PeakAlpha:0.00} (limit >= 0.95), orbit {HealFx.OrbitStart:0.00}..{HealFx.OrbitEnd:0.00} (limit start >= 0.40: outside the torso)");
            Assert.LessOrEqual(HealFx.Motes, 3, "fewer");
            Assert.GreaterOrEqual(r, 0.18f, "larger");
            Assert.LessOrEqual(rMax, 0.40f, "never a blob over the fighter");
            Assert.GreaterOrEqual(HealFx.PeakAlpha, 0.95f, "brighter");
            Assert.GreaterOrEqual(HealFx.OrbitStart, 0.40f, "they orbit outside the body");
            HealFx.RingPulse(0.05f, out float r0, out float a0);
            HealFx.RingPulse(HealFx.RingLife * 0.5f, out float r1, out float a1);
            HealFx.RingPulse(HealFx.RingLife, out _, out float aEnd);
            Debug.Log($"[b15 heal r2] ground ring: r {r0:0.00} -> {r1:0.00} (limit grows past 0.8 u), alpha {a0:0.00} -> {a1:0.00} -> {aEnd:0.00}");
            Assert.Greater(a0, 0.8f, "the ring pulse is visible");
            Assert.Greater(r1, 0.8f, "it spreads past the fighter's feet");
            Assert.AreEqual(0f, aEnd);
            string mech = File.ReadAllText(Path.Combine(Application.dataPath, "_Game", "Runtime", "View", "Mechanics", "MechanicsWorldView.cs"));
            StringAssert.Contains("HealFx.RingPulse(age, out float rr, out float ra);", mech);
            StringAssert.Contains("SwingTelegraph.SurfaceLift(", mech, "the ring sits on the walking surface (over the hill paving)");
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

using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Atmosphere;
using CriticalMass.Runtime.View.B13;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 view polish, Editor review item 1: the Kaiju read dark on the hill. The reviewer's in-Editor value test
    /// (cm-incidents/b15-editor-review/tools/kval.py: body luma, mask eroded 3 px, minus the ground in a 12 px ring)
    /// read body minus ground -0.19..0.00 at Day/Dusk (bar +0.12) and +0.01..+0.09 at Night (bar +0.15) on the adult
    /// parts model after f6a10529. Root cause: the Kaiju had no rim, so CM/Toon never gave it the character lift
    /// (charW = rim above 0): it took the full world grade, the night key included.
    ///
    /// This is a capture-free projection, not the capture: each measured value is carried to the new look by the ratio
    /// of a CPU model of the body before / after (the CM/Toon bands, CMTodCharacterBand / Gain, CMTodGrade; palette
    /// swatches of Kaiju/_parts~/scripts/build_kaiju.py in rough screen shares), and the measured ground is darkened by
    /// the contact shadow's mean alpha over the ring band (0.8..1.2 halo radii). Every case PRINTS what, value, limit.
    /// The in-engine judge stays the reviewer's capture; this keeps the numbers from drifting back.
    /// </summary>
    public sealed class KaijuHillValueTests
    {
        static readonly KaijuType[] Types = { KaijuType.Elder, KaijuType.Grove, KaijuType.Brute, KaijuType.Timber, KaijuType.Mire };
        static readonly TimeOfDayPreset[] Presets = { TimeOfDayPresets.Day, TimeOfDayPresets.Dusk, TimeOfDayPresets.Night };

        /// <summary>The reviewer's measure after f6a10529 (kaiju-value.txt, Azure, on the hill): body, ground; Day, Dusk, Night.</summary>
        static (float body, float ground)[] Measured(KaijuType t)
        {
            switch (t)
            {
                case KaijuType.Grove: return new[] { (0.612f, 0.632f), (0.580f, 0.605f), (0.381f, 0.309f) };
                case KaijuType.Brute: return new[] { (0.483f, 0.664f), (0.447f, 0.634f), (0.310f, 0.297f) };
                case KaijuType.Timber: return new[] { (0.654f, 0.654f), (0.599f, 0.614f), (0.391f, 0.303f) };
                case KaijuType.Mire: return new[] { (0.542f, 0.608f), (0.511f, 0.630f), (0.335f, 0.299f) };
                default: return new[] { (0.636f, 0.663f), (0.603f, 0.589f), (0.384f, 0.332f) };
            }
        }

        /// <summary>build_kaiju.py PAL: hide, hide_dk, plate, plate_lt, plate_dk, belly, belly_dk, claw, accent.</summary>
        static string[] Palette(KaijuType t)
        {
            switch (t)
            {
                case KaijuType.Grove: return new[] { "#7E6C54", "#5F5040", "#94704A", "#AE8A60", "#6E5238", "#C8B98E", "#A2936A", "#E4D9B8", "#86C24E" };
                case KaijuType.Brute: return new[] { "#5C4B47", "#463835", "#866F67", "#9D857A", "#685650", "#C4A88E", "#9E836C", "#EDE3CC", "#DCCFB3" };
                case KaijuType.Timber: return new[] { "#8C7B5C", "#6E5F45", "#CDBA92", "#E3D3AE", "#9E8862", "#D9C9A4", "#B5A47E", "#EFE6CF", "#E8DDBE" };
                case KaijuType.Mire: return new[] { "#6E7EA4", "#56638A", "#7F8DB4", "#97A4C8", "#5E6A92", "#AEB9D6", "#8E99BA", "#E2E4EE", "#8F7FC6" };
                default: return new[] { "#6E6A63", "#55524D", "#9C978A", "#B7B1A2", "#7A766C", "#CDB894", "#A8916C", "#E9E1CB", "#C99A3A" };
            }
        }

        static readonly float[] Shares = { 0.30f, 0.15f, 0.18f, 0.10f, 0.10f, 0.07f, 0.04f, 0.02f, 0.04f };
        static readonly Color Navy = new Color(0.0080f, 0.0168f, 0.0437f, 1f);
        static readonly float[] BandShare = { 0.2f, 0.3f, 0.5f };   // shadow, mid, lit (as WorldLookValueTests)

        static Color Lin(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c.linear;
        }

        /// <summary>CMToonBandColor with the material's ratios (lit x l, mid x 0.84 x m, shadow x 0.66 x s mixed 12% navy).</summary>
        static Color Band(Color a, float band, Vector3 v)
        {
            Color lit = new Color(Mathf.Clamp01(a.r * v.x), Mathf.Clamp01(a.g * v.x), Mathf.Clamp01(a.b * v.x), 1f);
            Color mid = a * (0.84f * v.y);
            Color sh = Color.Lerp(a * (0.66f * v.z), Navy, 0.12f);
            return band <= 1f ? Color.Lerp(sh, mid, band) : Color.Lerp(mid, lit, band - 1f);
        }

        static Color Gain(Color c, float g)
        {
            c = new Color(c.r * g, c.g * g, c.b * g, 1f);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), k = TimeOfDayGrade.CharKnee;
            if (m <= k) return c;
            float s = k + (1f - k) * (1f - Mathf.Exp(-(m - k) / (1f - k)));
            return new Color(c.r * s / m, c.g * s / m, c.b * s / m, 1f);
        }

        /// <summary>The OLD path (no rim: charW 0): no fill, no gain, time-of-day lift (lit - 1) x 1.25, parts lift 1.10/1.19/1.27.</summary>
        static float BodyBefore(KaijuType t, in TimeOfDayPreset p)
        {
            var v = new Vector3(1.10f, 1.19f, 1.27f);
            float todLift = Mathf.Clamp01((v.x - 1f) * 1.25f), sum = 0f;
            string[] pal = Palette(t);
            for (int i = 0; i < pal.Length; i++)
                for (int b = 0; b < 3; b++)
                    sum += Shares[i] * BandShare[b] * TimeOfDayGrade.DisplayLuma(TimeOfDayGrade.Apply(Band(Lin(pal[i]), b, v), b * 0.5f, todLift, p));
            return sum;
        }

        /// <summary>The NEW path (rim on: charW 1): band filled toward lit, the palette value, the grade skipped by the
        /// character lift, the character gain with its knee.</summary>
        static float BodyAfter(KaijuType t, in TimeOfDayPreset p)
        {
            Vector3 v = KaijuTypeLook.PaletteValue(t);
            float sum = 0f;
            string[] pal = Palette(t);
            for (int i = 0; i < pal.Length; i++)
                for (int b = 0; b < 3; b++)
                {
                    float band = b + (2f - b) * Mathf.Clamp01(p.CharFill);
                    Color c = TimeOfDayGrade.Apply(Band(Lin(pal[i]), band, v), band * 0.5f, 1f, p);
                    sum += Shares[i] * BandShare[b] * TimeOfDayGrade.DisplayLuma(Gain(c, Mathf.Max(1f, p.CharGain)));
                }
            return sum;
        }

        /// <summary>Mean contact-shadow alpha over the reviewer's ring band (0.8 .. 1.2 halo radii).</summary>
        static float RingAlpha(System.Func<float, float> alpha)
        {
            float s = 0f;
            for (int i = 0; i <= 20; i++) s += alpha(0.8f + 0.4f * i / 20f);
            return s / 21f;
        }

        static float OldShadow(float r) => r > 1f ? 0f : 0.50f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.70f, r)));

        /// <summary>A display-luma ground under a navy pool: undo the old pool alpha, apply the new one (alpha blend in linear).</summary>
        static float Pooled(float groundDisplay, float oldA, float newA)
        {
            float g = Mathf.GammaToLinearSpace(groundDisplay);
            float bare = (g - oldA * Navy.g) / Mathf.Max(1e-3f, 1f - oldA);
            return Mathf.LinearToGammaSpace(bare * (1f - newA) + Navy.g * newA);
        }

        [Test]
        public void CreatureStagesCarryARimSoTheCharacterLiftApplies()
        {
            foreach (string key in new[] { ArtMapping.KaijuAdult, ArtMapping.KaijuHatchling })
            {
                ModelSpec spec = ArtMapping.Spec(key);
                Assert.IsNotNull(spec, key);
                float drawn = SharpSwitches.RimFor(spec.Rim, true), drawnOff = SharpSwitches.RimFor(spec.Rim, false);
                Debug.Log($"[b15 kaiju value] {key}: rim {spec.Rim:0.00}, drawn {drawn:0.00} / {drawnOff:0.00} (limit > 0.001: CM/Toon charW)");
                Assert.Greater(drawn, 0.001f, key + ": without a rim CM/Toon treats the Kaiju as world (no character lift)");
                Assert.Greater(drawnOff, 0.001f, key);
            }
        }

        [Test]
        public void EveryTypeClearsTheHillBarInEveryTimeOfDay()
        {
            float oldA = RingAlpha(OldShadow), newA = RingAlpha(KaijuTypeView.ContactShadowAlpha);
            Debug.Log($"[b15 kaiju value] contact shadow over the ring band: old {oldA:0.000}, new {newA:0.000}");
            foreach (KaijuType t in Types)
            {
                var m = Measured(t);
                for (int i = 0; i < Presets.Length; i++)
                {
                    TimeOfDayPreset p = Presets[i];
                    float before = BodyBefore(t, p), after = BodyAfter(t, p);
                    float body = m[i].body * after / before, ground = Pooled(m[i].ground, oldA, newA);
                    float bar = WorldLookValueTests.Bar(p.Id), d = body - ground;
                    Debug.Log($"[b15 kaiju value] {t} {p.Name}: measured {m[i].body - m[i].ground:+0.000;-0.000} -> projected body {body:0.000} " +
                              $"ground {ground:0.000} delta {d:+0.000;-0.000} (limit >= +{bar:0.00}; body <= 0.90, no blow-out)");
                    Assert.GreaterOrEqual(d, bar, $"{t} {p.Name}");
                    Assert.LessOrEqual(body, 0.90f, $"{t} {p.Name}: the body washes out");
                }
            }
        }

        [Test]
        public void TheOldPathFailedTheSameBar()
        {
            // Control: the model of the old path reproduces the reviewer's verdict (BRUTE Day short of the bar by far).
            TimeOfDayPreset p = TimeOfDayPresets.Day;
            var m = Measured(KaijuType.Brute)[0];
            Debug.Log($"[b15 kaiju value] control BRUTE DAY measured {m.body - m.ground:+0.000;-0.000} (expected below +0.12)");
            Assert.Less(m.body - m.ground, WorldLookValueTests.Bar(p.Id));
            Assert.Greater(BodyAfter(KaijuType.Brute, p), BodyBefore(KaijuType.Brute, p) * 1.1f, "the new path lifts BRUTE");
        }

        [Test]
        public void PaletteValueKeepsThreeBandsAndOnlyTheDarkPalettesMove()
        {
            foreach (KaijuType t in Types)
            {
                Vector3 v = KaijuTypeLook.PaletteValue(t);
                float midOverLit = 0.84f * v.y / v.x, shOverMid = 0.66f * 0.88f * v.z / (0.84f * v.y);
                Debug.Log($"[b15 kaiju value] {t}: palette value {v}, mid/lit {midOverLit:0.000} (limit <= 0.90), shadow/mid {shOverMid:0.000} (limit <= 0.82)");
                Assert.LessOrEqual(midOverLit, 0.90f, t.ToString());
                Assert.LessOrEqual(shOverMid, 0.82f, t.ToString());
                if (t != KaijuType.Brute && t != KaijuType.Mire) Assert.AreEqual(Vector3.one, v, t + ": only BRUTE and MIRE take a palette value");
                else Assert.Greater(v.x, 1f, t.ToString());
            }
        }

        [Test]
        public void OneLiftPathAndTheStrongerPartsFresnel()
        {
            string anim = File.ReadAllText(Path.Combine(Application.dataPath, "_Game", "Runtime", "View", "Anim", "KaijuAnimator.cs"));
            StringAssert.DoesNotContain("KaijuTypeLook.PartsLitLift", anim, "the uniform parts lift stacked on the world look");
            StringAssert.Contains("KaijuTypeLook.PaletteValue(type)", anim);
            string hlsl = File.ReadAllText(Path.Combine(Application.dataPath, "_Game", "Art", "Shaders", "CMToonCommon.hlsl"));
            StringAssert.Contains("#define CM_KAIJU_PARTS_FRESNEL " + KaijuTypeLook.PartsFresnel.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), hlsl);
            Assert.Greater(KaijuTypeLook.PartsFresnel, 0.32f, "the parts fresnel is stronger than the painted-pattern models'");
            Assert.Greater(KaijuTypeView.ContactShadowAlpha(1.0f), 0.2f, "the pool still darkens the paving at the halo edge");
            Assert.AreEqual(0f, KaijuTypeView.ContactShadowAlpha(KaijuTypeView.ContactReach), 1e-4f, "soft edge, no hard disc");
        }
    }
}

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
    /// b15 view polish r2, Editor review round 2, Kaiju. WHY THIS TEST REPLACES THE BRIGHTNESS BAR: round 1 chased "body
    /// luma minus ground at least +0.12" on the hill, but the hill paving is itself pale (display luma about 0.66 by Day),
    /// so passing it meant lifting the Kaiju toward white: the reviewer's capture then read 8..47% of body pixels clipped
    /// (MIRE 35..47%, BRUTE washed to pale pink) and the Kaiju stayed as bright at Night as by Day (they looked unlit).
    /// A creature on a pale floor reads by its SILHOUETTE, not by being brighter than the floor. So this pins:
    /// (1) no clipping: every palette swatch, toned (CMToonKaijuTone, mirrored by KaijuTypeLook.Tone), banded and graded,
    ///     and its fresnel edge, stays under 0.98 in every channel (projected clipped share 0%, limit under 2%);
    /// (2) edge contrast: the silhouette edge (the fresnel edge leaning to the type hue, and the navy outline) against the
    ///     ground right next to it, darkened by the contact shadow, in Day, Dusk and Night;
    /// (3) identity: BRUTE a rich warm brown-red, MIRE a deep blue-violet, the others as authored;
    /// (4) the grade: the Kaiju takes half the time-of-day grade, so Night is darker than Day but still clear.
    /// A capture-free CPU projection (CMToon bands, TimeOfDayGrade.Apply), not the capture; the in-engine judge stays the
    /// reviewer's capture (tools/kval.py). Every case PRINTS what, value, limit.
    /// </summary>
    public sealed class KaijuSilhouetteTests
    {
        static readonly KaijuType[] Types = { KaijuType.Elder, KaijuType.Grove, KaijuType.Brute, KaijuType.Timber, KaijuType.Mire };
        static readonly TimeOfDayPreset[] Presets = { TimeOfDayPresets.Day, TimeOfDayPresets.Dusk, TimeOfDayPresets.Night };
        static readonly string[] PresetNames = { "Day", "Dusk", "Night" };
        /// <summary>The reviewer's ground next to the Kaiju on the hill, round 2 (kaiju-value.txt, both teams): display luma.</summary>
        static readonly float[] HillGround = { 0.66f, 0.63f, 0.32f };

        /// <summary>Silhouette limits (display luma). EdgeMin: the stronger of the two edge cues (light fresnel edge above
        /// the pooled ground, or the pooled ground above the navy outline). RimMin: the light edge always lifts off its own
        /// contact shadow. ClipLimit: projected share of clipped body tones.</summary>
        public const float EdgeMin = 0.20f, RimMin = 0.08f, ClipLimit = 0.02f, ClipChannel = 0.98f;

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
        static readonly float[] BandShare = { 0.2f, 0.3f, 0.5f };
        static readonly Color Navy = new Color(0.0080f, 0.0168f, 0.0437f, 1f);

        static Color Lin(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c.linear;
        }

        static Color Band(Color a, int band)
        {
            if (band == 2) return new Color(Mathf.Clamp01(a.r), Mathf.Clamp01(a.g), Mathf.Clamp01(a.b), 1f);
            if (band == 1) return a * 0.84f;
            return Color.Lerp(a * 0.66f, Navy, 0.12f);
        }

        /// <summary>The Kaiju path in CM/Toon: toned albedo, plain bands (no character fill or gain), half the grade.</summary>
        static Color Shade(string hex, KaijuType t, int band, in TimeOfDayPreset p) =>
            TimeOfDayGrade.Apply(Band(KaijuTypeLook.Tone(Lin(hex), t), band), band * 0.5f, 1f - KaijuTypeLook.TodShare, p);

        static Color Edge(KaijuType t, in TimeOfDayPreset p)
        {
            Color glow = Lin(KaijuTypeLook.GlowHex(t));
            return Color.Lerp(Shade(Palette(t)[0], t, 2, p), glow, KaijuTypeLook.PartsFresnel * KaijuTypeLook.GlowStrength);
        }

        static float Body(KaijuType t, in TimeOfDayPreset p)
        {
            float sum = 0f;
            string[] pal = Palette(t);
            for (int i = 0; i < pal.Length; i++)
                for (int b = 0; b < 3; b++) sum += Shares[i] * BandShare[b] * TimeOfDayGrade.DisplayLuma(Shade(pal[i], t, b, p));
            return sum;
        }

        static float PooledGround(float display)
        {
            float a = 0f;
            for (int i = 0; i <= 20; i++) a += KaijuTypeView.ContactShadowAlpha(0.8f + 0.4f * i / 20f);
            a /= 21f;
            return Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(display) * (1f - a) + Navy.g * a);
        }

        static float Max(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        [Test]
        public void NoBodyToneClipsInAnyTimeOfDay()
        {
            foreach (KaijuType t in Types)
            {
                float clipped = 0f, peak = 0f;
                string[] pal = Palette(t);
                for (int k = 0; k < Presets.Length; k++)
                {
                    for (int i = 0; i < pal.Length; i++)
                    {
                        Assert.LessOrEqual(Max(KaijuTypeLook.Tone(Lin(pal[i]), t)), KaijuTypeLook.ToneMax + 1e-4f, "the toned albedo is capped");
                        for (int b = 0; b < 3; b++)
                        {
                            float m = Max(Shade(pal[i], t, b, Presets[k]));
                            peak = Mathf.Max(peak, m);
                            if (m >= ClipChannel) clipped += Shares[i] * BandShare[b] / Presets.Length;
                        }
                    }
                    peak = Mathf.Max(peak, Max(Edge(t, Presets[k])));
                }
                Debug.Log($"[b15 kaiju silhouette] {t}: projected clipped share {clipped * 100f:0.0}% (limit < {ClipLimit * 100f:0}%), brightest channel {peak:0.000} (limit < {ClipChannel})");
                Assert.Less(clipped, ClipLimit, t + " clips");
                Assert.Less(peak, ClipChannel, t + " edge or body reaches white");
            }
        }

        [Test]
        public void TheSilhouetteEdgeSeparatesFromTheHillInDayDuskAndNight()
        {
            float outline = TimeOfDayGrade.DisplayLuma(Navy);
            foreach (KaijuType t in Types)
                for (int k = 0; k < Presets.Length; k++)
                {
                    float ground = PooledGround(HillGround[k]);
                    float rim = TimeOfDayGrade.DisplayLuma(Edge(t, Presets[k])) - ground, ink = ground - outline;
                    float edge = Mathf.Max(rim, ink);
                    Debug.Log($"[b15 kaiju silhouette] {t} {PresetNames[k]}: ground {HillGround[k]:0.00} pooled {ground:0.000}; fresnel edge {rim:+0.000;-0.000} (limit >= {RimMin}), outline {ink:+0.000} => edge {edge:0.000} (limit >= {EdgeMin})");
                    Assert.GreaterOrEqual(edge, EdgeMin, $"{t} {PresetNames[k]} silhouette edge");
                    Assert.GreaterOrEqual(rim, RimMin, $"{t} {PresetNames[k]}: the light edge lifts off its contact shadow");
                }
        }

        [Test]
        public void EachTypeKeepsItsIdentityColour()
        {
            Color brute = KaijuTypeLook.Tone(Lin("#5C4B47"), KaijuType.Brute).gamma, mire = KaijuTypeLook.Tone(Lin("#6E7EA4"), KaijuType.Mire).gamma;
            Color.RGBToHSV(brute, out float bh, out float bs, out float bv);
            Color.RGBToHSV(mire, out float mh, out float ms, out float mv);
            Debug.Log($"[b15 kaiju silhouette] BRUTE hide #{ColorUtility.ToHtmlStringRGB(brute)} hue {bh * 360f:0} sat {bs:0.00} value {bv:0.00} (limit hue 0..25, sat >= 0.45, value 0.35..0.65)");
            Debug.Log($"[b15 kaiju silhouette] MIRE hide #{ColorUtility.ToHtmlStringRGB(mire)} hue {mh * 360f:0} sat {ms:0.00} value {mv:0.00} (limit hue 235..262, sat >= 0.35, value 0.45..0.80)");
            Assert.That(bh * 360f, Is.InRange(0f, 25f), "BRUTE warm brown-red");
            Assert.GreaterOrEqual(bs, 0.45f);
            Assert.That(bv, Is.InRange(0.35f, 0.65f), "BRUTE at a mid value");
            Assert.That(mh * 360f, Is.InRange(235f, 262f), "MIRE blue-violet (off the corruption purple at 272)");
            Assert.GreaterOrEqual(ms, 0.35f);
            Assert.That(mv, Is.InRange(0.45f, 0.80f), "MIRE deep, not pale");
            foreach (KaijuType t in new[] { KaijuType.Elder, KaijuType.Grove, KaijuType.Timber })
                Assert.AreEqual(new Vector4(1f, 1f, 1f, 1f), KaijuTypeLook.ToneVector(t), t + " keeps its palette");
        }

        [Test]
        public void NightIsDarkerThanDayButStillClear()
        {
            foreach (KaijuType t in Types)
            {
                float day = Body(t, TimeOfDayPresets.Day), dusk = Body(t, TimeOfDayPresets.Dusk), night = Body(t, TimeOfDayPresets.Night);
                Debug.Log($"[b15 kaiju silhouette] {t}: body Day {day:0.000} Dusk {dusk:0.000} Night {night:0.000}; night/day {night / day:0.00} (limit 0.55..0.90)");
                Assert.Less(night / day, 0.90f, t + ": lit by night, not Day-bright");
                Assert.Greater(night / day, 0.55f, t + ": still clear at Night");
                Assert.That(day, Is.InRange(0.30f, 0.62f), t + ": a mid value by Day");
            }
        }

        [Test]
        public void CreatureStagesKeepTheirRimAndTheShaderMatchesTheConstants()
        {
            foreach (string key in new[] { ArtMapping.KaijuAdult, ArtMapping.KaijuHatchling })
            {
                ModelSpec spec = ArtMapping.Spec(key);
                Assert.IsNotNull(spec, key);
                Assert.Greater(SharpSwitches.RimFor(spec.Rim, true), 0.001f, key + ": the team rim stays on the silhouette");
            }
            string dir = Path.Combine(Application.dataPath, "_Game", "Art", "Shaders");
            string common = File.ReadAllText(Path.Combine(dir, "CMToonCommon.hlsl")), toon = File.ReadAllText(Path.Combine(dir, "CMToon.shader"));
            StringAssert.Contains("#define CM_KAIJU_TOD_SHARE " + KaijuTypeLook.TodShare.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture), common);
            StringAssert.Contains("#define CM_KAIJU_TONE_MAX " + KaijuTypeLook.ToneMax.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture), common);
            StringAssert.Contains("#define CM_KAIJU_PARTS_FRESNEL 0.50", common);
            StringAssert.Contains("return lerp(color, glow, (half)(rimGlow * fw) * k);", common, "the fresnel edge leans to the hue, never adds past white");
            StringAssert.Contains("half charW = step(0.001h, _RimStrength) * (1.0h - kaijuW);", toon, "no character fill or gain on the Kaiju");
            StringAssert.Contains("CMToonKaijuTone(albedo, input.color)", toon);
            Debug.Log($"[b15 kaiju silhouette] contact shadow {KaijuTypeView.ContactAlpha:0.00} to {KaijuTypeView.ContactPlateau:0.00}, reach {KaijuTypeView.ContactReach:0.00} halo radii (limit alpha >= 0.80, reach >= 1.6)");
            Assert.GreaterOrEqual(KaijuTypeView.ContactAlpha, 0.80f);
            Assert.GreaterOrEqual(KaijuTypeView.ContactReach, 1.6f);
        }
    }
}

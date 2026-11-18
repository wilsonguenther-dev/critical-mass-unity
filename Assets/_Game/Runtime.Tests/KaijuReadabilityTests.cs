using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.B13;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b14 polish, Kaiju readability at night (pure functions): each type's ground halo has its own SHAPE (colour-blind
    /// safe), covers a readable share of the ring, every glow hue sits away from both team hues and the corruption purple,
    /// and the body lift makes the hatched Kaiju lighter than today's bands.
    /// </summary>
    public sealed class KaijuReadabilityTests
    {
        static readonly KaijuType[] Types = { KaijuType.Elder, KaijuType.Grove, KaijuType.Brute, KaijuType.Timber, KaijuType.Mire };

        static bool[] Sample(KaijuType t, out float share)
        {
            const int nr = 24, na = 144;
            var on = new bool[nr * na];
            int count = 0;
            for (int ri = 0; ri < nr; ri++)
                for (int ai = 0; ai < na; ai++)
                {
                    float r = 0.45f + 0.55f * (ri + 0.5f) / nr;
                    float a = ai * Mathf.PI * 2f / na;
                    bool v = KaijuTypeView.HaloPattern(t, r, a) > 0.5f;
                    on[ri * na + ai] = v;
                    if (v) count++;
                }
            share = count / (float)on.Length;
            return on;
        }

        [Test]
        public void EveryTypeHaloHasItsOwnShape()
        {
            for (int i = 0; i < Types.Length; i++)
            {
                bool[] a = Sample(Types[i], out float sa);
                Debug.Log("kaiju halo " + Types[i] + ": glow share of the ring band " + sa.ToString("0.000") + " (limit 0.10 .. 0.75)");
                Assert.That(sa, Is.InRange(0.10f, 0.75f), Types[i] + " halo glow share");
                for (int j = i + 1; j < Types.Length; j++)
                {
                    bool[] b = Sample(Types[j], out _);
                    int diff = 0;
                    for (int k = 0; k < a.Length; k++) if (a[k] != b[k]) diff++;
                    float d = diff / (float)a.Length;
                    Debug.Log("kaiju halo " + Types[i] + " vs " + Types[j] + ": differing samples " + d.ToString("0.000") + " (limit >= 0.12)");
                    Assert.That(d, Is.GreaterThanOrEqualTo(0.12f), Types[i] + " vs " + Types[j] + " halo shapes too alike");
                }
            }
        }

        static float HueDeg(string hex)
        {
            Assert.IsTrue(ColorUtility.TryParseHtmlString(hex, out Color c), hex);
            Color.RGBToHSV(c, out float h, out _, out _);
            return h * 360f;
        }

        static float HueDist(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        [Test]
        public void GlowHuesStayOffTeamAndCorruptionHues()
        {
            float azure = HueDeg(CriticalMass.Sim.Config.Palette.Azure), crimson = HueDeg(CriticalMass.Sim.Config.Palette.Crimson), corrupt = HueDeg("#8E3FD1");
            for (int i = 0; i < Types.Length; i++)
            {
                string hex = KaijuTypeLook.GlowHex(Types[i]);
                float h = HueDeg(hex);
                float dA = HueDist(h, azure), dC = HueDist(h, crimson), dP = HueDist(h, corrupt);
                Debug.Log("kaiju glow " + Types[i] + " " + hex + ": hue " + h.ToString("0") + " deg, to azure " + dA.ToString("0") +
                          ", to crimson " + dC.ToString("0") + ", to corruption " + dP.ToString("0") + " (limit >= 20 each)");
                Assert.That(dA, Is.GreaterThanOrEqualTo(20f));
                Assert.That(dC, Is.GreaterThanOrEqualTo(20f));
                Assert.That(dP, Is.GreaterThanOrEqualTo(20f));
                Assert.That(KaijuTypeLook.Pattern(Types[i]), Is.InRange(1f, 5f));
            }
        }

        [Test]
        public void HatchedBodyIsLighterThanTodaysBands()
        {
            Debug.Log("kaiju body lift: lit x" + KaijuTypeLook.LitLift + ", mid x" + KaijuTypeLook.MidLift + ", shadow x" + KaijuTypeLook.ShadowLift + " (each > 1)");
            Assert.Greater(KaijuTypeLook.LitLift, 1f);
            Assert.Greater(KaijuTypeLook.MidLift, 1f);
            Assert.Greater(KaijuTypeLook.ShadowLift, KaijuTypeLook.MidLift);
            Assert.Greater(KaijuTypeLook.GlowStrength, 0.5f);
        }
    }
}

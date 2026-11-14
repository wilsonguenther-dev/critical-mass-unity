using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.View.Effects;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// fix/smoke-look-1 (2026-11-14): explosion smoke is several small soft puffs in every view, never one big ball, never
    /// opaque, gone in about a second and a half; fireballs and blast dust stop growing past a fighter's size.
    /// </summary>
    public sealed class SmokeLookTests
    {
        /// <summary>A fighter stands about this tall (u); tags float at 2.65 above the feet.</summary>
        const float FighterHeight = 2.1f;
        static readonly float[] Radii = { 0.5f, 1.1f, 2f, 3f, 6f, 8f };   // wall break .. Kaiju slam (events clamp at 6 and 8)

        [Test]
        public void EveryPuffIsBornWellUnderAFighterInRadiusAndGrowsOnlyALittle()
        {
            foreach (float R in Radii)
            {
                float birth = FxScene.SmokeBirthRadius(R);
                Assert.Greater(birth, 0.2f);
                Assert.LessOrEqual(birth, 0.3f * FighterHeight, $"birth radius for R {R}");
                Assert.LessOrEqual(FxScene.SmokeRadius(R, 1f), birth * FxScene.SmokeGrow + 1e-5f);
                Assert.LessOrEqual(FxScene.SmokeRadius(R, 1f), 0.45f * FighterHeight, $"largest puff for R {R}: never a big ball");
                Assert.AreEqual(birth, FxScene.SmokeRadius(R, 0f), 1e-5f);
            }
        }

        [Test]
        public void SmokeIsNeverOpaqueAndThinsOutToNothing()
        {
            Assert.LessOrEqual(FxScene.SmokeAlpha, 0.5f, "fighters stay visible through it from top-down");
            for (int k = 0; k <= 20; k++) Assert.LessOrEqual(FxScene.SmokeAlphaAt(k / 20f), FxScene.SmokeAlpha + 1e-6f);
            Assert.AreEqual(0f, FxScene.SmokeAlphaAt(0f), 1e-6f, "fades in");
            Assert.AreEqual(0f, FxScene.SmokeAlphaAt(1f), 1e-6f, "thinned out at the end");
            Assert.Less(FxScene.SmokeAlphaAt(0.8f), FxScene.SmokeAlphaAt(0.3f));
        }

        [Test]
        public void TheSmokeIsGoneInAboutASecondAndAHalf()
        {
            Assert.LessOrEqual(FxScene.SmokeEnd, 1.55);
            Assert.GreaterOrEqual(FxScene.SmokePuffs, 6, "several puffs, not one ball");
        }

        [Test]
        public void FireballAndBlastDustStopGrowingPastAFightersSize()
        {
            float fireball = FxScene.BoomVisualMax * 0.92f * 1.12f + 0.05f;   // the fireball's widest navy rim
            Assert.Less(fireball, FighterHeight * 0.85f);
            Assert.LessOrEqual(FxScene.BlastPuffMax, 0.8f);
            Assert.Greater(FxScene.SmokeBound(8f), FxScene.SmokeBound(0.5f) - 1e-5f);
            Assert.AreEqual(FxScene.SmokeBound(FxScene.BoomVisualMax), FxScene.SmokeBound(8f), 1e-5f, "the Kaiju slam is not bigger");
        }

        /// <summary>polish-3 (owner: the TNT fireball read as "a bunch of orange balloons"): one soft blast, at most four
        /// merged lobes, no wider than about one and a half fighters, gone by 0.6 s, cheaper than the old balls.</summary>
        [Test]
        public void TheFireballIsOneSoftMergedBlastGoneBySixTenths()
        {
            Assert.LessOrEqual(FxScene.FireballLobes, 4, "at most three or four lobes");
            Assert.Greater(FxScene.FireballLobeOverlap, 0.5f, "neighbouring lobes overlap by more than half their radius");
            Assert.LessOrEqual(FxScene.FireballEnd, 0.6 + 1e-9, "gone by 0.6 s");
            foreach (float R in Radii)
            {
                float widest = 0f;
                for (int k = 0; k <= 60; k++)
                {
                    float t = k * 0.01f;
                    widest = UnityEngine.Mathf.Max(widest, FxScene.FireballCoreRadius(R, t) * FxScene.FireballHalfK);
                }
                Assert.LessOrEqual(2f * widest, 1.5f * FighterHeight * 0.75f, $"R {R}: about one and a half fighters across at most");
                Assert.Greater(widest, 0.2f, $"R {R}: still a real blast");
                Assert.AreEqual(0f, FxScene.FireballCoreRadius(R, 0.6f), 1e-6f);
                Assert.Greater(FxScene.FireballCoreRadius(R, 0.12f), 2f * FxScene.FireballCoreRadius(R, 0f) - 1e-5f, "scales up fast");
            }
            Assert.AreEqual(1f, FxScene.FireballAlpha(0.2f), 1e-6f);
            Assert.Less(FxScene.FireballAlpha(0.5f), FxScene.FireballAlpha(0.4f), "fades");
            Assert.AreEqual(0f, FxScene.FireballAlpha(0.6f), 1e-6f);
            Assert.Less(FxScene.FireballYellowK(0.4f), FxScene.FireballYellowK(0.05f), "the yellow heart shrinks first");
            // cost: (core + lobes) x 3 layers + the yellow heart, against the old 4 fireball blobs + 7 lobes x 3 blobs
            int newVerts = 3 * ((12 + 1) + FxScene.FireballLobes * (10 + 1)) + (12 + 1);
            int oldVerts = 4 * (12 + 1) + 7 * ((9 + 1) + (9 + 1) + (7 + 1));
            Assert.LessOrEqual(newVerts, oldVerts);
        }

        [Test]
        public void TheSmokeCostsNoMoreThanTheOldColumn()
        {
            int newVerts = FxScene.SmokePuffs * 3 * (8 + 1);   // three 8-point blobs per puff
            int oldVerts = 6 * (11 + 11 + 9 + 9);               // six puffs of rim, body, light and dark blobs
            Assert.LessOrEqual(newVerts, oldVerts);
        }

        [Test]
        public void TheLowViewCapsStillHold()
        {
            Assert.Less(FxScene.LowSmokeLife, 1.0);
            Assert.Less(FxScene.LowSmokeRise, 1f);
            Assert.Less(ViewCull.VolumeScreenTan, ViewCull.MaxScreenTan);
        }
    }
}

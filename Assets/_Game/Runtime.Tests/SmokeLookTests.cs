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

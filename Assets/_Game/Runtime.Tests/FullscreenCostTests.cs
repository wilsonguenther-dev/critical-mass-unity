using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.View;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// perf/fullscreen-cost-1 (2026-11-14): the dynamic render scale steps down only on GPU-bound missed frames, never
    /// below the level's floor or above the chosen scale, waits between changes, steps back up only after calm, backs off
    /// after a bounce, and is off at High; the Low/Medium renderer no longer forces an intermediate colour target; new
    /// fighter models are spread over frames; the font warm-up keys and sizes are right.
    /// </summary>
    public sealed class FullscreenCostTests
    {
        const double Calm = 0, Long = 100;
        static readonly double R = FramePolicy.DynamicRecoverSeconds;

        [Test]
        public void StepsDownOnlyWhenFramesMissAndTheGpuIsBusy()
        {
            Assert.AreEqual(0.95f, FramePolicy.NextDynamicScale(1f, 0.8f, 1f, 0.05, 0.7, Long, Calm, R), 1e-5f, "GPU-bound misses: one step down");
            Assert.AreEqual(1f, FramePolicy.NextDynamicScale(1f, 0.8f, 1f, 0.05, 0.4, Long, Calm, R), 1e-5f, "CPU-bound misses: fewer pixels would not help");
            Assert.AreEqual(1f, FramePolicy.NextDynamicScale(1f, 0.8f, 1f, 0.01, 0.9, Long, Calm, R), 1e-5f, "a few misses are not enough");
            Assert.AreEqual(1f, FramePolicy.NextDynamicScale(1f, 0.8f, 1f, 0.05, 0.9, FramePolicy.DynamicHoldSeconds - 0.5, Calm, R), 1e-5f, "never two changes within the hold");
            Assert.AreEqual(0.95f, FramePolicy.NextDynamicScale(1f, 0.8f, 1f, 0.05, 0, Long, Calm, R), 1e-5f, "no frame timing: misses alone");
        }

        [Test]
        public void StaysBetweenTheFloorAndTheChosenScale()
        {
            Assert.AreEqual(0.8f, FramePolicy.NextDynamicScale(0.8f, 0.8f, 1f, 0.2, 0.9, Long, Calm, R), 1e-5f, "never under the floor");
            Assert.AreEqual(0.75f, FramePolicy.NextDynamicScale(0.75f, 0.8f, 0.75f, 0.2, 0.9, Long, Calm, R), 1e-5f, "a chosen scale under the floor stays as chosen");
            Assert.AreEqual(1f, FramePolicy.NextDynamicScale(1f, 0.8f, 1f, 0, 0.2, Long, Long, R), 1e-5f, "never above the chosen scale");
            float s = 1f;
            for (int k = 0; k < 10; k++) s = FramePolicy.NextDynamicScale(s, 0.8f, 1f, 0.1, 0.8, Long, Calm, R);
            Assert.AreEqual(0.8f, s, 1e-6f, "steps land exactly on the floor (no drift)");
            Assert.AreEqual(1f, FramePolicy.DynamicFloor(FramePolicy.LevelHigh), "High keeps native pixels");
            Assert.AreEqual(0.8f, FramePolicy.DynamicFloor(FramePolicy.LevelMedium));
            Assert.AreEqual(0.7f, FramePolicy.DynamicFloor(FramePolicy.LevelLow));
            Assert.AreEqual(0.7f, FramePolicy.DynamicFloor(FramePolicy.LevelVeryLow));
        }

        [Test]
        public void StepsBackUpOnlyAfterCalmAndBacksOffAfterABounce()
        {
            Assert.AreEqual(0.95f, FramePolicy.NextDynamicScale(0.9f, 0.8f, 1f, 0, 0.4, Long, R + 1, R), 1e-5f, "calm and light GPU: one step up");
            Assert.AreEqual(0.9f, FramePolicy.NextDynamicScale(0.9f, 0.8f, 1f, 0, 0.4, Long, R - 1, R), 1e-5f, "not calm long enough");
            Assert.AreEqual(0.9f, FramePolicy.NextDynamicScale(0.9f, 0.8f, 1f, 0, 0.55, Long, R + 1, R), 1e-5f, "GPU still busy: stay");
            Assert.AreEqual(0.9f, FramePolicy.NextDynamicScale(0.9f, 0.8f, 1f, 0, 0, Long, R + 1, R), 1e-5f, "no frame timing: twice the calm");
            Assert.AreEqual(0.95f, FramePolicy.NextDynamicScale(0.9f, 0.8f, 1f, 0, 0, Long, 2 * R + 1, R), 1e-5f);
            Assert.AreEqual(2 * R, FramePolicy.NextRecoverSeconds(R, 4), 1e-9, "a bounce doubles the wait");
            Assert.AreEqual(FramePolicy.DynamicRecoverMaxSeconds, FramePolicy.NextRecoverSeconds(FramePolicy.DynamicRecoverMaxSeconds, 1), 1e-9, "capped");
            Assert.AreEqual(R, FramePolicy.NextRecoverSeconds(40, FramePolicy.DynamicRecoverMaxSeconds + 1), 1e-9, "a long stay resets it");
        }

        [Test]
        public void LowAndMediumRendererDoesNotForceAnIntermediateTarget()
        {
            string dir = Path.Combine(Application.dataPath, "_Game/Settings");
            string fwd = File.ReadAllText(Path.Combine(dir, "CriticalMass_URP_Renderer_Forward.asset"));
            StringAssert.Contains("m_IntermediateTextureMode: 0", fwd, "Auto: no full-screen copy at native scale");
            string guid = null;
            foreach (string line in File.ReadAllLines(Path.Combine(dir, "CriticalMass_URP_Renderer_Forward.asset.meta")))
                if (line.StartsWith("guid: ")) guid = line.Substring(6).Trim();
            Assert.IsNotNull(guid);
            foreach (string a in new[] { "CriticalMass_URP_Low.asset", "CriticalMass_URP_Medium.asset" })
                StringAssert.Contains(guid, File.ReadAllText(Path.Combine(dir, a)), $"{a} draws with the Forward renderer");
            StringAssert.Contains("m_MSAA: 1", File.ReadAllText(Path.Combine(dir, "CriticalMass_URP_Medium.asset")), "Medium has no MSAA");
        }

        [Test]
        public void NewFighterModelsAreSpreadOverFrames()
        {
            Assert.That(ActorViews.NewModelsPerFrame, Is.InRange(4, 8));
            Assert.LessOrEqual((32 + ActorViews.NewModelsPerFrame - 1) / ActorViews.NewModelsPerFrame, 8, "32 fighters built within 8 frames");
        }

        [Test]
        public void FontWarmupSizesAndKeys()
        {
            Assert.AreEqual(26, GlyphWarmup.PixelSize(26, 0, false, 1f));
            Assert.AreEqual(39, GlyphWarmup.PixelSize(26, 0, false, 1.5f));
            Assert.AreEqual(40, GlyphWarmup.PixelSize(26, 40, true, 1f), "best fit warms its largest size");
            Assert.AreEqual(26, GlyphWarmup.PixelSize(26, 0, false, float.NaN));
            Assert.AreNotEqual(GlyphWarmup.Key(5, 26, FontStyle.Normal), GlyphWarmup.Key(5, 26, FontStyle.Bold));
            Assert.AreNotEqual(GlyphWarmup.Key(5, 26, FontStyle.Normal), GlyphWarmup.Key(5, 27, FontStyle.Normal));
            Assert.AreNotEqual(GlyphWarmup.Key(5, 26, FontStyle.Normal), GlyphWarmup.Key(6, 26, FontStyle.Normal));
            StringAssert.Contains("0123456789", GlyphWarmup.Chars);
        }
    }
}

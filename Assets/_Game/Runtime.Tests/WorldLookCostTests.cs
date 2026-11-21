using System;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Atmosphere;
using CriticalMass.Runtime.View.Mechanics;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 world look: frame-cost budgets (item 6), quality tiers (item 3), the heal hue and the scorch size (item 7).
    /// Pure or file reads; no scene.
    /// </summary>
    public sealed class WorldLookCostTests
    {
        [Test]
        public void TheDressedWorldResolvesFewMaterials()
        {
            // WorldView.Resolve: one material per WorldLook (Resources/Toon/World/World<Look>.mat, the ground's own), plus a
            // copy per team for the team-swapped looks (Crimson always; Hill and Neutral for Base and BaseFlat). The
            // combined scenery groups are one per (chunk, level, material), so this count bounds the scenery's SetPass.
            int looks = Enum.GetValues(typeof(WorldLook)).Length, copies = 0;
            foreach (WorldLook l in Enum.GetValues(typeof(WorldLook)))
            {
                if (l == WorldLook.Pool || !WorldKit.TeamSwapped(l)) continue;
                copies++;
                if (l == WorldLook.Base || l == WorldLook.BaseFlat) copies += 2;
            }
            int unique = looks + copies;
            string dir = Path.Combine(Application.dataPath, "_Game/Resources/Toon/World");
            int files = Directory.GetFiles(dir, "World*.mat").Length;
            Debug.Log($"[b15 cost] world materials: {looks} looks + {copies} team copies = {unique} (budget {WorldLookBudgets.WorldMaterialBudget}); {files} World*.mat files");
            Assert.LessOrEqual(unique, WorldLookBudgets.WorldMaterialBudget);
            Assert.LessOrEqual(files, looks, "no stray world material files beyond one per look");
        }

        [Test]
        public void VeryLowCostsNothingAndEveryTierIsCapped()
        {
            WorldLookBudgets.AirTier v = WorldLookBudgets.For(FramePolicy.LevelVeryLow);
            Assert.AreEqual(1, v.Msaa);
            Assert.IsFalse(v.Post);
            Assert.IsFalse(v.Bloom);
            Assert.AreEqual(0, v.Particles);
            Assert.AreEqual(0f, v.CostMs);
            Assert.AreEqual(4, WorldLookBudgets.For(FramePolicy.LevelHigh).Msaa, "art direction: MSAA 4x");
            Assert.AreEqual(4, WorldLookBudgets.For(FramePolicy.LevelHigh + 2).Msaa, "Ultra shares High's asset");
            int prev = -1;
            foreach (int level in new[] { FramePolicy.LevelVeryLow, FramePolicy.LevelLow, FramePolicy.LevelMedium, FramePolicy.LevelHigh })
            {
                WorldLookBudgets.AirTier t = WorldLookBudgets.For(level);
                Debug.Log($"[b15 tier] level {level}: MSAA {t.Msaa}x, post {t.Post}, bloom {t.Bloom}, particles {t.Particles}, ~{t.CostMs:0.00} ms: {t.Note}");
                Assert.GreaterOrEqual(t.Particles, prev, "never more particles on a lower tier");
                Assert.LessOrEqual(t.Particles, 256, "hard cap");
                prev = t.Particles;
            }
            Assert.IsFalse(WorldLookBudgets.For(FramePolicy.LevelMedium).Bloom, "no bloom on the Air's tier");
            Assert.Greater(WorldLookBudgets.BloomThreshold, 1f, "bloom only above every lit surface (glows only)");
        }

        [Test]
        public void ThePostGradeIsSmallAndHasNoVignette()
        {
            foreach (TimeOfDay t in new[] { TimeOfDay.Day, TimeOfDay.Dusk, TimeOfDay.Night })
            {
                View.Atmosphere.WorldPost.Grade g = View.Atmosphere.WorldPost.For(t);
                Assert.LessOrEqual(Mathf.Abs(g.Contrast), 10f);
                Assert.LessOrEqual(Mathf.Abs(g.Saturation), 10f);
                Assert.AreEqual(0f, g.Exposure, "exposure stays with the time-of-day grade");
            }
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Runtime/View/Atmosphere/WorldPost.cs"));
            StringAssert.DoesNotContain("Add<Vignette>", src);
            StringAssert.DoesNotContain("Add<DepthOfField>", src);
        }

        [Test]
        public void HeightFogStartsBelowTheIslandTops()
        {
            string dir = Path.Combine(Application.dataPath, "_Game/Resources/Toon/World");
            foreach (string f in Directory.GetFiles(dir, "World*.mat"))
            {
                string s = File.ReadAllText(f);
                if (!s.Contains("_FogTop")) continue;
                StringAssert.Contains("- _FogTop: -2\n", s.Replace("\r", ""), Path.GetFileName(f) + ": art direction 2.2 height fog below y -2");
            }
        }

        static float Hue(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            return h * 360f;
        }

        static float HueGap(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        [Test]
        public void TheHealHasItsOwnHue()
        {
            float heal = Hue(MechanicsWorldView.HealMint), biomass = Hue(CMTokens.Biomass.Color), azure = Hue(CMTokens.Azure.Color),
                crimson = Hue(CMTokens.Crimson.Color);
            Debug.Log($"[b15 hue] heal {heal:0} biomass {biomass:0} azure {azure:0} crimson {crimson:0}");
            Assert.GreaterOrEqual(HueGap(heal, biomass), 40f, "not the reserved Biomass lime");
            Assert.GreaterOrEqual(HueGap(heal, azure), 40f, "not the Azure team hue");
            Assert.GreaterOrEqual(HueGap(heal, crimson), 90f, "not the Crimson team hue");
        }

        [Test]
        public void TheScorchIsSmallAndFaint()
        {
            foreach (float r in new[] { 0.5f, 1f, 1.5f, 2.4f, 3.5f, 6f })
            {
                float across = FxScene.ScorchAcross(r);
                Assert.That(across, Is.InRange(FxScene.ScorchMinAcross - 1e-4f, FxScene.ScorchMaxAcross + 1e-4f), $"blast {r}: {across:0.00} u");
            }
            Debug.Log($"[b15 scorch] across at blast 2.4: {FxScene.ScorchAcross(2.4f):0.00} u (was {2f * 1.55f * 0.63f * 2.4f:0.00}), alpha {FxScene.ScorchAlpha}");
            Assert.LessOrEqual(FxScene.ScorchAlpha, 0.3f + 1e-6f, "art direction 8.19: 30%");
        }
    }
}

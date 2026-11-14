using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Atmosphere;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Arena night life 1 (2026-11-13): the Day, Dusk and Night presets exist and are complete, Day is today's look,
    /// gameplay readability holds in every preset (team colours, team markers and rings, pickups against the ground,
    /// measured with the CPU mirror of the shader grade), the choice resolves deterministically, the ambient life stays
    /// inside its per-quality budget (off or reduced on Low) and off the play surface, and every lit world shader takes
    /// the grade.
    /// </summary>
    public sealed class TimeOfDayTests
    {
        const int Low = FramePolicy.LevelLow, Medium = FramePolicy.LevelMedium, High = FramePolicy.LevelHigh;

        static Color Lin(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c.linear;
        }

        // ------------------------------------------------------------------------------------------- presets
        [Test]
        public void ThreePresetsExistAndAreComplete()
        {
            Assert.AreEqual(3, TimeOfDayPresets.All.Length);
            var seen = new HashSet<TimeOfDay>();
            foreach (TimeOfDayPreset p in TimeOfDayPresets.All)
            {
                Assert.IsTrue(p.IsComplete(out string why), p.Name + ": " + why);
                Assert.IsTrue(seen.Add(p.Id), "one preset per time: " + p.Id);
                Assert.AreEqual(p.Id, TimeOfDayPresets.For(p.Id).Id);
            }
            foreach (TimeOfDay t in (TimeOfDay[])Enum.GetValues(typeof(TimeOfDay))) Assert.IsTrue(seen.Contains(t), "a preset for " + t);
        }

        [Test]
        public void DayIsTodaysLookOnThePlaySurface()
        {
            TimeOfDayPreset d = TimeOfDayPresets.Day;
            Assert.AreEqual(Color.white, (Color)new Color(d.Key.r, d.Key.g, d.Key.b, 1f), "Day lit band x 1");
            Assert.AreEqual(Color.white, (Color)new Color(d.Shade.r, d.Shade.g, d.Shade.b, 1f), "Day shadow band x 1");
            Assert.AreEqual(0f, d.Ambient.r + d.Ambient.g + d.Ambient.b, "no sky fill");
            Assert.AreEqual(0f, d.RimStrength, "no moon rim");
            Assert.AreEqual(0f, d.FogMix, "the materials' own fog colour");
            Assert.AreEqual(0f, d.Stars + d.Moon + d.GlowBoost + d.Fireflies + d.LightPools + d.CreatureGlow, "nothing night-only");
            Assert.AreEqual(GameBootstrap.SunElevationDeg, d.SunElevationDeg);
            Assert.AreEqual(GameBootstrap.SunAzimuthFromScreenUpDeg, d.SunAzimuthFromScreenUpDeg);
            // The grade at Day is the identity for any colour, band and lift.
            foreach (string hex in new[] { "#A3C150", "#D8B78C", "#2A95AE", "#FF5A5F", "#18233B" })
                for (float band = 0f; band <= 1f; band += 0.5f)
                    for (float lift = 0f; lift <= 1f; lift += 1f)
                    {
                        Color c = Lin(hex), g = TimeOfDayGrade.Apply(c, band, lift, d);
                        Assert.Less(Mathf.Abs(g.r - c.r) + Mathf.Abs(g.g - c.g) + Mathf.Abs(g.b - c.b), 1e-5f, hex);
                    }
        }

        [Test]
        public void NightAndDuskDiffer()
        {
            Assert.Less(TimeOfDayPresets.Night.Key.g, 0.5f, "night is darker");
            Assert.Greater(TimeOfDayPresets.Night.Stars, 0.5f);
            Assert.Greater(TimeOfDayPresets.Night.Moon, 0.5f);
            Assert.Greater(TimeOfDayPresets.Night.LightPools, 0.5f, "lanterns lit at night");
            Assert.Less(TimeOfDayPresets.Dusk.SunElevationDeg, TimeOfDayPresets.Day.SunElevationDeg, "a low dusk sun");
            Assert.AreNotEqual(TimeOfDayPresets.Day.SunAzimuthFromScreenUpDeg, TimeOfDayPresets.Night.SunAzimuthFromScreenUpDeg, "the moon is elsewhere");
        }

        [Test]
        public void LerpHitsBothEndsAndStaysComplete()
        {
            TimeOfDayPreset a = TimeOfDayPresets.Day, b = TimeOfDayPresets.Night;
            Assert.AreEqual(a.Key, TimeOfDayPresets.Lerp(a, b, 0f).Key);
            Assert.AreEqual(b.Key, TimeOfDayPresets.Lerp(a, b, 1f).Key);
            for (float t = 0.1f; t < 1f; t += 0.2f)
                Assert.IsTrue(TimeOfDayPresets.Lerp(a, b, t).IsComplete(out string why), t + ": " + why);
            float az = TimeOfDayPresets.Lerp(TimeOfDayPresets.Dusk, b, 0.5f).SunAzimuthFromScreenUpDeg;
            Assert.That(az, Is.InRange(0f, 360f));
        }

        // ------------------------------------------------------------------------------------------- readability
        // Ground samples (environment kit v3 tones; the shadowed grass is the ground shader's shadow band) against the
        // things a player must find: team cloth (banded, characters skip most of the grade), team markers and unit rings
        // (self-lit / unlit: never graded), pickups (unlit FX). Perceptual difference in OKLab. Bars: every pair keeps at
        // least 80% of its Day difference and, where Day clears it, at least 0.08 (about four just-noticeable steps); self-lit and unlit
        // elements keep at least 85%. The measured numbers are in the arena-night-life-1 REPORT.md.
        static readonly (string name, string hex, float band)[] Ground =
        {
            ("grass", "#A3C150", 1f), ("path", "#D8B78C", 1f), ("dirt", "#D39A5C", 1f), ("grass in shadow", null, 0f),
        };

        static readonly (string name, string hex)[] Cloth = { ("azure cloth", "#2A95AE"), ("crimson cloth", "#FF5A5F") };

        static readonly (string name, string hex)[] SelfLit =
        {
            ("azure marker", "#27F8F8"), ("crimson marker", "#D6594F"), ("azure ring", "#16C7D9"), ("crimson ring", "#FF5A5F"),
            ("biomass pickup", "#B7F238"), ("coin pickup", "#C79245"),
        };

        static Color GroundColour(int i)
        {
            if (Ground[i].hex != null) return Lin(Ground[i].hex);
            // CM/WorldGround's shadow: colour x 0.66 mixed 12% toward Void Navy.
            Color g = Lin("#A3C150"), navy = Lin("#18233B");
            return Color.Lerp(g * 0.66f, navy, 0.12f);
        }

        /// <summary>The worst (lowest) ratio of preset difference to Day difference over every pair, with its name.</summary>
        public static (float ratio, float abs, string pair) Worst(in TimeOfDayPreset p, bool selfLit)
        {
            float worst = float.MaxValue, abs = 0f;
            string name = null, name2 = null;
            for (int i = 0; i < Ground.Length; i++)
            {
                Color g = GroundColour(i);
                Color gp = TimeOfDayGrade.Apply(g, Ground[i].band, 0f, p), gd = TimeOfDayGrade.Apply(g, Ground[i].band, 0f, TimeOfDayPresets.Day);
                foreach (var e in selfLit ? SelfLit : Cloth)
                {
                    Color c = Lin(e.hex);
                    Color cp = selfLit ? c : TimeOfDayGrade.Apply(c, 1f, 1f, p);
                    float dp = TimeOfDayGrade.DeltaE(cp, gp), dd = TimeOfDayGrade.DeltaE(c, gd);
                    float r = dp / Mathf.Max(1e-4f, dd);
                    if (r < worst) { worst = r; name = e.name + " on " + Ground[i].name; }
                    // The absolute floor applies to pairs that clear it in Day (coins on dirt are 0.035 in Day already:
                    // a Day finding in the report, not something a preset can fix).
                    if (dd >= 0.1f && (name2 == null || dp < abs)) { abs = dp; name2 = e.name + " on " + Ground[i].name; }
                }
            }
            return (worst, abs, name + " (lowest absolute: " + name2 + ")");
        }

        [Test]
        public void TeamClothStaysReadableInEveryPreset()
        {
            foreach (TimeOfDayPreset p in TimeOfDayPresets.All)
            {
                (float r, float abs, string pair) = Worst(p, false);
                Assert.GreaterOrEqual(r, 0.8f, $"{p.Name}: {pair} keeps {r:0.00} of its Day difference");
                Assert.GreaterOrEqual(abs, 0.08f, $"{p.Name}: {pair} difference {abs:0.000}");
            }
        }

        [Test]
        public void MarkersRingsAndPickupsStayReadableInEveryPreset()
        {
            foreach (TimeOfDayPreset p in TimeOfDayPresets.All)
            {
                (float r, float abs, string pair) = Worst(p, true);
                Assert.GreaterOrEqual(r, 0.85f, $"{p.Name}: {pair} keeps {r:0.00} of its Day difference");
                Assert.GreaterOrEqual(abs, 0.08f, $"{p.Name}: {pair} difference {abs:0.000}");
            }
        }

        [Test]
        public void CharactersStayTheBrightestLayerAtNight()
        {
            TimeOfDayPreset n = TimeOfDayPresets.Night;
            Color grass = TimeOfDayGrade.Apply(Lin("#A3C150"), 1f, 0f, n);
            Color cream = TimeOfDayGrade.Apply(Lin("#F7F0E5"), 1f, 1f, n);
            Assert.Greater(TimeOfDayGrade.Contrast(cream, grass), 3f, "a fighter's cream against moonlit grass");
        }

        // ------------------------------------------------------------------------------------------- the choice
        [Test]
        public void ChoiceResolvesDeterministicallyAndRandomSpreads()
        {
            Assert.AreEqual(TimeOfDay.Day, TimeOfDaySettings.Resolve(TimeOfDayChoice.Day, 7));
            Assert.AreEqual(TimeOfDay.Dusk, TimeOfDaySettings.Resolve(TimeOfDayChoice.Dusk, 7));
            Assert.AreEqual(TimeOfDay.Night, TimeOfDaySettings.Resolve(TimeOfDayChoice.Night, 7));
            var n = new int[3];
            for (int seed = 1; seed <= 300; seed++)
            {
                TimeOfDay a = TimeOfDaySettings.Resolve(TimeOfDayChoice.Random, seed);
                Assert.AreEqual(a, TimeOfDaySettings.Resolve(TimeOfDayChoice.Random, seed), "same seed, same time (rematches match)");
                n[(int)a]++;
            }
            foreach (int c in n) Assert.Greater(c, 60, "Random picks each time often: " + string.Join(",", n));
        }

        [Test]
        public void SettingDefaultsToDayAndSteps()
        {
            bool had = PlayerPrefs.HasKey(TimeOfDaySettings.Key);
            int old = PlayerPrefs.GetInt(TimeOfDaySettings.Key, 0);
            try
            {
                TimeOfDaySettings.Reset();
                Assert.AreEqual(TimeOfDayChoice.Day, TimeOfDaySettings.Choice);
                TimeOfDaySettings.Step(1);
                Assert.AreEqual(TimeOfDayChoice.Dusk, TimeOfDaySettings.Choice);
                TimeOfDaySettings.Step(1); TimeOfDaySettings.Step(1);
                Assert.AreEqual(TimeOfDayChoice.Random, TimeOfDaySettings.Choice);
                TimeOfDaySettings.Step(1);
                Assert.AreEqual(TimeOfDayChoice.Day, TimeOfDaySettings.Choice, "wraps");
                TimeOfDaySettings.Step(-1);
                Assert.AreEqual(TimeOfDayChoice.Random, TimeOfDaySettings.Choice);
                PlayerPrefs.SetInt(TimeOfDaySettings.Key, 99);
                Assert.AreEqual(TimeOfDayChoice.Day, TimeOfDaySettings.Choice, "a bad stored value reads as Day");
                Assert.AreEqual("NIGHT", TimeOfDaySettings.Label(TimeOfDayChoice.Night));
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(TimeOfDaySettings.Key, old); else PlayerPrefs.DeleteKey(TimeOfDaySettings.Key);
            }
        }

        // ------------------------------------------------------------------------------------------- budgets
        [Test]
        public void AmbientLifeIsOffOrReducedOnLow()
        {
            AmbientBudget low = AmbientBudget.For(Low), veryLow = AmbientBudget.For(FramePolicy.LevelVeryLow);
            foreach (AmbientBudget b in new[] { low, veryLow })
            {
                Assert.AreEqual(0, b.Skyrays + b.Loafers + b.Birds, "no creatures on Low");
                Assert.AreEqual(0, b.Fireflies, "no fireflies on Low");
                Assert.AreEqual(0, b.MistPerFall * b.MaxFalls, "no mist on Low");
                Assert.LessOrEqual(b.Clouds, 8);
                Assert.AreEqual(2, b.LanternStride, "half the lantern lights on Low");
                Assert.LessOrEqual(b.Triangles(200), 600, "Low triangles");
                Assert.LessOrEqual(b.Draws(200), 2, "Low draws");
            }
        }

        [Test]
        public void AmbientLifeBudgetsPerTier()
        {
            // Today's world budgets (tools/budgets.json): 1.4 M / 2.2 M / 3.0 M triangles at Low / Medium / High. The
            // ambient life takes at most 0.05 / 0.25 / 0.35 % of them and four draws.
            AmbientBudget m = AmbientBudget.For(Medium), h = AmbientBudget.For(High), l = AmbientBudget.For(Low);
            Assert.LessOrEqual(m.Triangles(200), 4500, "Medium triangles");
            Assert.LessOrEqual(h.Triangles(200), 7000, "High triangles");
            Assert.LessOrEqual(m.Draws(200), 4);
            Assert.LessOrEqual(h.Draws(200), 4);
            Assert.GreaterOrEqual(h.Triangles(200), m.Triangles(200));
            Assert.GreaterOrEqual(m.Triangles(200), l.Triangles(200));
            Assert.LessOrEqual(h.Lanterns(10000), AmbientBudget.MaxLanterns, "lantern lights capped");
        }

        [Test]
        public void CreatureMeshesMatchTheirTriangleConstants()
        {
            Assert.AreEqual(AmbientMeshes.SkyrayTris, AmbientMeshes.Skyray().Tris);
            Assert.AreEqual(AmbientMeshes.LoaferTris, AmbientMeshes.Loafer().Tris);
            Assert.AreEqual(AmbientMeshes.BirdTris, AmbientMeshes.Bird().Tris);
            foreach (AmbientMeshes.Part p in new[] { AmbientMeshes.Skyray(), AmbientMeshes.Loafer(), AmbientMeshes.Bird() })
            {
                Assert.AreEqual(p.V.Count, p.N.Count);
                Assert.AreEqual(p.V.Count, p.C.Count);
                foreach (int i in p.T) Assert.That(i, Is.InRange(0, p.V.Count - 1));
            }
            bool glow = false;
            foreach (Color c in AmbientMeshes.Skyray().C) glow |= c.a > 0.5f;
            Assert.IsTrue(glow, "the skyray carries glow spots for the night");
        }

        [Test]
        public void AmbientLifeNeverFliesOverThePlaySurface()
        {
            // Skybreak Basin is 112 x 72 cells; island tops are at y 0 and up, the height fog starts at y -3.5.
            foreach (AmbientCreature c in AmbientLayout.Creatures(AmbientBudget.For(High), 112, 72))
            {
                Assert.LessOrEqual(c.Height, -1.5f, "every creature flies below the island tops");
                if (c.Kind < 2) Assert.LessOrEqual(c.Height, -6f, "skyrays and loafers far below");   // 2 birds, 3 night gliders fly higher, outside the island ring
                Assert.Greater(Mathf.Abs(c.Speed), 0f);
                Assert.Less(Mathf.Abs(c.Speed) * Mathf.Max(c.RadiusX, c.RadiusZ), 6f, "slow: under 6 u/s");
            }
            foreach (AmbientSprite s in AmbientLayout.Clouds(AmbientBudget.For(High), 112, 72))
                Assert.LessOrEqual(s.Anchor.y, -4.5f, "clouds drift below the islands");
            // Deterministic: our own seed, the same layout every build.
            var a = AmbientLayout.Creatures(AmbientBudget.For(High), 112, 72);
            var b = AmbientLayout.Creatures(AmbientBudget.For(High), 112, 72);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i].Phase, b[i].Phase);
        }

        [Test]
        public void AnchoredSpritesFollowTheBudget()
        {
            var trees = new List<Vector3> { new Vector3(10, 0, -10), new Vector3(20, 0, -20) };
            var falls = new List<Vector3> { new Vector3(5, 0, -5) };
            var heads = new List<Vector3>(); var feet = new List<Vector3>();
            for (int i = 0; i < 40; i++) { heads.Add(new Vector3(i, 1.5f, -i)); feet.Add(new Vector3(i, 0f, -i)); }
            int Count(List<AmbientSprite> l, int kind) { int n = 0; foreach (var s in l) if (s.Kind == kind) n++; return n; }
            var hi = AmbientLayout.Anchored(AmbientBudget.For(High), trees, falls, heads, feet);
            Assert.AreEqual(AmbientBudget.For(High).Fireflies, Count(hi, 2));
            Assert.AreEqual(40, Count(hi, 3));
            Assert.AreEqual(40, Count(hi, 4));
            var lo = AmbientLayout.Anchored(AmbientBudget.For(Low), trees, falls, heads, feet);
            Assert.AreEqual(0, Count(lo, 2), "no fireflies on Low");
            Assert.AreEqual(0, Count(lo, 1), "no mist on Low");
            Assert.AreEqual(20, Count(lo, 3), "half the halos on Low");
        }

        // ------------------------------------------------------------------------------------------- shaders
        [Test]
        public void EveryLitWorldShaderTakesTheGrade()
        {
            string dir = Path.Combine(Application.dataPath, "_Game/Art/Shaders");
            string common = File.ReadAllText(Path.Combine(dir, "CMToonCommon.hlsl"));
            StringAssert.Contains("#include \"CMTimeOfDay.hlsl\"", common);
            StringAssert.Contains("CMTodFogColor(", common);
            StringAssert.Contains("CMTodGrade(", File.ReadAllText(Path.Combine(dir, "CMToon.shader")));
            foreach (string s in new[] { "CMWorldGround.shader", "CMToonGround.shader", "CMWorldDecal.shader" })
            {
                string src = File.ReadAllText(Path.Combine(dir, s));
                StringAssert.Contains("CMTimeOfDay.hlsl", src, s);
                StringAssert.Contains("CMTod", src.Substring(src.IndexOf("CMTimeOfDay.hlsl", StringComparison.Ordinal) + 10), s);
            }
            string tod = File.ReadAllText(Path.Combine(dir, "CMTimeOfDay.hlsl"));
            StringAssert.DoesNotContain("fwidth(", tod, "no derivatives inside the per-pixel branches");
        }
    }
}

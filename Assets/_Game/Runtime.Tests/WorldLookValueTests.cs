using System.IO;
using CriticalMass.Runtime.View.Atmosphere;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 world look, items 1 and 2 (WORLD-AUDIT #1 V-VAL-DAY, #2 V-NIGHT-DEFAULT): a capture-free check of the value
    /// structure with the CPU mirrors of the shader grades (TimeOfDayGrade.Ground / .Character / .Apply, the same maths as
    /// CMTimeOfDay.hlsl). Ground = the bible's ground swatches in their screen shares (three grass tones, three paving
    /// tones, dirt; 15% of it in cast shadow); fighter = a representative palette of the class art (team cloth, skin,
    /// steel, leather, cream, brass) over the three bands (50% lit, 30% mid, 20% shadow). Values are display luma, the
    /// number the audit's value test reads. The in-engine judge is Editor/WorldLook/WorldValueProbe (body mask, cast
    /// shadows off); these bars keep the presets from drifting back. Every case PRINTS what, value, limit.
    /// </summary>
    public sealed class WorldLookValueTests
    {
        static Color Lin(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c.linear;
        }

        static readonly (string hex, float share)[] GroundMix =
        {
            ("#ACCB56", 0.2f), ("#A3C150", 0.2f), ("#96B249", 0.2f), ("#E6C597", 0.12f), ("#D8B78C", 0.1f), ("#D39A5C", 0.08f),
        };

        static readonly (string hex, float share)[] FighterMix =
        {
            ("#2A95AE", 0.25f), ("#E8B796", 0.15f), ("#9AA3AE", 0.2f), ("#8A5A3C", 0.15f), ("#F7F0E5", 0.1f), ("#C79245", 0.15f),
        };

        /// <summary>Mean display luma of the ground behind a fighter under a preset (lit 85%, cast shadow 15%).</summary>
        public static float GroundLuma(in TimeOfDayPreset p)
        {
            float lit = 0f, sh = 0f, n = 0f;
            Color navy = Lin("#18233B");
            foreach (var (hex, w) in GroundMix)
            {
                Color g = TimeOfDayGrade.Ground(Lin(hex), p);
                Color s = Color.Lerp(g * 0.66f, navy, 0.12f);   // CM/WorldGround's shadow
                lit += w * TimeOfDayGrade.DisplayLuma(TimeOfDayGrade.Apply(g, 1f, 0f, p));
                sh += w * TimeOfDayGrade.DisplayLuma(TimeOfDayGrade.Apply(s, 0f, 0f, p));
                n += w;
            }
            return (0.85f * lit + 0.15f * sh) / n;
        }

        /// <summary>Mean display luma of a fighter's body under a preset (bands 50/30/20, character lift 1).</summary>
        public static float FighterLuma(in TimeOfDayPreset p)
        {
            float sum = 0f, n = 0f;
            foreach (var (hex, w) in FighterMix)
            {
                Color a = Lin(hex);
                float l = 0.5f * TimeOfDayGrade.DisplayLuma(TimeOfDayGrade.Apply(TimeOfDayGrade.Character(a, 2f, p), 1f, 1f, p))
                        + 0.3f * TimeOfDayGrade.DisplayLuma(TimeOfDayGrade.Apply(TimeOfDayGrade.Character(a, 1f, p), 0.5f, 1f, p))
                        + 0.2f * TimeOfDayGrade.DisplayLuma(TimeOfDayGrade.Apply(TimeOfDayGrade.Character(a, 0f, p), 0f, 1f, p));
                sum += w * l;
                n += w;
            }
            return sum / n;
        }

        /// <summary>The bars: Day and Dusk +0.12, Night +0.15 (ART-DIRECTION section 10, night rules).</summary>
        public static float Bar(TimeOfDay t) => t == TimeOfDay.Night ? 0.15f : 0.12f;

        [Test]
        public void FightersAreTheBrightestLayerInEveryPreset()
        {
            foreach (TimeOfDayPreset p in TimeOfDayPresets.All)
            {
                float g = GroundLuma(p), f = FighterLuma(p);
                Debug.Log($"[b15 value] {p.Name}: fighter body luma {f:0.000} ground {g:0.000} delta {f - g:+0.000;-0.000} (limit >= +{Bar(p.Id):0.00})");
                Assert.GreaterOrEqual(f - g, Bar(p.Id), $"{p.Name}: fighter {f:0.000} vs ground {g:0.000}");
            }
        }

        [Test]
        public void TheOldDayFailedTheSameBar()
        {
            // The control: the pre-b15 Day (identity ground, no character lift) fails, as the audit measured (0.41 vs 0.58).
            TimeOfDayPreset old = TimeOfDayPresets.Day;
            old.GroundValue = 1f; old.GroundContrast = 0f; old.GroundSaturation = 1f; old.CharGain = 1f; old.CharFill = 0f;
            float g = GroundLuma(old), f = FighterLuma(old);
            Debug.Log($"[b15 value] control old DAY: fighter {f:0.000} ground {g:0.000} delta {f - g:+0.000;-0.000} (expected below +0.12)");
            Assert.Less(f - g, 0.12f, "the control must fail the bar, or the bar measures nothing");
        }

        [Test]
        public void GrassTonesSitWithinSixPercentAfterTheGrade()
        {
            foreach (TimeOfDayPreset p in TimeOfDayPresets.All)
            {
                if (p.Id == TimeOfDay.Night) continue;   // night rules use their own bar (value is dominated by the key)
                float a = TimeOfDayGrade.DisplayLuma(TimeOfDayGrade.Apply(TimeOfDayGrade.Ground(Lin("#ACCB56"), p), 1f, 0f, p));
                float b = TimeOfDayGrade.DisplayLuma(TimeOfDayGrade.Apply(TimeOfDayGrade.Ground(Lin("#A3C150"), p), 1f, 0f, p));
                float c = TimeOfDayGrade.DisplayLuma(TimeOfDayGrade.Apply(TimeOfDayGrade.Ground(Lin("#96B249"), p), 1f, 0f, p));
                float spread = (Mathf.Max(a, Mathf.Max(b, c)) - Mathf.Min(a, Mathf.Min(b, c))) / ((a + b + c) / 3f);
                Debug.Log($"[b15 value] {p.Name}: grass tone spread {spread:0.000} (limit <= 0.060, bible AD8.6)");
                Assert.LessOrEqual(spread, 0.06f, p.Name);
            }
        }

        [Test]
        public void ThreeCharacterBandsStayDistinct()
        {
            foreach (TimeOfDayPreset p in TimeOfDayPresets.All)
                foreach (var (hex, _) in FighterMix)
                {
                    Color a = Lin(hex);
                    float l0 = TimeOfDayGrade.Luma(TimeOfDayGrade.Character(a, 0f, p)), l1 = TimeOfDayGrade.Luma(TimeOfDayGrade.Character(a, 1f, p)),
                          l2 = TimeOfDayGrade.Luma(TimeOfDayGrade.Character(a, 2f, p));
                    Assert.Less(l0, l1, $"{p.Name} {hex}: shadow under mid");
                    Assert.LessOrEqual(l1, l2, $"{p.Name} {hex}: mid under lit");
                }
        }

        [Test]
        public void IdentityNumbersLeaveColoursAlone()
        {
            TimeOfDayPreset p = TimeOfDayPresets.Day;
            p.GroundValue = 1f; p.GroundContrast = 0f; p.GroundSaturation = 1f; p.CharGain = 1f; p.CharFill = 0f;
            foreach (string hex in new[] { "#A3C150", "#E6C597", "#2A95AE", "#18233B" })
            {
                Color c = Lin(hex), g = TimeOfDayGrade.Ground(c, p);
                Assert.Less(Mathf.Abs(g.r - c.r) + Mathf.Abs(g.g - c.g) + Mathf.Abs(g.b - c.b), 1e-5f, hex);
            }
            Vector4 v = TimeOfDayDirector.GroundGradeVector(p);
            Assert.AreEqual(0f, v.x + v.y + v.z, 1e-6f, "identity ground grade is a zero vector (old look)");
        }

        [Test]
        public void DayIsTheDefaultEvenWhereAnOldBuildStoredNight()
        {
            bool hadOld = CmPrefs.HasKey(TimeOfDaySettings.OldKey), hadNew = CmPrefs.HasKey(TimeOfDaySettings.Key);
            int oldV = CmPrefs.GetInt(TimeOfDaySettings.OldKey, 0), newV = CmPrefs.GetInt(TimeOfDaySettings.Key, 0);
            try
            {
                CmPrefs.SetInt(TimeOfDaySettings.OldKey, (int)TimeOfDayChoice.Night);
                CmPrefs.DeleteKey(TimeOfDaySettings.Key);
                Assert.AreEqual(TimeOfDayChoice.Day, TimeOfDaySettings.Choice, "a stored old Night does not win over the Day default");
                Assert.IsTrue(TimeOfDaySettings.RetireOldKey());
                Assert.IsFalse(CmPrefs.HasKey(TimeOfDaySettings.OldKey));
                TimeOfDaySettings.Choice = TimeOfDayChoice.Night;
                Assert.AreEqual(TimeOfDayChoice.Night, TimeOfDaySettings.Choice, "the player's own choice is kept");
                Debug.Log("[b15 value] default time of day: DAY; old key retired; a new choice persists");
            }
            finally
            {
                if (hadOld) CmPrefs.SetInt(TimeOfDaySettings.OldKey, oldV); else CmPrefs.DeleteKey(TimeOfDaySettings.OldKey);
                if (hadNew) CmPrefs.SetInt(TimeOfDaySettings.Key, newV); else CmPrefs.DeleteKey(TimeOfDaySettings.Key);
            }
        }

        [Test]
        public void TheMoonIsAWorldPointWithParallax()
        {
            Vector3 centre = new Vector3(56f, 0f, -36f);
            Quaternion rot = Quaternion.Euler(55f, 225f, 0f);
            Vector3 fwd = rot * Vector3.forward;
            Vector3 dir = (rot * new Vector3(-0.3f, 0.2f, 1f)).normalized;
            Vector3 moon = TimeOfDayDirector.MoonAnchor(centre, fwd, dir);
            Vector3 camA = centre - fwd * 23.4f, camB = camA + new Vector3(20f, 0f, 0f);
            float shift = Vector3.Angle(moon - camA, moon - camB);
            float worldShift = Vector3.Angle(centre - camA, centre - camB);
            Debug.Log($"[b15 moon] 20 u pan: moon moves {shift:0.0} deg, island centre {worldShift:0.0} deg (moon must move, slower)");
            Assert.Greater(shift, 0.5f, "not pinned to the screen");
            Assert.Less(shift, worldShift * 0.5f, "farther than the island: it slides slower");
            Assert.Less(Vector3.Angle(moon - camA, dir), 0.01f, "centred camera: on the authored viewport point");
        }

        [Test]
        public void GroundAndCharacterShadersTakeTheB15Grade()
        {
            string dir = Path.Combine(Application.dataPath, "_Game", "Art", "Shaders");
            string tod = File.ReadAllText(Path.Combine(dir, "CMTimeOfDay.hlsl"));
            StringAssert.Contains("half3 CMTodGroundGrade(", tod);
            StringAssert.Contains("float4 _CMTodGround;", tod);
            StringAssert.Contains("float4 _CMTodChar;", tod);
            StringAssert.Contains("CMTodGroundGrade(col)", File.ReadAllText(Path.Combine(dir, "CMWorldGround.shader")));
            StringAssert.Contains("CMTodGroundGrade(albedo)", File.ReadAllText(Path.Combine(dir, "CMToonGround.shader")));
            string toon = File.ReadAllText(Path.Combine(dir, "CMToon.shader"));
            StringAssert.Contains("CMTodCharacterBand(band, charW)", toon);
            StringAssert.Contains("CMTodCharacterGain(lit, charW)", toon);
        }
    }
}

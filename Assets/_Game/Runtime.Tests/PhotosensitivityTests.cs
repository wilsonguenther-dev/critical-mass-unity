using System.Collections.Generic;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Runtime.View.Reactions;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 safe defaults, item 1 (blind-spot audit C1): on a fresh profile no big moment or impact edge flashes more than
    /// three times in any one second, nothing inverts the whole screen, and every big-moment flash is a soft, brightness-
    /// limited pulse. Limits from WCAG 2.1 SC 2.3.1 (cited in View.Moments.FlashSafety). Every case prints one line.
    /// </summary>
    public sealed class PhotosensitivityTests
    {
        string _savedKey;
        bool _hadKey;

        [SetUp]
        public void SetUp()
        {
            _hadKey = PlayerPrefs.HasKey(GameSettings.IntenseFlashingKey);
            _savedKey = _hadKey ? PlayerPrefs.GetInt(GameSettings.IntenseFlashingKey).ToString() : null;
            PlayerPrefs.DeleteKey(GameSettings.IntenseFlashingKey);   // a fresh profile
            BigMomentsSettings.ClearOverrides();
            FlashSafety.IntenseOverride = null;
            FlashSafety.ResetShared();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadKey) PlayerPrefs.SetInt(GameSettings.IntenseFlashingKey, int.Parse(_savedKey));
            else PlayerPrefs.DeleteKey(GameSettings.IntenseFlashingKey);
            BigMomentsSettings.ClearOverrides();
            FlashSafety.IntenseOverride = null;
            FlashSafety.ResetShared();
        }

        [Test]
        public void AFreshProfileGetsTheSafeFlashLook()
        {
            Assert.IsFalse(GameSettings.IntenseFlashing, "INTENSE FLASHING is off by default");
            Assert.IsTrue(GameSettings.ReduceFlashing);
            Assert.IsTrue(BigMomentsSettings.ReduceFlashing, "big moments read the same default");
            Assert.IsFalse(FlashSafety.IntenseAllowed);
            Assert.AreEqual(3, SettingsExtras.MaxFlashesPerSecond);
            MomentLook look = BigMomentsSettings.Look();
            Assert.IsFalse(look.Invert, "no whole-screen invert by default");
            Assert.LessOrEqual(look.FlashAlpha, FlashSafety.SafeFlashAlpha);
            Assert.GreaterOrEqual(look.FlashSeconds, FlashSafety.SafeFlashSeconds);
            float pulse = FeelGate.PulseHz(18f, GameSettings.ReduceFlashing);
            Assert.LessOrEqual(pulse, FeelGate.ReducedPulseHz);
            TestContext.WriteLine("fresh profile: intense=" + GameSettings.IntenseFlashing + " invert=" + look.Invert +
                                  " flashAlpha=" + look.FlashAlpha + " (limit " + FlashSafety.SafeFlashAlpha + ") flashSeconds=" + look.FlashSeconds +
                                  " (min " + FlashSafety.SafeFlashSeconds + ") pulseCeilingHz=" + pulse + " (limit " + FeelGate.ReducedPulseHz + ")");

            // The old key from earlier builds no longer turns the cap off: an older "reduce flashing 0" save is ignored.
            bool hadOld = PlayerPrefs.HasKey(GameSettings.ReduceFlashingKey);
            int old = PlayerPrefs.GetInt(GameSettings.ReduceFlashingKey, 0);
            try
            {
                PlayerPrefs.SetInt(GameSettings.ReduceFlashingKey, 0);
                Assert.IsTrue(GameSettings.ReduceFlashing, "a legacy key never lifts the safe default");
            }
            finally
            {
                if (hadOld) PlayerPrefs.SetInt(GameSettings.ReduceFlashingKey, old); else PlayerPrefs.DeleteKey(GameSettings.ReduceFlashingKey);
            }
        }

        [Test]
        public void EveryBigMomentRecipeMeetsTheFlashLimitsByDefault()
        {
            MomentLook look = BigMomentsSettings.Look();
            int recipes = 0;
            foreach (MomentId id in MomentRecipes.Gallery())
            {
                MomentRecipe r = MomentRecipes.Get(id);
                FlashMeasure m = FlashSafety.Measure(r, look);
                bool ok = !m.Invert && m.Starts <= FlashSafety.MaxFlashesPerSecond && m.PulseHz <= FlashSafety.MaxFlashesPerSecond &&
                          (m.Starts == 0 || (look.FlashAlpha <= FlashSafety.SafeFlashAlpha && look.FlashSeconds >= FlashSafety.SafeFlashSeconds));
                TestContext.WriteLine("moment " + id + ": scale=" + r.Scale + " starts=" + m.Starts + " invert=" + m.Invert +
                                      " screenShare=" + m.ScreenShare.ToString("0.00") + " luminanceStep=" + m.LuminanceStep.ToString("0.00") +
                                      " countsAsFlash=" + m.CountsAsFlash + " pulseHz=" + m.PulseHz + " limit: starts<=3 per second, no invert, alpha<=" +
                                      FlashSafety.SafeFlashAlpha + " -> " + (ok ? "PASS" : "FAIL"));
                Assert.IsTrue(ok, id + " breaks the default flash limits");
                recipes++;
            }
            Assert.Greater(recipes, 10, "every recipe was measured");

            // With intense flashing (opt-in) the invert comes back for the impact recipes: the switch really does something.
            MomentLook intense = MomentLook.Resolve(false, false, false);
            Assert.IsTrue(FlashSafety.Measure(MomentRecipes.Get(MomentId.HillCleared), intense).Invert);
        }

        /// <summary>Most flash starts inside any one-second window (half-open), over sorted start times.</summary>
        static int MaxInAnySecond(List<double> t)
        {
            int best = 0;
            for (int i = 0; i < t.Count; i++)
            {
                int n = 0;
                for (int j = i; j < t.Count && t[j] - t[i] < 1.0 - 1e-9; j++) n++;
                if (n > best) best = n;
            }
            return best;
        }

        /// <summary>Worst case: the director asks for an impact takeover and the camera for an edge flash on EVERY frame at
        /// 60 fps for 10 s. Returns the flash start times (moments and edges together).</summary>
        static List<double> Storm(out int moments, out int edges)
        {
            FlashSafety.ResetShared();
            var own = new View.Moments.FlashLimiter();
            MomentLook look = BigMomentsSettings.Look();
            MomentRecipe impact = MomentRecipes.Get(MomentId.BlastRadius);
            var starts = new List<double>();
            moments = edges = 0;
            double lastEdge = -10;
            for (int f = 0; f < 600; f++)
            {
                double t = f / 60.0;
                FlashSafety.DecideMoment(impact.Layers, look, own, t, t, true, out bool flash, out bool invert);
                if (flash) { starts.Add(t); moments++; }
                if (invert) { starts.Add(t); moments++; }
                if (FlashSafety.EdgeFlashAllowed(t, lastEdge, CameraResponse.MinFlashGap))
                {
                    lastEdge = t;
                    starts.Add(t);
                    edges++;
                }
            }
            starts.Sort();
            return starts;
        }

        [Test]
        public void BigMomentsAndImpactEdgesTogetherStartAtMostThreeFlashesInAnySecond()
        {
            List<double> safe = Storm(out int moments, out int edges);
            int worst = MaxInAnySecond(safe);
            TestContext.WriteLine("default storm (60 fps x 10 s, a takeover and an edge asked every frame): starts=" + safe.Count +
                                  " moments=" + moments + " edges=" + edges + " maxInAnySecond=" + worst + " (limit " + FlashSafety.MaxFlashesPerSecond + ")");
            Assert.LessOrEqual(worst, FlashSafety.MaxFlashesPerSecond, "WCAG 2.3.1: three flashes in any one second");
            Assert.Greater(safe.Count, 20, "the budget still lets flashes through (about 3 a second)");

            // Opt-in intense flashing: each system keeps its own cap (printed for the record; the player chose it).
            FlashSafety.IntenseOverride = true;
            BigMomentsSettings.ReduceFlashingOverride = false;
            List<double> intense = Storm(out int m2, out int e2);
            TestContext.WriteLine("intense storm (opt-in): starts=" + intense.Count + " moments=" + m2 + " edges=" + e2 +
                                  " maxInAnySecond=" + MaxInAnySecond(intense) + " (no shared budget; each system capped at 3)");
            Assert.Greater(intense.Count, safe.Count, "the opt-in really lifts the shared budget");
        }

        [Test]
        public void FullScreenAndEdgeFlashesAreLargeEnoughToCountSoTheyShareTheBudget()
        {
            // 341 x 256 of 1024 x 768 is a 10 degree field; a flash over more than a quarter of it counts (WCAG 2.3.1).
            Assert.AreEqual(0.111, FlashSafety.TenDegreeFieldShare, 0.001);
            Assert.AreEqual(0.0278, FlashSafety.AreaThresholdShare, 0.0005);
            Assert.Greater(FlashSafety.ImpactEdgeShare, FlashSafety.AreaThresholdShare, "an impact edge is a counted flash");
            MomentLook look = BigMomentsSettings.Look();
            FlashMeasure takeover = FlashSafety.Measure(MomentRecipes.Get(MomentId.OpeningStrike), look);
            Assert.IsTrue(takeover.CountsAsFlash, "the white pulse still counts, so it is rate-limited, not exempt");
            TestContext.WriteLine("area: tenDegreeField=" + FlashSafety.TenDegreeFieldShare.ToString("0.000") + " threshold=" +
                                  FlashSafety.AreaThresholdShare.ToString("0.0000") + " impactEdge=" + FlashSafety.ImpactEdgeShare +
                                  " takeoverFlash luminanceStep=" + takeover.LuminanceStep.ToString("0.00") + " counts=" + takeover.CountsAsFlash);
        }
        [Test]
        public void TheNoticeShowsOnceOnAPersonsFirstLaunchOnly()
        {
            var title = CriticalMass.Runtime.Flow.FlowState.Title;
            string[] person = { "Critical Mass" };
            Assert.IsTrue(CriticalMass.Runtime.Menus.PhotosensitivityNotice.ShouldShow(true, title, false, false, false, person), "first launch");
            Assert.IsFalse(CriticalMass.Runtime.Menus.PhotosensitivityNotice.ShouldShow(true, title, false, false, true, person), "dismissed once: never again");
            Assert.IsFalse(CriticalMass.Runtime.Menus.PhotosensitivityNotice.ShouldShow(true, title, true, false, false, person), "never in autoplay");
            Assert.IsFalse(CriticalMass.Runtime.Menus.PhotosensitivityNotice.ShouldShow(true, title, false, true, false, person), "never in a capture run");
            Assert.IsFalse(CriticalMass.Runtime.Menus.PhotosensitivityNotice.ShouldShow(false, title, false, false, false, person), "not in the editor");
            Assert.IsFalse(CriticalMass.Runtime.Menus.PhotosensitivityNotice.ShouldShow(true, CriticalMass.Runtime.Flow.FlowState.Match, false, false, false, person), "never into a match");
            Assert.IsFalse(CriticalMass.Runtime.Menus.PhotosensitivityNotice.ShouldShow(true, title, false, false, false, new[] { "x", "-flowShots" }), "never in an automation run");
            Assert.AreEqual(1f, CriticalMass.Runtime.Menus.PhotosensitivityNotice.DismissLockSeconds, 1e-6f);

            string body = CriticalMass.Runtime.Menus.PhotosensitivityNotice.Body;
            StringAssert.Contains("seizure", body);
            StringAssert.Contains("three a second", body, "it says what the default does");
            StringAssert.Contains("Intense flashing", body, "it says where to change it");
            foreach (string jargon in new[] { "WCAG", "photosensitive epilepsy", "luminance", "Hz" })
                StringAssert.DoesNotContain(jargon, body, "plain words only");
            int words = body.Split(new[] { ' ', '\n' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
            TestContext.WriteLine("notice: " + words + " words, dismiss lock " + CriticalMass.Runtime.Menus.PhotosensitivityNotice.DismissLockSeconds + " s, key " +
                                  CriticalMass.Runtime.Menus.PhotosensitivityNotice.SeenKey);
            Assert.LessOrEqual(words, 110, "one screen");
        }
    }
}

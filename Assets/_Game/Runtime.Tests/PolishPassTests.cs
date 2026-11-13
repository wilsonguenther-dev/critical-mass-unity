using System;
using System.Collections.Generic;
using System.Reflection;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Polish pass 1 (2026-11-12): every screen contained, consistent and complete. Builds each menu screen's layout in
    /// EditMode and checks, at the four target resolutions and the largest text size: nothing leaves the 1920x1080 frame,
    /// no line outgrows its box, every focusable item has five distinct looks and a way in and out, one default focus,
    /// no COMING SOON or programmer abbreviations. Then the HUD zones never overlap (except the one arbitrated pair) and
    /// every overlay sits inside its zone; the priority arbiter; text contrast from the colour constants; the motion rules.
    /// </summary>
    [TestFixture]
    public sealed class PolishPassTests
    {
        const float Tol = 1.5f;

        static bool Inside(Rect inner, Rect outer, float tol = Tol) =>
            inner.xMin >= outer.xMin - tol && inner.xMax <= outer.xMax + tol && inner.yMin >= outer.yMin - tol && inner.yMax <= outer.yMax + tol;

        static bool Overlaps(Rect a, Rect b) =>
            a.xMin < b.xMax - 0.01f && b.xMin < a.xMax - 0.01f && a.yMin < b.yMax - 0.01f && b.yMin < a.yMax - 0.01f;

        /// <summary>The rect of rt in the local space of <paramref name="space"/> (y up, centre origin).</summary>
        static Rect In(RectTransform space, RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector3 a = space.InverseTransformPoint(c[0]), b = space.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static Rect Frame => new Rect(-HudLayout.RefW * 0.5f, -HudLayout.RefH * 0.5f, HudLayout.RefW, HudLayout.RefH);

        // ---------------------------------------------------------------- the screens under test

        delegate FlowScreen Maker(GameObject go);

        static readonly KeyValuePair<string, Maker>[] Screens =
        {
            new KeyValuePair<string, Maker>("Home", go => { var s = go.AddComponent<MainMenu>(); s.EnsureBuilt(); return s; }),
            new KeyValuePair<string, Maker>("Mode select", go => { var s = go.AddComponent<ModeSelect>(); s.EnsureBuilt(); return s; }),
            new KeyValuePair<string, Maker>("Match setup", go => { var s = go.AddComponent<MatchSetup>(); s.EnsureBuilt(); return s; }),
            new KeyValuePair<string, Maker>("Ready", go =>
            {
                var s = go.AddComponent<ReadyScreen>();
                MatchSetupChoice c = PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 8);
                c.Class = ClassId.Miner;
                s.Bind(new PlayLaunch { Choice = c });
                return s;
            }),
            new KeyValuePair<string, Maker>("Results", go => { var s = go.AddComponent<ResultsScreen>(); s.EnsureBuilt(); return s; }),
        };

        static void ForEachScreen(Action<string, FlowScreen> check)
        {
            foreach (KeyValuePair<string, Maker> m in Screens)
            {
                var go = new GameObject("Polish " + m.Key);
                try
                {
                    FlowScreen s = m.Value(go);
                    Assert.IsNotNull(s.Canvas, m.Key + " built a canvas");
                    check(m.Key, s);
                }
                finally { Object.DestroyImmediate(go); }
            }
        }

        static RectTransform SpaceOf(FlowScreen s) => (RectTransform)s.Canvas.transform.Find("Rise/Space");

        // ---------------------------------------------------------------- 1. type scale

        [Test]
        public void TheTypeScaleIsOrderedAndSnapLandsOnIt()
        {
            for (int i = 1; i < UiScale.TypeSteps.Length; i++) Assert.Less(UiScale.TypeSteps[i - 1], UiScale.TypeSteps[i]);
            for (int px = 8; px <= 140; px++) CollectionAssert.Contains(UiScale.TypeSteps, UiScale.Snap(px), px.ToString());
            Assert.AreEqual(UiScale.TypeSmall, UiScale.Snap(20), "ties go down so a box never gets less room");
        }

        [Test]
        public void EveryMenuLineIsOnTheTypeScale()
        {
            ForEachScreen((name, s) =>
            {
                foreach (Text t in s.Canvas.GetComponentsInChildren<Text>(true))
                {
                    // Integration batch 5: keycap letters are glyph art sized from the glyph's height (InputGlyphView.CapScale),
                    // not menu lines (the Home fighter's ENTER CUSTOMISE chip is the first screen to build one in this test).
                    if (t.GetComponentInParent<InputGlyphView>(true) != null) continue;
                    int size = t.resizeTextForBestFit ? t.resizeTextMaxSize : t.fontSize;
                    if (s.TextScale != null && Mathf.Abs(s.TextScale.Applied - 1f) > 1e-3f) continue;
                    CollectionAssert.Contains(UiScale.TypeSteps, size, $"{name}: {Path(t.transform)} is {size} px, off the type scale");
                }
            });
        }

        // ---------------------------------------------------------------- 2. containment

        [Test]
        public void EveryScreenFitsTheFrameAtAllFourResolutions()
        {
            foreach (Vector2Int r in UiScale.AllResolutions)
            {
                float k = HudLayout.Scale(r.x, r.y);
                // The space is scaled uniformly and centred: it must fit, and the smallest text must stay readable.
                Assert.LessOrEqual(HudLayout.RefW * k, r.x + 0.5f, r.ToString());
                Assert.LessOrEqual(HudLayout.RefH * k, r.y + 0.5f, r.ToString());
                Assert.GreaterOrEqual(UiScale.TypeMin * k, 10.5f, r + ": the smallest line drops under 11 px");
            }
            ForEachScreen((name, s) =>
            {
                RectTransform space = SpaceOf(s);
                Assert.IsNotNull(space, name);
                foreach (RectTransform rt in space.GetComponentsInChildren<RectTransform>(false))
                {
                    if (rt == space || (rt.GetComponent<Graphic>() == null && rt.GetComponent<MenuItem>() == null)) continue;
                    if (rt.GetComponent<Graphic>() is Graphic g && g.color.a <= 0.01f) continue;
                    Rect a = In(space, rt);
                    Assert.IsTrue(Inside(a, Frame), $"{name}: {Path(rt)} {a} leaves the 1920x1080 frame");
                }
            });
        }

        [Test]
        public void NoLineOutgrowsItsBoxAtTheLargestTextSize()
        {
            ForEachScreen((name, s) =>
            {
                Assert.IsNotNull(s.TextScale, name + " captures its text for the UI scale setting");
                s.TextScale.Apply(UiScale.LargestUserScale);
                CheckTexts(name, s.Canvas.transform);
                s.TextScale.Apply(1f);
                CheckTexts(name, s.Canvas.transform);
            });
        }

        static void CheckTexts(string name, Transform root)
        {
            foreach (Text t in root.GetComponentsInChildren<Text>(false))
            {
                if (!t.enabled || string.IsNullOrEmpty(t.text) || t.font == null) continue;
                Rect r = t.rectTransform.rect;
                float outline = 3f;
                if (t.resizeTextForBestFit)
                {
                    Assert.IsTrue(UiScale.FitsWrapped(t.font, t.text, t.resizeTextMaxSize, t.resizeTextMinSize, r.width, r.height + 2f * outline, outline),
                        $"{name}: \"{t.text}\" ({Path(t.transform)}) does not fit {r.width}x{r.height} even at {t.resizeTextMinSize} px");
                }
                else if (t.horizontalOverflow == HorizontalWrapMode.Overflow)
                {
                    Assert.LessOrEqual(UiScale.PreferredWidth(t.font, t.text, t.fontSize), r.width + 2f,
                        $"{name}: \"{t.text}\" ({Path(t.transform)}) runs past its {r.width} px box");
                }
                else
                {
                    Assert.IsTrue(UiScale.FitsWrapped(t.font, t.text, t.fontSize, t.fontSize, r.width, r.height + 2f * outline, outline),
                        $"{name}: \"{t.text}\" ({Path(t.transform)}) wraps past its {r.width}x{r.height} box");
                }
            }
        }

        [Test]
        public void ButtonLabelsStayInsideTheirButtons()
        {
            ForEachScreen((name, s) =>
            {
                RectTransform space = SpaceOf(s);
                foreach (MenuItem it in space.GetComponentsInChildren<MenuItem>(false))
                {
                    if (it.Label == null) continue;
                    Rect body = In(space, (RectTransform)it.transform), label = In(space, it.Label.rectTransform);
                    Assert.IsTrue(Inside(label, body), $"{name}: the label of {Path(it.transform)} leaves its button");
                }
            });
        }

        // ---------------------------------------------------------------- 3. states and focus

        static readonly MethodInfo DoState = typeof(MenuItem).GetMethod("DoStateTransition", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly Type StateType = typeof(Selectable).GetNestedType("SelectionState", BindingFlags.NonPublic | BindingFlags.Public);

        static string Look(MenuItem it, string state)
        {
            DoState.Invoke(it, new[] { Enum.Parse(StateType, state), (object)true });
            Color f = it.Body != null ? it.Body.FillColor : Color.clear;
            Color l = it.Label != null ? it.Label.color : Color.clear;
            float rim = it.Body != null ? it.Body.RimPx : 0f;
            return $"{f}|{l}|{rim:0.0}|{it.transform.localScale.x:0.000}";
        }

        [Test]
        public void EveryButtonHasFiveDistinctLooksAndANavigationPath()
        {
            Assert.IsNotNull(DoState, "MenuItem.DoStateTransition");
            Assert.IsNotNull(StateType, "Selectable.SelectionState");
            ForEachScreen((name, s) =>
            {
                Assert.IsNotNull(s.DefaultItem, name + " has a default focus");
                Assert.IsTrue(s.DefaultItem.interactable, name + "'s default focus can be pressed");
                MenuItem.PointerNavigated = false;
                foreach (MenuItem it in s.Canvas.GetComponentsInChildren<MenuItem>(false))
                {
                    if (!it.interactable) continue;
                    var looks = new HashSet<string>
                    {
                        Look(it, "Normal"), Look(it, "Highlighted"), Look(it, "Pressed"), Look(it, "Selected"), Look(it, "Disabled"),
                    };
                    Look(it, "Normal");
                    Assert.AreEqual(5, looks.Count, $"{name}: {Path(it.transform)} has {looks.Count} distinct looks, not 5 (normal, hover, pressed, focused, disabled)");
                    Navigation n = it.navigation;
                    Assert.AreNotEqual(Navigation.Mode.None, n.mode, $"{name}: {Path(it.transform)} cannot be reached by keyboard or controller");
                    if (n.mode == Navigation.Mode.Explicit)
                        Assert.IsTrue(n.selectOnUp != null || n.selectOnDown != null || n.selectOnLeft != null || n.selectOnRight != null,
                            $"{name}: {Path(it.transform)} is a navigation dead end");
                }
            });
        }

        [Test]
        public void NoScreenShowsComingSoonOrProgrammerAbbreviations()
        {
            ForEachScreen((name, s) =>
            {
                foreach (Text t in s.Canvas.GetComponentsInChildren<Text>(false))
                {
                    if (!t.enabled || string.IsNullOrEmpty(t.text)) continue;
                    string up = t.text.ToUpperInvariant();
                    StringAssert.DoesNotContain("COMING SOON", up, name + ": " + Path(t.transform));
                    StringAssert.DoesNotContain("TODO", up, name + ": " + Path(t.transform));
                    StringAssert.DoesNotContain("PLACEHOLDER", up, name + ": " + Path(t.transform));
                    foreach (string b in InputGlyphs.Banned)
                        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(up, "\\b" + b + "\\b"), $"{name}: \"{t.text}\" uses {b}");
                }
            });
        }

        // ---------------------------------------------------------------- 4. motion

        [Test]
        public void MotionHonoursReduceMotion()
        {
            try
            {
                UiMotion.ReduceOverride = false;
                Assert.AreEqual(0f, UiMotion.Enter(0f), 1e-4f);
                Assert.AreEqual(1f, UiMotion.Enter(UiMotion.EnterSeconds), 1e-4f);
                Assert.AreEqual(UiMotion.RisePx, UiMotion.Rise);
                Assert.Greater(UiMotion.Exit(0.01f), 0f, "a short fade out, not a pop");
                Assert.AreEqual(0f, UiMotion.Exit(UiMotion.ExitSeconds), 1e-4f);
                Assert.Less(UiMotion.Count(0f, 100f, 0.1f), 100f, "numbers count up");
                Assert.Less(UiMotion.Bar(0f, 1f, 0.016f), 1f, "bars ease");
                Assert.LessOrEqual(UiMotion.EnterSeconds, 0.25f, "under 250 ms (code-agent rule 6)");

                UiMotion.ReduceOverride = true;
                Assert.AreEqual(0f, UiMotion.Rise, "no rise with Reduce motion");
                Assert.AreEqual(0f, UiMotion.Exit(0f), "hides at once with Reduce motion");
                Assert.AreEqual(100f, UiMotion.Count(0f, 100f, 0f), "counts land at once");
                Assert.AreEqual(1f, UiMotion.Bar(0f, 1f, 0.016f), "bars land at once");
            }
            finally { UiMotion.ReduceOverride = null; }
        }

        [Test]
        public void AHiddenScreenStopsTakingInputAtOnceAndFinishesItsFade()
        {
            var go = new GameObject("Polish Hide");
            try
            {
                UiMotion.ReduceOverride = true;
                MainMenu m = go.AddComponent<MainMenu>();
                m.Show();
                Assert.IsTrue(m.Canvas.enabled);
                m.Hide();
                Assert.IsFalse(m.IsVisible);
                Assert.IsFalse(m.Canvas.enabled, "Reduce motion: hidden at once");
                UiMotion.ReduceOverride = false;
                m.Show();
                m.Hide();
                Assert.IsFalse(m.IsVisible, "input goes elsewhere on the first frame");
                CanvasGroup g = m.Canvas.GetComponent<CanvasGroup>();
                Assert.IsFalse(g.blocksRaycasts, "a leaving screen never eats clicks");
            }
            finally
            {
                UiMotion.ReduceOverride = null;
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- 7. HUD zones

        [Test]
        public void HudZonesNeverOverlapAtAllFourResolutions()
        {
            foreach (Vector2Int r in UiScale.AllResolutions)
            {
                for (int i = 0; i < HudZones.All.Length; i++)
                {
                    Rect a = HudZones.PixelRect(HudZones.All[i], r.x, r.y);
                    Assert.IsTrue(Inside(a, new Rect(0f, 0f, r.x, r.y)), $"{r}: {HudZones.All[i]} {a} leaves the screen");
                    for (int j = i + 1; j < HudZones.All.Length; j++)
                    {
                        if (HudZones.Arbitrated(HudZones.All[i], HudZones.All[j])) continue;
                        Rect b = HudZones.PixelRect(HudZones.All[j], r.x, r.y);
                        Assert.IsFalse(Overlaps(a, b), $"{r}: {HudZones.All[i]} {a} overlaps {HudZones.All[j]} {b}");
                    }
                }
            }
        }

        static Rect TopCentre(float centreFromTop, float w, float h) => new Rect(960f - w * 0.5f, centreFromTop - h * 0.5f, w, h);

        [Test]
        public void EveryOverlaySitsInsideItsZone()
        {
            // Event banner: plate, its two discs (icon disc 150 px at 40 px in, badge disc 104 px at 34 px in) and the 18 px lift.
            float bx0 = 960f - Banner.Width * 0.5f + 40f - 75f, bx1 = 960f + Banner.Width * 0.5f - 34f + 52f;
            var banner = Rect.MinMaxRect(bx0, Banner.CentreFromTop - Banner.Height * 0.5f - 18f, bx1, Banner.CentreFromTop + Banner.Height * 0.5f);
            AssertIn("event banner", banner, HudZone.Callout);
            AssertIn("autopilot banner", new Rect(960f - PlayerPresence.BannerW * 0.5f, PlayerPresence.BannerTop, PlayerPresence.BannerW, PlayerPresence.BannerH), HudZone.Callout);
            AssertIn("respawn notice", new Rect(960f - 380f, PlayerPresence.BannerTop + PlayerPresence.BannerH + 8f, 760f, 44f), HudZone.Callout);
            AssertIn("lockout warning", new Rect(960f - Game.HumanControlGuard.WarningW * 0.5f, HudZones.AlertTop, Game.HumanControlGuard.WarningW, 48f), HudZone.Callout);
            AssertIn("kaiju callout (with its drop-in)", TopCentre(View.KaijuMoments.CalloutCentre - 14f, 520f, 54f), HudZone.Callout);
            AssertIn("kaiju callout (resting)", TopCentre(View.KaijuMoments.CalloutCentre, 520f, 54f), HudZone.Callout);
            AssertIn("kill feed", new Rect(1920f - KillFeed.Right - KillFeed.LineW, KillFeed.Top, KillFeed.LineW, KillFeed.Pitch * KillFeed.Lines), HudZone.Feed);
            AssertIn("big-moment stinger", new Rect(1920f - View.Moments.BigMoments.StingerRight - View.Moments.BigMoments.StingerW,
                View.Moments.BigMoments.StingerTop, View.Moments.BigMoments.StingerW, View.Moments.BigMoments.StingerH), HudZone.Stinger);
            AssertIn("achievement toast", new Rect(1920f - AchievementToast.ToastRight - AchievementToast.ToastW, AchievementToast.ToastTop,
                AchievementToast.ToastW, AchievementToast.ToastH), HudZone.Toast);
            AssertIn("mascot (upper)", new Rect(MascotGuide.Margin, MascotGuide.UpperY, MascotGuide.PanelW, MascotGuide.PanelH), HudZone.Guide);
            AssertIn("mascot (lower)", new Rect(MascotGuide.Margin, MascotGuide.LowerY, MascotGuide.PanelW, MascotGuide.PanelH), HudZone.Guide);
            AssertIn("death recap", new Rect(960f - DeathRecap.CardW * 0.5f, DeathRecap.TopY, DeathRecap.CardW, DeathRecap.CardH), HudZone.Callout);   // owner walk 1: centre lane
            AssertIn("you are the ... line", new Rect(960f - PlayerPresence.IntroW * 0.5f, 1080f - 250f - 120f, PlayerPresence.IntroW, 120f), HudZone.Lower);
        }

        static void AssertIn(string what, Rect r, HudZone z) =>
            Assert.IsTrue(Inside(r, HudZones.Of(z), 0.5f), $"{what} {r} leaves its zone {z} {HudZones.Of(z)}");

        [Test]
        public void TheHigherPriorityHolderWinsASharedZoneForOneFrameOfGrace()
        {
            try
            {
                HudZones.Clear();
                HudZones.FrameOverride = 100;
                Assert.IsFalse(HudZones.Blocked(HudZone.Callout, HudPriority.Banner));
                HudZones.Hold(HudZone.Callout, HudPriority.Autopilot);
                Assert.IsTrue(HudZones.Blocked(HudZone.Callout, HudPriority.Banner), "autopilot outranks an event banner");
                Assert.IsTrue(HudZones.Blocked(HudZone.Callout, HudPriority.KaijuCallout));
                Assert.IsFalse(HudZones.Blocked(HudZone.Callout, HudPriority.Autopilot), "a holder is not blocked by itself");
                Assert.IsFalse(HudZones.Blocked(HudZone.Lower, HudPriority.Guide), "zones are separate");
                HudZones.FrameOverride = 101;
                Assert.IsTrue(HudZones.Blocked(HudZone.Callout, HudPriority.Banner), "update order never makes it flicker");
                HudZones.FrameOverride = 102;
                Assert.IsFalse(HudZones.Blocked(HudZone.Callout, HudPriority.Banner), "released when no longer held");
                HudZones.Hold(HudZone.Lower, HudPriority.Recap);
                Assert.IsTrue(HudZones.Blocked(HudZone.Lower, HudPriority.Guide), "the mascot steps back for the death recap");
            }
            finally
            {
                HudZones.FrameOverride = -1;
                HudZones.Clear();
            }
        }

        // ---------------------------------------------------------------- 8. accessibility

        [Test]
        public void EveryTextColourReadsAtFourAndAHalfToOneOnItsPanel()
        {
            foreach (UiContrast.Pair p in UiContrast.TextPairs)
                Assert.GreaterOrEqual(p.Ratio, UiContrast.TextMinimum, $"{p.Name}: {p.Ratio:0.00} : 1");
            Assert.Less(UiContrast.Ratio(CMTokens.Crimson.Color, CMTokens.PanelNavy.Color), UiContrast.TextMinimum,
                "bible Crimson is a fill colour; text uses CrimsonInk (this guards the reason the ink exists)");
        }

        [Test]
        public void TeamIsNeverColourAlone()
        {
            Assert.AreNotEqual(GameHud.TeamDot(TeamId.Azure), GameHud.TeamDot(TeamId.Crimson), "map dots differ by shape");
            Assert.AreNotEqual(UiIcons.Crest[(int)TeamId.Azure], UiIcons.Crest[(int)TeamId.Crimson], "crests differ (scoreboard, kill feed)");
        }

        [Test]
        public void TheTextSizeSettingGrowsMenuLinesInsideTheirBoxes()
        {
            ForEachScreen((name, s) =>
            {
                Assert.Greater(s.TextScale.Count, 0, name);
                s.TextScale.Apply(UiScale.LargestUserScale);
                foreach (Text t in s.Canvas.GetComponentsInChildren<Text>(true))
                {
                    // The key-hint glyph strip is built on first show and sized by PromptStrip itself.
                    if (string.IsNullOrEmpty(t.text) || Path(t.transform).Contains("Hint Prompts")) continue;
                    Assert.IsTrue(t.resizeTextForBestFit, $"{name}: {Path(t.transform)} must best-fit inside its box at 130 %");
                    Assert.AreEqual(VerticalWrapMode.Truncate, t.verticalOverflow, $"{name}: {Path(t.transform)} is bounded");
                }
                s.TextScale.Apply(1f);
            });
        }

        // ---------------------------------------------------------------- helpers

        static string Path(Transform t)
        {
            string p = t.name;
            for (Transform x = t.parent; x != null && x.name != "Space"; x = x.parent) p = x.name + "/" + p;
            return p;
        }
    }
}

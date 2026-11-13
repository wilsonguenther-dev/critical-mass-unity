using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Secrets;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// fix/ui-rough-spots (2026-11-12): the one results layout never overlaps at 1280x720 or 2560x1440 and stays inside the
    /// title-safe margins; class names and words-pack lines fit their rects at the sizes the screens may shrink them to; the
    /// shared controls resolve by context; the hill-alone secret is captioned once.
    /// </summary>
    [TestFixture]
    public sealed class UiRoughSpotsTests
    {
        static bool Overlaps(Rect a, Rect b) => a.xMin < b.xMax - 0.01f && b.xMin < a.xMax - 0.01f && a.yMin < b.yMax - 0.01f && b.yMin < a.yMax - 0.01f;

        static bool Inside(Rect inner, Rect outer) =>
            inner.xMin >= outer.xMin - 0.01f && inner.xMax <= outer.xMax + 0.01f && inner.yMin >= outer.yMin - 0.01f && inner.yMax <= outer.yMax + 0.01f;

        // ---------------------------------------------------------------- results layout

        [Test]
        public void ResultsZonesNeverOverlapAtEitherResolution()
        {
            var zones = new List<KeyValuePair<string, Rect>>(ResultsLayout.Zones());
            foreach (Vector2Int res in UiScale.CheckResolutions)
            {
                for (int i = 0; i < zones.Count; i++)
                for (int j = i + 1; j < zones.Count; j++)
                {
                    Rect a = ResultsLayout.OnScreen(zones[i].Value, res.x, res.y), b = ResultsLayout.OnScreen(zones[j].Value, res.x, res.y);
                    Assert.IsFalse(Overlaps(a, b), $"{res.x}x{res.y}: {zones[i].Key} {a} overlaps {zones[j].Key} {b}");
                }
            }
        }

        [Test]
        public void EveryResultsPageStaysInsideTheContentAreaAndItsPanelsNeverOverlap()
        {
            foreach (ResultsTab tab in new[] { ResultsTab.Summary, ResultsTab.Rewards, ResultsTab.Achievements, ResultsTab.Highlights })
            {
                List<KeyValuePair<string, Rect>> panels = ResultsLayout.Panels(tab, RewardsPanel.Width, RewardsPanel.PanelHeight);
                Assert.Greater(panels.Count, 0, tab.ToString());
                foreach (Vector2Int res in UiScale.CheckResolutions)
                {
                    Rect content = ResultsLayout.OnScreen(ResultsLayout.Content, res.x, res.y);
                    for (int i = 0; i < panels.Count; i++)
                    {
                        Rect a = ResultsLayout.OnScreen(panels[i].Value, res.x, res.y);
                        Assert.IsTrue(Inside(a, content), $"{tab} {res.x}x{res.y}: {panels[i].Key} {a} leaves the content area {content}");
                        for (int j = i + 1; j < panels.Count; j++)
                        {
                            Rect b = ResultsLayout.OnScreen(panels[j].Value, res.x, res.y);
                            Assert.IsFalse(Overlaps(a, b), $"{tab} {res.x}x{res.y}: {panels[i].Key} overlaps {panels[j].Key}");
                        }
                    }
                }
            }
        }

        [Test]
        public void ResultsButtonsAndTabsAreAlwaysOnScreenInsideTheSafeMargins()
        {
            var rects = new List<Rect>
            {
                ResultsLayout.MainMenuButton, ResultsLayout.ChangeClassButton, ResultsLayout.PlayAgainButton,
            };
            for (int i = 0; i < ResultsLayout.TabCount; i++) rects.Add(ResultsLayout.Tab(i));
            foreach (Vector2Int res in UiScale.CheckResolutions)
            {
                float s = Mathf.Min(res.x / 1920f, res.y / 1080f), m = UiScale.SafeMargin * s;
                var safe = new Rect(m, m, res.x - 2f * m, res.y - 2f * m);
                foreach (Rect r in rects)
                {
                    Rect on = ResultsLayout.OnScreen(r, res.x, res.y);
                    Assert.IsTrue(Inside(on, safe), $"{res.x}x{res.y}: {on} outside the safe area {safe}");
                    Assert.IsTrue(Inside(r, ResultsLayout.Buttons) || Inside(r, ResultsLayout.Tabs), $"{r} sits in its zone");
                }
                for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    Assert.IsFalse(Overlaps(rects[i], rects[j]), $"{rects[i]} overlaps {rects[j]}");
            }
        }

        [Test]
        public void TheRewardsPanelIsNeverScaledBelowTheTypeFloor()
        {
            // The rewards panel's smallest label is 16 px; scaled to fit the content area it must stay readable (>= 14 px
            // at 1080p, about 9 px at 720p is the hard floor of legibility for the Ui face's caps).
            float s = ResultsLayout.FitScale(RewardsPanel.Width, RewardsPanel.PanelHeight);
            Assert.LessOrEqual(s, 1f);
            Assert.GreaterOrEqual(16f * s, 14f, "scale " + s);
        }

        [Test]
        public void TheResultsScreenShowsExactlyOnePageAtATime()
        {
            var go = new GameObject("Results Under Test");
            try
            {
                ResultsScreen r = go.AddComponent<ResultsScreen>();
                r.EnsureBuilt();
                foreach (ResultsTab tab in new[] { ResultsTab.Rewards, ResultsTab.Achievements, ResultsTab.Highlights, ResultsTab.Summary })
                {
                    r.ShowTab(tab);
                    Assert.AreEqual(tab, r.ActiveTab);
                    for (int i = 0; i < ResultsLayout.TabCount; i++)
                        Assert.AreEqual(i == (int)tab, r.Page((ResultsTab)i).gameObject.activeSelf, $"page {(ResultsTab)i} with {tab} on show");
                    Assert.IsTrue(r.PlayAgainItem.gameObject.activeInHierarchy, "play again is always visible");
                    Assert.IsTrue(r.MainMenuItem.gameObject.activeInHierarchy, "main menu is always visible");
                    Assert.IsTrue(r.ChangeClassItem.gameObject.activeInHierarchy, "change class is always visible");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------------------------------------------------------------- text fits

        static Font Display => CMTokens.Font(CMTokens.Display);
        static Font Ui => CMTokens.Font(CMTokens.Ui);

        [Test]
        public void EveryClassNameFitsItsMatchSetupCard()
        {
            if (Display == null) Assert.Ignore("display font not loadable in this run");
            float w = MatchSetup.CardW - 2f * MatchSetup.NamePad;
            foreach (ClassId c in ClassIds.All)
            {
                string name = FlowWords.ClassName(c);
                // One line at >= 18 px, or two lines (the card's name rect is 54 px tall) at >= 18 px.
                Assert.IsTrue(UiScale.FitsOneLine(Display, name, UiScale.TypeHeading, UiScale.TypeSmall, w) ||
                              UiScale.FitsWrapped(Display, name, UiScale.TypeHeading, UiScale.TypeSmall, w, 54f, 3f),
                    name + " does not fit a " + w + " px card at " + UiScale.TypeSmall + " px");
                Assert.IsTrue(UiScale.FitsWrapped(Ui, FlowWords.ClassRole(c), UiScale.TypeBody, UiScale.TypeMin, MatchSetup.CardW - 20f, 32f),
                    "role of " + name);
                Assert.IsTrue(UiScale.FitsWrapped(Ui, FlowWords.AbilityLine(c), 17, UiScale.TypeMin, MatchSetup.CardW - 20f, 28f),
                    "ability line of " + name);
            }
        }

        [Test]
        public void EveryWordsPackLineFitsTheLoadingScreen()
        {
            if (Ui == null) Assert.Ignore("ui font not loadable in this run");
            foreach (string tip in LoreWords.Tips())
                Assert.IsTrue(UiScale.FitsWrapped(Ui, tip, LoadingScreen.TipSize, LoadingScreen.TipMinSize, LoadingScreen.TipW, LoadingScreen.TipH),
                    "tip does not fit: " + tip);
            foreach (string line in LoreWords.LoreLines())
                Assert.IsTrue(UiScale.FitsWrapped(Ui, line, LoadingScreen.LoreSize, LoadingScreen.LoreMinSize, LoadingScreen.LoreW, LoadingScreen.LoreH, 3f),
                    "lore line does not fit: " + line);
        }

        [Test]
        public void EveryAchievementFitsItsResultsCell()
        {
            if (Ui == null || Display == null) Assert.Ignore("fonts not loadable in this run");
            float textW = ResultsLayout.AchCellW - 220f;
            foreach (AchievementDef a in Achievements.All)
            {
                Assert.IsTrue(UiScale.FitsOneLine(Display, a.Title.ToUpperInvariant(), UiScale.TypeBody, UiScale.TypeMin, textW, 2f), a.Title);
                Assert.IsTrue(UiScale.FitsWrapped(Ui, a.Description, UiScale.TypeSmall, UiScale.TypeMin, textW, 36f), a.Description);
            }
            Assert.LessOrEqual(Achievements.All.Length, ResultsLayout.AchCols * ResultsLayout.AchRowsPerCol, "every achievement has a cell");
        }

        [Test]
        public void HudClassNameAndKaijuHeadlineFitOnOneLine()
        {
            if (Ui == null || Display == null) Assert.Ignore("fonts not loadable in this run");
            // The HUD card's class name plate (112 px, best-fit 16..12, 2 px outline): the closer's capture walk cut DEMOLITIONS.
            Assert.IsTrue(UiScale.FitsOneLine(Ui, "DEMOLITIONS", UiScale.TypeMin, 12, 112f, 2f), "DEMOLITIONS fits the HUD name plate");
            // The hatch banner headline (plate width - 300, best-fit 48..30, 4 px outline).
            foreach (string team in new[] { "AZURE", "CRIMSON", "NOBODY" })
                Assert.IsTrue(UiScale.FitsOneLine(Display, CriticalMass.Runtime.View.KaijuMomentTracker.WordSidesWith + team, UiScale.TypeDisplay,
                    UiScale.TypeHeading, CriticalMass.Runtime.Hud.Banner.Width - 300f, 4f), team);
        }

        [Test]
        public void ResultsTabAndButtonLabelsFitOnOneLine()
        {
            if (Display == null) Assert.Ignore("display font not loadable in this run");
            foreach (string t in ResultsScreen.TabNames)
                Assert.IsTrue(UiScale.FitsOneLine(Display, t, UiScale.TypeSmall + 4, UiScale.TypeMin, ResultsLayout.TabW, 2f), t);
            Assert.IsTrue(UiScale.FitsOneLine(Display, "CHANGE CLASS", UiScale.TypeButton, UiScale.TypeMin, ResultsLayout.ChangeClassButton.width, 2f));
            Assert.IsTrue(UiScale.FitsOneLine(Display, "PLAY AGAIN", 32, UiScale.TypeMin, ResultsLayout.PlayAgainButton.width, 2f));
        }

        // ---------------------------------------------------------------- shared controls, captions

        [Test]
        public void SharedControlsResolveByContext()
        {
            InputContextKind play = InputContext.Resolve(false, false), map = InputContext.Resolve(true, false), spec = InputContext.Resolve(false, true);
            Assert.AreEqual(InputContextKind.Play, play);
            Assert.AreEqual(InputContextKind.MapOpen, map);
            Assert.AreEqual(InputContextKind.Spectating, spec);
            Assert.AreEqual(InputContextKind.Spectating, InputContext.Resolve(true, true), "spectating wins over a map left open");
            // Mouse wheel: exactly one owner in every context.
            foreach (InputContextKind k in new[] { play, map, spec })
            {
                int owners = (InputContext.DigSwitchOwnsWheel(k) ? 1 : 0) + (InputContext.MapOwnsWheel(k) ? 1 : 0) + (InputContext.SpectatorOwnsWheel(k) ? 1 : 0);
                Assert.AreEqual(1, owners, k.ToString());
            }
            Assert.IsTrue(InputContext.DigSwitchOwnsWheel(play));
            Assert.IsTrue(InputContext.MapOwnsWheel(map));
            Assert.IsTrue(InputContext.SpectatorOwnsWheel(spec));
            // D-pad: the HUD's actions in play and on the map, the spectator's objective jumps while spectating.
            Assert.IsTrue(InputContext.HudOwnsDpad(play));
            Assert.IsTrue(InputContext.HudOwnsDpad(map));
            Assert.IsFalse(InputContext.HudOwnsDpad(spec));
        }

        [Test]
        public void TheHillAloneSecretIsCaptionedExactlyOnce()
        {
            // subtitles on, announcer installed with subtitles: the announcer's subtitle is the one caption
            Assert.IsFalse(SecretWatcher.ShouldShowOwnCaption(true, true, true));
            // no announcer, or its subtitles off: the secret's own bar
            Assert.IsTrue(SecretWatcher.ShouldShowOwnCaption(true, false, false));
            Assert.IsTrue(SecretWatcher.ShouldShowOwnCaption(true, true, false));
            // subtitles off: no caption bar
            Assert.IsFalse(SecretWatcher.ShouldShowOwnCaption(false, false, false));
        }

        [Test]
        public void IdleTakeoverWaitsLongEnoughToReadAndTheBannerIsBig()
        {
            Assert.GreaterOrEqual(CriticalMass.Runtime.Spectator.PlayerPresence.DefaultIdleAutopilotSeconds, 30f);
            Assert.GreaterOrEqual(CriticalMass.Runtime.Spectator.PlayerPresence.BannerW, 960f);
            Assert.GreaterOrEqual(CriticalMass.Runtime.Spectator.PlayerPresence.BannerH, 88f);
        }

        [Test]
        public void TheTitleSaysTheMatchBehindItIsALiveBotMatch()
        {
            StringAssert.Contains("BOT MATCH", CriticalMass.Runtime.Hud.TitleScreen.LiveTagText);
        }
    }
}

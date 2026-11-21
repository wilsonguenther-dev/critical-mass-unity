using System;
using System.Collections.Generic;
using System.Reflection;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 profile: the PROFILE screen and the results progress strip are contained (every rect inside the screen and the
    /// safe margin at 1280 x 720, 1920 x 1080 and 2560 x 1080, nothing overlapping, nothing scrolling), the built screen's
    /// lines stay in their boxes on every page (the shared ScreenContainment rule), the strip never covers a results zone,
    /// and the pages show honest numbers.
    /// </summary>
    public sealed class ProfileScreenTests
    {
        static readonly Vector2[] Sizes = { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1080) };

        static void P(string s) => Debug.Log("[b15-profile-screen] " + s);

        static bool Overlap(Rect a, Rect b) =>
            a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f;

        static bool Inside(Rect inner, Rect outer) =>
            inner.xMin >= outer.xMin - 0.5f && inner.xMax <= outer.xMax + 0.5f && inner.yMin >= outer.yMin - 0.5f && inner.yMax <= outer.yMax + 0.5f;

        static void NoOverlaps(string group, List<(string name, Rect rect)> rects)
        {
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    bool o = Overlap(rects[i].rect, rects[j].rect);
                    if (o) P($"OVERLAP {group}: {rects[i].name} / {rects[j].name}");
                    Assert.IsFalse(o, $"{group}: {rects[i].name} overlaps {rects[j].name}");
                }
        }

        static void OnScreen(string group, List<(string name, Rect rect)> rects)
        {
            float m = UiScale.SafeMargin;
            var safe = new Rect(m, m * 0.5f, HudLayout.RefW - 2f * m, HudLayout.RefH - m);
            foreach (Vector2 sz in Sizes)
                foreach ((string name, Rect rect) in rects)
                {
                    Rect px = ProfileLayout.ToPixels(rect, sz.x, sz.y);
                    P($"{sz.x}x{sz.y} {group} {name,-16} x={px.x:0} y={px.y:0} w={px.width:0} h={px.height:0}");
                    Assert.GreaterOrEqual(px.xMin, -0.5f, name);
                    Assert.GreaterOrEqual(px.yMin, -0.5f, name);
                    Assert.LessOrEqual(px.xMax, sz.x + 0.5f, name);
                    Assert.LessOrEqual(px.yMax, sz.y + 0.5f, name);
                    Assert.IsTrue(Inside(rect, safe), $"{group} {name} keeps the safe margin");
                }
        }

        [Test]
        public void TheProfileLayoutIsContainedAtThreeSizes()
        {
            List<(string, Rect)> zones = ProfileLayout.Zones();
            OnScreen("zone", zones);
            NoOverlaps("zones", zones);
            var tabs = new List<(string, Rect)>();
            for (int i = 0; i < ProfileLayout.TabCount; i++) tabs.Add(("tab " + i, ProfileLayout.Tab(i)));
            NoOverlaps("tabs", tabs);
            foreach ((string n, Rect r) in tabs) Assert.IsTrue(Inside(r, ProfileLayout.Tabs), n + " inside the tab row");
            foreach (ProfileTab tab in (ProfileTab[])Enum.GetValues(typeof(ProfileTab)))
            {
                List<(string, Rect)> panels = ProfileLayout.Panels(tab);
                OnScreen(tab.ToString(), panels);
                NoOverlaps(tab.ToString(), panels);
                foreach ((string n, Rect r) in panels) Assert.IsTrue(Inside(r, ProfileLayout.Content), $"{tab} {n} inside the content area");
                P($"{tab}: {panels.Count} panels inside the content area, none overlapping");
            }
            List<(string, Rect)> bottom = ProfileLayout.BottomButtons();
            NoOverlaps("bottom", bottom);
            foreach ((string n, Rect r) in bottom) Assert.IsTrue(Inside(r, ProfileLayout.Bottom), n + " inside the bottom row");
            Assert.AreEqual(MatchHistory.PageSize, ProfileLayout.HistoryRows, "one history page fills the rows, no scrolling");
            Assert.GreaterOrEqual(ProfileLayout.AchCols * ProfileLayout.AchRows, Achievements.All.Length, "every achievement has a cell");
            float smallest = ProfileLayout.SmallestType * HudLayout.Scale(1280, 720);
            P($"smallest type at 1280x720: {smallest:0.0} px");
            Assert.GreaterOrEqual(smallest, 12f);
        }

        [Test]
        public void TheStripSitsInTheResultsFreeBand()
        {
            var parts = new List<(string, Rect)> { ("strip", MasteryStripLayout.Strip) };
            OnScreen("strip", parts);
            foreach ((string n, Rect r) in MasteryStripLayout.Parts()) Assert.IsTrue(Inside(r, MasteryStripLayout.Strip), n + " inside the strip");
            NoOverlaps("strip parts", MasteryStripLayout.Parts());
            foreach (KeyValuePair<string, Rect> z in ResultsLayout.Zones())
            {
                bool o = Overlap(MasteryStripLayout.Strip, z.Value);
                P($"strip {MasteryStripLayout.Strip} vs results {z.Key} {z.Value}: overlap {o}");
                Assert.IsFalse(o, "the strip covers the results " + z.Key);
            }
            foreach (Rect b in new[] { ResultsLayout.MainMenuButton, ResultsLayout.ChangeClassButton, ResultsLayout.PlayAgainButton, ResultsLayout.Footer })
                Assert.IsFalse(Overlap(MasteryStripLayout.Strip, b), "the strip covers " + b);
        }

        [Test]
        public void TheStripSaysWhatWasEarnedAndWhatIsNext()
        {
            var p = new PlayerProfile();
            p.MasteryOf(ClassId.Builder).Xp = MasteryRules.TotalFor(4) - 50;
            var stats = new RoleStats { PiecesUsed = 6, PiecesBuilt = 8, HillSeconds = 90 };
            var ctx = new MasteryContext { Completed = true, Outcome = MatchOutcome.Win, BotOnly = true, Day = 1 };
            MetaResult m = MetaRecorder.Record(p, ClassId.Builder, TeamId.Azure, stats, ctx, 640, new DateTime(2026, 11, 17, 20, 0, 0, DateTimeKind.Utc));
            (string earned, string next) = MasteryStripLayout.Lines(m);
            P($"bots-only builder win: xp {m.Award.Xp} level {m.Award.LevelBefore}->{m.Award.LevelAfter}; strip '{earned}' | '{next}'");
            Assert.Greater(m.Award.Xp, 0);
            Assert.AreEqual(3, m.Award.LevelBefore);
            Assert.AreEqual(4, m.Award.LevelAfter);
            StringAssert.Contains("4", earned);
            Assert.AreEqual(1, m.Unlocked.Count, "level 4 unlocks the first trim");
            Assert.AreEqual(RewardKind.Trim, m.Unlocked[0].Kind);
            Assert.IsNotEmpty(next);
            StringAssert.Contains(MetaWords.Why(MasteryWhy.BotMatch), next, "a reduced award says why");
            Assert.AreEqual(1, p.History.Length);
            Assert.AreEqual("Builder", p.History[0].Class);
            Assert.IsTrue(p.History[0].BotOnly);
            Assert.AreEqual(1, p.MasteryOf(ClassId.Builder).Matches);

            var practice = new MasteryContext { Completed = true, Outcome = MatchOutcome.Win, PracticeMode = true };
            MetaResult none = MetaRecorder.Record(p, ClassId.Builder, TeamId.Azure, stats, practice, 640, DateTime.UtcNow);
            (string e2, string n2) = MasteryStripLayout.Lines(none);
            P($"practice: '{e2}' | '{n2}'");
            StringAssert.Contains(MetaWords.Why(MasteryWhy.PracticeMode), e2);
            Assert.AreEqual(1, p.MasteryOf(ClassId.Builder).Matches, "practice adds no career match");
        }

        static PlayerProfile Sample()
        {
            var p = new PlayerProfile { PlayerName = "MOSS WARDEN", MatchesPlayed = 23, Wins = 12, Losses = 11 };
            ClassMastery m = p.MasteryOf(ClassId.Warrior);
            m.Xp = MasteryRules.TotalFor(12) + 120;
            m.Matches = 23; m.Wins = 12; m.Losses = 11; m.KnockOuts = 231; m.HillSeconds = 4210; m.Delivered = 66;
            ClassLoadout l = p.Loadout(ClassId.Warrior);
            l.Title = 4; l.Frame = 2; l.Trim = 3; l.Badge = 3;
            string[] maps = { "BASIN", "HIGH FERNS" };
            for (int i = 0; i < 19; i++)
                MatchHistory.Add(p, new MatchRecord
                {
                    DateUtc = MatchHistory.Stamp(new DateTime(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc).AddHours(i * 7)),
                    Map = maps[i % 2], Class = i % 3 == 0 ? "Healer" : "Warrior", Result = i % 3, Score = 400 + i * 37,
                    Stats = new RoleStats { KnockOuts = i, HillSeconds = 30 + i, Healing = i * 40 }, BotOnly = i % 4 == 0,
                    TurningPoint = i % 2 == 0 ? "Crimson took the hill at 6:12 and held it to the end." : string.Empty,
                    ReplayFile = i < 3 ? "kept-" + i + ".cmreplay" : string.Empty,
                });
            p.Achievement("hill-holder").Best = 42;
            p.Achievement("payback").Unlocked = true;
            return p;
        }

        [Test]
        public void TheBuiltScreenKeepsEveryLineInItsBoxOnEveryPage()
        {
            MethodInfo build = typeof(ScreenContainmentTests).GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo report = typeof(ScreenContainmentTests).GetMethod("Report", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(build, "ScreenContainmentTests.Build (the shared containment rule)");
            Assert.IsNotNull(report);
            var bad = new List<string>();
            PlayerProfile sample = Sample();
            Action<GameObject> make = go =>
            {
                ProfileMenuScreen s = go.AddComponent<ProfileMenuScreen>();
                s.Shown = sample;
                s.Build();
            };
            Action<GameObject, Action<string>> pages = (go, check) =>
            {
                ProfileMenuScreen s = go.GetComponent<ProfileMenuScreen>();
                foreach (ProfileTab tab in (ProfileTab[])Enum.GetValues(typeof(ProfileTab)))
                {
                    s.ShowTab(tab);
                    if (tab == ProfileTab.Mastery)
                        for (int c = 0; c < 6; c++) { s.ShowClass((ClassId)c); check("mastery " + (ClassId)c); }
                    else check(tab.ToString());
                }
                s.ShowTab(ProfileTab.History);
                for (int k = 1; k < MatchHistory.PageCount(sample.History.Length); k++)
                {
                    s.ShowHistoryPage(k);   // what NEXT does
                    Assert.AreEqual(k, s.HistoryPage);
                    check("history page " + (k + 1));
                }
            };
            build.Invoke(null, new object[] { "Profile", make, bad, pages });
            P($"profile screen containment: {bad.Count} problems");
            foreach (string b in bad) P(b);
            report.Invoke(null, new object[] { bad, "profile lines out of their box or into another line" });
        }

        [Test]
        public void ThePagesShowHonestNumbers()
        {
            var go = new GameObject("Profile Honest", typeof(RectTransform));
            try
            {
                ProfileMenuScreen s = go.AddComponent<ProfileMenuScreen>();
                PlayerProfile p = Sample();
                s.Shown = p;
                s.Build();
                s.ShowTab(ProfileTab.Mastery);
                s.ShowClass(ClassId.Warrior);
                P("mastery heading: " + s.HeadingText);
                StringAssert.Contains("12", s.HeadingText);
                s.ShowTab(ProfileTab.History);
                P("history detail: " + s.DetailText);
                Assert.AreEqual(19, p.History.Length);
                Assert.AreEqual(3, MatchHistory.PageCount(p.History.Length));
                for (int i = 0; i < ProfileLayout.HistoryRows; i++) Assert.IsTrue(s.HistoryItem(i).gameObject.activeSelf, "a full first page");
                s.ShowTab(ProfileTab.Achievements);
                int hill = Array.FindIndex(Achievements.All, a => a.Id == "hill-holder");
                int payback = Array.FindIndex(Achievements.All, a => a.Id == "payback");
                P($"hill holder: '{s.AchievementState(hill)}'; payback: '{s.AchievementState(payback)}'");
                StringAssert.Contains("42", s.AchievementState(hill), "the best so far, not a made-up number");
                StringAssert.Contains("60", s.AchievementState(hill));
                Assert.AreEqual(MetaWords.Unlocked, s.AchievementState(payback));
                GameConfig cfg = GameConfig.CreateDefault();
                for (int i = 0; i < Achievements.All.Length; i++)
                    Assert.AreEqual(Achievements.Earnable(i, cfg) || p.HasAchievement(Achievements.All[i].Id), s.AchievementShown(i), Achievements.All[i].Id);
                // a record without its replay file says so instead of failing
                MatchRecord gone = p.History[p.History.Length - 1];   // the oldest kept a replay name; no such file here
                P("detail of a record whose replay is gone: " + ProfileMenuScreen.Detail(gone));
                StringAssert.Contains(MetaWords.ReplayGone, ProfileMenuScreen.Detail(gone));
                StringAssert.Contains(MetaWords.NoReplay, ProfileMenuScreen.Detail(p.History[5]));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

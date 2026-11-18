using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 items 1 and 2 (systems audit W1): the match story (lead graph samples, lead changes, hatch and hut marks, the
    /// turning-point sentence), the role awards (selection, ties, nobody, the player's best stat), MVP points that weigh the
    /// job, and the STORY page layout at 1280x720, 1920x1080 and 2560x1080 (contained, no scrolling).
    /// </summary>
    [TestFixture]
    public sealed class MatchStoryTests
    {
        static World SmallWorld(int team = 5)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            return World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = team, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        // ---------------------------------------------------------------- timeline and the turning point

        [Test]
        public void TheTimelineSamplesOncePerSecondAndMarksEveryLeadChange()
        {
            MatchTimeline tl = MatchTimeline.Begin(60);
            double az = 0, cr = 0;
            // Azure leads from 2 s, Crimson overtakes at 20 s, Azure takes it back at 40 s; 30 Hz ticks
            for (int tick = 0; tick <= 60 * 30; tick++)
            {
                double t = tick / 30.0;
                if (t >= 2 && t < 10) az += 1 / 30.0;
                if (t >= 10 && t < 40) cr += 1 / 30.0;
                if (t >= 40) az += 2 / 30.0;
                tl.Observe(t, az, cr);
            }
            Assert.AreEqual(61, tl.Count, "one sample per whole second, 0 to 60");
            Assert.AreEqual(3, tl.LeadChanges, "Azure, then Crimson, then Azure again");
            Assert.AreEqual(TimelineMarkKind.LeadChange, tl.Marks[0].Kind);
            Assert.AreEqual(TeamId.Azure, tl.Marks[0].Team);
            Assert.AreEqual(TeamId.Crimson, tl.Marks[1].Team);
            Assert.AreEqual(18.0, tl.Marks[1].Time, 0.1, "Crimson passes 8 s of Azure 8 s after it starts at 10 s");
            Assert.AreEqual(TeamId.Azure, tl.Marks[2].Team);
        }

        [Test]
        public void TheTurningPointNamesTheWinnersLastLeadInPlainWords()
        {
            var held = MatchTimeline.Begin(480);
            held.Observe(0, 0, 0);
            held.Observe(192, 0, 1);   // Crimson takes the lead at 3:12
            held.Observe(480, 100, 200);
            string s = held.TurningPoint(MatchResult.Crimson, 100, 200);
            Debug.Log("[b15 story] held: " + s);
            Assert.AreEqual("Crimson took the lead at 3:12 and never lost it.", s);

            var back = MatchTimeline.Begin(480);
            back.Observe(10, 1, 0);
            back.Observe(100, 1, 9);   // Crimson leads by 8 (not a comeback)
            back.Observe(200, 20, 10);   // Azure back in front at 3:20
            string b = back.TurningPoint(MatchResult.Azure, 200, 150);
            Debug.Log("[b15 story] retook: " + b);
            Assert.AreEqual("Azure took the lead back at 3:20 and held it to the end.", b);

            var come = MatchTimeline.Begin(480);
            come.Observe(10, 0, 40);   // Azure 40 s down
            come.Observe(300, 41, 40);   // and ahead at 5:00
            string c = come.TurningPoint(MatchResult.Azure, 200, 150);
            Debug.Log("[b15 story] comeback: " + c);
            Assert.AreEqual("Azure came back from 40 s down and took the lead for good at 5:00.", c);

            var late = MatchTimeline.Begin(480);
            late.Observe(10, 0, 5);
            late.Observe(470, 6, 5);
            StringAssert.Contains("clock almost out", late.TurningPoint(MatchResult.Azure, 6, 5));

            Assert.AreEqual(MatchWords.TurnDraw, held.TurningPoint(MatchResult.Draw, 100, 100));
            Assert.AreEqual(MatchWords.TurnNobody, MatchTimeline.Begin(480).TurningPoint(MatchResult.Draw, 0, 0));
        }

        [Test]
        public void StatsRecordTheStoryAndEveryRoleCounterFromSimEvents()
        {
            World w = SmallWorld();
            MatchStats s = MatchStats.Begin(w);
            int healer = 2, builder = 3, miner = 4, demo = 1, enemy = 6;
            w.Actors[enemy].Hp = 20;
            w.Actors[enemy].MaxHp = 100;
            int ally = 0;
            w.Actors[ally].MaxHp = 100;
            w.Actors[ally].Hp = 40;   // healed by 25 from 15: a save
            World.PushEvent(w, SimEventType.HealLanded, ally, healer, 0, 0, 25);
            World.PushEvent(w, SimEventType.HealLanded, healer, healer, 0, 0, 10);   // her own: healing, not a save
            World.PushEvent(w, SimEventType.WallBuilt, builder, 120, 0, 0, 3);
            World.PushEvent(w, SimEventType.WallBuilt, builder, -1, 0, 0, -2);   // refused: nothing
            World.PushEvent(w, SimEventType.PiecePlaced, builder, 0, 0, 0, 900);
            World.PushEvent(w, SimEventType.BankDeposit, miner, 0, 0, 0, 3, BiomassType.Ore, DamageCause.None);
            World.PushEvent(w, SimEventType.BankDeposit, miner, 0, 0, 0, 2, BiomassType.Wood, DamageCause.None);   // not ore
            World.PushEvent(w, SimEventType.HutLevel, 0, miner, 0, 0, 1);
            World.PushEvent(w, SimEventType.Explosion, demo, 2, 10, 10, 2, null, DamageCause.Explosion, 3);
            World.PushEvent(w, SimEventType.WallBroken, enemy, 5, 11, 10, 3);   // an enemy wall inside the blast
            World.PushEvent(w, SimEventType.WallBroken, builder, 5, 11, 10, 3);   // an ally's wall: no credit
            World.PushEvent(w, SimEventType.Handoff, 5, 6, 0, 0, 2);
            s.Consume(w);
            Assert.AreEqual(35, s.Actors[healer].HealingDone, 1e-9);
            Assert.AreEqual(1, s.Actors[healer].Saves);
            Assert.AreEqual(2, s.Actors[builder].StructuresBuilt);
            Assert.AreEqual(3, s.Actors[builder].WallCells);
            Assert.AreEqual(3, s.Actors[miner].OreBanked);
            Assert.AreEqual(1, s.Actors[miner].HutLevelsFunded);
            Assert.AreEqual(1, s.Actors[demo].MultiHits);
            Assert.AreEqual(1, s.Actors[demo].StructuresBlown, "only the enemy wall in the blast counts");
            Assert.AreEqual(1, s.Actors[5].Handoffs);
            int hut = 0;
            for (int i = 0; i < s.Timeline.MarkCount; i++) if (s.Timeline.Marks[i].Kind == TimelineMarkKind.HutLevel) hut++;
            Assert.AreEqual(1, hut, "the hut level is a mark on the graph");
            Assert.Greater(s.Timeline.Count, 0, "the first tick is sampled");
        }

        [Test]
        public void AWholeBotMatchLeavesAGraphMarksAndATurningPoint()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            cfg.MatchSeconds = 120;
            cfg.HatchSeconds = 40;
            World w = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            MatchStats s = MatchStats.Begin(w);
            var scratch = SimInput.EmptyInput();
            for (int t = 0; t < 120 * 30 + 30 && w.Match.Phase == MatchPhase.Playing; t++)
            {
                MatchTick.Advance(w, null, scratch);
                s.Consume(w);
            }
            s.Finish(w);
            MatchSummary sum = s.Summarize(w);
            int hatch = 0;
            for (int i = 0; i < s.Timeline.MarkCount; i++) if (s.Timeline.Marks[i].Kind == TimelineMarkKind.Hatch) hatch++;
            Debug.Log($"[b15 story] 5v5 seed 4242 2:00: samples {s.Timeline.Count}, marks {s.Timeline.MarkCount}, lead changes {s.Timeline.LeadChanges}, hatch marks {hatch}, turning point \"{sum.TurningPoint}\"");
            Assert.GreaterOrEqual(s.Timeline.Count, 119, "a sample per second");
            Assert.AreEqual(1, hatch, "the hatch is marked once");
            Assert.IsFalse(string.IsNullOrEmpty(sum.TurningPoint));
            Assert.AreEqual((float)w.Match.HillSeconds.Azure, s.Timeline.Azure[s.Timeline.Count - 1], 1e-3f, "the last sample is the final total");
        }

        // ---------------------------------------------------------------- awards and MVP

        static ActorStats[] Roster(int n)
        {
            var a = new ActorStats[n];
            for (int i = 0; i < n; i++) a[i] = new ActorStats { Id = i, Team = i < n / 2 ? TeamId.Azure : TeamId.Crimson, Cls = ClassId.Warrior };
            return a;
        }

        [Test]
        public void EachAwardGoesToTheBestAtThatJobWithStableTieBreaks()
        {
            ActorStats[] a = Roster(6);
            a[0].HillSeconds = 90; a[0].Kills = 9;
            a[1].HealingDone = 400; a[1].Saves = 2; a[1].Cls = ClassId.Healer;
            a[4].HealingDone = 400; a[4].Saves = 5; a[4].Cls = ClassId.Healer;   // same HP, more saves: wins
            a[2].StructuresBuilt = 7; a[2].WallCells = 18;
            a[3].OreBanked = 12; a[3].HutLevelsFunded = 2;
            a[5].Biomass = 30;
            AwardResult[] r = RoleAwards.Pick(a, a.Length);
            Assert.AreEqual(RoleAwards.Count, r.Length);
            Assert.AreEqual(0, r[(int)AwardKind.HillAnchor].Actor);
            Assert.AreEqual(4, r[(int)AwardKind.Lifesaver].Actor, "a tie on HP healed goes to more saves");
            Assert.AreEqual(2, r[(int)AwardKind.MasterBuilder].Actor);
            Assert.AreEqual(3, r[(int)AwardKind.Prospector].Actor);
            Assert.AreEqual(5, r[(int)AwardKind.TopFeeder].Actor);
            Assert.AreEqual(-1, r[(int)AwardKind.Demolisher].Actor, "nobody blew anything up: no winner");
            Assert.AreEqual(MatchWords.AwardNobody, RoleAwards.Line(r[(int)AwardKind.Demolisher]));
            Assert.AreEqual("400 HP healed, 5 saves", RoleAwards.Line(r[(int)AwardKind.Lifesaver]));
            Assert.AreEqual("12 ore banked, 2 hut levels", RoleAwards.Line(r[(int)AwardKind.Prospector]));

            // a full tie goes to fewer deaths, then the lower id
            ActorStats[] t = Roster(2);
            t[0].OreBanked = 5; t[1].OreBanked = 5; t[0].HutLevelsFunded = 1; t[1].HutLevelsFunded = 1;
            t[0].Deaths = 3; t[1].Deaths = 1;
            Assert.AreEqual(1, RoleAwards.Winner(t, 2, AwardKind.Prospector));
            t[1].Deaths = 3;
            Assert.AreEqual(0, RoleAwards.Winner(t, 2, AwardKind.Prospector));
        }

        [Test]
        public void ThePlayersBestStatIsTheJobWhereTheyRankHighest()
        {
            ActorStats[] a = Roster(4);
            a[0].HillSeconds = 10; a[1].HillSeconds = 50; a[2].HillSeconds = 30;   // player 0: 3rd on the hill
            a[0].StructuresBuilt = 4;   // but 1st at building
            BestStat b = RoleAwards.BestOf(a, a.Length, 0);
            Assert.IsTrue(b.Has);
            Assert.AreEqual(AwardKind.MasterBuilder, b.Kind);
            Assert.AreEqual(1, b.Rank);
            Assert.AreEqual("4 walls and pieces built, #1 of 4", RoleAwards.BestLine(b, a[0]));

            ActorStats[] none = Roster(2);
            none[0].Kills = 3;
            Assert.AreEqual("3 knock-outs", RoleAwards.BestLine(RoleAwards.BestOf(none, 2, 0), none[0]));
            Assert.AreEqual(MatchWords.BestNothing, RoleAwards.BestLine(RoleAwards.BestOf(none, 2, 1), none[1]));
        }

        [Test]
        public void TheMvpWeighsTheJobNotOnlyKnockOuts()
        {
            World w = SmallWorld();
            MatchStats s = MatchStats.Begin(w);
            int warrior = 1, healer = 2;
            w.Actors[warrior].Kills = 10;   // 1000 points of fighting
            for (int k = 0; k < 40; k++) World.PushEvent(w, SimEventType.HealLanded, 0, healer, 0, 0, 30);   // 1200 HP healed
            s.Finish(w);
            Debug.Log($"[b15 mvp] warrior 10 KO = {s.Actors[warrior].Points} pts; healer 1200 HP healed = {s.Actors[healer].Points} pts (role {s.Actors[healer].RolePoints})");
            Assert.AreEqual(1000, s.Actors[warrior].Points);
            Assert.Greater(s.Actors[healer].RolePoints, 0);
            Assert.AreEqual(healer, s.Mvp(TeamId.Azure), "a Healer who healed 1200 HP outscores a 10 knock-out Warrior");
        }

        // ---------------------------------------------------------------- the STORY page layout

        static readonly Vector2Int[] Screens = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080) };

        static bool Overlaps(Rect a, Rect b) => a.xMin < b.xMax - 0.01f && b.xMin < a.xMax - 0.01f && a.yMin < b.yMax - 0.01f && b.yMin < a.yMax - 0.01f;
        static bool Inside(Rect i, Rect o) => i.xMin >= o.xMin - 0.01f && i.xMax <= o.xMax + 0.01f && i.yMin >= o.yMin - 0.01f && i.yMax <= o.yMax + 0.01f;

        [Test]
        public void TheStoryPageFitsEveryScreenShapeWithoutScrollingOrOverlap()
        {
            var groups = new List<(Rect panel, List<KeyValuePair<string, Rect>> parts)>
            {
                (ResultsLayout.StoryPanel, ResultsLayout.StoryParts()),
                (ResultsLayout.AwardsPanel, ResultsLayout.AwardParts()),
            };
            Assert.IsFalse(Overlaps(ResultsLayout.StoryPanel, ResultsLayout.AwardsPanel));
            foreach (Vector2Int res in Screens)
            {
                Rect content = ResultsLayout.OnScreen(ResultsLayout.Content, res.x, res.y);
                Rect screen = new Rect(0, 0, res.x, res.y);
                foreach (var (panel, parts) in groups)
                {
                    Rect p = ResultsLayout.OnScreen(panel, res.x, res.y);
                    Assert.IsTrue(Inside(p, content), $"{res.x}x{res.y}: panel {p} inside the content area {content}");
                    Assert.IsTrue(Inside(p, screen), $"{res.x}x{res.y}: panel on screen");
                    for (int i = 0; i < parts.Count; i++)
                    {
                        Rect a = ResultsLayout.OnScreen(parts[i].Value, res.x, res.y);
                        Assert.IsTrue(Inside(a, p), $"{res.x}x{res.y}: {parts[i].Key} {a} inside its panel {p}");
                        for (int j = i + 1; j < parts.Count; j++)
                            Assert.IsFalse(Overlaps(a, ResultsLayout.OnScreen(parts[j].Value, res.x, res.y)), $"{res.x}x{res.y}: {parts[i].Key} overlaps {parts[j].Key}");
                    }
                }
            }
            for (int i = 0; i < ResultsLayout.LegendItems; i++) Assert.IsTrue(Inside(ResultsLayout.LegendItem(i), ResultsLayout.StoryLegend));
        }

        [Test]
        public void EveryStoryAndAwardLineFitsItsBoxAtTheLargestNumbers()
        {
            Font ui = CMTokens.Font(CMTokens.Ui), display = CMTokens.Font(CMTokens.Display);
            if (ui == null || display == null) Assert.Ignore("fonts not loadable in this run");
            float turnW = ResultsLayout.StoryTurn.width;
            foreach (string t in new[] { MatchWords.TurnComeback, MatchWords.TurnRetook, MatchWords.TurnLedAndHeld, MatchWords.TurnLate, MatchWords.TurnDraw })
            {
                string line = MatchWords.Fill(t, "team", "Crimson", "time", "7:59", "deficit", "480");
                Assert.IsTrue(UiScale.FitsOneLine(ui, line, UiScale.TypeBody, UiScale.TypeMin, turnW, 2f), line);
            }
            float lineW = ResultsLayout.AwardRow(0).width - 82f;
            for (int k = 0; k < RoleAwards.Count; k++)
            {
                Assert.IsTrue(UiScale.FitsOneLine(display, MatchWords.AwardTitles[k], UiScale.TypeSmall, UiScale.TypeMin, 250f, 2f), MatchWords.AwardTitles[k]);
                string l = MatchWords.Fill(MatchWords.AwardLines[k], "a", "9999", "b", "99");
                Assert.IsTrue(UiScale.FitsOneLine(ui, l, UiScale.TypeSmall, UiScale.TypeMin, lineW, 2f), l);
            }
            string best = MatchWords.Fill(MatchWords.BestRank, "line", MatchWords.Fill(MatchWords.BestLines[(int)AwardKind.MasterBuilder], "a", "9999"), "rank", "32", "n", "32");
            Assert.IsTrue(UiScale.FitsOneLine(ui, best, UiScale.TypeBody, UiScale.TypeMin, ResultsLayout.AwardsBest.width - 186f, 2f), best);
            Assert.IsTrue(UiScale.FitsOneLine(display, MatchWords.StoryTab, UiScale.TypeSmall + 4, UiScale.TypeMin, ResultsLayout.TabW, 2f));
        }

        [Test]
        public void TheResultsScreenShowsTheStoryOfABoundMatch()
        {
            World w = SmallWorld();
            MatchStats s = MatchStats.Begin(w);
            var scratch = SimInput.EmptyInput();
            for (int t = 0; t < 900; t++) { MatchTick.Advance(w, null, scratch); s.Consume(w); }
            s.Finish(w);
            var go = new GameObject("Results Under Test");
            try
            {
                ResultsScreen r = go.AddComponent<ResultsScreen>();
                r.EnsureBuilt();
                r.Bind(w, s, s.Summarize(w));
                r.ShowTab(ResultsTab.Story);
                Assert.AreEqual(ResultsTab.Story, r.ActiveTab);
                Assert.IsTrue(r.Page(ResultsTab.Story).gameObject.activeSelf);
                Assert.IsFalse(r.Page(ResultsTab.Summary).gameObject.activeSelf);
                Assert.IsFalse(string.IsNullOrEmpty(r.TurningPointText), "the turning point sentence is shown");
                Assert.AreEqual(RoleAwards.Count, r.AwardsShown.Length);
                Assert.IsNotNull(r.Graph.Timeline);
                Assert.Greater(r.Graph.Timeline.Count, 20);
                StringAssert.StartsWith("YOUR BEST", r.YourBestText);
                Debug.Log($"[b15 story] results bound: \"{r.TurningPointText}\"; your best \"{r.YourBestText}\"");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

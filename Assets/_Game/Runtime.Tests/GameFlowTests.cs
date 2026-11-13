using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>DG-01 game flow without Play Mode: seeds, requests, the overlay registry, menus' navigation, stats, words.</summary>
    [TestFixture]
    public sealed class GameFlowTests
    {
        [SetUp]
        public void SetUp()
        {
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = true;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
        }

        [TearDown]
        public void TearDown()
        {
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = false;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
            FlowWords.ResetForTests();
        }

        static readonly MatchSetupChoice CrimsonMiner5 = new MatchSetupChoice
        {
            Team = TeamId.Crimson, Class = ClassId.Miner, TeamSize = 5, Pace = 2,
        };

        // ---- seeds and requests ----

        [Test]
        public void EveryQueuedMatchGetsANewPositiveSeed()
        {
            var seen = new HashSet<int>();
            int prev = 0;
            for (int i = 0; i < 200; i++)
            {
                GameFlow.QueueMatch(CrimsonMiner5);
                MatchRequest r = GameFlow.RequestForBuild();
                Assert.IsNotNull(r);
                Assert.Greater(r.Seed, 0);
                Assert.AreNotEqual(prev, r.Seed, "two matches in a row had the same seed");
                prev = r.Seed;
                seen.Add(r.Seed);
            }
            Assert.Greater(seen.Count, 190, "seeds should be (almost) all distinct");
        }

        [Test]
        public void FirstBootIsTheTitleOverABotBackdrop()
        {
            MatchRequest r = GameFlow.RequestForBuild();
            Assert.AreEqual(FlowState.Title, GameFlow.State);
            Assert.IsTrue(r.Backdrop);
            Assert.AreEqual(16, r.TeamSize);
            Assert.AreNotEqual(new GameConfig().Seed, r.Seed, "the backdrop must not be the fixed 1337 match");
        }

        [Test]
        public void FlowOffBuildsTheConfigDefaultMatch()
        {
            GameFlow.ForceEnabled = false;
            Assert.IsNull(GameFlow.RequestForBuild());
            Assert.AreEqual(FlowState.Off, GameFlow.State);
        }

        [Test]
        public void RequestCreatesACrimsonMinerWorldOfTen()
        {
            GameFlow.QueueMatch(CrimsonMiner5);
            MatchRequest r = GameFlow.RequestForBuild();
            Assert.AreEqual(FlowState.Match, GameFlow.State);
            Assert.IsFalse(r.Backdrop);
            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(r.ToWorldOptions(cfg));
            Assert.AreEqual(0, w.Tick);
            Assert.AreEqual(10, w.ActorCount);
            Assert.AreEqual(TeamId.Crimson, w.Actors[0].Team);
            Assert.AreEqual(ClassId.Miner, w.Actors[0].Cls);
            Assert.IsTrue(w.Actors[0].IsHuman);
            Assert.AreEqual(r.Seed, w.Opts.Seed);
            Assert.AreEqual(2.0, r.Pace);
        }

        [Test]
        public void PauseMenuRestartStartsTheLastSetupOnANewSeed()
        {
            GameFlow.QueueMatch(CrimsonMiner5);
            int first = GameFlow.RequestForBuild().Seed;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.Play;
            MatchRequest r = GameFlow.RequestForBuild();
            Assert.AreEqual(PauseMenu.AfterReload.None, PauseMenu.PendingAfterReload, "the flow takes the request over");
            Assert.AreEqual(FlowState.Match, GameFlow.State);
            Assert.AreEqual(ClassId.Miner, r.Class);
            Assert.AreEqual(TeamId.Crimson, r.Team);
            Assert.AreNotEqual(first, r.Seed);
        }

        [Test]
        public void PauseMenuQuitToTitleGoesToTheTitleBackdrop()
        {
            GameFlow.QueueMatch(CrimsonMiner5);
            GameFlow.RequestForBuild();
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.Title;
            MatchRequest r = GameFlow.RequestForBuild();
            Assert.AreEqual(FlowState.Title, GameFlow.State);
            Assert.IsTrue(r.Backdrop);
        }

        // ---- overlay registry ----

        [GameOverlay(Order = 5, Scope = OverlayScope.MatchOnly)]
        sealed class ProbeOverlay : MonoBehaviour
        {
        }

        [GameOverlay(Order = 1)]
        sealed class ProbeOverlayFirst : MonoBehaviour
        {
        }

        [MenuEntry("barracks", "BARRACKS", 30)]
        sealed class ProbeBarracks : MonoBehaviour, IMenuScreen
        {
            public void Open(Action onClosed) => onClosed();
        }

        [MenuEntry("probe-extra", "EXTRA", 70)]
        sealed class ProbeExtra : MonoBehaviour, IMenuScreen
        {
            public void Open(Action onClosed) => onClosed();
        }

        [Test]
        public void DefaultMenuHasEveryLineAndUnbuiltOnesAreComingSoon()
        {
            List<MenuEntry> e = OverlayRegistry.MenuEntries(Array.Empty<Type>());
            string[] ids = e.ConvertAll(x => x.Id).ToArray();
            CollectionAssert.AreEqual(new[] { "play", "howto", "barracks", "controls", "settings", "codex", "credits", "quit" }, ids);
            Assert.IsTrue(e.Find(x => x.Id == "play").Available);
            Assert.IsTrue(e.Find(x => x.Id == "controls").Available);
            Assert.IsTrue(e.Find(x => x.Id == "settings").Available);
            Assert.IsTrue(e.Find(x => x.Id == "quit").Available);
            Assert.IsFalse(e.Find(x => x.Id == "barracks").Available);
            Assert.IsFalse(e.Find(x => x.Id == "codex").Available);
            Assert.IsFalse(e.Find(x => x.Id == "credits").Available);
            Assert.AreEqual(OverlayRegistry.TutorialStart() != null, e.Find(x => x.Id == "howto").Available);
        }

        [Test]
        public void ARegisteredScreenFillsItsComingSoonLineOrAddsANewOne()
        {
            List<MenuEntry> e = OverlayRegistry.MenuEntries(new[] { typeof(ProbeBarracks), typeof(ProbeExtra) });
            MenuEntry b = e.Find(x => x.Id == "barracks");
            Assert.IsTrue(b.Available);
            Assert.AreEqual(typeof(ProbeBarracks), b.ScreenType);
            int extra = e.FindIndex(x => x.Id == "probe-extra");
            int credits = e.FindIndex(x => x.Id == "credits");
            int quit = e.FindIndex(x => x.Id == "quit");
            Assert.That(extra > credits && extra < quit, "ordered by Order");
        }

        [Test]
        public void OverlaysInstallInOrderAndRespectScope()
        {
            List<Type> t = OverlayRegistry.OverlayTypes(new[] { typeof(ProbeOverlay), typeof(ProbeOverlayFirst), typeof(ProbeExtra) });
            CollectionAssert.AreEqual(new[] { typeof(ProbeOverlayFirst), typeof(ProbeOverlay) }, t);
            Assert.IsTrue(OverlayRegistry.InScope(OverlayScope.MatchOnly, FlowState.Match));
            Assert.IsTrue(OverlayRegistry.InScope(OverlayScope.MatchOnly, FlowState.Off));
            Assert.IsFalse(OverlayRegistry.InScope(OverlayScope.MatchOnly, FlowState.Menu));
            Assert.IsTrue(OverlayRegistry.InScope(OverlayScope.MenusOnly, FlowState.Title));
            Assert.IsFalse(OverlayRegistry.InScope(OverlayScope.MenusOnly, FlowState.Match));
        }

        [Test]
        public void TestAssembliesNeverReachTheRealMenu()
        {
            List<MenuEntry> e = OverlayRegistry.MenuEntries();
            Assert.IsNull(e.Find(x => x.Id == "probe-extra"));
            Assert.IsFalse(OverlayRegistry.OverlayTypes().Contains(typeof(ProbeOverlay)));
        }

        // ---- menus: navigation skips coming soon, focus starts on the main action ----

        static void AssertChainSkipsDisabled(IEnumerable<MenuItem> items)
        {
            foreach (MenuItem it in items)
            {
                if (!it.interactable)
                {
                    Assert.AreEqual(Navigation.Mode.None, it.navigation.mode, it.name + " is coming soon and must not be reachable");
                    continue;
                }
                Navigation n = it.navigation;
                Assert.AreEqual(Navigation.Mode.Explicit, n.mode, it.name);
                foreach (Selectable s in new[] { n.selectOnUp, n.selectOnDown, n.selectOnLeft, n.selectOnRight })
                    if (s != null) Assert.IsTrue(s.interactable, it.name + " leads to a disabled item " + s.name);
            }
        }

        [Test]
        public void MainMenuBuildsEveryLineAndNavigationSkipsComingSoon()
        {
            var go = new GameObject("DG01 MainMenu Test");
            try
            {
                MainMenu m = go.AddComponent<MainMenu>();
                m.EnsureBuilt();
                Assert.AreEqual(m.Entries.Count, m.Items.Count);
                for (int i = 0; i < m.Entries.Count; i++) Assert.AreEqual(m.Entries[i].Available, m.Items[i].interactable, m.Entries[i].Id);
                Assert.IsFalse(m.Item("barracks").interactable);
                Assert.IsFalse(m.Item("codex").interactable);
                Assert.IsTrue(m.Item("play").interactable);
                AssertChainSkipsDisabled(m.Items);
                Assert.IsNotNull(m.Item("barracks").transform.Find("Soon"), "coming soon lines carry the tag");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MatchSetupShowsFiveCardsAndOnlyPlayableClassesAreReachable()
        {
            var go = new GameObject("DG01 Setup Test");
            try
            {
                MatchSetup s = go.AddComponent<MatchSetup>();
                s.EnsureBuilt();
                var all = new List<MenuItem> { s.TeamItem, s.SizeItem, s.PaceItem, s.StartItem, s.BackItem };
                for (int c = 0; c < 5; c++)
                {
                    MenuItem card = s.Card((ClassId)c);
                    Assert.IsNotNull(card);
                    Assert.AreEqual(MatchSetup.Playable.Contains((ClassId)c), card.interactable, ((ClassId)c).ToString());
                    Assert.IsNotNull(card.transform.Find("Portrait"));
                    Assert.IsFalse(string.IsNullOrEmpty(card.transform.Find("Role").GetComponent<Text>().text), "role line");
                    all.Add(card);
                }
                AssertChainSkipsDisabled(all);
                s.SetClass(ClassId.Builder);
                Assert.AreNotEqual(ClassId.Builder, s.Choice.Class, "a coming-soon class can't be picked");
                s.SetTeam(TeamId.Crimson);
                s.SetClass(ClassId.Miner);
                s.SetSize(5);
                Assert.AreEqual(TeamId.Crimson, s.Choice.Team);
                Assert.AreEqual(ClassId.Miner, s.Choice.Class);
                Assert.AreEqual(5, s.Choice.TeamSize);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ---- stats, MVP and the reasons ----

        static World SmallWorld()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            return World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        [Test]
        public void StatsCountDeliveriesKaijuKnockoutsAndPickAnMvpPerTeam()
        {
            World w = SmallWorld();
            MatchStats s = MatchStats.Begin(w);
            Assert.AreEqual(0, s.StartTick);
            World.PushEvent(w, SimEventType.DeliverBiomass, 2, 0, 0, 0, 3);
            World.PushEvent(w, SimEventType.DeliverCorruption, 7, 1, 0, 0, 2);
            World.PushEvent(w, SimEventType.KaijuKO, -1, 3, 0, 0, 0);
            World.PushEvent(w, SimEventType.Death, 6, Damage.KaijuSource, 0, 0, 10);
            World.PushEvent(w, SimEventType.Hit, 6, 1, 0, 0, 12.5);
            w.Actors[1].Kills = 4;
            w.Actors[8].HillSeconds = 30;
            s.Finish(w);
            Assert.AreEqual(3, s.Actors[2].Biomass);
            Assert.AreEqual(2, s.Actors[7].Corruption);
            Assert.AreEqual(1, s.Actors[3].KaijuKOs);
            Assert.AreEqual(1, s.KaijuKOsByTeam[(int)TeamId.Azure]);
            Assert.AreEqual(1, s.KaijuKillsOfTeam[(int)TeamId.Crimson]);
            Assert.AreEqual(12.5, s.Actors[1].Damage, 1e-9);
            Assert.AreEqual(400, s.Actors[1].Points);
            Assert.AreEqual(1, s.Mvp(TeamId.Azure), "4 knock-outs (400) beat a Kaiju knock-out (150) and 3 biomass (75)");
            Assert.AreEqual(8, s.Mvp(TeamId.Crimson), "30 hill seconds (300) beat 2 corruption (20)");
        }

        [Test]
        public void TimerExpiryEndsTheMatchAndTheReasonNamesBothHillTotals()
        {
            // The owner's field report: clock 00:00 at 226 to 230. The sim ends the match on the clock alone.
            World w = SmallWorld();
            MatchStats s = MatchStats.Begin(w);
            InputFrame scratch = SimInput.EmptyInput();
            w.Match.Time = w.Config.MatchSeconds - 2.0 / w.Config.SimHz;
            w.Match.HillSeconds.Set(226, 230);
            for (int i = 0; i < 4 && w.Match.Phase != MatchPhase.Ended; i++)
            {
                MatchTick.Advance(w, null, scratch);
                s.Consume(w);
            }
            Assert.AreEqual(MatchPhase.Ended, w.Match.Phase, "the clock ran out, so the sim must end the match");
            s.Finish(w);
            MatchSummary sum = s.Summarize(w);
            Assert.AreEqual(w.Match.Winner, sum.Winner);
            double az = w.Match.HillSeconds.Azure, cr = w.Match.HillSeconds.Crimson;
            StringAssert.Contains(Math.Round(Math.Max(az, cr)).ToString("0"), sum.Reason);
            StringAssert.Contains(Math.Round(Math.Min(az, cr)).ToString("0"), sum.Reason);
            if (sum.Winner == MatchResult.Crimson) Assert.AreEqual("DEFEAT", sum.Title, "the player is Azure");
            Assert.IsFalse(string.IsNullOrEmpty(sum.KaijuCaption));
            Assert.IsFalse(string.IsNullOrEmpty(sum.KaijuReason));
        }

        [Test]
        public void ADrawSaysDrawAndShowsBothTotals()
        {
            World w = SmallWorld();
            MatchStats s = MatchStats.Begin(w);
            w.Match.HillSeconds.Set(200, 200);
            w.Match.Phase = MatchPhase.Ended;
            w.Match.Winner = MatchResult.Draw;
            s.Finish(w);
            MatchSummary sum = s.Summarize(w);
            Assert.AreEqual("DRAW", sum.Title);
            StringAssert.Contains("200", sum.Reason);
        }

        [Test]
        public void KaijuReasonsMatchTheHatchOutcome()
        {
            World w = SmallWorld();
            MatchStats s = MatchStats.Begin(w);
            w.Match.Allegiance.Set(640, 410);
            w.Kaiju.State = KaijuState.Azure;
            World.PushEvent(w, SimEventType.Hatch, -1, -1, 0, 0, 1);
            s.Consume(w);
            MatchSummary sum = s.Summarize(w);
            Assert.AreEqual(KaijuState.Azure, sum.Kaiju);
            StringAssert.Contains("Azure", sum.KaijuCaption);
            StringAssert.Contains("640", sum.KaijuReason);
            StringAssert.Contains("410", sum.KaijuReason);

            World b = SmallWorld();
            MatchStats sb = MatchStats.Begin(b);
            b.Match.Instability = 112;
            b.Kaiju.State = KaijuState.Berserk;
            World.PushEvent(b, SimEventType.Hatch, -1, -1, 0, 0, 3);
            sb.Consume(b);
            MatchSummary bs = sb.Summarize(b);
            Assert.AreEqual(KaijuState.Berserk, bs.Kaiju);
            Assert.IsFalse(string.IsNullOrEmpty(bs.KaijuReason));

            World e = SmallWorld();
            MatchSummary es = MatchStats.Begin(e).Summarize(e);
            Assert.AreEqual(KaijuState.Egg, es.Kaiju);
            StringAssert.Contains("never hatched", es.KaijuCaption);
        }

        // ---- words ----

        [Test]
        public void EveryClassHasANameRoleAndAbilityLineWithOrWithoutTheWordsPack()
        {
            for (int c = 0; c < 5; c++)
            {
                Assert.IsFalse(string.IsNullOrEmpty(FlowWords.ClassName((ClassId)c)));
                Assert.IsFalse(string.IsNullOrEmpty(FlowWords.ClassRole((ClassId)c)));
                StringAssert.Contains("·", FlowWords.AbilityLine((ClassId)c));
                Assert.That(FlowWords.Difficulty((ClassId)c), Is.InRange(1, 3));
            }
        }

        [Test]
        public void ParsesTheWordsPackShapeAndRejectsJunk()
        {
            const string json = "{\"schema\":\"cm.lore.classes/1\",\"classes\":[{\"id\":\"Miner\",\"name\":\"Miner\",\"role\":\"Opens the roads.\"," +
                                "\"difficulty\":2,\"abilities\":[{\"input\":\"attack\",\"name\":\"Pick\"}]}]}";
            FlowWords.ClassWords[] c = FlowWords.ParseClasses(json);
            Assert.IsNotNull(c);
            Assert.AreEqual("Opens the roads.", c[0].role);
            Assert.AreEqual("Pick", c[0].abilities[0].name);
            Assert.IsNull(FlowWords.ParseClasses("not json"));
            Assert.IsNull(FlowWords.ParseClasses(""));
        }

        [Test]
        public void TokenFillUsesOnlyKnownValues()
        {
            var v = new Dictionary<string, string> { { "run:winner", "Azure" } };
            Assert.AreEqual("Azure won", FlowWords.Fill("{run:winner} won", v));
            Assert.IsNull(FlowWords.Fill("{run:winner} by {cfg:unknown}", v), "an unfilled token falls back to built-in words");
            Assert.IsNull(FlowWords.Fill("", v));
        }
    }
}

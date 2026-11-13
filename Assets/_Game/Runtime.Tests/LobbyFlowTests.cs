using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Home lobby without Play Mode: the navigation graph (every screen reachable, a way back from each, one default focus),
    /// mode to MatchRequest mapping (sizes the sim supports, rules on the world's own config, the human in control), and the
    /// continue logic (Play again, Rematch on the same seed after an abandoned match).
    /// </summary>
    [TestFixture]
    public sealed class LobbyFlowTests
    {
        readonly Dictionary<string, int?> _saved = new Dictionary<string, int?>();
        bool _savedSpectate;
        ClassId _savedClass;

        [SetUp]
        public void SetUp()
        {
            _saved.Clear();
            var keys = new List<string>(SetupPrefs.AllKeys);
            keys.AddRange(PlayModes.AllKeys);
            foreach (string k in keys)
            {
                _saved[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
                PlayerPrefs.DeleteKey(k);
            }
            _savedSpectate = SpectatorMode.SpectateNextMatch;
            _savedClass = ClassSelect.SelectedClass;
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = true;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (KeyValuePair<string, int?> kv in _saved)
            {
                if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value);
                else PlayerPrefs.DeleteKey(kv.Key);
            }
            PlayerPrefs.Save();
            SpectatorMode.SpectateNextMatch = _savedSpectate;
            ClassSelect.SelectedClass = _savedClass;
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = false;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
            MatchSpeed.DisplayPace = 1;
        }

        static readonly MatchMode[] AllModes =
        {
            MatchMode.Custom, MatchMode.Quick, MatchMode.FullBattle, MatchMode.Training, MatchMode.Practice, MatchMode.Spectate, MatchMode.Demo,
        };

        // ---- navigation graph ----

        [Test]
        public void EveryLobbyScreenIsReachableFromTheTitle()
        {
            var seen = new HashSet<LobbyScreen> { LobbyScreen.Title };
            var queue = new Queue<LobbyScreen>();
            queue.Enqueue(LobbyScreen.Title);
            while (queue.Count > 0)
            {
                LobbyScreen s = queue.Dequeue();
                Assert.IsTrue(LobbyNav.Forward.ContainsKey(s), s + " has no forward list");
                foreach (LobbyScreen n in LobbyNav.Forward[s])
                    if (seen.Add(n)) queue.Enqueue(n);
            }
            foreach (LobbyScreen s in (LobbyScreen[])Enum.GetValues(typeof(LobbyScreen)))
                Assert.IsTrue(seen.Contains(s), s + " cannot be reached from the title");
        }

        [Test]
        public void EveryScreenHasABackPathHome()
        {
            foreach (LobbyScreen s in (LobbyScreen[])Enum.GetValues(typeof(LobbyScreen)))
            {
                Assert.IsTrue(LobbyNav.Back.ContainsKey(s), s + " has no Back");
                if (s == LobbyScreen.Match) continue;   // the pause menu: Resume, or Quit to the title
                LobbyScreen at = s;
                int hops = 0;
                while (at != LobbyScreen.Home && hops < 4)
                {
                    at = LobbyNav.Back[at];
                    hops++;
                }
                Assert.AreEqual(LobbyScreen.Home, at, s + ": pressing Back must reach HOME (no dead ends, no loops)");
            }
            // In a match the pause menu's Quit to title reaches the title, and the title's Back is HOME.
            CollectionAssert.Contains(LobbyNav.Forward[LobbyScreen.Match], LobbyScreen.Title);
            Assert.AreEqual(LobbyScreen.Home, LobbyNav.Back[LobbyScreen.Title]);
        }

        static void AssertOneDefaultFocus(FlowScreen s)
        {
            MenuItem d = s.DefaultItem;
            Assert.IsNotNull(d, s.GetType().Name + " has no default focus");
            Assert.IsTrue(d.interactable, s.GetType().Name + ": the default focus must be pressable");
            Assert.IsTrue(d.transform.IsChildOf(s.Canvas.transform), s.GetType().Name + ": the default focus belongs to the screen");
        }

        static void AssertNavigationLeadsToLiveItems(IEnumerable<MenuItem> items)
        {
            foreach (MenuItem it in items)
            {
                if (it == null || !it.interactable) continue;
                Navigation n = it.navigation;
                Assert.AreEqual(Navigation.Mode.Explicit, n.mode, it.name);
                foreach (Selectable s in new[] { n.selectOnUp, n.selectOnDown, n.selectOnLeft, n.selectOnRight })
                    if (s != null) Assert.IsTrue(s.interactable, it.name + " leads to a disabled item " + s.name);
                Assert.IsTrue(n.selectOnUp != null || n.selectOnDown != null, it.name + " is a navigation dead end");
            }
        }

        [Test]
        public void HomeHasOneDefaultFocusAndNoComingSoonTiles()
        {
            var go = new GameObject("Lobby Home Test");
            try
            {
                MainMenu m = go.AddComponent<MainMenu>();
                m.EnsureBuilt();
                AssertOneDefaultFocus(m);
                Assert.AreSame(m.PlayItem, m.DefaultItem, "PLAY is the focused primary button");
                foreach (MenuEntry e in m.Entries)
                {
                    Assert.IsTrue(e.Available, e.Id + " is shown but leads nowhere");
                    Assert.IsFalse(MainMenu.PlayFlowIds.Contains(e.Id), e.Id + " belongs on the mode cards");
                }
                foreach (MenuItem it in m.Items) Assert.IsNull(it.transform.Find("Soon"), "no coming soon on home");
                var all = new List<MenuItem>(m.Items) { m.ChangeModeItem, m.PlayItem };
                AssertNavigationLeadsToLiveItems(all);
                Assert.IsNotNull(m.Item("barracks"), "BARRACKS tile");
                Assert.IsNotNull(m.Item("settings"), "SETTINGS tile");
                Assert.IsNotNull(m.Item("quit"), "QUIT tile");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ModeSelectShowsSixCardsWithAWayBack()
        {
            var go = new GameObject("Lobby Modes Test");
            try
            {
                ModeSelect s = go.AddComponent<ModeSelect>();
                s.EnsureBuilt();
                AssertOneDefaultFocus(s);
                Assert.AreEqual(6, s.Cards.Count);
                foreach (MatchMode m in PlayModes.SelectOrder)
                {
                    MenuItem card = s.Card(m);
                    Assert.IsNotNull(card, m.ToString());
                    Assert.IsTrue(card.interactable, m + " is pressable");
                    Assert.IsNotNull(card.transform.Find("Line"), m + " has its one-line description");
                    Assert.IsNotNull(card.transform.Find("Teams"), m + " shows team size");
                    Assert.IsNotNull(card.transform.Find("Length"), m + " shows typical length");
                }
                Assert.IsNotNull(s.Card(MatchMode.Spectate).transform.Find("Watching"), "Spectate is labelled as watching");
                Assert.IsNull(s.Card(MatchMode.Spectate).transform.Find("Pip 0"), "watching has no difficulty");
                Assert.IsNotNull(s.BackItem);
                var all = new List<MenuItem>(s.Cards.Values) { s.BackItem };
                AssertNavigationLeadsToLiveItems(all);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ReadyScreenListsTheRealRostersAndLabelsBots()
        {
            var go = new GameObject("Lobby Ready Test");
            try
            {
                ReadyScreen r = go.AddComponent<ReadyScreen>();
                MatchSetupChoice c = PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 8);
                c.Class = ClassId.Miner;
                r.Bind(new PlayLaunch { Choice = c });
                AssertOneDefaultFocus(r);
                Assert.AreSame(r.StartItem, r.DefaultItem, "START NOW skips the countdown");
                Assert.IsNotNull(r.BackItem);
                Assert.AreEqual(8, r.RowsPerSide);
                Assert.AreEqual("YOU", r.RowTag(0, 0));
                for (int i = 1; i < 8; i++) Assert.AreEqual("BOT", r.RowTag(0, i));
                for (int i = 0; i < 8; i++) Assert.AreEqual("BOT", r.RowTag(1, i), "every opponent is labelled a bot");
                Assert.IsFalse(r.RowShown(0, 8), "no rows past the team size");

                // Integration batch 4: bots carry the names they will have in the match (the sim's table for this seed).
                var named = new PlayLaunch { Choice = c, Seed = 4242 };
                r.Bind(named);
                Assert.AreEqual(4242, named.Seed, "a given seed is kept");
                var world = World.CreateWorld(MatchRequest.From(c, 4242).ToWorldOptions(GameConfig.CreateDefault()));
                CriticalMass.Runtime.View.Mechanics.DepthNames.Bind(world);
                for (int i = 1; i < 8; i++)
                    Assert.AreEqual(CriticalMass.Runtime.View.Mechanics.DepthNames.Name(i), r.RowName(0, i), "ally row " + i);
                for (int i = 0; i < 8; i++)
                    Assert.AreEqual(CriticalMass.Runtime.View.Mechanics.DepthNames.Name(8 + i), r.RowName(1, i), "enemy row " + i);
                Assert.IsNotNull(r.RowName(1, 0));
                var fresh = new PlayLaunch { Choice = c };
                r.Bind(fresh);
                Assert.AreNotEqual(0, fresh.Seed, "the READY screen fixes the seed so the names on it are the match's");

                // The roster on screen is the one the sim builds (classes come from World.BuildRoster, seed-independent).
                var expect = new ClassId[16];
                World.BuildRoster(GameConfig.CreateDefault(), 8, ClassId.Miner, expect);
                Assert.AreEqual(ClassId.Miner, expect[0]);

                MatchSetupChoice watch = PlayModes.ToChoice(MatchMode.Spectate, MatchSetupChoice.Default, 5);
                r.Bind(new PlayLaunch { Choice = watch });
                Assert.AreEqual(16, r.RowsPerSide);
                Assert.AreEqual("AUTOPILOT", r.RowTag(0, 0), "spectating: your fighter plays itself");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ---- mode -> MatchRequest ----

        [Test]
        public void ModesMapToSizesTheSimSupports()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            var expect = new Dictionary<MatchMode, int>
            {
                { MatchMode.Quick, 5 }, { MatchMode.FullBattle, 16 }, { MatchMode.Training, 5 }, { MatchMode.Practice, 5 },
                { MatchMode.Spectate, 16 }, { MatchMode.Custom, 16 }, { MatchMode.Demo, 5 },
            };
            foreach (MatchMode m in AllModes)
            {
                MatchSetupChoice c = PlayModes.ToChoice(m, MatchSetupChoice.Default, 5);
                MatchRequest r = MatchRequest.From(c, 4242);
                Assert.AreEqual(m, r.Mode, m.ToString());
                Assert.AreEqual(expect[m], r.TeamSize, m + " team size");
                Assert.AreEqual(r.TeamSize, World.ClampTeamSize(cfg, r.TeamSize), m + ": a size the sim really runs");
                Assert.IsFalse(r.Backdrop, m + " is a real match, never a backdrop");
                Assert.AreEqual(4242, r.Seed);
            }
            Assert.AreEqual(8, MatchRequest.From(PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 8), 1).TeamSize, "quick 8 v 8");
            Assert.AreEqual(5, PlayModes.ValidQuickSize(7), "unsupported quick sizes fall back to 5 v 5");
            foreach (int n in PlayModes.QuickSizes) Assert.LessOrEqual(n, cfg.TeamSizeMax);
            CollectionAssert.Contains(PlayModes.QuickSizes, GameFlow.BackdropTeamSize, "8 v 8 is the size the menu backdrop already runs");
        }

        [Test]
        public void ShortModesRunAtStandardSoTheirLengthIsTrue()
        {
            MatchSetupChoice fast = MatchSetupChoice.Default;
            fast.Pace = MatchSpeed.FastPace;
            Assert.AreEqual(MatchSpeed.StandardPace, PlayModes.ToChoice(MatchMode.Quick, fast, 5).Pace);
            Assert.AreEqual(MatchSpeed.StandardPace, PlayModes.ToChoice(MatchMode.Demo, fast, 5).Pace);
            Assert.AreEqual(MatchSpeed.FastPace, PlayModes.ToChoice(MatchMode.FullBattle, fast, 5).Pace, "full battle keeps the chosen speed");
            Assert.AreEqual("ABOUT 4 MIN", PlayModes.LengthLabel(MatchMode.Quick, MatchSpeed.StandardPace));
            Assert.AreEqual("80 SECONDS", PlayModes.LengthLabel(MatchMode.Demo, MatchSpeed.StandardPace));
        }

        [Test]
        public void ModeRulesGoOnTheWorldsOwnConfigAndCustomIsUnchanged()
        {
            GameConfig reference = GameConfig.CreateDefault();

            GameConfig custom = GameConfig.CreateDefault();
            MatchRequest.From(PlayModes.ToChoice(MatchMode.Custom, MatchSetupChoice.Default, 5), 7).ToWorldOptions(custom);
            Assert.AreEqual(reference.MatchSeconds, custom.MatchSeconds);
            Assert.AreEqual(reference.HatchSeconds, custom.HatchSeconds);
            Assert.AreEqual(reference.Bots.PassiveSeconds, custom.Bots.PassiveSeconds, "custom = today's rules bit for bit");

            GameConfig quick = GameConfig.CreateDefault();
            WorldOptions o = MatchRequest.From(PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 5), 7).ToWorldOptions(quick);
            Assert.AreEqual(PlayModes.QuickMatchSeconds, quick.MatchSeconds);
            Assert.AreEqual(PlayModes.QuickHatchSeconds, quick.HatchSeconds);
            Assert.AreEqual(5, o.TeamSize);
            Assert.AreSame(quick, o.Config);

            GameConfig practice = GameConfig.CreateDefault();
            MatchRequest.From(PlayModes.ToChoice(MatchMode.Practice, MatchSetupChoice.Default, 5), 7).ToWorldOptions(practice);
            Assert.AreEqual(PlayModes.PracticeMatchSeconds, practice.MatchSeconds);
            Assert.GreaterOrEqual(practice.Bots.PassiveSeconds, practice.MatchSeconds, "practice: bots never target the player");

            GameConfig demo = GameConfig.CreateDefault();
            MatchRequest.From(PlayModes.ToChoice(MatchMode.Demo, MatchSetupChoice.Default, 5), 7).ToWorldOptions(demo);
            Assert.AreEqual(PlayModes.DemoMatchSeconds, demo.MatchSeconds);
            Assert.AreEqual(PlayModes.DemoHatchSeconds, demo.HatchSeconds);
            Assert.AreEqual(PlayModes.DemoPassiveSeconds, demo.Bots.PassiveSeconds);

            // A backdrop never takes a mode's rules.
            GameConfig back = GameConfig.CreateDefault();
            var r = new MatchRequest { Seed = 3, TeamSize = 8, Pace = 0.5, Backdrop = true, Rules = PlayModes.Rules(MatchMode.Demo) };
            r.ToWorldOptions(back);
            Assert.AreEqual(reference.MatchSeconds, back.MatchSeconds);

            // The hatch never lands after the final whistle.
            GameConfig clamp = GameConfig.CreateDefault();
            new MatchRules { MatchSeconds = 60, HatchSeconds = 90 }.ApplyTo(clamp);
            Assert.AreEqual(60, clamp.HatchSeconds);
        }

        [Test]
        public void EveryModeButSpectateStartsWithTheHumanInControl()
        {
            foreach (MatchMode m in AllModes)
            {
                if (m == MatchMode.Custom || m == MatchMode.Training) continue;   // Custom keeps setup's flags; Training is the guided match
                SpectatorMode.SpectateNextMatch = m != MatchMode.Spectate;      // a stale flag from the other side
                GameFlow.QueueMatch(PlayModes.ToChoice(m, MatchSetupChoice.Default, 5));
                MatchRequest r = GameFlow.RequestForBuild();
                Assert.IsFalse(r.Backdrop, m + ": not a backdrop, so actor 0 is not put on autopilot");
                Assert.AreEqual(m == MatchMode.Spectate, SpectatorMode.SpectateNextMatch, m + ": spectating only in Spectate");
                Assert.IsFalse(PlayModes.GuidedMatchPending, m + ": never the guided match");
            }
            // Custom: setup decides (its SPECTATE choice survives).
            SpectatorMode.SpectateNextMatch = true;
            GameFlow.QueueMatch(PlayModes.ToChoice(MatchMode.Custom, MatchSetupChoice.Default, 5));
            Assert.IsTrue(SpectatorMode.SpectateNextMatch);
        }

        [Test]
        public void ARematchSeedIsHonoured()
        {
            GameFlow.QueueMatch(PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 5), 987654);
            Assert.AreEqual(987654, GameFlow.RequestForBuild().Seed);
            GameFlow.QueueMatch(PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 5), 0);
            Assert.AreNotEqual(987654, GameFlow.RequestForBuild().Seed, "0 means a new seed");
        }

        // ---- continue logic ----

        [Test]
        public void HomeOffersPlayFirstThenPlayAgainWithTheLastMode()
        {
            HomeOffer o = PlayModes.Offer();
            Assert.IsFalse(o.PlayAgain);
            Assert.AreEqual(MatchMode.Quick, o.Mode, "first visit: Quick match is suggested");
            Assert.AreEqual("PLAY", o.PrimaryTitle);
            Assert.IsFalse(o.Rematch);

            PlayModes.Save(MatchMode.FullBattle, 8);
            Assert.AreEqual(MatchMode.FullBattle, PlayModes.Offer().Mode, "a returning player sees the mode they picked last");
            Assert.IsFalse(PlayModes.Offer().PlayAgain, "but PLAY AGAIN only after a match this session");
            Assert.AreEqual(8, PlayModes.SavedQuickSize);

            MatchSetupChoice practice = PlayModes.ToChoice(MatchMode.Practice, MatchSetupChoice.Default, 5);
            GameFlow.QueueMatch(practice);
            GameFlow.RequestForBuild();
            o = PlayModes.Offer();
            Assert.IsTrue(o.PlayAgain);
            Assert.AreEqual(MatchMode.Practice, o.Mode);
            Assert.AreEqual(practice.TeamSize, o.Setup.TeamSize);
            Assert.AreEqual("PLAY AGAIN", o.PrimaryTitle);
        }

        [Test]
        public void AfterTrainingOrTheDemoHomeSuggestsARealMatch()
        {
            foreach (MatchMode lesson in new[] { MatchMode.Training, MatchMode.Demo })
            {
                HomeOffer o = PlayModes.Offer(true, lesson, MatchSetupChoice.Default, MatchMode.FullBattle, false, default, 0);
                Assert.IsFalse(o.PlayAgain, lesson + " is not offered again");
                Assert.IsTrue(o.NextStep);
                Assert.AreEqual(MatchMode.Quick, o.Mode);
            }
        }

        [Test]
        public void QuittingAMatchOffersARematchOnTheSameSeedNotAFakeResume()
        {
            MatchSetupChoice quick = PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 8);
            GameFlow.QueueMatch(quick);
            int seed = GameFlow.RequestForBuild().Seed;
            Assert.AreEqual(FlowState.Match, GameFlow.State);

            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.Title;   // pause, Quit to title
            MatchRequest back = GameFlow.RequestForBuild();
            Assert.IsTrue(back.Backdrop);
            HomeOffer o = PlayModes.Offer();
            Assert.IsTrue(o.Rematch, "an abandoned match offers a rematch");
            Assert.AreEqual(seed, o.RematchSeed, "on the same seed");
            Assert.AreEqual(MatchMode.Quick, o.RematchSetup.Mode);
            Assert.AreEqual(8, o.RematchSetup.TeamSize);

            GameFlow.QueueMatch(o.RematchSetup, o.RematchSeed);
            Assert.AreEqual(seed, GameFlow.RequestForBuild().Seed, "the rematch is created from that seed");
            Assert.IsFalse(PlayModes.Offer().Rematch, "starting any match clears the offer");
        }

        [Test]
        public void RestartsFinishedMatchesAndLessonsAreNotAbandoned()
        {
            GameFlow.QueueMatch(PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 5));
            GameFlow.RequestForBuild();
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.Play;   // Restart match
            GameFlow.RequestForBuild();
            Assert.IsFalse(PlayModes.Offer().Rematch, "a restart is not an abandon");

            GameFlow.QueueMatch(PlayModes.ToChoice(MatchMode.Demo, MatchSetupChoice.Default, 5));
            GameFlow.RequestForBuild();
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.Title;
            GameFlow.RequestForBuild();
            Assert.IsFalse(PlayModes.Offer().Rematch, "leaving the demo early offers no rematch");
        }

        // ---- quick demo ----

        [Test]
        public void DemoBeatsCoverMoveHitHillAndKaijuInsideItsClock()
        {
            QuickDemo.Beat[] b = QuickDemo.Beats;
            for (int i = 1; i < b.Length; i++) Assert.Greater(b[i].At, b[i - 1].At, "beats in time order");
            Assert.Less(b[b.Length - 1].At, PlayModes.DemoMatchSeconds, "the last line comes before the final whistle");
            Assert.AreEqual(-1, QuickDemo.BeatAt(0));
            Assert.AreEqual(b.Length - 1, QuickDemo.BeatAt(PlayModes.DemoMatchSeconds));
            int hit = Array.FindIndex(b, x => x.Id == "demo.hit"), kaiju = Array.FindIndex(b, x => x.Id == "demo.kaiju");
            Assert.GreaterOrEqual(b[hit].At, PlayModes.DemoPassiveSeconds - 1, "the hit line comes when the bots start attacking");
            Assert.Greater(b[kaiju].At, PlayModes.DemoHatchSeconds, "the Kaiju line comes after the hatch");
            Assert.That(PlayModes.DemoMatchSeconds >= 60 && PlayModes.DemoMatchSeconds <= 90, "a 60 to 90 second demo");
            QuickDemo.RegisterLines();
            foreach (QuickDemo.Beat x in b)
                Assert.AreNotEqual(x.Id, CriticalMass.Runtime.Hud.MascotLines.Resolve(x.Id), x.Id + " has words");
        }
    }
}

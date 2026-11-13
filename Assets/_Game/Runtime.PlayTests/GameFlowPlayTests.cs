using System;
using System.Collections;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// DG-01 in the real Boot scene with the flow forced on: title, main menu, match setup (Crimson, Miner, 5 v 5), the
    /// match at 8x to its end, results, Play again, three matches in a row with a new seed each, zero logged errors and
    /// under 50 MB of memory growth; the timer-expiry end (the owner's 226 to 230 report) reaching the results within a
    /// second with live controls; keyboard navigation skipping coming-soon lines; the Settings bridge and Back.
    /// </summary>
    public sealed class GameFlowPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";
        const long MemoryBudgetBytes = 50L * 1024 * 1024;

        Keyboard _kb;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;

        [SetUp]
        public void SetUp()
        {
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = true;
            GameFlow.Autoplay = false;
            SimDriverBehaviour.CaptureMode = false;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
        }

        [TearDown]
        public void TearDown()
        {
            if (_kb != null) InputSystem.RemoveDevice(_kb);
            _kb = null;
            if (_settingsSaved)
            {
                InputSettings s = InputSystem.settings;
                s.backgroundBehavior = _savedBackground;
                s.editorInputBehaviorInPlayMode = _savedEditorBehaviour;
                _settingsSaved = false;
            }
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = false;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
        }

        static SimDriverBehaviour Driver() => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();

        /// <summary>Waits until a driver other than previous exists with a world (a Boot build finished).</summary>
        static IEnumerator WaitForNewDriver(SimDriverBehaviour previous, float timeout = 15f)
        {
            timeout = TestClock.Budget(timeout);   // a Boot build: load time, not a promise (P-04)
            float t0 = Time.realtimeSinceStartup;
            while (true)
            {
                SimDriverBehaviour d = Driver();
                if (d != null && d != previous && d.World != null && FlowDirector.Current != null && !FlowDirector.Current.Loading) yield break;
                if (Time.realtimeSinceStartup - t0 > timeout) Assert.Fail("no new Boot build within " + timeout + " s");
                yield return null;
            }
        }

        /// <summary>Presses item as Enter / A would (ISubmitHandler), after the open-guard of the screen.</summary>
        static IEnumerator Press(MenuItem item)
        {
            Assert.IsNotNull(item);
            Assert.IsTrue(item.IsInteractable(), item.name + " is not pressable");
            while (Time.unscaledTime < MenuItem.SubmitBlockedUntil) yield return null;
            ExecuteEvents.Execute(item.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            yield return null;
        }

        /// <summary>Home lobby: HOME, CHANGE MODE, then the CUSTOM card opens the full match setup.</summary>
        static IEnumerator OpenCustomSetup(FlowDirector d)
        {
            yield return Press(d.MainMenu.ChangeModeItem);
            Assert.IsTrue(d.Modes.IsVisible, "CHANGE MODE opens the mode cards");
            yield return Press(d.Modes.Card(MatchMode.Custom));
        }

        static IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - t0 > timeout) Assert.Fail(what + " (waited " + timeout + " s)");
                yield return null;
            }
        }

        static long Memory() => Profiler.GetTotalAllocatedMemoryLong();

        IEnumerator BootToTitle()
        {
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return WaitForNewDriver(null);
            Assert.AreEqual(FlowState.Title, GameFlow.State);
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<TitleScreen>(), "the title shows first");
            Assert.IsTrue(Driver().Request != null && Driver().Request.Backdrop, "the title runs over a bot backdrop, not the match");
            Assert.IsFalse(Driver().GetComponent<GameHud>().enabled, "no HUD behind the title");
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator TitleMenuSetupMatchResultsPlayAgainThreeTimes()
        {
            yield return BootToTitle();
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return null;
            FlowDirector d = FlowDirector.Current;
            Assert.AreEqual(FlowState.Menu, GameFlow.State);
            Assert.IsTrue(d.MainMenu.IsVisible);
            Assert.AreEqual(d.MainMenu.PlayItem, d.MainMenu.Focused, "home: PLAY is focused");

            yield return OpenCustomSetup(d);
            Assert.AreEqual(FlowState.Setup, GameFlow.State);
            Assert.IsTrue(d.Setup.IsVisible);
            d.Setup.SetTeam(TeamId.Crimson);
            d.Setup.SetClass(ClassId.Miner);
            d.Setup.SetSize(5);
            d.Setup.SetPace(2);
            SimDriverBehaviour previous = Driver();
            yield return Press(d.Setup.StartItem);

            int prevSeed = previous.World.Opts.Seed;
            long mem0 = 0;
            for (int match = 0; match < 3; match++)
            {
                yield return WaitForNewDriver(previous);
                SimDriverBehaviour driver = Driver();
                World w = driver.World;
                d = FlowDirector.Current;
                Assert.AreEqual(FlowState.Match, GameFlow.State, "match " + match);
                MatchStats stats = MatchStatsRecorder.Current;
                Assert.IsNotNull(stats);
                Assert.AreEqual(0, stats.StartTick, "stats begin at the world's tick 0");
                Assert.AreEqual(TeamId.Crimson, stats.StartTeam, "actor 0 is Crimson at tick 0");
                Assert.AreEqual(ClassId.Miner, stats.StartClass, "actor 0 is a Miner at tick 0");
                Assert.AreEqual(TeamId.Crimson, w.Actors[0].Team);
                Assert.AreEqual(ClassId.Miner, w.Actors[0].Cls);
                Assert.AreEqual(10, w.ActorCount, "5 v 5");
                Assert.AreNotEqual(prevSeed, w.Opts.Seed, "every match gets a new seed");
                prevSeed = w.Opts.Seed;
                Assert.IsTrue(driver.GetComponent<GameHud>().enabled, "the HUD shows in the match");

                if (match == 0)
                {
                    yield return Resources.UnloadUnusedAssets();
                    GC.Collect();
                    mem0 = Memory();
                }

                driver.HumanAutopilot = true;
                driver.TimeScale = 8;
                yield return Until(() => w.Match.Phase == MatchPhase.Ended, 300f, "the match did not end at 8x");
                float endedAt = Time.realtimeSinceStartup;
                yield return Until(() => d.Results.IsVisible, 1.5f, "no results screen after the match ended");
                Assert.Less(Time.realtimeSinceStartup - endedAt, 1.0f + 0.1f, "results within a second");
                Assert.AreEqual(FlowState.Results, GameFlow.State);
                Assert.IsFalse(driver.GetComponent<GameHud>().enabled, "the HUD is hidden on the results");
                Assert.That(d.Results.TitleText, Is.EqualTo("VICTORY").Or.EqualTo("DEFEAT").Or.EqualTo("DRAW"));
                StringAssert.Contains("S", d.Results.ReasonText);
                Assert.GreaterOrEqual(d.Results.Stats.Mvp(TeamId.Azure), 0);
                Assert.GreaterOrEqual(d.Results.Stats.Mvp(TeamId.Crimson), 0);
                yield return Until(() => d.Results.Focused == d.Results.PlayAgainItem, 1f, "Play again is focused");

                previous = driver;
                if (match < 2) yield return Press(d.Results.PlayAgainItem);
            }

            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            long growth = Memory() - mem0;
            Assert.Less(growth, MemoryBudgetBytes, $"memory grew {growth / (1024 * 1024)} MB across 3 matches");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TimerExpiryAtCloseScoresShowsResultsWithinASecondAndPlayAgainWorks()
        {
            GameFlow.QueueMatch(new MatchSetupChoice { Team = TeamId.Azure, Class = ClassId.Warrior, TeamSize = 5, Pace = 2 });
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return WaitForNewDriver(null);
            SimDriverBehaviour driver = Driver();
            FlowDirector d = FlowDirector.Current;
            Assert.AreEqual(FlowState.Match, GameFlow.State);
            World w = driver.World;
            driver.TimeScale = 0;
            driver.StepTicks(30);
            // The owner's report: clock at 00:00 with 226 to 230. Put the clock two ticks from the end and step past it.
            w.Match.Time = w.Config.MatchSeconds - 2.0 / w.Config.SimHz;
            w.Match.HillSeconds.Set(226, 230);
            driver.StepTicks(4);
            Assert.AreEqual(MatchPhase.Ended, w.Match.Phase);
            float endedAt = Time.realtimeSinceStartup;
            yield return Until(() => d.Results.IsVisible, 1.0f, "the results screen must appear within a second of the clock running out");
            Assert.AreEqual(FlowState.Results, GameFlow.State);
            HumanInputSource input = driver.GetComponent<HumanInputSource>();
            Assert.IsNotNull(input.PauseHandler, "Escape / Start are live on the results (Back to the menu)");
            Assert.IsFalse(input.State.Paused);
            yield return Until(() => d.Results.Focused == d.Results.PlayAgainItem, 1f, "Play again is focused: the controls moved to the menu");
            double az = w.Match.HillSeconds.Azure, cr = w.Match.HillSeconds.Crimson;
            StringAssert.Contains(Math.Round(Math.Max(az, cr)).ToString("0"), d.Results.ReasonText);
            StringAssert.Contains(Math.Round(Math.Min(az, cr)).ToString("0"), d.Results.ReasonText);
            Assert.AreEqual(w.Match.Winner, d.Results.Summary.Winner);

            int seed = w.Opts.Seed;
            yield return Press(d.Results.PlayAgainItem);
            yield return WaitForNewDriver(driver);
            Assert.AreEqual(FlowState.Match, GameFlow.State);
            Assert.AreNotEqual(seed, Driver().World.Opts.Seed);
            Assert.AreEqual(0, MatchStatsRecorder.Current.StartTick);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PauseMenuOpenAtTheEndIsClosedAndResultsShow()
        {
            GameFlow.QueueMatch(new MatchSetupChoice { Team = TeamId.Crimson, Class = ClassId.Farmer, TeamSize = 5, Pace = 1 });
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return WaitForNewDriver(null);
            SimDriverBehaviour driver = Driver();
            FlowDirector d = FlowDirector.Current;
            World w = driver.World;
            driver.TimeScale = 0;
            w.Match.Time = w.Config.MatchSeconds - 2.0 / w.Config.SimHz;
            driver.StepTicks(4);   // ends the match
            PauseMenu pm = driver.GetComponent<PauseMenu>();
            Assert.IsNotNull(pm);
            pm.Pause();   // the player hit Escape on the last frame
            yield return Until(() => d.Results.IsVisible, 1.0f, "results within a second even with the pause menu up");
            Assert.IsFalse(pm.IsOpen, "the pause menu is closed for the results");
            Assert.IsFalse(driver.GetComponent<HumanInputSource>().State.Paused);
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator PrepareKeyboard()
        {
            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _kb = InputSystem.AddDevice<Keyboard>();
            yield return null;
        }

        IEnumerator Tap(Key key)
        {
            InputSystem.QueueStateEvent(_kb, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;
            yield return null;
        }

        /// <summary>
        /// Integration batch 3 (owner's blocking report on the batch 2 build: "it runs by itself", W never takes control).
        /// A match started the way a player does (title, main menu PLAY, setup START), then a real key event through the
        /// Input System (a keyboard device and a queued state event, not the input source): within one second the human
        /// fighter must move screen-up, the driver's autopilot must be off and the AUTOPILOT banner gone.
        /// </summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator RealKeyboardTakesControlOfTheFighterWithinOneSecond()
        {
            yield return BootToTitle();
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return null;
            FlowDirector d = FlowDirector.Current;
            yield return OpenCustomSetup(d);
            Assert.AreEqual(FlowState.Setup, GameFlow.State);
            SimDriverBehaviour previous = Driver();
            yield return Press(d.Setup.StartItem);
            yield return WaitForNewDriver(previous);
            SimDriverBehaviour driver = Driver();
            World w = driver.World;
            yield return Until(() => w.Match.Phase == MatchPhase.Playing && w.Actors[0].Alive, 30f, "the match reached Playing with the fighter alive");
            yield return PrepareKeyboard();
            var presence = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Spectator.PlayerPresence>();
            bool autoBefore = driver.HumanAutopilot;
            bool bannerBefore = presence != null && presence.BannerVisible;
            double x0 = w.X[0], z0 = w.Z[0];
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.W));
            float t0 = Time.realtimeSinceStartup;
            double along = 0;
            while (Time.realtimeSinceStartup - t0 < 1f)
            {
                yield return null;
                along = (w.X[0] - x0) * HumanInputState.ScreenUpX + (w.Z[0] - z0) * HumanInputState.ScreenUpZ;
                if (!driver.HumanAutopilot && along > 0.3 && (presence == null || !presence.BannerVisible)) break;
            }
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;
            string report = $"autopilot before {autoBefore}, banner before {bannerBefore}; after {Time.realtimeSinceStartup - t0:0.00} s: autopilot {driver.HumanAutopilot}, " +
                            $"banner {(presence != null && presence.BannerVisible)}, moved {along:0.00} u screen-up, keyboard current {Keyboard.current == _kb}";
            Debug.Log("[RealInput] " + report);
            Assert.IsFalse(driver.HumanAutopilot, "the autopilot still drives the fighter after a real W press: " + report);
            Assert.IsFalse(presence != null && presence.BannerVisible, "the AUTOPILOT banner is still up: " + report);
            Assert.Greater(along, 0.3, "the fighter did not move screen-up within one second of a real W press: " + report);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator KeyboardSkipsComingSoonOpensSettingsAndBackReturnsToTheMenu()
        {
            yield return BootToTitle();
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return null;
            yield return PrepareKeyboard();
            FlowDirector d = FlowDirector.Current;
            while (Time.unscaledTime < MenuItem.SubmitBlockedUntil) yield return null;

            // Home lobby: down from PLAY wraps to the first tile on the rail (only lines that lead somewhere are shown).
            string next = d.MainMenu.Entries[0].Id;
            yield return Tap(Key.DownArrow);
            Assert.AreEqual(d.MainMenu.Item(next), d.MainMenu.Focused, "down from PLAY wraps to the first home tile");

            // Walk down to Controls and open it: the pause menu's own settings page on the Controls tab.
            for (int guard = 0; guard < 8 && d.MainMenu.Focused != d.MainMenu.Item("controls"); guard++) yield return Tap(Key.DownArrow);
            Assert.AreEqual(d.MainMenu.Item("controls"), d.MainMenu.Focused);
            yield return Tap(Key.Enter);
            PauseMenu pm = Driver().GetComponent<PauseMenu>();
            Assert.IsTrue(pm.IsOpen && pm.CurrentPage == PauseMenu.Page.Settings, "Controls opens the settings page");
            Assert.AreEqual(PauseMenu.Tab.Controls, pm.CurrentTab);
            Assert.IsFalse(d.MainMenu.IsVisible);

            // Escape backs out of the settings page to the main menu (never to the pause root, never a dead end).
            yield return new WaitForSecondsRealtime(0.35f);
            yield return Tap(Key.Escape);
            yield return Until(() => d.MainMenu.IsVisible, 1f, "Back from settings returns to the main menu");
            Assert.IsFalse(pm.IsOpen);
            Assert.AreEqual(d.MainMenu.Item("controls"), d.MainMenu.Focused, "focus comes back to the line that opened it");

            // Escape on the main menu goes back to the title.
            yield return Tap(Key.Escape);
            yield return Until(() => UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null, 1f, "Back from the menu shows the title");
            Assert.AreEqual(FlowState.Title, GameFlow.State);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SetupBackReturnsToTheMenuAndChangeClassBackReturnsToResults()
        {
            yield return BootToTitle();
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return null;
            FlowDirector d = FlowDirector.Current;
            yield return OpenCustomSetup(d);
            Assert.IsTrue(d.Setup.IsVisible);
            yield return Press(d.Setup.BackItem);
            Assert.IsTrue(d.Modes.IsVisible, "setup's Back returns to the mode cards");
            yield return Press(d.Modes.BackItem);
            Assert.IsTrue(d.MainMenu.IsVisible);
            Assert.AreEqual(FlowState.Menu, GameFlow.State);

            yield return OpenCustomSetup(d);
            SimDriverBehaviour previous = Driver();
            d.Setup.SetSize(5);
            yield return Press(d.Setup.StartItem);
            yield return WaitForNewDriver(previous);
            d = FlowDirector.Current;
            World w = Driver().World;
            Driver().TimeScale = 0;
            Driver().StepTicks(30);   // past the start (as TimerExpiry...): a tick-0 clock change does not end the match
            w.Match.Time = w.Config.MatchSeconds - 2.0 / w.Config.SimHz;
            Driver().StepTicks(4);
            Assert.AreEqual(MatchPhase.Ended, w.Match.Phase, "the clock ran out");
            // The 1 s promise is TimerExpiry...'s; here the match came through the menus' fade, and batch-mode frames are
            // slow, so this navigation test allows 3 s.
            // ci/playmode-triage (P-04): the menus' fade and ResultsDelaySeconds run on unscaled time, so the budget scales.
            yield return Until(() => d.Results.IsVisible, TestClock.Budget(3.0f), "results");
            yield return Press(d.Results.ChangeClassItem);
            Assert.IsTrue(d.Setup.IsVisible);
            yield return Press(d.Setup.BackItem);
            Assert.IsTrue(d.Results.IsVisible, "Back from Change class returns to the same results");
            Assert.AreEqual(FlowState.Results, GameFlow.State);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

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
            Assert.AreEqual(d.MainMenu.Item("play"), d.MainMenu.Focused, "Play is focused");

            yield return Press(d.MainMenu.Item("play"));
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

        [UnityTest, Timeout(120000)]
        public IEnumerator KeyboardSkipsComingSoonOpensSettingsAndBackReturnsToTheMenu()
        {
            yield return BootToTitle();
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return null;
            yield return PrepareKeyboard();
            FlowDirector d = FlowDirector.Current;
            while (Time.unscaledTime < MenuItem.SubmitBlockedUntil) yield return null;

            // Down from Play lands on the next line that exists (How to play, Barracks are coming soon unless registered).
            string next = null;
            var entries = d.MainMenu.Entries;
            for (int i = 1; i < entries.Count && next == null; i++)
                if (entries[i].Available) next = entries[i].Id;
            yield return Tap(Key.DownArrow);
            Assert.AreEqual(d.MainMenu.Item(next), d.MainMenu.Focused, "down skips the coming-soon lines");

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
            yield return Press(d.MainMenu.Item("play"));
            Assert.IsTrue(d.Setup.IsVisible);
            yield return Press(d.Setup.BackItem);
            Assert.IsTrue(d.MainMenu.IsVisible);
            Assert.AreEqual(FlowState.Menu, GameFlow.State);

            yield return Press(d.MainMenu.Item("play"));
            SimDriverBehaviour previous = Driver();
            d.Setup.SetSize(5);
            yield return Press(d.Setup.StartItem);
            yield return WaitForNewDriver(previous);
            d = FlowDirector.Current;
            World w = Driver().World;
            Driver().TimeScale = 0;
            w.Match.Time = w.Config.MatchSeconds - 2.0 / w.Config.SimHz;
            Driver().StepTicks(4);
            yield return Until(() => d.Results.IsVisible, 1.0f, "results");
            yield return Press(d.Results.ChangeClassItem);
            Assert.IsTrue(d.Setup.IsVisible);
            yield return Press(d.Setup.BackItem);
            Assert.IsTrue(d.Results.IsVisible, "Back from Change class returns to the same results");
            Assert.AreEqual(FlowState.Results, GameFlow.State);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

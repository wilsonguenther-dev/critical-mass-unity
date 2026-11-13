using System;
using System.Collections;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// fix/human-control (owner, 2026-11-12: "it's just playing by itself ... I can't even move"). Every way into a match
    /// a player has, walked through the real game flow (title, main menu, match setup, results, pause menu, spectator
    /// mode) with the flow forced on and the clock running, and then a virtual keyboard and a virtual gamepad driven
    /// through the Input System device layer (InputSystem.AddDevice + queued state events, the same path a real key
    /// takes): W and the left stick must move actor 0 up the screen, nothing may leave actor 0 on autopilot, and the
    /// lockout watchdog must stay quiet. The older control tests set HumanAutopilot = false by hand in a flow-off world,
    /// so none of them walked a menu path into a match before pressing a key.
    /// </summary>
    public sealed class HumanControlPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";
        /// <summary>Real seconds a key or the stick is held for a move check.</summary>
        const float HoldSeconds = 1.0f;
        /// <summary>World units actor 0 must travel up the screen in HoldSeconds (about a quarter of a Warrior's run).</summary>
        const double MinTravel = 0.5;

        Keyboard _kb;
        Gamepad _pad;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = true;
            GameFlow.Autoplay = false;
            SimDriverBehaviour.CaptureMode = false;
            SpectatorMode.SpectateNextMatch = false;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
            HumanControlGuard.IdleAutopilotEnabled = true;
            HumanControlGuard.IdleAutopilotSeconds = HumanControlGuard.DefaultIdleAutopilotSeconds;

            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _kb = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_kb != null) InputSystem.RemoveDevice(_kb);
            if (_pad != null) InputSystem.RemoveDevice(_pad);
            _kb = null;
            _pad = null;
            if (_settingsSaved)
            {
                InputSettings s = InputSystem.settings;
                s.backgroundBehavior = _savedBackground;
                s.editorInputBehaviorInPlayMode = _savedEditorBehaviour;
                _settingsSaved = false;
            }
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = false;
            SpectatorMode.SpectateNextMatch = false;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
            HumanControlGuard.IdleAutopilotEnabled = true;
            HumanControlGuard.IdleAutopilotSeconds = HumanControlGuard.DefaultIdleAutopilotSeconds;
        }

        // ---------------------------------------------------------------- helpers

        static SimDriverBehaviour Driver() => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();

        static IEnumerator WaitForNewDriver(SimDriverBehaviour previous, float timeout = 20f)
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

        static IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - t0 > timeout) Assert.Fail(what + " (waited " + timeout + " s)");
                yield return null;
            }
        }

        static IEnumerator Press(MenuItem item)
        {
            Assert.IsNotNull(item);
            Assert.IsTrue(item.IsInteractable(), item.name + " is not pressable");
            while (Time.unscaledTime < MenuItem.SubmitBlockedUntil) yield return null;
            ExecuteEvents.Execute(item.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
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

        IEnumerator Real(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        /// <summary>Title, main menu, PLAY (or SPECTATE), match setup, START: returns when the new match world exists.</summary>
        IEnumerator BootIntoMatch(bool spectate = false)
        {
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return WaitForNewDriver(null);
            Assert.AreEqual(FlowState.Title, GameFlow.State);
            TitleScreen title = UnityEngine.Object.FindAnyObjectByType<TitleScreen>();
            Assert.IsNotNull(title, "the title shows first");
            Assert.IsTrue(Driver().HumanAutopilot, "the title's backdrop plays itself");
            title.Hide();
            yield return null;
            FlowDirector d = FlowDirector.Current;
            Assert.AreEqual(FlowState.Menu, GameFlow.State);
            yield return Press(d.MainMenu.Item(spectate ? OverlayRegistry.Spectate : OverlayRegistry.Play));
            Assert.AreEqual(FlowState.Setup, GameFlow.State);
            Assert.AreEqual(spectate, d.Setup.Spectate, spectate ? "SPECTATE opens setup on SPECTATE" : "PLAY opens setup on a team");
            if (!spectate) d.Setup.SetTeam(TeamId.Azure);
            d.Setup.SetSize(16);
            d.Setup.SetPace(2);
            SimDriverBehaviour backdrop = Driver();
            yield return Press(d.Setup.StartItem);
            yield return WaitForNewDriver(backdrop);
            Assert.AreEqual(FlowState.Match, GameFlow.State);
        }

        /// <summary>The live match is running (loading screen gone, Playing, actor 0 standing), then the spawn crowd clears.</summary>
        IEnumerator Settle(string where)
        {
            SimDriverBehaviour d = Driver();
            yield return Until(() => !Menus.LoadingScreen.IsOpen, 20f, where + ": the loading screen never closed");
            yield return Until(() => d.World.Match.Phase == MatchPhase.Playing && d.World.Actors[0].Alive, 20f,
                where + ": the match never reached Playing with actor 0 standing");
            yield return Real(1.5f);
        }

        static double TravelUp(World w, double x0, double z0) =>
            (w.X[0] - x0) * HumanInputState.ScreenUpX + (w.Z[0] - z0) * HumanInputState.ScreenUpZ;

        /// <summary>
        /// The player has the fighter: not on autopilot, not spectating, the guard sees a human match with no lockout, and
        /// W on the keyboard and then the left stick each carry actor 0 up the screen.
        /// </summary>
        IEnumerator AssertPlayerMovesFighter(string where)
        {
            yield return Settle(where);
            SimDriverBehaviour d = Driver();
            World w = d.World;
            HumanControlGuard guard = d.GetComponent<HumanControlGuard>();
            Assert.IsNotNull(guard, where + ": the overlay registry did not install HumanControlGuard");
            Assert.IsTrue(guard.HumanMatch, where + ": the guard does not see a human match");
            Assert.IsFalse(SpectatorMode.IsSpectating, where + ": spectating");
            Assert.IsFalse(d.HumanAutopilot, where + ": actor 0 is on autopilot");
            Assert.IsFalse(d.GetComponent<HumanInputSource>().State.Paused, where + ": paused");
            Assert.IsNull(guard.LockoutReason, where + ": gameplay input is off: " + guard.LockoutReason);
            InputActionMap map = InputSystem.actions.FindActionMap("Player", true);
            Assert.IsTrue(map.enabled, where + ": the Player action map is disabled");
            Assert.IsTrue(map["Move"].enabled, where + ": the Move action is disabled");

            // Keyboard W.
            yield return Until(() => w.Actors[0].Alive, 10f, where + ": actor 0 down before the key check");
            double x0 = w.X[0], z0 = w.Z[0];
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.W));
            yield return Real(HoldSeconds);
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;
            Assert.IsFalse(d.HumanAutopilot, where + ": autopilot while W was held");
            Assert.Greater(TravelUp(w, x0, z0), MinTravel, where + ": W did not move the fighter up the screen");

            // Gamepad left stick.
            yield return Until(() => w.Actors[0].Alive, 10f, where + ": actor 0 down before the stick check");
            x0 = w.X[0];
            z0 = w.Z[0];
            InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = new Vector2(0f, 1f) });
            yield return Real(HoldSeconds);
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            yield return null;
            Assert.IsFalse(d.HumanAutopilot, where + ": autopilot while the stick was pushed");
            Assert.Greater(TravelUp(w, x0, z0), MinTravel, where + ": the left stick did not move the fighter up the screen");
            Assert.AreEqual(0, guard.LockoutsReported, where + ": the lockout watchdog fired");
        }

        // ---------------------------------------------------------------- entry points

        [UnityTest, Timeout(120000)]
        public IEnumerator PlayFromTheMainMenuThroughMatchSetup()
        {
            yield return BootIntoMatch();
            yield return AssertPlayerMovesFighter("Play > match setup > Start");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PlayAgainFromTheResults()
        {
            yield return BootIntoMatch();
            yield return Settle("first match");
            FlowDirector.Current.ShowResults();
            yield return null;
            Assert.AreEqual(FlowState.Results, GameFlow.State);
            SimDriverBehaviour before = Driver();
            yield return Press(FlowDirector.Current.Results.PlayAgainItem);
            yield return WaitForNewDriver(before);
            yield return AssertPlayerMovesFighter("Results > Play again");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator RestartFromThePauseMenu()
        {
            yield return BootIntoMatch();
            yield return Settle("first match");
            yield return Tap(Key.Escape);
            PauseMenu pm = Driver().GetComponent<PauseMenu>();
            Assert.IsTrue(pm.IsOpen, "Escape opens the pause menu");
            SimDriverBehaviour before = Driver();
            pm.RestartMatch();
            yield return WaitForNewDriver(before);
            yield return AssertPlayerMovesFighter("Pause > Restart match");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AfterClosingTheFullMap()
        {
            yield return BootIntoMatch();
            yield return Settle("match");
            HudInteract hud = Driver().GetComponent<HudInteract>();
            Assert.IsNotNull(hud);
            yield return Tap(Key.M);
            Assert.IsTrue(hud.MapOpen, "M opens the full map");
            yield return Tap(Key.M);
            Assert.IsFalse(hud.MapOpen, "M closes the full map");
            yield return AssertPlayerMovesFighter("after closing the map");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AfterPauseAndResume()
        {
            yield return BootIntoMatch();
            yield return Settle("match");
            PauseMenu pm = Driver().GetComponent<PauseMenu>();
            yield return Tap(Key.Escape);
            Assert.IsTrue(pm.IsOpen, "Escape opens the pause menu");
            yield return Tap(Key.Escape);
            yield return Until(() => !pm.IsOpen, 2f, "Escape again closes the pause menu");
            yield return AssertPlayerMovesFighter("after pause and resume");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AfterFocusLossAndRegain()
        {
            yield return BootIntoMatch();
            yield return Settle("match");
            // Alt-tab: the same callbacks Unity sends the Game root's components when the window loses and regains focus.
            Driver().gameObject.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
            yield return Real(0.5f);
            Driver().gameObject.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
            yield return null;
            yield return AssertPlayerMovesFighter("after alt-tab out and back");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AfterLeavingSpectate()
        {
            yield return BootIntoMatch();
            yield return Settle("match");
            Assert.IsTrue(SpectatorMode.Enter(), "spectate from the match");
            yield return null;
            Assert.IsTrue(SpectatorMode.IsSpectating);
            Assert.IsTrue(Driver().HumanAutopilot, "spectating: the fighter plays itself");
            yield return Until(() => Driver().World.Actors[0].Alive, 15f, "actor 0 standing to rejoin at once");
            yield return Tap(Key.R);
            yield return Until(() => !SpectatorMode.IsSpectating, 15f, "R rejoins");
            yield return AssertPlayerMovesFighter("after leaving Spectate");
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- lobby paths (integration batch 4)

        /// <summary>Boots to the title and waits for its backdrop (the title is the first screen).</summary>
        IEnumerator BootToTitle()
        {
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return WaitForNewDriver(null);
            Assert.AreEqual(FlowState.Title, GameFlow.State);
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<TitleScreen>(), "the title shows first");
        }

        /// <summary>Title, HOME, PLAY (the mode cards on a fresh session), then the card for mode m.</summary>
        IEnumerator HomeToModeCard(MatchMode m)
        {
            yield return BootToTitle();
            TitleScreen title = UnityEngine.Object.FindAnyObjectByType<TitleScreen>();
            yield return Press(title.MainMenuItem);
            yield return Until(() => GameFlow.State == FlowState.Menu && FlowDirector.Current.MainMenu.IsVisible, 5f, "HOME opens the home lobby");
            FlowDirector d = FlowDirector.Current;
            yield return Press(d.MainMenu.PlayItem);
            yield return Until(() => d.Modes.IsVisible, 5f, "PLAY opens the mode cards");
            yield return Press(d.Modes.Card(m));
        }

        /// <summary>The class screen is up: confirm the focused class; the READY screen follows.</summary>
        IEnumerator ConfirmClassThenReady()
        {
            yield return Until(() => ClassSelect.IsOpen, 5f, "the mode card opens class select");
            yield return null;
            ClassSelect.Instance.ConfirmSetup(ClassSelect.SelectedClass);
            yield return StartFromReady();
        }

        /// <summary>READY: START NOW, then wait for the match world.</summary>
        IEnumerator StartFromReady()
        {
            FlowDirector d = FlowDirector.Current;
            yield return Until(() => d.Ready.IsVisible, 5f, "the READY screen shows");
            SimDriverBehaviour before = Driver();
            yield return Press(d.Ready.StartItem);
            yield return WaitForNewDriver(before);
            Assert.AreEqual(FlowState.Match, GameFlow.State);
        }

        IEnumerator ModeCardPath(MatchMode m)
        {
            yield return HomeToModeCard(m);
            yield return ConfirmClassThenReady();
            yield return AssertPlayerMovesFighter("Home > Play > " + m + " > class select > Ready");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TitlePlayNowGivesControl()
        {
            yield return BootToTitle();
            SimDriverBehaviour before = Driver();
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().PressPlayNow();
            yield return WaitForNewDriver(before);
            yield return AssertPlayerMovesFighter("Title > PLAY NOW");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TitleQuickDemoGivesControl()
        {
            yield return BootToTitle();
            SimDriverBehaviour before = Driver();
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().PressDemo();
            yield return WaitForNewDriver(before);
            yield return AssertPlayerMovesFighter("Title > QUICK DEMO");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator HomeQuickMatchGivesControl() { yield return ModeCardPath(MatchMode.Quick); }

        [UnityTest, Timeout(120000)]
        public IEnumerator HomeFullBattleGivesControl() { yield return ModeCardPath(MatchMode.FullBattle); }

        [UnityTest, Timeout(120000)]
        public IEnumerator HomeTrainingGivesControl() { yield return ModeCardPath(MatchMode.Training); }

        [UnityTest, Timeout(120000)]
        public IEnumerator HomePracticeGivesControl() { yield return ModeCardPath(MatchMode.Practice); }

        [UnityTest, Timeout(120000)]
        public IEnumerator HomeCustomGivesControl()
        {
            yield return HomeToModeCard(MatchMode.Custom);
            FlowDirector d = FlowDirector.Current;
            yield return Until(() => GameFlow.State == FlowState.Setup && d.Setup.IsVisible, 5f, "CUSTOM opens the match setup");
            SimDriverBehaviour before = Driver();
            yield return Press(d.Setup.StartItem);
            yield return WaitForNewDriver(before);
            yield return AssertPlayerMovesFighter("Home > Play > Custom > setup > Start");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator HomeSpectateNeverHandsOverTheFighter()
        {
            yield return HomeToModeCard(MatchMode.Spectate);
            yield return StartFromReady();
            yield return Settle("Spectate");
            SimDriverBehaviour d = Driver();
            Assert.IsTrue(d.HumanAutopilot, "Spectate: the fighter plays itself");
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.W));
            yield return Real(HoldSeconds);
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = new Vector2(0f, 1f) });
            yield return Real(HoldSeconds);
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            yield return null;
            Assert.IsTrue(d.HumanAutopilot, "Spectate is the one mode where W and the stick do not take the fighter");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator RematchFromHomeAfterQuittingGivesControl()
        {
            yield return ModeCardPathNoAsserts(MatchMode.Quick);
            yield return Settle("first match");
            PauseMenu pm = Driver().GetComponent<PauseMenu>();
            yield return Tap(Key.Escape);
            Assert.IsTrue(pm.IsOpen, "Escape opens the pause menu");
            SimDriverBehaviour before = Driver();
            pm.QuitToTitle();
            yield return WaitForNewDriver(before);
            TitleScreen title = UnityEngine.Object.FindAnyObjectByType<TitleScreen>();
            Assert.IsNotNull(title, "Quit to title shows the title");
            yield return Press(title.MainMenuItem);
            FlowDirector d = FlowDirector.Current;
            yield return Until(() => d.MainMenu.IsVisible, 5f, "HOME after quitting");
            Assert.IsNotNull(d.MainMenu.RematchItem, "home offers REMATCH after an abandoned match");
            yield return Press(d.MainMenu.RematchItem);
            yield return StartFromReady();
            yield return AssertPlayerMovesFighter("Quit > Home > REMATCH > Ready");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator ModeCardPlayAgainAndPauseResumeKeepControl()
        {
            yield return ModeCardPathNoAsserts(MatchMode.Quick);
            yield return Settle("Quick match");
            FlowDirector.Current.ShowResults();
            yield return null;
            SimDriverBehaviour before = Driver();
            yield return Press(FlowDirector.Current.Results.PlayAgainItem);
            yield return WaitForNewDriver(before);
            yield return AssertPlayerMovesFighter("Quick match > results > Play again");
            PauseMenu pm = Driver().GetComponent<PauseMenu>();
            yield return Tap(Key.Escape);
            Assert.IsTrue(pm.IsOpen, "Escape opens the pause menu");
            yield return Tap(Key.Escape);
            yield return Until(() => !pm.IsOpen, 2f, "Escape again resumes");
            yield return AssertPlayerMovesFighter("Quick match > pause > resume");
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator ModeCardPathNoAsserts(MatchMode m)
        {
            yield return HomeToModeCard(m);
            yield return ConfirmClassThenReady();
        }

        // ---------------------------------------------------------------- autopilot and the watchdog

        [UnityTest, Timeout(120000)]
        public IEnumerator IdleAutopilotSaysSoAndAnyInputTakesControlOnTheSameFrame()
        {
            yield return BootIntoMatch();
            yield return Settle("match");
            SimDriverBehaviour d = Driver();
            HumanControlGuard guard = d.GetComponent<HumanControlGuard>();
            PlayerPresence presence = d.GetComponent<PlayerPresence>();
            Assert.AreEqual(45f, HumanControlGuard.DefaultIdleAutopilotSeconds, "autopilot waits for a long idle");

            HumanControlGuard.IdleAutopilotSeconds = 0.3f;
            yield return Until(() => d.HumanAutopilot, 3f, "idle autopilot");
            yield return null;
            Assert.IsTrue(guard.IdleAutopilot);
            Assert.IsTrue(presence.BannerVisible, "the banner shows");
            StringAssert.Contains("COMPUTER IS PLAYING YOUR FIGHTER", presence.BannerPrompt);
            StringAssert.IsMatch("PRESS ANY (KEY|BUTTON) TO PLAY", presence.BannerPrompt);   // the prompt names the device used last (a test gamepad exists)

            // A key: control is back by the end of the frame the event arrives on (the guard runs before the driver).
            HumanControlGuard.IdleAutopilotSeconds = HumanControlGuard.DefaultIdleAutopilotSeconds;
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.Space));
            yield return null;
            Assert.IsFalse(d.HumanAutopilot, "a key takes control on the same frame");
            Assert.AreEqual(Time.frameCount, guard.TookBackFrame, "same frame");
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;

            // A gamepad button, a mouse button: the same.
            d.HumanAutopilot = true;
            yield return null;
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.South));
            yield return null;
            Assert.IsFalse(d.HumanAutopilot, "a controller button takes control on the same frame");
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            yield return null;
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                d.HumanAutopilot = true;
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
                yield return null;
                Assert.IsFalse(d.HumanAutopilot, "a mouse button takes control on the same frame");
                InputSystem.QueueStateEvent(mouse, new MouseState());
                yield return null;
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
            yield return Real(0.2f);
            Assert.IsFalse(presence.BannerVisible, "the banner goes with the autopilot");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator WatchdogLogsShowsAndRepairsInputOffForTwoSeconds()
        {
            yield return BootIntoMatch();
            yield return Settle("match");
            HumanControlGuard guard = Driver().GetComponent<HumanControlGuard>();
            InputActionMap map = InputSystem.actions.FindActionMap("Player", true);
            map.Disable();
            yield return Real(1.0f);
            Assert.IsFalse(guard.LockoutWarningVisible, "no warning before the two seconds");
            Assert.AreEqual("Player action map disabled", guard.LockoutReason);
            LogAssert.Expect(LogType.Error, new Regex(@"\[CM\.Control\] LOCKOUT: .*Player action map disabled"));
            yield return Until(() => guard.LockoutsReported == 1, 3f, "the watchdog reports the lockout");
            Assert.IsTrue(map.enabled, "the watchdog switches the map back on");
            yield return AssertPlayerMovesFighterAfterRepair();
        }

        IEnumerator AssertPlayerMovesFighterAfterRepair()
        {
            SimDriverBehaviour d = Driver();
            World w = d.World;
            yield return Until(() => w.Actors[0].Alive, 10f, "actor 0 standing");
            double x0 = w.X[0], z0 = w.Z[0];
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.W));
            yield return Real(HoldSeconds);
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;
            Assert.Greater(TravelUp(w, x0, z0), MinTravel, "W moves the fighter after the repair");
        }

        // ---------------------------------------------------------------- Play versus Spectate

        [UnityTest, Timeout(120000)]
        public IEnumerator PlayAndSpectateAreClearlyDifferent()
        {
            yield return BootIntoMatch(spectate: true);
            yield return Settle("Spectate");
            SimDriverBehaviour d = Driver();
            Assert.IsTrue(SpectatorMode.IsSpectating, "SPECTATE > WATCH MATCH starts watching");
            Assert.IsTrue(d.HumanAutopilot, "watching: the fighter plays itself");
            SpectatorOverlay overlay = d.GetComponent<SpectatorOverlay>();
            Assert.IsTrue(overlay.WatchVisible, "the spectator card shows in Spectate");
            Assert.IsFalse(d.GetComponent<PlayerPresence>().BannerVisible, "no autopilot banner while spectating");

            // Back to the menu, then PLAY: a different mode, no card, the fighter is the player's.
            SimDriverBehaviour before = d;
            FlowDirector.Current.ToMainMenu();
            yield return WaitForNewDriver(before);
            Assert.AreEqual(FlowState.Menu, GameFlow.State);
            FlowDirector fd = FlowDirector.Current;
            yield return Press(fd.MainMenu.Item(OverlayRegistry.Play));
            Assert.IsFalse(fd.Setup.Spectate, "PLAY never carries Spectate into setup");
            before = Driver();
            yield return Press(fd.Setup.StartItem);
            yield return WaitForNewDriver(before);
            yield return Settle("Play after Spectate");
            SpectatorOverlay overlay2 = Driver().GetComponent<SpectatorOverlay>();
            Assert.IsFalse(SpectatorMode.IsSpectating, "PLAY plays");
            Assert.IsFalse(overlay2.WatchVisible, "no spectator card in a played match");
            yield return AssertPlayerMovesFighter("Play after a Spectate match");
            LogAssert.NoUnexpectedReceived();
        }
    }
}

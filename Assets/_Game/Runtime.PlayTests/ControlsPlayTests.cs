using System;
using System.Collections;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// controls-v1 in the real Boot scene with virtual devices (InputSystem.AddDevice and queued state events, as
    /// HumanInputPlayTests; the Input System test package is not added, so Packages/manifest.json is untouched):
    /// arrow keys and the left stick move actor 0 up the screen, the right stick aims, every controller button reaches
    /// its action, Escape / Start open the pause menu and B / Start close it, and a pause neither advances nor changes the
    /// sim (the world after pause and resume equals an unpaused reference world tick for tick).
    /// </summary>
    public sealed class ControlsPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";
        const int HoldTicks = 30;
        const double SettleSeconds = 3;
        static readonly double S = Math.Sqrt(0.5);

        SimDriverBehaviour _driver;
        HumanInputSource _input;
        PauseMenu _menu;
        Keyboard _kb;
        Gamepad _pad;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Capture mode: actor 0 on autopilot and the wall clock stopped from tick 0, so the world is a pure function
            // of the ticks stepped (the pause test compares it with a reference world). Reset at once: only this driver.
            SimDriverBehaviour.CaptureMode = true;
            SceneManager.LoadScene(BootScene);
            yield return null;
            SimDriverBehaviour.CaptureMode = false;
            yield return null;
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver, "Boot did not build a SimDriverBehaviour");
            _input = _driver.GetComponent<HumanInputSource>();
            _menu = _driver.GetComponent<PauseMenu>();
            Assert.IsNotNull(_input);
            Assert.IsNotNull(_menu, "MenuInstaller did not attach the pause menu");
            Assert.AreEqual(0, _driver.World.Tick, "capture mode: no ticks before the test steps them");

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
            SimDriverBehaviour.CaptureMode = false;
            _driver = null;
            _input = null;
            _menu = null;
        }

        /// <summary>Human-driven actor 0 after the spawn crowd has left the pad (HumanInputPlayTests.SettleSeconds).</summary>
        void TakeControl()
        {
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            GameConfig c = _driver.World.Config;
            _driver.StepTicks((int)Math.Ceiling(SettleSeconds * c.SimHz));
        }

        /// <summary>Queues a state; the next frame's input update processes it before any Update runs.</summary>
        IEnumerator Send<TState>(InputDevice d, TState state) where TState : struct, IInputStateTypeInfo
        {
            InputSystem.QueueStateEvent(d, state);
            yield return null;
        }

        IEnumerator HoldAndStep(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                yield return null;
                _driver.StepTicks(1);
            }
        }

        /// <summary>Screen displacement of actor 0 between two sim points, through the gameplay camera.</summary>
        static Vector2 ScreenDelta(double x0, double z0, double x1, double z1)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Assert.IsNotNull(cam);
            Vector3 a = cam.WorldToScreenPoint(SimSpace.ToWorld(x0, 0, z0));
            Vector3 b = cam.WorldToScreenPoint(SimSpace.ToWorld(x1, 0, z1));
            return new Vector2(b.x - a.x, b.y - a.y);
        }

        [UnityTest]
        public IEnumerator ArrowKeysMoveActor0UpAndRightOnTheScreen()
        {
            TakeControl();
            double x0 = _driver.World.X[0], z0 = _driver.World.Z[0];
            yield return Send(_kb, new KeyboardState(Key.UpArrow));
            yield return HoldAndStep(HoldTicks);
            double x1 = _driver.World.X[0], z1 = _driver.World.Z[0];
            Assert.Greater((-(x1 - x0) - (z1 - z0)) * S, 0.5, "up arrow: along sim screen-up");
            Vector2 d = ScreenDelta(x0, z0, x1, z1);
            Assert.Greater(d.y, 0f, "up arrow moves up the screen");
            Assert.Less(Mathf.Abs(d.x), 0.2f * d.y, "no sideways drift on screen");

            yield return Send(_kb, new KeyboardState(Key.RightArrow));
            yield return HoldAndStep(HoldTicks);
            double x2 = _driver.World.X[0], z2 = _driver.World.Z[0];
            d = ScreenDelta(x1, z1, x2, z2);
            Assert.Greater(d.x, 0f, "right arrow moves right on the screen");
            Assert.Less(Mathf.Abs(d.y), 0.35f * d.x);
            yield return Send(_kb, new KeyboardState());
        }

        [UnityTest]
        public IEnumerator LeftStickUpMovesActor0UpTheScreenAndTheControllerBecomesActive()
        {
            TakeControl();
            double x0 = _driver.World.X[0], z0 = _driver.World.Z[0];
            yield return Send(_pad, new GamepadState { leftStick = new Vector2(0f, 1f) });
            Assert.AreEqual(ControlDevice.Gamepad, ControlBindings.ActiveDevice);
            yield return HoldAndStep(HoldTicks);
            Vector2 d = ScreenDelta(x0, z0, _driver.World.X[0], _driver.World.Z[0]);
            Assert.Greater(d.y, 0f);
            Assert.Less(Mathf.Abs(d.x), 0.2f * d.y);

            // Inside the dead zone: no move.
            yield return Send(_pad, new GamepadState { leftStick = new Vector2(0.1f, 0.1f) });
            Assert.AreEqual(Vector2.zero, InputSystem.actions.FindAction("Player/Move").ReadValue<Vector2>());
            yield return Send(_pad, new GamepadState());
            yield return Send(_kb, new KeyboardState(Key.A));
            Assert.AreEqual(ControlDevice.KeyboardMouse, ControlBindings.ActiveDevice);
            yield return Send(_kb, new KeyboardState());
        }

        [UnityTest]
        public IEnumerator RightStickAimsAndRestingItAimsAlongTheMove()
        {
            TakeControl();
            yield return Send(_pad, new GamepadState { rightStick = new Vector2(1f, 0f) });
            yield return null;
            Assert.AreEqual(AimResolver.Mode.Stick, _input.Aim.Current);
            Assert.IsTrue(_input.State.HasAim);
            double px = _driver.World.X[0], pz = _driver.World.Z[0];
            Assert.AreEqual(px + S * AimResolver.StickAimDistance, _input.State.AimX, 1e-3, "aim to screen right");
            Assert.AreEqual(pz - S * AimResolver.StickAimDistance, _input.State.AimZ, 1e-3);
            InputFrame f = SimInput.EmptyInput();
            _input.State.Fill(f);
            Assert.IsTrue(double.IsFinite(f.AimX) && double.IsFinite(f.AimZ), "the sim receives the stick aim point");

            // Right stick released, left stick up: the aim turns to the move direction (up the screen).
            yield return Send(_pad, new GamepadState { leftStick = new Vector2(0f, 1f) });
            yield return null;
            px = _driver.World.X[0];
            pz = _driver.World.Z[0];
            Assert.AreEqual(px - S * AimResolver.StickAimDistance, _input.State.AimX, 1e-3);
            Assert.AreEqual(pz - S * AimResolver.StickAimDistance, _input.State.AimZ, 1e-3);
            yield return Send(_pad, new GamepadState());
        }

        [UnityTest]
        public IEnumerator EveryControllerButtonFiresItsAction()
        {
            TakeControl();
            InputFrame f = SimInput.EmptyInput();
            HumanInputState st = _input.State;

            yield return Press(new GamepadState().WithButton(GamepadButton.South));
            st.Fill(f);
            Assert.IsTrue(f.Jump, "A = jump");
            yield return Release(f);

            yield return Press(new GamepadState { rightTrigger = 1f });
            st.Fill(f);
            Assert.IsTrue(f.Attack, "RT = attack");
            yield return Release(f);

            yield return Press(new GamepadState { leftTrigger = 1f });
            st.Fill(f);
            Assert.IsTrue(f.Ability, "LT = class ability");
            yield return Release(f);

            yield return Press(new GamepadState().WithButton(GamepadButton.East));
            st.Fill(f);
            Assert.IsTrue(f.Ability, "B = class ability");
            yield return Release(f);

            yield return Press(new GamepadState().WithButton(GamepadButton.West));
            st.Fill(f);
            Assert.IsTrue(f.Interact, "X = interact");
            yield return Release(f);

            yield return Press(new GamepadState().WithButton(GamepadButton.North));
            st.Fill(f);
            Assert.IsTrue(f.Terraform, "Y = dig / place");
            yield return Release(f);

            InputAction sw = InputSystem.actions.FindAction("Player/SwitchDigPlace");
            yield return Press(new GamepadState().WithButton(GamepadButton.RightShoulder));
            Assert.AreEqual(1f, sw.ReadValue<float>(), "RB = switch to place");
            yield return Release(f);
            yield return Press(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            Assert.AreEqual(-1f, sw.ReadValue<float>(), "LB = switch to dig");
            yield return Release(f);

            yield return Press(new GamepadState().WithButton(GamepadButton.Select));
            Assert.IsTrue(st.ScoreboardHeld, "View = scoreboard");
            yield return Release(f);
            Assert.IsFalse(st.ScoreboardHeld);

            yield return Press(new GamepadState().WithButton(GamepadButton.Start));
            Assert.IsTrue(st.Paused, "Start = pause");
            yield return null;
            Assert.IsTrue(_menu.IsOpen, "the pause menu opens");
            yield return Release(f);
            yield return Press(new GamepadState().WithButton(GamepadButton.Start));
            Assert.IsFalse(st.Paused, "Start again = resume");
            yield return null;
            Assert.IsFalse(_menu.IsOpen);
            yield return Send(_pad, new GamepadState());
        }

        IEnumerator Press(GamepadState s) => Send(_pad, s);

        IEnumerator Release(InputFrame f)
        {
            yield return Send(_pad, new GamepadState());
            _input.State.Fill(f);   // consume any latch
            yield return new WaitForSecondsRealtime(0.32f);   // past the menu's after-press quiet time
        }

        [UnityTest]
        public IEnumerator EscapeOpensTheMenuAndBGoesBackThenResumes()
        {
            TakeControl();
            yield return Send(_kb, new KeyboardState(Key.Escape));
            yield return Send(_kb, new KeyboardState());
            yield return null;
            Assert.IsTrue(_input.State.Paused);
            Assert.IsTrue(_menu.IsOpen);
            Assert.AreEqual(PauseMenu.Page.Root, _menu.CurrentPage);

            _menu.ShowSettings(PauseMenu.Tab.Audio);
            Assert.AreEqual(PauseMenu.Page.Settings, _menu.CurrentPage);
            yield return Send(_pad, new GamepadState().WithButton(GamepadButton.East));
            yield return Send(_pad, new GamepadState());
            Assert.AreEqual(PauseMenu.Page.Root, _menu.CurrentPage, "B: settings -> pause page");
            Assert.IsTrue(_input.State.Paused);

            yield return Send(_kb, new KeyboardState(Key.Escape));
            yield return Send(_kb, new KeyboardState());
            yield return null;
            Assert.IsFalse(_input.State.Paused, "Escape on the pause page resumes");
            Assert.IsFalse(_menu.IsOpen);
        }

        [UnityTest]
        public IEnumerator PausingNeitherAdvancesNorChangesTheSim()
        {
            // Actor 0 stays on autopilot (capture mode), so the world depends only on the ticks stepped.
            GameConfig cfg = _driver.World.Config;
            _driver.StepTicks(90);
            yield return Send(_pad, new GamepadState().WithButton(GamepadButton.Start));
            yield return Send(_pad, new GamepadState());
            Assert.IsTrue(_input.State.Paused);
            int tick = _driver.World.Tick;
            double x5 = _driver.World.X[5], t = _driver.World.Match.Time;

            // Wall clock running while paused, and buttons pressed: nothing reaches the sim.
            _driver.TimeScale = 2;
            for (int i = 0; i < 20; i++) yield return null;
            Assert.AreEqual(tick, _driver.World.Tick, "no tick while paused, before any press");
            yield return Send(_pad, new GamepadState { rightTrigger = 1f });
            Assert.AreEqual(tick, _driver.World.Tick, $"no tick while paused, after the trigger (paused {_input.State.Paused}, menu {_menu.IsOpen})");
            yield return Send(_pad, new GamepadState().WithButton(GamepadButton.South));
            yield return Send(_pad, new GamepadState());
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(tick, _driver.World.Tick, $"no tick while paused (paused {_input.State.Paused}, menu {_menu.IsOpen}, page {_menu.CurrentPage})");
            Assert.AreEqual(x5, _driver.World.X[5]);
            Assert.AreEqual(t, _driver.World.Match.Time);
            InputFrame f = SimInput.EmptyInput();
            _input.State.Fill(f);
            Assert.IsFalse(f.Attack || f.Jump, "presses while paused are dropped");

            _driver.TimeScale = 0;
            yield return new WaitForSecondsRealtime(0.32f);
            _menu.Resume();
            Assert.IsFalse(_input.State.Paused);
            // Integration batch 2: PlayerPresence (#35) hands actor 0 to the player on any input, by design. This test compares
            // against an all-autopilot reference world, so it puts the autopilot back before stepping.
            _driver.HumanAutopilot = true;
            _driver.StepTicks(150);
            int total = _driver.World.Tick;

            World reference = World.CreateWorld(new WorldOptions
            {
                Seed = cfg.Seed,
                TeamSize = cfg.TeamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateDefault(),
            });
            InputFrame scratch = SimInput.EmptyInput();
            for (int i = 0; i < total; i++) MatchTick.Advance(reference, null, scratch);
            Assert.AreEqual(total, reference.Tick);
            Assert.AreEqual(reference.ActorCount, _driver.World.ActorCount);
            for (int i = 0; i < reference.ActorCount; i++)
            {
                Assert.AreEqual(reference.X[i], _driver.World.X[i], $"actor {i} x");
                Assert.AreEqual(reference.Z[i], _driver.World.Z[i], $"actor {i} z");
            }
            Assert.AreEqual(reference.Match.Time, _driver.World.Match.Time);
        }
    }
}

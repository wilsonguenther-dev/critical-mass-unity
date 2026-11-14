using System;
using System.Collections;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
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
    /// controls-1 in the real Boot scene with virtual devices (as ControlsPlayTests): keyboard-only play attacks with J and
    /// Z, uses the ability with K and X, faces the move direction with the keys, holds it with lock facing, hands the aim to
    /// the mouse the moment it moves and back to the keys on a keyboard attack; the controller families (A B X Y and the
    /// shape family) bind every action and survive an unplug mid-match (pause with the notice, resume on reconnect, the
    /// player keeps control).
    /// </summary>
    public sealed class KeyboardAndControllerPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";
        const double SettleSeconds = 3;
        static readonly double S = Math.Sqrt(0.5);

        SimDriverBehaviour _driver;
        HumanInputSource _input;
        Keyboard _kb;
        Mouse _mouse;
        readonly List<InputDevice> _extra = new List<InputDevice>();
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SimDriverBehaviour.CaptureMode = true;
            SceneManager.LoadScene(BootScene);
            yield return null;
            SimDriverBehaviour.CaptureMode = false;
            yield return null;
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver, "Boot did not build a SimDriverBehaviour");
            _input = _driver.GetComponent<HumanInputSource>();
            Assert.IsNotNull(_input);
            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _kb = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_kb != null) InputSystem.RemoveDevice(_kb);
            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
            foreach (InputDevice d in _extra)
                if (d != null && d.added) InputSystem.RemoveDevice(d);
            _extra.Clear();
            _kb = null;
            _mouse = null;
            if (_settingsSaved)
            {
                InputSettings s = InputSystem.settings;
                s.backgroundBehavior = _savedBackground;
                s.editorInputBehaviorInPlayMode = _savedEditorBehaviour;
                _settingsSaved = false;
            }
            ControllerSettings.ForceFamily(null);
            ControlBindings.SetKeyboardOnly(false);   // the game's start state (keys and mouse), for the classes after this one
            ControlBindings.SetHand(KeyboardHand.Wasd);
            SimDriverBehaviour.CaptureMode = false;
            _driver = null;
            _input = null;
        }

        void TakeControl()
        {
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            GameConfig c = _driver.World.Config;
            _driver.StepTicks((int)Math.Ceiling(SettleSeconds * c.SimHz));
        }

        static IEnumerator Send<TState>(InputDevice d, TState state) where TState : struct, IInputStateTypeInfo
        {
            InputSystem.QueueStateEvent(d, state);
            yield return null;
        }

        IEnumerator Tap(Key k)
        {
            yield return Send(_kb, new KeyboardState(k));
            yield return Send(_kb, new KeyboardState());
        }

        /// <summary>Unit sim direction from actor 0 to the current aim point.</summary>
        void AimDir(out double dx, out double dz)
        {
            Assert.IsTrue(_input.State.HasAim, "the keys aim: a point is always known");
            dx = _input.State.AimX - _driver.World.X[0];
            dz = _input.State.AimZ - _driver.World.Z[0];
            double l = Math.Sqrt(dx * dx + dz * dz);
            dx /= l;
            dz /= l;
        }

        [UnityTest]
        public IEnumerator JAndZAttackKAndXUseTheAbilityAndEachSetsItsHand()
        {
            TakeControl();
            HumanInputState st = _input.State;
            InputFrame f = SimInput.EmptyInput();
            var cases = new[] { (Key.J, true, KeyboardHand.Wasd), (Key.Z, true, KeyboardHand.Arrows), (Key.K, false, KeyboardHand.Wasd), (Key.X, false, KeyboardHand.Arrows) };
            foreach (var (key, attack, hand) in cases)
            {
                yield return Send(_kb, new KeyboardState(key));
                st.Fill(f);
                if (attack) Assert.IsTrue(f.Attack, $"{key} attacks");
                else Assert.IsTrue(f.Ability || st.AbilityBuffered, $"{key} uses the class ability");
                Assert.AreEqual(hand, ControlBindings.Hand, $"{key} belongs to the {hand} hand");
                yield return Send(_kb, new KeyboardState());
                st.Fill(f);
            }
            // C jumps (the arrow hand), V interacts.
            yield return Send(_kb, new KeyboardState(Key.C));
            st.Fill(f);
            Assert.IsTrue(f.Jump, "C jumps");
            yield return Send(_kb, new KeyboardState(Key.V));
            st.Fill(f);
            Assert.IsTrue(f.Interact, "V interacts");
            yield return Send(_kb, new KeyboardState());
        }

        [UnityTest]
        public IEnumerator KeyboardOnlyTheFighterFacesWhereItMovesAndLockFacingHoldsIt()
        {
            TakeControl();
            yield return Tap(Key.K);   // a keyboard ability key with the mouse never moved: the keys aim
            Assert.AreEqual(AimResolver.Mode.Keys, _input.Aim.Current);
            Assert.IsTrue(ControlBindings.KeyboardOnly);
            yield return Send(_kb, new KeyboardState(Key.UpArrow));
            yield return null;
            AimDir(out double dx, out double dz);
            // Screen up at yaw 45 is sim (-S, -S); target assist may bend it by up to 30 degrees.
            Assert.Greater(dx * -S + dz * -S, Math.Cos(31 * Math.PI / 180), "faces up the screen");
            // Lock facing (L) held, then right arrow: still faces up.
            yield return Send(_kb, new KeyboardState(Key.UpArrow, Key.L));
            yield return Send(_kb, new KeyboardState(Key.RightArrow, Key.L));
            yield return null;
            Assert.IsTrue(_input.FacingLocked);
            AimDir(out double lx, out double lz);
            Assert.Greater(lx * -S + lz * -S, 0.99, "lock facing keeps the facing while strafing");
            // Released: turns to screen right, sim (S, -S).
            yield return Send(_kb, new KeyboardState(Key.RightArrow));
            yield return null;
            AimDir(out double rx, out double rz);
            Assert.Greater(rx * S + rz * -S, Math.Cos(31 * Math.PI / 180), "faces right once released");
            // Standing still: keeps the last facing.
            yield return Send(_kb, new KeyboardState());
            yield return null;
            AimDir(out double sx, out double sz);
            Assert.Greater(sx * S + sz * -S, Math.Cos(31 * Math.PI / 180), "keeps facing standing still");
            InputFrame f = SimInput.EmptyInput();
            _input.State.Fill(f);
            Assert.IsTrue(double.IsFinite(f.AimX), "the sim receives the keyboard aim as a point");
        }

        [UnityTest]
        public IEnumerator TheMouseTakesTheAimWhenItMovesAndAKeyboardAttackHandsItBack()
        {
            TakeControl();
            yield return Tap(Key.J);
            Assert.AreEqual(AimResolver.Mode.Keys, _input.Aim.Current);
            yield return Send(_mouse, new MouseState { delta = new Vector2(20f, 0f), position = new Vector2(400f, 300f) });
            yield return null;
            Assert.AreEqual(AimResolver.Mode.Pointer, _input.Aim.Current, "a mouse move takes the aim");
            Assert.IsFalse(ControlBindings.KeyboardOnly);
            yield return Send(_mouse, new MouseState { position = new Vector2(400f, 300f) });
            // Too soon after the mouse moved: the mouse keeps the aim.
            yield return Tap(Key.J);
            Assert.AreEqual(AimResolver.Mode.Pointer, _input.Aim.Current, "a resting mouse needs a moment before the keys take over");
            yield return new WaitForSecondsRealtime(ControlBindings.MouseIdleSeconds + 0.1f);
            yield return Tap(Key.J);
            Assert.AreEqual(AimResolver.Mode.Keys, _input.Aim.Current, "a keyboard attack with the mouse resting hands the aim back");
            Assert.IsTrue(ControlBindings.KeyboardOnly);
            Assert.AreEqual("J", ControlBindings.PromptLabel(InputSystem.actions.FindAction("Player/Attack"), ControlDevice.KeyboardMouse));
        }

        [UnityTest]
        public IEnumerator LeftShiftLocksFacingOnTheKeysAndWalksToThePointerOnlyWithTheMouse()
        {
            TakeControl();
            yield return Tap(Key.X);
            Assert.AreEqual(AimResolver.Mode.Keys, _input.Aim.Current);
            yield return Send(_kb, new KeyboardState(Key.LeftShift));
            yield return null;
            Assert.IsTrue(_input.FacingLocked, "keys aim: L SHIFT locks the facing");
            Assert.IsFalse(_input.State.PointerWalkHeld, "keys aim: L SHIFT never walks to a resting cursor");
            yield return Send(_kb, new KeyboardState());
            yield return Send(_mouse, new MouseState { delta = new Vector2(20f, 0f), position = new Vector2(400f, 300f) });
            yield return Send(_kb, new KeyboardState(Key.LeftShift));
            yield return null;
            Assert.IsFalse(_input.FacingLocked, "mouse aim: no lock");
            Assert.IsTrue(_input.State.PointerWalkHeld, "mouse aim: L SHIFT walks to the pointer");
            yield return Send(_kb, new KeyboardState());
        }

        // ---- controller families ----

        static readonly (string layout, PadFamily family)[] Families =
        {
            ("XInputController", PadFamily.Xbox),
            ("DualShock4GamepadHID", PadFamily.PlayStation),
            ("DualSenseGamepadHID", PadFamily.PlayStation),
        };

        InputDevice AddPad(string layout)
        {
            InputDevice d;
            try
            {
                d = InputSystem.AddDevice(layout);
            }
            catch (Exception)
            {
                return null;
            }
            _extra.Add(d);
            return d;
        }

        [UnityTest]
        public IEnumerator EveryControllerFamilyBindsEveryControllerAction()
        {
            int tried = 0;
            foreach (var (layout, family) in Families)
            {
                InputDevice d = AddPad(layout);
                if (d == null) continue;   // a layout this editor platform does not register
                tried++;
                yield return null;
                Assert.IsInstanceOf<Gamepad>(d, layout);
                Assert.AreEqual(family, ControllerSettings.FamilyOf((Gamepad)d), layout);
                InputActionMap map = InputSystem.actions.FindActionMap("Player", true);
                foreach (InputAction a in map.actions)
                    foreach (InputBinding b in a.bindings)
                    {
                        if (b.isComposite || b.groups == null || !b.groups.Contains("Gamepad")) continue;
                        Assert.IsNotNull(InputControlPath.TryFindControl(d, b.effectivePath), $"{layout}: {a.name} {b.effectivePath}");
                    }
                CollectionAssert.IsEmpty(ControlSchemes.Missing(map, ControlScheme.Gamepad), layout);
                InputSystem.RemoveDevice(d);
                yield return null;
            }
            if (tried == 0) Assert.Ignore("no controller layouts registered on this platform");
        }

        [UnityTest]
        public IEnumerator UnplugMidMatchPausesWithTheNoticeAndReconnectResumesWithControl()
        {
            foreach (var (layout, _) in Families)
            {
                InputDevice d = AddPad(layout);
                if (d == null) continue;
                TitleScreen title = UnityEngine.Object.FindAnyObjectByType<TitleScreen>();
                if (title != null) title.Hide();
                yield return null;
                yield return null;
                PauseMenu menu = UnityEngine.Object.FindAnyObjectByType<PauseMenu>();
                Assert.IsNotNull(menu);
                if (menu.IsOpen) menu.Resume();
                _driver.HumanAutopilot = false;
                yield return null;
                InputSystem.RemoveDevice(d);
                yield return null;
                yield return null;
                Assert.IsTrue(menu.IsOpen, $"{layout}: unplugging pauses");
                Assert.AreEqual(SystemEvents.ControllerNotice, SystemEvents.Notice, layout);
                InputDevice again = AddPad(layout);
                yield return null;
                yield return null;
                Assert.IsNull(SystemEvents.Notice, $"{layout}: reconnecting clears the notice");
                Assert.IsFalse(menu.IsOpen, $"{layout}: reconnecting resumes");
                Assert.IsFalse(_driver.HumanAutopilot, $"{layout}: the player keeps control");
                Assert.IsTrue(InputSystem.actions.FindActionMap("Player").enabled, $"{layout}: input is live");
                InputSystem.RemoveDevice(again);
                yield return null;
                yield return null;
                if (menu.IsOpen) menu.Resume();
                yield break;   // one family is enough for the live-match path; the families' bindings are above
            }
            Assert.Ignore("no controller layouts registered on this platform");
        }
    }
}

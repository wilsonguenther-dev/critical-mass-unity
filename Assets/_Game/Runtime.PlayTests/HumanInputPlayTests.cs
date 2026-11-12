using System;
using System.Collections;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// U-041 (new, no vitest twin): drives HumanInputSource through the real project-wide actions with a virtual
    /// keyboard (no Input System test framework, so Packages/manifest.json is untouched).
    /// </summary>
    public sealed class HumanInputPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";
        const int HoldTicks = 30;

        SimDriverBehaviour _driver;
        Keyboard _kb;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver, "Boot did not build a SimDriverBehaviour");
            _driver.HumanAutopilot = false;

            // The batchmode test runner has no focused Game view, so under the default "pointers and keyboards respect
            // Game view focus" the Input System routes keyboard events to editor updates and the player-side actions
            // never see them. Route all device input to the game regardless of focus for these tests only (the
            // settings object is modified in place, never replaced: replacing destroys the temporary default instance),
            // and restore both values in TearDown.
            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            _kb = InputSystem.AddDevice<Keyboard>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_kb != null)
            {
                InputSystem.RemoveDevice(_kb);
                _kb = null;
            }
            if (_settingsSaved)
            {
                InputSettings s = InputSystem.settings;
                s.backgroundBehavior = _savedBackground;
                s.editorInputBehaviorInPlayMode = _savedEditorBehaviour;
                _settingsSaved = false;
            }
            _driver = null;
        }

        [UnityTest]
        public IEnumerator HoldingWOnAVirtualKeyboardMovesActor0UpTheScreen()
        {
            double x0 = _driver.World.X[0];
            double z0 = _driver.World.Z[0];

            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.W));
            InputSystem.Update();
            for (int i = 0; i < HoldTicks; i++)
            {
                yield return null;
                _driver.StepTicks(1);
            }

            Assert.IsTrue(_driver.World.Actors[0].IsHuman);
            double dx = _driver.World.X[0] - x0;
            double dz = _driver.World.Z[0] - z0;
            // Screen up is sim (-1, -1) / sqrt 2 (SimSpaceTests).
            Assert.Greater((-dx - dz) / Math.Sqrt(2), 0.5);
            // No sideways drift.
            Assert.Less(Math.Abs(dx - dz) / Math.Sqrt(2), 0.1);
        }

        [UnityTest]
        public IEnumerator ReleasingWStopsActor0WithinTheBrakeTime()
        {
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.W));
            InputSystem.Update();
            for (int i = 0; i < HoldTicks; i++)
            {
                yield return null;
                _driver.StepTicks(1);
            }

            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            InputSystem.Update();

            GameConfig c = _driver.World.Config;
            int brakeTicks = (int)Math.Ceiling(c.Movement.BrakeSeconds * c.SimHz) + 1;
            for (int i = 0; i < brakeTicks; i++)
            {
                yield return null;
                _driver.StepTicks(1);
            }

            double vx = _driver.World.Vx[0];
            double vz = _driver.World.Vz[0];
            // Bots may still nudge actor 0 through separation, so the bound is a small speed, not zero.
            Assert.Less(Math.Sqrt(vx * vx + vz * vz), 0.05 * c.Classes[(int)ClassId.Warrior].MoveSpeed);
        }
    }
}

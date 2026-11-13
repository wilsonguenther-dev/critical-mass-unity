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
    /// DG-02 (no vitest twin): Space on a virtual keyboard, through the real project-wide actions and HumanInputSource,
    /// makes actor 0 airborne within 1 sim tick. Same harness as HumanInputPlayTests (TimeScale 0, focus-independent input).
    /// </summary>
    public sealed class JumpPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";
        const double SettleSeconds = 3;

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
            _driver.TimeScale = 0;
            GameConfig c = _driver.World.Config;
            _driver.StepTicks((int)Math.Ceiling(SettleSeconds * c.SimHz));

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
        public IEnumerator PressingSpaceMakesActor0AirborneWithin1Tick()
        {
            Assert.AreEqual(0, _driver.World.Actors[0].AirLen, "grounded before the press");
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.Space));
            InputSystem.Update();
            yield return null;
            _driver.StepTicks(1);
            Assert.Greater(_driver.World.Actors[0].AirLen, 0, "Space made actor 0 airborne on the first tick after the press");
            Assert.AreEqual(1, _driver.World.Actors[0].AirTick);
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            InputSystem.Update();
            yield return null;
        }
    }
}

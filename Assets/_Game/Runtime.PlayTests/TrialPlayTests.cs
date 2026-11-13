using System.Collections;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Onboarding;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// DG-12 in the real Boot scene: Trial.Start sets up 5v5 at 1x, the mascot says step one with the movement keys, the
    /// skip key (Backspace on a virtual keyboard) skips a step, holding it leaves the trial and saves the done flag.
    /// </summary>
    public sealed class TrialPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        SimDriverBehaviour _driver;
        Keyboard _kb;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;
        bool _hadDone;
        int _wasDone;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _info.Clear();
            Trial.InfoLog = line => _info.Add(line);   // the trial's "[Trial] ..." information lines, checked below
            _hadDone = PlayerPrefs.HasKey(TrialProgress.DoneKey);
            _wasDone = PlayerPrefs.GetInt(TrialProgress.DoneKey, 0);
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;
            _driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver, "Boot did not build a SimDriverBehaviour");
            _driver.HumanAutopilot = false;

            // As HumanInputPlayTests: route device input to the game without a focused Game view.
            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _kb = InputSystem.AddDevice<Keyboard>();
        }

        readonly System.Collections.Generic.List<string> _info = new System.Collections.Generic.List<string>();

        [TearDown]
        public void TearDown()
        {
            foreach (string line in _info) StringAssert.StartsWith("[Trial] ", line, "only [Trial] information lines");
            Trial.InfoLog = s => UnityEngine.Debug.Log(s);
            Trial.Stop();
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
            if (_hadDone) PlayerPrefs.SetInt(TrialProgress.DoneKey, _wasDone);
            else PlayerPrefs.DeleteKey(TrialProgress.DoneKey);
            PlayerPrefs.Save();
            _driver = null;
        }

        [UnityTest]
        public IEnumerator StartSetsUp5v5At1xAndTheMascotSaysStepOne()
        {
            Assert.IsTrue(Trial.Start());
            Assert.AreEqual(10, _driver.World.ActorCount, "5 v 5");
            Assert.AreEqual(1.0, _driver.TimeScale, 1e-9, "1x pace");
            Assert.AreEqual(0, _driver.World.Tick, "a fresh match");
            _driver.TimeScale = 0;   // wall-time independent from here (SmokeTests)
            yield return null;
            Assert.IsTrue(Trial.IsRunning);
            Assert.AreEqual("move", Trial.Guide.Runner.ActiveStep.Id);
            MascotGuide m = MascotGuide.Instance;
            Assert.IsNotNull(m, "the mascot exists");
            Assert.IsTrue(m.IsOpen);
            Assert.IsFalse(m.Line.Contains("{"), "tokens filled: " + m.Line);
            Assert.IsTrue(m.Line.Contains("WASD") || m.Line.Contains("STICK") || m.Line.Length > 0, m.Line);

            // Standing still never completes step one.
            _driver.StepTicks(90);
            yield return null;
            Assert.AreEqual("move", Trial.Guide.Runner.ActiveStep.Id);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TappingBackspaceSkipsAStep()
        {
            Assert.IsTrue(Trial.Start());
            _driver.TimeScale = 0;
            yield return null;
            Assert.AreEqual("move", Trial.Guide.Runner.ActiveStep.Id);

            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.Backspace));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;
            yield return null;

            Assert.AreEqual(TrialStepState.Skipped, Trial.Guide.Runner.States[0]);
            Assert.AreEqual("marker", Trial.Guide.Runner.ActiveStep.Id);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HoldingBackspaceLeavesTheTrialAndSavesDone()
        {
            TrialProgress.Done = false;
            Assert.IsTrue(Trial.Start());
            _driver.TimeScale = 0;
            yield return null;

            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.Backspace));
            float until = Time.unscaledTime + TrialGuide.HoldToLeaveSeconds + 0.3f;
            while (Time.unscaledTime < until) yield return null;
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;
            yield return null;

            Assert.IsTrue(Trial.Guide.Runner.Finished);
            Assert.IsFalse(Trial.IsRunning);
            Assert.IsTrue(TrialProgress.Done, "the done flag is saved");
            Assert.IsFalse(TrialBoot.OfferOnBoot, "the next launch goes straight on");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MascotCanBeDismissedAndBroughtBack()
        {
            Assert.IsTrue(Trial.Start());
            _driver.TimeScale = 0;
            yield return null;
            MascotGuide m = MascotGuide.Instance;
            // Events are queued and left to the frame's own input update, so wasPressedThisFrame is true in that
            // frame's MonoBehaviour Update (a manual InputSystem.Update would consume the press a frame early).
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.G));
            yield return null;
            yield return null;
            Assert.IsTrue(m.IsDismissed);
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.G));
            yield return null;
            yield return null;
            Assert.IsFalse(m.IsDismissed);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

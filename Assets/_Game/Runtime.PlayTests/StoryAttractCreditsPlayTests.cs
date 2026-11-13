using System.Collections;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menus;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// DG-19, DG-28 and DG-29 in the real Boot scene with a virtual keyboard (queued state events, as ControlsPlayTests):
    /// the installer attaches all three; Space skips the story on its first frame; the attract tour starts after the idle
    /// time on the title and one key press ends it on the next frame; the credits open and Escape closes them.
    /// </summary>
    public sealed class StoryAttractCreditsPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        Keyboard _kb;
        int _savedSeen;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _savedSeen = PlayerPrefs.GetInt(StoryIntro.SeenKey, 0);
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;
            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _kb = InputSystem.AddDevice<Keyboard>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            AttractMode.End();
            StoryIntro.Skip();
            Credits.Hide();
            AttractMode.TitleOpen = null;
            AttractMode.AllowInBatchMode = false;
            if (_kb != null) InputSystem.RemoveDevice(_kb);
            _kb = null;
            if (_settingsSaved)
            {
                InputSettings s = InputSystem.settings;
                s.backgroundBehavior = _savedBackground;
                s.editorInputBehaviorInPlayMode = _savedEditorBehaviour;
                _settingsSaved = false;
            }
            PlayerPrefs.SetInt(StoryIntro.SeenKey, _savedSeen);
            PlayerPrefs.Save();
        }

        IEnumerator Send(KeyboardState state)
        {
            InputSystem.QueueStateEvent(_kb, state);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InstallerAttachesStoryAttractAndCredits()
        {
            SimDriverBehaviour driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(driver);
            Assert.IsNotNull(driver.GetComponent<StoryIntro>());
            Assert.IsNotNull(driver.GetComponent<AttractMode>());
            Assert.IsNotNull(driver.GetComponent<Credits>());
            Assert.IsFalse(StoryIntro.IsPlaying, "the story never auto-plays in the editor or batch mode");
            yield return null;
        }

        IEnumerator WaitFilmTime(double seconds)
        {
            float until = Time.realtimeSinceStartup + 30f;
            while (StoryIntro.IsPlaying && StoryIntro.Instance.FilmTime < seconds && Time.realtimeSinceStartup < until) yield return null;
        }

        [UnityTest]
        public IEnumerator SkipNeedsADeliberatePressAfterTheLock()
        {
            bool skipped = false;
            void OnFinished(bool s) => skipped = s;
            StoryIntro.Finished += OnFinished;
            try
            {
                Assert.IsTrue(StoryIntro.Play());
                Assert.IsTrue(StoryIntro.IsPlaying);
                Assert.AreEqual(0.0, StoryIntro.Instance.Timeline.Time);
                yield return Send(new KeyboardState(Key.Space));
                Assert.IsTrue(StoryIntro.IsPlaying, "Space on the first frame does not skip (story intro 2)");
                yield return Send(new KeyboardState());
                yield return WaitFilmTime(1.0);
                Assert.IsTrue(StoryIntro.IsPlaying);
                yield return Send(new KeyboardState(Key.Space));   // pressed inside the lock and held through it
                yield return WaitFilmTime(SkipGate.LockSeconds + 0.3);
                Assert.IsTrue(StoryIntro.IsPlaying, "a key held through the lock does not skip");
                Assert.Greater(StoryIntro.Instance.FilmTime, SkipGate.LockSeconds, "the film ran past the lock");
                yield return Send(new KeyboardState());
                yield return Send(new KeyboardState(Key.Space));
                Assert.IsFalse(StoryIntro.IsPlaying, "a fresh press after the lock skips");
                Assert.IsTrue(skipped);
                Assert.AreEqual(1, PlayerPrefs.GetInt(StoryIntro.SeenKey, 0));
                yield return Send(new KeyboardState());
            }
            finally
            {
                StoryIntro.Finished -= OnFinished;
            }
        }

        [UnityTest]
        public IEnumerator IdleOnTheTitleStartsTheTourAndAnyKeyEndsItWithinOneFrame()
        {
            AttractMode.AllowInBatchMode = true;
            AttractMode.TitleOpen = () => true;
            AttractMode a = AttractMode.Instance;
            Assert.IsNotNull(a);
            // The 20 s count itself is the EditMode test; here the clock starts just short of it.
            a.Clock.Idle = AttractMode.IdleSeconds - 0.05;
            for (int i = 0; i < 300 && !AttractMode.IsTouring; i++) yield return null;
            Assert.IsTrue(AttractMode.IsTouring, "the tour starts after the idle time");
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(AttractMode.IsTouring, "the tour keeps running with no input");
            yield return Send(new KeyboardState(Key.Space));
            Assert.IsFalse(AttractMode.IsTouring, "one key press ends the tour on the frame it arrives");
            yield return Send(new KeyboardState());
            Assert.Less(a.Clock.Idle, 1.0, "the idle clock restarts");
        }

        [UnityTest]
        public IEnumerator CreditsOpenAndEscapeClosesThem()
        {
            Assert.IsTrue(Credits.Show());
            Assert.IsTrue(Credits.IsOpen);
            yield return null;
            yield return Send(new KeyboardState(Key.Escape));
            Assert.IsFalse(Credits.IsOpen, "Escape closes the credits");
            yield return Send(new KeyboardState());
        }
    }
}

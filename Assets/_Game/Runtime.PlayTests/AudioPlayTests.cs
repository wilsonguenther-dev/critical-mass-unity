using System.Collections;
using System.Collections.Generic;
using CriticalMass.Runtime.Audio;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// DG-18 and DG-31 in the real Boot scene: the persistent audio object installs itself with one AudioListener, the
    /// music director picks the match music, an announcer line shows its subtitle, and every control on every page of
    /// the pause menu and settings has its UI sound (the DG-31 walk; later screens from the game-flow item are bound by
    /// the same binder and get the same check by calling UiSound.CountUnbound on their root).
    /// </summary>
    public sealed class AudioPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        SimDriverBehaviour _driver;
        PauseMenu _menu;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SimDriverBehaviour.CaptureMode = true;
            SceneManager.LoadScene(BootScene);
            yield return null;
            SimDriverBehaviour.CaptureMode = false;
            yield return null;
            _driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver, "Boot did not build a SimDriverBehaviour");
            _menu = _driver.GetComponent<PauseMenu>();
            Assert.IsNotNull(_menu, "MenuInstaller did not attach the pause menu");
            // The audio object looks for the match every 15 frames.
            for (int i = 0; i < 20; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_menu != null && _menu.IsOpen) _menu.Resume();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AudioObjectInstallsOnceWithOneListener()
        {
            Assert.IsNotNull(AudioHost.Root, "AudioHost did not install");
            Assert.IsNotNull(AudioHost.Root.GetComponent<MusicDirector>());
            Assert.IsNotNull(AudioHost.Root.GetComponent<Announcer>());
            Assert.IsNotNull(AudioHost.Root.GetComponent<UiSoundBinder>());
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, "exactly one AudioListener");
            Assert.AreEqual(1, Object.FindObjectsByType<MusicDirector>(FindObjectsSortMode.None).Length, "a scene load must not add a second director");
            foreach (AudioSource s in AudioHost.Root.GetComponents<AudioSource>())
                Assert.IsNotNull(s.outputAudioMixerGroup, "every source routes through the settings mixer");
            yield return null;
        }

        [UnityTest]
        public IEnumerator MatchMusicPlays()
        {
            // Editor: no title screen, so the match music is wanted at once.
            Assert.AreEqual(MusicState.Match, MusicDirector.CurrentState);
            MusicDirector.SetMenuMusic(true);
            yield return null;
            Assert.AreEqual(MusicState.Title, MusicDirector.CurrentState);
            MusicDirector.SetMenuMusic(false);
            yield return null;
            Assert.AreEqual(MusicState.Match, MusicDirector.CurrentState);
        }

        [UnityTest]
        public IEnumerator AnnouncerLineShowsItsSubtitle()
        {
            bool saved = Announcer.SubtitlesEnabled;
            Announcer.SubtitlesEnabled = true;
            try
            {
                AnnouncerLine line = AnnouncerLines.For(AnnouncerTrigger.HillTaken);
                Announcer.ForceLine(AnnouncerTrigger.HillTaken);
                yield return null;
                yield return null;
                Assert.IsTrue(Announcer.IsSpeaking);
                Assert.AreEqual(line.Text, Announcer.CurrentText);
                Assert.IsNotNull(line.Clip, "voice clip loaded for " + line.Id);
            }
            finally
            {
                Announcer.SubtitlesEnabled = saved;
            }
        }

        [UnityTest]
        public IEnumerator EveryPauseAndSettingsControlHasItsUiSound()
        {
            _menu.Pause();
            yield return null;
            yield return null;
            Assert.IsTrue(_menu.IsOpen);
            var missing = new List<string>();
            foreach (PauseMenu.Tab tab in new[] { PauseMenu.Tab.Controls, PauseMenu.Tab.Audio, PauseMenu.Tab.Video, PauseMenu.Tab.Game })
            {
                _menu.ShowSettings(tab);
                yield return null;
                yield return null;
                missing.Clear();
                Assert.AreEqual(0, UiSound.CountUnbound(_menu.Canvas.transform, missing), tab + ": " + string.Join(", ", missing));
            }
            _menu.ShowRoot();
            yield return null;
            Assert.Greater(UiSound.CountSelectables(_menu.Canvas.transform), 10, "the walk saw the menu's controls");
            Assert.AreEqual(0, UiSound.CountUnbound(_menu.Canvas.transform, missing), string.Join(", ", missing));
        }
    }
}

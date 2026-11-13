using System;
using System.Collections;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// DG-10 class select in setup and on the respawn screen, DG-13 loading screen timing and progress, DG-14 controls
    /// card from the pause menu, from Settings, Controls and once at the first spawn. The real Boot scene, capture mode
    /// (world advanced only by StepTicks), as ControlsPlayTests.
    /// </summary>
    public sealed class ClassLoadingControlsPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        SimDriverBehaviour _driver;
        HumanInputSource _input;
        int _savedClass;
        bool _hadClass, _hadSeen;
        int _savedSeen;
        bool _savedAutoCard, _savedAutoRespawn;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _hadClass = PlayerPrefs.HasKey(ClassSelect.PrefsKey);
            _savedClass = PlayerPrefs.GetInt(ClassSelect.PrefsKey, 0);
            _hadSeen = PlayerPrefs.HasKey(ControlsCardScreen.SeenKey);
            _savedSeen = PlayerPrefs.GetInt(ControlsCardScreen.SeenKey, 0);
            _savedAutoCard = ControlsCardScreen.AutoFirstTime;
            _savedAutoRespawn = ClassSelect.AutoRespawn;
            ControlsCardScreen.AutoFirstTime = false;
            SimDriverBehaviour.CaptureMode = true;
            SceneManager.LoadScene(BootScene);
            yield return null;
            SimDriverBehaviour.CaptureMode = false;
            yield return null;
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver);
            _input = _driver.GetComponent<HumanInputSource>();
            Assert.IsNotNull(_input);
        }

        [TearDown]
        public void TearDown()
        {
            ClassSelect.Hide();
            ControlsCardScreen.Hide();
            if (_hadClass) PlayerPrefs.SetInt(ClassSelect.PrefsKey, _savedClass);
            else PlayerPrefs.DeleteKey(ClassSelect.PrefsKey);
            if (_hadSeen) PlayerPrefs.SetInt(ControlsCardScreen.SeenKey, _savedSeen);
            else PlayerPrefs.DeleteKey(ControlsCardScreen.SeenKey);
            PlayerPrefs.Save();
            ControlsCardScreen.AutoFirstTime = _savedAutoCard;
            ClassSelect.AutoRespawn = _savedAutoRespawn;
            SimDriverBehaviour.CaptureMode = false;
            _driver = null;
            _input = null;
        }

        // ---------------------------------------------------------------- DG-10

        [UnityTest]
        public IEnumerator PickingFarmerInSetupMakesActorZeroAFarmer()
        {
            ClassId got = ClassId.Warrior;
            bool called = false;
            ClassSelect s = ClassSelect.ShowSetup(TeamId.Crimson, c =>
            {
                got = c;
                called = true;
            });
            yield return null;
            Assert.AreEqual(ClassSelect.SelectMode.Setup, s.Mode);
            Assert.AreEqual(ClassIds.All.Count, s.SetupTiles.Count, "a card for every class");
            s.FocusClass(ClassId.Farmer);
            yield return null;
            Assert.AreEqual(ClassId.Farmer, s.Focused, "focus moves the detail card");
            Assert.AreEqual(ClassId.Farmer, s.Detail.Class);
            s.ConfirmSetup(ClassId.Farmer);
            yield return null;
            Assert.IsTrue(called);
            Assert.AreEqual(ClassId.Farmer, got);
            Assert.AreEqual(ClassId.Farmer, ClassSelect.SelectedClass);
            Assert.IsFalse(ClassSelect.IsOpen);

            // The game flow builds the match from the choice (WorldOptions.HumanClass).
            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = cfg.Seed,
                TeamSize = cfg.TeamSize,
                HumanTeam = TeamId.Crimson,
                HumanClass = ClassSelect.SelectedClass,
                Config = cfg,
            });
            Assert.AreEqual(ClassId.Farmer, w.Actors[0].Cls);
            Assert.AreEqual(TeamId.Crimson, w.Actors[0].Team);
        }

        [UnityTest]
        public IEnumerator PickingMinerOnTheRespawnScreenRespawnsAsAMiner()
        {
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            _driver.StepTicks(30);
            World w = _driver.World;
            Actor a = w.Actors[0];
            ClassId before = a.Cls;
            Assert.AreNotEqual(ClassId.Miner, before);
            // Knock actor 0 out (test setup: the respawn path is the sim's own from here on).
            a.Alive = false;
            a.Hp = 0;
            a.RespawnIn = 1.0;
            yield return null;
            yield return null;
            ClassSelect s = ClassSelect.Instance;
            Assert.IsNotNull(s, "ClassSelectInstaller attached the screen");
            Assert.AreEqual(ClassSelect.SelectMode.Respawn, s.Mode, "the respawn screen opens while knocked out");
            Assert.AreEqual(ClassIds.All.Count, s.RespawnTiles.Count);
            s.PickRespawn(ClassId.Miner);
            _driver.StepTicks(1);
            Assert.AreEqual(ClassId.Miner, a.PendingClass, "the sim holds the pick");
            int guard = 0;
            while (!a.Alive && guard++ < 200) _driver.StepTicks(1);
            Assert.IsTrue(a.Alive, "respawned");
            Assert.AreEqual(ClassId.Miner, a.Cls, "respawned as a Miner");
            yield return null;
            Assert.AreEqual(ClassSelect.SelectMode.None, s.Mode, "the respawn screen closes on respawn");
        }

        // ---------------------------------------------------------------- DG-13

        [UnityTest]
        public IEnumerator TheLoadingScreenNeverGoesBackwardAndShowsAtLeastMinSeconds()
        {
            float hiddenAt = -1f;
            LoadingScreen ls = LoadingScreen.Show(1234);
            float openedAt = Time.unscaledTime;
            Assert.IsTrue(LoadingScreen.IsOpen);
            Assert.IsFalse(string.IsNullOrEmpty(ls.Tip), "a tip");
            CollectionAssert.Contains((ICollection)new List<string>(LoreWords.Tips()), ls.Tip, "tips come from the words");
            Assert.IsFalse(string.IsNullOrEmpty(ls.LoreLine), "a lore line");

            LoadingScreen.Report(LoadStep.World, 0.6f);
            float t1 = ls.Target;
            LoadingScreen.Report(LoadStep.World, 0.2f);
            Assert.AreEqual(t1, ls.Target, "a lower report never moves it back");
            LoadingScreen.Complete(LoadStep.Dressing);
            Assert.AreEqual(0.65f, ls.Target, 1e-4f, "world and dressing done");
            LoadingScreen.Report(LoadStep.World, 0.1f);
            Assert.AreEqual(0.65f, ls.Target, 1e-4f);

            LoadingScreen.Finish(() => hiddenAt = Time.unscaledTime);
            float last = 0f;
            float limit = Time.unscaledTime + 5f;
            while (hiddenAt < 0f && Time.unscaledTime < limit)
            {
                Assert.GreaterOrEqual(ls.Shown, last, "the bar never goes backward");
                last = ls.Shown;
                yield return null;
            }
            Assert.Greater(hiddenAt, 0f, "hidden after Finish");
            Assert.GreaterOrEqual(hiddenAt - openedAt, LoadingScreen.MinSeconds, "on screen at least 1.2 s");
            Assert.IsFalse(LoadingScreen.IsOpen);
        }

        // ---------------------------------------------------------------- DG-14

        [UnityTest]
        public IEnumerator TheControlsCardOpensFromThePauseMenuAndFromSettings()
        {
            PauseMenu menu = _driver.GetComponent<PauseMenu>();
            Assert.IsNotNull(menu);
            menu.Pause();
            yield return null;
            Assert.IsTrue(Has(menu, "CONTROLS"), "a CONTROLS entry on the pause menu");
            menu.ShowControls();
            yield return null;
            Assert.IsTrue(ControlsCardScreen.IsOpen);
            Assert.AreEqual(ControlsCardScreen.CardMode.Modal, ControlsCardScreen.Instance.Mode);
            Assert.AreEqual(ControlBindings.ActiveDevice, ControlsCardScreen.Instance.Card.Device, "the device used last");
            // Escape / Start while paused: the card closes first, the menu stays.
            Assert.IsTrue(_input.PauseHandler());
            Assert.IsFalse(ControlsCardScreen.IsOpen);
            Assert.IsTrue(menu.IsOpen);

            menu.ShowSettings(PauseMenu.Tab.Controls);
            yield return null;
            Assert.IsTrue(Has(menu, "View Controls"), "VIEW CONTROLS at the top of Settings, Controls");
            menu.ShowControls();
            yield return null;
            Assert.IsTrue(ControlsCardScreen.IsOpen);
            ControlBindings.SetActiveDevice(ControlDevice.Gamepad);
            yield return null;
            yield return null;
            Assert.AreEqual(ControlDevice.Gamepad, ControlsCardScreen.Instance.Card.Device, "glyphs follow the device");
            Assert.AreEqual("RT", ControlsCardScreen.Instance.Card.Shown("Attack", 0));
            ControlBindings.SetActiveDevice(ControlDevice.KeyboardMouse);
            ControlsCardScreen.Hide();
            menu.Resume();
        }

        [UnityTest]
        public IEnumerator TheFirstTimeCardShowsOnceAndClosesByItself()
        {
            ControlsCardScreen.ResetSeen();
            Assert.IsTrue(ControlsCardScreen.ShowFirstTime());
            Assert.AreEqual(ControlsCardScreen.CardMode.FirstTime, ControlsCardScreen.Instance.Mode);
            Assert.IsTrue(ControlsCardScreen.Seen);
            Assert.IsFalse(ControlsCardScreen.ShowFirstTime(), "never again unless asked");
            yield return new WaitForSecondsRealtime(ControlsCardScreen.FirstTimeSeconds + 0.4f);
            Assert.IsFalse(ControlsCardScreen.IsOpen, "gone after about 6 s");
        }

        static bool Has(PauseMenu menu, string name)
        {
            foreach (Transform t in menu.Canvas.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return true;
            return false;
        }
    }
}

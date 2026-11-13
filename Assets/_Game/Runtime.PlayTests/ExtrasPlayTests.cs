using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Secrets;
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
    /// DG-23, DG-25, DG-26 and DG-30 in the real Boot scene (capture mode: actor 0 on autopilot, the wall clock stopped,
    /// so only the ticks a test steps advance the match):
    /// every ACCESS option toggles from the settings page and persists across a scene reload; H toggles the clean HUD;
    /// the UI scale scales the HUD clusters; the pause-menu Codex shows n of 8; walking over a relic opens its card;
    /// the hill-alone secret fires; a match run at 8x records the three highlights with the right ticks, captures a
    /// still of each (where the device can), and Share writes a framed PNG under persistentDataPath.
    /// </summary>
    public sealed class ExtrasPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";
        const int TicksPerFrameAt8x = 8;   // 8x at SimHz 30 on a 30 Hz frame budget

        SimDriverBehaviour _driver;
        PauseMenu _menu;
        Keyboard _kb;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;
        readonly Dictionary<string, int?> _prefs = new Dictionary<string, int?>();
        readonly List<string> _files = new List<string>();

        static IEnumerable<string> Keys()
        {
            foreach (string k in SettingsExtras.AllKeys) yield return k;
            yield return GameSettings.ShakeKey;
            yield return CodexProgress.Key;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _prefs.Clear();
            foreach (string k in Keys()) _prefs[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
            SettingsExtras.ResetAll();
            CodexProgress.ResetAll();
            yield return Boot();
            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _kb = InputSystem.AddDevice<Keyboard>();
            yield return null;
        }

        IEnumerator Boot()
        {
            SimDriverBehaviour.CaptureMode = true;
            SceneManager.LoadScene(BootScene);
            yield return null;
            SimDriverBehaviour.CaptureMode = false;
            yield return null;
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver, "Boot did not build a SimDriverBehaviour");
            _menu = _driver.GetComponent<PauseMenu>();
            Assert.IsNotNull(_menu, "MenuInstaller did not attach the pause menu");
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
            foreach (KeyValuePair<string, int?> p in _prefs)
            {
                if (p.Value.HasValue) PlayerPrefs.SetInt(p.Key, p.Value.Value);
                else PlayerPrefs.DeleteKey(p.Key);
            }
            PlayerPrefs.Save();
            foreach (string f in _files) if (File.Exists(f)) File.Delete(f);
            _files.Clear();
            JumpGate.Override = null;
            SimDriverBehaviour.CaptureMode = false;
            if (_menu != null && _menu.IsOpen) _menu.Resume();
        }

        static MenuItem Item(string name)
        {
            GameObject go = GameObject.Find(name);
            Assert.IsNotNull(go, "no settings line named " + name);
            MenuItem it = go.GetComponent<MenuItem>();
            Assert.IsNotNull(it, name + " is not a MenuItem");
            return it;
        }

        [UnityTest]
        public IEnumerator EveryAccessOptionTogglesFromTheSettingsPageAndPersists()
        {
            _menu.Pause();
            yield return null;
            Assert.AreEqual(5, _menu.TabCount, "the ACCESS tab follows the four built-in tabs");
            _menu.ShowSettings((PauseMenu.Tab)4);
            yield return null;
            Item("HIDE NAMES").Stepped(1);
            Item("CLEAN HUD   (H)").Stepped(1);
            Item("SUBTITLES").Stepped(1);
            Item("UI SCALE").Stepped(1);
            Item("REDUCE FLASHING").Stepped(1);
            MenuItem shake = Item("CAMERA SHAKE");
            shake.Stepped(1);
            shake.Stepped(1);
            Assert.AreEqual("ON", Item("HIDE NAMES").Value.text);
            Assert.AreEqual(1, PlayerPrefs.GetInt(SettingsExtras.HideNamesKey));
            Assert.AreEqual(1, PlayerPrefs.GetInt(SettingsExtras.CleanHudKey));
            Assert.AreEqual(0, PlayerPrefs.GetInt(SettingsExtras.SubtitlesKey));
            Assert.AreEqual(110, PlayerPrefs.GetInt(SettingsExtras.UiScaleKey));
            Assert.AreEqual(1, PlayerPrefs.GetInt(SettingsExtras.ReduceFlashingKey));
            Assert.AreEqual(10, PlayerPrefs.GetInt(SettingsExtras.ShakeAmountKey), "two steps of 5");
            _menu.Resume();
            yield return null;

            // A reload builds a fresh menu: every option reads back as set.
            yield return Boot();
            Assert.IsTrue(SettingsExtras.HideNames);
            Assert.IsTrue(SettingsExtras.CleanHud);
            Assert.IsFalse(SettingsExtras.Subtitles);
            Assert.AreEqual(110, SettingsExtras.UiScalePercent);
            Assert.IsTrue(SettingsExtras.ReduceFlashing);
            Assert.AreEqual(10, SettingsExtras.ShakeAmount);
            _menu.Pause();
            yield return null;
            _menu.ShowSettings((PauseMenu.Tab)4);
            yield return null;
            Assert.AreEqual("110%", Item("UI SCALE").Value.text);
            Item("REDUCE FLASHING").Stepped(1);
            Assert.IsFalse(SettingsExtras.ReduceFlashing, "and toggles back");
            Item("Reset Access").Submitted();
            Assert.IsFalse(SettingsExtras.HideNames);
            Assert.AreEqual(0, SettingsExtras.ShakeAmount);
        }

        [UnityTest]
        public IEnumerator HTogglesTheCleanHudAndUiScaleScalesTheHud()
        {
            GameHud hud = _driver.GetComponent<GameHud>();
            Assert.IsNotNull(hud);
            yield return null;
            Assert.IsTrue(hud.Canvas.enabled);
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.H));
            yield return null;
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;
            Assert.IsTrue(SettingsExtras.CleanHud);
            Assert.IsFalse(hud.Canvas.enabled, "H hides the HUD");
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.H));
            yield return null;
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return null;
            Assert.IsFalse(SettingsExtras.CleanHud);
            Assert.IsTrue(hud.Canvas.enabled, "H shows it again");

            SettingsExtras.UiScalePercent = 120;
            yield return null;
            yield return null;
            foreach (RectTransform c in hud.Clusters) Assert.AreEqual(1.2f, c.localScale.x, 1e-4f, c.name);
            SettingsExtras.UiScalePercent = 100;
            yield return null;
            yield return null;
            foreach (RectTransform c in hud.Clusters) Assert.AreEqual(1f, c.localScale.x, 1e-4f, c.name);
        }

        [UnityTest]
        public IEnumerator TheCodexShowsNOfEightAndWalkingOverARelicOpensItsCard()
        {
            RelicField field = _driver.GetComponent<RelicField>();
            Assert.IsNotNull(field, "the relic field installs itself");
            yield return null;
            Assert.IsNotNull(field.Spots);
            Assert.AreEqual(8, field.Spots.Length);

            _menu.Pause();
            yield return null;
            int codex = _menu.ExtraPageIndex(CodexPage.Label);
            Assert.GreaterOrEqual(codex, 0, "CODEX is on the pause menu");
            _menu.ShowExtra(codex);
            Assert.AreEqual(PauseMenu.Page.Extra, _menu.CurrentPage);
            Assert.AreEqual("0 OF 8 FOUND", CodexPage.CountText);
            _menu.Back();
            Assert.AreEqual(PauseMenu.Page.Root, _menu.CurrentPage, "back to the pause page");
            _menu.Resume();
            yield return null;

            // Test-only: put actor 0 on relic 0 (the view reads positions; nothing else in the sim changes).
            World w = _driver.World;
            RelicPlacement.Spot s = field.Spots[0];
            w.X[0] = w.PrevX[0] = s.X;
            w.Z[0] = w.PrevZ[0] = s.Z;
            yield return null;
            yield return null;
            Assert.IsTrue(CodexProgress.IsFound(0), "picked up");
            Assert.IsTrue(LoreCard.IsOpen, "the lore card opens");
            Assert.AreEqual(LoreText.GetRelic("relic-01").Name.ToUpperInvariant(), LoreCard.Title);
            Assert.AreEqual(LoreText.GetRelic("relic-01").Line1, LoreCard.Line1);
            StringAssert.Contains("1 OF 8", LoreCard.Footer);

            _menu.Pause();
            yield return null;
            _menu.ShowExtra(codex);
            Assert.AreEqual("1 OF 8 FOUND", CodexPage.CountText);
            StringAssert.StartsWith(LoreText.GetRelic("relic-01").Name.ToUpperInvariant() + "|", CodexPage.TileText(0));
            StringAssert.StartsWith("UNDISCOVERED|", CodexPage.TileText(1));
        }

        [UnityTest]
        public IEnumerator HoldingTheHillAloneShowsTheSecretLine()
        {
            SecretWatcher watcher = _driver.GetComponent<SecretWatcher>();
            Assert.IsNotNull(watcher);
            string spoken = null;
            Action<string> onLine = id => spoken = id;
            SecretsBoard.AnnouncerLine += onLine;
            try
            {
                World w = _driver.World;
                for (int frame = 0; frame < 70 && spoken == null; frame++)
                {
                    // Test-only: hold every Azure ally off the hill and actor 0 on it, then advance the clock 1 s a frame.
                    for (int i = 1; i < w.ActorCount; i++)
                        if (w.Actors[i].Team == TeamId.Azure) { w.X[i] = w.PrevX[i] = 8; w.Z[i] = w.PrevZ[i] = 36; }
                    w.X[0] = w.PrevX[0] = w.Map.Hill.X;
                    w.Z[0] = w.PrevZ[0] = w.Map.Hill.Z;
                    w.Actors[0].Alive = true;
                    w.Match.Time += 1.0;
                    yield return null;
                }
                Assert.AreEqual(SecretWatcher.HillAloneLine, spoken);
                Assert.IsTrue(CaptionBar.IsShowing, "subtitles on: the caption shows");
                Assert.AreEqual(LoreText.Announcer("an-22"), CaptionBar.Current);
            }
            finally
            {
                SecretsBoard.AnnouncerLine -= onLine;
            }
        }

        [UnityTest]
        public IEnumerator AMatchAt8xRecordsThreeHighlightsWithTheirTicksAndSharesAFramedPng()
        {
            MomentRecorder rec = _driver.GetComponent<MomentRecorder>();
            Assert.IsNotNull(rec, "the recorder installs itself");
            World w = _driver.World;
            var seenAt = new int[3] { -1, -1, -1 };
            int cursor = w.EventHead;

            IEnumerator RunUntil(Func<bool> done, int maxTicks)
            {
                for (int t = 0; t < maxTicks && !done(); t += TicksPerFrameAt8x)
                {
                    for (int k = 0; k < TicksPerFrameAt8x; k++)
                    {
                        _driver.StepTicks(1);
                        for (; cursor < w.EventHead; cursor++)
                        {
                            SimEvent e = w.Events[cursor % World.EventCapacity];
                            if (e.Type == SimEventType.Death && seenAt[0] < 0) seenAt[0] = w.Tick;
                            if (e.Type == SimEventType.Hatch && seenAt[1] < 0) seenAt[1] = w.Tick;
                        }
                    }
                    yield return null;
                }
            }

            // 1) the first knock-out of a bot match
            yield return RunUntil(() => rec.Has(MomentKind.FirstKnockOut), 30 * 240);
            Assert.IsTrue(rec.Has(MomentKind.FirstKnockOut), "a knock-out within 4 sim minutes");
            yield return new WaitForSecondsRealtime(1.2f);
            // 2) the hatch, forced now
            w.ForceHatch = true;
            yield return RunUntil(() => rec.Has(MomentKind.Hatch), 30 * 10);
            Assert.IsTrue(rec.Has(MomentKind.Hatch));
            yield return new WaitForSecondsRealtime(1.2f);
            // 3) a multi knock-out: the match's own if it had one, else two scripted knock-outs by one actor (test-only events)
            if (!rec.Has(MomentKind.MultiKnockOut))
            {
                World.PushEvent(w, SimEventType.Death, 20, 5, w.X[20], w.Z[20], 1);
                World.PushEvent(w, SimEventType.Death, 21, 5, w.X[21], w.Z[21], 1);
                cursor = w.EventHead;
                seenAt[2] = w.Tick;
                yield return null;
            }
            Assert.IsTrue(rec.Has(MomentKind.MultiKnockOut));

            Assert.That(rec.Get(MomentKind.FirstKnockOut).Tick - seenAt[0], Is.InRange(0, TicksPerFrameAt8x), "first knock-out tick stamp");
            Assert.That(rec.Get(MomentKind.Hatch).Tick - seenAt[1], Is.InRange(0, TicksPerFrameAt8x), "hatch tick stamp");
            if (seenAt[2] >= 0) Assert.AreEqual(seenAt[2], rec.Get(MomentKind.MultiKnockOut).Tick, "multi knock-out tick stamp");

            if (!MomentRecorder.CanCapture) Assert.Pass("no graphics device or no asynchronous readback: moments checked, stills skipped"); // integration batch 2: Ignore turned the whole run Skipped and failed the gate
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until &&
                   (rec.Still(MomentKind.FirstKnockOut) == null || rec.Still(MomentKind.Hatch) == null || rec.Still(MomentKind.MultiKnockOut) == null))
                yield return null;
            for (int k = 0; k < 3; k++)
            {
                Texture2D still = rec.Still((MomentKind)k);
                Assert.IsNotNull(still, (MomentKind)k + " still");
                Assert.AreEqual(MomentRecorder.StillWidth, still.width);
                Assert.AreEqual(MomentRecorder.StillHeight, still.height);
            }
            Assert.LessOrEqual(rec.StillsInMemory, MomentRecorder.MaxStills);

            ResultsHighlights.Show();
            Assert.IsTrue(ResultsHighlights.IsOpen);
            Assert.AreSame(rec.Still(MomentKind.Hatch), ResultsHighlights.StillShown(MomentKind.Hatch));
            string saved = null;
            Action<string> onSaved = p => saved = p;
            MomentRecorder.PhotoSaved += onSaved;
            try
            {
                Assert.IsTrue(rec.SaveHighlightsCard());
                until = Time.realtimeSinceStartup + 10f;
                while (saved == null && Time.realtimeSinceStartup < until) yield return null;
            }
            finally
            {
                MomentRecorder.PhotoSaved -= onSaved;
                ResultsHighlights.Hide();
            }
            Assert.IsNotNull(saved, "the card was written");
            _files.Add(saved);
            StringAssert.StartsWith(Path.Combine(Application.persistentDataPath, MomentRecorder.PhotoFolder), saved);
            Assert.IsFalse(File.Exists(saved + ".tmp"), "written atomically");
            var tex = new Texture2D(2, 2);
            Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(saved)));
            Assert.AreEqual(2 * 32 + 3 * MomentRecorder.StillWidth + 2 * 24, tex.width, "three stills in the frame");
            Color32 corner = tex.GetPixel(0, tex.height - 1);
            Assert.AreEqual(new Color32(0x18, 0x23, 0x3B, 255), corner, "Void Navy frame");
            UnityEngine.Object.Destroy(tex);
        }
    }
}

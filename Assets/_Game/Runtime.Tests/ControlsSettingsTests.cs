using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// controls-v1: the binding table (arrows beside WASD, the Xbox controller scheme, dead zones), screen-relative
    /// movement and controller aim, glyph text, conflict detection, rebind persistence and the stored settings. Every test
    /// works on a copy of Controls.inputactions and restores the PlayerPrefs keys it touches.
    /// </summary>
    public sealed class ControlsSettingsTests
    {
        const string AssetPath = "Assets/_Game/Settings/Controls.inputactions";
        static readonly double S = Math.Sqrt(0.5);

        InputActionAsset _asset;
        readonly Dictionary<string, (bool has, string s, int i, float f)> _saved = new Dictionary<string, (bool, string, int, float)>();

        [SetUp]
        public void SetUp()
        {
            var src = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            Assert.IsNotNull(src, "Controls.inputactions not imported");
            _asset = InputActionAsset.FromJson(src.ToJson());
            _saved.Clear();
            foreach (string k in GameSettings.AllKeys.Concat(new[] { ControlBindings.PrefsKey }))
                _saved[k] = (PlayerPrefs.HasKey(k), PlayerPrefs.GetString(k, ""), PlayerPrefs.GetInt(k, 0), PlayerPrefs.GetFloat(k, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null) UnityEngine.Object.DestroyImmediate(_asset);
            // Restore exactly what was there (type is unknown to PlayerPrefs, so restore by the key's own kind).
            foreach (var kv in _saved)
            {
                PlayerPrefs.DeleteKey(kv.Key);
                if (!kv.Value.has) continue;
                if (kv.Key == ControlBindings.PrefsKey) PlayerPrefs.SetString(kv.Key, kv.Value.s);
                else if (kv.Key.StartsWith("cm.audio.")) PlayerPrefs.SetFloat(kv.Key, kv.Value.f);
                else PlayerPrefs.SetInt(kv.Key, kv.Value.i);
            }
            PlayerPrefs.Save();
        }

        InputAction A(string name) => _asset.FindActionMap("Player", true).FindAction(name, true);

        static string[] Paths(InputAction a, string group) =>
            a.bindings.Where(b => !b.isComposite && b.groups != null && b.groups.Split(';').Contains(group)).Select(b => b.path).ToArray();

        // ---- bindings ----

        [Test]
        public void MoveHasAnArrowKeyCompositeBesideWasd()
        {
            InputAction move = A("Move");
            var b = move.bindings;
            int arrows = -1, wasd = -1;
            for (int i = 0; i < b.Count; i++)
            {
                if (b[i].isComposite && b[i].name == "Arrows") arrows = i;
                if (b[i].isComposite && b[i].name == "WASD") wasd = i;
            }
            Assert.GreaterOrEqual(wasd, 0, "WASD composite");
            Assert.GreaterOrEqual(arrows, 0, "Arrows composite");
            var parts = new Dictionary<string, string>();
            for (int j = arrows + 1; j < b.Count && b[j].isPartOfComposite; j++) parts[b[j].name] = b[j].path;
            Assert.AreEqual("<Keyboard>/upArrow", parts["up"]);
            Assert.AreEqual("<Keyboard>/downArrow", parts["down"]);
            Assert.AreEqual("<Keyboard>/leftArrow", parts["left"]);
            Assert.AreEqual("<Keyboard>/rightArrow", parts["right"]);
            for (int j = arrows + 1; j < b.Count && b[j].isPartOfComposite; j++) StringAssert.Contains("KeyboardMouse", b[j].groups);
        }

        [Test]
        public void GamepadSchemeIsTheXboxLayout()
        {
            const string G = "Gamepad";
            CollectionAssert.AreEqual(new[] { "<Gamepad>/leftStick" }, Paths(A("Move"), G));
            CollectionAssert.AreEqual(new[] { "<Gamepad>/rightStick" }, Paths(A("Aim"), G));
            CollectionAssert.AreEqual(new[] { "<Gamepad>/buttonSouth" }, Paths(A("Jump"), G));
            CollectionAssert.AreEqual(new[] { "<Gamepad>/rightTrigger" }, Paths(A("Attack"), G));
            CollectionAssert.AreEqual(new[] { "<Gamepad>/leftTrigger", "<Gamepad>/buttonEast" }, Paths(A("Ability"), G));
            CollectionAssert.AreEqual(new[] { "<Gamepad>/buttonWest" }, Paths(A("Interact"), G));
            CollectionAssert.AreEqual(new[] { "<Gamepad>/buttonNorth" }, Paths(A("DigPlace"), G));
            CollectionAssert.AreEqual(new[] { "<Gamepad>/leftShoulder", "<Gamepad>/rightShoulder" }, Paths(A("SwitchDigPlace"), G));
            CollectionAssert.AreEqual(new[] { "<Gamepad>/start" }, Paths(A("Pause"), G));
            CollectionAssert.AreEqual(new[] { "<Gamepad>/select" }, Paths(A("Scoreboard"), G));
            StringAssert.Contains("StickDeadzone", A("Move").bindings.First(b => b.path == "<Gamepad>/leftStick").processors);
            StringAssert.Contains("StickDeadzone", A("Aim").bindings.First(b => b.path == "<Gamepad>/rightStick").processors);
        }

        [Test]
        public void EverySettingsRowFindsItsBinding()
        {
            foreach (ControlRow row in ControlBindings.Rows)
                foreach (RebindSlot slot in row.Keyboard.Concat(row.Gamepad))
                    Assert.GreaterOrEqual(ControlBindings.FindBindingIndex(A(slot.Action), slot), 0, $"{row.Label} {slot.Action} {slot.Device}");
        }

        [Test]
        public void TheDefaultsHaveNoConflicts()
        {
            foreach (ControlRow row in ControlBindings.Rows)
                foreach (RebindSlot slot in row.Keyboard.Concat(row.Gamepad))
                {
                    InputAction a = A(slot.Action);
                    CollectionAssert.IsEmpty(ControlBindings.Conflicts(a, ControlBindings.FindBindingIndex(a, slot)), row.Label);
                }
        }

        [Test]
        public void AConflictIsDetectedInTheSameDeviceOnly()
        {
            InputAction jump = A("Jump");
            int kb = ControlBindings.FindBindingIndex(jump, new RebindSlot("Jump", ControlDevice.KeyboardMouse));
            jump.ApplyBindingOverride(kb, "<Keyboard>/f");
            List<string> c = ControlBindings.Conflicts(jump, kb);
            CollectionAssert.AreEqual(new[] { "Dig / place" }, c);

            // A on the controller as jump and as dig: a gamepad conflict; the keyboard F above is not in it.
            InputAction dig = A("DigPlace");
            int pad = ControlBindings.FindBindingIndex(dig, new RebindSlot("DigPlace", ControlDevice.Gamepad));
            dig.ApplyBindingOverride(pad, "<Gamepad>/buttonSouth");
            CollectionAssert.AreEqual(new[] { "Jump" }, ControlBindings.Conflicts(dig, pad));
        }

        [Test]
        public void ARebindPersistsAndReloads()
        {
            InputAction jump = A("Jump");
            int kb = ControlBindings.FindBindingIndex(jump, new RebindSlot("Jump", ControlDevice.KeyboardMouse));
            jump.ApplyBindingOverride(kb, "<Keyboard>/q");
            InputAction up = A("Move");
            int arrowUp = ControlBindings.FindBindingIndex(up, new RebindSlot("Move", ControlDevice.KeyboardMouse, 0, "Arrows", "up"));
            up.ApplyBindingOverride(arrowUp, "<Keyboard>/i");
            ControlBindings.Persist(_asset);
            Assert.That(PlayerPrefs.GetString(ControlBindings.PrefsKey), Does.Contain("<Keyboard>/q"));

            // A fresh copy of the asset (a new launch) gets the overrides back from PlayerPrefs.
            var src = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            InputActionAsset fresh = InputActionAsset.FromJson(src.ToJson());
            try
            {
                InputAction j2 = fresh.FindActionMap("Player").FindAction("Jump");
                Assert.AreEqual("<Keyboard>/space", j2.bindings[kb].effectivePath);
                ControlBindings.Restore(fresh);
                Assert.AreEqual("<Keyboard>/q", j2.bindings[kb].effectivePath);
                Assert.AreEqual("<Keyboard>/i", fresh.FindActionMap("Player").FindAction("Move").bindings[arrowUp].effectivePath);
                Assert.AreEqual("Q", ControlBindings.Display(j2, kb));

                ControlBindings.ResetAll(fresh);
                Assert.AreEqual("<Keyboard>/space", j2.bindings[kb].effectivePath);
                Assert.IsFalse(PlayerPrefs.HasKey(ControlBindings.PrefsKey));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fresh);
            }
        }

        [Test]
        public void GlyphTextIsXboxOnAControllerAndShortOnKeyboard()
        {
            Assert.AreEqual("A", ControlBindings.DisplayPath("<Gamepad>/buttonSouth"));
            Assert.AreEqual("B", ControlBindings.DisplayPath("<Gamepad>/buttonEast"));
            Assert.AreEqual("X", ControlBindings.DisplayPath("<Gamepad>/buttonWest"));
            Assert.AreEqual("Y", ControlBindings.DisplayPath("<Gamepad>/buttonNorth"));
            Assert.AreEqual("RT", ControlBindings.DisplayPath("<Gamepad>/rightTrigger"));
            Assert.AreEqual("LB", ControlBindings.DisplayPath("<Gamepad>/leftShoulder"));
            Assert.AreEqual("MENU", ControlBindings.DisplayPath("<Gamepad>/start"));
            Assert.AreEqual("VIEW", ControlBindings.DisplayPath("<Gamepad>/select"));
            Assert.AreEqual("LMB", ControlBindings.DisplayPath("<Mouse>/leftButton"));
            Assert.AreEqual("UP", ControlBindings.DisplayPath("<Keyboard>/upArrow"));
            Assert.AreEqual("E", ControlBindings.DisplayPath("<Keyboard>/e"));
            Assert.AreEqual("SPACE", ControlBindings.DisplayPath("<Keyboard>/space"));
            Assert.AreEqual("LMB", ControlBindings.PromptLabel(A("Attack"), ControlDevice.KeyboardMouse));
            Assert.AreEqual("RT", ControlBindings.PromptLabel(A("Attack"), ControlDevice.Gamepad));
            Assert.AreEqual("LT", ControlBindings.PromptLabel(A("Ability"), ControlDevice.Gamepad));
            Assert.AreEqual("X", ControlBindings.PromptLabel(A("Interact"), ControlDevice.Gamepad));
        }

        // ---- movement and aim ----

        [Test]
        public void UpOnKeysOrStickMovesUpTheScreenAtYaw45()
        {
            // Screen up at yaw 45 is sim (-1, -1)/sqrt2: the camera's forward on the ground (Unity yaw 315, z negated).
            Vector3 fwd = Quaternion.Euler(0f, 315f, 0f) * Vector3.forward;
            SimSpace.ToSim(fwd, out double fx, out double fz);
            Assert.AreEqual(-S, fx, 1e-6);
            Assert.AreEqual(-S, fz, 1e-6);

            var st = new HumanInputState();
            InputFrame f = SimInput.EmptyInput();
            st.SetMove(0, 1);
            st.Fill(f);
            Assert.AreEqual(fx, f.MoveX, 1e-6);   // fx, fz come from a float quaternion
            Assert.AreEqual(fz, f.MoveZ, 1e-6);
            st.SetMove(1, 0);
            st.Fill(f);
            Assert.AreEqual(S, f.MoveX, 1e-9);   // screen right = sim (1, -1)/sqrt2
            Assert.AreEqual(-S, f.MoveZ, 1e-9);
        }

        [Test]
        public void StickAimPointsWhereTheStickPointsThenFollowsMovement()
        {
            var aim = new AimResolver();
            Assert.AreEqual(AimResolver.Mode.Pointer, aim.Current);
            Assert.IsFalse(aim.ResolveStick(0, 0, 0, 0, 10, 20, out _, out _), "no direction yet = no aim");

            aim.NoteStick();
            Assert.AreEqual(AimResolver.Mode.Stick, aim.Current);
            Assert.IsTrue(aim.ResolveStick(0, 1, 0, 0, 10, 20, out double x, out double z));
            Assert.AreEqual(10 - S * AimResolver.StickAimDistance, x, 1e-9);   // up the screen
            Assert.AreEqual(20 - S * AimResolver.StickAimDistance, z, 1e-9);

            // Right stick at rest: the aim follows the move direction (screen right).
            Assert.IsTrue(aim.ResolveStick(0, 0, 1, 0, 10, 20, out x, out z));
            Assert.AreEqual(10 + S * AimResolver.StickAimDistance, x, 1e-9);
            Assert.AreEqual(20 - S * AimResolver.StickAimDistance, z, 1e-9);

            // Nothing pushed: the last direction stays, around the new position.
            Assert.IsTrue(aim.ResolveStick(0, 0, 0, 0, 0, 0, out x, out z));
            Assert.AreEqual(S * AimResolver.StickAimDistance, x, 1e-9);
            Assert.AreEqual(-S * AimResolver.StickAimDistance, z, 1e-9);

            // The stick wins over movement.
            Assert.IsTrue(aim.ResolveStick(-1, 0, 1, 0, 0, 0, out x, out z));
            Assert.AreEqual(-S * AimResolver.StickAimDistance, x, 1e-9);

            aim.NotePointer();
            Assert.AreEqual(AimResolver.Mode.Pointer, aim.Current);
        }

        // ---- stored settings ----

        [Test]
        public void SettingsPersistInPlayerPrefsWithDefaults()
        {
            foreach (string k in GameSettings.AllKeys) PlayerPrefs.DeleteKey(k);
            Assert.IsTrue(GameSettings.Blood, "blood defaults on");
            Assert.AreEqual(0, PlayerPrefs.GetInt(GameSettings.BloodKey, 0), "a default is not written until changed");
            Assert.AreEqual(1f, GameSettings.MasterVolume);

            GameSettings.Blood = false;
            GameSettings.DamageNumbers = false;
            GameSettings.CameraShake = false;
            GameSettings.MasterVolume = 0.4f;
            GameSettings.MusicVolume = 0.25f;
            GameSettings.EffectsVolume = 0.6f;
            GameSettings.FullScreen = false;
            GameSettings.VSync = false;
            GameSettings.Quality = 0;
            GameSettings.Resolution = new Vector2Int(1280, 720);

            // What another system (or the next launch) reads straight from PlayerPrefs.
            Assert.AreEqual("cm.blood", GameSettings.BloodKey);
            Assert.AreEqual(0, PlayerPrefs.GetInt("cm.blood", 1));
            Assert.AreEqual(0.4f, PlayerPrefs.GetFloat(GameSettings.MasterKey), 1e-6f);
            Assert.IsFalse(GameSettings.DamageNumbers);
            Assert.IsFalse(GameSettings.CameraShake);
            Assert.AreEqual(0.25f, GameSettings.MusicVolume, 1e-6f);
            Assert.AreEqual(0.6f, GameSettings.EffectsVolume, 1e-6f);
            Assert.IsFalse(GameSettings.FullScreen);
            Assert.IsFalse(GameSettings.VSync);
            Assert.AreEqual(0, GameSettings.Quality);
            Assert.AreEqual(new Vector2Int(1280, 720), GameSettings.Resolution);

            GameSettings.ResetAll();
            Assert.IsTrue(GameSettings.Blood);
            Assert.AreEqual(-1, GameSettings.Quality);
        }

        [Test]
        public void FrameRateLimitAndBackgroundThrottleAreStoredForTheLimiter()
        {
            PlayerPrefs.DeleteKey("cm.fpsCap");
            PlayerPrefs.DeleteKey("cm.bgThrottle");
            Assert.AreEqual(60, GameSettings.FpsCap, "default 60");
            Assert.IsTrue(GameSettings.BackgroundThrottle, "default on");
            CollectionAssert.AreEqual(new[] { 30, 60, 120, 0 }, GameSettings.FpsCapOptions);
            Assert.AreEqual("60 FPS", PauseMenu.FpsCapText());

            GameSettings.FpsCap = 120;
            Assert.AreEqual(120, PlayerPrefs.GetInt("cm.fpsCap"));
            GameSettings.FpsCap = 0;
            Assert.AreEqual(0, PlayerPrefs.GetInt("cm.fpsCap"));
            Assert.AreEqual("DISPLAY RATE", PauseMenu.FpsCapText());
            GameSettings.FpsCap = 45;   // not a choice: back to the default
            Assert.AreEqual(60, PlayerPrefs.GetInt("cm.fpsCap"));
            GameSettings.BackgroundThrottle = false;
            Assert.AreEqual(0, PlayerPrefs.GetInt("cm.bgThrottle", 1));
        }

        [Test]
        public void VolumesMapToMixerDecibelsAndTheMixerExposesThem()
        {
            Assert.AreEqual(0f, GameSettings.ToDecibels(1f), 1e-5f);
            Assert.AreEqual(-80f, GameSettings.ToDecibels(0f));
            Assert.AreEqual(-6.0206f, GameSettings.ToDecibels(0.5f), 1e-3f);
            var mixer = Resources.Load<UnityEngine.Audio.AudioMixer>(GameSettings.MixerResource);
            Assert.IsNotNull(mixer, "Resources/" + GameSettings.MixerResource + ".mixer");
            Assert.IsTrue(mixer.GetFloat(GameSettings.MasterParam, out _));
            Assert.IsTrue(mixer.GetFloat(GameSettings.MusicParam, out _));
            Assert.IsTrue(mixer.GetFloat(GameSettings.EffectsParam, out _));
            Assert.IsNotEmpty(mixer.FindMatchingGroups("Music"));
            Assert.IsNotEmpty(mixer.FindMatchingGroups("Effects"));
        }
    }
}

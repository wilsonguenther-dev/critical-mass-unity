using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using B = CriticalMass.Runtime.Input.HumanInputState.HumanButton;

namespace CriticalMass.Runtime.Tests.PlayerExperience
{
    /// <summary>
    /// Lane I (player-experience wave, 2026-11-22): presses are never lost at the input layer, H no longer hides the HUD,
    /// one job per button, and no default binding sits on a key the game reads straight from the keyboard in a match.
    /// </summary>
    public sealed class PxInputBindingsTests
    {
        const string AssetPath = "Assets/_Game/Settings/Controls.inputactions";

        /// <summary>Keys read directly (not through the Player map) while a match is being played, in any file. A default
        /// binding on one of these does two things at once.</summary>
        static readonly string[] HardCodedInPlay =
        {
            "<Keyboard>/h",   // was SettingsExtras' clean-HUD toggle; listed so it can never come back while H is Interact
            "<Keyboard>/f1", "<Keyboard>/f3", "<Keyboard>/f6", "<Keyboard>/f12",
            "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5", "<Keyboard>/6", "<Keyboard>/7",
        };

        /// <summary>Collisions found by lane I in files other lanes own (FINDINGS.md, "Needs from other lanes"): reported, not
        /// failed here. Key: path; value: who reads it.</summary>
        static readonly Dictionary<string, string> KnownOtherLane = new Dictionary<string, string>
        {
            { "<Keyboard>/p", "Hud/ProgressionHud.cs opens the character panel on P (Cancel, J K L layout)" },
            { "<Keyboard>/g", "Hud/MascotGuide.cs closes the guide on G (Ping)" },
            { "<Keyboard>/v", "Online/OnlineVoice.cs push-to-talk on V (Interact, arrows layout)" },
        };

        InputActionAsset _asset;
        readonly Dictionary<string, (bool has, int v)> _prefs = new Dictionary<string, (bool, int)>();

        [SetUp]
        public void SetUp()
        {
            var src = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            Assert.IsNotNull(src, "Controls.inputactions not imported");
            _asset = InputActionAsset.FromJson(src.ToJson());
            _prefs.Clear();
            var keys = new List<string>(AimHelpSettings.AllKeys) { SettingsExtras.CleanHudKey, SettingsExtras.CleanHudMigrationKey };
            foreach (string k in keys) _prefs[k] = (PlayerPrefs.HasKey(k), PlayerPrefs.GetInt(k, 0));
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null) UnityEngine.Object.DestroyImmediate(_asset);
            foreach (var kv in _prefs)
            {
                PlayerPrefs.DeleteKey(kv.Key);
                if (kv.Value.has) PlayerPrefs.SetInt(kv.Key, kv.Value.v);
            }
            PlayerPrefs.Save();
        }

        InputActionMap Map => _asset.FindActionMap("Player", true);

        IEnumerable<(string action, string path)> DefaultPaths()
        {
            foreach (InputAction a in Map.actions)
                foreach (InputBinding b in a.bindings)
                    if (!b.isComposite && !string.IsNullOrEmpty(b.path)) yield return (a.name, b.path.ToLowerInvariant());
        }

        [Test]
        public void NoDefaultBindingSitsOnAHardCodedKey()
        {
            var hard = new HashSet<string>(Array.ConvertAll(HardCodedInPlay, s => s.ToLowerInvariant()));
            foreach (var (action, path) in DefaultPaths())
            {
                if (action == "Interact" && path == "<keyboard>/h") continue; // H is Interact by design; nothing else reads H in play now
                Assert.IsFalse(hard.Contains(path), action + " is bound to " + path + ", which the game also reads directly");
            }
            foreach (var kv in KnownOtherLane)
                foreach (var (action, path) in DefaultPaths())
                    if (path == kv.Key) Assert.Warn(action + " on " + kv.Key + " collides with " + kv.Value + " (routed to its owner)");
        }

        [Test]
        public void TheHKeyNoLongerTogglesTheHud()
        {
            string src = File.ReadAllText("Assets/_Game/Runtime/Menus/SettingsExtras.cs");
            StringAssert.DoesNotContain("hKey", src, "no hard-coded H in SettingsExtras");
            Assert.AreEqual(0, SettingsExtras.HardCodedMatchKeys.Length);
            CollectionAssert.Contains(new List<string>(PathsOf("Interact")), "<keyboard>/h", "H stays the J K L layout's Interact");
        }

        [Test]
        public void AStoredHiddenHudIsClearedOnceByTheMigration()
        {
            PlayerPrefs.DeleteKey(SettingsExtras.CleanHudMigrationKey);
            SettingsExtras.CleanHud = true;
            Assert.IsTrue(SettingsExtras.MigrateCleanHudOnce(), "the old flag is cleared");
            Assert.IsFalse(SettingsExtras.CleanHud);
            SettingsExtras.CleanHud = true;   // chosen again from the settings row after the upgrade
            Assert.IsFalse(SettingsExtras.MigrateCleanHudOnce(), "runs once");
            Assert.IsTrue(SettingsExtras.CleanHud, "a later choice is kept");
        }

        [Test]
        public void AnInteractTapShorterThanATickReachesTheSimOnce()
        {
            var s = new HumanInputState();
            s.Reset();
            InputFrame f = SimInput.EmptyInput();
            s.ButtonDown(B.Interact);
            s.ButtonUp(B.Interact);   // released before any step
            s.Fill(f);
            Assert.IsTrue(f.Interact, "the tap arrives");
            s.Fill(f);
            Assert.IsFalse(f.Interact, "once");
            s.ButtonDown(B.Interact);
            s.Fill(f);
            Assert.IsTrue(f.Interact);
            s.Fill(f);
            Assert.IsTrue(f.Interact, "a hold still holds");
            s.ButtonUp(B.Interact);
            s.Fill(f);
            Assert.IsFalse(f.Interact);
        }

        [Test]
        public void EveryShortTapReachesTheSim()
        {
            foreach (B b in new[] { B.Attack, B.Ability, B.DigPlace, B.Jump, B.Dodge, B.Cancel, B.Interact })
            {
                var s = new HumanInputState();
                s.Reset();
                InputFrame f = SimInput.EmptyInput();
                s.ButtonDown(b);
                s.ButtonUp(b);
                s.Fill(f);
                bool v = b == B.Attack ? f.Attack : b == B.Ability ? f.Ability : b == B.DigPlace ? f.Terraform : b == B.Jump ? f.Jump
                    : b == B.Dodge ? f.Dodge : b == B.Cancel ? f.Cancel : f.Interact;
                Assert.IsTrue(v, b + ": a tap inside one frame reaches the next step");
            }
        }

        [Test]
        public void TheAbilityBufferIsAsLongAsTheSimsAttackBuffer()
        {
            Assert.AreEqual(10, InputBuffer.MaxEarlyTicks(30, InputBuffer.AbilityWindowSeconds));
            Assert.AreEqual(4, InputBuffer.MaxEarlyTicks(30), "the attack's old window is unchanged");
            Assert.GreaterOrEqual(InputBuffer.AttackWindow(null), InputBuffer.WindowSeconds);
        }

        [Test]
        public void OneJobPerButton()
        {
            CollectionAssert.DoesNotContain(PathsOf("CameraView"), "<gamepad>/leftstickpress", "L3 is the emote wheel only");
            CollectionAssert.Contains(PathsOf("Ping"), "<mouse>/middlebutton", "middle click pings");
            foreach (var (action, path) in DefaultPaths())
                if (path == "<mouse>/middlebutton") Assert.AreEqual("Ping", action, "middle mouse has one job");
            CollectionAssert.Contains(PathsOf("Look"), "<keyboard>/backquote", "hold-to-look has its own key");
            foreach (var (action, path) in DefaultPaths())
                if (path == "<keyboard>/backquote") Assert.AreEqual("Look", action);
            Assert.IsNotNull(ControlSchemes.Equivalent("CameraView", ControlScheme.Gamepad));
            Assert.IsNotNull(ControlSchemes.Equivalent("Look", ControlScheme.Gamepad));
        }

        [Test]
        public void AimHelpSettingsPersistWithDefaults()
        {
            AimHelpSettings.ResetAll();
            Assert.AreEqual(AimHelpLevel.Low, AimHelpSettings.Get(AimDevice.Mouse));
            Assert.AreEqual(AimHelpLevel.High, AimHelpSettings.Get(AimDevice.Controller));
            Assert.AreEqual(AimHelpLevel.High, AimHelpSettings.Get(AimDevice.Keys));
            AimHelpSettings.Step(AimDevice.Mouse, 1);
            Assert.AreEqual(AimHelpLevel.High, AimHelpSettings.Get(AimDevice.Mouse));
            AimHelpSettings.Step(AimDevice.Mouse, 1);
            Assert.AreEqual(AimHelpLevel.Off, AimHelpSettings.Get(AimDevice.Mouse));
            Assert.AreEqual(0, PlayerPrefs.GetInt(AimHelpSettings.MouseKey, -1));
            AimHelpSettings.Step(AimDevice.Mouse, -1);
            Assert.AreEqual(AimHelpLevel.High, AimHelpSettings.Get(AimDevice.Mouse));
        }

        List<string> PathsOf(string action)
        {
            var r = new List<string>();
            InputAction a = Map.FindAction(action, false);
            if (a == null) return r;
            foreach (InputBinding b in a.bindings) if (!string.IsNullOrEmpty(b.path)) r.Add(b.path.ToLowerInvariant());
            return r;
        }
    }
}

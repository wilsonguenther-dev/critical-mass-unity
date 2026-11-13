using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using B = CriticalMass.Runtime.Input.HumanInputState.HumanButton;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// controls-1 round 2 (owner 2026-11-13: "arrows for movement and J for attack felt uncomfortable; Z, X, C on my left
    /// hand with the arrows on my right makes sense"). Hand separation of every default, the named presets (one press,
    /// saved, CUSTOM after a rebind, no collisions), and the respawn hold-off. Works on a copy of Controls.inputactions and
    /// puts back every PlayerPrefs key it touches.
    /// </summary>
    public sealed class ControlPresetsTests
    {
        const string AssetPath = "Assets/_Game/Settings/Controls.inputactions";

        // The two halves of a keyboard (thumbs share SPACE).
        static readonly HashSet<string> LeftHalf = new HashSet<string>
        {
            "escape", "tab", "leftShift", "leftCtrl", "leftAlt", "1", "2", "3", "4", "5",
            "q", "w", "e", "r", "t", "a", "s", "d", "f", "g", "z", "x", "c", "v", "b",
        };
        static readonly HashSet<string> RightHalf = new HashSet<string>
        {
            "y", "u", "i", "o", "p", "h", "j", "k", "l", "semicolon", "quote", "n", "m", "comma", "period", "slash",
            "rightShift", "rightAlt", "rightCtrl", "enter", "upArrow", "downArrow", "leftArrow", "rightArrow",
        };

        // The verbs pressed while moving: never on the movement hand.
        static readonly string[] Verbs = { "Attack", "Ability", "Jump", "Interact", "DigPlace", "SwitchDigPlace", "LockFacing" };

        InputActionAsset _asset;
        bool _kbOnly;
        KeyboardHand _hand;
        readonly Dictionary<string, string> _prefs = new Dictionary<string, string>();

        [SetUp]
        public void SetUp()
        {
            var src = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            Assert.IsNotNull(src, "Controls.inputactions not imported");
            _asset = InputActionAsset.FromJson(src.ToJson());
            _kbOnly = ControlBindings.KeyboardOnly;
            _hand = ControlBindings.Hand;
            _prefs.Clear();
            foreach (string k in new[] { ControlBindings.PrefsKey, ControlPresets.PrefsKey })
                _prefs[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k) : null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null) Object.DestroyImmediate(_asset);
            foreach (var kv in _prefs)
            {
                if (kv.Value == null) PlayerPrefs.DeleteKey(kv.Key);
                else PlayerPrefs.SetString(kv.Key, kv.Value);
            }
            PlayerPrefs.Save();
            ControlPresets.SetLeftHandedForTests(ControlPresets.Stored == ControlPreset.LeftHanded);
            ControlBindings.SetKeyboardOnly(_kbOnly);
            ControlBindings.SetHand(_hand);
        }

        InputAction A(string name) => ControlBindings.Map(_asset).FindAction(name, true);

        static string Key(string path) => path.StartsWith("<Keyboard>/") ? path.Substring("<Keyboard>/".Length) : path;

        string PromptPath(string action)
        {
            InputAction a = A(action);
            int i = ControlBindings.PromptBindingIndex(a, ControlDevice.KeyboardMouse);
            return i < 0 ? "" : a.bindings[i].effectivePath;
        }

        [Test]
        public void NoKeyboardLayoutPutsAVerbOnTheMovementHand()
        {
            ControlPresets.SetLeftHandedForTests(false);
            ControlBindings.SetKeyboardOnly(true);
            ControlBindings.SetHand(KeyboardHand.Arrows);
            foreach (string v in Verbs)
            {
                string k = Key(PromptPath(v));
                Assert.IsTrue(LeftHalf.Contains(k), $"arrows layout: {v} is on {k}, not the left hand");
            }
            ControlBindings.SetHand(KeyboardHand.Wasd);
            foreach (string v in Verbs)
            {
                string k = Key(PromptPath(v));
                Assert.IsTrue(RightHalf.Contains(k) || k == "space", $"WASD + J K L layout: {v} is on {k}, not the right hand or a thumb");
            }
            // The owner's pair, exactly.
            ControlBindings.SetHand(KeyboardHand.Arrows);
            Assert.AreEqual("<Keyboard>/z", PromptPath("Attack"));
            Assert.AreEqual("<Keyboard>/x", PromptPath("Ability"));
            Assert.AreEqual("<Keyboard>/c", PromptPath("Jump"));
        }

        [Test]
        public void EveryPresetAppliesInOnePressSavesAndHasNoCollisions()
        {
            foreach (ControlPreset p in ControlPresets.All)
            {
                ControlPresets.Apply(_asset, p);
                Assert.AreEqual(p, ControlPresets.Stored, p.ToString());
                Assert.AreEqual(p, ControlPresets.Current(_asset), p + " is current right after it was applied");
                Assert.AreEqual(p == ControlPreset.LeftHanded, ControlPresets.LeftHandedActive);
                foreach (ControlScheme s in new[] { ControlScheme.Keyboard, ControlScheme.KeyboardMouse })
                    CollectionAssert.IsEmpty(ControlSchemes.Collisions(ControlBindings.Map(_asset), s), $"{p} in {s}");
                foreach (ControlScheme s in new[] { ControlScheme.Keyboard, ControlScheme.KeyboardMouse, ControlScheme.Gamepad })
                    CollectionAssert.IsEmpty(ControlSchemes.Missing(ControlBindings.Map(_asset), s), $"{p} in {s}");
                Assert.IsNotEmpty(ControlPresets.Summary(p));
            }
            ControlPresets.Apply(_asset, ControlPreset.ArrowsZxc);
            Assert.AreEqual(KeyboardHand.Arrows, ControlBindings.Hand);
            Assert.IsTrue(ControlBindings.KeyboardOnly, "arrows + Z X C prompts name keys, not clicks");
            ControlPresets.Apply(_asset, ControlPreset.WasdJkl);
            Assert.AreEqual(KeyboardHand.Wasd, ControlBindings.Hand);
            Assert.IsTrue(ControlBindings.KeyboardOnly);
            ControlPresets.Apply(_asset, ControlPreset.WasdMouse);
            Assert.IsFalse(ControlBindings.KeyboardOnly, "WASD + mouse prompts name the clicks");
            Assert.AreEqual("<Mouse>/leftButton", PromptPath("Attack"));
            Assert.AreEqual("<Keyboard>/e", PromptPath("Interact"));
        }

        [Test]
        public void LeftHandedMovesTheVerbsBesideTheArrowsAndPromptsFollow()
        {
            ControlPresets.Apply(_asset, ControlPreset.LeftHanded);
            Assert.IsFalse(ControlBindings.KeyboardOnly, "the mouse is in the left hand");
            Assert.AreEqual("<Mouse>/leftButton", PromptPath("Attack"));
            Assert.AreEqual("<Mouse>/rightButton", PromptPath("Ability"));
            foreach (string v in new[] { "Jump", "Interact", "DigPlace", "Ping" })
            {
                string k = Key(PromptPath(v));
                Assert.IsTrue(RightHalf.Contains(k), $"left-handed: {v} is on {k}, not beside the arrows");
            }
            Assert.AreEqual("<Keyboard>/rightShift", PromptPath("Jump"));
            // Every moved key has a readable glyph.
            foreach (ControlPresets.Remap r in ControlPresets.Remaps(ControlPreset.LeftHanded))
                {
                    Assert.AreNotEqual("-", ControlBindings.DisplayPath(r.To), r.To);
                    Assert.AreEqual(GlyphKind.Key, InputGlyphs.ForPath(r.To).Kind, r.To);
                }
        }

        [Test]
        public void ARebindAfterAPresetIsCustomAndResetAllForgetsThePreset()
        {
            ControlPresets.Apply(_asset, ControlPreset.LeftHanded);
            InputAction attack = A("Attack");
            int j = attack.bindings.ToList().FindIndex(b => b.path == "<Keyboard>/j");
            attack.ApplyBindingOverride(j, "<Keyboard>/o");
            Assert.IsNull(ControlPresets.Current(_asset), "a rebind on top of a preset is a custom layout");
            Assert.AreEqual(ControlPreset.LeftHanded, ControlPresets.Stored, "the preset picked is still remembered");
            ControlBindings.ResetAll(_asset);
            Assert.IsNull(ControlPresets.Stored);
            Assert.IsFalse(ControlPresets.LeftHandedActive);
            Assert.IsFalse(_asset.actionMaps.SelectMany(m => m.bindings).Any(b => b.overridePath != null));
        }

        // ---- respawn hold-off ----

        static World NewWorld(GameConfig cfg) => World.CreateWorld(new WorldOptions
        {
            Seed = cfg.Seed,
            TeamSize = cfg.TeamSize,
            HumanTeam = TeamId.Azure,
            HumanClass = ClassId.Warrior,
            Config = cfg,
        });

        [Test]
        public void AnAttackHeldThroughADeathDoesNotSwingOnRespawnUntilPressedAgain()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            w.Actors[0].StandUp = 0;
            var st = new HumanInputState { BufferWorld = w };
            InputFrame f = SimInput.EmptyInput();

            st.ButtonDown(B.Attack);
            st.Fill(f);
            Assert.IsTrue(f.Attack, "alive: a held attack attacks");
            w.Actors[0].Alive = false;
            st.Fill(f);
            st.ButtonDown(B.Jump);
            st.ButtonDown(B.Ability);
            w.Actors[0].Alive = true;
            st.Fill(f);
            Assert.IsFalse(f.Attack, "the first frame of the new life: the old hold does not swing");
            Assert.IsFalse(f.Ability, "nor a press queued while dead");
            Assert.IsFalse(f.Jump);
            Assert.IsTrue(st.AttackHeldOff);
            st.ButtonDown(B.Attack, repeat: true);
            st.Fill(f);
            Assert.IsFalse(f.Attack, "an OS key repeat of the old hold is not a new press");
            st.ButtonUp(B.Attack);
            st.ButtonDown(B.Attack);
            st.Fill(f);
            Assert.IsTrue(f.Attack, "a fresh press attacks at once");
            Assert.IsFalse(st.AttackHeldOff);
        }

        // ---- audio mutes ----

        [Test]
        public void MutesSilenceTheirBusKeepTheSliderAndPersist()
        {
            var r = GameSettings.Mix(0.9f, 0.7f, 0.6f, false, false, false, true);
            Assert.AreEqual((0.9f, 0.7f, 0.6f), r, "nothing muted: the sliders as set");
            r = GameSettings.Mix(0.9f, 0.7f, 0.6f, true, false, false, true);
            Assert.AreEqual(0f, r.music);
            Assert.AreEqual(0.6f, r.effects, 1e-6f);
            r = GameSettings.Mix(0.9f, 0.7f, 0.6f, false, true, false, true);
            Assert.AreEqual(0f, r.effects);
            Assert.AreEqual(0.7f, r.music, 1e-6f);
            Assert.AreEqual(0.9f, GameSettings.Mix(0.9f, 0.7f, 0.6f, false, false, true, true).master, 1e-6f, "focused: plays");
            Assert.AreEqual(0f, GameSettings.Mix(0.9f, 0.7f, 0.6f, false, false, true, false).master, "background: silent");
            Assert.AreEqual(0.9f, GameSettings.Mix(0.9f, 0.7f, 0.6f, false, false, false, false).master, 1e-6f, "option off: background plays");

            string[] keys = { GameSettings.MuteMusicKey, GameSettings.MuteEffectsKey, GameSettings.MuteInBackgroundKey, GameSettings.ColourBlindTeamsKey };
            var saved = keys.ToDictionary(k => k, k => PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null);
            bool cb = CMTokens.ColourBlindTeams;
            try
            {
                float music = GameSettings.MusicVolume;
                GameSettings.MuteMusic = true;
                GameSettings.MuteEffects = true;
                GameSettings.MuteInBackground = true;
                Assert.IsTrue(GameSettings.MuteMusic && GameSettings.MuteEffects && GameSettings.MuteInBackground);
                Assert.AreEqual(music, GameSettings.MusicVolume, "a mute keeps the slider");
                foreach (string k in new[] { GameSettings.MuteMusicKey, GameSettings.MuteEffectsKey, GameSettings.MuteInBackgroundKey })
                    Assert.AreEqual(1, PlayerPrefs.GetInt(k, 0), k);
                CollectionAssert.IsSubsetOf(keys, GameSettings.AllKeys, "RESET ALL SETTINGS clears them");
            }
            finally
            {
                foreach (var kv in saved)
                {
                    if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value);
                    else PlayerPrefs.DeleteKey(kv.Key);
                }
                CMTokens.ColourBlindTeams = cb;
                PlayerPrefs.Save();
            }
        }

        // ---- colour-blind teams, checked with numbers ----

        static float Lin(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        static Vector3 Linear(Color c) => new Vector3(Lin(c.r), Lin(c.g), Lin(c.b));

        static float Luminance(Vector3 l) => 0.2126f * l.x + 0.7152f * l.y + 0.0722f * l.z;

        static float Contrast(Color a, Color b)
        {
            float la = Luminance(Linear(a)), lb = Luminance(Linear(b));
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        // Machado, Oliveira and Fernandes (2009), severity 1.0, on linear RGB.
        static readonly float[][,] Cvd =
        {
            new float[,] { { 0.152286f, 1.052583f, -0.204868f }, { 0.114503f, 0.786281f, 0.099216f }, { -0.003882f, -0.048116f, 1.051998f } },
            new float[,] { { 0.367322f, 0.860646f, -0.227968f }, { 0.280085f, 0.672501f, 0.047413f }, { -0.011820f, 0.042940f, 0.968881f } },
            new float[,] { { 1.255528f, -0.076749f, -0.178779f }, { -0.078411f, 0.930809f, 0.147602f }, { 0.004733f, 0.691367f, 0.303900f } },
        };

        static Vector3 Simulate(Vector3 l, float[,] m) => new Vector3(
            Mathf.Clamp01(m[0, 0] * l.x + m[0, 1] * l.y + m[0, 2] * l.z),
            Mathf.Clamp01(m[1, 0] * l.x + m[1, 1] * l.y + m[1, 2] * l.z),
            Mathf.Clamp01(m[2, 0] * l.x + m[2, 1] * l.y + m[2, 2] * l.z));

        static Vector3 Lab(Vector3 l)
        {
            float x = (0.4124f * l.x + 0.3576f * l.y + 0.1805f * l.z) / 0.95047f;
            float y = 0.2126f * l.x + 0.7152f * l.y + 0.0722f * l.z;
            float z = (0.0193f * l.x + 0.1192f * l.y + 0.9505f * l.z) / 1.08883f;
            float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
            return new Vector3(116f * F(y) - 16f, 500f * (F(x) - F(y)), 200f * (F(y) - F(z)));
        }

        /// <summary>The weakest CIE Lab distance between two colours under the three simulated deficiencies.</summary>
        static float WorstDistance(Color a, Color b)
        {
            float worst = float.MaxValue;
            foreach (float[,] m in Cvd) worst = Mathf.Min(worst, Vector3.Distance(Lab(Simulate(Linear(a), m)), Lab(Simulate(Linear(b), m))));
            return worst;
        }

        [Test]
        public void TheColourBlindTeamsStayApartUnderEveryDeficiencyAndReadOnThePanels()
        {
            Color azure = CMTokens.AzureColourSafe.Color, crimson = CMTokens.CrimsonColourSafe.Color;
            foreach (Color c in new[] { azure, crimson })
            {
                Assert.GreaterOrEqual(Contrast(c, CMTokens.PanelNavy.Color), 4.5f, "text on Panel Navy");
                Assert.GreaterOrEqual(Contrast(c, CMTokens.VoidNavy.Color), 4.5f, "on Void Navy");
            }
            float safe = WorstDistance(azure, crimson), bible = WorstDistance(CMTokens.Azure.Color, CMTokens.Crimson.Color);
            Assert.GreaterOrEqual(safe, 60f, "the two teams stay far apart for red-weak, green-weak and blue-weak sight");
            Assert.Greater(safe, bible, "and further apart than the bible pair at its weakest");
            Assert.GreaterOrEqual(Contrast(azure, crimson), 1.5f, "a lightness gap too, for no colour sight at all");

            bool was = CMTokens.ColourBlindTeams;
            try
            {
                CMTokens.ColourBlindTeams = true;
                Assert.AreEqual(azure, CMTokens.Team(TeamId.Azure));
                Assert.AreEqual(crimson, CMTokens.TeamInk(TeamId.Crimson));
                CMTokens.ColourBlindTeams = false;
                Assert.AreEqual(CMTokens.Azure.Color, CMTokens.Team(TeamId.Azure));
            }
            finally { CMTokens.ColourBlindTeams = was; }
        }

        // ---- HUD cards and the class preview from the keys ----

        [Test]
        public void TheKeyboardWalksTheHudCardsAndTurnsTheClassPreview()
        {
            Assert.AreEqual(new Vector2(0f, 1f), HudInteract.KeyDirection(true, false, false, false));
            Assert.AreEqual(new Vector2(-1f, -1f), HudInteract.KeyDirection(false, true, true, false));
            Assert.AreEqual(Vector2.zero, HudInteract.KeyDirection(true, true, true, true), "opposite keys cancel");

            Assert.AreEqual(-1f, ClassSelect.PreviewTurnInput(true, false, 0f), "Q or , turns left at full speed");
            Assert.AreEqual(1f, ClassSelect.PreviewTurnInput(false, true, 0f), "E or . turns right");
            Assert.AreEqual(0f, ClassSelect.PreviewTurnInput(true, true, 0.9f), "both keys: still");
            Assert.AreEqual(0f, ClassSelect.PreviewTurnInput(false, false, 0.15f), "stick inside its dead zone");
            Assert.AreEqual(0.6f, ClassSelect.PreviewTurnInput(false, false, 0.6f), 1e-6f, "the stick at its push");
            Assert.AreEqual(-1f, ClassSelect.PreviewTurnInput(true, false, 0.8f), "a key wins over the stick");
        }
    }
}

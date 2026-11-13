using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// controls-1 (owner, 2026-11-13: "You have built no keyboard keys to attack"): the three schemes are each complete
    /// (every Player action has a binding or a named equivalent in keyboard only, keyboard and mouse, and controller), no
    /// key does two things in one scheme, both keyboard hand positions reach every verb, keyboard aim faces the move
    /// direction with lock facing and target assist, prompts name keys (never clicks) in keyboard-only play, the controller
    /// families get their own labels, and the controller settings shape the sticks. Works on a copy of Controls.inputactions.
    /// </summary>
    public sealed class ControlSchemesTests
    {
        const string AssetPath = "Assets/_Game/Settings/Controls.inputactions";
        static readonly ControlScheme[] Schemes = { ControlScheme.Keyboard, ControlScheme.KeyboardMouse, ControlScheme.Gamepad };

        InputActionAsset _asset;
        bool _kbOnly;
        KeyboardHand _hand;
        readonly Dictionary<string, (bool has, int v)> _prefs = new Dictionary<string, (bool, int)>();

        [SetUp]
        public void SetUp()
        {
            var src = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            Assert.IsNotNull(src, "Controls.inputactions not imported");
            _asset = InputActionAsset.FromJson(src.ToJson());
            _kbOnly = ControlBindings.KeyboardOnly;
            _hand = ControlBindings.Hand;
            _prefs.Clear();
            foreach (string k in ControllerSettings.AllKeys) _prefs[k] = (PlayerPrefs.HasKey(k), PlayerPrefs.GetInt(k, 0));
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null) UnityEngine.Object.DestroyImmediate(_asset);
            ControlBindings.SetKeyboardOnly(_kbOnly);
            ControlBindings.SetHand(_hand);
            ControllerSettings.ForceFamily(null);
            foreach (var kv in _prefs)
            {
                PlayerPrefs.DeleteKey(kv.Key);
                if (kv.Value.has) PlayerPrefs.SetInt(kv.Key, kv.Value.v);
            }
            PlayerPrefs.Save();
        }

        InputActionMap Map => _asset.FindActionMap("Player", true);
        InputAction A(string name) => Map.FindAction(name, true);

        // ---- completeness and collisions ----

        [Test]
        public void EveryActionIsReachableInAllThreeSchemes()
        {
            foreach (ControlScheme s in Schemes)
                CollectionAssert.IsEmpty(ControlSchemes.Missing(Map, s), $"actions with no binding and no equivalent in {s}");
        }

        [Test]
        public void NoKeyDoesTwoThingsInOneScheme()
        {
            foreach (ControlScheme s in Schemes)
                CollectionAssert.IsEmpty(ControlSchemes.Collisions(Map, s), $"collisions in {s}");
        }

        [Test]
        public void ACollisionInASchemeIsCaught()
        {
            // Proves the collision check bites: J as attack and as jump in keyboard-only play.
            InputAction jump = A("Jump");
            int i = ControlBindings.FindBindingIndex(jump, new RebindSlot("Jump", ControlDevice.KeyboardMouse));
            jump.ApplyBindingOverride(i, "<Keyboard>/j");
            List<string> c = ControlSchemes.Collisions(Map, ControlScheme.Keyboard);
            Assert.IsTrue(c.Any(x => x.StartsWith("<Keyboard>/j:") && x.Contains("Attack") && x.Contains("Jump")), string.Join("; ", c));
        }

        [Test]
        public void AnActionWithNoBindingIsCaught()
        {
            InputAction dig = A("DigPlace");
            for (int i = 0; i < dig.bindings.Count; i++)
                if (dig.bindings[i].groups.Contains("Gamepad")) dig.ApplyBindingOverride(i, "");
            CollectionAssert.Contains(ControlSchemes.Missing(Map, ControlScheme.Gamepad), "DigPlace (Gamepad)");
        }

        [Test]
        public void BothHandPositionsReachEveryCombatVerb()
        {
            // name = the hand; shared keys (no name) serve both.
            var expect = new Dictionary<string, string[]>
            {
                { "Attack", new[] { "<Keyboard>/j", "<Keyboard>/z" } },
                { "Ability", new[] { "<Keyboard>/k", "<Keyboard>/x" } },
                { "Jump", new[] { "<Keyboard>/space", "<Keyboard>/c" } },
                { "Interact", new[] { "<Keyboard>/e", "<Keyboard>/v" } },
                { "SwitchDigPlace", new[] { "<Keyboard>/q", "<Keyboard>/slash" } },
                { "LockFacing", new[] { "<Keyboard>/l", "<Keyboard>/leftShift" } },
            };
            foreach (var kv in expect)
            {
                List<string> paths = ControlSchemes.Paths(A(kv.Key), ControlScheme.Keyboard);
                foreach (string p in kv.Value) CollectionAssert.Contains(paths, p, kv.Key);
            }
            Assert.AreEqual(ControlBindings.ArrowsHand, A("Attack").bindings.First(b => b.path == "<Keyboard>/z").name);
            Assert.AreEqual(ControlBindings.WasdHand, A("Attack").bindings.First(b => b.path == "<Keyboard>/j").name);
            // Every other verb has a keyboard key (shared by both hands).
            foreach (string a in new[] { "DigPlace", "Ping", "Scoreboard", "Pause", "Map", "OpenStation", "StationBuy", "Emote", "HudCursor", "MinimapZoom" })
                Assert.IsNotEmpty(ControlSchemes.Paths(A(a), ControlScheme.Keyboard), a);
        }

        [Test]
        public void TheMouseSchemeKeepsClicksAndTheKeyboardKeys()
        {
            List<string> attack = ControlSchemes.Paths(A("Attack"), ControlScheme.KeyboardMouse);
            CollectionAssert.Contains(attack, "<Mouse>/leftButton");
            CollectionAssert.Contains(attack, "<Keyboard>/j");
            CollectionAssert.Contains(ControlSchemes.Paths(A("Ability"), ControlScheme.KeyboardMouse), "<Mouse>/rightButton");
            CollectionAssert.IsEmpty(ControlSchemes.Paths(A("LockFacing"), ControlScheme.KeyboardMouse), "the mouse sets the facing");
            CollectionAssert.IsEmpty(ControlSchemes.Paths(A("MoveToPointer"), ControlScheme.Keyboard), "walking to the pointer needs a mouse");
        }

        [Test]
        public void LockFacingAndWalkToPointerMayShareLeftShiftOnly()
        {
            InputAction lockF = A("LockFacing");
            int i = ControlBindings.FindBindingIndex(lockF, new RebindSlot("LockFacing", ControlDevice.KeyboardMouse, 1));
            Assert.AreEqual("<Keyboard>/leftShift", lockF.bindings[i].effectivePath);
            CollectionAssert.IsEmpty(ControlBindings.Conflicts(lockF, i), "scheme-exclusive pair");
            Assert.IsTrue(ControlSchemes.SchemeExclusive("MoveToPointer", "LockFacing"));
            Assert.IsFalse(ControlSchemes.SchemeExclusive("Attack", "Jump"));
        }

        [Test]
        public void EverySettingsSlotSitsInTheColumnOfItsHand()
        {
            foreach (ControlRow row in ControlBindings.Rows)
                foreach (RebindSlot slot in row.Keyboard)
                {
                    InputAction a = A(slot.Action);
                    int i = ControlBindings.FindBindingIndex(a, slot);
                    Assert.GreaterOrEqual(i, 0, row.Label);
                    InputBinding b = a.bindings[i];
                    string name = b.isPartOfComposite ? slot.Composite : b.name;
                    if (name == ControlBindings.ArrowsHand) Assert.AreEqual(SlotColumn.Arrows, slot.Column, row.Label);
                    else if (name == ControlBindings.WasdHand) Assert.AreEqual(SlotColumn.Wasd, slot.Column, row.Label);
                    if (b.path.StartsWith("<Mouse>")) Assert.AreEqual(SlotColumn.Mouse, slot.Column, row.Label);
                }
            // The grid draws one cell per column: no two slots of a row share one.
            foreach (ControlRow row in ControlBindings.Rows)
            {
                var cols = row.Keyboard.Concat(row.Gamepad).Select(s => s.Column).ToList();
                Assert.AreEqual(cols.Count, cols.Distinct().Count(), row.Label);
            }
        }

        // ---- prompts ----

        [Test]
        public void KeyboardOnlyPromptsNameTheKeyOfTheHandInUseNeverAClick()
        {
            ControlBindings.SetKeyboardOnly(true);
            ControlBindings.SetHand(KeyboardHand.Wasd);
            Assert.AreEqual("J", ControlBindings.PromptLabel(A("Attack"), ControlDevice.KeyboardMouse));
            Assert.AreEqual("K", ControlBindings.PromptLabel(A("Ability"), ControlDevice.KeyboardMouse));
            Assert.AreEqual("SPACE", ControlBindings.PromptLabel(A("Jump"), ControlDevice.KeyboardMouse));
            Assert.AreEqual("G", ControlBindings.PromptLabel(A("Ping"), ControlDevice.KeyboardMouse), "shared key, not middle click");
            ControlBindings.SetHand(KeyboardHand.Arrows);
            Assert.AreEqual("Z", ControlBindings.PromptLabel(A("Attack"), ControlDevice.KeyboardMouse));
            Assert.AreEqual("X", ControlBindings.PromptLabel(A("Ability"), ControlDevice.KeyboardMouse));
            Assert.AreEqual("C", ControlBindings.PromptLabel(A("Jump"), ControlDevice.KeyboardMouse));
            Assert.AreEqual("V", ControlBindings.PromptLabel(A("Interact"), ControlDevice.KeyboardMouse));
            Assert.AreEqual("F", ControlBindings.PromptLabel(A("DigPlace"), ControlDevice.KeyboardMouse), "shared key");
            ControlBindings.SetKeyboardOnly(false);
            Assert.AreEqual("LEFT CLICK", ControlBindings.PromptLabel(A("Attack"), ControlDevice.KeyboardMouse), "mouse in use: the click");
        }

        [Test]
        public void KeyboardOnlyGlyphsHaveNoMouseAnywhere()
        {
            if (InputSystem.actions == null) Assert.Ignore("no project-wide actions in this editor");
            ControlBindings.SetKeyboardOnly(true);
            foreach (KeyboardHand h in new[] { KeyboardHand.Wasd, KeyboardHand.Arrows })
            {
                ControlBindings.SetHand(h);
                foreach (string action in InputGlyphs.PlayerActions)
                {
                    if (action == "SwitchDigPlace" || action == "MoveToPointer") continue;
                    GlyphSpec g = InputGlyphs.ForAction(action, ControlDevice.KeyboardMouse);
                    Assert.AreNotEqual(GlyphKind.Mouse, g.Kind, $"{action} ({h}) shows the mouse in keyboard-only play");
                    StringAssert.DoesNotContain("click", g.Words.ToLowerInvariant(), action);
                }
            }
            ControlBindings.SetHand(KeyboardHand.Arrows);
            Assert.AreEqual("ARROWS", InputGlyphs.ForAction("Move", ControlDevice.KeyboardMouse).Cap);
        }

        [Test]
        public void HandOfReadsTheCompositeOrTheBindingName()
        {
            InputAction attack = A("Attack");
            InputAction move = A("Move");
            Keyboard kb = InputSystem.AddDevice<Keyboard>();
            try
            {
                _asset.Enable();
                Assert.AreEqual(KeyboardHand.Arrows, ControlBindings.HandOf(attack, kb.zKey));
                Assert.AreEqual(KeyboardHand.Wasd, ControlBindings.HandOf(attack, kb.jKey));
                Assert.AreEqual(KeyboardHand.Arrows, ControlBindings.HandOf(move, kb.upArrowKey));
                Assert.AreEqual(KeyboardHand.Wasd, ControlBindings.HandOf(move, kb.wKey));
                Assert.IsNull(ControlBindings.HandOf(A("Scoreboard"), kb.tabKey), "shared keys name no hand");
            }
            finally
            {
                _asset.Disable();
                InputSystem.RemoveDevice(kb);
            }
        }

        [Test]
        public void TheControlsCardShowsEveryLineInEveryScheme()
        {
            if (InputSystem.actions == null) Assert.Ignore("no project-wide actions in this editor");
            string saved = InputSystem.actions.SaveBindingOverridesAsJson();
            InputSystem.actions.RemoveAllBindingOverrides();
            try { CardLines(); }
            finally { if (!string.IsNullOrEmpty(saved)) InputSystem.actions.LoadBindingOverridesFromJson(saved); }
        }

        static void CardLines()
        {
            foreach (ControlsCard.Col c in new[] { ControlsCard.Col.Wasd, ControlsCard.Col.Arrows, ControlsCard.Col.Mouse, ControlsCard.Col.Pad })
            {
                List<string> lines = ControlsCard.Describe(c);
                Assert.AreEqual(ControlsCard.LineLabels.Length, lines.Count);
                foreach (string l in lines)
                {
                    if (c == ControlsCard.Col.Mouse && l.StartsWith("Lock facing")) continue;
                    Assert.IsFalse(l.EndsWith(": "), $"{c}: '{l}' shows no key");
                }
            }
            CollectionAssert.Contains(ControlsCard.Describe(ControlsCard.Col.Wasd), "Attack: J");
            CollectionAssert.Contains(ControlsCard.Describe(ControlsCard.Col.Arrows), "Attack: Z");
            CollectionAssert.Contains(ControlsCard.Describe(ControlsCard.Col.Arrows), "Move: ARROWS");
            CollectionAssert.Contains(ControlsCard.Describe(ControlsCard.Col.Arrows), "Scoreboard: TAB");
            CollectionAssert.Contains(ControlsCard.Describe(ControlsCard.Col.Mouse), "Attack: LEFT CLICK");
            CollectionAssert.Contains(ControlsCard.Describe(ControlsCard.Col.Pad), "Lock facing (hold): R STICK");
        }

        // ---- keyboard aim ----

        [Test]
        public void KeyboardAimFacesTheMoveKeepsItStandingAndLocks()
        {
            var aim = new AimResolver();
            // Standing before any move: the actor's own facing (0 rad = +X).
            Assert.IsTrue(aim.ResolveKeys(0, 0, 10, 20, 0, false, null, null, 0, out double x, out double z));
            Assert.AreEqual(10 + AimResolver.StickAimDistance, x, 1e-9);
            Assert.AreEqual(20, z, 1e-9);
            // Moving +Z: faces +Z.
            aim.ResolveKeys(0, 1, 10, 20, 0, false, null, null, 0, out x, out z);
            Assert.AreEqual(10, x, 1e-9);
            Assert.AreEqual(20 + AimResolver.StickAimDistance, z, 1e-9);
            // Stop: keeps +Z.
            aim.ResolveKeys(0, 0, 10, 20, 0, false, null, null, 0, out x, out z);
            Assert.AreEqual(20 + AimResolver.StickAimDistance, z, 1e-9);
            // Lock held, move -X: still faces +Z (strafe).
            aim.ResolveKeys(-1, 0, 10, 20, 0, true, null, null, 0, out x, out z);
            Assert.AreEqual(10, x, 1e-9);
            Assert.AreEqual(20 + AimResolver.StickAimDistance, z, 1e-9);
            // Lock released while moving -X: faces -X.
            aim.ResolveKeys(-1, 0, 10, 20, 0, false, null, null, 0, out x, out z);
            Assert.AreEqual(10 - AimResolver.StickAimDistance, x, 1e-9);
        }

        [Test]
        public void TargetAssistPicksTheNearestEnemyInTheForwardConeOnly()
        {
            var aim = new AimResolver();
            // Facing +X from the origin. Enemy 0: ahead 20 degrees off at 5 units; enemy 1: ahead at 6 (farther);
            // enemy 2: behind at 2; enemy 3: 60 degrees off at 3; enemy 4: dead ahead at 9 (out of range).
            double r20 = 20 * Math.PI / 180, r60 = 60 * Math.PI / 180;
            double[] ex = { 5 * Math.Cos(r20), 6, -2, 3 * Math.Cos(r60), 9 };
            double[] ez = { 5 * Math.Sin(r20), 0, 0, 3 * Math.Sin(r60), 0 };
            aim.ResolveKeys(1, 0, 0, 0, 0, false, ex, ez, ex.Length, out double x, out double z);
            Assert.AreEqual(0, aim.AssistTarget);
            Assert.AreEqual(ex[0], x, 1e-9);
            Assert.AreEqual(ez[0], z, 1e-9);
            // Nobody in the cone: the plain facing.
            aim.ResolveKeys(1, 0, 0, 0, 0, false, new[] { -2.0 }, new[] { 0.0 }, 1, out x, out z);
            Assert.AreEqual(-1, aim.AssistTarget);
            Assert.AreEqual(AimResolver.StickAimDistance, x, 1e-9);
            // Locked: no assist.
            aim.ResolveKeys(1, 0, 0, 0, 0, true, ex, ez, ex.Length, out x, out z);
            Assert.AreEqual(-1, aim.AssistTarget);
            // A close target is aimed along its line, never inside the facing dead zone.
            aim.ResolveKeys(1, 0, 0, 0, 0, false, new[] { 1.0 }, new[] { 0.0 }, 1, out x, out z);
            Assert.AreEqual(AimResolver.StickAimDistance, x, 1e-9);
        }

        // ---- controllers ----

        [Test]
        public void ControllerFamiliesAreRecognisedByLayoutAndName()
        {
            Assert.AreEqual(PadFamily.PlayStation, ControllerSettings.FamilyOf("DualSenseGamepadHID"));
            Assert.AreEqual(PadFamily.PlayStation, ControllerSettings.FamilyOf("DualShock4GamepadHID"));
            Assert.AreEqual(PadFamily.PlayStation, ControllerSettings.FamilyOf("Gamepad", "Wireless Controller", "Sony Interactive Entertainment"));
            Assert.AreEqual(PadFamily.Xbox, ControllerSettings.FamilyOf("XInputControllerWindows"));
            Assert.AreEqual(PadFamily.Xbox, ControllerSettings.FamilyOf("XboxGamepadMacOSWireless"));
            Assert.AreEqual(PadFamily.Xbox, ControllerSettings.FamilyOf("Gamepad", "Xbox Wireless Controller", "Microsoft"));
            Assert.AreEqual(PadFamily.Generic, ControllerSettings.FamilyOf("Gamepad", "USB Gamepad", "Generic"));
        }

        [Test]
        public void EachFamilyHasItsOwnButtonLabelsAndNoBrandNames()
        {
            GlyphSpec cross = InputGlyphs.Pad("buttonSouth", "<Gamepad>/buttonSouth", PadFamily.PlayStation);
            Assert.AreEqual("Cross button", cross.Words);
            Assert.AreEqual("Circle button", InputGlyphs.Pad("buttonEast", "<Gamepad>/buttonEast", PadFamily.PlayStation).Words);
            Assert.AreEqual("R2", InputGlyphs.Pad("rightTrigger", "<Gamepad>/rightTrigger", PadFamily.PlayStation).Cap);
            Assert.AreEqual("A", InputGlyphs.Pad("buttonSouth", "<Gamepad>/buttonSouth", PadFamily.Xbox).Cap);
            Assert.AreEqual("RT", InputGlyphs.Pad("rightTrigger", "<Gamepad>/rightTrigger", PadFamily.Generic).Cap);
            ControllerSettings.ForceFamily(PadFamily.PlayStation);
            Assert.AreEqual("CROSS", ControlBindings.DisplayPath("<Gamepad>/buttonSouth"));
            Assert.AreEqual("OPTIONS", ControlBindings.DisplayPath("<Gamepad>/start"));
            ControllerSettings.ForceFamily(PadFamily.Xbox);
            Assert.AreEqual("A", ControlBindings.DisplayPath("<Gamepad>/buttonSouth"));
            foreach (string s in new[] { ControlsCardScreen.ConnectHelp, ControllerSettings.Describe(null), ControlsCardScreen.LayoutText() })
                foreach (string brand in new[] { "Xbox", "PlayStation", "DualSense", "DualShock", "Sony", "Microsoft" })
                    StringAssert.DoesNotContain(brand, s);
        }

        [Test]
        public void ControllerSettingsShapeTheSticksAndPersist()
        {
            // Default (small dead zone, normal curve): unchanged.
            Vector2 v = ControllerSettings.Shape(new Vector2(0.5f, 0f), 0.2f, 1f);
            Assert.AreEqual(0.5f, v.x, 1e-5f);
            // Large dead zone (0.4 raw = 0.2667 processed): a light push is zero, a full push still full.
            Assert.AreEqual(Vector2.zero, ControllerSettings.Shape(new Vector2(0.2f, 0f), 0.4f, 1f));
            Assert.AreEqual(1f, ControllerSettings.Shape(new Vector2(0f, 1f), 0.4f, 1f).y, 1e-5f);
            // Low sensitivity: half a push moves slower than half speed; direction kept.
            Vector2 low = ControllerSettings.Shape(new Vector2(0.3f, 0.4f), 0.2f, 1.6f);
            Assert.Less(low.magnitude, 0.5f);
            Assert.AreEqual(0.75f, low.y / low.x, 1e-4f);

            ControllerSettings.InvertAimY = true;
            Assert.AreEqual(-1f, ControllerSettings.ShapeAim(new Vector2(0f, 1f)).y, 1e-5f);
            ControllerSettings.InvertAimY = false;
            ControllerSettings.Vibration = false;
            Assert.IsFalse(ControllerSettings.Vibration);
            ControllerSettings.DeadZoneIndex = 2;
            Assert.AreEqual(2, PlayerPrefs.GetInt(ControllerSettings.DeadZoneKey));
            ControllerSettings.ResetAll();
            Assert.IsTrue(ControllerSettings.Vibration);
            Assert.AreEqual(0, ControllerSettings.DeadZoneIndex);
            Assert.AreEqual(1, ControllerSettings.SensitivityIndex);
        }
    }
}

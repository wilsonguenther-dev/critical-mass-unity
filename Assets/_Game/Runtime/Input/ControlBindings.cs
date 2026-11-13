using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CriticalMass.Runtime.Input
{
    /// <summary>The two binding groups of Controls.inputactions (the three play schemes are ControlScheme).</summary>
    public enum ControlDevice { KeyboardMouse, Gamepad }

    /// <summary>
    /// controls-1: the three ways to play, each complete on its own (ControlSchemes proves it). Keyboard = keys only, the
    /// fighter faces where it moves (AimResolver.Mode.Keys); KeyboardMouse = keys plus the mouse aiming and clicking;
    /// Gamepad = a controller. The live one is ControlBindings.ActiveScheme (the device used last).
    /// </summary>
    public enum ControlScheme { Keyboard, KeyboardMouse, Gamepad }

    /// <summary>Which hand position the keyboard player uses: left hand on W A S D, or right hand on the arrow keys.</summary>
    public enum KeyboardHand { Wasd, Arrows }

    /// <summary>A column of the settings grid and the controls card.</summary>
    public enum SlotColumn { Auto = -1, Wasd = 0, Arrows = 1, Mouse = 2, Pad = 3, Pad2 = 4 }

    /// <summary>One rebindable (or shown-only) binding of the Player map, found by action, composite part and ordinal.</summary>
    public sealed class RebindSlot
    {
        public readonly string Action;
        /// <summary>Composite name (WASD, Arrows, Shoulders) or null for a plain binding.</summary>
        public readonly string Composite;
        /// <summary>Composite part (up, down, left, right, negative, positive) or null.</summary>
        public readonly string Part;
        public readonly ControlDevice Device;
        /// <summary>For a plain binding: the n-th plain binding of the action in the device's group.</summary>
        public readonly int Ordinal;
        /// <summary>False for bindings shown but fixed (mouse pointer aim, mouse wheel).</summary>
        public readonly bool Rebindable;
        /// <summary>
        /// controls-1: the column the settings grid and the controls card draw this slot in (left hand on W A S D, right hand
        /// on the arrow keys, the mouse, the controller). Auto = from the slot itself: a composite named WASD or Arrows, the
        /// controller's first or second slot; a plain keyboard slot without a column goes to the W A S D column.
        /// </summary>
        public readonly SlotColumn Column;

        public RebindSlot(string action, ControlDevice device, int ordinal = 0, string composite = null, string part = null,
            bool rebindable = true, SlotColumn column = SlotColumn.Auto)
        {
            Action = action;
            Device = device;
            Ordinal = ordinal;
            Composite = composite;
            Part = part;
            Rebindable = rebindable;
            if (column == SlotColumn.Auto)
            {
                if (device == ControlDevice.Gamepad) column = ordinal > 0 || part == "positive" ? SlotColumn.Pad2 : SlotColumn.Pad;
                else if (composite == ControlBindings.ArrowsHand) column = SlotColumn.Arrows;
                else column = SlotColumn.Wasd;
            }
            Column = column;
        }
    }

    /// <summary>One line of the Controls tab: a label and up to two slots per device.</summary>
    public sealed class ControlRow
    {
        public readonly string Label;
        public readonly RebindSlot[] Keyboard;
        public readonly RebindSlot[] Gamepad;

        public ControlRow(string label, RebindSlot[] keyboard, RebindSlot[] gamepad)
        {
            Label = label;
            Keyboard = keyboard;
            Gamepad = gamepad;
        }
    }

    /// <summary>
    /// The Player map's bindings as the settings page shows them: the row table, glyph text per device, conflict
    /// detection, and saving the player's overrides between launches (PlayerPrefs key cm.bindings, the Input System's own
    /// override JSON). Also tracks which device the player used last (ActiveDevice) for prompts and aim.
    /// </summary>
    public static class ControlBindings
    {
        public const string MapName = "Player";
        public const string KeyboardGroup = "KeyboardMouse";
        public const string GamepadGroup = "Gamepad";
        /// <summary>PlayerPrefs key of the override JSON (InputActionAsset.SaveBindingOverridesAsJson).</summary>
        public const string PrefsKey = "cm.bindings";
        /// <summary>controls-1: the binding name that marks a key of the left-hand (W A S D) position; also the Move composite's name.</summary>
        public const string WasdHand = "WASD";
        /// <summary>controls-1: the binding name that marks a key of the right-hand (arrow keys) position; also the Move composite's name.</summary>
        public const string ArrowsHand = "Arrows";

        const ControlDevice K = ControlDevice.KeyboardMouse;
        const ControlDevice G = ControlDevice.Gamepad;
        static readonly RebindSlot[] None = new RebindSlot[0];

        /// <summary>Every row of the Controls tab, in screen order.</summary>
        /// <para>controls-1: every action is playable from the keyboard alone, from both hand positions (left hand on W A S D
        /// with the right on J K L, or right hand on the arrows with the left on Z X C V). Keyboard slots name their column;
        /// the binding order inside each action never moved, so ordinal 0 is still the slot the earlier tabs showed.</para>
        public static readonly ControlRow[] Rows =
        {
            new ControlRow("Move up", new[] { new RebindSlot("Move", K, 0, "WASD", "up"), new RebindSlot("Move", K, 0, "Arrows", "up") }, None),
            new ControlRow("Move down", new[] { new RebindSlot("Move", K, 0, "WASD", "down"), new RebindSlot("Move", K, 0, "Arrows", "down") }, None),
            new ControlRow("Move left", new[] { new RebindSlot("Move", K, 0, "WASD", "left"), new RebindSlot("Move", K, 0, "Arrows", "left") }, None),
            new ControlRow("Move right", new[] { new RebindSlot("Move", K, 0, "WASD", "right"), new RebindSlot("Move", K, 0, "Arrows", "right") }, None),
            new ControlRow("Move (stick)", None, new[] { new RebindSlot("Move", G) }),
            // Integration batch 2: a second default (hold L SHIFT) for trackpads and two-button mice with no side button.
            // controls-1: walk to pointer belongs to the mouse scheme only (L SHIFT is the keyboard scheme's lock facing).
            new ControlRow("Walk to pointer", new[] { new RebindSlot("MoveToPointer", K, 0, column: SlotColumn.Mouse), new RebindSlot("MoveToPointer", K, 1, column: SlotColumn.Wasd) }, None),
            new ControlRow("Aim", new[] { new RebindSlot("Point", K, 0, rebindable: false, column: SlotColumn.Mouse) }, new[] { new RebindSlot("Aim", G) }),
            // controls-1: keyboard aim = the fighter faces where it moves; hold this to keep the facing and strafe.
            new ControlRow("Lock facing (hold)", new[] { new RebindSlot("LockFacing", K, 0, column: SlotColumn.Wasd), new RebindSlot("LockFacing", K, 1, column: SlotColumn.Arrows) }, None),
            new ControlRow("Attack", new[] { new RebindSlot("Attack", K, 0, column: SlotColumn.Mouse), new RebindSlot("Attack", K, 1, column: SlotColumn.Wasd), new RebindSlot("Attack", K, 2, column: SlotColumn.Arrows) },
                new[] { new RebindSlot("Attack", G) }),
            new ControlRow("Class ability", new[] { new RebindSlot("Ability", K, 0, column: SlotColumn.Mouse), new RebindSlot("Ability", K, 1, column: SlotColumn.Wasd), new RebindSlot("Ability", K, 2, column: SlotColumn.Arrows) },
                new[] { new RebindSlot("Ability", G), new RebindSlot("Ability", G, 1) }),
            new ControlRow("Jump", new[] { new RebindSlot("Jump", K, 0, column: SlotColumn.Wasd), new RebindSlot("Jump", K, 1, column: SlotColumn.Arrows) }, new[] { new RebindSlot("Jump", G) }),
            new ControlRow("Interact / carry", new[] { new RebindSlot("Interact", K, 0, column: SlotColumn.Mouse), new RebindSlot("Interact", K, 2, column: SlotColumn.Wasd), new RebindSlot("Interact", K, 1, column: SlotColumn.Arrows) }, new[] { new RebindSlot("Interact", G) }),
            new ControlRow("Dig / place", new[] { new RebindSlot("DigPlace", K, 0, column: SlotColumn.Mouse), new RebindSlot("DigPlace", K, 1, column: SlotColumn.Wasd) }, new[] { new RebindSlot("DigPlace", G) }),
            new ControlRow("Switch dig / place", new[] { new RebindSlot("SwitchDigPlace", K, 0, rebindable: false, column: SlotColumn.Mouse),
                    new RebindSlot("SwitchDigPlace", K, 1, column: SlotColumn.Wasd), new RebindSlot("SwitchDigPlace", K, 2, column: SlotColumn.Arrows) },
                new[] { new RebindSlot("SwitchDigPlace", G, 0, "Shoulders", "negative"), new RebindSlot("SwitchDigPlace", G, 0, "Shoulders", "positive") }),
            new ControlRow("Scoreboard", new[] { new RebindSlot("Scoreboard", K) }, new[] { new RebindSlot("Scoreboard", G) }),
            new ControlRow("Full map", new[] { new RebindSlot("Map", K) }, new[] { new RebindSlot("Map", G) }),
            new ControlRow("Ping wheel", new[] { new RebindSlot("Ping", K), new RebindSlot("Ping", K, 1, column: SlotColumn.Mouse) }, new[] { new RebindSlot("Ping", G) }),
            // feat/mechanics-depth-view: the base station panel (B, or press the right stick, inside your base ring) and its
            // confirm (ENTER; on a controller the panel takes A like every menu, and holds it so the player never jumps).
            new ControlRow("Base station", new[] { new RebindSlot("OpenStation", K) }, new[] { new RebindSlot("OpenStation", G) }),
            new ControlRow("Station: confirm", new[] { new RebindSlot("StationBuy", K, 0, column: SlotColumn.Arrows) }, None),
            // feat/new-mechanics-view: the emote wheel (T, or press the left stick), view only (EmoteWheel).
            new ControlRow("Emote wheel", new[] { new RebindSlot("Emote", K) }, new[] { new RebindSlot("Emote", G) }),
            new ControlRow("HUD cursor", new[] { new RebindSlot("HudCursor", K) }, new[] { new RebindSlot("HudCursor", G) }),
            new ControlRow("Minimap zoom", new[] { new RebindSlot("MinimapZoom", K) }, new[] { new RebindSlot("MinimapZoom", G) }),
            // feat/ground-camera: high view / ground view (F5, double-tap View); playing, it flips Settings, Game, CAMERA.
            new ControlRow("Camera view", new[] { new RebindSlot("CameraView", K) }, new[] { new RebindSlot("CameraView", G) }),
            new ControlRow("Pause menu", new[] { new RebindSlot("Pause", K) }, new[] { new RebindSlot("Pause", G) }),
        };

        /// <summary>Raised after any binding change made through this class (rebind, reset, load).</summary>
        public static event Action BindingsChanged;

        public static string Group(ControlDevice d) => d == ControlDevice.Gamepad ? GamepadGroup : KeyboardGroup;

        static bool InGroup(InputBinding b, string group)
        {
            if (string.IsNullOrEmpty(b.groups)) return false;
            string[] gs = b.groups.Split(InputBinding.Separator);
            for (int i = 0; i < gs.Length; i++)
                if (gs[i] == group) return true;
            return false;
        }

        /// <summary>The binding index of a slot in its action, or -1.</summary>
        public static int FindBindingIndex(InputAction action, RebindSlot slot)
        {
            if (action == null) return -1;
            var bs = action.bindings;
            string group = Group(slot.Device);
            if (slot.Composite != null)
            {
                for (int i = 0; i < bs.Count; i++)
                {
                    if (!bs[i].isComposite || bs[i].name != slot.Composite) continue;
                    for (int j = i + 1; j < bs.Count && bs[j].isPartOfComposite; j++)
                        if (string.Equals(bs[j].name, slot.Part, StringComparison.OrdinalIgnoreCase)) return j;
                    return -1;
                }
                return -1;
            }
            int n = 0;
            for (int i = 0; i < bs.Count; i++)
            {
                if (bs[i].isComposite || bs[i].isPartOfComposite || !InGroup(bs[i], group)) continue;
                if (n++ == slot.Ordinal) return i;
            }
            return -1;
        }

        public static InputActionMap Map(InputActionAsset asset) => asset != null ? asset.FindActionMap(MapName, false) : null;

        /// <summary>Short text for a control path, Xbox names on a controller (A, B, X, Y, LB, RT, VIEW, MENU), plain words for
        /// mouse buttons (LEFT CLICK, RIGHT CLICK, MIDDLE CLICK, SCROLL). Drawn prompts: InputGlyphs.</summary>
        public static string DisplayPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "-";
            string p = path;
            int slash = p.IndexOf('/');
            string device = slash > 0 ? p.Substring(0, slash) : p;
            string control = slash > 0 ? p.Substring(slash + 1) : string.Empty;
            bool pad = device.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       device.IndexOf("XInput", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       device.IndexOf("DualShock", StringComparison.OrdinalIgnoreCase) >= 0;
            if (pad && ControllerSettings.CurrentFamily == PadFamily.PlayStation)
            {
                // controls-1: the shape family's names in text cells (the drawn glyph shows the shape itself).
                switch (control)
                {
                    case "buttonSouth": return "CROSS";
                    case "buttonEast": return "CIRCLE";
                    case "buttonWest": return "SQUARE";
                    case "buttonNorth": return "TRIANGLE";
                    case "leftShoulder": return "L1";
                    case "rightShoulder": return "R1";
                    case "leftTrigger": return "L2";
                    case "rightTrigger": return "R2";
                    case "start": return "OPTIONS";
                    case "select": return "CREATE";
                    case "leftStickPress": return "L3";
                    case "rightStickPress": return "R3";
                }
            }
            if (pad)
            {
                switch (control)
                {
                    case "buttonSouth": return "A";
                    case "buttonEast": return "B";
                    case "buttonWest": return "X";
                    case "buttonNorth": return "Y";
                    case "leftShoulder": return "LB";
                    case "rightShoulder": return "RB";
                    case "leftTrigger": return "LT";
                    case "rightTrigger": return "RT";
                    case "start": return "MENU";
                    case "select": return "VIEW";
                    case "leftStick": return "L STICK";
                    case "rightStick": return "R STICK";
                    case "leftStickPress": return "LS";
                    case "rightStickPress": return "RS";
                    case "dpad": return "D-PAD";
                    case "dpad/up": return "D-PAD UP";
                    case "dpad/down": return "D-PAD DOWN";
                    case "dpad/left": return "D-PAD LEFT";
                    case "dpad/right": return "D-PAD RIGHT";
                }
            }
            if (device == "<Mouse>")
            {
                switch (control)
                {
                    // Plain words, never the LMB / RMB / MMB abbreviations (owner, 2026-11-12); glyphs: InputGlyphs.
                    case "leftButton": return "LEFT CLICK";
                    case "rightButton": return "RIGHT CLICK";
                    case "middleButton": return "MIDDLE CLICK";
                    // The thumb button nearest the palm is "back" (Windows XButton1, "Mouse 4" in most games).
                    case "backButton": return "MOUSE 4";
                    case "forwardButton": return "MOUSE 5";
                    case "scroll/y":
                    case "scroll": return "SCROLL";
                    case "position": return "MOUSE";
                }
            }
            if (device == "<Keyboard>")
            {
                switch (control)
                {
                    case "upArrow": return "UP";
                    case "downArrow": return "DOWN";
                    case "leftArrow": return "LEFT";
                    case "rightArrow": return "RIGHT";
                    case "escape": return "ESC";
                    case "leftShift": return "L SHIFT";
                    case "rightShift": return "R SHIFT";
                    case "leftCtrl": return "L CTRL";
                    case "rightCtrl": return "R CTRL";
                    case "leftAlt": return "L ALT";
                    case "rightAlt": return "R ALT";
                    case "backquote": return "`";
                }
            }
            string s = InputControlPath.ToHumanReadableString(p, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return string.IsNullOrEmpty(s) ? "-" : s.ToUpperInvariant();
        }

        /// <summary>Glyph text of one binding (its effective path, so overrides show).</summary>
        public static string Display(InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return "-";
            InputBinding b = action.bindings[bindingIndex];
            string text = DisplayPath(b.effectivePath);
            return ControlSchemes.IsDoubleTap(b) ? "DOUBLE-TAP " + text : text;
        }

        /// <summary>The prompt text of an action on a device: its first binding in that device's group (HUD prompts).</summary>
        public static string PromptLabel(InputAction action, ControlDevice device)
        {
            if (action == null) return "-";
            int i = PromptBindingIndex(action, device);
            return i < 0 ? "-" : Display(action, i);
        }

        /// <summary>
        /// controls-1: the binding a prompt names for an action on a device. Controller: its first binding. Keyboard group:
        /// with the mouse in use (ActiveScheme KeyboardMouse) the first binding, as before (LEFT CLICK); keyboard only, the
        /// key of the hand position in use (a binding named after Hand), else a shared key (no name), else any key, so a
        /// keyboard player is never told to click. -1 = none.
        /// </summary>
        public static int PromptBindingIndex(InputAction action, ControlDevice device)
        {
            if (action == null) return -1;
            if (device == ControlDevice.Gamepad) return FindBindingIndex(action, new RebindSlot(action.name, device));
            var bs = action.bindings;
            if (!KeyboardOnly)
            {
                int first = FindBindingIndex(action, new RebindSlot(action.name, device));
                // Left-handed preset: the mouse is in the left hand, so a key prompt names the key by the arrows (right hand).
                if (!ControlPresets.LeftHandedActive || first < 0 || !(bs[first].effectivePath ?? "").StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase)) return first;
                for (int i = 0; i < bs.Count; i++)
                    if (!bs[i].isComposite && !bs[i].isPartOfComposite && InGroup(bs[i], KeyboardGroup) && string.Equals(bs[i].name, ArrowsHand, StringComparison.Ordinal) &&
                        (bs[i].effectivePath ?? "").StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase)) return i;
                return first;
            }
            string hand = HandName(Hand);
            int best = -1, bestScore = -1;
            for (int i = 0; i < bs.Count; i++)
            {
                if (bs[i].isComposite || bs[i].isPartOfComposite || !InGroup(bs[i], KeyboardGroup)) continue;
                string path = bs[i].effectivePath ?? string.Empty;
                bool key = path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase);
                int score = !key ? 0 : string.Equals(bs[i].name, hand, StringComparison.Ordinal) ? 3 : string.IsNullOrEmpty(bs[i].name) ? 2 : 1;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The binding name of a hand position (WASD, Arrows).</summary>
        public static string HandName(KeyboardHand h) => h == KeyboardHand.Arrows ? ArrowsHand : WasdHand;

        /// <summary>
        /// The hand position a control belongs to in an action: the Move composite it is part of, or the plain binding's
        /// name. Null when the control is not bound there or the binding names no hand (shared keys such as TAB, ESC, F).
        /// </summary>
        public static KeyboardHand? HandOf(InputAction action, InputControl control)
        {
            if (action == null || control == null || !(control.device is Keyboard)) return null;
            int i = action.GetBindingIndexForControl(control);
            if (i < 0) return null;
            var bs = action.bindings;
            string name = bs[i].name;
            if (bs[i].isPartOfComposite)
            {
                int h = i;
                while (h > 0 && bs[h].isPartOfComposite) h--;
                name = bs[h].name;
            }
            if (string.Equals(name, ArrowsHand, StringComparison.Ordinal)) return KeyboardHand.Arrows;
            if (string.Equals(name, WasdHand, StringComparison.Ordinal)) return KeyboardHand.Wasd;
            return null;
        }

        /// <summary>
        /// Other bindings of the Player map in the same device group whose effective path equals binding bindingIndex of
        /// action (composite heads skipped; the binding itself excluded). Empty = no conflict.
        /// </summary>
        public static List<string> Conflicts(InputAction action, int bindingIndex)
        {
            var result = new List<string>();
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return result;
            InputBinding me = action.bindings[bindingIndex];
            string path = me.effectivePath;
            if (string.IsNullOrEmpty(path)) return result;
            InputActionMap map = action.actionMap;
            if (map == null) return result;
            string[] myGroups = string.IsNullOrEmpty(me.groups) ? new string[0] : me.groups.Split(InputBinding.Separator);
            foreach (InputAction other in map.actions)
            {
                var bs = other.bindings;
                for (int i = 0; i < bs.Count; i++)
                {
                    if (other == action && i == bindingIndex) continue;
                    if (bs[i].isComposite) continue;
                    if (!string.Equals(bs[i].effectivePath, path, StringComparison.OrdinalIgnoreCase)) continue;
                    // feat/ground-camera: a double tap and a press or hold of one button are different gestures.
                    if (ControlSchemes.IsDoubleTap(bs[i]) != ControlSchemes.IsDoubleTap(me)) continue;
                    // controls-1: a pair that never shares a scheme (lock facing is keyboard-only play, walk to pointer
                    // needs the mouse) may share a key; HumanInputSource lets exactly one of them act (ControlSchemes).
                    if (ControlSchemes.SchemeExclusive(action.name, other.name)) continue;
                    bool shared = false;
                    for (int g = 0; g < myGroups.Length && !shared; g++) shared = InGroup(bs[i], myGroups[g]);
                    if (!shared) continue;
                    result.Add(ActionLabel(other.name, bs[i]));
                }
            }
            return result;
        }

        /// <summary>The row label a binding belongs to (for conflict messages).</summary>
        static string ActionLabel(string action, InputBinding b)
        {
            for (int r = 0; r < Rows.Length; r++)
            {
                ControlRow row = Rows[r];
                RebindSlot[][] sets = { row.Keyboard, row.Gamepad };
                for (int s = 0; s < 2; s++)
                    for (int k = 0; k < sets[s].Length; k++)
                    {
                        RebindSlot slot = sets[s][k];
                        if (slot.Action != action) continue;
                        if (slot.Part != null && !string.Equals(slot.Part, b.name, StringComparison.OrdinalIgnoreCase)) continue;
                        return row.Label;
                    }
            }
            return action;
        }

        /// <summary>The asset's overrides as JSON ("" when none).</summary>
        public static string SaveJson(InputActionAsset asset) => asset != null ? asset.SaveBindingOverridesAsJson() : string.Empty;

        /// <summary>Replaces every override of the asset with those in json (empty = defaults).</summary>
        public static void LoadJson(InputActionAsset asset, string json)
        {
            if (asset == null) return;
            asset.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(json)) asset.LoadBindingOverridesFromJson(json);
            BindingsChanged?.Invoke();
        }

        /// <summary>Writes the asset's overrides to PlayerPrefs (cm.bindings) and flushes them to disk.</summary>
        public static void Persist(InputActionAsset asset)
        {
            PlayerPrefs.SetString(PrefsKey, SaveJson(asset));
            PlayerPrefs.Save();
            BindingsChanged?.Invoke();
        }

        /// <summary>Applies the overrides saved by Persist (none saved = defaults).</summary>
        public static void Restore(InputActionAsset asset)
        {
            try
            {
                LoadJson(asset, PlayerPrefs.GetString(PrefsKey, string.Empty));
            }
            catch (Exception e)
            {
                // DG-53: a corrupt or foreign cm.bindings must never stop the game: drop it once and keep the defaults.
                if (asset != null) asset.RemoveAllBindingOverrides();
                PlayerPrefs.DeleteKey(PrefsKey);
                Debug.LogWarning($"[CM.Controls] saved bindings could not be read ({e.GetType().Name}); using the defaults");
                BindingsChanged?.Invoke();
            }
        }

        /// <summary>Every binding back to Controls.inputactions and the saved overrides deleted.</summary>
        public static void ResetAll(InputActionAsset asset)
        {
            if (asset != null) asset.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            ControlPresets.Forget();
            PlayerPrefs.Save();
            BindingsChanged?.Invoke();
        }

        /// <summary>Notify listeners after an interactive rebind changed a binding.</summary>
        public static void NotifyChanged() => BindingsChanged?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LoadSavedOnBoot()
        {
            // Domain reload is off in Play Mode: reset the device state, then apply the player's saved bindings.
            _active = ControlDevice.KeyboardMouse;
            _sampledFrame = -1;
            _keyboardOnly = false;
            _hand = KeyboardHand.Wasd;
            _lastMouseUse = float.NegativeInfinity;
            if (Application.isBatchMode) return;   // tests and gates run on the defaults
            InputActionAsset actions = InputSystem.actions;
            if (actions != null) Restore(actions);
            ControlPresets.RestoreOnBoot();
        }

        // ---- the device used last ----

        static ControlDevice _active = ControlDevice.KeyboardMouse;
        static int _sampledFrame = -1;

        /// <summary>The device the player used last (keyboard and mouse until a controller is touched).</summary>
        public static ControlDevice ActiveDevice => _active;

        /// <summary>Raised when ActiveDevice changes (controls-1: also when KeyboardOnly or Hand changes, so every prompt redraws).</summary>
        public static event Action<ControlDevice> ActiveDeviceChanged;

        // ---- controls-1: keyboard only versus keyboard and mouse, and the hand position ----

        static bool _keyboardOnly;
        static KeyboardHand _hand = KeyboardHand.Wasd;
        static float _lastMouseUse = float.NegativeInfinity;

        /// <summary>Seconds the mouse must rest before a keyboard attack key hands aim and prompts back to the keyboard.</summary>
        public const float MouseIdleSeconds = 0.5f;

        /// <summary>
        /// The keyboard player is playing without the mouse: true from boot until the mouse moves or clicks, and again when a
        /// keyboard attack or ability key is pressed with the mouse resting MouseIdleSeconds (HumanInputSource). Prompts then
        /// name keys, never clicks, and the fighter faces where it moves.
        /// </summary>
        public static bool KeyboardOnly => _keyboardOnly;

        /// <summary>The hand position used last (the Move composite or the named key pressed last).</summary>
        public static KeyboardHand Hand => _hand;

        /// <summary>The scheme in use now: the controller, keyboard only, or keyboard and mouse.</summary>
        public static ControlScheme ActiveScheme =>
            _active == ControlDevice.Gamepad ? ControlScheme.Gamepad : _keyboardOnly ? ControlScheme.Keyboard : ControlScheme.KeyboardMouse;

        /// <summary>Unscaled seconds since the mouse last moved or clicked (infinity before it ever did).</summary>
        public static float MouseIdleFor => Time.unscaledTime - _lastMouseUse;

        /// <summary>
        /// controls-1: keyboard-only steering for the ping and emote wheels: the Move keys (W A S D or arrows) as a
        /// direction scaled to radius; zero unless the keyboard plays alone (mouse and stick steer as before).
        /// </summary>
        public static Vector2 KeyboardSteer(float radius)
        {
            if (!KeyboardOnly || _active != ControlDevice.KeyboardMouse) return Vector2.zero;
            InputActionMap map = Map(InputSystem.actions);
            InputAction move = map != null ? map.FindAction("Move", false) : null;
            return move != null ? move.ReadValue<Vector2>() * radius : Vector2.zero;
        }

        /// <summary>A new match: no mouse use seen yet (the menu clicks before it do not count).</summary>
        public static void ForgetMouseUse() => _lastMouseUse = float.NegativeInfinity;

        /// <summary>A mouse button drove an action (its click may arrive before Sample sees it): the mouse is in use.</summary>
        public static void NoteMouseUsed()
        {
            _lastMouseUse = Time.unscaledTime;
            SetKeyboardOnly(false);
        }

        /// <summary>Sets keyboard-only play (tests, HumanInputSource); prompts redraw on a change.</summary>
        public static void SetKeyboardOnly(bool on)
        {
            if (_keyboardOnly == on) return;
            _keyboardOnly = on;
            ActiveDeviceChanged?.Invoke(_active);
        }

        /// <summary>Sets the hand position (tests, HumanInputSource); prompts redraw on a change.</summary>
        public static void SetHand(KeyboardHand h)
        {
            if (_hand == h) return;
            _hand = h;
            if (_active == ControlDevice.KeyboardMouse && _keyboardOnly) ActiveDeviceChanged?.Invoke(_active);
        }

        /// <summary>Sets the active device (tests, and Sample).</summary>
        public static void SetActiveDevice(ControlDevice d)
        {
            if (_active == d) return;
            _active = d;
            ActiveDeviceChanged?.Invoke(d);
        }

        /// <summary>Stick deflection (after the binding dead zones) that counts as "the controller was used".</summary>
        public const float StickUseThreshold = 0.35f;

        /// <summary>Polls the current devices once per frame and updates ActiveDevice.</summary>
        public static void Sample()
        {
            int f = Time.frameCount;
            if (f == _sampledFrame) return;
            _sampledFrame = f;
            Gamepad pad = Gamepad.current;
            if (pad != null && GamepadUsed(pad))
            {
                SetActiveDevice(ControlDevice.Gamepad);
                return;
            }
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool mouseUsed = mouse != null && MouseUsed(mouse);
            if (mouseUsed)
            {
                // controls-1: a real mouse move or click ends keyboard-only play (prompts show clicks, the mouse aims).
                _lastMouseUse = Time.unscaledTime;
                SetKeyboardOnly(false);
            }
            if ((kb != null && kb.anyKey.wasPressedThisFrame) || mouseUsed)
                SetActiveDevice(ControlDevice.KeyboardMouse);
        }

        /// <summary>The mouse moved more than 2 px or a button went down this frame.</summary>
        public static bool MouseUsed(Mouse mouse) =>
            mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame ||
            mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame ||
            mouse.backButton.wasPressedThisFrame || mouse.forwardButton.wasPressedThisFrame;

        /// <summary>A stick is pushed past StickUseThreshold or any button or trigger went down this frame.</summary>
        public static bool GamepadUsed(Gamepad pad)
        {
            if (pad.leftStick.ReadValue().sqrMagnitude > StickUseThreshold * StickUseThreshold) return true;
            if (pad.rightStick.ReadValue().sqrMagnitude > StickUseThreshold * StickUseThreshold) return true;
            var all = pad.allControls;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is ButtonControl b && !b.synthetic && b.wasPressedThisFrame) return true;
            return false;
        }
    }
}

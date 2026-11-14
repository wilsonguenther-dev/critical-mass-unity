using System;
using CriticalMass.Runtime.Hud.Style;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Input
{
    /// <summary>The drawn shape of an input prompt (InputGlyphGraphic draws each one in code).</summary>
    public enum GlyphKind
    {
        None,
        /// <summary>A keyboard keycap with the key's name on it (arrows draw an arrow instead of a name).</summary>
        Key,
        /// <summary>A mouse with one part highlighted (left, right, middle, wheel) or none (move / aim).</summary>
        Mouse,
        /// <summary>A round controller face button in its colour with its letter (A, B, X, Y).</summary>
        FaceButton,
        /// <summary>A shoulder bumper: a wide rounded tag (LB, RB).</summary>
        Bumper,
        /// <summary>A trigger: a tall tag rounded on top (LT, RT).</summary>
        Trigger,
        /// <summary>A thumbstick seen from above (L, R); Mark Press adds the press ring.</summary>
        Stick,
        /// <summary>The directional pad, one arm highlighted (or none).</summary>
        DPad,
        /// <summary>The small centre buttons of a controller (Menu, View).</summary>
        SmallButton,
    }

    /// <summary>Which part of a glyph is highlighted.</summary>
    public enum GlyphMark { None, Left, Right, Middle, Wheel, Up, Down, Press, Move }

    /// <summary>
    /// One input prompt: what to draw (Kind, Mark, Cap on the glyph, Face colour) and the plain words for it ("Left click",
    /// "Space", "A button"). Never an abbreviation of a mouse button.
    /// </summary>
    public readonly struct GlyphSpec : IEquatable<GlyphSpec>
    {
        public readonly GlyphKind Kind;
        public readonly GlyphMark Mark;
        /// <summary>Text printed on the glyph ("E", "Space", "A", "LB"); null for drawn-only glyphs (mouse, arrows, d-pad).</summary>
        public readonly string Cap;
        /// <summary>Plain words for the input, for text lines and next to the glyph where there is room.</summary>
        public readonly string Words;
        /// <summary>The glyph's face colour (controller face buttons in their own colour; keys and the rest Warm Cream).</summary>
        public readonly Color Face;

        public GlyphSpec(GlyphKind kind, GlyphMark mark, string cap, string words, Color face)
        {
            Kind = kind;
            Mark = mark;
            Cap = cap;
            Words = words;
            Face = face;
        }

        public bool IsEmpty => Kind == GlyphKind.None;

        public static readonly GlyphSpec Empty = new GlyphSpec(GlyphKind.None, GlyphMark.None, null, "-", Color.clear);

        public bool Equals(GlyphSpec o) => Kind == o.Kind && Mark == o.Mark && Cap == o.Cap && Words == o.Words && Face == o.Face;
        public override bool Equals(object obj) => obj is GlyphSpec o && Equals(o);
        public override int GetHashCode() => ((int)Kind * 31 + (int)Mark) * 31 + (Cap != null ? Cap.GetHashCode() : 0);
    }

    /// <summary>
    /// The one place that turns an input into a readable prompt (owner, 2026-11-12: "I don't know what you mean by these
    /// control buttons, MMB LMB RMB"). Given a Player action and a device (default: the device used last,
    /// ControlBindings.ActiveDevice) it returns a GlyphSpec for the action's CURRENT binding (rebinds show at once: every
    /// call reads the effective path through ControlBindings.FindBindingIndex) with the plain words for it. Mouse buttons
    /// are drawn as a mouse with the button highlighted and named "Left click", "Right click", "Middle click", "Scroll";
    /// keys are keycaps with the key's name; controller buttons are coloured face buttons, shaped bumper and trigger tags,
    /// stick and d-pad icons. Drawing is InputGlyphView / InputGlyphGraphic (Hud/Ui); text-only lines use Words.
    /// Pure lookups, no Unity objects: call on a device or binding change, not every frame (strings are built per call).
    /// </summary>
    public static class InputGlyphs
    {
        /// <summary>Every action of the Player map a prompt can name (tests check each maps to a glyph and words).</summary>
        public static readonly string[] PlayerActions =
        {
            "Move", "Aim", "Point", "Attack", "Ability", "Jump", "Interact", "DigPlace", "SwitchDigPlace", "Scoreboard", "Pause",
            "Map", "Ping", "HudCursor", "MinimapZoom", "LockFacing", "ShowTerrain",
        };

        /// <summary>Abbreviations the owner could not read; no user-facing string may contain them as a word.</summary>
        public static readonly string[] Banned = { "LMB", "RMB", "MMB" };

        // Controller face colours (the standard layout's colours, so a player finds the button by colour).
        public static readonly Color FaceA = new Color32(0x6C, 0xC2, 0x4A, 0xFF);
        public static readonly Color FaceB = new Color32(0xE2, 0x57, 0x4C, 0xFF);
        public static readonly Color FaceX = new Color32(0x3E, 0x8E, 0xDE, 0xFF);
        public static readonly Color FaceY = new Color32(0xF4, 0xC4, 0x30, 0xFF);
        static Color Cream => CMTokens.WarmCream.Color;
        static Color Stone => CMTokens.Stone.Color;

        // ---------------------------------------------------------------- actions

        /// <summary>The prompt for an action on the device used last.</summary>
        public static GlyphSpec ForAction(string action) => ForAction(action, ControlBindings.ActiveDevice);

        /// <summary>
        /// The prompt for an action's current binding on a device (ordinal: the n-th plain binding in the device's group;
        /// controls-1: the default -1 = the binding a prompt should name, ControlBindings.PromptBindingIndex, so a keyboard
        /// player sees J or Z for Attack, never Left click).
        /// Move on the keyboard is one wide keycap with the four bound keys ("WASD"); with no plain binding the first
        /// composite part of the device's group is used (Shoulders: LB). Unknown action or no binding: Empty.
        /// </summary>
        public static GlyphSpec ForAction(string action, ControlDevice device, int ordinal = -1)
        {
            if (string.IsNullOrEmpty(action)) return GlyphSpec.Empty;
            if (action == "Move" && device == ControlDevice.KeyboardMouse) return MoveKeys();
            // controls-1: keyboard only, the aim is the move direction (the move keys), never the mouse.
            if ((action == "Aim" || action == "Point") && device == ControlDevice.KeyboardMouse && ordinal < 0 && ControlBindings.KeyboardOnly)
                return MoveKeys();
            if (action == "Point" && device == ControlDevice.Gamepad) action = "Aim";
            if (action == "Aim" && device == ControlDevice.KeyboardMouse) action = "Point";
            InputAction a = Find(action);
            string path = a != null ? PathFor(a, device, ordinal) : null;
            if (path == null) path = DefaultPath(action, device);
            if (path == null) return GlyphSpec.Empty;
            GlyphSpec g = ForPath(path);
            if (action == "Point" && g.Kind == GlyphKind.Mouse) return new GlyphSpec(GlyphKind.Mouse, GlyphMark.Move, null, "Mouse", Cream);
            return g;
        }

        /// <summary>The plain words for an action on a device ("Left click", "Space", "Right trigger").</summary>
        public static string Words(string action, ControlDevice device) => ForAction(action, device).Words;

        /// <summary>The plain words for an action on the device used last.</summary>
        public static string Words(string action) => Words(action, ControlBindings.ActiveDevice);

        /// <summary>Move as separate glyphs: the four bound keys (up, left, down, right) or the stick; returns the count.</summary>
        public static int MoveGlyphs(ControlDevice device, GlyphSpec[] output)
        {
            if (output == null || output.Length == 0) return 0;
            if (device == ControlDevice.Gamepad)
            {
                output[0] = ForAction("Move", ControlDevice.Gamepad);
                if (output[0].IsEmpty) output[0] = ForPath("<Gamepad>/leftStick");
                return 1;
            }
            InputAction a = Find("Move");
            int n = 0;
            for (int k = 0; k < MoveParts.Length && n < output.Length; k++)
            {
                string p = a != null ? PartPath(a, ControlBindings.HandName(ControlBindings.Hand), MoveParts[k]) : null;
                output[n++] = ForPath(p ?? (ControlBindings.Hand == KeyboardHand.Arrows ? ArrowDefaults[k] : MoveDefaults[k]));
            }
            return n;
        }

        /// <summary>"W A S D or the arrow keys" / "Left stick", from the current bindings.</summary>
        public static string MoveWords(ControlDevice device)
        {
            if (device == ControlDevice.Gamepad) return ForAction("Move", ControlDevice.Gamepad).Words;
            return MoveKeys(KeyboardHand.Wasd).Words + " or the arrow keys";
        }

        static readonly string[] MoveParts = { "up", "left", "down", "right" };
        static readonly string[] MoveDefaults = { "<Keyboard>/w", "<Keyboard>/a", "<Keyboard>/s", "<Keyboard>/d" };

        static readonly string[] ArrowDefaults = { "<Keyboard>/upArrow", "<Keyboard>/leftArrow", "<Keyboard>/downArrow", "<Keyboard>/rightArrow" };

        static GlyphSpec MoveKeys() => MoveKeys(ControlBindings.Hand);

        /// <summary>Move as one wide keycap for a hand position: "WASD", or the arrows ("Arrow keys").</summary>
        static GlyphSpec MoveKeys(KeyboardHand hand)
        {
            InputAction a = Find("Move");
            if (hand == KeyboardHand.Arrows)
            {
                bool defaults = true;
                for (int k = 0; k < MoveParts.Length && a != null; k++)
                    if (!string.Equals(PartPath(a, ControlBindings.ArrowsHand, MoveParts[k]), ArrowDefaults[k], StringComparison.OrdinalIgnoreCase)) defaults = false;
                if (defaults) return new GlyphSpec(GlyphKind.Key, GlyphMark.None, "ARROWS", "Arrow keys", Cream);
            }
            string comp = ControlBindings.HandName(hand);
            string[] fallback = hand == KeyboardHand.Arrows ? ArrowDefaults : MoveDefaults;
            var caps = new string[MoveParts.Length];
            bool single = true;
            for (int k = 0; k < MoveParts.Length; k++)
            {
                string p = a != null ? PartPath(a, comp, MoveParts[k]) : null;
                GlyphSpec g = ForPath(p ?? fallback[k]);
                caps[k] = g.Cap ?? g.Words;
                if (caps[k].Length > 1) single = false;
            }
            string cap = single ? string.Concat(caps) : string.Join(" ", caps);
            return new GlyphSpec(GlyphKind.Key, GlyphMark.None, cap, string.Join(" ", caps), Cream);
        }

        static InputAction Find(string action)
        {
            InputActionAsset asset = InputSystem.actions;
            InputActionMap map = ControlBindings.Map(asset);
            return map != null ? map.FindAction(action, false) : null;
        }

        /// <summary>The effective path of an action's binding for a device: the n-th plain binding, else the first composite part.</summary>
        public static string PathFor(InputAction action, ControlDevice device, int ordinal = 0)
        {
            if (action == null) return null;
            int i = ordinal < 0 ? ControlBindings.PromptBindingIndex(action, device)
                : ControlBindings.FindBindingIndex(action, new RebindSlot(action.name, device, ordinal));
            if (i >= 0) return action.bindings[i].effectivePath;
            string group = ControlBindings.Group(device);
            var bs = action.bindings;
            for (int k = 0; k < bs.Count; k++)
            {
                if (!bs[k].isPartOfComposite || string.IsNullOrEmpty(bs[k].groups)) continue;
                if (Array.IndexOf(bs[k].groups.Split(InputBinding.Separator), group) >= 0) return bs[k].effectivePath;
            }
            return null;
        }

        static string PartPath(InputAction a, string composite, string part)
        {
            int i = ControlBindings.FindBindingIndex(a, new RebindSlot(a.name, ControlDevice.KeyboardMouse, 0, composite, part));
            return i >= 0 ? a.bindings[i].effectivePath : null;
        }

        /// <summary>Controls.inputactions defaults, for prompts when the action asset is missing (tests, tools).</summary>
        static string DefaultPath(string action, ControlDevice device)
        {
            bool pad = device == ControlDevice.Gamepad;
            switch (action)
            {
                case "Move": return pad ? "<Gamepad>/leftStick" : null;
                case "Aim": return "<Gamepad>/rightStick";
                case "Point": return "<Mouse>/position";
                case "Attack": return pad ? "<Gamepad>/rightTrigger" : "<Mouse>/leftButton";
                case "Ability": return pad ? "<Gamepad>/leftTrigger" : "<Mouse>/rightButton";
                case "Jump": return pad ? "<Gamepad>/buttonSouth" : "<Keyboard>/space";
                case "Interact": return pad ? "<Gamepad>/buttonWest" : "<Keyboard>/e";
                case "DigPlace": return pad ? "<Gamepad>/buttonNorth" : "<Keyboard>/f";
                case "SwitchDigPlace": return pad ? "<Gamepad>/leftShoulder" : "<Mouse>/scroll/y";
                case "Scoreboard": return pad ? "<Gamepad>/select" : "<Keyboard>/tab";
                case "Pause": return pad ? "<Gamepad>/start" : "<Keyboard>/escape";
                case "Map": return pad ? "<Gamepad>/dpad/up" : "<Keyboard>/m";
                case "Ping": return pad ? "<Gamepad>/dpad/left" : "<Keyboard>/g";
                case "HudCursor": return pad ? "<Gamepad>/dpad/down" : "<Keyboard>/leftAlt";
                case "MinimapZoom": return pad ? "<Gamepad>/dpad/right" : "<Keyboard>/n";
                case "LockFacing": return pad ? "<Gamepad>/rightStick" : "<Keyboard>/l";
                case "ShowTerrain": return pad ? "<Gamepad>/rightStickPress" : "<Keyboard>/q";
            }
            return null;
        }

        // ---------------------------------------------------------------- paths

        /// <summary>The prompt for a control path ("&lt;Mouse&gt;/leftButton", "&lt;Keyboard&gt;/space", "&lt;Gamepad&gt;/buttonSouth").</summary>
        public static GlyphSpec ForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return GlyphSpec.Empty;
            int slash = path.IndexOf('/');
            string device = slash > 0 ? path.Substring(0, slash) : path;
            string control = slash > 0 ? path.Substring(slash + 1) : string.Empty;
            if (IsPad(device)) return Pad(control, path);
            if (device.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                device.IndexOf("Pointer", StringComparison.OrdinalIgnoreCase) >= 0) return Mouse(control);
            return KeyFor(control, path);
        }

        public static bool IsPad(string device) =>
            device.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0 ||
            device.IndexOf("XInput", StringComparison.OrdinalIgnoreCase) >= 0 ||
            device.IndexOf("DualShock", StringComparison.OrdinalIgnoreCase) >= 0 ||
            device.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0 ||
            device.IndexOf("Joystick", StringComparison.OrdinalIgnoreCase) >= 0;

        static GlyphSpec Mouse(string control)
        {
            switch (control)
            {
                case "leftButton":
                case "press": return new GlyphSpec(GlyphKind.Mouse, GlyphMark.Left, null, "Left click", Cream);
                case "rightButton": return new GlyphSpec(GlyphKind.Mouse, GlyphMark.Right, null, "Right click", Cream);
                case "middleButton": return new GlyphSpec(GlyphKind.Mouse, GlyphMark.Middle, null, "Middle click", Cream);
                case "scroll":
                case "scroll/y":
                case "scroll/up":
                case "scroll/down": return new GlyphSpec(GlyphKind.Mouse, GlyphMark.Wheel, null, "Scroll", Cream);
                case "forwardButton": return new GlyphSpec(GlyphKind.Mouse, GlyphMark.None, null, "Mouse side button (front)", Cream);
                case "backButton": return new GlyphSpec(GlyphKind.Mouse, GlyphMark.None, null, "Mouse side button (back)", Cream);
                default: return new GlyphSpec(GlyphKind.Mouse, GlyphMark.Move, null, "Mouse", Cream);
            }
        }

        static GlyphSpec KeyFor(string control, string path)
        {
            switch (control)
            {
                case "upArrow": return new GlyphSpec(GlyphKind.Key, GlyphMark.Up, null, "Up arrow", Cream);
                case "downArrow": return new GlyphSpec(GlyphKind.Key, GlyphMark.Down, null, "Down arrow", Cream);
                case "leftArrow": return new GlyphSpec(GlyphKind.Key, GlyphMark.Left, null, "Left arrow", Cream);
                case "rightArrow": return new GlyphSpec(GlyphKind.Key, GlyphMark.Right, null, "Right arrow", Cream);
                case "space": return Key("Space", "Space");
                case "enter":
                case "numpadEnter": return Key("Enter", "Enter");
                case "escape": return Key("Esc", "Escape");
                case "tab": return Key("Tab", "Tab");
                case "backspace": return Key("Backspace", "Backspace");
                case "leftShift":
                case "rightShift":
                case "shift": return Key("Shift", "Shift");
                case "leftCtrl":
                case "rightCtrl":
                case "ctrl": return Key("Ctrl", "Ctrl");
                case "leftAlt":
                case "rightAlt":
                case "alt": return Key("Alt", "Alt");
                case "backquote": return Key("`", "Backquote");
                case "slash": return Key("/", "Slash");
                case "period": return Key(".", "Period");
                case "comma": return Key(",", "Comma");
                case "semicolon": return Key(";", "Semicolon");
                case "minus": return Key("-", "Minus");
                case "equals": return Key("=", "Equals");
                case "leftBracket": return Key("[", "Left bracket");
                case "rightBracket": return Key("]", "Right bracket");
                case "anyKey": return Key("Any key", "Any key");
            }
            if (control.Length == 1) return Key(control.ToUpperInvariant(), control.ToUpperInvariant());
            if (control.Length == 6 && control.StartsWith("digit", StringComparison.Ordinal)) return Key(control.Substring(5), control.Substring(5));
            string s = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (string.IsNullOrEmpty(s)) s = control;
            if (s.Length > 0) s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            return Key(s, s);
        }

        static GlyphSpec Key(string cap, string words) => new GlyphSpec(GlyphKind.Key, GlyphMark.None, cap, words, Cream);

        // controls-1: the shape family's face colours (cross blue, circle red, square pink, triangle green).
        public static readonly Color FaceCross = new Color32(0x7C, 0xB2, 0xE8, 0xFF);
        public static readonly Color FaceCircle = new Color32(0xE2, 0x57, 0x4C, 0xFF);
        public static readonly Color FaceSquare = new Color32(0xD8, 0x8C, 0xC8, 0xFF);
        public static readonly Color FaceTriangle = new Color32(0x4C, 0xC2, 0xA4, 0xFF);

        /// <summary>The glyph of a controller control for the connected family (ControllerSettings.CurrentFamily).</summary>
        static GlyphSpec Pad(string control, string path) => Pad(control, path, ControllerSettings.CurrentFamily);

        /// <summary>
        /// controls-1: the glyph of a controller control in a family: the shape family draws cross, circle, square and
        /// triangle (L1 R1 L2 R2, L3 R3, Options, Create), every other controller A B X Y (LB RB LT RT, Menu, View).
        /// Never a brand name: the family only changes labels and colours.
        /// </summary>
        public static GlyphSpec Pad(string control, string path, PadFamily family)
        {
            if (family == PadFamily.PlayStation)
            {
                switch (control)
                {
                    case "buttonSouth": return new GlyphSpec(GlyphKind.FaceButton, GlyphMark.None, "\u00D7", "Cross button", FaceCross);
                    case "buttonEast": return new GlyphSpec(GlyphKind.FaceButton, GlyphMark.None, "\u25CB", "Circle button", FaceCircle);
                    case "buttonWest": return new GlyphSpec(GlyphKind.FaceButton, GlyphMark.None, "\u25A1", "Square button", FaceSquare);
                    case "buttonNorth": return new GlyphSpec(GlyphKind.FaceButton, GlyphMark.None, "\u25B3", "Triangle button", FaceTriangle);
                    case "leftShoulder": return new GlyphSpec(GlyphKind.Bumper, GlyphMark.Left, "L1", "L1 button", Stone);
                    case "rightShoulder": return new GlyphSpec(GlyphKind.Bumper, GlyphMark.Right, "R1", "R1 button", Stone);
                    case "leftTrigger": return new GlyphSpec(GlyphKind.Trigger, GlyphMark.Left, "L2", "L2 trigger", Stone);
                    case "rightTrigger": return new GlyphSpec(GlyphKind.Trigger, GlyphMark.Right, "R2", "R2 trigger", Stone);
                    case "leftStickPress": return new GlyphSpec(GlyphKind.Stick, GlyphMark.Press, "L3", "Press the left stick (L3)", Stone);
                    case "rightStickPress": return new GlyphSpec(GlyphKind.Stick, GlyphMark.Press, "R3", "Press the right stick (R3)", Stone);
                    case "start": return new GlyphSpec(GlyphKind.SmallButton, GlyphMark.None, "Options", "Options button", Stone);
                    case "select": return new GlyphSpec(GlyphKind.SmallButton, GlyphMark.None, "Create", "Create or Share button", Stone);
                }
            }
            switch (control)
            {
                case "buttonSouth": return new GlyphSpec(GlyphKind.FaceButton, GlyphMark.None, "A", "A button", FaceA);
                case "buttonEast": return new GlyphSpec(GlyphKind.FaceButton, GlyphMark.None, "B", "B button", FaceB);
                case "buttonWest": return new GlyphSpec(GlyphKind.FaceButton, GlyphMark.None, "X", "X button", FaceX);
                case "buttonNorth": return new GlyphSpec(GlyphKind.FaceButton, GlyphMark.None, "Y", "Y button", FaceY);
                case "leftShoulder": return new GlyphSpec(GlyphKind.Bumper, GlyphMark.Left, "LB", "Left bumper", Stone);
                case "rightShoulder": return new GlyphSpec(GlyphKind.Bumper, GlyphMark.Right, "RB", "Right bumper", Stone);
                case "leftTrigger": return new GlyphSpec(GlyphKind.Trigger, GlyphMark.Left, "LT", "Left trigger", Stone);
                case "rightTrigger": return new GlyphSpec(GlyphKind.Trigger, GlyphMark.Right, "RT", "Right trigger", Stone);
                case "leftStick": return new GlyphSpec(GlyphKind.Stick, GlyphMark.Move, "L", "Left stick", Stone);
                case "rightStick": return new GlyphSpec(GlyphKind.Stick, GlyphMark.Move, "R", "Right stick", Stone);
                case "leftStickPress": return new GlyphSpec(GlyphKind.Stick, GlyphMark.Press, "L", "Press the left stick", Stone);
                case "rightStickPress": return new GlyphSpec(GlyphKind.Stick, GlyphMark.Press, "R", "Press the right stick", Stone);
                case "start": return new GlyphSpec(GlyphKind.SmallButton, GlyphMark.None, "Menu", "Menu button", Stone);
                case "select": return new GlyphSpec(GlyphKind.SmallButton, GlyphMark.None, "View", "View button", Stone);
                case "dpad": return new GlyphSpec(GlyphKind.DPad, GlyphMark.None, null, "D-pad", Stone);
                case "dpad/up": return new GlyphSpec(GlyphKind.DPad, GlyphMark.Up, null, "D-pad up", Stone);
                case "dpad/down": return new GlyphSpec(GlyphKind.DPad, GlyphMark.Down, null, "D-pad down", Stone);
                case "dpad/left": return new GlyphSpec(GlyphKind.DPad, GlyphMark.Left, null, "D-pad left", Stone);
                case "dpad/right": return new GlyphSpec(GlyphKind.DPad, GlyphMark.Right, null, "D-pad right", Stone);
            }
            string s = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (string.IsNullOrEmpty(s)) s = control;
            return new GlyphSpec(GlyphKind.SmallButton, GlyphMark.None, s, s, Stone);
        }

        // ---------------------------------------------------------------- hint tokens

        /// <summary>
        /// A key word of the menus' hint lines ("ENTER SELECT · ESC BACK", "A SELECT · B BACK", "LB / RB TABS",
        /// "LEFT / RIGHT CHANGE") as a glyph for the device the line was written for; Empty when the word is not a key.
        /// </summary>
        public static GlyphSpec ForToken(string token, ControlDevice device)
        {
            if (string.IsNullOrEmpty(token)) return GlyphSpec.Empty;
            bool pad = device == ControlDevice.Gamepad;
            switch (token.ToUpperInvariant())
            {
                case "ENTER": return ForPath("<Keyboard>/enter");
                case "ESC":
                case "ESCAPE": return ForPath("<Keyboard>/escape");
                case "TAB": return ForPath("<Keyboard>/tab");
                case "SPACE": return ForPath("<Keyboard>/space");
                case "BACKSPACE": return ForPath("<Keyboard>/backspace");
                case "LEFT": return ForPath(pad ? "<Gamepad>/dpad/left" : "<Keyboard>/leftArrow");
                case "RIGHT": return ForPath(pad ? "<Gamepad>/dpad/right" : "<Keyboard>/rightArrow");
                case "UP": return ForPath(pad ? "<Gamepad>/dpad/up" : "<Keyboard>/upArrow");
                case "DOWN": return ForPath(pad ? "<Gamepad>/dpad/down" : "<Keyboard>/downArrow");
                case "A": return pad ? ForPath("<Gamepad>/buttonSouth") : ForPath("<Keyboard>/a");
                case "B": return pad ? ForPath("<Gamepad>/buttonEast") : GlyphSpec.Empty;
                case "X": return pad ? ForPath("<Gamepad>/buttonWest") : GlyphSpec.Empty;
                case "Y": return pad ? ForPath("<Gamepad>/buttonNorth") : GlyphSpec.Empty;
                case "LB": return ForPath("<Gamepad>/leftShoulder");
                case "RB": return ForPath("<Gamepad>/rightShoulder");
                case "LT": return ForPath("<Gamepad>/leftTrigger");
                case "RT": return ForPath("<Gamepad>/rightTrigger");
                case "MENU":
                case "START": return ForPath("<Gamepad>/start");
                case "VIEW": return ForPath("<Gamepad>/select");
                case "CLICK": return ForPath("<Mouse>/leftButton");
            }
            return GlyphSpec.Empty;
        }

        // ---------------------------------------------------------------- checks

        /// <summary>True when s contains LMB, RMB or MMB as a word (any case).</summary>
        public static bool ContainsBannedAbbreviation(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int n = s.Length;
            int i = 0;
            while (i < n)
            {
                while (i < n && !char.IsLetter(s[i])) i++;
                int start = i;
                while (i < n && char.IsLetter(s[i])) i++;
                int len = i - start;
                if (len != 3) continue;
                for (int b = 0; b < Banned.Length; b++)
                    if (string.Compare(s, start, Banned[b], 0, 3, StringComparison.OrdinalIgnoreCase) == 0) return true;
            }
            return false;
        }
    }
}

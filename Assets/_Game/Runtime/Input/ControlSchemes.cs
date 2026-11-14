using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// controls-1 (owner, 2026-11-13: "You have built no keyboard keys to attack"): the three play schemes as data, and the
    /// proof that each one is complete. A scheme's bindings are read from the Player map (effective paths, so rebinds
    /// count):
    /// <list type="bullet">
    /// <item>Keyboard: every KeyboardMouse-group binding on the keyboard, minus the mouse-only actions.</item>
    /// <item>KeyboardMouse: every KeyboardMouse-group binding (keys and mouse), minus the keyboard-only actions.</item>
    /// <item>Gamepad: every Gamepad-group binding.</item>
    /// </list>
    /// An action is covered in a scheme when it has a binding there, or when Equivalent names what does its job in that
    /// scheme (keyboard aim is the move direction, a controller strafes with the right stick). Missing returns the
    /// uncovered actions; Collisions returns keys bound to two actions of one scheme. EditMode tests fail on either.
    /// Menus are not in the Player map: they use the EventSystem's default UI actions (arrows and W A S D or the stick
    /// navigate, Enter or A confirms, Escape or B goes back), listed in MenuActions for the bindings table.
    /// </summary>
    public static class ControlSchemes
    {
        /// <summary>Actions that need a pointer: meaningless with the keyboard alone.</summary>
        public static readonly string[] MouseOnly = { "MoveToPointer", "Point" };

        /// <summary>Actions only the keyboard scheme uses (the mouse sets the facing in the other scheme).</summary>
        public static readonly string[] KeyboardOnlyActions = { "LockFacing" };

        /// <summary>Actions that are pass-through readers (no game verb of their own).</summary>
        static readonly string[] NotVerbs = { };

        /// <summary>Pairs that may share a key because they never act in the same scheme (HumanInputSource gates them).</summary>
        static readonly string[][] ExclusivePairs = { new[] { "LockFacing", "MoveToPointer" } };

        /// <summary>What does an action's job in a scheme without its own binding there (null = it needs a binding).</summary>
        public static string Equivalent(string action, ControlScheme scheme)
        {
            switch (scheme)
            {
                case ControlScheme.Keyboard:
                    if (action == "Aim" || action == "Point") return "the fighter faces where it moves, with target assist (AimResolver.Mode.Keys)";
                    if (action == "MoveToPointer") return "not used: walking to the pointer needs a mouse; the move keys walk";
                    break;
                case ControlScheme.KeyboardMouse:
                    if (action == "Aim") return "the mouse pointer (Point)";
                    if (action == "LockFacing") return "not needed: the mouse sets the facing";
                    break;
                case ControlScheme.Gamepad:
                    if (action == "Point") return "the right stick (Aim)";
                    if (action == "LockFacing") return "the right stick (Aim) holds a facing while the left stick moves";
                    if (action == "MoveToPointer") return "not used: walking to the pointer needs a mouse; the left stick walks";
                    if (action == "StationBuy") return "A (UI submit) inside the open station panel";
                    break;
            }
            return null;
        }

        /// <summary>
        /// feat/terrain-affordances: pairs that share a button because their contexts never overlap: on a controller, R3
        /// opens the base station inside your own station ring (StationPanel) and, everywhere else, holding it shows all
        /// usable terrain (TerrainAffordanceView ignores the hold while the station panel is open).
        /// </summary>
        static readonly string[][] ContextPairs = { new[] { "OpenStation", "ShowTerrain" } };

        /// <summary>True when the two actions share a button by context (ContextPairs).</summary>
        public static bool ContextShared(string a, string b)
        {
            for (int i = 0; i < ContextPairs.Length; i++)
            {
                string x = ContextPairs[i][0], y = ContextPairs[i][1];
                if ((a == x && b == y) || (a == y && b == x)) return true;
            }
            return false;
        }

        /// <summary>One key may serve both actions: never in the same scheme, or never in the same context.</summary>
        public static bool MayShareKey(string a, string b) => SchemeExclusive(a, b) || ContextShared(a, b);

        /// <summary>True when the two actions never act in the same scheme, so one key may serve both.</summary>
        public static bool SchemeExclusive(string a, string b)
        {
            for (int i = 0; i < ExclusivePairs.Length; i++)
            {
                string x = ExclusivePairs[i][0], y = ExclusivePairs[i][1];
                if ((a == x && b == y) || (a == y && b == x)) return true;
            }
            return false;
        }

        /// <summary>Does the action act in the scheme at all?</summary>
        public static bool ActsIn(string action, ControlScheme scheme)
        {
            if (scheme == ControlScheme.Keyboard) return Array.IndexOf(MouseOnly, action) < 0;
            if (scheme == ControlScheme.KeyboardMouse) return Array.IndexOf(KeyboardOnlyActions, action) < 0;
            return true;
        }

        static bool InGroup(InputBinding b, string group)
        {
            if (string.IsNullOrEmpty(b.groups)) return false;
            string[] gs = b.groups.Split(InputBinding.Separator);
            for (int i = 0; i < gs.Length; i++)
                if (gs[i] == group) return true;
            return false;
        }

        /// <summary>Does binding b count for the scheme (group and device)?</summary>
        public static bool InScheme(InputBinding b, ControlScheme scheme)
        {
            if (b.isComposite) return false;
            string path = b.effectivePath ?? string.Empty;
            switch (scheme)
            {
                case ControlScheme.Gamepad: return InGroup(b, ControlBindings.GamepadGroup);
                case ControlScheme.Keyboard:
                    return InGroup(b, ControlBindings.KeyboardGroup) && path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase);
                default: return InGroup(b, ControlBindings.KeyboardGroup);
            }
        }

        /// <summary>The effective paths an action has in a scheme (composite parts included).</summary>
        public static List<string> Paths(InputAction action, ControlScheme scheme)
        {
            var r = new List<string>();
            if (action == null || !ActsIn(action.name, scheme)) return r;
            var bs = action.bindings;
            for (int i = 0; i < bs.Count; i++)
                if (InScheme(bs[i], scheme) && !string.IsNullOrEmpty(bs[i].effectivePath))
                    r.Add(IsDoubleTap(bs[i]) ? bs[i].effectivePath + DoubleTapSuffix : bs[i].effectivePath);
            return r;
        }

        /// <summary>feat/ground-camera: a double-tap binding (MultiTap) is its own gesture, so it may share a button with a
        /// press or hold (Camera view: double-tap View; Scoreboard: hold View).</summary>
        public const string DoubleTapSuffix = " (double tap)";

        public static bool IsDoubleTap(InputBinding b) =>
            !string.IsNullOrEmpty(b.effectiveInteractions) && b.effectiveInteractions.IndexOf("MultiTap", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Actions of the map with neither a binding nor an Equivalent in the scheme ("Action (scheme)").</summary>
        public static List<string> Missing(InputActionMap map, ControlScheme scheme)
        {
            var r = new List<string>();
            if (map == null) return r;
            foreach (InputAction a in map.actions)
            {
                if (Array.IndexOf(NotVerbs, a.name) >= 0) continue;
                if (Paths(a, scheme).Count > 0) continue;
                if (Equivalent(a.name, scheme) != null) continue;
                r.Add(a.name + " (" + scheme + ")");
            }
            return r;
        }

        /// <summary>
        /// Keys bound to two different actions acting in the same scheme ("path: A + B"); scheme-exclusive pairs and
        /// SwitchDigPlace's wheel sharing with the map and spectator zoom (InputContext, not bindings) are not collisions.
        /// </summary>
        public static List<string> Collisions(InputActionMap map, ControlScheme scheme)
        {
            var r = new List<string>();
            if (map == null) return r;
            var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (InputAction a in map.actions)
            {
                foreach (string p in Paths(a, scheme))
                {
                    if (owner.TryGetValue(p, out string o))
                    {
                        if (o != a.name && !MayShareKey(o, a.name)) r.Add(p + ": " + o + " + " + a.name);
                    }
                    else owner[p] = a.name;
                }
            }
            return r;
        }

        /// <summary>Menu actions (EventSystem default UI actions, InputSystemUIInputModule.AssignDefaultActions) per scheme.</summary>
        public static readonly string[][] MenuActions =
        {
            new[] { "Move focus", "ARROWS or W A S D", "ARROWS or W A S D (mouse: hover)", "D-PAD or L STICK" },
            new[] { "Confirm", "ENTER (SPACE on title, story and cards)", "ENTER or LEFT CLICK", "A" },
            new[] { "Back", "ESC", "ESC", "B" },
            new[] { "Next tab (Settings)", "TAB", "TAB or click the tab", "LB / RB" },
        };
    }
}

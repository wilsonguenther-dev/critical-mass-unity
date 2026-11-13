using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Input
{
    /// <summary>The named keyboard layouts on the Controls screen (controls-1, owner 2026-11-13).</summary>
    public enum ControlPreset { ArrowsZxc, WasdJkl, WasdMouse, LeftHanded }

    /// <summary>
    /// controls-1 (owner, 2026-11-13: "Z, X, C on my left hand with the arrows on my right makes sense: hands separated").
    /// Named layouts that apply in one press, on top of per-key rebinding. The three right-handed layouts are all live at
    /// once in Controls.inputactions (arrows + Z X C, WASD + J K L, WASD + mouse), so picking one of them resets every key
    /// to the defaults and sets which pair every prompt and the tutorial name. LEFT-HANDED is the mouse in the left hand
    /// and the arrows under the right: it moves the left-hand keys of the arrows layout to the keys beside the arrows.
    /// The research behind the layouts: controls-1/AUDIT.md, "Default layout: sources". The choice is saved
    /// (PlayerPrefs cm.controls.preset) next to the overrides (cm.bindings); any later rebind keeps working on top of it.
    /// </summary>
    public static class ControlPresets
    {
        public const string PrefsKey = "cm.controls.preset";

        public static readonly ControlPreset[] All = { ControlPreset.ArrowsZxc, ControlPreset.WasdJkl, ControlPreset.WasdMouse, ControlPreset.LeftHanded };

        /// <summary>One key moved by a preset: the action's keyboard binding whose default path is From now reads To.</summary>
        public readonly struct Remap
        {
            public readonly string Action, From, To;

            public Remap(string action, string from, string to)
            {
                Action = action;
                From = from;
                To = to;
            }
        }

        static readonly Remap[] NoRemaps = new Remap[0];

        /// <summary>
        /// LEFT-HANDED: the mouse (left hand) attacks, uses the ability and aims; the right hand moves on the arrows and
        /// takes the verbs that sat on the left hand in the arrows layout, on the keys next to the arrows. TAB, M, T, B and
        /// ESC stay where the genre puts them (menus and overlays, not used while fighting).
        /// </summary>
        static readonly Remap[] LeftHandedRemaps =
        {
            new Remap("Jump", "<Keyboard>/c", "<Keyboard>/rightShift"),
            new Remap("Interact", "<Keyboard>/v", "<Keyboard>/slash"),
            new Remap("DigPlace", "<Keyboard>/f", "<Keyboard>/period"),
            new Remap("SwitchDigPlace", "<Keyboard>/r", "<Keyboard>/comma"),
            new Remap("Ping", "<Keyboard>/g", "<Keyboard>/semicolon"),
            new Remap("HudCursor", "<Keyboard>/leftAlt", "<Keyboard>/rightAlt"),
        };

        public static IReadOnlyList<Remap> Remaps(ControlPreset p) => p == ControlPreset.LeftHanded ? LeftHandedRemaps : NoRemaps;

        public static string Title(ControlPreset p)
        {
            switch (p)
            {
                case ControlPreset.ArrowsZxc: return "ARROWS + Z X C";
                case ControlPreset.WasdJkl: return "WASD + J K L";
                case ControlPreset.WasdMouse: return "WASD + MOUSE";
                default: return "LEFT-HANDED";
            }
        }

        /// <summary>One line on what the layout puts where (shown under the preset row).</summary>
        public static string Summary(ControlPreset p)
        {
            switch (p)
            {
                case ControlPreset.ArrowsZxc: return "Arrows move (right hand). Z attack, X ability, C jump, V interact, F dig, R switch, L SHIFT lock facing (left hand).";
                case ControlPreset.WasdJkl: return "WASD move (left hand). J attack, K ability, SPACE jump, H interact, U dig, I switch, L lock facing (right hand).";
                case ControlPreset.WasdMouse: return "WASD move, SPACE jump, E interact, F dig (left hand). The mouse aims, clicks attack and ability, the wheel switches.";
                default: return "Mouse in the left hand aims and attacks. Arrows move; R SHIFT jump, / interact, . dig, , switch, ; ping (right hand).";
            }
        }

        static bool _leftHanded;

        /// <summary>The LEFT-HANDED layout is in use (key prompts then name the right-hand keys).</summary>
        public static bool LeftHandedActive => _leftHanded;

        /// <summary>The preset picked last (null = never picked, or forgotten by RESET ALL).</summary>
        public static ControlPreset? Stored
        {
            get
            {
                string s = PlayerPrefs.GetString(PrefsKey, string.Empty);
                return Enum.TryParse(s, out ControlPreset p) && Array.IndexOf(All, p) >= 0 ? p : (ControlPreset?)null;
            }
        }

        /// <summary>
        /// Applies a preset in one step: every override dropped, the preset's keys moved, the result saved, and the prompt
        /// hand and scheme set to the layout's pair (they still follow the device the player actually uses afterwards).
        /// </summary>
        public static void Apply(InputActionAsset asset, ControlPreset p)
        {
            if (asset == null) return;
            asset.RemoveAllBindingOverrides();
            InputActionMap map = ControlBindings.Map(asset);
            foreach (Remap r in Remaps(p))
            {
                InputAction a = map != null ? map.FindAction(r.Action, false) : null;
                if (a == null) continue;
                for (int i = 0; i < a.bindings.Count; i++)
                    if (string.Equals(a.bindings[i].path, r.From, StringComparison.OrdinalIgnoreCase))
                    {
                        a.ApplyBindingOverride(i, r.To);
                        break;
                    }
            }
            PlayerPrefs.SetString(PrefsKey, p.ToString());
            _leftHanded = p == ControlPreset.LeftHanded;
            ControlBindings.Persist(asset);   // saves and raises BindingsChanged
            ApplyHand(p);
        }

        /// <summary>The prompt hand and scheme of a layout.</summary>
        static void ApplyHand(ControlPreset p)
        {
            bool arrows = p == ControlPreset.ArrowsZxc || p == ControlPreset.LeftHanded;
            ControlBindings.SetHand(arrows ? KeyboardHand.Arrows : KeyboardHand.Wasd);
            ControlBindings.SetKeyboardOnly(p == ControlPreset.ArrowsZxc || p == ControlPreset.WasdJkl);
        }

        /// <summary>
        /// The preset the asset is in now: the stored one, while the asset's overrides are still exactly that preset's
        /// (a later rebind makes it CUSTOM = null). Presets without remaps match an asset without overrides.
        /// </summary>
        public static ControlPreset? Current(InputActionAsset asset)
        {
            ControlPreset? s = Stored;
            return s.HasValue && Matches(asset, s.Value) ? s : null;
        }

        /// <summary>The asset's overrides are exactly the preset's remaps (pure read).</summary>
        public static bool Matches(InputActionAsset asset, ControlPreset p)
        {
            InputActionMap map = ControlBindings.Map(asset);
            if (map == null) return false;
            var want = new HashSet<string>();
            foreach (Remap r in Remaps(p)) want.Add(r.Action + "|" + r.From.ToLowerInvariant() + "|" + r.To.ToLowerInvariant());
            int have = 0;
            foreach (InputAction a in map.actions)
                foreach (InputBinding b in a.bindings)
                {
                    if (b.overridePath == null) continue;
                    if (!want.Contains(a.name + "|" + (b.path ?? "").ToLowerInvariant() + "|" + b.overridePath.ToLowerInvariant())) return false;
                    have++;
                }
            return have == want.Count;
        }

        /// <summary>RESET ALL: no preset is remembered (the defaults are the three right-handed layouts at once).</summary>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            _leftHanded = false;
        }

        /// <summary>Boot (after the saved overrides were applied): the remembered layout's prompt pair.</summary>
        public static void RestoreOnBoot()
        {
            ControlPreset? s = Stored;
            _leftHanded = s == ControlPreset.LeftHanded;
            if (s.HasValue) ApplyHand(s.Value);
        }

        /// <summary>Tests: the cached left-handed flag without touching PlayerPrefs.</summary>
        public static void SetLeftHandedForTests(bool on) => _leftHanded = on;
    }
}

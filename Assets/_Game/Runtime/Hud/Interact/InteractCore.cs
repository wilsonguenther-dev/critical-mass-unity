using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace CriticalMass.Runtime.Hud.Interact
{
    /// <summary>
    /// One of the Player actions this item adds to Controls.inputactions (Map, Ping, HudCursor, MinimapZoom), so it rebinds
    /// and saves with the rest (cm.bindings). If the action is missing (an older asset) the default key and controller button
    /// are read straight from the devices, so the feature still works.
    /// </summary>
    public sealed class InteractKey
    {
        public readonly string Action;
        public readonly Key DefaultKey;
        public readonly GamepadButton DefaultPad;
        readonly string _kbFallback, _padFallback;
        InputAction _a;
        string _glyphKb, _glyphPad;

        public InteractKey(string action, Key key, GamepadButton pad, string kbGlyph, string padGlyph)
        {
            Action = action;
            DefaultKey = key;
            DefaultPad = pad;
            _kbFallback = kbGlyph;
            _padFallback = padGlyph;
        }

        /// <summary>The bound action (null: the device fallback is read).</summary>
        public InputAction Bound => _a;

        public void Bind(InputActionMap map)
        {
            _a = map != null ? map.FindAction(Action, false) : null;
            RefreshGlyphs();
        }

        /// <summary>Re-reads the prompt text (after a rebind or a device change; allocates, never per frame).</summary>
        public void RefreshGlyphs()
        {
            // Through InputGlyphs: the plain words of the current binding on each device.
            _glyphKb = InputGlyphs.Words(Action, ControlDevice.KeyboardMouse);
            _glyphPad = InputGlyphs.Words(Action, ControlDevice.Gamepad);
            _glyphKb = string.IsNullOrEmpty(_glyphKb) ? null : _glyphKb.ToUpperInvariant();
            _glyphPad = string.IsNullOrEmpty(_glyphPad) ? null : _glyphPad.ToUpperInvariant();
            if (string.IsNullOrEmpty(_glyphKb) || _glyphKb == "-") _glyphKb = _kbFallback;
            if (string.IsNullOrEmpty(_glyphPad) || _glyphPad == "-") _glyphPad = _padFallback;
        }

        public string Glyph(ControlDevice d) => d == ControlDevice.Gamepad ? _glyphPad ?? _padFallback : _glyphKb ?? _kbFallback;

        public bool IsPressed
        {
            get
            {
                if (_a != null) return _a.enabled && _a.IsPressed();
                Keyboard kb = Keyboard.current;
                if (kb != null && kb[DefaultKey].isPressed) return true;
                Gamepad pad = Gamepad.current;
                return pad != null && pad[DefaultPad].isPressed;
            }
        }

        public bool WasPressed
        {
            get
            {
                if (_a != null) return _a.enabled && _a.WasPressedThisFrame();
                Keyboard kb = Keyboard.current;
                if (kb != null && kb[DefaultKey].wasPressedThisFrame) return true;
                Gamepad pad = Gamepad.current;
                return pad != null && pad[DefaultPad].wasPressedThisFrame;
            }
        }

        /// <summary>Held on the keyboard or mouse (the HUD cursor is a hold on keyboard).</summary>
        public bool IsPressedOnKeyboardMouse
        {
            get
            {
                if (_a != null)
                {
                    if (!_a.enabled) return false;
                    var cs = _a.controls;
                    for (int i = 0; i < cs.Count; i++)
                        if ((cs[i].device is Keyboard || cs[i].device is Mouse) && cs[i] is ButtonControl b && b.isPressed) return true;
                    return false;
                }
                Keyboard kb = Keyboard.current;
                return kb != null && kb[DefaultKey].isPressed;
            }
        }

        /// <summary>
        /// Pressed this frame by a device that owns this key in context k (2026-11-12): while spectating the controller
        /// d-pad belongs to the spectator's objective jumps, so only the keyboard press counts (InputContext).
        /// </summary>
        public bool WasPressedIn(CriticalMass.Runtime.Input.InputContextKind k) =>
            WasPressed && (CriticalMass.Runtime.Input.InputContext.HudOwnsDpad(k) || !WasPressedOnGamepad);

        /// <summary>Pressed this frame on a controller (the HUD focus is a toggle on controller).</summary>
        public bool WasPressedOnGamepad
        {
            get
            {
                if (_a != null)
                {
                    if (!_a.enabled) return false;
                    var cs = _a.controls;
                    for (int i = 0; i < cs.Count; i++)
                        if (cs[i].device is Gamepad && cs[i] is ButtonControl b && b.wasPressedThisFrame) return true;
                    return false;
                }
                Gamepad pad = Gamepad.current;
                return pad != null && pad[DefaultPad].wasPressedThisFrame;
            }
        }

        public bool WasReleased
        {
            get
            {
                if (_a != null) return _a.enabled && _a.WasReleasedThisFrame();
                Keyboard kb = Keyboard.current;
                if (kb != null && kb[DefaultKey].wasReleasedThisFrame) return true;
                Gamepad pad = Gamepad.current;
                return pad != null && pad[DefaultPad].wasReleasedThisFrame;
            }
        }
    }

    /// <summary>Why the gate holds game actions back this frame (combined as flags).</summary>
    [Flags]
    public enum GateReason
    {
        None = 0,
        /// <summary>The full map or an info card is open.</summary>
        Modal = 1,
        /// <summary>The mouse is over a live HUD control (the minimap always; the rest while the cursor is free).</summary>
        PointerOverHud = 2,
        /// <summary>The HUD cursor key is held (keyboard) or HUD focus is on (controller).</summary>
        CursorFree = 4,
        /// <summary>The ping wheel is open.</summary>
        PingWheel = 8,
        /// <summary>The controller drives the open overlay (sticks pan, triggers zoom, A clicks).</summary>
        PadOwnsSticks = 16,
        /// <summary>Controller HUD focus mode: the left stick and A move and click the focus.</summary>
        PadHudFocus = 32,
    }

    /// <summary>
    /// Keeps HUD and map input from reaching the sim: while a reason holds, the matching Player actions are disabled (an
    /// action disabled mid-press is cancelled, so HumanInputSource gets its ButtonUp), and only the actions the gate itself
    /// disabled are enabled again. HumanInputSource and the sim are not edited; the sim simply never sees the press.
    /// </summary>
    public sealed class InputGate
    {
        /// <summary>The Player actions the gate may hold back, in bit order.</summary>
        public static readonly string[] Actions = { "Attack", "Ability", "DigPlace", "SwitchDigPlace", "Aim", "Move", "Jump", "Interact" };

        const int Attack = 1 << 0, Ability = 1 << 1, DigPlace = 1 << 2, SwitchDig = 1 << 3, Aim = 1 << 4, Move = 1 << 5,
            Jump = 1 << 6, Interact = 1 << 7;

        readonly InputAction[] _actions = new InputAction[Actions.Length];
        readonly bool[] _mine = new bool[Actions.Length];
        InputActionMap _map;

        /// <summary>The action mask held back for these reasons (pure; EditMode tests read it).</summary>
        public static int MaskFor(GateReason r)
        {
            int m = 0;
            if ((r & (GateReason.Modal | GateReason.PointerOverHud | GateReason.CursorFree | GateReason.PingWheel)) != 0) m |= Attack | Ability;
            if ((r & (GateReason.Modal | GateReason.CursorFree)) != 0) m |= DigPlace | SwitchDig;
            if ((r & GateReason.PadOwnsSticks) != 0) m |= Aim | Attack | Ability;
            if ((r & GateReason.PadHudFocus) != 0) m |= Move | Aim | Jump | Interact | Attack | Ability | DigPlace | SwitchDig;
            return m;
        }

        public static bool Blocks(GateReason r, string action)
        {
            int i = Array.IndexOf(Actions, action);
            return i >= 0 && (MaskFor(r) & (1 << i)) != 0;
        }

        /// <summary>The mask applied last.</summary>
        public int Applied { get; private set; }

        public void Bind(InputActionMap map)
        {
            Release();
            _map = map;
            for (int i = 0; i < Actions.Length; i++) _actions[i] = map != null ? map.FindAction(Actions[i], false) : null;
        }

        /// <summary>Disables the masked actions (once), re-enables what this gate disabled when the reason ends.</summary>
        public void Apply(GateReason r)
        {
            int mask = MaskFor(r);
            Applied = mask;
            for (int i = 0; i < _actions.Length; i++)
            {
                InputAction a = _actions[i];
                if (a == null) continue;
                bool block = (mask & (1 << i)) != 0;
                if (block)
                {
                    if (a.enabled)
                    {
                        a.Disable();
                        _mine[i] = true;
                    }
                }
                else if (_mine[i])
                {
                    _mine[i] = false;
                    if (!a.enabled && _map != null && _map.enabled) a.Enable();
                }
            }
        }

        /// <summary>Everything this gate disabled is enabled again.</summary>
        public void Release()
        {
            for (int i = 0; i < _actions.Length; i++)
            {
                if (_mine[i] && _actions[i] != null && !_actions[i].enabled && _map != null && _map.enabled) _actions[i].Enable();
                _mine[i] = false;
            }
            Applied = 0;
        }
    }

    /// <summary>The minimap's visibility rule, shared by the full map so both show the same enemies.</summary>
    public static class MapRules
    {
        /// <summary>True when a living actor of team <paramref name="mine"/> is within GameHud.SightRadius of actor i (the
        /// same rule and radius as the HUD minimap).</summary>
        public static bool Seen(World w, int i, TeamId mine, int n)
        {
            double r2 = GameHud.SightRadius * GameHud.SightRadius;
            for (int j = 0; j < n; j++)
            {
                if (!w.Actors[j].Alive || w.Actors[j].Team != mine) continue;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i];
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        /// <summary>Whether actor i is drawn on a map for viewer me: living teammates always, living enemies only when seen.</summary>
        public static bool Visible(World w, int i, int me)
        {
            if (i < 0 || i >= w.ActorCount || !w.Actors[i].Alive) return false;
            TeamId mine = me >= 0 && me < w.ActorCount ? w.Actors[me].Team : TeamId.Azure;
            return w.Actors[i].Team == mine || Seen(w, i, mine, w.ActorCount);
        }
    }

    /// <summary>
    /// View-side names for the units on the map (the sim has no names). From the words pack (Content/Lore/names.json,
    /// {"azure": [...], "crimson": [...]}) when present, else the built-in lists; by team and team slot, so a unit keeps
    /// its name for the whole match.
    /// </summary>
    public static class Callsigns
    {
        static readonly string[] AzureBuiltIn =
        {
            "Brook", "Cobalt", "Tide", "Wren", "Slate", "Marlin", "Frost", "Heron", "Skye", "Indigo", "Gale", "Pike", "Reef",
            "Lark", "Ash", "Moss",
        };
        static readonly string[] CrimsonBuiltIn =
        {
            "Cinder", "Rook", "Flint", "Garnet", "Vex", "Saffron", "Brand", "Kestrel", "Rust", "Scarlet", "Jasper", "Talon",
            "Sorrel", "Blaze", "Thorn", "Coal",
        };
        static string[] _azure, _crimson;
        static string[] _numbered;

        [Serializable] sealed class NamesFile { public string[] azure; public string[] crimson; }

        static void Init()
        {
            if (_azure != null) return;
            _azure = AzureBuiltIn;
            _crimson = CrimsonBuiltIn;
            NamesFile f = LoreText.Load<NamesFile>("names.json");
            if (f != null && f.azure != null && f.azure.Length > 0) _azure = f.azure;
            if (f != null && f.crimson != null && f.crimson.Length > 0) _crimson = f.crimson;
            _numbered = new string[64];
            for (int i = 0; i < _numbered.Length; i++) _numbered[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The unit's name ("YOU" for the local player).</summary>
        public static string For(World w, int id, int me)
        {
            Init();
            if (id == me) return "YOU";
            if (w == null || id < 0 || id >= w.ActorCount) return "-";
            int slot = World.TeamSlot(w, id);
            string[] list = w.Actors[id].Team == TeamId.Crimson ? _crimson : _azure;
            if (slot >= 0 && slot < list.Length) return list[slot];
            return slot >= 0 && slot < _numbered.Length ? _numbered[slot] : "-";
        }
    }

    /// <summary>
    /// Words pack access (Assets/_Game/Content/Lore/*.json, branch feat/dg-03-04-rings-words) with {cfg:id} tokens resolved
    /// through tokens.json against GameConfig. A missing file, field or token returns null and the caller uses its built-in
    /// text. Read on demand (a card opens), never per frame.
    /// </summary>
    public static class LoreText
    {
        public static string Folder => Path.Combine(Application.dataPath, "_Game", "Resources", "Lore");

        public static T Load<T>(string file) where T : class
        {
            try
            {
                // The words pack ships in Resources/Lore (the batch 1 rule), so player builds read it too.
                TextAsset ta = Resources.Load<TextAsset>("Lore/" + Path.GetFileNameWithoutExtension(file));
                if (ta != null && !string.IsNullOrEmpty(ta.text)) return JsonUtility.FromJson<T>(ta.text);
                if (!Application.isEditor) return null;
                string p = Path.Combine(Folder, file);
                if (!File.Exists(p)) return null;
                return JsonUtility.FromJson<T>(File.ReadAllText(p));
            }
            catch (Exception)
            {
                return null;
            }
        }

        [Serializable] sealed class Token { public string id; public string path; public string format; }
        [Serializable] sealed class TokensFile { public Token[] tokens; }
        [Serializable] sealed class TipRow { public string id; public string text; public string needsRule; }
        [Serializable] sealed class TipsFile { public TipRow[] tips; }
        [Serializable] public sealed class PackAbility { public string input; public string name; public string text; public string cooldown; public string needsRule; }
        [Serializable] public sealed class PackClass { public string id; public string name; public string role; public string bio; public PackAbility[] abilities; }
        [Serializable] sealed class ClassesFile { public PackClass[] classes; }

        static Dictionary<string, Token> _tokens;
        static Dictionary<string, string> _tips;
        static PackClass[] _classes;
        static bool _loaded;

        static void Init()
        {
            if (_loaded) return;
            _loaded = true;
            _tokens = new Dictionary<string, Token>();
            TokensFile tf = Load<TokensFile>("tokens.json");
            if (tf != null && tf.tokens != null)
                foreach (Token t in tf.tokens)
                    if (t != null && !string.IsNullOrEmpty(t.id)) _tokens[t.id] = t;
            _tips = new Dictionary<string, string>();
            TipsFile tips = Load<TipsFile>("tips.json");
            if (tips != null && tips.tips != null)
                foreach (TipRow t in tips.tips)
                    if (t != null && !string.IsNullOrEmpty(t.id) && string.IsNullOrEmpty(t.needsRule)) _tips[t.id] = t.text;
            ClassesFile cf = Load<ClassesFile>("classes.json");
            _classes = cf != null ? cf.classes : null;
        }

        /// <summary>A tip by id with tokens resolved, or null.</summary>
        public static string Tip(string id, GameConfig cfg)
        {
            Init();
            return _tips.TryGetValue(id, out string s) ? Resolve(s, cfg) : null;
        }

        /// <summary>The pack's class entry by class id ("Warrior"...), or null.</summary>
        public static PackClass Class(ClassId c)
        {
            Init();
            if (_classes == null) return null;
            string id = c.ToString();
            foreach (PackClass k in _classes)
                if (k != null && string.Equals(k.id, id, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }

        /// <summary>The pack ability of a class by input ("attack", "ability", "passive"), or null (or one waiting on a rule).</summary>
        public static PackAbility Ability(ClassId c, string input)
        {
            PackClass k = Class(c);
            if (k == null || k.abilities == null) return null;
            foreach (PackAbility a in k.abilities)
                if (a != null && a.input == input && string.IsNullOrEmpty(a.needsRule)) return a;
            return null;
        }

        /// <summary>Resolves every {cfg:id}; null when one cannot be resolved (the caller falls back).</summary>
        public static string Resolve(string text, GameConfig cfg)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Init();
            if (cfg == null) cfg = GameConfig.CreateDefault();
            int at = text.IndexOf("{cfg:", StringComparison.Ordinal);
            if (at < 0) return text.IndexOf('{') >= 0 ? null : text;
            var sb = new System.Text.StringBuilder(text.Length + 16);
            int from = 0;
            while (at >= 0)
            {
                int end = text.IndexOf('}', at);
                if (end < 0) return null;
                sb.Append(text, from, at - from);
                string id = text.Substring(at + 5, end - at - 5);
                if (!_tokens.TryGetValue(id, out Token tk) || !TryValue(cfg, tk.path, out double v)) return null;
                sb.Append(Format(v, tk.format, cfg));
                from = end + 1;
                at = text.IndexOf("{cfg:", from, StringComparison.Ordinal);
            }
            sb.Append(text, from, text.Length - from);
            string s = sb.ToString();
            return s.IndexOf('{') >= 0 ? null : s;
        }

        static string Format(double v, string format, GameConfig cfg)
        {
            switch (format)
            {
                case "clock":
                    int sec = (int)Math.Round(v);
                    return (sec / 60).ToString(CultureInfo.InvariantCulture) + ":" + (sec % 60).ToString("00", CultureInfo.InvariantCulture);
                case "percent":
                    return Math.Round(v * 100).ToString(CultureInfo.InvariantCulture);
                case "percentOff":
                    return Math.Round((1 - v) * 100).ToString(CultureInfo.InvariantCulture);
                case "ticksAsSeconds":
                    return Number(cfg.SimHz > 0 ? v / cfg.SimHz : v);
                default:
                    return Number(v);
            }
        }

        public static string Number(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>Reads a dotted GameConfig path ("KaijuFeed.BerserkThreshold", "Classes[0].MaxHp") as a number.</summary>
        public static bool TryValue(object root, string path, out double value)
        {
            value = 0;
            if (root == null || string.IsNullOrEmpty(path)) return false;
            object o = root;
            foreach (string raw in path.Split('.'))
            {
                string part = raw;
                int index = -1;
                int br = part.IndexOf('[');
                if (br >= 0)
                {
                    int close = part.IndexOf(']', br);
                    if (close < 0 || !int.TryParse(part.Substring(br + 1, close - br - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) return false;
                    part = part.Substring(0, br);
                }
                if (o == null) return false;
                const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
                FieldInfo f = o.GetType().GetField(part, F);
                if (f != null) o = f.GetValue(o);
                else
                {
                    PropertyInfo p = o.GetType().GetProperty(part, F);
                    if (p == null) return false;
                    o = p.GetValue(o);
                }
                if (index >= 0)
                {
                    if (!(o is Array arr) || index >= arr.Length) return false;
                    o = arr.GetValue(index);
                }
            }
            switch (o)
            {
                case double d: value = d; return true;
                case float fl: value = fl; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                default: return false;
            }
        }
    }

    /// <summary>
    /// Calls into other items without a compile-time dependency (they are on open branches): the scoreboard
    /// (Hud.Scoreboard.ForceOpen, DG-11), a character panel (static Show or Toggle on Menus.CharacterPanel,
    /// Hud.CharacterPanel or Menus.CharacterSheet) and a UI sound hook (static Play(string) on Audio.UiSound, Audio.UiSounds
    /// or View.Effects.UiSound). Looked up once; a missing type means the call reports false and this item falls back.
    /// </summary>
    public static class ItemHooks
    {
        static bool _looked;
        static FieldInfo _scoreboardForce;
        static MethodInfo _characterPanel, _sound;
        static readonly object[] SoundArg = new object[1];

        static readonly string[] CharacterPanelTypes =
            { "CriticalMass.Runtime.Menu.CharacterPanel", "CriticalMass.Runtime.Menus.CharacterPanel", "CriticalMass.Runtime.Hud.CharacterPanel", "CriticalMass.Runtime.Menus.CharacterSheet" };
        static readonly string[] SoundTypes =
            { "CriticalMass.Runtime.Audio.UiSound", "CriticalMass.Runtime.Audio.UiSounds", "CriticalMass.Runtime.View.Effects.UiSound" };

        static void Look()
        {
            if (_looked) return;
            _looked = true;
            Assembly asm = typeof(GameHud).Assembly;
            const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
            Type sb = asm.GetType("CriticalMass.Runtime.Hud.Scoreboard", false);
            FieldInfo f = sb != null ? sb.GetField("ForceOpen", S) : null;
            _scoreboardForce = f != null && f.FieldType == typeof(bool) ? f : null;
            foreach (string n in CharacterPanelTypes)
            {
                Type t = asm.GetType(n, false);
                if (t == null) continue;
                _characterPanel = t.GetMethod("Toggle", S, null, Type.EmptyTypes, null) ?? t.GetMethod("Show", S, null, Type.EmptyTypes, null);
                if (_characterPanel != null) break;
            }
            foreach (string n in SoundTypes)
            {
                Type t = asm.GetType(n, false);
                if (t == null) continue;
                _sound = t.GetMethod("Play", S, null, new[] { typeof(string) }, null);
                if (_sound != null) break;
            }
        }

        public static bool HasScoreboard { get { Look(); return _scoreboardForce != null; } }
        public static bool HasCharacterPanel { get { Look(); return _characterPanel != null; } }
        public static bool HasSound { get { Look(); return _sound != null; } }

        /// <summary>Whether this item holds the scoreboard open.</summary>
        public static bool ScoreboardForcedByUs { get; private set; }

        /// <summary>Opens or closes the scoreboard; false when the scoreboard type is not in this build.</summary>
        public static bool ToggleScoreboard()
        {
            Look();
            if (_scoreboardForce == null) return false;
            bool on = !(bool)_scoreboardForce.GetValue(null);
            _scoreboardForce.SetValue(null, on);
            ScoreboardForcedByUs = on;
            return true;
        }

        public static void ReleaseScoreboard()
        {
            if (!ScoreboardForcedByUs) return;
            ScoreboardForcedByUs = false;
            if (_scoreboardForce != null) _scoreboardForce.SetValue(null, false);
        }

        public static bool OpenCharacterPanel()
        {
            Look();
            if (_characterPanel == null) return false;
            try
            {
                _characterPanel.Invoke(null, null);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        /// <summary>Plays a UI cue through the sound item's static hook if it exists (no audio code here).</summary>
        public static void Sound(string cue)
        {
            Look();
            if (_sound == null) return;
            SoundArg[0] = cue;
            try { _sound.Invoke(null, SoundArg); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}

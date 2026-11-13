using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// The controls card (DG-14; controls-1 three schemes): every game action with its keys, read from the CURRENT bindings
    /// (after rebinding) through ControlBindings. Wide (w &gt;= 1100, the Controls screen): one table with a column per way
    /// to play: KEYBOARD with the left hand on W A S D, KEYBOARD with the right hand on the arrows, KEYBOARD AND MOUSE, and
    /// CONTROLLER, the column in use lit in brass. Narrow (the loading screen): the scheme in use only, two chips a line.
    /// "Change these in Settings, Controls" under it. Redraws only when the device or a binding changes (events).
    /// </summary>
    public sealed class ControlsCard
    {
        public const string Footer = "Change these in Settings, Controls";
        public const float WideMin = 1100f;

        /// <summary>The table's columns (wide card) in screen order.</summary>
        public enum Col { Wasd = 0, Arrows = 1, Mouse = 2, Pad = 3 }
        public static readonly string[] ColHeads = { "KEYS: W A S D HAND", "KEYS: ARROW HAND", "KEYS AND MOUSE", "CONTROLLER" };

        sealed class Line
        {
            public string Label;
            public int[] Rows;
            public Text Name;
            public readonly Text[] Keys = new Text[4];
            public readonly VectorShape[] Chips = new VectorShape[4];
        }

        public readonly RectTransform Root;
        public readonly VectorShape Body;
        public ControlDevice Device { get; private set; }
        public ControlScheme Scheme { get; private set; }
        public bool Wide { get; }
        readonly List<Line> _lines = new List<Line>();
        readonly Text[] _heads = new Text[4];
        Text _device;

        static Color Navy => CMTokens.VoidNavy.Color;
        static Color Cream => CMTokens.WarmCream.Color;
        static Color Brass => CMTokens.ToolBrass.Color;

        ControlsCard(RectTransform root, VectorShape body, bool wide)
        {
            Root = root;
            Body = body;
            Wide = wide;
        }

        /// <summary>Card lines in screen order (labels of ControlBindings.Rows; Move merges the four move rows).</summary>
        public static readonly string[] LineLabels =
        {
            "Move", "Aim", "Lock facing (hold)", "Attack", "Class ability", "Jump", "Interact / carry", "Dig / place",
            "Switch dig / place", "Ping wheel", "Scoreboard", "Full map", "Base station", "Pause menu",
        };

        static int[] RowsFor(string label)
        {
            if (label == "Move") return new[] { RowIndex("Move up"), RowIndex("Move left"), RowIndex("Move down"), RowIndex("Move right"), RowIndex("Move (stick)") };
            return new[] { RowIndex(label) };
        }

        static int RowIndex(string label)
        {
            for (int i = 0; i < ControlBindings.Rows.Length; i++) if (ControlBindings.Rows[i].Label == label) return i;
            return -1;
        }

        /// <summary>Builds a card at (x, y) from parent's top-left (1920x1080 reference units); the table when w &gt;= WideMin.</summary>
        public static ControlsCard Build(Transform parent, float x, float y, float w, float h, string name = "Controls Card")
        {
            VectorShape body = MenuUi.Panel(parent, name, x, y, w, h);
            bool wide = w >= WideMin;
            float pad = 32f;
            var card = new ControlsCard(body.rectTransform, body, wide);
            RectTransform rt = body.rectTransform;
            MenuUi.Label(rt, "Title", CMTokens.Display, wide ? 40 : 34, Cream, TextAnchor.MiddleLeft, pad, 18f, w * 0.5f, 52f, "CONTROLS", 4f);
            card._device = MenuUi.Label(rt, "Device", CMTokens.Ui, wide ? 20 : 18, Brass, TextAnchor.MiddleRight, w * 0.4f, 18f, w * 0.6f - pad, 52f, "", 2f);
            VectorShape rule = MenuUi.Shape(rt, "Rule", ShapeKind.RoundRect, pad, 76f, w - pad * 2f, 4f, CMTokens.WithAlpha(Cream, 0.35f), 2f);
            rule.OutlinePx = 0f;
            rule.RimPx = 0f;

            int cols = wide ? 4 : 2;
            float labelW = wide ? 250f : 210f, gap = 10f;
            float chipW = (w - pad * 2f - labelW - gap * (cols - 1)) / cols;
            float top = wide ? 128f : 92f, bottom = h - 60f;
            if (wide)
            {
                for (int c = 0; c < 4; c++)
                {
                    card._heads[c] = MenuUi.Label(rt, "Head " + c, CMTokens.Ui, 17, Cream, TextAnchor.MiddleCenter, pad + labelW + c * (chipW + gap), 86f,
                        chipW, 34f, ColHeads[c], 2f);
                    ClassCard.Fit(card._heads[c], 11);
                }
            }
            float lineH = Mathf.Min(52f, (bottom - top) / LineLabels.Length);
            for (int i = 0; i < LineLabels.Length; i++)
            {
                float ly = top + i * lineH;
                var line = new Line { Label = LineLabels[i], Rows = RowsFor(LineLabels[i]) };
                line.Name = MenuUi.Label(rt, "Line " + line.Label, CMTokens.Ui, lineH >= 44f ? 22 : 18, Cream, TextAnchor.MiddleLeft, pad, ly, labelW, lineH - 4f,
                    line.Label.ToUpperInvariant(), 2f);
                ClassCard.Fit(line.Name, 12);
                for (int k = 0; k < cols; k++)
                {
                    float cx = pad + labelW + k * (chipW + gap);
                    VectorShape chip = MenuUi.Shape(rt, $"Chip {line.Label} {k}", ShapeKind.RoundRect, cx, ly + 2f, chipW, lineH - 6f, Navy, 8f);
                    chip.OutlinePx = 2f;
                    chip.RimPx = 2f;
                    line.Chips[k] = chip;
                    line.Keys[k] = MenuUi.Label(chip.transform, "Key", CMTokens.Mono, lineH >= 44f ? 19 : 16, Cream, TextAnchor.MiddleCenter, 0f, 0f, chipW, lineH - 6f, "", 0f);
                    ClassCard.Fit(line.Keys[k], 10);
                }
                card._lines.Add(line);
            }
            MenuUi.Label(rt, "Footer", CMTokens.Ui, wide ? 20 : 18, CMTokens.WithAlpha(Cream, 0.9f), TextAnchor.MiddleCenter, pad, h - 52f, w - pad * 2f, 40f, Footer, 2f);
            card.Refresh(ControlBindings.ActiveDevice);
            return card;
        }

        /// <summary>Redraws every key from the current bindings for the scheme in use (ControlBindings.ActiveScheme).</summary>
        public void Refresh(ControlDevice device)
        {
            Device = device;
            Scheme = device == ControlDevice.Gamepad ? ControlScheme.Gamepad : ControlBindings.KeyboardOnly ? ControlScheme.Keyboard : ControlScheme.KeyboardMouse;
            HudText.Set(_device, Scheme == ControlScheme.Gamepad ? "IN USE: CONTROLLER" : Scheme == ControlScheme.Keyboard ? "IN USE: KEYBOARD" : "IN USE: KEYBOARD AND MOUSE");
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            int lit = ActiveCol(Scheme);
            for (int c = 0; Wide && c < 4; c++)
                if (_heads[c] != null) _heads[c].color = c == lit ? Brass : CMTokens.WithAlpha(Cream, 0.75f);
            for (int i = 0; i < _lines.Count; i++)
            {
                Line l = _lines[i];
                if (Wide)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        string t = CellText(map, l.Label, l.Rows, (Col)c);
                        SetChip(l, c, t.Length == 0 ? "-" : t);
                        l.Chips[c].OutlinePx = c == lit ? 3f : 2f;
                        l.Keys[c].color = c == lit ? Cream : CMTokens.WithAlpha(Cream, 0.8f);
                    }
                }
                else
                {
                    string[] parts = Split(CellText(map, l.Label, l.Rows, (Col)ActiveCol(Scheme)));
                    SetChip(l, 0, parts.Length > 0 ? parts[0] : "");
                    SetChip(l, 1, parts.Length > 1 ? parts[1] : "");
                }
            }
        }

        static string[] Split(string s) => string.IsNullOrEmpty(s) ? new string[0] : s.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>The column of a scheme (keyboard only lights the hand position in use).</summary>
        public static int ActiveCol(ControlScheme s) =>
            s == ControlScheme.Gamepad ? (int)Col.Pad : s == ControlScheme.KeyboardMouse ? (int)Col.Mouse
            : ControlBindings.Hand == KeyboardHand.Arrows ? (int)Col.Arrows : (int)Col.Wasd;

        void SetChip(Line l, int k, string text)
        {
            bool on = !string.IsNullOrEmpty(text);
            if (l.Chips[k].gameObject.activeSelf != on) l.Chips[k].gameObject.SetActive(on);
            if (on) HudText.Set(l.Keys[k], text);
        }

        /// <summary>
        /// The text of one table cell ("" = none). Keyboard hand columns: the slots of that column, else the shared key
        /// (a keyboard binding that names no hand) so every action shows a key in both hand positions. Keys and mouse: the
        /// mouse slot, else the left-hand key. Controller: its slots joined with " / ".
        /// </summary>
        static string CellText(InputActionMap map, string label, int[] rows, Col col)
        {
            if (map == null || rows.Length == 0 || rows[0] < 0) return "";
            if (label == "Move")
            {
                if (col == Col.Pad) return SlotsText(map, rows[4], ControlDevice.Gamepad, SlotColumn.Auto);
                string wasd = JoinMove(map, rows, SlotColumn.Wasd), arrows = JoinMove(map, rows, SlotColumn.Arrows);
                if (arrows == "UP LEFT DOWN RIGHT") arrows = "ARROWS";
                if (col == Col.Wasd) return wasd;
                if (col == Col.Arrows) return arrows;
                return wasd.Length == 0 ? arrows : arrows.Length == 0 ? wasd : wasd + " / " + arrows;
            }
            if (label == "Aim")
            {
                if (col == Col.Pad) return SlotsText(map, rows[0], ControlDevice.Gamepad, SlotColumn.Auto);
                if (col == Col.Mouse) return "MOUSE";
                return "FACES WHERE YOU MOVE";
            }
            if (label == "Lock facing (hold)")
            {
                if (col == Col.Pad) return "R STICK";
                if (col == Col.Mouse) return "";
            }
            switch (col)
            {
                case Col.Pad: return SlotsText(map, rows[0], ControlDevice.Gamepad, SlotColumn.Auto);
                case Col.Mouse:
                {
                    string m = SlotsText(map, rows[0], ControlDevice.KeyboardMouse, SlotColumn.Mouse);
                    return m.Length > 0 ? m : HandText(map, rows[0], SlotColumn.Wasd);
                }
                case Col.Arrows: return HandText(map, rows[0], SlotColumn.Arrows);
                default: return HandText(map, rows[0], SlotColumn.Wasd);
            }
        }

        static string HandText(InputActionMap map, int row, SlotColumn hand)
        {
            string t = SlotsText(map, row, ControlDevice.KeyboardMouse, hand);
            if (t.Length > 0) return t;
            // A shared key (no hand name) serves both hand positions.
            ControlRow r = ControlBindings.Rows[row];
            for (int k = 0; k < r.Keyboard.Length; k++)
            {
                // A shared key in the mouse column (E, F) serves the hand positions too; mouse buttons are skipped below.
                RebindSlot s = r.Keyboard[k];
                InputAction a = map.FindAction(s.Action, false);
                int idx = ControlBindings.FindBindingIndex(a, s);
                if (idx < 0 || !string.IsNullOrEmpty(a.bindings[idx].name)) continue;
                string path = a.bindings[idx].effectivePath ?? "";
                if (!path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase)) continue;
                return ControlBindings.Display(a, idx);
            }
            return "";
        }

        static string JoinMove(InputActionMap map, int[] rows, SlotColumn hand)
        {
            string s = "";
            for (int i = 0; i < 4; i++)
            {
                string t = SlotsText(map, rows[i], ControlDevice.KeyboardMouse, hand);
                if (t.Length == 0) return "";
                s = s.Length == 0 ? t : s + " " + t;
            }
            return s;
        }

        /// <summary>The slots of a row on a device in one column (Auto = every slot of the device), joined with " / ".</summary>
        static string SlotsText(InputActionMap map, int row, ControlDevice device, SlotColumn column)
        {
            if (row < 0 || row >= ControlBindings.Rows.Length) return "";
            ControlRow r = ControlBindings.Rows[row];
            RebindSlot[] set = device == ControlDevice.Gamepad ? r.Gamepad : r.Keyboard;
            string s = "";
            for (int k = 0; k < set.Length; k++)
            {
                if (column != SlotColumn.Auto && set[k].Column != column) continue;
                InputAction a = map.FindAction(set[k].Action, false);
                int idx = ControlBindings.FindBindingIndex(a, set[k]);
                if (idx < 0) continue;
                string t = ControlBindings.Display(a, idx);
                if (t == "-" || t.Length == 0 || s == t || s.EndsWith(" / " + t, StringComparison.Ordinal)) continue;
                s = s.Length == 0 ? t : s + " / " + t;
            }
            return s;
        }

        /// <summary>What the card shows for a device: KeyboardMouse = the keys and mouse column, Gamepad = the controller column.</summary>
        public static List<string> Describe(ControlDevice device) =>
            Describe(device == ControlDevice.Gamepad ? Col.Pad : Col.Mouse);

        /// <summary>One "LABEL: KEY / KEY" string per line for a column (tests, captures, the PR's bindings table).</summary>
        public static List<string> Describe(Col col)
        {
            var result = new List<string>();
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            for (int i = 0; i < LineLabels.Length; i++)
            {
                string t = CellText(map, LineLabels[i], RowsFor(LineLabels[i]), col);
                result.Add(LineLabels[i] + ": " + t);
            }
            return result;
        }

        /// <summary>
        /// The k-th key shown on a line for the card's scheme in use ("" when none): the in-use column's text split on " / "
        /// (wide card), or chip k (narrow card). Tests.
        /// </summary>
        public string Shown(string label, int k)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Label != label) continue;
                if (!Wide) return _lines[i].Chips[k].gameObject.activeSelf ? _lines[i].Keys[k].text : "";
                string[] parts = Split(_lines[i].Keys[ActiveCol(Scheme)].text);
                return k < parts.Length ? parts[k] : "";
            }
            return "";
        }
    }

    /// <summary>
    /// The controls card on its own screen (DG-14). Three ways in: the pause menu's CONTROLS entry, the VIEW CONTROLS
    /// button at the top of Settings, Controls (both call Show), and once, by itself, for about 6 seconds at the first
    /// spawn of the first match ever played (ShowFirstTime; dismissed by any input; PlayerPrefs cm.controlsCardSeen
    /// keeps it from coming back unless ResetSeen is called). Modal (Show): a Void Navy wash, the card and a BACK button
    /// with the focus; Escape / Start / B close it (while the pause menu is open, PauseMenu forwards those presses).
    /// Follows the device used last and every rebind live. Screen Space - Camera canvas, sorting order 45 (above the
    /// pause menu's 40).
    /// <para>controls-1: two pages, ALL CONTROLS (the three-scheme table) and CONTROLLER (the connected controller and its
    /// button layout, stick dead zone, stick sensitivity, invert aim, vibration, CHANGE BUTTONS into Settings, Controls,
    /// and the steps to connect a controller by Bluetooth or USB). Keyboard: arrows or W A S D move the focus, ENTER or
    /// SPACE press, LEFT / RIGHT change a setting, TAB switches the page, ESC goes back; controller: d-pad or stick, A, B,
    /// LB / RB for the page.</para>
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 31, Scope = CriticalMass.Runtime.Flow.OverlayScope.Always)]   // installed once by GameBootstrap through the overlay registry
    public sealed class ControlsCardScreen : MonoBehaviour
    {
        public const string SeenKey = "cm.controlsCardSeen";
        public const float FirstTimeSeconds = 6f;
        /// <summary>Inputs in the first moments of the auto card do not dismiss it (the key that started the match).</summary>
        public const float FirstTimeGraceSeconds = 0.35f;
        const float FadeSeconds = 0.18f;

        public enum CardMode { Closed, Modal, FirstTime }

        static ControlsCardScreen _instance;

        /// <summary>The auto first-spawn card is allowed (the game flow can turn it off and call ShowFirstTime itself).</summary>
        public static bool AutoFirstTime = true;

        public static bool IsOpen => _instance != null && _instance.Mode != CardMode.Closed;
        public static ControlsCardScreen Instance => _instance;
        public static event Action Closed;

        public CardMode Mode { get; private set; } = CardMode.Closed;
        public Canvas Canvas { get; private set; }
        public ControlsCard Card { get; private set; }

        RectTransform _space;
        CanvasGroup _group;
        Image _wash;
        MenuItem _back;
        MenuItem _tabAll, _tabPad;
        readonly List<MenuItem> _presetItems = new List<MenuItem>();
        Text _presetLine;
        RectTransform _padPage;
        Text _padTitle, _padLayout, _padHelp;
        readonly List<MenuItem> _padItems = new List<MenuItem>();
        readonly List<Action> _padRefresh = new List<Action>();
        Text _hint;

        /// <summary>The page shown: false = ALL CONTROLS, true = CONTROLLER.</summary>
        public bool ControllerPage { get; private set; }
        VectorShape _timer;
        float _openedAt, _alpha;
        Action _onClosed;
        GameObject _restoreFocus;
        bool _dirty = true;
        SimDriverBehaviour _driver;
        HumanInputSource _input;
        int _nextLook;

        /// <summary>The player has seen the first-time card (or opened the card).</summary>
        public static bool Seen
        {
            get => PlayerPrefs.GetInt(SeenKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(SeenKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static void ResetSeen() => Seen = false;

        /// <summary>The screen object (created on first use, in the active scene).</summary>
        public static ControlsCardScreen Ensure()
        {
            if (_instance != null) return _instance;
            _instance = FindAnyObjectByType<ControlsCardScreen>();
            if (_instance != null) return _instance;
            var go = new GameObject("Controls Card");
            _instance = go.AddComponent<ControlsCardScreen>();
            return _instance;
        }

        /// <summary>Opens the card as a modal screen; onClosed runs when it closes (the opener restores its focus there).</summary>
        public static void Show(Action onClosed = null)
        {
            ControlsCardScreen s = Ensure();
            s.Open(CardMode.Modal, onClosed);
        }

        /// <summary>The first-time card: shows (and returns true) only if it was never seen. Marks it seen.</summary>
        public static bool ShowFirstTime()
        {
            if (Seen) return false;
            Seen = true;
            Ensure().Open(CardMode.FirstTime, null);
            return true;
        }

        public static void Hide()
        {
            if (_instance != null) _instance.Close();
        }

        void OnEnable()
        {
            if (_instance == null) _instance = this;
            ControlBindings.ActiveDeviceChanged += OnDevice;
            ControlBindings.BindingsChanged += OnBindings;
            InputSystem.onDeviceChange += OnPadChange;
        }

        void OnDisable()
        {
            ControlBindings.ActiveDeviceChanged -= OnDevice;
            ControlBindings.BindingsChanged -= OnBindings;
            InputSystem.onDeviceChange -= OnPadChange;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void OnDevice(ControlDevice d) => _dirty = true;
        void OnBindings()
        {
            _dirty = true;
            RefreshPresetLine();
        }
        void OnPadChange(InputDevice d, InputDeviceChange c)
        {
            if (d is Gamepad) _dirty = true;
        }

        void Build()
        {
            if (Canvas != null) return;
            PauseMenu.EnsureEventSystem(transform);
            var go = new GameObject("Controls Card Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.sortingOrder = 45;
            AttachCamera();
            go.AddComponent<GraphicRaycaster>();
            _group = go.AddComponent<CanvasGroup>();
            var root = (RectTransform)go.transform;

            _wash = MenuUi.Rt(root, "Wash").gameObject.AddComponent<Image>();
            MenuUi.Stretch(_wash.rectTransform);
            _wash.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.72f);

            _space = MenuUi.Rt(root, "Space");
            _space.anchorMin = _space.anchorMax = new Vector2(0.5f, 0.5f);
            _space.pivot = new Vector2(0.5f, 0.5f);
            _space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            const float w = 1400f, h = 720f, x = (1920f - w) * 0.5f, y = 130f;
            Card = ControlsCard.Build(_space, x, y, w, h);
            _back = MenuUi.Item(_space, "Back", (1920f - 320f) * 0.5f, y + h + 24f, 320f, 64f, "BACK", MenuItem.Kind.Button, UiScale.TypeHeading, true, CMTokens.Display);
            _back.Body.Radius = 16f;
            _back.Submitted = Close;
            _tabAll = MenuUi.Item(_space, "Tab All Controls", x, y - 64f, 300f, 52f, "ALL CONTROLS", MenuItem.Kind.Button, UiScale.TypeBody, true, CMTokens.Display);
            _tabAll.Submitted = () => SetPage(false);
            _tabPad = MenuUi.Item(_space, "Tab Controller", x + 316f, y - 64f, 300f, 52f, "CONTROLLER", MenuItem.Kind.Button, UiScale.TypeBody, true, CMTokens.Display);
            _tabPad.Submitted = () => SetPage(true);
            BuildPresets(x, y, w);
            BuildControllerPage(x, y, w, h);
            WireNavigation();
            _hint = MenuUi.Label(_space, "Hint", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 260f, 1020f, 1400f, 36f, "", 2f);
            _timer = MenuUi.Shape(_space, "Timer", ShapeKind.RoundRect, x + 32f, y + h - 10f, w - 64f, 6f, CMTokens.ToolBrass.Color, -1f);
            _timer.EmptyColor = CMTokens.WithAlpha(CMTokens.MeterEmpty.Color, 0.8f);
            _timer.OutlinePx = 0f;
            _timer.RimPx = 0f;
            Canvas.enabled = false;
        }

        // ---- controls-1 round 2: named keyboard layouts (owner 2026-11-13) ----

        void BuildPresets(float x, float y, float w)
        {
            float px = x + 632f, rowW = w - 632f, gap = 8f;
            float pw = (rowW - gap * (ControlPresets.All.Length - 1)) / ControlPresets.All.Length;
            _presetLine = MenuUi.Label(_space, "Layout Line", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, x, y - 108f, w, 36f, "", 2f);
            ClassCard.Fit(_presetLine, 11);
            for (int i = 0; i < ControlPresets.All.Length; i++)
            {
                ControlPreset p = ControlPresets.All[i];
                MenuItem it = MenuUi.Item(_space, "Preset " + p, px + i * (pw + gap), y - 64f, pw, 52f, ControlPresets.Title(p), MenuItem.Kind.Button, UiScale.TypeSmall, true, CMTokens.Display);
                ClassCard.Fit(it.Label, 11);
                it.Submitted = () => ApplyPreset(p);
                _presetItems.Add(it);
            }
            RefreshPresetLine();
        }

        /// <summary>One press: the layout's keys, saved; the table and every prompt redraw (BindingsChanged).</summary>
        public void ApplyPreset(ControlPreset p)
        {
            ControlPresets.Apply(InputSystem.actions, p);
            RefreshPresetLine();
            _dirty = true;
        }

        /// <summary>The layout in use, in words: a preset, or CUSTOM once a key was changed on top of it.</summary>
        public static string PresetLineText(InputActionAsset asset)
        {
            ControlPreset? c = asset != null ? ControlPresets.Current(asset) : null;
            if (c.HasValue) return "LAYOUT: " + ControlPresets.Title(c.Value) + ".  " + ControlPresets.Summary(c.Value);
            bool custom = false;
            InputActionMap map = asset != null ? ControlBindings.Map(asset) : null;
            if (map != null)
                foreach (InputBinding bd in map.bindings)
                    if (bd.overridePath != null) custom = true;
            return custom ? "LAYOUT: CUSTOM (your own keys). Pick a layout to start again from it, or change single keys in Settings, Controls."
                : "LAYOUT: ALL THREE AT ONCE. Arrows + Z X C, WASD + J K L and WASD + mouse all work; prompts follow the keys you press.";
        }

        void RefreshPresetLine()
        {
            if (_presetLine != null) HudText.Set(_presetLine, PresetLineText(InputSystem.actions));
        }

        // ---- controls-1: the CONTROLLER page ----

        void BuildControllerPage(float x, float y, float w, float h)
        {
            VectorShape body = MenuUi.Panel(_space, "Controller Page", x, y, w, h);
            _padPage = body.rectTransform;
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color;
            MenuUi.Label(_padPage, "Title", CMTokens.Display, 40, cream, TextAnchor.MiddleLeft, 32f, 18f, w * 0.5f, 52f, "CONTROLLER", 4f);
            _padTitle = MenuUi.Label(_padPage, "Detected", CMTokens.Ui, 20, brass, TextAnchor.MiddleRight, w * 0.35f, 18f, w * 0.65f - 32f, 52f, "", 2f);
            ClassCard.Fit(_padTitle, 12);
            // The layout diagram: every controller control and what it does, left half of the pad then right half.
            MenuUi.Label(_padPage, "Layout Head", CMTokens.Ui, 20, brass, TextAnchor.MiddleLeft, 32f, 86f, 620f, 32f, "BUTTON LAYOUT", 2f);
            _padLayout = MenuUi.Label(_padPage, "Layout", CMTokens.Mono, 18, cream, TextAnchor.UpperLeft, 32f, 122f, 620f, 380f, "", 0f);
            // Settings, right column.
            float sx = 690f, sw = w - sx - 32f, sy = 86f;
            MenuUi.Label(_padPage, "Settings Head", CMTokens.Ui, 20, brass, TextAnchor.MiddleLeft, sx, sy, sw, 32f, "SETTINGS", 2f);
            sy += 40f;
            AddCycle("Dead Zone", sx, sy, sw, () => "STICK DEAD ZONE   " + ControllerSettings.DeadZoneNames[ControllerSettings.DeadZoneIndex],
                d => ControllerSettings.DeadZoneIndex = Wrap(ControllerSettings.DeadZoneIndex + d, ControllerSettings.DeadZones.Length));
            sy += 58f;
            AddCycle("Sensitivity", sx, sy, sw, () => "STICK SENSITIVITY   " + ControllerSettings.SensitivityNames[ControllerSettings.SensitivityIndex],
                d => ControllerSettings.SensitivityIndex = Wrap(ControllerSettings.SensitivityIndex + d, ControllerSettings.Curves.Length));
            sy += 58f;
            AddCycle("Invert", sx, sy, sw, () => "INVERT AIM UP / DOWN   " + (ControllerSettings.InvertAimY ? "ON" : "OFF"),
                d => ControllerSettings.InvertAimY = !ControllerSettings.InvertAimY);
            sy += 58f;
            AddCycle("Vibration", sx, sy, sw, () => "VIBRATION   " + (ControllerSettings.Vibration ? "ON" : "OFF"),
                d => ControllerSettings.Vibration = !ControllerSettings.Vibration);
            sy += 58f;
            MenuItem rebind = MenuUi.Item(_padPage, "Change Buttons", sx, sy, sw * 0.5f - 6f, 48f, "CHANGE BUTTONS", MenuItem.Kind.Button, UiScale.TypeSmall, true, CMTokens.Display);
            rebind.Submitted = OpenRebinding;
            _padItems.Add(rebind);
            MenuItem reset = MenuUi.Item(_padPage, "Reset Controller", sx + sw * 0.5f + 6f, sy, sw * 0.5f - 6f, 48f, "RESET SETTINGS", MenuItem.Kind.Button, UiScale.TypeSmall, true, CMTokens.Display);
            reset.Submitted = () =>
            {
                ControllerSettings.ResetAll();
                RefreshControllerPage();
            };
            _padItems.Add(reset);
            // Connect a controller (the full steps: docs/CONTROLLERS.md).
            MenuUi.Label(_padPage, "Help Head", CMTokens.Ui, 20, brass, TextAnchor.MiddleLeft, 32f, 510f, w - 64f, 32f, "CONNECT A CONTROLLER", 2f);
            _padHelp = MenuUi.Label(_padPage, "Help", CMTokens.Ui, 17, cream, TextAnchor.UpperLeft, 32f, 546f, w - 64f, h - 560f, ConnectHelp, 0f);
            ClassCard.Fit(_padHelp, 11);
            _padPage.gameObject.SetActive(false);
        }

        /// <summary>The help panel's plain steps (no brand names; the button names are on the controllers themselves).</summary>
        public const string ConnectHelp =
            "USB: plug the controller in; it works at once.\n" +
            "Bluetooth, A B X Y controller: hold the pairing button on its top edge until the centre light flashes fast. " +
            "Cross / circle controller: hold the small Create (or Share) button and the centre button together until the light bar flashes.\n" +
            "Mac: System Settings, Bluetooth, then Connect next to the controller.   Windows: Settings, Bluetooth and devices, Add device, Bluetooth.\n" +
            "Unplug or switch off mid-match and the game pauses; connect again and it carries on.";

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        void AddCycle(string name, float x, float y, float w, Func<string> text, Action<int> change)
        {
            MenuItem it = MenuUi.Item(_padPage, name, x, y, w, 48f, "", MenuItem.Kind.Stepper, UiScale.TypeSmall, false, CMTokens.Display);
            Action refresh = () => HudText.Set(it.Label, text());
            it.Submitted = () =>
            {
                change(1);
                refresh();
            };
            it.Stepped = d =>
            {
                change(d >= 0 ? 1 : -1);
                refresh();
            };
            _padItems.Add(it);
            _padRefresh.Add(refresh);
            refresh();
        }

        void OpenRebinding()
        {
            PauseMenu pm = FindAnyObjectByType<PauseMenu>();
            Close();
            if (pm != null)
            {
                if (!pm.IsOpen) pm.Pause();
                pm.ShowSettings(PauseMenu.Tab.Controls);
            }
        }

        /// <summary>The controller page's detected-controller line, layout diagram and setting labels from the live state.</summary>
        public void RefreshControllerPage()
        {
            if (_padPage == null) return;
            Gamepad pad = Gamepad.current;
            int n = Gamepad.all.Count;
            HudText.Set(_padTitle, ControllerSettings.Describe(pad) + (n > 1 ? $"   ({n} connected)" : ""));
            HudText.Set(_padLayout, LayoutText());
            for (int i = 0; i < _padRefresh.Count; i++) _padRefresh[i]();
        }

        /// <summary>One line per controller control: its label in the connected family and the actions on it.</summary>
        public static string LayoutText()
        {
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            string[] controls =
            {
                "leftTrigger", "leftShoulder", "leftStick", "leftStickPress", "dpad/up", "dpad/left", "dpad/down", "dpad/right", "select",
                "rightTrigger", "rightShoulder", "rightStick", "rightStickPress", "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "start",
            };
            var sb = new System.Text.StringBuilder();
            for (int c = 0; c < controls.Length; c++)
            {
                string path = "<Gamepad>/" + controls[c];
                var names = new List<string>();
                if (map != null)
                    foreach (InputAction a in map.actions)
                        foreach (InputBinding b in a.bindings)
                            if (!b.isComposite && string.Equals(b.effectivePath, path, StringComparison.OrdinalIgnoreCase) && !names.Contains(Verb(a.name))) names.Add(Verb(a.name));
                if (controls[c] == "rightStick" && !names.Contains("Aim")) names.Add("Aim");
                if (controls[c] == "buttonSouth") names.Add("Menus: confirm");
                if (controls[c] == "buttonEast") names.Add("Menus: back");
                string key = ControlBindings.DisplayPath(path);
                sb.Append(key.PadRight(12)).Append(names.Count > 0 ? string.Join(", ", names) : "-").Append('\n');
            }
            return sb.ToString();
        }

        static string Verb(string action)
        {
            switch (action)
            {
                case "DigPlace": return "Dig / place";
                case "SwitchDigPlace": return "Switch dig / place";
                case "OpenStation": return "Base station";
                case "HudCursor": return "HUD cursor";
                case "MinimapZoom": return "Minimap zoom";
                case "ShowTerrain": return "Show usable terrain";
                case "Ability": return "Class ability";
                case "Interact": return "Interact / carry";
                case "Move": return "Move";
                default: return action;
            }
        }

        /// <summary>Shows a page and moves the focus onto its tab.</summary>
        public void SetPage(bool controller)
        {
            Build();
            ControllerPage = controller;
            Card.Root.gameObject.SetActive(!controller);
            _padPage.gameObject.SetActive(controller);
            ShowPresets(!controller && Mode == CardMode.Modal);
            if (controller) RefreshControllerPage();
            WireNavigation();
            UpdateHint();
            EventSystem es = EventSystem.current;
            if (es != null && Mode == CardMode.Modal) es.SetSelectedGameObject((controller ? _tabPad : _tabAll).gameObject);
        }

        void ShowPresets(bool on)
        {
            for (int i = 0; i < _presetItems.Count; i++)
                if (_presetItems[i].gameObject.activeSelf != on) _presetItems[i].gameObject.SetActive(on);
            if (_presetLine != null && _presetLine.gameObject.activeSelf != on) _presetLine.gameObject.SetActive(on);
            if (on) RefreshPresetLine();
        }

        /// <summary>Explicit focus order: the two tabs, the page's items (controller page), BACK; nothing is mouse-only.</summary>
        void WireNavigation()
        {
            if (_back == null) return;
            var order = new List<Selectable> { _tabAll };
            if (ControllerPage) order.AddRange(_padItems);
            order.Add(_back);
            Selectable firstBelow = order.Count > 1 ? order[1] : _back;
            _tabAll.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = _tabPad, selectOnDown = firstBelow, selectOnUp = _back };
            bool presets = !ControllerPage && _presetItems.Count > 0 && _presetItems[0].gameObject.activeInHierarchy;
            _tabPad.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _tabAll, selectOnRight = presets ? _presetItems[0] : null, selectOnDown = firstBelow, selectOnUp = _back };
            // The layout buttons continue the tab row: left and right walk it, down goes to the page, up to BACK.
            for (int i = 0; i < _presetItems.Count; i++)
                _presetItems[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i == 0 ? (Selectable)_tabPad : _presetItems[i - 1],
                    selectOnRight = i + 1 < _presetItems.Count ? _presetItems[i + 1] : null,
                    selectOnDown = firstBelow,
                    selectOnUp = _back,
                };
            for (int i = 1; i < order.Count; i++)
            {
                Selectable up = order[i - 1] == _tabAll ? (ControllerPage ? (Selectable)_tabPad : _tabAll) : order[i - 1];
                Selectable down = i + 1 < order.Count ? order[i + 1] : (ControllerPage ? (Selectable)_tabPad : _tabAll);
                // The two buttons sharing a row (CHANGE BUTTONS, RESET SETTINGS) also reach each other sideways.
                var n = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down };
                if (order[i] is MenuItem mi && mi.ItemKind == MenuItem.Kind.Stepper) { n.selectOnLeft = null; n.selectOnRight = null; }
                order[i].navigation = n;
            }
        }

        void AttachCamera()
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                if (Canvas.renderMode != RenderMode.ScreenSpaceCamera) Canvas.renderMode = RenderMode.ScreenSpaceCamera;
                if (Canvas.worldCamera != cam)
                {
                    Canvas.worldCamera = cam;
                    Canvas.planeDistance = Mathf.Max(0.38f, cam.nearClipPlane * 1.25f);
                }
            }
            else if (Canvas.worldCamera == null && Canvas.renderMode != RenderMode.ScreenSpaceOverlay) Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        /// <summary>Canvas scale for the camera's pixel size (the capture tool calls it after retargeting).</summary>
        public void UpdateScale()
        {
            if (Canvas == null) return;
            AttachCamera();
            UnityEngine.Camera cam = Canvas.renderMode == RenderMode.ScreenSpaceCamera ? Canvas.worldCamera : null;
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(Canvas.scaleFactor - s) > 1e-4f) Canvas.scaleFactor = s;
        }

        void Open(CardMode mode, Action onClosed)
        {
            Build();
            Mode = mode;
            _onClosed = onClosed;
            _openedAt = Time.unscaledTime;
            _alpha = 0f;
            _group.alpha = 0f;
            Canvas.enabled = true;
            bool modal = mode == CardMode.Modal;
            _wash.raycastTarget = modal;
            _wash.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, modal ? 0.72f : 0.35f);
            _group.blocksRaycasts = modal;
            _group.interactable = modal;
            _back.gameObject.SetActive(modal);
            _tabAll.gameObject.SetActive(modal);
            _tabPad.gameObject.SetActive(modal);
            ShowPresets(modal && !ControllerPage);
            WireNavigation();
            if (!modal && ControllerPage) SetPage(false);
            _timer.gameObject.SetActive(!modal);
            _timer.Fill01 = 1f;
            _dirty = true;
            ControlBindings.Sample();
            Card.Refresh(ControlBindings.ActiveDevice);
            UpdateHint();
            UpdateScale();
            if (modal)
            {
                EventSystem es = EventSystem.current;
                _restoreFocus = es != null ? es.currentSelectedGameObject : null;
                if (es != null)
                {
                    es.SetSelectedGameObject(null);
                    _back.Select();
                }
                // controls-1: the controller page opens first when the player is on a controller.
                if (ControlBindings.ActiveDevice == ControlDevice.Gamepad && !ControllerPage) SetPage(true);
            }
        }

        public void Close()
        {
            if (Mode == CardMode.Closed) return;
            bool modal = Mode == CardMode.Modal;
            Mode = CardMode.Closed;
            if (Canvas != null) Canvas.enabled = false;
            EventSystem es = EventSystem.current;
            if (modal && es != null && es.currentSelectedGameObject == _back.gameObject)
            {
                es.SetSelectedGameObject(null);
                if (_restoreFocus != null && _restoreFocus.activeInHierarchy) es.SetSelectedGameObject(_restoreFocus);
            }
            _restoreFocus = null;
            // The press that closed the card must not also press what the focus returns to.
            MenuItem.SubmitBlockedUntil = Mathf.Max(MenuItem.SubmitBlockedUntil, Time.unscaledTime + 0.15f);
            Action cb = _onClosed;
            _onClosed = null;
            cb?.Invoke();
            Closed?.Invoke();
        }

        void UpdateHint()
        {
            if (_hint == null) return;
            bool pad = ControlBindings.ActiveDevice == ControlDevice.Gamepad;
            string s = Mode == CardMode.FirstTime ? (pad ? "PRESS ANY BUTTON TO PLAY" : "PRESS ANY KEY TO PLAY")
                : pad ? "A SELECT   ·   LB / RB PAGE   ·   B BACK" : "ENTER SELECT   ·   TAB PAGE   ·   ESC BACK";
            HudText.Set(_hint, s);
        }

        void Update()
        {
            if (Mode == CardMode.Closed)
            {
                WatchFirstSpawn();
                return;
            }
            ControlBindings.Sample();
            UpdateScale();
            if (_dirty)
            {
                _dirty = false;
                Card.Refresh(ControlBindings.ActiveDevice);
                if (ControllerPage) RefreshControllerPage();
                UpdateHint();
            }
            float age = Time.unscaledTime - _openedAt;
            _alpha = Mathf.Min(1f, age / FadeSeconds);
            if (Mathf.Abs(_group.alpha - _alpha) > 1e-3f) _group.alpha = _alpha;

            if (Mode == CardMode.FirstTime)
            {
                _timer.Fill01 = 1f - Mathf.Clamp01(age / FirstTimeSeconds);
                if (age >= FirstTimeSeconds || (age >= FirstTimeGraceSeconds && AnyInput())) Close();
                return;
            }
            // Modal: Escape / Start / B close, unless the open pause menu owns those presses (it forwards them).
            Keyboard kb = Keyboard.current;
            Gamepad gp = Gamepad.current;
            // controls-1: TAB, LB or RB switch the page (also while the pause menu owns Escape).
            if ((kb != null && kb.tabKey.wasPressedThisFrame) || (gp != null && (gp.leftShoulder.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame)))
                SetPage(!ControllerPage);
            if (PauseOwnsInput()) return;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && (gp.buttonEast.wasPressedThisFrame || gp.startButton.wasPressedThisFrame)))
                Close();
            else if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && AnyNavigation()) _back.Select();
        }

        void FindGame()
        {
            if (_input == null) _input = GetComponent<HumanInputSource>();
            if (_driver == null) _driver = _input != null ? _input.GetComponent<SimDriverBehaviour>() : FindAnyObjectByType<SimDriverBehaviour>();
            if (_input == null && _driver != null) _input = _driver.GetComponent<HumanInputSource>();
        }

        bool PauseOwnsInput()
        {
            FindGame();
            if (_input == null) return false;
            PauseMenu pm = _input.GetComponent<PauseMenu>();
            return pm != null && pm.IsOpen;
        }

        static bool AnyNavigation()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                               kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) return true;
            Gamepad gp = Gamepad.current;
            return gp != null && (gp.dpad.up.wasPressedThisFrame || gp.dpad.down.wasPressedThisFrame);
        }

        /// <summary>Any key, mouse button or controller button / stick this frame.</summary>
        public static bool AnyInput()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            Mouse m = Mouse.current;
            if (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame)) return true;
            Gamepad gp = Gamepad.current;
            return gp != null && ControlBindings.GamepadUsed(gp);
        }

        /// <summary>
        /// First spawn of the first match ever: actor 0 alive and human-driven, ticking, no title, pause, loading or class
        /// screen in the way. Player builds and the editor; never in batch mode, tests or the capture tool's matches.
        /// </summary>
        void WatchFirstSpawn()
        {
            if (!AutoFirstTime || Application.isBatchMode || SimDriverBehaviour.CaptureMode) return;
            if (Time.frameCount < _nextLook) return;
            _nextLook = Time.frameCount + 10;
            if (Seen) return;
            FindGame();
            if (_driver == null || _driver.World == null || _input == null || _driver.HumanAutopilot) return;
            if (_driver.GetComponent<TitleScreen>() != null || _input.State.Paused || LoadingScreen.IsOpen || ClassSelect.IsOpen) return;
            CriticalMass.Sim.Core.World w = _driver.World;
            if (w.Tick < 1 || w.ActorCount < 1 || !w.Actors[0].Alive) return;
            ShowFirstTime();
        }
    }

    /// <summary>Creates the controls card screen with the game on boot and after every scene load (MenuInstaller pattern).</summary>
    public static class ControlsCardInstaller
    {
        // Integration batch 2: installed by GameBootstrap through the DG-01 overlay registry ([GameOverlay]); Attach stays for tests.
        public static ControlsCardScreen Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            HumanInputSource input = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
            if (input == null) return null;
            ControlsCardScreen s = input.GetComponent<ControlsCardScreen>();
            if (s == null) s = input.gameObject.AddComponent<ControlsCardScreen>();
            return s;
        }
    }
}

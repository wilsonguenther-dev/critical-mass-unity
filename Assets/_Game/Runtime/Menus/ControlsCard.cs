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
    /// The one-screen controls card (DG-14): every game action with its key glyphs, read from the CURRENT bindings (after
    /// rebinding) through ControlBindings, for the device used last (keyboard and mouse, or controller), with the line
    /// "Change these in Settings, Controls" under it. A widget: the loading screen embeds one, ControlsCardScreen shows
    /// one on its own. It redraws only when the device or a binding changes (events), never per frame.
    /// </summary>
    public sealed class ControlsCard
    {
        public const string Footer = "Change these in Settings, Controls";

        /// <summary>One card line: a label and the ControlBindings rows whose slots it shows (Move merges four rows).</summary>
        sealed class Line
        {
            public string Label;
            public int[] Rows;
            public Text Name;
            public readonly Text[] Keys = new Text[2];
            public readonly VectorShape[] Chips = new VectorShape[2];
        }

        public readonly RectTransform Root;
        public readonly VectorShape Body;
        public ControlDevice Device { get; private set; }
        readonly List<Line> _lines = new List<Line>();
        Text _device;
        readonly float _chipW;

        static Color Navy => CMTokens.VoidNavy.Color;
        static Color Cream => CMTokens.WarmCream.Color;
        static Color Brass => CMTokens.ToolBrass.Color;

        ControlsCard(RectTransform root, VectorShape body, float chipW)
        {
            Root = root;
            Body = body;
            _chipW = chipW;
        }

        /// <summary>Card lines in screen order: label and the indices into ControlBindings.Rows.</summary>
        public static readonly string[] LineLabels =
            { "Move", "Aim", "Attack", "Class ability", "Jump", "Interact / carry", "Dig / place", "Switch dig / place", "Scoreboard", "Pause menu" };

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

        /// <summary>Builds a card at (x, y) from parent's top-left (1920x1080 reference units); two columns when w &gt;= 900.</summary>
        public static ControlsCard Build(Transform parent, float x, float y, float w, float h, string name = "Controls Card")
        {
            VectorShape body = MenuUi.Panel(parent, name, x, y, w, h);
            bool two = w >= 900f;
            float pad = 32f, colGap = 40f;
            float colW = two ? (w - pad * 2f - colGap) * 0.5f : w - pad * 2f;
            float chipW = Mathf.Min(150f, (colW - 210f - 10f) * 0.5f);
            var card = new ControlsCard(body.rectTransform, body, chipW);
            RectTransform rt = body.rectTransform;
            MenuUi.Label(rt, "Title", CMTokens.Display, two ? 44 : 34, Cream, TextAnchor.MiddleLeft, pad, 22f, w * 0.5f, 56f, "CONTROLS", 4f);
            card._device = MenuUi.Label(rt, "Device", CMTokens.Ui, two ? 22 : 18, Brass, TextAnchor.MiddleRight, w * 0.45f, 22f, w * 0.55f - pad, 56f, "", 2f);
            VectorShape rule = MenuUi.Shape(rt, "Rule", ShapeKind.RoundRect, pad, 86f, w - pad * 2f, 4f, CMTokens.WithAlpha(Cream, 0.35f), 2f);
            rule.OutlinePx = 0f;
            rule.RimPx = 0f;

            int perCol = two ? (LineLabels.Length + 1) / 2 : LineLabels.Length;
            float top = 104f, bottom = h - 64f;
            float lineH = Mathf.Min(58f, (bottom - top) / perCol);
            for (int i = 0; i < LineLabels.Length; i++)
            {
                int col = two ? i / perCol : 0, row = two ? i % perCol : i;
                float lx = pad + col * (colW + colGap), ly = top + row * lineH;
                var line = new Line { Label = LineLabels[i], Rows = RowsFor(LineLabels[i]) };
                line.Name = MenuUi.Label(rt, "Line " + line.Label, CMTokens.Ui, lineH >= 50f ? 24 : 20, Cream, TextAnchor.MiddleLeft, lx, ly, 210f, lineH - 8f,
                    line.Label.ToUpperInvariant(), 2f);
                for (int k = 0; k < 2; k++)
                {
                    float cx = lx + colW - (2 - k) * chipW - (1 - k) * 10f;
                    VectorShape chip = MenuUi.Shape(rt, $"Chip {line.Label} {k}", ShapeKind.RoundRect, cx, ly + 4f, chipW, lineH - 16f, Navy, 8f);
                    chip.OutlinePx = 2f;
                    chip.RimPx = 2f;
                    line.Chips[k] = chip;
                    line.Keys[k] = MenuUi.Label(chip.transform, "Key", CMTokens.Mono, lineH >= 50f ? 20 : 17, Cream, TextAnchor.MiddleCenter, 0f, 0f, chipW, lineH - 16f, "", 0f);
                    ClassCard.Fit(line.Keys[k], 11);
                }
                card._lines.Add(line);
            }
            MenuUi.Label(rt, "Footer", CMTokens.Ui, two ? 22 : 18, CMTokens.WithAlpha(Cream, 0.9f), TextAnchor.MiddleCenter, pad, h - 56f, w - pad * 2f, 40f, Footer, 2f);
            card.Refresh(ControlBindings.ActiveDevice);
            return card;
        }

        /// <summary>Redraws every glyph for a device from the current bindings.</summary>
        public void Refresh(ControlDevice device)
        {
            Device = device;
            HudText.Set(_device, device == ControlDevice.Gamepad ? "CONTROLLER" : "KEYBOARD AND MOUSE");
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            for (int i = 0; i < _lines.Count; i++)
            {
                Line l = _lines[i];
                string a, b;
                Glyphs(map, l, device, out a, out b);
                SetChip(l, 0, a);
                SetChip(l, 1, b);
            }
        }

        void SetChip(Line l, int k, string text)
        {
            bool on = !string.IsNullOrEmpty(text);
            // A single glyph sits in the right-hand chip so every key column lines up.
            int slot = k;
            if (l.Chips[k].gameObject.activeSelf != on) l.Chips[k].gameObject.SetActive(on);
            if (on) HudText.Set(l.Keys[slot], text);
        }

        /// <summary>The glyph text(s) of a line on a device ("" = none). Public through Describe for tests.</summary>
        static void Glyphs(InputActionMap map, Line l, ControlDevice device, out string first, out string second)
        {
            first = second = "";
            if (map == null) return;
            if (l.Label == "Move")
            {
                if (device == ControlDevice.Gamepad)
                {
                    first = SlotText(map, l.Rows[4], device, 0);
                    return;
                }
                first = Join(map, l.Rows, device, 0);
                second = Join(map, l.Rows, device, 1);
                if (second == "UP LEFT DOWN RIGHT") second = "ARROWS";
                return;
            }
            first = SlotText(map, l.Rows[0], device, 0);
            second = SlotText(map, l.Rows[0], device, 1);
            if (second == first) second = "";
        }

        static string Join(InputActionMap map, int[] rows, ControlDevice device, int slot)
        {
            string s = "";
            for (int i = 0; i < 4; i++)
            {
                string t = SlotText(map, rows[i], device, slot);
                if (t.Length == 0) return "";
                s = s.Length == 0 ? t : s + " " + t;
            }
            return s;
        }

        static string SlotText(InputActionMap map, int row, ControlDevice device, int slot)
        {
            if (row < 0 || row >= ControlBindings.Rows.Length) return "";
            ControlRow r = ControlBindings.Rows[row];
            RebindSlot[] set = device == ControlDevice.Gamepad ? r.Gamepad : r.Keyboard;
            if (slot >= set.Length) return "";
            InputAction a = map.FindAction(set[slot].Action, false);
            int idx = ControlBindings.FindBindingIndex(a, set[slot]);
            if (idx < 0) return "";
            string t = ControlBindings.Display(a, idx);
            return t == "-" ? "" : t;
        }

        /// <summary>What the card shows on a device: one "LABEL: KEY / KEY" string per line (tests, captures).</summary>
        public static List<string> Describe(ControlDevice device)
        {
            var result = new List<string>();
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            for (int i = 0; i < LineLabels.Length; i++)
            {
                var l = new Line { Label = LineLabels[i], Rows = RowsFor(LineLabels[i]) };
                Glyphs(map, l, device, out string a, out string b);
                result.Add(l.Label + ": " + a + (b.Length > 0 ? " / " + b : ""));
            }
            return result;
        }

        /// <summary>The glyph shown on a line's chip k for the card's current device ("" when hidden). Tests.</summary>
        public string Shown(string label, int k)
        {
            for (int i = 0; i < _lines.Count; i++)
                if (_lines[i].Label == label) return _lines[i].Chips[k].gameObject.activeSelf ? _lines[i].Keys[k].text : "";
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
    /// </summary>
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
        Text _hint;
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
        }

        void OnDisable()
        {
            ControlBindings.ActiveDeviceChanged -= OnDevice;
            ControlBindings.BindingsChanged -= OnBindings;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void OnDevice(ControlDevice d) => _dirty = true;
        void OnBindings() => _dirty = true;

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

            const float w = 1240f, h = 640f, x = (1920f - w) * 0.5f, y = 150f;
            Card = ControlsCard.Build(_space, x, y, w, h);
            _back = MenuUi.Item(_space, "Back", (1920f - 320f) * 0.5f, y + h + 30f, 320f, 72f, "BACK", MenuItem.Kind.Button, 32, true, CMTokens.Display);
            _back.Body.Radius = 16f;
            _back.Submitted = Close;
            _back.navigation = new Navigation { mode = Navigation.Mode.None };
            _hint = MenuUi.Label(_space, "Hint", CMTokens.Ui, 20, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 260f, 1020f, 1400f, 36f, "", 2f);
            _timer = MenuUi.Shape(_space, "Timer", ShapeKind.RoundRect, x + 32f, y + h - 10f, w - 64f, 6f, CMTokens.ToolBrass.Color, -1f);
            _timer.EmptyColor = CMTokens.WithAlpha(CMTokens.MeterEmpty.Color, 0.8f);
            _timer.OutlinePx = 0f;
            _timer.RimPx = 0f;
            Canvas.enabled = false;
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
                : pad ? "A OR B BACK" : "ENTER OR ESC BACK";
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
            if (PauseOwnsInput()) return;
            Keyboard kb = Keyboard.current;
            Gamepad gp = Gamepad.current;
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
            if (kb != null && (kb.tabKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)) return true;
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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

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

using System;
using System.Collections.Generic;
using CriticalMass.Net.Replay;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Replay
{
    /// <summary>
    /// b13-replay: the main menu's REPLAYS line. A contained list of the saved matches, newest first, one page of PageSize
    /// rows at a time (date, map, mode and size, winner, length; no scrolling: PREV / NEXT pages through the at most 20
    /// files), in the menu's own sticker style, keyboard and controller navigable (up / down, Enter / A, Escape / B).
    /// Choosing a replay this build can play starts it; one from another version shows a plain message instead and
    /// nothing loads.
    /// </summary>
    [MenuEntry(MenuId, "REPLAYS", 55)]
    public sealed class ReplaysMenuScreen : MonoBehaviour, IMenuScreen, IMenuBack
    {
        public const string MenuId = "replays";
        public const int PageSize = 8;
        const float RowX = 260f, RowY = 190f, RowW = 1400f, RowH = 70f, RowGap = 10f;

        public static ReplaysMenuScreen Instance { get; private set; }
        public bool IsOpen => _open;
        /// <summary>The entries listed when the screen last opened (newest first).</summary>
        public List<ReplayEntry> Entries { get; private set; } = new List<ReplayEntry>();
        public int Page { get; private set; }
        /// <summary>The message line ("" when none).</summary>
        public string Notice { get; private set; } = "";

        Action _onClosed;
        bool _built, _open;
        Canvas _canvas;
        readonly List<MenuItem> _rows = new List<MenuItem>();
        MenuItem _prev, _next, _back;
        Text _notice, _pageText, _empty, _hint;
        ControlDevice _hintDevice = (ControlDevice)(-1);

        void Awake() { if (Instance == null) Instance = this; }

        void OnDestroy()
        {
            if (_open) Close(false);
            if (Instance == this) Instance = null;
        }

        public void Open(Action onClosed)
        {
            _onClosed = onClosed;
            Build();
            Entries = ReplayStore.List();
            Page = 0;
            Notice = "";
            _open = true;
            _canvas.enabled = true;
            OverlayGuard.Begin(gameObject, ConsumePause);
            MenuItem.SubmitBlockedUntil = Time.unscaledTime + 0.15f;   // the key that opened the screen must not press a row
            ShowPage();
        }

        public void Back() => Close(true);

        bool ConsumePause()
        {
            Close(true);
            return true;
        }

        void Close(bool raise)
        {
            _open = false;
            if (_canvas != null) _canvas.enabled = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            OverlayGuard.End();
            if (!raise) return;
            Action a = _onClosed;
            _onClosed = null;
            a?.Invoke();
        }

        void Update()
        {
            OverlayGuard.Tick();
            if (!_open) return;
            UpdateScale();
            ControlBindings.Sample();
            ControlDevice d = ControlBindings.ActiveDevice;
            if (d != _hintDevice)
            {
                _hintDevice = d;
                HudText.Set(_hint, d == ControlDevice.Gamepad ? "A  WATCH     B  BACK" : "ENTER  WATCH     ESC  BACK");
            }
            Gamepad pad = Gamepad.current;
            Keyboard kb = Keyboard.current;
            if ((pad != null && pad.buttonEast.wasPressedThisFrame) || (kb != null && kb.escapeKey.wasPressedThisFrame))
            {
                Close(true);
                return;
            }
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == null && NavigationPressed()) Select(FirstFocus());
        }

        static bool NavigationPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.leftStick.ReadValue().sqrMagnitude > 0.25f);
        }

        static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            s.Select();
        }

        void UpdateScale()
        {
            UnityEngine.Camera cam = _canvas.worldCamera;
            if (cam == null) { cam = UnityEngine.Camera.main; if (cam != null) _canvas.worldCamera = cam; }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(_canvas.scaleFactor - s) > 1e-4f) _canvas.scaleFactor = s;
        }

        /// <summary>Pages for n entries (at least 1; pure; tests).</summary>
        public static int PageCount(int n) => Math.Max(1, (n + PageSize - 1) / PageSize);

        void ShowPage()
        {
            int pages = PageCount(Entries.Count);
            Page = Mathf.Clamp(Page, 0, pages - 1);
            for (int i = 0; i < _rows.Count; i++)
            {
                int k = Page * PageSize + i;
                bool on = k < Entries.Count;
                _rows[i].gameObject.SetActive(on);
                if (!on) continue;
                ReplayEntry e = Entries[k];
                HudText.Set(_rows[i].Label, ReplayWords.Row(e));
                _rows[i].Label.color = e.Compat == ReplayCompatibility.Ok ? MenuItem.IdleText : CMTokens.WithAlpha(MenuItem.IdleText, 0.55f);
            }
            _empty.gameObject.SetActive(Entries.Count == 0);
            _prev.gameObject.SetActive(pages > 1);
            _next.gameObject.SetActive(pages > 1);
            _pageText.gameObject.SetActive(pages > 1);
            HudText.Set(_pageText, "PAGE " + (Page + 1) + " OF " + pages);
            HudText.Set(_notice, Notice);
            Wire();
            Select(FirstFocus());
        }

        Selectable FirstFocus()
        {
            foreach (MenuItem r in _rows) if (r.gameObject.activeSelf) return r;
            return _back;
        }

        /// <summary>Explicit up / down navigation through the visible rows, then the bottom row (PREV, NEXT, BACK).</summary>
        void Wire()
        {
            var col = new List<Selectable>();
            foreach (MenuItem r in _rows) if (r.gameObject.activeSelf) col.Add(r);
            var bottom = new List<Selectable>();
            if (_prev.gameObject.activeSelf) bottom.Add(_prev);
            if (_next.gameObject.activeSelf) bottom.Add(_next);
            bottom.Add(_back);
            Selectable lastRow = col.Count > 0 ? col[col.Count - 1] : null;
            for (int i = 0; i < col.Count; i++)
            {
                var n = new Navigation { mode = Navigation.Mode.Explicit };
                n.selectOnUp = i > 0 ? col[i - 1] : _back;
                n.selectOnDown = i < col.Count - 1 ? col[i + 1] : bottom[bottom.Count - 1];
                col[i].navigation = n;
            }
            for (int i = 0; i < bottom.Count; i++)
            {
                var n = new Navigation { mode = Navigation.Mode.Explicit };
                n.selectOnLeft = i > 0 ? bottom[i - 1] : null;
                n.selectOnRight = i < bottom.Count - 1 ? bottom[i + 1] : null;
                n.selectOnUp = lastRow;
                n.selectOnDown = col.Count > 0 ? col[0] : null;
                bottom[i].navigation = n;
            }
        }

        void Choose(int row)
        {
            int k = Page * PageSize + row;
            if (k < 0 || k >= Entries.Count) return;
            ReplayEntry e = Entries[k];
            if (e.Compat != ReplayCompatibility.Ok)
            {
                Notice = e.Why;
                HudText.Set(_notice, Notice);
                return;
            }
            ReplayData d = ReplayStore.Load(e.Path, out string why);
            if (d == null || !ReplayPlayback.Play(d, out why))
            {
                Notice = why ?? ReplayCompat.Message(ReplayCompatibility.Damaged, null);
                HudText.Set(_notice, Notice);
                return;
            }
            Close(false);   // the replay's reload replaces the menus
        }

        /// <summary>Capture tool: choose the row at index (0 = the newest) without a key press.</summary>
        public void ChooseForCapture(int index)
        {
            Page = index / PageSize;
            ShowPage();
            Choose(index % PageSize);
        }

        void Build()
        {
            if (_built) return;
            _built = true;
            PauseMenu.EnsureEventSystem(transform);
            var go = new GameObject("Replays Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = 50;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = cam != null ? Mathf.Max(0.38f, cam.nearClipPlane * 1.25f) : 0.38f;
            go.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)go.transform;
            Image wash = MenuUi.Rt(root, "Wash").gameObject.AddComponent<Image>();
            MenuUi.Stretch(wash.rectTransform);
            wash.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.9f);
            wash.raycastTarget = true;
            RectTransform space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            Color cream = CMTokens.WarmCream.Color;

            MenuUi.Label(space, "Title", CMTokens.Display, UiScale.TypeTitle, cream, TextAnchor.MiddleCenter, 460f, 56f, 1000f, 90f, "REPLAYS", 4f);
            MenuUi.Panel(space, "List Panel", RowX - 30f, RowY - 30f, RowW + 60f, PageSize * (RowH + RowGap) + 50f);
            for (int i = 0; i < PageSize; i++)
            {
                int row = i;
                MenuItem it = MenuUi.Item(space, "Row " + i, RowX, RowY + i * (RowH + RowGap), RowW, RowH, "", MenuItem.Kind.Button, UiScale.TypeButton);
                it.Submitted = () => Choose(row);
                _rows.Add(it);
            }
            _empty = MenuUi.Contain(MenuUi.Label(space, "Empty", CMTokens.Ui, UiScale.TypeLead, cream, TextAnchor.MiddleCenter,
                RowX, RowY + 200f, RowW, 120f, "No replays yet. Every match you play is saved here (the newest " + ReplayStore.Keep + ").", 2f), 2);
            float by = RowY + PageSize * (RowH + RowGap) + 50f;
            _prev = MenuUi.Item(space, "Prev", RowX, by, 300f, 76f, "PREV", MenuItem.Kind.Button, UiScale.TypeLead, true, CMTokens.Display);
            _prev.Submitted = () => { Page--; if (Page < 0) Page = PageCount(Entries.Count) - 1; ShowPage(); };
            _pageText = MenuUi.Contain(MenuUi.Label(space, "Page", CMTokens.Ui, UiScale.TypeBody, cream, TextAnchor.MiddleCenter,
                RowX + 320f, by, 300f, 76f, "", 2f), 1);
            _next = MenuUi.Item(space, "Next", RowX + 640f, by, 300f, 76f, "NEXT", MenuItem.Kind.Button, UiScale.TypeLead, true, CMTokens.Display);
            _next.Submitted = () => { Page = (Page + 1) % PageCount(Entries.Count); ShowPage(); };
            _back = MenuUi.Item(space, "Back", RowX + RowW - 400f, by, 400f, 76f, "BACK", MenuItem.Kind.Button, UiScale.TypeLead, true, CMTokens.Display);
            _back.Submitted = () => Close(true);
            _notice = MenuUi.Contain(MenuUi.Label(space, "Notice", CMTokens.Ui, UiScale.TypeBody, CMTokens.ToolBrass.Color, TextAnchor.MiddleCenter,
                RowX, by + 92f, RowW, 60f, "", 2f), 2);
            _hint = MenuUi.Contain(MenuUi.Label(space, "Hint", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleCenter,
                RowX, by + 156f, RowW, 34f, "", 2f), 1);
            _canvas.enabled = false;
        }
    }
}

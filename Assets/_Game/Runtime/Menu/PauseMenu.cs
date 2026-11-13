using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using B = CriticalMass.Runtime.Input.HumanInputState.HumanButton;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// The pause menu and settings page (controls-v1). Opens whenever HumanInputState.Paused is true (Escape or Start, the
    /// existing Pause action) and closes when it is false, so pausing stays exactly the sim pause that existed before: the
    /// driver adds no ticks while paused and Fill writes neutral frames. While open it takes the Pause press as "back"
    /// (HumanInputSource.PauseHandler); B on a controller is "back" too. Keyboard (arrows, Enter), mouse (hover, click,
    /// drag) and controller (stick or d-pad, A) all drive the same focus through the EventSystem.
    /// Pages: root (Resume, Settings, Restart match, Quit to title, Quit game) and Settings with four tabs (Controls with
    /// interactive rebinding, Audio, Video, Game). Built in code, like the HUD, on a Screen Space - Camera canvas above it.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        public enum Page { Root, Settings }
        public enum Tab { Controls, Audio, Video, Game }

        /// <summary>What the next PauseMenu does after a scene reload (Restart match, Quit to title).</summary>
        public enum AfterReload { None, Play, Title }

        public static AfterReload PendingAfterReload;

        public HumanInputSource Input;
        public Canvas Canvas { get; private set; }
        public bool IsOpen { get; private set; }
        public Page CurrentPage { get; private set; } = Page.Root;
        public Tab CurrentTab { get; private set; } = Tab.Controls;
        /// <summary>A rebinding is waiting for a key or button.</summary>
        public bool IsRebinding => _rebind != null;
        /// <summary>The last conflict warning ("" when none).</summary>
        public string Status => _status != null ? _status.text : string.Empty;

        const float RebindTimeoutSeconds = 8f;
        const float AfterRebindQuietSeconds = 0.3f;

        RectTransform _root;
        GameObject _rootPage, _settingsPage;
        readonly GameObject[] _tabPages = new GameObject[4];
        readonly MenuItem[] _tabButtons = new MenuItem[4];
        MenuItem _resume;
        Text _status, _hint, _title;
        readonly List<CellRef> _cells = new List<CellRef>();
        readonly List<Action> _refreshers = new List<Action>();
        InputActionRebindingExtensions.RebindingOperation _rebind;
        CellRef _rebindCell;
        float _rebindStarted;
        float _quietUntil;
        bool _built;

        sealed class CellRef
        {
            public MenuItem Item;
            public RebindSlot Slot;
            public ControlRow Row;
        }

        void Awake()
        {
            if (Input == null) Input = GetComponent<HumanInputSource>();
        }

        void OnEnable()
        {
            if (Input == null) Input = GetComponent<HumanInputSource>();
            if (Input != null) Input.PauseHandler = OnPausePressed;
            ControlBindings.BindingsChanged += RefreshCells;
        }

        void OnDisable()
        {
            if (Input != null && Input.PauseHandler == (Func<bool>)OnPausePressed) Input.PauseHandler = null;
            ControlBindings.BindingsChanged -= RefreshCells;
            CancelRebind();
        }

        void Start()
        {
            Build();
            AfterReload pending = PendingAfterReload;
            PendingAfterReload = AfterReload.None;
            TitleScreen title = GetComponent<TitleScreen>();
            if (pending == AfterReload.Play && title != null) title.Hide();
            if (pending == AfterReload.Title && title == null)
            {
                GameHud hud = GetComponent<GameHud>();
                TitleScreen.Show(gameObject, hud);
            }
        }

        // ---- open, close, back ----

        bool OnPausePressed()
        {
            // The title screen takes every key itself; the press that dismisses it must not also open the menu.
            if (GetComponent<TitleScreen>() != null) return true;
            if (!IsOpen) return false;
            if (IsRebinding || Time.unscaledTime < _quietUntil) return true;
            Back();
            return true;
        }

        /// <summary>Settings: back to the root page. Root: resume the match.</summary>
        public void Back()
        {
            if (CurrentPage == Page.Settings) ShowRoot();
            else Resume();
        }

        public void Resume()
        {
            if (Input != null && Input.State.Paused) Input.State.ButtonDown(B.Pause);
            Close();
        }

        /// <summary>Opens the menu by pausing (as the Pause key does).</summary>
        public void Pause()
        {
            if (Input != null && !Input.State.Paused) Input.State.ButtonDown(B.Pause);
            Open();
        }

        void Open()
        {
            Build();
            IsOpen = true;
            Canvas.enabled = true;
            ShowRoot();
        }

        void Close()
        {
            CancelRebind();
            IsOpen = false;
            if (Canvas != null) Canvas.enabled = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (Input == null) return;
            if (Input.PauseHandler == null) Input.PauseHandler = OnPausePressed;
            ControlBindings.Sample();
            bool paused = Input.State.Paused;
            if (paused && !IsOpen) Open();
            else if (!paused && IsOpen) Close();
            if (!IsOpen) return;
            UpdateScale();
            UpdateHint();

            if (_rebind != null)
            {
                if (Time.unscaledTime - _rebindStarted > RebindTimeoutSeconds) _rebind.Cancel();
                return;
            }
            if (Time.unscaledTime < _quietUntil) return;
            EventSystem es = EventSystem.current;
            if (es != null && !es.sendNavigationEvents) es.sendNavigationEvents = true;

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame) Back();
                else if (CurrentPage == Page.Settings && pad.leftShoulder.wasPressedThisFrame) ShowTab((Tab)(((int)CurrentTab + 3) % 4));
                else if (CurrentPage == Page.Settings && pad.rightShoulder.wasPressedThisFrame) ShowTab((Tab)(((int)CurrentTab + 1) % 4));
            }
            // Nothing focused (the mouse clicked empty space): any navigation key puts the focus back.
            if (es != null && es.currentSelectedGameObject == null && NavigationPressed()) SelectFirst();
        }

        static bool NavigationPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                               kb.tabKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
                                   pad.leftStick.ReadValue().sqrMagnitude > 0.25f);
        }

        void UpdateHint()
        {
            if (_hint == null) return;
            bool pad = ControlBindings.ActiveDevice == ControlDevice.Gamepad;
            string s;
            if (IsRebinding) s = pad ? "PRESS A BUTTON   ·   WAIT 8 S TO CANCEL" : "PRESS A KEY OR MOUSE BUTTON   ·   ESC CANCELS";
            else if (CurrentPage == Page.Settings)
                s = pad ? "A SELECT   ·   B BACK   ·   LB / RB TABS   ·   LEFT / RIGHT CHANGE" : "ENTER SELECT   ·   ESC BACK   ·   LEFT / RIGHT CHANGE";
            else s = pad ? "A SELECT   ·   B RESUME" : "ENTER SELECT   ·   ESC RESUME";
            HudText.Set(_hint, s);
        }

        // ---- pages ----

        public void ShowRoot()
        {
            Build();
            CancelRebind();
            CurrentPage = Page.Root;
            _rootPage.SetActive(true);
            _settingsPage.SetActive(false);
            HudText.Set(_title, "PAUSED");
            Select(_resume);
            UpdateHint();
        }

        public void ShowSettings(Tab tab = Tab.Controls)
        {
            Build();
            CurrentPage = Page.Settings;
            _rootPage.SetActive(false);
            _settingsPage.SetActive(true);
            HudText.Set(_title, "SETTINGS");
            ShowTab(tab);
        }

        public void ShowTab(Tab tab)
        {
            CancelRebind();
            CurrentTab = tab;
            for (int i = 0; i < 4; i++)
            {
                _tabPages[i].SetActive(i == (int)tab);
                VectorShape s = _tabButtons[i].Body;
                s.SetRim(i == (int)tab ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color);
                s.RimPx = i == (int)tab ? 4f : 2f;
                s.SetVerticesDirty();
            }
            SetStatus(string.Empty, false);
            RefreshAll();
            Select(_tabButtons[(int)tab]);
            UpdateHint();
        }

        static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            s.Select();
        }

        void SelectFirst()
        {
            if (CurrentPage == Page.Root) Select(_resume);
            else Select(_tabButtons[(int)CurrentTab]);
        }

        // ---- actions ----

        public void RestartMatch()
        {
            PendingAfterReload = AfterReload.Play;
            ReloadBoot();
        }

        public void QuitToTitle()
        {
            PendingAfterReload = AfterReload.Title;
            ReloadBoot();
        }

        static void ReloadBoot()
        {
            SceneManager.LoadScene(GameBootstrap.BootScenePath);
        }

        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- build ----

        static Color Navy => CMTokens.VoidNavy.Color;
        static Color Cream => CMTokens.WarmCream.Color;
        static Color Brass => CMTokens.ToolBrass.Color;

        public void Build()
        {
            if (_built) return;
            _built = true;
            EnsureEventSystem(transform);

            var go = new GameObject("Pause Menu Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.sortingOrder = 40;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Canvas.worldCamera = cam;
            Canvas.planeDistance = cam != null ? Mathf.Max(0.4f, cam.nearClipPlane * 1.3f) : 0.4f;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            // Full-screen Void Navy wash; it also catches clicks so nothing behind the menu reacts.
            var wash = MenuUi.Rt(_root, "Wash").gameObject.AddComponent<Image>();
            MenuUi.Stretch(wash.rectTransform);
            wash.color = CMTokens.WithAlpha(Navy, 0.62f);
            wash.raycastTarget = true;

            // Layout space: 1920x1080 reference, centred (the canvas scale factor is min(w/1920, h/1080)).
            RectTransform space = MenuUi.Rt(_root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            _title = MenuUi.Label(space, "Title", CMTokens.Display, 64, Cream, TextAnchor.MiddleCenter, 460f, 40f, 1000f, 90f, "PAUSED", 4f);
            _hint = MenuUi.Label(space, "Hint", CMTokens.Ui, 20, Cream, TextAnchor.MiddleCenter, 260f, 1020f, 1400f, 36f, "", 2f);

            BuildRoot(space);
            BuildSettings(space);
            Canvas.enabled = false;
            UpdateScale();
        }

        /// <summary>Canvas scale for the camera's current pixel size (the capture tool calls it after retargeting).</summary>
        public void UpdateScale()
        {
            if (Canvas == null) return;
            UnityEngine.Camera cam = Canvas.worldCamera;
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
                if (cam != null) Canvas.worldCamera = cam;
            }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(Canvas.scaleFactor - s) > 1e-4f) Canvas.scaleFactor = s;
        }

        /// <summary>The UI needs one EventSystem with the Input System's UI module (keyboard, mouse, controller).</summary>
        public static EventSystem EnsureEventSystem(Transform parent)
        {
            EventSystem es = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            if (es != null) return es;
            var go = new GameObject("EventSystem");
            go.transform.SetParent(parent, false);
            es = go.AddComponent<EventSystem>();
            InputSystemUIInputModule m = go.AddComponent<InputSystemUIInputModule>();
            m.AssignDefaultActions();
            return es;
        }

        void BuildRoot(RectTransform space)
        {
            RectTransform page = MenuUi.Rt(space, "Root Page");
            MenuUi.Stretch(page);
            _rootPage = page.gameObject;
            // DG-01: How to play (the guided match, when an item registers its start) and Controls joined the list.
            bool howTo = Flow.OverlayRegistry.TutorialStart() != null;
            MenuUi.Panel(page, "Panel", 640f, 150f, 640f, 760f);
            string[] labels = { "RESUME", "HOW TO PLAY", "CONTROLS", "SETTINGS", "RESTART MATCH", "QUIT TO TITLE", "QUIT GAME" };
            Action[] acts =
            {
                Resume, () => { if (Flow.OverlayRegistry.StartTutorial()) Resume(); }, () => ShowSettings(Tab.Controls),
                () => ShowSettings(Tab.Audio), RestartMatch, QuitToTitle, QuitGame,
            };
            var items = new List<MenuItem>();
            for (int i = 0; i < labels.Length; i++)
            {
                MenuItem it = MenuUi.Item(page, labels[i], 700f, 190f + i * 100f, 520f, 80f, labels[i], MenuItem.Kind.Button, 32,
                    true, CMTokens.Display);
                it.Body.Radius = 16f;
                it.Submitted = acts[i];
                if (i == 1 && !howTo)
                {
                    // Present but not ready: dimmed, tagged and skipped by navigation.
                    it.interactable = false;
                    MenuUi.Label(it.transform, "Soon", CMTokens.Ui, 16, Brass, TextAnchor.MiddleRight, 330f, 0f, 170f, 80f, "COMING SOON", 2f);
                    continue;
                }
                items.Add(it);
            }
            Chain(items);
            _resume = items[0];
        }

        /// <summary>Explicit up/down navigation through items (wrapping), so the focus never leaves the list.</summary>
        static void Chain(IList<MenuItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var n = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = items[(i + items.Count - 1) % items.Count],
                    selectOnDown = items[(i + 1) % items.Count],
                };
                items[i].navigation = n;
            }
        }

        const float PanelX = 280f, PanelY = 140f, PanelW = 1360f, PanelH = 870f;
        const float ContentX = PanelX + 40f, ContentY = PanelY + 110f;

        void BuildSettings(RectTransform space)
        {
            RectTransform page = MenuUi.Rt(space, "Settings Page");
            MenuUi.Stretch(page);
            _settingsPage = page.gameObject;
            MenuUi.Panel(page, "Panel", PanelX, PanelY, PanelW, PanelH);
            string[] tabNames = { "CONTROLS", "AUDIO", "VIDEO", "GAME" };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                MenuItem t = MenuUi.Item(page, "Tab " + tabNames[i], ContentX + i * 250f, PanelY + 30f, 230f, 60f, tabNames[i],
                    MenuItem.Kind.Button, 26, true, CMTokens.Display);
                t.Submitted = () => ShowTab((Tab)k);
                _tabButtons[i] = t;
            }
            MenuItem back = MenuUi.Item(page, "Back", PanelX + PanelW - 40f - 200f, PanelY + 30f, 200f, 60f, "BACK",
                MenuItem.Kind.Button, 26, true, CMTokens.Display);
            back.Submitted = ShowRoot;

            _tabPages[0] = BuildControls(page);
            _tabPages[1] = BuildAudio(page);
            _tabPages[2] = BuildVideo(page);
            _tabPages[3] = BuildGame(page);

            _status = MenuUi.Label(page, "Status", CMTokens.Ui, 22, Brass, TextAnchor.MiddleLeft, ContentX, PanelY + PanelH - 70f,
                900f, 40f, "", 2f);

            // Tab row: left/right across tabs and Back, down into the active tab's first line.
            var row = new List<Selectable>(_tabButtons) { back };
            for (int i = 0; i < row.Count; i++)
            {
                var n = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = row[(i + row.Count - 1) % row.Count],
                    selectOnRight = row[(i + 1) % row.Count],
                };
                row[i].navigation = n;
            }
            for (int i = 0; i < 4; i++)
            {
                MenuItem first = FirstItem(_tabPages[i]);
                Navigation n = _tabButtons[i].navigation;
                n.selectOnDown = first;
                _tabButtons[i].navigation = n;
            }
        }

        static MenuItem FirstItem(GameObject page)
        {
            MenuItem[] all = page.GetComponentsInChildren<MenuItem>(true);
            return all.Length > 0 ? all[0] : null;
        }

        void SetStatus(string s, bool warning)
        {
            if (_status == null) return;
            _status.color = warning ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color;
            HudText.Set(_status, s);
        }

        // ---- Controls tab ----

        // Easy movement: rows 36 + 3 (were 40 + 4) so the Walk to pointer row and the two movement toggles fit above Reset.
        const float RowH = 36f, RowGap = 3f, LabelW = 330f, CellW = 210f, CellGap = 14f, ToggleH = 46f, ToggleW = 600f;

        GameObject BuildControls(RectTransform parent)
        {
            RectTransform tab = MenuUi.Rt(parent, "Controls Tab");
            MenuUi.Stretch(tab);
            float y = ContentY;
            string[] heads = { "KEYBOARD", "KEYBOARD 2", "CONTROLLER", "CONTROLLER 2" };
            for (int c = 0; c < 4; c++)
                MenuUi.Label(tab, "Head " + c, CMTokens.Ui, 18, Brass, TextAnchor.MiddleCenter, ContentX + LabelW + c * (CellW + CellGap), y,
                    CellW, 28f, heads[c], 2f);
            y += 34f;

            InputActionAsset asset = InputSystem.actions;
            var grid = new List<MenuItem[]>();
            for (int r = 0; r < ControlBindings.Rows.Length; r++)
            {
                ControlRow row = ControlBindings.Rows[r];
                MenuUi.Label(tab, "Row " + row.Label, CMTokens.Ui, 22, Cream, TextAnchor.MiddleLeft, ContentX + 8f, y, LabelW - 8f, RowH,
                    row.Label.ToUpperInvariant(), 2f);
                var line = new MenuItem[4];
                for (int c = 0; c < 4; c++)
                {
                    RebindSlot[] set = c < 2 ? row.Keyboard : row.Gamepad;
                    int k = c % 2;
                    if (k >= set.Length) continue;
                    RebindSlot slot = set[k];
                    MenuItem cell = MenuUi.Item(tab, $"Cell {row.Label} {c}", ContentX + LabelW + c * (CellW + CellGap), y, CellW, RowH, "-",
                        MenuItem.Kind.Button, 20, true, CMTokens.Mono);
                    var cref = new CellRef { Item = cell, Slot = slot, Row = row };
                    cell.Submitted = () => StartRebind(cref);
                    if (!slot.Rebindable) cell.interactable = false;
                    _cells.Add(cref);
                    line[c] = cell;
                }
                grid.Add(line);
                y += RowH + RowGap;
            }
            MenuItem reset = MenuUi.Item(tab, "Reset Controls", PanelX + PanelW - 40f - 340f, PanelY + PanelH - 78f, 340f, 56f,
                "RESET TO DEFAULTS", MenuItem.Kind.Button, 24, true, CMTokens.Display);
            reset.Submitted = () =>
            {
                ControlBindings.ResetAll(InputSystem.actions);
                SetStatus("Every control is back to its default.", false);
            };

            // Easy movement toggles (GameSettings, kept between launches; HumanInputSource applies them at once).
            y += 4f;
            MenuItem assist = MoveToggle(tab, "Movement Assist", ContentX, y, () => GameSettings.MovementAssist,
                v => GameSettings.MovementAssist = v, "MOVEMENT ASSIST",
                "Movement assist on: one key follows a bridge it points along, and you slide past corners.",
                "Movement assist off: you move exactly where the keys point.");
            MenuItem keys = MoveToggle(tab, "Keys Follow Map", ContentX + ToggleW + 20f, y, () => GameSettings.KeysFollowMap,
                v => GameSettings.KeysFollowMap = v, "KEYS FOLLOW THE MAP",
                "Keys follow the map: W, A, S and D move along the map's grid instead of the screen.",
                "Keys follow the screen: W moves up the screen.");

            WireGrid(grid, reset);
            // The grid's bottom edge now leads to the toggles, the toggles to Reset.
            MenuItem lastLeft = null, lastRight = null;
            for (int r = grid.Count - 1; r >= 0 && (lastLeft == null || lastRight == null); r--)
            {
                if (lastLeft == null) lastLeft = grid[r][0] ?? grid[r][1];
                if (lastRight == null) lastRight = grid[r][2] ?? grid[r][3];
            }
            for (int r = 0; r < grid.Count; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    MenuItem it = grid[r][c];
                    if (it == null || it.navigation.selectOnDown != reset) continue;
                    Navigation n = it.navigation;
                    n.selectOnDown = c < 2 ? assist : keys;
                    it.navigation = n;
                }
            }
            assist.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnUp = lastLeft ?? (Selectable)_tabButtons[0], selectOnDown = reset, selectOnRight = keys,
            };
            keys.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnUp = lastRight ?? lastLeft ?? (Selectable)_tabButtons[0], selectOnDown = reset,
                selectOnLeft = assist,
            };
            Navigation rn = reset.navigation;
            rn.selectOnUp = keys;
            reset.navigation = rn;
            _refreshers.Add(RefreshCells);
            return tab.gameObject;
        }

        /// <summary>An ON/OFF button for a movement setting: submit flips it, the label shows the state.</summary>
        MenuItem MoveToggle(RectTransform tab, string name, float x, float y, Func<bool> get, Action<bool> set, string label,
            string onStatus, string offStatus)
        {
            MenuItem it = MenuUi.Item(tab, name, x, y, ToggleW, ToggleH, label, MenuItem.Kind.Button, 22, true, CMTokens.Display);
            Action refresh = () => HudText.Set(it.Label, label + (get() ? "   ON" : "   OFF"));
            it.Submitted = () =>
            {
                set(!get());
                refresh();
                SetStatus(get() ? onStatus : offStatus, false);
            };
            _refreshers.Add(refresh);
            refresh();
            return it;
        }

        /// <summary>Explicit grid navigation: up/down within a column (skipping gaps), left/right within a row.</summary>
        void WireGrid(List<MenuItem[]> grid, MenuItem reset)
        {
            int rows = grid.Count;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    MenuItem it = grid[r][c];
                    if (it == null) continue;
                    var n = new Navigation { mode = Navigation.Mode.Explicit };
                    n.selectOnUp = Find(grid, r, c, -1) ?? (Selectable)_tabButtons[0];
                    n.selectOnDown = Find(grid, r, c, 1) ?? reset;
                    for (int d = c - 1; d >= 0 && n.selectOnLeft == null; d--) n.selectOnLeft = grid[r][d];
                    for (int d = c + 1; d < 4 && n.selectOnRight == null; d++) n.selectOnRight = grid[r][d];
                    it.navigation = n;
                }
            }
            MenuItem last = null;
            for (int r = rows - 1; r >= 0 && last == null; r--) last = grid[r][0] ?? grid[r][2];
            reset.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = last, selectOnDown = _tabButtons[0] };
        }

        static MenuItem Find(List<MenuItem[]> grid, int r, int c, int dir)
        {
            for (int i = r + dir; i >= 0 && i < grid.Count; i += dir)
            {
                if (grid[i][c] != null) return grid[i][c];
                // A gap in this column: the nearest filled cell of that row.
                for (int d = 1; d < 4; d++)
                {
                    if (c - d >= 0 && grid[i][c - d] != null) return grid[i][c - d];
                    if (c + d < 4 && grid[i][c + d] != null) return grid[i][c + d];
                }
            }
            return null;
        }

        void RefreshCells()
        {
            InputActionAsset asset = InputSystem.actions;
            InputActionMap map = ControlBindings.Map(asset);
            for (int i = 0; i < _cells.Count; i++)
            {
                CellRef c = _cells[i];
                if (c == _rebindCell && _rebind != null) continue;
                InputAction a = map != null ? map.FindAction(c.Slot.Action, false) : null;
                int idx = ControlBindings.FindBindingIndex(a, c.Slot);
                HudText.Set(c.Item.Label, ControlBindings.Display(a, idx));
            }
        }

        void StartRebind(CellRef cell)
        {
            if (_rebind != null || !cell.Slot.Rebindable) return;
            InputActionAsset asset = InputSystem.actions;
            InputActionMap map = ControlBindings.Map(asset);
            InputAction action = map != null ? map.FindAction(cell.Slot.Action, false) : null;
            int index = ControlBindings.FindBindingIndex(action, cell.Slot);
            if (index < 0) return;

            map.Disable();
            _rebindCell = cell;
            _rebindStarted = Time.unscaledTime;
            MenuItem.SubmitBlockedUntil = float.MaxValue;
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = false;
            HudText.Set(cell.Item.Label, "PRESS...");
            SetStatus($"{cell.Row.Label}: press the new {(cell.Slot.Device == ControlDevice.Gamepad ? "button" : "key")}.", false);

            var op = action.PerformInteractiveRebinding(index)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .OnMatchWaitForAnother(0.1f);
            if (cell.Slot.Device == ControlDevice.Gamepad) op.WithControlsHavingToMatchPath("<Gamepad>");
            else op.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");
            op.OnComplete(o => FinishRebind(action, index, false))
              .OnCancel(o => FinishRebind(action, index, true));
            _rebind = op;
            op.Start();
        }

        void FinishRebind(InputAction action, int index, bool cancelled)
        {
            InputActionRebindingExtensions.RebindingOperation op = _rebind;
            _rebind = null;
            CellRef cell = _rebindCell;
            _rebindCell = null;
            op?.Dispose();
            if (action != null && action.actionMap != null) action.actionMap.Enable();
            _quietUntil = Time.unscaledTime + AfterRebindQuietSeconds;
            MenuItem.SubmitBlockedUntil = _quietUntil;
            if (cancelled) SetStatus("Unchanged.", false);
            else
            {
                List<string> clash = ControlBindings.Conflicts(action, index);
                string key = ControlBindings.Display(action, index);
                if (clash.Count > 0) SetStatus($"Warning: {key} is also bound to {string.Join(", ", clash)}.", true);
                else SetStatus($"{(cell != null ? cell.Row.Label : action.name)} is now {key}.", false);
                ControlBindings.Persist(action.actionMap.asset);
            }
            RefreshCells();
            if (cell != null) Select(cell.Item);
        }

        void CancelRebind()
        {
            if (_rebind != null) _rebind.Cancel();
        }

        // ---- Audio, Video, Game tabs ----

        const float LineH = 64f, LineGap = 14f, LineW = PanelW - 80f;

        GameObject BuildAudio(RectTransform parent)
        {
            RectTransform tab = MenuUi.Rt(parent, "Audio Tab");
            MenuUi.Stretch(tab);
            var items = new List<MenuItem>
            {
                Slider(tab, 0, "MASTER VOLUME", () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v),
                Slider(tab, 1, "MUSIC", () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v),
                Slider(tab, 2, "EFFECTS", () => GameSettings.EffectsVolume, v => GameSettings.EffectsVolume = v),
            };
            Note(tab, 3, "Routed through the game's audio mixer (Master, Music, Effects).");
            ChainInTab(items);
            return tab.gameObject;
        }

        GameObject BuildVideo(RectTransform parent)
        {
            RectTransform tab = MenuUi.Rt(parent, "Video Tab");
            MenuUi.Stretch(tab);
            var items = new List<MenuItem>
            {
                Stepper(tab, 0, "RESOLUTION", ResolutionText, StepResolution),
                Stepper(tab, 1, "DISPLAY", () => GameSettings.FullScreen ? "FULL SCREEN" : "WINDOWED",
                    d => GameSettings.FullScreen = !GameSettings.FullScreen),
                Stepper(tab, 2, "VERTICAL SYNC", () => GameSettings.VSync ? "ON" : "OFF", d => GameSettings.VSync = !GameSettings.VSync),
                Stepper(tab, 3, "QUALITY", QualityText, StepQuality),
                Stepper(tab, 4, "FRAME RATE LIMIT", FpsCapText, StepFpsCap),
                Stepper(tab, 5, "SLOW DOWN IN BACKGROUND", () => GameSettings.BackgroundThrottle ? "ON" : "OFF",
                    d => GameSettings.BackgroundThrottle = !GameSettings.BackgroundThrottle),
            };
            Note(tab, 6, GameSettings.CanChangeWindow ? "Changes apply at once and are kept for the next launch." :
                "Video settings are saved here and apply in the built game.");
            ChainInTab(items);
            return tab.gameObject;
        }

        GameObject BuildGame(RectTransform parent)
        {
            RectTransform tab = MenuUi.Rt(parent, "Game Tab");
            MenuUi.Stretch(tab);
            var items = new List<MenuItem>
            {
                Stepper(tab, 0, "CAMERA SHAKE", () => GameSettings.CameraShake ? "ON" : "OFF", d => GameSettings.CameraShake = !GameSettings.CameraShake),
                Stepper(tab, 1, "DAMAGE NUMBERS", () => GameSettings.DamageNumbers ? "ON" : "OFF", d => GameSettings.DamageNumbers = !GameSettings.DamageNumbers),
                Stepper(tab, 2, "BLOOD", () => GameSettings.Blood ? "ON" : "OFF", d => GameSettings.Blood = !GameSettings.Blood),
            };
            MenuItem reset = MenuUi.Item(tab, "Reset Settings", ContentX, ContentY + 3 * (LineH + LineGap) + 20f, 420f, 56f,
                "RESET ALL SETTINGS", MenuItem.Kind.Button, 24, true, CMTokens.Display);
            reset.Submitted = () =>
            {
                GameSettings.ResetAll();
                RefreshAll();
                SetStatus("Audio, video and game settings are back to their defaults.", false);
            };
            items.Add(reset);
            ChainInTab(items);
            return tab.gameObject;
        }

        void ChainInTab(List<MenuItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var n = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = i == 0 ? (Selectable)_tabButtons[0] : items[i - 1],
                    selectOnDown = i + 1 < items.Count ? items[i + 1] : (Selectable)_tabButtons[0],
                };
                items[i].navigation = n;
            }
        }

        void Note(RectTransform tab, int line, string text)
        {
            MenuUi.Label(tab, "Note", CMTokens.Ui, 20, CMTokens.WithAlpha(Cream, 0.8f), TextAnchor.MiddleLeft, ContentX + 8f,
                ContentY + line * (LineH + LineGap) + 10f, LineW, 36f, text, 2f);
        }

        MenuItem Slider(RectTransform tab, int line, string label, Func<float> get, Action<float> set)
        {
            float y = ContentY + line * (LineH + LineGap);
            MenuItem it = MenuUi.Item(tab, label, ContentX, y, LineW, LineH, label, MenuItem.Kind.Slider, 26, false, CMTokens.Display);
            VectorShape bar = MenuUi.Shape(it.transform, "Bar", ShapeKind.RoundRect, 440f, 18f, 660f, 28f, CMTokens.Biomass.Color, -1f);
            bar.EmptyColor = CMTokens.MeterEmpty.Color;
            bar.RimPx = 0f;
            it.Bar = bar;
            it.Value = MenuUi.Label(it.transform, "Value", CMTokens.Mono, 24, MenuItem.IdleText, TextAnchor.MiddleRight, LineW - 160f, 0f, 140f, LineH, "", 2f);
            Action refresh = () =>
            {
                float v = get();
                bar.Fill01 = v;
                HudText.Set(it.Value, Mathf.RoundToInt(v * 100f) + "%");
            };
            it.Stepped = d =>
            {
                set(Mathf.Clamp01(Mathf.Round((get() + d * 0.05f) * 20f) / 20f));
                refresh();
            };
            it.SetFromPointer = v =>
            {
                set(Mathf.Round(v * 20f) / 20f);
                refresh();
            };
            _refreshers.Add(refresh);
            return it;
        }

        MenuItem Stepper(RectTransform tab, int line, string label, Func<string> text, Action<int> step)
        {
            float y = ContentY + line * (LineH + LineGap);
            MenuItem it = MenuUi.Item(tab, label, ContentX, y, LineW, LineH, label, MenuItem.Kind.Stepper, 26, false, CMTokens.Display);
            it.Value = MenuUi.Label(it.transform, "Value", CMTokens.Ui, 26, MenuItem.IdleText, TextAnchor.MiddleCenter, 460f, 0f, 640f, LineH, "", 2f);
            MenuUi.Label(it.transform, "Left", CMTokens.Display, 28, Brass, TextAnchor.MiddleCenter, 420f, 0f, 40f, LineH, "<", 2f);
            MenuUi.Label(it.transform, "Right", CMTokens.Display, 28, Brass, TextAnchor.MiddleCenter, 1100f, 0f, 40f, LineH, ">", 2f);
            Action refresh = () => HudText.Set(it.Value, text());
            it.Stepped = d =>
            {
                step(d);
                refresh();
            };
            _refreshers.Add(refresh);
            return it;
        }

        void RefreshAll()
        {
            for (int i = 0; i < _refreshers.Count; i++) _refreshers[i]();
        }

        static string ResolutionText()
        {
            Vector2Int r = GameSettings.Resolution;
            if (r.x <= 0 || r.y <= 0) r = new Vector2Int(Screen.width, Screen.height);
            return $"{r.x} x {r.y}";
        }

        static void StepResolution(int d)
        {
            List<Vector2Int> opts = GameSettings.ResolutionOptions();
            Vector2Int cur = GameSettings.Resolution;
            if (cur.x <= 0 || cur.y <= 0) cur = new Vector2Int(Screen.width, Screen.height);
            int i = opts.IndexOf(cur);
            if (i < 0) i = opts.Count - 1;
            i = (i + d + opts.Count) % opts.Count;
            GameSettings.Resolution = opts[i];
        }

        /// <summary>"60 FPS" or "DISPLAY RATE" for 0.</summary>
        public static string FpsCapText()
        {
            int v = GameSettings.FpsCap;
            return v == 0 ? "DISPLAY RATE" : v + " FPS";
        }

        static void StepFpsCap(int d)
        {
            int[] o = GameSettings.FpsCapOptions;
            int i = Array.IndexOf(o, GameSettings.FpsCap);
            if (i < 0) i = 1;
            GameSettings.FpsCap = o[(i + d + o.Length) % o.Length];
        }

        static string QualityText()
        {
            string[] names = QualitySettings.names;
            int q = GameSettings.Quality;
            if (q < 0 || q >= names.Length) q = QualitySettings.GetQualityLevel();
            return names.Length > 0 ? names[q].ToUpperInvariant() : "-";
        }

        static void StepQuality(int d)
        {
            int n = QualitySettings.names.Length;
            if (n == 0) return;
            int q = GameSettings.Quality;
            if (q < 0 || q >= n) q = QualitySettings.GetQualityLevel();
            GameSettings.Quality = (q + d + n) % n;
        }
    }

    /// <summary>
    /// Attaches the pause menu and the HUD prompt switcher to the game GameBootstrap builds, on boot and after every scene
    /// load, without editing the bootstrap (GameBootstrap.Build is idempotent: it returns the existing game).
    /// </summary>
    public static class MenuInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        /// <summary>Adds PauseMenu and HudPrompts to the game's HumanInputSource object; returns the menu (null: no game).</summary>
        public static PauseMenu Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            HumanInputSource input = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
            if (input == null) return null;
            GameObject go = input.gameObject;
            PauseMenu menu = go.GetComponent<PauseMenu>();
            if (menu == null)
            {
                menu = go.AddComponent<PauseMenu>();
                menu.Input = input;
            }
            if (go.GetComponent<HudPrompts>() == null) go.AddComponent<HudPrompts>();
            return menu;
        }
    }
}

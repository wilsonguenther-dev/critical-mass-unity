using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
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
    /// <para>2026-11-12 (spectator and readable prompts): the root page adds SPECTATE / REJOIN AS (class), CHANGE CLASS (a
    /// class page, or DG-10's ClassSelect when in the build; the pick applies on the next respawn) and SWITCH TEAM (a
    /// confirm page, then the match restarts on the other side). Hints are drawn as glyphs (PromptStrip) and the Controls
    /// tab cells show each binding's glyph beside its name.</para>
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        public enum Page { Root, Settings, Extra, Class, Confirm }
        public enum Tab { Controls, Audio, Video, Game }

        /// <summary>What the next PauseMenu does after a scene reload (Restart match, Quit to title).</summary>
        public enum AfterReload { None, Play, Title }

        public static AfterReload PendingAfterReload;

        // ---- extension points (DG-25, DG-30): other features add a settings tab or a root-page entry without editing
        // this file. Register before the menu builds (a BeforeSceneLoad RuntimeInitializeOnLoadMethod); registration is
        // keyed by name, so registering again after a play-mode restart (domain reload is off) is a no-op. ----

        /// <summary>A settings tab added after the four built-in ones.</summary>
        public sealed class ExtraTab
        {
            /// <summary>Tab button text (short, upper case).</summary>
            public string Name;
            /// <summary>Builds the tab's lines on the empty, stretched tab page (use Stepper, Slider, Note and the layout
            /// constants) and returns its focusable lines top to bottom; the menu chains them for keyboard and controller.</summary>
            public Func<PauseMenu, RectTransform, List<MenuItem>> Build;
        }

        /// <summary>A root-page entry that opens a page of its own (Back returns to the root page).</summary>
        public sealed class ExtraPage
        {
            /// <summary>Root-page button text.</summary>
            public string Label;
            /// <summary>Title shown above the page.</summary>
            public string Title;
            /// <summary>Builds the page once, on an empty page stretched over the 1920x1080 layout space; returns what to focus first.</summary>
            public Func<PauseMenu, RectTransform, Selectable> Build;
            /// <summary>Runs every time the page opens (refresh its contents here).</summary>
            public Action Opened;
        }

        public static readonly List<ExtraTab> ExtraTabs = new List<ExtraTab>();
        public static readonly List<ExtraPage> ExtraPages = new List<ExtraPage>();

        public static void AddTab(ExtraTab tab)
        {
            if (tab == null || string.IsNullOrEmpty(tab.Name)) return;
            for (int i = 0; i < ExtraTabs.Count; i++) if (ExtraTabs[i].Name == tab.Name) { ExtraTabs[i] = tab; return; }
            ExtraTabs.Add(tab);
        }

        public static void AddPage(ExtraPage page)
        {
            if (page == null || string.IsNullOrEmpty(page.Label)) return;
            for (int i = 0; i < ExtraPages.Count; i++) if (ExtraPages[i].Label == page.Label) { ExtraPages[i] = page; return; }
            ExtraPages.Add(page);
        }

        public HumanInputSource Input;
        public Canvas Canvas { get; private set; }
        public bool IsOpen { get; private set; }
        public Page CurrentPage { get; private set; } = Page.Root;
        public Tab CurrentTab { get; private set; } = Tab.Controls;
        /// <summary>A rebinding is waiting for a key or button.</summary>
        public bool IsRebinding => _rebind != null;
        /// <summary>The root page's SPECTATE / REJOIN AS, CHANGE CLASS and SWITCH TEAM lines (tests, captures).</summary>
        public MenuItem SpectateItem => _spectate;
        public MenuItem ChangeClassItem => _changeClass;
        public MenuItem SwitchTeamItem => _switchTeam;
        /// <summary>The last conflict warning ("" when none).</summary>
        public string Status => _status != null ? _status.text : string.Empty;

        const float RebindTimeoutSeconds = 8f;
        const float AfterRebindQuietSeconds = 0.3f;

        RectTransform _root;
        GameObject _rootPage, _settingsPage;
        GameObject[] _tabPages = new GameObject[4];
        MenuItem[] _tabButtons = new MenuItem[4];
        readonly List<ExtraPage> _extraDefs = new List<ExtraPage>();
        readonly List<GameObject> _extraPages = new List<GameObject>();
        readonly List<Selectable> _extraFirst = new List<Selectable>();
        /// <summary>Settings tabs: the four built-in ones plus ExtraTabs registered before the menu built.</summary>
        public int TabCount => _tabPages.Length;
        /// <summary>Index into the menu's extra pages of the open one (-1 when none).</summary>
        public int CurrentExtra { get; private set; } = -1;
        /// <summary>Index of the extra page with this root-page label (-1 when absent).</summary>
        public int ExtraPageIndex(string label)
        {
            for (int i = 0; i < _extraDefs.Count; i++) if (_extraDefs[i].Label == label) return i;
            return -1;
        }
        GameObject _classPage, _confirmPage;
        MenuItem _spectate, _changeClass, _switchTeam, _confirmYes;
        readonly MenuItem[] _classItems = new MenuItem[ClassIds.All.Count];
        MenuItem _classBack;
        Text _rootNote, _confirmTitle;
        PromptStrip _hintStrip;
        bool _external;
        MenuItem _resume;
        Text _status, _hint, _title;
        readonly List<CellRef> _cells = new List<CellRef>();
        readonly List<Action> _refreshers = new List<Action>();
        InputActionRebindingExtensions.RebindingOperation _rebind;
        CellRef _rebindCell;
        float _rebindStarted;
        float _quietUntil;
        bool _built;
        // Polish pass 1: the shared motion (UiMotion fade in and out) and the text-size setting (UiTextScale).
        CanvasGroup _group;
        UiTextScale _textScale;
        float _openedAt, _closedAt;
        bool _closing;

        /// <summary>The text-size setting as applied to the menu (tests).</summary>
        public UiTextScale TextScale => _textScale;

        sealed class CellRef
        {
            public MenuItem Item;
            public RebindSlot Slot;
            public ControlRow Row;
            public InputGlyphView Glyph;
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
            // Integration batch 4 (known defect: the station panel's Escape chained through to pause): an open station
            // panel takes the press and closes, whichever of the two holds HumanInputSource.PauseHandler right now.
            if (!IsOpen && Hud.Mechanics.StationPanel.IsOpen)
            {
                Hud.Mechanics.StationPanel.Close();
                return true;
            }
            if (!IsOpen) return false;
            // DG-14: the controls card opened from this menu closes first (back to the entry that opened it).
            if (ControlsCardScreen.IsOpen)
            {
                ControlsCardScreen.Hide();
                return true;
            }
            if (_external)
            {
                ClassSwitch.CloseExternalPicker();
                EndExternal(null);
                return true;
            }
            if (IsRebinding || Time.unscaledTime < _quietUntil) return true;
            Back();
            return true;
        }

        /// <summary>Settings, class or confirm page: back to the root page. Root: resume the match.</summary>
        public void Back()
        {
            if (CurrentPage != Page.Root) ShowRoot();
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
            _closing = false;
            Canvas.enabled = true;
            _openedAt = Time.unscaledTime;
            if (_group != null)
            {
                _group.alpha = UiMotion.Reduced ? 1f : 0f;
                _group.blocksRaycasts = true;
                _group.interactable = true;
            }
            ShowRoot();
        }

        void Close()
        {
            CancelRebind();
            if (_external)
            {
                ClassSwitch.CloseExternalPicker();
                _external = false;
            }
            FlushSettings();   // DG-53: saved once when the menu closes, not on every slider tick
            IsOpen = false;
            // A short fade out (input goes back to the game at once); Reduce motion closes at once.
            _closedAt = Time.unscaledTime;
            _closing = Canvas != null && Canvas.enabled && _group != null && !UiMotion.Reduced;
            if (_group != null)
            {
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }
            if (!_closing && Canvas != null) Canvas.enabled = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void FlushSettings()
        {
            if (!GameSettings.Flush() && _settingsPage != null) SetStatus("Settings could not be saved.", true);
        }

        // ---- controls-1: press-again confirmation for restart and quit ----

        public const float ConfirmSeconds = 3f;
        MenuItem _armed;
        string _armedLabel;
        float _armedAt;

        /// <summary>The item waiting for its confirming second press (tests).</summary>
        public MenuItem ArmedItem => _armed;

        void ConfirmThen(MenuItem item, string label, Action act)
        {
            if (_armed == item && Time.unscaledTime - _armedAt <= ConfirmSeconds)
            {
                Disarm();
                act();
                return;
            }
            Disarm();
            _armed = item;
            _armedLabel = label;
            _armedAt = Time.unscaledTime;
            HudText.Set(item.Label, "PRESS AGAIN: " + label);
            SetStatus(label.Substring(0, 1) + label.Substring(1).ToLowerInvariant() + "? Press it again to confirm.", true);
        }

        void Disarm()
        {
            if (_armed != null && _armed.Label != null) HudText.Set(_armed.Label, _armedLabel);
            _armed = null;
        }

        void Update()
        {
            if (Input == null) return;
            if (Input.PauseHandler == null) Input.PauseHandler = OnPausePressed;
            ControlBindings.Sample();
            if (_armed != null && (Time.unscaledTime - _armedAt > ConfirmSeconds || !IsOpen ||
                                   (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != _armed.gameObject))) Disarm();
            bool paused = Input.State.Paused;
            if (paused && !IsOpen) Open();
            else if (!paused && IsOpen) Close();
            if (!IsOpen)
            {
                if (_closing)
                {
                    float a = UiMotion.Exit(Time.unscaledTime - _closedAt);
                    _group.alpha = a;
                    if (a <= 0f)
                    {
                        _closing = false;
                        Canvas.enabled = false;
                    }
                }
                return;
            }
            if (_group != null && _group.alpha < 1f) _group.alpha = UiMotion.Reduced ? 1f : UiMotion.Enter(Time.unscaledTime - _openedAt);
            UpdateScale();
            UpdateHint();

            if (_rebind != null)
            {
                if (Time.unscaledTime - _rebindStarted > RebindTimeoutSeconds) _rebind.Cancel();
                return;
            }
            if (Time.unscaledTime < _quietUntil) return;
            if (ControlsCardScreen.IsOpen)
            {
                Gamepad cardPad = Gamepad.current;
                if (cardPad != null && cardPad.buttonEast.wasPressedThisFrame) ControlsCardScreen.Hide();
                return;
            }
            if (_external) return;   // DG-10's class picker has the input until it closes
            EventSystem es = EventSystem.current;
            if (es != null && !es.sendNavigationEvents) es.sendNavigationEvents = true;

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame) Back();
                else if (CurrentPage == Page.Settings && pad.leftShoulder.wasPressedThisFrame) ShowTab((Tab)(((int)CurrentTab + TabCount - 1) % TabCount));
                else if (CurrentPage == Page.Settings && pad.rightShoulder.wasPressedThisFrame) ShowTab((Tab)(((int)CurrentTab + 1) % TabCount));
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
            else if (CurrentPage == Page.Extra) s = pad ? "A SELECT   ·   B BACK" : "ENTER SELECT   ·   ESC BACK";
            else if (CurrentPage == Page.Class || CurrentPage == Page.Confirm)
                s = pad ? "A SELECT   ·   B BACK" : "ENTER SELECT   ·   ESC BACK";
            else s = pad ? "A SELECT   ·   B RESUME" : "ENTER SELECT   ·   ESC RESUME";
            HudText.Set(_hint, s);
            // Drawn: the key words become glyphs (the text above stays as the hidden plain line).
            if (_hintStrip != null) _hintStrip.SetHint(s, ControlBindings.ActiveDevice);
        }

        // ---- pages ----

        public void ShowRoot()
        {
            Build();
            CancelRebind();
            if (CurrentPage == Page.Settings) FlushSettings();   // DG-53: the settings page closed: one save
            CurrentPage = Page.Root;
            SetExtraPage(-1);
            _rootPage.SetActive(true);
            _settingsPage.SetActive(false);
            _classPage.SetActive(false);
            _confirmPage.SetActive(false);
            HudText.Set(_title, "PAUSED");
            RefreshRootLabels();
            Select(_resume);
            UpdateHint();
        }

        public void ShowSettings(Tab tab = Tab.Controls)
        {
            Build();
            CurrentPage = Page.Settings;
            SetExtraPage(-1);
            _rootPage.SetActive(false);
            _settingsPage.SetActive(true);
            _classPage.SetActive(false);
            _confirmPage.SetActive(false);
            HudText.Set(_title, "SETTINGS");
            ShowTab(tab);
        }

        /// <summary>Opens extra page <paramref name="index"/> (see ExtraPageIndex).</summary>
        public void ShowExtra(int index)
        {
            Build();
            if (index < 0 || index >= _extraPages.Count) return;
            CancelRebind();
            CurrentPage = Page.Extra;
            _rootPage.SetActive(false);
            _settingsPage.SetActive(false);
            _classPage.SetActive(false);
            _confirmPage.SetActive(false);
            SetExtraPage(index);
            HudText.Set(_title, _extraDefs[index].Title ?? string.Empty);
            try { _extraDefs[index].Opened?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            Select(_extraFirst[index]);
            UpdateHint();
        }

        void SetExtraPage(int index)
        {
            CurrentExtra = index;
            for (int i = 0; i < _extraPages.Count; i++) _extraPages[i].SetActive(i == index);
        }

        public void ShowTab(Tab tab)
        {
            CancelRebind();
            if ((int)tab < 0 || (int)tab >= TabCount) tab = Tab.Controls;
            CurrentTab = tab;
            for (int i = 0; i < TabCount; i++)
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
            else if (CurrentPage == Page.Extra && CurrentExtra >= 0) Select(_extraFirst[CurrentExtra]);
            else if (CurrentPage == Page.Class) Select(FirstClassItem());
            else if (CurrentPage == Page.Confirm) Select(_confirmYes);
            else Select(_tabButtons[(int)CurrentTab]);
        }

        // ---- actions ----

        /// <summary>DG-14: the one-screen controls card over the menu; the focus comes back here when it closes.</summary>
        public void ShowControls() => ControlsCardScreen.Show();

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

        // ---- spectate, change class, switch team (2026-11-12) ----

        World CurrentWorld()
        {
            SimDriverBehaviour d = GetComponent<SimDriverBehaviour>();
            return d != null ? d.World : null;
        }

        /// <summary>Root labels that depend on the match: SPECTATE or REJOIN AS (class); the pending class note.</summary>
        void RefreshRootLabels()
        {
            if (_spectate != null)
            {
                string cls = KillWords.ClassName(SpectatorMode.RejoinClass()).ToUpperInvariant();
                HudText.Set(_spectate.Label, SpectatorMode.IsSpectating ? "REJOIN AS " + cls : "SPECTATE");
                _spectate.interactable = SpectatorMode.Current != null;
            }
            if (_rootNote != null)
                HudText.Set(_rootNote, ClassSwitch.HasPending
                    ? "YOU'LL RESPAWN AS THE " + KillWords.ClassName(ClassSwitch.Pending).ToUpperInvariant()
                    : SpectatorMode.IsSpectating && SpectatorMode.Current.RejoinPending ? "BACK IN THE FIGHT AT YOUR RESPAWN" : "");
        }

        /// <summary>Spectate (actor 0 plays itself, free camera) or rejoin; resumes the match either way.</summary>
        public void ToggleSpectate()
        {
            if (SpectatorMode.Current == null) return;
            SpectatorMode.Toggle();
            Resume();
        }

        /// <summary>The class picker: DG-10's ClassSelect when in the build, else the class page. Applies on the next respawn.</summary>
        public void ShowClassPicker()
        {
            World w = CurrentWorld();
            TeamId team = w != null && w.ActorCount > 0 ? w.Actors[0].Team : TeamId.Azure;
            if (ClassSwitch.ExternalPickerAvailable)
            {
                _external = true;
                if (Canvas != null) Canvas.enabled = false;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                if (ClassSwitch.OpenExternalPicker(team, c => EndExternal(c), () => EndExternal(null))) return;
                _external = false;
                if (Canvas != null) Canvas.enabled = true;
            }
            ShowClassPage();
        }

        void EndExternal(ClassId? picked)
        {
            if (!_external) return;
            _external = false;
            if (picked.HasValue) ClassSwitch.Request(CurrentWorld(), picked.Value);
            if (IsOpen && Canvas != null) Canvas.enabled = true;
            if (IsOpen) ShowRoot();
        }

        public void ShowClassPage()
        {
            Build();
            CurrentPage = Page.Class;
            SetExtraPage(-1);
            _rootPage.SetActive(false);
            _settingsPage.SetActive(false);
            _confirmPage.SetActive(false);
            _classPage.SetActive(true);
            HudText.Set(_title, "CHANGE CLASS");
            World w = CurrentWorld();
            ClassId now = SpectatorMode.RejoinClass();
            for (int i = 0; i < _classItems.Length; i++)
            {
                MenuItem it = _classItems[i];
                if (it == null) continue;
                bool chosen = (int)now == i;
                it.Body.SetRim(chosen ? Brass : Cream);
                it.Body.RimPx = chosen ? 4f : 2f;
                it.Body.SetVerticesDirty();
            }
            Select(FirstClassItem((int)now));
            UpdateHint();
        }

        MenuItem FirstClassItem(int prefer = -1)
        {
            if (prefer >= 0 && prefer < _classItems.Length && _classItems[prefer] != null && _classItems[prefer].interactable)
                return _classItems[prefer];
            for (int i = 0; i < _classItems.Length; i++)
                if (_classItems[i] != null && _classItems[i].interactable) return _classItems[i];
            return _classBack;
        }

        /// <summary>Holds cls for the next respawn and returns to the root page with a line saying so.</summary>
        public void PickClass(ClassId cls)
        {
            ClassSwitch.Request(CurrentWorld(), cls);
            ShowRoot();
        }

        public void ShowSwitchTeam()
        {
            Build();
            CurrentPage = Page.Confirm;
            SetExtraPage(-1);
            _rootPage.SetActive(false);
            _settingsPage.SetActive(false);
            _classPage.SetActive(false);
            _confirmPage.SetActive(true);
            HudText.Set(_title, "SWITCH TEAM");
            HudText.Set(_confirmTitle, "SWITCH TO " + (OtherTeam() == TeamId.Azure ? "AZURE" : "CRIMSON") + "?");
            Select(_confirmYes);
            UpdateHint();
        }

        TeamId OtherTeam()
        {
            World w = CurrentWorld();
            TeamId mine = w != null && w.ActorCount > 0 ? w.Actors[0].Team : Flow.GameFlow.LastSetup.Team;
            return mine == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
        }

        /// <summary>Restarts the match on the other side (same class, size and pace; a new seed), playing.</summary>
        public void ConfirmSwitchTeam()
        {
            Flow.MatchSetupChoice c = Flow.GameFlow.LastSetup;
            World w = CurrentWorld();
            c.Team = OtherTeam();
            if (w != null && w.ActorCount > 0) c.Class = SpectatorMode.RejoinClass();
            SpectatorMode.SpectateNextMatch = false;
            Flow.GameFlow.BeginMatch(c);
        }

        void BuildClassPage(RectTransform space)
        {
            RectTransform page = MenuUi.Rt(space, "Class Page");
            MenuUi.Stretch(page);
            _classPage = page.gameObject;
            MenuUi.Panel(page, "Panel", 560f, 160f, 800f, 700f);
            MenuUi.Label(page, "Line", CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.MiddleCenter, 560f, 186f, 800f, 40f,
                "YOUR NEW CLASS APPLIES WHEN YOU NEXT RESPAWN", 2f);
            var items = new List<MenuItem>();
            for (int i = 0; i < _classItems.Length; i++)
            {
                ClassId cls = (ClassId)i;
                string name = KillWords.ClassName(cls).ToUpperInvariant();
                MenuItem it = MenuUi.Item(page, "Class " + name, 700f, 250f + i * 84f, 520f, 70f, name, MenuItem.Kind.Button, UiScale.TypeHeading, true,
                    CMTokens.Display);
                it.Body.Radius = 16f;
                it.Submitted = () => PickClass(cls);
                _classItems[i] = it;
                if (!Menus.MatchSetup.Playable.Contains(cls))
                {
                    // Not playable in this build: hidden, not a dead COMING SOON row (all six are playable since batch 3).
                    it.interactable = false;
                    it.gameObject.SetActive(false);
                    continue;
                }
                items.Add(it);
            }
            _classBack = MenuUi.Item(page, "Back", 760f, 690f + 60f, 400f, 70f, "BACK", MenuItem.Kind.Button, UiScale.TypeButton, true, CMTokens.Display);
            _classBack.Submitted = ShowRoot;
            items.Add(_classBack);
            Chain(items);
            _classPage.SetActive(false);
        }

        void BuildConfirmPage(RectTransform space)
        {
            RectTransform page = MenuUi.Rt(space, "Confirm Page");
            MenuUi.Stretch(page);
            _confirmPage = page.gameObject;
            MenuUi.Panel(page, "Panel", 560f, 300f, 800f, 420f);
            _confirmTitle = MenuUi.Fit(MenuUi.Label(page, "Question", CMTokens.Display, UiScale.TypeDisplay, Cream, TextAnchor.MiddleCenter, 560f, 340f, 800f, 70f,
                "SWITCH TEAM?", 3f), 44);
            MenuUi.Label(page, "Line", CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.MiddleCenter, 560f, 420f, 800f, 40f,
                "THE MATCH RESTARTS WITH YOU ON THE OTHER SIDE", 2f);
            _confirmYes = MenuUi.Item(page, "Yes", 620f, 560f, 330f, 80f, "YES, SWITCH", MenuItem.Kind.Button, UiScale.TypeHeading, true, CMTokens.Display);
            _confirmYes.Submitted = ConfirmSwitchTeam;
            MenuItem no = MenuUi.Item(page, "No", 970f, 560f, 330f, 80f, "NO", MenuItem.Kind.Button, UiScale.TypeHeading, true, CMTokens.Display);
            no.Submitted = ShowRoot;
            _confirmYes.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = no, selectOnRight = no };
            no.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _confirmYes, selectOnRight = _confirmYes };
            _confirmPage.SetActive(false);
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
            _group = go.AddComponent<CanvasGroup>();
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

            // 2026-11-12 (UI rough spots): the title used to sit on the HUD's score and clock (top 128 px). A solid title
            // band now covers that strip while the menu is open, so the title reads on calm space and never on the clock.
            Image band = MenuUi.Rt(space, "Title Band").gameObject.AddComponent<Image>();
            band.rectTransform.anchorMin = new Vector2(0f, 1f);
            band.rectTransform.anchorMax = new Vector2(1f, 1f);
            band.rectTransform.pivot = new Vector2(0.5f, 1f);
            band.rectTransform.sizeDelta = new Vector2(4000f, UiScale.TitleBandH);   // wider than any aspect's margin
            band.rectTransform.anchoredPosition = Vector2.zero;
            band.color = CMTokens.WithAlpha(Navy, 0.94f);
            band.raycastTarget = false;
            _title = MenuUi.Label(space, "Title", CMTokens.Display, UiScale.TypeTitle, Cream, TextAnchor.MiddleCenter, 460f, 20f, 1000f, 96f, "PAUSED", 4f);
            MenuUi.Fit(_title, UiScale.TypeHeading);
            _hint = MenuUi.Label(space, "Hint", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter, 260f, 1020f, 1400f, 36f, "", 2f);
            _hint.gameObject.SetActive(false);   // drawn by _hintStrip; kept as the plain line
            _hintStrip = PromptStrip.Make(space, "Hint Prompts", 30f, 20, Cream);
            MenuUi.Place(_hintStrip.Rect, 260f, 1016f, 1400f, 44f);

            _extraDefs.Clear();
            _extraDefs.AddRange(ExtraPages);
            BuildRoot(space);
            BuildSettings(space);
            BuildExtraPages(space);
            BuildClassPage(space);
            BuildConfirmPage(space);
            Canvas.enabled = false;
            _textScale = UiTextScale.Capture(space);
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
            if (_textScale != null) _textScale.Apply(UiTextScale.Setting);
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
            AddSpaceSubmit(m);
            return es;
        }

        /// <summary>
        /// controls-1: SPACE confirms in every menu as well as ENTER (the default UI actions bind Submit to ENTER only), so a
        /// keyboard player in either hand position presses with the thumb. Added once; a failure leaves ENTER working.
        /// </summary>
        public static void AddSpaceSubmit(InputSystemUIInputModule m)
        {
            InputAction submit = m != null && m.submit != null ? m.submit.action : null;
            if (submit == null) return;
            foreach (InputBinding b in submit.bindings)
                if (string.Equals(b.path, "<Keyboard>/space", StringComparison.OrdinalIgnoreCase)) return;
            InputActionMap map = submit.actionMap;
            bool on = map != null ? map.enabled : submit.enabled;
            try
            {
                if (map != null) map.Disable();
                else submit.Disable();
                submit.AddBinding("<Keyboard>/space");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CM.Controls] SPACE could not be added to the menus' confirm ({e.GetType().Name})");
            }
            finally
            {
                if (on)
                {
                    if (map != null) map.Enable();
                    else submit.Enable();
                }
            }
        }

        void BuildRoot(RectTransform space)
        {
            RectTransform page = MenuUi.Rt(space, "Root Page");
            MenuUi.Stretch(page);
            _rootPage = page.gameObject;
            // DG-01: How to play (the guided match, when an item registers its start) and Controls joined the list.
            // Integration batch 2: extra pages (the Codex) follow SETTINGS; the stride tightens so 8 entries fit one panel.
            bool howTo = Flow.OverlayRegistry.TutorialStart() != null;
            // 2026-11-12: Spectate / Rejoin, Change class and Switch team joined the list.
            var labels = new List<string> { "RESUME", "SPECTATE", "CHANGE CLASS", "SWITCH TEAM", "HOW TO PLAY", "CONTROLS", "SETTINGS" };
            var acts = new List<Action>
            {
                Resume, ToggleSpectate, ShowClassPicker, ShowSwitchTeam,
                () => { if (Flow.OverlayRegistry.StartTutorial()) Resume(); }, ShowControls, // DG-14: the one-screen controls card
                () => ShowSettings(Tab.Audio),
            };
            if (!howTo)
            {
                // 2026-11-12: no guided match registered: the line is left out rather than shown as COMING SOON.
                labels.RemoveAt(4);
                acts.RemoveAt(4);
            }
            for (int e = 0; e < _extraDefs.Count; e++)
            {
                int k = e;
                labels.Add(_extraDefs[e].Label);
                acts.Add(() => ShowExtra(k));
            }
            labels.AddRange(new[] { "RESTART MATCH", "QUIT TO TITLE", "QUIT GAME" });
            acts.AddRange(new Action[] { RestartMatch, QuitToTitle, QuitGame });
            // The list stays centred in one panel: 10 lines at 72 px (PR #35's layout), tighter when extra pages add lines.
            int n = labels.Count;
            float stride = n <= 10 ? 72f : 66f, itemH = n <= 10 ? 62f : 58f;
            float total = (n - 1) * stride + itemH;
            // The panel starts under the title band (UiScale.TitleBandH) with one 16 px step of air.
            float y0 = Mathf.Max(UiScale.TitleBandH + UiScale.S4 + 28f, 540f - total * 0.5f);
            MenuUi.Panel(page, "Panel", 640f, y0 - 28f, 640f, total + 56f);
            var items = new List<MenuItem>();
            for (int i = 0; i < n; i++)
            {
                MenuItem it = MenuUi.Item(page, labels[i], 700f, y0 + i * stride, 520f, itemH, labels[i], MenuItem.Kind.Button,
                    n <= 10 ? 28 : 26, true, CMTokens.Display);
                it.Body.Radius = 16f;
                it.Submitted = acts[i];
                // controls-1 hygiene: restart and the two quits ask once ("press again") so one stray ENTER, SPACE or A
                // never throws a match away.
                if (labels[i] == "RESTART MATCH" || labels[i] == "QUIT TO TITLE" || labels[i] == "QUIT GAME")
                {
                    MenuItem me = it;
                    Action act = acts[i];
                    string label = labels[i];
                    it.Submitted = () => ConfirmThen(me, label, act);
                }
                if (i == 1) _spectate = it;
                if (i == 2) _changeClass = it;
                if (i == 3) _switchTeam = it;
                items.Add(it);
            }
            // Under the panel, never inside its last line.
            _rootNote = MenuUi.Fit(MenuUi.Label(page, "Note", CMTokens.Ui, UiScale.TypeBody, Brass, TextAnchor.MiddleCenter, 520f,
                Mathf.Min(y0 + total + 28f + UiScale.S3, 1016f - 40f), 880f, 36f, "", 2f), UiScale.TypeMin);
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

        public const float PanelX = 280f, PanelY = 140f, PanelW = 1360f, PanelH = 870f;
        public const float ContentX = PanelX + 40f, ContentY = PanelY + 110f;

        void BuildSettings(RectTransform space)
        {
            RectTransform page = MenuUi.Rt(space, "Settings Page");
            MenuUi.Stretch(page);
            _settingsPage = page.gameObject;
            MenuUi.Panel(page, "Panel", PanelX, PanelY, PanelW, PanelH);
            var extraTabs = new List<ExtraTab>(ExtraTabs);
            var tabNames = new List<string> { "CONTROLS", "AUDIO", "VIDEO", "GAME" };
            for (int e = 0; e < extraTabs.Count; e++) tabNames.Add(extraTabs[e].Name);
            int tabs = tabNames.Count;
            _tabPages = new GameObject[tabs];
            _tabButtons = new MenuItem[tabs];
            // Four tabs keep the original layout; more share the row up to the Back button.
            float tabStride = tabs <= 4 ? 250f : 1060f / tabs, tabW = tabs <= 4 ? 230f : tabStride - 14f;
            for (int i = 0; i < tabs; i++)
            {
                int k = i;
                MenuItem t = MenuUi.Item(page, "Tab " + tabNames[i], ContentX + i * tabStride, PanelY + 30f, tabW, 60f, tabNames[i],
                    MenuItem.Kind.Button, tabs <= 4 ? 26 : 22, true, CMTokens.Display);
                t.Submitted = () => ShowTab((Tab)k);
                _tabButtons[i] = t;
            }
            MenuItem back = MenuUi.Item(page, "Back", PanelX + PanelW - 40f - 200f, PanelY + 30f, 200f, 60f, "BACK",
                MenuItem.Kind.Button, UiScale.TypeButton, true, CMTokens.Display);
            back.Submitted = ShowRoot;

            _tabPages[0] = BuildControls(page);
            _tabPages[1] = BuildAudio(page);
            _tabPages[2] = BuildVideo(page);
            _tabPages[3] = BuildGame(page);
            for (int e = 0; e < extraTabs.Count; e++) _tabPages[4 + e] = BuildExtraTab(page, extraTabs[e]);

            _status = MenuUi.Label(page, "Status", CMTokens.Ui, UiScale.TypeBody, Brass, TextAnchor.MiddleLeft, ContentX, PanelY + PanelH - 70f,
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
            for (int i = 0; i < tabs; i++)
            {
                MenuItem first = FirstItem(_tabPages[i]);
                Navigation n = _tabButtons[i].navigation;
                n.selectOnDown = first;
                _tabButtons[i].navigation = n;
            }
        }

        GameObject BuildExtraTab(RectTransform parent, ExtraTab def)
        {
            RectTransform tab = MenuUi.Rt(parent, def.Name + " Tab");
            MenuUi.Stretch(tab);
            List<MenuItem> items = null;
            try { items = def.Build != null ? def.Build(this, tab) : null; }
            catch (Exception e) { Debug.LogException(e); }
            if (items != null && items.Count > 0) ChainInTab(items);
            return tab.gameObject;
        }

        void BuildExtraPages(RectTransform space)
        {
            _extraPages.Clear();
            _extraFirst.Clear();
            for (int i = 0; i < _extraDefs.Count; i++)
            {
                RectTransform page = MenuUi.Rt(space, _extraDefs[i].Label + " Page");
                MenuUi.Stretch(page);
                Selectable first = null;
                try { first = _extraDefs[i].Build != null ? _extraDefs[i].Build(this, page) : null; }
                catch (Exception e) { Debug.LogException(e); }
                page.gameObject.SetActive(false);
                _extraPages.Add(page.gameObject);
                _extraFirst.Add(first);
            }
        }

        static MenuItem FirstItem(GameObject page)
        {
            MenuItem[] all = page.GetComponentsInChildren<MenuItem>(true);
            return all.Length > 0 ? all[0] : null;
        }

        public void SetStatus(string s, bool warning)
        {
            if (_status == null) return;
            _status.color = warning ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color;
            HudText.Set(_status, s);
        }

        // ---- Controls tab ----

        // Easy movement: rows 36 + 3 (were 40 + 4) so the Walk to pointer row and the two movement toggles fit above Reset.
        // Integration batch 2: the two toggles sit on Reset's line (420 px each, left of it), which leaves the list its height.
        const float RowH = 36f, RowGap = 3f, LabelW = 330f, CellW = 210f, CellGap = 14f, ToggleH = 46f, ToggleW = 420f;

        // Integration batch 2: 19 rows (Walk to pointer, Map, Ping, HUD cursor and Minimap zoom joined) at 30 + 3 px take the room 14 rows
        // took at 40 + 4 px, so the list still ends above the status line.
        const float CtlRowH = 30f, CtlRowGap = 3f;
        // controls-1: five columns (keys W A S D hand, keys arrow hand, mouse, controller, controller 2) and 24 rows at
        // 26 + 2 px, so the list still ends above the toggles and Reset.
        const int GridCols = 5, KeyCols = 3;
        const float GridRowH = 26f, GridRowGap = 2f, GridCellW = 172f, GridCellGap = 8f;

        GameObject BuildControls(RectTransform parent)
        {
            RectTransform tab = MenuUi.Rt(parent, "Controls Tab");
            MenuUi.Stretch(tab);
            float y = ContentY;
            string[] heads = { "KEYS: W A S D", "KEYS: ARROWS", "MOUSE", "CONTROLLER", "CONTROLLER 2" };
            for (int c = 0; c < GridCols; c++)
                MenuUi.Label(tab, "Head " + c, CMTokens.Ui, UiScale.TypeMin, Brass, TextAnchor.MiddleCenter, ContentX + LabelW + c * (GridCellW + GridCellGap), y,
                    GridCellW, 28f, heads[c], 2f);
            y += 32f;

            // DG-14: VIEW CONTROLS at the top of the tab, in the empty corner above the action labels.
            MenuItem view = MenuUi.Item(tab, "View Controls", ContentX, ContentY - 4f, LabelW - 20f, 34f, "VIEW CONTROLS",
                MenuItem.Kind.Button, UiScale.TypeSmall, true, CMTokens.Display);
            view.Submitted = ShowControls;

            InputActionAsset asset = InputSystem.actions;
            var grid = new List<MenuItem[]>();
            for (int r = 0; r < ControlBindings.Rows.Length; r++)
            {
                ControlRow row = ControlBindings.Rows[r];
                MenuUi.Label(tab, "Row " + row.Label, CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, ContentX + 8f, y, LabelW - 8f, GridRowH,
                    row.Label.ToUpperInvariant(), 2f);
                var line = new MenuItem[GridCols];
                var slots = new List<RebindSlot>(row.Keyboard);
                slots.AddRange(row.Gamepad);
                for (int si = 0; si < slots.Count; si++)
                {
                    RebindSlot slot = slots[si];
                    int c = Mathf.Clamp((int)slot.Column, 0, GridCols - 1);
                    if (line[c] != null) continue;
                    MenuItem cell = MenuUi.Item(tab, $"Cell {row.Label} {c}", ContentX + LabelW + c * (GridCellW + GridCellGap), y, GridCellW, GridRowH, "-",
                        MenuItem.Kind.Button, UiScale.TypeMin, true, CMTokens.Mono);
                    var cref = new CellRef { Item = cell, Slot = slot, Row = row };
                    // The binding's glyph beside its name (a mouse with the button lit, a keycap, a controller button).
                    cref.Glyph = InputGlyphView.Make(cell.transform, "Glyph", 22f);
                    cref.Glyph.Rect.anchoredPosition = new Vector2(8f, 0f);
                    cell.Submitted = () => StartRebind(cref);
                    if (!slot.Rebindable) cell.interactable = false;
                    _cells.Add(cref);
                    line[c] = cell;
                }
                grid.Add(line);
                y += GridRowH + GridRowGap;
            }
            MenuItem reset = MenuUi.Item(tab, "Reset Controls", PanelX + PanelW - 40f - 340f, PanelY + PanelH - 78f, 340f, 56f,
                "RESET TO DEFAULTS", MenuItem.Kind.Button, UiScale.TypeBody, true, CMTokens.Display);
            reset.Submitted = () =>
            {
                ControlBindings.ResetAll(InputSystem.actions);
                SetStatus("Every control is back to its default.", false);
            };

            // Easy movement toggles (GameSettings, kept between launches; HumanInputSource applies them at once).
            y = PanelY + PanelH - 78f + 5f;
            MenuItem assist = MoveToggle(tab, "Movement Assist", ContentX, y, () => GameSettings.MovementAssist,
                v => GameSettings.MovementAssist = v, "MOVEMENT ASSIST",
                "Movement assist on: one key follows a bridge it points along, and you slide past corners.",
                "Movement assist off: you move exactly where the keys point.");
            MenuItem keys = MoveToggle(tab, "Keys Follow Map", ContentX + ToggleW + 20f, y, () => GameSettings.KeysFollowMap,
                v => GameSettings.KeysFollowMap = v, "KEYS FOLLOW THE MAP",
                "Keys follow the map: W, A, S and D move along the map's grid instead of the screen.",
                "Keys follow the screen: W moves up the screen.");

            WireGrid(grid, reset);
            MenuItem firstCell = grid.Count > 0 ? (grid[0][0] ?? grid[0][KeyCols]) : reset;
            view.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = _tabButtons[0], selectOnDown = firstCell };
            for (int c = 0; grid.Count > 0 && c < GridCols; c++)
            {
                if (grid[0][c] == null) continue;
                Navigation n = grid[0][c].navigation;
                n.selectOnUp = view;
                grid[0][c].navigation = n;
            }
            // The grid's bottom edge now leads to the toggles, the toggles to Reset.
            MenuItem lastLeft = null, lastRight = null;
            for (int r = grid.Count - 1; r >= 0 && (lastLeft == null || lastRight == null); r--)
            {
                if (lastLeft == null) lastLeft = grid[r][0] ?? grid[r][1] ?? grid[r][2];
                if (lastRight == null) lastRight = grid[r][3] ?? grid[r][4];
            }
            for (int r = 0; r < grid.Count; r++)
            {
                for (int c = 0; c < GridCols; c++)
                {
                    MenuItem it = grid[r][c];
                    if (it == null || it.navigation.selectOnDown != reset) continue;
                    Navigation n = it.navigation;
                    n.selectOnDown = c < KeyCols ? assist : keys;
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
            MenuItem it = MenuUi.Item(tab, name, x, y, ToggleW, ToggleH, label, MenuItem.Kind.Button, UiScale.TypeSmall, true, CMTokens.Display);
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
                for (int c = 0; c < GridCols; c++)
                {
                    MenuItem it = grid[r][c];
                    if (it == null) continue;
                    var n = new Navigation { mode = Navigation.Mode.Explicit };
                    n.selectOnUp = Find(grid, r, c, -1) ?? (Selectable)_tabButtons[0];
                    n.selectOnDown = Find(grid, r, c, 1) ?? reset;
                    for (int d = c - 1; d >= 0 && n.selectOnLeft == null; d--) n.selectOnLeft = grid[r][d];
                    for (int d = c + 1; d < GridCols && n.selectOnRight == null; d++) n.selectOnRight = grid[r][d];
                    it.navigation = n;
                }
            }
            MenuItem last = null;
            for (int r = rows - 1; r >= 0 && last == null; r--) last = grid[r][0] ?? grid[r][KeyCols];
            reset.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = last, selectOnDown = _tabButtons[0] };
        }

        static MenuItem Find(List<MenuItem[]> grid, int r, int c, int dir)
        {
            for (int i = r + dir; i >= 0 && i < grid.Count; i += dir)
            {
                if (grid[i][c] != null) return grid[i][c];
                // A gap in this column: the nearest filled cell of that row.
                for (int d = 1; d < GridCols; d++)
                {
                    if (c - d >= 0 && grid[i][c - d] != null) return grid[i][c - d];
                    if (c + d < GridCols && grid[i][c + d] != null) return grid[i][c + d];
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
                if (c.Glyph != null)
                {
                    string path = a != null && idx >= 0 ? a.bindings[idx].effectivePath : null;
                    GlyphSpec g = InputGlyphs.ForPath(path);
                    c.Glyph.gameObject.SetActive(!g.IsEmpty);
                    if (!g.IsEmpty) c.Glyph.Set(g);
                }
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

        public const float LineH = 64f, LineGap = 14f, LineW = PanelW - 80f;

        GameObject BuildAudio(RectTransform parent)
        {
            RectTransform tab = MenuUi.Rt(parent, "Audio Tab");
            MenuUi.Stretch(tab);
            var items = new List<MenuItem>
            {
                Slider(tab, 0, "MASTER VOLUME", () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v),
                Slider(tab, 1, "MUSIC", () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v),
                Slider(tab, 2, "EFFECTS", () => GameSettings.EffectsVolume, v => GameSettings.EffectsVolume = v),
                // controls-1 round 2: mute switches keep the slider where it was.
                Stepper(tab, 3, "MUTE MUSIC", () => GameSettings.MuteMusic ? "ON" : "OFF", d => GameSettings.MuteMusic = !GameSettings.MuteMusic),
                Stepper(tab, 4, "MUTE SOUND EFFECTS", () => GameSettings.MuteEffects ? "ON" : "OFF", d => GameSettings.MuteEffects = !GameSettings.MuteEffects),
                Stepper(tab, 5, "MUTE WHEN IN BACKGROUND", () => GameSettings.MuteInBackground ? "ON" : "OFF", d => GameSettings.MuteInBackground = !GameSettings.MuteInBackground),
            };
            Note(tab, 6, "Routed through the game's audio mixer (Master, Music, Effects). A mute keeps the slider's level.");
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
                Stepper(tab, 5, "SAVE POWER IN BACKGROUND", () => GameSettings.BackgroundThrottle ? "ON" : "OFF",
                    d => GameSettings.BackgroundThrottle = !GameSettings.BackgroundThrottle),
            };
            // DG-62: the safe-start notice lives here, neutral, never on the title screen.
            string safe = CriticalMass.Runtime.Game.FrameGovernor.SafeNotice;
            Note(tab, 6, safe ?? (GameSettings.CanChangeWindow ? "Changes apply at once and are kept for the next launch." :
                "Video settings are saved here and apply in the built game."));
            ChainInTab(items);
            return tab.gameObject;
        }

        GameObject BuildGame(RectTransform parent)
        {
            RectTransform tab = MenuUi.Rt(parent, "Game Tab");
            MenuUi.Stretch(tab);
            var items = new List<MenuItem>
            {
                // Camera shake, reduce flashing and reduce motion live on the ACCESS tab (one source of truth, SettingsExtras).
                Stepper(tab, 0, "DAMAGE NUMBERS", () => GameSettings.DamageNumbers ? "ON" : "OFF", d => GameSettings.DamageNumbers = !GameSettings.DamageNumbers),
                Stepper(tab, 1, "BLOOD", () => GameSettings.Blood ? "ON" : "OFF", d => GameSettings.Blood = !GameSettings.Blood),
                // Owner, 2026-11-13: the story intro plays at every launch; this turns that off, WATCH INTRO plays it now.
                Stepper(tab, 2, "PLAY INTRO AT LAUNCH", () => GameSettings.IntroAtLaunch ? "ON" : "OFF", d => GameSettings.IntroAtLaunch = !GameSettings.IntroAtLaunch),
                // controls-1 round 2: blue / amber teams for colour-blind players (HUD, rings, team text).
                Stepper(tab, 3, "COLOUR-BLIND TEAMS", () => GameSettings.ColourBlindTeams ? "BLUE / AMBER" : "OFF", d => GameSettings.ColourBlindTeams = !GameSettings.ColourBlindTeams),
                Stepper(tab, 4, "INTRO SUBTITLES", () => GameSettings.IntroSubtitles ? "ON" : "OFF", d => GameSettings.IntroSubtitles = !GameSettings.IntroSubtitles),
            };
            MenuItem watch = MenuUi.Item(tab, "Watch Intro", ContentX, ContentY + GameTabLines * (LineH + LineGap) + 20f, 420f, 56f,
                "WATCH INTRO", MenuItem.Kind.Button, UiScale.TypeBody, true, CMTokens.Display);
            watch.Submitted = WatchIntro;
            items.Add(watch);
            MenuItem reset = MenuUi.Item(tab, "Reset Settings", ContentX, ContentY + GameTabLines * (LineH + LineGap) + 96f, 420f, 56f,
                "RESET ALL SETTINGS", MenuItem.Kind.Button, UiScale.TypeBody, true, CMTokens.Display);
            reset.Submitted = () =>
            {
                GameSettings.ResetAll();
                CriticalMass.Runtime.Game.SupportReport.ResetDataFiles();   // DG-64: the data files and the keys together
                RefreshAll();
                SetStatus("Audio, video and game settings are back to their defaults.", false);
            };
            items.Add(reset);
            // DG-64: a zip of the logs a tester can send us (no upload; the decision ledger rules out crash services).
            MenuItem report = MenuUi.Item(tab, "Problem Report", ContentX, ContentY + GameTabLines * (LineH + LineGap) + 172f, 420f, 56f,
                "SAVE A PROBLEM REPORT", MenuItem.Kind.Button, UiScale.TypeBody, true, CMTokens.Display);
            report.Submitted = () =>
            {
                string zip = CriticalMass.Runtime.Game.SupportReport.Export(null, out string error);
                if (zip == null)
                {
                    SetStatus("The problem report could not be saved: " + error + ".", true);
                    return;
                }
                SetStatus("Problem report saved: " + System.IO.Path.GetFileName(zip) + " (shown in its folder).", false);
                CriticalMass.Runtime.Game.SupportReport.Reveal(zip);
            };
            items.Add(report);
            ChainInTab(items);
            return tab.gameObject;
        }

        /// <summary>Stepper lines on the Game tab (the three buttons sit under them).</summary>
        const int GameTabLines = 5;

        /// <summary>Settings > WATCH INTRO: closes the menu and plays the story intro (on the title, Home or setup; never over a
        /// live match, where the menu says so instead).</summary>
        public void WatchIntro()
        {
            FlowState st = CriticalMass.Runtime.Flow.GameFlow.State;
            if (st == FlowState.Match || st == FlowState.Results)
            {
                SetStatus("The intro can be watched from the title or Home.", false);
                return;
            }
            if (_external)
            {
                ClassSwitch.CloseExternalPicker();
                EndExternal(null);
            }
            if (IsOpen) Resume();
            // Next frame: the settings bridge puts Home back this frame, so the film freezes Home (not an empty screen).
            StartCoroutine(PlayIntroNextFrame());
        }

        static System.Collections.IEnumerator PlayIntroNextFrame()
        {
            yield return null;
            StoryIntro.Play();
        }

        static string ShakeText()
        {
            if (GameSettings.ReduceMotion) return "OFF (REDUCE MOTION)";
            float v = GameSettings.ShakeScale;
            return v <= 0f ? "OFF" : v < 0.75f ? "LIGHT" : "FULL";
        }

        static void StepShake(int d)
        {
            float[] steps = GameSettings.ShakeSteps;
            float v = GameSettings.ShakeScale;
            int at = 0;
            for (int i = 0; i < steps.Length; i++) if (Mathf.Abs(steps[i] - v) < Mathf.Abs(steps[at] - v)) at = i;
            int n = steps.Length;
            GameSettings.ShakeScale = steps[((at + (d < 0 ? -1 : 1)) % n + n) % n];
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

        public void Note(RectTransform tab, int line, string text)
        {
            MenuUi.Label(tab, "Note", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.8f), TextAnchor.MiddleLeft, ContentX + 8f,
                ContentY + line * (LineH + LineGap) + 10f, LineW, 36f, text, 2f);
        }

        public MenuItem Slider(RectTransform tab, int line, string label, Func<float> get, Action<float> set)
        {
            float y = ContentY + line * (LineH + LineGap);
            MenuItem it = MenuUi.Item(tab, label, ContentX, y, LineW, LineH, label, MenuItem.Kind.Slider, UiScale.TypeButton, false, CMTokens.Display);
            VectorShape bar = MenuUi.Shape(it.transform, "Bar", ShapeKind.RoundRect, 440f, 18f, 660f, 28f, CMTokens.Biomass.Color, -1f);
            bar.EmptyColor = CMTokens.MeterEmpty.Color;
            bar.RimPx = 0f;
            it.Bar = bar;
            it.Value = MenuUi.Label(it.transform, "Value", CMTokens.Mono, UiScale.TypeBody, MenuItem.IdleText, TextAnchor.MiddleRight, LineW - 160f, 0f, 140f, LineH, "", 2f);
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

        public MenuItem Stepper(RectTransform tab, int line, string label, Func<string> text, Action<int> step)
        {
            float y = ContentY + line * (LineH + LineGap);
            MenuItem it = MenuUi.Item(tab, label, ContentX, y, LineW, LineH, label, MenuItem.Kind.Stepper, UiScale.TypeButton, false, CMTokens.Display);
            it.Value = MenuUi.Fit(MenuUi.Label(it.transform, "Value", CMTokens.Ui, UiScale.TypeButton, MenuItem.IdleText, TextAnchor.MiddleCenter, 460f, 0f, 640f, LineH, "", 2f), 26);
            MenuUi.Label(it.transform, "Left", CMTokens.Display, UiScale.TypeButton, Brass, TextAnchor.MiddleCenter, 420f, 0f, 40f, LineH, "<", 2f);
            MenuUi.Label(it.transform, "Right", CMTokens.Display, UiScale.TypeButton, Brass, TextAnchor.MiddleCenter, 1100f, 0f, 40f, LineH, ">", 2f);
            Action refresh = () => HudText.Set(it.Value, text());
            it.Stepped = d =>
            {
                step(d);
                refresh();
            };
            _refreshers.Add(refresh);
            return it;
        }

        public void RefreshAll()
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

        /// <summary>"60 FPS", "OFF" (0: the display's rate) or "UNLIMITED" (-1: vsync off).</summary>
        public static string FpsCapText()
        {
            int v = GameSettings.FpsCap;
            return v == 0 ? "OFF" : v == CriticalMass.Runtime.Game.FramePolicy.Unlimited ? "UNLIMITED" : v + " FPS";
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

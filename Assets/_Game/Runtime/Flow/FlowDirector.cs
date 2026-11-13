using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>
    /// DG-01: runs the game flow on the Game root GameBootstrap builds. Title (TitleScreen) -> main menu -> match setup ->
    /// match (the HUD) -> results -> Play again / Change class / Main menu. Watches the sim's own end signal
    /// (World.Match.Phase == Ended, set by Match.MatchSystem when the clock reaches MatchSeconds, whatever the score,
    /// draws included) and puts the results screen up ResultsDelaySeconds later, closing the pause menu if it is open, so
    /// a finished match never leaves the player in the world with dead controls; this holds with the flow off too
    /// (editor), except in capture mode. Owns the Pause press while one of its screens is up (Escape / Start = Back) and
    /// gives it back to the pause menu in a match. Fades to Void Navy for 160 ms before a reload and back after it (view
    /// only).
    /// </summary>
    public sealed class FlowDirector : MonoBehaviour
    {
        public const float ResultsDelaySeconds = 0.6f;
        public const float FadeSeconds = 0.16f;

        public static FlowDirector Current { get; private set; }

        public SimDriverBehaviour Driver;
        public GameHud Hud;
        public HumanInputSource Input;

        /// <summary>Unscaled time the match end was first seen (-1: not ended).</summary>
        public float EndedAt { get; private set; } = -1f;
        public bool Loading => _loadPath != null;
        public bool SettingsOpen => _settingsBridge;

        Func<bool> _onPause;
        bool _settingsBridge;
        string _settingsReturnId;
        MonoBehaviour _external;
        float _resultsAt;
        string _loadPath;
        bool _loadIssued;
        float _fadeStart;
        float _fadeFrom, _fadeTo;
        Canvas _fadeCanvas;
        Image _fade;
        static bool _fadeInNext;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            Current = null;
            _fadeInNext = false;
        }

        public MainMenu MainMenu => GetScreen<MainMenu>();
        public MatchSetup Setup => GetScreen<MatchSetup>();
        public ResultsScreen Results => GetScreen<ResultsScreen>();
        /// <summary>Home lobby: the mode cards (Setup state).</summary>
        public ModeSelect Modes => GetScreen<ModeSelect>();
        /// <summary>Home lobby: the "match ready" rosters and countdown (Setup state).</summary>
        public ReadyScreen Ready => GetScreen<ReadyScreen>();

        T GetScreen<T>() where T : FlowScreen
        {
            T s = GetComponent<T>();
            if (s == null)
            {
                s = gameObject.AddComponent<T>();
                s.Director = this;
            }
            return s;
        }

        /// <summary>Adds the stats recorder and the director to the Game root and enters the flow's current state.</summary>
        public static FlowDirector Install(GameObject game, SimDriverBehaviour driver, GameHud hud)
        {
            if (game.GetComponent<MatchStatsRecorder>() == null) game.AddComponent<MatchStatsRecorder>();
            FlowDirector d = game.GetComponent<FlowDirector>();
            if (d == null) d = game.AddComponent<FlowDirector>();
            d.Driver = driver;
            d.Hud = hud;
            d.Input = game.GetComponent<HumanInputSource>();
            d.EnterState();
            return d;
        }

        void Awake()
        {
            Current = this;
            _onPause = OnPause;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            Menus.LoadingScreen.Hidden -= ReleaseLoadingHold;
            if (Input != null && Input.PauseHandler == _onPause) Input.PauseHandler = null;
        }

        void EnterState()
        {
            if (_fadeInNext)
            {
                _fadeInNext = false;
                StartFade(1f, 0f);
            }
            switch (GameFlow.State)
            {
                case FlowState.Title:
                    SetHud(false);
                    if (GetComponent<TitleScreen>() == null) TitleScreen.Show(gameObject, Hud);
                    break;
                case FlowState.Menu:
                    SetHud(false);
                    MainMenu.Show();
                    break;
                case FlowState.Setup:
                    SetHud(false);
                    // A backdrop renewal while in the play flow comes back to the mode cards, not the custom setup.
                    if (PlayFlow.InModeFlow) Modes.Show();
                    else Setup.Show();
                    break;
                case FlowState.Match:
                case FlowState.Off:
                    SetHud(true);
                    HoldForLoading();
                    break;
            }
        }

        // ---- DG-13 loading screen: the match clock waits behind it ----

        double _heldPace = -1;

        /// <summary>The loading screen is up (GameFlow.BeginMatch): no sim tick runs until it hides.</summary>
        void HoldForLoading()
        {
            if (!Menus.LoadingScreen.IsOpen || Driver == null) return;
            _heldPace = Driver.TimeScale;
            Driver.TimeScale = 0;
            Menus.LoadingScreen.Hidden -= ReleaseLoadingHold;
            Menus.LoadingScreen.Hidden += ReleaseLoadingHold;
            Menus.LoadingScreen.WatchBuilt();
        }

        void ReleaseLoadingHold()
        {
            Menus.LoadingScreen.Hidden -= ReleaseLoadingHold;
            if (_heldPace >= 0 && Driver != null) Driver.TimeScale = _heldPace;
            _heldPace = -1;
        }

        /// <summary>The HUD shows only in a match (its component is off behind the menus, so a backdrop never counts).</summary>
        void SetHud(bool on)
        {
            if (Hud == null) return;
            Hud.enabled = on;
            if (Hud.Canvas != null) Hud.Canvas.enabled = on;
        }

        /// <summary>The visible flow screen, or null.</summary>
        public FlowScreen Visible
        {
            get
            {
                FlowScreen[] all = GetComponents<FlowScreen>();
                for (int i = 0; i < all.Length; i++)
                    if (all[i].IsVisible) return all[i];
                return null;
            }
        }

        /// <summary>Hides every flow screen (the play flow before it opens the class screen).</summary>
        public void HideFlowScreens() => HideScreens();

        void HideScreens()
        {
            FlowScreen[] all = GetComponents<FlowScreen>();
            for (int i = 0; i < all.Length; i++) all[i].Hide();
        }

        // ---- transitions (screens call these) ----

        /// <summary>The title was dismissed: the main menu (autoplay: straight into a match). false: the flow is off.</summary>
        public static bool TitleDismissed(TitleScreen title)
        {
            FlowDirector d = Current;
            if (d == null || GameFlow.State == FlowState.Off) return false;
            if (GameFlow.Autoplay)
            {
                GameFlow.BeginMatch(GameFlow.LastSetup);
                return true;
            }
            d.ShowMenu();
            return true;
        }

        /// <summary>
        /// Back to the title. The title appears at the end of this frame (LateUpdate), so the Escape or B that asked for it
        /// is no longer "pressed this frame" when the title first reads its any-key, and it does not dismiss itself.
        /// </summary>
        public void ShowTitle()
        {
            HideScreens();
            GameFlow.Enter(FlowState.Title);
            SetHud(false);
            _titleNext = true;
        }

        bool _titleNext;

        public void ShowMenu(string focusId = null)
        {
            PlayFlow.InModeFlow = false;
            HideScreens();
            GameFlow.Enter(FlowState.Menu);
            SetHud(false);
            MainMenu.Show();
            if (focusId != null) MainMenu.FocusEntry(focusId);
        }

        /// <summary>Home lobby: the mode select, focused on focus.</summary>
        public void ShowModes(MatchMode focus)
        {
            HideScreens();
            GameFlow.Enter(FlowState.Setup);
            SetHud(false);
            PlayFlow.InModeFlow = true;
            Modes.Show();
            Modes.FocusMode(focus);
        }

        /// <summary>Home lobby: the ready screen for a launch (rosters, map, objective, a skippable 3 s countdown).</summary>
        public void ShowReady(PlayLaunch launch)
        {
            if (launch == null) return;
            HideScreens();
            GameFlow.Enter(FlowState.Setup);
            SetHud(false);
            PlayFlow.InModeFlow = true;
            Ready.Bind(launch);
            Ready.Show();
        }

        /// <summary>Match setup; fromResults: its Back returns to the results (Change class).</summary>
        public void ShowSetup(bool fromResults = false)
        {
            HideScreens();
            GameFlow.Enter(FlowState.Setup);
            SetHud(false);
            Setup.FromResults = fromResults;
            Setup.Show();
        }

        /// <summary>Back from Change class: the same results again.</summary>
        public void ReturnToResults()
        {
            HideScreens();
            GameFlow.Enter(FlowState.Results);
            SetHud(false);
            Results.Show();
        }

        /// <summary>Opens the pause menu's own settings page (on tab) from the main menu; its Back returns here.</summary>
        public void OpenSettings(PauseMenu.Tab tab, string returnId)
        {
            PauseMenu pm = GetComponent<PauseMenu>();
            if (pm == null) return;
            HideScreens();
            _settingsBridge = true;
            _bridgePage = PauseMenu.Page.Settings;
            _settingsReturnId = returnId;
            if (Input != null && Input.PauseHandler == _onPause) Input.PauseHandler = null;
            pm.Pause();
            pm.ShowSettings(tab);
        }

        /// <summary>
        /// Opens one of the pause menu's extra pages (the Codex) from the main menu, the way OpenSettings opens the
        /// settings page; its Back returns here. False when the menu has no page with that label.
        /// </summary>
        public bool OpenPausePage(string label, string returnId)
        {
            PauseMenu pm = GetComponent<PauseMenu>();
            if (pm == null) return false;
            if (Input != null && Input.PauseHandler == _onPause) Input.PauseHandler = null;
            pm.Pause();
            int index = pm.ExtraPageIndex(label);
            if (index < 0)
            {
                pm.Resume();
                return false;
            }
            HideScreens();
            _settingsBridge = true;
            _bridgePage = PauseMenu.Page.Extra;
            _settingsReturnId = returnId;
            pm.ShowExtra(index);
            return true;
        }

        PauseMenu.Page _bridgePage = PauseMenu.Page.Settings;

        /// <summary>Opens a registered menu screen (Barracks, Codex...); its onClosed brings the main menu back.</summary>
        public void OpenRegistered(MenuEntry e)
        {
            if (e == null || e.ScreenType == null) return;
            Component c = GetComponent(e.ScreenType);
            if (c == null) c = gameObject.AddComponent(e.ScreenType);
            if (!(c is IMenuScreen screen)) return;
            HideScreens();
            _external = c as MonoBehaviour;
            string id = e.Id;
            screen.Open(() =>
            {
                _external = null;
                ShowMenu(id);
            });
        }

        /// <summary>Results' PLAY AGAIN (focused, one press): the same setup on a new seed, with the short loading screen.</summary>
        public void PlayAgain() => GameFlow.BeginMatch(GameFlow.LastSetup, true);

        public void ToMainMenu() => GameFlow.ReturnTo(FlowState.Menu);

        // ---- reload with a fade ----

        public void FadeThenLoad(string scenePath)
        {
            if (_loadPath != null) return;
            _loadPath = scenePath;
            HideScreens();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            StartFade(_fade != null ? _fade.color.a : 0f, 1f);
        }

        void StartFade(float from, float to)
        {
            EnsureFade();
            _fadeFrom = from;
            _fadeTo = to;
            _fadeStart = Time.unscaledTime;
            _fadeCanvas.enabled = true;
            SetFadeAlpha(from);
        }

        void EnsureFade()
        {
            if (_fade != null) return;
            var go = new GameObject("Flow Fade", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _fadeCanvas = go.AddComponent<Canvas>();
            _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _fadeCanvas.sortingOrder = 100;
            _fade = MenuUi.Rt(go.transform, "Fade").gameObject.AddComponent<Image>();
            MenuUi.Stretch(_fade.rectTransform);
            _fade.raycastTarget = true;
            SetFadeAlpha(0f);
            _fadeCanvas.enabled = false;
        }

        void SetFadeAlpha(float a)
        {
            if (_fade != null) _fade.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, a);
        }

        void UpdateFade()
        {
            if (_fadeCanvas == null || !_fadeCanvas.enabled) return;
            float k = Mathf.Clamp01((Time.unscaledTime - _fadeStart) / FadeSeconds);
            SetFadeAlpha(Mathf.Lerp(_fadeFrom, _fadeTo, k));
            if (k < 1f) return;
            if (_loadPath != null)
            {
                if (_loadIssued) return;   // the load happens at the start of the next frame
                _loadIssued = true;
                _fadeInNext = true;
                SceneManager.LoadScene(_loadPath);
                return;
            }
            if (_fadeTo <= 0f) _fadeCanvas.enabled = false;
        }

        // ---- per frame ----

        bool OnPause()
        {
            if (_loadPath != null) return true;
            // The loading screen covers the match: the press is swallowed (no pause menu under it).
            if (Menus.LoadingScreen.IsOpen) return true;
            // The story intro is on screen (WATCH INTRO from Home): it owns Esc (a deliberate skip); Home must not go Back
            // underneath it.
            if (CriticalMass.Runtime.Menus.StoryIntro.IsPlaying) return true;
            if (_external != null)
            {
                if (_external is IMenuBack back) back.Back();
                return true;
            }
            if (GetComponent<TitleScreen>() != null) return true;   // the title takes every key itself
            FlowScreen s = Visible;
            if (s != null) s.Back();
            return true;
        }

        void Update()
        {
            UpdateFade();
            if (Driver == null || Driver.World == null || _loadPath != null) return;
            FlowState st = GameFlow.State;
            ManagePause(st);
            World w = Driver.World;
            switch (st)
            {
                case FlowState.Match:
                case FlowState.Off:
                    WatchMatchEnd(w, st);
                    break;
                case FlowState.Title:
                case FlowState.Menu:
                case FlowState.Setup:
                    // The backdrop is about to run out: a fresh one on the same screen (never a frozen, finished world).
                    if (Driver.Request != null && Driver.Request.Backdrop && !_settingsBridge && _external == null &&
                        (w.Match.Phase == MatchPhase.Ended || w.Match.Time >= w.Config.MatchSeconds - 3))
                        GameFlow.RenewBackdrop();
                    break;
                case FlowState.Results:
                    if (GameFlow.Autoplay && Time.unscaledTime - _resultsAt >= GameFlow.AutoplayResultsSeconds) PlayAgain();
                    else if (!Results.IsVisible && _external == null) Results.Show();   // never left without a screen
                    break;
            }
        }

        /// <summary>
        /// Who takes the Pause press (pure; tests): the flow (its screens' Back) outside a match, and also while the
        /// loading screen covers a match (polish-audit-1: Escape or Start during loading toggled the pause menu UNDER the
        /// opaque loading screen, where Enter or A then pressed its invisible items, and the match opened paused).
        /// The settings bridge always leaves it to the pause menu.
        /// </summary>
        public static bool FlowOwnsPause(FlowState st, bool settingsBridge, bool loadingOpen) =>
            !settingsBridge && (loadingOpen || (st != FlowState.Match && st != FlowState.Off));

        void ManagePause(FlowState st)
        {
            if (Input == null) return;
            bool mine = FlowOwnsPause(st, _settingsBridge, Menus.LoadingScreen.IsOpen);
            if (mine) Input.PauseHandler = _onPause;
            else if (Input.PauseHandler == _onPause) Input.PauseHandler = null;   // the pause menu takes it back
        }

        void WatchMatchEnd(World w, FlowState st)
        {
            if (w.Match.Phase != MatchPhase.Ended)
            {
                EndedAt = -1f;
                return;
            }
            if (st == FlowState.Off && SimDriverBehaviour.CaptureMode) return;
            if (EndedAt < 0f) EndedAt = Time.unscaledTime;
            if (Time.unscaledTime - EndedAt >= ResultsDelaySeconds) ShowResults();
        }

        /// <summary>The match is over: close the pause menu, hide the HUD, show why it ended.</summary>
        public void ShowResults()
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null) return;
            PauseMenu pm = GetComponent<PauseMenu>();
            if (pm != null && pm.IsOpen) pm.Resume();
            _settingsBridge = false;
            HideScreens();
            GameFlow.Enter(FlowState.Results);
            SetHud(false);
            MatchStats stats = MatchStatsRecorder.Current;
            if (stats == null || stats.ActorCount != w.ActorCount) stats = MatchStats.Begin(w);
            stats.Finish(w);
            Results.Bind(w, stats, stats.Summarize(w));
            Results.Show();
            _resultsAt = Time.unscaledTime;
        }

        void LateUpdate()
        {
            if (_titleNext)
            {
                _titleNext = false;
                if (GameFlow.State == FlowState.Title && GetComponent<TitleScreen>() == null) TitleScreen.Show(gameObject, Hud);
            }
            if (!_settingsBridge) return;
            PauseMenu pm = GetComponent<PauseMenu>();
            if (pm != null && pm.IsOpen && pm.CurrentPage == _bridgePage) return;
            // The settings page's Back (Escape, B or its button) lands on the pause root: resume and return to the menu
            // in the same frame, so the pause root is never drawn behind the main menu.
            _settingsBridge = false;
            if (pm != null && pm.IsOpen) pm.Resume();
            ShowMenu(_settingsReturnId);
        }
    }
}

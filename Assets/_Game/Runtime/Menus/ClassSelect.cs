using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>Shared canvas setup for the DG-10/13/14 screens: Screen Space - Camera on Camera.main (so the capture
    /// tool's render includes it), Overlay when there is no camera yet; scale min(w/1920, h/1080) like the HUD.</summary>
    public static class MenuCanvas
    {
        public static Canvas Make(Transform parent, string name, int sortingOrder, out RectTransform space)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            Canvas c = go.AddComponent<Canvas>();
            c.sortingOrder = sortingOrder;
            Attach(c);
            go.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)go.transform;
            space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            return c;
        }

        public static void Attach(Canvas c)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                if (c.renderMode != RenderMode.ScreenSpaceCamera) c.renderMode = RenderMode.ScreenSpaceCamera;
                if (c.worldCamera != cam)
                {
                    c.worldCamera = cam;
                    c.planeDistance = Mathf.Max(0.36f, cam.nearClipPlane * 1.2f);
                }
            }
            else if (c.worldCamera == null && c.renderMode != RenderMode.ScreenSpaceOverlay) c.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        /// <summary>Re-attaches the camera if it changed and applies the HUD scale; returns the scale.</summary>
        public static float UpdateScale(Canvas c)
        {
            if (c == null) return 1f;
            Attach(c);
            UnityEngine.Camera cam = c.renderMode == RenderMode.ScreenSpaceCamera ? c.worldCamera : null;
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return c.scaleFactor;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(c.scaleFactor - s) > 1e-4f) c.scaleFactor = s;
            return s;
        }

        /// <summary>A full-screen Void Navy wash that also catches clicks.</summary>
        public static Image Wash(Transform canvasRoot, float alpha)
        {
            Image wash = MenuUi.Rt(canvasRoot, "Wash").gameObject.AddComponent<Image>();
            MenuUi.Stretch(wash.rectTransform);
            wash.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, alpha);
            wash.raycastTarget = true;
            wash.transform.SetAsFirstSibling();
            return wash;
        }
    }

    /// <summary>
    /// Class select (DG-10). Two modes on one Screen Space - Camera canvas:
    /// <para>Setup (ShowSetup, called by the game flow's match setup): five class tiles across the top, the focused class's
    /// full card (portrait, role, difficulty, bio, three abilities with key glyphs and cooldowns) and a turntable preview
    /// of its model (drag, or the right stick, turns it; it turns by itself when left alone). Enter / A / click on a tile,
    /// or PLAY AS ..., confirms; Escape / B goes back. The choice is kept (SelectedClass, PlayerPrefs cm.class) and passed
    /// to the callback: the flow builds the match with WorldOptions.HumanClass = it. The world is never touched here.</para>
    /// <para>Respawn (automatic): while actor 0 is knocked out (and the match runs, unpaused, no title), a strip of five
    /// compact cards sits under the HUD's respawn countdown; 1-5, click, or the focus plus Enter / A picks. The pick goes
    /// through HumanInputState.QueueClassPick, the one input path the sim already accepts while dead (Respawn.
    /// RequestClassChange); the "respawning as" line reads the sim's PendingClass back, so it shows what the sim holds.</para>
    /// Glyphs follow the device used last and every rebind. View only, no per-frame allocation.
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 30, Scope = CriticalMass.Runtime.Flow.OverlayScope.Always)]   // installed once by GameBootstrap through the overlay registry
    public sealed class ClassSelect : MonoBehaviour
    {
        public enum SelectMode { None, Setup, Respawn }

        public const string PrefsKey = "cm.class";
        public const int SetupSorting = 30, RespawnSorting = 15;

        static ClassSelect _instance;

        public static ClassSelect Instance => _instance;
        public static bool IsOpen => _instance != null && _instance.Mode != SelectMode.None;

        /// <summary>The class confirmed in setup last (kept between launches); Warrior until one is chosen.</summary>
        public static ClassId SelectedClass
        {
            get
            {
                int v = PlayerPrefs.GetInt(PrefsKey, 0);
                return v >= 0 && v < ClassIds.All.Count ? ClassIds.All[v] : ClassId.Warrior;
            }
            set
            {
                PlayerPrefs.SetInt(PrefsKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Setup confirmed a class.</summary>
        public static event Action<ClassId> Confirmed;
        /// <summary>A class was queued on the respawn screen (the sim applies it at the respawn).</summary>
        public static event Action<ClassId> RespawnPicked;

        /// <summary>false: the respawn strip never opens by itself (a later screen can take over).</summary>
        public static bool AutoRespawn = true;

        public SelectMode Mode { get; private set; } = SelectMode.None;
        public Canvas Canvas { get; private set; }
        public ClassId Focused { get; private set; } = ClassId.Warrior;
        public TeamId Team { get; private set; } = TeamId.Azure;
        public IReadOnlyList<ClassCard> SetupTiles => _setupTiles;
        public IReadOnlyList<ClassCard> RespawnTiles => _respawnTiles;
        public ClassCard Detail => _detail;

        RectTransform _space, _setupPage, _respawnPage;
        Image _wash;
        readonly List<ClassCard> _setupTiles = new List<ClassCard>(5);
        readonly List<ClassCard> _respawnTiles = new List<ClassCard>(5);
        ClassCard _detail;
        MenuItem _confirm;
        Text _hint, _respawnLine, _setupTitle;
        RawImage _previewImage;
        Text _previewNote;
        ClassPreview _preview;
        Action<ClassId> _onConfirm;
        Action _onBack;
        float _scale = 1f;
        float _lastDrag = -10f;
        bool _glyphsDirty = true;
        ClassId _shownPending = (ClassId)(-1);
        SimDriverBehaviour _driver;
        HumanInputSource _input;
        int _nextLook;
        string[] _respawnLines;

        static readonly string[] Hotkeys = { "1", "2", "3", "4", "5", "6" };

        // ---------------------------------------------------------------- static API

        /// <summary>The screen object; created in the active scene when the installer has not made one.</summary>
        public static ClassSelect Ensure()
        {
            if (_instance != null) return _instance;
            _instance = FindAnyObjectByType<ClassSelect>();
            if (_instance == null) _instance = new GameObject("Class Select").AddComponent<ClassSelect>();
            return _instance;
        }

        /// <summary>Opens setup for a team, focused on SelectedClass. onConfirm gets the chosen class; onBack runs on back.</summary>
        public static ClassSelect ShowSetup(TeamId team, Action<ClassId> onConfirm = null, Action onBack = null)
        {
            ClassSelect s = Ensure();
            // Owner walk 1: the click that opened this screen (a mode card, a double-click) must not also pick a class.
            MenuItem.SubmitBlockedUntil = Mathf.Max(MenuItem.SubmitBlockedUntil, Time.unscaledTime + CriticalMass.Runtime.Flow.FlowScreen.SubmitGuardSeconds);
            s.OpenSetup(team, SelectedClass, onConfirm, onBack);
            return s;
        }

        public static void Hide()
        {
            if (_instance != null) _instance.Close();
        }

        // ---------------------------------------------------------------- lifecycle

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
            _preview?.Dispose();
            _preview = null;
            if (_instance == this) _instance = null;
        }

        void OnDevice(ControlDevice d) => _glyphsDirty = true;
        void OnBindings() => _glyphsDirty = true;

        void FindGame()
        {
            if (_input == null) _input = GetComponent<HumanInputSource>();
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if ((_input == null || _driver == null) && Time.frameCount >= _nextLook)
            {
                _nextLook = Time.frameCount + 30;
                if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
                if (_input == null && _driver != null) _input = _driver.GetComponent<HumanInputSource>();
            }
        }

        // ---------------------------------------------------------------- build

        static Color Cream => CMTokens.WarmCream.Color;
        static Color Brass => CMTokens.ToolBrass.Color;

        const float TileW = 300f, TileH = 236f, TileGap = 24f, TileY = 140f;
        const float RTileW = 236f, RTileH = 236f, RTileGap = 16f, RTileY = 668f;
        const float DetailX = 162f, DetailY = 400f, DetailW = 1000f, DetailH = 610f;
        const float PrevX = 1186f, PrevY = 400f, PrevW = 572f, PrevH = 520f;

        public void Build()
        {
            if (Canvas != null) return;
            PauseMenu.EnsureEventSystem(transform);
            Canvas = MenuCanvas.Make(transform, "Class Select Canvas", SetupSorting, out _space);
            _wash = MenuCanvas.Wash(Canvas.transform, 0.9f);

            // ---- setup page
            _setupPage = MenuUi.Rt(_space, "Setup Page");
            MenuUi.Stretch(_setupPage);
            _setupTitle = MenuUi.Fit(MenuUi.Label(_setupPage, "Title", CMTokens.Display, UiScale.TypeTitle, Cream, TextAnchor.MiddleCenter, 360f, 34f, 1200f, 84f, "CHOOSE YOUR CLASS", 4f), 60);
            // Finisher, batch 4: six classes (Healer joined in batch 3) in one row that fits the screen; the row was laid
            // out for five, so the sixth tile ran off the right edge.
            int nTiles = ClassIds.All.Count;
            float tileW = Mathf.Min(TileW, (HudLayout.RefW - 2f * 80f - (nTiles - 1) * TileGap) / nTiles);
            float x0 = (HudLayout.RefW - (nTiles * tileW + (nTiles - 1) * TileGap)) * 0.5f;
            for (int i = 0; i < ClassIds.All.Count; i++)
            {
                ClassId cls = ClassIds.All[i];
                ClassCard t = ClassCard.Build(_setupPage, ClassCard.Layout.Tile, x0 + i * (tileW + TileGap), TileY, tileW, TileH, "Tile " + cls);
                t.Tile.Focused = () => FocusSetup(cls);
                t.Tile.Submitted = () => ConfirmSetup(cls);
                _setupTiles.Add(t);
            }
            _detail = ClassCard.Build(_setupPage, ClassCard.Layout.Detail, DetailX, DetailY, DetailW, DetailH, "Detail");
            VectorShape frame = MenuUi.Panel(_setupPage, "Preview Frame", PrevX, PrevY, PrevW, PrevH);
            _previewImage = MenuUi.Rt(frame.transform, "Preview").gameObject.AddComponent<RawImage>();
            MenuUi.Place(_previewImage.rectTransform, 12f, 12f, PrevW - 24f, PrevH - 24f);
            _previewImage.raycastTarget = true;
            _previewImage.gameObject.AddComponent<ClassPreviewDrag>().Owner = this;
            // Owner walk 1: the model on the class card is a control too: drag turns it, a click opens the Barracks for it.
            _previewNote = MenuUi.Label(frame.transform, "Customise Note", CMTokens.Display, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter,
                12f, PrevH - 52f, PrevW - 24f, 36f, "DRAG TO TURN   ·   CLICK TO CUSTOMISE", 2f);
            _previewNote.raycastTarget = false;
            _previewNote.color = CMTokens.WithAlpha(Cream, 0.7f);
            _confirm = MenuUi.Item(_setupPage, "Confirm", PrevX, PrevY + PrevH + 20f, PrevW, 70f, "PLAY AS WARRIOR", MenuItem.Kind.Button, UiScale.TypeHeading, true, CMTokens.Display);
            _confirm.Body.Radius = 16f;
            ClassCard.Fit(_confirm.Label, 18);
            _confirm.Submitted = () => ConfirmSetup(Focused);
            WireSetupNavigation();

            // ---- respawn page
            _respawnPage = MenuUi.Rt(_space, "Respawn Page");
            MenuUi.Stretch(_respawnPage);
            int nR = ClassIds.All.Count;
            float rx0 = (HudLayout.RefW - (nR * RTileW + (nR - 1) * RTileGap)) * 0.5f;
            _respawnLine = MenuUi.Label(_respawnPage, "Next Life", CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.MiddleCenter, rx0, RTileY - 46f, nR * RTileW + (nR - 1) * RTileGap, 36f, "", 3f);
            for (int i = 0; i < ClassIds.All.Count; i++)
            {
                ClassId cls = ClassIds.All[i];
                ClassCard t = ClassCard.Build(_respawnPage, ClassCard.Layout.Tile, rx0 + i * (RTileW + RTileGap), RTileY, RTileW, RTileH, "Respawn Tile " + cls);
                t.Tile.Submitted = () => PickRespawn(cls);
                t.SetHotkey(Hotkeys[i]);
                _respawnTiles.Add(t);
            }
            WireRow(_respawnTiles, null);
            _respawnLines = new string[ClassIds.All.Count];
            for (int i = 0; i < _respawnLines.Length; i++)
                _respawnLines[i] = "NEXT LIFE: " + LoreWords.Class(ClassIds.All[i]).Name.ToUpperInvariant() + "     1-" + ClassIds.All.Count + " TO SWITCH";

            _hint = MenuUi.Label(_space, "Hint", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter, 260f, 1028f, 1400f, 36f, "", 2f);
            Canvas.enabled = false;
            _setupPage.gameObject.SetActive(false);
            _respawnPage.gameObject.SetActive(false);
        }

        static void WireRow(List<ClassCard> row, Selectable down)
        {
            for (int i = 0; i < row.Count; i++)
            {
                row[i].Tile.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = row[(i + row.Count - 1) % row.Count].Tile,
                    selectOnRight = row[(i + 1) % row.Count].Tile,
                    selectOnDown = down,
                };
            }
        }

        void WireSetupNavigation()
        {
            WireRow(_setupTiles, _confirm);
            UpdateConfirmUp();
        }

        void UpdateConfirmUp()
        {
            _confirm.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = _setupTiles[(int)Focused].Tile };
        }

        // ---------------------------------------------------------------- open / close

        void OpenSetup(TeamId team, ClassId initial, Action<ClassId> onConfirm, Action onBack)
        {
            Build();
            CloseRespawn(false);
            Mode = SelectMode.Setup;
            Team = team;
            _onConfirm = onConfirm;
            _onBack = onBack;
            Canvas.sortingOrder = SetupSorting;
            Canvas.enabled = true;
            _wash.enabled = true;
            _setupPage.gameObject.SetActive(true);
            _respawnPage.gameObject.SetActive(false);
            _scale = MenuCanvas.UpdateScale(Canvas);
            for (int i = 0; i < _setupTiles.Count; i++) _setupTiles[i].Bind(ClassIds.All[i], team, _scale);
            if (_preview == null) _preview = new ClassPreview();
            _preview.SetActive(true);
            FocusSetup(initial);
            Select(_setupTiles[(int)initial].Tile);
            UpdateHint();
        }

        void FocusSetup(ClassId cls)
        {
            Focused = cls;
            _detail.Bind(cls, Team, _scale);
            for (int i = 0; i < _setupTiles.Count; i++) _setupTiles[i].Tile.SetPicked(ClassIds.All[i] == cls);
            HudText.Set(_confirm.Label, "PLAY AS " + LoreWords.Class(cls).Name.ToUpperInvariant());
            UpdateConfirmUp();
            if (_preview != null)
            {
                _preview.Show(cls, Team);
                _previewImage.texture = _preview.HasModel ? _preview.Texture : UiIcons.Get(UiIcons.Portrait[(int)cls, (int)Team], 4);
                _previewImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                RectTransform pr = _previewImage.rectTransform;
                float side = Mathf.Min(PrevW - 24f, PrevH - 24f);
                pr.sizeDelta = _preview.HasModel ? new Vector2(PrevW - 24f, PrevH - 24f) : new Vector2(side * 0.8f, side * 0.8f);
            }
        }

        /// <summary>Setup: moves the focus (and the detail card and preview) to cls.</summary>
        public void FocusClass(ClassId cls)
        {
            if (Mode != SelectMode.Setup) return;
            Select(_setupTiles[(int)cls].Tile);
            FocusSetup(cls);
        }

        /// <summary>Setup: keeps cls as SelectedClass, closes, then calls the callback and Confirmed.</summary>
        public void ConfirmSetup(ClassId cls)
        {
            if (Mode != SelectMode.Setup) return;
            SelectedClass = cls;
            Action<ClassId> cb = _onConfirm;
            Close();
            cb?.Invoke(cls);
            Confirmed?.Invoke(cls);
        }

        void OpenRespawn()
        {
            Build();
            Mode = SelectMode.Respawn;
            World w = _driver.World;
            Team = w.Actors[0].Team;
            Canvas.sortingOrder = RespawnSorting;
            Canvas.enabled = true;
            _wash.enabled = false;
            _setupPage.gameObject.SetActive(false);
            _respawnPage.gameObject.SetActive(true);
            _scale = MenuCanvas.UpdateScale(Canvas);
            for (int i = 0; i < _respawnTiles.Count; i++) _respawnTiles[i].Bind(ClassIds.All[i], Team, _scale);
            _shownPending = (ClassId)(-1);
            ClassId next = NextClass(w);
            ShowPending(next);
            Select(_respawnTiles[(int)next].Tile);
            UpdateHint();
        }

        void CloseRespawn(bool deselect)
        {
            if (Mode != SelectMode.Respawn) return;
            Mode = SelectMode.None;
            if (deselect) DeselectIfOurs();
            if (_respawnPage != null) _respawnPage.gameObject.SetActive(false);
            if (Canvas != null) Canvas.enabled = false;
        }

        public void Close()
        {
            if (Mode == SelectMode.None) return;
            if (Mode == SelectMode.Respawn)
            {
                CloseRespawn(true);
                return;
            }
            Mode = SelectMode.None;
            DeselectIfOurs();
            _preview?.SetActive(false);
            _setupPage.gameObject.SetActive(false);
            Canvas.enabled = false;
            _onConfirm = null;
            _onBack = null;
        }

        void DeselectIfOurs()
        {
            EventSystem es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null || Canvas == null) return;
            if (es.currentSelectedGameObject.transform.IsChildOf(Canvas.transform)) es.SetSelectedGameObject(null);
        }

        static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            s.Select();
        }

        // ---------------------------------------------------------------- respawn picks

        static ClassId NextClass(World w) => w.Actors[0].PendingClass ?? w.Actors[0].Cls;

        /// <summary>Respawn: queue cls for the next life (HumanInputState.QueueClassPick; the sim accepts it while dead).</summary>
        public void PickRespawn(ClassId cls)
        {
            FindGame();
            if (_input == null) return;
            _input.State.QueueClassPick(cls);
            SelectedClass = cls;
            ShowPending(cls);
            if (Mode == SelectMode.Respawn) Select(_respawnTiles[(int)cls].Tile);
            RespawnPicked?.Invoke(cls);
        }

        void ShowPending(ClassId cls)
        {
            if (cls == _shownPending) return;
            _shownPending = cls;
            for (int i = 0; i < _respawnTiles.Count; i++) _respawnTiles[i].Tile.SetPicked(ClassIds.All[i] == cls);
            HudText.Set(_respawnLine, _respawnLines[(int)cls]);
        }

        // ---------------------------------------------------------------- update

        void UpdateHint()
        {
            if (_hint == null) return;
            bool pad = ControlBindings.ActiveDevice == ControlDevice.Gamepad;
            string s;
            if (Mode == SelectMode.Setup) s = pad ? "A CHOOSE   ·   B BACK   ·   R STICK TURNS" : "ENTER OR CLICK TO CHOOSE   ·   ESC BACK   ·   DRAG TO TURN";
            else s = pad ? "D-PAD PICKS   ·   A CONFIRMS" : "1-6 OR CLICK TO PICK";
            HudText.Set(_hint, s);
        }

        bool RespawnWanted()
        {
            if (!AutoRespawn || _driver == null || _input == null) return false;
            World w = _driver.World;
            if (w == null || w.ActorCount < 1 || w.Match.Phase == MatchPhase.Ended) return false;
            if (_driver.HumanAutopilot || _input.State.Paused || ControlsCardScreen.IsOpen || LoadingScreen.IsOpen) return false;
            if (_driver.GetComponent<TitleScreen>() != null) return false;
            return !w.Actors[0].Alive;
        }

        void Update()
        {
            FindGame();
            if (Mode != SelectMode.Setup)
            {
                bool want = RespawnWanted();
                if (want && Mode == SelectMode.None) OpenRespawn();
                else if (!want && Mode == SelectMode.Respawn) CloseRespawn(true);
            }
            if (Mode == SelectMode.None) return;
            ControlBindings.Sample();
            float s = MenuCanvas.UpdateScale(Canvas);
            if (_glyphsDirty || Mathf.Abs(s - _scale) > 1e-3f)
            {
                _glyphsDirty = false;
                _scale = s;
                RefreshCards();
                UpdateHint();
            }
            if (Mode == SelectMode.Respawn) UpdateRespawn();
            else UpdateSetup();
        }

        void RefreshCards()
        {
            ControlDevice d = ControlBindings.ActiveDevice;
            List<ClassCard> tiles = Mode == SelectMode.Setup ? _setupTiles : _respawnTiles;
            for (int i = 0; i < tiles.Count; i++) tiles[i].Bind(ClassIds.All[i], Team, _scale);
            if (Mode == SelectMode.Setup) _detail.Bind(Focused, Team, _scale);
            else _detail.RefreshGlyphs(d);
        }

        void UpdateRespawn()
        {
            World w = _driver.World;
            ShowPending(NextClass(w));
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) PickRespawn(ClassId.Warrior);
                else if (kb.digit2Key.wasPressedThisFrame) PickRespawn(ClassId.Farmer);
                else if (kb.digit3Key.wasPressedThisFrame) PickRespawn(ClassId.Miner);
                else if (kb.digit4Key.wasPressedThisFrame) PickRespawn(ClassId.Demolitions);
                else if (kb.digit5Key.wasPressedThisFrame) PickRespawn(ClassId.Builder);
                else if (kb.digit6Key.wasPressedThisFrame) PickRespawn(ClassId.Healer);
            }
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == null && Gamepad.current != null && ControlBindings.ActiveDevice == ControlDevice.Gamepad)
                Select(_respawnTiles[(int)NextClass(w)].Tile);
        }

        void UpdateSetup()
        {
            // Owner walk 1: the Barracks opened from the class card own the input until they close (and the closing press
            // must not also close this screen).
            if (CharacterPanel.IsOpen || CharacterPanel.ClosedFrame >= Time.frameCount - 1) return;
            Keyboard kb = Keyboard.current;
            Gamepad gp = Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame))
            {
                Action back = _onBack;
                Close();
                back?.Invoke();
                return;
            }
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == null && ClassSelectNav.Pressed()) Select(_setupTiles[(int)Focused].Tile);
            float stick = gp != null ? gp.rightStick.ReadValue().x : 0f;
            if (Mathf.Abs(stick) > 0.2f)
            {
                _lastDrag = Time.unscaledTime;
                _preview?.Turn(stick * 180f * Time.unscaledDeltaTime);
            }
            _preview?.Tick(Time.unscaledDeltaTime, Time.unscaledTime - _lastDrag > 1.5f);
        }

        /// <summary>Owner walk 1: a click on the class card's model opens the Barracks for that class over this screen.</summary>
        internal void OpenBarracks()
        {
            if (Mode != SelectMode.Setup || CharacterPanel.IsOpen) return;
            Barracks.Show(Focused);
        }

        /// <summary>The pointer is over the model (the note brightens).</summary>
        internal void HoverPreview(bool on)
        {
            if (_previewNote != null) _previewNote.color = on ? Brass : CMTokens.WithAlpha(Cream, 0.7f);
        }

        /// <summary>Pointer drag on the preview turns the model (degrees per canvas unit 0.45).</summary>
        internal void Drag(float dx)
        {
            _lastDrag = Time.unscaledTime;
            _preview?.Turn(-dx * 0.45f);
        }

        static class ClassSelectNav
        {
            public static bool Pressed()
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame ||
                                   kb.enterKey.wasPressedThisFrame)) return true;
                Gamepad gp = Gamepad.current;
                return gp != null && (gp.dpad.left.wasPressedThisFrame || gp.dpad.right.wasPressedThisFrame || gp.leftStick.ReadValue().sqrMagnitude > 0.25f);
            }
        }
    }

    /// <summary>Pointer drag on the setup preview turns the model.</summary>
    public sealed class ClassPreviewDrag : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerClickHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        public ClassSelect Owner;
        bool _dragging;
        public void OnBeginDrag(PointerEventData e) => _dragging = true;
        public void OnEndDrag(PointerEventData e) => _dragging = false;
        public void OnDrag(PointerEventData e)
        {
            if (Owner != null) Owner.Drag(e.delta.x);
        }

        /// <summary>Owner walk 1: a click (not the end of a drag) opens the Barracks for the class on show.</summary>
        public void OnPointerClick(PointerEventData e)
        {
            if (_dragging || e.button != PointerEventData.InputButton.Left || Owner == null || Time.unscaledTime < MenuItem.SubmitBlockedUntil) return;
            Owner.OpenBarracks();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            UiCursor.Hover(true);
            if (Owner != null) Owner.HoverPreview(true);
        }

        public void OnPointerExit(PointerEventData e)
        {
            UiCursor.Hover(false);
            if (Owner != null) Owner.HoverPreview(false);
        }

        void OnDisable()
        {
            _dragging = false;
            if (UiCursor.Hovering) UiCursor.Reset();
        }
    }

    /// <summary>
    /// The setup turntable: the class's game model (ArtCatalog, team material) in a small rig far from the play area
    /// (layer 30, its own camera rendering into a RenderTexture), idle-animated and turning. One model per class key at
    /// most (cap 5), made once and reused. Classes without a model show their portrait instead (HasModel false).
    /// </summary>
    public sealed class ClassPreview
    {
        public const int Layer = 30;
        public const int TexW = 1120, TexH = 992;
        static readonly Vector3 Origin = new Vector3(5000f, -800f, 5000f);
        const float AutoTurnDegPerSec = 28f;

        GameObject _rig;
        UnityEngine.Camera _cam;
        RenderTexture _rt;
        Transform _pivot;
        readonly Dictionary<string, ModelView> _models = new Dictionary<string, ModelView>();
        ModelView _current;
        float _yaw = -30f;
        double _idleT;

        public Texture Texture => _rt;
        public bool HasModel => _current != null;

        void EnsureRig()
        {
            if (_rig != null) return;
            _rig = new GameObject("Class Preview Rig");
            _rig.transform.position = Origin;
            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(_rig.transform, false);
            var camGo = new GameObject("Preview Camera");
            camGo.transform.SetParent(_rig.transform, false);
            _cam = camGo.AddComponent<UnityEngine.Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.Lerp(CMTokens.PanelNavy.Color, CMTokens.VoidNavy.Color, 0.35f);
            _cam.cullingMask = 1 << Layer;
            _cam.fieldOfView = 26f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 60f;
            _rt = new RenderTexture(TexW, TexH, 24, RenderTextureFormat.ARGB32) { name = "Class Preview", antiAliasing = 4 };
            _rt.Create();
            _cam.targetTexture = _rt;
            _cam.enabled = false;
        }

        // Integration batch 2 (#25 fix): with no graphics device (the -nographics test run) a camera that draws into a texture
        // makes the pipeline log "RenderTexture.Create failed"; the preview simply stays off there.
        static bool HasGraphics => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        public void SetActive(bool on)
        {
            if (on) EnsureRig();
            if (_cam != null) _cam.enabled = on && _current != null && HasGraphics;
        }

        public void Show(ClassId cls, TeamId team)
        {
            EnsureRig();
            if (_current != null) _current.SetActive(false);
            _current = null;
            string key = ArtMapping.ClassModel(cls);
            ArtCatalog cat = ArtCatalog.Load();
            ArtEntry e = key != null && cat != null ? cat.Find(key) : null;
            if (e != null && ArtMapping.Spec(key) != null)
            {
                if (!_models.TryGetValue(key, out ModelView m) && _models.Count < ClassIds.All.Count)
                {
                    m = new ModelView(e, _pivot, "Preview " + key);
                    foreach (Renderer r in m.Renderers) r.gameObject.layer = Layer;
                    m.Root.layer = Layer;
                    _models[key] = m;
                }
                if (m != null)
                {
                    m.SetMaterial(ArtMaterials.ForTeam(m.Spec, team));
                    m.SetActive(true);
                    _current = m;
                    Frame(m);
                }
            }
            if (_cam != null) _cam.enabled = _current != null && HasGraphics;
        }

        /// <summary>Points the camera at the model's bounds, three-quarter from slightly above.</summary>
        void Frame(ModelView m)
        {
            Bounds b = new Bounds(_pivot.position + Vector3.up, Vector3.one);
            bool any = false;
            foreach (Renderer r in m.Renderers)
            {
                if (!any) b = r.bounds;
                else b.Encapsulate(r.bounds);
                any = true;
            }
            float radius = Mathf.Max(0.5f, b.extents.magnitude);
            float dist = radius / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            Vector3 dir = Quaternion.Euler(14f, 180f, 0f) * Vector3.forward;
            _cam.transform.position = b.center - dir * dist;
            _cam.transform.LookAt(b.center);
        }

        public void Turn(float degrees) => _yaw += degrees;

        /// <summary>Idle pose and turn; autoTurn when nobody is turning it.</summary>
        public void Tick(float dt, bool autoTurn)
        {
            if (_current == null) return;
            if (autoTurn) _yaw += AutoTurnDegPerSec * dt;
            _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            _idleT += dt;
            _current.BeginPose();
            _current.Pose(ArtMapping.Idle, _idleT, 1f, true);
            _current.Evaluate();
        }

        public void Dispose()
        {
            foreach (ModelView m in _models.Values) m.Dispose();
            _models.Clear();
            _current = null;
            if (_rt != null)
            {
                if (_cam != null) _cam.targetTexture = null;
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
                _rt = null;
            }
            if (_rig != null) UnityEngine.Object.Destroy(_rig);
            _rig = null;
        }
    }

    /// <summary>Attaches ClassSelect to the game's HumanInputSource object on boot and after every scene load.</summary>
    public static class ClassSelectInstaller
    {
        // Integration batch 2: installed by GameBootstrap through the DG-01 overlay registry ([GameOverlay]); Attach stays for tests.
        public static ClassSelect Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            HumanInputSource input = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
            if (input == null) return null;
            ClassSelect s = input.GetComponent<ClassSelect>();
            if (s == null) s = input.gameObject.AddComponent<ClassSelect>();
            return s;
        }
    }
}

using System;
using System.IO;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CriticalMass.Runtime.Menus.Characters
{
    /// <summary>
    /// b13-v: the CHARACTERS screen (main menu, order 50; also "See how to play" on the class select). Left: the six class
    /// tiles (portrait, name, one-line role). Right: three tabs, WATCH (the class's 8 s looping clip through a VideoPlayer
    /// into a RenderTexture, or the four slides as a cross-faded flipbook when the clip is missing or not ready in 2 s),
    /// HOW IT WORKS (four slides, dots, left / right to step) and CONTROLS (the class's rows with the CURRENT keyboard and
    /// controller bindings, so a rebind shows). Contained, no scrolling, keyboard, mouse and controller. Allocates on open
    /// (stills, the clip texture) and releases everything on close; nothing allocates per frame.
    /// </summary>
    [MenuEntry(MenuId, "CHARACTERS", 45)]   // b13 integrator: 45, after SETTINGS (40) and before CODEX (50), which already holds 50
    public sealed class CharactersScreen : MonoBehaviour, IMenuScreen, IMenuBack
    {
        public const string MenuId = "characters";
        public const int SortingOrder = 52;
        static readonly string[] TabNames = { "WATCH", "HOW IT WORKS", "CONTROLS" };

        public static CharactersScreen Instance { get; private set; }
        public static bool IsOpenNow => Instance != null && Instance._open;
        /// <summary>The frame the screen closed (a screen under it ignores that frame's Escape).</summary>
        public static int ClosedFrame { get; private set; } = -10;

        public bool IsOpen => _open;
        public ShowcaseNav Nav => _nav;
        /// <summary>True while WATCH shows the flipbook instead of the clip (tests and captures).</summary>
        public bool Flipbook { get; private set; } = true;

        Action _onClosed;
        bool _built, _open;
        Canvas _canvas;
        ShowcaseNav _nav;
        readonly MenuItem[] _tiles = new MenuItem[6];
        readonly HudIcon[] _portraits = new HudIcon[6];
        readonly MenuItem[] _tabs = new MenuItem[3];
        MenuItem _back, _prev, _next;
        RectTransform _watch, _how, _controls;
        Text _summary, _caption, _slideTitle, _slideLine, _hint, _ctrlHeadKey, _ctrlHeadPad;
        RawImage _videoImage, _flipA, _flipB, _still;
        readonly VectorShape[] _dots = new VectorShape[CharacterShowcaseData.SlideCount];
        readonly Text[] _ctrlLabel = new Text[8], _ctrlKey = new Text[8], _ctrlPad = new Text[8];
        readonly RectTransform[] _ctrlRow = new RectTransform[8];
        readonly Texture2D[,] _stills = new Texture2D[6, CharacterShowcaseData.SlideCount];
        string[] _captions, _plainCaptions;
        GameObject _videoHost;
        VideoPlayer _vp;
        RenderTexture _rt;
        bool _vpFailed, _vpPrepared, _clipThere;
        float _vpWait, _flipT;
        int _shownClass = -1, _shownSlide = -1, _shownFlipA = -1, _shownFlipB = -1;
        ShowcaseTab _shownTab = (ShowcaseTab)(-1);
        bool _shownInPane;
        ControlDevice _hintDevice = (ControlDevice)(-1);
        bool _bindingsDirty = true;
        int _backFrame = -1;

        void Awake() { if (Instance == null) Instance = this; }

        void OnDestroy()
        {
            if (_open) Close(false);
            if (Instance == this) Instance = null;
        }

        /// <summary>Opens the screen from anywhere (the class select's "See how to play") focused on a class.</summary>
        public static CharactersScreen ShowFor(ClassId cls, Action onClosed)
        {
            CharactersScreen s = Instance;
            if (s == null) s = new GameObject("Characters Screen").AddComponent<CharactersScreen>();
            s.OpenOn(cls, ShowcaseTab.Watch, onClosed);
            return s;
        }

        public void Open(Action onClosed) => OpenOn(ClassSelect.SelectedClass, ShowcaseTab.Watch, onClosed);

        public void OpenOn(ClassId cls, ShowcaseTab tab, Action onClosed)
        {
            if (_open) Close(false);   // opened again while open: release the last clip and stills first
            _onClosed = onClosed;
            Build();
            _nav = new ShowcaseNav { Class = Mathf.Clamp((int)cls, 0, 5), Tab = tab };
            _open = true;
            _canvas.enabled = true;
            LoadStills();
            _videoHost = new GameObject("Characters Clip");
            _videoHost.transform.SetParent(transform, false);
            _rt = new RenderTexture(CharacterShowcaseData.ClipW, CharacterShowcaseData.ClipH, 0, RenderTextureFormat.ARGB32) { name = "Characters Clip" };
            _videoImage.texture = _rt;
            OverlayGuard.Begin(gameObject, ConsumePause);
            MenuItem.SubmitBlockedUntil = Time.unscaledTime + 0.15f;
            ControlBindings.BindingsChanged += MarkBindings;
            _bindingsDirty = true;
            _shownClass = -1;
            _shownTab = (ShowcaseTab)(-1);
            Refresh();
        }

        void MarkBindings() => _bindingsDirty = true;

        public void Back() => Close(true);

        bool ConsumePause()
        {
            Press(ShowcaseInput.Back);
            return true;
        }

        void Close(bool raise)
        {
            if (!_open) return;
            _open = false;
            ClosedFrame = Time.frameCount;
            ControlBindings.BindingsChanged -= MarkBindings;
            StopClip();
            if (_videoImage != null) _videoImage.texture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_videoHost != null) { Destroy(_videoHost); _videoHost = null; }
            ReleaseStills();
            if (_canvas != null) _canvas.enabled = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            OverlayGuard.End();
            if (!raise) return;
            Action a = _onClosed;
            _onClosed = null;
            a?.Invoke();
        }

        // ---------------------------------------------------------------- input

        /// <summary>One press (keyboard, controller, a click on a tile or a tab, or a capture tool).</summary>
        public void Press(ShowcaseInput input)
        {
            if (!_open) return;
            // the pause key reaches us twice in one frame (the overlay guard and our own read): one Back per frame
            if (input == ShowcaseInput.Back) { if (_backFrame == Time.frameCount) return; _backFrame = Time.frameCount; }
            _nav = ShowcaseNav.Step(_nav, input, CharacterShowcaseData.All.Length);
            if (_nav.Closed) { Close(true); return; }
            if (input == ShowcaseInput.Left || input == ShowcaseInput.Right) _flipT = _nav.Slide * CharacterShowcaseData.SlideSeconds;
            Refresh();
        }

        /// <summary>Capture and test hook: show a class and a tab without a key press.</summary>
        public void Show(ClassId cls, ShowcaseTab tab, int slide = 0)
        {
            if (!_open) return;
            _nav.Class = Mathf.Clamp((int)cls, 0, 5);
            _nav.Tab = tab;
            _nav.Slide = Mathf.Clamp(slide, 0, CharacterShowcaseData.SlideCount - 1);
            Refresh();
        }

        static ShowcaseInput ReadInput()
        {
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) return ShowcaseInput.Back;
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) return ShowcaseInput.Up;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) return ShowcaseInput.Down;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) return ShowcaseInput.Left;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) return ShowcaseInput.Right;
                if (kb.qKey.wasPressedThisFrame) return ShowcaseInput.PrevTab;
                if (kb.eKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) return ShowcaseInput.NextTab;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) return ShowcaseInput.Select;
            }
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame) return ShowcaseInput.Back;
                if (pad.dpad.up.wasPressedThisFrame) return ShowcaseInput.Up;
                if (pad.dpad.down.wasPressedThisFrame) return ShowcaseInput.Down;
                if (pad.dpad.left.wasPressedThisFrame) return ShowcaseInput.Left;
                if (pad.dpad.right.wasPressedThisFrame) return ShowcaseInput.Right;
                if (pad.leftShoulder.wasPressedThisFrame) return ShowcaseInput.PrevTab;
                if (pad.rightShoulder.wasPressedThisFrame) return ShowcaseInput.NextTab;
                if (pad.buttonSouth.wasPressedThisFrame) return ShowcaseInput.Select;
                ShowcaseInput stick = StickEdge(pad.leftStick.ReadValue());
                if (stick != ShowcaseInput.None) return stick;
            }
            return ShowcaseInput.None;
        }

        static bool _stickHeld;

        static ShowcaseInput StickEdge(Vector2 v)
        {
            bool held = v.sqrMagnitude > 0.36f;
            if (!held) { _stickHeld = false; return ShowcaseInput.None; }
            if (_stickHeld) return ShowcaseInput.None;
            _stickHeld = true;
            if (Mathf.Abs(v.y) >= Mathf.Abs(v.x)) return v.y > 0f ? ShowcaseInput.Up : ShowcaseInput.Down;
            return v.x > 0f ? ShowcaseInput.Right : ShowcaseInput.Left;
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
                HudText.Set(_hint, d == ControlDevice.Gamepad
                    ? "D-PAD  CLASS     LB / RB  TAB     A  SELECT     B  BACK"
                    : "ARROWS OR W A S D  CLASS     Q / E  TAB     ENTER  SELECT     ESC  BACK");
            }
            if (_bindingsDirty && _nav.Tab == ShowcaseTab.Controls) FillControls();
            if (Time.unscaledTime >= MenuItem.SubmitBlockedUntil)
            {
                ShowcaseInput input = ReadInput();
                if (input != ShowcaseInput.None) Press(input);
                if (!_open) return;
            }
            TickWatch(Time.unscaledDeltaTime);
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

        // ---------------------------------------------------------------- view

        void Refresh()
        {
            int c = _nav.Class;
            ShowcaseClass k = CharacterShowcaseData.All[c];
            bool classChanged = c != _shownClass, tabChanged = _nav.Tab != _shownTab;
            if (classChanged)
            {
                _shownClass = c;
                HudText.Set(_summary, k.Summary);
                for (int i = 0; i < _tiles.Length; i++) _tiles[i].Body.SetRim(i == c ? CMTokens.ToolBrass.Color : CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.25f));
                _bindingsDirty = true;
                _shownSlide = -1;
                _shownFlipA = _shownFlipB = -1;
            }
            if (tabChanged || _nav.InPane != _shownInPane)
            {
                _shownTab = _nav.Tab;
                _shownInPane = _nav.InPane;
                for (int i = 0; i < _tabs.Length; i++)
                    _tabs[i].Body.SetRim(i == (int)_nav.Tab ? CMTokens.ToolBrass.Color : CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.25f));
                _watch.gameObject.SetActive(_nav.Tab == ShowcaseTab.Watch);
                _how.gameObject.SetActive(_nav.Tab == ShowcaseTab.HowItWorks);
                _controls.gameObject.SetActive(_nav.Tab == ShowcaseTab.Controls);
            }
            Selectable focus = _nav.InPane ? (Selectable)_tabs[(int)_nav.Tab] : _tiles[c];
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != focus.gameObject) { EventSystem.current.SetSelectedGameObject(null); focus.Select(); }
            if (_nav.Tab == ShowcaseTab.Watch)
            {
                if (classChanged || tabChanged) StartClip(k.Id);
                // b14 review: no clip and no still on disk = the portrait stands in; it is not "in a real match"
                HudText.Set(_caption, _media[c] ? _captions[c] : _plainCaptions[c]);
            }
            else StopClip();
            if (_nav.Tab == ShowcaseTab.HowItWorks && _nav.Slide != _shownSlide)
            {
                _shownSlide = _nav.Slide;
                ShowcaseSlide sl = k.Slides[_nav.Slide];
                HudText.Set(_slideTitle, sl.Title);
                HudText.Set(_slideLine, sl.Line);
                _still.texture = StillOrPortrait(c, _nav.Slide);
                for (int i = 0; i < _dots.Length; i++) _dots[i].SetFill(i == _nav.Slide ? CMTokens.ToolBrass.Color : CMTokens.MeterEmpty.Color);
            }
            if (_nav.Tab == ShowcaseTab.Controls && _bindingsDirty) FillControls();
        }

        void FillControls()
        {
            _bindingsDirty = false;
            ShowcaseControl[] rows = CharacterShowcaseData.ControlsFor(CharacterShowcaseData.All[_nav.Class].Id);
            for (int i = 0; i < _ctrlRow.Length; i++)
            {
                bool on = i < rows.Length;
                _ctrlRow[i].gameObject.SetActive(on);
                if (!on) continue;
                HudText.Set(_ctrlLabel[i], rows[i].Label.ToUpperInvariant() + "  ·  " + rows[i].What);
                HudText.Set(_ctrlKey[i], CharacterShowcaseData.KeyWords(rows[i].Action, ControlDevice.KeyboardMouse).ToUpperInvariant());
                HudText.Set(_ctrlPad[i], CharacterShowcaseData.KeyWords(rows[i].Action, ControlDevice.Gamepad).ToUpperInvariant());
            }
        }

        // ---------------------------------------------------------------- watch

        void StartClip(ClassId cls)
        {
            StopClip();
            _flipT = 0f;
            _vpWait = 0f;
            _vpFailed = false;
            _vpPrepared = false;
            string url = CharacterShowcaseData.ClipPath(cls);
            _clipThere = CharacterShowcaseData.FileThere(url);
            Flipbook = true;
            if (!_clipThere || _videoHost == null) { ShowFlip(); return; }
            _vp = _videoHost.AddComponent<VideoPlayer>();
            _vp.playOnAwake = false;
            _vp.source = VideoSource.Url;
            _vp.url = url;
            _vp.isLooping = true;
            _vp.skipOnDrop = true;
            _vp.waitForFirstFrame = true;
            _vp.renderMode = VideoRenderMode.RenderTexture;
            _vp.targetTexture = _rt;
            _vp.audioOutputMode = VideoAudioOutputMode.None;
            _vp.errorReceived += OnClipError;
            _vp.prepareCompleted += OnClipPrepared;
            _vp.Prepare();   // never blocks: the flipbook shows until the clip is ready
            ShowFlip();
        }

        void OnClipError(VideoPlayer vp, string message)
        {
            _vpFailed = true;
            Debug.Log("[CM.Characters] clip fell back to slides: " + message);
        }

        void OnClipPrepared(VideoPlayer vp)
        {
            _vpPrepared = true;
            if (!_vpFailed && _vpWait <= CharacterShowcaseData.PrepareTimeout) vp.Play();
        }

        void StopClip()
        {
            if (_vp == null) return;
            _vp.errorReceived -= OnClipError;
            _vp.prepareCompleted -= OnClipPrepared;
            _vp.Stop();
            _vp.targetTexture = null;
            Destroy(_vp);
            _vp = null;
        }

        void TickWatch(float dt)
        {
            if (_nav.Tab != ShowcaseTab.Watch) return;
            if (_vp != null && !_vpPrepared) _vpWait += dt;
            bool clip = _vp != null && CharacterShowcaseData.UseClip(_clipThere, _vpFailed, _vpWait, _vpPrepared) && _vpPrepared;
            if (_vp != null && !CharacterShowcaseData.UseClip(_clipThere, _vpFailed, _vpWait, _vpPrepared)) StopClip();
            if (Flipbook == clip)
            {
                Flipbook = !clip;
                _videoImage.gameObject.SetActive(clip);
                _flipA.gameObject.SetActive(!clip);
                _flipB.gameObject.SetActive(!clip);
            }
            if (clip) return;
            _flipT += dt;
            ShowFlip();
        }

        void ShowFlip()
        {
            CharacterShowcaseData.Flipbook(_flipT, out int a, out int b, out float blend);
            int c = _nav.Class;
            if (a != _shownFlipA) { _shownFlipA = a; _flipA.texture = StillOrPortrait(c, a); }
            if (b != _shownFlipB) { _shownFlipB = b; _flipB.texture = StillOrPortrait(c, b); }
            Color ca = _flipA.color, cb = _flipB.color;
            ca.a = 1f;
            cb.a = blend;
            _flipA.color = ca;
            _flipB.color = cb;
        }

        Texture StillOrPortrait(int c, int slide)
        {
            Texture2D t = _stills[c, slide];
            if (t != null) return t;
            return UiIcons.Get(UiIcons.Portrait[c, 0], 4);
        }

        /// <summary>b14 review: class c has a clip or a still on disk (else the Watch tab shows the portrait).</summary>
        readonly bool[] _media = new bool[6];

        void LoadStills()
        {
            for (int c = 0; c < 6; c++)
                _media[c] = CharacterShowcaseData.FileThere(CharacterShowcaseData.ClipPath(CharacterShowcaseData.All[c].Id));
            for (int c = 0; c < 6; c++)
                for (int n = 0; n < CharacterShowcaseData.SlideCount; n++)
                {
                    string p = CharacterShowcaseData.StillPath(CharacterShowcaseData.All[c].Id, n + 1);
                    if (!CharacterShowcaseData.FileThere(p)) continue;
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Characters Still " + c + "-" + n };
                    if (!tex.LoadImage(File.ReadAllBytes(p), false)) { Destroy(tex); continue; }
                    tex.Compress(false);
                    tex.Apply(false, true);   // GPU copy only: about 0.9 MB a still
                    _stills[c, n] = tex;
                    _media[c] = true;
                }
        }

        void ReleaseStills()
        {
            for (int c = 0; c < 6; c++)
                for (int n = 0; n < CharacterShowcaseData.SlideCount; n++)
                    if (_stills[c, n] != null) { Destroy(_stills[c, n]); _stills[c, n] = null; }
            if (_still != null) _still.texture = null;
            if (_flipA != null) _flipA.texture = null;
            if (_flipB != null) _flipB.texture = null;
            _shownFlipA = _shownFlipB = _shownSlide = -1;
        }

        // ---------------------------------------------------------------- build

        static void NoNav(Selectable s) => s.navigation = new Navigation { mode = Navigation.Mode.None };

        static RawImage Image(Transform parent, string name, Rect r)
        {
            RawImage ri = MenuUi.Rt(parent, name).gameObject.AddComponent<RawImage>();
            ri.raycastTarget = false;
            MenuUi.Place(ri.rectTransform, r.x, r.y, r.width, r.height);
            return ri;
        }

        void Build()
        {
            if (_built) return;
            _built = true;
            PauseMenu.EnsureEventSystem(transform);
            var go = new GameObject("Characters Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = SortingOrder;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = cam != null ? Mathf.Max(0.38f, cam.nearClipPlane * 1.25f) : 0.38f;
            go.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)go.transform;
            Image wash = MenuUi.Rt(root, "Wash").gameObject.AddComponent<Image>();
            MenuUi.Stretch(wash.rectTransform);
            wash.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.94f);
            wash.raycastTarget = true;
            RectTransform space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color;
            Rect t = CharacterShowcaseData.Title;
            MenuUi.Label(space, "Title", CMTokens.Display, UiScale.TypeTitle, cream, TextAnchor.MiddleCenter, t.x, t.y, t.width, t.height, "CHARACTERS", 4f);

            // tiles
            for (int i = 0; i < _tiles.Length; i++)
            {
                int cls = i;
                Rect r = CharacterShowcaseData.Tile(i);
                ShowcaseClass k = CharacterShowcaseData.All[i];
                MenuItem it = MenuUi.Item(space, "Tile " + k.FileId, r.x, r.y, r.width, r.height, "", MenuItem.Kind.Button, UiScale.TypeButton);
                it.Label.gameObject.SetActive(false);
                it.Submitted = () => { _nav.Class = cls; _nav.Slide = 0; Press(ShowcaseInput.Select); };
                NoNav(it);
                _portraits[i] = HudIcon.Make(it.transform, "Portrait", 12f, 11f, 96f, 96f);
                _portraits[i].Set(UiIcons.Portrait[i, 0], 2);
                MenuUi.Fit(MenuUi.Label(it.transform, "Name", CMTokens.Display, UiScale.TypeButton, cream, TextAnchor.MiddleLeft, 122f, 12f, r.width - 140f, 46f, k.Name, 2f), 16);
                MenuUi.Fit(MenuUi.Label(it.transform, "Role", CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleLeft, 122f, 60f, r.width - 140f, 42f, k.Role, 2f), 14);
                _tiles[i] = it;
            }

            // tabs and the pane
            for (int i = 0; i < _tabs.Length; i++)
            {
                int tab = i;
                Rect r = CharacterShowcaseData.Tab(i);
                MenuItem it = MenuUi.Item(space, "Tab " + TabNames[i], r.x, r.y, r.width, r.height, TabNames[i], MenuItem.Kind.Button, UiScale.TypeLead, true, CMTokens.Display);
                it.Submitted = () => { _nav.Tab = (ShowcaseTab)tab; _nav.Slide = 0; _nav.InPane = true; Refresh(); };
                NoNav(it);
                _tabs[i] = it;
            }
            Rect p = CharacterShowcaseData.Pane;
            MenuUi.Panel(space, "Pane", p.x, p.y, p.width, p.height);
            Rect su = CharacterShowcaseData.Summary;
            _summary = MenuUi.Contain(MenuUi.Label(space, "Summary", CMTokens.Ui, UiScale.TypeLead, cream, TextAnchor.MiddleCenter, su.x, su.y, su.width, su.height, "", 2f), 2);

            _watch = MenuUi.Rt(space, "Watch");
            MenuUi.Stretch(_watch);
            Rect v = CharacterShowcaseData.Video;
            _flipA = Image(_watch, "Flip A", v);
            _flipB = Image(_watch, "Flip B", v);
            _videoImage = Image(_watch, "Clip", v);
            _videoImage.gameObject.SetActive(false);
            Rect vc = CharacterShowcaseData.VideoCaption;
            _caption = MenuUi.Fit(MenuUi.Label(_watch, "Caption", CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleCenter, vc.x, vc.y, vc.width, vc.height, "", 2f), 14);
            _captions = new string[CharacterShowcaseData.All.Length];
            for (int i = 0; i < _captions.Length; i++) _captions[i] = "THE " + CharacterShowcaseData.All[i].Name + " IN A REAL MATCH";
            _plainCaptions = new string[_captions.Length];
            for (int i = 0; i < _plainCaptions.Length; i++) _plainCaptions[i] = "THE " + CharacterShowcaseData.All[i].Name;

            _how = MenuUi.Rt(space, "How It Works");
            MenuUi.Stretch(_how);
            _still = Image(_how, "Still", CharacterShowcaseData.Still);
            Rect st = CharacterShowcaseData.SlideTitle, sl = CharacterShowcaseData.SlideLine;
            _slideTitle = MenuUi.Fit(MenuUi.Label(_how, "Slide Title", CMTokens.Display, UiScale.TypeHeading, brass, TextAnchor.MiddleCenter, st.x, st.y, st.width, st.height, "", 2f), 18);
            _slideLine = MenuUi.Contain(MenuUi.Label(_how, "Slide Line", CMTokens.Ui, UiScale.TypeLead, cream, TextAnchor.MiddleCenter, sl.x, sl.y, sl.width, sl.height, "", 2f), 2);
            float dotsW = CharacterShowcaseData.SlideCount * CharacterShowcaseData.DotSize + (CharacterShowcaseData.SlideCount - 1) * CharacterShowcaseData.DotGap;
            float dx0 = p.x + (p.width - dotsW) * 0.5f;
            for (int i = 0; i < _dots.Length; i++)
            {
                _dots[i] = MenuUi.Shape(_how, "Dot " + i, ShapeKind.Circle, dx0 + i * (CharacterShowcaseData.DotSize + CharacterShowcaseData.DotGap), CharacterShowcaseData.DotY,
                    CharacterShowcaseData.DotSize, CharacterShowcaseData.DotSize, CMTokens.MeterEmpty.Color, 9f);
                _dots[i].RimPx = 0f;
            }
            Rect pa = CharacterShowcaseData.PrevArrow, na = CharacterShowcaseData.NextArrow;
            _prev = MenuUi.Item(_how, "Prev", pa.x, pa.y, pa.width, pa.height, "<", MenuItem.Kind.Button, UiScale.TypeHeading, true, CMTokens.Display);
            _prev.Submitted = () => Press(ShowcaseInput.Left);
            NoNav(_prev);
            _next = MenuUi.Item(_how, "Next", na.x, na.y, na.width, na.height, ">", MenuItem.Kind.Button, UiScale.TypeHeading, true, CMTokens.Display);
            _next.Submitted = () => Press(ShowcaseInput.Right);
            NoNav(_next);

            _controls = MenuUi.Rt(space, "Controls");
            MenuUi.Stretch(_controls);
            float hy = CharacterShowcaseData.CtrlY;
            MenuUi.Fit(MenuUi.Label(_controls, "Head Action", CMTokens.Display, UiScale.TypeSmall, brass, TextAnchor.MiddleLeft, CharacterShowcaseData.CtrlLabelX, hy, CharacterShowcaseData.CtrlLabelW, 40f, "ACTION", 2f), 12);
            _ctrlHeadKey = MenuUi.Fit(MenuUi.Label(_controls, "Head Keys", CMTokens.Display, UiScale.TypeSmall, brass, TextAnchor.MiddleCenter, CharacterShowcaseData.CtrlKeyX, hy, CharacterShowcaseData.CtrlKeyW, 40f, "KEYBOARD AND MOUSE", 2f), 12);
            _ctrlHeadPad = MenuUi.Fit(MenuUi.Label(_controls, "Head Pad", CMTokens.Display, UiScale.TypeSmall, brass, TextAnchor.MiddleCenter, CharacterShowcaseData.CtrlPadX, hy, CharacterShowcaseData.CtrlKeyW, 40f, "CONTROLLER", 2f), 12);
            for (int i = 0; i < _ctrlRow.Length; i++)
            {
                Rect r = CharacterShowcaseData.CtrlRow(i);
                RectTransform row = MenuUi.Rt(_controls, "Row " + i);
                MenuUi.Place(row, r.x, r.y, r.width, r.height);
                MenuUi.Shape(row, "Band", ShapeKind.RoundRect, 0f, 0f, r.width, r.height, CMTokens.WithAlpha(CMTokens.PanelNavy.Color, i % 2 == 0 ? 0.9f : 0.6f), 10f).RimPx = 0f;
                _ctrlLabel[i] = MenuUi.Fit(MenuUi.Label(row, "Label", CMTokens.Ui, UiScale.TypeBody, cream, TextAnchor.MiddleLeft, 14f, 0f, CharacterShowcaseData.CtrlKeyX - r.x - 24f, r.height, "", 2f), 12);
                VectorShape kc = MenuUi.Shape(row, "Key Chip", ShapeKind.RoundRect, CharacterShowcaseData.CtrlKeyX - r.x, 6f, CharacterShowcaseData.CtrlKeyW, r.height - 12f, CMTokens.VoidNavy.Color, 8f);
                kc.RimColor = cream;
                kc.RimPx = 2f;
                _ctrlKey[i] = MenuUi.Fit(MenuUi.Label(kc.transform, "Key", CMTokens.Mono, UiScale.TypeSmall, cream, TextAnchor.MiddleCenter, 6f, 0f, CharacterShowcaseData.CtrlKeyW - 12f, r.height - 12f, "", 0f), 10);
                VectorShape pc = MenuUi.Shape(row, "Pad Chip", ShapeKind.RoundRect, CharacterShowcaseData.CtrlPadX - r.x, 6f, CharacterShowcaseData.CtrlKeyW, r.height - 12f, CMTokens.VoidNavy.Color, 8f);
                pc.RimColor = cream;
                pc.RimPx = 2f;
                _ctrlPad[i] = MenuUi.Fit(MenuUi.Label(pc.transform, "Key", CMTokens.Mono, UiScale.TypeSmall, cream, TextAnchor.MiddleCenter, 6f, 0f, CharacterShowcaseData.CtrlKeyW - 12f, r.height - 12f, "", 0f), 10);
                _ctrlRow[i] = row;
            }

            Rect b = CharacterShowcaseData.Back, h = CharacterShowcaseData.Hint;
            _back = MenuUi.Item(space, "Back", b.x, b.y, b.width, b.height, "BACK", MenuItem.Kind.Button, UiScale.TypeLead, true, CMTokens.Display);
            _back.Submitted = () => Close(true);
            NoNav(_back);
            _hint = MenuUi.Fit(MenuUi.Label(space, "Hint", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleCenter, h.x, h.y, h.width, h.height, "", 2f), 11);
            _canvas.enabled = false;
        }
    }
}

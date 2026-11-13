using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Spectator
{
    /// <summary>
    /// Who is playing, made obvious (owner, 2026-11-12: "the game's just running itself right now").
    /// <list type="bullet">
    /// <item>On spawn, after every respawn and when control comes back from spectating: a bobbing "YOU" tag over actor 0 for
    /// MarkerSeconds and, for IntroSeconds, "YOU ARE THE AZURE WARRIOR" with "MOVE WITH [W][A][S][D] OR [arrows]" (or the
    /// left stick) drawn as glyphs from the current bindings.</item>
    /// <item>Autopilot: whenever actor 0 is bot-driven in a real match (SimDriverBehaviour.HumanAutopilot outside spectator
    /// mode, capture, the menus' backdrop and the -autoplay tool runs), the banner "AUTOPILOT: PRESS ANY KEY TO TAKE
    /// CONTROL" shows and the first key, click, mouse move, controller button or stick push hands actor 0 back to the
    /// human input at once. After IdleAutopilotSeconds with no input from a standing player, autopilot takes over (same
    /// banner, same way back), so an idle player is never mistaken for one who is playing.</item>
    /// <item>Short notices: "YOU'LL RESPAWN AS THE MINER" after a live class change.</item>
    /// </list>
    /// View only: reads the world, writes only the driver's public HumanAutopilot field.
    /// </summary>
    [GameOverlay(Order = 71, Scope = OverlayScope.MatchOnly)]
    [DefaultExecutionOrder(950)]
    public sealed class PlayerPresence : MonoBehaviour
    {
        public const float MarkerSeconds = 4f;
        public const float IntroSeconds = 2f;
        public const float NoticeSeconds = 3f;
        public const float FadeSeconds = 0.2f;
        /// <summary>Seconds without any input before autopilot takes a standing player's fighter (tests shorten it).</summary>
        public static float IdleAutopilotSeconds = DefaultIdleAutopilotSeconds;
        public const float DefaultIdleAutopilotSeconds = 10f;
        public const int SortingOrder = 13;
        /// <summary>Height of the YOU tag's point above the ground under actor 0, world units.</summary>
        public const float MarkerHeight = 2.6f;

        /// <summary>false: idle never hands actor 0 to autopilot (the banner still shows for any other autopilot).</summary>
        public static bool IdleAutopilotEnabled = true;

        public SimDriverBehaviour Driver { get; private set; }
        public HumanInputSource Input { get; private set; }

        /// <summary>The banner is on screen.</summary>
        public bool BannerVisible => _banner != null && _banner.activeSelf;
        /// <summary>The YOU tag is on screen.</summary>
        public bool MarkerVisible => _marker != null && _marker.gameObject.activeSelf;
        /// <summary>The "you are the ..." line is on screen.</summary>
        public bool IntroVisible => _intro != null && _intro.activeSelf;
        public string IntroText => _introTitle != null ? _introTitle.text : string.Empty;
        public string IntroPrompt => _introStrip != null ? _introStrip.PlainText : string.Empty;
        /// <summary>Autopilot was turned on by this component after the idle time.</summary>
        public bool IdleAutopilot => _idleAutopilot;
        /// <summary>Unscaled time of the last input seen.</summary>
        public float LastInputAt => _lastInput;

        Canvas _canvas;
        RectTransform _marker;
        CanvasGroup _markerGroup;
        GameObject _intro, _banner, _notice;
        CanvasGroup _introGroup;
        Text _introTitle, _noticeText;
        PromptStrip _introStrip, _bannerStrip;
        float _markerUntil = -1f, _introUntil = -1f, _noticeUntil = -1f;
        float _lastInput;
        bool _idleAutopilot;
        bool _seenAlive, _wasAlive;
        int _epoch = int.MinValue;
        bool _pendingIntro;
        ControlDevice _introDevice;
        bool _bannerBuilt;
        ControlDevice _bannerDevice;
        readonly GlyphSpec[] _move = new GlyphSpec[4];
        readonly GlyphSpec[] _arrows = new GlyphSpec[4];
        bool _arrowsReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IdleAutopilotSeconds = DefaultIdleAutopilotSeconds;
            IdleAutopilotEnabled = true;
        }

        void Awake()
        {
            Driver = GetComponent<SimDriverBehaviour>();
            Input = GetComponent<HumanInputSource>();
            _lastInput = Time.unscaledTime;
        }

        void OnEnable()
        {
            SpectatorMode.ControlReturned += OnControlReturned;
            ClassSwitch.Requested += OnClassRequested;
            ControlBindings.BindingsChanged += OnBindings;
        }

        void OnDisable()
        {
            SpectatorMode.ControlReturned -= OnControlReturned;
            ClassSwitch.Requested -= OnClassRequested;
            ControlBindings.BindingsChanged -= OnBindings;
        }

        void OnControlReturned()
        {
            _lastInput = Time.unscaledTime;
            ShowSelf();
        }

        void OnBindings()
        {
            _bannerBuilt = false;
            if (IntroVisible) BuildIntroPrompt(ControlBindings.ActiveDevice);
        }

        void OnClassRequested(ClassId c)
        {
            Build();
            HudText.Set(_noticeText, "YOU'LL RESPAWN AS THE " + KillWords.ClassName(c).ToUpperInvariant());
            _noticeUntil = Time.unscaledTime + NoticeSeconds;
        }

        /// <summary>Shows the YOU tag and the "you are the ..." line now (spawn, respawn, rejoin).</summary>
        public void ShowSelf()
        {
            _pendingIntro = true;
        }

        // ---------------------------------------------------------------- frame

        void Update()
        {
            if (Driver == null) return;
            World w = Driver.World;
            if (w == null || w.ActorCount <= 0) return;
            bool title = GetComponent<TitleScreen>() != null;
            bool tool = SimDriverBehaviour.CaptureMode || GameFlow.Autoplay || AutoplayArgs.Current.Autoplay ||
                        (Driver.Request != null && Driver.Request.Backdrop);
            bool spectating = SpectatorMode.IsSpectating;
            bool paused = Input != null && Input.State.Paused;
            bool playing = w.Match.Phase == MatchPhase.Playing;
            bool trial = Onboarding.Trial.IsRunning;
            bool alive = w.Actors[0].Alive;

            // Spawn and respawn: a new world, or actor 0 back on its feet.
            if (w.Epoch != _epoch)
            {
                _epoch = w.Epoch;
                _seenAlive = false;
                _wasAlive = false;
            }
            if (alive && (!_seenAlive || !_wasAlive)) _pendingIntro = true;
            _seenAlive |= alive;
            _wasAlive = alive;

            bool input = AnyInput();
            float now = Time.unscaledTime;
            // The idle clock does not run behind the loading screen: Play Now hands the human control on the first frame.
            if (input || !alive || paused || title || spectating || Menus.LoadingScreen.IsOpen) _lastInput = now;

            bool eligible = !tool && !spectating && !title && playing;
            if (eligible && Driver.HumanAutopilot && input)
            {
                // Any input takes control at once.
                Driver.HumanAutopilot = false;
                _idleAutopilot = false;
                _pendingIntro = true;
            }
            else if (eligible && !Driver.HumanAutopilot && IdleAutopilotEnabled && !trial && alive && !paused &&
                     now - _lastInput >= IdleAutopilotSeconds)
            {
                Driver.HumanAutopilot = true;
                _idleAutopilot = true;
            }
            if (!Driver.HumanAutopilot) _idleAutopilot = false;

            bool banner = eligible && Driver.HumanAutopilot && !paused;
            bool show = !tool && !spectating && !title;
            if (_pendingIntro && show && alive && !paused)
            {
                _pendingIntro = false;
                _markerUntil = now + MarkerSeconds;
                if (!Driver.HumanAutopilot) _introUntil = now + IntroSeconds;
            }
            bool marker = show && alive && now < _markerUntil + FadeSeconds;
            bool intro = show && alive && !paused && now < _introUntil + FadeSeconds;
            bool notice = show && now < _noticeUntil;
            if (!banner && !marker && !intro && !notice && _canvas == null) return;
            Build();
            FeedUi.Refresh(_canvas);

            if (_banner.activeSelf != banner) _banner.SetActive(banner);
            if (banner) UpdateBanner();
            if (_notice.activeSelf != notice) _notice.SetActive(notice);

            if (_intro.activeSelf != intro)
            {
                _intro.SetActive(intro);
                if (intro) BuildIntro(w);
            }
            if (intro)
            {
                ControlDevice d = ControlBindings.ActiveDevice;
                if (d != _introDevice) BuildIntroPrompt(d);
                _introGroup.alpha = Fade(now, _introUntil);
            }

            if (_marker.gameObject.activeSelf != marker) _marker.gameObject.SetActive(marker);
            if (marker) PlaceMarker(w, now);
        }

        static float Fade(float now, float until)
        {
            float left = until + FadeSeconds - now;
            return Mathf.Clamp01(left / FadeSeconds);
        }

        /// <summary>Any key, mouse button, mouse move, scroll, controller button or stick push this frame (held keys count).</summary>
        public static bool AnyInput()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed) return true;
            Mouse m = Mouse.current;
            if (m != null && (ControlBindings.MouseUsed(m) || m.leftButton.isPressed || m.rightButton.isPressed ||
                              m.middleButton.isPressed || Mathf.Abs(m.scroll.ReadValue().y) > 0.01f)) return true;
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                if (ControlBindings.GamepadUsed(pad)) return true;
                float t = ControlBindings.StickUseThreshold;
                if (pad.leftStick.ReadValue().sqrMagnitude > t * t || pad.rightStick.ReadValue().sqrMagnitude > t * t) return true;
                if (pad.leftTrigger.ReadValue() > 0.5f || pad.rightTrigger.ReadValue() > 0.5f) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- pieces

        void Build()
        {
            if (_canvas != null) return;
            _canvas = FeedUi.MakeCanvas(transform, "Player Presence Canvas", SortingOrder);
            Color navy = CMTokens.VoidNavy.Color, cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color;

            // YOU tag: a brass pill with a pointer, over actor 0.
            _marker = FeedUi.Anchored(_canvas.transform, "You Marker", new Vector2(0.5f, 0.5f), 0f, 0f, 96f, 70f);
            _marker.pivot = new Vector2(0.5f, 0f);
            _markerGroup = _marker.gameObject.AddComponent<CanvasGroup>();
            _markerGroup.blocksRaycasts = false;
            VectorShape pill = MenuUi.Shape(_marker, "Pill", ShapeKind.RoundRect, 4f, 0f, 88f, 44f, brass, -1f);
            pill.OutlinePx = 4f;
            pill.RimPx = 0f;
            MenuUi.Label(_marker, "You", CMTokens.Display, 26, navy, TextAnchor.MiddleCenter, 4f, 0f, 88f, 44f, "YOU", 0f);
            VectorShape point = MenuUi.Shape(_marker, "Point", ShapeKind.TriangleDown, 34f, 42f, 28f, 22f, brass, 0f);
            point.OutlinePx = 4f;
            point.RimPx = 0f;
            _marker.gameObject.SetActive(false);

            // "You are the ..." line, low centre.
            RectTransform intro = FeedUi.Anchored(_canvas.transform, "Intro", new Vector2(0.5f, 0f), 0f, 250f, 900f, 120f);
            _intro = intro.gameObject;
            _introGroup = _intro.AddComponent<CanvasGroup>();
            _introGroup.blocksRaycasts = false;
            VectorShape ip = MenuUi.Shape(intro, "Plate", ShapeKind.RoundRect, 0f, 0f, 900f, 120f, CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.94f), 18f);
            ip.OutlinePx = 4f;
            ip.RimPx = 2f;
            _introTitle = MenuUi.Label(intro, "Title", CMTokens.Display, 34, cream, TextAnchor.MiddleCenter, 0f, 10f, 900f, 50f, "", 3f);
            _introStrip = PromptStrip.Make(intro, "Prompt", 34f, 22, cream);
            RectTransform sr = _introStrip.Rect;
            sr.anchorMin = sr.anchorMax = new Vector2(0f, 0f);
            sr.pivot = Vector2.zero;
            sr.sizeDelta = new Vector2(900f, 50f);
            sr.anchoredPosition = new Vector2(0f, 10f);
            _intro.SetActive(false);

            // Autopilot banner, under the match panel.
            RectTransform banner = FeedUi.Anchored(_canvas.transform, "Autopilot Banner", new Vector2(0.5f, 1f), 0f, -150f, 760f, 64f);
            _banner = banner.gameObject;
            VectorShape bp = MenuUi.Shape(banner, "Plate", ShapeKind.RoundRect, 0f, 0f, 760f, 64f, navy, 16f);
            bp.OutlinePx = 4f;
            bp.RimPx = 3f;
            bp.RimColor = brass;
            VectorShape tag = MenuUi.Shape(banner, "Tag", ShapeKind.RoundRect, 14f, 12f, 190f, 40f, brass, -1f);
            tag.RimPx = 0f;
            MenuUi.Label(banner, "Autopilot", CMTokens.Display, 22, navy, TextAnchor.MiddleCenter, 14f, 12f, 190f, 40f, "AUTOPILOT", 0f);
            _bannerStrip = PromptStrip.Make(banner, "Prompt", 32f, 22, cream);
            RectTransform br = _bannerStrip.Rect;
            br.anchorMin = br.anchorMax = new Vector2(0f, 0.5f);
            br.pivot = new Vector2(0f, 0.5f);
            br.sizeDelta = new Vector2(530f, 64f);
            br.anchoredPosition = new Vector2(218f, 0f);
            _bannerStrip.Align = TextAnchor.MiddleLeft;
            _banner.SetActive(false);

            // Notice line.
            RectTransform notice = FeedUi.Anchored(_canvas.transform, "Notice", new Vector2(0.5f, 1f), 0f, -226f, 760f, 44f);
            _notice = notice.gameObject;
            VectorShape np = MenuUi.Shape(notice, "Plate", ShapeKind.RoundRect, 0f, 0f, 760f, 44f, CMTokens.PanelNavy.Color, -1f);
            np.OutlinePx = 3f;
            _noticeText = MenuUi.Label(notice, "Text", CMTokens.Ui, 22, cream, TextAnchor.MiddleCenter, 0f, 0f, 760f, 44f, "", 2f);
            _notice.SetActive(false);
        }

        void UpdateBanner()
        {
            ControlDevice d = ControlBindings.ActiveDevice;
            if (_bannerBuilt && d == _bannerDevice) return;
            _bannerBuilt = true;
            _bannerDevice = d;
            _bannerStrip.Begin();
            _bannerStrip.AddText(d == ControlDevice.Gamepad ? "PRESS ANY BUTTON TO TAKE CONTROL" : "PRESS ANY KEY TO TAKE CONTROL");
            _bannerStrip.End();
        }

        void BuildIntro(World w)
        {
            Actor a = w.Actors[0];
            HudText.Set(_introTitle, "YOU ARE THE " + SpectatorOverlay.ActorName(a.Team, a.Cls));
            _introTitle.color = CMTokens.Team(a.Team);
            BuildIntroPrompt(ControlBindings.ActiveDevice);
        }

        void BuildIntroPrompt(ControlDevice d)
        {
            if (_introStrip == null) return;
            _introDevice = d;
            _introStrip.Begin();
            _introStrip.AddText("MOVE WITH");
            int n = InputGlyphs.MoveGlyphs(d, _move);
            for (int i = 0; i < n; i++) _introStrip.AddGlyph(_move[i]);
            if (d == ControlDevice.KeyboardMouse)
            {
                if (!_arrowsReady)
                {
                    _arrowsReady = true;
                    _arrows[0] = InputGlyphs.ForPath("<Keyboard>/upArrow");
                    _arrows[1] = InputGlyphs.ForPath("<Keyboard>/leftArrow");
                    _arrows[2] = InputGlyphs.ForPath("<Keyboard>/downArrow");
                    _arrows[3] = InputGlyphs.ForPath("<Keyboard>/rightArrow");
                }
                _introStrip.AddText("OR");
                for (int i = 0; i < 4; i++) _introStrip.AddGlyph(_arrows[i]);
            }
            _introStrip.End();
        }

        void PlaceMarker(World w, float now)
        {
            UnityEngine.Camera cam = _canvas.worldCamera != null ? _canvas.worldCamera : UnityEngine.Camera.main;
            if (cam == null) return;
            double a = Driver.Alpha;
            double x = w.PrevX[0] + (w.X[0] - w.PrevX[0]) * a;
            double z = w.PrevZ[0] + (w.Z[0] - w.PrevZ[0]) * a;
            double y = GroundHeight.At(w.Map, x, z) + MarkerHeight;
            Vector3 sp = cam.WorldToScreenPoint(SimSpace.ToWorld(x, y, z));
            if (sp.z <= 0f)
            {
                _markerGroup.alpha = 0f;
                return;
            }
            var root = (RectTransform)_canvas.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam,
                    out Vector2 local)) return;
            float bob = Mathf.Sin(now * 6f) * 4f;
            _marker.anchoredPosition = local + new Vector2(0f, 10f + bob);
            _markerGroup.alpha = Fade(now, _markerUntil);
        }
    }
}

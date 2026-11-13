using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// Simple title screen: the bible's own primary logo (Visual Asset Bible p04, cut from its cream page by
    /// Art/UI/Logo/_source~/cut_bible_logo.py; 766 x 597 native, drawn 1:1 at 1080p from @1x, from @2x up to 4K, @4x above)
    /// with a slow bob and a pulsing glow on the egg's seams, over the live bot match under a Void Navy wash, and
    /// two stickers under it: PLAY NOW (first and focused; Enter, Space, controller A or a click drops straight into a match
    /// with the last class and settings, Flow.PlayNow) and MAIN MENU (Escape, B, Start, or the sticker). Left / right
    /// moves the focus. The redrawn vector logo (critical-mass-logo@Nx) stays in the repo, unused. Hide() is the main-menu
    /// path (tools and tests call it); with the flow off both paths just show the HUD. Shown on boot in player builds
    /// only (ShowOnBoot), never in the editor, batch mode or tests; the capture tool shows it explicitly.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        public static bool ShowOnBoot => !Application.isEditor && !Application.isBatchMode;

        public GameHud Hud;
        Canvas _canvas;
        RawImage _logo, _glow;
        RectTransform _eggHit;
        Text _prompt;
        /// <summary>Logo size in canvas units (1080p px): the @1x export's pixel size, so 1080p draws it 1:1.</summary>
        public static readonly Vector2 LogoSize = new Vector2(766f, 597f);
        public const float LogoY = 70f, BobPx = 6f, BobPeriod = 4f, GlowPeriod = 2.6f;
        /// <summary>The two stickers, in 1920 x 1080 space from the top-left: y, width, height, gap.</summary>
        public const float ButtonsY = 838f, ButtonW = 420f, ButtonH = 84f, ButtonGap = 24f;
        public RawImage Logo => _logo;
        /// <summary>PLAY NOW, the first and focused sticker.</summary>
        public MenuItem PlayNowItem => _playNow;
        public MenuItem MainMenuItem => _toMenu;
        MenuItem _playNow, _toMenu;
        float _t;

        public static TitleScreen Show(GameObject host, GameHud hud)
        {
            var t = host.AddComponent<TitleScreen>();
            t.Hud = hud;
            t.Build();
            return t;
        }

        void Build()
        {
            var go = new GameObject("Title Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = 20;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = cam != null ? Mathf.Max(0.45f, cam.nearClipPlane * 1.4f) : 0.45f;
            var root = (RectTransform)go.transform;

            var wash = new GameObject("Wash", typeof(RectTransform)).AddComponent<ScreenVignette>();
            wash.gameObject.layer = 5;
            wash.transform.SetParent(root, false);
            Stretch(wash.rectTransform);
            wash.Wash = 0.6f;
            wash.EdgeAlpha = 0.8f;
            wash.raycastTarget = false;

            _logo = new GameObject("Logo", typeof(RectTransform)).AddComponent<RawImage>();
            _logo.gameObject.layer = 5;
            _logo.transform.SetParent(root, false);
            _logo.raycastTarget = false;
            RectTransform lr = _logo.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
            lr.sizeDelta = LogoSize;
            lr.anchoredPosition = new Vector2(0f, LogoY);
            _glow = new GameObject("Egg Glow", typeof(RectTransform)).AddComponent<RawImage>();
            _glow.gameObject.layer = 5;
            _glow.transform.SetParent(lr, false);
            _glow.raycastTarget = false;
            Stretch(_glow.rectTransform);
            // Integration batch 3: egg-01, the title egg. A clear hit area over the egg in the middle of the logo; clicks on it
            // count toward the secret instead of dismissing the title (keys and the pad still dismiss).
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            var eggHit = new GameObject("Egg Hit", typeof(RectTransform));
            eggHit.layer = 5;
            RectTransform er = (RectTransform)eggHit.transform;
            er.SetParent(lr, false);
            er.anchorMin = er.anchorMax = new Vector2(0.5f, 0.55f);
            er.sizeDelta = new Vector2(230f, 250f);
            er.anchoredPosition = Vector2.zero;
            _eggHit = er;
            _eggHitStatic = er;
            _eggCam = cam;
            CriticalMass.Runtime.Secrets.TitleEgg.Attach(er);

            // PLAY NOW (focused, brass rim like START MATCH) and MAIN MENU, side by side under the tagline.
            var space = new GameObject("Space", typeof(RectTransform));
            space.layer = 5;
            var sp = (RectTransform)space.transform;
            sp.SetParent(root, false);
            sp.anchorMin = sp.anchorMax = new Vector2(0.5f, 0.5f);
            sp.pivot = new Vector2(0.5f, 0.5f);
            sp.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            float bx = 960f - ButtonW - ButtonGap * 0.5f;
            _playNow = MenuUi.Item(sp, "Play Now", bx, ButtonsY, ButtonW, ButtonH, Flow.PlayNow.Label, MenuItem.Kind.Button, 38, true,
                CMTokens.Display);
            _playNow.Body.Radius = 18f;
            _playNow.Body.RimPx = 4f;
            _playNow.Body.SetRim(CMTokens.ToolBrass.Color);
            _playNow.Submitted = PressPlayNow;
            _toMenu = MenuUi.Item(sp, "Main Menu", 960f + ButtonGap * 0.5f, ButtonsY, ButtonW, ButtonH, "MAIN MENU", MenuItem.Kind.Button, 30,
                true, CMTokens.Display);
            _toMenu.Body.Radius = 18f;
            _toMenu.Body.RimPx = 3f;
            _toMenu.Submitted = Hide;
            _playNow.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = _toMenu, selectOnLeft = _toMenu };
            _toMenu.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _playNow, selectOnRight = _playNow };
            PauseMenu.EnsureEventSystem(transform);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_playNow.gameObject);

            _prompt = HudText.Make(root, "Prompt", CMTokens.Ui, 22, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 2f);
            RectTransform pr = _prompt.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(1100f, 40f);
            pr.anchoredPosition = new Vector2(0f, -432f);
            _prompt.text = "ENTER, SPACE OR A   PLAY NOW      ·      ESC OR B   MAIN MENU";
            Text sub = HudText.Make(root, "Tagline", CMTokens.Ui, 22, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 2f);
            RectTransform sr = sub.rectTransform;
            sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.5f);
            sr.sizeDelta = new Vector2(900f, 40f);
            sr.anchoredPosition = new Vector2(0f, -258f);
            sub.text = "16 v 16. Hold the hill. Turn the Kaiju.";
            if (Hud != null && Hud.Canvas != null) Hud.Canvas.enabled = false;
            UpdateScale();
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
        }

        /// <summary>Canvas scale and logo resolution for the camera's current pixel size.</summary>
        public void UpdateScale()
        {
            UnityEngine.Camera cam = _canvas.worldCamera;
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            _canvas.scaleFactor = HudLayout.Scale(w, h);
            string tier = _canvas.scaleFactor <= 1.01f ? "1x" : _canvas.scaleFactor <= 2.01f ? "2x" : "4x";
            string want = "critical-mass-bible-logo@" + tier;
            if (_logo.texture == null || _logo.texture.name != want)
            {
                _logo.texture = Resources.Load<Texture2D>("UI/Logo/" + want);
                _glow.texture = Resources.Load<Texture2D>("UI/Logo/critical-mass-bible-logo-glow@" + tier);
            }
        }

        void Update()
        {
            UpdateScale();
            _t += Time.unscaledDeltaTime;
            Color c = _prompt.color;
            c.a = 0.75f + 0.25f * Mathf.Cos(_t * Mathf.PI);   // slow 0.5 Hz breathe, no flashing
            _prompt.color = c;
            Animate(_t);
            if (_hidden) return;
            // Keep a focus for the keyboard and controller if the mouse cleared it.
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != _playNow.gameObject && es.currentSelectedGameObject != _toMenu.gameObject)
                es.SetSelectedGameObject(_playNow.gameObject);
            if (Confirm())
            {
                if (es != null && es.currentSelectedGameObject == _toMenu.gameObject) Hide();
                else PressPlayNow();
            }
            else if (BackPressed()) Hide();
        }

        /// <summary>Enter, Space or controller A: the focused sticker (PLAY NOW unless the focus moved to MAIN MENU).</summary>
        static bool Confirm()
        {
            Keyboard k = Keyboard.current;
            if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame))
                return true;
            return Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
        }

        /// <summary>Escape, Backspace, controller B or Start: the main menu.</summary>
        static bool BackPressed()
        {
            Keyboard k = Keyboard.current;
            if (k != null && (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame)) return true;
            Gamepad g = Gamepad.current;
            return g != null && (g.buttonEast.wasPressedThisFrame || g.startButton.wasPressedThisFrame);
        }

        /// <summary>
        /// PLAY NOW: straight into a match with the last class and settings (first launch: the guided-match offer first, its
        /// Skip drops into the match). With the flow off (editor) or in autoplay it is the same as Hide().
        /// </summary>
        public void PressPlayNow()
        {
            if (_hidden) return;
            if (!Flow.GameFlow.Enabled || Flow.FlowDirector.Current == null || Flow.GameFlow.State == Flow.FlowState.Off ||
                Flow.GameFlow.Autoplay)
            {
                Hide();
                return;
            }
            _hidden = true;
            if (_canvas != null) Destroy(_canvas.gameObject);
            Destroy(this);
            Flow.PlayNow.FromTitle();
        }

        /// <summary>View-only idle motion: a slow bob and the egg's seam glow breathing (no flashing, below 0.5 Hz).</summary>
        public void Animate(float t)
        {
            _logo.rectTransform.anchoredPosition = new Vector2(0f, LogoY + BobPx * Mathf.Sin(t * 2f * Mathf.PI / BobPeriod));
            Color g = Color.white;
            g.a = 0.25f + 0.5f * (0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / GlowPeriod));
            _glow.color = g;
        }

        /// <summary>The pointer is over the title egg's hit area (egg-01). Clicks no longer dismiss the title (only the two
        /// stickers act on a click), so the egg's clicks never leave it; kept for the egg's own checks.</summary>
        static RectTransform _eggHitStatic;
        static UnityEngine.Camera _eggCam;

        static bool PointerOnEgg()
        {
            RectTransform _eggHit = _eggHitStatic;
            if (_eggHit == null || Mouse.current == null || !_eggHit.gameObject.activeInHierarchy) return false;
            UnityEngine.Camera cam = _eggCam;
            return RectTransformUtility.RectangleContainsScreenPoint(_eggHit, Mouse.current.position.ReadValue(), cam);
        }

        /// <summary>
        /// Dismisses the title. With the DG-01 flow running the main menu comes next (autoplay: a match) and the HUD stays
        /// hidden; with the flow off the HUD shows, as before.
        /// </summary>
        public void Hide()
        {
            if (_hidden) return;
            _hidden = true;
            bool flow = Flow.FlowDirector.TitleDismissed(this);
            if (!flow && Hud != null && Hud.Canvas != null) Hud.Canvas.enabled = true;
            if (_canvas != null) Destroy(_canvas.gameObject);
            Destroy(this);
        }

        bool _hidden;
    }
}

using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// Simple title screen: the bible's own primary logo (Visual Asset Bible p04, cut from its cream page by
    /// Art/UI/Logo/_source~/cut_bible_logo.py; 766 x 597 native, drawn 1:1 at 1080p from @1x, from @2x up to 4K, @4x above)
    /// with a slow bob and a pulsing glow on the egg's seams, over the live bot match under a Void Navy wash, and
    /// "PRESS ANY KEY" in the HUD display face. The redrawn vector logo (critical-mass-logo@Nx) stays in the repo, unused. Any key, click or gamepad
    /// button hides it and shows the HUD. Shown on boot in player builds only (ShowOnBoot), never in the editor, batch
    /// mode or tests; the capture tool shows it explicitly.
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
        public RawImage Logo => _logo;
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

            _prompt = HudText.Make(root, "Prompt", CMTokens.Display, 34, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter);
            RectTransform pr = _prompt.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(900f, 60f);
            pr.anchoredPosition = new Vector2(0f, -400f);
            _prompt.text = "PRESS ANY KEY";
            Text sub = HudText.Make(root, "Tagline", CMTokens.Ui, 22, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 2f);
            RectTransform sr = sub.rectTransform;
            sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.5f);
            sr.sizeDelta = new Vector2(900f, 40f);
            sr.anchoredPosition = new Vector2(0f, -350f);
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
            if (AnyInput()) Hide();
        }

        /// <summary>View-only idle motion: a slow bob and the egg's seam glow breathing (no flashing, below 0.5 Hz).</summary>
        public void Animate(float t)
        {
            _logo.rectTransform.anchoredPosition = new Vector2(0f, LogoY + BobPx * Mathf.Sin(t * 2f * Mathf.PI / BobPeriod));
            Color g = Color.white;
            g.a = 0.25f + 0.5f * (0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / GlowPeriod));
            _glow.color = g;
        }

        static bool AnyInput()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOnEgg()) return true;
            if (Gamepad.current != null && (Gamepad.current.startButton.wasPressedThisFrame || Gamepad.current.buttonSouth.wasPressedThisFrame)) return true;
            return false;
        }

        /// <summary>The pointer is over the title egg's hit area (egg-01): that click is the secret's, not "any key".</summary>
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

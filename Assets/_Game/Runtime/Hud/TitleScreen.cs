using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// Simple title screen: the bible p04 primary logo (vector source Art/UI/Logo/_source~, PNG exports in
    /// Resources/UI/Logo at 1x/2x/4x; the 4x is picked above 1440 p, else the 2x, drawn at or below its pixel size so it is
    /// never upscaled) over the live bot match under a Void Navy wash, and "PRESS ANY KEY". Any key, click or gamepad
    /// button hides it and shows the HUD. Shown on boot in player builds only (ShowOnBoot), never in the editor, batch
    /// mode or tests; the capture tool shows it explicitly.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        public static bool ShowOnBoot => !Application.isEditor && !Application.isBatchMode;

        public GameHud Hud;
        Canvas _canvas;
        RawImage _logo;
        Text _prompt;
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
            lr.sizeDelta = new Vector2(820f, 820f * 470f / 600f);
            lr.anchoredPosition = new Vector2(0f, 70f);

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
            string want = h > 1440 ? "UI/Logo/critical-mass-logo@4x" : "UI/Logo/critical-mass-logo@2x";
            if (_logo.texture == null || _logo.texture.name != want.Substring(want.LastIndexOf('/') + 1))
                _logo.texture = Resources.Load<Texture2D>(want);
        }

        void Update()
        {
            UpdateScale();
            _t += Time.unscaledDeltaTime;
            Color c = _prompt.color;
            c.a = 0.75f + 0.25f * Mathf.Cos(_t * Mathf.PI);   // slow 0.5 Hz breathe, no flashing
            _prompt.color = c;
            if (AnyInput()) Hide();
        }

        static bool AnyInput()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            if (Gamepad.current != null && (Gamepad.current.startButton.wasPressedThisFrame || Gamepad.current.buttonSouth.wasPressedThisFrame)) return true;
            return false;
        }

        public void Hide()
        {
            if (Hud != null && Hud.Canvas != null) Hud.Canvas.enabled = true;
            if (_canvas != null) Destroy(_canvas.gameObject);
            Destroy(this);
        }
    }
}

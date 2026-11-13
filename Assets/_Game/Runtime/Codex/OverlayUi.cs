using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Codex
{
    /// <summary>
    /// Shared pieces for the small overlays of DG-23, DG-25 and DG-26 (lore card, captions, highlights): a Screen Space -
    /// Camera canvas laid out in 1920x1080 reference units like the HUD and the pause menu, kept at the HUD scale by
    /// OverlayScaler. Sorting: HUD 10, title 20, these 25 to 35, pause menu 40.
    /// </summary>
    public static class OverlayUi
    {
        public const int CaptionOrder = 28;
        public const int LoreCardOrder = 30;
        public const int HighlightsOrder = 35;

        /// <summary>A new overlay canvas under <paramref name="parent"/> (null: scene root) with a centred 1920x1080 space.</summary>
        public static Canvas Make(Transform parent, string name, int order, bool raycasts, bool userScale, out RectTransform space)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            if (parent != null) go.transform.SetParent(parent, false);
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.sortingOrder = order;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            c.worldCamera = cam;
            c.planeDistance = cam != null ? Mathf.Max(0.42f, cam.nearClipPlane * 1.35f) : 0.42f;
            if (raycasts) go.AddComponent<GraphicRaycaster>();
            OverlayScaler sc = go.AddComponent<OverlayScaler>();
            sc.UserScale = userScale;
            sc.Apply();
            space = MenuUi.Rt(go.transform, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            return c;
        }

        /// <summary>A plain rectangle (no outline) for washes and placeholders.</summary>
        public static Image Fill(Transform parent, string name, Color c, bool raycast = false)
        {
            Image img = MenuUi.Rt(parent, name).gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        public static Color Navy => CMTokens.VoidNavy.Color;
        public static Color Cream => CMTokens.WarmCream.Color;
        public static Color Brass => CMTokens.ToolBrass.Color;
    }

    /// <summary>Keeps an overlay canvas at the HUD scale for the camera's pixel size (times the UI scale option when set).</summary>
    public sealed class OverlayScaler : MonoBehaviour
    {
        public bool UserScale;
        Canvas _canvas;

        void LateUpdate() => Apply();

        public void Apply()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) return;
            UnityEngine.Camera cam = _canvas.worldCamera;
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
                if (cam != null) _canvas.worldCamera = cam;
            }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h) * (UserScale ? SettingsExtras.UiScale : 1f);
            if (Mathf.Abs(_canvas.scaleFactor - s) > 1e-4f) _canvas.scaleFactor = s;
        }
    }

    /// <summary>
    /// A subtitle-style caption bar in the lower third (secret announcer line, photo saved, easter egg lines): a chunky
    /// Panel Navy plate with a Void Navy outline, Warm Cream text, an optional slot on the left for a small figure.
    /// Fades in and out in 180 ms; one caption at a time (a new one replaces the old).
    /// </summary>
    public sealed class CaptionBar : MonoBehaviour
    {
        const float FadeSeconds = 0.18f;
        const float PlateW = 980f, PlateH = 84f, PlateY = 700f;

        static CaptionBar _instance;

        Canvas _canvas;
        CanvasGroup _group;
        Text _text;
        RectTransform _slot;
        float _age, _hold;
        bool _showing;

        public static bool IsShowing => _instance != null && _instance._showing;
        public static string Current => _instance != null && _instance._text != null ? _instance._text.text : string.Empty;

        /// <summary>The left slot (96x96 reference px) of the bar, for a figure; cleared on every Show.</summary>
        public static RectTransform Slot => Ensure()._slot;

        public static void Show(string text, float seconds = 4f)
        {
            CaptionBar c = Ensure();
            for (int i = c._slot.childCount - 1; i >= 0; i--) Destroy(c._slot.GetChild(i).gameObject);
            HudText.Set(c._text, text ?? string.Empty);
            c._age = 0f;
            c._hold = Mathf.Max(0.5f, seconds);
            c._showing = true;
            c._canvas.enabled = true;
        }

        public static void Hide()
        {
            if (_instance == null) return;
            _instance._showing = false;
            _instance._canvas.enabled = false;
        }

        static CaptionBar Ensure()
        {
            if (_instance != null) return _instance;
            var host = new GameObject("Caption Bar");
            _instance = host.AddComponent<CaptionBar>();
            _instance.Build();
            return _instance;
        }

        void Build()
        {
            _canvas = OverlayUi.Make(transform, "Caption Canvas", OverlayUi.CaptionOrder, false, true, out RectTransform space);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            float x = (HudLayout.RefW - PlateW) * 0.5f;
            VectorShape plate = MenuUi.Panel(space, "Plate", x, PlateY, PlateW, PlateH);
            plate.Radius = 16f;
            _slot = MenuUi.Rt(space, "Slot");
            MenuUi.Place(_slot, x + 12f, PlateY - 30f, 96f, 96f);
            _text = MenuUi.Fit(MenuUi.Label(space, "Text", CMTokens.Ui, UiScale.TypeButton, OverlayUi.Cream, TextAnchor.MiddleCenter, x + 110f, PlateY, PlateW - 140f,
                PlateH, string.Empty, 2f), 26);
            _canvas.enabled = false;
        }

        void Update()
        {
            if (!_showing) return;
            _age += Time.unscaledDeltaTime;
            float a = Mathf.Min(1f, _age / FadeSeconds);
            float end = _hold + FadeSeconds;
            if (_age > end) a = Mathf.Max(0f, 1f - (_age - end) / FadeSeconds);
            _group.alpha = a;
            if (_age > end + FadeSeconds) Hide();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}

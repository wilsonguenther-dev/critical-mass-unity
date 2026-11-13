using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// D5's one-time hint for hold-to-look: a navy pill above the bottom edge, "HOLD [MMB] TO LOOK AROUND" (or "CLICK
    /// [RS] TO LOOK AROUND" on a controller), shown once ever, ShowAtSimSeconds into the first match, for ShowSimSeconds
    /// or until the player looks. Remembered in PlayerPrefs (<see cref="PrefKey"/>). Fades in and out in 200 ms.
    /// </summary>
    public sealed class LookHint : MonoBehaviour
    {
        public const string PrefKey = "cm.hint.holdToLook";
        public const double ShowAtSimSeconds = 16;
        public const double ShowSimSeconds = 12;
        public const float FadeSeconds = 0.2f;

        // Words: built-in fallbacks (the words pack has no line for this hint yet).
        public const string WordHold = "HOLD", WordClick = "CLICK", WordTail = "TO LOOK AROUND", KeyMouse = "MIDDLE CLICK", KeyPad = "RS";

        public SimDriverBehaviour Driver;

        /// <summary>True while the hint is on screen.</summary>
        public bool IsOpen => _shown && _alpha > 0f;

        private Canvas _canvas;
        private CanvasGroup _group;
        private Text _verb, _key, _tail;
        private bool _shown, _done;
        private float _alpha;
        private double _shownAt;

        private void OnEnable() => CameraFollowTarget.LookUsed += Retire;
        private void OnDisable() => CameraFollowTarget.LookUsed -= Retire;

        private void Start()
        {
            _done = PlayerPrefs.GetInt(PrefKey, 0) != 0 || SimDriverBehaviour.CaptureMode;
        }

        /// <summary>Never show the hint again (also called when the player looks).</summary>
        public void Retire()
        {
            if (!_done)
            {
                PlayerPrefs.SetInt(PrefKey, 1);
                PlayerPrefs.Save();
            }
            _done = true;
        }

        private void Build()
        {
            var go = new GameObject("Look Hint Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = 12;
            _canvas.planeDistance = 0.5f;
            _group = go.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _group.alpha = 0f;

            var pill = new GameObject("Pill", typeof(RectTransform));
            pill.layer = 5;
            pill.transform.SetParent(go.transform, false);
            var prt = (RectTransform)pill.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.sizeDelta = new Vector2(460f, 56f);
            prt.anchoredPosition = new Vector2(0f, 178f);
            VectorShape bg = pill.AddComponent<VectorShape>();
            bg.Kind = ShapeKind.RoundRect;
            bg.Radius = -1f;
            bg.FillColor = CMTokens.PanelNavy.Color;
            bg.raycastTarget = false;

            _verb = HudText.Make(prt, "Verb", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleRight, 2f);
            Place(_verb.rectTransform, -182f, 0f, 120f, 40f);
            var chip = new GameObject("Key Chip", typeof(RectTransform));
            chip.layer = 5;
            chip.transform.SetParent(prt, false);
            var crt = (RectTransform)chip.transform;
            Place(crt, -78f, 0f, 76f, 38f);
            VectorShape cs = chip.AddComponent<VectorShape>();
            cs.Kind = ShapeKind.RoundRect;
            cs.Radius = 8f;
            cs.FillColor = CMTokens.ToolBrass.Color;
            cs.raycastTarget = false;
            _key = HudText.Make(crt, "Key", CMTokens.Mono, UiScale.TypeSmall, CMTokens.VoidNavy.Color, TextAnchor.MiddleCenter, 0f);
            Place(_key.rectTransform, 0f, 0f, 76f, 38f);
            _tail = HudText.Make(prt, "Tail", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 2f);
            Place(_tail.rectTransform, 92f, 0f, 260f, 40f);
            HudText.Set(_tail, WordTail);
        }

        /// <summary>Centre-anchored placement: (x, y) is the offset of the rect's centre from the parent's centre.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private void LateUpdate()
        {
            if (_done && !_shown) return;
            var w = Driver != null ? Driver.World : null;
            if (w == null) return;
            double t = w.Match.Time;
            if (!_shown && !_done && t >= ShowAtSimSeconds)
            {
                if (_canvas == null) Build();
                _shown = true;
                _shownAt = t;
            }
            if (!_shown) return;
            bool on = !_done; // the hint retires as soon as the player looks (Retire sets _done)
            if (on && t - _shownAt >= ShowSimSeconds)
            {
                Retire();
                on = false;
            }
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null && _canvas.worldCamera != cam) _canvas.worldCamera = cam;
            float pw = cam != null ? cam.pixelWidth : Screen.width, ph = cam != null ? cam.pixelHeight : Screen.height;
            if (pw >= 1 && ph >= 1)
            {
                float s = HudLayout.Scale(pw, ph);
                if (Mathf.Abs(_canvas.scaleFactor - s) > 1e-4f) _canvas.scaleFactor = s;
            }
            bool pad = ControlBindings.ActiveDevice == ControlDevice.Gamepad;
            HudText.Set(_verb, pad ? WordClick : WordHold);
            HudText.Set(_key, pad ? KeyPad : KeyMouse);
            _alpha = Mathf.MoveTowards(_alpha, on ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            _group.alpha = _alpha;
            if (!on && _alpha <= 0f)
            {
                _shown = false;
                _canvas.gameObject.SetActive(false);
            }
        }
    }
}

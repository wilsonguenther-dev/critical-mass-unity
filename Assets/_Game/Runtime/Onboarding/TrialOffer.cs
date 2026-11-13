using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// The first-launch question, "First time? 3-minute guided match", with the mascot asking it from its corner. Two
    /// sticker buttons with one shared focus for keyboard (arrows, Enter), mouse (hover, click) and controller (stick or
    /// D-pad, A). The match behind it stands still while it is open (the driver's TimeScale is 0; restored on skip).
    /// Either answer sets the done flag, so the next launch goes straight to the menu.
    /// </summary>
    public sealed class TrialOffer : MonoBehaviour
    {
        public static TrialOffer Current { get; private set; }
        public bool IsOpen => _canvas != null && _canvas.enabled;

        Canvas _canvas;
        MenuItem _play, _skip;
        SimDriverBehaviour _driver;
        double _savedScale;
        float _t;
        RectTransform _card;

        public static TrialOffer Show(GameObject host)
        {
            if (Current != null) return Current;
            var o = host.AddComponent<TrialOffer>();
            o.Build();
            return o;
        }

        void Build()
        {
            Current = this;
            TrialText.RegisterFallbacks(null);
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver != null)
            {
                _savedScale = _driver.TimeScale;
                _driver.TimeScale = 0;
            }
            PauseMenu.EnsureEventSystem(transform);

            var go = new GameObject("Trial Offer Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = 30;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = cam != null ? Mathf.Max(0.41f, cam.nearClipPlane * 1.32f) : 0.41f;
            go.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)go.transform;

            Image wash = MenuUi.Rt(root, "Wash").gameObject.AddComponent<Image>();
            MenuUi.Stretch(wash.rectTransform);
            wash.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.55f);
            wash.raycastTarget = true;

            RectTransform space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            _card = MenuUi.Rt(space, "Card");
            MenuUi.Stretch(_card);
            MenuUi.Panel(_card, "Panel", 610f, 290f, 700f, 400f);
            MenuUi.Label(_card, "Title", CMTokens.Display, 56, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 610f, 320f, 700f, 80f,
                MascotLines.Resolve(TrialText.OfferTitle), 4f);
            MenuUi.Label(_card, "Sub", CMTokens.Ui, 24, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 610f, 400f, 700f, 40f,
                MascotLines.Resolve(TrialText.OfferSub), 2f);
            _play = MenuUi.Item(_card, "Play", 670f, 470f, 580f, 72f, MascotLines.Resolve(TrialText.OfferPlay), MenuItem.Kind.Button, 28,
                true, CMTokens.Display);
            _skip = MenuUi.Item(_card, "Skip", 670f, 560f, 580f, 64f, MascotLines.Resolve(TrialText.OfferSkip), MenuItem.Kind.Button, 22,
                true);
            _play.Submitted = OnPlay;
            _skip.Submitted = OnSkip;
            var nav = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = _skip, selectOnUp = _skip };
            _play.navigation = nav;
            _skip.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = _play, selectOnUp = _play };

            // The key that dismissed the title must not also answer this.
            MenuItem.SubmitBlockedUntil = Time.unscaledTime + 0.3f;
            UpdateScale();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_play.gameObject);

            MascotGuide m = MascotGuide.Ensure(gameObject);
            if (m != null)
            {
                m.AcceptsToggle = false;
                m.React(MascotReaction.Surprised);
                m.Say(TrialText.Offer);
                m.SetHint(string.Empty);
            }
        }

        void UpdateScale()
        {
            UnityEngine.Camera cam = _canvas.worldCamera;
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
                if (cam != null) _canvas.worldCamera = cam;
            }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(_canvas.scaleFactor - s) > 1e-4f) _canvas.scaleFactor = s;
        }

        void Update()
        {
            UpdateScale();
            // A short view-only pop (200 ms).
            _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / 0.2f);
            float e = 1f - (1f - _t) * (1f - _t);
            _card.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
            // Keep a focus for the keyboard and controller if the mouse cleared it.
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == null) es.SetSelectedGameObject(_play.gameObject);
        }

        void OnPlay()
        {
            TrialProgress.Done = true;
            Close(false);
            Trial.Start();
        }

        void OnSkip()
        {
            TrialProgress.Done = true;
            Close(true);
            MascotGuide m = MascotGuide.Instance;
            if (m != null) m.Hide();
        }

        void Close(bool restoreScale)
        {
            if (restoreScale && _driver != null) _driver.TimeScale = _savedScale > 0 ? _savedScale : _driver.Config.DefaultSpeed;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Destroy(this);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }
    }
}

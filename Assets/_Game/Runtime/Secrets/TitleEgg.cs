using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Secrets
{
    /// <summary>
    /// egg-01, the title egg: hit the egg on the title screen ten times and a tiny hatchling climbs out and waddles off
    /// screen ("Fine. Go on, then."). Off until the title screen hands over its egg: today's title (Hud/TitleScreen) is
    /// dismissed by the first click, so the game-flow title (DG-01) calls Attach with the egg's rect, and Hit() from its
    /// own controller or keyboard input if it wants pads to play. Each hit punches the egg (scale 0.92 for 90 ms).
    /// </summary>
    public sealed class TitleEgg : MonoBehaviour, IPointerClickHandler
    {
        const float WaddleSeconds = 2.8f, PunchSeconds = 0.09f;

        static TitleEgg _current;
        readonly TitleEggCounter _counter = new TitleEggCounter();
        RectTransform _egg;
        Vector3 _eggScale = Vector3.one;
        float _punch = -1f;
        Hatchling _hatchling;
        Text _say;
        float _walk = -1f;
        Vector2 _start;

        public static bool IsAttached => _current != null;
        public static int Hits => _current != null ? _current._counter.Hits : 0;
        public static bool Hatched => _current != null && _current._counter.Hatched;

        /// <summary>
        /// Makes <paramref name="egg"/> (the title's egg graphic) clickable for the secret. Adds a transparent hit area over
        /// it (its canvas needs a GraphicRaycaster). Safe to call again; the latest egg wins.
        /// </summary>
        public static TitleEgg Attach(RectTransform egg)
        {
            if (egg == null) return null;
            TitleEgg t = egg.GetComponent<TitleEgg>();
            if (t == null)
            {
                Graphic hit = egg.GetComponent<Graphic>();
                if (hit == null)
                {
                    Image clear = egg.gameObject.AddComponent<Image>();
                    clear.color = new Color(0f, 0f, 0f, 0f);
                    hit = clear;
                }
                hit.raycastTarget = true;
                t = egg.gameObject.AddComponent<TitleEgg>();
                t._egg = egg;
                t._eggScale = egg.localScale;
            }
            _current = t;
            SecretsBoard.TitleEggAttached = true;
            return t;
        }

        /// <summary>One hit on the attached egg (pads and keys can call this); false when no egg is attached.</summary>
        public static bool Hit()
        {
            if (_current == null) return false;
            _current.OnHit();
            return true;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) OnHit();
        }

        void OnHit()
        {
            _punch = 0f;
            if (!_counter.Hit()) return;
            SecretsBoard.Fire(SecretsBoard.TitleEgg);
            var parent = _egg.parent as RectTransform;
            if (parent == null) return;
            // The hatchling starts at the egg's foot and walks to the right edge of the egg's parent.
            Vector2 p = _egg.anchoredPosition;
            _hatchling = Hatchling.Make(parent, 0f, 0f, 120f, Hatchling.Pose.Waddle);
            RectTransform hr = (RectTransform)_hatchling.transform;
            hr.anchorMin = hr.anchorMax = _egg.anchorMin;
            _start = p + new Vector2(0f, -_egg.rect.height * 0.35f);
            hr.anchoredPosition = _start;
            _say = HudText.Make(hr, "Say", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 2f);
            _say.rectTransform.anchoredPosition = new Vector2(0f, 96f);
            _say.rectTransform.sizeDelta = new Vector2(360f, 36f);
            _say.text = LoreText.EggLine(SecretsBoard.TitleEgg);
            _walk = 0f;
        }

        void Update()
        {
            if (_punch >= 0f && _egg != null)
            {
                _punch += Time.unscaledDeltaTime;
                float k = _punch < PunchSeconds ? 0.92f : 1f;
                _egg.localScale = _eggScale * k;
                if (_punch >= PunchSeconds) _punch = -1f;
            }
            if (_walk < 0f || _hatchling == null) return;
            _walk += Time.unscaledDeltaTime;
            var parent = (RectTransform)_hatchling.transform.parent;
            float dist = parent.rect.width * 0.5f + 200f;
            float t = Mathf.Clamp01(_walk / WaddleSeconds);
            ((RectTransform)_hatchling.transform).anchoredPosition = _start + new Vector2(dist * t, 0f);
            if (_say != null) _say.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, _walk < 1.6f ? 1f : Mathf.Clamp01(1f - (_walk - 1.6f) / 0.25f));
            if (_walk >= WaddleSeconds)
            {
                Destroy(_hatchling.gameObject);
                _hatchling = null;
                _walk = -1f;
            }
        }

        void OnDestroy()
        {
            if (_current == this)
            {
                _current = null;
                SecretsBoard.TitleEggAttached = false;
            }
        }
    }

    /// <summary>
    /// egg-05, the credits gag: the line under the final credit, then the hatchling looks at the camera. Off until the
    /// credits screen (DG-29) calls Build where its last line ends.
    /// </summary>
    public static class CreditsGag
    {
        public static string Line => LoreText.EggLine(SecretsBoard.CreditsGag);

        /// <summary>Builds the gag at (x, y) of <paramref name="parent"/> (reference px, top-left origin), width w; returns its root.</summary>
        public static RectTransform Build(RectTransform parent, float x, float y, float w)
        {
            RectTransform root = MenuUi.Rt(parent, "Credits Gag");
            MenuUi.Place(root, x, y, w, 200f);
            MenuUi.Label(root, "Line", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 0f, 0f, w, 40f, Line, 2f);
            Hatchling.Make(root, (w - 120f) * 0.5f, 60f, 120f, Hatchling.Pose.Look);
            SecretsBoard.CreditsGagPlaced = true;
            SecretsBoard.Fire(SecretsBoard.CreditsGag);
            return root;
        }
    }
}

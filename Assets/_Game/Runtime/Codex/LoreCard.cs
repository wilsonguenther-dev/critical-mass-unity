using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Codex
{
    /// <summary>
    /// The 2-line lore card a relic opens on pickup (DG-25): a chunky Panel Navy card low in the centre of the screen,
    /// clear of the player card and the minimap, with a brass relic badge, the relic's name, its two lines and a footer
    /// ("BASIN RELIC 3 OF 8"). Not modal: play goes on under it. Slides up 24 px and fades in 200 ms, holds 6 s, fades
    /// out 200 ms; a second pickup replaces it. Follows the UI scale option.
    /// </summary>
    public sealed class LoreCard : MonoBehaviour
    {
        public const float HoldSeconds = 6f;
        const float InSeconds = 0.2f, OutSeconds = 0.2f, Rise = 24f;
        const float CardW = 760f, CardH = 176f, CardX = (1920f - CardW) * 0.5f, CardY = 716f;

        static LoreCard _instance;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _card;
        Text _title, _line1, _line2, _footer;
        VectorShape _badge;
        float _age;
        bool _open;

        public static bool IsOpen => _instance != null && _instance._open;
        public static string Title => _instance != null ? _instance._title.text : string.Empty;
        public static string Line1 => _instance != null ? _instance._line1.text : string.Empty;
        public static string Line2 => _instance != null ? _instance._line2.text : string.Empty;
        public static string Footer => _instance != null ? _instance._footer.text : string.Empty;

        /// <summary>Shows a relic's card by words pack id; <paramref name="footer"/> e.g. "BASIN RELIC 3 OF 8".</summary>
        public static void ShowRelic(string relicId, string footer, bool secret = false)
        {
            LoreText.Relic r = LoreText.GetRelic(relicId);
            Show(r.Name, r.Line1, r.Line2, footer, secret);
        }

        public static void Show(string title, string line1, string line2, string footer, bool secret = false)
        {
            LoreCard c = Ensure();
            HudText.Set(c._title, (title ?? string.Empty).ToUpperInvariant());
            HudText.Set(c._line1, line1 ?? string.Empty);
            HudText.Set(c._line2, line2 ?? string.Empty);
            HudText.Set(c._footer, footer ?? string.Empty);
            c._badge.Kind = secret ? ShapeKind.Crown : ShapeKind.Diamond;
            c._badge.SetVerticesDirty();
            c._age = 0f;
            c._open = true;
            c._canvas.enabled = true;
            c.Apply();
        }

        public static void Hide()
        {
            if (_instance == null) return;
            _instance._open = false;
            _instance._canvas.enabled = false;
        }

        static LoreCard Ensure()
        {
            if (_instance != null) return _instance;
            var host = new GameObject("Lore Card");
            _instance = host.AddComponent<LoreCard>();
            _instance.Build();
            return _instance;
        }

        void Build()
        {
            _canvas = OverlayUi.Make(transform, "Lore Card Canvas", OverlayUi.LoreCardOrder, false, true, out RectTransform space);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _card = MenuUi.Rt(space, "Card");
            MenuUi.Place(_card, CardX, CardY, CardW, CardH);
            VectorShape panel = MenuUi.Panel(_card, "Panel", 0f, 0f, CardW, CardH);
            panel.Radius = 20f;
            // Brass badge on a Void Navy disc, left.
            MenuUi.Shape(_card, "Badge Disc", ShapeKind.Circle, 28f, 40f, 96f, 96f, CMTokens.VoidNavy.Color, -1f).RimPx = 0f;
            _badge = MenuUi.Shape(_card, "Badge", ShapeKind.Diamond, 46f, 54f, 60f, 68f, CMTokens.ToolBrass.Color, -1f);
            const float tx = 148f, tw = CardW - tx - 32f;
            _title = MenuUi.Label(_card, "Title", CMTokens.Display, UiScale.TypeHeading, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, tx, 18f, tw, 44f, "", 3f);
            _line1 = MenuUi.Label(_card, "Line 1", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, tx, 66f, tw, 34f, "", 2f);
            _line2 = MenuUi.Label(_card, "Line 2", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, tx, 100f, tw, 34f, "", 2f);
            _footer = MenuUi.Label(_card, "Footer", CMTokens.Mono, UiScale.TypeMin, CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.8f), TextAnchor.MiddleRight,
                tx, 138f, tw, 26f, "", 2f);
            _canvas.enabled = false;
        }

        void Update()
        {
            if (!_open) return;
            _age += Time.unscaledDeltaTime;
            Apply();
            if (_age > InSeconds + HoldSeconds + OutSeconds) Hide();
        }

        void Apply()
        {
            float a, rise;
            if (_age < InSeconds)
            {
                float t = _age / InSeconds;
                t = 1f - (1f - t) * (1f - t);   // ease out
                a = t;
                rise = (1f - t) * Rise;
            }
            else if (_age < InSeconds + HoldSeconds)
            {
                a = 1f;
                rise = 0f;
            }
            else
            {
                a = Mathf.Clamp01(1f - (_age - InSeconds - HoldSeconds) / OutSeconds);
                rise = 0f;
            }
            _group.alpha = a;
            MenuUi.Place(_card, CardX, CardY + rise, CardW, CardH);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}

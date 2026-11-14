using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// One input prompt on screen: the drawn glyph (InputGlyphGraphic), the letters on it (a keycap's name, a face
    /// button's letter, LB / RT on a bumper or trigger) and, where there is room, the plain words beside it ("Left click").
    /// Set(spec) lays it out from the left edge of its RectTransform and sizes the RectTransform to fit (Width); nothing
    /// is rebuilt when the same spec is set again, so callers may set it every frame without cost.
    /// </summary>
    public sealed class InputGlyphView : MonoBehaviour
    {
        public InputGlyphGraphic Graphic { get; private set; }
        public Text Cap { get; private set; }
        public Text WordsText { get; private set; }
        public GlyphSpec Spec { get; private set; }
        public RectTransform Rect { get; private set; }
        /// <summary>Glyph height in canvas units (1080p px).</summary>
        public float Height { get; private set; } = 32f;
        /// <summary>Glyph plus words, after the last Set.</summary>
        public float Width { get; private set; }
        public float GlyphWidth { get; private set; }

        bool _showWords;
        bool _hasSpec;
        const float WordsGap = 8f;

        /// <summary>
        /// Creates a prompt under parent. height: the glyph's height; words: show the plain words to the right in the HUD's
        /// UI face (wordsSize, wordsColor). The RectTransform is anchored and pivoted on its left-middle edge.
        /// </summary>
        public static InputGlyphView Make(Transform parent, string name, float height, bool words = false, int wordsSize = 20,
            Color? wordsColor = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<InputGlyphView>();
            v.Init(height, words, wordsSize, wordsColor ?? CMTokens.WarmCream.Color);
            return v;
        }

        void Init(float height, bool words, int wordsSize, Color wordsColor)
        {
            Rect = (RectTransform)transform;
            Rect.anchorMin = Rect.anchorMax = new Vector2(0f, 0.5f);
            Rect.pivot = new Vector2(0f, 0.5f);
            Height = height;
            _showWords = words;

            var g = new GameObject("Glyph", typeof(RectTransform));
            g.layer = gameObject.layer;
            g.transform.SetParent(transform, false);
            Graphic = g.AddComponent<InputGlyphGraphic>();
            Graphic.raycastTarget = false;
            LeftMiddle(Graphic.rectTransform);

            Cap = HudText.Make(g.transform, "Cap", CMTokens.Ui, Mathf.RoundToInt(height * 0.5f), CMTokens.VoidNavy.Color,
                TextAnchor.MiddleCenter, 0f);
            Cap.rectTransform.anchorMin = Vector2.zero;
            Cap.rectTransform.anchorMax = Vector2.one;
            Cap.rectTransform.offsetMin = Cap.rectTransform.offsetMax = Vector2.zero;

            WordsText = HudText.Make(transform, "Words", CMTokens.Ui, wordsSize, wordsColor, TextAnchor.MiddleLeft, 2f);
            LeftMiddle(WordsText.rectTransform);
            WordsText.gameObject.SetActive(words);
        }

        static void LeftMiddle(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Shows or hides the words beside the glyph (relayout).</summary>
        public bool ShowWords
        {
            get => _showWords;
            set
            {
                if (_showWords == value) return;
                _showWords = value;
                WordsText.gameObject.SetActive(value);
                Layout();
            }
        }

        /// <summary>Changes the glyph height (relayout).</summary>
        public void SetHeight(float h)
        {
            if (Mathf.Abs(h - Height) < 0.01f) return;
            Height = h;
            Layout();
        }

        /// <summary>Shows spec; returns Width. Does nothing when spec is the one shown.</summary>
        public float Set(GlyphSpec spec)
        {
            if (_hasSpec && spec.Equals(Spec)) return Width;
            _hasSpec = true;
            Spec = spec;
            Graphic.Set(spec.Kind, spec.Mark, spec.Face);
            HudText.Set(Cap, spec.Cap ?? string.Empty);
            HudText.Set(WordsText, spec.Words ?? string.Empty);
            Layout();
            return Width;
        }

        /// <summary>The glyph's width for a kind, height and cap (keys and small buttons grow with their name).</summary>
        public static float GlyphWidthFor(GlyphKind kind, string cap, float h)
        {
            int n = cap != null ? cap.Length : 0;
            switch (kind)
            {
                case GlyphKind.Key: return Mathf.Max(h, n * h * 0.36f + h * 0.55f);
                case GlyphKind.Mouse: return h * 0.72f;
                case GlyphKind.Bumper: return h * 1.5f;
                case GlyphKind.Trigger: return h * 0.95f;
                case GlyphKind.SmallButton: return Mathf.Max(h * 1.3f, n * h * 0.32f + h * 0.5f);
                case GlyphKind.None: return 0f;
                default: return h;
            }
        }

        static float CapScale(GlyphKind kind)
        {
            switch (kind)
            {
                case GlyphKind.Key: return 0.46f;
                case GlyphKind.FaceButton: return 0.56f;
                case GlyphKind.Bumper: return 0.4f;
                case GlyphKind.Trigger: return 0.4f;
                case GlyphKind.Stick: return 0.38f;
                case GlyphKind.SmallButton: return 0.34f;
                default: return 0.45f;
            }
        }

        void Layout()
        {
            float h = Height;
            GlyphSpec s = Spec;
            GlyphWidth = GlyphWidthFor(s.Kind, s.Cap, h);
            RectTransform g = Graphic.rectTransform;
            g.sizeDelta = new Vector2(GlyphWidth, h);
            g.anchoredPosition = Vector2.zero;
            Cap.fontSize = Mathf.Max(8, Mathf.RoundToInt(h * CapScale(s.Kind)));
            Rect cr = InputGlyphGraphic.CapRect(s.Kind);
            RectTransform c = Cap.rectTransform;
            c.anchorMin = new Vector2(cr.xMin, cr.yMin);
            c.anchorMax = new Vector2(cr.xMax, cr.yMax);
            c.offsetMin = c.offsetMax = Vector2.zero;
            Cap.gameObject.SetActive(!string.IsNullOrEmpty(s.Cap));
            float w = GlyphWidth;
            if (_showWords && !string.IsNullOrEmpty(s.Words))
            {
                float ww = Mathf.Ceil(ContainedText.NaturalWidth(WordsText)) + 4f;
                RectTransform t = WordsText.rectTransform;
                t.sizeDelta = new Vector2(ww, h);
                t.anchoredPosition = new Vector2(w + WordsGap, 0f);
                w += WordsGap + ww;
            }
            Width = w;
            Rect.sizeDelta = new Vector2(w, h);
        }
    }
}

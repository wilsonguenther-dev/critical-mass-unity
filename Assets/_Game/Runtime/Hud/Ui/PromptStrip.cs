using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// A line of prompts: glyphs and words side by side ("[Enter] SELECT   [Esc] BACK", "MOVE WITH [W][A][S][D]"). Build a
    /// line with Begin, AddGlyph / AddText / AddGap, End; or hand it a menu hint string (SetHint) and the key words in it
    /// become glyphs for the device it was written for. Pieces are pooled (at most MaxPieces each of glyphs and texts) and
    /// the line is laid out only when it changes; SetHint with the same string does nothing.
    /// </summary>
    public sealed class PromptStrip : MonoBehaviour
    {
        public const int MaxPieces = 16;
        public const char Separator = '·';

        public RectTransform Rect { get; private set; }
        /// <summary>Width of the laid-out line.</summary>
        public float Width { get; private set; }
        /// <summary>Middle (default) centres the line on the strip's rect; Left starts at its left edge.</summary>
        public TextAnchor Align = TextAnchor.MiddleCenter;
        /// <summary>The words of the last line, glyphs as their plain words (tests read it).</summary>
        public string PlainText { get; private set; } = string.Empty;

        float _glyphH;
        int _fontSize;
        Color _textColor;
        Typeface _face;
        readonly List<InputGlyphView> _glyphs = new List<InputGlyphView>();
        readonly List<Text> _texts = new List<Text>();
        readonly List<RectTransform> _order = new List<RectTransform>();
        readonly List<float> _gaps = new List<float>();
        int _usedGlyphs, _usedTexts;
        float _pendingGap;
        string _hint;
        ControlDevice _hintDevice;
        readonly System.Text.StringBuilder _plain = new System.Text.StringBuilder(64);

        /// <summary>A strip stretched over parent's rect (or placed by the caller afterwards).</summary>
        public static PromptStrip Make(Transform parent, string name, float glyphHeight, int fontSize, Color textColor,
            Typeface? face = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<PromptStrip>();
            s.Rect = (RectTransform)go.transform;
            s._glyphH = glyphHeight;
            s._fontSize = fontSize;
            s._textColor = textColor;
            s._face = face ?? CMTokens.Ui;
            return s;
        }

        void EnsureRect()
        {
            if (Rect == null) Rect = (RectTransform)transform;
        }

        // ---- building ----

        public void Begin()
        {
            EnsureRect();
            _usedGlyphs = 0;
            _usedTexts = 0;
            _order.Clear();
            _gaps.Clear();
            _pendingGap = 0f;
            _plain.Length = 0;
            _hint = null;
        }

        public void AddGap(float px) => _pendingGap += px;

        public InputGlyphView AddGlyph(GlyphSpec spec)
        {
            if (spec.IsEmpty || _usedGlyphs >= MaxPieces) return null;
            InputGlyphView v;
            if (_usedGlyphs < _glyphs.Count) v = _glyphs[_usedGlyphs];
            else
            {
                v = InputGlyphView.Make(transform, "Glyph " + _glyphs.Count, _glyphH);
                _glyphs.Add(v);
            }
            _usedGlyphs++;
            v.gameObject.SetActive(true);
            v.SetHeight(_glyphH);
            v.Set(spec);
            Push(v.Rect, 6f);
            if (_plain.Length > 0) _plain.Append(' ');
            _plain.Append(spec.Words);
            return v;
        }

        public Text AddText(string s)
        {
            if (string.IsNullOrEmpty(s) || _usedTexts >= MaxPieces) return null;
            Text t;
            if (_usedTexts < _texts.Count) t = _texts[_usedTexts];
            else
            {
                t = HudText.Make(transform, "Text " + _texts.Count, _face, _fontSize, _textColor, TextAnchor.MiddleLeft, 2f);
                RectTransform rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                _texts.Add(t);
            }
            _usedTexts++;
            t.gameObject.SetActive(true);
            t.fontSize = _fontSize;
            HudText.Set(t, s);
            float w = Mathf.Ceil(ContainedText.NaturalWidth(t)) + 4f;
            t.rectTransform.sizeDelta = new Vector2(w, Mathf.Max(_glyphH, _fontSize * 1.4f));
            Push(t.rectTransform, 6f);
            if (_plain.Length > 0) _plain.Append(' ');
            _plain.Append(s);
            return t;
        }

        void Push(RectTransform rt, float gapBefore)
        {
            _gaps.Add(_order.Count == 0 ? 0f : gapBefore + _pendingGap);
            _pendingGap = 0f;
            _order.Add(rt);
        }

        /// <summary>Hides unused pieces and lays the line out.</summary>
        public void End()
        {
            for (int i = _usedGlyphs; i < _glyphs.Count; i++) _glyphs[i].gameObject.SetActive(false);
            for (int i = _usedTexts; i < _texts.Count; i++) _texts[i].gameObject.SetActive(false);
            float x = 0f;
            for (int i = 0; i < _order.Count; i++)
            {
                x += _gaps[i];
                _order[i].anchoredPosition = new Vector2(x, 0f);
                x += _order[i].sizeDelta.x;
            }
            Width = x;
            float start = 0f;
            float w = Rect.rect.width;
            if (Align == TextAnchor.MiddleCenter || Align == TextAnchor.UpperCenter || Align == TextAnchor.LowerCenter)
                start = (w - Width) * 0.5f;
            else if (Align == TextAnchor.MiddleRight || Align == TextAnchor.UpperRight || Align == TextAnchor.LowerRight)
                start = w - Width;
            for (int i = 0; i < _order.Count; i++)
            {
                RectTransform rt = _order[i];
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + start, 0f);
            }
            PlainText = _plain.ToString();
        }

        // ---- menu hint strings ----

        /// <summary>
        /// A menu hint ("ENTER SELECT   ·   ESC BACK", "A SELECT   ·   LB / RB TABS"): in each part between the dots the
        /// leading key words (with "/" between them) become glyphs for device and the rest stays words; a part that does
        /// not start with a key ("PRESS A KEY OR MOUSE BUTTON") stays words. Does nothing when hint and device are unchanged.
        /// </summary>
        public void SetHint(string hint, ControlDevice device)
        {
            if (ReferenceEquals(hint, _hint) && device == _hintDevice) return;
            if (hint == _hint && device == _hintDevice && hint != null) return;
            Begin();
            _hint = hint;
            _hintDevice = device;
            if (!string.IsNullOrEmpty(hint))
            {
                string[] parts = hint.Split(Separator);
                for (int p = 0; p < parts.Length; p++)
                {
                    if (p > 0) AddGap(26f);
                    AddHintPart(parts[p].Trim(), device);
                }
            }
            string keep = _hint;
            End();
            _hint = keep;
        }

        void AddHintPart(string part, ControlDevice device)
        {
            if (part.Length == 0) return;
            string[] words = part.Split(' ');
            int i = 0;
            int glyphs = 0;
            // Leading keys: KEY, or KEY / KEY ...; needs at least one word after the keys to be a hint.
            while (i < words.Length)
            {
                string w = words[i];
                if (w.Length == 0) { i++; continue; }
                if (w == "/" && glyphs > 0) { i++; continue; }
                GlyphSpec g = InputGlyphs.ForToken(w, device);
                if (g.IsEmpty) break;
                if (i == words.Length - 1 && glyphs == 0) break;   // a lone word is not a key prompt
                AddGlyph(g);
                glyphs++;
                i++;
            }
            if (i < words.Length)
            {
                string rest = string.Join(" ", words, i, words.Length - i).Trim();
                AddText(rest);
            }
        }
    }
}

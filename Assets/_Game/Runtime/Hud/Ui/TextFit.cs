using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// hud-containment-1: the ONE text rule for the HUD and the menus. Every text lives in a box that measures it:
    /// <list type="number">
    /// <item>the box may grow up to a stated maximum (the caller sizes the box first, e.g. a pill fitted to its words);</item>
    /// <item>then the text wraps, where the box allows a second line (MaxLines 2);</item>
    /// <item>then it shrinks one point at a time down to a stated minimum size;</item>
    /// <item>then it ends with an ellipsis, cut at a word end when a word end fits.</item>
    /// </list>
    /// No text draws outside its box. Unity's own best fit is never used: with horizontal overflow it is ignored, and with
    /// wrap plus vertical truncate it drops the last line or cuts a word in half.
    /// <para>Type scale (reference px, UiScale): 104 hero, 64 title, 48 display, 36 lead, 30 heading, 28 button, 22 body,
    /// 18 small, 16 min. A shrinking HUD text never goes under 11 px (12 px for names on a card); menu text never under 14.</para>
    /// <para>Cost: work only when the text or the box changes; results are cached by (text, size range, box, font, lines),
    /// so a text set to the same words every frame costs a string compare and no allocation.</para>
    /// </summary>
    public static class TextFit
    {
        /// <summary>The ellipsis the rule ends a cut text with (three dots when the font lacks the glyph).</summary>
        public const char Ellipsis = '…';
        /// <summary>The smallest size any shrinking HUD text may reach.</summary>
        public const int HudFloor = 11;
        /// <summary>The smallest size any shrinking menu text may reach.</summary>
        public const int MenuFloor = 14;

        /// <summary>
        /// Capture flag (-hudStress, HudStressFlag): every contained text is padded by 30% (its own words repeated) so the
        /// longest strings of a future language show on every screen. View only.
        /// </summary>
        public static bool Stress;

        struct Fitted { public int Size; public string Shown; public bool Wrap; }

        static readonly Dictionary<(string, int, int, int, int, int, int), Fitted> Cache = new Dictionary<(string, int, int, int, int, int, int), Fitted>();

        /// <summary>Lines a box of this height allows at a size (at least 1; a one-line chip stays one line).</summary>
        public static int LinesFor(float boxH, int size) => Mathf.Max(1, Mathf.FloorToInt(boxH / (Mathf.Max(1, size) * 1.17f)));

        /// <summary>
        /// Applies the rule to t for source s inside t's current rect: size from max down to min, wrapping when maxLines is 2,
        /// then the ellipsis. Returns the text set.
        /// </summary>
        public static string Apply(Text t, string s, int max, int min, int maxLines)
        {
            if (t == null) return s;
            if (s == null) s = string.Empty;
            if (Stress && s.Length > 0) s = Padded(s);
            if (min > max) min = max;
            Rect r = t.rectTransform.rect;
            var key = (s, max, min, Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height), t.font != null ? t.font.GetInstanceID() : 0, maxLines);
            t.resizeTextForBestFit = false;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (Cache.TryGetValue(key, out Fitted f))
            {
                SetMode(t, f.Wrap);
                if (t.fontSize != f.Size) t.fontSize = f.Size;
                if (t.text != f.Shown) t.text = f.Shown;
                return f.Shown;
            }
            f = Fit(t, s, max, min, maxLines, r.width, r.height);
            Cache[key] = f;
            return f.Shown;
        }

        static void SetMode(Text t, bool wrap)
        {
            HorizontalWrapMode m = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            if (t.horizontalOverflow != m) t.horizontalOverflow = m;
        }

        static Fitted Fit(Text t, string s, int max, int min, int maxLines, float w, float h)
        {
            var f = new Fitted { Size = max, Shown = s, Wrap = false };
            SetMode(t, false);
            t.text = s;
            if (w <= 1f || s.Length == 0) { t.fontSize = max; return f; }
            for (int size = max; size >= min; size--)
            {
                t.fontSize = size;
                SetMode(t, false);
                if (t.preferredWidth <= w + 0.5f) { f.Size = size; return f; }
                if (maxLines >= 2 && LinesFor(h, size) >= 2 && s.IndexOf(' ') > 0)
                {
                    SetMode(t, true);   // preferredHeight with wrap measures at the box width
                    if (t.preferredHeight <= h + 0.5f && !CutsAWord(t, s, w)) { f.Size = size; f.Wrap = true; return f; }
                }
            }
            // at the floor: one line (or two) ending with an ellipsis
            t.fontSize = min;
            bool wrap = maxLines >= 2 && LinesFor(h, min) >= 2;
            SetMode(t, wrap);
            f.Size = min;
            f.Wrap = wrap;
            f.Shown = Cut(t, s, w, h, wrap);
            return f;
        }

        /// <summary>True when one word alone is wider than the box (wrap would break it mid-word).</summary>
        static bool CutsAWord(Text t, string s, float w)
        {
            int start = 0;
            string keep = t.text;
            HorizontalWrapMode mode = t.horizontalOverflow;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            bool bad = false;
            for (int i = 0; i <= s.Length && !bad; i++)
            {
                if (i < s.Length && s[i] != ' ') continue;
                if (i - start > 0)
                {
                    t.text = s.Substring(start, i - start);
                    if (t.preferredWidth > w + 0.5f) bad = true;
                }
                start = i + 1;
            }
            t.text = keep;
            t.horizontalOverflow = mode;
            return bad;
        }

        static bool Fits(Text t, float w, float h, bool wrap) => wrap ? t.preferredHeight <= h + 0.5f : t.preferredWidth <= w + 0.5f;

        /// <summary>The longest start of s plus an ellipsis that fits; a word end is preferred, else any character.</summary>
        static string Cut(Text t, string s, float w, float h, bool wrap)
        {
            t.text = s;
            if (Fits(t, w, h, wrap)) return s;
            string dots = t.font != null && t.font.HasCharacter(Ellipsis) ? Ellipsis.ToString() : "...";
            string best = null;
            for (int n = s.Length - 1; n > 0; n--)
            {
                bool wordEnd = s[n] == ' ' || s[n - 1] == ' ';
                if (!wordEnd) continue;
                string c = s.Substring(0, n).TrimEnd() + dots;
                t.text = c;
                if (Fits(t, w, h, wrap)) { best = c; break; }
            }
            if (best == null)
                for (int n = s.Length - 1; n > 0; n--)
                {
                    string c = s.Substring(0, n) + dots;
                    t.text = c;
                    if (Fits(t, w, h, wrap)) { best = c; break; }
                }
            if (best == null) best = dots;
            t.text = best;
            return best;
        }

        static readonly Dictionary<string, string> PadCache = new Dictionary<string, string>();

        /// <summary>s plus 30% more of its own characters (the stress capture's future-language length).</summary>
        public static string Padded(string s)
        {
            if (PadCache.TryGetValue(s, out string p)) return p;
            int extra = Mathf.CeilToInt(s.Length * 0.3f);
            var sb = new System.Text.StringBuilder(s.Length + extra + 1);
            sb.Append(s);
            for (int i = 0; i < extra; i++) sb.Append(s[i % s.Length] == '\n' ? ' ' : s[i % s.Length]);
            p = sb.ToString();
            PadCache[s] = p;
            return p;
        }

        /// <summary>
        /// For the layout tests and the audit: true when the drawn text of t fits inside t's rect (width for one line,
        /// height when wrapping), measured at t's current settings.
        /// </summary>
        public static bool Contained(Text t)
        {
            if (t == null || string.IsNullOrEmpty(t.text)) return true;
            Rect r = t.rectTransform.rect;
            if (t.horizontalOverflow == HorizontalWrapMode.Wrap) return t.preferredHeight <= r.height + 0.5f;
            return t.preferredWidth <= r.width + 0.5f && t.preferredHeight <= r.height + Mathf.Max(6f, t.fontSize * 0.5f);
        }
    }

    /// <summary>
    /// hud-containment-1: puts a label under <see cref="TextFit"/> for good. Whenever its text (or its box) changes, the rule
    /// runs once, just before the canvases rebuild (Canvas.preWillRenderCanvases). HudText.Make attaches it to every label (MenuUi.Fit, MenuUi.Contain and SetOneLine set other ranges), so every HUD and menu label follows the one rule.
    /// No per-frame work: Unity tells it when the text dirtied its layout.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContainedText : MonoBehaviour
    {
        public int MaxSize = 22, MinSize = TextFit.HudFloor, MaxLines = 1;
        /// <summary>The words the game set, before any cut (what the label says in full).</summary>
        public string Source { get; private set; } = string.Empty;
        string _shown;
        int _size;
        Text _text;
        bool _applying;

        static readonly List<ContainedText> Dirty = new List<ContainedText>(512);   // sized for every HUD and menu label: no growth in play
        static readonly Dictionary<Text, ContainedText> ByText = new Dictionary<Text, ContainedText>();
        static bool _hooked;

        public Text Label => _text;
        /// <summary>What the rule last put on the label (the source, or its cut), null before the first run.</summary>
        public string Shown => _shown;

        /// <summary>Puts t under the rule with this size range (lines from the box height unless given).</summary>
        public static ContainedText Attach(Text t, int max, int min, int lines = 0)
        {
            if (t == null) return null;
            ContainedText c = t.GetComponent<ContainedText>();
            if (c == null) c = t.gameObject.AddComponent<ContainedText>();
            c._text = t;
            c.MaxSize = Mathf.Max(max, min);
            c.MinSize = Mathf.Min(max, min);
            c._autoLines = lines <= 0;
            c.MaxLines = lines > 0 ? lines : TextFit.LinesFor(t.rectTransform.rect.height, c.MinSize);
            ByText[t] = c;
            c.Source = t.text ?? string.Empty;
            c.Hook();
            c.ApplyNow();
            return c;
        }

        /// <summary>The containment of a label, or null (a dictionary lookup, no GetComponent).</summary>
        public static ContainedText Of(Text t) => t != null && ByText.TryGetValue(t, out ContainedText c) && c != null ? c : null;

        /// <summary>The smallest size a label built at this size may shrink to under the rule (60% of it, never under the HUD floor).</summary>
        public static int FloorFor(int size) => Mathf.Min(size, Mathf.Max(TextFit.HudFloor, Mathf.RoundToInt(size * 0.6f)));

        /// <summary>Sets the size range (and lines, 0 = from the box) and runs the rule again if it changed.</summary>
        public void Configure(int max, int min, int lines = 0)
        {
            int mx = Mathf.Max(max, min), mn = Mathf.Min(max, min);
            int ln = lines > 0 ? lines : (_text != null ? TextFit.LinesFor(_text.rectTransform.rect.height, mn) : 1);
            if (mx == MaxSize && mn == MinSize && ln == MaxLines && _autoLines == lines <= 0) return;
            MaxSize = mx;
            MinSize = mn;
            MaxLines = ln;
            _autoLines = lines <= 0;
            _shown = null;
        }

        /// <summary>Sets the label's words and applies the rule now (same words again: no work).</summary>
        public void SetSource(string s)
        {
            if (s == null) s = string.Empty;
            if (_shown != null && s == Source) return;
            _applying = true;
            try { _text.text = s; }
            finally { _applying = false; }
            Source = s;
            _shown = null;
            ApplyNow();
        }

        /// <summary>The source words of a contained label, or null if t is not contained (HudText.Set reads it).</summary>
        public static string SourceOf(Text t) => t != null && ByText.TryGetValue(t, out ContainedText c) && c != null ? c.Source : null;

        void Awake() { if (_text == null) _text = GetComponent<Text>(); }

        void OnEnable()
        {
            if (_text == null) _text = GetComponent<Text>();
            if (_text != null) { ByText[_text] = this; Hook(); MarkDirty(); }
        }

        void OnDisable() { if (_text != null) _text.UnregisterDirtyLayoutCallback(MarkDirty); }

        void OnDestroy() { if (_text != null) ByText.Remove(_text); }

        bool _autoLines;

        void OnRectTransformDimensionsChange()
        {
            if (_text == null) return;
            if (_autoLines) MaxLines = TextFit.LinesFor(_text.rectTransform.rect.height, MinSize);
            _shown = null;
            MarkDirty();
        }

        void Hook()
        {
            if (_text == null) return;
            _text.UnregisterDirtyLayoutCallback(MarkDirty);
            _text.RegisterDirtyLayoutCallback(MarkDirty);
            // before the UI rebuild of this frame (willRenderCanvases runs the rebuild), so a changed text never shows uncut
            if (!_hooked) { Canvas.preWillRenderCanvases += Flush; _hooked = true; }
        }

        /// <summary>While true the rule waits (a caller measuring the full words to size the box).</summary>
        public bool Paused;

        /// <summary>
        /// The one-line width of a label's full words at its current size, ignoring any cut (for code that sizes a box to
        /// its words: the box grows first, then the rule fits what is left). Leaves the full words on the label.
        /// </summary>
        public static float NaturalWidth(Text t)
        {
            ContainedText c = Of(t);
            if (c == null) return t.preferredWidth;
            string full = c._shown != null && t.text == c._shown ? c.Source : t.text;
            c.Paused = true;
            HorizontalWrapMode mode = t.horizontalOverflow;
            try
            {
                if (t.text != full) t.text = full;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                float w = t.preferredWidth;
                t.horizontalOverflow = mode;
                return w;
            }
            finally { c.Paused = false; c.Source = full; c._shown = null; if (Application.isPlaying) c.MarkDirty(); }   // edit mode: the caller sizes the box to the full words
        }

        void MarkDirty()
        {
            if (_applying || Paused) return;
            // Edit mode (layout tests, capture tools) has no canvas render loop: apply at once so reads see the rule's result.
            if (!Application.isPlaying) { ApplyNow(); return; }
            if (!Dirty.Contains(this)) Dirty.Add(this);
        }

        static void Flush()
        {
            for (int i = 0; i < Dirty.Count; i++)
            {
                ContainedText c = Dirty[i];
                if (c != null && c.isActiveAndEnabled) c.ApplyNow();
            }
            Dirty.Clear();
        }

        static bool IsCutOf(string shown, string source) =>
            shown.Length < source.Length + 3 && (shown[shown.Length - 1] == TextFit.Ellipsis || shown.EndsWith("...")) &&
            source.StartsWith(shown.TrimEnd('.', TextFit.Ellipsis).TrimEnd());

        /// <summary>Runs the rule again for the same words (after a size range change).</summary>
        public void Reapply()
        {
            if (_text == null) return;
            _shown = null;
            _text.text = Source;
            ApplyNow();
        }

        /// <summary>Runs the rule now (tests call it; the HUD lets Flush call it once per change).</summary>
        public void ApplyNow()
        {
            if (_text == null) return;
            string now = _text.text ?? string.Empty;
            if (_shown != null && ReferenceEquals(now, _shown) && _text.fontSize == _size) return;   // our own result, nothing new
            // the game set a new size itself (a story caption, a scaled overlay): that size becomes the rule's top
            if (_size > 0 && _text.fontSize != _size) { MaxSize = _text.fontSize; if (MinSize > MaxSize) MinSize = MaxSize; }
            if (_shown != null && now != _shown) Source = now;
            else if (_shown == null && now != Source && !(Source.Length > 0 && now.Length > 0 && IsCutOf(now, Source))) Source = now;
            _applying = true;
            try { _shown = TextFit.Apply(_text, Source, MaxSize, MinSize, MaxLines); _size = _text.fontSize; }
            finally { _applying = false; }
        }
    }
}

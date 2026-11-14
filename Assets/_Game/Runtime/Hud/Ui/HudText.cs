using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// Sticker outline for text: copies the glyph quads around a circle of Radius canvas units in OutlineColor under the
    /// face (12 copies for a 3 px outline). The font is a dynamic TTF, rasterised by uGUI at fontSize x canvas scale, so text
    /// stays sharp at every resolution.
    /// </summary>
    public sealed class TextOutline : BaseMeshEffect
    {
        public float Radius = CMTokens.OutlinePx;
        public Color OutlineColor = CMTokens.VoidNavy.Color;
        public int Directions = 12;
        static readonly List<UIVertex> Verts = new List<UIVertex>(4096);
        static readonly List<UIVertex> Out = new List<UIVertex>(4096 * 13);

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Radius <= 0) return;
            Verts.Clear();
            vh.GetUIVertexStream(Verts);
            Out.Clear();
            for (int d = 0; d < Directions; d++)
            {
                float a = Mathf.PI * 2f * d / Directions;
                Vector3 off = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * Radius;
                for (int i = 0; i < Verts.Count; i++)
                {
                    UIVertex v = Verts[i];
                    v.position += off;
                    Color32 c = OutlineColor;
                    c.a = (byte)(c.a * v.color.a / 255);
                    v.color = c;
                    Out.Add(v);
                }
            }
            Out.AddRange(Verts);
            vh.Clear();
            vh.AddUIVertexTriangleStream(Out);
        }
    }

    /// <summary>Text factory for the HUD and title screen.</summary>
    public static class HudText
    {
        public static Text Make(Transform parent, string name, Typeface face, int size, Color color, TextAnchor anchor,
            float outline = CMTokens.OutlinePx)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = CMTokens.Font(face);
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = false;
            if (outline > 0)
            {
                TextOutline o = go.AddComponent<TextOutline>();
                o.Radius = outline;
            }
            return t;
        }

        /// <summary>
        /// One line, shrunk from max to at most min until it fits the label's width (batch 4: scoreboard names ran into the
        /// next column and "LADDER AND BRIDGE" wrapped and was cut). Work only when the text changes.
        /// </summary>
        public static void SetOneLine(Text t, string s, int max, int min)
        {
            if (t == null) return;
            if (s == null) s = string.Empty;
            if (!t.resizeTextForBestFit && t.horizontalOverflow == HorizontalWrapMode.Overflow && t.text == s) return;
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float w = t.rectTransform.rect.width;
            var key = (s, max, min, Mathf.RoundToInt(w), t.font != null ? t.font.GetInstanceID() : 0);
            if (OneLineSizes.TryGetValue(key, out var known))   // no layout pass, no allocation
            {
                if (t.fontSize != known.Item1) t.fontSize = known.Item1;
                Set(t, known.Item2);   // the line as fitted (cut with an ellipsis when even the floor was too wide)
                return;
            }
            t.text = s;
            int size = max;
            t.fontSize = size;
            if (w > 1f) while (size > min && t.preferredWidth > w) t.fontSize = --size;
            // hud-overlaps-1: at the floor a line that still does not fit is cut with an ellipsis instead of running
            // past its box (the map-event pill ran under its clock: "EVERY DELIVERY COUNTS DOUB0:10").
            string shown = w > 1f ? FitWithEllipsis(t, s, w) : s;
            OneLineSizes[key] = (size, shown);
        }

        static readonly Dictionary<(string, int, int, int, int), (int, string)> OneLineSizes = new Dictionary<(string, int, int, int, int), (int, string)>();

        /// <summary>
        /// At the label's current size: sets s if it fits width w, else the longest start of s plus an ellipsis that
        /// fits (hud-overlaps-1). Returns the text set. Measures, so call it only when the text or the box changes.
        /// </summary>
        public static string FitWithEllipsis(Text t, string s, float w)
        {
            if (s == null) s = string.Empty;
            t.text = s;
            if (s.Length == 0 || t.preferredWidth <= w + 0.5f) return s;
            string dots = t.font != null && t.font.HasCharacter('…') ? "…" : "...";
            for (int n = s.Length - 1; n > 0; n--)
            {
                string cut = s.Substring(0, n).TrimEnd() + dots;
                t.text = cut;
                if (t.preferredWidth <= w + 0.5f) return cut;
            }
            t.text = dots;
            return dots;
        }

        /// <summary>Sets text only when it changed (no mesh rebuild, no allocation on equal strings).</summary>
        public static void Set(Text t, string s)
        {
            if (!ReferenceEquals(t.text, s) && t.text != s) t.text = s;
        }
    }

    /// <summary>
    /// Cached number strings so per-frame HUD updates never format (allocation-free): integers 0..9999 and mm:ss for
    /// 0..5999 seconds, built once.
    /// </summary>
    public static class NumberCache
    {
        static string[] _ints;
        static string[] _mmss;

        public static string Int(int v)
        {
            if (_ints == null)
            {
                _ints = new string[10000];
                for (int i = 0; i < _ints.Length; i++) _ints[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (v < 0) v = 0;
            return v < _ints.Length ? _ints[v] : _ints[_ints.Length - 1];
        }

        /// <summary>mm:ss rounding up (DebugHud.Mmss rule).</summary>
        public static string Mmss(double seconds)
        {
            if (_mmss == null)
            {
                _mmss = new string[6000];
                for (int i = 0; i < _mmss.Length; i++) _mmss[i] = (i / 60).ToString("00") + ":" + (i % 60).ToString("00");
            }
            double clamped = double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
            int t = (int)System.Math.Ceiling(clamped - 1e-6);
            if (t < 0) t = 0;
            if (t >= _mmss.Length) t = _mmss.Length - 1;
            return _mmss[t];
        }
    }
}

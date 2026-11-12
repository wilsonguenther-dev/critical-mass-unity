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

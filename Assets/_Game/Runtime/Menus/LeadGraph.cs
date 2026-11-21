using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// b15 item 1: the results screen's lead graph. Both teams' hill seconds over the match (MatchTimeline samples), Azure as
    /// a solid line and Crimson as a dashed one, so the two read apart without colour (colour-blind palette or not: the
    /// colours come from CMTokens.Team, which follows the setting). Marks: the Kaiju hatch (a diamond at the top and a
    /// thin vertical rule), hut levels (squares along the bottom, Azure's row under Crimson's), lead changes (a ring on the
    /// new leader's line). Faint guide lines every quarter of the height and every minute. One mesh, built when the data is
    /// set, never per frame.
    /// </summary>
    // b15 Editor review: a custom Graphic gets no CanvasRenderer of its own (only Image-style classes require one), and in
    // the Editor GetComponent hands Graphic a fake null, so the graph and its swatches drew nothing (the STORY tab was empty).
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LeadGraph : MaskableGraphic
    {
        public const float LineW = 4f, DashOn = 12f, DashOff = 7f, MarkSize = 16f, HutSize = 10f;

        public Color AzureColor = Color.blue, CrimsonColor = Color.red;
        public Color GuideColor = new Color(1f, 1f, 1f, 0.12f), RuleColor = new Color(1f, 1f, 1f, 0.35f), Ink = new Color(0f, 0f, 0f, 1f);

        MatchTimeline _tl;
        double _maxY = 1, _maxT = 480;

        /// <summary>The timeline drawn (null = empty graph with guides only).</summary>
        public MatchTimeline Timeline => _tl;
        public double MaxY => _maxY;
        public double MaxT => _maxT;

        public void SetData(MatchTimeline tl)
        {
            _tl = tl;
            _maxT = tl != null ? System.Math.Max(tl.MatchSeconds, tl.Count > 0 ? tl.T[tl.Count - 1] : 0) : 480;
            if (_maxT <= 0) _maxT = 480;
            _maxY = NiceCeiling(tl != null ? tl.MaxSeconds() : 0);
            SetVerticesDirty();
        }

        /// <summary>The y-axis top: the largest hill total rounded up to a multiple of 30 s (at least 30).</summary>
        public static double NiceCeiling(double v) => System.Math.Max(30, System.Math.Ceiling(v / 30.0) * 30.0);

        /// <summary>Graph point of (t, seconds) inside r (y up).</summary>
        public static Vector2 Point(Rect r, double t, double secs, double maxT, double maxY) =>
            new Vector2(r.xMin + (float)(System.Math.Max(0, System.Math.Min(1, t / maxT)) * r.width),
                r.yMin + (float)(System.Math.Max(0, System.Math.Min(1, secs / maxY)) * r.height));

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            for (int q = 0; q <= 4; q++) Line(vh, new Vector2(r.xMin, r.yMin + r.height * q / 4f), new Vector2(r.xMax, r.yMin + r.height * q / 4f), q == 0 ? 2f : 1f, q == 0 ? RuleColor : GuideColor);
            for (int m = 60; m < _maxT; m += 60)
            {
                float x = r.xMin + (float)(m / _maxT) * r.width;
                Line(vh, new Vector2(x, r.yMin), new Vector2(x, r.yMax), 1f, GuideColor);
            }
            if (_tl == null || _tl.Count == 0) return;
            // the hatch rule sits under the lines
            for (int i = 0; i < _tl.MarkCount; i++)
            {
                TimelineMark mk = _tl.Marks[i];
                if (mk.Kind != TimelineMarkKind.Hatch) continue;
                Vector2 p = Point(r, mk.Time, 0, _maxT, _maxY);
                Line(vh, new Vector2(p.x, r.yMin), new Vector2(p.x, r.yMax), 2f, RuleColor);
            }
            Polyline(vh, r, _tl.Crimson, CrimsonColor, true);
            Polyline(vh, r, _tl.Azure, AzureColor, false);
            for (int i = 0; i < _tl.MarkCount; i++)
            {
                TimelineMark mk = _tl.Marks[i];
                switch (mk.Kind)
                {
                    case TimelineMarkKind.Hatch:
                    {
                        Vector2 p = Point(r, mk.Time, _maxY, _maxT, _maxY);
                        Diamond(vh, p, MarkSize + 4f, Ink);
                        Diamond(vh, p, MarkSize, HatchColor(mk.Code));
                        break;
                    }
                    case TimelineMarkKind.HutLevel:
                    {
                        Vector2 p = Point(r, mk.Time, 0, _maxT, _maxY);
                        p.y = r.yMin + (mk.Team == TeamId.Azure ? HutSize : HutSize * 2.6f);
                        Square(vh, p, HutSize + 4f, Ink);
                        Square(vh, p, HutSize, mk.Team == TeamId.Azure ? AzureColor : CrimsonColor);
                        break;
                    }
                    case TimelineMarkKind.LeadChange:
                    {
                        double v = mk.Team == TeamId.Azure ? mk.Azure : mk.Crimson;
                        Vector2 p = Point(r, mk.Time, v, _maxT, _maxY);
                        Ring(vh, p, MarkSize * 0.5f + 2f, Ink, 12);
                        Ring(vh, p, MarkSize * 0.5f, mk.Team == TeamId.Azure ? AzureColor : CrimsonColor, 12);
                        break;
                    }
                }
            }
        }

        Color HatchColor(int code) =>
            code == (int)KaijuState.Azure ? AzureColor : code == (int)KaijuState.Crimson ? CrimsonColor
            : code == (int)KaijuState.Berserk ? CMTokens.Corruption.Color : CMTokens.WarmCream.Color;

        void Polyline(VertexHelper vh, Rect r, float[] ys, Color c, bool dashed)
        {
            float carry = 0f;
            bool on = true;
            for (int i = 1; i < _tl.Count; i++)
            {
                Vector2 a = Point(r, _tl.T[i - 1], ys[i - 1], _maxT, _maxY), b = Point(r, _tl.T[i], ys[i], _maxT, _maxY);
                if (!dashed) { Line(vh, a, b, LineW, c); continue; }
                float len = Vector2.Distance(a, b), pos = 0f;
                while (pos < len)
                {
                    float left = (on ? DashOn : DashOff) - carry, step = Mathf.Min(left, len - pos);
                    if (on && step > 0.01f) Line(vh, Vector2.Lerp(a, b, pos / len), Vector2.Lerp(a, b, (pos + step) / len), LineW, c);
                    pos += step;
                    carry += step;
                    if (carry >= (on ? DashOn : DashOff) - 0.001f) { carry = 0f; on = !on; }
                }
            }
        }

        // ---- primitives (shared with GraphSwatch) ----

        internal static void Line(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            // a little overlap along the segment hides the joints of a polyline
            Vector2 e = d.normalized * (w * 0.25f);
            Quad(vh, a - e - n, a - e + n, b + e + n, b + e - n, c);
        }

        internal static void Quad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(p0, c, Vector4.zero);
            vh.AddVert(p1, c, Vector4.zero);
            vh.AddVert(p2, c, Vector4.zero);
            vh.AddVert(p3, c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        internal static void Diamond(VertexHelper vh, Vector2 p, float s, Color c)
        {
            float h = s * 0.5f;
            Quad(vh, p + new Vector2(0, -h), p + new Vector2(-h, 0), p + new Vector2(0, h), p + new Vector2(h, 0), c);
        }

        internal static void Square(VertexHelper vh, Vector2 p, float s, Color c)
        {
            float h = s * 0.5f;
            Quad(vh, p + new Vector2(-h, -h), p + new Vector2(-h, h), p + new Vector2(h, h), p + new Vector2(h, -h), c);
        }

        internal static void Ring(VertexHelper vh, Vector2 p, float radius, Color c, int seg)
        {
            int start = vh.currentVertCount;
            vh.AddVert(p, c, Vector4.zero);
            for (int k = 0; k <= seg; k++)
            {
                float ang = k * Mathf.PI * 2f / seg;
                vh.AddVert(p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius, c, Vector4.zero);
                if (k > 0) vh.AddTriangle(start, start + k, start + k + 1);
            }
        }
    }

    /// <summary>b15 item 1: one legend swatch of the lead graph (the same line styles and mark shapes).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class GraphSwatch : MaskableGraphic
    {
        public enum Style { Solid, Dashed, Diamond, Square, Circle }
        public Style Kind;
        public Color Ink = new Color(0f, 0f, 0f, 1f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 c = r.center;
            switch (Kind)
            {
                case Style.Solid:
                    LeadGraph.Line(vh, new Vector2(r.xMin, c.y), new Vector2(r.xMax, c.y), LeadGraph.LineW, color);
                    break;
                case Style.Dashed:
                    for (float x = r.xMin; x < r.xMax; x += LeadGraph.DashOn + LeadGraph.DashOff)
                        LeadGraph.Line(vh, new Vector2(x, c.y), new Vector2(Mathf.Min(r.xMax, x + LeadGraph.DashOn), c.y), LeadGraph.LineW, color);
                    break;
                case Style.Diamond:
                    LeadGraph.Diamond(vh, c, LeadGraph.MarkSize + 4f, Ink);
                    LeadGraph.Diamond(vh, c, LeadGraph.MarkSize, color);
                    break;
                case Style.Square:
                    LeadGraph.Square(vh, c, LeadGraph.HutSize + 4f, Ink);
                    LeadGraph.Square(vh, c, LeadGraph.HutSize, color);
                    break;
                case Style.Circle:
                    LeadGraph.Ring(vh, c, LeadGraph.MarkSize * 0.5f + 2f, Ink, 12);
                    LeadGraph.Ring(vh, c, LeadGraph.MarkSize * 0.5f, color, 12);
                    break;
            }
        }
    }
}

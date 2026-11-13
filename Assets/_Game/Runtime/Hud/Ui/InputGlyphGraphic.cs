using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// Draws one input glyph in code, in the HUD's sticker style (chunky Void Navy outline, flat fills, Tool Brass for the
    /// highlighted part): a mouse with its left, right or middle button or wheel lit; a keycap with a darker lip (the
    /// arrow keys get a drawn arrow); a round controller face button in its own colour; bumper and trigger tags; a stick
    /// seen from above (a press adds a ring); the d-pad with one arm lit; the small centre buttons. Letters on the glyph
    /// are a Text child (InputGlyphView). The mesh is rebuilt only when the glyph changes (SetVerticesDirty); outline
    /// widths follow the glyph's size so a 24 px glyph is not all outline. Uses VectorShape's outline builders.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InputGlyphGraphic : MaskableGraphic
    {
        GlyphKind _kind;
        GlyphMark _mark;
        Color _face = Color.white;

        public GlyphKind Kind => _kind;
        public GlyphMark Mark => _mark;

        public static Color Navy => CMTokens.VoidNavy.Color;
        public static Color Lit => CMTokens.ToolBrass.Color;

        /// <summary>Sets what to draw; rebuilds the mesh only on a change.</summary>
        public void Set(GlyphKind kind, GlyphMark mark, Color face)
        {
            if (kind == _kind && mark == _mark && face == _face) return;
            _kind = kind;
            _mark = mark;
            _face = face;
            SetVerticesDirty();
        }

        /// <summary>Where the cap letters sit inside the glyph's rect (0..1 of width and height), per kind.</summary>
        public static Rect CapRect(GlyphKind kind)
        {
            switch (kind)
            {
                case GlyphKind.Key: return new Rect(0.06f, 0.2f, 0.88f, 0.76f);
                case GlyphKind.Trigger: return new Rect(0f, 0f, 1f, 0.82f);
                default: return new Rect(0f, 0f, 1f, 1f);
            }
        }

        static readonly List<Vector2> A = new List<Vector2>(128);
        static readonly List<Vector2> B = new List<Vector2>(128);
        static readonly List<Vector2> C = new List<Vector2>(128);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (r.width < 2f || r.height < 2f || _kind == GlyphKind.None) return;
            float s = Mathf.Min(r.width, r.height);
            float o = Mathf.Clamp(s * 0.09f, 1.5f, CMTokens.OutlinePx);
            Color tint = color;
            Color navy = Navy * tint, face = _face * tint, lit = Lit * tint;
            switch (_kind)
            {
                case GlyphKind.Key: DrawKey(vh, r, o, navy, face, lit); break;
                case GlyphKind.Mouse: DrawMouse(vh, r, o, navy, face, lit); break;
                case GlyphKind.FaceButton:
                {
                    Rect c = Square(r);
                    Shape(vh, ShapeKind.Circle, c, 0f, navy, face, o);
                    break;
                }
                case GlyphKind.Bumper:
                {
                    Rect b = new Rect(r.xMin, r.yMin + r.height * 0.18f, r.width, r.height * 0.64f);
                    Shape(vh, ShapeKind.RoundRect, b, b.height * 0.5f, navy, face, o);
                    break;
                }
                case GlyphKind.Trigger: DrawTrigger(vh, r, o, navy, face); break;
                case GlyphKind.Stick: DrawStick(vh, r, o, navy, face, lit); break;
                case GlyphKind.DPad: DrawDPad(vh, r, o, navy, face, lit); break;
                case GlyphKind.SmallButton:
                {
                    Rect b = new Rect(r.xMin, r.yMin + r.height * 0.12f, r.width, r.height * 0.76f);
                    Shape(vh, ShapeKind.RoundRect, b, b.height * 0.5f, navy, face, o);
                    break;
                }
            }
        }

        static Rect Square(Rect r)
        {
            float s = Mathf.Min(r.width, r.height);
            return new Rect(r.center.x - s * 0.5f, r.center.y - s * 0.5f, s, s);
        }

        // ---------------------------------------------------------------- glyphs

        void DrawKey(VertexHelper vh, Rect r, float o, Color navy, Color face, Color lit)
        {
            float rad = Mathf.Min(r.height * 0.22f, 8f);
            // Body (darker lip below), then the key face raised on it.
            Shape(vh, ShapeKind.RoundRect, r, rad, navy, CMTokens.Stone.Color * new Color(1, 1, 1, face.a), o);
            Rect top = new Rect(r.xMin + o, r.yMin + r.height * 0.2f, r.width - 2f * o, r.height * 0.8f - o);
            BuildOutline(ShapeKind.RoundRect, top, Mathf.Max(1f, rad - o), A);
            Fan(vh, A, top.center, face);
            if (IsArrow(_mark)) DrawArrow(vh, r, _mark, navy);
        }

        static bool IsArrow(GlyphMark m) => m == GlyphMark.Up || m == GlyphMark.Down || m == GlyphMark.Left || m == GlyphMark.Right;

        void DrawMouse(VertexHelper vh, Rect r, float o, Color navy, Color face, Color lit)
        {
            // A mouse is taller than wide: 0.7 : 1 inside the rect.
            float h = r.height, w = Mathf.Min(r.width, h * 0.72f);
            Rect body = new Rect(r.center.x - w * 0.5f, r.yMin, w, h);
            Vector2 c = BuildOutline(ShapeKind.RoundRect, body, w * 0.5f, A);
            Fan(vh, A, c, navy);
            Offset(A, -o, B);
            Fan(vh, B, body.center, face);
            float midY = body.yMin + body.height * 0.55f;
            float cx = body.center.x;
            GlyphMark m = _mark;
            if (m == GlyphMark.Left || m == GlyphMark.Right)
            {
                // The lit button: the inner body clipped to the top half, on one side of the centre line.
                ClipHalf(B, midY, true, C);
                ClipSide(C, cx, m == GlyphMark.Left, A);
                if (A.Count >= 3) Fan(vh, A, Centroid(A), lit);
            }
            // Seams: the button split and the line between the buttons and the palm.
            float t = Mathf.Max(1f, o * 0.6f);
            Bar(vh, new Rect(cx - t * 0.5f, midY, t, body.yMax - midY), navy);
            Bar(vh, new Rect(body.xMin + o * 0.5f, midY - t * 0.5f, body.width - o, t), navy);
            // The wheel: lit for Middle and Wheel; Wheel adds the up / down ticks.
            float ww = Mathf.Max(3f, w * 0.2f), wh = body.height * 0.2f;
            Rect wheel = new Rect(cx - ww * 0.5f, midY + body.height * 0.08f, ww, wh);
            bool wheelLit = m == GlyphMark.Middle || m == GlyphMark.Wheel;
            Shape(vh, ShapeKind.RoundRect, wheel, ww * 0.5f, navy, wheelLit ? lit : face, Mathf.Max(1f, o * 0.6f));
            if (m == GlyphMark.Wheel)
            {
                float a = ww * 0.9f;
                Tri(vh, new Vector2(cx, wheel.yMax + a * 1.3f), new Vector2(cx - a, wheel.yMax + a * 0.35f), new Vector2(cx + a, wheel.yMax + a * 0.35f), navy);
                Tri(vh, new Vector2(cx, wheel.yMin - a * 1.3f), new Vector2(cx + a, wheel.yMin - a * 0.35f), new Vector2(cx - a, wheel.yMin - a * 0.35f), navy);
            }
        }

        static void DrawTrigger(VertexHelper vh, Rect r, float o, Color navy, Color face)
        {
            // A tag rounded on top and flat below: a round rect with a square block over its lower corners.
            float rad = r.width * 0.42f;
            Rect b = new Rect(r.xMin + r.width * 0.08f, r.yMin, r.width * 0.84f, r.height);
            Shape(vh, ShapeKind.RoundRect, b, rad, navy, face, o);
            Rect low = new Rect(b.xMin, b.yMin, b.width, Mathf.Min(rad, b.height * 0.4f));
            Bar(vh, low, navy);
            Bar(vh, new Rect(low.xMin + o, low.yMin + o, low.width - 2f * o, low.height), face);
        }

        void DrawStick(VertexHelper vh, Rect r, float o, Color navy, Color face, Color lit)
        {
            Rect c = Square(r);
            bool press = _mark == GlyphMark.Press;
            Shape(vh, ShapeKind.Circle, c, 0f, navy, press ? lit : CMTokens.PanelNavy.Color, o);
            float k = 0.66f;
            Rect cap = new Rect(c.center.x - c.width * k * 0.5f, c.center.y - c.height * k * 0.5f, c.width * k, c.height * k);
            Shape(vh, ShapeKind.Circle, cap, 0f, navy, face, o);
        }

        void DrawDPad(VertexHelper vh, Rect r, float o, Color navy, Color face, Color lit)
        {
            Rect c = Square(r);
            float arm = c.width * 0.36f;
            Rect hz = new Rect(c.xMin, c.center.y - arm * 0.5f, c.width, arm);
            Rect vt = new Rect(c.center.x - arm * 0.5f, c.yMin, arm, c.height);
            float rad = arm * 0.22f;
            BuildOutline(ShapeKind.RoundRect, hz, rad, A);
            Fan(vh, A, hz.center, navy);
            BuildOutline(ShapeKind.RoundRect, vt, rad, A);
            Fan(vh, A, vt.center, navy);
            Rect hzi = Inset(hz, o), vti = Inset(vt, o);
            BuildOutline(ShapeKind.RoundRect, hzi, Mathf.Max(1f, rad - o), A);
            Fan(vh, A, hzi.center, face);
            BuildOutline(ShapeKind.RoundRect, vti, Mathf.Max(1f, rad - o), A);
            Fan(vh, A, vti.center, face);
            float half = (c.width - arm) * 0.5f - o;
            switch (_mark)
            {
                case GlyphMark.Up: Bar(vh, new Rect(vti.xMin, c.center.y + arm * 0.5f, vti.width, half), lit); break;
                case GlyphMark.Down: Bar(vh, new Rect(vti.xMin, c.yMin + o, vti.width, half), lit); break;
                case GlyphMark.Left: Bar(vh, new Rect(c.xMin + o, hzi.yMin, half, hzi.height), lit); break;
                case GlyphMark.Right: Bar(vh, new Rect(c.center.x + arm * 0.5f, hzi.yMin, half, hzi.height), lit); break;
            }
        }

        static Rect Inset(Rect r, float d) => new Rect(r.xMin + d, r.yMin + d, Mathf.Max(0.5f, r.width - 2f * d), Mathf.Max(0.5f, r.height - 2f * d));

        /// <summary>The arrow of an arrow key, drawn on the key face.</summary>
        static void DrawArrow(VertexHelper vh, Rect r, GlyphMark m, Color navy)
        {
            Rect top = new Rect(r.xMin, r.yMin + r.height * 0.2f, r.width, r.height * 0.8f);
            Vector2 c = top.center;
            float a = Mathf.Min(top.width, top.height) * 0.26f;
            Vector2 dir, side;
            switch (m)
            {
                case GlyphMark.Up: dir = Vector2.up; break;
                case GlyphMark.Down: dir = Vector2.down; break;
                case GlyphMark.Left: dir = Vector2.left; break;
                default: dir = Vector2.right; break;
            }
            side = new Vector2(-dir.y, dir.x);
            Tri(vh, c + dir * a, c - dir * a * 0.6f + side * a, c - dir * a * 0.6f - side * a, navy);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }

        // ---------------------------------------------------------------- geometry helpers

        static Vector2 BuildOutline(ShapeKind kind, Rect r, float radius, List<Vector2> pts) => VectorShape.BuildOutline(kind, r, radius, pts);

        static void Offset(List<Vector2> src, float d, List<Vector2> dst) => VectorShape.Offset(src, d, dst);

        /// <summary>A shape with a Void Navy outline of width o and a flat fill.</summary>
        static void Shape(VertexHelper vh, ShapeKind kind, Rect r, float radius, Color outline, Color fill, float o)
        {
            Vector2 c = VectorShape.BuildOutline(kind, r, radius, A);
            Fan(vh, A, c, outline);
            VectorShape.Offset(A, -o, B);
            Fan(vh, B, c, fill);
        }

        static void Fan(VertexHelper vh, List<Vector2> poly, Vector2 centre, Color c)
        {
            if (poly.Count < 3) return;
            int start = vh.currentVertCount;
            vh.AddVert(centre, c, Vector4.zero);
            for (int i = 0; i < poly.Count; i++) vh.AddVert(poly[i], c, Vector4.zero);
            for (int i = 0; i < poly.Count; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % poly.Count);
        }

        static void Bar(VertexHelper vh, Rect r, Color c)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            int s = vh.currentVertCount;
            vh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), c, Vector4.zero);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }

        static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int s = vh.currentVertCount;
            vh.AddVert(a, col, Vector4.zero);
            vh.AddVert(b, col, Vector4.zero);
            vh.AddVert(c, col, Vector4.zero);
            vh.AddTriangle(s, s + 1, s + 2);
        }

        /// <summary>Keeps the part of a polygon above (top = true) or below y.</summary>
        static void ClipHalf(List<Vector2> src, float y, bool top, List<Vector2> dst)
        {
            dst.Clear();
            int n = src.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = src[i], b = src[(i + 1) % n];
                bool ain = top ? a.y >= y : a.y <= y, bin = top ? b.y >= y : b.y <= y;
                if (ain) dst.Add(a);
                if (ain != bin) dst.Add(Vector2.Lerp(a, b, (y - a.y) / (b.y - a.y)));
            }
        }

        /// <summary>Keeps the part of a polygon left (left = true) or right of x.</summary>
        static void ClipSide(List<Vector2> src, float x, bool left, List<Vector2> dst)
        {
            dst.Clear();
            int n = src.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = src[i], b = src[(i + 1) % n];
                bool ain = left ? a.x <= x : a.x >= x, bin = left ? b.x <= x : b.x >= x;
                if (ain) dst.Add(a);
                if (ain != bin) dst.Add(Vector2.Lerp(a, b, (x - a.x) / (b.x - a.x)));
            }
        }

        static Vector2 Centroid(List<Vector2> p)
        {
            Vector2 s = Vector2.zero;
            for (int i = 0; i < p.Count; i++) s += p[i];
            return s / Mathf.Max(1, p.Count);
        }
    }
}

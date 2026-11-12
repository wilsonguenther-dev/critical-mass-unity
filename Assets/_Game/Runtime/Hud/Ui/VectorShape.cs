using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>Outline shapes a VectorShape can draw (all star-shaped around their fan centre).</summary>
    public enum ShapeKind
    {
        RoundRect, Circle, Chamfer, Shield, Heart, TriangleDown, TriangleRight, Diamond, Crown, Egg, Hexagon, Pie, Banner,
    }

    /// <summary>
    /// A uGUI graphic that tessellates a vector outline at the canvas's live scale, so it is sharp at 720p, 1080p and 4K
    /// (no sprites, no scaling of pixels). Sticker craft (art direction 6.5): flat fill, a Void Navy outer outline
    /// (OutlinePx, 3 px at 1080p) and a Warm Cream inner rim (RimPx, 2 px), drawn as rings so a hollow shape stays hollow,
    /// plus a one-device-pixel alpha fringe outside for anti-aliasing. Fill01 cuts the fill horizontally (bars) and
    /// EmptyColor paints the uncovered part. The mesh is rebuilt only when a property changes (SetVerticesDirty).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class VectorShape : MaskableGraphic
    {
        public ShapeKind Kind = ShapeKind.RoundRect;
        /// <summary>Corner radius (RoundRect) or chamfer size (Chamfer), canvas units; -1 = pill (half the height).</summary>
        public float Radius = 8f;
        public Color FillColor = Color.white;
        public Color EmptyColor = new Color(0, 0, 0, 0);
        public float OutlinePx = CMTokens.OutlinePx;
        public float RimPx = CMTokens.RimPx;
        public Color OutlineColor = CMTokens.VoidNavy.Color;
        public Color RimColor = CMTokens.WarmCream.Color;
        float _fill01 = 1f;
        /// <summary>Pie: swept fraction clockwise from 12 o'clock. Other kinds: horizontal fill fraction.</summary>
        public float Fill01
        {
            get => _fill01;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Abs(value - _fill01) < 0.0005f) return;
                _fill01 = value;
                SetVerticesDirty();
            }
        }

        public void SetRim(Color c)
        {
            if (c == RimColor) return;
            RimColor = c;
            SetVerticesDirty();
        }

        public void SetEmpty(Color c)
        {
            if (c == EmptyColor) return;
            EmptyColor = c;
            SetVerticesDirty();
        }

        public void SetFill(Color c)
        {
            if (c == FillColor) return;
            FillColor = c;
            SetVerticesDirty();
        }

        static readonly List<Vector2> Outer = new List<Vector2>(256);
        static readonly List<Vector2> Mid = new List<Vector2>(256);
        static readonly List<Vector2> Inner = new List<Vector2>(256);
        static readonly List<Vector2> Fringe = new List<Vector2>(256);
        static readonly List<Vector2> Clip = new List<Vector2>(256);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (r.width <= 0.01f || r.height <= 0.01f) return;
            float devicePx = canvas != null && canvas.scaleFactor > 0 ? 1f / canvas.scaleFactor : 1f;
            Color tint = color;

            if (Kind == ShapeKind.Pie)
            {
                BuildPie(vh, r, _fill01, FillColor * tint);
                return;
            }

            Vector2 centre = BuildOutline(Kind, r, Radius, Outer);
            float maxInset = Mathf.Min(r.width, r.height) * 0.32f;
            float o = Mathf.Min(OutlinePx, maxInset);
            float rim = Mathf.Min(RimPx, Mathf.Max(0, maxInset - o));
            Offset(Outer, -o, Mid);
            Offset(Outer, -(o + rim), Inner);
            Offset(Outer, devicePx, Fringe);

            Color oc = OutlineColor * tint;
            Color clear = oc;
            clear.a = 0;
            if (o > 0)
            {
                Ring(vh, Fringe, Outer, clear, oc);
                Ring(vh, Outer, Mid, oc, oc);
            }
            else
            {
                Ring(vh, Fringe, Outer, (FillColor * tint) * new Color(1, 1, 1, 0), FillColor * tint);
            }
            if (rim > 0) Ring(vh, Mid, Inner, RimColor * tint, RimColor * tint);

            List<Vector2> fillPoly = o > 0 || rim > 0 ? Inner : Outer;
            if (_fill01 >= 0.9995f)
            {
                Fan(vh, fillPoly, centre, FillColor * tint);
                return;
            }
            if (EmptyColor.a > 0) Fan(vh, fillPoly, centre, EmptyColor * tint);
            if (_fill01 <= 0.0005f) return;
            float cut = r.xMin + r.width * _fill01;
            ClipLeft(fillPoly, cut, Clip);
            if (Clip.Count >= 3) Fan(vh, Clip, Centroid(Clip), FillColor * tint);
        }

        // ---------------------------------------------------------------- outlines (counter-clockwise)

        /// <summary>Fills pts with the outer outline of kind in r (CCW) and returns the fan centre.</summary>
        public static Vector2 BuildOutline(ShapeKind kind, Rect r, float radius, List<Vector2> pts)
        {
            pts.Clear();
            switch (kind)
            {
                case ShapeKind.RoundRect:
                {
                    float rad = radius < 0 ? r.height * 0.5f : radius;
                    rad = Mathf.Min(rad, r.width * 0.5f, r.height * 0.5f);
                    int seg = rad > 12 ? 10 : 6;
                    Arc(pts, new Vector2(r.xMax - rad, r.yMin + rad), rad, -90, 0, seg);
                    Arc(pts, new Vector2(r.xMax - rad, r.yMax - rad), rad, 0, 90, seg);
                    Arc(pts, new Vector2(r.xMin + rad, r.yMax - rad), rad, 90, 180, seg);
                    Arc(pts, new Vector2(r.xMin + rad, r.yMin + rad), rad, 180, 270, seg);
                    return r.center;
                }
                case ShapeKind.Circle:
                    Ellipse(pts, r, 56, 1f);
                    return r.center;
                case ShapeKind.Egg:
                    Ellipse(pts, r, 48, 0.78f);
                    return new Vector2(r.center.x, r.yMin + r.height * 0.42f);
                case ShapeKind.Chamfer:
                {
                    float c = Mathf.Min(radius, r.width * 0.3f, r.height * 0.3f);
                    Add(pts, r.xMin + c, r.yMin); Add(pts, r.xMax - c, r.yMin); Add(pts, r.xMax, r.yMin + c);
                    Add(pts, r.xMax, r.yMax - c); Add(pts, r.xMax - c, r.yMax); Add(pts, r.xMin + c, r.yMax);
                    Add(pts, r.xMin, r.yMax - c); Add(pts, r.xMin, r.yMin + c);
                    return r.center;
                }
                case ShapeKind.Hexagon:
                    for (int i = 0; i < 6; i++)
                    {
                        float a = Mathf.Deg2Rad * (60 * i - 90 + 30);
                        Add(pts, r.center.x + Mathf.Cos(a) * r.width * 0.5f, r.center.y + Mathf.Sin(a) * r.height * 0.5f);
                    }
                    return r.center;
                case ShapeKind.Shield:
                    N(pts, r, 0.5f, 0f); N(pts, r, 0.95f, 0.38f); N(pts, r, 1f, 0.88f); N(pts, r, 0.9f, 1f);
                    N(pts, r, 0.5f, 0.92f); N(pts, r, 0.1f, 1f); N(pts, r, 0f, 0.88f); N(pts, r, 0.05f, 0.38f);
                    return new Vector2(r.center.x, r.yMin + r.height * 0.55f);
                case ShapeKind.Banner:
                    N(pts, r, 0f, 0.22f); N(pts, r, 0.5f, 0f); N(pts, r, 1f, 0.22f); N(pts, r, 1f, 1f); N(pts, r, 0f, 1f);
                    return new Vector2(r.center.x, r.yMin + r.height * 0.55f);
                case ShapeKind.Heart:
                    for (int i = 0; i < 48; i++)
                    {
                        float t = Mathf.PI * 2f * i / 48f - Mathf.PI;  // starts at the bottom tip, CCW
                        float x = 16f * Mathf.Pow(Mathf.Sin(t), 3);
                        float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2 * t) - 2f * Mathf.Cos(3 * t) - Mathf.Cos(4 * t);
                        N(pts, r, 0.5f + x / 34f, (y + 17.5f) / 30.5f);
                    }
                    pts.Reverse();
                    EnsureCcw(pts);
                    return new Vector2(r.center.x, r.yMin + r.height * 0.5f);
                case ShapeKind.TriangleDown:
                    N(pts, r, 0.5f, 0f); N(pts, r, 1f, 1f); N(pts, r, 0f, 1f);
                    return new Vector2(r.center.x, r.yMin + r.height * 0.6f);
                case ShapeKind.TriangleRight:
                    N(pts, r, 0f, 0f); N(pts, r, 1f, 0.5f); N(pts, r, 0f, 1f);
                    return new Vector2(r.xMin + r.width * 0.35f, r.center.y);
                case ShapeKind.Diamond:
                    N(pts, r, 0.5f, 0f); N(pts, r, 1f, 0.5f); N(pts, r, 0.5f, 1f); N(pts, r, 0f, 0.5f);
                    return r.center;
                case ShapeKind.Crown:
                    N(pts, r, 0.06f, 0f); N(pts, r, 0.94f, 0f); N(pts, r, 1f, 0.85f); N(pts, r, 0.72f, 0.5f);
                    N(pts, r, 0.5f, 0.97f); N(pts, r, 0.28f, 0.5f); N(pts, r, 0f, 0.85f);
                    return new Vector2(r.center.x, r.yMin + r.height * 0.12f);
            }
            return r.center;
        }

        static void N(List<Vector2> p, Rect r, float u, float v) => p.Add(new Vector2(r.xMin + u * r.width, r.yMin + v * r.height));
        static void Add(List<Vector2> p, float x, float y) => p.Add(new Vector2(x, y));

        static void Arc(List<Vector2> p, Vector2 c, float rad, float a0, float a1, int seg)
        {
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, i / (float)seg);
                p.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
            }
        }

        static void Ellipse(List<Vector2> p, Rect r, int seg, float topScale)
        {
            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                float s = Mathf.Sin(a);
                float k = s > 0 ? Mathf.Lerp(1f, topScale, s) : 1f;
                p.Add(new Vector2(r.center.x + Mathf.Cos(a) * r.width * 0.5f * k, r.center.y + s * r.height * 0.5f));
            }
        }

        static void EnsureCcw(List<Vector2> p)
        {
            float area = 0;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                area += a.x * b.y - b.x * a.y;
            }
            if (area < 0) p.Reverse();
        }

        /// <summary>Offsets a CCW polygon by d (positive = outward) with clamped miters.</summary>
        public static void Offset(List<Vector2> src, float d, List<Vector2> dst)
        {
            dst.Clear();
            int n = src.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = src[(i + n - 1) % n], cur = src[i], next = src[(i + 1) % n];
                Vector2 e0 = cur - prev, e1 = next - cur;
                if (e0.sqrMagnitude < 1e-8f) e0 = e1;
                if (e1.sqrMagnitude < 1e-8f) e1 = e0;
                Vector2 n0 = new Vector2(e0.y, -e0.x).normalized;  // outward for CCW
                Vector2 n1 = new Vector2(e1.y, -e1.x).normalized;
                Vector2 m = (n0 + n1);
                if (m.sqrMagnitude < 1e-8f) m = n1;
                m.Normalize();
                float cos = Mathf.Max(Vector2.Dot(m, n1), 0.4f);
                dst.Add(cur + m * (d / cos));
            }
        }

        static void ClipLeft(List<Vector2> src, float cutX, List<Vector2> dst)
        {
            dst.Clear();
            int n = src.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = src[i], b = src[(i + 1) % n];
                bool ain = a.x <= cutX, bin = b.x <= cutX;
                if (ain) dst.Add(a);
                if (ain != bin)
                {
                    float t = (cutX - a.x) / (b.x - a.x);
                    dst.Add(Vector2.Lerp(a, b, t));
                }
            }
        }

        static Vector2 Centroid(List<Vector2> p)
        {
            Vector2 s = Vector2.zero;
            for (int i = 0; i < p.Count; i++) s += p[i];
            return s / p.Count;
        }

        // ---------------------------------------------------------------- mesh

        static void Fan(VertexHelper vh, List<Vector2> poly, Vector2 centre, Color c)
        {
            int start = vh.currentVertCount;
            vh.AddVert(centre, c, Vector4.zero);
            for (int i = 0; i < poly.Count; i++) vh.AddVert(poly[i], c, Vector4.zero);
            for (int i = 0; i < poly.Count; i++)
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % poly.Count);
        }

        static void Ring(VertexHelper vh, List<Vector2> outer, List<Vector2> inner, Color co, Color ci)
        {
            int start = vh.currentVertCount;
            int n = outer.Count;
            for (int i = 0; i < n; i++)
            {
                vh.AddVert(outer[i], co, Vector4.zero);
                vh.AddVert(inner[i], ci, Vector4.zero);
            }
            for (int i = 0; i < n; i++)
            {
                int a = start + i * 2, b = start + ((i + 1) % n) * 2;
                vh.AddTriangle(a, b, a + 1);
                vh.AddTriangle(b, b + 1, a + 1);
            }
        }

        static void BuildPie(VertexHelper vh, Rect r, float f, Color c)
        {
            if (f <= 0.0005f) return;
            int seg = Mathf.Max(2, Mathf.CeilToInt(64 * f));
            int start = vh.currentVertCount;
            vh.AddVert(r.center, c, Vector4.zero);
            float rad = Mathf.Max(r.width, r.height);   // each ray stops at the rect edge
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * 0.5f - Mathf.PI * 2f * f * i / seg;  // clockwise from 12 o'clock
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float k = rad;
                if (Mathf.Abs(dir.x) > 1e-4f) k = Mathf.Min(k, r.width * 0.5f / Mathf.Abs(dir.x));
                if (Mathf.Abs(dir.y) > 1e-4f) k = Mathf.Min(k, r.height * 0.5f / Mathf.Abs(dir.y));
                vh.AddVert(r.center + dir * k, c, Vector4.zero);
            }
            for (int i = 0; i < seg; i++) vh.AddTriangle(start, start + 2 + i, start + 1 + i);
        }
    }
}

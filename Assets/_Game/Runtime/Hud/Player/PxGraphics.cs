using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// The small vector graphics of the player HUD (lane D). Each builds its mesh only when its rect or settings change;
    /// motion is RectTransform rotation/scale and fade is a CanvasGroup alpha, so a moving cue never re-tessellates.
    /// Vertex colour is white times the Graphic colour (set once), like every other HUD graphic.
    /// </summary>
    public static class PxMesh
    {
        public static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            int s = vh.currentVertCount;
            vh.AddVert(a, ca, Vector4.zero);
            vh.AddVert(b, cb, Vector4.zero);
            vh.AddVert(c, cc, Vector4.zero);
            vh.AddVert(d, cd, Vector4.zero);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }

        /// <summary>A stroke from p to q, width w, colour c.</summary>
        public static void Stroke(VertexHelper vh, Vector2 p, Vector2 q, float w, Color c)
        {
            Vector2 d = q - p;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            Quad(vh, p - n, p + n, q + n, q - n, c, c, c, c);
        }
    }

    /// <summary>A full-screen edge band (hurt vignette, low-health pulse): colour at the edge, clear Band in from it.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PxEdgeGlow : MaskableGraphic
    {
        /// <summary>Share of the half-screen the band reaches in from each edge.</summary>
        public float Band = 0.22f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Color edge = color, none = color;
            none.a = 0f;
            float ix = r.width * 0.5f * Band, iy = r.height * 0.5f * Band;
            Vector2 o0 = new Vector2(r.xMin, r.yMin), o1 = new Vector2(r.xMax, r.yMin), o2 = new Vector2(r.xMax, r.yMax), o3 = new Vector2(r.xMin, r.yMax);
            Vector2 i0 = new Vector2(r.xMin + ix, r.yMin + iy), i1 = new Vector2(r.xMax - ix, r.yMin + iy), i2 = new Vector2(r.xMax - ix, r.yMax - iy), i3 = new Vector2(r.xMin + ix, r.yMax - iy);
            PxMesh.Quad(vh, o0, o1, i1, i0, edge, edge, none, none);
            PxMesh.Quad(vh, o1, o2, i2, i1, edge, edge, none, none);
            PxMesh.Quad(vh, o2, o3, i3, i2, edge, edge, none, none);
            PxMesh.Quad(vh, o3, o0, i0, i3, edge, edge, none, none);
        }
    }

    /// <summary>
    /// An annulus sector centred on the rect's centre, pointing UP at rotation 0 (rotate the RectTransform by -bearing).
    /// Span 360 draws a full ring. A dark outline ring rides under it so it reads on sky, grass and stone.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PxArc : MaskableGraphic
    {
        public float RadiusPx = 170f, ThicknessPx = 14f, SpanDeg = 50f, OutlinePx = 2f;
        public Color Outline = new Color(0.07f, 0.09f, 0.16f, 0.85f);
        const int Steps = 18;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (OutlinePx > 0f) Ring(vh, RadiusPx - OutlinePx, ThicknessPx + 2f * OutlinePx, SpanDeg + (SpanDeg >= 360f ? 0f : 4f), Outline);
            Ring(vh, RadiusPx, ThicknessPx, SpanDeg, color);
        }

        static void Ring(VertexHelper vh, float r0, float t, float span, Color c)
        {
            float half = Mathf.Min(span, 360f) * 0.5f * Mathf.Deg2Rad;
            float r1 = r0 + t;
            // the sector's angle is measured from screen-up, clockwise (bearing convention)
            for (int i = 0; i < Steps; i++)
            {
                float a0 = -half + 2f * half * i / Steps, a1 = -half + 2f * half * (i + 1) / Steps;
                Vector2 d0 = new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)), d1 = new Vector2(Mathf.Sin(a1), Mathf.Cos(a1));
                PxMesh.Quad(vh, d0 * r0, d0 * r1, d1 * r1, d1 * r0, c, c, c, c);
            }
        }
    }

    /// <summary>The hit marker: four diagonal strokes around the centre with a gap (an X with an open middle), outlined.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PxTicks : MaskableGraphic
    {
        public float GapPx = 9f, LengthPx = 12f, WidthPx = 3f, OutlinePx = 1.5f;
        public Color Outline = new Color(0.07f, 0.09f, 0.16f, 0.9f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                float w = pass == 0 ? WidthPx + 2f * OutlinePx : WidthPx;
                float ext = pass == 0 ? OutlinePx : 0f;
                Color c = pass == 0 ? Outline : color;
                for (int k = 0; k < 4; k++)
                {
                    float a = (45f + 90f * k) * Mathf.Deg2Rad;
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    PxMesh.Stroke(vh, d * (GapPx - ext), d * (GapPx + LengthPx + ext), w, c);
                }
            }
        }
    }

    /// <summary>Four corner brackets just inside the rect (the hittable / aim-lock cue on a target body), outlined.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PxBracket : MaskableGraphic
    {
        public float CornerPx = 11f, WidthPx = 3f, OutlinePx = 1.5f;
        public Color Outline = new Color(0.07f, 0.09f, 0.16f, 0.85f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            for (int pass = 0; pass < 2; pass++)
            {
                float w = pass == 0 ? WidthPx + 2f * OutlinePx : WidthPx;
                float ext = pass == 0 ? OutlinePx : 0f;
                Color c = pass == 0 ? Outline : color;
                for (int k = 0; k < 4; k++)
                {
                    float sx = (k & 1) == 0 ? -1f : 1f, sy = (k & 2) == 0 ? -1f : 1f;
                    Vector2 corner = new Vector2(sx > 0 ? r.xMax : r.xMin, sy > 0 ? r.yMax : r.yMin);
                    Vector2 inset = new Vector2(-sx, -sy) * (WidthPx * 0.5f);
                    Vector2 p = corner + inset;
                    PxMesh.Stroke(vh, p + new Vector2(sx * ext, 0f), p + new Vector2(-sx * (CornerPx + ext), 0f), w, c);
                    PxMesh.Stroke(vh, p + new Vector2(0f, sy * ext), p + new Vector2(0f, -sy * (CornerPx + ext)), w, c);
                }
            }
        }
    }
}

using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// fix/hud-edge-markers-1: where an off-screen edge marker (the hill pointer, the supply-drop pointer) may sit. The owner's
    /// rule is that no HUD item overlaps another; a live match showed the drop pointer drawn on the COINS panel with its
    /// distance label on the minimap's top edge (sheet-walls-5b38dff). A marker is a box (badge plus distance label), not a
    /// point: it sits on the screen's inset edge and slides along that edge, both ways, to the nearest spot where the whole
    /// box clears every HUD panel by Margin. If the edge has no free spot it tries a ring further in.
    /// Panels: the fixed layout rects (score bar, Kaiju rage panel, portrait and ability row, minimap, coins, power-up
    /// timer, build bank, objective strip with the resource row) plus every live HudKeepOut panel (kill feed, recap).
    /// Space: origin bottom-left, y up, in the caller's units; k = caller units per HUD reference unit (1920 x 1080).
    /// Fixed arrays, no allocation; view only.
    /// </summary>
    public static class HudEdgeMarkers
    {
        /// <summary>Half size of a marker in reference units: the 48 px badge plus the distance label under it.</summary>
        public static readonly Vector2 MarkerHalf = new Vector2(44f, 60f);
        public const float Margin = 8f, Step = 6f, RingStep = 16f;
        public const int Rings = 16, LayoutCount = 8;
        public const int MaxPanels = LayoutCount + HudKeepOut.Max;
        static readonly Rect[] Buf = new Rect[MaxPanels];

        /// <summary>Layout panel i in reference space (y down from the top).</summary>
        public static Rect LayoutRef(int i)
        {
            switch (i)
            {
                case 0: return HudLayout.RefRect(HudCluster.Match);     // top score bar and hill bar
                case 1: return HudLayout.RefRect(HudCluster.Kaiju);     // Kaiju rage panel
                case 2: return HudLayout.RefRect(HudCluster.Player);    // portrait and ability row
                case 3: return HudLayout.RefRect(HudCluster.Minimap);
                case 4: return HudZones.Coins;
                case 5: return HudZones.Power;                          // power-up timer, left of the coins
                case 6: return HudZones.Kit;                            // team build bank, above the portrait row
                default: return HudZones.Job;                           // objective strip, resource row, event banner
            }
        }

        public static string LayoutName(int i) =>
            i switch { 0 => "score bar", 1 => "Kaiju panel", 2 => "portrait row", 3 => "minimap", 4 => "coins", 5 => "power-up", 6 => "build bank", _ => "objective strip" };

        /// <summary>A reference rect (y down) in a w x h space (y up), k units per reference unit, held to its nearest side
        /// (or centred when it spans the middle), the way the HUD anchors its clusters.</summary>
        public static Rect ToSpace(Rect r, float w, float h, float k)
        {
            float cx = HudLayout.RefW * 0.5f, cy = HudLayout.RefH * 0.5f;
            float x0 = r.xMin < cx && r.xMax > cx ? w * 0.5f + (r.xMin - cx) * k
                : r.center.x < cx ? r.xMin * k : w - (HudLayout.RefW - r.xMin) * k;
            float top = r.center.y < cy ? r.yMin * k : h - (HudLayout.RefH - r.yMin) * k;
            return new Rect(x0, h - top - r.height * k, r.width * k, r.height * k);
        }

        /// <summary>Fills buf with the layout panels in a w x h space; returns the count.</summary>
        public static int LayoutPanels(float w, float h, float k, Rect[] buf)
        {
            for (int i = 0; i < LayoutCount; i++) buf[i] = ToSpace(LayoutRef(i), w, h, k);
            return LayoutCount;
        }

        /// <summary>The marker's box at centre p.</summary>
        public static Rect Box(Vector2 p, Vector2 halfRef, float k) =>
            new Rect(p.x - halfRef.x * k, p.y - halfRef.y * k, 2f * halfRef.x * k, 2f * halfRef.y * k);

        /// <summary>Live placement: layout panels plus the live HudKeepOut panels (screen px / pxPerUnit).</summary>
        // fix/frame-cost-1: up to 8 edge pings, the hill pointer and the drop pointer each gathered the panel list (8 layout
        // rects plus up to 16 live RectTransform corner projections) every frame. Gathered once per frame per space now.
        static int _cacheFrame = -1, _cacheN;
        static float _cw, _ch, _ck, _cp;

        public static Vector2 PlaceLive(Vector2 want, Vector2 halfRef, float w, float h, float k, float pxPerUnit, float inset)
        {
            using var scope = Game.FrameCostMarkers.HudEdge.Auto();
            int frame = Time.frameCount;
            if (frame != _cacheFrame || w != _cw || h != _ch || k != _ck || pxPerUnit != _cp)
            {
                int n = LayoutPanels(w, h, k, Buf);
                float inv = pxPerUnit > 1e-4f ? 1f / pxPerUnit : 1f;
                for (int i = 0; i < HudKeepOut.Count && n < MaxPanels; i++)
                    if (HudKeepOut.ScreenRect(i, out Rect r)) Buf[n++] = new Rect(r.x * inv, r.y * inv, r.width * inv, r.height * inv);
                _cacheFrame = frame; _cw = w; _ch = h; _ck = k; _cp = pxPerUnit; _cacheN = n;
            }
            return Place(want, halfRef, w, h, k, inset, Buf, _cacheN);
        }

        /// <summary>
        /// The marker centre nearest to want, on the screen's edge inset by inset (or a ring further in), whose box clears
        /// every panel by Margin and stays on screen. want is first moved to the nearest point of that edge.
        /// </summary>
        public static Vector2 Place(Vector2 want, Vector2 halfRef, float w, float h, float k, float inset, Rect[] panels, int n)
        {
            float hx = halfRef.x * k, hy = halfRef.y * k, m = Margin * k, step = Mathf.Max(1f, Step * k);
            float baseIns = Mathf.Max(inset, Mathf.Max(hx, hy) + 2f * k);
            Vector2 first = want;
            for (int ring = 0; ring < Rings; ring++)
            {
                float ins = baseIns + ring * RingStep * k;
                float x0 = ins, x1 = w - ins, y0 = ins, y1 = h - ins;
                if (x1 <= x0 || y1 <= y0) break;
                float rw = x1 - x0, rh = y1 - y0, len = 2f * (rw + rh);
                float t = ToT(want, x0, x1, y0, y1);
                Vector2 p0 = FromT(t, x0, y0, rw, rh);
                if (ring == 0) first = p0;
                for (float s = 0f; s <= len * 0.5f; s += step)
                {
                    Vector2 a = FromT(Wrap(t + s, len), x0, y0, rw, rh);
                    if (Free(a, hx, hy, m, w, h, panels, n)) return a;
                    if (s <= 0f) continue;
                    Vector2 b = FromT(Wrap(t - s, len), x0, y0, rw, rh);
                    if (Free(b, hx, hy, m, w, h, panels, n)) return b;
                }
            }
            return first;
        }

        static bool Free(Vector2 p, float hx, float hy, float m, float w, float h, Rect[] panels, int n)
        {
            float bx0 = p.x - hx, bx1 = p.x + hx, by0 = p.y - hy, by1 = p.y + hy;
            if (bx0 < 0f || by0 < 0f || bx1 > w || by1 > h) return false;
            for (int i = 0; i < n; i++)
            {
                Rect r = panels[i];
                if (bx1 > r.xMin - m && bx0 < r.xMax + m && by1 > r.yMin - m && by0 < r.yMax + m) return false;
            }
            return true;
        }

        static float Wrap(float t, float len) => t < 0f ? t + len : t >= len ? t - len : t;

        // Perimeter: bottom edge left to right, right edge up, top edge right to left, left edge down.
        static Vector2 FromT(float t, float x0, float y0, float rw, float rh)
        {
            if (t < rw) return new Vector2(x0 + t, y0);
            t -= rw;
            if (t < rh) return new Vector2(x0 + rw, y0 + t);
            t -= rh;
            if (t < rw) return new Vector2(x0 + rw - t, y0 + rh);
            t -= rw;
            return new Vector2(x0, y0 + rh - Mathf.Min(t, rh));
        }

        static float ToT(Vector2 p, float x0, float x1, float y0, float y1)
        {
            float x = Mathf.Clamp(p.x, x0, x1), y = Mathf.Clamp(p.y, y0, y1);
            float rw = x1 - x0, rh = y1 - y0;
            float dl = x - x0, dr = x1 - x, db = y - y0, dt = y1 - y;
            float min = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
            if (min == db) return x - x0;
            if (min == dr) return rw + (y - y0);
            if (min == dt) return rw + rh + (x1 - x);
            return 2f * rw + rh + (y1 - y);
        }
    }
}

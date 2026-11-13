using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// Owner walk 1: the screen areas the fixed HUD panels cover (the clock band, the Kaiju panel, the kill feed, the player
    /// card, the minimap, the coin counter and the power-up timer). World-anchored labels (bot name tags) hide while they
    /// would sit on one, and the off-screen pointers slide along the edge until they clear it, so nothing that follows the
    /// world draws through a panel. Panels register once at build; the test reads live rects only for active ones. Fixed
    /// arrays, no allocation; view only.
    /// </summary>
    public static class HudKeepOut
    {
        public const int Max = 16;
        static readonly RectTransform[] Rts = new RectTransform[Max];
        static readonly Canvas[] Canvases = new Canvas[Max];
        static int _n;
        static readonly Vector3[] Corners = new Vector3[4];

        public static int Count => _n;

        public static void Add(RectTransform rt)
        {
            if (rt == null) return;
            for (int i = 0; i < _n; i++) if (Rts[i] == rt) return;
            // drop destroyed entries first (a new match rebuilds the HUD)
            int w = 0;
            for (int i = 0; i < _n; i++) if (Rts[i] != null) { Canvases[w] = Canvases[i]; Rts[w++] = Rts[i]; }
            for (int i = w; i < _n; i++) { Rts[i] = null; Canvases[i] = null; }
            _n = w;
            if (_n < Max) { Canvases[_n] = null; Rts[_n++] = rt; }
        }

        public static void Clear()
        {
            for (int i = 0; i < Max; i++) { Rts[i] = null; Canvases[i] = null; }
            _n = 0;
        }

        /// <summary>Screen rect (px, y up) of entry i; false when it is gone or not shown.</summary>
        public static bool ScreenRect(int i, out Rect r)
        {
            r = default;
            RectTransform rt = i >= 0 && i < _n ? Rts[i] : null;
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            Canvas c = Canvases[i];
            if (c == null) c = Canvases[i] = rt.GetComponentInParent<Canvas>();
            if (c == null || !c.enabled) return false;
            UnityEngine.Camera cam = c.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.rootCanvas.worldCamera;
            rt.GetWorldCorners(Corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
            r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return r.width > 0.5f && r.height > 0.5f;
        }

        /// <summary>The point (screen px, y up) lies on a panel, grown by pad px.</summary>
        public static bool Contains(Vector2 p, float pad)
        {
            for (int i = 0; i < _n; i++)
            {
                if (!ScreenRect(i, out Rect r)) continue;
                if (p.x > r.xMin - pad && p.x < r.xMax + pad && p.y > r.yMin - pad && p.y < r.yMax + pad) return true;
            }
            return false;
        }

        /// <summary>
        /// Slides an edge point (screen px, y up) off every panel it lands on: up or down, toward the screen centre cy, to
        /// the panel's edge plus pad. Two passes (a panel below another).
        /// </summary>
        public static Vector2 PushOut(Vector2 p, float cy, float pad)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool moved = false;
                for (int i = 0; i < _n; i++)
                {
                    if (!ScreenRect(i, out Rect r)) continue;
                    if (!(p.x > r.xMin - pad && p.x < r.xMax + pad && p.y > r.yMin - pad && p.y < r.yMax + pad)) continue;
                    p.y = r.center.y > cy ? r.yMin - pad : r.yMax + pad;
                    moved = true;
                }
                if (!moved) break;
            }
            return p;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();
    }
}

using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// mv-6 readability-4 item 1 (pass 4: "42m" over "10m", 144 of 240 samples): the edge-of-screen distance markers come
    /// from three owners (GameHud hill and Kaiju pointers, DepthHud drop pointer, PingViews pings), each placed alone, so two
    /// markers on the same edge sat on each other. This spreads them: every marker reports its spot (in its own space, w x h)
    /// and its distance; the nearer ones keep their spot, a farther one slides along its edge until it clears every nearer
    /// one by Gap, and only the Cap nearest show. Nearer means last frame's list (one frame stale, so the owners can run in
    /// any order). Active only under the class moves (WorldWords.MovesCap); off, Spread returns the spot unchanged.
    /// Works in a 1920 x 1080 reference frame so the owners' different canvases compare.
    /// </summary>
    public static class EdgeSpread
    {
        public const int Cap = 4, Max = 16;
        public const float Gap = 10f;   // reference px between two markers' boxes

        struct Entry { public int Id; public float Dist; public Rect Box; }

        static readonly Entry[] _prev = new Entry[Max], _cur = new Entry[Max];
        static int _prevN, _curN, _frame = -1;

        public static bool On => CriticalMass.Runtime.Hud.Player.WorldWords.MovesCap;

        /// <summary>The spot for marker id (centre p, half size half, both in a w x h space, y up) at distance dist; shown
        /// false when it is not among the Cap nearest. Off (switches off): p unchanged, shown true.</summary>
        public static Vector2 Spread(int id, Vector2 p, Vector2 half, float w, float h, float dist, out bool shown)
        {
            shown = true;
            if (!On || w <= 1f || h <= 1f) return p;
            int f = Time.frameCount;
            if (f != _frame)
            {
                _frame = f;
                System.Array.Copy(_cur, _prev, _curN);
                _prevN = _curN;
                _curN = 0;
            }
            float sx = HudLayout.RefW / w, sy = HudLayout.RefH / h;
            Vector2 q = new Vector2(p.x * sx, p.y * sy), hq = new Vector2(half.x * sx, half.y * sy);
            int rank = 0;
            for (int i = 0; i < _prevN; i++) if (_prev[i].Id != id && Nearer(_prev[i].Dist, _prev[i].Id, dist, id)) rank++;
            shown = rank < Cap;
            if (shown) q = Slide(q, hq, dist, id, _prev, _prevN);
            if (_curN < Max) _cur[_curN++] = new Entry { Id = id, Dist = dist, Box = new Rect(q - hq, 2f * hq) };
            return new Vector2(q.x / sx, q.y / sy);
        }

        static bool Nearer(float d, int idA, float dist, int id) => d < dist || (d == dist && idA < id);

        /// <summary>Pure: slides box (centre q, half hq, reference px) along its edge clear of every nearer box (by Gap).
        /// On the left / right edge it moves vertically, on the top / bottom edge sideways, away from what it hits.</summary>
        static Vector2 Slide(Vector2 q, Vector2 hq, float dist, int id, Entry[] list, int n)
        {
            bool side = Mathf.Min(q.x, HudLayout.RefW - q.x) < Mathf.Min(q.y, HudLayout.RefH - q.y);
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int i = 0; i < n; i++)
                {
                    if (list[i].Id == id || !Nearer(list[i].Dist, list[i].Id, dist, id)) continue;
                    Rect o = list[i].Box;
                    Rect me = new Rect(q - hq - Vector2.one * (Gap * 0.5f), 2f * hq + Vector2.one * Gap);
                    if (!me.Overlaps(o)) continue;
                    if (side) q.y = q.y >= o.center.y ? o.yMax + hq.y + Gap : o.yMin - hq.y - Gap;
                    else q.x = q.x >= o.center.x ? o.xMax + hq.x + Gap : o.xMin - hq.x - Gap;
                    moved = true;
                }
                if (!moved) break;
            }
            q.x = Mathf.Clamp(q.x, hq.x, HudLayout.RefW - hq.x);
            q.y = Mathf.Clamp(q.y, hq.y, HudLayout.RefH - hq.y);
            return q;
        }

        /// <summary>Tests: forget both frames.</summary>
        public static void Reset() { _prevN = _curN = 0; _frame = -1; }
    }
}

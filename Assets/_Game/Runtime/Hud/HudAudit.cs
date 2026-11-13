using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// Owner walk 1: what the match HUD draws right now, piece by piece, in screen px. A piece is a direct child of a safe
    /// area root (the clusters, the kill feed, the banner, the ping lines, the coin chip) or of an anchor frame (the job line,
    /// stock, event bar, coins, power-up timer, class kit, prompts). A piece counts when it is active, its canvas is on, its
    /// groups are not faded out and it draws something. <see cref="Overlaps"/> lists every pair whose rects intersect (the
    /// capture walk logs it per shot; the PlayMode collision test asserts it is empty at every screen shape). Allocates;
    /// for tests and captures only, never per frame.
    /// </summary>
    public static class HudAudit
    {
        public struct Item
        {
            public string Name;
            public Rect Screen;   // px, y up
            public RectTransform Rt;
        }

        static readonly Vector3[] C = new Vector3[4];

        /// <summary>Every visible HUD piece.</summary>
        public static List<Item> Collect()
        {
            var list = new List<Item>(48);
            foreach (HudSafeSpace s in HudSafeSpace.Live)
            {
                if (s == null || !s.isActiveAndEnabled) continue;
                Transform t = s.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i) as RectTransform;
                    if (c == null) continue;
                    if (s.Zoned)
                    {
                        if (!c.name.StartsWith("Zone ", System.StringComparison.Ordinal)) continue;
                        for (int k = 0; k < c.childCount; k++) Add(list, c.GetChild(k) as RectTransform);
                    }
                    else Add(list, c);
                }
            }
            return list;
        }

        static void Add(List<Item> list, RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return;
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.enabled || !canvas.rootCanvas.enabled) return;
            float alpha = 1f;
            foreach (CanvasGroup g in rt.GetComponentsInParent<CanvasGroup>()) alpha *= g.alpha;
            if (alpha < 0.05f) return;
            bool draws = false;
            foreach (Graphic gr in rt.GetComponentsInChildren<Graphic>())
                if (gr.enabled && gr.color.a > 0.05f && !(gr is Image im && im.sprite == null && im.color.a < 0.06f)) { draws = true; break; }
            if (!draws) return;
            Canvas root = canvas.rootCanvas;
            UnityEngine.Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            rt.GetWorldCorners(C);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, C[0]), b = RectTransformUtility.WorldToScreenPoint(cam, C[2]);
            var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            if (r.width < 1f || r.height < 1f) return;
            // full-screen washes and stretch containers are backgrounds, not pieces
            float sw = cam != null ? cam.pixelWidth : UnityEngine.Screen.width, sh = cam != null ? cam.pixelHeight : UnityEngine.Screen.height;
            if (r.width * r.height > 0.35f * sw * sh) return;
            list.Add(new Item { Name = root.name + "/" + rt.name, Screen = r, Rt = rt });
        }

        /// <summary>Pairs of visible pieces whose rects intersect by more than <paramref name="tolPx"/> on both axes.</summary>
        public static List<string> Overlaps(float tolPx = 1f)
        {
            List<Item> items = Collect();
            var bad = new List<string>();
            for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
            {
                Rect a = items[i].Screen, b = items[j].Screen;
                float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (ox > tolPx && oy > tolPx) bad.Add(items[i].Name + " x " + items[j].Name);
            }
            return bad;
        }

        /// <summary>Pieces that leave the safe area of a w x h target (px), beyond tolPx.</summary>
        public static List<string> OutsideSafeArea(float w, float h, float tolPx = 1f)
        {
            Vector4 ins = HudSafeArea.InsetsPx(w, h);
            var safe = Rect.MinMaxRect(ins.x, ins.w, w - ins.z, h - ins.y);
            var bad = new List<string>();
            foreach (Item it in Collect())
                if (it.Screen.xMin < safe.xMin - tolPx || it.Screen.xMax > safe.xMax + tolPx || it.Screen.yMin < safe.yMin - tolPx || it.Screen.yMax > safe.yMax + tolPx)
                    bad.Add(it.Name);
            return bad;
        }

        /// <summary>One line for logs: the piece count and every overlap.</summary>
        public static string Describe()
        {
            List<Item> items = Collect();
            List<string> o = Overlaps();
            var sb = new StringBuilder();
            sb.Append("hud pieces ").Append(items.Count).Append(", overlaps ").Append(o.Count);
            for (int i = 0; i < o.Count && i < 12; i++) sb.Append(i == 0 ? ": " : "; ").Append(o[i]);
            return sb.ToString();
        }
    }
}

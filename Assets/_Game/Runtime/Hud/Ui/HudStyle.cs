using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// fix/hud-containment-5 (owner: "it does not look professional"): one set of finish tokens for every in-match panel,
    /// pill and chip, applied by a sweep (GameHud, KaijuMoments) so a piece built anywhere ends up the same:
    /// panel (a plate 56 px or taller): radius PanelRadius; chip (shorter): radius ChipRadius; pill (a line up to 40 px
    /// tall and six times as wide, like the job line, or built as a pill): half its height. Every stroke is OutlinePx (3) Void Navy, every rim RimPx (2). The
    /// backing plate of a group (the first shape under its parent, not inside another shape) carries the drop shadow,
    /// ShadowY px straight down at ShadowColor; the rows, fills and meters inside it carry none. Padding inside a panel is
    /// Pad, between pieces Gap. Circles, pies, triangles, chamfered banners, bars under MinH and meters (an empty colour)
    /// keep their own shape.
    /// </summary>
    public static class HudStyle
    {
        public const float PanelRadius = 14f, ChipRadius = 10f, PanelMinH = 56f, MinH = 20f, PillMaxH = 40f;
        public const float OutlinePx = CMTokens.OutlinePx, RimPx = CMTokens.RimPx;
        public const float ShadowY = 4f, Pad = 12f, Gap = 12f;
        public static readonly Color ShadowColor = new Color(0.02f, 0.03f, 0.08f, 0.35f);

        /// <summary>The corner radius the tokens give a w x h round rect (-1 = pill, half the height).</summary>
        public static float RadiusFor(float w, float h, float built)
        {
            if (built < 0f || (h <= PillMaxH && w >= 6f * h)) return -1f;
            return h >= PanelMinH ? PanelRadius : ChipRadius;
        }

        /// <summary>True when the tokens restyle this shape (a round rect plate, chip or pill; not a bar or a meter).</summary>
        public static bool Styles(VectorShape s)
        {
            if (s == null || s.Kind != ShapeKind.RoundRect || s.EmptyColor.a > 0f) return false;
            Rect r = s.rectTransform.rect;
            return r.height >= MinH && r.width >= MinH;
        }

        /// <summary>Applies the tokens to one shape.</summary>
        public static void Apply(VectorShape s)
        {
            if (!Styles(s)) return;
            Rect r = s.rectTransform.rect;
            float rad = RadiusFor(r.width, r.height, s.Radius);
            bool plate = IsPlate(s);
            float o = s.OutlinePx > 0f ? OutlinePx : 0f, rim = s.RimPx > 0f ? RimPx : 0f, sh = plate ? ShadowY : 0f;
            if (Mathf.Approximately(s.Radius, rad) && Mathf.Approximately(s.OutlinePx, o) && Mathf.Approximately(s.RimPx, rim) &&
                Mathf.Approximately(s.ShadowPx, sh)) return;
            s.Radius = rad;
            s.OutlinePx = o;
            s.RimPx = rim;
            s.ShadowPx = sh;
            s.SetVerticesDirty();
        }

        /// <summary>The group's backing plate: the first shape child of its parent, with no shape above it.</summary>
        public static bool IsPlate(VectorShape s)
        {
            Transform p = s.transform.parent;
            if (p == null || p.GetComponentInParent<VectorShape>(true) != null) return false;
            for (int i = 0; i < p.childCount; i++)
            {
                VectorShape first = p.GetChild(i).GetComponent<VectorShape>();
                if (first != null) return first == s;
            }
            return false;
        }

        static readonly List<VectorShape> Found = new List<VectorShape>(512);

        /// <summary>Applies the tokens to every shape under root (no allocation after the first call).</summary>
        public static void Sweep(Transform root)
        {
            if (root == null) return;
            Found.Clear();
            root.GetComponentsInChildren(true, Found);
            for (int i = 0; i < Found.Count; i++) Apply(Found[i]);
            Found.Clear();
        }
    }
}

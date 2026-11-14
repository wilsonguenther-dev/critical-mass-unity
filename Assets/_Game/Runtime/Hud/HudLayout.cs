using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>The four HUD clusters of bible p15.</summary>
    public enum HudCluster { Match, Kaiju, Player, Minimap }

    /// <summary>
    /// Bible p15 layout in reference pixels of a 1920x1080 screen (top-left origin), plus the anchor each cluster keeps at
    /// other sizes. The HUD scales uniformly by min(w/1920, h/1080) (canvas scale factor), so 1280x720 is the 1080p layout
    /// at 2/3 and 3840x2160 at 2x, re-tessellated (VectorShape) and re-rasterised (dynamic fonts), never stretched.
    /// GameHud places its clusters from these numbers; HudLayoutTests checks them at the three target resolutions.
    /// </summary>
    public static class HudLayout
    {
        public const float RefW = 1920f, RefH = 1080f;
        public const float Margin = 24f;

        // cluster sizes (reference px)
        public static readonly Vector2 MatchSize = new Vector2(800f, 128f);
        public static readonly Vector2 KaijuSize = new Vector2(340f, 100f);
        public static readonly Vector2 PlayerSize = new Vector2(640f, 132f);
        /// <summary>The baked minimap (Ui/MinimapGeometry, 1x px = canvas units) plus a 12 px frame on each side.</summary>
        public static readonly Vector2 MinimapSize = new Vector2(Ui.MinimapGeometry.Projection.Width + 2 * MinimapFrame, Ui.MinimapGeometry.Projection.Height + 2 * MinimapFrame);
        public const float MinimapFrame = 12f;

        // size floor (art direction 6.5)
        public const float ClockDigitPx = 36f, ScoreDigitPx = 32f, PanelIconPx = 40f, AbilityButtonPx = 64f;
        /// <summary>Ability button side as built (above the 64 px floor; the bible draws them about 90 px).</summary>
        public const float AbilityButton = 76f;

        // ---- ability names over the three buttons (hud-overlaps-1)
        // The old labels were 104 px boxes on an 84 px pitch, so neighbouring boxes shared 20 px and two wide names
        // (HEALING WINGS next to CORRUPTION) printed over each other. Now the names are measured and packed in one row:
        // one shared size (largest from AbilityNameMax down to AbilityNameFloor) at which every name fits with a gap to
        // its neighbours, each name kept centred over its own button where it can and never with its centre off that
        // button. Only if nothing fits at the floor are boxes capped and the names cut with an ellipsis. All in
        // reference px: the HUD scales uniformly (Scale), so the same layout holds at every screen shape.
        /// <summary>Left edge of the first ability button and the distance between buttons (Player cluster px).</summary>
        public const float AbilityX0 = 392f, AbilityPitch = 84f;
        /// <summary>Clear space kept between two ability names (5 screen px at 1280x720).</summary>
        public const float AbilityNameGap = 8f;
        /// <summary>How far the row of names may reach past the outer buttons (above the health plate and empty canvas).</summary>
        public const float AbilityNameReach = 20f;
        public const int AbilityNameMax = 16, AbilityNameFloor = 12;

        /// <summary>Centre x of ability button i.</summary>
        public static float AbilityCentre(int i) => AbilityX0 + i * AbilityPitch + AbilityButton * 0.5f;

        /// <summary>A name's box: its measured width at a size plus room for glyph rounding.</summary>
        static float NameBox(float widthAtMax, int size) => widthAtMax <= 0f ? 0f : widthAtMax * size / AbilityNameMax * 1.04f + 2f;

        /// <summary>
        /// Lays out the ability names. widthsAtMax holds each name's one-line width at AbilityNameMax (0 for none).
        /// Returns the shared font size; boxX / boxW receive each name's box (left edge and width, Player cluster px).
        /// Boxes of neighbours never come closer than AbilityNameGap. A name wider than its box is cut by the caller.
        /// </summary>
        public static int LayoutAbilityNames(float[] widthsAtMax, float[] boxX, float[] boxW)
        {
            int n = widthsAtMax.Length;
            for (int size = AbilityNameMax; size >= AbilityNameFloor; size--)
            {
                for (int i = 0; i < n; i++) boxW[i] = NameBox(widthsAtMax[i], size);
                if (Pack(boxX, boxW, n)) return size;
            }
            // Nothing fits at the floor: each name keeps a centred box up to the split line with its neighbour.
            float ks = AbilityNameFloor / (float)AbilityNameMax;
            for (int i = 0; i < n; i++)
            {
                float wi = widthsAtMax[i] * ks;
                float half = wi * 0.5f + 2f;
                if (i + 1 < n) half = Mathf.Min(half, Boundary(wi, widthsAtMax[i + 1] * ks) - AbilityNameGap * 0.5f);
                if (i > 0) half = Mathf.Min(half, AbilityPitch - Boundary(widthsAtMax[i - 1] * ks, wi) - AbilityNameGap * 0.5f);
                boxW[i] = Mathf.Max(0f, 2f * half);
                boxX[i] = AbilityCentre(i) - half;
            }
            return AbilityNameFloor;
        }

        /// <summary>
        /// Places boxes of the given widths left to right: each wants to centre on its button, its centre must stay on
        /// the button, the row stays within the reach, neighbours keep the gap. Forward pass pushes right, backward
        /// pass pulls left; false if no placement exists.
        /// </summary>
        static bool Pack(float[] x, float[] w, int n)
        {
            float left = AbilityX0 - AbilityNameReach, right = AbilityX0 + (n - 1) * AbilityPitch + AbilityButton + AbilityNameReach;
            float prevEnd = float.NegativeInfinity, lo = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                float c = AbilityCentre(i), lb = Mathf.Max(left, c - AbilityButton * 0.5f - w[i] * 0.5f), ub = Mathf.Min(right - w[i], c + AbilityButton * 0.5f - w[i] * 0.5f);
                // feasibility: the earliest this box can start given everything before it
                lo = i == 0 ? lb : Mathf.Max(lb, lo + w[i - 1] + AbilityNameGap);
                if (lo > ub + 1e-3f) return false;
                x[i] = Mathf.Max(Mathf.Clamp(c - w[i] * 0.5f, lb, ub), prevEnd);
                prevEnd = x[i] + w[i] + AbilityNameGap;
            }
            for (int i = n - 1; i >= 0; i--)
            {
                float c = AbilityCentre(i), ub = Mathf.Min(right - w[i], c + AbilityButton * 0.5f - w[i] * 0.5f);
                if (i + 1 < n) ub = Mathf.Min(ub, x[i + 1] - AbilityNameGap - w[i]);
                x[i] = Mathf.Min(x[i], ub);
            }
            return true;
        }

        /// <summary>Distance from the left name's centre to the line two neighbours split at when they cannot both fit:
        /// in proportion to their widths, each keeping a quarter pitch at least.</summary>
        static float Boundary(float wl, float wr)
        {
            float need = (wl + wr) * 0.5f;
            if (need + AbilityNameGap <= AbilityPitch) return wl * 0.5f + (AbilityPitch - need) * 0.5f;
            float share = wl + wr > 1e-3f ? AbilityPitch * wl / (wl + wr) : AbilityPitch * 0.5f;
            return Mathf.Clamp(share, AbilityPitch * 0.25f, AbilityPitch * 0.75f);
        }

        /// <summary>Uniform HUD scale for a screen.</summary>
        public static float Scale(float w, float h) => Mathf.Min(w / RefW, h / RefH);

        /// <summary>Cluster rect at 1920x1080, top-left origin.</summary>
        public static Rect RefRect(HudCluster c)
        {
            switch (c)
            {
                case HudCluster.Match: return new Rect((RefW - MatchSize.x) * 0.5f, 0f, MatchSize.x, MatchSize.y);
                case HudCluster.Kaiju: return new Rect(RefW - Margin - KaijuSize.x, 8f, KaijuSize.x, KaijuSize.y);
                case HudCluster.Player: return new Rect(Margin, RefH - Margin - PlayerSize.y, PlayerSize.x, PlayerSize.y);
                default: return new Rect(RefW - Margin - MinimapSize.x, RefH - Margin - MinimapSize.y, MinimapSize.x, MinimapSize.y);
            }
        }

        /// <summary>Anchor of a cluster in normalised screen space (uGUI convention, y up).</summary>
        public static Vector2 Anchor(HudCluster c)
        {
            switch (c)
            {
                case HudCluster.Match: return new Vector2(0.5f, 1f);
                case HudCluster.Kaiju: return new Vector2(1f, 1f);
                case HudCluster.Player: return new Vector2(0f, 0f);
                default: return new Vector2(1f, 0f);
            }
        }

        /// <summary>Cluster rect in device pixels on a w x h screen (top-left origin), keeping its anchor.</summary>
        public static Rect PixelRect(HudCluster c, float w, float h)
        {
            float s = Scale(w, h);
            Rect r = RefRect(c);
            Vector2 a = Anchor(c);
            // reference distance of the rect's top-left from its anchor point
            float ax = a.x * RefW, ay = (1f - a.y) * RefH;
            float px = a.x * w + (r.x - ax) * s;
            float py = (1f - a.y) * h + (r.y - ay) * s;
            return new Rect(px, py, r.width * s, r.height * s);
        }

        /// <summary>uGUI anchoredPosition (canvas units, from the anchor) of the cluster's pivot, pivot = anchor.</summary>
        public static Vector2 AnchoredPosition(HudCluster c)
        {
            Rect r = RefRect(c);
            Vector2 a = Anchor(c);
            float pivotX = r.x + a.x * r.width;
            float pivotYDown = r.y + (1f - a.y) * r.height;
            return new Vector2(pivotX - a.x * RefW, -(pivotYDown - (1f - a.y) * RefH));
        }
    }
}

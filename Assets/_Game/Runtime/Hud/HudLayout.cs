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
        public static readonly Vector2 MinimapSize = new Vector2(272f, 222f);

        // size floor (art direction 6.5)
        public const float ClockDigitPx = 36f, ScoreDigitPx = 32f, PanelIconPx = 40f, AbilityButtonPx = 64f;
        /// <summary>Ability button side as built (above the 64 px floor; the bible draws them about 90 px).</summary>
        public const float AbilityButton = 76f;

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

using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// Owner walk 1: the part of the screen every HUD and menu may draw in. Screen.safeArea when the game renders to the
    /// screen (the camera notch of a full-screen MacBook Pro, a TV's overscan), nothing when it renders to a texture, or
    /// the capture / test override. Insets are device pixels from each edge (x left, y top, z right, w bottom).
    /// <para>Notch guard: some macOS set-ups report the full screen as safe while the window covers the notch. When the
    /// player runs full screen at the exact shape of a notched MacBook Pro panel (3024x1964 or 3456x2234 and their halves:
    /// height / width 0.645..0.650) and the reported safe area is the whole screen, the top menu-bar band (37 points:
    /// 3.77 % of the height) is kept clear anyway. A screen of any other shape is never touched.</para>
    /// View only; no allocation.
    /// </summary>
    public static class HudSafeArea
    {
        /// <summary>Captures and tests: when set, these insets (device px) apply to every screen size.</summary>
        public static bool HasOverride;
        public static Vector4 OverrideInsets;
        /// <summary>Top inset of a notched MacBook Pro panel as a share of its height (37 of 982 points).</summary>
        public const float NotchShare = 37f / 982f;
        /// <summary>Bumped whenever the override changes, so cached layouts know to re-read.</summary>
        public static int Version { get; private set; }

        public static void SetOverride(Vector4 insetsPx)
        {
            HasOverride = true;
            OverrideInsets = insetsPx;
            Version++;
        }

        public static void ClearOverride()
        {
            HasOverride = false;
            OverrideInsets = Vector4.zero;
            Version++;
        }

        /// <summary>The notch insets of a notched MacBook Pro panel of w x h device px (0 when the shape is any other).</summary>
        public static Vector4 NotchFor(float w, float h)
        {
            if (w < 1f || h < 1f) return Vector4.zero;
            float r = h / w;
            return r >= 0.645f && r <= 0.650f ? new Vector4(0f, Mathf.Round(h * NotchShare), 0f, 0f) : Vector4.zero;
        }

        /// <summary>Insets in device px for a render target of w x h px.</summary>
        public static Vector4 InsetsPx(float w, float h)
        {
            if (HasOverride) return OverrideInsets;
            if (Application.isEditor || w < 1f || h < 1f) return Vector4.zero;
            // Only when drawing to the screen itself (a render texture of another size has no notch).
            if (Mathf.Abs(w - Screen.width) > 1f || Mathf.Abs(h - Screen.height) > 1f) return Vector4.zero;
            Rect s = Screen.safeArea;
            var ins = new Vector4(Mathf.Max(0f, s.xMin), Mathf.Max(0f, h - s.yMax), Mathf.Max(0f, w - s.xMax), Mathf.Max(0f, s.yMin));
            if (ins == Vector4.zero && Application.platform == RuntimePlatform.OSXPlayer && Screen.fullScreen) ins = NotchFor(w, h);
            return ins;
        }

        /// <summary>Insets in canvas units (reference px) for a canvas of scale factor <paramref name="scale"/>.</summary>
        public static Vector4 InsetsRef(float w, float h, float scale)
        {
            Vector4 p = InsetsPx(w, h);
            return scale > 1e-4f ? p / scale : Vector4.zero;
        }
    }
}

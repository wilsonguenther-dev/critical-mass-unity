using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>The nine anchor frames of the HUD: each keeps the 1920x1080 layout's distance to its own corner or edge.</summary>
    public enum HudAnchor { TopLeft, Top, TopRight, Left, Centre, Right, BottomLeft, Bottom, BottomRight }

    /// <summary>
    /// Owner walk 1: the one HUD layout rule for every screen shape. Every HUD piece is still authored in the 1920x1080
    /// reference space (top-left origin), but it is drawn inside one of nine anchor frames: a 1920x1080 rect pinned to the
    /// matching corner, edge or centre of the SAFE AREA (the screen minus the camera notch, HudSafeArea). At 16:9 the nine
    /// frames coincide with the screen, so nothing moves; at 16:10, the MacBook's 1.54 or an ultrawide, a piece keeps its
    /// distance to its own edge, so pieces authored side by side (the Kaiju panel, the kill feed, the power-up timer, the
    /// coins and the minimap) stay side by side instead of drifting by the extra height or width, and nothing sits under the
    /// notch. Which frame a piece belongs to follows from where its rect sits in the reference space (thirds of 1920 and
    /// 1080: <see cref="Classify"/>), the same rule the collision test uses.
    /// </summary>
    public static class HudFrames
    {
        /// <summary>The frame a reference rect (top-left origin) belongs to: by its centre, in thirds of the reference space.</summary>
        public static HudAnchor Classify(Rect r)
        {
            float cx = r.center.x, cy = r.center.y;
            int h = cx < HudLayout.RefW / 3f ? 0 : cx > HudLayout.RefW * 2f / 3f ? 2 : 1;
            int v = cy < HudLayout.RefH / 3f ? 0 : cy > HudLayout.RefH * 2f / 3f ? 2 : 1;
            return (HudAnchor)(v * 3 + h);
        }

        /// <summary>The frame's anchor in normalised uGUI space (y up).</summary>
        public static Vector2 Norm(HudAnchor a) => new Vector2((int)a % 3 * 0.5f, 1f - (int)a / 3 * 0.5f);

        /// <summary>
        /// Where a reference rect (top-left origin, 1920x1080 space) lands on a w x h px screen with the given safe-area insets
        /// (px: left, top, right, bottom), in device px, top-left origin. The HUD scale is HudLayout.Scale(w, h).
        /// </summary>
        public static Rect PixelRect(Rect reference, HudAnchor a, float w, float h, Vector4 insetsPx)
        {
            float s = HudLayout.Scale(w, h);
            float sw = w - insetsPx.x - insetsPx.z, sh = h - insetsPx.y - insetsPx.w;
            Vector2 n = Norm(a);
            // the frame's top-left corner on screen (px, y down)
            float fx = insetsPx.x + n.x * (sw - HudLayout.RefW * s);
            float fy = insetsPx.y + (1f - n.y) * (sh - HudLayout.RefH * s);
            return new Rect(fx + reference.x * s, fy + reference.y * s, reference.width * s, reference.height * s);
        }

        /// <summary>PixelRect with the frame picked by Classify.</summary>
        public static Rect PixelRect(Rect reference, float w, float h, Vector4 insetsPx) => PixelRect(reference, Classify(reference), w, h, insetsPx);

        /// <summary>The reference rect (top-left origin) of a child placed MenuUi.Place style (anchor top-left of its parent).</summary>
        public static Rect RefRectOf(RectTransform rt)
        {
            Vector2 size = rt.sizeDelta, ap = rt.anchoredPosition, pv = rt.pivot;
            return new Rect(ap.x - pv.x * size.x, -ap.y - (1f - pv.y) * size.y, size.x, size.y);
        }
    }

    /// <summary>
    /// Owner walk 1: put on a HUD canvas's layout space (or a stretch container on the canvas). Keeps the container
    /// stretched over the safe area (insets / canvas scale, re-read only when the size, scale or override changes) and, when
    /// <see cref="Zoned"/>, re-homes every child placed MenuUi.Place style (anchored to the parent's top-left) into the
    /// anchor frame its reference rect belongs to (HudFrames.Classify). Children anchored any other way (world-projected
    /// markers, centred pointers) stay in the container and keep working: the container is the whole safe area. View only,
    /// no allocation after the first frame (the re-home runs only when the child count changes).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudSafeSpace : MonoBehaviour
    {
        public bool Zoned;
        RectTransform _rt;
        Canvas _canvas;
        readonly RectTransform[] _frames = new RectTransform[9];
        float _w = -1f, _h = -1f, _s = -1f;
        int _ver = -1, _children = -1;
        static readonly List<RectTransform> Scratch = new List<RectTransform>(64);
        /// <summary>Every live safe space (captures re-apply them after a resolution switch).</summary>
        public static readonly List<HudSafeSpace> Live = new List<HudSafeSpace>(32);

        /// <summary>Stretches <paramref name="space"/> over its canvas's safe area; zoned = re-home Place-style children.</summary>
        public static HudSafeSpace Attach(RectTransform space, bool zoned)
        {
            HudSafeSpace s = space.GetComponent<HudSafeSpace>();
            if (s == null) s = space.gameObject.AddComponent<HudSafeSpace>();
            s.Zoned = zoned;
            space.anchorMin = Vector2.zero;
            space.anchorMax = Vector2.one;
            space.pivot = new Vector2(0.5f, 0.5f);
            space.offsetMin = space.offsetMax = Vector2.zero;
            s.Apply(true);
            return s;
        }

        /// <summary>A stretch container on canvas root <paramref name="parent"/> kept on the safe area (anchor-placed children).</summary>
        public static RectTransform MakeSafeRoot(Transform parent, string name = "Safe Area")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Attach(rt, false);
            return rt;
        }

        /// <summary>The frame for an anchor (built on first use).</summary>
        public RectTransform Frame(HudAnchor a)
        {
            int i = (int)a;
            if (_frames[i] != null) return _frames[i];
            var go = new GameObject("Zone " + a, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            go.transform.SetSiblingIndex(0);
            var rt = (RectTransform)go.transform;
            Vector2 n = HudFrames.Norm(a);
            rt.anchorMin = rt.anchorMax = rt.pivot = n;
            rt.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            rt.anchoredPosition = Vector2.zero;
            _frames[i] = rt;
            return rt;
        }

        void OnEnable()
        {
            if (!Live.Contains(this)) Live.Add(this);
        }

        void OnDisable() => Live.Remove(this);

        void LateUpdate() => Apply(false);

        /// <summary>Re-reads the safe area (force: always) and re-homes new children.</summary>
        public void Apply(bool force)
        {
            if (_rt == null) _rt = (RectTransform)transform;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null)
            {
                Canvas root = _canvas.rootCanvas;
                UnityEngine.Camera cam = root.worldCamera;
                float w = cam != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? cam.pixelWidth : Screen.width;
                float h = cam != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? cam.pixelHeight : Screen.height;
                float s = root.scaleFactor;
                if (force || w != _w || h != _h || s != _s || _ver != HudSafeArea.Version)
                {
                    _w = w; _h = h; _s = s; _ver = HudSafeArea.Version;
                    Vector4 ins = HudSafeArea.InsetsRef(w, h, s);
                    _rt.offsetMin = new Vector2(ins.x, ins.w);
                    _rt.offsetMax = new Vector2(-ins.z, -ins.y);
                }
            }
            if (Zoned && transform.childCount != _children) Rehome();
        }

        void Rehome()
        {
            Scratch.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i) as RectTransform;
                if (c == null || IsFrame(c)) continue;
                if (c.anchorMin == new Vector2(0f, 1f) && c.anchorMax == new Vector2(0f, 1f)) Scratch.Add(c);
            }
            for (int i = 0; i < Scratch.Count; i++)
            {
                RectTransform c = Scratch[i];
                c.SetParent(Frame(HudFrames.Classify(HudFrames.RefRectOf(c))), false);
            }
            Scratch.Clear();
            _children = transform.childCount;
        }

        bool IsFrame(RectTransform c)
        {
            for (int i = 0; i < _frames.Length; i++) if (_frames[i] == c) return true;
            return false;
        }

        /// <summary>Re-applies every live safe space now (captures, after switching the render size).</summary>
        public static void ApplyAll()
        {
            for (int i = 0; i < Live.Count; i++) if (Live[i] != null) Live[i].Apply(true);
        }
    }
}

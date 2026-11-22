using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>Lane D building helpers: world-anchored pieces are centred on a point given in canvas units from bottom-left.</summary>
    public static class PxUi
    {
        public static RectTransform Rt(Transform parent, string name) => CriticalMass.Runtime.Menu.MenuUi.Rt(parent, name);

        /// <summary>Anchored at the parent's bottom-left with a centre pivot (world-layer convention of GameHud).</summary>
        public static RectTransform Centred(Transform parent, string name, float w, float h)
        {
            RectTransform rt = Rt(parent, name);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>Anchored at the parent's centre (screen-centre cues: hit marker, kill confirm).</summary>
        public static RectTransform Middle(Transform parent, string name, float w, float h)
        {
            RectTransform rt = Rt(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static CanvasGroup Group(RectTransform rt)
        {
            CanvasGroup g = rt.gameObject.AddComponent<CanvasGroup>();
            g.interactable = false;
            g.blocksRaycasts = false;
            return g;
        }

        public static T Graphic<T>(RectTransform parent, string name, float w, float h, Color c) where T : MaskableGraphic
        {
            RectTransform rt = Rt(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            T g = rt.gameObject.AddComponent<T>();
            g.color = c;
            g.raycastTarget = false;
            return g;
        }

        public static VectorShape Shape(Transform parent, string name, ShapeKind kind, float x, float y, float w, float h, Color fill, float radius, float outline, float rim)
        {
            VectorShape s = CriticalMass.Runtime.Menu.MenuUi.Shape(parent, name, kind, x, y, w, h, fill, radius);
            s.OutlinePx = outline;
            s.RimPx = rim;
            return s;
        }

        public static Text Label(Transform parent, string name, Typeface face, int size, Color c, TextAnchor anchor, float x, float y, float w, float h, float outline)
            => CriticalMass.Runtime.Menu.MenuUi.Label(parent, name, face, size, c, anchor, x, y, w, h, string.Empty, outline);

        public static void Show(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        public static void Alpha(CanvasGroup g, float a)
        {
            a = Mathf.Clamp01(a);
            if (Mathf.Abs(g.alpha - a) >= 1f / 255f) g.alpha = a;
        }
    }

    /// <summary>Projection of sim points for the HUD (both cameras: the top-down follow camera and the third-person view).</summary>
    public static class PxProject
    {
        /// <summary>True while the owner's third-person (or first-person) play view is on (the reticle sits at screen centre).</summary>
        public static bool ThirdPerson => GroundView.PlayActive;

        /// <summary>An actor's interpolated world point at height lift over the ground (the same rule as the name tags).</summary>
        public static Vector3 Actor(World w, int id, double alpha, float lift)
        {
            double x = w.PrevX[id] + (w.X[id] - w.PrevX[id]) * alpha, z = w.PrevZ[id] + (w.Z[id] - w.PrevZ[id]) * alpha;
            return SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + lift, z);
        }

        public static Vector3 Point(World w, double x, double z, float lift) => SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + lift, z);

        /// <summary>Screen pixels of a world point; false when behind the camera or off screen (margin px allowed).</summary>
        public static bool Screen(UnityEngine.Camera cam, Vector3 world, float margin, out Vector2 px)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            px = new Vector2(sp.x, sp.y);
            return sp.z > 0f && sp.x >= -margin && sp.y >= -margin && sp.x <= cam.pixelWidth + margin && sp.y <= cam.pixelHeight + margin;
        }

        /// <summary>
        /// Bearing on screen, degrees clockwise from screen-up, of the ground direction (dx, dz) seen from world point from:
        /// the screen delta to a point 1 u along that direction (both cameras; the near point is always in front of the
        /// camera when from is). Falls back to the camera's ground basis when the near point projects behind.
        /// </summary>
        public static float Bearing(UnityEngine.Camera cam, Vector3 from, float dx, float dz)
        {
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4f) return 0f;
            Vector3 near = from + new Vector3(dx / len, 0f, dz / len);
            Vector3 a = cam.WorldToScreenPoint(from), b = cam.WorldToScreenPoint(near);
            if (a.z > 0.05f && b.z > 0.05f && (b - a).sqrMagnitude > 1e-4f) return PxBearing.FromScreenDelta(b.x - a.x, b.y - a.y);
            Transform t = cam.transform;
            Vector3 up = t.forward.y < -0.9f ? t.up : t.forward;   // a straight-down camera: its up is screen-up on the ground
            return PxBearing.FromBasis(dx, dz, up.x, up.z, t.right.x, t.right.z);
        }
    }

    /// <summary>Pure bearing maths (tests: PxHudBearingTests).</summary>
    public static class PxBearing
    {
        /// <summary>Degrees clockwise from screen-up of a screen delta (x right, y up), 0..360.</summary>
        public static float FromScreenDelta(float sx, float sy) => Wrap(Mathf.Atan2(sx, sy) * Mathf.Rad2Deg);

        /// <summary>Degrees clockwise from screen-up of a ground direction (dx, dz) for a camera whose screen-up and screen-right
        /// project on the ground as (upX, upZ) and (rightX, rightZ).</summary>
        public static float FromBasis(float dx, float dz, float upX, float upZ, float rightX, float rightZ)
        {
            float fwd = dx * upX + dz * upZ, side = dx * rightX + dz * rightZ;
            return Wrap(Mathf.Atan2(side, fwd) * Mathf.Rad2Deg);
        }

        public static float Wrap(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>Smallest difference between two bearings (0..180).</summary>
        public static float Delta(float a, float b)
        {
            float d = Mathf.Abs(Wrap(a) - Wrap(b));
            return d > 180f ? 360f - d : d;
        }
    }
}

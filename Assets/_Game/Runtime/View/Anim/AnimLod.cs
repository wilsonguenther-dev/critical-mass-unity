using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// Animation level of detail for 32 fighters (DG-108, A22): a model's graph is evaluated every frame within NearRadius of
    /// the camera's ground focus, every second frame beyond it (staggered by actor id so half the far crowd updates each
    /// frame), and not at all while none of its renderers is visible. Clip times are absolute (set from sim time every
    /// frame), so a skipped frame only holds last frame's pose; nothing drifts. Models whose animator is set to
    /// AlwaysAnimate (the sanity tests) are never throttled, nor is a model in its first frames after being shown.
    /// </summary>
    public static class AnimLod
    {
        public const float NearRadius = 15f;
        /// <summary>Frames after a model is shown that always evaluate (isVisible is false until it has rendered once).</summary>
        public const int FreshFrames = 3;
        /// <summary>Global switch (the capture tool and perf comparisons can turn the throttle off).</summary>
        public static bool Enabled = true;

        static Vector3 _focus;
        static bool _hasFocus;
        static int _frame;
        static int _pinA = -1, _pinB = -1;

        /// <summary>Once per view update: the gameplay camera's ground focus (its forward ray on the y = 0 plane).</summary>
        public static void BeginFrame()
        {
            _frame = Time.frameCount;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _hasFocus = false;
            bool low = CriticalMass.Runtime.Camera.GroundView.LodActive;
            // fix/px-combat-camera-2: in the low views the followed fighter (and the enemy the combat framing shows) always
            // pose every frame, and the focus is the followed body's look point, not where the view meets y = 0: at the
            // framed pitch (8 deg) that ground hit lands 10 to 20 u ahead, far enough to throttle my own fighter.
            _pinA = low ? CriticalMass.Runtime.Camera.GroundView.LodSelf : -1;
            _pinB = low ? CriticalMass.Runtime.Camera.GroundView.DuelTarget : -1;
            if (cam == null) return;
            Transform t = cam.transform;
            _focus = FocusPoint(t.position, t.forward, low, CriticalMass.Runtime.Camera.GroundView.LodAnchor);
            _hasFocus = true;
        }

        /// <summary>The throttle's focus (pure; tests): a low view's followed look point; else the camera's forward ray on
        /// the y = 0 plane (the top-down rule, unchanged), or the camera itself when it looks level or up.</summary>
        public static Vector3 FocusPoint(Vector3 camPos, Vector3 forward, bool lowView, Vector3 followed)
        {
            if (lowView) return followed;
            if (forward.y < -0.05f) return camPos + forward * (-camPos.y / forward.y);
            return camPos;
        }

        /// <summary>Actor ids that always evaluate this frame (low views: the followed fighter and the framed enemy).</summary>
        public static bool Pinned(int id) => id >= 0 && (id == _pinA || id == _pinB);

        public static bool Near(Vector3 pos)
        {
            if (!_hasFocus) return true;
            float dx = pos.x - _focus.x, dz = pos.z - _focus.z;
            return dx * dx + dz * dz <= NearRadius * NearRadius;
        }

        /// <summary>Whether to evaluate a model's graph this frame.</summary>
        public static bool ShouldEvaluate(bool alwaysAnimate, bool fresh, bool anyVisible, Vector3 pos, int id)
        {
            if (!Enabled || alwaysAnimate || fresh || Pinned(id)) return true;
            if (!anyVisible) return false;
            // perf-60hz-32p: on the title (the backdrop match nobody plays) every fighter poses every other frame.
            bool near = Near(pos) && !CriticalMass.Runtime.Game.FrameGovernor.IdleTitle;
            return near || ((_frame + id) & 1) == 0;
        }

        /// <summary>The pure decision for tests: visible, near or on the stagger frame.</summary>
        public static bool Decide(bool alwaysAnimate, bool fresh, bool anyVisible, bool near, int frame, int id) =>
            alwaysAnimate || fresh || (anyVisible && (near || ((frame + id) & 1) == 0));
    }
}

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

        /// <summary>Once per view update: the gameplay camera's ground focus (its forward ray on the y = 0 plane).</summary>
        public static void BeginFrame()
        {
            _frame = Time.frameCount;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _hasFocus = false;
            if (cam == null) return;
            Transform t = cam.transform;
            Vector3 f = t.forward;
            if (f.y < -0.05f)
            {
                float k = -t.position.y / f.y;
                _focus = t.position + f * k;
            }
            else _focus = t.position;
            _hasFocus = true;
        }

        public static bool Near(Vector3 pos)
        {
            if (!_hasFocus) return true;
            float dx = pos.x - _focus.x, dz = pos.z - _focus.z;
            return dx * dx + dz * dz <= NearRadius * NearRadius;
        }

        /// <summary>Whether to evaluate a model's graph this frame.</summary>
        public static bool ShouldEvaluate(bool alwaysAnimate, bool fresh, bool anyVisible, Vector3 pos, int id)
        {
            if (!Enabled || alwaysAnimate || fresh) return true;
            if (!anyVisible) return false;
            return Near(pos) || ((_frame + id) & 1) == 0;
        }

        /// <summary>The pure decision for tests: visible, near or on the stagger frame.</summary>
        public static bool Decide(bool alwaysAnimate, bool fresh, bool anyVisible, bool near, int frame, int id) =>
            alwaysAnimate || fresh || (anyVisible && (near || ((frame + id) & 1) == 0));
    }
}

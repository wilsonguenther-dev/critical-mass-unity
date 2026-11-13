using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// The one owner of a fighter model's drawn size (fix/model-scale, 2026-11-12). The model's base scale is stored once
    /// (ModelView.BaseScale, read from the imported prefab); every effect (hit punch, death pop, squash and stretch, take-off
    /// stretch, landing squash) contributes a multiplicative factor that the animator rebuilds from 1 every frame; this
    /// composes base x factor and clamps the factor to [MinFactor, MaxFactor], so no effect, interrupted or overlapping,
    /// can ever leave a fighter at a wrong size. Logs once per session when the clamp fires. View only, never the sim.
    /// </summary>
    public static class ModelScale
    {
        /// <summary>The sane range for the combined effect factor (the largest designed stack is about 1.2 x 1.08).</summary>
        public const float MinFactor = 0.5f, MaxFactor = 1.5f;

        /// <summary>Times the clamp fired since the last ResetForTests (tests, perf report).</summary>
        public static int ClampCount { get; private set; }

        private static bool _logged;

        /// <summary>base x factor per axis, with the factor clamped to the sane range (and NaN read as 1).</summary>
        public static Vector3 Compose(Vector3 baseScale, Vector3 factor, string who)
        {
            Vector3 f = new Vector3(Clamp(factor.x), Clamp(factor.y), Clamp(factor.z));
            if (f != factor)
            {
                ClampCount++;
                if (!_logged)
                {
                    _logged = true;
                    Debug.LogWarning($"[ModelScale] effect scale factor {factor} on {who ?? "a model"} clamped to {f} (sane range {MinFactor}..{MaxFactor}); logged once");
                }
            }
            return Vector3.Scale(baseScale, f);
        }

        /// <summary>A bone's scale from its rest scale and a factor: set from the rest every frame, never multiplied in place.</summary>
        public static Vector3 FromRest(Vector3 rest, float factor) => rest * Clamp(factor);

        static float Clamp(float v) => float.IsNaN(v) ? 1f : Mathf.Clamp(v, MinFactor, MaxFactor);

        public static void ResetForTests()
        {
            ClampCount = 0;
            _logged = false;
        }
    }
}

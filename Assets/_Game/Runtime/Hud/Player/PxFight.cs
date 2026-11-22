using UnityEngine;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Review fix round, defect 5 (the owner's rule: nothing on top of the fight): the local player is "in a fight" for
    /// Window seconds after a hit he dealt or took. While true, big panels and cards hold back (Banner postpones its queue,
    /// one message at a time) and the HUD's own words stay small and out of the centre. PlayerHud marks it.
    /// </summary>
    public static class PxFight
    {
        public const float Window = 3f;
        static float _lastAt = -100f;

        public static bool Active => Time.unscaledTime - _lastAt < Window;

        /// <summary>True when a hit at lastHitAt still counts at now (pure; tests).</summary>
        public static bool ActiveAt(float lastHitAt, float now) => now - lastHitAt < Window;

        public static void Mark() => _lastAt = Time.unscaledTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() => _lastAt = -100f;
    }
}

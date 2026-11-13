using CriticalMass.Runtime.Menu;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Style
{
    /// <summary>
    /// Polish pass 1: the one motion language of every screen and overlay built in code. Screens enter with a short fade
    /// and rise and leave with a shorter fade (no pops); numbers count up; bars ease toward their value. Everything here
    /// is view only, runs on unscaled time and honours the Reduce motion setting (fades only, no rise, counts and bars
    /// land at once). Nothing animates while its canvas is hidden: callers only tick while visible, and the helpers are
    /// pure functions, so there is no per-frame state or allocation here.
    /// </summary>
    public static class UiMotion
    {
        /// <summary>Screen and overlay entrance (fade + rise), seconds.</summary>
        public const float EnterSeconds = 0.18f;
        /// <summary>Screen and overlay exit (fade), seconds: softer and shorter than the entrance.</summary>
        public const float ExitSeconds = 0.12f;
        /// <summary>Entrance rise in reference px (0 with Reduce motion on).</summary>
        public const float RisePx = 28f;
        /// <summary>A number counting up to its value (results, rewards, level bar), seconds.</summary>
        public const float CountSeconds = 0.6f;
        /// <summary>Bars ease toward their value with this time constant, seconds.</summary>
        public const float BarSeconds = 0.12f;

        /// <summary>Tests force a value; null reads the player's setting.</summary>
        public static bool? ReduceOverride;

        /// <summary>Reduce motion is on (the one key every reader shares).</summary>
        public static bool Reduced => ReduceOverride ?? GameSettings.ReduceMotion;

        /// <summary>The entrance rise to use now.</summary>
        public static float Rise => Reduced ? 0f : RisePx;

        /// <summary>Ease-out cubic of a 0..1 progress (the one curve).</summary>
        public static float EaseOut(float k)
        {
            k = Mathf.Clamp01(k);
            float m = 1f - k;
            return 1f - m * m * m;
        }

        /// <summary>Entrance progress 0..1 (eased) for a screen shown <paramref name="elapsed"/> seconds ago.</summary>
        public static float Enter(float elapsed) => EaseOut(elapsed / EnterSeconds);

        /// <summary>Exit alpha 1..0 for a screen asked to hide <paramref name="elapsed"/> seconds ago (0 with Reduce motion).</summary>
        public static float Exit(float elapsed) => Reduced ? 0f : 1f - EaseOut(elapsed / ExitSeconds);

        /// <summary>The value a count-up shows <paramref name="elapsed"/> seconds after it started (lands at once with Reduce motion).</summary>
        public static float Count(float from, float to, float elapsed)
        {
            if (Reduced || elapsed >= CountSeconds) return to;
            return Mathf.Lerp(from, to, EaseOut(elapsed / CountSeconds));
        }

        /// <summary>Whole-number count-up (results figures).</summary>
        public static int CountInt(int from, int to, float elapsed) => Mathf.RoundToInt(Count(from, to, elapsed));

        /// <summary>One frame of a bar easing from <paramref name="shown"/> toward <paramref name="target"/> (frame-rate independent).</summary>
        public static float Bar(float shown, float target, float dt)
        {
            if (Reduced || dt <= 0f) return Reduced ? target : shown;
            float k = 1f - Mathf.Exp(-dt / BarSeconds);
            float v = shown + (target - shown) * k;
            return Mathf.Abs(target - v) < 0.001f ? target : v;
        }
    }
}

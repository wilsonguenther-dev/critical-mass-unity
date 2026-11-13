using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// Physics audit M7 view half (DG-101): counts the local player's ability presses that the sim will refuse, so the HUD
    /// can shake the ability icon and play a soft "not ready" click once per press. Read-only on the World; never writes
    /// the sim or the input frame.
    /// <para>The readiness rule mirrors the sim's ability gate (Abilities.AbilitiesSystem: alive, not getting up
    /// (StandUp), AbilityCd spent) read from the World, never hard-coded. A press that becomes ready within GraceTicks is
    /// not counted, because PR #25's input buffer (DG-14, 4 ticks) will still fire it. When the sim lane adds the attack
    /// wind-up and hit-stun gates (contract C2), add them to <see cref="WillRefuse"/> next to StandUp, the same way
    /// PR #25's InputReadiness must.</para>
    /// </summary>
    public static class AbilityDenial
    {
        /// <summary>PR #25's buffer window (ticks): a press that becomes ready inside it is buffered, not refused.</summary>
        public const int GraceTicks = 4;

        static bool _prev;

        /// <summary>Refused presses since boot (the HUD compares it with the count it saw last).</summary>
        public static int Count { get; private set; }

        /// <summary>One sim step: the human frame's Ability value before the step, the world and the step length.</summary>
        public static void Observe(bool abilityPressed, World w, int actor, double stepSeconds)
        {
            bool edge = abilityPressed && !_prev;
            _prev = abilityPressed;
            if (edge && WillRefuse(w, actor, stepSeconds)) Count++;
        }

        /// <summary>
        /// true when the sim will not cast this press, even after the buffer window (pure; tests). A dead player, an
        /// ended match or an unknown actor gives no cue (false).
        /// </summary>
        public static bool WillRefuse(World w, int actor, double stepSeconds)
        {
            if (w == null || w.Match == null || w.Match.Phase == MatchPhase.Ended || actor < 0 || actor >= w.ActorCount) return false;
            Actor a = w.Actors[actor];
            if (!a.Alive) return false;
            double grace = GraceTicks * stepSeconds;
            if (a.StandUp > grace) return true;
            return a.AbilityCd > grace;
        }

        /// <summary>Tests: back to no presses seen.</summary>
        public static void ResetForTests()
        {
            _prev = false;
            Count = 0;
        }
    }
}

using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Team to team-state enum mapping by name (U-047, review R20261112T1705-1-6). The browser assigns team strings
    /// (winner = team, kaiju.state = team, hillState = team); the C# enum integers differ between TeamId, MatchResult,
    /// HillState and KaijuState, so every such assignment goes through this class and never through a cast.
    /// </summary>
    public static class SimEnumMap
    {
        public static MatchResult ToMatchResult(TeamId t)
        {
            switch (t)
            {
                case TeamId.Azure: return MatchResult.Azure;
                case TeamId.Crimson: return MatchResult.Crimson;
                default: throw new ArgumentOutOfRangeException(nameof(t), t, "not a team");
            }
        }

        public static HillState ToHillState(TeamId t)
        {
            switch (t)
            {
                case TeamId.Azure: return HillState.Azure;
                case TeamId.Crimson: return HillState.Crimson;
                default: throw new ArgumentOutOfRangeException(nameof(t), t, "not a team");
            }
        }

        public static KaijuState ToKaijuState(TeamId t)
        {
            switch (t)
            {
                case TeamId.Azure: return KaijuState.Azure;
                case TeamId.Crimson: return KaijuState.Crimson;
                default: throw new ArgumentOutOfRangeException(nameof(t), t, "not a team");
            }
        }

        /// <summary>The team a match result names; null for None and Draw.</summary>
        public static TeamId? TeamOf(MatchResult r)
        {
            switch (r)
            {
                case MatchResult.Azure: return TeamId.Azure;
                case MatchResult.Crimson: return TeamId.Crimson;
                default: return null;
            }
        }

        /// <summary>The team owning the hill; null for Neutral and Contested.</summary>
        public static TeamId? TeamOf(HillState s)
        {
            switch (s)
            {
                case HillState.Azure: return TeamId.Azure;
                case HillState.Crimson: return TeamId.Crimson;
                default: return null;
            }
        }

        /// <summary>The team the kaiju fights for; null for Egg, Neutral and Berserk.</summary>
        public static TeamId? TeamOf(KaijuState s)
        {
            switch (s)
            {
                case KaijuState.Azure: return TeamId.Azure;
                case KaijuState.Crimson: return TeamId.Crimson;
                default: return null;
            }
        }
    }
}

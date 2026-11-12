using System;

namespace CriticalMass.Sim.Core
{
    /// <summary>Match clock reading (browser <c>matchClock</c> return object).</summary>
    public readonly struct MatchClock
    {
        public readonly double Elapsed, Remaining, UntilHatch;

        public MatchClock(double elapsed, double remaining, double untilHatch)
        {
            Elapsed = elapsed;
            Remaining = remaining;
            UntilHatch = untilHatch;
        }
    }

    /// <summary>Match end and winner (browser match.ts).</summary>
    public static class Match
    {
        /// <summary>Hill totals within eps seconds of each other are a draw.</summary>
        public static MatchResult DecideWinner(double azure, double crimson, double eps)
        {
            double diff = azure - crimson;
            if (Math.Abs(diff) <= eps) return MatchResult.Draw;
            return diff > 0 ? MatchResult.Azure : MatchResult.Crimson;
        }

        /// <summary>Runs after HillSystem. Match.Time is advanced after the systems, so the post-step time is Time + dt.</summary>
        public static void MatchSystem(World w, double dt)
        {
            MatchState m = w.Match;
            if (m.Phase == MatchPhase.Ended) return;
            if (m.Time + dt >= w.Config.MatchSeconds)
            {
                m.Phase = MatchPhase.Ended;
                m.Winner = DecideWinner(m.HillSeconds.Azure, m.HillSeconds.Crimson, w.Config.DrawEpsilonSeconds);
                World.PushEvent(w, SimEventType.MatchEnd, 0, 0, w.Map.Hill.X, w.Map.Hill.Z, m.HillSeconds.Azure - m.HillSeconds.Crimson);
            }
        }

        /// <summary>Elapsed, remaining and time until hatch, each clamped at 0 (browser <c>matchClock</c>).</summary>
        public static MatchClock GetMatchClock(World w)
        {
            double t = w.Match.Time;
            return new MatchClock(
                t,
                Math.Max(0, w.Config.MatchSeconds - t),
                Math.Max(0, w.Config.HatchSeconds - t));
        }
    }
}

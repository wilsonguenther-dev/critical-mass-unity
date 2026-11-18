using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// b15 world look, motion item 4c (WORLD-AUDIT "post-match-end jitter"): once the match has ended SimStep returns
    /// before it moves anything (Sim/Core/SimStep.cs), so the previous-tick and current-tick states the view interpolates
    /// between stay one tick apart for ever, and the interpolation alpha the clock keeps cycling 0..1 replays that last
    /// tick's motion every tick: every fighter shivers in place behind the result screen. Holding alpha at 1 shows the
    /// final state, still. View only: the sim, its ticks and every checksum are untouched.
    /// </summary>
    public static class ViewAlpha
    {
        public static double Hold(double alpha, bool ended) => ended ? 1.0 : alpha;

        public static bool Ended(World w) => w != null && w.Match != null && w.Match.Phase == MatchPhase.Ended;
    }
}

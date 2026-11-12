using System;
using System.Collections.Generic;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>CM-21 (2): the bot heading metric (non-overlapping 3-tick windows, a jump = heading change > 30 deg between sampled ends).</summary>
    public static class HeadingMetric
    {
        public const int HeadingWindowTicks = 3;
        public const double HeadingJumpDeg = 30;
        public const double HeadingMinSpeedFraction = 0.5;

        /// <summary>Shortest absolute angle between two headings, radians.</summary>
        public static double HeadingDelta(double a, double b)
        {
            double d = a - b;
            d -= JsMath.Round(d / (2 * Math.PI)) * 2 * Math.PI;
            return Math.Abs(d);
        }

        /// <summary>Jump count for one actor: heading[t] is the heading after tick t (radians), sampled[t] whether tick t met the speed floor.</summary>
        public static int CountHeadingJumps(IReadOnlyList<double> heading, IReadOnlyList<bool> sampled)
        {
            double limit = (HeadingJumpDeg * Math.PI) / 180;
            int jumps = 0;
            for (int t = HeadingWindowTicks; t < heading.Count; t += HeadingWindowTicks)
            {
                int s = t - HeadingWindowTicks;
                if (!sampled[s] || !sampled[t]) continue;
                if (HeadingDelta(heading[t], heading[s]) > limit) jumps++;
            }
            return jumps;
        }
    }
}

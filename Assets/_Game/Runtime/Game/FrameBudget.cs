using System;
using System.Collections;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// perf-smooth-1: runs a stepped job (an IEnumerator whose MoveNext does one small unit of work) for at most a time
    /// budget per frame. It always runs at least one step, and starts no new step once the budget is used, so a frame's
    /// work is the budget plus at most one step. The clock is injected (tests use a fake one).
    /// </summary>
    public static class FrameBudget
    {
        /// <summary>Runs steps until the clock (ms, any origin) passes budgetMs from now; true when the job finished.</summary>
        public static bool Run(IEnumerator job, double budgetMs, Func<double> clockMs)
        {
            if (job == null) return true;
            double t0 = clockMs();
            do
            {
                if (!job.MoveNext()) return true;
            }
            while (clockMs() - t0 < budgetMs);
            return false;
        }
    }
}

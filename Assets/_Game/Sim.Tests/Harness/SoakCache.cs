using System.Collections.Generic;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>Runs each (batch, mode) once per test run and keeps the outcomes (the vitest beforeAll); matches run one after another, never in parallel.</summary>
    public static class SoakCache
    {
        private static readonly Dictionary<string, IReadOnlyList<MatchOutcome>> Cache = new Dictionary<string, IReadOnlyList<MatchOutcome>>();

        /// <summary>The outcomes of batch name played with actor 0 in mode, seed order.</summary>
        public static IReadOnlyList<MatchOutcome> Batch(string name, HumanMode mode)
        {
            OutcomeBatch b = OutcomeBatches.Get(name);
            string key = name + "/" + mode;
            if (Cache.TryGetValue(key, out IReadOnlyList<MatchOutcome> hit)) return hit;
            var output = new List<MatchOutcome>(b.Count);
            for (int k = 0; k < b.Count; k++)
                output.Add(OutcomeRunner.RunOutcomeMatch(b.FirstSeed + k, b.TeamSize, mode));
            IReadOnlyList<MatchOutcome> result = output.AsReadOnly();
            Cache[key] = result;
            return result;
        }
    }
}

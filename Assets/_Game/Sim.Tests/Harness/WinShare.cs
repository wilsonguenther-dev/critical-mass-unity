using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>
    /// b14-c item 5 (the owner's walls ruling, widened to every rule item and APPROVED): the ONE win-share judge. Every rule item's
    /// "team win share within 0.15 of the same-seed control" runs on at least MinMatches16 16v16 matches (Seeds16: the six ORDER
    /// seeds first, then 1 to 19 without 7), in the Slow category, prints both arms (Azure / Crimson / draws and both teams'
    /// shares) and is judged here, on both teams' shares. A 16v16 tally under MinMatches16 fails the judge: no six-match limit.
    /// </summary>
    public static class WinShare
    {
        public const double Limit = 0.15;
        public const int MinMatches16 = 24;

        /// <summary>The 24 16v16 seeds every rule item's win-share limit runs on (the six ORDER seeds first).</summary>
        public static readonly int[] Seeds16 = { 1337, 4242, 7, 99, 2024, 31337, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 };

        /// <summary>The ORDER 16v16 seeds (the first six of Seeds16).</summary>
        public static readonly int[] OrderSeeds16 = { 1337, 4242, 7, 99, 2024, 31337 };

        /// <summary>Seeds16 after the six ORDER seeds (for a test that already plays the ORDER seeds).</summary>
        public static readonly int[] ExtraSeeds16 = { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 };

        /// <summary>One arm's results.</summary>
        public sealed class Tally
        {
            public int Azure, Crimson, Draws;
            public int Matches => Azure + Crimson + Draws;
            public double AzureShare => Matches > 0 ? (double)Azure / Matches : 0;
            public double CrimsonShare => Matches > 0 ? (double)Crimson / Matches : 0;

            public void Add(MatchResult r)
            {
                if (r == MatchResult.Azure) Azure++;
                else if (r == MatchResult.Crimson) Crimson++;
                else Draws++;
            }

            public void Add(Tally t)
            {
                Azure += t.Azure;
                Crimson += t.Crimson;
                Draws += t.Draws;
            }

            public override string ToString() => "Azure " + Azure + " / Crimson " + Crimson + " / draws " + Draws + " of " + Matches +
                                                 " (Azure " + AzureShare.ToString("0.000") + ", Crimson " + CrimsonShare.ToString("0.000") + ")";
        }

        /// <summary>
        /// Judges one pool: both teams' shares within Limit of the control's. sixteen = the pool is the 16v16 one (then it must hold
        /// at least MinMatches16 matches in each arm). Prints one line with both arms and the verdict; returns pass.
        /// </summary>
        public static bool Judge(string item, string pool, Tally on, Tally control, bool sixteen)
        {
            double da = Math.Abs(on.AzureShare - control.AzureShare), dc = Math.Abs(on.CrimsonShare - control.CrimsonShare);
            bool big = !sixteen || (on.Matches >= MinMatches16 && control.Matches >= MinMatches16);
            bool ok = big && da <= Limit + 1e-9 && dc <= Limit + 1e-9 && on.Matches == control.Matches && on.Matches > 0;
            Console.WriteLine("[win share] " + item + ", " + pool + ": on " + on + "; control " + control + "; shift Azure " + da.ToString("0.000") +
                              ", Crimson " + dc.ToString("0.000") + " (limit " + Limit.ToString("0.00") + " each" +
                              (sixteen ? ", at least " + MinMatches16 + " 16v16 matches per arm" : "") + ") " + (ok ? "PASS" : "FAIL"));
            return ok;
        }
    }
}

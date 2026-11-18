namespace CriticalMass.Sim
{
    /// <summary>
    /// Version of the simulation: what a replay header stores and what two online games must share. Pure C#: this
    /// assembly has no UnityEngine reference.
    /// <para>b15-rejoin: the number now follows the rules instead of sitting at 1. RulesDigest is the fingerprint of a fixed
    /// set of reference matches (SimVersionTests); when a change to the sim moves it, that test fails and says to bump
    /// Schema and paste the printed digest. A replay from another Schema is refused with "This replay is from a different
    /// game version" instead of playing to its first fingerprint mismatch; a room refuses a joiner whose Schema or
    /// RulesDigest differs, in plain words.</para>
    /// </summary>
    public static class SimVersion
    {
        /// <summary>History: 1 = the browser port through batch 14 (never bumped); 2 = b15-rejoin full-state fingerprint.</summary>
        public const int Schema = 2;

        /// <summary>The reference-match digest this Schema was pinned for (printed by SimVersionTests).</summary>
        public const ulong RulesDigest = 0x0000000000000000UL;
    }
}

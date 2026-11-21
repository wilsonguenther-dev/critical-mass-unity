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
        /// <summary>History: 1 = the browser port through batch 14 (never bumped); 2 = b15-rejoin full-state fingerprint;
        /// 3 = b15 terrain height (MapDef.Lift is sim state; older replays are "from a different game version");
        /// 4 = batch 15 wave two (combat feel, bot brain, comeback + hut choices: new hashed sim state and rules).</summary>
        public const int Schema = 4;

        /// <summary>The reference-match digest this Schema was pinned for (printed by SimVersionTests).</summary>
        public const ulong RulesDigest = 0xC3FD3B160874096BUL;   // batch 15 wave two (schema 4): printed by sim run 44856207208 at 7fbcbddc
    }
}

// CM b13 item K (per-team feed plans from the seed). S0 stub: identity until item K fills it.
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>CM b13 item K: per-team feed plans from the seed. S0 stub: identity until item K fills it.</summary>
    public static class BotKaijuFeed
    {
        /// <summary>The plant-gather utility multiplier for bot id (1 = today).</summary>
        public static double GatherScale(World w, BotsState st, int id) => 1.0;
        /// <summary>True when supply bot id holding kind feeds the egg instead of banking (false = today).</summary>
        public static bool SupplyFeedsEgg(World w, BotsState st, int id, BiomassType kind) => false;
    }
}

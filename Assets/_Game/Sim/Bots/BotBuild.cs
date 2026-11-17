// CM b13 item B (Builder bots build walls and blocks). S0 stub: identity until item B fills it.
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>CM b13 item B: Builder bots build walls and blocks. S0 stub: identity until item B fills it.</summary>
    public static class BotBuild
    {
        /// <summary>The Builder's decision; true = decided here (BotFsm.DecideBuilder skips the rest).</summary>
        public static bool DecideBuilder(World w, BotsState st, int id, AiState s) => false;
    }
}

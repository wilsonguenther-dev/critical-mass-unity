// CM b13 item H (Miner bots mine ore and bank it at the hut). S0 stub: identity until item H fills it.
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>CM b13 item H: Miner bots mine ore and bank it at the hut. S0 stub: identity until item H fills it.</summary>
    public static class BotMine
    {
        /// <summary>Offers the Miner's task utilities (BotFsm.ConsiderExternal) before the state is chosen.</summary>
        public static void Consider(World w, BotsState st, int id) { }
        /// <summary>The Miner's ability decision; true = decided here (BotFsm skips the rest).</summary>
        public static bool DecideAbility(World w, BotsState st, int id, AiState s) => false;
        /// <summary>Acts at the task point (TaskKind 1-9); true = the frame was written.</summary>
        public static bool Act(World w, BotsState st, int id, InputFrame o) => false;
    }
}

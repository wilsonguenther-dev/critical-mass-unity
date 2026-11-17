// CM b13 item C (crystal runners, feeders, guards and purifiers). S0 stub: identity until item C fills it.
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>CM b13 item C: crystal runners, feeders, guards and purifiers. S0 stub: identity until item C fills it.</summary>
    public static class BotCorrupt
    {
        /// <summary>Offers the corruption task utilities (BotFsm.ConsiderExternal) before the state is chosen.</summary>
        public static void Consider(World w, BotsState st, int id) { }
        /// <summary>Acts at the task point (TaskKind 20-29); true = the frame was written.</summary>
        public static bool Act(World w, BotsState st, int id, InputFrame o) => false;
    }
}

using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Options of one match (browser <c>WorldOptions</c>); <c>Config</c> is the config every Sim function reads.</summary>
    public sealed class WorldOptions
    {
        public int Seed;
        public int TeamSize;
        public TeamId HumanTeam;
        public ClassId HumanClass;
        public GameConfig Config;
        /// <summary>
        /// DG-21: skill of the bots on the team the human does NOT play (the match-setup "Bot difficulty" choice). Default
        /// Normal = the browser bots exactly.
        /// </summary>
        public BotDifficulty BotDifficulty;
        /// <summary>DG-21: skill of the bots on the human's own team (actor 0 on autopilot included). Default Normal; the menu leaves it.</summary>
        public BotDifficulty AllyBotDifficulty;
    }
}

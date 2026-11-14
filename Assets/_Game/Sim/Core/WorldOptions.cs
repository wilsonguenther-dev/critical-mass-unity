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
        /// <summary>
        /// Sim-physics lane (OD-1): the match pace (sim ticks per wall second / SimHz) the match is played at; 0 = the
        /// config's DefaultSpeed. Durations converts every real-second reaction window and floor with it once per world, so a
        /// telegraph lasts its minimum on screen whatever the pace. The match setup passes its pace choice here.
        /// </summary>
        public double Pace;
        /// <summary>
        /// online-rooms-2: actor ids, other than actor 0, that a second, third... human plays (an online room). Null or empty =
        /// single player exactly as before (actor 0 is the only IsHuman actor). Ids index the roster: the HumanTeam holds
        /// ids 0..n-1, the other team n..2n-1. Ids outside the roster are ignored.
        /// </summary>
        public int[] ExtraHumans;
        /// <summary>online-rooms-2: the class each ExtraHumans entry picked in the room (same length; missing = the roster's class).</summary>
        public ClassId[] ExtraHumanClasses;
    }
}

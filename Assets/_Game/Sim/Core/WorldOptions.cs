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
    }
}

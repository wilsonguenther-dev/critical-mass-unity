using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Sim-physics lane switches (PhysicsConfig): a rule is on when its own switch is on and Classic (browser parity) is off.</summary>
    public static class Phys
    {
        /// <summary>True when flag is on and the world is not in Classic (browser parity) mode.</summary>
        public static bool On(World w, bool flag) => flag && !w.Config.Physics.Classic;

        /// <summary>True when flag is on and the config is not in Classic mode.</summary>
        public static bool On(GameConfig c, bool flag) => flag && !c.Physics.Classic;
    }
}

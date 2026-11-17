// CM b13 item J (dodge for every class). S0 stub: identity until item J fills it.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>CM b13 item J: the dodges state on the World (World.Dodges). S0 stub: identity until item J fills it.</summary>
    public sealed class DodgeState
    {
        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w) { }

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w) { }
    }

    /// <summary>CM b13 item J: a short invulnerable dash for every class. S0 stub: identity until item J fills it.</summary>
    public static class Dodge
    {
        /// <summary>The rule is on (item J: DepthRules.On(w, w.Config.Depth.Dodge.On)).</summary>
        public static bool On(World w) => false;
        /// <summary>Moves actor id this tick when it dodges; true = its movement was handled (Movement skips the walk).</summary>
        public static bool Step(World w, int id, double dt) => false;
        /// <summary>True when actor id ignores damage of this cause now (dodge invulnerability).</summary>
        public static bool Immune(World w, int id, DamageCause cause) => false;
        /// <summary>True while actor id dodges.</summary>
        public static bool Active(World w, int id) => false;
        /// <summary>Ticks until actor id may dodge again (0 = ready).</summary>
        public static int CooldownTicks(World w, int id) => 0;
        /// <summary>0..1 through the dash at render fraction alpha; -1 when not dodging.</summary>
        public static double Phase(World w, int id, double alpha) => -1;
    }
}

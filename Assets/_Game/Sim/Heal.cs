using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// CM-37: the healing fountain (browser src/sim/heal.ts, 09e0904). Each team's base has a fountain zone centred on its own
    /// respawn pad (Map.Pads[team], radius Heal.ZoneRadius). Every sim tick a living actor inside its OWN team's zone with
    /// Hp &lt; MaxHp regains Heal.HpPerTick (clamped to MaxHp) and one Heal event is pushed (A = actor, B = -1, at the actor,
    /// Value = HP restored this tick). Enemies standing in the zone never heal. Time is counted in ticks, never dt.
    /// Allocation-free; config is read at call time.
    /// </summary>
    public static class Heal
    {
        /// <summary>True when (x, z) lies inside team's fountain zone.</summary>
        public static bool InFountainZone(World w, TeamId team, double x, double z)
        {
            PadDef pad = w.Map.Pads[(int)team];
            double r = w.Config.Heal.ZoneRadius;
            double dx = x - pad.X;
            double dz = z - pad.Z;
            return dx * dx + dz * dz <= r * r;
        }

        /// <summary>One tick of fountain healing. Returns the number of actors healed this tick (tests, debug).</summary>
        public static int HealSystem(World w)
        {
            double per = w.Config.Heal.HpPerTick;
            if (!(per > 0)) return 0;
            int healed = 0;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || !(a.Hp < a.MaxHp)) continue;
                double x = w.X[i];
                double z = w.Z[i];
                if (!InFountainZone(w, a.Team, x, z)) continue;
                double room = a.MaxHp - a.Hp;
                double amount = per < room ? per : room;
                a.Hp += amount;
                healed++;
                World.PushEvent(w, SimEventType.Heal, i, -1, x, z, amount);
            }
            return healed;
        }
    }
}

using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// b16 KAIJU WAITS OFF THE CIRCLE (mechanics audit ranked #3): with no target, a sided Kaiju used to walk to 1.5 u of the hill
    /// centre, the one spot where KAIJU HOLDS NOTHING zeroes its own team's score (60 s a match), and its own bots then escorted
    /// it off. It now waits on the hill's edge outside the scoring ring (MechRules.KaijuIdleRadius), on the side facing the other
    /// team's pad, facing in. Its targeting is unchanged, so it still walks onto the hill to fight an enemy who steps on it.
    /// </summary>
    public sealed partial class Kaiju
    {
        /// <summary>Candidate turns from the enemy-pad direction (radians, 25 degree steps, nearest first).</summary>
        internal static readonly double[] IdleTurns = { 0, 0.4363323129985824, -0.4363323129985824, 0.8726646259971648, -0.8726646259971648,
            1.3089969389957472, -1.3089969389957472, 1.7453292519943295, -1.7453292519943295 };

        /// <summary>
        /// Kaiju.Ai movement, the no-target branch of a sided Kaiju: replaces the hill-centre goal with the idle point on the rim
        /// (the first candidate cell that is a clearance cell and lies outside the ring). True = replaced; at the point it faces
        /// the hill centre. Allocation-free, no RNG; Full rules only (MechRulesConfig.KaijuIdleOffHill).
        /// </summary>
        internal static bool IdleOffHillGoal(World w, ref double gx, ref double gz, ref double stop)
        {
            Kaiju k = w.Kaiju;
            if (!MechRules.KaijuIdleOn(w)) return false;
            if (k.State != KaijuState.Azure && k.State != KaijuState.Crimson) return false;
            HillDef hill = w.Map.Hill;
            PadDef enemy = w.Map.Pads[k.State == KaijuState.Azure ? (int)TeamId.Crimson : (int)TeamId.Azure];
            double ux = enemy.X - hill.X, uz = enemy.Z - hill.Z;
            double l = Math.Sqrt(ux * ux + uz * uz);
            if (l < 1e-6)
            {
                ux = 1;
                uz = 0;
                l = 1;
            }
            ux /= l;
            uz /= l;
            double r = MechRules.KaijuIdleRadius(w);
            double ring = w.Config.Hill.Radius + w.Config.Kaiju.Radius;
            RefreshClearance(w);
            for (int i = 0; i < IdleTurns.Length; i++)
            {
                double c = JsMath.Cos(IdleTurns[i]), s = JsMath.Sin(IdleTurns[i]);
                double dx = ux * c - uz * s, dz = ux * s + uz * c;
                int cx = JsMath.FloorToInt(hill.X + dx * r), cz = JsMath.FloorToInt(hill.Z + dz * r);
                if (!IsClear(w, cx, cz)) continue;
                double ox = cx + 0.5 - hill.X, oz = cz + 0.5 - hill.Z;
                double hold = Math.Max(0.05, w.Config.Depth.Mech.KaijuIdleStop);
                if (Math.Sqrt(ox * ox + oz * oz) - hold < ring) continue;   // even at the stop distance it stays outside the ring
                gx = cx + 0.5;
                gz = cz + 0.5;
                stop = hold;
                double ex = gx - k.X, ez = gz - k.Z;
                if (ex * ex + ez * ez <= stop * stop) k.Facing = JsMath.Atan2(hill.Z - k.Z, hill.X - k.X);
                return true;
            }
            return false;
        }
    }
}

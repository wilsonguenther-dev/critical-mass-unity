using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// DG-02 (CM-32 view, browser Actors.tsx): the jump arc drawn on an airborne actor. The sim owns the numbers
    /// (Jump.JumpPhase, Jump.JumpLift); this only turns them into an offset above the ground height ActorViews already
    /// used at the interpolated point: base = lerp(ground at takeoff, ground at the planned landing, phase) + lift.
    /// Zero on the ground. Reads the Sim only. The drawn arc is reshaped by ViewLift (rise fast, hang, fall faster).
    /// </summary>
    public static class JumpLift
    {
        /// <summary>Unity offset to add to actor i's drawn position at render alpha (x, z: the interpolated sim point).</summary>
        public static Vector3 Offset(World w, MapDef map, int i, double alpha, double x, double z)
        {
            Actor a = w.Actors[i];
            double u = Jump.JumpPhase(a, alpha);
            if (u < 0) return Vector3.zero;
            double y0 = GroundHeight.At(map, a.AirFromX, a.AirFromZ);
            double y = y0 + (GroundHeight.At(map, a.AirToX, a.AirToZ) - y0) * u + ViewLift(w.Config, a, u);
            return Vector3.up * (float)(y - GroundHeight.At(map, x, z));
        }

        /// <summary>Phase skew (more than 1 = the fall is quicker than the rise) and top flattening (under 1 = a longer hang).</summary>
        public const double FallSkew = 1.2, HangPower = 0.75;

        /// <summary>
        /// DG-90 (S15 view arc, animation pass 2026-11-12): the drawn height over the sim's parabola, reshaped to rise fast,
        /// hang at the top and fall a little faster than it rose. Same apex, zero at take-off and landing; the sim's flight,
        /// landing point and timing are untouched (view only).
        /// </summary>
        public static double ViewLift(CriticalMass.Sim.Config.GameConfig c, Actor a, double u)
        {
            double peak = Jump.JumpLift(c, a, 0.5);
            if (!(peak > 1e-9) || !(u > 0) || !(u < 1)) return 0;
            double skew = System.Math.Pow(u, FallSkew);
            double shape = Jump.JumpLift(c, a, skew) / peak;
            return peak * System.Math.Pow(System.Math.Max(0, shape), HangPower);
        }
    }
}

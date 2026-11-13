using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// DG-02 (CM-32 view, browser Actors.tsx): the jump arc drawn on an airborne actor. The sim owns the numbers
    /// (Jump.JumpPhase, Jump.JumpLift); this only turns them into an offset above the ground height ActorViews already
    /// used at the interpolated point: base = lerp(ground at takeoff, ground at the planned landing, phase) + lift.
    /// Zero on the ground. Reads the Sim only.
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
            double y = y0 + (GroundHeight.At(map, a.AirToX, a.AirToZ) - y0) * u + Jump.JumpLift(w.Config, a, u);
            return Vector3.up * (float)(y - GroundHeight.At(map, x, z));
        }
    }
}

// b16 physics fixes (physics audit 2026-11-21): the Kaiju's appended state and its give-way for a pinned fighter.
using System;

namespace CriticalMass.Sim.Core
{
    public sealed partial class Kaiju
    {
        /// <summary>b16 [2]: the blast slide velocity (u/s), decaying per tick (PhysicsFeel.KaijuPushStep); 0 without the rule.</summary>
        public double PushVx;
        public double PushVz;

        /// <summary>
        /// b16 [1]: a fighter pinned against a wall with no free spot on its own side holds the Kaiju back: the body steps
        /// (dx, dz) away from it. From a spot its wall collider fits, only to a spot it fits (else the ordinary shove through the
        /// collider, which may refuse); from a spot it already overlaps a wall at (a squeeze, a scripted test), the step is taken
        /// as long as it does not make that worse at the destination than leaving it where it is (the step points back the way
        /// it came). Returns true when it moved.
        /// </summary>
        internal static bool PinGiveWay(World w, double dx, double dz)
        {
            if (double.IsNaN(dx) || double.IsNaN(dz) || (dx == 0 && dz == 0)) return false;
            Kaiju k = w.Kaiju;
            double x0 = k.X, z0 = k.Z;
            double nx = JsMath.F32(x0 + dx), nz = JsMath.F32(z0 + dz);
            bool wasClear = CircleClear(w);
            if (!wasClear || CircleClearAt(w, nx, nz, WallR(w)))
            {
                k.X = nx;
                k.Z = nz;
                return true;
            }
            ShoveKaiju(w, dx, dz);
            return k.X != x0 || k.Z != z0;
        }
    }
}

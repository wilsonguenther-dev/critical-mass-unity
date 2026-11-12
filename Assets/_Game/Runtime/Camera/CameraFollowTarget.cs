using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// The Cinemachine follow target (CameraRig.tsx follow, CM-27 (1)): actor 0's interpolated position plus an eased
    /// look-ahead, clamped to the inset map bounds, smoothed by a critically damped spring per axis; teleports snap.
    /// </summary>
    public sealed class CameraFollowTarget : MonoBehaviour
    {
        public SimDriverBehaviour Driver;

        private readonly Spring1 _sx = CameraSpring.CreateSpring(0);
        private readonly Spring1 _sz = CameraSpring.CreateSpring(0);
        private double _easeX;
        private double _easeZ;
        private double _lastGoalX;
        private double _lastGoalZ;
        private bool _hasLast;

        private void LateUpdate()
        {
            if (Driver == null) return;
            World w = Driver.World;
            if (w == null || w.ActorCount <= 0) return;

            double dt = Driver.FrameDt;
            double a = Driver.Alpha;
            double px = w.PrevX[0] + (w.X[0] - w.PrevX[0]) * a;
            double pz = w.PrevZ[0] + (w.Z[0] - w.PrevZ[0]) * a;

            double lax = w.Vx[0] * CameraSpring.LookAheadSeconds;
            double laz = w.Vz[0] * CameraSpring.LookAheadSeconds;
            double ll = Math.Sqrt(lax * lax + laz * laz);
            if (ll > CameraSpring.LookAheadMax)
            {
                lax *= CameraSpring.LookAheadMax / ll;
                laz *= CameraSpring.LookAheadMax / ll;
            }
            double k = 1 - Math.Exp(-dt / CameraSpring.LookAheadTau);
            _easeX += (lax - _easeX) * k;
            _easeZ += (laz - _easeZ) * k;

            BoundsDef b = w.Map.Bounds;
            double inset = Driver.Config.Render.CameraBoundsInset;
            double goalX = Clamp(px + _easeX, b.MinX + inset, b.MaxX - inset);
            double goalZ = Clamp(pz + _easeZ, b.MinZ + inset, b.MaxZ - inset);

            bool teleport = !_hasLast;
            if (!teleport)
            {
                double dx = goalX - _lastGoalX;
                double dz = goalZ - _lastGoalZ;
                double speed = Math.Sqrt(dx * dx + dz * dz) / dt;
                teleport = speed > CameraSpring.TeleportSpeed;
            }
            if (teleport)
            {
                _sx.X = goalX;
                _sx.V = 0;
                _sz.X = goalZ;
                _sz.V = 0;
            }
            else
            {
                CameraSpring.SpringStep(_sx, goalX, CameraSpring.FollowOmega, dt);
                CameraSpring.SpringStep(_sz, goalZ, CameraSpring.FollowOmega, dt);
            }
            _lastGoalX = goalX;
            _lastGoalZ = goalZ;
            _hasLast = true;

            transform.position = SimSpace.ToWorld(_sx.X, 0, _sz.X);
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}

using System;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>One axis of a critically damped spring: position and velocity (browser rig.ts <c>Spring1</c>).</summary>
    public sealed class Spring1
    {
        public double X;
        public double V;
    }

    /// <summary>Port of src/game/rig.ts (09e0904): the CM-27 (1) follow spring and the CameraRig.tsx follow constants.</summary>
    public static class CameraSpring
    {
        /// <summary>CM-27 (1) follow spring natural frequency (rad/s), critically damped.</summary>
        public const double FollowOmega = 9;
        /// <summary>Look-ahead = render velocity x this (s), capped at LookAheadMax (u).</summary>
        public const double LookAheadSeconds = 0.25;
        public const double LookAheadMax = 2.0;
        /// <summary>Look-ahead easing time constant (s).</summary>
        public const double LookAheadTau = 0.1;
        /// <summary>A frame-to-frame jump faster than this (u/s) is a teleport (respawn, restart), not movement.</summary>
        public const double TeleportSpeed = 20;

        /// <summary>A spring at rest at x.</summary>
        public static Spring1 CreateSpring(double x)
        {
            return new Spring1 { X = x, V = 0 };
        }

        /// <summary>Advance a critically damped spring toward a fixed target by dt seconds with the exact solution (stable for any dt).</summary>
        public static void SpringStep(Spring1 s, double target, double omega, double dt)
        {
            if (!(dt > 0)) return;
            double y = s.X - target;
            double n = s.V + omega * y;
            double e = Math.Exp(-omega * dt);
            s.X = target + (y + n * dt) * e;
            s.V = (s.V - n * omega * dt) * e;
        }

        /// <summary>Smallest whole number of sim ticks after which a spring released from rest at distance d is within eps of its target.</summary>
        public static int SpringSettleTicks(double d, double eps, double omega, double hz)
        {
            if (!(d > eps)) return 0;
            double r = eps / d;
            double x = 0;
            while ((1 + x) * Math.Exp(-x) > r) x += 1e-4;
            return (int)Math.Ceiling(((x / omega) * hz) - 1e-9);
        }
    }
}

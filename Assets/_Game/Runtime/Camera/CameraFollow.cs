using System;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>One frame's inputs to <see cref="CameraFollow.Step"/>, all in sim space.</summary>
    public struct CameraFollowInput
    {
        /// <summary>Frame time (s), the driver's FrameDt.</summary>
        public double Dt;
        /// <summary>Interpolated human position and the ground height under it (GroundHeight.At).</summary>
        public double HumanX, HumanZ, HumanY;
        /// <summary>The pointer's ground aim point, when HasAim.</summary>
        public bool HasAim;
        public double AimX, AimZ;
        /// <summary>Look target clamp box (map bounds inset by CameraBoundsInset).</summary>
        public double MinX, MaxX, MinZ, MaxZ;
        /// <summary>World.Epoch: a change is a new match and snaps the camera (CameraRig.tsx snap(world)).</summary>
        public int Epoch;
        /// <summary>Override target (capture tool, the browser's DIRECTOR.overrideOn); OverrideY is its ground height.</summary>
        public bool Override;
        public double OverrideX, OverrideZ, OverrideY;
    }

    /// <summary>
    /// The gameplay follow (pure, testable). Port of CameraRig.tsx 418-529 at 09e0904 (review advisory R20261112T1918-2-2)
    /// with the U-005 amendment (2026-11-12) on top:
    /// browser: (1) look-ahead from the RENDER velocity of the interpolated human (frame-to-frame), (2) a frame jump over
    /// TeleportSpeed only zeroes that velocity, the spring flies home (snaps only on a new match), (3) the goal is
    /// clamped, then after the spring step each axis is clamped again with its velocity zeroed, (4) a third spring on Y
    /// toward the human's ground height.
    /// amendment: the look target adds a fixed 2.0 u toward screen-up, the look-ahead cap is 3.0 u (was 2.0) and the
    /// pointer aim adds an aim look-ahead toward the cursor (gain 0.25) inside that cap.
    /// </summary>
    public sealed class CameraFollow
    {
        /// <summary>Screen-up direction on the sim ground (HumanInputState basis, yaw 45: (-1, -1) / sqrt 2).</summary>
        private readonly double _upX, _upZ;

        public readonly Spring1 SX = CameraSpring.CreateSpring(0);
        public readonly Spring1 SY = CameraSpring.CreateSpring(0);
        public readonly Spring1 SZ = CameraSpring.CreateSpring(0);
        /// <summary>The eased look-ahead (u), the browser's LA.</summary>
        public double LaX, LaZ;

        private double _lastX, _lastZ;
        private bool _hasLast;
        private int _epoch;
        private bool _hasEpoch;
        private bool _wasOverride;

        public CameraFollow(double screenUpX, double screenUpZ)
        {
            _upX = screenUpX;
            _upZ = screenUpZ;
        }

        /// <summary>Start framed on (x, y, z) from rest, with no look-ahead (CameraRig.tsx snap).</summary>
        public void Snap(double x, double y, double z)
        {
            SX.X = x; SY.X = y; SZ.X = z;
            SX.V = SY.V = SZ.V = 0;
            LaX = LaZ = 0;
            _hasLast = false;
        }

        /// <summary>The next Step with Override set frames its target at once, even if an override was already on.</summary>
        public void SnapToNextOverride() => _wasOverride = false;

        /// <summary>Advance one rendered frame; the look target is (SX.X, SY.X, SZ.X) afterwards.</summary>
        public void Step(in CameraFollowInput f)
        {
            double dt = f.Dt;
            double hx = f.HumanX, hz = f.HumanZ;
            double screenOffset = GameplayCamera.ScreenUpOffset;

            // A new match: start framed on the human again.
            if (!_hasEpoch || f.Epoch != _epoch)
            {
                _hasEpoch = true;
                _epoch = f.Epoch;
                Snap(Clamp(hx + _upX * screenOffset, f.MinX, f.MaxX), f.HumanY, Clamp(hz + _upZ * screenOffset, f.MinZ, f.MaxZ));
            }

            // (1) Render velocity of the interpolated human; a teleport zeroes it (browser rule), nothing snaps.
            double vx = 0, vz = 0;
            if (_hasLast && dt > 0)
            {
                vx = (hx - _lastX) / dt;
                vz = (hz - _lastZ) / dt;
                if (Math.Sqrt(vx * vx + vz * vz) > CameraSpring.TeleportSpeed) vx = vz = 0;
            }
            _lastX = hx;
            _lastZ = hz;
            _hasLast = true;

            double dx = vx * CameraSpring.LookAheadSeconds;
            double dz = vz * CameraSpring.LookAheadSeconds;
            if (f.HasAim && !f.Override)
            {
                dx += (f.AimX - hx) * GameplayCamera.AimLookAheadGain;
                dz += (f.AimZ - hz) * GameplayCamera.AimLookAheadGain;
            }
            if (f.Override) dx = dz = 0;
            double dl = Math.Sqrt(dx * dx + dz * dz);
            if (dl > GameplayCamera.LookAheadMax)
            {
                dx *= GameplayCamera.LookAheadMax / dl;
                dz *= GameplayCamera.LookAheadMax / dl;
            }
            if (dt > 0)
            {
                double k = 1 - Math.Exp(-dt / CameraSpring.LookAheadTau);
                LaX += (dx - LaX) * k;
                LaZ += (dz - LaZ) * k;
            }

            // (3) Goal: human + look-ahead + the screen-up offset (or the override), clamped.
            double gx, gz, ty;
            if (f.Override)
            {
                gx = f.OverrideX;
                gz = f.OverrideZ;
            }
            else
            {
                gx = hx + LaX + _upX * screenOffset;
                gz = hz + LaZ + _upZ * screenOffset;
            }
            double tx = Clamp(gx, f.MinX, f.MaxX);
            double tz = Clamp(gz, f.MinZ, f.MaxZ);
            ty = f.Override ? f.OverrideY : f.HumanY;

            // The override frames its target at once (the browser's DIRECTOR.snap on switching it on).
            if (f.Override && !_wasOverride)
            {
                SX.X = tx; SY.X = ty; SZ.X = tz;
                SX.V = SY.V = SZ.V = 0;
            }
            _wasOverride = f.Override;

            CameraSpring.SpringStep(SX, tx, CameraSpring.FollowOmega, dt);
            CameraSpring.SpringStep(SZ, tz, CameraSpring.FollowOmega, dt);
            CameraSpring.SpringStep(SY, ty, CameraSpring.FollowOmega, dt);
            if (SX.X < f.MinX || SX.X > f.MaxX)
            {
                SX.X = Clamp(SX.X, f.MinX, f.MaxX);
                SX.V = 0;
            }
            if (SZ.X < f.MinZ || SZ.X > f.MaxZ)
            {
                SZ.X = Clamp(SZ.X, f.MinZ, f.MaxZ);
                SZ.V = 0;
            }
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}

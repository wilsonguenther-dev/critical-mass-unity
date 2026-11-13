using System;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// Which device aims, and where a controller aims (pure C#, view side only; the sim sees an aim point exactly as from
    /// the mouse). The mouse aims when it moved or clicked last; a controller aims when one of its sticks moved last.
    /// Controller aim: the right stick's direction when it is pushed, otherwise the move direction (left stick or keys),
    /// otherwise the last direction used. The point sits StickAimDistance ahead of actor 0 (the bots' aim-ahead), so the
    /// sim's ability clamps and barricade placement see a point at a sensible range. Directions are screen axes (right,
    /// up) mapped to sim XZ by the same yaw-45 basis HumanInputState uses for movement, so "stick up" aims up the screen.
    /// </summary>
    public sealed class AimResolver
    {
        public enum Mode { Pointer, Stick }

        /// <summary>Sim units from actor 0 to the controller aim point (BotSteer.AimAhead is 3).</summary>
        public const double StickAimDistance = 3;

        /// <summary>The device that aims now.</summary>
        public Mode Current { get; private set; } = Mode.Pointer;

        bool _hasDir;
        double _dirX;
        double _dirZ;

        /// <summary>The mouse moved or clicked: it aims from now on.</summary>
        public void NotePointer() => Current = Mode.Pointer;

        /// <summary>A controller stick moved: the controller aims from now on.</summary>
        public void NoteStick() => Current = Mode.Stick;

        /// <summary>Back to mouse aim with no remembered direction.</summary>
        public void Reset()
        {
            Current = Mode.Pointer;
            _hasDir = false;
            _dirX = 0;
            _dirZ = 0;
        }

        /// <summary>Screen axes (right, up) to a unit sim XZ direction; false for a zero vector.</summary>
        public static bool ScreenToSimDir(double right, double up, out double x, out double z)
        {
            x = right * HumanInputState.ScreenRightX + up * HumanInputState.ScreenUpX;
            z = right * HumanInputState.ScreenRightZ + up * HumanInputState.ScreenUpZ;
            double len = Math.Sqrt(x * x + z * z);
            if (len < 1e-9)
            {
                x = 0;
                z = 0;
                return false;
            }
            x /= len;
            z /= len;
            return true;
        }

        /// <summary>
        /// The controller aim point around actor 0 at (px, pz). stick and move are screen axes, already dead-zoned (zero
        /// inside the dead zone). False (no aim) until any direction has been seen.
        /// </summary>
        public bool ResolveStick(double stickRight, double stickUp, double moveRight, double moveUp, double px, double pz,
            out double aimX, out double aimZ)
        {
            if (ScreenToSimDir(stickRight, stickUp, out double dx, out double dz) ||
                ScreenToSimDir(moveRight, moveUp, out dx, out dz))
            {
                _hasDir = true;
                _dirX = dx;
                _dirZ = dz;
            }
            if (!_hasDir)
            {
                aimX = 0;
                aimZ = 0;
                return false;
            }
            aimX = px + _dirX * StickAimDistance;
            aimZ = pz + _dirZ * StickAimDistance;
            return true;
        }
    }
}

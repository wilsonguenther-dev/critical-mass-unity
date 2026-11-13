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
        /// <summary>Pointer = the mouse aims; Stick = a controller aims; Keys = keyboard only (controls-1): the fighter faces
        /// where it moves, keeps that facing standing still, LockFacing holds it, and target assist bends it toward the nearest
        /// enemy in a forward cone.</summary>
        public enum Mode { Pointer, Stick, Keys }

        /// <summary>Keyboard target assist: enemies this far (sim units) and inside this half-angle of the facing pull the aim.</summary>
        public const double AssistRange = 7;
        public const double AssistHalfAngleDeg = 30;

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

        /// <summary>A keyboard attack key with the mouse resting (or keyboard-only play from the start): the keys aim.</summary>
        public void NoteKeys() => Current = Mode.Keys;

        /// <summary>Sets the mode directly (HumanInputSource at enable, from ControlBindings.ActiveScheme).</summary>
        public void SetMode(Mode m) => Current = m;

        /// <summary>Forget the keyboard facing (a respawn faces the hill; the keys take the actor's new facing from there).</summary>
        public void ForgetKeysDirection() => _hasDir = false;

        /// <summary>The keyboard facing direction (sim XZ unit) is known.</summary>
        public bool HasKeysDirection => _hasDir;

        /// <summary>The keyboard aim targeted an enemy on the last ResolveKeys (tests, HUD).</summary>
        public int AssistTarget { get; private set; } = -1;

        /// <summary>Back to mouse aim with no remembered direction.</summary>
        public void Reset()
        {
            Current = Mode.Pointer;
            _hasDir = false;
            _dirX = 0;
            _dirZ = 0;
            AssistTarget = -1;
        }

        /// <summary>
        /// Keyboard aim (Mode.Keys) around actor at (px, pz). moveX/moveZ: this frame's move in sim XZ (zero = standing);
        /// facing: the actor's sim facing in radians, used until a move was seen; locked: LockFacing held, the direction
        /// stays as it is and no assist applies. Assist: among the enemies (enemyX/enemyZ, count n), the nearest inside
        /// AssistRange and AssistHalfAngleDeg of the direction is aimed at directly (the remembered direction is not changed,
        /// so the fighter turns back to its move facing when the target leaves the cone). Always true: a facing is always
        /// known. Pure: the sim only ever sees the resulting point, exactly like a mouse aim point.
        /// </summary>
        public bool ResolveKeys(double moveX, double moveZ, double px, double pz, double facing, bool locked,
            double[] enemyX, double[] enemyZ, int n, out double aimX, out double aimZ)
        {
            AssistTarget = -1;
            double len = Math.Sqrt(moveX * moveX + moveZ * moveZ);
            if (!locked && len > 1e-9)
            {
                _dirX = moveX / len;
                _dirZ = moveZ / len;
                _hasDir = true;
            }
            else if (!_hasDir)
            {
                _dirX = Math.Cos(facing);
                _dirZ = Math.Sin(facing);
                _hasDir = true;
            }
            aimX = px + _dirX * StickAimDistance;
            aimZ = pz + _dirZ * StickAimDistance;
            if (locked || enemyX == null || enemyZ == null) return true;
            double cosLimit = Math.Cos(AssistHalfAngleDeg * Math.PI / 180), best = AssistRange * AssistRange;
            for (int i = 0; i < n && i < enemyX.Length && i < enemyZ.Length; i++)
            {
                double ex = enemyX[i] - px, ez = enemyZ[i] - pz;
                double d2 = ex * ex + ez * ez;
                if (d2 < 1e-6 || d2 > best) continue;
                double d = Math.Sqrt(d2);
                if ((ex * _dirX + ez * _dirZ) / d < cosLimit) continue;
                best = d2;
                AssistTarget = i;
            }
            if (AssistTarget >= 0)
            {
                // Aim at the target, never closer than the facing dead zone allows (3 units out along its line).
                double ex = enemyX[AssistTarget] - px, ez = enemyZ[AssistTarget] - pz;
                double d = Math.Sqrt(ex * ex + ez * ez);
                double r = Math.Max(d, StickAimDistance) / d;
                aimX = px + ex * r;
                aimZ = pz + ez * r;
            }
            return true;
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

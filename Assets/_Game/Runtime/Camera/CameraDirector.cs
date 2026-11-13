using System;
using CriticalMass.Runtime.Input;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>One frame's inputs to <see cref="CameraDirector.Step"/>, all in sim space.</summary>
    public struct CameraDirectorInput
    {
        /// <summary>Frame time (s), the driver's FrameDt (view clock: zoom easing and the combat hold).</summary>
        public double Dt;
        /// <summary>Interpolated sim time (s): the hatch focus runs on it so a capture replays it identically.</summary>
        public double SimTime;
        /// <summary>The human's interpolated position.</summary>
        public double HumanX, HumanZ;
        /// <summary>Living enemies within <see cref="CameraDirector.CombatRadius"/> of the human (CameraFollowTarget counts them).</summary>
        public int EnemiesNear;
        /// <summary>True from the hatch on; HatchSimTime is the sim time it was first seen, (HatchX, HatchZ) where.</summary>
        public bool Hatched;
        public double HatchSimTime;
        public double HatchX, HatchZ;
        /// <summary>Hold-to-look is held (middle mouse, or the right stick click); Look* is the screen direction, length 0..1.</summary>
        public bool Looking;
        public double LookRight, LookUp;
        /// <summary>The capture tool's override is on: every rule stands down.</summary>
        public bool Override;
    }

    /// <summary>
    /// DG-20 camera polish rules on top of the U-005 follow (pure, testable, view only: nothing here reaches the sim).
    /// The outputs are a distance multiplier for the rig (<see cref="Zoom"/>) and a sim-space goal offset for
    /// <see cref="CameraFollow"/> (<see cref="OffsetX"/>, <see cref="OffsetZ"/>); the follow spring smooths the offset.
    /// <list type="bullet">
    /// <item>Combat framing: 3 or more living enemies within 8 u zoom the camera out 12% (distance x 1.12), held 1 s
    /// after the crowd thins so it never pumps.</item>
    /// <item>Hatch focus: for 2.0 sim seconds after the hatch the look target leans toward the hatch point, by half the
    /// distance and at most 6 u (the player stays on screen), with a 6% zoom-out.</item>
    /// <item>Hold-to-look (D5): up to 12 u in the held screen direction; release and the spring brings the view home.</item>
    /// </list>
    /// No shake, no rotation: the camera angle never changes (U-005).
    /// </summary>
    public sealed class CameraDirector
    {
        public const double CombatRadius = 8;
        public const int CombatEnemies = 3;
        public const double CombatZoom = 1.12;
        /// <summary>Seconds the combat zoom holds after the enemy count drops below CombatEnemies.</summary>
        public const double CombatHoldSeconds = 1.0;
        /// <summary>Zoom easing time constant (s).</summary>
        public const double ZoomTau = 0.3;

        public const double HatchFocusSeconds = 2.0;
        public const double HatchFocusIn = 0.3;
        public const double HatchFocusOut = 0.6;
        public const double HatchFocusShare = 0.5;
        public const double HatchFocusMaxOffset = 6.0;
        public const double HatchFocusZoom = 1.06;

        /// <summary>D5: hold-to-look reaches this far (u).</summary>
        public const double LookMaxUnits = 12;

        /// <summary>Distance multiplier for the rig, 1 = the U-005 framing.</summary>
        public double Zoom { get; private set; } = 1;
        /// <summary>The zoom the rules ask for this frame (before easing).</summary>
        public double ZoomGoal { get; private set; } = 1;
        /// <summary>Goal offset (sim u) added to the follow goal.</summary>
        public double OffsetX { get; private set; }
        public double OffsetZ { get; private set; }
        /// <summary>True while the combat framing rule holds (crowd present or inside the hold).</summary>
        public bool CombatFraming { get; private set; }
        /// <summary>0..1 hatch focus weight this frame.</summary>
        public double HatchFocusWeight { get; private set; }
        /// <summary>True while hold-to-look moves the view (the follow drops its cursor look-ahead then).</summary>
        public bool Looking { get; private set; }

        private double _combatHold;

        public void Reset()
        {
            Zoom = ZoomGoal = 1;
            OffsetX = OffsetZ = 0;
            CombatFraming = false;
            HatchFocusWeight = 0;
            Looking = false;
            _combatHold = 0;
        }

        /// <summary>0..1 weight of the hatch focus t sim seconds after the hatch (smooth in, hold, smooth out).</summary>
        public static double HatchFocusAt(double t)
        {
            if (!(t >= 0) || t >= HatchFocusSeconds) return 0;
            if (t < HatchFocusIn) return Smooth(t / HatchFocusIn);
            double outStart = HatchFocusSeconds - HatchFocusOut;
            if (t > outStart) return Smooth(1 - (t - outStart) / HatchFocusOut);
            return 1;
        }

        /// <summary>Screen direction (right, up), length clamped to 1, to the hold-to-look offset in sim u.</summary>
        public static void LookOffset(double right, double up, out double x, out double z)
        {
            double len = Math.Sqrt(right * right + up * up);
            if (len > 1)
            {
                right /= len;
                up /= len;
            }
            x = (right * HumanInputState.ScreenRightX + up * HumanInputState.ScreenUpX) * LookMaxUnits;
            z = (right * HumanInputState.ScreenRightZ + up * HumanInputState.ScreenUpZ) * LookMaxUnits;
        }

        public void Step(in CameraDirectorInput f)
        {
            double dt = f.Dt > 0 ? f.Dt : 0;
            if (f.Override)
            {
                Reset();
                return;
            }

            // Combat framing with a hold, so a crowd that flickers around 3 does not pump the zoom.
            if (f.EnemiesNear >= CombatEnemies) _combatHold = CombatHoldSeconds;
            else _combatHold = Math.Max(0, _combatHold - dt);
            CombatFraming = f.EnemiesNear >= CombatEnemies || _combatHold > 0;

            // Hatch focus, on sim time.
            double hw = f.Hatched ? HatchFocusAt(f.SimTime - f.HatchSimTime) : 0;
            HatchFocusWeight = hw;
            double ox = 0, oz = 0;
            if (hw > 0)
            {
                double dx = f.HatchX - f.HumanX, dz = f.HatchZ - f.HumanZ;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d > 1e-6)
                {
                    double reach = Math.Min(d * HatchFocusShare, HatchFocusMaxOffset);
                    ox += dx / d * reach * hw;
                    oz += dz / d * reach * hw;
                }
            }

            // Hold-to-look replaces the hatch lean while held (the player asked to look somewhere).
            Looking = f.Looking;
            if (f.Looking)
            {
                LookOffset(f.LookRight, f.LookUp, out double lx, out double lz);
                ox = lx;
                oz = lz;
            }
            OffsetX = ox;
            OffsetZ = oz;

            double goal = 1;
            if (CombatFraming) goal = CombatZoom;
            double hz = 1 + (HatchFocusZoom - 1) * hw;
            if (hz > goal) goal = hz;
            ZoomGoal = goal;
            if (dt > 0) Zoom += (goal - Zoom) * (1 - Math.Exp(-dt / ZoomTau));
        }

        private static double Smooth(double t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3 - 2 * t);
        }
    }

    /// <summary>
    /// The controller half of hold-to-look (D5 says "click the right stick"): a click (press and release inside
    /// <see cref="ClickSeconds"/>) latches look until the next click; a longer press looks only while held. The mouse's
    /// middle button is always hold. Pure, so the rule is testable.
    /// </summary>
    public sealed class LookLatch
    {
        public const double ClickSeconds = 0.25;

        private bool _down;
        private double _downAt;
        private bool _latched;
        private bool _releasingLatch;

        /// <summary>True while look is on (held, or latched by a click).</summary>
        public bool Active => (_down && !_releasingLatch) || _latched;

        public void Reset()
        {
            _down = _latched = _releasingLatch = false;
        }

        /// <summary>Feed the button state each frame with the view clock (s).</summary>
        public void Update(bool pressed, double now)
        {
            if (pressed && !_down)
            {
                _down = true;
                _downAt = now;
                // A press while latched ends the latch (the button acts as "back").
                _releasingLatch = _latched;
                _latched = false;
            }
            else if (!pressed && _down)
            {
                _down = false;
                if (!_releasingLatch && now - _downAt < ClickSeconds) _latched = true;
                _releasingLatch = false;
            }
        }
    }
}

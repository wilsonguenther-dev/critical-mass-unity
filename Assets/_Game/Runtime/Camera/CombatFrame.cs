using System;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// fix/px-combat-camera (2026-11-23, owner: "I cannot see my hits"; "does the camera need to move when the action is
    /// happening"): the third-person COMBAT FRAMING. With an enemy inside 1.5 x my melee reach (or while I swing, guard, am
    /// staggered or traded a hit in the last 1.5 s) the camera eases over InSeconds into an over-the-shoulder two-shot:
    /// the look point moves FocusShare of the way toward the enemy at FocusHeight, the camera comes in to Distance, drops to
    /// PitchDeg, and orbits YawDeg off the line to the enemy (plus ShoulderU of lens shift) on the side the player's own
    /// view already shows more of him, so my fighter stands on one side of the screen and the enemy beside him, never
    /// behind him. It holds HoldSeconds after the fight ends, then eases home over OutSeconds. View only, never the sim:
    /// the aim and the move keys keep the player's own yaw (GroundView.AimYaw), so the framing never steers a swing.
    /// The numbers below are the design (an analytic model of GroundFollow's pose, CombatCameraTests): at 0.8 and 1.0 x
    /// reach, every class, 16:9, FOV 65: at least 0.76 of the enemy's screen box clear of mine (target 0.6, the unframed
    /// camera 0.00 to 0.21), the enemy 1.54 to 1.59 x as tall on screen as the unframed camera draws him (target 1.4) and
    /// 1.71 to 1.73 x the old duel framing, my own fighter inside the screen.
    /// </summary>
    public static class CombatFrame
    {
        /// <summary>The framed pose at full weight and strength 1 (third person: distance 4.4, pivot 2.2, pitch 14).</summary>
        public const double Distance = 3.2, FocusHeight = 1.3, FocusShare = 0.25, PitchDeg = 8, YawDeg = 48, ShoulderU = 0.8;
        /// <summary>Ease in (s), hold after the fight ends (s), ease out (s).</summary>
        public const double InSeconds = 0.3, HoldSeconds = 1.0, OutSeconds = 0.6;
        /// <summary>Engage: an enemy within ReachShare x my attack range; or, while I swing, guard, am staggered or traded a hit
        /// within RecentSeconds, the nearest enemy within BusyRange u.</summary>
        public const double ReachShare = 1.5, RecentSeconds = 1.5, BusyRange = 4.0;
        /// <summary>Side hysteresis: the side flips only when the player's own view stands more than SideHysteresisDeg on
        /// the other side of the line for SideHoldSeconds (no flip-flop as the enemy circles).</summary>
        public const double SideHysteresisDeg = 25, SideHoldSeconds = 0.75;
        /// <summary>Walls: a side whose camera (shoulder and pull-back) would be pulled in below WallShare of its length is
        /// blocked; the framing moves to the other side after WallSwapSeconds, and when both are blocked the orbit and the
        /// shoulder ease to 0 (the closer, lower framing stays; GroundFollow's collision keeps the lens out of the wall).</summary>
        public const double WallShare = 0.7, WallSwapSeconds = 0.25;
        /// <summary>The framing never turns the view more than MaxDeviationDeg off the player's own yaw, and while he steers
        /// the camera himself (manual look) at most SteerCapDeg: his input wins, the framing only adds a bounded offset.</summary>
        public const double MaxDeviationDeg = 75, SteerCapDeg = 12, SteerInSeconds = 0.3, SteerOutSeconds = 1.0;
        /// <summary>The yaw offset and the shoulder chase their target with this time constant (s) and rate limits (deg/s,
        /// u/s): no snap when the side changes or the enemy circles.</summary>
        public const double ChaseTau = 0.12, MaxYawRate = 200, MaxShoulderRate = 2.5;

        /// <summary>A narrower lens than the default would crop my own fighter at Distance: the framed distance grows by the
        /// lens ratio (1 at FOV 65 and wider).</summary>
        public static double FovFit(double fovDeg) =>
            fovDeg >= FollowCamera.FovDefault || fovDeg <= 1 ? 1 : Math.Tan(FollowCamera.FovDefault * Math.PI / 360) / Math.Tan(fovDeg * Math.PI / 360);

        static double Smooth01(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

        // ---------------------------------------------------------------- the analytic model (tests, the report's numbers)

        /// <summary>A screen rectangle in normalised device units (-1..1 both ways).</summary>
        public struct Box
        {
            public double MinX, MinY, MaxX, MaxY;
            public double Height => MaxY - MinY;
            public bool OnScreen => MinX >= -1 && MaxX <= 1 && MinY >= -1 && MaxY <= 1;
        }

        /// <summary>Share of box e not covered by box m (the D2 measure).</summary>
        public static double OutsideShare(Box m, Box e)
        {
            double ox = Math.Max(0, Math.Min(m.MaxX, e.MaxX) - Math.Max(m.MinX, e.MinX));
            double oy = Math.Max(0, Math.Min(m.MaxY, e.MaxY) - Math.Max(m.MinY, e.MinY));
            double area = (e.MaxX - e.MinX) * (e.MaxY - e.MinY);
            return area > 0 ? 1 - ox * oy / area : 0;
        }

        /// <summary>
        /// The two fighters' screen boxes for GroundFollow's third-person pose on open flat ground (pure): me at the origin, the
        /// enemy d u straight ahead along my own yaw (the worst case: the unframed camera puts him exactly behind me), the
        /// bodies as upright cylinders (radius, height). strength 0 = the unframed camera; 1 = full combat framing (turn +1).
        /// </summary>
        public static void Model(double meRadius, double meHeight, double enemyRadius, double enemyHeight, double d,
                                 double fovDeg, double strength, out Box me, out Box enemy, double aspect = 16.0 / 9.0)
        {
            double k = Math.Max(0, Math.Min(1, strength));
            double dist = FollowCamera.ThirdDistance + (Distance * FovFit(fovDeg) - FollowCamera.ThirdDistance) * k;
            double pivotH = FollowCamera.ThirdPivotHeight + (FocusHeight - FollowCamera.ThirdPivotHeight) * k;
            double pitch = FollowCamera.ThirdPitch + (PitchDeg - FollowCamera.ThirdPitch) * k;
            double yaw = YawDeg * k;               // turn +1: right of the line
            double shoulder = -ShoulderU * k;
            Pose(0, pivotH, FocusShare * k * d, yaw, pitch, shoulder, dist, out double cx, out double cy, out double cz,
                out double fx, out double fy, out double fz, out double rx, out double rz, out double ux, out double uy, out double uz);
            double f = 1 / Math.Tan(fovDeg * Math.PI / 360);
            me = Cylinder(0, 0, meRadius, meHeight, cx, cy, cz, fx, fy, fz, rx, rz, ux, uy, uz, f, aspect);
            enemy = Cylinder(0, d, enemyRadius, enemyHeight, cx, cy, cz, fx, fy, fz, rx, rz, ux, uy, uz, f, aspect);
        }

        /// <summary>GroundFollow's camera in a local frame (x right of my own yaw, y up, z along it): pivot (px, py, pz), the
        /// view turned yawDeg right of +z and pitchDeg down, shifted shoulder along its right, dist back along the view.</summary>
        static void Pose(double px, double py, double pz, double yawDeg, double pitchDeg, double shoulder, double dist,
                         out double cx, out double cy, out double cz, out double fx, out double fy, out double fz,
                         out double rx, out double rz, out double ux, out double uy, out double uz)
        {
            double t = yawDeg * Math.PI / 180, p = pitchDeg * Math.PI / 180;
            double st = Math.Sin(t), ct = Math.Cos(t), sp = Math.Sin(p), cp = Math.Cos(p);
            fx = st * cp; fy = -sp; fz = ct * cp;
            rx = ct; rz = -st;
            ux = st * sp; uy = cp; uz = ct * sp;
            cx = px + rx * shoulder - fx * dist;
            cy = py - fy * dist;
            cz = pz + rz * shoulder - fz * dist;
        }

        static Box Cylinder(double x, double z, double r, double h, double cx, double cy, double cz, double fx, double fy, double fz,
                            double rx, double rz, double ux, double uy, double uz, double f, double aspect)
        {
            var b = new Box { MinX = 9, MinY = 9, MaxX = -9, MaxY = -9 };
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8, px = x + r * Math.Cos(a), pz = z + r * Math.Sin(a);
                for (int j = 0; j < 2; j++)
                {
                    double dx = px - cx, dy = j * h - cy, dz = pz - cz;
                    double depth = Math.Max(1e-4, dx * fx + dy * fy + dz * fz);
                    double sx = (dx * rx + dz * rz) / depth * f / aspect, sy = (dx * ux + dy * uy + dz * uz) / depth * f;
                    b.MinX = Math.Min(b.MinX, sx); b.MaxX = Math.Max(b.MaxX, sx);
                    b.MinY = Math.Min(b.MinY, sy); b.MaxY = Math.Max(b.MaxY, sy);
                }
            }
            return b;
        }
    }

    /// <summary>One frame's inputs to <see cref="CombatFramer.Step"/>. Yaws in Unity degrees.</summary>
    public struct CombatFrameInput
    {
        public double Dt;
        /// <summary>A cut (entering the view, a new followed body): the weight jumps to its goal, the side is chosen fresh.</summary>
        public bool Snap;
        /// <summary>The COMBAT CAMERA setting (and not reduce motion).</summary>
        public bool Enabled;
        /// <summary>The strength slider, 0..1 (1 = the designed framing).</summary>
        public double Strength;
        /// <summary>A live target to frame this frame (LineYaw valid).</summary>
        public bool Engaged;
        /// <summary>The last framed target still stands (the hold keeps framing it; a dead or knocked-out one is let go).</summary>
        public bool TargetAlive;
        /// <summary>The player's own camera yaw (auto follow or his look) and the yaw from him to the target.</summary>
        public double OwnYaw, LineYaw;
        /// <summary>The player steers the camera himself (manual look, recently).</summary>
        public bool Steering;
        /// <summary>Wall clearance (0..1, 1 = the whole shoulder and pull-back fit) for turn +1 and turn -1.</summary>
        public double ClearPlus, ClearMinus;
    }

    /// <summary>
    /// The combat framing's state (pure, testable): weight with ease-in / hold / ease-out, the side (Turn) with hysteresis and
    /// the wall fallback, the steering cap, and the offsets GroundView adds to the player's own camera. Turn +1: the view is
    /// turned right of the line to the enemy (the camera stands on the line's left, my fighter is drawn right, the enemy left).
    /// </summary>
    public sealed class CombatFramer
    {
        /// <summary>Raw eased weight 0..1 (linear in time) and the applied share K = smoothstep(weight) x strength.</summary>
        public double Weight { get; private set; }
        public double K { get; private set; }
        public int Turn { get; private set; }
        /// <summary>What GroundView adds: yaw (deg) and pitch (deg) on the player's own; pivot lift (u, negative = lower);
        /// lens shift (u along the view's right); the share of the way from me to the enemy the look point moves.</summary>
        public double YawOffset { get; private set; }
        public double PitchOffset { get; private set; }
        public double Lift { get; private set; }
        public double Shoulder { get; private set; }
        public double Focus { get; private set; }
        /// <summary>Orbit share kept by the wall rule (1 = the side is clear, 0 = both sides blocked: no orbit, no shoulder).</summary>
        public double WallKeep { get; private set; } = 1;
        public double SteerWeight { get; private set; }
        /// <summary>The framing is on (engaged or holding) this frame.</summary>
        public bool Active { get; private set; }

        double _hold, _swapHeld, _lineYaw;
        bool _have;

        public void Reset()
        {
            Weight = K = YawOffset = PitchOffset = Lift = Shoulder = Focus = SteerWeight = 0;
            WallKeep = 1;
            Turn = 0;
            Active = _have = false;
            _hold = _swapHeld = 0;
        }

        static double Smooth01(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        public void Step(in CombatFrameInput f)
        {
            double dt = f.Dt > 0 ? f.Dt : 0;
            bool engaged = f.Enabled && f.Engaged;
            if (engaged) { _hold = CombatFrame.HoldSeconds; _have = true; _lineYaw = f.LineYaw; }
            else if (_hold > 0) _hold = f.Enabled && f.TargetAlive ? _hold - dt : 0;
            if (!engaged && f.Enabled && f.TargetAlive && _hold > 0 && _have) _lineYaw = f.LineYaw;   // the hold frames the live body
            bool on = engaged || (_hold > 0 && _have);
            Active = on;
            bool fresh = Weight <= 0;
            if (f.Snap) Weight = on ? 1 : 0;
            else Weight = on ? Math.Min(1, Weight + dt / CombatFrame.InSeconds) : Math.Max(0, Weight - dt / CombatFrame.OutSeconds);
            if (Weight <= 0 && !on)
            {
                Reset();
                return;
            }

            // The side: the one the player's own view already shows more of the enemy from, kept with hysteresis.
            double d0 = GroundCamera.DeltaAngle(_lineYaw, f.OwnYaw);   // + = my own view is already turned right of the line
            if (Turn == 0 || (f.Snap && on) || (fresh && on))
            {
                Turn = d0 >= 0 ? 1 : -1;
                double here0 = Turn > 0 ? f.ClearPlus : f.ClearMinus, there0 = Turn > 0 ? f.ClearMinus : f.ClearPlus;
                if (here0 < CombatFrame.WallShare && there0 > here0) Turn = -Turn;   // a wall on that side: start on the other
                _swapHeld = 0;
            }
            double here = Turn > 0 ? f.ClearPlus : f.ClearMinus, there = Turn > 0 ? f.ClearMinus : f.ClearPlus;
            bool wallSwap = here < CombatFrame.WallShare && there >= CombatFrame.WallShare;
            bool viewSwap = -Turn * d0 > CombatFrame.SideHysteresisDeg && there >= CombatFrame.WallShare;
            if (on && (wallSwap || viewSwap))
            {
                _swapHeld += dt;
                if (_swapHeld >= (wallSwap ? CombatFrame.WallSwapSeconds : CombatFrame.SideHoldSeconds))
                {
                    Turn = -Turn;
                    _swapHeld = 0;
                    here = there;
                }
            }
            else _swapHeld = 0;

            // Both sides blocked: the orbit and the shoulder ease out (then in again when a side clears).
            double keepGoal = here >= CombatFrame.WallShare ? 1 : 0;
            WallKeep = f.Snap ? keepGoal : keepGoal > WallKeep ? Math.Min(keepGoal, WallKeep + dt / CombatFrame.InSeconds)
                                                               : Math.Max(keepGoal, WallKeep - dt / CombatFrame.InSeconds);
            double steerGoal = f.Steering ? 1 : 0;
            SteerWeight = f.Snap ? steerGoal : steerGoal > SteerWeight ? Math.Min(1, SteerWeight + dt / CombatFrame.SteerInSeconds)
                                                                       : Math.Max(0, SteerWeight - dt / CombatFrame.SteerOutSeconds);

            double strength = Clamp(double.IsNaN(f.Strength) ? 1 : f.Strength, 0, 1);
            K = Smooth01(Weight) * strength;
            double keep = Smooth01(WallKeep);

            // Yaw: toward the line + Turn x YawDeg, never more than the cap off the player's own yaw, scaled by K.
            double wantYaw = _lineYaw + Turn * CombatFrame.YawDeg * keep;
            double cap = CombatFrame.MaxDeviationDeg + (CombatFrame.SteerCapDeg - CombatFrame.MaxDeviationDeg) * SteerWeight;
            double yawGoal = Clamp(GroundCamera.DeltaAngle(f.OwnYaw, wantYaw), -cap, cap) * K;
            double shoulderGoal = -Turn * CombatFrame.ShoulderU * K * keep;
            if (f.Snap)
            {
                YawOffset = yawGoal;
                Shoulder = shoulderGoal;
            }
            else
            {
                double a = dt > 0 ? 1 - Math.Exp(-dt / CombatFrame.ChaseTau) : 0;
                double ym = CombatFrame.MaxYawRate * dt, sm = CombatFrame.MaxShoulderRate * dt;
                YawOffset += Clamp((yawGoal - YawOffset) * a, -ym, ym);
                Shoulder += Clamp((shoulderGoal - Shoulder) * a, -sm, sm);
                // the cap also bounds the chased value (while he steers, the offset never lags above it)
                double hard = Math.Max(cap * K, CombatFrame.SteerCapDeg * K);
                YawOffset = Clamp(YawOffset, -hard, hard);
            }
            PitchOffset = (CombatFrame.PitchDeg - FollowCamera.ThirdPitch) * K;
            Lift = (CombatFrame.FocusHeight - FollowCamera.ThirdPivotHeight) * K;
            Focus = CombatFrame.FocusShare * K;
        }
    }
}

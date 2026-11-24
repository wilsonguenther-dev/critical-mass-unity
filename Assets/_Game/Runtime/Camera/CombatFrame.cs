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
        /// <remarks>fix/px-rough-spots (owner, 2026-11-23: "how do we get the person we're fighting perfectly framed"; film 4:02 my
        /// fighter half cut off at the left edge, 0:08 / 7:30 the camera pressed right behind him): the framing no longer comes in
        /// closer than the plain camera (it was 3.2 u, which drew my fighter at half the screen height and cut him at the
        /// bottom and the side); the look point sits further toward the enemy and a little higher, and <see cref="FitDistance"/>
        /// pulls the lens back until BOTH whole bodies fit inside the safe frame.</remarks>
        /// <summary>fix/px-rough-spots (owner, 2026-11-23, on the bot-played round: "why is the camera moving like that ... it's
        /// like spinning around him"): in a fight the camera's facing belongs to the player. The framing changes distance,
        /// height, pitch and a small sideways lens shift only; it never turns the view. Orbit = true brings back the old
        /// 48-degree swing (tests of that logic only; no setting exposes it).</summary>
        public static bool Orbit = false;

        public const double Distance = 4.4, FocusHeight = 1.6, FocusShare = 0.4, PitchDeg = 12, YawDeg = 48, ShoulderU = 0.8;

        /// <summary>fix/px-rough-spots pass 3 (code-only builder: with the no-turn camera a straight-ahead duel hid the enemy
        /// behind my own fighter): the NO-TURN two-shot, the way third-person action games frame melee without rotating. While
        /// framed the lens shifts NoTurnShoulderU sideways (over one shoulder, the side picked once per fight), the pivot
        /// rises NoTurnLiftU and the view pitches down to NoTurnPitchDeg, and the look point moves NoTurnFocus of the way to
        /// the enemy, all eased in by the framing weight. The camera's yaw stays the player's.</summary>
        public const double NoTurnShoulderU = 1.2, NoTurnPitchDeg = 40, NoTurnLiftU = 1.4, NoTurnFocus = 0.6, NoTurnDistance = 4.4;
        /// <summary>The look point never moves more than this toward the enemy (u): a long-reach duel (Farmer, 1.8 u apart) kept
        /// my own feet below the screen with the share alone.</summary>
        public const double NoTurnFocusMaxU = 0.75;
        /// <summary>The no-turn pose changes pitch and height a lot more than the old framing: it eases in over NoTurnInSeconds and
        /// out over NoTurnOutSeconds (smoothstep), never a snap.</summary>
        public const double NoTurnInSeconds = 0.45, NoTurnOutSeconds = 0.8;
        /// <summary>Straight ahead (his view within this of the line to the enemy) the first side is the LEFT shoulder (lens left, my
        /// fighter drawn right of centre, the enemy left of him): the fighters carry their weapon on the right, so the right
        /// shoulder put my weapon arm over him (measured, pass 3: Miner 51-59% of the enemy showing on the right, 78-83% on the left).</summary>
        public const double NoTurnAheadDeg = 8;

        /// <summary>fix/px-rough-spots pass 3: how clear the no-turn pose is for one shoulder side (+1 lens right, -1 left, 0
        /// none) at the player's own yaw: 1 = the lens shift and the pull-back fit, less = pulled in (share), 0 = a fighter's
        /// chest is hidden from the lens.</summary>
        public static double NoTurnClear(CriticalMass.Sim.Grid.MapDef map, double fx, double fy, double fz, double ownYaw, int side, double strength,
            double mx, double my, double mz, double ex, double ey, double ez, SeeThroughCylinder[] cyl, int n)
        {
            double pitch = FollowCamera.ThirdPitch + (NoTurnPitchDeg - FollowCamera.ThirdPitch) * strength;
            double sh = side * NoTurnShoulderU * strength, dist = FollowCamera.ThirdDistance;
            GroundCamera.Basis(ownYaw, out double ux, out double uz, out double rx, out double rz);
            double pr = pitch * Math.PI / 180, cp = Math.Cos(pr), sp = Math.Sin(pr);
            double share = 1, s = Math.Abs(sh), px = fx, pz = fz, py = fy + NoTurnLiftU * strength;
            if (s > 1e-6)
            {
                double sx = sh >= 0 ? rx : -rx, sz = sh >= 0 ? rz : -rz;
                double got = GroundCollision.Resolve(map, px, py, pz, sx, 0, sz, s, cyl, n);
                share = Math.Min(share, got / s);
                px += sx * got;
                pz += sz * got;
            }
            double back = GroundCollision.Resolve(map, px, py, pz, -ux * cp, sp, -uz * cp, dist, cyl, n);
            share = Math.Min(share, back / dist);
            double lx = px - ux * cp * back, ly = py + sp * back, lz = pz - uz * cp * back;
            if (GroundCollision.Occluded(map, lx, ly, lz, mx, my, mz, SightStopU, cyl, n) || GroundCollision.Occluded(map, lx, ly, lz, ex, ey, ez, SightStopU, cyl, n))
                share = 0;
            return share;
        }

        /// <summary>fix/px-rough-spots: the two-shot fit. Both fighters (upright cylinders FitBodyRadius x FitBodyTall over their
        /// ground) must project inside this safe frame (normalised device units, -1..1): FitHalfX either side, above FitBottom
        /// (the ability bar and the health panel) and below FitTop (the score, objective and resource rows). The lens moves
        /// back in FitStep steps, never past FitMaxDistance.</summary>
        public const double FitHalfX = 0.82, FitTop = 0.62, FitBottom = -0.78, FitBodyRadius = 0.5, FitBodyTall = 2.2, FitStep = 0.1, FitMaxDistance = 6.5;

        /// <summary>fix/px-rough-spots (pure; GroundView and the tests): the smallest boom length from start up to FitMaxDistance at
        /// which every body (x, ground y, z: sim) projects inside the safe frame for a lens looking along (yawDeg, pitchDeg) at the
        /// pivot (px, py, pz). FitMaxDistance when none fits.</summary>
        public static double FitDistance(double px, double py, double pz, double yawDeg, double pitchDeg, double vfovDeg, double aspect, double start,
            double[] bx, double[] by, double[] bz, int n)
        {
            GroundCamera.Basis(yawDeg, out double ux, out double uz, out double rx, out double rz);
            double pr = pitchDeg * Math.PI / 180, cp = Math.Cos(pr), sp = Math.Sin(pr);
            double fx = ux * cp, fy = -sp, fz = uz * cp;            // forward
            double upx = ux * sp, upy = cp, upz = uz * sp;          // up (perpendicular to forward, in the yaw plane)
            double f = 1 / Math.Tan(Math.Max(10, vfovDeg) * Math.PI / 360), asp = aspect > 0.1 ? aspect : 16.0 / 9.0;
            for (double dist = Math.Max(0.5, start); dist <= FitMaxDistance + 1e-9; dist += FitStep)
            {
                double cx = px - fx * dist, cy = py - fy * dist, cz = pz - fz * dist;
                bool fits = true;
                for (int b = 0; b < n && fits; b++)
                    for (int k = 0; k < 8 && fits; k++)
                    {
                        double a = k * Math.PI / 4, qx = bx[b] + FitBodyRadius * Math.Cos(a), qz = bz[b] + FitBodyRadius * Math.Sin(a);
                        for (int j = 0; j < 2 && fits; j++)
                        {
                            double dx = qx - cx, dy = by[b] + j * FitBodyTall - cy, dz = qz - cz;
                            double depth = dx * fx + dy * fy + dz * fz;
                            if (depth < 0.3) { fits = false; break; }
                            double sx = (dx * rx + dz * rz) / depth * f / asp, sy = (dx * upx + dy * upy + dz * upz) / depth * f;
                            if (Math.Abs(sx) > FitHalfX || sy > FitTop || sy < FitBottom) fits = false;
                        }
                    }
                if (fits) return dist;
            }
            return FitMaxDistance;
        }
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
        /// <summary>fix/px-combat-camera-2 (pass-2 review: an 80 deg mouse turn moved the view ~5 deg, the fighter turned his
        /// back on the enemy while the screen still showed him, and the next click swung at air): the player's look turns
        /// the VIEW one to one, always. While he looks around (look input within LookHoldSeconds, or his own yaw turning
        /// faster than LookRateDeg per s) the framing's yaw offset is HELD, never counter-rotated; it re-settles only once he
        /// stops. Turned more than ReleaseDeg off the line to the enemy, the framing lets go (eases out, no hold) and takes
        /// him again only once he faces within ReacquireDeg. It never adds more than MaxDeviationDeg to his own yaw.</summary>
        public const double MaxDeviationDeg = 75, LookHoldSeconds = 0.25, LookRateDeg = 90, ReleaseDeg = 40, ReacquireDeg = 30;
        /// <summary>fix/px-combat-camera-2 (pass-2 review: the camera parked inside a tree canopy for four of six classes):
        /// scenery (trees with their crowns, rocks, crystals, props, buildings) counts exactly like walls. A framing is
        /// blocked when its lens is pulled in below WallShare OR when the sight line from the lens to EITHER fighter's chest
        /// (ChestHeight over his ground) meets anything before SightStopU short of the chest (a SightRadius probe).</summary>
        public const double ChestHeight = 1.15, SightStopU = 0.45, SightRadius = 0.05;
        /// <summary>fix/px-combat-camera-2 (pass-2 review: the enemy's name and bar floated up under the resource banner):
        /// while framed, a head tag sits at most TagGapShare of the screen height above the drawn head top and never above
        /// the top HUD rows (score, objective, resources: the top TagTopShare of the screen).</summary>
        public const double TagGapShare = 0.03, TagTopShare = 0.25;
        /// <summary>fix/px-combat-camera-3 (pass 3: framed only 1% of a forest fight, 8% of the hill minute): STICKY. Once
        /// framed it stays while an enemy stands within KeepRange u (no facing cone); a framing that is blocked tries the
        /// same side at half and a quarter of the swing (24, 12 deg) before the other side, then the closer-and-higher
        /// fallback (no swing, FallbackPitchDeg, FallbackCloser nearer); scenery hiding a fighter from EVERY pose is
        /// tolerated OcclusionGraceSeconds before it lets go.</summary>
        public const double KeepRange = 5.0, FallbackPitchDeg = 20, FallbackCloser = 0.2, OcclusionGraceSeconds = 0.5;
        /// <summary>fix/px-combat-camera-3 (pass 3: after a drag the view swung back 45 deg by itself; re-framing rotated
        /// another 43): when the player steers (manual look mode, or he looked within RespectSeconds of this fight) the
        /// framing may rotate his view at most RespectYawDeg, at RespectYawRate deg/s, and frames with a wider lens shift
        /// (RespectShoulderU) instead. When it lets go, its yaw offset is folded into his own yaw (TakeYaw), so the view
        /// never unwinds on its own.</summary>
        public const double RespectYawDeg = 10, RespectYawRate = 20, RespectSeconds = 5, RespectShoulderU = 1.2;
        /// <summary>fix/px-combat-camera-3 (pass 3: when the framing lets go the plain camera hid the enemy behind me, 21-37%
        /// visible): with an enemy within KeepRange and no framing, the plain camera takes a small collision-checked lens
        /// shift (AssistShoulderU, to the enemy's side) and AssistPitchDeg more pitch, eased over AssistSeconds.</summary>
        public const double AssistShoulderU = 0.6, AssistPitchDeg = 6, AssistSeconds = 0.35;

        /// <summary>The swing share kept by the best clear pose of one side: 1 (48 deg), 0.5 (24), 0.25 (12) or 0.</summary>
        public static double BestSwing(double full, double half, double quarter) =>
            full >= WallShare ? 1 : half >= WallShare ? 0.5 : quarter >= WallShare ? 0.25 : 0;
        /// <summary>The framed pitch (deg) and distance share for a kept swing share (1: the design; 0: the fallback).</summary>
        public static double PosePitch(double keep) => PitchDeg + (FallbackPitchDeg - PitchDeg) * (1 - keep);
        public static double PoseReach(double keep) => 1 - FallbackCloser * (1 - keep);
        /// <summary>The yaw offset and the shoulder chase their target with this time constant (s) and rate limits (deg/s,
        /// u/s): no snap when the side changes or the enemy circles.</summary>
        public const double ChaseTau = 0.12, MaxYawRate = 200, MaxShoulderRate = 2.5;

        /// <summary>A narrower lens than the default would crop my own fighter at Distance: the framed distance grows by the
        /// lens ratio (1 at FOV 65 and wider).</summary>
        public static double FovFit(double fovDeg) =>
            fovDeg >= FollowCamera.FovDefault || fovDeg <= 1 ? 1 : Math.Tan(FollowCamera.FovDefault * Math.PI / 360) / Math.Tan(fovDeg * Math.PI / 360);

        static double Smooth01(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

        /// <summary>
        /// How clear one framing is (pure; GroundView and the tests): 1 = the shoulder shift and the whole pull-back fit,
        /// less = pulled in by the grid or a scenery cylinder (share of its length), 0 = a fighter's chest cannot be seen
        /// from the lens. turn +1 / -1: the orbit sides; 0: the no-orbit pose (on the line, no shoulder). (fx, fy, fz): the
        /// framed look point (sim). (mx, my, mz) / (ex, ey, ez): my chest and the enemy's (sim). The lens comes out in l*.
        /// </summary>
        public static double SideClear(CriticalMass.Sim.Grid.MapDef map, double fx, double fy, double fz, double lineYaw, int turn, double strength, double fovFit,
            double mx, double my, double mz, double ex, double ey, double ez, SeeThroughCylinder[] cyl, int n,
            out double lx, out double ly, out double lz) =>
            SideClear(map, fx, fy, fz, lineYaw, turn, turn == 0 ? 0 : 1, strength, fovFit, mx, my, mz, ex, ey, ez, cyl, n, out lx, out ly, out lz);

        /// <summary>fix/px-combat-camera-3: one pose of the ladder: turn (+1 / -1 / 0) at swing share (1, 0.5, 0.25; 0 = the
        /// closer-and-higher fallback, PosePitch / PoseReach).</summary>
        public static double SideClear(CriticalMass.Sim.Grid.MapDef map, double fx, double fy, double fz, double lineYaw, int turn, double swing, double strength, double fovFit,
            double mx, double my, double mz, double ex, double ey, double ez, SeeThroughCylinder[] cyl, int n,
            out double lx, out double ly, out double lz)
        {
            double keep = turn == 0 ? 0 : swing;
            double yaw = lineYaw + turn * YawDeg * keep;
            double pitch = FollowCamera.ThirdPitch + (PosePitch(keep) - FollowCamera.ThirdPitch) * strength;
            double sh = -turn * ShoulderU * keep * strength;
            double dist = FollowCamera.ThirdDistance + (Distance * fovFit * PoseReach(keep) - FollowCamera.ThirdDistance) * strength;
            GroundCamera.Basis(yaw, out double ux, out double uz, out double rx, out double rz);
            double pr = pitch * Math.PI / 180, cp = Math.Cos(pr), sp = Math.Sin(pr);
            double share = 1, s = Math.Abs(sh);
            double px = fx, pz = fz;
            if (s > 1e-6)
            {
                double sx = sh >= 0 ? rx : -rx, sz = sh >= 0 ? rz : -rz;
                double got = GroundCollision.Resolve(map, px, fy, pz, sx, 0, sz, s, cyl, n);
                share = Math.Min(share, got / s);
                px += sx * got;
                pz += sz * got;
            }
            double back = dist;
            if (dist > 1e-6)
            {
                back = GroundCollision.Resolve(map, px, fy, pz, -ux * cp, sp, -uz * cp, dist, cyl, n);
                share = Math.Min(share, back / dist);
            }
            lx = px - ux * cp * back;
            ly = fy + sp * back;
            lz = pz - uz * cp * back;
            if (GroundCollision.Occluded(map, lx, ly, lz, mx, my, mz, SightStopU, cyl, n) || GroundCollision.Occluded(map, lx, ly, lz, ex, ey, ez, SightStopU, cyl, n))
                share = 0;
            return share;
        }

        /// <summary>Screen y (pixels, up) of a head tag while framed (pure): no more than TagGapShare x screen height above
        /// the head top's screen y, and never into the top HUD rows. liftY: where the tag's own world anchor projects.</summary>
        public static double TagScreenY(double liftY, double headTopY, double screenH)
        {
            double y = Math.Min(liftY, headTopY + TagGapShare * screenH);
            return Math.Min(y, (1 - TagTopShare) * screenH);
        }

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
        /// <summary>The player is looking around (look input within LookHoldSeconds, or his own yaw turning fast): the yaw
        /// offset is held so his turn moves the view one to one.</summary>
        public bool Looking;
        /// <summary>Clearance (CombatFrame.SideClear: walls AND scenery AND sight to both chests; 0..1) for turn +1 and -1.</summary>
        public double ClearPlus, ClearMinus;
        /// <summary>Even the no-orbit pose is blocked (scenery or a wall hides a fighter from it): the framing lets go after
        /// OcclusionGraceSeconds of it.</summary>
        public bool NoClearFrame;
        /// <summary>fix/px-combat-camera-3: the reduced swings' clearance (24 deg, 12 deg) for turn +1 and -1 (0 = not tried).</summary>
        public double ClearPlusHalf, ClearMinusHalf, ClearPlusQuarter, ClearMinusQuarter;
        /// <summary>fix/px-combat-camera-3: the player steers the yaw himself (manual look mode): the framing respects it.</summary>
        public bool Manual;
    }

    /// <summary>
    /// The combat framing's state (pure, testable): weight with ease-in / hold / ease-out, the side (Turn) with hysteresis and
    /// the wall and scenery fallback, the look hold and turn-away release, and the offsets GroundView adds to the player's own camera. Turn +1: the view is
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
        /// <summary>He turned more than ReleaseDeg off the line: let go until he faces within ReacquireDeg again.</summary>
        public bool TurnedAway { get; private set; }
        /// <summary>fix/px-combat-camera-3: the distance share of the kept pose (PoseReach) and the respect mode (see
        /// RespectYawDeg) this frame.</summary>
        public double Reach { get; private set; } = 1;
        public bool Respecting { get; private set; }
        /// <summary>Seconds every pose has been blocked (logs; lets go at OcclusionGraceSeconds).</summary>
        public double BlockedFor => _blockedFor;
        /// <summary>The framing is on (engaged or holding) this frame.</summary>
        public bool Active { get; private set; }

        double _hold, _swapHeld, _lineYaw, _blockedFor, _respect, _restView = double.NaN;
        bool _have;

        double _fold;

        /// <summary>fix/px-combat-camera-3: yaw (deg) the framing gave up since the last call (its bound shrank: easing out,
        /// or respecting his steering). The caller adds it to the player's own yaw, so the drawn view (own + offset) does
        /// not move: a framing that lets go never unwinds the view as a rotation, and the aim lands on screen centre.</summary>
        public double TakeFold()
        {
            double y = _fold;
            _fold = 0;
            return y;
        }

        public void Reset()
        {
            Weight = K = YawOffset = PitchOffset = Lift = Shoulder = Focus = 0;
            TurnedAway = false;
            WallKeep = 1;
            Reach = 1;
            Turn = 0;
            Active = _have = false;
            _hold = _swapHeld = 0;
        }

        static double Smooth01(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        static double SideSwing(in CombatFrameInput f, int turn) => turn > 0
            ? CombatFrame.BestSwing(f.ClearPlus, f.ClearPlusHalf, f.ClearPlusQuarter)
            : CombatFrame.BestSwing(f.ClearMinus, f.ClearMinusHalf, f.ClearMinusQuarter);

        public void Step(in CombatFrameInput f)
        {
            double dt = f.Dt > 0 ? f.Dt : 0;
            // fix/px-combat-camera-2: turned away from the enemy (or no clear framing at all): let go, no hold.
            double facing = Math.Abs(GroundCamera.DeltaAngle(f.OwnYaw, f.LineYaw));
            bool wasOn = Active || Weight > 0;
            // fix/px-combat-camera-3: only HIS look lets go by turning away (auto follow drifting behind a moving fighter
            // never does: that was most of the forest drops); brief occlusion of every pose is tolerated.
            // his turn = how far his look took the drawn view from where it rested (the framed view sits up to 48 deg off
            // the line by design, and folded yaw moves his own yaw, so neither is the measure); back = the view faces the fight
            double view = f.OwnYaw + YawOffset;
            if (!f.Looking || double.IsNaN(_restView)) _restView = view;
            double turned = Math.Abs(GroundCamera.DeltaAngle(_restView, view));
            double viewFacing = Math.Abs(GroundCamera.DeltaAngle(view, f.LineYaw));
            TurnedAway = f.Enabled && (TurnedAway ? viewFacing > CombatFrame.ReacquireDeg : wasOn && f.Looking && turned > CombatFrame.ReleaseDeg);
            _blockedFor = f.NoClearFrame ? _blockedFor + dt : 0;
            // his steering during a framed fight (or manual look mode) earns respect; the auto follow's own swing after a
            // fight (no framing on) does not
            if ((f.Looking && wasOn) || f.Manual) _respect = CombatFrame.RespectSeconds;
            else if (!wasOn) _respect = Math.Max(0, _respect - dt);
            Respecting = f.Manual || _respect > 0;
            bool letGo = TurnedAway || _blockedFor >= CombatFrame.OcclusionGraceSeconds;
            // a fresh engagement needs him facing the fight; once framed it is sticky (no facing rule, GroundView keeps the
            // target while any enemy stands within KeepRange)
            bool engaged = f.Enabled && f.Engaged && !letGo && (wasOn || facing <= CombatFrame.ReleaseDeg);
            if (engaged) { _hold = CombatFrame.HoldSeconds; _have = true; _lineYaw = f.LineYaw; }
            else if (_hold > 0) _hold = f.Enabled && f.TargetAlive && !letGo ? _hold - dt : 0;
            if (!engaged && f.Enabled && f.TargetAlive && _hold > 0 && _have) _lineYaw = f.LineYaw;   // the hold frames the live body
            bool on = engaged || (_hold > 0 && _have);
            Active = on;
            bool fresh = Weight <= 0;
            double inS = CombatFrame.Orbit ? CombatFrame.InSeconds : CombatFrame.NoTurnInSeconds, outS = CombatFrame.Orbit ? CombatFrame.OutSeconds : CombatFrame.NoTurnOutSeconds;
            if (f.Snap) Weight = on ? 1 : 0;
            else Weight = on ? Math.Min(1, Weight + dt / inS) : Math.Max(0, Weight - dt / outS);
            if (Weight <= 0 && !on)
            {
                _fold += YawOffset;   // whatever is left is his now (never a rotation back)
                bool away = TurnedAway;
                Reset();
                TurnedAway = away;   // let go stays let go until he faces the fight again
                return;
            }

            double strength = Clamp(double.IsNaN(f.Strength) ? 1 : f.Strength, 0, 1);
            // The side: the one the player's own view already shows more of the enemy from, kept with hysteresis.
            double d0 = GroundCamera.DeltaAngle(_lineYaw, f.OwnYaw);   // + = my own view is already turned right of the line
            if (!CombatFrame.Orbit)
            {
                // fix/px-rough-spots pass 3: the no-turn two-shot. The side is picked ONCE per fight (no flip as he circles):
                // toward the side the enemy already stands on, straight ahead the left shoulder, a blocked side gives way to
                // the other; then it holds until the framing has fully let go (Reset).
                if (Turn == 0 || (fresh && on))
                {
                    Turn = Math.Abs(d0) <= CombatFrame.NoTurnAheadDeg ? 1 : d0 >= 0 ? 1 : -1;   // Turn +1 = lens left
                    double mine = Turn > 0 ? f.ClearPlus : f.ClearMinus, other = Turn > 0 ? f.ClearMinus : f.ClearPlus;
                    if (mine < CombatFrame.WallShare && other > mine) Turn = -Turn;
                    _swapHeld = 0;
                }
                WallKeep = 1;
                K = Smooth01(Weight) * strength;
                Reach = CombatFrame.NoTurnDistance / CombatFrame.Distance;
                double shGoal = -Turn * CombatFrame.NoTurnShoulderU * K;
                if (f.Snap) Shoulder = shGoal;
                else
                {
                    double a0 = dt > 0 ? 1 - Math.Exp(-dt / CombatFrame.ChaseTau) : 0;
                    Shoulder += Clamp((shGoal - Shoulder) * a0, -CombatFrame.MaxShoulderRate * dt, CombatFrame.MaxShoulderRate * dt);
                }
                _fold += YawOffset;   // never a yaw of its own (an Orbit-era offset left over is his)
                YawOffset = 0;
                PitchOffset = (CombatFrame.NoTurnPitchDeg - FollowCamera.ThirdPitch) * K;
                Lift = CombatFrame.NoTurnLiftU * K;
                Focus = CombatFrame.NoTurnFocus * K;
                return;
            }
            if (Turn == 0 || (f.Snap && on) || (fresh && on))
            {
                Turn = d0 >= 0 ? 1 : -1;
                if (SideSwing(f, Turn) < SideSwing(f, -Turn)) Turn = -Turn;   // a wall or scenery on that side: start on the other
                _swapHeld = 0;
            }
            double here = SideSwing(f, Turn), there = SideSwing(f, -Turn);
            bool wallSwap = there > here;
            bool viewSwap = !f.Looking && -Turn * d0 > CombatFrame.SideHysteresisDeg && there > 0 && there >= here;   // never mid-look
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

            // The kept swing: the best clear pose on this side (48, 24, 12 deg), else the closer-and-higher fallback.
            double keepGoal = here;
            WallKeep = f.Snap ? keepGoal : keepGoal > WallKeep ? Math.Min(keepGoal, WallKeep + dt / CombatFrame.InSeconds)
                                                               : Math.Max(keepGoal, WallKeep - dt / CombatFrame.InSeconds);

            K = Smooth01(Weight) * strength;
            double keep = Smooth01(WallKeep);

            // Yaw: toward the line + Turn x YawDeg, never more than the cap off the player's own yaw, scaled by K.
            double wantYaw = _lineYaw + Turn * CombatFrame.YawDeg * keep;
            // fix/px-combat-camera-3: respecting his steering, at most RespectYawDeg of rotation (slowly) and a wider shift
            double cap = Respecting ? CombatFrame.RespectYawDeg : CombatFrame.MaxDeviationDeg;
            double yawGoal = CombatFrame.Orbit ? Clamp(GroundCamera.DeltaAngle(f.OwnYaw, wantYaw), -cap, cap) * K : 0;
            double shoulderGoal = -Turn * (Respecting ? CombatFrame.RespectShoulderU : CombatFrame.ShoulderU) * K * keep;
            if (f.Snap)
            {
                YawOffset = yawGoal;
                Shoulder = shoulderGoal;
            }
            else
            {
                double a = dt > 0 ? 1 - Math.Exp(-dt / CombatFrame.ChaseTau) : 0;
                double ym = (Respecting ? CombatFrame.RespectYawRate : CombatFrame.MaxYawRate) * dt, sm = CombatFrame.MaxShoulderRate * dt;
                // fix/px-combat-camera-2: while he looks around the offset is held (his turn moves the view 1:1); it
                // re-settles only once he stops. As K eases out the bound below takes it home.
                if (!f.Looking) YawOffset += Clamp((yawGoal - YawOffset) * a, -ym, ym);
                Shoulder += Clamp((shoulderGoal - Shoulder) * a, -sm, sm);
                double clamped = Clamp(YawOffset, -cap * K, cap * K);
                _fold += YawOffset - clamped;
                YawOffset = clamped;
            }
            PitchOffset = (CombatFrame.PosePitch(keep) - FollowCamera.ThirdPitch) * K;
            Reach = CombatFrame.PoseReach(keep);
            Lift = (CombatFrame.FocusHeight - FollowCamera.ThirdPivotHeight) * K;
            Focus = CombatFrame.FocusShare * K;
        }
    }
}

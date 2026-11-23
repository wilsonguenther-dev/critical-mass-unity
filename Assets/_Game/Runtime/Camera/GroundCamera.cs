using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// Ground view (feat/ground-camera, 2026-11-13, owner: "I want the camera to be able to look, characters to be able to
    /// play from this point of view"): the numbers and the pure maths of the low third-person camera. View only: nothing
    /// here reaches the sim. Unity yaw convention throughout (degrees, Quaternion.Euler(0, yaw, 0)); sim space is SimSpace's
    /// (sim z = -Unity z), so a Unity yaw psi looks along sim (sin psi, -cos psi) and its screen right is sim (cos psi, sin psi).
    /// At psi = 315 that is exactly HumanInputState's fixed 45-degree basis (GroundCameraTests pins it).
    /// </summary>
    public static class GroundCamera
    {
        /// <summary>Distance from the pivot to the camera along the view ray (u): about 3 m behind.</summary>
        public const double Distance = 3.0;
        /// <summary>Pivot height above the followed fighter's ground (u): his upper chest (CharacterHeight is 2.0).</summary>
        public const double PivotHeight = 1.3;
        /// <summary>Over-the-shoulder offset to the camera's right (u), so the fighter does not cover the reticle.</summary>
        public const double Shoulder = 0.45;
        /// <summary>Default pitch (degrees down): camera 1.3 + 3 x sin 7 = about 1.65 u up, horizon on screen.</summary>
        public const double PitchDeg = 7;
        public const double MinPitchDeg = -8, MaxPitchDeg = 38;
        /// <summary>Lens: wider than the high view's 35 (a 35-degree lens at 3 m is a telephoto close-up).</summary>
        public const float FovDeg = 55f;
        public const float NearClip = 0.1f;
        /// <summary>Far clip: the backdrop dome (GroundBackdrop.DomeRadius 420) fits inside it; the arena is 112 x 72.</summary>
        public const float FarClip = 600f;
        /// <summary>Auto yaw easing (s): the camera swings behind the fighter's facing (or toward the hill).</summary>
        public const double YawTau = 0.55;
        /// <summary>Seconds after the last manual orbit before auto yaw takes the camera back (watching only).</summary>
        public const double OrbitHoldSeconds = 2.5;
        /// <summary>Pivot spring stiffness (CameraSpring's critically damped spring; snaps on a cut).</summary>
        public const double PivotOmega = 14;
        /// <summary>Camera distance easing back out after a collision pull-in (s); pull-in itself is immediate.</summary>
        public const double OutTau = 0.35;
        /// <summary>Playing: the aim point sits this far ahead of the fighter along the camera's facing when the reticle
        /// ray finds no ground nearer (u), and never nearer than MinAim.</summary>
        public const double AimDistance = 9, MinAim = 2.5, MaxAim = 22;

        /// <summary>Sim-space basis of a Unity yaw: forward (screen up) and right on the ground, unit length.</summary>
        public static void Basis(double unityYawDeg, out double upX, out double upZ, out double rightX, out double rightZ)
        {
            double r = unityYawDeg * Math.PI / 180;
            double s = Math.Sin(r), c = Math.Cos(r);
            upX = s;
            upZ = -c;
            rightX = c;
            rightZ = s;
        }

        /// <summary>
        /// Camera-relative move: (right, up) is the stick or keys (each -1..1) relative to the camera's facing; the result is
        /// the world-space move the sim already takes (InputFrame.MoveX / MoveZ), length clamped to 1, direction exact.
        /// </summary>
        public static void CameraRelativeMove(double right, double up, double unityYawDeg, out double x, out double z)
        {
            Basis(unityYawDeg, out double ux, out double uz, out double rx, out double rz);
            x = right * rx + up * ux;
            z = right * rz + up * uz;
            double l2 = x * x + z * z;
            if (l2 > 1)
            {
                double inv = 1 / Math.Sqrt(l2);
                x *= inv;
                z *= inv;
            }
        }

        /// <summary>The aim point the sim gets while playing in ground view: d u ahead of the fighter along the camera facing.</summary>
        public static void AimPoint(double px, double pz, double unityYawDeg, double d, out double ax, out double az)
        {
            Basis(unityYawDeg, out double ux, out double uz, out _, out _);
            if (!(d >= MinAim)) d = MinAim;
            if (d > MaxAim) d = MaxAim;
            ax = px + ux * d;
            az = pz + uz * d;
        }

        /// <summary>The Unity yaw that looks from sim (fx, fz) toward sim (tx, tz); fallback when the points coincide.</summary>
        public static double YawToward(double fx, double fz, double tx, double tz, double fallback)
        {
            double dx = tx - fx, dz = tz - fz;
            if (dx * dx + dz * dz < 1e-8) return fallback;
            // forward sim = (sin psi, -cos psi): psi = atan2(dx, -dz).
            return Normalize(Math.Atan2(dx, -dz) * 180 / Math.PI);
        }

        /// <summary>The Unity yaw that looks along a sim facing (radians, sim (cos f, sin f)); SimSpace.YawDeg's maths.</summary>
        public static double YawOfFacing(double facing) => Normalize(90.0 + facing * 180.0 / Math.PI);

        public static double Normalize(double deg) => ((deg % 360) + 360) % 360;

        /// <summary>The next (dir +1) or previous (dir -1) living actor after start, wrapping; -1 when nobody stands.</summary>
        public static int NextAlive(World w, int start, int dir)
        {
            if (w == null || w.ActorCount <= 0) return -1;
            int n = w.ActorCount;
            if (start < 0 || start >= n) start = dir < 0 ? 0 : n - 1;
            for (int k = 1; k <= n; k++)
            {
                int c = ((start + (dir < 0 ? -k : k)) % n + n) % n;
                if (w.Actors[c].Alive) return c;
            }
            return -1;
        }

        /// <summary>Shortest signed angle from a to b, degrees (-180, 180].</summary>
        public static double DeltaAngle(double a, double b)
        {
            double d = Normalize(b - a);
            return d > 180 ? d - 360 : d;
        }
    }

    /// <summary>
    /// Keeps the ground camera out of geometry (pure): walls, intact breakables and higher terraces from the grid
    /// (SeeThroughProbe.Top, the grid view's boxes), plus vertical cylinders for trees, landmarks (towers) and the Kaiju.
    /// The camera is a sphere of <see cref="Radius"/>; the segment from the pivot to the wanted camera position is marched
    /// and the camera stops <see cref="Clearance"/> before the first contact (never closer than <see cref="MinDistance"/>,
    /// where the view stands inside the fighter's own shoulders rather than inside a wall).
    /// </summary>
    public static class GroundCollision
    {
        public const double Radius = 0.3, Clearance = 0.15, MinDistance = 0.55, Step = 0.1;
        /// <summary>The drawn wall art stands a little above the grid's 1.5 u block: keep this much extra headroom.</summary>
        public const double WallMargin = 0.35;

        /// <summary>Solid top of the world at sim (x, z): -infinity off the map and over the void (cliffs fall away).</summary>
        public static double SolidTop(MapDef map, double x, double z)
        {
            if (map == null || map.Cells == null) return double.NegativeInfinity;
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return double.NegativeInfinity;
            int i = cz * map.Width + cx;
            byte c = map.Cells[i];
            if (c == Cell.Void) return double.NegativeInfinity;
            double h = map.Height01 != null ? map.Height01[i] : 0;
            if (c == Cell.Wall || c == Cell.BreakableIntact) return h + SeeThroughProbe.WallHeight + WallMargin;
            if (c == Cell.BreakableCracked) return h + 0.75 + WallMargin;
            if (c == Cell.Water) return -0.2;
            return h;
        }

        /// <summary>True when a sphere of radius r at sim (x, y, z) overlaps the grid or a cylinder.</summary>
        public static bool Inside(MapDef map, double x, double y, double z, double r, SeeThroughCylinder[] cyl, int n)
        {
            // Grid: the centre and four points on the sphere's equator (cells are 1 u, r is 0.3: enough samples).
            if (y - r < SolidTop(map, x, z)) return true;
            if (y < SolidTop(map, x + r, z) || y < SolidTop(map, x - r, z) || y < SolidTop(map, x, z + r) || y < SolidTop(map, x, z - r))
                return true;
            if (cyl != null)
            {
                int m = Math.Min(n, cyl.Length);
                for (int k = 0; k < m; k++)
                {
                    SeeThroughCylinder c = cyl[k];
                    double dx = x - c.X, dz = z - c.Z, rr = c.R + r;
                    if (dx * dx + dz * dz < rr * rr && y + r > c.Y0 && y - r < c.Y1) return true;
                }
            }
            return false;
        }

        /// <summary>fix/px-combat-camera-3: the share of the screen (0..1, a box estimate) a vertical cylinder covers seen from
        /// the lens at (lx, ly, lz) with vertical FOV vfov and aspect (1 when the lens is inside it).</summary>
        public static double OccluderShare(double lx, double ly, double lz, in SeeThroughCylinder c, double vfovDeg, double aspect)
        {
            double dx = c.X - lx, dz = c.Z - lz, dc = Math.Sqrt(dx * dx + dz * dz);
            if (dc <= c.R) return ly >= c.Y0 && ly <= c.Y1 ? 1 : 0.5;
            double tv = Math.Tan(Math.Max(10, vfovDeg) * Math.PI / 360), th = tv * Math.Max(0.5, aspect);
            double half = Math.Asin(Math.Min(1, c.R / dc));
            double w = Math.Min(1, Math.Tan(Math.Min(half, 1.5)) / th);
            double near = Math.Max(0.05, dc - c.R);
            double top = Math.Atan((c.Y1 - ly) / near), bot = Math.Atan((c.Y0 - ly) / near);
            double h = Math.Min(1, Math.Max(0, Math.Tan(Math.Min(top, 1.5)) - Math.Tan(Math.Max(bot, -1.5))) / (2 * tv));
            return w * h;
        }

        /// <summary>fix/px-combat-camera-3: true when the segment a-b passes through the cylinder (in plan and in height),
        /// ignoring its last stopShort u.</summary>
        public static bool Crosses(double ax, double ay, double az, double bx, double by, double bz, double stopShort, in SeeThroughCylinder c)
        {
            double dx = bx - ax, dy = by - ay, dz = bz - az;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double end = len - stopShort;
            if (!(end > 0)) return false;
            for (double t = 0; t <= end; t += Step * 0.5)
            {
                double k = t / len, x = ax + dx * k, y = ay + dy * k, z = az + dz * k;
                double ex = x - c.X, ez = z - c.Z;
                if (ex * ex + ez * ez < c.R * c.R && y > c.Y0 && y < c.Y1) return true;
            }
            return false;
        }

        /// <summary>fix/px-combat-camera-3: how far back (at most want) the lens may sit along (dx, dy, dz) from (px, py, pz)
        /// so that no cylinder crossing the sight line to the chest (sx, sy, sz) covers more than MaxOccluderShare of the
        /// screen: pulled in past it. When even MinDistance does not clear it, want comes back with blocked set (the caller
        /// fades it instead) and r its radius.</summary>
        public static double SightLimit(double px, double py, double pz, double dx, double dy, double dz, double want,
            double sx, double sy, double sz, double vfovDeg, double aspect, SeeThroughCylinder[] cyl, int n, out bool blocked, out double r)
        {
            blocked = false;
            r = 0;
            if (cyl == null || n <= 0 || !(want > MinDistance)) return want;
            int m = Math.Min(n, cyl.Length);
            for (double t = want; t >= MinDistance - 1e-9; t -= Step)
            {
                double lx = px + dx * t, ly = py + dy * t, lz = pz + dz * t;
                bool big = false;
                for (int k = 0; k < m && !big; k++)
                {
                    if (!Crosses(lx, ly, lz, sx, sy, sz, CombatFrame.SightStopU, cyl[k])) continue;
                    if (OccluderShare(lx, ly, lz, cyl[k], vfovDeg, aspect) <= GroundFollow.MaxOccluderShare) continue;
                    big = true;
                    r = Math.Max(r, cyl[k].R);
                }
                if (!big) { r = 0; return t; }
            }
            blocked = true;
            return want;
        }

        /// <summary>fix/px-combat-camera-2: true when the sight line from a (the lens) to b (a fighter's chest) meets the grid
        /// or a cylinder (trees with their crowns, rocks, crystals, props, buildings) before stopShort u from b. The lens
        /// itself inside something counts as blocked.</summary>
        public static bool Occluded(MapDef map, double ax, double ay, double az, double bx, double by, double bz, double stopShort,
            SeeThroughCylinder[] cyl, int n)
        {
            double dx = bx - ax, dy = by - ay, dz = bz - az;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double end = len - stopShort;
            if (!(end > 0)) return false;
            dx /= len; dy /= len; dz /= len;
            for (double t = 0; t <= end; t += Step)
                if (Inside(map, ax + dx * t, ay + dy * t, az + dz * t, CombatFrame.SightRadius, cyl, n)) return true;
            return Inside(map, ax + dx * end, ay + dy * end, az + dz * end, CombatFrame.SightRadius, cyl, n);
        }

        /// <summary>
        /// How far (u) the camera may sit from the pivot (px, py, pz) along the unit direction (dx, dy, dz), at most want.
        /// The pivot itself is never tested (it is inside the fighter). Below MinDistance the caller lifts the camera over
        /// the fighter's head instead (GroundFollow).
        /// </summary>
        public static double Resolve(MapDef map, double px, double py, double pz, double dx, double dy, double dz, double want,
            SeeThroughCylinder[] cyl, int n)
        {
            if (!(want > 0)) return 0;
            int steps = (int)Math.Ceiling(want / Step);
            double safe = 0;
            for (int i = 1; i <= steps; i++)
            {
                double t = Math.Min(want, i * Step);
                if (Inside(map, px + dx * t, py + dy * t, pz + dz * t, Radius, cyl, n))
                    return Math.Max(0, Math.Min(safe, t - Clearance));
                safe = t;
            }
            return want;
        }
    }

    /// <summary>
    /// feat/follow-camera-1: the follow's shape. Unset (default) = the ground view's own numbers (GroundCamera.Distance,
    /// PivotHeight, Shoulder, pitch limits); the third-person and first-person views pass theirs (FollowCamera).
    /// </summary>
    public struct GroundRig
    {
        public bool Set;
        /// <summary>First person: the camera sits at the pivot (the eyes), a little ahead along the view; no pull-in.</summary>
        public bool FirstPerson;
        public double Distance, PivotHeight, Shoulder, MinPitch, MaxPitch;
    }

    /// <summary>One frame's inputs to <see cref="GroundFollow.Step"/>.</summary>
    public struct GroundFollowInput
    {
        /// <summary>The follow's shape (unset: the ground view's).</summary>
        public GroundRig Rig;
        public double Dt;
        /// <summary>The followed body in sim space and the ground under it.</summary>
        public double TargetX, TargetZ, GroundY;
        /// <summary>Extra pivot height (the Kaiju is 5 u tall: the camera frames its chest, not its feet).</summary>
        public double PivotLift;
        /// <summary>Extra distance (the Kaiju needs room).</summary>
        public double ExtraDistance;
        /// <summary>The yaw auto mode swings toward (Unity degrees): behind the fighter's facing, toward his aim or the hill.</summary>
        public double WantYaw;
        /// <summary>Auto yaw on (watching); off while playing (the player's mouse or stick owns the yaw).</summary>
        public bool AutoYaw;
        /// <summary>Manual orbit this frame (degrees): yaw right, pitch down.</summary>
        public double OrbitYaw, OrbitPitch;
        /// <summary>Cut: frame the target at once (new target, entering the view).</summary>
        public bool Snap;
        public MapDef Map;
        public SeeThroughCylinder[] Cylinders;
        public int CylinderCount;
        /// <summary>fix/px-combat-camera-3 (pass 3: the plain follow pumped 1.6 to 4.7 u in half a second in a forest): the
        /// distance pulls in at once but lets out only after the path has been clear for ClearHoldSeconds, then over about
        /// a second (SlowOutTau). Off: the old OutTau ease (the director's watch view).</summary>
        public bool SlowOut;
        /// <summary>fix/px-combat-camera-3: the followed fighter's chest (sim). With SightOn, a scenery cylinder between the
        /// lens and it that would cover more than MaxOccluderShare of the screen pulls the lens in past it.</summary>
        public bool SightOn;
        public double SightX, SightY, SightZ;
        /// <summary>Vertical field of view (deg) and aspect for the occluder's screen share.</summary>
        public double VFovDeg, Aspect;
        /// <summary>fix/px-camera-in-editor: the third-person play camera's simple rules (GroundFollow.StepRobust): the
        /// boom collides only with what is passed in Cylinders (hard scenery) and the grid, never shorter than RobustMin,
        /// rises up to RobustMaxSteep over a wall, never goes over the head, and keeps the fighter (SightX/Y/Z) in frame.
        /// Whatever is still in the way is faded by GroundView, never dodged by moving in.</summary>
        public bool Robust;
        /// <summary>fix/px-camera-in-editor: 1 = the robust look keeps the whole fighter in frame, 0 = only his chest (the
        /// combat framing aims between the fighters).</summary>
        public double BodyRule;
    }

    /// <summary>
    /// The low follow (pure, testable): a pivot on the followed body's chest on a stiff spring, a yaw that eases behind
    /// the body (or is turned by the player), a pitch the player may orbit, an over-the-shoulder offset, and a distance
    /// that collision pulls in at once and lets out slowly. Output: camera position (sim x, height, sim z) and the yaw /
    /// pitch it looks along.
    /// </summary>
    public sealed class GroundFollow
    {
        public double Yaw { get; private set; } = 315;
        public double Pitch { get; private set; } = GroundCamera.PitchDeg;
        /// <summary>Current camera distance after collision and easing.</summary>
        public double Dist { get; private set; } = GroundCamera.Distance;
        /// <summary>The distance collision allowed this frame (tests).</summary>
        public double Allowed { get; private set; } = GroundCamera.Distance;
        public double PivotX => _px.X;
        public double PivotY => _py.X;
        public double PivotZ => _pz.X;
        /// <summary>Camera position, sim x / height / sim z.</summary>
        public double CamX { get; private set; }
        public double CamY { get; private set; }
        public double CamZ { get; private set; }
        /// <summary>fix/px-combat-camera-3: a scenery cylinder still stands between the lens and the chest this frame (the
        /// pull-in could not clear it), and its radius: GroundView widens the occluder dither for it.</summary>
        public bool SightBlocked { get; private set; }
        public double SightR { get; private set; }

        public const double ClearHoldSeconds = 0.4, SlowOutTau = 0.33, MaxOccluderShare = 0.35;
        double _clearFor;

        readonly Spring1 _px = CameraSpring.CreateSpring(0), _py = CameraSpring.CreateSpring(0), _pz = CameraSpring.CreateSpring(0);
        double _sinceOrbit = 999;
        bool _placed;

        public void SetYaw(double yaw) => Yaw = GroundCamera.Normalize(yaw);
        public void SetPitch(double p) => Pitch = Clamp(p, GroundCamera.MinPitchDeg, GroundCamera.MaxPitchDeg);
        /// <summary>Pitch within explicit limits (the follow views' wider ranges).</summary>
        public void SetPitch(double p, double lo, double hi) => Pitch = Clamp(p, lo, hi);
        /// <summary>Counts as a manual orbit now: auto yaw waits OrbitHoldSeconds before it swings back.</summary>
        public void HoldAuto() => _sinceOrbit = 0;

        public void Step(in GroundFollowInput f)
        {
            double dt = f.Dt > 0 ? f.Dt : 0;
            bool rig = f.Rig.Set;
            double pivotH = rig ? f.Rig.PivotHeight : GroundCamera.PivotHeight;
            double minP = rig ? f.Rig.MinPitch : GroundCamera.MinPitchDeg, maxP = rig ? f.Rig.MaxPitch : GroundCamera.MaxPitchDeg;
            double shoulder = rig ? f.Rig.Shoulder : GroundCamera.Shoulder;
            double baseDist = rig ? f.Rig.Distance : GroundCamera.Distance;
            double gx = f.TargetX, gy = f.GroundY + pivotH + f.PivotLift, gz = f.TargetZ;
            bool snap = f.Snap || !_placed;
            if (snap)
            {
                _px.X = gx; _py.X = gy; _pz.X = gz;
                _px.V = _py.V = _pz.V = 0;
                _placed = true;
            }
            else
            {
                CameraSpring.SpringStep(_px, gx, GroundCamera.PivotOmega, dt);
                CameraSpring.SpringStep(_py, gy, GroundCamera.PivotOmega, dt);
                CameraSpring.SpringStep(_pz, gz, GroundCamera.PivotOmega, dt);
            }

            // Yaw: manual orbit first; auto swings behind the body once the orbit has rested.
            bool orbiting = Math.Abs(f.OrbitYaw) > 1e-6 || Math.Abs(f.OrbitPitch) > 1e-6;
            if (orbiting) _sinceOrbit = 0;
            else _sinceOrbit += dt;
            Yaw = GroundCamera.Normalize(Yaw + f.OrbitYaw);
            Pitch = Clamp(Pitch + f.OrbitPitch, minP, maxP);
            if (f.AutoYaw)
            {
                if (snap) Yaw = GroundCamera.Normalize(f.WantYaw);
                else if (_sinceOrbit >= GroundCamera.OrbitHoldSeconds && dt > 0)
                {
                    double k = 1 - Math.Exp(-dt / GroundCamera.YawTau);
                    Yaw = GroundCamera.Normalize(Yaw + GroundCamera.DeltaAngle(Yaw, f.WantYaw) * k);
                    // The pitch drifts home too, so an orbit never leaves the director's shots looking at the sky.
                    Pitch += (GroundCamera.PitchDeg - Pitch) * k * 0.5;
                }
            }

            // The view ray (sim space): forward (yaw, pitch), the camera behind the pivot along it, shifted right.
            GroundCamera.Basis(Yaw, out double ux, out double uz, out double rx, out double rz);
            double pr = Pitch * Math.PI / 180;
            double cp = Math.Cos(pr), sp = Math.Sin(pr);
            double fx = ux * cp, fy = -sp, fz = uz * cp;
            if (rig && f.Rig.FirstPerson)
            {
                // The eyes: the pivot a little ahead along the ground facing (the hidden body never fills the lens).
                // follow-camera-3: the eyes never poke into a wall the fighter is pressed against.
                double ef = FollowCamera.EyeOffset(f.Map, _px.X, _py.X, _pz.X, ux, uz, f.Cylinders, f.CylinderCount);
                CamX = _px.X + ux * ef;
                CamY = _py.X;
                CamZ = _pz.X + uz * ef;
                Dist = Allowed = 0;
                OverHead = false;
                return;
            }
            double ox = _px.X + rx * shoulder, oy = _py.X, oz = _pz.X + rz * shoulder;
            // The shoulder offset itself must not sit inside a wall: shrink it first (pivot to shoulder segment).
            // fix/px-combat-camera: either side (the combat framing shifts the lens left as well as right; a left shift was
            // never tested against walls before). A positive shoulder takes exactly the old path.
            double sAbs = Math.Abs(shoulder), sDir = shoulder >= 0 ? 1 : -1;
            double sh = sAbs > 0 ? GroundCollision.Resolve(f.Map, _px.X, _py.X, _pz.X, rx * sDir, 0, rz * sDir, sAbs, f.Cylinders, f.CylinderCount) : 0;
            if (sh < sAbs)
            {
                ox = _px.X + rx * sDir * sh;
                oz = _pz.X + rz * sDir * sh;
            }
            double want = baseDist + f.ExtraDistance;
            if (f.Robust) { StepRobust(f, dt, snap, ox, oy, oz, ux, uz, want); return; }
            double allowed = GroundCollision.Resolve(f.Map, ox, oy, oz, -fx, -fy, -fz, want, f.Cylinders, f.CylinderCount);
            SightBlocked = false;
            SightR = 0;
            if (f.SightOn)
            {
                allowed = GroundCollision.SightLimit(ox, oy, oz, -fx, -fy, -fz, allowed, f.SightX, f.SightY, f.SightZ, f.VFovDeg, f.Aspect,
                                                     f.Cylinders, f.CylinderCount, out bool blocked, out double r);
                SightBlocked = blocked;
                SightR = r;
            }
            Allowed = allowed;
            if (snap || allowed < Dist) { Dist = allowed; _clearFor = 0; }
            else if (dt > 0)
            {
                if (f.SlowOut)
                {
                    // pull in at once; out only once the path has stayed clear ClearHoldSeconds, then slowly (no pumping)
                    _clearFor = allowed > Dist + 0.05 ? _clearFor + dt : 0;
                    if (_clearFor >= ClearHoldSeconds) Dist += (allowed - Dist) * (1 - Math.Exp(-dt / SlowOutTau));
                }
                else Dist += (allowed - Dist) * (1 - Math.Exp(-dt / GroundCamera.OutTau));
            }
            if (Dist > allowed) Dist = allowed;
            CamX = ox - fx * Dist;
            CamY = oy - fy * Dist;
            CamZ = oz - fz * Dist;
            // Never under the ground below the camera (a downhill orbit or a low pitch over a terrace edge).
            double floor = GroundCollision.SolidTop(f.Map, CamX, CamZ);
            if (CamY - GroundCollision.Radius < floor) CamY = floor + GroundCollision.Radius;
            // Backed against a wall (no room even for MinDistance) or still touching after the lift: look over the
            // fighter's head from just above him; last resort the pivot itself (inside the fighter, never inside a wall).
            if (Dist < GroundCollision.MinDistance || GroundCollision.Inside(f.Map, CamX, CamY, CamZ, GroundCollision.Radius, f.Cylinders, f.CylinderCount))
            {
                double hx = gx - fx * 0.25, hy = gy + HeadLift, hz = gz - fz * 0.25;
                if (!GroundCollision.Inside(f.Map, hx, hy, hz, GroundCollision.Radius, f.Cylinders, f.CylinderCount))
                {
                    CamX = hx; CamY = hy; CamZ = hz;
                }
                else
                {
                    CamX = gx; CamY = gy; CamZ = gz;
                }
                OverHead = true;
            }
            else OverHead = false;
        }

        // ------------------------------------------------------------------ fix/px-camera-in-editor: the robust boom

        /// <summary>Never closer to the boom's pivot than this (u); the lens keeps RobustRadius clear of walls and the
        /// ground RobustFloor under it (the HUD planes at 0.4-0.6 m never meet geometry); never steeper than RobustMaxSteep
        /// (deg) looking at the chest; the chest stays RobustFrameMargin (deg) inside the frame.</summary>
        public const double RobustMin = 1.6, RobustRadius = 0.6, RobustFloor = 0.7, RobustMaxSteep = 55, RobustFrameMargin = 5;
        /// <summary>The fighter for the frame rule: the sight point (chest) is RobustChestUp over his feet, he is RobustBodyTall.</summary>
        public const double RobustChestUp = 1.2, RobustBodyTall = 2.1;
        /// <summary>Distance: in at once (a wall), out only after RobustHold s clear, then on RobustOutTau (no pumping).
        /// Wall rise: up on RobustRiseTau, down after RobustHold on RobustFallTau.</summary>
        public const double RobustHold = 1.0, RobustOutTau = 1.3, RobustRiseTau = 0.08, RobustFallTau = 0.9;

        double _rise, _riseClear, _outClear;
        /// <summary>fix/px-camera-in-editor: even at RobustMin and the steepest rise the boom touches hard geometry (the
        /// caller fades what is in the way); the boom's pitch and the drawn look pitch this frame (tests, captures).</summary>
        public bool Blocked { get; private set; }
        public double BoomPitch { get; private set; }

        static double ResolveR(MapDef map, double px, double py, double pz, double dx, double dy, double dz, double want,
            SeeThroughCylinder[] cyl, int n, double radius)
        {
            if (!(want > 0)) return 0;
            int steps = (int)Math.Ceiling(want / GroundCollision.Step);
            double safe = 0;
            for (int i = 1; i <= steps; i++)
            {
                double t = Math.Min(want, i * GroundCollision.Step);
                if (GroundCollision.Inside(map, px + dx * t, py + dy * t, pz + dz * t, radius, cyl, n))
                    return Math.Max(0, Math.Min(safe, t - GroundCollision.Clearance));
                safe = t;
            }
            return want;
        }

        double BoomAt(in GroundFollowInput f, double ox, double oy, double oz, double ux, double uz, double pitchDeg, double want)
        {
            double pr = pitchDeg * Math.PI / 180, cp = Math.Cos(pr), sp = Math.Sin(pr);
            return ResolveR(f.Map, ox, oy, oz, -ux * cp, sp, -uz * cp, want, f.Cylinders, f.CylinderCount, RobustRadius);
        }

        double SteepAt(in GroundFollowInput f, double ox, double oy, double oz, double ux, double uz, double pitchDeg, double dist)
        {
            double pr = pitchDeg * Math.PI / 180;
            double cx = ox - ux * Math.Cos(pr) * dist, cy = oy + Math.Sin(pr) * dist, cz = oz - uz * Math.Cos(pr) * dist;
            double h = Math.Sqrt((cx - f.SightX) * (cx - f.SightX) + (cz - f.SightZ) * (cz - f.SightZ));
            return Math.Atan2(cy - f.SightY, Math.Max(1e-3, h)) * 180 / Math.PI;
        }

        void StepRobust(in GroundFollowInput f, double dt, bool snap, double ox, double oy, double oz, double ux, double uz, double want)
        {
            OverHead = false;
            double basePitch = Pitch;
            want = Math.Max(RobustMin, want);
            // 1. the wall rise: the smallest extra pitch (5 deg steps) that lets the boom reach RobustMin, within the
            // steepness limit; none fits: the steepest allowed (what is still in the way is faded by the caller)
            double riseWant = 0;
            if (BoomAt(f, ox, oy, oz, ux, uz, basePitch, want) < RobustMin)
            {
                riseWant = -1;
                double last = 0;
                for (double b = 5; b <= 60; b += 5)
                {
                    if (SteepAt(f, ox, oy, oz, ux, uz, basePitch + b, RobustMin) > RobustMaxSteep) break;
                    last = b;
                    if (BoomAt(f, ox, oy, oz, ux, uz, basePitch + b, want) >= RobustMin) { riseWant = b; break; }
                }
                if (riseWant < 0) riseWant = last;
            }
            if (snap || dt <= 0) { _rise = snap ? riseWant : _rise; _riseClear = 0; }
            else if (riseWant > _rise) { _rise += (riseWant - _rise) * (1 - Math.Exp(-dt / RobustRiseTau)); _riseClear = 0; }
            else
            {
                _riseClear += dt;
                if (_riseClear >= RobustHold) _rise += (riseWant - _rise) * (1 - Math.Exp(-dt / RobustFallTau));
            }
            double boomPitch = basePitch + _rise;
            BoomPitch = boomPitch;
            // 2. the distance: in at once, out slowly after a hold; never under RobustMin
            double allowed = Math.Max(RobustMin, Math.Min(want, BoomAt(f, ox, oy, oz, ux, uz, boomPitch, want)));
            Allowed = allowed;
            if (snap || allowed < Dist) { Dist = allowed; _outClear = 0; }
            else if (dt > 0)
            {
                _outClear = allowed > Dist + 0.05 ? _outClear + dt : 0;
                if (_outClear >= RobustHold) Dist += (allowed - Dist) * (1 - Math.Exp(-dt / RobustOutTau));
            }
            Dist = Math.Max(RobustMin, Math.Min(Dist, want));
            double pr = boomPitch * Math.PI / 180, cp = Math.Cos(pr), sp = Math.Sin(pr);
            CamX = ox - ux * cp * Dist;
            CamY = oy + sp * Dist;
            CamZ = oz - uz * cp * Dist;
            double floor = GroundCollision.SolidTop(f.Map, CamX, CamZ);
            if (CamY - RobustFloor < floor) CamY = floor + RobustFloor;
            // never closer than RobustMin to the fighter himself either (the framing's look point sits toward the enemy, and
            // a knockback can carry him at the lens): further out along the same boom, so the framing's angle is kept
            {
                double t = Dist;
                for (int i = 0; i < 60; i++)
                {
                    double ddx = CamX - f.SightX, ddy = CamY - f.SightY, ddz = CamZ - f.SightZ;
                    if (ddx * ddx + ddy * ddy + ddz * ddz >= RobustMin * RobustMin) break;
                    t += 0.05;
                    CamX = ox - ux * cp * t;
                    CamY = Math.Max(oy + sp * t, floor + RobustFloor);
                    CamZ = oz - uz * cp * t;
                }
            }
            Blocked = GroundCollision.Inside(f.Map, CamX, CamY, CamZ, GroundCollision.Radius, f.Cylinders, f.CylinderCount)
                      || BoomAt(f, ox, oy, oz, ux, uz, boomPitch, want) < RobustMin - 1e-6;
            SightBlocked = false;
            SightR = 0;
            // 3. the drawn look: the whole fighter (feet to head top) stays RobustFrameMargin inside the bottom and top of
            // the frame; when he is too tall for that at this distance, he is centred
            double h = Math.Max(1e-3, Math.Sqrt((CamX - f.SightX) * (CamX - f.SightX) + (CamZ - f.SightZ) * (CamZ - f.SightZ)));
            double feetY = f.SightY - RobustChestUp, headY = feetY + RobustBodyTall;
            double sFeet = Math.Atan2(CamY - feetY, h) * 180 / Math.PI, sHead = Math.Atan2(CamY - headY, h) * 180 / Math.PI;
            double half = Math.Max(10, (f.VFovDeg > 0 ? f.VFovDeg : 60) * 0.5) - RobustFrameMargin;
            // in a framed fight (BodyRule 0) only the chest is held in frame: the framing's own aim between the two fighters
            // is kept (the whole-body rule pitched down onto me and put my body over the enemy)
            double sChest = Math.Atan2(CamY - f.SightY, h) * 180 / Math.PI;
            double lo = sFeet - half, hi = sHead + half;
            double whole = lo <= hi ? Clamp(boomPitch, lo, hi) : (sFeet + sHead) * 0.5;
            double chestOnly = Clamp(boomPitch, sChest - half, sChest + half);
            double k = Clamp(f.BodyRule, 0, 1);
            Pitch = chestOnly + (whole - chestOnly) * k;
        }

        /// <summary>Over-the-head fallback height above the pivot (u).</summary>
        public const double HeadLift = 1.1;
        /// <summary>True when this frame used the over-the-head fallback (tests).</summary>
        public bool OverHead { get; private set; }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>
    /// The auto-director (pure): picks an interesting body to follow and cuts to it with a minimum shot length. Scores:
    /// the hatched Kaiju; fighters in a duel (an enemy within 3 u); a crowd (bodies within 6 u); on or near the hill;
    /// carrying cargo or meat. A cut needs the current shot to be MinShot old and the best to beat the current by
    /// SwitchMargin (or the current target to be gone); after MaxShot the best different target is taken.
    /// </summary>
    public sealed class GroundDirector
    {
        public const int KaijuTarget = -2;
        public const double MinShot = 4.0, MaxShot = 11.0, SwitchMargin = 1.3;
        public const double DuelRange = 3.0, CrowdRange = 6.0;

        public int Current { get; private set; } = -1;
        public double ShotAge { get; private set; }
        public int Cuts { get; private set; }

        public void Reset()
        {
            Current = -1;
            ShotAge = 0;
            Cuts = 0;
        }

        /// <summary>True when the target (an actor id or KaijuTarget) exists and can be filmed.</summary>
        public static bool Valid(World w, int id)
        {
            if (w == null) return false;
            if (id == KaijuTarget) return w.Kaiju != null && w.Match.Hatched && w.Kaiju.State != KaijuState.Egg;
            return id >= 0 && id < w.ActorCount && w.Actors[id].Alive;
        }

        /// <summary>How interesting a target is right now (0 = nothing happening, below 0 = invalid).</summary>
        public static double Score(World w, int id)
        {
            if (!Valid(w, id)) return -1;
            if (id == KaijuTarget) return 6.0;
            double x = w.X[id], z = w.Z[id];
            TeamId team = w.Actors[id].Team;
            double s = 0.1;
            double nearestEnemy = double.MaxValue;
            int crowd = 0;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == id || !w.Actors[j].Alive) continue;
                double dx = w.X[j] - x, dz = w.Z[j] - z, d2 = dx * dx + dz * dz;
                if (d2 <= CrowdRange * CrowdRange) crowd++;
                if (w.Actors[j].Team != team && d2 < nearestEnemy) nearestEnemy = d2;
            }
            if (nearestEnemy <= DuelRange * DuelRange) s += 3.0;
            s += Math.Min(3.0, crowd * 0.4);
            HillDef hill = w.Map != null ? w.Map.Hill : null;
            if (hill != null)
            {
                double hx = x - hill.X, hz = z - hill.Z;
                if (hx * hx + hz * hz <= (hill.R + 2) * (hill.R + 2)) s += 2.0;
            }
            Actor a = w.Actors[id];
            if (a.Carry != CarryKind.None || a.CarryMeat > 0) s += 1.5;
            if (w.Match.Hatched && w.Kaiju != null && w.Kaiju.State != KaijuState.Egg)
            {
                double kx = x - w.Kaiju.X, kz = z - w.Kaiju.Z;
                if (kx * kx + kz * kz < 64) s += 1.0;
            }
            return s;
        }

        /// <summary>One frame; true when this frame is a cut (Current changed).</summary>
        public bool Step(World w, double dt)
        {
            if (w == null) return false;
            ShotAge += dt > 0 ? dt : 0;
            int best = -1;
            double bestScore = 0;
            int bestOther = -1;
            double bestOtherScore = 0;
            for (int id = KaijuTarget; id < w.ActorCount; id++)
            {
                if (id == -1) continue;
                double s = Score(w, id);
                if (s <= 0) continue;
                if (s > bestScore) { bestScore = s; best = id; }
                if (id != Current && s > bestOtherScore) { bestOtherScore = s; bestOther = id; }
            }
            if (best < 0) return false;
            bool currentValid = Valid(w, Current);
            int pick = Current;
            if (!currentValid) pick = best;
            else if (ShotAge >= MaxShot && bestOther >= 0) pick = bestOther;
            else if (ShotAge >= MinShot && best != Current && bestScore > Score(w, Current) * SwitchMargin) pick = best;
            if (pick == Current) return false;
            Current = pick;
            ShotAge = 0;
            Cuts++;
            return true;
        }

        /// <summary>Follow this target from now (a manual pick); the shot clock restarts.</summary>
        public void Force(int id)
        {
            Current = id;
            ShotAge = 0;
        }
    }
}

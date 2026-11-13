using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.Spectator
{
    /// <summary>What the spectator camera looks at.</summary>
    public enum SpectatorFocus { Player, Objective, Free }

    /// <summary>The places the number keys (or the d-pad) jump to.</summary>
    public enum SpectatorObjective { Hill, Shrine, AzureBase, CrimsonBase, Kaiju }

    /// <summary>
    /// The spectator camera's rules, free of Unity objects so tests can step them: follow a player (cycle through every
    /// actor, or pick the one nearest a ground point), jump to an objective (the hill, the shrine, either base, the Kaiju,
    /// which is followed as it moves), or fly free with the move input; zoom; the top-down tactical view flag. The look
    /// point (X, Z, sim space) eases toward the goal on CameraSpring's critically damped spring, so a cut between players
    /// travels instead of teleporting. Reads the world only; never writes it.
    /// </summary>
    public sealed class SpectatorRig
    {
        /// <summary>Free-fly speed in sim units per second at zoom 1 (scales with zoom).</summary>
        public const double FlySpeed = 22;
        public const double MinZoom = 0.6, MaxZoom = 2.4, ZoomStep = 0.12;
        /// <summary>Clicking within this many sim units of a player follows them.</summary>
        public const double PickRadius = 2.5;
        /// <summary>Spring stiffness of the look point (CameraSpring.FollowOmega is 9; a little softer for cuts).</summary>
        public const double Omega = 7;
        /// <summary>Speeds the spectator may pick, as multiples of the match's own pace (SimDriverBehaviour.TimeScale).</summary>
        public static readonly double[] Speeds = { 0.5, 1, 2 };
        public const int DefaultSpeed = 1;

        public SpectatorFocus Focus { get; private set; } = SpectatorFocus.Player;
        /// <summary>The followed actor (valid while Focus is Player).</summary>
        public int Followed { get; private set; }
        public SpectatorObjective Objective { get; private set; }
        public double Zoom { get; private set; } = 1;
        public bool Tactical { get; private set; }
        public int SpeedIndex { get; private set; } = DefaultSpeed;
        public double Speed => Speeds[SpeedIndex];
        /// <summary>The eased look point, sim space.</summary>
        public double X => _sx.X;
        public double Z => _sz.X;

        readonly Spring1 _sx = CameraSpring.CreateSpring(0);
        readonly Spring1 _sz = CameraSpring.CreateSpring(0);
        double _freeX, _freeZ;
        bool _placed;

        /// <summary>Starts on actor follow (snaps the look point there), zoom 1, normal view, normal speed.</summary>
        public void Reset(World w, int follow)
        {
            Focus = SpectatorFocus.Player;
            Followed = Valid(w, follow) ? follow : 0;
            Zoom = 1;
            Tactical = false;
            SpeedIndex = DefaultSpeed;
            _placed = false;
            if (w != null && w.ActorCount > 0) Snap(w.X[Followed], w.Z[Followed]);
        }

        public void Snap(double x, double z)
        {
            _sx.X = x;
            _sz.X = z;
            _sx.V = _sz.V = 0;
            _freeX = x;
            _freeZ = z;
            _placed = true;
        }

        static bool Valid(World w, int id) => w != null && id >= 0 && id < w.ActorCount;

        /// <summary>Follow actor id; false when there is no such actor.</summary>
        public bool Follow(World w, int id)
        {
            if (!Valid(w, id)) return false;
            Focus = SpectatorFocus.Player;
            Followed = id;
            return true;
        }

        /// <summary>
        /// The next (dir +1) or previous (dir -1) actor after the followed one, by id, wrapping, skipping the knocked out
        /// while anyone is standing. From an objective or free flight it starts at the followed actor. Returns the id.
        /// </summary>
        public int Cycle(World w, int dir)
        {
            if (w == null || w.ActorCount <= 0) return Followed;
            int n = w.ActorCount;
            int step = dir < 0 ? -1 : 1;
            int start = Valid(w, Followed) ? Followed : 0;
            bool anyAlive = false;
            for (int i = 0; i < n; i++)
                if (w.Actors[i].Alive)
                {
                    anyAlive = true;
                    break;
                }
            int id = start;
            for (int k = 1; k <= n; k++)
            {
                int c = ((start + step * k) % n + n) % n;
                if (anyAlive && !w.Actors[c].Alive) continue;
                id = c;
                break;
            }
            Follow(w, id);
            return id;
        }

        /// <summary>Look at an objective (the Kaiju is followed as it moves).</summary>
        public void JumpTo(SpectatorObjective o)
        {
            Focus = SpectatorFocus.Objective;
            Objective = o;
        }

        /// <summary>The living actor nearest (sx, sz) within PickRadius, or -1.</summary>
        public static int PickAt(World w, double sx, double sz)
        {
            if (w == null) return -1;
            int best = -1;
            double bestD = PickRadius * PickRadius;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                double dx = w.X[i] - sx, dz = w.Z[i] - sz;
                double d = dx * dx + dz * dz;
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The living actor nearest the look point (any distance), or -1.</summary>
        public int Nearest(World w)
        {
            if (w == null) return -1;
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                double dx = w.X[i] - X, dz = w.Z[i] - Z;
                double d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Free flight: (right, up) is the move input in screen axes (-1..1, the Player map's Move). Any input leaves the
        /// followed player or objective where the camera is and flies from there.
        /// </summary>
        public void FreeFly(double right, double up, double dt)
        {
            if (right * right + up * up < 1e-4) return;
            if (Focus != SpectatorFocus.Free)
            {
                Focus = SpectatorFocus.Free;
                _freeX = X;
                _freeZ = Z;
            }
            double v = FlySpeed * Zoom * (Tactical ? 1.6 : 1.0) * dt;
            _freeX += (right * HumanInputState.ScreenRightX + up * HumanInputState.ScreenUpX) * v;
            _freeZ += (right * HumanInputState.ScreenRightZ + up * HumanInputState.ScreenUpZ) * v;
        }

        /// <summary>Zoom out (steps &gt; 0) or in (steps &lt; 0), clamped to MinZoom..MaxZoom.</summary>
        public void ZoomBy(double steps)
        {
            Zoom = Math.Max(MinZoom, Math.Min(MaxZoom, Zoom * Math.Pow(1 + ZoomStep, steps)));
        }

        public void SetTactical(bool on) => Tactical = on;
        public void ToggleTactical() => Tactical = !Tactical;

        /// <summary>The next speed (wrapping) or a direction (-1 slower, +1 faster, clamped).</summary>
        public void StepSpeed(int dir, bool wrap = false)
        {
            int i = SpeedIndex + (dir < 0 ? -1 : 1);
            if (wrap) i = (i + Speeds.Length) % Speeds.Length;
            SpeedIndex = Math.Max(0, Math.Min(Speeds.Length - 1, i));
        }

        /// <summary>Where an objective is now (sim space); false when the map has no such place.</summary>
        public static bool ObjectivePoint(World w, SpectatorObjective o, out double x, out double z)
        {
            x = z = 0;
            if (w == null || w.Map == null) return false;
            MapDef m = w.Map;
            switch (o)
            {
                case SpectatorObjective.Hill:
                    if (m.Hill == null) return false;
                    x = m.Hill.X;
                    z = m.Hill.Z;
                    return true;
                case SpectatorObjective.Shrine:
                    if (m.Shrine == null) return false;
                    x = m.Shrine.X;
                    z = m.Shrine.Z;
                    return true;
                case SpectatorObjective.AzureBase:
                case SpectatorObjective.CrimsonBase:
                {
                    int t = o == SpectatorObjective.AzureBase ? (int)TeamId.Azure : (int)TeamId.Crimson;
                    if (m.Pads == null || t >= m.Pads.Length || m.Pads[t] == null) return false;
                    x = m.Pads[t].X;
                    z = m.Pads[t].Z;
                    return true;
                }
                case SpectatorObjective.Kaiju:
                    if (w.Kaiju == null) return false;
                    x = w.Kaiju.X;
                    z = w.Kaiju.Z;
                    return true;
            }
            return false;
        }

        /// <summary>The goal of the look point this frame (alpha: the driver's interpolation fraction).</summary>
        public void Goal(World w, double alpha, out double gx, out double gz)
        {
            gx = X;
            gz = Z;
            if (w == null || w.ActorCount <= 0) return;
            switch (Focus)
            {
                case SpectatorFocus.Player:
                {
                    int i = Valid(w, Followed) ? Followed : 0;
                    gx = w.PrevX[i] + (w.X[i] - w.PrevX[i]) * alpha;
                    gz = w.PrevZ[i] + (w.Z[i] - w.PrevZ[i]) * alpha;
                    break;
                }
                case SpectatorFocus.Objective:
                    if (Objective == SpectatorObjective.Kaiju && w.Kaiju != null)
                    {
                        gx = w.Kaiju.PrevX + (w.Kaiju.X - w.Kaiju.PrevX) * alpha;
                        gz = w.Kaiju.PrevZ + (w.Kaiju.Z - w.Kaiju.PrevZ) * alpha;
                    }
                    else if (!ObjectivePoint(w, Objective, out gx, out gz))
                    {
                        gx = X;
                        gz = Z;
                    }
                    break;
                case SpectatorFocus.Free:
                    gx = _freeX;
                    gz = _freeZ;
                    break;
            }
            BoundsDef b = w.Map != null ? w.Map.Bounds : null;
            if (b != null)
            {
                gx = Math.Max(b.MinX, Math.Min(b.MaxX, gx));
                gz = Math.Max(b.MinZ, Math.Min(b.MaxZ, gz));
                if (Focus == SpectatorFocus.Free)
                {
                    _freeX = gx;
                    _freeZ = gz;
                }
            }
        }

        /// <summary>One rendered frame: eases the look point toward the goal (free flight follows the input directly).</summary>
        public void Step(World w, double alpha, double dt)
        {
            Goal(w, alpha, out double gx, out double gz);
            if (!_placed)
            {
                Snap(gx, gz);
                return;
            }
            if (Focus == SpectatorFocus.Free)
            {
                // Free flight is already smooth (input times dt); a spring here would only add lag.
                _sx.X = gx;
                _sz.X = gz;
                _sx.V = _sz.V = 0;
                return;
            }
            if (dt <= 0) return;
            CameraSpring.SpringStep(_sx, gx, Omega, dt);
            CameraSpring.SpringStep(_sz, gz, Omega, dt);
        }
    }
}

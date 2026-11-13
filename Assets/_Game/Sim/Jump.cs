using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// CM-32 jumping (browser src/sim/jump.ts, 09e0904). Travis (2026-11-06): "Jumping". Pure sim, no allocation per step,
    /// all timing in sim ticks.
    /// <para>InputFrame.Jump starts a jump when the actor is alive, not getting up (StandUp 0) and on the ground (AirLen 0).
    /// Movement clears the flag when it reads it, so a stored true never repeats a jump by itself.</para>
    /// <para>The horizontal velocity is LOCKED at takeoff (no air control) and the flight lasts AirLen ticks; the takeoff
    /// step is tick 1, the actor lands at the end of tick AirLen. Mid-air it passes over in-map VOID (Collide), so a 1-cell
    /// gap that blocks walking can be jumped.</para>
    /// <para>Landing guard (bible p13: "no accidental fall deaths"): at takeoff PlanJump replays the whole flight with
    /// Collide.SweepActor (the same Float32 rounding as the real flight, so the planned landing IS the landing) and takes
    /// the longest k in [MinAirTicks, AirTicks] whose end point has a clear footprint; none = the jump is refused. If the
    /// grid still changed under a planned landing, LandJump returns the actor to its takeoff point.</para>
    /// <para>DG-02 additions (not in the browser): a jump BUFFER (a press stays wanted for BufferTicks more ticks, so a press
    /// just before landing, before the get-up ends or refused by the guard fires on the first tick it can) and COYOTE time
    /// (a grounded actor whose footing was removed under it may still take off for CoyoteTicks ticks). Both are whole
    /// ticks from GameConfig seconds, so every machine gets the same result.</para>
    /// <para>The view: JumpPhase (0..1 through the flight at the render interpolation alpha) and JumpLift (the parabola's
    /// height, 0 at takeoff and landing, apex at the middle). A shortened jump of k ticks lifts ApexUnits x (k / AirTicks)^2.</para>
    /// </summary>
    public static class Jump
    {
        /// <summary>Planned flight: PathX[k], PathZ[k] = the end point of air tick k (index 0 = takeoff). Grown, never shrunk.</summary>
        private static double[] PathX = new double[19];
        private static double[] PathZ = new double[19];

        /// <summary>J, airborne ticks of a full jump (18 at the browser config).</summary>
        public static int AirTicks(GameConfig c) => (int)c.Jump.AirTicks;

        /// <summary>DG-02: coyote window in whole sim ticks, JS round(CoyoteSeconds x SimHz) (3 at 0.1 s and 30 Hz).</summary>
        public static int CoyoteTicks(GameConfig c) => (int)JsMath.Round(c.Jump.CoyoteSeconds * c.SimHz);

        /// <summary>DG-02: jump buffer in whole sim ticks, JS round(BufferSeconds x SimHz) (3 at 0.1 s and 30 Hz).</summary>
        public static int BufferTicks(GameConfig c) => (int)JsMath.Round(c.Jump.BufferSeconds * c.SimHz);

        /// <summary>
        /// Plan a jump for actor id taking off now with velocity (vx, vz) at step size dt: the number of air ticks (the
        /// longest clear landing in [MinAirTicks, AirTicks]), or 0 when no landing in that range is clear (refused).
        /// Reads the world only.
        /// </summary>
        public static int PlanJump(World w, int id, double vx, double vz, double dt)
        {
            int airTicks = AirTicks(w.Config);
            int minAir = (int)w.Config.Jump.MinAirTicks;
            if (PathX.Length < airTicks + 1)
            {
                PathX = new double[airTicks + 1];
                PathZ = new double[airTicks + 1];
            }
            double x = w.X[id];
            double z = w.Z[id];
            PathX[0] = x;
            PathZ[0] = z;
            double dx = vx * dt;
            double dz = vz * dt;
            for (int k = 1; k <= airTicks; k++)
            {
                if (dx != 0 || dz != 0)
                {
                    Collide.SweepActor(w, id, x, z, dx, dz, true, out x, out z);
                }
                PathX[k] = x;
                PathZ[k] = z;
            }
            for (int k = airTicks; k >= minAir; k--) if (Collide.FootprintClear(w, id, PathX[k], PathZ[k])) return k;
            return 0;
        }

        /// <summary>
        /// Try to start a jump for actor id with its current (already updated) velocity. On success the actor is airborne
        /// with AirTick 0 (Movement's airborne branch flies tick 1 in this same step) and true is returned. DG-02: refused
        /// once the actor has stood unsupported for more than CoyoteTicks ticks.
        /// </summary>
        public static bool StartJump(World w, int id, double dt)
        {
            Actor a = w.Actors[id];
            if (!a.Alive || a.StandUp > 0 || a.AirLen > 0) return false;
            if (a.UnsupportedTicks > CoyoteTicks(w.Config)) return false;
            int k = PlanJump(w, id, w.Vx[id], w.Vz[id], dt);
            if (k == 0) return false;
            a.AirLen = k;
            a.AirTick = 0;
            a.AirFromX = w.X[id];
            a.AirFromZ = w.Z[id];
            a.AirToX = PathX[k];
            a.AirToZ = PathZ[k];
            return true;
        }

        /// <summary>Back on the ground (also used on death / get-up). No position change.</summary>
        public static void ClearJump(Actor a)
        {
            a.AirLen = 0;
            a.AirTick = 0;
        }

        /// <summary>
        /// The flight's last tick ended: land. The planned landing was clear at takeoff; if the grid changed under it since,
        /// the actor goes back to its takeoff point with zero velocity (never left on a blocked cell). The browser logs the
        /// failure; the Sim has no logger (conventions 11).
        /// </summary>
        public static void LandJump(World w, int id)
        {
            Actor a = w.Actors[id];
            ClearJump(a);
            if (Collide.FootprintClear(w, id, w.X[id], w.Z[id])) return;
            w.X[id] = a.AirFromX;
            w.Z[id] = a.AirFromZ;
            w.Vx[id] = JsMath.F32(0);
            w.Vz[id] = JsMath.F32(0);
        }

        /// <summary>
        /// Progress 0..1 through the current flight at render interpolation alpha (the view draws prev to current, i.e.
        /// tick AirTick - 1 + alpha), or -1 on the ground.
        /// </summary>
        public static double JumpPhase(Actor a, double alpha)
        {
            if (a.AirLen <= 0) return -1;
            double t = (a.AirTick - 1 + alpha) / a.AirLen;
            return t < 0 ? 0 : t > 1 ? 1 : t;
        }

        /// <summary>Height of the jump arc above its base at phase u (0..1): 4 h u (1 - u), h = ApexUnits x (AirLen / AirTicks)^2.</summary>
        public static double JumpLift(GameConfig c, Actor a, double u)
        {
            if (a.AirLen <= 0 || !(u > 0) || !(u < 1)) return 0;
            double s = a.AirLen / c.Jump.AirTicks;
            return 4 * c.Jump.ApexUnits * s * s * u * (1 - u);
        }
    }
}

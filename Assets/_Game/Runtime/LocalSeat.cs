namespace CriticalMass.Runtime
{
    /// <summary>
    /// online-rooms-2: the fighter (actor id) this machine's player controls and the camera, HUD, sound and effects follow.
    /// 0 in single player, always. In an online room each machine plays its own seat (the host actor 0, a joiner for
    /// example actor 16), so every view that used to read actor 0 reads this instead. Set once before an online match's
    /// world is created and reset to 0 when it ends; never read by the Sim.
    /// </summary>
    public static class LocalSeat
    {
        public static int Actor { get; private set; }

        public static void Set(int actor) => Actor = actor < 0 ? 0 : actor;

        public static void Reset() => Actor = 0;
    }

    /// <summary>
    /// online-rooms-9: local feel in an online match without touching the lockstep. The local fighter's body turns at
    /// once toward the aim this machine just sampled (view only: ActorViews reads it for LocalSeat.Actor), while the sim
    /// applies that input Lead ticks later on every machine alike. Nothing here is ever read by the Sim or sent.
    /// </summary>
    public static class OnlineFeel
    {
        public static bool Active;
        static double _ax = double.NaN, _az = double.NaN;

        /// <summary>The latest sampled aim point (sim space), or NaN for none.</summary>
        public static void Note(double aimX, double aimZ) { _ax = aimX; _az = aimZ; }

        public static void Reset() { Active = false; _ax = _az = double.NaN; }

        /// <summary>The yaw (sim radians, the sim's Facing convention) from (x, z) toward the latest aim, if there is one.</summary>
        public static bool TryFacing(double x, double z, out double facing)
        {
            facing = 0;
            if (!Active || double.IsNaN(_ax) || double.IsNaN(_az)) return false;
            double dx = _ax - x, dz = _az - z;
            if (dx * dx + dz * dz < 0.25) return false;   // the pointer on the fighter: keep the sim's facing
            facing = System.Math.Atan2(dz, dx);
            return true;
        }
    }
}

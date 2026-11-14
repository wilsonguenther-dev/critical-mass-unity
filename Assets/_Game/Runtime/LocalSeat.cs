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

        /// <summary>
        /// online-offline-cost-1: true only while an online match runs (or in a hidden online test peer). The offline
        /// SimDriver reads this one plain bool per frame instead of reaching into the online types, so an offline match
        /// never loads or touches anything online. Set by OnlineMatch and OnlineHeadless; never read by the Sim.
        /// </summary>
        public static bool OnlineLive;
    }
}

namespace CriticalMass.Sim
{
    /// <summary>
    /// Version of the simulation data schema. Pure C#: this assembly has no UnityEngine reference.
    /// </summary>
    public static class SimVersion
    {
        /// <summary>b15 comeback + hut choices: 1 -> 2 (RALLY credit and the hut vote are sim state, hashed in StateHash; older
        /// replays are "from a different game version"). If feat/b15-terrain-height (#211, also 1 -> 2) merges first, this
        /// becomes 3 at integration.</summary>
        public const int Schema = 2;
    }
}

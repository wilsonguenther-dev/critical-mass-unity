namespace CriticalMass.Sim
{
    /// <summary>
    /// Version of the simulation data schema. Pure C#: this assembly has no UnityEngine reference.
    /// </summary>
    public static class SimVersion
    {
        /// <summary>b15 terrain height: 1 -> 2 (MapDef.Lift is sim state; older replays are "from a different game version").</summary>
        public const int Schema = 2;
    }
}

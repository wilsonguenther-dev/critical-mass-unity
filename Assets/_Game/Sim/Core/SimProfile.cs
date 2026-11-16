using System;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// fix/frame-cost-1: timing hooks around Sim systems so the player can attribute frame cost (the Sim has no engine
    /// references, so it cannot hold a ProfilerMarker itself). Runtime installs the delegates; null (tests, headless) =
    /// nothing. They read and write no Sim state: the checksum cannot change.
    /// </summary>
    public static class SimProfile
    {
        public const int KaijuHill = 0;
        /// <summary>b11-dg: Kaiju.RefreshClearance when the grid changed ("CM.KaijuClear" in the player, inside CM.KaijuHill).</summary>
        public const int KaijuClearance = 1;
        public static Action<int> Begin, End;

        /// <summary>
        /// b11-dg: Kaiju shadow-grid rebuild counters (whole grid / changed cells only / region labelling), for tests and the
        /// capture. Counted only; nothing in the Sim reads them.
        /// </summary>
        public static int ClearanceFullRebuilds, ClearancePatches, ClearanceRegionBuilds;

        /// <summary>
        /// b11-dg, tests only: true = every clearance refresh is a full rebuild (the path before b11-dg). Both paths give
        /// byte-identical shadow cells, squeeze flags and regions, so the Sim result is the same either way.
        /// </summary>
        public static bool ForceFullClearance;

        /// <summary>Zero the counters (tests).</summary>
        public static void ResetCounters()
        {
            ClearanceFullRebuilds = 0;
            ClearancePatches = 0;
            ClearanceRegionBuilds = 0;
        }
    }
}

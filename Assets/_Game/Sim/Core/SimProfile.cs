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
        public static Action<int> Begin, End;
    }
}

using CriticalMass.Sim.Core;
using Unity.Profiling;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// fix/frame-cost-1: named profiler scopes for the batch-10 frame-cost question (the Air: about 0.6 to 1.1 ms more main
    /// thread per frame than 232f4bd). CM.OnlineView = the online-only view work (turn-to-aim), CM.KaijuHill = the whole
    /// Kaiju system per sim tick (hill targeting, siege, crush), CM.Pace = the pacing writer, CM.HudEdge = the edge-marker
    /// placement. ProfilerMarker Begin/End allocate nothing; the Sim hook is installed once at load.
    /// </summary>
    public static class FrameCostMarkers
    {
        public static readonly ProfilerMarker OnlineView = new ProfilerMarker("CM.OnlineView");
        public static readonly ProfilerMarker KaijuHill = new ProfilerMarker("CM.KaijuHill");
        public static readonly ProfilerMarker Pace = new ProfilerMarker("CM.Pace");
        public static readonly ProfilerMarker HudEdge = new ProfilerMarker("CM.HudEdge");
        /// <summary>b11-dg: the Kaiju shadow-grid refresh after a grid change (inside CM.KaijuHill or CM.Sim's other Kaiju calls).</summary>
        public static readonly ProfilerMarker KaijuClear = new ProfilerMarker("CM.KaijuClear");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            SimProfile.Begin = BeginSim;
            SimProfile.End = EndSim;
        }

        static void BeginSim(int id)
        {
            if (id == SimProfile.KaijuHill) KaijuHill.Begin();
            else if (id == SimProfile.KaijuClearance) KaijuClear.Begin();
        }

        static void EndSim(int id)
        {
            if (id == SimProfile.KaijuHill) KaijuHill.End();
            else if (id == SimProfile.KaijuClearance) KaijuClear.End();
        }
    }
}

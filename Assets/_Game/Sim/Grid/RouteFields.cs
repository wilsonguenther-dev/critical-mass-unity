using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Grid
{
    /// <summary>
    /// b15 (systems audit W6 / defect 16): every bot route field the rules fill lazily gets its buffer at CreateWorld, so no
    /// route allocates its 64,568 B FlowField (a double[112x72] plus the object) the first time a bot walks it mid-match.
    /// <para>Covered: each wood / water / ore source's route (Sources.EnsureField), both hut pad routes (Blacksmith.PadField)
    /// and every ping route slot (PingRules). The Kaiju route field was already allocated by CreateWorld; the barricade
    /// reach check keeps one static field per process.</para>
    /// <para>Pure performance: only empty buffers are created here, nothing is computed. The first-use tests read the new
    /// FieldBuilt / PadFieldBuilt flags (set on the first compute, never cleared, exactly as the old non-null field was never
    /// nulled), and ComputeFlowField overwrites every cell, so every rebuild decision and every distance is what it was.</para>
    /// </summary>
    public static class RouteFields
    {
        /// <summary>
        /// Tests only: true = the old lazy path (no buffer until first use), to print the before / after allocation and to
        /// prove the two paths give identical matches. Never set by the game.
        /// </summary>
        public static bool Lazy;

        /// <summary>Buffers allocated by the last Preallocate call (tests and the capture).</summary>
        public static int LastPreallocated;

        /// <summary>Allocates every missing route buffer of w (CreateWorld, once). Returns how many it allocated.</summary>
        public static int Preallocate(World w)
        {
            LastPreallocated = 0;
            if (Lazy || w == null || w.Map == null) return 0;
            int n = w.Map.Width * w.Map.Height;
            int made = 0;
            DepthState d = w.Depth;
            if (d != null)
            {
                if (d.Sources != null)
                    foreach (SourceNode s in d.Sources)
                        if (s.Field == null) { s.Field = FlowField.Allocate(n); made++; }
                if (d.PingFields != null)
                    for (int i = 0; i < d.PingFields.Length; i++)
                        if (d.PingFields[i] == null) { d.PingFields[i] = FlowField.Allocate(n); made++; }
            }
            HutState h = w.Hut;
            if (h != null && h.PadField != null)
                for (int t = 0; t < h.PadField.Length; t++)
                    if (h.PadField[t] == null) { h.PadField[t] = FlowField.Allocate(n); made++; }
            LastPreallocated = made;
            return made;
        }
    }
}

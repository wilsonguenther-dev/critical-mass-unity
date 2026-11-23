// Class moves build (2026-11-22), lane 1: the view / input-side switches of the class-moves items (the sim's are in
// Sim/Config/MovesConfig.cs). Lane 1 owns this file; lane 6 reads it.
namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// View and input switches of the class moves. OFF by default; the play build (scripting define CM_MOVES_ON) turns them on,
    /// exactly like MovesConfig.PlayBuildAllOn for the sim. None of them can move a sim golden: the sim only receives the
    /// resulting InputFrame (an aim point), and bots never read these.
    /// </summary>
    public static class MovesInput
    {
#if CM_MOVES_ON
        const bool PlayBuild = true;
#else
        const bool PlayBuild = false;
#endif
        /// <summary>The play build was compiled with CM_MOVES_ON (every class-moves switch on).</summary>
        public static bool PlayBuildAllOn => PlayBuild;

        /// <summary>T1-6 (lane 6, AimResolver): stick deflection 0.3..1.0 maps a cast from 1.5 u to the ability's maximum range,
        /// and a keyboard-only cast lands on the assisted target (30 degrees, 7 u) else 3 u. False = AimResolver.StickAimDistance
        /// (3 u) as before. Settable for tests.</summary>
        public static bool StickCastDistance = PlayBuild;
    }
}

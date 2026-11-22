namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// Player-experience wave (2026-11-22): what the aim layer decided this frame, for other view systems to READ (the HUD
    /// draws a bracket on LockTarget and lights HittableTarget; the third-person reticle follows the final aim). Written
    /// only by the input layer (HumanInputSource / AimResolver) once per frame; never read by the sim. The sim receives
    /// only the final aim point in InputFrame, exactly as before.
    /// </summary>
    public static class AimAssist
    {
        /// <summary>The enemy actor the assist is bending the aim toward this frame (-1 none).</summary>
        public static int LockTarget = -1;
        /// <summary>The nearest enemy an attack press THIS frame would hit: inside the class reach + body and the arc at the
        /// facing the final aim gives, with a clear line (the sim's own test, read-only). -1 none.</summary>
        public static int HittableTarget = -1;
        /// <summary>The final aim point sent to the sim this frame (valid while HasAim).</summary>
        public static bool HasAim;
        public static double AimX, AimZ;
        /// <summary>0 = assist off, 1 = full strength (the setting in effect for the device in use).</summary>
        public static float Strength;

        public static void Clear()
        {
            LockTarget = -1;
            HittableTarget = -1;
            HasAim = false;
            Strength = 0;
        }
    }
}

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
        /// <summary>0 = assist off, 1 = full strength (the setting in effect for the device in use): Low 0.5, High 1.</summary>
        public static float Strength;
        /// <summary>Lane I: the hurt ally the Healer's press is bent toward this frame (-1 none; LockTarget stays enemy-only).</summary>
        public static int AllyTarget = -1;
        /// <summary>Lane I: the aim before the assist (the mouse point, the stick/keys point, or the camera point), so a
        /// camera that must not follow the assist (lane C) can read the player's own aim.</summary>
        public static bool RawHasAim;
        public static double RawAimX, RawAimZ;
        /// <summary>Lane I: the device class whose AIM HELP setting is in effect.</summary>
        public static AimDevice Device;

        public static void Clear()
        {
            LockTarget = -1;
            HittableTarget = -1;
            HasAim = false;
            Strength = 0;
            AllyTarget = -1;
            RawHasAim = false;
        }
    }
}

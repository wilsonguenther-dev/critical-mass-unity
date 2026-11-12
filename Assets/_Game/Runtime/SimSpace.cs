using System;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime
{
    /// <summary>
    /// The only place in the project that converts between Sim axes and Unity axes (O-13 ruling). Sim (x, z) is the
    /// browser's right-handed ground plane; Unity is left-handed, so sim z maps to Unity -z. No other file negates a z.
    /// </summary>
    public static class SimSpace
    {
        /// <summary>Sim point (x, z) at the given height to a Unity world position.</summary>
        public static Vector3 ToWorld(double simX, double height, double simZ) => new Vector3((float)simX, (float)height, (float)-simZ);

        /// <summary>Unity world position to the sim ground point (height dropped).</summary>
        public static void ToSim(Vector3 world, out double simX, out double simZ)
        {
            simX = world.x;
            simZ = -(double)world.z;
        }

        /// <summary>
        /// Unity yaw in degrees for a sim facing (radians). Sim facing f points along sim (cos f, sin f), which is Unity
        /// (cos f, 0, -sin f); Quaternion.Euler(0, 90 + f_deg, 0) * Vector3.forward is exactly that. Sim facing 0 = +X.
        /// </summary>
        public static float YawDeg(double facing) => (float)(90.0 + facing * 180.0 / Math.PI);

        /// <summary>The fixed isometric camera rotation: Euler(pitch, 360 - yaw, 0) = (38, 315, 0) by default.</summary>
        public static Quaternion CameraRotation(GameConfig c) => Quaternion.Euler((float)c.Render.CameraPitchDeg, (float)(360 - c.Render.CameraYawDeg), 0f);
    }
}

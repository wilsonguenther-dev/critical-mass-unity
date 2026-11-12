using System;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// The gameplay camera's numbers (U-005 amendment 2026-11-12, owner direction; art direction 2026-11-12 section 2.1).
    /// Perspective, pitch 55 degrees, vertical field of view 35 degrees, 23.4 u from the look target along the view ray,
    /// yaw 45 (RenderConfig.CameraYawDeg, unchanged), never rotates, no shake.
    /// These live in the view because the Sim assembly is frozen: RenderConfig.CameraPitchDeg (38), CameraDistance (60)
    /// and ViewHeightUnits (20) stay in GameConfig (its golden test pins them) but the gameplay camera no longer reads them.
    /// </summary>
    public static class GameplayCamera
    {
        /// <summary>Pitch, degrees down from horizontal.</summary>
        public const double PitchDeg = 55;
        /// <summary>Vertical field of view, degrees.</summary>
        public const double FovDeg = 35;
        /// <summary>Camera to look target along the view ray (u): 19.2 u above the target.</summary>
        public const double Distance = 23.4;
        /// <summary>The look target sits this far toward screen-up from the player, so the player is at about 58% of the screen height.</summary>
        public const double ScreenUpOffset = 2.0;
        /// <summary>Aim look-ahead cap (u), toward the cursor (was 2 u of velocity look-ahead).</summary>
        public const double LookAheadMax = 3.0;
        /// <summary>
        /// Aim look-ahead = (aim point - player) x this, before the cap. 0.25 puts the cap at a cursor about 12 u away,
        /// which is the top edge of the screen. Not in the owner's numbers: chosen here, flagged in the PR.
        /// </summary>
        public const double AimLookAheadGain = 0.25;
        public const float NearClip = 0.3f;
        public const float FarClip = 200f;

        /// <summary>The fixed camera rotation Euler(pitch, 360 - yaw, 0) = (55, 315, 0).</summary>
        public static Quaternion Rotation(GameConfig c) => Quaternion.Euler((float)PitchDeg, (float)(360 - c.Render.CameraYawDeg), 0f);

        /// <summary>Camera position for a Unity-space look target.</summary>
        public static Vector3 PositionFor(GameConfig c, Vector3 target) => target - Rotation(c) * Vector3.forward * (float)Distance;

        /// <summary>
        /// The world ray through a viewport point (0..1, 0..1) of the gameplay camera looking at target, for a screen of the
        /// given aspect (width / height). The same maths as Camera.ViewportPointToRay for a perspective camera.
        /// </summary>
        public static Ray ViewportRay(GameConfig c, Vector3 target, double vx, double vy, double aspect)
        {
            Quaternion r = Rotation(c);
            double t = Math.Tan(FovDeg * Math.PI / 360);
            var local = new Vector3((float)((vx * 2 - 1) * t * aspect), (float)((vy * 2 - 1) * t), 1f);
            return new Ray(PositionFor(c, target), (r * local).normalized);
        }
    }
}

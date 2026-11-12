using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>Builds the fixed-angle orthographic Cinemachine 3 follow rig (CameraRig.tsx follow; no rotation control).</summary>
    public static class CameraRig
    {
        /// <summary>Creates <c>CameraTarget</c> (with <see cref="CameraFollowTarget"/>) and <c>CM Camera</c>; returns <c>CM Camera</c>.</summary>
        public static GameObject Create(SimDriverBehaviour driver)
        {
            GameConfig cfg = driver.Config;

            var targetGo = new GameObject("CameraTarget");
            var followTarget = targetGo.AddComponent<CameraFollowTarget>();
            followTarget.Driver = driver;

            var camGo = new GameObject("CM Camera");
            camGo.transform.rotation = SimSpace.CameraRotation(cfg);

            var cam = camGo.AddComponent<CinemachineCamera>();
            cam.Follow = targetGo.transform;
            cam.Lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            cam.Lens.OrthographicSize = (float)(cfg.Render.ViewHeightUnits / 2);
            cam.Lens.NearClipPlane = 0.1f;
            cam.Lens.FarClipPlane = 200f;

            var follow = camGo.AddComponent<CinemachineFollow>();
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = Vector3.zero;
            follow.FollowOffset = -(SimSpace.CameraRotation(cfg) * Vector3.forward) * (float)cfg.Render.CameraDistance;

            return camGo;
        }
    }
}

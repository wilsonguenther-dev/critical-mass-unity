using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// Builds the fixed-angle perspective Cinemachine 3 follow rig (U-005 amendment 2026-11-12: pitch 55, vertical FOV 35,
    /// 23.4 u from the look target, yaw 45, no rotation control, no shake). The follow itself is CameraFollowTarget.
    /// DG-20 adds CameraDirector (combat framing, hatch focus, hold-to-look) through CameraFollowTarget, the SeeThrough
    /// silhouette and the one-time LookHint, all on the objects made here.
    /// </summary>
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
            Quaternion rotation = GameplayCamera.Rotation(cfg);
            camGo.transform.rotation = rotation;

            var cam = camGo.AddComponent<CinemachineCamera>();
            cam.Follow = targetGo.transform;
            cam.Lens.ModeOverride = LensSettings.OverrideModes.None; // the Unity camera is perspective (GameBootstrap)
            cam.Lens.FieldOfView = (float)GameplayCamera.FovDeg;
            cam.Lens.NearClipPlane = GameplayCamera.NearClip;
            cam.Lens.FarClipPlane = GameplayCamera.FarClip;
            cam.Lens.Dutch = 0f;

            var follow = camGo.AddComponent<CinemachineFollow>();
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = Vector3.zero;
            follow.FollowOffset = -(rotation * Vector3.forward) * (float)GameplayCamera.Distance;

            // DG-20: the director's zoom scales this offset; see-through silhouette and the one-time look hint.
            followTarget.Rig = follow;
            followTarget.BaseOffset = follow.FollowOffset;
            var seeThrough = camGo.AddComponent<SeeThrough>();
            seeThrough.Driver = driver;
            var hint = camGo.AddComponent<LookHint>();
            hint.Driver = driver;
            // feat/ground-camera: the low third-person view (spectating, knocked out, the experimental CAMERA: GROUND setting).
            var ground = camGo.AddComponent<GroundView>();
            ground.Driver = driver;

            return camGo;
        }
    }
}

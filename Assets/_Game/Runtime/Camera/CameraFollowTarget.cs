using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// The Cinemachine follow target: each LateUpdate feeds actor 0's interpolated position, its ground height and the
    /// pointer aim into <see cref="CameraFollow"/> (CameraRig.tsx follow plus the U-005 amendment of 2026-11-12) and
    /// places itself at the resulting look target. <see cref="SetOverride"/> frames a fixed point instead (capture tool).
    /// </summary>
    public sealed class CameraFollowTarget : MonoBehaviour
    {
        public SimDriverBehaviour Driver;

        private readonly CameraFollow _follow = new CameraFollow(HumanInputState.ScreenUpX, HumanInputState.ScreenUpZ);
        private HumanInputSource _input;
        private bool _override;
        private double _ox, _oz;

        /// <summary>The follow state (tests and the debug HUD read it).</summary>
        public CameraFollow Follow => _follow;

        /// <summary>Frame the sim point (x, z) instead of the human until <see cref="ClearOverride"/> (snaps on).</summary>
        public void SetOverride(double simX, double simZ)
        {
            _override = true;
            _follow.SnapToNextOverride();
            _ox = simX;
            _oz = simZ;
        }

        /// <summary>Back to following the human; the spring flies home.</summary>
        public void ClearOverride() => _override = false;

        private void LateUpdate()
        {
            if (Driver == null) return;
            World w = Driver.World;
            if (w == null || w.ActorCount <= 0) return;

            double a = Driver.Alpha;
            MapDef map = w.Map;
            BoundsDef b = map.Bounds;
            double inset = Driver.Config.Render.CameraBoundsInset;
            if (_input == null) _input = Driver.GetComponent<HumanInputSource>();

            var f = new CameraFollowInput
            {
                Dt = Driver.FrameDt,
                HumanX = w.PrevX[0] + (w.X[0] - w.PrevX[0]) * a,
                HumanZ = w.PrevZ[0] + (w.Z[0] - w.PrevZ[0]) * a,
                MinX = b.MinX + inset,
                MaxX = b.MaxX - inset,
                MinZ = b.MinZ + inset,
                MaxZ = b.MaxZ - inset,
                Epoch = w.Epoch,
                Override = _override,
            };
            f.HumanY = GroundHeight.At(map, f.HumanX, f.HumanZ);
            if (_input != null && !Driver.HumanAutopilot && _input.State.HasAim)
            {
                f.HasAim = true;
                f.AimX = _input.State.AimX;
                f.AimZ = _input.State.AimZ;
            }
            if (_override)
            {
                f.OverrideX = _ox;
                f.OverrideZ = _oz;
                f.OverrideY = GroundHeight.At(map, _ox, _oz);
            }
            _follow.Step(f);

            transform.position = SimSpace.ToWorld(_follow.SX.X, _follow.SY.X, _follow.SZ.X);
        }
    }
}

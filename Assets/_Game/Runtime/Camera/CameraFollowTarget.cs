using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// DG-20 (2026-11-12): the follow also takes CameraDirector's goal offset (hatch focus, hold-to-look) and its zoom,
    /// which scales the rig's CinemachineFollow offset (the angle never changes). Hold-to-look: the middle mouse button
    /// held, or the right stick click (LookLatch), pans toward the pointer or the right stick, up to 12 u. Camera changes
    /// never touch the sim's aim: HumanInputSource still casts the pointer through Camera.main onto the ground plane, so
    /// the sim receives the ground point under the cursor whatever the camera does.
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

        /// <summary>The rig's follow (CameraRig.Create sets it) and its U-005 offset; the director's zoom scales it.</summary>
        public CinemachineFollow Rig;
        public Vector3 BaseOffset;

        /// <summary>Raised the first frame hold-to-look is used in a session (LookHint retires its hint on it).</summary>
        public static event System.Action LookUsed;

        /// <summary>Right stick dead zone for hold-to-look (the Aim action's own processors are not read here).</summary>
        public const float LookStickDeadZone = 0.2f;

        private readonly CameraDirector _director = new CameraDirector();
        private readonly LookLatch _padLook = new LookLatch();
        private int _hatchEpoch = int.MinValue;
        private bool _hatchSeen;
        private double _hatchTime, _hatchX, _hatchZ;
        private bool _lookUsedSent;

        /// <summary>The DG-20 rules' state (tests and the debug HUD read it).</summary>
        public CameraDirector Director => _director;

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

        private void LateUpdate() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.Camera, cost); }

        void LateUpdateCost()
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

            // DG-20: combat framing, hatch focus and hold-to-look.
            var d = new CameraDirectorInput
            {
                Dt = Driver.FrameDt,
                SimTime = w.Match.Time + a / Driver.Config.SimHz,
                HumanX = f.HumanX,
                HumanZ = f.HumanZ,
                EnemiesNear = EnemiesNear(w, f.HumanX, f.HumanZ),
                Override = _override,
            };
            if (w.Epoch != _hatchEpoch)
            {
                _hatchEpoch = w.Epoch;
                _hatchSeen = false;
                _director.Reset();
                _padLook.Reset();
            }
            if (w.Match.Hatched && w.Kaiju.State != KaijuState.Egg && !_hatchSeen)
            {
                _hatchSeen = true;
                _hatchTime = d.SimTime;
                _hatchX = w.Kaiju.X;
                _hatchZ = w.Kaiju.Z;
            }
            d.Hatched = _hatchSeen;
            d.HatchSimTime = _hatchTime;
            d.HatchX = _hatchX;
            d.HatchZ = _hatchZ;
            ReadLook(ref d);
            _director.Step(d);
            if (!_override)
            {
                f.ExtraX = _director.OffsetX;
                f.ExtraZ = _director.OffsetZ;
                f.SuppressAim = _director.Looking;
            }
            _follow.Step(f);

            transform.position = SimSpace.ToWorld(_follow.SX.X, _follow.SY.X, _follow.SZ.X);
            // M11: reduce motion keeps the distance fixed (no combat or hatch zoom pumping).
            float zoom = CriticalMass.Runtime.View.Effects.FeelGate.Allowed(CriticalMass.Runtime.View.Effects.FeelEffect.CombatZoom) ? (float)_director.Zoom : 1f;
            if (Rig != null) Rig.FollowOffset = BaseOffset * zoom;
        }

        /// <summary>Living actors of the other team within CameraDirector.CombatRadius of the human (actor 0).</summary>
        public static int EnemiesNear(World w, double hx, double hz)
        {
            if (w == null || w.ActorCount <= 0) return 0;
            TeamId team = w.Actors[0].Team;
            double r2 = CameraDirector.CombatRadius * CameraDirector.CombatRadius;
            int n = 0;
            for (int i = 1; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.Team == team) continue;
                double dx = w.X[i] - hx, dz = w.Z[i] - hz;
                if (dx * dx + dz * dz <= r2) n++;
            }
            return n;
        }

        /// <summary>Hold-to-look from the devices (view only; the Player map is not touched, nothing reaches the sim).</summary>
        private void ReadLook(ref CameraDirectorInput d)
        {
            bool blocked = _override || Driver.HumanAutopilot || SimDriverBehaviour.CaptureMode || (_input != null && _input.State.Paused);
            double now = Time.unscaledTimeAsDouble;
            Gamepad pad = Gamepad.current;
            _padLook.Update(!blocked && pad != null && pad.rightStickButton.isPressed, now);
            if (blocked) return;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.middleButton.isPressed && Screen.width > 0 && Screen.height > 0)
            {
                Vector2 p = mouse.position.ReadValue();
                d.Looking = true;
                d.LookRight = (p.x - Screen.width * 0.5) / (Screen.width * 0.5);
                d.LookUp = (p.y - Screen.height * 0.5) / (Screen.height * 0.5);
            }
            else if (pad != null && _padLook.Active)
            {
                Vector2 s = pad.rightStick.ReadValue();
                float m = s.magnitude;
                d.Looking = true;
                if (m > LookStickDeadZone)
                {
                    float k = Mathf.Clamp01((m - LookStickDeadZone) / (1f - LookStickDeadZone)) / m;
                    d.LookRight = s.x * k;
                    d.LookUp = s.y * k;
                }
            }
            if (d.Looking && !_lookUsedSent)
            {
                _lookUsedSent = true;
                LookUsed?.Invoke();
            }
        }
    }
}

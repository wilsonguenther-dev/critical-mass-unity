using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>What the ground view is doing this frame.</summary>
    public enum GroundViewMode { Off, Watch, Play }

    /// <summary>
    /// Ground view (feat/ground-camera, 2026-11-13). A low third-person camera, 3 u behind and about 1.65 u up, over the
    /// followed body's shoulder, with the horizon on screen.
    /// <list type="bullet">
    /// <item>Watch (spectator ground view): while spectating, while knocked out and waiting to respawn, and in automation
    /// runs (-groundView, -groundShots). Follows SpectatorMode's followed fighter (its Q / E and bumpers cycle him, its
    /// objectives pick the Kaiju), else the killer, else Q / E and the bumpers cycle here. Right mouse drag or the right
    /// stick orbits; the yaw swings back behind the fighter 2.5 s later. F6 toggles the auto-director (GroundDirector:
    /// duels, crowds, the hill, carriers, the Kaiju; at least 4 s a shot), on by default when nobody picks.</item>
    /// <item>Play (experimental, Settings, Game, CAMERA: GROUND): the local player plays from the same camera. The mouse
    /// (cursor locked) or the right stick turns it; the move keys and the left stick are camera-relative and become the
    /// same world-space move the sim already takes (HumanInputState.SetCameraBasis); the aim is the point ahead of the
    /// fighter along the camera's facing at the reticle's ground distance (GroundCamera.AimPoint). The sim receives
    /// exactly the InputFrame fields it always did: no Sim change, determinism untouched.</item>
    /// </list>
    /// The rebindable "Camera view" action (F5, double-tap View on a controller) toggles: watching, between the high
    /// spectator view and ground view; playing, the CAMERA setting itself. While on, CinemachineBrain is disabled and this
    /// writes Camera.main directly (a hard cut both ways, nothing blends through walls); the brain takes the gameplay rig
    /// back on exit with its lens. GroundCollision keeps the camera out of walls, towers, trees and cliffs. The backdrop
    /// (GroundBackdrop) gives the low camera a sky and far islands; fighter LODs are picked around the followed body
    /// (LodActive / LodAnchor); the minimap and Kaiju panels hide while watching (minimal spectator layout); camera shake
    /// is limited to 30% (CameraShake.Limit). Never writes the sim.
    /// </summary>
    [DefaultExecutionOrder(-200)]   // after every Update (the driver's Alpha, SpectatorMode's rig), before CameraShake's LateUpdate
    public sealed class GroundView : MonoBehaviour
    {
        public const string ActionName = "CameraView";
        /// <summary>Camera shake share kept in ground view (a 0.32 u shake at 3 u from the pivot is a lot).</summary>
        public const float ShakeLimit = 0.3f;
        /// <summary>Mouse look degrees per pixel and right stick degrees per second (yaw, pitch).</summary>
        public const float MouseDegPerPx = 0.14f, StickYawDegPerSec = 160f, StickPitchDegPerSec = 90f;
        /// <summary>Off-screen threat ring: enemies within this many u outside the view get a pointer.</summary>
        public const double ThreatRange = 14;
        const int MaxCylinders = 64, MaxStatic = 1024;

        public static GroundView Current { get; private set; }
        /// <summary>True while the local player plays from the ground camera (HumanInputSource reads it).</summary>
        public static bool PlayActive => Current != null && Current.Mode == GroundViewMode.Play;
        /// <summary>True while ground view drives the camera.</summary>
        public static bool IsActive => Current != null && Current.Mode != GroundViewMode.Off;
        /// <summary>The yaw the keys and the left stick move relative to (the follow views hold it while a move is held;
        /// InputLatch), and the camera's own yaw otherwise.</summary>
        public static double PlayYaw => Current != null ? Current._inputYaw : 315;
        /// <summary>LOD contract C4 from this camera: fighters pick LOD0 by distance to the followed body, which counts as "self".</summary>
        public static bool LodActive => IsActive && Current._lodValid;
        public static Vector3 LodAnchor => Current != null ? Current._lodAnchor : Vector3.zero;
        public static int LodSelf => Current != null ? Current._lodSelf : 0;
        /// <summary>feat/follow-camera-1: the view the local player plays in now (TopDown when the high camera drives).</summary>
        public static CameraView ActiveView => PlayActive ? Current._view : CameraView.TopDown;
        /// <summary>First person: the local player's own body is not drawn (ActorViews reads it).</summary>
        public static bool HideSelf => PlayActive && Current._view == CameraView.FirstPerson;
        /// <summary>Capture runs only (FollowCameraShots): the view to use instead of the setting, and play from it even
        /// while the autopilot drives actor 0.</summary>
        public static CameraView? ViewOverride;
        public static bool ForcePlay;

        public SimDriverBehaviour Driver;
        public GroundViewMode Mode { get; private set; }
        /// <summary>The followed actor id, or GroundDirector.KaijuTarget.</summary>
        public int Target { get; private set; } = -1;
        public bool DirectorOn { get; private set; } = true;
        /// <summary>The player asked for ground view while watching (F5); automation sets it too.</summary>
        public bool WatchGround { get; set; }
        public GroundFollow Follow => _follow;
        public GroundDirector Director => _director;
        /// <summary>Frames ground view has been on since it last turned on (tests).</summary>
        public int ActiveFrames { get; private set; }

        readonly GroundFollow _follow = new GroundFollow();
        readonly GroundDirector _director = new GroundDirector();
        readonly SeeThroughCylinder[] _cyl = new SeeThroughCylinder[MaxCylinders];
        readonly SeeThroughCylinder[] _static = new SeeThroughCylinder[MaxStatic];
        int _staticCount, _staticVersion = -1;
        int _cylCount;
        GroundViewMode _lastMode;
        int _lastTarget = int.MinValue;
        int _lastSpectated = -1;
        CinemachineBrain _brain;
        UnityEngine.Camera _cam;
        GroundBackdrop _backdrop;
        InputAction _toggle;
        bool _toggleLooked;
        HumanInputSource _input;
        WorldView _world;
        bool _lodValid;
        Vector3 _lodAnchor;
        int _lodSelf;
        double _aimDistance = GroundCamera.AimDistance;
        readonly YawFollow _yawFollow = new YawFollow();
        readonly InputLatch _latch = new InputLatch();
        readonly StrafeGate _strafe = new StrafeGate();
        Vector3 _chest;
        bool _fadeSet;
        static readonly int FadeCamId = Shader.PropertyToID("_CMFadeCam"), FadeTargetId = Shader.PropertyToID("_CMFadeTarget");
        double _inputYaw = 315;
        float _sens = 1f;
        bool _invertY;
        CameraView _view = CameraView.ThirdPerson, _lineView = (CameraView)(-1);
        bool _lineManual;
        string _playLine = "";
        double _sinceLook = 999;
        float _fov = FollowCamera.FovDefault;
        bool _manual;
        static readonly int[] HudPanels = { (int)HudCluster.Minimap, (int)HudCluster.Kaiju };
        readonly Vector3[] _corners = new Vector3[4];
        readonly bool[] _hidClusters = new bool[4];
        bool _cursorLocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; ViewOverride = null; ForcePlay = false; }

        void Awake() => Current = this;

        void Start()
        {
            // Automation runs: watch from the ground with the director (-groundView), or the scripted capture tour.
            if (AutoplayArgs.HasAutomationFlag("-groundview") || AutoplayArgs.HasAutomationFlag("-groundshots")) WatchGround = true;
            if (AutoplayArgs.HasAutomationFlag("-groundshots") && GetComponent<GroundViewShots>() == null)
                gameObject.AddComponent<GroundViewShots>().View = this;
            if (AutoplayArgs.HasAutomationFlag(FollowCameraShots.Flag) && GetComponent<FollowCameraShots>() == null)
                gameObject.AddComponent<FollowCameraShots>().View = this;
            if (GetComponent<CriticalMass.Runtime.View.LowViewTags>() == null) gameObject.AddComponent<CriticalMass.Runtime.View.LowViewTags>();
        }

        void OnDestroy()
        {
            if (Mode != GroundViewMode.Off) Leave();
            if (Current == this) Current = null;
        }

        // ---------------------------------------------------------------- public API (keys, tests, the capture tour)

        /// <summary>The toggle: watching flips ground / high; playing flips the CAMERA setting.</summary>
        public void Toggle()
        {
            if (WatchAllowed() && !PlayAllowedNow())
            {
                WatchGround = !WatchGround;
                Toast(WatchGround ? "GROUND VIEW" : "HIGH VIEW");
            }
            else
            {
                // feat/follow-camera-1: playing, F5 cycles TOP-DOWN, THIRD PERSON, FIRST PERSON (Settings, Game, CAMERA too).
                CameraView next = FollowCamera.Next(ChosenView());
                if (ViewOverride.HasValue) ViewOverride = next; else GameSettings.CameraView = next;
                Toast(next == CameraView.ThirdPerson ? "VIEW: THIRD PERSON" : next == CameraView.FirstPerson ? "VIEW: FIRST PERSON" : "VIEW: TOP-DOWN");
            }
        }

        /// <summary>Follow the next (dir +1) or previous living fighter; the director stands down.</summary>
        public void Cycle(int dir)
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null || w.ActorCount <= 0) return;
            DirectorOn = false;
            int c = GroundCamera.NextAlive(w, Target, dir);
            if (c >= 0) SetTarget(c);
        }

        /// <summary>Follow this actor (or GroundDirector.KaijuTarget); the director stands down.</summary>
        public void Pick(int id)
        {
            DirectorOn = false;
            SetTarget(id);
        }

        public void SetDirector(bool on)
        {
            DirectorOn = on;
            if (on) _director.Reset();
        }

        void SetTarget(int id)
        {
            Target = id;
            _director.Force(id);
            SpectatorMode sm = SpectatorMode.Current;
            World w = Driver != null ? Driver.World : null;
            if (sm != null && sm.Active && id >= 0 && w != null) sm.Rig.Follow(w, id);
            if (sm != null && sm.Active) _lastSpectated = sm.Rig.Followed;
        }

        /// <summary>Points the camera this way now (capture tour; the auto yaw then holds it for OrbitHoldSeconds).</summary>
        public void Aim(double unityYaw, double pitch)
        {
            _follow.SetYaw(unityYaw);
            _follow.SetPitch(pitch);
            _follow.HoldAuto();
        }

        // ---------------------------------------------------------------- rules

        bool InMatch()
        {
            if (Driver == null || Driver.World == null || Driver.World.ActorCount <= 0) return false;
            if (Driver.Request != null && Driver.Request.Backdrop) return false;
            var st = CriticalMass.Runtime.Flow.GameFlow.State;
            return st == CriticalMass.Runtime.Flow.FlowState.Match || st == CriticalMass.Runtime.Flow.FlowState.Off;
        }

        /// <summary>Watching is possible: spectating, knocked out (waiting to respawn), or an automation run.</summary>
        public bool WatchAllowed()
        {
            if (!InMatch()) return false;
            World w = Driver.World;
            if (w.Match.Phase == MatchPhase.Ended) return false;
            return SpectatorMode.IsSpectating || !w.Actors[0].Alive || Driver.HumanAutopilot;
        }

        static CameraView ChosenView() => ViewOverride ?? GameSettings.CameraView;

        bool PlayAllowedNow()
        {
            if (!InMatch() || ChosenView() == CameraView.TopDown) return false;
            World w = Driver.World;
            return w.Match.Phase != MatchPhase.Ended && !SpectatorMode.IsSpectating && (!Driver.HumanAutopilot || ForcePlay) && w.Actors[0].Alive;
        }

        GroundViewMode WantMode()
        {
            if (PlayAllowedNow()) return GroundViewMode.Play;
            // Knocked out with the ground camera chosen: the death cam is ground view too.
            if (WatchAllowed() && (WatchGround || (GameSettings.GroundCamera && !SpectatorMode.IsSpectating))) return GroundViewMode.Watch;
            return GroundViewMode.Off;
        }

        // ---------------------------------------------------------------- frame

        void LateUpdate()
        {
            var cost = FrameCost.Enter();
            try { Frame(); }
            finally { FrameCost.Exit(FrameCost.Slot.Camera, cost); }
        }

        void Frame()
        {
            ReadToggle();
            GroundViewMode want = WantMode();
            if (want != Mode)
            {
                if (Mode == GroundViewMode.Off) Enter();
                Mode = want;
                if (want == GroundViewMode.Off) Leave();
            }
            if (Mode == GroundViewMode.Off)
            {
                _lodValid = false;
                return;
            }
            ActiveFrames++;
            World w = Driver.World;
            MapDef map = w.Map;
            double dt = Driver.FrameDt;
            bool paused = _input != null && _input.State.Paused;

            // Who to follow.
            bool snap = _lastMode != Mode;
            int target = Mode == GroundViewMode.Play ? 0 : WatchTarget(w, paused ? 0 : dt);
            if (target != _lastTarget) snap = true;
            _lastTarget = target;
            _lastMode = Mode;
            Target = target;
            if (!GroundDirector.Valid(w, target)) target = 0;

            double a = Driver.Alpha;
            double tx, tz, facing, lift = 0, extra = 0;
            if (target == GroundDirector.KaijuTarget)
            {
                Kaiju k = w.Kaiju;
                tx = k.PrevX + (k.X - k.PrevX) * a;
                tz = k.PrevZ + (k.Z - k.PrevZ) * a;
                facing = k.Facing;
                lift = 2.2;
                extra = 4.5;
            }
            else
            {
                tx = w.PrevX[target] + (w.X[target] - w.PrevX[target]) * a;
                tz = w.PrevZ[target] + (w.Z[target] - w.PrevZ[target]) * a;
                facing = w.Actors[target].Facing;
            }
            double gy = GroundHeight.At(map, tx, tz);

            // Orbit / look input.
            ReadLook(paused, dt, out double orbitYaw, out double orbitPitch);
            GroundRig rig = default;
            _chest = SimSpace.ToWorld(tx, gy + 1.2 + lift, tz);
            if (Mode == GroundViewMode.Play)
            {
                CameraView v = ChosenView();
                if (v != _view) { _view = v; snap = true; }
                rig = _view == CameraView.FirstPerson ? FollowCamera.FirstRig : FollowCamera.ThirdRig;
                PlayFollow(w, target, facing, paused ? 0 : dt, snap, orbitYaw, orbitPitch, rig, ref tx, ref tz);
                orbitYaw = orbitPitch = 0;   // PlayFollow owns the yaw and pitch
            }

            GatherCylinders(w, tx, tz, target);
            var f = new GroundFollowInput
            {
                Dt = paused ? 0 : dt,
                TargetX = tx,
                TargetZ = tz,
                GroundY = gy,
                PivotLift = lift,
                ExtraDistance = extra,
                WantYaw = GroundCamera.YawOfFacing(facing),
                AutoYaw = Mode == GroundViewMode.Watch,
                Rig = rig,
                OrbitYaw = orbitYaw,
                OrbitPitch = orbitPitch,
                Snap = snap,
                Map = map,
                Cylinders = _cyl,
                CylinderCount = _cylCount,
            };
            _follow.Step(f);
            if (Mode != GroundViewMode.Play) _inputYaw = _follow.Yaw;
            ApplyCamera();

            _lodValid = true;
            _lodAnchor = SimSpace.ToWorld(tx, 0, tz);
            _lodSelf = target >= 0 ? target : -1;
            if (Mode == GroundViewMode.Play) UpdateAimDistance(w);
            UpdateCursor(paused);
        }

        /// <summary>
        /// feat/follow-camera-1: the third / first person yaw and pitch. Auto (default): the yaw follows the character's
        /// travel direction (its facing when standing) on YawFollow's critically damped spring after a short delay, and the
        /// pitch eases to the view's own. Manual (Settings, Video, CAMERA FOLLOW): the mouse or right stick orbits; after
        /// RecenterSeconds without look input, or a middle click, it re-centres behind. Third person also looks a little
        /// ahead along the travel (none under reduce motion).
        /// </summary>
        void PlayFollow(World w, int id, double facing, double dt, bool snap, double orbitYaw, double orbitPitch, GroundRig rig, ref double tx, ref double tz)
        {
            double hz = w.Config.SimHz > 0 ? w.Config.SimHz : 30;
            double vx = (w.X[id] - w.PrevX[id]) * hz, vz = (w.Z[id] - w.PrevZ[id]) * hz;
            bool moving = vx * vx + vz * vz > FollowCamera.MovingSpeed * FollowCamera.MovingSpeed;
            double behind = moving ? GroundCamera.YawToward(0, 0, vx, vz, GroundCamera.YawOfFacing(facing)) : GroundCamera.YawOfFacing(facing);
            bool reduce = CriticalMass.Runtime.View.Effects.FeelGate.ReduceMotion;
            double defPitch = rig.FirstPerson ? FollowCamera.FirstPitch : FollowCamera.ThirdPitch;
            if (snap)
            {
                _yawFollow.Reset(GroundCamera.YawOfFacing(facing));
                _follow.SetPitch(defPitch, rig.MinPitch, rig.MaxPitch);
                _sinceLook = 999;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.middleButton.wasPressedThisFrame) { _yawFollow.Recenter(behind); _sinceLook = 999; }
            bool looked = _manual && (Math.Abs(orbitYaw) > 1e-6 || Math.Abs(orbitPitch) > 1e-6);
            if (looked)
            {
                _sinceLook = 0;
                _yawFollow.Reset(_follow.Yaw + orbitYaw);
                _follow.SetPitch(_follow.Pitch + orbitPitch, rig.MinPitch, rig.MaxPitch);
            }
            else
            {
                _sinceLook += dt;
                if (!_manual || _sinceLook >= FollowCamera.RecenterSeconds)
                {
                    // follow-camera-2: a sideways run with no forward input holds the yaw; forward input coming back
                    // re-centres behind at once.
                    bool hold = false, recenter = false;
                    if (_input != null && !_manual) _strafe.Step(_input.State.MoveRight, _input.State.MoveUp, out hold, out recenter);
                    if (recenter) _yawFollow.Recenter(behind);
                    _yawFollow.Step(dt, hold ? _yawFollow.Target : behind, reduce);
                    if (dt > 0) _follow.SetPitch(_follow.Pitch + (defPitch - _follow.Pitch) * (1 - Math.Exp(-dt / 0.4)), rig.MinPitch, rig.MaxPitch);
                }
            }
            _follow.SetYaw(_yawFollow.Yaw);
            bool held = _input != null && _input.State.MoveHeld;
            _inputYaw = snap ? _follow.Yaw : _latch.Step(held, looked, _follow.Yaw);
            if (!rig.FirstPerson)
            {
                FollowCamera.Lead(vx, vz, reduce, out double lx, out double lz);
                tx += lx;
                tz += lz;
            }
        }

        int WatchTarget(World w, double dt)
        {
            SpectatorMode sm = SpectatorMode.Current;
            bool spectating = sm != null && sm.Active;
            if (spectating)
            {
                // The spectator's own picks (Q / E, bumpers, click, objectives) win and stop the director.
                if (sm.Rig.Focus == SpectatorFocus.Objective && sm.Rig.Objective == SpectatorObjective.Kaiju && GroundDirector.Valid(w, GroundDirector.KaijuTarget))
                {
                    if (Target != GroundDirector.KaijuTarget) { DirectorOn = false; _director.Force(GroundDirector.KaijuTarget); }
                    return GroundDirector.KaijuTarget;
                }
                if (sm.Rig.Focus == SpectatorFocus.Player && sm.Rig.Followed != _lastSpectated)
                {
                    if (_lastSpectated >= 0) DirectorOn = false;
                    _lastSpectated = sm.Rig.Followed;
                    if (!DirectorOn) _director.Force(sm.Rig.Followed);
                }
            }
            else
            {
                Keyboard kb = Keyboard.current;
                Gamepad pad = Gamepad.current;
                if (kb != null && kb.qKey.wasPressedThisFrame) Cycle(-1);
                if (kb != null && kb.eKey.wasPressedThisFrame) Cycle(1);
                if (pad != null && pad.leftShoulder.wasPressedThisFrame) Cycle(-1);
                if (pad != null && pad.rightShoulder.wasPressedThisFrame) Cycle(1);
            }
            Keyboard k2 = Keyboard.current;
            if (k2 != null && k2.f6Key.wasPressedThisFrame)
            {
                SetDirector(!DirectorOn);
                Toast(DirectorOn ? "AUTO-DIRECTOR ON" : "AUTO-DIRECTOR OFF");
            }
            if (DirectorOn)
            {
                if (_director.Step(w, dt) || (spectating && _director.Current >= 0 && sm.Rig.Followed != _director.Current))
                {
                    if (spectating && _director.Current >= 0)
                    {
                        sm.Rig.Follow(w, _director.Current);
                        _lastSpectated = _director.Current;
                    }
                }
                if (GroundDirector.Valid(w, _director.Current)) return _director.Current;
            }
            if (spectating && sm.Rig.Focus == SpectatorFocus.Player && GroundDirector.Valid(w, sm.Rig.Followed)) return sm.Rig.Followed;
            if (GroundDirector.Valid(w, Target)) return Target;
            // Nobody picked yet, or the followed body went down: the killer, then a team-mate, then anyone standing.
            int pick = -1;
            int killer = w.Actors[0].LastAttacker;
            if (!w.Actors[0].Alive && killer >= 0 && killer < w.ActorCount && w.Actors[killer].Alive) pick = killer;
            for (int i = 0; i < w.ActorCount && pick < 0; i++)
                if (w.Actors[i].Alive && w.Actors[i].Team == w.Actors[0].Team) pick = i;
            for (int i = 0; i < w.ActorCount && pick < 0; i++)
                if (w.Actors[i].Alive) pick = i;
            if (pick >= 0) _director.Force(pick);
            return pick >= 0 ? pick : 0;
        }

        void ReadToggle()
        {
            if (!_toggleLooked || (_toggle == null && Time.frameCount % 120 == 0))
            {
                _toggleLooked = true;
                InputActionMap map = ControlBindings.Map(InputSystem.actions);
                _toggle = map != null ? map.FindAction(ActionName, false) : null;
            }
            if (!InMatch()) return;
            bool pressed = _toggle != null ? _toggle.WasPerformedThisFrame() : Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame;
            if (pressed) Toggle();
        }

        void ReadLook(bool paused, double dt, out double yaw, out double pitch)
        {
            yaw = pitch = 0;
            if (paused || SimDriverBehaviour.CaptureMode) return;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;
            bool play = Mode == GroundViewMode.Play;
            // feat/follow-camera-1: playing, the look input only orbits in manual mode (auto follow owns the yaw otherwise).
            if (Time.frameCount % 30 == 0 || ActiveFrames <= 1)
            {
                _manual = GameSettings.CameraManual;
                _fov = GameSettings.CameraFov;
                _sens = GameSettings.CameraSensitivity;
                _invertY = GameSettings.CameraInvertY;
            }
            if (play && !_manual) return;
            float sens = play ? _sens : 1f;
            if (mouse != null && (play ? _cursorLocked : mouse.rightButton.isPressed))
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * MouseDegPerPx * sens;
                pitch -= d.y * MouseDegPerPx * sens * ((play ? _invertY : ControllerSettings.InvertAimY) ? -1f : 1f);
            }
            if (pad != null && play)
            {
                Vector2 s0 = pad.rightStick.ReadValue();
                float m0 = s0.magnitude;
                if (m0 > 0.2f)
                {
                    float k0 = Mathf.Clamp01((m0 - 0.2f) / 0.8f) / m0 * sens;
                    yaw += s0.x * k0 * StickYawDegPerSec * dt;
                    pitch -= s0.y * k0 * StickPitchDegPerSec * dt * (_invertY ? -1f : 1f);
                }
                return;
            }
            if (pad != null)
            {
                Vector2 s = pad.rightStick.ReadValue();
                float m = s.magnitude;
                if (m > 0.2f)
                {
                    float k = Mathf.Clamp01((m - 0.2f) / 0.8f) / m;
                    yaw += s.x * k * StickYawDegPerSec * dt;
                    pitch -= s.y * k * StickPitchDegPerSec * dt * (ControllerSettings.InvertAimY ? -1f : 1f);
                }
            }
        }

        /// <summary>The reticle (screen centre) to the ground: its distance from the fighter is the aim distance.</summary>
        void UpdateAimDistance(World w)
        {
            if (_cam == null) return;
            Ray ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            double hx = w.X[0], hz = w.Z[0];
            if (GroundHeight.TryAimPoint(ray, w.Map, hx, hz, out double sx, out double sz))
            {
                double dx = sx - hx, dz = sz - hz;
                GroundCamera.Basis(_follow.Yaw, out double ux, out double uz, out _, out _);
                double along = dx * ux + dz * uz;   // ahead of the fighter along the camera facing
                _aimDistance = along > GroundCamera.MinAim && along < GroundCamera.MaxAim * 1.5 ? along : GroundCamera.AimDistance;
            }
            else _aimDistance = GroundCamera.AimDistance;
        }

        /// <summary>The aim point the sim gets this frame while playing (false when not playing).</summary>
        public static bool TryPlayAim(World w, out double ax, out double az)
        {
            ax = az = 0;
            GroundView g = Current;
            if (g == null || g.Mode != GroundViewMode.Play || w == null || w.ActorCount <= 0) return false;
            GroundCamera.AimPoint(w.X[0], w.Z[0], g._follow.Yaw, g._aimDistance, out ax, out az);
            return true;
        }

        void UpdateCursor(bool paused)
        {
            bool want = Mode == GroundViewMode.Play && !paused && Application.isFocused && !SimDriverBehaviour.CaptureMode;
            if (want == _cursorLocked) return;
            _cursorLocked = want;
            Cursor.lockState = want ? CursorLockMode.Locked : CursorLockMode.None;
            if (want) Cursor.visible = false;
        }

        // ---------------------------------------------------------------- camera

        void Enter()
        {
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = UnityEngine.Camera.main;
            if (_cam != null) _brain = _cam.GetComponent<CinemachineBrain>();
            if (_brain != null) _brain.enabled = false;
            if (_input == null && Driver != null) _input = Driver.GetComponent<HumanInputSource>();
            if (_backdrop == null)
            {
                var go = new GameObject("Ground Backdrop");
                _backdrop = go.AddComponent<GroundBackdrop>();
                _backdrop.Build();
            }
            _backdrop.gameObject.SetActive(true);
            CameraShake.Limit = ShakeLimit;
            ActiveFrames = 0;
            _lastMode = GroundViewMode.Off;
            _lastTarget = int.MinValue;
            SpectatorMode sm = SpectatorMode.Current;
            _lastSpectated = sm != null && sm.Active ? sm.Rig.Followed : -1;
            SetMinimalHud(true);
        }

        /// <summary>follow-camera-2: the toon shader's see-through line (CMToonFadeClip) from the lens to the followed body's
        /// chest; off in first person and when the low views are off. Two global vectors a frame, no allocation.</summary>
        void SetFade(Vector3 camPos)
        {
            bool on = Mode != GroundViewMode.Off && !(Mode == GroundViewMode.Play && _view == CameraView.FirstPerson);
            if (on)
            {
                Shader.SetGlobalVector(FadeCamId, new Vector4(camPos.x, camPos.y, camPos.z, 1f));
                Shader.SetGlobalVector(FadeTargetId, new Vector4(_chest.x, _chest.y, _chest.z, ViewCull.FadeRadius));
                _fadeSet = true;
            }
            else if (_fadeSet)
            {
                Shader.SetGlobalVector(FadeCamId, Vector4.zero);
                _fadeSet = false;
            }
        }

        void Leave()
        {
            if (_fadeSet) { Shader.SetGlobalVector(FadeCamId, Vector4.zero); _fadeSet = false; }
            Mode = GroundViewMode.Off;
            _lodValid = false;
            if (_brain != null) _brain.enabled = true;
            if (_cam != null)
            {
                // The brain writes the rig's lens again on its next update; restore now so this frame is right too.
                _cam.fieldOfView = (float)GameplayCamera.FovDeg;
                _cam.nearClipPlane = GameplayCamera.NearClip;
                _cam.farClipPlane = GameplayCamera.FarClip;
            }
            if (_backdrop != null) _backdrop.gameObject.SetActive(false);
            CameraShake.Limit = 1f;
            SetMinimalHud(false);
            if (_cursorLocked)
            {
                _cursorLocked = false;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        void ApplyCamera()
        {
            if (_cam == null || !_cam.isActiveAndEnabled)
            {
                _cam = UnityEngine.Camera.main;
                if (_cam == null) return;
                _brain = _cam.GetComponent<CinemachineBrain>();
                if (_brain != null) _brain.enabled = false;
            }
            Vector3 pos = SimSpace.ToWorld(_follow.CamX, _follow.CamY, _follow.CamZ);
            _cam.transform.SetPositionAndRotation(pos, Quaternion.Euler((float)_follow.Pitch, (float)_follow.Yaw, 0f));
            float fov = Mode == GroundViewMode.Play ? _fov : GroundCamera.FovDeg;
            if (_cam.fieldOfView != fov) _cam.fieldOfView = fov;
            if (_cam.nearClipPlane != GroundCamera.NearClip) _cam.nearClipPlane = GroundCamera.NearClip;
            if (_cam.farClipPlane != GroundCamera.FarClip) _cam.farClipPlane = GroundCamera.FarClip;
            if (_backdrop != null) _backdrop.Place(pos);
            SetFade(pos);
        }

        /// <summary>The HUD's minimap and Kaiju panels hide while watching from the ground (the clock and score stay).</summary>
        void SetMinimalHud(bool minimal)
        {
            GameHud hud = Driver != null ? Driver.GetComponent<GameHud>() : null;
            if (hud == null || hud.Clusters == null) return;
            foreach (int i in HudPanels)
            {
                RectTransform c = hud.Clusters[i];
                if (c == null) continue;
                if (minimal)
                {
                    // Only while watching: playing keeps the full HUD (the player needs his minimap).
                    _hidClusters[i] = false;
                    continue;
                }
                if (_hidClusters[i]) c.gameObject.SetActive(true);
                _hidClusters[i] = false;
            }
        }

        void Update()
        {
            // The minimal layout follows the mode (Watch hides two panels, Play shows them), checked once a frame.
            GameHud hud = Driver != null ? Driver.GetComponent<GameHud>() : null;
            if (hud == null || hud.Clusters == null) return;
            bool hide = Mode == GroundViewMode.Watch;
            foreach (int i in HudPanels)
            {
                RectTransform c = hud.Clusters[i];
                if (c == null) continue;
                if (hide && c.gameObject.activeSelf)
                {
                    c.gameObject.SetActive(false);
                    _hidClusters[i] = true;
                }
                else if (!hide && _hidClusters[i])
                {
                    c.gameObject.SetActive(true);
                    _hidClusters[i] = false;
                }
            }
        }

        // ---------------------------------------------------------------- collision volumes

        void GatherCylinders(World w, double tx, double tz, int target)
        {
            RefreshStatic();
            int n = 0;
            for (int i = 0; i < _staticCount && n < MaxCylinders - 1; i++)
            {
                double dx = _static[i].X - tx, dz = _static[i].Z - tz;
                double reach = GroundCamera.Distance + 6 + _static[i].R;
                if (dx * dx + dz * dz > reach * reach) continue;
                _cyl[n++] = _static[i];
            }
            Kaiju k = w.Kaiju;
            if (target != GroundDirector.KaijuTarget && w.Match.Hatched && k.State != KaijuState.Egg)
            {
                double a = Driver.Alpha;
                double kx = k.PrevX + (k.X - k.PrevX) * a, kz = k.PrevZ + (k.Z - k.PrevZ) * a;
                double ky = GroundHeight.At(w.Map, kx, kz);
                _cyl[n++] = new SeeThroughCylinder { X = kx, Z = kz, R = w.Config.Kaiju.Radius, Y0 = ky, Y1 = ky + w.Config.Render.KaijuHeight };
            }
            _cylCount = n;
        }

        /// <summary>Trees and landmarks (towers) from the dressed world as cylinders (rebuilt when the dressing changes).</summary>
        void RefreshStatic()
        {
            if (_world == null && Driver != null) _world = Driver.GetComponent<WorldView>();
            if (_world == null || _world.Plan == null) { _staticCount = 0; return; }
            if (_staticVersion == WorldView.DressVersion) return;
            _staticVersion = WorldView.DressVersion;
            _staticCount = 0;
            List<WorldPlacement> ps = _world.Plan.Placements;
            for (int i = 0; i < ps.Count && _staticCount < MaxStatic; i++)
            {
                WorldPlacement p = ps[i];
                double r, h;
                if (p.Cat == "tree")
                {
                    r = 0.6;
                    h = p.Piece != null && p.Piece.IndexOf("tall", StringComparison.Ordinal) >= 0 ? SeeThrough.TreeHeightTall : SeeThrough.TreeHeightRound;
                }
                else if (p.Cat == "landmark") { r = 1.6; h = 9.0; }
                else continue;
                _static[_staticCount++] = new SeeThroughCylinder { X = p.SimX, Z = p.SimZ, R = r, Y0 = p.Y - 1, Y1 = p.Y + h };
            }
        }

        /// <summary>The camera volumes near the target this frame (tests run the same Inside check).</summary>
        public int Cylinders(out SeeThroughCylinder[] cyl)
        {
            cyl = _cyl;
            return _cylCount;
        }

        // ---------------------------------------------------------------- overlay (reticle, threat ring, mode line)

        void Toast(string s)
        {
            // hud-containment-2: the notice goes to the toast lane (a cap and a queue; it waits behind an achievement toast).
            ToastLane.Post(s, Time.unscaledTime);
        }

        static Texture2D _tri, _dot;
        static GUIStyle _style;

        static Texture2D Tri()
        {
            if (_tri != null) return _tri;
            const int n = 32;
            _tri = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // A triangle pointing up (+y), 1 px soft edge.
                    float half = (y + 0.5f) / n * 0.5f * n;
                    float d = half - Mathf.Abs(x + 0.5f - n * 0.5f);
                    byte a = (byte)(Mathf.Clamp01(d) * 255);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            _tri.SetPixels32(px);
            _tri.Apply();
            return _tri;
        }

        static Texture2D Dot()
        {
            if (_dot != null) return _dot;
            const int n = 16;
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(n * 0.5f - d) * 255));
                }
            _dot.SetPixels32(px);
            _dot.Apply();
            return _dot;
        }

        static readonly string WatchLine = "GROUND VIEW   F5 HIGH VIEW   Q / E NEXT   RIGHT DRAG ORBIT   F6 DIRECTOR";

        /// <summary>The play hint, rebuilt only when the view or the follow mode changes (no per-frame strings).</summary>
        string PlayHint()
        {
            if (_lineView == _view && _lineManual == _manual) return _playLine;
            _lineView = _view;
            _lineManual = _manual;
            string next = FollowCamera.Label(FollowCamera.Next(_view));
            _playLine = FollowCamera.Label(_view) + (_manual ? "   MOUSE LOOK   MIDDLE CLICK RE-CENTRE" : "   AUTO FOLLOW")
                + "   F5 " + next;
            return _playLine;
        }

        void OnGUI()
        {
            if (Mode == GroundViewMode.Off || Event.current.type != EventType.Repaint) return;
            float s = Screen.height / 1080f;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            _style.fontSize = Mathf.Max(10, Mathf.RoundToInt(15 * s));
            Color old = GUI.color;
            // Mode line at the bottom centre, quiet (it never covers the fight).
            GUI.color = new Color(1f, 0.97f, 0.88f, 0.75f);
            string line = Mode == GroundViewMode.Play ? PlayHint() : WatchLine;
            if (GetComponent<GroundViewShots>() == null && GetComponent<FollowCameraShots>() == null) GUI.Label(new Rect(0, Screen.height - 40 * s, Screen.width, 28 * s), line, _style);
            string toast = ToastLane.Current(Time.unscaledTime);
            if (toast != null)
            {
                GUI.color = new Color(1f, 0.85f, 0.45f, 1f);
                _style.fontSize = Mathf.RoundToInt(26 * s);
                GUI.Label(ToastLane.PixelRect(Screen.width, Screen.height, 40 * s), toast, _style);
            }
            if (Mode == GroundViewMode.Play) { DrawAimTrail(s); DrawPlayAids(s); DrawViewCone(s); }
            GUI.color = old;
        }

        /// <summary>
        /// feat/follow-camera-1: the minimap stays fixed (the baked island picture, the markers and the click-to-ping maths
        /// all assume its heading), and a soft view cone at the player's marker shows where the low camera looks. Drawn
        /// with the reticle's dot texture (no new texture, no allocation).
        /// </summary>
        void DrawViewCone(float s)
        {
            World w = Driver != null ? Driver.World : null;
            GameHud hud = Driver != null ? Driver.GetComponent<GameHud>() : null;
            if (w == null || hud == null || hud.Clusters == null || hud.Canvas == null) return;
            RectTransform c = hud.Clusters[(int)HudCluster.Minimap];
            if (c == null || !c.gameObject.activeInHierarchy) return;
            CriticalMass.Runtime.Hud.Ui.MinimapProjection proj = CriticalMass.Runtime.Hud.Ui.MinimapGeometry.Projection;
            if (proj == null || proj.Width <= 0 || proj.Height <= 0) return;
            c.GetWorldCorners(_corners);
            UnityEngine.Camera uiCam = hud.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hud.Canvas.worldCamera;
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(uiCam, _corners[0]);
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(uiCam, _corners[2]);
            float fw = (tr.x - bl.x), fh = (tr.y - bl.y);
            if (fw < 8f || fh < 8f) return;
            // The map image sits inside the frame (HudLayout.MinimapFrame on each side of the reference size).
            float kx = fw / HudLayout.MinimapSize.x, ky = fh / HudLayout.MinimapSize.y;
            float left = bl.x + HudLayout.MinimapFrame * kx, top = Screen.height - tr.y + HudLayout.MinimapFrame * ky;
            float iw = fw - 2f * HudLayout.MinimapFrame * kx, ih = fh - 2f * HudLayout.MinimapFrame * ky;
            Vector2 px = proj.ToPixel(w.X[0], w.Z[0]);
            Vector2 at = new Vector2(left + px.x / proj.Width * iw, top + px.y / proj.Height * ih);
            GroundCamera.Basis(_follow.Yaw, out double ux, out double uz, out _, out _);
            Vector2 dir = new Vector2((float)proj.U(ux, uz), (float)proj.V(ux, uz));
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            Vector2 side = new Vector2(-dir.y, dir.x);
            float half = Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
            GUI.color = new Color(1f, 0.96f, 0.86f, 0.22f);
            for (int i = 1; i <= 7; i++)
            {
                float d = i * 4.5f * s, r = Mathf.Max(1.5f * s, d * half * 0.55f);
                Vector2 p = at + dir * d;
                for (int j = -1; j <= 1; j++)
                {
                    Vector2 q = p + side * (j * d * half * 0.6f);
                    GUI.DrawTexture(new Rect(q.x - r * 0.5f, q.y - r * 0.5f, r, r), Dot());
                }
            }
        }

        /// <summary>
        /// feat/follow-camera-1: where skills and shots go, on the ground in front of the character: a dotted trail from his
        /// feet to the aim point the sim gets (TryPlayAim), the dots smaller with distance, a larger dot at the end. Points
        /// behind the lens are skipped (first person starts the trail below the screen). No texture or mesh added.
        /// </summary>
        void DrawAimTrail(float s)
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null || _cam == null || !TryPlayAim(w, out double ax, out double az)) return;
            double a = Driver.Alpha;
            double px = w.PrevX[0] + (w.X[0] - w.PrevX[0]) * a, pz = w.PrevZ[0] + (w.Z[0] - w.PrevZ[0]) * a;
            const int n = 9;
            for (int k = 2; k <= n; k++)
            {
                double t = k / (double)n;
                double x = px + (ax - px) * t, z = pz + (az - pz) * t;
                Vector3 g = SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + 0.06, z);
                Vector3 sp = _cam.WorldToScreenPoint(g);
                if (sp.z <= 0.2f) continue;
                float size = Mathf.Clamp(60f / sp.z, 3f, 14f) * s * (k == n ? 1.8f : 1f);
                float gy = Screen.height - sp.y;
                GUI.color = new Color(0.06f, 0.08f, 0.16f, 0.45f);
                GUI.DrawTexture(new Rect(sp.x - size * 0.65f, gy - size * 0.65f, size * 1.3f, size * 1.3f), Dot());
                GUI.color = new Color(1f, 0.96f, 0.86f, k == n ? 0.9f : 0.6f);
                GUI.DrawTexture(new Rect(sp.x - size * 0.5f, gy - size * 0.5f, size, size), Dot());
            }
        }

        void DrawPlayAids(float s)
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null || _cam == null) return;
            // Reticle: a small cream dot with a dark ring, at the screen centre (the aim point is along this ray).
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            GUI.color = new Color(0.06f, 0.08f, 0.16f, 0.8f);
            GUI.DrawTexture(new Rect(c.x - 7 * s, c.y - 7 * s, 14 * s, 14 * s), Dot());
            GUI.color = new Color(1f, 0.96f, 0.86f, 1f);
            GUI.DrawTexture(new Rect(c.x - 4 * s, c.y - 4 * s, 8 * s, 8 * s), Dot());
            // Off-screen threat ring: enemies within ThreatRange that the camera cannot see, as team-coloured pointers on a
            // ring around the reticle (behind the player = bottom of the ring).
            TeamId me = w.Actors[0].Team;
            float ring = Screen.height * 0.30f;
            GroundCamera.Basis(_follow.Yaw, out double ux, out double uz, out double rx, out double rz);
            for (int i = 1; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.Team == me) continue;
                double dx = w.X[i] - w.X[0], dz = w.Z[i] - w.Z[0];
                double d2 = dx * dx + dz * dz;
                if (d2 > ThreatRange * ThreatRange) continue;
                Vector3 vp = _cam.WorldToViewportPoint(SimSpace.ToWorld(w.X[i], GroundHeight.At(w.Map, w.X[i], w.Z[i]) + 1.0, w.Z[i]));
                if (vp.z > 0 && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f) continue;
                double fwd = dx * ux + dz * uz, side = dx * rx + dz * rz;
                float ang = Mathf.Atan2((float)side, (float)fwd);   // 0 = ahead (top of the ring), +90 = right
                float near = 1f - Mathf.Sqrt((float)d2) / (float)ThreatRange;
                Vector2 p = c + new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * ring;
                float size = (18f + 14f * near) * s;
                Color tc = CMTokens.Team(a.Team);
                tc.a = 0.55f + 0.45f * near;
                GUI.color = tc;
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang * Mathf.Rad2Deg + 180f, p);   // the texture apex is its bottom row: point outward, at the threat
                GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Tri());
                GUI.matrix = m;
            }
        }
    }
}

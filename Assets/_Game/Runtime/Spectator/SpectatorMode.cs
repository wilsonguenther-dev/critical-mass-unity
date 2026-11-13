using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Spectator
{
    /// <summary>
    /// Spectator mode (owner, 2026-11-12: "there's no way for me to select spectator mode or go into it live or switch").
    /// Entered from match setup ("SPECTATE" beside the two teams: SpectateNextMatch), from the pause menu at any time
    /// (Enter), or from the offer shown while knocked out (SpectatorOverlay: V / Y / click). While spectating, actor 0
    /// plays itself through the sim's existing autopilot path (SimDriverBehaviour.HumanAutopilot = true, so MatchTick.Advance
    /// fills actor 0 from the bot brain; nothing under Sim changes) and the camera is free:
    /// <list type="bullet">
    /// <item>follow any player: click them, or cycle with Q / E (keyboard) or the bumpers (controller);</item>
    /// <item>jump to objectives: 1 hill, 2 shrine, 3 Azure base, 4 Crimson base, 5 Kaiju (d-pad up, down, left, right and
    /// the right stick press);</item>
    /// <item>fly free with the move keys or the left stick; zoom with the wheel or the triggers; T or Y toggles the top-down
    /// tactical view; minus / equals (or the left stick press) change the speed (0.5x, 1x, 2x of the match pace, through
    /// the driver's own TimeScale, which only changes how many fixed ticks run per wall second);</item>
    /// <item>R or X (or the pause menu's "Rejoin as ...") gives control back: at once when actor 0 is standing, else at its
    /// respawn (actor 0 is handed back to the human input straight away, so a class picked while down reaches the sim).</item>
    /// </list>
    /// Camera work goes only through the rig's public hooks: CameraFollowTarget.SetOverride / ClearOverride for the look
    /// point, CameraFollowTarget.BaseOffset (its public offset, which it multiplies by its own zoom every frame) for zoom
    /// and the tactical height, and the CM Camera's transform rotation for the tactical pitch; every one is restored on
    /// exit. The sim is only read; the driver's HumanAutopilot and TimeScale are its public fields (as DG-05's autoplay).
    /// </summary>
    [GameOverlay(Order = 70, Scope = OverlayScope.MatchOnly)]
    [DefaultExecutionOrder(900)]   // after the driver's Update (Alpha), before every LateUpdate (the camera follow)
    public sealed class SpectatorMode : MonoBehaviour
    {
        /// <summary>The tactical view's pitch (degrees down from the horizon) and its distance factor over the gameplay rig.</summary>
        public const float TacticalPitchDeg = 84f;
        public const float TacticalDistanceScale = 1.9f;

        public static SpectatorMode Current { get; private set; }

        /// <summary>True while the local player is spectating.</summary>
        public static bool IsSpectating => Current != null && Current.Active;

        /// <summary>Match setup's "SPECTATE": the next match starts in spectator mode (kept for Restart; cleared by Rejoin).</summary>
        public static bool SpectateNextMatch;

        /// <summary>Raised on entering (true) and leaving (false) spectator mode.</summary>
        public static event Action<bool> Changed;

        /// <summary>Raised when control returns to the player (PlayerPresence shows the YOU marker).</summary>
        public static event Action ControlReturned;

        public SimDriverBehaviour Driver { get; private set; }
        public HumanInputSource Input { get; private set; }
        public SpectatorRig Rig { get; } = new SpectatorRig();
        public bool Active { get; private set; }
        /// <summary>Rejoin asked while actor 0 was down: control returns at its respawn.</summary>
        public bool RejoinPending { get; private set; }

        CameraFollowTarget _follow;
        Transform _cmCamera;
        Vector3 _baseOffset;
        Quaternion _baseRotation;
        bool _cameraSaved;
        double _baseTimeScale;
        bool _hiddenPlayerCluster;
        InputAction _move;
        SpectatorOverlay _overlay;
        bool _pendingFromSetup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            SpectateNextMatch = false;
            Changed = null;
            ControlReturned = null;
        }

        void Awake()
        {
            Current = this;
            Driver = GetComponent<SimDriverBehaviour>();
            Input = GetComponent<HumanInputSource>();
            _overlay = GetComponent<SpectatorOverlay>();
            if (_overlay == null) _overlay = gameObject.AddComponent<SpectatorOverlay>();
            _overlay.Mode = this;
        }

        void Start()
        {
            ClassSwitch.Clear();   // a pick held in the last world never carries into a new one
            // Match setup chose SPECTATE: start the match watching (never behind the menus, never in capture or autoplay).
            _pendingFromSetup = SpectateNextMatch && Driver != null && !SimDriverBehaviour.CaptureMode &&
                                (Driver.Request == null || !Driver.Request.Backdrop);
        }

        void OnDestroy()
        {
            if (Active) Exit();
            if (Current == this) Current = null;
        }

        // ---------------------------------------------------------------- static API

        /// <summary>Enter spectator mode on the current match; false when there is none.</summary>
        public static bool Enter() => Current != null && Current.EnterNow();

        /// <summary>Leave spectator mode (at once when actor 0 stands, else at its respawn); false when not spectating.</summary>
        public static bool Rejoin() => Current != null && Current.RequestRejoin();

        /// <summary>Spectate when playing, rejoin when spectating.</summary>
        public static void Toggle()
        {
            if (IsSpectating) Rejoin();
            else Enter();
        }

        /// <summary>The class the player rejoins as: a class queued for the next respawn, else actor 0's own.</summary>
        public static ClassId RejoinClass()
        {
            SpectatorMode m = Current;
            World w = m != null && m.Driver != null ? m.Driver.World : null;
            if (ClassSwitch.HasPending) return ClassSwitch.Pending;
            if (w == null || w.ActorCount <= 0) return ClassId.Warrior;
            Actor a = w.Actors[0];
            return a.PendingClass ?? a.Cls;
        }

        // ---------------------------------------------------------------- enter / exit

        public bool EnterNow()
        {
            if (Active) return true;
            World w = Driver != null ? Driver.World : null;
            if (w == null || w.ActorCount <= 0) return false;
            Active = true;
            RejoinPending = false;
            Driver.HumanAutopilot = true;
            _baseTimeScale = Driver.TimeScale;
            Rig.Reset(w, 0);
            FindCamera();
            SaveCamera();
            SetPlayerCluster(false);
            SetDeathRecap(false);
            ClassSwitch.SetRespawnPickerAuto(false);   // DG-10's respawn strip (when merged) must not open for the bot's knock-outs
            if (Input != null) Input.State.Blur();
            Changed?.Invoke(true);
            return true;
        }

        public bool RequestRejoin()
        {
            if (!Active) return false;
            SpectateNextMatch = false;
            World w = Driver != null ? Driver.World : null;
            bool standing = w != null && w.ActorCount > 0 && w.Actors[0].Alive;
            // Hand actor 0 back to the human input now (a down actor ignores input; a class pick reaches the sim).
            if (Driver != null) Driver.HumanAutopilot = false;
            if (Input != null) Input.State.Blur();
            if (standing) Exit();
            else RejoinPending = true;
            return true;
        }

        void Exit()
        {
            Active = false;
            RejoinPending = false;
            if (Driver != null)
            {
                Driver.HumanAutopilot = false;
                if (_baseTimeScale > 0) Driver.TimeScale = _baseTimeScale;
            }
            RestoreCamera();
            SetPlayerCluster(true);
            SetDeathRecap(true);
            ClassSwitch.SetRespawnPickerAuto(true);
            if (Input != null) Input.State.Blur();
            Changed?.Invoke(false);
            ControlReturned?.Invoke();
        }

        // ---------------------------------------------------------------- frame

        void Update()
        {
            if (_pendingFromSetup)
            {
                _pendingFromSetup = false;
                EnterNow();
            }
            ClassSwitch.Tick(Driver, Input);
            if (!Active) return;
            World w = Driver != null ? Driver.World : null;
            if (w == null) return;

            // Nothing may leave actor 0 to the human input while spectating (rejoin pending excepted).
            if (!RejoinPending && !Driver.HumanAutopilot) Driver.HumanAutopilot = true;
            if (RejoinPending && w.ActorCount > 0 && w.Actors[0].Alive)
            {
                Exit();
                return;
            }

            bool paused = Input != null && Input.State.Paused;
            double dt = Driver.FrameDt;
            if (!paused) ReadControls(w, dt);
            if (!Active) return; // integration batch 2 (#35 fix): R rejoined inside ReadControls; applying the camera again undid the restore
            Rig.Step(w, Driver.Alpha, paused ? 0 : dt);
            ApplyCamera();
        }

        void ReadControls(World w, double dt)
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;

            if (kb != null)
            {
                if (kb.qKey.wasPressedThisFrame) Rig.Cycle(w, -1);
                if (kb.eKey.wasPressedThisFrame) Rig.Cycle(w, 1);
                if (kb.digit1Key.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.Hill);
                if (kb.digit2Key.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.Shrine);
                if (kb.digit3Key.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.AzureBase);
                if (kb.digit4Key.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.CrimsonBase);
                if (kb.digit5Key.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.Kaiju);
                if (kb.tKey.wasPressedThisFrame) Rig.ToggleTactical();
                if (kb.minusKey.wasPressedThisFrame) StepSpeed(-1, false);
                if (kb.equalsKey.wasPressedThisFrame) StepSpeed(1, false);
                if (kb.rKey.wasPressedThisFrame)
                {
                    RequestRejoin();
                    return;
                }
            }
            if (pad != null)
            {
                if (pad.leftShoulder.wasPressedThisFrame) Rig.Cycle(w, -1);
                if (pad.rightShoulder.wasPressedThisFrame) Rig.Cycle(w, 1);
                if (pad.dpad.up.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.Hill);
                if (pad.dpad.down.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.Shrine);
                if (pad.dpad.left.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.AzureBase);
                if (pad.dpad.right.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.CrimsonBase);
                if (pad.rightStickButton.wasPressedThisFrame) Rig.JumpTo(SpectatorObjective.Kaiju);
                if (pad.buttonNorth.wasPressedThisFrame) Rig.ToggleTactical();
                if (pad.leftStickButton.wasPressedThisFrame) StepSpeed(1, true);
                if (pad.buttonSouth.wasPressedThisFrame)
                {
                    int near = Rig.Nearest(w);
                    if (near >= 0) Rig.Follow(w, near);
                }
                if (pad.buttonWest.wasPressedThisFrame)
                {
                    RequestRejoin();
                    return;
                }
                float zoom = pad.leftTrigger.ReadValue() - pad.rightTrigger.ReadValue();
                if (Mathf.Abs(zoom) > 0.1f) Rig.ZoomBy(zoom * dt * 6.0);
            }
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) Rig.ZoomBy(-Mathf.Sign(wheel));
                if (mouse.leftButton.wasPressedThisFrame && !PointerOverUi()) PickUnderPointer(w, mouse.position.ReadValue());
            }

            if (_move == null)
            {
                InputActionMap map = ControlBindings.Map(InputSystem.actions);
                _move = map != null ? map.FindAction("Move", false) : null;
            }
            if (_move != null)
            {
                Vector2 m = _move.ReadValue<Vector2>();
                Rig.FreeFly(m.x, m.y, dt);
            }
        }

        void StepSpeed(int dir, bool wrap)
        {
            if (_baseTimeScale <= 0) return;   // the wall clock is stopped (capture, tests): nothing to scale
            Rig.StepSpeed(dir, wrap);
            Driver.TimeScale = _baseTimeScale * Rig.Speed;
        }

        static bool PointerOverUi()
        {
            EventSystem es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        /// <summary>Follows the player under a screen point (the ground point under it, through the gameplay camera).</summary>
        public bool PickUnderPointer(World w, Vector2 screen)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null || w == null) return false;
            Ray ray = cam.ScreenPointToRay(screen);
            if (!GroundHeight.TryAimPoint(ray, w.Map, Rig.X, Rig.Z, out double sx, out double sz)) return false;
            int id = SpectatorRig.PickAt(w, sx, sz);
            return id >= 0 && Rig.Follow(w, id);
        }

        // ---------------------------------------------------------------- camera hooks

        void FindCamera()
        {
            if (_follow == null) _follow = FindAnyObjectByType<CameraFollowTarget>();
            if (_follow != null && _follow.Rig != null) _cmCamera = _follow.Rig.transform;
        }

        void SaveCamera()
        {
            if (_follow == null) return;
            _baseOffset = _follow.BaseOffset;
            _baseRotation = _cmCamera != null ? _cmCamera.rotation : Quaternion.identity;
            _cameraSaved = true;
        }

        void ApplyCamera()
        {
            if (_follow == null)
            {
                FindCamera();
                if (_follow == null) return;
                if (!_cameraSaved) SaveCamera();
            }
            _follow.SetOverride(Rig.X, Rig.Z);
            float zoom = (float)Rig.Zoom;
            if (Rig.Tactical && Driver != null)
            {
                Quaternion rot = Quaternion.Euler(TacticalPitchDeg, (float)(360 - Driver.Config.Render.CameraYawDeg), 0f);
                if (_cmCamera != null && _cmCamera.rotation != rot) _cmCamera.rotation = rot;
                _follow.BaseOffset = -(rot * Vector3.forward) * (float)GameplayCamera.Distance * TacticalDistanceScale * zoom;
            }
            else
            {
                if (_cmCamera != null && _cmCamera.rotation != _baseRotation) _cmCamera.rotation = _baseRotation;
                _follow.BaseOffset = _baseOffset * zoom;
            }
        }

        void RestoreCamera()
        {
            if (_follow == null) return;
            _follow.ClearOverride();
            if (_cameraSaved)
            {
                _follow.BaseOffset = _baseOffset;
                if (_cmCamera != null) _cmCamera.rotation = _baseRotation;
            }
        }

        // ---------------------------------------------------------------- the player's own HUD while watching

        /// <summary>The HUD's player card (hearts, abilities) describes actor 0; while watching it hides, the overlay shows the followed player.</summary>
        void SetPlayerCluster(bool on)
        {
            GameHud hud = GetComponent<GameHud>();
            if (hud == null || hud.Clusters == null) return;
            RectTransform c = hud.Clusters[(int)HudCluster.Player];
            if (c == null) return;
            if (!on)
            {
                _hiddenPlayerCluster = c.gameObject.activeSelf;
                c.gameObject.SetActive(false);
            }
            else if (_hiddenPlayerCluster)
            {
                c.gameObject.SetActive(true);
                _hiddenPlayerCluster = false;
            }
        }

        /// <summary>The death recap describes actor 0's knock-outs; it is off while actor 0 plays itself.</summary>
        void SetDeathRecap(bool on)
        {
            DeathRecap r = FindAnyObjectByType<DeathRecap>();
            if (r == null) return;
            r.enabled = on;   // on: it manages its own canvas again from its next frame
            if (!on && r.Canvas != null) r.Canvas.enabled = false;
        }
    }
}

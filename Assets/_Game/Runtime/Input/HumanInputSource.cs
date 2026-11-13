using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.InputSystem;
using B = CriticalMass.Runtime.Input.HumanInputState.HumanButton;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// Reads the project-wide InputSystem.actions "Player" map into a HumanInputState (browser humanInput.ts listeners).
    /// Buttons: InputAction.started fires once per press (key repeat raises no new started) -> ButtonDown, canceled ->
    /// ButtonUp. Move and the pointer aim are sampled every frame; the aim ray hits the plane at the ground height of
    /// actor 0's cell (GroundHeight.TryAimPoint, browser pickHumanAim) and goes through SimSpace.ToSim (the only z
    /// negation, O-13). With no SimDriverBehaviour World on this GameObject the plane is y = 0. The gameplay camera is
    /// perspective (U-005 amendment 2026-11-12): ScreenPointToRay starts each ray at the camera and points it through the
    /// pixel, so nothing here assumes parallel rays; at pitch 55 and FOV 35 even the top-edge ray points 37.5 degrees
    /// down, so the plane is always hit. Move stays a screen-space intent mapped by yaw only (HumanInputState).
    /// <para>Controller (controls-v1): the "Aim" action (right stick) aims through AimResolver whenever a stick moved more
    /// recently than the mouse; with the right stick at rest the aim follows the move direction. The result reaches the
    /// sim exactly like a mouse aim point (State.SetAim), so the sim input path is unchanged. PauseHandler lets the pause
    /// menu take the Pause press while it is open (Escape or Start then means "back" instead of toggling).</para>
    /// </summary>
    public sealed class HumanInputSource : MonoBehaviour
    {
        /// <summary>Action names in the Player map bound to game buttons, parallel to Buttons.</summary>
        private static readonly string[] ButtonActions = { "Jump", "Attack", "Ability", "Interact", "DigPlace", "Pause", "Scoreboard" };
        private static readonly B[] Buttons = { B.Jump, B.Attack, B.Ability, B.Interact, B.DigPlace, B.Pause, B.Scoreboard };

        /// <summary>The human's input between two sim steps; SimDriverBehaviour calls State.Fill once per tick.</summary>
        public HumanInputState State { get; } = new HumanInputState();

        private InputActionMap _map;
        private SimDriverBehaviour _driver;
        private InputAction _move;
        private InputAction _point;
        private InputAction _aim;

        /// <summary>Which device aims and the controller aim point (view side).</summary>
        public AimResolver Aim { get; } = new AimResolver();

        /// <summary>
        /// Called on every Pause press before the toggle; returning true consumes the press (the pause menu uses it for
        /// "back" while open). Null = every press toggles State.Paused.
        /// </summary>
        public Func<bool> PauseHandler;
        /// <summary>DG-09: the SwitchDigPlace axis (wheel scroll/y, LB negative / RB positive), edge-detected each frame.</summary>
        private InputAction _switchDigPlace;
        private bool _switchWasActive;
        private readonly InputAction[] _actions = new InputAction[ButtonActions.Length];
        private readonly Action<InputAction.CallbackContext>[] _started = new Action<InputAction.CallbackContext>[ButtonActions.Length];
        private readonly Action<InputAction.CallbackContext>[] _canceled = new Action<InputAction.CallbackContext>[ButtonActions.Length];

        private void OnEnable()
        {
            var map = InputSystem.actions.FindActionMap("Player", true);
            map.Enable();
            _map = map;
            _move = map["Move"];
            _point = map["Point"];
            _aim = map.FindAction("Aim", false);
            _switchDigPlace = map.FindAction("SwitchDigPlace");
            _switchWasActive = false;
            for (int i = 0; i < ButtonActions.Length; i++)
            {
                B b = Buttons[i];
                InputAction a = map[ButtonActions[i]];
                _actions[i] = a;
                _started[i] = _ =>
                {
                    if (b == B.Pause && PauseHandler != null && PauseHandler()) return;
                    State.ButtonDown(b);
                };
                _canceled[i] = _ => State.ButtonUp(b);
                a.started += _started[i];
                a.canceled += _canceled[i];
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < _actions.Length; i++)
            {
                InputAction a = _actions[i];
                if (a == null) continue;
                a.started -= _started[i];
                a.canceled -= _canceled[i];
                _actions[i] = null;
                _started[i] = null;
                _canceled[i] = null;
            }
            _map = null;
            _move = null;
            _point = null;
            _aim = null;
            _switchDigPlace = null;
        }

        private void Update()
        {
            if (_map == null) return;
            var m = _move.ReadValue<Vector2>();
            State.SetMove(m.x, m.y);
            // DG-09 (U-053): a wheel notch or a bumper press flips the miner's dig / place mode. The axis goes non-zero for a
            // scroll frame or while a bumper is held; only the rising edge counts, so a held bumper switches once.
            if (_switchDigPlace != null)
            {
                bool active = Mathf.Abs(_switchDigPlace.ReadValue<float>()) > 0.5f;
                if (active && !_switchWasActive) State.SwitchDigPlace();
                _switchWasActive = active;
            }

            // Which device aims: the one used last (mouse movement or click vs a controller stick).
            ControlBindings.Sample();
            Vector2 stick = _aim != null ? _aim.ReadValue<Vector2>() : Vector2.zero;
            bool moveByPad = m.sqrMagnitude > 0f && _move.activeControl != null && _move.activeControl.device is Gamepad;
            if (stick.sqrMagnitude > 0f || moveByPad) Aim.NoteStick();
            else if (Mouse.current != null && ControlBindings.MouseUsed(Mouse.current)) Aim.NotePointer();

            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            if (Aim.Current == AimResolver.Mode.Stick)
            {
                if (w != null && w.ActorCount > 0 && Aim.ResolveStick(stick.x, stick.y, m.x, m.y, w.X[0], w.Z[0], out double ax, out double az))
                    State.SetAim(ax, az);
                else State.ClearAim();
                return;
            }

            // UnityEngine.Camera spelled out: CriticalMass.Runtime.Camera (F-08b) is a namespace and would shadow it.
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (Mouse.current == null || cam == null)
            {
                State.ClearAim();
                return;
            }
            Ray ray = cam.ScreenPointToRay(_point.ReadValue<Vector2>());
            // pickHumanAim (humanInput.ts:166): the plane at the ground height of actor 0's cell, not y = 0.
            MapDef map = w != null && w.ActorCount > 0 ? w.Map : null;
            double hx = map != null ? w.X[0] : 0, hz = map != null ? w.Z[0] : 0;
            if (GroundHeight.TryAimPoint(ray, map, hx, hz, out double sx, out double sz)) State.SetAim(sx, sz);
            else State.ClearAim();
        }

        private void OnApplicationFocus(bool f)
        {
            if (!f) State.Blur();
        }
    }
}

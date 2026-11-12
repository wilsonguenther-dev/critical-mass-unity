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
    /// negation, O-13). With no SimDriverBehaviour World on this GameObject the plane is y = 0.
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
            for (int i = 0; i < ButtonActions.Length; i++)
            {
                B b = Buttons[i];
                InputAction a = map[ButtonActions[i]];
                _actions[i] = a;
                _started[i] = _ => State.ButtonDown(b);
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
        }

        private void Update()
        {
            if (_map == null) return;
            var m = _move.ReadValue<Vector2>();
            State.SetMove(m.x, m.y);

            // UnityEngine.Camera spelled out: CriticalMass.Runtime.Camera (F-08b) is a namespace and would shadow it.
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (Mouse.current == null || cam == null)
            {
                State.ClearAim();
                return;
            }
            Ray ray = cam.ScreenPointToRay(_point.ReadValue<Vector2>());
            // pickHumanAim (humanInput.ts:166): the plane at the ground height of actor 0's cell, not y = 0.
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
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

using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
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
    /// <para>Easy movement (2026-11-12): the rebindable "MoveToPointer" action (default mouse back button, MOUSE 4) walks
    /// toward the ground point under the cursor while held; the settings "Movement assist" and "Keys follow the map"
    /// (GameSettings) are copied into State, which applies them in Fill (MoveAssist). DefaultExecutionOrder(-100): this
    /// Update samples the devices BEFORE SimDriverBehaviour.Update steps the sim in the same frame, so a key press reaches
    /// the next tick with no extra frame of delay (the two scripts had the same order, which Unity leaves unspecified).</para>
    /// </summary>
    /// <para>controls-1 (owner, 2026-11-13): three live schemes, last device wins, no setting. Keyboard only: Attack J / Z,
    /// ability K / X, jump SPACE / C (the left hand on W A S D, or the right hand on the arrows) and the fighter faces where
    /// it moves (AimResolver.Mode.Keys: last facing kept standing still, LockFacing L / L SHIFT holds it to strafe, target
    /// assist toward the nearest enemy in a 30 degree cone within 7 units). The mouse takes the aim back the moment it
    /// moves or clicks; a keyboard attack or ability key with the mouse resting ControlBindings.MouseIdleSeconds hands it
    /// to the keys again. A controller stick takes it as before. Walk to pointer only acts while the mouse aims, lock
    /// facing only while the keys aim, so the two may share L SHIFT. Every result reaches the sim as an aim point
    /// (State.SetAim), so the sim input path is unchanged.</para>
    /// <para>Controller settings (ControllerSettings: dead zone, sensitivity, invert aim Y) shape the stick values here,
    /// view side, before they become a move intent or an aim direction.</para>
    [DefaultExecutionOrder(-100)]
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
        private InputAction _walk;
        private InputAction _lock;
        private bool _keyAimPressed;
        private bool _wasAlive;
        private double[] _enemyX = new double[64];
        private double[] _enemyZ = new double[64];

        /// <summary>LockFacing is held and the keys aim (tests).</summary>
        public bool FacingLocked { get; private set; }

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
            _walk = map.FindAction("MoveToPointer", false);
            _lock = map.FindAction("LockFacing", false);
            _keyAimPressed = false;
            // controls-1: the match starts with the scheme the player used last (keyboard only until the mouse is used).
            ControlScheme scheme = ControlBindings.ActiveScheme;
            Aim.SetMode(scheme == ControlScheme.Gamepad ? AimResolver.Mode.Stick
                : scheme == ControlScheme.Keyboard ? AimResolver.Mode.Keys : AimResolver.Mode.Pointer);
            ReadSettings();
            GameSettings.Changed += ReadSettings;
            for (int i = 0; i < ButtonActions.Length; i++)
            {
                B b = Buttons[i];
                InputAction a = map[ButtonActions[i]];
                _actions[i] = a;
                _started[i] = ctx =>
                {
                    if (b == B.Pause && PauseHandler != null && PauseHandler()) return;
                    // controls-1: a keyboard attack or ability key may hand the aim to the keys; any named key sets the hand.
                    if (ctx.control != null && ctx.control.device is Keyboard)
                    {
                        if (b == B.Attack || b == B.Ability) _keyAimPressed = true;
                        KeyboardHand? hand = ControlBindings.HandOf(ctx.action, ctx.control);
                        if (hand.HasValue) ControlBindings.SetHand(hand.Value);
                    }
                    State.ButtonDown(b);
                };
                _canceled[i] = _ => State.ButtonUp(b);
                a.started += _started[i];
                a.canceled += _canceled[i];
            }
        }

        private void ReadSettings()
        {
            State.MovementAssist = GameSettings.MovementAssist;
            State.KeysFollowMap = GameSettings.KeysFollowMap;
        }

        private void OnDisable()
        {
            GameSettings.Changed -= ReadSettings;
            State.BindWorld(null);
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
            _walk = null;
            _lock = null;
            FacingLocked = false;
        }

        private void Update()
        {
            if (_map == null) return;
            var m = _move.ReadValue<Vector2>();
            bool padMove = m.sqrMagnitude > 0f && _move.activeControl != null && _move.activeControl.device is Gamepad;
            if (padMove) m = ControllerSettings.ShapeMove(m);
            // DG-09 (U-053): a wheel notch or a bumper press flips the miner's dig / place mode. The axis goes non-zero for a
            // scroll frame or while a bumper is held; only the rising edge counts, so a held bumper switches once.
            if (_switchDigPlace != null)
            {
                bool active = Mathf.Abs(_switchDigPlace.ReadValue<float>()) > 0.5f;
                // 2026-11-12: the wheel (and its bumpers) zoom the full map while it is open and the spectator camera
                // while spectating; only in play does it switch dig / place (InputContext).
                if (active && !_switchWasActive && InputContext.DigSwitchOwnsWheel(InputContext.Current)) State.SwitchDigPlace();
                _switchWasActive = active;
            }
            bool fromKeys = m.sqrMagnitude > 0f && _move.activeControl != null && _move.activeControl.device is Keyboard;
            State.SetMove(m.x, m.y, fromKeys);
            if (fromKeys)
            {
                KeyboardHand? hand = ControlBindings.HandOf(_move, _move.activeControl);
                if (hand.HasValue) ControlBindings.SetHand(hand.Value);
            }

            // Which device aims: the one used last (mouse movement or click, a controller stick, or a keyboard attack key
            // with the mouse resting).
            ControlBindings.Sample();
            Vector2 stick = _aim != null ? ControllerSettings.ShapeAim(_aim.ReadValue<Vector2>()) : Vector2.zero;
            bool moveByPad = padMove;
            bool keyAim = _keyAimPressed || (_lock != null && _lock.WasPressedThisFrame() && _lock.activeControl != null &&
                                             _lock.activeControl.device is Keyboard && !(_walk != null && _walk.IsPressed() && Aim.Current == AimResolver.Mode.Pointer));
            _keyAimPressed = false;
            if (stick.sqrMagnitude > 0f || moveByPad) Aim.NoteStick();
            else if (Mouse.current != null && ControlBindings.MouseUsed(Mouse.current)) Aim.NotePointer();
            else if (keyAim && ControlBindings.MouseIdleFor >= ControlBindings.MouseIdleSeconds)
            {
                Aim.NoteKeys();
                ControlBindings.SetKeyboardOnly(true);
            }
            else if (fromKeys && Aim.Current == AimResolver.Mode.Stick)
            {
                // Back from the controller to the keys: the keyboard scheme the player had before.
                Aim.SetMode(ControlBindings.KeyboardOnly ? AimResolver.Mode.Keys : AimResolver.Mode.Pointer);
            }

            // Walk to pointer acts only while the mouse aims (it shares L SHIFT with lock facing, which acts only while
            // the keys aim).
            bool walk = _walk != null && _walk.IsPressed() && Aim.Current == AimResolver.Mode.Pointer;
            State.SetPointerWalk(walk);
            FacingLocked = _lock != null && _lock.IsPressed() && Aim.Current == AimResolver.Mode.Keys;

            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            State.BindWorld(w);
            // UnityEngine.Camera spelled out: CriticalMass.Runtime.Camera (F-08b) is a namespace and would shadow it.
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            // Pointer walking reads the cursor's ground point whichever device aims.
            if (walk && Mouse.current != null && cam != null && PointerGround(cam, w, out double wx, out double wz)) State.SetWalkTarget(wx, wz);
            else State.ClearWalkTarget();
            if (Aim.Current == AimResolver.Mode.Keys)
            {
                if (w != null && w.ActorCount > 0)
                {
                    // A respawn faces the hill (Respawn.cs); the keyboard facing starts again from there.
                    bool alive = w.Actors[0].Alive;
                    if (alive && !_wasAlive) Aim.ForgetKeysDirection();
                    _wasAlive = alive;
                    State.MoveDirection(out double dx, out double dz);
                    int n = CollectEnemies(w);
                    Aim.ResolveKeys(dx, dz, w.X[0], w.Z[0], w.Actors[0].Facing, FacingLocked, _enemyX, _enemyZ, n, out double kx, out double kz);
                    State.SetAim(kx, kz);
                }
                else State.ClearAim();
                return;
            }
            if (Aim.Current == AimResolver.Mode.Stick)
            {
                if (w != null && w.ActorCount > 0 && Aim.ResolveStick(stick.x, stick.y, m.x, m.y, w.X[0], w.Z[0], out double ax, out double az))
                    State.SetAim(ax, az);
                else State.ClearAim();
                return;
            }

            if (Mouse.current == null || cam == null)
            {
                State.ClearAim();
                return;
            }
            if (PointerGround(cam, w, out double sx, out double sz)) State.SetAim(sx, sz);
            else State.ClearAim();
        }

        /// <summary>Living actors of the other team, positions copied for the keyboard target assist (read-only).</summary>
        private int CollectEnemies(World w)
        {
            if (_enemyX.Length < w.ActorCount)
            {
                _enemyX = new double[w.ActorCount];
                _enemyZ = new double[w.ActorCount];
            }
            var me = w.Actors[0].Team;
            int n = 0;
            for (int i = 1; i < w.ActorCount; i++)
            {
                var a = w.Actors[i];
                if (a == null || !a.Alive || a.Team == me) continue;
                _enemyX[n] = w.X[i];
                _enemyZ[n] = w.Z[i];
                n++;
            }
            return n;
        }

        /// <summary>The sim ground point under the pointer (false when the ray misses).</summary>
        private bool PointerGround(UnityEngine.Camera cam, World w, out double sx, out double sz)
        {
            Ray ray = cam.ScreenPointToRay(_point.ReadValue<Vector2>());
            // pickHumanAim (humanInput.ts:166): the plane at the ground height of actor 0's cell, not y = 0.
            MapDef map = w != null && w.ActorCount > 0 ? w.Map : null;
            double hx = map != null ? w.X[0] : 0, hz = map != null ? w.Z[0] : 0;
            return GroundHeight.TryAimPoint(ray, map, hx, hz, out sx, out sz);
        }

        private void OnApplicationFocus(bool f)
        {
            if (!f) State.Blur();
        }
    }
}

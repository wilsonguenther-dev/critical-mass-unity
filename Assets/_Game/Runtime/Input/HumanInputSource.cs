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
        private static readonly string[] ButtonActions = { "Jump", "Attack", "Ability", "Interact", "DigPlace", "Pause", "Scoreboard", "Dodge", "Cancel" };
        private static readonly B[] Buttons = { B.Jump, B.Attack, B.Ability, B.Interact, B.DigPlace, B.Pause, B.Scoreboard, B.Dodge, B.Cancel };

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
            EnsureDodgeAction(map); // CM b13 J: until Settings/Controls.inputactions carries the Dodge action
            EnsureCancelAction(map); // b16: an older or rebound asset without the Cancel action gets it in memory
            map.Enable();
            BuilderToolbelt.Local = State.Toolbelt;   // b14: the HUD and the terrain view read the local Builder's wheel
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
            // Every match starts in the remembered layout's scheme (keys and mouse when none was picked).
            ControlPresets.StartOfMatch();
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
                    // online-rooms-2: while a chat line is being typed (and on the frame Escape or Enter closes it) the keyboard
                    // belongs to the chat box: no pause menu, no scoreboard, no jump; the fighter gets neutral input meanwhile.
                    if (Online.OnlineChatHud.EatsKeys && ctx.control != null && ctx.control.device is Keyboard) return;
                    if (b == B.Pause && PauseHandler != null && PauseHandler()) return;
                    // controls-1: a keyboard attack or ability key may hand the aim to the keys; any named key sets the hand.
                    if (ctx.control != null && ctx.control.device is Keyboard)
                    {
                        if (b == B.Attack || b == B.Ability) _keyAimPressed = true;
                        KeyboardHand? hand = ControlBindings.HandOf(ctx.action, ctx.control);
                        if (hand.HasValue) ControlBindings.SetHand(hand.Value);
                    }
                    else if (ctx.control != null && ctx.control.device is Mouse && (b == B.Attack || b == B.Ability))
                    {
                        // A click aims with the mouse at once, even when the press lands in a frame Sample does not see.
                        ControlBindings.NoteMouseUsed();
                        Aim.NotePointer();
                    }
                    State.ButtonDown(b);
                };
                _canceled[i] = _ => State.ButtonUp(b);
                a.started += _started[i];
                a.canceled += _canceled[i];
            }
        }

        /// <summary>b14 (owner decision: the class ability KEEPS B; Left Shift was bound to two actions): Dodge is Left Shift
        /// (mouse and W A S D hands) and Right Ctrl (Arrows hand), and the controller's right bumper. Walk to pointer's second
        /// key moved to Y and the Arrows layout's lock facing to Left Ctrl; the Builder's next-tool button is the left bumper
        /// alone. ControlsSettingsTests.TheDefaultsHaveNoConflicts checks every slot.</summary>
        public static readonly string[] DodgeDefaults = { "<Keyboard>/leftShift", "<Keyboard>/rightCtrl", "<Gamepad>/rightShoulder" };

        /// <summary>CM b13 J: adds the Dodge action with DodgeDefaults when the actions asset lacks it (in memory only) and
        /// moves the east face button off the ability. A no-op once the asset carries Dodge (the routed asset patch).</summary>
        public static void EnsureDodgeAction(InputActionMap map)
        {
            if (map == null || map.FindAction("Dodge", false) != null) return;
            bool was = map.enabled;
            if (was) map.Disable();
            InputAction d = map.AddAction("Dodge", InputActionType.Button);
            d.AddBinding(DodgeDefaults[0], groups: "KeyboardMouse");
            d.AddBinding(DodgeDefaults[1], groups: "KeyboardMouse");
            d.AddBinding(DodgeDefaults[2], groups: "Gamepad");
            if (was) map.Enable();
        }

        /// <summary>b16 CANCEL defaults, in the Controls tab's slot order: mouse forward side button (mouse column), P (by J K L),
        /// \ (by Enter and the arrows), LB (controller; shared by context with Next tool, ControlSchemes.ContextPairs). Free in
        /// every preset: the left-handed one puts Jump on Right Shift and Ping on ; (ControlPresets.LeftHandedRemaps).</summary>
        public static readonly string[] CancelDefaults = { "<Mouse>/forwardButton", "<Keyboard>/p", "<Keyboard>/backslash", "<Gamepad>/leftShoulder" };

        /// <summary>b16: adds the Cancel action with CancelDefaults when the actions asset lacks it (in memory only).</summary>
        public static void EnsureCancelAction(InputActionMap map)
        {
            if (map == null || map.FindAction("Cancel", false) != null) return;
            bool was = map.enabled;
            if (was) map.Disable();
            InputAction c = map.AddAction("Cancel", InputActionType.Button);
            c.AddBinding(CancelDefaults[0], groups: "KeyboardMouse");
            c.AddBinding(CancelDefaults[1], groups: "KeyboardMouse").WithName(ControlBindings.WasdHand);
            c.AddBinding(CancelDefaults[2], groups: "KeyboardMouse").WithName(ControlBindings.ArrowsHand);
            c.AddBinding(CancelDefaults[3], groups: "Gamepad");
            if (was) map.Enable();
        }

        private void ReadSettings()
        {
            State.MovementAssist = GameSettings.MovementAssist;
            State.KeysFollowMap = GameSettings.KeysFollowMap;
            State.CancelButton = GameSettings.CancelButton;   // b16
        }

        private void OnDisable()
        {
            GameSettings.Changed -= ReadSettings;
            State.BindWorld(null);
            if (BuilderToolbelt.Local == State.Toolbelt) BuilderToolbelt.Local = null;
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
                float sv = _switchDigPlace.ReadValue<float>();
                bool active = Mathf.Abs(sv) > 0.5f;
                // 2026-11-12: the wheel (and its bumpers) zoom the full map while it is open and the spectator camera
                // while spectating; only in play does it switch dig / place (InputContext). b14: wheel up, R / I and the
                // left bumper step the Builder's tool wheel forward, wheel down steps it back.
                if (active && !_switchWasActive && InputContext.DigSwitchOwnsWheel(InputContext.Current)) State.SwitchDigPlace(sv > 0f ? 1 : -1);
                _switchWasActive = active;
            }
            // b14: number keys 1 to 7 pick a Builder tool directly (BuilderTool order); ignored for every other class.
            Keyboard kbd = Keyboard.current;
            if (kbd != null && !Online.OnlineChatHud.EatsKeys && InputContext.DigSwitchOwnsWheel(InputContext.Current))
            {
                if (kbd.digit1Key.wasPressedThisFrame) State.PickBuilderTool(0);
                else if (kbd.digit2Key.wasPressedThisFrame) State.PickBuilderTool(1);
                else if (kbd.digit3Key.wasPressedThisFrame) State.PickBuilderTool(2);
                else if (kbd.digit4Key.wasPressedThisFrame) State.PickBuilderTool(3);
                else if (kbd.digit5Key.wasPressedThisFrame) State.PickBuilderTool(4);
                else if (kbd.digit6Key.wasPressedThisFrame) State.PickBuilderTool(5);
                else if (kbd.digit7Key.wasPressedThisFrame) State.PickBuilderTool(6);
            }
            State.Toolbelt.Actor = LocalSeat.Actor;
            bool fromKeys = m.sqrMagnitude > 0f && _move.activeControl != null && _move.activeControl.device is Keyboard;
            State.SetMove(m.x, m.y, fromKeys);
            bool arrowsMove = false;
            if (fromKeys)
            {
                KeyboardHand? hand = ControlBindings.HandOf(_move, _move.activeControl);
                if (hand.HasValue) ControlBindings.SetHand(hand.Value);
                // controls-1 (owner 2026-11-13): moving on the arrows means the right hand is on the arrows, not the mouse,
                // so the prompts and the tutorial switch to Z X C at once (not after the first attack). Not in the
                // LEFT-HANDED layout, where the mouse is in the left hand while the right hand moves on the arrows.
                arrowsMove = hand == KeyboardHand.Arrows && !ControlPresets.LeftHandedActive;
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
            else if ((keyAim || (arrowsMove && Aim.Current == AimResolver.Mode.Pointer)) && ControlBindings.MouseIdleFor >= ControlBindings.MouseIdleSeconds)
            {
                Aim.NoteKeys();
                ControlBindings.SetKeyboardOnly(true);
            }
            else if (fromKeys && Aim.Current == AimResolver.Mode.Stick)
            {
                // Back from the controller to the keys: the keyboard scheme the player had before.
                Aim.SetMode(ControlBindings.KeyboardOnly ? AimResolver.Mode.Keys : AimResolver.Mode.Pointer);
            }

            // Walk to pointer acts only while the mouse aims; lock facing only while the keys aim (b14: they no longer share a key).
            bool walk = _walk != null && _walk.IsPressed() && Aim.Current == AimResolver.Mode.Pointer;
            State.SetPointerWalk(walk);
            FacingLocked = _lock != null && _lock.IsPressed() && Aim.Current == AimResolver.Mode.Keys;

            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            State.BindWorld(w);
            // feat/ground-camera (experimental CAMERA: GROUND): the keys and the left stick move relative to the ground camera,
            // and the aim is the point ahead along its facing at the reticle's ground distance. Same InputFrame fields as
            // ever (a world-space move and a ground aim point): no Sim change, determinism untouched.
            if (CriticalMass.Runtime.Camera.GroundView.PlayActive && w != null && w.ActorCount > 0)
            {
                CriticalMass.Runtime.Camera.GroundCamera.Basis(CriticalMass.Runtime.Camera.GroundView.PlayYaw,
                    out double gux, out double guz, out double grx, out double grz);
                State.SetCameraBasis(gux, guz, grx, grz);
                State.SetPointerWalk(false);
                State.ClearWalkTarget();
                FacingLocked = false;
                if (CriticalMass.Runtime.Camera.GroundView.TryPlayAim(w, out double gax, out double gaz)) State.SetAim(gax, gaz);
                else State.ClearAim();
                return;
            }
            State.ClearCameraBasis();
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
                    bool alive = w.Actors[LocalSeat.Actor].Alive;
                    if (alive && !_wasAlive) Aim.ForgetKeysDirection();
                    _wasAlive = alive;
                    State.MoveDirection(out double dx, out double dz);
                    int n = CollectEnemies(w);
                    Aim.ResolveKeys(dx, dz, w.X[LocalSeat.Actor], w.Z[LocalSeat.Actor], w.Actors[LocalSeat.Actor].Facing, FacingLocked, _enemyX, _enemyZ, n, out double kx, out double kz);
                    State.SetAim(kx, kz);
                }
                else State.ClearAim();
                return;
            }
            if (Aim.Current == AimResolver.Mode.Stick)
            {
                if (w != null && w.ActorCount > 0 && Aim.ResolveStick(stick.x, stick.y, m.x, m.y, w.X[LocalSeat.Actor], w.Z[LocalSeat.Actor], out double ax, out double az))
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
            var me = w.Actors[LocalSeat.Actor].Team;
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
            double hx = map != null ? w.X[LocalSeat.Actor] : 0, hz = map != null ? w.Z[LocalSeat.Actor] : 0;
            return GroundHeight.TryAimPoint(ray, map, hx, hz, out sx, out sz);
        }

        private void OnApplicationFocus(bool f)
        {
            if (!f) State.Blur();
        }
    }
}

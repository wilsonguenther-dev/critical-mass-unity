using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
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
        // Tier F31 / F34: the aiming device follows the most recent real input; untouched frames resend the held raw aim.
        private readonly AimDeviceArbiter _devices = new AimDeviceArbiter();
        private readonly AimHold _hold = new AimHold();
        private bool _touched;
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

        /// <summary>Fix round 5 (item 9): the actions every seat needs (Dodge / Cancel are added when missing).</summary>
        public static readonly string[] RequiredActions = { "Move", "Point", "Jump", "Attack", "Ability", "Interact", "DigPlace", "Pause", "Scoreboard" };
        /// <summary>Errors logged for a missing or empty input-actions asset (one per run; tests).</summary>
        public static int MissingActionsErrors;
        static bool _missingLogged;

        /// <summary>Fix round 5 (item 9, pure; tests): null when the asset can drive a seat, else why not.</summary>
        public static string ActionsProblem(InputActionAsset asset)
        {
            if (asset == null) return "no project-wide input actions asset (InputSystem.actions is null)";
            InputActionMap map = asset.FindActionMap("Player", false);
            if (map == null) return "the input actions asset '" + asset.name + "' has no Player map";
            foreach (string a in RequiredActions) if (map.FindAction(a, false) == null) return "the Player map has no '" + a + "' action";
            return null;
        }

        private void OnEnable()
        {
            // fix round 5 (item 9): a missing or empty input-actions asset no longer throws out of the match build: ONE
            // error, this seat hands itself to the autopilot, and the match goes on.
            string problem = ActionsProblem(InputSystem.actions);
            if (problem != null)
            {
                if (!_missingLogged) { _missingLogged = true; MissingActionsErrors++; Debug.LogError("[input] " + problem + ": the local seat plays on autopilot this match."); }
                SimDriverBehaviour driver = GetComponent<SimDriverBehaviour>();
                if (driver == null) driver = FindFirstObjectByType<SimDriverBehaviour>();
                if (driver != null) driver.HumanAutopilot = true;
                _unusable = true;
                enabled = false;
                return;
            }
            _unusable = false;
            // Review defect 0: one owner for the map's life. Dodge / Cancel are added (old assets only) with the whole asset
            // disabled, nothing is re-created that exists, and OnDisable gives the map back (PlayerActionsLifetime).
            var map = PlayerActionsLifetime.Acquire();
            if (map == null) map = InputSystem.actions.FindActionMap("Player", true);
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
            _devices.Reset();
            _hold.Reset();
            _touched = false;
            ReadSettings();
            GameSettings.Changed += ReadSettings;
            AimHelpSettings.Changed += ReadSettings;
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
                        // The prompts learn the mouse is in the hand; the AIM does not move to a resting cursor on a click
                        // (tier F31): AimDeviceArbiter hands the aim over only on a real mouse move.
                        ControlBindings.NoteMouseUsed();
                    }
                    // Lane I item 2: keyboard alone, a Farmer or Healer holds the ability key to choose the distance; the
                    // press reaches the sim on release (a tap shorter than CastDistance.TapSeconds casts at the default).
                    if (b == B.Ability && ctx.control != null && ctx.control.device is Keyboard && Aim.Current == AimResolver.Mode.Keys && LocalClassHoldsToCast())
                    {
                        _castHolding = true;
                        _castRelease = false;
                        _castDownAt = Time.unscaledTimeAsDouble;
                        return;
                    }
                    State.ButtonDown(b);
                };
                _canceled[i] = _ =>
                {
                    if (b == B.Ability && _castHolding)
                    {
                        _castHolding = false;
                        _castHeldFor = Time.unscaledTimeAsDouble - _castDownAt;
                        _castRelease = true;
                        State.ButtonDown(B.Ability);
                    }
                    State.ButtonUp(b);
                };
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

        bool LocalClassHoldsToCast()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            int me = LocalSeat.Actor;
            return w != null && me >= 0 && me < w.ActorCount && w.Actors[me].Alive && HoldToCast(w.Actors[me].Cls);
        }

        private void ReadSettings()
        {
            _levelsRead = false;   // lane I: AIM HELP rows re-read on the next frame
            State.MovementAssist = GameSettings.MovementAssist;
            State.KeysFollowMap = GameSettings.KeysFollowMap;
            State.CancelButton = GameSettings.CancelButton;   // b16
        }

        bool _unusable;
        /// <summary>Fix round 6: this reader turned itself off on purpose (missing / empty input actions, seat on autopilot);
        /// the lockout watchdog must not "repair" it (that was a second red error).</summary>
        public bool ActionsMissing => _unusable;

        private void OnDisable()
        {
            if (_unusable) return;   // fix round 5 (item 9): OnEnable bound nothing
            GameSettings.Changed -= ReadSettings;
            AimHelpSettings.Changed -= ReadSettings;
            _castHolding = _castRelease = false;
            _assist.Reset();
            AimAssist.Clear();
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
            PlayerActionsLifetime.Release(_map);
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
            if (kbd != null && !Online.OnlineChatHud.EatsKeys && !CriticalMass.Runtime.Hud.Mechanics.StationPanel.IsOpen && InputContext.DigSwitchOwnsWheel(InputContext.Current))
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
            // Tier F31: the device used most recently aims (a mouse MOVE, a deflected stick, the move keys or a keyboard attack
            // key); a resting cursor never keeps the aim from a keys player (AimDeviceArbiter.PointerHoldSeconds after its
            // last move it lets go). Fix round 7: a mouse BUTTON press is aim input (it aims at the cursor, see below).
            double nowT = Time.unscaledTimeAsDouble;
            Mouse mouse = Mouse.current;
            Vector2 md = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            bool pointerMoved = AimDeviceArbiter.PointerMoved(md.x, md.y) || (_walk != null && _walk.WasPressedThisFrame());
            bool stickUsed = stick.sqrMagnitude > 0f || moveByPad;
            if (stickUsed) _devices.NoteStick(nowT);
            if (pointerMoved) _devices.NotePointerMoved(nowT);
            // fix round 7: a mouse button press aims at the cursor for that press (noted before the keys, so a tie goes to it)
            bool mousePressed = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                || mouse.middleButton.wasPressedThisFrame || mouse.forwardButton.wasPressedThisFrame || mouse.backButton.wasPressedThisFrame);
            if (mousePressed) _devices.NotePointerPressed(nowT);
            if (fromKeys || keyAim) _devices.NoteKeys(nowT, keyAim);
            AimResolver.Mode aimWas = Aim.Current;
            AimResolver.Mode aimNow = _devices.Decide(nowT, aimWas);
            if (aimNow != aimWas)
            {
                Aim.SetMode(aimNow);
                if (aimNow == AimResolver.Mode.Keys) ControlBindings.SetKeyboardOnly(true);
            }
            // Tier F34: a frame is "touched" only when the player moved a device or pressed something this frame.
            Gamepad padNow = Gamepad.current;
            _touched = pointerMoved || stickUsed || m.sqrMagnitude > 0f
                       || (kbd != null && kbd.anyKey.wasPressedThisFrame)
                       || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                           || mouse.middleButton.wasPressedThisFrame || mouse.scroll.ReadValue().sqrMagnitude > 0f))
                       || (padNow != null && ControlBindings.GamepadUsed(padNow));

            // Walk to pointer acts only while the mouse aims; lock facing only while the keys aim (b14: they no longer share a key).
            bool walk = _walk != null && _walk.IsPressed() && Aim.Current == AimResolver.Mode.Pointer;
            State.SetPointerWalk(walk);
            FacingLocked = _lock != null && _lock.IsPressed() && Aim.Current == AimResolver.Mode.Keys;

            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            State.BindWorld(w);
            int me = LocalSeat.Actor;
            bool seated = w != null && w.ActorCount > me && me >= 0;
            if (_castRelease && !State.AbilityIntent) _castRelease = false;
            // b16 / lane I: tell the HUD which job LB does right now (Cancel inside your own swing window, else Next tool).
            InputContext.PadLbCancels = seated && State.CancelButton && CombatFeel.On(w) && CombatFeel.CancelWindow(w, me);
            // feat/ground-camera (experimental CAMERA: GROUND): the keys and the left stick move relative to the ground camera,
            // and the aim is the point ahead along its facing at the reticle's ground distance. Same InputFrame fields as
            // ever (a world-space move and a ground aim point): no Sim change, determinism untouched.
            if (CriticalMass.Runtime.Camera.GroundView.PlayActive && seated && w.ActorCount > 0)
            {
                CriticalMass.Runtime.Camera.GroundCamera.Basis(CriticalMass.Runtime.Camera.GroundView.PlayYaw,
                    out double gux, out double guz, out double grx, out double grz);
                State.SetCameraBasis(gux, guz, grx, grz);
                State.SetPointerWalk(false);
                State.ClearWalkTarget();
                FacingLocked = false;
                if (CriticalMass.Runtime.Camera.GroundView.TryPlayAim(w, out double gax, out double gaz)) State.SetAim(gax, gaz);
                else State.ClearAim();
                // Lane I: the auto-follow camera lags the travel, so a keys or one-stick player's intent is the move direction.
                bool manual = GameSettings.CameraManual;
                AimDevice dev = Aim.Current == AimResolver.Mode.Stick ? AimDevice.Controller : manual ? AimDevice.Mouse : AimDevice.Keys;
                State.MoveDirection(out double ix, out double iz);
                FinishAim(w, dev, true, !manual && State.MoveHeld, ix, iz);
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
                if (seated && w.ActorCount > 0)
                {
                    // A respawn faces the hill (Respawn.cs); the keyboard facing starts again from there.
                    bool alive = w.Actors[me].Alive;
                    if (alive && !_wasAlive) Aim.ForgetKeysDirection();
                    _wasAlive = alive;
                    State.MoveDirection(out double dx, out double dz);
                    // Lane I: no built-in 30 degree snap any more (it skipped actor 0); AimAssistBrain below does it for every
                    // device, with the KEYS strength setting.
                    Aim.ResolveKeys(dx, dz, w.X[me], w.Z[me], w.Actors[me].Facing, FacingLocked, null, null, 0, out double kx, out double kz);
                    // Item 2: hold the ability key to throw farther (release casts); a tap casts at the default.
                    if (_castHolding || _castRelease)
                    {
                        double held = _castHolding ? Time.unscaledTimeAsDouble - _castDownAt : _castHeldFor;
                        if (held < CastDistance.TapSeconds) held = 0;
                        ScaleAim(w.X[me], w.Z[me], ref kx, ref kz, CastDistance.FromHold(held, AbilityMaxRange(w, me)));
                    }
                    State.SetAim(kx, kz);
                }
                else State.ClearAim();
                FinishAim(w, AimDevice.Keys, false, false, 0, 0);
                return;
            }
            if (Aim.Current == AimResolver.Mode.Stick)
            {
                if (seated && w.ActorCount > 0 && Aim.ResolveStick(stick.x, stick.y, m.x, m.y, w.X[me], w.Z[me], out double ax, out double az))
                {
                    // Item 2: with the right stick pushed, its deflection picks the cast distance (1 u .. the ability's range).
                    float mag = stick.magnitude;
                    if (mag > 0f) ScaleAim(w.X[me], w.Z[me], ref ax, ref az, CastDistance.FromStick(Math.Min(1.0, mag), AbilityMaxRange(w, me)));
                    State.SetAim(ax, az);
                }
                else State.ClearAim();
                FinishAim(w, AimDevice.Controller, false, false, 0, 0);
                return;
            }

            if (Mouse.current == null || cam == null)
            {
                State.ClearAim();
                FinishAim(w, AimDevice.Mouse, false, false, 0, 0);
                return;
            }
            if (PointerGround(cam, w, out double sx, out double sz)) State.SetAim(sx, sz);
            else State.ClearAim();
            FinishAim(w, AimDevice.Mouse, false, false, 0, 0);
        }

        // ---- Lane I (player-experience wave 2026-11-22): aim help in every view and device ----

        readonly AimAssistBrain _assist = new AimAssistBrain();
        AimCandidate[] _cand = new AimCandidate[64];
        double _lastAssistTime = -1, _lastRawX, _lastRawZ, _lastRawInputAt = -100;
        bool _levelsRead;
        readonly AimHelpLevel[] _levels = new AimHelpLevel[3];
        // Item 2 (keys): the ability key is being held to choose a distance; released and waiting for the next step.
        bool _castHolding, _castRelease;
        double _castDownAt, _castHeldFor;

        /// <summary>The assist brain (tests).</summary>
        public AimAssistBrain Assist => _assist;

        void ReadLevels()
        {
            _levels[0] = AimHelpSettings.Get(AimDevice.Mouse);
            _levels[1] = AimHelpSettings.Get(AimDevice.Controller);
            _levels[2] = AimHelpSettings.Get(AimDevice.Keys);
            _levelsRead = true;
        }

        /// <summary>The aim point (ax, az) moved to dist from (px, pz) along its own direction.</summary>
        static void ScaleAim(double px, double pz, ref double ax, ref double az, double dist)
        {
            double dx = ax - px, dz = az - pz, l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-9) return;
            ax = px + dx / l * dist;
            az = pz + dz / l * dist;
        }

        /// <summary>The farthest useful aim for the class's aimed ability (roots, pulse, TNT); the stick default otherwise.</summary>
        public static double AbilityMaxRange(World w, int id)
        {
            if (w == null || w.Config == null || id < 0 || id >= w.ActorCount) return AimResolver.StickAimDistance;
            switch (w.Actors[id].Cls)
            {
                case ClassId.Farmer: return w.Config.Abilities.FarmerRoots.CastRange;
                case ClassId.Healer: return w.Config.Abilities.Healer.PulseCastRange;
                case ClassId.Demolitions: return w.Config.Abilities.DemolitionsTnt.ThrowRange;
                default: return AimResolver.StickAimDistance;
            }
        }

        /// <summary>Classes whose ability key, on the keyboard alone, is held to choose a distance and cast on release.</summary>
        public static bool HoldToCast(ClassId c) => c == ClassId.Farmer || c == ClassId.Healer;

        /// <summary>
        /// Runs after every aim branch: bends State's aim toward the assist candidate (AimAssistBrain) and publishes this
        /// frame's AimAssist statics for the HUD and the camera. Never moves the camera. Off = State's aim untouched.
        /// </summary>
        void FinishAim(World w, AimDevice dev, bool thirdPerson, bool hasIntent, double ix, double iz)
        {
            int me = LocalSeat.Actor;
            // Tier F34: with no device input this frame the raw aim is the held one, relative to the body (a moving camera,
            // a sliding cursor projection or a push never reads as a player to the sim's idle rule).
            if (w != null && me >= 0 && me < w.ActorCount)
            {
                bool hold = State.HasAim;
                double hx = State.AimX, hz = State.AimZ;
                _hold.Apply(_touched, w.X[me], w.Z[me], ref hold, ref hx, ref hz);
                if (hold) State.SetAim(hx, hz);
                else State.ClearAim();
            }
            AimAssist.RawHasAim = State.HasAim;
            AimAssist.RawAimX = State.AimX;
            AimAssist.RawAimZ = State.AimZ;
            if (w == null || w.Config == null || me < 0 || me >= w.ActorCount || !w.Actors[me].Alive || !State.HasAim || State.Paused)
            {
                _assist.Reset();
                AimAssist.Clear();
                // Review item 3: Clear drops RawHasAim; the raw aim stays published on this path too (lane C caps the
                // camera lean with it every frame, in every branch).
                AimAssist.RawHasAim = State.HasAim;
                AimAssist.RawAimX = State.AimX;
                AimAssist.RawAimZ = State.AimZ;
                AimAssist.HasAim = State.HasAim;
                AimAssist.AimX = State.AimX;
                AimAssist.AimZ = State.AimZ;
                return;
            }
            if (!_levelsRead) ReadLevels();
            AimHelpLevel level = _levels[(int)dev];
            AimAssistTuning tun = AimAssistTuning.For(level);
            // The idle pull only follows real input (Idle rule for lane S2: an untouched aim stays constant).
            double rdx = State.AimX - _lastRawX, rdz = State.AimZ - _lastRawZ;
            double tnow = Time.unscaledTimeAsDouble;
            if (State.MoveHeld || rdx * rdx + rdz * rdz > AimAssistTunables.IdleInputMoveUnits * AimAssistTunables.IdleInputMoveUnits) _lastRawInputAt = tnow;
            _lastRawX = State.AimX;
            _lastRawZ = State.AimZ;
            if (tnow - _lastRawInputAt > AimAssistTunables.IdlePullNeedsInputSeconds) tun.IdleBend = 0;
            GameConfig cfg = w.Config;
            Actor self = w.Actors[me];
            double px = w.X[me], pz = w.Z[me];
            int n = BuildCandidates(w, me, px, pz);
            LightWeapon lw = LightWeapons.Of(w, self.Cls);
            ClassConfig cc = cfg.Class(self.Cls);
            bool tnt = CombatFeel.On(w) && self.Cls == ClassId.Demolitions && cfg.Abilities.DemolitionsTnt.ThrowOnAttack && w.Tick >= w.Demolitions.TntReadyTick[me];
            bool abilityPress = (State.AbilityIntent || _castHolding || _castRelease) && self.Cls == ClassId.Farmer && !State.AttackIntent;
            double now = Time.unscaledTimeAsDouble;
            double dt = _lastAssistTime >= 0 ? Math.Max(0, Math.Min(0.25, now - _lastAssistTime)) : 0;
            _lastAssistTime = now;
            var f = new AimAssistFrame
            {
                Px = px, Pz = pz, Facing = self.Facing, HasRaw = true, RawX = State.AimX, RawZ = State.AimZ,
                HasIntent = hasIntent, IntentX = ix, IntentZ = iz,
                Attacking = State.AttackIntent || self.SwingPending || abilityPress,
                Dt = dt,
                Kind = abilityPress ? AimPressKind.Ability : tnt ? AimPressKind.Tnt : self.Cls == ClassId.Healer ? AimPressKind.Healer : AimPressKind.Melee,
                Reach = abilityPress ? cfg.Abilities.FarmerRoots.CastRange : lw != null ? lw.Range : cc.AttackRange,
                ArcDeg = lw != null ? lw.ArcDeg : cc.AttackArcDeg,
                TntRange = cfg.Abilities.DemolitionsTnt.ThrowRange + cfg.Abilities.DemolitionsTnt.BlastRadius,
                TntLead = Math.Min(cfg.Abilities.DemolitionsTnt.FuseTicks / cfg.SimHz, AimAssistTunables.TntLeadMaxSeconds),
                MendRange = CombatFeel.MendRange(cfg),
                ThirdPerson = thirdPerson,
            };
            _assist.Step(f, _cand, n, tun, out double fx, out double fz);
            if (_assist.Lock >= 0) State.SetAim(fx, fz);
            int lockId = _assist.Lock;
            AimAssist.LockTarget = lockId >= 0 && !_assist.LockIsAlly ? lockId : -1;
            AimAssist.AllyTarget = lockId >= 0 && _assist.LockIsAlly ? lockId : -1;
            AimAssist.HasAim = State.HasAim;
            AimAssist.AimX = State.AimX;
            AimAssist.AimZ = State.AimZ;
            AimAssist.Strength = level == AimHelpLevel.Off ? 0f : level == AimHelpLevel.Low ? 0.5f : 1f;
            AimAssist.Device = dev;
            if (_assist.JustEngaged && AimAssist.LockTarget >= 0) PlayerFeedback.Report(FeedbackChannel.AimAssistLock, AimAssist.LockTarget, AimAssist.Strength);
            // The hittable cue: the nearest enemy a swing along the facing this aim gives would hit (the sim's own test).
            int hit = -1;
            if (!tnt && !abilityPress)
            {
                double facing = AimHitTest.FacingFor(px, pz, State.AimX, State.AimZ, self.Facing);
                int k = AimHitTest.Nearest(px, pz, facing, f.Reach, f.ArcDeg, Phys.On(w, cfg.Physics.ArcCoversBody), Phys.On(w, cfg.Physics.WallsBlockHits), _cand, n);
                hit = k >= 0 ? _cand[k].Id : -1;
            }
            AimAssist.HittableTarget = hit;
        }

        /// <summary>Every other living actor as an assist candidate (enemies and allies; actor 0 included, the local seat
        /// skipped), with the fog rule and the sim's wall test (read-only).</summary>
        int BuildCandidates(World w, int me, double px, double pz)
        {
            if (_cand.Length < w.ActorCount) _cand = new AimCandidate[w.ActorCount];
            TeamId team = w.Actors[me].Team;
            int n = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (i == me) continue;
                Actor a = w.Actors[i];
                if (a == null || !a.Alive) continue;
                bool enemy = a.Team != team;
                _cand[n++] = new AimCandidate
                {
                    Id = i, X = w.X[i], Z = w.Z[i], Vx = w.Vx[i], Vz = w.Vz[i], Radius = w.Config.Class(a.Cls).Radius,
                    Enemy = enemy, Alive = true,
                    Hidden = enemy && CriticalMass.Runtime.View.Fog.FogOfWar.Hides(i),
                    ClearLine = SegmentClear.Clear(w.Map, px, pz, w.X[i], w.Z[i]),
                    Hurt = a.Hp < a.MaxHp,
                };
            }
            return n;
        }

        /// <summary>Living actors of the other team, positions copied (read-only). Lane I: from actor 0, skipping the local seat
        /// (it used to start at 1, so an enemy in seat 0 was never assisted online).</summary>
        private int CollectEnemies(World w)
        {
            if (_enemyX.Length < w.ActorCount)
            {
                _enemyX = new double[w.ActorCount];
                _enemyZ = new double[w.ActorCount];
            }
            int me = LocalSeat.Actor;
            var team = w.Actors[me].Team;
            int n = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (i == me) continue;
                var a = w.Actors[i];
                if (a == null || !a.Alive || a.Team == team) continue;
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
            if (!f) { State.Blur(); _castHolding = _castRelease = false; }
        }
    }
}

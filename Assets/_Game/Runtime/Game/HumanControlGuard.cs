using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// The player always has control in a match (owner, 2026-11-12: "it's just playing by itself ... I can't even move";
    /// fix/human-control). One owner for who drives actor 0, running after HumanInputSource (-100) samples the devices
    /// and BEFORE SimDriverBehaviour (0) steps the sim, so whatever it decides reaches the very next tick:
    /// <list type="bullet">
    /// <item>A human match (not the menus' backdrop, not capture, not -autoplay, not spectating) starts with actor 0 under
    /// human control, whatever any earlier screen left in the driver's HumanAutopilot field.</item>
    /// <item>Any gameplay input (key, mouse button, mouse move, scroll, stick push, trigger or pad button) while autopilot
    /// drives actor 0 hands it back on the same frame.</item>
    /// <item>Autopilot takes over only after IdleAutopilotSeconds with no input from a standing, unpaused player.</item>
    /// <item>Watchdog: if gameplay input is shut off for more than LockoutSeconds in a live human match (the Player
    /// action map or its Move action disabled by something other than the controller HUD focus, or the input reader
    /// missing or disabled; autopilot is not a lockout, because any input ends it on the same frame) a loud
    /// "[CM.Control]" error is logged, a red warning is drawn on screen, and the input is switched back on.</item>
    /// <item>Automated runs (-autoplay, the demo smoke and soak tools) draw a permanent plate saying the computer is
    /// playing and that this window is a test, so nobody mistakes a test window for their own game (the 2026-11-12
    /// report was a demo-smoke window).</item>
    /// </list>
    /// PlayerPresence draws the autopilot banner from this component's state.
    /// </summary>
    [GameOverlay(Order = 69, Scope = OverlayScope.Always)]
    [DefaultExecutionOrder(-50)]
    public sealed class HumanControlGuard : MonoBehaviour
    {
        public const string LogTag = "[CM.Control]";
        /// <summary>Seconds without any input before autopilot takes a standing player's fighter (tests shorten it).</summary>
        public static float IdleAutopilotSeconds = DefaultIdleAutopilotSeconds;
        public const float DefaultIdleAutopilotSeconds = 45f;
        /// <summary>false: idle never hands actor 0 to autopilot.</summary>
        public static bool IdleAutopilotEnabled = true;
        /// <summary>Gameplay input shut off this long in a live human match is a defect: logged, shown, repaired.</summary>
        public const float LockoutSeconds = 2f;
        public const int SortingOrder = 99;

        public static HumanControlGuard Current { get; private set; }

        public SimDriverBehaviour Driver { get; private set; }
        public HumanInputSource Input { get; private set; }

        /// <summary>This world is a match a person plays (not backdrop, capture or automated run).</summary>
        public bool HumanMatch { get; private set; }
        /// <summary>Autopilot drives actor 0 because the player was idle (the banner says so).</summary>
        public bool IdleAutopilot { get; private set; }
        /// <summary>The autopilot banner should show: a human match, actor 0 on autopilot, not spectating, not paused.</summary>
        public bool AutopilotBanner { get; private set; }
        /// <summary>Unscaled time of the last gameplay input seen.</summary>
        public float LastInputAt { get; private set; }
        /// <summary>Frame on which input last took control back from autopilot (-1: never).</summary>
        public int TookBackFrame { get; private set; } = -1;
        /// <summary>Why gameplay input is shut off right now (null: it is not).</summary>
        public string LockoutReason { get; private set; }
        /// <summary>The red on-screen lockout warning is showing.</summary>
        public bool LockoutWarningVisible => _warning != null && _warning.activeSelf;
        /// <summary>The automated-run plate is showing.</summary>
        public bool AutomatedPlateVisible => _plate != null && _plate.activeSelf;
        /// <summary>Lockouts reported (and repaired) in this world.</summary>
        public int LockoutsReported { get; private set; }

        float _lockoutSince = -1f;
        bool _lockoutReported;
        bool _startChecked;
        bool _focusLogged;
        Canvas _canvas;
        GameObject _warning, _plate;
        Text _warningText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            IdleAutopilotSeconds = DefaultIdleAutopilotSeconds;
            IdleAutopilotEnabled = true;
        }

        void Awake()
        {
            Current = this;
            Driver = GetComponent<SimDriverBehaviour>();
            Input = GetComponent<HumanInputSource>();
            LastInputAt = Time.unscaledTime;
        }

        void OnEnable()
        {
            SpectatorMode.ControlReturned += OnControlReturned;
        }

        void OnDisable()
        {
            SpectatorMode.ControlReturned -= OnControlReturned;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void OnControlReturned()
        {
            LastInputAt = Time.unscaledTime;
            IdleAutopilot = false;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused || _focusLogged || !AutomatedRun) return;
            _focusLogged = true;
            Debug.Log($"{LogTag} this window is an automated test run (-autoplay) and was just focused: the computer plays it and keys do nothing. It is not the game a person launched.");
        }

        /// <summary>An automated run (-autoplay: the demo smoke and soak tools), not a person's game.</summary>
        public static bool AutomatedRun => GameFlow.Autoplay || AutoplayArgs.Current.Autoplay;

        /// <summary>A world the tools or the menus drive: actor 0 belongs to the bot brain there by design.</summary>
        public static bool ToolWorld(SimDriverBehaviour d) =>
            SimDriverBehaviour.CaptureMode || AutomatedRun || (d != null && d.Request != null && d.Request.Backdrop);

        // ---------------------------------------------------------------- frame

        void Update()
        {
            if (Input == null) Input = GetComponent<HumanInputSource>();
            UpdatePlate();
            World w = Driver != null ? Driver.World : null;
            if (w == null || w.ActorCount <= 0)
            {
                HumanMatch = false;
                AutopilotBanner = false;
                SetWarning(null);
                return;
            }
            bool title = GetComponent<TitleScreen>() != null;
            HumanMatch = !ToolWorld(Driver) && !title;
            bool spectating = SpectatorMode.IsSpectating;
            bool paused = Input != null && Input.State.Paused;
            bool playing = w.Match.Phase == MatchPhase.Playing;
            bool alive = w.Actors[0].Alive;
            bool trial = Onboarding.Trial.IsRunning;
            float now = Time.unscaledTime;

            // A human match starts under human control, whatever an earlier screen or tool left in the driver.
            if (!_startChecked && HumanMatch)
            {
                _startChecked = true;
                // A capture-built world (tests, capture tools) starts on autopilot by design, for a deterministic tick 0.
                bool watchFromSetup = SpectatorMode.SpectateNextMatch;
                if (Driver.HumanAutopilot && !spectating && !watchFromSetup && !Driver.CreatedInCaptureMode)
                {
                    Driver.HumanAutopilot = false;
                    Debug.LogWarning($"{LogTag} a match started with actor 0 on autopilot; control handed to the player.");
                }
                if (!Driver.CreatedInCaptureMode)
                    Debug.Log($"{LogTag} match start: actor 0 is {(watchFromSetup ? "watched (Spectate)" : "under the player's control")}.");
            }

            bool input = AnyGameplayInput();
            // The idle clock does not run behind the loading screen (feat/play-now): Play Now hands the human control on the first frame.
            if (input || !alive || paused || title || spectating || !HumanMatch || Menus.LoadingScreen.IsOpen) LastInputAt = now;

            bool eligible = HumanMatch && !spectating;
            if (eligible && Driver.HumanAutopilot && input)
            {
                // Any input takes control at once: this runs before the driver's Update, so the next tick is the player's.
                Driver.HumanAutopilot = false;
                if (IdleAutopilot) Debug.Log($"{LogTag} input took control back from idle autopilot.");
                else Debug.LogWarning($"{LogTag} input took control back from autopilot that had no idle reason.");
                IdleAutopilot = false;
                TookBackFrame = Time.frameCount;
            }
            else if (eligible && playing && !Driver.HumanAutopilot && IdleAutopilotEnabled && !trial && alive && !paused &&
                     now - LastInputAt >= IdleAutopilotSeconds)
            {
                Driver.HumanAutopilot = true;
                IdleAutopilot = true;
                Debug.Log($"{LogTag} no input for {IdleAutopilotSeconds:0} s: autopilot plays actor 0 until any key or button.");
            }
            if (!Driver.HumanAutopilot) IdleAutopilot = false;
            AutopilotBanner = eligible && playing && Driver.HumanAutopilot && !paused;

            Watch(eligible && playing && !paused && !trial, now);
        }

        /// <summary>
        /// Any key, mouse button, mouse move, scroll, controller button, stick push or trigger this frame (held keys
        /// count). Reads the devices directly, so it works even when an action map is disabled.
        /// </summary>
        public static bool AnyGameplayInput()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed) return true;
            Mouse m = Mouse.current;
            if (m != null && (ControlBindings.MouseUsed(m) || m.leftButton.isPressed || m.rightButton.isPressed ||
                              m.middleButton.isPressed || m.backButton.isPressed || m.forwardButton.isPressed ||
                              Mathf.Abs(m.scroll.ReadValue().y) > 0.01f)) return true;
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                if (ControlBindings.GamepadUsed(pad)) return true;
                float t = ControlBindings.StickUseThreshold;
                if (pad.leftStick.ReadValue().sqrMagnitude > t * t || pad.rightStick.ReadValue().sqrMagnitude > t * t) return true;
                if (pad.leftTrigger.ReadValue() > 0.5f || pad.rightTrigger.ReadValue() > 0.5f) return true;
                if (pad.dpad.ReadValue().sqrMagnitude > 0.25f) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- watchdog

        void Watch(bool live, float now)
        {
            string reason = live && !Menus.LoadingScreen.IsOpen ? FindLockout() : null;
            LockoutReason = reason;
            if (reason == null)
            {
                _lockoutSince = -1f;
                _lockoutReported = false;
                SetWarning(null);
                return;
            }
            if (_lockoutSince < 0f) _lockoutSince = now;
            if (now - _lockoutSince < LockoutSeconds) return;
            if (!_lockoutReported)
            {
                _lockoutReported = true;
                LockoutsReported++;
                Debug.LogError($"{LogTag} LOCKOUT: gameplay input has been off for {now - _lockoutSince:0.0} s in a live match ({reason}). Switching it back on.");
            }
            SetWarning("INPUT WAS OFF FOR " + (now - _lockoutSince).ToString("0") + " S (" + reason.ToUpperInvariant() + "). TURNED BACK ON. PLEASE REPORT.");
            Repair();
        }

        /// <summary>Why gameplay input is shut off (null when it is not). Pure read.</summary>
        string FindLockout()
        {
            if (Input == null || !Input.isActiveAndEnabled) return "input reader off";
            InputActionMap map = InputSystem.actions != null ? InputSystem.actions.FindActionMap("Player", false) : null;
            if (map == null) return "no Player action map";
            if (!map.enabled) return "Player action map disabled";
            InputAction move = map.FindAction("Move", false);
            HudInteract hud = GetComponent<HudInteract>();
            bool padFocus = hud != null && hud.PadHudFocus;
            if (move != null && !move.enabled && !padFocus) return "Move action disabled";
            return null;
        }

        /// <summary>Switches gameplay input back on (the watchdog only acts after LockoutSeconds).</summary>
        void Repair()
        {
            if (Input != null && !Input.enabled) Input.enabled = true;
            InputActionMap map = InputSystem.actions != null ? InputSystem.actions.FindActionMap("Player", false) : null;
            if (map != null && !map.enabled) map.Enable();
            InputAction move = map != null ? map.FindAction("Move", false) : null;
            HudInteract hud = GetComponent<HudInteract>();
            if (move != null && !move.enabled && !(hud != null && hud.PadHudFocus)) move.Enable();
        }

        // ---------------------------------------------------------------- view

        void Build()
        {
            if (_canvas != null) return;
            _canvas = FeedUi.MakeCanvas(transform, "Human Control Canvas", SortingOrder);
            Color cream = CMTokens.WarmCream.Color;

            RectTransform warn = FeedUi.Anchored(_canvas.transform, "Lockout Warning", new Vector2(0.5f, 1f), 0f, -90f, 1200f, 48f);
            _warning = warn.gameObject;
            VectorShape wp = MenuUi.Shape(warn, "Plate", ShapeKind.RoundRect, 0f, 0f, 1200f, 48f, new Color(0.62f, 0.08f, 0.08f, 0.95f), 12f);
            wp.OutlinePx = 3f;
            _warningText = MenuUi.Label(warn, "Text", CMTokens.Ui, 20, cream, TextAnchor.MiddleCenter, 0f, 0f, 1200f, 48f, "", 2f);
            _warning.SetActive(false);

            RectTransform plate = FeedUi.Anchored(_canvas.transform, "Automated Run Plate", new Vector2(0.5f, 0f), 0f, 24f, 1240f, 56f);
            _plate = plate.gameObject;
            VectorShape pp = MenuUi.Shape(plate, "Plate", ShapeKind.RoundRect, 0f, 0f, 1240f, 56f, CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.92f), 14f);
            pp.OutlinePx = 3f;
            pp.RimPx = 3f;
            pp.RimColor = CMTokens.ToolBrass.Color;
            MenuUi.Label(plate, "Text", CMTokens.Ui, 22, cream, TextAnchor.MiddleCenter, 0f, 0f, 1240f, 56f,
                "AUTOMATED TEST RUN: THE COMPUTER IS PLAYING THIS WINDOW. YOUR KEYS DO NOTHING HERE. IT CLOSES BY ITSELF.", 2f);
            _plate.SetActive(false);
        }

        void SetWarning(string text)
        {
            bool on = text != null;
            if (!on && _warning == null) return;
            Build();
            if (on) HudText.Set(_warningText, text);
            if (_warning.activeSelf != on) _warning.SetActive(on);
        }

        void UpdatePlate()
        {
            bool on = AutomatedRun && !SimDriverBehaviour.CaptureMode;
            if (!on && _plate == null) return;
            Build();
            if (_plate.activeSelf != on) _plate.SetActive(on);
        }
    }
}

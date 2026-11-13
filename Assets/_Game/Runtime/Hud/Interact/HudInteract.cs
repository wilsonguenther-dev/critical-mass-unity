using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Hud.Interact
{
    /// <summary>
    /// The full map's public API for other items (game flow, onboarding, tests): Show, Hide, Toggle, IsOpen and OpenChanged.
    /// </summary>
    public static class FullMap
    {
        public static bool IsOpen => HudInteract.Instance != null && HudInteract.Instance.MapOpen;
        public static void Show() { if (HudInteract.Instance != null) HudInteract.Instance.OpenMap(); }
        public static void Hide() { if (HudInteract.Instance != null) HudInteract.Instance.CloseMap(); }
        public static void Toggle() { if (IsOpen) Hide(); else Show(); }
        /// <summary>Raised with true on open and false on close.</summary>
        public static event Action<bool> OpenChanged;
        internal static void Raise(bool open) => OpenChanged?.Invoke(open);
    }

    /// <summary>
    /// The interactive layer over the HUD (one canvas, sorting 14: above the HUD and kill feed, below class select, the
    /// scoreboard and the pause menu): the full map (M / D-pad up, or a click on the minimap), pings (hold G / D-pad left
    /// for the wheel, in the world or on the map), the minimap's peek and zoom (N / D-pad right), and the clickable HUD
    /// (hover or focus tooltips, clicks; hold Alt, or press D-pad down on a controller, to free the cursor without
    /// attacking). Reads the sim, never writes it; the InputGate keeps HUD and map input away from the sim's attack.
    /// Installed by InteractInstaller on the game object GameBootstrap builds (the DG-01 overlay registry is not on main;
    /// when it lands this class can take [GameOverlay(Scope = OverlayScope.MatchOnly)] instead of the installer).
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 58, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]   // installed once by GameBootstrap through the overlay registry
    public sealed class HudInteract : MonoBehaviour
    {
        public const int SortingOrder = 14;
        public static HudInteract Instance { get; private set; }

        public SimDriverBehaviour Driver;
        public GameHud Hud;
        public HumanInputSource Human;

        public Canvas Canvas { get; private set; }
        RectTransform _root, _space;

        public readonly FullMapView Map = new FullMapView();
        public readonly PingWheel Wheel = new PingWheel();
        public readonly PingMarkers Markers = new PingMarkers();
        public readonly PingFeed Feed = new PingFeed();
        public readonly HudHotspots Hotspots = new HudHotspots();
        public readonly MinimapControls Minimap = new MinimapControls();
        public readonly Tooltip Tip = new Tooltip();
        public readonly InfoCard Card = new InfoCard();
        public readonly InputGate Gate = new InputGate();

        public readonly InteractKey MapKey = new InteractKey("Map", Key.M, GamepadButton.DpadUp, "M", "D-PAD UP");
        public readonly InteractKey PingKey = new InteractKey("Ping", Key.G, GamepadButton.DpadLeft, "G", "D-PAD LEFT");
        public readonly InteractKey CursorKey = new InteractKey("HudCursor", Key.LeftAlt, GamepadButton.DpadDown, "L ALT", "D-PAD DOWN");
        public readonly InteractKey ZoomKey = new InteractKey("MinimapZoom", Key.N, GamepadButton.DpadRight, "N", "D-PAD RIGHT");

        public bool MapOpen => Map.IsOpen;
        public bool CursorFree { get; private set; }
        public bool PadHudFocus => _padHudFocus;
        public GateReason Reasons { get; private set; }
        public int Hovered => _hovered;

        int Me => HudBinder.LocalActor;

        bool _built, _padHudFocus, _mapDrag, _promptsDirty = true, _pauseHooked, _titleUp;
        int _hovered = -1;
        float _hoverSince, _nextTitleCheck, _navRepeatAt;
        Vector2 _dragLast, _wheelStart;
        Func<bool> _prevPause, _pauseDelegate;
        InputActionMap _map;
        Action<Ping, int> _onPinged;
        Action<double> _onRefused;
        Action _onBindings;
        Action<ControlDevice> _onDevice;

        // ---------------------------------------------------------------- lifecycle

        void OnEnable()
        {
            Instance = this;
            if (_onPinged == null)
            {
                _onPinged = (p, slot) => Feed.Add(p, Time.unscaledTime);
                _onRefused = wait => Feed.AddRefused(wait, Time.unscaledTime);
                _onBindings = () => _promptsDirty = true;
                _onDevice = d => _promptsDirty = true;
            }
            Pings.Placed += _onPinged;
            Pings.Refused += _onRefused;
            ControlBindings.BindingsChanged += _onBindings;
            ControlBindings.ActiveDeviceChanged += _onDevice;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Pings.Placed -= _onPinged;
            Pings.Refused -= _onRefused;
            ControlBindings.BindingsChanged -= _onBindings;
            ControlBindings.ActiveDeviceChanged -= _onDevice;
            Gate.Release();
            Minimap.EndPeek();
            UnhookPause();
            ItemHooks.ReleaseScoreboard();
        }

        void Start() => Build();

        /// <summary>Builds every widget once (hidden). Safe to call again.</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;
            if (Driver == null) Driver = GetComponent<SimDriverBehaviour>();
            if (Hud == null) Hud = GetComponent<GameHud>();
            if (Human == null) Human = GetComponent<HumanInputSource>();
            if (Hud != null) Hud.Build();
            _pauseDelegate = OnPausePressed;

            Canvas = InteractUi.MakeCanvas(transform, "HUD Interact Canvas", SortingOrder, out _root, out _space);
            int tier = UiIcons.TierFor(Canvas.scaleFactor);
            Feed.Build(_root);
            Markers.Build(_root);
            Hotspots.Build(_root, Hud, transform);
            Map.Build(_root, tier);
            _space.SetAsLastSibling();
            Card.Build(_space);
            Wheel.Build(_root);
            Tip.Build(_root);
            Minimap.Bind(Hud);
            if (Hud != null) Markers.BindMinimap(Hud.MapArea);

            _map = InputSystem.actions != null ? InputSystem.actions.FindActionMap("Player", false) : null;
            MapKey.Bind(_map);
            PingKey.Bind(_map);
            CursorKey.Bind(_map);
            ZoomKey.Bind(_map);
            Gate.Bind(_map);
            _promptsDirty = true;
        }

        void RefreshPrompts()
        {
            _promptsDirty = false;
            MapKey.RefreshGlyphs();
            PingKey.RefreshGlyphs();
            CursorKey.RefreshGlyphs();
            ZoomKey.RefreshGlyphs();
            ControlDevice d = ControlBindings.ActiveDevice;
            Hotspots.RefreshKeys(_map, d);
            Map.SetHints(d, MapKey.Glyph(d), PingKey.Glyph(d));
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            if (!_built) return;
            World w = Driver != null ? Driver.World : null;
            InteractUi.UpdateScale(Canvas);
            float now = Time.unscaledTime;
            if (now >= _nextTitleCheck)
            {
                _nextTitleCheck = now + 0.25f;
                _titleUp = GetComponent<TitleScreen>() != null;
            }
            if (w == null || _titleUp || w.ActorCount <= Me)
            {
                Idle();
                return;
            }
            ControlBindings.Sample();
            if (_promptsDirty) RefreshPrompts();
            Pings.Track(w);

            UnityEngine.Camera cam = Canvas.worldCamera;
            bool padActive = ControlBindings.ActiveDevice == ControlDevice.Gamepad;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;
            Vector2 screen = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            Vector2 rootLocal = ScreenToRoot(_root, screen, cam);
            Hotspots.FindTargets();
            Hotspots.UpdateRects(cam);

            GateReason reasons = GateReason.None;
            bool paused = Human != null && Human.State.Paused;
            if (paused)
            {
                // the pause menu owns input; keep the map drawn under it
                Minimap.EndPeek();
                Wheel.Cancel();
                Tip.Hide();
                Hotspots.Highlight(-1);
                if (Map.IsOpen || Card.IsOpen) reasons |= GateReason.Modal;
            }
            else
            {
                // in controller HUD focus the d-pad navigates, so its map, zoom and ping buttons wait until focus ends (B)
                if (MapKey.WasPressed && !Wheel.IsOpen && !_padHudFocus) { if (Map.IsOpen) CloseMap(); else OpenMap(); }
                if (ZoomKey.WasPressed && !Map.IsOpen && !_padHudFocus) Minimap.ToggleZoom();
                if (CursorKey.WasPressedOnGamepad && !_padHudFocus && !Map.IsOpen && !Card.IsOpen && !Wheel.IsOpen)
                {
                    SetPadFocus(true);
                    _navRepeatAt = now + 0.35f;   // the press that entered focus does not also move it
                }
                CursorFree = CursorKey.IsPressedOnKeyboardMouse || _padHudFocus;

                bool swallow = TickPing(w, now, cam, rootLocal, padActive, pad, mouse, ref reasons);
                if (Map.IsOpen) TickMap(screen, rootLocal, cam, padActive, pad, mouse, swallow, ref reasons);
                else if (Card.IsOpen) TickCard(rootLocal, pad, mouse, swallow, ref reasons);
                else TickHud(w, screen, rootLocal, cam, now, padActive, pad, mouse, swallow, ref reasons);
            }
            Reasons = reasons;
            Gate.Apply(reasons);
            HookPause(Map.IsOpen || Card.IsOpen || Wheel.IsOpen);
            Markers.Tick(w, cam, now, Me);
            Feed.Tick(now);
            Map.Tick(w, Me, now);
        }

        void LateUpdate()
        {
            if (_built && Driver != null && Driver.World != null) Minimap.LateTick(Driver.World, Me);
        }

        void Idle()
        {
            if (Map.IsOpen) CloseMap();
            Wheel.Cancel();
            Card.Close();
            Tip.Hide();
            Hotspots.Highlight(-1);
            Minimap.EndPeek();
            _padHudFocus = false;
            CursorFree = false;
            Reasons = GateReason.None;
            Gate.Apply(GateReason.None);
            HookPause(false);
        }

        // ---------------------------------------------------------------- pings

        bool TickPing(World w, float now, UnityEngine.Camera cam, Vector2 rootLocal, bool padActive, Gamepad pad, Mouse mouse, ref GateReason reasons)
        {
            if (!Wheel.IsOpen && PingKey.WasPressed && !Card.IsOpen && !_padHudFocus)
            {
                double x, z;
                Vector2 at;
                bool ok = true, fromMap = false;
                if (Map.IsOpen)
                {
                    Vector2 v = padActive ? Vector2.zero : ScreenToRoot(Map.Viewport, mouse != null ? mouse.position.ReadValue() : Vector2.zero, cam);
                    ok = FullMapView.InsideView(v);
                    Map.ViewToSim(v, out x, out z);
                    at = ViewToRoot(v, cam);
                    fromMap = true;
                }
                else
                {
                    Actor me = w.Actors[Me];
                    if (Human != null && Human.State.HasAim)
                    {
                        x = Human.State.AimX;
                        z = Human.State.AimZ;
                    }
                    else
                    {
                        x = w.X[Me] + Math.Cos(me.Facing) * 6.0;
                        z = w.Z[Me] + Math.Sin(me.Facing) * 6.0;
                    }
                    at = padActive ? WorldToRoot(w, x, z, cam) : rootLocal;
                }
                if (ok)
                {
                    Wheel.Open(x, z, at, _root.rect, now, fromMap);
                    _wheelStart = rootLocal;
                    Tip.Hide();
                }
            }
            if (!Wheel.IsOpen) return false;
            reasons |= GateReason.PingWheel;
            if (padActive) reasons |= GateReason.PadOwnsSticks;
            Vector2 d = padActive && pad != null ? pad.rightStick.ReadValue() * PingWheel.Radius : rootLocal - _wheelStart;
            Wheel.Steer(d);
            bool confirm = mouse != null && mouse.leftButton.wasPressedThisFrame && Wheel.Selected >= 0;
            bool cancel = (mouse != null && mouse.rightButton.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (cancel) Wheel.Cancel();
            else if (confirm || PingKey.WasReleased || !PingKey.IsPressed)
            {
                if (Wheel.Release(now, out PingKind kind)) Pings.Place(w, kind, Wheel.X, Wheel.Z, Me);
            }
            return confirm || cancel;
        }

        // ---------------------------------------------------------------- full map

        void TickMap(Vector2 screen, Vector2 rootLocal, UnityEngine.Camera cam, bool padActive, Gamepad pad, Mouse mouse, bool swallow, ref GateReason reasons)
        {
            reasons |= GateReason.Modal;
            if (padActive) reasons |= GateReason.PadOwnsSticks;
            float dt = Time.unscaledDeltaTime;
            Vector2 v = ScreenToRoot(Map.Viewport, screen, cam);
            bool inside = !padActive && FullMapView.InsideView(v);
            if (!padActive && mouse != null && !swallow && !Wheel.IsOpen)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (inside && Mathf.Abs(scroll) > 0.01f) Map.ZoomAt(ZoomFactor(scroll), v);
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    if (Map.OnCloseChip(rootLocal) || !FullMapView.OnPanel(rootLocal)) { CloseMap(); return; }
                    if (inside) { _mapDrag = true; _dragLast = v; }
                }
            }
            if (_mapDrag)
            {
                if (mouse != null && mouse.leftButton.isPressed) { Map.PanBy(v - _dragLast); _dragLast = v; }
                else _mapDrag = false;
            }
            if (padActive && pad != null && !Wheel.IsOpen)
            {
                Vector2 rs = pad.rightStick.ReadValue();
                if (rs.sqrMagnitude > 0.04f) Map.PanBy(-rs * (FullMapView.PadPanSpeed * dt));
                float zr = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                if (Mathf.Abs(zr) > 0.05f) Map.ZoomAt(Mathf.Exp(zr * FullMapView.PadZoomRate * dt), Vector2.zero);
                if (pad.buttonEast.wasPressedThisFrame) { CloseMap(); return; }
            }
            Map.ShowReticle(padActive);
            Map.Hover(padActive ? Vector2.zero : v, padActive || (inside && !_mapDrag));
        }

        /// <summary>Wheel notches to a zoom factor (mouse wheels report about 120 per notch, trackpads small steps).</summary>
        public static float ZoomFactor(float scroll)
        {
            float notches = Mathf.Abs(scroll) >= 30f ? scroll / 120f : scroll / 10f;
            return Mathf.Pow(1.2f, Mathf.Clamp(notches, -2f, 2f));
        }

        public void OpenMap()
        {
            if (!_built || Driver == null || Driver.World == null || Map.IsOpen) return;
            Card.Close();
            Tip.Hide();
            SetPadFocus(false);
            Minimap.EndPeek();
            Texture hudTex = Hud != null && Hud.MapArea != null ? Hud.MapArea.GetComponent<UnityEngine.UI.RawImage>().texture : null;
            Map.Show(Driver.World, Me, hudTex);
            ControlDevice d = ControlBindings.ActiveDevice;
            Map.SetHints(d, MapKey.Glyph(d), PingKey.Glyph(d));
            ItemHooks.Sound("map-open");
            FullMap.Raise(true);
        }

        public void CloseMap()
        {
            if (!Map.IsOpen) return;
            if (Wheel.IsOpen && Wheel.FromMap) Wheel.Cancel();
            Map.Hide();
            _mapDrag = false;
            ItemHooks.Sound("map-close");
            FullMap.Raise(false);
        }

        // ---------------------------------------------------------------- cards

        void TickCard(Vector2 rootLocal, Gamepad pad, Mouse mouse, bool swallow, ref GateReason reasons)
        {
            reasons |= GateReason.Modal;
            if (!swallow && mouse != null && mouse.leftButton.wasPressedThisFrame && !Card.Contains(_root, rootLocal)) Card.Close();
            if (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame)) Card.Close();
        }

        // ---------------------------------------------------------------- clickable HUD

        void TickHud(World w, Vector2 screen, Vector2 rootLocal, UnityEngine.Camera cam, float now, bool padActive, Gamepad pad, Mouse mouse,
            bool swallow, ref GateReason reasons)
        {
            if (CursorFree) reasons |= GateReason.CursorFree;
            if (_padHudFocus)
            {
                reasons |= GateReason.PadHudFocus;
                TickPadFocus(now, pad);
                return;
            }
            int hit = Hotspots.HitTest(rootLocal, CursorFree);
            if ((hit >= 0 && !padActive) || Minimap.Pressed) reasons |= GateReason.PointerOverHud;
            if (mouse != null && !swallow && !Wheel.IsOpen)
            {
                if (mouse.leftButton.wasPressedThisFrame && hit >= 0)
                {
                    if (Hotspots.Def(hit).Click == HotClick.OpenMap) Minimap.Down(screen, now);
                    else Activate(hit);
                }
                if (Minimap.Pressed)
                {
                    if (mouse.leftButton.isPressed) Minimap.Held(screen, now, cam, Canvas.scaleFactor);
                    else if (Minimap.Up(now)) OpenMap();
                }
            }
            if (hit != _hovered)
            {
                _hovered = hit;
                _hoverSince = now;
            }
            float dwell = CursorFree ? 0.05f : 0.35f;
            if (hit >= 0 && now - _hoverSince >= dwell && !Minimap.Peeking) ShowTip(hit);
            else Tip.Hide();
            Hotspots.Highlight(CursorFree && hit >= 0 ? hit : -1);
        }

        void SetPadFocus(bool on)
        {
            _padHudFocus = on;
            Hotspots.Focused = on ? Hotspots.First() : -1;
            if (!on)
            {
                Tip.Hide();
                Hotspots.Highlight(-1);
            }
        }

        void TickPadFocus(float now, Gamepad pad)
        {
            if (pad == null) { SetPadFocus(false); return; }
            Vector2 dir = Vector2.zero;
            if (pad.dpad.up.isPressed) dir.y += 1f;
            if (pad.dpad.down.isPressed) dir.y -= 1f;
            if (pad.dpad.left.isPressed) dir.x -= 1f;
            if (pad.dpad.right.isPressed) dir.x += 1f;
            Vector2 ls = pad.leftStick.ReadValue();
            if (ls.sqrMagnitude > 0.36f) dir += ls;
            if (dir.sqrMagnitude > 0.1f)
            {
                if (now >= _navRepeatAt)
                {
                    bool first = _navRepeatAt <= 0f;
                    _navRepeatAt = now + (first ? 0.35f : 0.16f);
                    Vector2 n = dir.normalized;
                    Hotspots.Focused = Hotspots.Navigate(Hotspots.Focused, n);
                }
            }
            else _navRepeatAt = 0f;
            int f = Hotspots.Focused;
            if (f < 0 || !Hotspots.Visible(f)) Hotspots.Focused = f = Hotspots.First();
            if (pad.buttonSouth.wasPressedThisFrame && f >= 0) Activate(f);
            if (pad.buttonEast.wasPressedThisFrame) { SetPadFocus(false); return; }
            Hotspots.Highlight(f);
            if (f >= 0) ShowTip(f);
            else Tip.Hide();
        }

        void ShowTip(int i)
        {
            HotspotDef d = Hotspots.Def(i);
            Rect r = Hotspots.RectOf(i);
            Rect bounds = _root.rect;
            // below the element when it sits in the upper half, above it otherwise
            float y = r.center.y > 0f ? r.yMin - 10f : r.yMax + 10f + Tip.Height;
            Tip.Show(d, d.Name, d.Meaning, Hotspots.KeyText(i), new Vector2(r.xMin, y), bounds);
        }

        /// <summary>Runs a HUD element's click (tests and the controller call this directly).</summary>
        public void Activate(int i)
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null || i < 0 || i >= Hotspots.Count) return;
            HotspotDef d = Hotspots.Def(i);
            ItemHooks.Sound("ui-click");
            switch (d.Click)
            {
                case HotClick.Scoreboard:
                    if (!ItemHooks.ToggleScoreboard()) ToggleCard("score", ScoreCard);
                    break;
                case HotClick.PingHill:
                    Pings.Place(w, PingWords.ForHill(w.Match.HillState, w.Actors[Me].Team), w.Map.Hill.X, w.Map.Hill.Z, Me);
                    break;
                case HotClick.PingKaiju:
                {
                    bool egg = !w.Match.Hatched || w.Kaiju.State == KaijuState.Egg;
                    Pings.Place(w, PingWords.ForKaiju(w.Kaiju.State, w.Actors[Me].Team), egg ? w.Map.Shrine.X : w.Kaiju.X, egg ? w.Map.Shrine.Z : w.Kaiju.Z, Me);
                    break;
                }
                case HotClick.KaijuCard: ToggleCard("kaiju", KaijuCard); break;
                case HotClick.CharacterPanel:
                    if (!ItemHooks.OpenCharacterPanel()) ToggleCard("character", CharacterCard);
                    break;
                case HotClick.AbilityCard:
                    _cardArg = d.Arg;
                    ToggleCard("ability-" + d.Arg, AbilityCard);
                    break;
                case HotClick.HealthCard: ToggleCard("health", HealthCard); break;
                case HotClick.RespawnCard: ToggleCard("respawn", RespawnCard); break;
                case HotClick.OpenMap: OpenMap(); break;
            }
        }

        int _cardArg;

        void ToggleCard(string id, Action open)
        {
            if (Card.Id == id) { Card.Close(); return; }
            Tip.Hide();
            open();
        }

        string CardHint => ControlBindings.ActiveDevice == ControlDevice.Gamepad ? "B  CLOSE" : "ESC OR CLICK OUTSIDE  CLOSE";

        GameConfig Cfg => Driver != null && Driver.World != null ? Driver.World.Config : GameConfig.CreateDefault();

        static string N(double v) => LoreText.Number(v);

        static string Clock(double seconds)
        {
            int s = (int)Math.Round(Math.Max(0, seconds));
            return (s / 60) + ":" + (s % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        void KaijuCard()
        {
            World w = Driver.World;
            GameConfig c = Cfg;
            MatchState m = w.Match;
            bool egg = !m.Hatched || w.Kaiju.State == KaijuState.Egg;
            string sub = egg ? "EGG · HATCHES IN " + Clock(global::CriticalMass.Runtime.Flow.MatchSpeed.ToReal(c.HatchSeconds - m.Time))
                : w.Kaiju.State == KaijuState.Azure ? "AZURE · FIGHTS FOR AZURE"
                : w.Kaiju.State == KaijuState.Crimson ? "CRIMSON · FIGHTS FOR CRIMSON"
                : w.Kaiju.State == KaijuState.Berserk ? "BERSERK · FIGHTS EVERYONE" : "WILD · FIGHTS WHOEVER HITS IT";
            var f = c.KaijuFeed;
            string[] lines =
            {
                "Allegiance now: Azure " + N(m.Allegiance.Azure) + ", Crimson " + N(m.Allegiance.Crimson) + ". Rage " + N(m.Instability) + " of " + N(f.BerserkThreshold) + (m.BerserkLocked ? ", locked: it will hatch berserk." : "."),
                LoreText.Tip("tip-04", c) ?? "The egg hatches at " + Clock(c.HatchSeconds) + ". Whatever it was fed by then decides who it fights for.",
                LoreText.Tip("tip-05", c) ?? "Each biomass delivered is worth " + N(f.BiomassAllegiancePerUnit) + " allegiance. Corruption gives " + N(f.CorruptionAllegiancePerUnit) + ", and costs far more than it pays.",
                LoreText.Tip("tip-06", c) ?? "Each corruption delivered adds " + N(f.CorruptionInstabilityPerUnit) + " rage. At " + N(f.BerserkThreshold) + ", the egg is lost to berserk for good.",
                LoreText.Tip("tip-08", c) ?? "A berserk Kaiju attacks both sides, but it hunts the bigger biomass feeder harder.",
            };
            Card.Open("kaiju", egg ? "THE KAIJU EGG" : "THE KAIJU", sub, lines, CardHint);
        }

        static readonly string[] ClassUpper = { "WARRIOR", "FARMER", "MINER", "DEMOLITIONS", "BUILDER" };

        void AbilityCard()
        {
            World w = Driver.World;
            GameConfig c = Cfg;
            Actor a = w.Actors[Me];
            int cls = Mathf.Clamp((int)a.Cls, 0, ClassUpper.Length - 1);
            ClassConfig cc = c.Classes[cls];
            ControlDevice d = ControlBindings.ActiveDevice;
            int arg = _cardArg;
            if (arg == 2)
            {
                string carrying = a.Carry == CarryKind.Biomass ? "You carry " + a.CarryAmount + " biomass." :
                    a.Carry == CarryKind.Corruption ? "You carry " + a.CarryAmount + " corruption." : "Your hands are empty.";
                Card.Open("ability-2", "CARRY", ClassUpper[cls], new[]
                {
                    carrying,
                    "Gather at a green biomass plant or a violet corruption growth, then bring it to the egg at the shrine.",
                    LoreText.Tip("tip-05", c) ?? "Biomass wins the Kaiju's allegiance for your team. Corruption raises its rage toward berserk.",
                    "Key: " + Hotspots.KeyText(IndexOf("ability-carry")),
                }, CardHint);
                return;
            }
            string input = arg == 0 ? "attack" : "ability";
            LoreText.PackAbility pa = LoreText.Ability(a.Cls, input);
            string name = pa != null && !string.IsNullOrEmpty(pa.name) ? pa.name.ToUpperInvariant() : arg == 0 ? "ATTACK" : "CLASS ABILITY";
            string text = pa != null ? LoreText.Resolve(pa.text, c) : null;
            if (text == null)
                text = arg == 0 ? "Your basic attack for " + N(cc.AttackDamage) + " damage." : "Your class's special move. The ring on its button sweeps while it recovers.";
            double cd = arg == 0 ? cc.AttackCooldown : cc.AbilityCooldown;
            Card.Open("ability-" + arg, name, ClassUpper[cls] + (arg == 0 ? " · ATTACK" : " · CLASS ABILITY"), new[]
            {
                text,
                "Cooldown: " + N(cd) + " s.",
                "Key: " + Hotspots.KeyText(IndexOf(arg == 0 ? "ability-attack" : "ability-class")) + (d == ControlDevice.Gamepad ? "" : "   (hold " + CursorKey.Glyph(d) + " to click the HUD without attacking)"),
            }, CardHint);
        }

        int IndexOf(string id)
        {
            for (int i = 0; i < Hotspots.Count; i++) if (Hotspots.Def(i).Id == id) return i;
            return 0;
        }

        void HealthCard()
        {
            World w = Driver.World;
            GameConfig c = Cfg;
            Actor a = w.Actors[Me];
            int cls = Mathf.Clamp((int)a.Cls, 0, ClassUpper.Length - 1);
            Card.Open("health", "HEALTH", ClassUpper[cls], new[]
            {
                a.Alive ? "Health " + N(Math.Ceiling(Math.Max(0, a.Hp))) + " of " + N(a.MaxHp) + ". Each heart is a quarter." : "Knocked out. Back in " + N(Math.Ceiling(Math.Max(0, global::CriticalMass.Runtime.Flow.MatchSpeed.ToReal(a.RespawnIn)))) + " s.",
                LoreText.Tip("tip-14", c) ?? "Knocked out? You are back in " + N(c.Respawn.DelaySeconds) + " s, and nothing can hurt you for " + N(c.Respawn.ProtectionSeconds) + " s after.",
                "Knockouts " + a.Kills + ", knocked out " + a.Deaths + ".",
            }, CardHint);
        }

        void RespawnCard()
        {
            GameConfig c = Cfg;
            Card.Open("respawn", "RESPAWN", null, new[]
            {
                LoreText.Tip("tip-14", c) ?? "Knocked out? You are back in " + N(c.Respawn.DelaySeconds) + " s, and nothing can hurt you for " + N(c.Respawn.ProtectionSeconds) + " s after.",
                LoreText.Tip("tip-20", c) ?? "While you wait to respawn, you can pick a different class.",
            }, CardHint);
        }

        void CharacterCard()
        {
            World w = Driver.World;
            GameConfig c = Cfg;
            Actor a = w.Actors[Me];
            int cls = Mathf.Clamp((int)a.Cls, 0, ClassUpper.Length - 1);
            LoreText.PackClass pc = LoreText.Class(a.Cls);
            LoreText.PackAbility atk = LoreText.Ability(a.Cls, "attack"), abl = LoreText.Ability(a.Cls, "ability"), pas = LoreText.Ability(a.Cls, "passive");
            string team = a.Team == TeamId.Crimson ? "CRIMSON" : "AZURE";
            Card.Open("character", ClassUpper[cls], team + (pc != null && !string.IsNullOrEmpty(pc.role) ? " · " + pc.role.ToUpperInvariant() : ""), new[]
            {
                pc != null ? LoreText.Resolve(pc.bio, c) : null,
                atk != null ? atk.name + ": " + (LoreText.Resolve(atk.text, c) ?? "") : "Attack: " + N(c.Classes[cls].AttackDamage) + " damage.",
                abl != null ? abl.name + ": " + (LoreText.Resolve(abl.text, c) ?? "") : "Class ability: " + N(c.Classes[cls].AbilityCooldown) + " s cooldown.",
                pas != null ? pas.name + ": " + (LoreText.Resolve(pas.text, c) ?? "") : "Health " + N(c.Classes[cls].MaxHp) + ".",
                "Knockouts " + a.Kills + ", knocked out " + a.Deaths + ".",
                LoreText.Tip("tip-20", c) ?? "While you wait to respawn, you can pick a different class.",
            }, CardHint);
        }

        void ScoreCard()
        {
            World w = Driver.World;
            MatchState m = w.Match;
            Actor a = w.Actors[Me];
            Card.Open("score", "SCORE", "HILL SECONDS DECIDE THE MATCH", new[]
            {
                "Azure " + N(Math.Floor(m.HillSeconds.Azure)) + " s of hill, Crimson " + N(Math.Floor(m.HillSeconds.Crimson)) + " s.",
                "Time left " + Clock(global::CriticalMass.Runtime.Flow.MatchSpeed.ToReal(w.Config.MatchSeconds - m.Time)) + ".",
                "You: " + a.Kills + " knockouts, knocked out " + a.Deaths + ", " + N(Math.Floor(a.HillSeconds)) + " s on the hill.",
            }, CardHint);
        }

        // ---------------------------------------------------------------- escape

        bool OnPausePressed()
        {
            if (Wheel.IsOpen) { Wheel.Cancel(); return true; }
            if (Card.IsOpen) { Card.Close(); return true; }
            if (Map.IsOpen) { CloseMap(); return true; }
            return _prevPause != null && _prevPause();
        }

        void HookPause(bool want)
        {
            if (Human == null) return;
            if (want && !_pauseHooked)
            {
                _prevPause = Human.PauseHandler;
                Human.PauseHandler = _pauseDelegate;
                _pauseHooked = true;
            }
            else if (!want && _pauseHooked) UnhookPause();
        }

        void UnhookPause()
        {
            if (!_pauseHooked) return;
            _pauseHooked = false;
            if (Human != null && Human.PauseHandler == _pauseDelegate) Human.PauseHandler = _prevPause;
            _prevPause = null;
        }

        // ---------------------------------------------------------------- coordinates

        static Vector2 ScreenToRoot(RectTransform rt, Vector2 screen, UnityEngine.Camera cam)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, cam, out Vector2 local);
            return local;
        }

        Vector2 ViewToRoot(Vector2 v, UnityEngine.Camera cam)
        {
            Vector3 world = Map.Viewport.TransformPoint(new Vector3(v.x, v.y, 0f));
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            return ScreenToRoot(_root, screen, cam);
        }

        Vector2 WorldToRoot(World w, double x, double z, UnityEngine.Camera cam)
        {
            if (cam == null) return Vector2.zero;
            Vector3 sp = cam.WorldToScreenPoint(SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z), z));
            return ScreenToRoot(_root, new Vector2(sp.x, sp.y), cam);
        }
    }

    /// <summary>
    /// Adds HudInteract to the game GameBootstrap builds, on boot and after every scene load, without editing the
    /// bootstrap (the pause menu's MenuInstaller pattern; GameBootstrap.Build is idempotent).
    /// </summary>
    public static class InteractInstaller
    {
        // Integration batch 2: installed by GameBootstrap through the DG-01 overlay registry ([GameOverlay]); Attach stays for tests.
        /// <summary>Adds HudInteract next to the GameHud; returns it (null: no game in this scene).</summary>
        public static HudInteract Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            GameHud hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
            if (hud == null) return null;
            HudInteract hi = hud.GetComponent<HudInteract>();
            if (hi == null) hi = hud.gameObject.AddComponent<HudInteract>();
            return hi;
        }
    }
}

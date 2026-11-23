using System;
using System.Diagnostics;
using CriticalMass.Net.Replay;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Replay
{
    /// <summary>
    /// b13-replay: plays a saved match in the normal match scene. SimDriverBehaviour builds no world of its own for a replay
    /// request: it shows ReplayPlayer's world, and this component ticks it with the recorded inputs on its own SimClock
    /// (the same accumulator and catch-up cap as a played match), raising the driver's Ticked for every tick so every view,
    /// effect, HUD part and the results screen work as in a played match.
    /// <para>Controls (keyboard / controller): pause and resume (Space / A), speed 0.5x 1x 2x 4x of the match pace
    /// (minus and plus / d-pad down and up), 10 s back and forward (J and L / d-pad left and right), follow the previous or
    /// next player (Q and E / LB and RB; the recorded player is the normal gameplay camera), free camera (F / View button,
    /// then the move keys or the left stick), exit to the menu (Backspace / B; the pause menu's Quit works too).</para>
    /// <para>Seeking: forward runs the extra ticks at once over a few frames (ForwardBudgetMs of sim per frame). The sim has no
    /// cheap snapshot (no save and restore of a World), so back rebuilds the world from the recorded start and
    /// re-simulates to the target behind a cover (RewindBudgetMs per frame), through the same Boot reload a played match
    /// uses so every view rebinds to the new world.</para>
    /// <para>Every recorded fingerprint is checked on the way; the first mismatch stops playback with a plain message
    /// instead of showing a different match.</para>
    /// </summary>
    [DefaultExecutionOrder(900)]   // after the driver's Update, before every LateUpdate (the camera follow), as SpectatorMode
    public sealed class ReplayPlayback : MonoBehaviour
    {
        public static readonly double[] Speeds = { 0.5, 1, 2, 4 };
        public const int DefaultSpeedIndex = 1;
        public const double JumpSeconds = 10;
        public const double ForwardBudgetMs = 8, RewindBudgetMs = 24;
        const string Tag = "[CM.Replay]";

        // ---------------------------------------------------------------- static launch state (survives the Boot reload)

        /// <summary>The replay the next driver shows (set by Play / Rewind, taken by the driver's Awake).</summary>
        public static ReplayPlayer Pending { get; private set; }
        static ReplayData _data;
        static MatchRequest _request;
        static int _pendingSeek = -1, _pendingSpeed = DefaultSpeedIndex, _pendingFollow = -1, _gen;
        static bool _pendingFree;

        public static ReplayPlayback Current { get; private set; }

        /// <summary>Class moves lane 6 (T1-8): a replay rendered to a film by MatchVideo (-replayFile with -matchVideo): the
        /// playback bar and hints are not drawn, so the film shows only the recorded player's gameplay view and HUD.</summary>
        public static bool FilmMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Pending = null;
            _data = null;
            _request = null;
            _pendingSeek = -1;
            _pendingSpeed = DefaultSpeedIndex;
            _pendingFollow = -1;
            _pendingFree = false;
            Current = null;
            _suppressing = false;
            _savedAutoFinish = true;
        }

        /// <summary>
        /// Starts watching a replay (from the Replays screen or -replayLast). False with a plain reason when this build
        /// cannot play it (other version, other rules): nothing is loaded then.
        /// </summary>
        public static bool Play(ReplayData d, out string why)
        {
            why = null;
            if (d == null) { why = ReplayCompat.Message(ReplayCompatibility.Damaged, null); return false; }
            ReplayCompatibility c = ReplayCompat.Check(d.Header, BuildInfo.Version, BuildInfo.Commit);
            if (c != ReplayCompatibility.Ok) { why = ReplayCompat.Message(c, d.Header); return false; }
            ReplayPlayer p;
            try { p = new ReplayPlayer(d); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"{Tag} could not build the replay's world: {e.GetType().Name}: {e.Message}");
                why = ReplayCompat.Message(ReplayCompatibility.Damaged, d.Header);
                return false;
            }
            if (!p.ConfigMatches) { why = ReplayCompat.Message(ReplayCompatibility.OtherVersion, d.Header); return false; }
            _data = d;
            _request = RequestFor(d.Header);
            _pendingSeek = -1;
            _pendingSpeed = DefaultSpeedIndex;
            _pendingFollow = -1;
            _pendingFree = false;
            Pending = p;
            GameFlow.BeginReplay(_request, false);
            return true;
        }

        /// <summary>The flow request a replay is shown under (seed, team, class, size, pace: the loading screen and the HUD read them).</summary>
        public static MatchRequest RequestFor(ReplayHeader h)
        {
            ReplayStart s = h.Start ?? new ReplayStart();
            int local = s.LocalActor >= 0 && s.LocalActor < h.Roster.Length ? s.LocalActor : 0;
            return new MatchRequest
            {
                Seed = s.Seed,
                Team = h.Roster.Length > 0 ? h.Roster[local].Team : s.HumanTeam,
                Class = h.Roster.Length > 0 ? h.Roster[local].Class : s.HumanClass,
                TeamSize = s.TeamSize,
                Pace = s.Pace > 0 ? s.Pace : MatchSpeed.DefaultPace,
                BotDifficulty = s.BotDifficulty,
                Mode = Enum.IsDefined(typeof(MatchMode), h.Mode) ? (MatchMode)h.Mode : MatchMode.Custom,
                Replay = true,
            };
        }

        /// <summary>The driver's Awake takes the pending replay (and the camera follows the recorded player).</summary>
        public static ReplayPlayer TakePending()
        {
            ReplayPlayer p = Pending;
            Pending = null;
            _gen++;
            if (p != null) LocalSeat.Set(p.Header.Start != null ? p.Header.Start.LocalActor : 0);
            return p;
        }

        /// <summary>Leaves the replay for the main menu.</summary>
        public static void ExitToMenu()
        {
            Pending = null;
            _data = null;
            GameFlow.ReturnTo(FlowState.Menu);
        }

        // ---------------------------------------------------------------- instance

        public SimDriverBehaviour Driver { get; private set; }
        public ReplayPlayer Player { get; private set; }
        public bool Paused { get; private set; }
        public int SpeedIndex { get; private set; } = DefaultSpeedIndex;
        public double Speed => Speeds[SpeedIndex];
        public bool FreeCamera { get; private set; }
        /// <summary>The followed actor (LocalSeat.Actor: the normal gameplay camera).</summary>
        public int Followed { get; private set; }
        /// <summary>The tick a seek runs to (-1: none).</summary>
        public int SeekTarget { get; private set; } = -1;
        /// <summary>True while a rewind re-simulates behind the cover.</summary>
        public bool Rewinding { get; private set; }
        /// <summary>The plain message on screen (out of step, end of a replay that was left early), or null.</summary>
        public string Message { get; private set; }

        readonly SimClock _clock = new SimClock();
        readonly SpectatorRig _rig = new SpectatorRig();
        readonly Stopwatch _sw = new Stopwatch();
        HumanInputSource _input;
        CameraFollowTarget _follow;
        InputAction _move;
        int _myGen;
        static bool _savedAutoFinish = true, _suppressing;
        double _dt;

        // UI
        Canvas _canvas;
        Text _status, _hint, _message;
        RectTransform _barFill;
        GameObject _cover, _messagePanel;
        Text _coverText;
        int _shownSecond = -1, _shownSpeed = -1, _shownFollow = -2, _shownFill = -1, _shownCover = -1;
        bool _shownPaused, _shownFree;
        ControlDevice _hintDevice = (ControlDevice)(-1);
        string _shownMessage;

        void Awake()
        {
            Current = this;
            _myGen = _gen;
            Driver = GetComponent<SimDriverBehaviour>();
            Player = Driver != null ? Driver.ReplayPlayer : null;
            // Watching a match again earns nothing: the reward tracker banks a finished match's coins only when AutoFinish is on.
            if (!_suppressing) { _savedAutoFinish = Progression.MatchRewardTracker.AutoFinish; _suppressing = true; }
            Progression.MatchRewardTracker.AutoFinish = false;
            _dt = Driver != null && Driver.World != null ? SimStep.Dt(Driver.World.Config) : 1.0 / 30;
            SpeedIndex = Mathf.Clamp(_pendingSpeed, 0, Speeds.Length - 1);
            FreeCamera = _pendingFree;
            Followed = LocalSeat.Actor;
            if (_pendingSeek > 0 && Player != null)
            {
                SeekTarget = Math.Min(_pendingSeek, Player.TotalTicks);
                Rewinding = true;
            }
            _pendingSeek = -1;
            if (Player != null && Player.World != null)
            {
                _rig.Reset(Player.World, _pendingFollow >= 0 ? _pendingFollow : LocalSeat.Actor);
                if (_pendingFollow >= 0 && _pendingFollow < Player.World.ActorCount) Followed = _pendingFollow;
            }
            _pendingFollow = -1;
            UnityEngine.Debug.Log($"{Tag} playing: {Player?.TotalTicks ?? 0} ticks, seed {Player?.Header.Start?.Seed ?? 0}" + (Rewinding ? $", rewinding to tick {SeekTarget}" : ""));
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_follow != null) _follow.ClearOverride();
            if (_myGen == _gen && Pending == null)   // a rewind's new scene keeps the seat and the reward hold
            {
                LocalSeat.Reset();
                Progression.MatchRewardTracker.AutoFinish = _savedAutoFinish;
                _suppressing = false;
            }
        }

        void Update()
        {
            if (Driver == null || Player == null) return;
            if (_input == null) _input = GetComponent<HumanInputSource>();   // added after the driver by GameBootstrap
            World w = Player.World;
            if (Message == null) ReadControls(w);
            else ReadExitOnly();

            bool held = (_input != null && _input.State.Paused) || View.Dressing.WorldView.Building || FirstRunWarm.HoldsSim ||
                        Menus.LoadingScreen.IsOpen;
            float capture = Time.captureDeltaTime;
            double unscaled = capture > 0f ? capture : Time.unscaledDeltaTime;
            if (SeekTarget >= 0 && !held)
            {
                RunSeek(w);
                Driver.ReplayAlpha(0);
            }
            else
            {
                double scale = (Driver.TimeScale > 0 ? Driver.TimeScale : 1) * Speed;
                int ticks = _clock.Advance(unscaled, scale, _dt, held || Paused || Player.Finished);
                for (int i = 0; i < ticks; i++)
                {
                    if (!Player.Step()) break;
                    Driver.ReplayTicked(w);
                }
                Driver.ReplayAlpha(Player.Finished ? 1 : Math.Max(0, Math.Min(1, _clock.Alpha(_dt))));
            }
            WatchEnd();
            UpdateCamera(w, held ? 0 : Driver.FrameDt);
            UpdateUi();
        }

        void RunSeek(World w)
        {
            double budget = Rewinding ? RewindBudgetMs : ForwardBudgetMs;
            _sw.Restart();
            while (Player.TicksPlayed < SeekTarget && _sw.Elapsed.TotalMilliseconds < budget)
            {
                if (!Player.Step()) break;
                Driver.ReplayTicked(w);
            }
            if (Player.TicksPlayed >= SeekTarget || Player.Finished)
            {
                SeekTarget = -1;
                Rewinding = false;
            }
        }

        void WatchEnd()
        {
            if (Message != null) return;
            if (Player.Desynced || Player.StreamBroken)
            {
                int t = Math.Max(0, Player.DesyncTick);
                string at = ReplayWords.Length(t / Math.Max(1.0, Player.Header.SimHz));
                Message = Player.StreamBroken
                    ? "This replay file is damaged after " + at + ". Playback stopped."
                    : "This replay went out of step with this game at " + at + ". Playback stopped so it never shows a different match.";
                SeekTarget = -1;
                Rewinding = false;
                UnityEngine.Debug.LogWarning($"{Tag} stopped: {(Player.StreamBroken ? "stream damaged" : "fingerprint mismatch")} at world tick {Player.DesyncTick}, " +
                                             $"{Player.FingerprintsChecked} fingerprints matched before it");
                return;
            }
            // A match that ended plays into the normal results screen (FlowDirector); a recording that was left early ends here.
            if (Player.Finished && Player.World.Match.Phase != MatchPhase.Ended)
                Message = "End of the replay (the match was left before the final whistle).";
        }

        // ---------------------------------------------------------------- controls

        void ReadExitOnly()
        {
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if ((kb != null && kb.backspaceKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) ExitToMenu();
            else if ((kb != null && kb.jKey.wasPressedThisFrame) || (pad != null && pad.dpad.left.wasPressedThisFrame))
            {
                if (!Player.Desynced && !Player.StreamBroken) { Message = null; Jump(-1); }
            }
        }

        void ReadControls(World w)
        {
            if (_input != null && _input.State.Paused) return;   // the pause menu has the keys
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.kKey.wasPressedThisFrame) TogglePause();
                if (kb.jKey.wasPressedThisFrame) Jump(-1);
                if (kb.lKey.wasPressedThisFrame) Jump(1);
                if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) StepSpeed(-1);
                if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) StepSpeed(1);
                if (kb.qKey.wasPressedThisFrame) Cycle(w, -1);
                if (kb.eKey.wasPressedThisFrame) Cycle(w, 1);
                if (kb.fKey.wasPressedThisFrame) ToggleFree();
                if (kb.backspaceKey.wasPressedThisFrame) { ExitToMenu(); return; }
            }
            if (pad != null)
            {
                if (pad.buttonSouth.wasPressedThisFrame) TogglePause();
                if (pad.dpad.left.wasPressedThisFrame) Jump(-1);
                if (pad.dpad.right.wasPressedThisFrame) Jump(1);
                if (pad.dpad.down.wasPressedThisFrame) StepSpeed(-1);
                if (pad.dpad.up.wasPressedThisFrame) StepSpeed(1);
                if (pad.leftShoulder.wasPressedThisFrame) Cycle(w, -1);
                if (pad.rightShoulder.wasPressedThisFrame) Cycle(w, 1);
                if (pad.selectButton.wasPressedThisFrame) ToggleFree();
                if (pad.buttonEast.wasPressedThisFrame) { ExitToMenu(); return; }
            }
        }

        public void TogglePause() => Paused = !Paused;

        public void SetSpeedIndex(int i) => SpeedIndex = Mathf.Clamp(i, 0, Speeds.Length - 1);

        public void StepSpeed(int dir) => SetSpeedIndex(SpeedIndex + Math.Sign(dir));

        /// <summary>dir +1: JumpSeconds forward (fast simulation); -1: back (rebuild and re-simulate from the start).</summary>
        public void Jump(int dir)
        {
            if (Player == null || Rewinding) return;
            int step = (int)Math.Round(JumpSeconds * Player.Header.SimHz);
            int from = SeekTarget >= 0 ? SeekTarget : Player.TicksPlayed;
            if (dir > 0)
            {
                SeekTarget = Math.Min(Player.TotalTicks, from + step);
                return;
            }
            int target = Math.Max(0, from - step);
            if (_data == null) return;
            ReplayPlayer fresh;
            try { fresh = new ReplayPlayer(_data); }
            catch (Exception) { return; }
            Pending = fresh;
            _pendingSeek = target;
            _pendingSpeed = SpeedIndex;
            _pendingFree = FreeCamera;
            _pendingFollow = Followed;
            GameFlow.BeginReplay(_request ?? RequestFor(_data.Header), true);
        }

        /// <summary>Follow the previous (-1) or next (+1) player (capture tool and the Q / E keys).</summary>
        public void CycleFollow(int dir) { if (Player != null) Cycle(Player.World, dir); }

        /// <summary>Free camera on or off (capture tool and the F key).</summary>
        public void SetFreeCamera(bool on) { if (on != FreeCamera) ToggleFree(); }

        void Cycle(World w, int dir)
        {
            if (!FreeCamera && Followed != _rig.Followed) _rig.Follow(w, Followed);
            FreeCamera = false;
            Followed = _rig.Cycle(w, dir);
        }

        void ToggleFree()
        {
            FreeCamera = !FreeCamera;
            if (!FreeCamera && Player != null) _rig.Follow(Player.World, Followed);
        }

        // ---------------------------------------------------------------- camera

        void UpdateCamera(World w, double dt)
        {
            if (_follow == null) _follow = FindAnyObjectByType<CameraFollowTarget>();
            if (_follow == null) return;
            bool normal = !FreeCamera && Followed == LocalSeat.Actor;
            if (normal)
            {
                _follow.ClearOverride();   // the recorded player's own gameplay camera
                _rig.Snap(w.X[Followed], w.Z[Followed]);
                return;
            }
            if (FreeCamera)
            {
                if (_move == null)
                {
                    InputActionMap map = ControlBindings.Map(InputSystem.actions);
                    _move = map != null ? map.FindAction("Move", false) : null;
                }
                if (_move != null && Message == null)
                {
                    Vector2 m = _move.ReadValue<Vector2>();
                    _rig.FreeFly(m.x, m.y, dt);
                }
            }
            _rig.Step(w, Driver.Alpha, dt);
            _follow.SetOverride(_rig.X, _rig.Z);
        }

        // ---------------------------------------------------------------- the bar (rebuilt only when what it says changes)

        void UpdateUi()
        {
            if (_canvas == null) Build();
            bool show = GameFlow.State != FlowState.Results && !FilmMode;   // the results screen of a replayed match stands alone
            if (_canvas.enabled != show) _canvas.enabled = show;
            if (!show) return;
            if (_canvas.worldCamera == null) _canvas.worldCamera = UnityEngine.Camera.main;
            UnityEngine.Camera cam = _canvas.worldCamera;
            float sw = cam != null ? cam.pixelWidth : Screen.width, sh = cam != null ? cam.pixelHeight : Screen.height;
            if (sw >= 1 && sh >= 1)
            {
                float s = HudLayout.Scale(sw, sh);
                if (Mathf.Abs(_canvas.scaleFactor - s) > 1e-4f) _canvas.scaleFactor = s;
            }
            double hz = Math.Max(1.0, Player.Header.SimHz);
            int second = (int)(Player.TicksPlayed / hz);
            if (second != _shownSecond || SpeedIndex != _shownSpeed || Paused != _shownPaused || Followed != _shownFollow || FreeCamera != _shownFree)
            {
                _shownSecond = second;
                _shownSpeed = SpeedIndex;
                _shownPaused = Paused;
                _shownFollow = Followed;
                _shownFree = FreeCamera;
                HudText.Set(_status, StatusLine(Paused, Player.TicksPlayed, Player.TotalTicks, hz, Speed, FreeCamera, FollowName(Followed)));
            }
            int fill = Player.TotalTicks > 0 ? (int)(BarW * Math.Min(1.0, (double)Player.TicksPlayed / Player.TotalTicks)) : 0;
            if (fill != _shownFill)
            {
                _shownFill = fill;
                _barFill.sizeDelta = new Vector2(fill, _barFill.sizeDelta.y);
            }
            ControlBindings.Sample();
            ControlDevice d = ControlBindings.ActiveDevice;
            if (d != _hintDevice)
            {
                _hintDevice = d;
                HudText.Set(_hint, HintLine(d == ControlDevice.Gamepad));
            }
            int cover = Rewinding ? (int)(100.0 * Player.TicksPlayed / Math.Max(1, SeekTarget)) : -1;
            if (cover != _shownCover)
            {
                _shownCover = cover;
                _cover.SetActive(cover >= 0);
                if (cover >= 0) HudText.Set(_coverText, "REWINDING  " + Math.Min(99, cover) + "%");
            }
            if (Message != _shownMessage)
            {
                _shownMessage = Message;
                _messagePanel.SetActive(Message != null);
                if (Message != null)
                    HudText.Set(_message, Message + (_hintDevice == ControlDevice.Gamepad ? "\nB  BACK TO THE MENU" : "\nBACKSPACE  BACK TO THE MENU"));
            }
        }

        string FollowName(int id)
        {
            ReplayHeader h = Player.Header;
            if (id < 0 || id >= h.Roster.Length) return "";
            ReplayActor a = h.Roster[id];
            string name = !string.IsNullOrEmpty(a.Name) ? a.Name.ToUpperInvariant() : (a.Human ? "PLAYER" : "BOT") + " " + (id + 1);
            Actor live = Player.World.Actors[id];
            return name + " · " + live.Cls.ToString().ToUpperInvariant() + " · " + live.Team.ToString().ToUpperInvariant();
        }

        /// <summary>"PLAYING  3:12 / 10:00   2X   FOLLOWING WILSON · WARRIOR · AZURE" (pure; tests).</summary>
        public static string StatusLine(bool paused, int ticks, int total, double hz, double speed, bool free, string follow)
        {
            string sp = speed == 0.5 ? "0.5X" : ((int)speed).ToString(System.Globalization.CultureInfo.InvariantCulture) + "X";
            return (paused ? "PAUSED" : "PLAYING") + "  " + ReplayWords.Length(ticks / hz) + " / " + ReplayWords.Length(total / hz) +
                   "   " + sp + "   " + (free ? "FREE CAMERA" : "FOLLOWING " + follow);
        }

        public static string HintLine(bool pad) => pad
            ? "A  PAUSE     D-PAD LEFT / RIGHT  10 S     D-PAD DOWN / UP  SPEED     LB / RB  PLAYER     VIEW  FREE CAMERA     B  EXIT"
            : "SPACE  PAUSE     J / L  10 S     - / +  SPEED     Q / E  PLAYER     F  FREE CAMERA     BACKSPACE  EXIT";

        const float BarX = 360f, BarY = 958f, BarW = 1200f, BarH = 104f;

        void Build()
        {
            var go = new GameObject("Replay Bar Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = 45;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = cam != null ? Mathf.Max(0.38f, cam.nearClipPlane * 1.25f) : 0.38f;
            var root = (RectTransform)go.transform;
            RectTransform space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color;

            // Rewind cover: the world re-simulates underneath.
            Image wash = MenuUi.Rt(root, "Rewind Cover").gameObject.AddComponent<Image>();
            MenuUi.Stretch(wash.rectTransform);
            wash.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.94f);
            wash.raycastTarget = false;
            _cover = wash.gameObject;
            _coverText = MenuUi.Contain(MenuUi.Label(wash.transform, "Rewind Text", CMTokens.Display, UiScale.TypeDisplay, cream, TextAnchor.MiddleCenter,
                0f, 0f, 10f, 10f, "REWINDING", 4f), 1);
            MenuUi.Stretch(_coverText.rectTransform);
            _cover.SetActive(false);

            // The bar: one contained panel at the bottom centre, over the HUD's bottom row.
            MenuUi.Panel(space, "Replay Bar", BarX - 24f, BarY - 12f, BarW + 48f, BarH + 24f);
            _status = MenuUi.Contain(MenuUi.Label(space, "Status", CMTokens.Ui, UiScale.TypeButton, cream, TextAnchor.MiddleLeft,
                BarX, BarY, BarW, 40f, "", 2f), 1);
            MenuUi.Shape(space, "Track", ShapeKind.RoundRect, BarX, BarY + 46f, BarW, 12f, CMTokens.WithAlpha(cream, 0.25f), 6f);
            VectorShape fillShape = MenuUi.Shape(space, "Fill", ShapeKind.RoundRect, BarX, BarY + 46f, BarW, 12f, brass, 6f);
            _barFill = fillShape.rectTransform;
            _barFill.pivot = new Vector2(0f, 0.5f);
            _barFill.anchoredPosition = new Vector2(BarX, -(BarY + 46f + 6f));
            _barFill.sizeDelta = new Vector2(0f, 12f);
            _hint = MenuUi.Contain(MenuUi.Label(space, "Hint", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleLeft,
                BarX, BarY + 66f, BarW, 34f, "", 2f), 1);

            // Message panel (out of step, damaged, end of an early-left recording).
            _messagePanel = MenuUi.Panel(space, "Message", 460f, 380f, 1000f, 240f).gameObject;
            _message = MenuUi.Contain(MenuUi.Label(_messagePanel.transform, "Message Text", CMTokens.Ui, UiScale.TypeLead, cream, TextAnchor.MiddleCenter,
                40f, 24f, 920f, 192f, "", 3f), 4);
            _messagePanel.SetActive(false);
        }
    }
}

using System;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// The one fixed-step accumulator (port of browser src/game/SimDriver.tsx). Steps the Sim on the Unity main thread
    /// only (U-047): no thread, no job. Per-tick order is MatchTick.Advance (F-06e). The Runtime writes Sim state only
    /// through MatchTick.Advance and HumanInputState.Fill. Match end: the driver keeps ticking (Step is inert).
    /// </summary>
    public sealed class SimDriverBehaviour : MonoBehaviour
    {
        /// <summary>Spiral-of-death guard on the accumulator input, seconds (SimClock.MaxDelta).</summary>
        private const double MaxDelta = SimClock.MaxDelta;
        /// <summary>Clamp for FrameDt (P8), seconds.</summary>
        private const double MaxFrameDt = 0.1;

        /// <summary>The match world, created in Awake.</summary>
        public World World { get; private set; }

        /// <summary>The config the world was created from (GameConfig.CreateDefault()).</summary>
        public GameConfig Config { get; private set; }

        /// <summary>Sim seconds per wall second; set from Config.DefaultSpeed in Awake.</summary>
        public double TimeScale = 2;

        /// <summary>true: actor 0 is bot-driven (MatchTick.Advance(w, null, scratch)).</summary>
        public bool HumanAutopilot;

        /// <summary>
        /// Capture tool (Editor CaptureTool): the next driver to wake starts with actor 0 on autopilot and the wall clock
        /// stopped (TimeScale 0), so only StepTicks advances the match and a capture does not depend on frame times.
        /// </summary>
        public static bool CaptureMode;

        /// <summary>Capture fixture (feat/skill-effects-1, -skillShots only): when set, actor 0's frame each tick is a copy of it.</summary>
        public static InputFrame ScriptedHumanFrame;

        /// <summary>This driver woke in capture mode (actor 0 on autopilot by design; HumanControlGuard leaves it alone).</summary>
        public bool CreatedInCaptureMode { get; private set; }

        /// <summary>Interpolation fraction between the previous and the current tick, 0..1.</summary>
        public double Alpha { get; private set; }

        /// <summary>min(Time.unscaledDeltaTime, 0.1), for the camera.</summary>
        public double FrameDt { get; private set; }

        /// <summary>
        /// Raised after every MatchTick.Advance with the world (view effects read the tick's events with an exact tick stamp).
        /// Subscribers read the world only; they never write it.
        /// </summary>
        public event Action<World> Ticked;

        private double _dt;
        /// <summary>DG-60: the accumulator with its catch-up cap (at most 4 ticks per frame at the game's pace).</summary>
        private readonly SimClock _clock = new SimClock();

        /// <summary>Sim time and ticks dropped by the catch-up cap since this match began (F3 overlay, perf report).</summary>
        public double DroppedSimSeconds => _clock.DroppedSimSeconds;
        public long DroppedTicks => _clock.DroppedTicks;
        private HumanInputSource _input;
        private readonly InputFrame _humanFrame = SimInput.EmptyInput();
        private readonly InputFrame _scratch = SimInput.EmptyInput();

        /// <summary>fix/movement-shiver-1: the "no shiver" detector over the live match (editor, development builds and automation
        /// runs only; read-only on the World). Each flag is one `[CM.Shiver]` log line for the play-test logs.</summary>
        private readonly ShiverWatch _shiver = new ShiverWatch();
        private static int _shiverLogOn = -1;   // read on the first tick (Unity calls are not allowed in a static initializer)

        /// <summary>fix/movement-shiver-2: flags the live detector raised since boot (read by captures and play-tests).</summary>
        public static int ShiverFlags { get; private set; }

        /// <summary>
        /// fix/movement-shiver-2: `[CM.Shiver]` lines go to the player log only on automation runs (-autoplay) or with
        /// -shiverLog. Round 1 also logged in the editor, and the PlayMode test runner fails a test on any log line it was not
        /// told to expect (six unrelated tests went red). The dedicated Sim tests (MovementShiverTests) are the gate.
        /// </summary>
        public static bool ShiverLogWanted()
        {
            if (AutoplayArgs.Current.Autoplay) return true;
            foreach (string arg in Environment.GetCommandLineArgs()) if (arg == "-shiverLog") return true;
            return false;
        }
        private World _shiverWorld;
        private static bool _shiverLogLines;
        private int _shiverLogged;
        private const int ShiverLogCap = 200;
        private readonly InputFrame _neutral = SimInput.EmptyInput();

        /// <summary>
        /// DG-01: the match the next driver to wake creates (GameBootstrap sets it from GameFlow just before adding the
        /// driver; Awake consumes it). Null = GameConfig defaults (seed 1337, Azure Warrior, 16v16 at the default pace).
        /// </summary>
        public static Flow.MatchRequest NextMatch;

        /// <summary>The request this world was created from (null: config defaults).</summary>
        public Flow.MatchRequest Request { get; private set; }

        private void Awake()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            cfg.Render.CameraYawDeg = CriticalMass.Runtime.Camera.CameraHeading.Chosen;   // creator's angle (135) or classic (45)
            AutoplayArgs.ApplyCameraYaw(cfg);   // -cameraYaw: automation runs only
            CriticalMass.Runtime.Camera.CameraHeading.Active = cfg.Render.CameraYawDeg;   // controls and minimap match the screen
            Config = cfg;
            Flow.MatchRequest req = NextMatch;
            NextMatch = null;
            Request = req;
            // b13-replay: a saved replay builds its own world from the recorded starting conditions; this driver only shows
            // it (Replay.ReplayPlayback ticks it with the recorded inputs, as OnlineRunner does for a room).
            if (req != null && req.Replay && Replay.ReplayPlayback.Pending != null)
            {
                _replay = Replay.ReplayPlayback.TakePending();
                World = _replay.World;
                World.Config.Render.CameraYawDeg = cfg.Render.CameraYawDeg;   // view-only setting; the Sim never reads Render
                Config = World.Config;
                TimeScale = req.Pace > 0 ? req.Pace : cfg.DefaultSpeed;
                HumanAutopilot = false;
                _dt = SimStep.Dt(Config);
                Alpha = 0;
                RuntimeCounters.Reset();
                gameObject.AddComponent<Replay.ReplayPlayback>();
                return;
            }
            // online-rooms-2: in an online room the lockstep session owns the world and its clock; this driver only shows
            // it (the session ticks in OnlineRunner, inputs come from HumanInputSource through FillOnline).
            CriticalMass.Net.LockstepSession online = Online.OnlineMatch.Session;
            if (ShowsOnlineWorld(online != null, req))
            {
                _online = online;
                World = _online.World;
                World.Config.Render.CameraYawDeg = cfg.Render.CameraYawDeg;   // view-only setting; the Sim never reads Render
                Config = World.Config;
                TimeScale = CriticalMass.Net.LockstepSession.WallPace(_online.Setup, World.Config);
                HumanAutopilot = false;
                _dt = SimStep.Dt(Config);
                Alpha = 0;
                RuntimeCounters.Reset();
                Online.OnlineMatch.Ticked += ForwardTicked;
                Online.OnlineMatch.LocalInput = FillOnline;
                OnlineFeel.Active = true;
                gameObject.AddComponent<Online.OnlineMatchOverlay>();
                Online.OnlineChatHud.Lane = CriticalMass.Runtime.Hud.HudZones.Chat;   // the match HUD's own chat lane
                Online.OnlineChatHud.Centred = false;
                Online.OnlineChatHud.Visible = true;
                return;
            }
            World = World.CreateWorld(req != null ? req.ToWorldOptions(cfg) : new WorldOptions
            {
                Seed = cfg.Seed,
                TeamSize = cfg.TeamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = cfg,
            });
            TimeScale = req != null && req.Pace > 0 ? req.Pace : cfg.DefaultSpeed;
            if (req != null && req.Backdrop) HumanAutopilot = true;
            if (CaptureMode)
            {
                CreatedInCaptureMode = true;
                HumanAutopilot = true;
                TimeScale = 0;
            }
            _dt = SimStep.Dt(cfg);
            Alpha = 0;
            RuntimeCounters.Reset();
        }

        /// <summary>The HumanInputSource on this GameObject (added after this component by GameBootstrap, so resolved lazily).</summary>
        private HumanInputSource InputSource
        {
            get
            {
                if (_input == null) _input = GetComponent<HumanInputSource>();
                return _input;
            }
        }

        /// <summary>online-rooms-2: the lockstep session this driver shows (null in single player).</summary>
        private CriticalMass.Net.LockstepSession _online;

        /// <summary>online-rooms-2: true while this driver shows an online match (pace, pause and speed are the room's).</summary>
        public bool IsOnline => _online != null;

        /// <summary>b13-replay: the replay this driver shows (null in a played match); Replay.ReplayPlayback ticks it.</summary>
        private CriticalMass.Net.Replay.ReplayPlayer _replay;

        /// <summary>b13-replay: true while this driver shows a saved replay.</summary>
        public bool IsReplay => _replay != null;

        /// <summary>b13-replay: the replay's player (null in a played match).</summary>
        public CriticalMass.Net.Replay.ReplayPlayer ReplayPlayer => _replay;

        /// <summary>b13-replay: ReplayPlayback ran one replay tick: the same per-tick notification a played tick raises.</summary>
        internal void ReplayTicked(World w)
        {
            if (w != World) return;
            FrameCost.FrameTicks++;
            Ticked?.Invoke(w);
        }

        /// <summary>b13-replay: ReplayPlayback's interpolation fraction for this frame.</summary>
        internal void ReplayAlpha(double alpha) => Alpha = ViewAlpha.Hold(alpha, ViewAlpha.Ended(World));   // b15: still after the end

        private void ForwardTicked(World w)
        {
            if (w != World) return;
            FrameCost.FrameTicks++;
            Ticked?.Invoke(w);
        }

        /// <summary>online-rooms-2: this machine's player's frame for the next lockstep tick (neutral while a menu is open).</summary>
        private void FillOnline(InputFrame into)
        {
            HumanInputSource input = InputSource;
            // Typing a chat line: neutral inputs, so letters never move the fighter.
            if (input != null && !input.State.Paused && !Online.OnlineChatHud.Typing) input.State.Fill(into);
            else
            {
                into.CopyFrom(_neutral);
                into.AimX = double.NaN;
                into.AimZ = double.NaN;
            }
            OnlineFeel.Note(into.AimX, into.AimZ);   // online-rooms-9: view-only immediate turn (ActorViews)
        }

        private void OnDestroy()
        {
            if (_online == null) return;
            Online.OnlineChatHud.Visible = false;
            OnlineFeel.Reset();
            Online.OnlineMatch.Ticked -= ForwardTicked;
            if (Online.OnlineMatch.LocalInput == (Action<InputFrame>)FillOnline) Online.OnlineMatch.LocalInput = null;
            // Leaving the match scene (results, menu, quit) leaves the room.
            if (Online.OnlineMatch.Session == _online) Online.OnlineMatch.Leave();
        }

        private void Update()
        {
            // online-rooms-3: while an online match runs (or the game is a hidden online test peer) no other world ticks:
            // the online session is the only thing stepping the Sim.
            if (_online == null && (Online.OnlineMatch.Session != null || Online.OnlineHeadless.Active)) return;
            if (_replay != null)
            {
                float rc = Time.captureDeltaTime;
                FrameDt = Math.Min(rc > 0f ? rc : Time.unscaledDeltaTime, MaxFrameDt);
                return;   // b13-replay: ReplayPlayback ticks the replay world and sets Alpha
            }
            if (_online != null)
            {
                float cd = Time.captureDeltaTime;
                FrameDt = Math.Min(cd > 0f ? cd : Time.unscaledDeltaTime, MaxFrameDt);
                Alpha = ViewAlpha.Hold(_online.Alpha, ViewAlpha.Ended(World));   // the session ticks in OnlineRunner; never paused, never sped up (b15: still after the end)
                return;
            }
            if (CriticalMass.Runtime.Game.LoadTimer.Armed) CriticalMass.Runtime.Game.LoadTimer.FirstUpdate();   // perf/start-hitch-1: every Start() of the load frame has run
            // fix/facing-frame-timing: a capture clock (Time.captureDeltaTime: movie capture, the sanity tests' fixed load
            // frames, the frame-rate tests' fake clocks) drives the sim too. Unity's capture clock does not touch
            // unscaledDeltaTime (documented), so the driver used to run on real frame time under it: the load frames
            // advanced the accumulator by however long the runner took, the tick cap pinned the tick (4) but not the
            // interpolation alpha (0.44 / 0.54 / ... per load on GitHub), and every drawn pose after the stopped clock was
            // offset by that fraction of a tick: the FacingSanity reads changed from run to run with the tick pinned.
            float capture = Time.captureDeltaTime;
            double unscaled = capture > 0f ? capture : Time.unscaledDeltaTime;
            FrameDt = Math.Min(unscaled, MaxFrameDt);
            HumanInputSource input = InputSource;
            // perf-smooth-1: no tick while the world art is still building over frames (a player's match start, behind the
            // loading screen): the match starts from the same state, only later in wall time.
            // first-run-warm-2: nor while the first-run warm-up runs (FirstRunWarm.HoldsSim, capped), the automated match included.
            bool paused = (input != null && input.State.Paused) || View.Dressing.WorldView.Building || View.Dressing.WorldView.AwaitsWorld(World) || FirstRunWarm.HoldsSim ||
                          BackdropHeld(Request != null && Request.Backdrop, CriticalMass.Runtime.Hud.TitleBackdrop.Frozen);
            // DG-60: after a hitch at most SimClock.TickCap ticks run this frame and the rest of the backlog is dropped
            // (counted), so one slow frame never makes the next one slow too. Same inputs per tick: determinism holds.
            int ticks = _clock.Advance(unscaled, TimeScale, _dt, paused);
            for (int i = 0; i < ticks; i++) Tick(input);
            RuntimeCounters.SimDroppedSeconds = _clock.DroppedSimSeconds;
            RuntimeCounters.SimDroppedTicks = _clock.DroppedTicks;
            Alpha = ViewAlpha.Hold(_clock.Alpha(_dt), ViewAlpha.Ended(World));   // b15 world look: no shiver behind the result screen
        }

        /// <summary>
        /// b12-x1 (title idle CPU): a backdrop world (the bot match behind the title and the menus, its own world, never a
        /// real match) takes no tick while the title shows its still (TitleBackdrop.Frozen). TitleBackdrop also sets
        /// TimeScale 0, but an automation run's -autoplaySpeed rewrites TimeScale every frame, so the hold lives here too.
        /// Wall time only: the backdrop resumes from the same state; no rule, no checksum (pure; tests).
        /// </summary>
        public static bool BackdropHeld(bool backdropWorld, bool titleFrozen) => backdropWorld && titleFrozen;

        /// <summary>b12-x1 (pure; tests): a backdrop request never takes the online session's world (the Awake rule).</summary>
        public static bool ShowsOnlineWorld(bool sessionActive, Flow.MatchRequest req) => sessionActive && (req == null || !req.Backdrop);

        /// <summary>b12-x1 (pure; tests): simulated ticks per wall second at a time scale (0 while held).</summary>
        public static double TicksPerWallSecond(double timeScale, double simHz, bool held) => held ? 0.0 : Math.Max(0.0, timeScale) * simHz;

        /// <summary>Runs n ticks immediately (smoke test); the accumulator and Alpha are left as they are.</summary>
        public void StepTicks(int n)
        {
            if (_online != null || _replay != null) return;   // online-rooms-2: only the lockstep session steps an online world (b13: only ReplayPlayback a replay)
            HumanInputSource input = InputSource;
            for (int i = 0; i < n; i++) Tick(input);
        }

        private void WatchShiver()
        {
            if (!ReferenceEquals(_shiverWorld, World))
            {
                _shiverWorld = World;
                _shiver.Clear();
                _shiverLogged = 0;
            }
            int before = _shiver.Flags.Count;
            _shiver.Observe(World);
            for (int i = before; i < _shiver.Flags.Count && _shiverLogged < ShiverLogCap; i++, _shiverLogged++)
                if (_shiverLogLines) Debug.Log(ShiverWatch.Format(_shiver.Flags[i]));
            ShiverFlags += _shiver.NewThisTick;
        }

        private void Tick(HumanInputSource input)
        {
            if (input != null) input.State.Fill(_humanFrame);
            else
            {
                // No HumanInputSource on this object: a neutral frame with no aim (non-finite, D-011).
                _humanFrame.CopyFrom(_neutral);
                _humanFrame.AimX = double.NaN;
                _humanFrame.AimZ = double.NaN;
            }
            // feat/skill-effects-1: the -skillShots director (View/Skills/SkillCapture.cs) presses skills through the real input
            // path. Null in normal play.
            if (ScriptedHumanFrame != null) _humanFrame.CopyFrom(ScriptedHumanFrame);
            // Physics audit M7 / DG-101: count ability presses the sim will refuse (read-only on the World) for the HUD's cue.
            if (input != null && !HumanAutopilot) AbilityDenial.Observe(_humanFrame.Ability, World, LocalSeat.Actor, _dt);
            FrameCost.Mark c0 = FrameCost.Enter();
            long lt = CriticalMass.Runtime.Game.LoadTimer.WatchingTicks ? CriticalMass.Runtime.Game.LoadTimer.Now() : 0;   // perf/start-hitch-1: the first ticks after a load, timed
            using (CriticalMass.Net.SimThreadGuard.Enter()) MatchTick.Advance(World, HumanAutopilot ? null : _humanFrame, _scratch);
            PlayerSeatLogHook.AfterTick?.Invoke(World, HumanAutopilot ? null : _humanFrame);   // px lane T: player-seat telemetry (null unless the dev logger is on)
            if (lt != 0) CriticalMass.Runtime.Game.LoadTimer.TickDone(lt);
            if (_shiverLogOn < 0)
            {
                _shiverLogLines = ShiverLogWanted();
                _shiverLogOn = _shiverLogLines || Application.isEditor || Debug.isDebugBuild ? 1 : 0;
            }
            if (_shiverLogOn == 1) WatchShiver();
            FrameCost.Exit(FrameCost.Slot.Sim, c0);
            FrameCost.FrameTicks++;
            c0 = FrameCost.Enter();
            Ticked?.Invoke(World);
            FrameCost.Exit(FrameCost.Slot.Feeds, c0);
        }
    }
}

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
            Config = cfg;
            Flow.MatchRequest req = NextMatch;
            NextMatch = null;
            Request = req;
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

        private void Update()
        {
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
            bool paused = input != null && input.State.Paused;
            // DG-60: after a hitch at most SimClock.TickCap ticks run this frame and the rest of the backlog is dropped
            // (counted), so one slow frame never makes the next one slow too. Same inputs per tick: determinism holds.
            int ticks = _clock.Advance(unscaled, TimeScale, _dt, paused);
            for (int i = 0; i < ticks; i++) Tick(input);
            RuntimeCounters.SimDroppedSeconds = _clock.DroppedSimSeconds;
            RuntimeCounters.SimDroppedTicks = _clock.DroppedTicks;
            Alpha = _clock.Alpha(_dt);
        }

        /// <summary>Runs n ticks immediately (smoke test); the accumulator and Alpha are left as they are.</summary>
        public void StepTicks(int n)
        {
            HumanInputSource input = InputSource;
            for (int i = 0; i < n; i++) Tick(input);
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
            // Physics audit M7 / DG-101: count ability presses the sim will refuse (read-only on the World) for the HUD's cue.
            if (input != null && !HumanAutopilot) AbilityDenial.Observe(_humanFrame.Ability, World, 0, _dt);
            long c0 = FrameCost.Begin();
            MatchTick.Advance(World, HumanAutopilot ? null : _humanFrame, _scratch);
            FrameCost.End(FrameCost.Slot.Sim, c0);
            c0 = FrameCost.Begin();
            Ticked?.Invoke(World);
            FrameCost.End(FrameCost.Slot.Feeds, c0);
        }
    }
}

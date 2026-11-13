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
            double unscaled = Time.unscaledDeltaTime;
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
            MatchTick.Advance(World, HumanAutopilot ? null : _humanFrame, _scratch);
            Ticked?.Invoke(World);
        }
    }
}

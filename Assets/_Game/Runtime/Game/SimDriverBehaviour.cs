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
        /// <summary>Spiral-of-death guard on the accumulator input, seconds.</summary>
        private const double MaxDelta = 0.25;
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

        private double _dt;
        private double _acc;
        private HumanInputSource _input;
        private readonly InputFrame _humanFrame = SimInput.EmptyInput();
        private readonly InputFrame _scratch = SimInput.EmptyInput();
        private readonly InputFrame _neutral = SimInput.EmptyInput();

        private void Awake()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            Config = cfg;
            World = World.CreateWorld(new WorldOptions
            {
                Seed = cfg.Seed,
                TeamSize = cfg.TeamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = cfg,
            });
            TimeScale = cfg.DefaultSpeed;
            if (CaptureMode)
            {
                HumanAutopilot = true;
                TimeScale = 0;
            }
            _dt = SimStep.Dt(cfg);
            _acc = 0;
            Alpha = 0;
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
            if (!paused) _acc += Math.Min(unscaled, MaxDelta) * TimeScale;
            while (_acc >= _dt)
            {
                Tick(input);
                _acc -= _dt;
            }
            Alpha = _acc / _dt;
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
        }
    }
}

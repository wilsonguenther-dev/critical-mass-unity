using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>Where a step stands in the current trial.</summary>
    public enum TrialStepState { Pending, Active, Done, Skipped, Off }

    /// <summary>
    /// Runs a step list against a live world: one active step at a time, completion read from sim state once per sim tick
    /// (TrialChecks), skip on request, and steps whose mechanic or precondition is missing switched off with one log line
    /// each. Pure C#: reads the World, never writes it, so tests drive it headless next to MatchTick.Advance.
    /// </summary>
    public sealed class TrialRunner
    {
        public readonly TrialStep[] Steps;
        public readonly TrialStepState[] States;
        /// <summary>Why each Off step is off ("" for the others).</summary>
        public readonly string[] OffReasons;
        public readonly TrialProbe Probe = new TrialProbe();

        /// <summary>Index of the active step; -1 before Begin; Steps.Length once finished.</summary>
        public int Current { get; private set; } = -1;
        public bool Started => Current >= 0;
        public bool Finished => Current >= Steps.Length;
        public TrialStep ActiveStep => Current >= 0 && Current < Steps.Length ? Steps[Current] : null;
        /// <summary>Match time (sim seconds) when Begin ran.</summary>
        public double StartedAt { get; private set; }

        /// <summary>A step became active (index).</summary>
        public event Action<int> StepStarted;
        /// <summary>A step ended (index, Done or Skipped).</summary>
        public event Action<int, TrialStepState> StepEnded;
        /// <summary>The last step ended.</summary>
        public event Action Completed;

        readonly Action<string> _log;
        readonly Func<TrialFeature, bool> _has;
        byte[] _snapshot;
        int _lastTick = int.MinValue;

        /// <param name="log">Receives one line per switched-off or skipped step (null = UnityEngine.Debug.Log).</param>
        /// <param name="has">Feature probe (null = TrialFeatures.Has).</param>
        public TrialRunner(TrialStep[] steps, Action<string> log = null, Func<TrialFeature, bool> has = null)
        {
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            States = new TrialStepState[steps.Length];
            OffReasons = new string[steps.Length];
            for (int i = 0; i < OffReasons.Length; i++) OffReasons[i] = string.Empty;
            if (log == null) log = DefaultLog;
            if (has == null) has = TrialFeatures.Has;
            _log = log;
            _has = has;
        }

        static void DefaultLog(string line) => (Trial.InfoLog ?? UnityEngine.Debug.Log)(line);

        /// <summary>Probes every step's mechanic, switches off the missing ones, and starts the first runnable step.</summary>
        public void Begin(World w)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            if (_snapshot == null || _snapshot.Length != w.Map.Cells.Length) _snapshot = new byte[w.Map.Cells.Length];
            StartedAt = w.Match.Time;
            _lastTick = int.MinValue;
            for (int i = 0; i < Steps.Length; i++)
            {
                States[i] = TrialStepState.Pending;
                OffReasons[i] = string.Empty;
                TrialStep s = Steps[i];
                if (!_has(s.Needs)) SwitchOff(i, TrialFeatures.MissingReason(s.Needs));
            }
            StartAt(w, 0);
        }

        /// <summary>Checks the active step once per sim tick (repeat calls within one tick do nothing). True when a step ended.</summary>
        public bool Observe(World w)
        {
            TrialStep s = ActiveStep;
            if (s == null || w.Tick == _lastTick) return false;
            _lastTick = w.Tick;
            if (!TrialChecks.Observe(w, s, Probe, _snapshot)) return false;
            End(w, TrialStepState.Done);
            return true;
        }

        /// <summary>Skips the active step (the player chose to).</summary>
        public void Skip(World w)
        {
            TrialStep s = ActiveStep;
            if (s == null) return;
            _log("[Trial] step '" + s.Id + "' skipped by the player");
            End(w, TrialStepState.Skipped);
        }

        /// <summary>Skips every remaining step.</summary>
        public void SkipAll(World w)
        {
            while (ActiveStep != null) Skip(w);
        }

        /// <summary>The active step's arrow target.</summary>
        public bool Target(World w, out double x, out double z)
        {
            TrialStep s = ActiveStep;
            if (s == null)
            {
                x = z = 0;
                return false;
            }
            return TrialChecks.Target(w, s, Probe, out x, out z);
        }

        /// <summary>Count of steps in a state.</summary>
        public int Count(TrialStepState st)
        {
            int n = 0;
            for (int i = 0; i < States.Length; i++)
                if (States[i] == st) n++;
            return n;
        }

        void End(World w, TrialStepState how)
        {
            int i = Current;
            States[i] = how;
            StepEnded?.Invoke(i, how);
            StartAt(w, i + 1);
        }

        void SwitchOff(int i, string reason)
        {
            States[i] = TrialStepState.Off;
            OffReasons[i] = reason;
            _log("[Trial] step '" + Steps[i].Id + "' is off: " + reason);
        }

        void StartAt(World w, int from)
        {
            for (int i = from; i < Steps.Length; i++)
            {
                if (States[i] == TrialStepState.Off) continue;
                TrialStep s = Steps[i];
                TrialChecks.Begin(w, s, Probe, _snapshot);
                string reason = TrialChecks.CannotRunReason(w, s, Probe);
                if (reason.Length > 0)
                {
                    SwitchOff(i, reason);
                    continue;
                }
                States[i] = TrialStepState.Active;
                Current = i;
                _lastTick = int.MinValue;
                StepStarted?.Invoke(i);
                return;
            }
            bool wasRunning = Current < Steps.Length;
            Current = Steps.Length;
            if (wasRunning) Completed?.Invoke();
        }
    }
}

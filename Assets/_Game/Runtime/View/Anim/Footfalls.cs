using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>What touched the ground.</summary>
    public enum FootfallKind : byte { Step, Land, KaijuStep }

    /// <summary>
    /// One foot contact (DG-93): a fighter's toe or the Kaiju's foot reaching the ground at a clip's measured touchdown
    /// phase, or a jump landing. Strength and Radius follow the WorldForce table in contract C1 (Kaiju step 0.6 / 3.0 u,
    /// landing 0.4 / 1.2 u, fighter step 0.12 / 0.5 u). View only; nothing here feeds the sim.
    /// </summary>
    public struct Footfall
    {
        public int Tick;
        /// <summary>Actor id, or -2 for the Kaiju.</summary>
        public int Actor;
        public FootfallKind Kind;
        /// <summary>World position of the foot bone (or the body when the bones were not evaluated this frame).</summary>
        public Vector3 Position;
        public float Strength;
        public float Radius;
        public bool Left;
        public TeamId Team;
    }

    /// <summary>
    /// Receives footfalls. The world-force bus (DG-77, WorldForceBus) implements this and turns each into a
    /// WorldForce { Shape = Footfall / Landing }; audio and dust can listen directly. Kept tiny so this lane compiles
    /// without that system.
    /// </summary>
    public interface IFootfallSink
    {
        void OnFootfall(in Footfall f);
    }

    /// <summary>
    /// The footfall dispatcher: a fixed list of sinks (no allocation), a per-frame cap so a crowd never floods listeners
    /// (the Kaiju and landings always pass; fighter steps stop at StepCapPerFrame).
    /// </summary>
    public static class Footfalls
    {
        public const int MaxSinks = 8;
        public const int StepCapPerFrame = 12;
        public const float KaijuStepStrength = 0.6f, KaijuStepRadius = 3.0f, LandStrength = 0.4f, LandRadius = 1.2f,
            StepStrength = 0.12f, StepRadius = 0.5f;

        static readonly IFootfallSink[] _sinks = new IFootfallSink[MaxSinks];
        static int _count, _frame = -1, _stepsThisFrame;

        /// <summary>Footfalls raised since start (tests and the capture report).</summary>
        public static long Emitted { get; private set; }
        public static long Dropped { get; private set; }

        /// <summary>The last footfall raised (tests).</summary>
        public static Footfall Last { get; private set; }

        public static void Register(IFootfallSink s)
        {
            if (s == null) return;
            for (int i = 0; i < _count; i++) if (ReferenceEquals(_sinks[i], s)) return;
            if (_count < MaxSinks) _sinks[_count++] = s;
            else Debug.LogWarning("[Footfalls] sink list full (" + MaxSinks + ")");
        }

        public static void Unregister(IFootfallSink s)
        {
            for (int i = 0; i < _count; i++)
                if (ReferenceEquals(_sinks[i], s))
                {
                    _sinks[i] = _sinks[--_count];
                    _sinks[_count] = null;
                    return;
                }
        }

        /// <summary>Raises one footfall to every sink. False when a fighter step was over the frame cap.</summary>
        public static bool Emit(in Footfall f)
        {
            int frame = Time.frameCount;
            if (frame != _frame) { _frame = frame; _stepsThisFrame = 0; }
            if (f.Kind == FootfallKind.Step && ++_stepsThisFrame > StepCapPerFrame)
            {
                Dropped++;
                return false;
            }
            Emitted++;
            Last = f;
            for (int i = 0; i < _count; i++) _sinks[i].OnFootfall(in f);
            return true;
        }

        /// <summary>Clears sinks and counters (tests, domain reload off).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForTests()
        {
            for (int i = 0; i < MaxSinks; i++) _sinks[i] = null;
            _count = 0;
            Emitted = Dropped = 0;
            _frame = -1;
        }
    }
}

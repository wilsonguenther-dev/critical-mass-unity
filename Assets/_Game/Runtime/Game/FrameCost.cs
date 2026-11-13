using System.Diagnostics;
using System.Text;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// Closer (integration batch 4): wall-time accumulators for the few per-frame systems that can dominate an idle frame
    /// (the backdrop sim and its feeds, the immediate-mode mechanics and depth views, the actor views), so a player log can
    /// say where the title's CPU goes without a development build. Two Stopwatch reads per section; no allocation.
    /// The autoplay runner logs and resets them when the title hides ([CM.Cost]).
    /// </summary>
    public static class FrameCost
    {
        public enum Slot { Sim = 0, Feeds = 1, MechanicsView = 2, DepthView = 3, ActorViews = 4, Sound = 5, Count = 6 }

        static readonly double[] Ms = new double[(int)Slot.Count];
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;
        static int _frameAtReset = -1;

        public static long Begin() => Stopwatch.GetTimestamp();

        public static void End(Slot s, long started) => Ms[(int)s] += (Stopwatch.GetTimestamp() - started) * TickMs;

        public static void Reset()
        {
            for (int i = 0; i < Ms.Length; i++) Ms[i] = 0;
            _frameAtReset = UnityEngine.Time.frameCount;
        }

        /// <summary>"sim 3.1, feeds 0.4, ..." in ms per frame since the last Reset.</summary>
        public static string Report()
        {
            int frames = UnityEngine.Mathf.Max(1, UnityEngine.Time.frameCount - _frameAtReset);
            var sb = new StringBuilder();
            sb.Append("frames ").Append(frames);
            for (int i = 0; i < Ms.Length; i++)
                sb.Append(", ").Append(((Slot)i).ToString()).Append(' ').Append((Ms[i] / frames).ToString("0.00")).Append(" ms");
            return sb.ToString();
        }
    }
}

using System.Diagnostics;
using System.Text;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// Closer (integration batch 4): wall-time accumulators for the per-frame systems that can dominate a frame, so a
    /// release player log can say where the CPU goes without a development build. Two Stopwatch reads per section; no
    /// allocation. The autoplay runner logs and resets the totals when the title hides ([CM.Cost]).
    /// <para>perf-smooth-1: every slot also keeps this frame's milliseconds and, while FrameRecorder records, this frame's
    /// managed allocation (bytes, from FrameRecorder's allocation counter), so the frame CSV can say which system made a
    /// slow frame and which one allocated. Use Enter and Exit (a struct mark: no allocation).</para>
    /// </summary>
    public static class FrameCost
    {
        public enum Slot
        {
            Sim = 0, Feeds = 1, MechanicsView = 2, DepthView = 3, ActorViews = 4, Sound = 5,
            Hud = 6, HudInteract = 7, NameTags = 8, KillFeed = 9, Scoreboard = 10, Vfx = 11, BigMoments = 12,
            Camera = 13, Moments = 14, CombatLog = 15, Rings = 16, Ground = 17, World = 18, Count = 19,
        }

        /// <summary>A section start: timestamp and (while recording) the allocation counter.</summary>
        public readonly struct Mark
        {
            public readonly long Ts, Alloc;
            public Mark(long ts, long alloc) { Ts = ts; Alloc = alloc; }
        }

        static readonly double[] Ms = new double[(int)Slot.Count];
        /// <summary>This frame's ms and bytes per slot (FrameRecorder reads and clears them at the end of each frame).</summary>
        public static readonly double[] FrameMs = new double[(int)Slot.Count];
        public static readonly long[] FrameAlloc = new long[(int)Slot.Count];
        /// <summary>Sim ticks run this frame (FrameRecorder clears it).</summary>
        public static int FrameTicks;
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;
        static int _frameAtReset = -1;

        public static long Begin() => Stopwatch.GetTimestamp();

        public static void End(Slot s, long started)
        {
            double ms = (Stopwatch.GetTimestamp() - started) * TickMs;
            Ms[(int)s] += ms;
            FrameMs[(int)s] += ms;
        }

        public static Mark Enter() => new Mark(Stopwatch.GetTimestamp(), FrameRecorder.Active ? FrameRecorder.AllocatedNow() : 0);

        public static void Exit(Slot s, in Mark m)
        {
            End(s, m.Ts);
            if (FrameRecorder.Active) FrameAlloc[(int)s] += FrameRecorder.AllocatedNow() - m.Alloc;
        }

        /// <summary>Clears this frame's per-slot numbers (FrameRecorder, after it read them).</summary>
        public static void ClearFrame()
        {
            for (int i = 0; i < FrameMs.Length; i++)
            {
                FrameMs[i] = 0;
                FrameAlloc[i] = 0;
            }
            FrameTicks = 0;
        }

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

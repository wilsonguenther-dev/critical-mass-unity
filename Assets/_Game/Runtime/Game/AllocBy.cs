using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// online-offline-cost-1: -allocBy (automation only) names the script that allocates or costs time every frame,
    /// without the editor. Two sources, both logged every <see cref="ReportSeconds"/> s as
    /// "[CM.AllocBy] ..." lines (top <see cref="Top"/> each):
    /// <list type="number">
    /// <item>Engine markers: every available profiler marker whose name holds ".LateUpdate()" or ".Update()" (the
    /// per-script samples, for example "ActorViews.LateUpdate() [Invoke]"), found at run time with
    /// ProfilerRecorderHandle.GetAvailable and re-scanned each report (markers appear when a script first runs). Time
    /// only: a ProfilerRecorder gives a marker's time, not its allocations. Needs a Development build; a release
    /// player has none, and the log says how many it found.</item>
    /// <item>Our own scopes: every LateUpdate and Update in Runtime opens <see cref="Late"/> or <see cref="Upd"/> as
    /// its first line, which measures bytes allocated (FrameRecorder's counter) and time per script type. Works in
    /// any build; nested calls (base.Update) count once, in the outermost script.</item>
    /// </list>
    /// Off (the normal game): Late and Upd return an empty struct after one bool test; nothing is recorded or made.
    /// Main thread only, like the methods it wraps.
    /// </summary>
    public static class AllocBy
    {
        public const string Flag = "-allocby";
        public const float ReportSeconds = 10f;
        public const int Top = 15;

        public static bool On { get; private set; }

        /// <summary>One open measurement; disposing it (the end of the wrapped method) books it.</summary>
        public readonly struct Scope : IDisposable
        {
            readonly int _slot;
            readonly long _t0, _a0;

            internal Scope(int slot, long t0, long a0)
            {
                _slot = slot;
                _t0 = t0;
                _a0 = a0;
            }

            public void Dispose()
            {
                if (_slot != 0) End(_slot, _t0, _a0);
            }
        }

        /// <summary>First line of a LateUpdate: <c>using var __ab = AllocBy.Late(this);</c></summary>
        public static Scope Late(object self) => On ? Begin(self, true) : default;

        /// <summary>First line of an Update: <c>using var __ab = AllocBy.Upd(this);</c></summary>
        public static Scope Upd(object self) => On ? Begin(self, false) : default;

        static int _depth;
        static readonly Dictionary<Type, int> LateSlots = new Dictionary<Type, int>();
        static readonly Dictionary<Type, int> UpdSlots = new Dictionary<Type, int>();
        static readonly List<string> Names = new List<string> { "" };
        static readonly List<long> Bytes = new List<long> { 0 };
        static readonly List<long> Ticks = new List<long> { 0 };
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        static Scope Begin(object self, bool late)
        {
            if (self == null || _depth > 0)
            {
                _depth++;
                return new Scope(-1, 0, 0);
            }
            Type t = self.GetType();
            Dictionary<Type, int> map = late ? LateSlots : UpdSlots;
            if (!map.TryGetValue(t, out int slot))
            {
                slot = Names.Count;
                Names.Add(t.Name + (late ? ".LateUpdate" : ".Update"));
                Bytes.Add(0);
                Ticks.Add(0);
                map[t] = slot;
            }
            _depth++;
            return new Scope(slot, Stopwatch.GetTimestamp(), FrameRecorder.AllocatedNow());
        }

        static void End(int slot, long t0, long a0)
        {
            _depth = Math.Max(0, _depth - 1);
            if (slot < 0) return;
            long a = FrameRecorder.AllocatedNow() - a0;
            if (a > 0) Bytes[slot] += a;   // a collection inside the method reads negative on the heap counter: count 0
            Ticks[slot] += Stopwatch.GetTimestamp() - t0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (!AutoplayArgs.HasAutomationFlag(Flag)) return;
            FrameRecorder.EnsureAllocCounter();
            On = true;
            var go = new GameObject("CriticalMass AllocBy") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AllocByRunner>();
            Debug.Log($"[CM.AllocBy] on: development build {Debug.isDebugBuild}, allocation counter {FrameRecorder.AllocSource}, " +
                      $"report every {ReportSeconds:0} s, top {Top}");
        }

        /// <summary>Per-frame marker reads and the periodic report.</summary>
        sealed class AllocByRunner : MonoBehaviour
        {
            readonly List<ProfilerRecorderHandle> _handles = new List<ProfilerRecorderHandle>();
            readonly HashSet<string> _known = new HashSet<string>();
            readonly List<ProfilerRecorder> _recs = new List<ProfilerRecorder>();
            readonly List<string> _recNames = new List<string>();
            readonly List<long> _recNs = new List<long>();
            ProfilerRecorder _gcFrame;
            long _gcFrameBytes;
            int _frames;
            float _nextReport;

            void Start()
            {
                _gcFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                Scan();
                _nextReport = Time.unscaledTime + ReportSeconds;
            }

            void Scan()
            {
                _handles.Clear();
                ProfilerRecorderHandle.GetAvailable(_handles);
                foreach (ProfilerRecorderHandle h in _handles)
                {
                    ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                    string n = d.Name;
                    if (string.IsNullOrEmpty(n) || _known.Contains(n)) continue;
                    if (n.IndexOf(".LateUpdate()", StringComparison.Ordinal) < 0 && n.IndexOf(".Update()", StringComparison.Ordinal) < 0) continue;
                    _known.Add(n);
                    var r = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default);   // Default sums all samples in a frame
                    if (!r.Valid) { r.Dispose(); continue; }
                    r.Start();
                    _recs.Add(r);
                    _recNames.Add(n);
                    _recNs.Add(0);
                }
            }

            void LateUpdate()
            {
                // Runs at the default order; the engine markers read here are last frame's totals, which is what is wanted.
                for (int i = 0; i < _recs.Count; i++) _recNs[i] += _recs[i].LastValue;
                if (_gcFrame.Valid) _gcFrameBytes += _gcFrame.LastValue;
                _frames++;
                if (Time.unscaledTime < _nextReport) return;
                _nextReport = Time.unscaledTime + ReportSeconds;
                Report();
                Scan();
            }

            void Report()
            {
                int n = Math.Max(1, _frames);
                var sb = new StringBuilder(2048);
                long ours = 0;
                for (int i = 1; i < Bytes.Count; i++) ours += Bytes[i];
                sb.Append($"[CM.AllocBy] window {_frames} frames: GC Allocated In Frame avg ")
                  .Append(_gcFrame.Valid ? (_gcFrameBytes / n).ToString() : "n/a (release build)")
                  .Append($" B/frame; our scopes {ours / n} B/frame over {Names.Count - 1} scripts; engine script markers {_recs.Count}\n");

                var order = new List<int>();
                for (int i = 1; i < Names.Count; i++) order.Add(i);
                order.Sort((x, y) => Bytes[y].CompareTo(Bytes[x]));
                for (int k = 0; k < order.Count && k < Top; k++)
                    sb.Append($"[CM.AllocBy] alloc {Names[order[k]]} {Bytes[order[k]] / n} B/frame\n");
                order.Sort((x, y) => Ticks[y].CompareTo(Ticks[x]));
                for (int k = 0; k < order.Count && k < Top; k++)
                    sb.Append($"[CM.AllocBy] time {Names[order[k]]} {Ticks[order[k]] * TickMs / n:0.000} ms/frame\n");

                var rec = new List<int>();
                for (int i = 0; i < _recs.Count; i++) rec.Add(i);
                rec.Sort((x, y) => _recNs[y].CompareTo(_recNs[x]));
                for (int k = 0; k < rec.Count && k < Top; k++)
                    sb.Append($"[CM.AllocBy] marker {_recNames[rec[k]]} {_recNs[rec[k]] / 1e6 / n:0.000} ms/frame\n");
                Debug.Log(sb.ToString().TrimEnd('\n'));

                for (int i = 1; i < Bytes.Count; i++) { Bytes[i] = 0; Ticks[i] = 0; }
                for (int i = 0; i < _recNs.Count; i++) _recNs[i] = 0;
                _gcFrameBytes = 0;
                _frames = 0;
            }

            void OnDestroy()
            {
                foreach (ProfilerRecorder r in _recs) r.Dispose();
                _recs.Clear();
                _gcFrame.Dispose();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Profiling;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// perf-smooth-1: a release-player frame-cost recorder (diagnostics; installed only with -perfLog or -frameCsv, so a
    /// normal launch never pays for it). It needs no development build:
    /// <list type="bullet">
    /// <item>a timestamp system before every engine subsystem of every PlayerLoop phase (about 150 Stopwatch reads a frame),
    /// so each frame's main-thread time splits into Update.ScriptRunBehaviourUpdate, PostLateUpdate.FinishFrameRendering,
    /// PlayerUpdateCanvases and so on, plus "Outside" (the time between the end of one player loop and the start of the
    /// next: the OS event pump and the present wait);</item>
    /// <item>FrameTimingManager's main, present-wait, render-thread and GPU times; the GC.Collect marker; collections;</item>
    /// <item>the FrameCost slots (sim, feeds, actor views, HUD, name tags, VFX, audio, camera ...) in ms and bytes;</item>
    /// <item>managed allocation per frame and per subsystem from the best counter the runtime has (GetTotalAllocatedBytes,
    /// else the per-thread counter, else the Mono heap's used size, which counts in whole heap blocks).</item>
    /// </list>
    /// Rows go into preallocated arrays (nothing allocates while recording); the CSV and the summary are written at the
    /// match end next to perf.json ("frames.csv", "frames-m2.csv" ...). The summary names what the slow frames spend more
    /// on than the normal ones (the cause of a stutter), the top allocators and the loading frames.
    /// </summary>
    public static class FrameRecorder
    {
        public const double SpikeMs = 33.3;
        public const int MaxFrames = 1 << 16;
        const int TopN = 4;

        /// <summary>true while a match is recorded (FrameCost reads the allocation counter only then).</summary>
        public static bool Active { get; private set; }
        public static bool Installed { get; private set; }
        /// <summary>Which allocation counter is in use (summary and CSV header).</summary>
        public static string AllocSource { get; private set; } = "none";

        /// <summary>Set by the world build while it runs (the frame is a loading frame).</summary>
        public static bool Loading;

        static Func<long> _alloc = () => 0;
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        public static long AllocatedNow() => _alloc();

        // ---- player loop stamps ----
        static string[] _labels;          // label per stamp index (the subsystem that runs after it)
        static int[] _phaseOf;            // top-level phase per stamp index
        static string[] _phaseNames;
        static double[] _accMs;           // this frame's ms per label
        static long[] _accAlloc;          // this frame's bytes per label
        static int _prev = -1;
        static long _prevTs, _prevAlloc, _frameStartTs, _frameStartAlloc, _loopEndTs;
        static int _gcCount;
        static ProfilerRecorder _gcRec;
        // development builds only: Unity's exact managed allocation of the previous frame (release players report nothing)
        static ProfilerRecorder _gcAllocRec;

        // ---- recorded rows ----
        public const int ColFrameMs = 0, ColDtMs = 1, ColTicks = 2, ColMain = 3, ColWait = 4, ColRender = 5, ColGpu = 6,
            ColGcMs = 7, ColGcCount = 8, ColAllocKb = 9, ColLoading = 10, ColOutside = 11, FixedCols = 12;
        static int _cols, _rows;
        static float[] _data;
        static short[] _top;
        static double _recordStart;
        static double[] _sumAll, _sumSpike, _allocAll;
        static double[] _slotAll, _slotSpike, _slotAlloc;
        static int _spikes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (Application.isEditor) return;
            string cl = Environment.CommandLine;
            if (cl.IndexOf("-perfLog", StringComparison.OrdinalIgnoreCase) < 0 && cl.IndexOf("-frameCsv", StringComparison.OrdinalIgnoreCase) < 0) return;
            Install();
        }

        /// <summary>Inserts the stamps into the current player loop (idempotent).</summary>
        public static void Install()
        {
            if (Installed) return;
            Installed = true;
            PickAllocCounter();
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            var labels = new List<string>();
            var phaseOf = new List<int>();
            var phases = new List<string>();
            PlayerLoopSystem[] top = root.subSystemList ?? new PlayerLoopSystem[0];
            for (int p = 0; p < top.Length; p++)
            {
                string pn = top[p].type != null ? top[p].type.Name : "Phase" + p;
                phases.Add(pn);
                PlayerLoopSystem[] subs = top[p].subSystemList ?? new PlayerLoopSystem[0];
                var list = new List<PlayerLoopSystem>(subs.Length * 2 + 1);
                for (int j = 0; j < subs.Length; j++)
                {
                    list.Add(StampSystem(labels.Count));
                    labels.Add(pn + "." + (subs[j].type != null ? subs[j].type.Name : "?" + j));
                    phaseOf.Add(p);
                    list.Add(subs[j]);
                }
                list.Add(StampSystem(labels.Count));
                labels.Add(pn + ".(between)");
                phaseOf.Add(p);
                top[p].subSystemList = list.ToArray();
            }
            root.subSystemList = top;
            PlayerLoop.SetPlayerLoop(root);
            _labels = labels.ToArray();
            _phaseOf = phaseOf.ToArray();
            _phaseNames = phases.ToArray();
            _accMs = new double[_labels.Length];
            _accAlloc = new long[_labels.Length];
            _cols = FixedCols + _phaseNames.Length + (int)FrameCost.Slot.Count;
            _data = new float[MaxFrames * _cols];
            _top = new short[MaxFrames * TopN];
            _sumAll = new double[_labels.Length];
            _sumSpike = new double[_labels.Length];
            _allocAll = new double[_labels.Length];
            _slotAll = new double[(int)FrameCost.Slot.Count];
            _slotSpike = new double[(int)FrameCost.Slot.Count];
            _slotAlloc = new double[(int)FrameCost.Slot.Count];
            _gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Collect", 2);
            if (UnityEngine.Debug.isDebugBuild)
            {
                _gcAllocRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 2);
                if (_gcAllocRec.Valid) AllocSource = "GC Allocated In Frame (development build, exact; per-subsystem split from " + AllocSource + ")";
            }
            UnityEngine.Debug.Log($"[CM.Frames] recorder installed: {_labels.Length} stamps, allocation counter {AllocSource}");
        }

        static PlayerLoopSystem StampSystem(int k) => new PlayerLoopSystem
        {
            type = typeof(FrameRecorder),
            updateDelegate = () => Stamp(k),
        };

        static void PickAllocCounter()
        {
            try
            {
                MethodInfo total = typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) });
                if (total != null)
                {
                    var f = (Func<bool, long>)Delegate.CreateDelegate(typeof(Func<bool, long>), total);
                    if (Probe(() => f(false))) { _alloc = () => f(false); AllocSource = "GC.GetTotalAllocatedBytes"; return; }
                }
            }
            catch (Exception) { }
            if (Probe(GC.GetAllocatedBytesForCurrentThread))
            {
                _alloc = GC.GetAllocatedBytesForCurrentThread;
                AllocSource = "GC.GetAllocatedBytesForCurrentThread";
                return;
            }
            _alloc = Profiler.GetMonoUsedSizeLong;
            AllocSource = "mono_used_delta (heap blocks; approximate, collections excluded)";
        }

        static byte[] _probeSink;

        static bool Probe(Func<long> f)
        {
            long a = f();
            _probeSink = new byte[64 * 1024];
            long b = f();
            return _probeSink.Length > 0 && b - a >= 60 * 1024;
        }

        static void Stamp(int k)
        {
            long now = Stopwatch.GetTimestamp();
            long alloc = Active ? _alloc() : 0;
            if (k == 0)
            {
                if (_prev >= 0) EndFrame(now, alloc);
                _frameStartTs = now;
                _frameStartAlloc = alloc;
            }
            else if (_prev >= 0)
            {
                _accMs[_prev] += (now - _prevTs) * TickMs;
                if (Active) _accAlloc[_prev] += alloc - _prevAlloc;
            }
            if (k == _labels.Length - 1) _loopEndTs = now;
            _prev = k;
            _prevTs = now;
            _prevAlloc = alloc;
        }

        static void EndFrame(long now, long alloc)
        {
            // the last label ran until the loop end; the time from there to now is outside the player loop
            if (_prev >= 0 && _prev != _labels.Length - 1)
            {
                _accMs[_prev] += (now - _prevTs) * TickMs;
            }
            double outside = _loopEndTs > 0 && _prev == _labels.Length - 1 ? (now - _loopEndTs) * TickMs : 0;
            int gc = GC.CollectionCount(0);
            if (Active && _rows < MaxFrames) Record(now, alloc, outside, gc);
            _gcCount = gc;
            for (int i = 0; i < _accMs.Length; i++)
            {
                _accMs[i] = 0;
                _accAlloc[i] = 0;
            }
            FrameCost.ClearFrame();
        }

        static void Record(long now, long alloc, double outside, int gc)
        {
            int o = _rows * _cols;
            double frameMs = (now - _frameStartTs) * TickMs;
            _data[o + ColFrameMs] = (float)frameMs;
            _data[o + ColDtMs] = Time.unscaledDeltaTime * 1000f;
            _data[o + ColTicks] = FrameCost.FrameTicks;
            _data[o + ColMain] = SessionKeeper.CpuMainMs;
            _data[o + ColWait] = SessionKeeper.CpuPresentWaitMs;
            _data[o + ColRender] = SessionKeeper.CpuRenderMs;
            _data[o + ColGpu] = SessionKeeper.GpuFrameMs;
            _data[o + ColGcMs] = _gcRec.Valid ? (float)(_gcRec.LastValue * 1e-6) : -1f;
            _data[o + ColGcCount] = gc - _gcCount;
            _data[o + ColAllocKb] = _gcAllocRec.Valid && _gcAllocRec.Count > 0
                ? _gcAllocRec.LastValue / 1024f
                : (alloc - _frameStartAlloc) / 1024f;
            _data[o + ColLoading] = Loading ? 1 : 0;
            _data[o + ColOutside] = (float)outside;
            int c = o + FixedCols;
            for (int p = 0; p < _phaseNames.Length; p++) _data[c + p] = 0;
            for (int i = 0; i < _accMs.Length; i++) _data[c + _phaseOf[i]] += (float)_accMs[i];
            c += _phaseNames.Length;
            for (int s = 0; s < (int)FrameCost.Slot.Count; s++) _data[c + s] = (float)FrameCost.FrameMs[s];
            bool spike = frameMs >= SpikeMs && !Loading;
            if (spike) _spikes++;
            for (int i = 0; i < _accMs.Length; i++)
            {
                _sumAll[i] += _accMs[i];
                if (spike) _sumSpike[i] += _accMs[i];
                _allocAll[i] += _accAlloc[i];
            }
            for (int s = 0; s < (int)FrameCost.Slot.Count; s++)
            {
                _slotAll[s] += FrameCost.FrameMs[s];
                if (spike) _slotSpike[s] += FrameCost.FrameMs[s];
                _slotAlloc[s] += FrameCost.FrameAlloc[s];
            }
            int t = _rows * TopN;
            for (int n = 0; n < TopN; n++) _top[t + n] = -1;
            if (frameMs >= 25.0)
            {
                for (int n = 0; n < TopN; n++)
                {
                    int best = -1;
                    for (int i = 0; i < _accMs.Length; i++)
                    {
                        bool taken = false;
                        for (int m = 0; m < n; m++) if (_top[t + m] == i) taken = true;
                        if (!taken && (best < 0 || _accMs[i] > _accMs[best])) best = i;
                    }
                    _top[t + n] = (short)best;
                }
            }
            _rows++;
        }

        /// <summary>Starts recording a match (clears the previous one).</summary>
        public static void Begin()
        {
            if (!Installed) return;
            _rows = 0;
            _spikes = 0;
            Array.Clear(_sumAll, 0, _sumAll.Length);
            Array.Clear(_sumSpike, 0, _sumSpike.Length);
            Array.Clear(_allocAll, 0, _allocAll.Length);
            Array.Clear(_slotAll, 0, _slotAll.Length);
            Array.Clear(_slotSpike, 0, _slotSpike.Length);
            Array.Clear(_slotAlloc, 0, _slotAlloc.Length);
            _recordStart = Time.realtimeSinceStartupAsDouble;
            _gcCount = GC.CollectionCount(0);
            _prevAlloc = _frameStartAlloc = _alloc();
            Active = true;
        }

        /// <summary>Stops recording; writes the CSV at csvPath and returns the summary as one JSON object ("null" when off).</summary>
        public static string End(string csvPath)
        {
            if (!Installed || !Active) return "null";
            Active = false;
            try
            {
                if (!string.IsNullOrEmpty(csvPath)) WriteCsv(csvPath);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[CM.Frames] could not write " + csvPath + ": " + e.Message);
            }
            string json = SummaryJson();
            UnityEngine.Debug.Log("[CM.Frames] " + json);
            return json;
        }

        /// <summary>"a/perf.json" -> "a/frames.csv"; "a/perf-m2.json" -> "a/frames-m2.csv".</summary>
        public static string CsvPathFor(string perfPath)
        {
            if (string.IsNullOrEmpty(perfPath)) return null;
            string dir = Path.GetDirectoryName(perfPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(perfPath);
            string suffix = name.StartsWith("perf", StringComparison.Ordinal) ? name.Substring(4) : "-" + name;
            return Path.Combine(dir, "frames" + suffix + ".csv");
        }

        static void WriteCsv(string path)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(_rows * 160);
            sb.Append("# alloc_source=").Append(AllocSource).Append('\n');
            sb.Append("frame,frame_ms,dt_ms,ticks,ftm_main_ms,ftm_present_wait_ms,ftm_render_ms,ftm_gpu_ms,gc_ms,gc_count,alloc_kb,loading,outside_ms");
            foreach (string p in _phaseNames) sb.Append(',').Append(p).Append("_ms");
            for (int s = 0; s < (int)FrameCost.Slot.Count; s++) sb.Append(",slot_").Append(((FrameCost.Slot)s).ToString()).Append("_ms");
            sb.Append(",top\n");
            for (int r = 0; r < _rows; r++)
            {
                int o = r * _cols;
                sb.Append(r);
                for (int k = 0; k < _cols; k++)
                {
                    sb.Append(',');
                    float v = _data[o + k];
                    if (v == 0f) sb.Append('0');
                    else sb.Append(v.ToString("0.###", c));
                }
                sb.Append(',');
                for (int n = 0; n < TopN; n++)
                {
                    short i = _top[r * TopN + n];
                    if (i < 0) break;
                    if (n > 0) sb.Append('|');
                    sb.Append(_labels[i]);
                }
                sb.Append('\n');
            }
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString());
        }

        static float Pct(List<float> sorted, double q) => sorted.Count == 0 ? 0f : sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * (sorted.Count - 1) + 0.5))];

        static string SummaryJson()
        {
            var c = CultureInfo.InvariantCulture;
            var frames = new List<float>(_rows);
            var allocs = new List<float>(_rows);
            var gcMs = new List<float>();
            int loadingFrames = 0, collections = 0;
            float loadingMax = 0f, gcMax = 0f;
            int[] ticks = new int[6];
            int over20 = 0, over33 = 0, over50 = 0, match = 0;
            for (int r = 0; r < _rows; r++)
            {
                int o = r * _cols;
                float ms = _data[o + ColFrameMs];
                if (_data[o + ColLoading] > 0) { loadingFrames++; loadingMax = Math.Max(loadingMax, ms); continue; }
                match++;
                frames.Add(ms);
                if (ms > 20f) over20++;
                if (ms >= SpikeMs) over33++;
                if (ms > 50f) over50++;
                int tk = (int)_data[o + ColTicks];
                ticks[Math.Max(0, Math.Min(5, tk))]++;
                if (_data[o + ColGcCount] > 0) collections += (int)_data[o + ColGcCount];
                float g = _data[o + ColGcMs];
                if (g > 0f) { gcMs.Add(g); gcMax = Math.Max(gcMax, g); }
                if (r > 120) allocs.Add(_data[o + ColAllocKb]);
            }
            frames.Sort();
            allocs.Sort();
            double allocMean = 0;
            foreach (float a in allocs) allocMean += a;
            allocMean = allocs.Count > 0 ? allocMean / allocs.Count : 0;
            int normal = Math.Max(1, match - _spikes);
            int spikes = Math.Max(1, _spikes);
            // what slow frames spend more on than normal ones (mean ms per frame, spike minus normal)
            var attrib = new List<(string name, double spike, double normal)>();
            for (int i = 0; i < _labels.Length; i++)
            {
                double sp = _sumSpike[i] / spikes, nm = (_sumAll[i] - _sumSpike[i]) / normal;
                attrib.Add((_labels[i], sp, nm));
            }
            attrib.Sort((a, b) => (b.spike - b.normal).CompareTo(a.spike - a.normal));
            var allocTop = new List<(string name, double kb)>();
            for (int i = 0; i < _labels.Length; i++) allocTop.Add((_labels[i], _allocAll[i] / 1024.0 / Math.Max(1, _rows)));
            allocTop.Sort((a, b) => b.kb.CompareTo(a.kb));
            var sb = new StringBuilder();
            sb.Append('{');
            sb.AppendFormat(c, "\"frames\": {0}, \"loading_frames\": {1}, \"loading_max_ms\": {2:0.##}, ", match, loadingFrames, loadingMax);
            sb.AppendFormat(c, "\"frame_ms\": {{\"p50\": {0:0.###}, \"p95\": {1:0.###}, \"p99\": {2:0.###}, \"max\": {3:0.###}}}, ",
                Pct(frames, 0.5), Pct(frames, 0.95), Pct(frames, 0.99), frames.Count > 0 ? frames[frames.Count - 1] : 0f);
            sb.AppendFormat(c, "\"over_20\": {0}, \"over_33\": {1}, \"over_50\": {2}, \"over_33_pct\": {3:0.###}, ", over20, over33, over50,
                match > 0 ? 100.0 * over33 / match : 0);
            sb.AppendFormat(c, "\"ticks_per_frame\": [{0}, {1}, {2}, {3}, {4}, {5}], ", ticks[0], ticks[1], ticks[2], ticks[3], ticks[4], ticks[5]);
            sb.AppendFormat(c, "\"gc_collections\": {0}, \"gc_max_ms\": {1:0.###}, \"alloc_source\": \"{2}\", ", collections, gcMax, AllocSource);
            sb.AppendFormat(c, "\"alloc_kb_per_frame\": {{\"mean\": {0:0.###}, \"p50\": {1:0.###}, \"p99\": {2:0.###}, \"max\": {3:0.###}}}, ",
                allocMean, Pct(allocs, 0.5), Pct(allocs, 0.99), allocs.Count > 0 ? allocs[allocs.Count - 1] : 0f);
            sb.Append("\"spike_excess_ms\": [");
            for (int n = 0; n < Math.Min(12, attrib.Count); n++)
            {
                if (n > 0) sb.Append(", ");
                sb.AppendFormat(c, "[\"{0}\", {1:0.###}, {2:0.###}]", attrib[n].name, attrib[n].spike, attrib[n].normal);
            }
            sb.Append("], \"slots_ms\": {");
            for (int s = 0; s < (int)FrameCost.Slot.Count; s++)
            {
                if (s > 0) sb.Append(", ");
                sb.AppendFormat(c, "\"{0}\": [{1:0.###}, {2:0.###}, {3:0.###}]", ((FrameCost.Slot)s).ToString(),
                    (_slotAll[s] - _slotSpike[s]) / normal, _slotSpike[s] / spikes, _slotAlloc[s] / 1024.0 / Math.Max(1, _rows));
            }
            sb.Append("}, \"alloc_kb_per_frame_by_subsystem\": [");
            for (int n = 0; n < Math.Min(10, allocTop.Count); n++)
            {
                if (n > 0) sb.Append(", ");
                sb.AppendFormat(c, "[\"{0}\", {1:0.###}]", allocTop[n].name, allocTop[n].kb);
            }
            sb.AppendFormat(c, "], \"recorded_seconds\": {0:0.#}}}", Time.realtimeSinceStartupAsDouble - _recordStart);
            return sb.ToString();
        }
    }
}

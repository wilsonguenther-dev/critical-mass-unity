using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// DG-05 performance overlay and frame recorder. F3 toggles a small box (frame ms now, p50, p95 and worst over the
    /// last 10 s, main-thread ms, batches, SetPass calls, triangles, GC bytes per frame, build stamp). The version label
    /// sits in the bottom-right corner while the title screen is up. Autoplay (AutoplayArgs) records every frame of a
    /// match with BeginRecording/EndRecording and writes the report as JSON. Reads only; never writes game or Sim state.
    /// Installed by AutoplayArgs on the "CriticalMass Services" object, so the HUD files are untouched.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        public const int RingFrames = 600;
        public const double HitchMs = 50.0;

        /// <summary>The overlay box (F3).</summary>
        public bool Visible;

        static PerfOverlay _instance;
        readonly float[] _ring = new float[RingFrames];
        int _ringCount, _ringHead;
        PerfOverlayGui _gui;
        float _textAge = 1f;

        // Frame-time markers and counters. Top-level PlayerLoop phases partition the frame; "[Invoke]" script markers
        // (development builds) split the script time per component.
        static readonly string[] Markers =
        {
            "Main Thread", "PlayerLoop", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "PostLateUpdate.FinishFrameRendering", "PostLateUpdate.PlayerUpdateCanvases", "PostLateUpdate.UpdateAllRenderers",
            "PostLateUpdate.UpdateAllSkinnedMeshes", "PreLateUpdate.DirectorUpdateAnimationBegin",
            "PreLateUpdate.DirectorUpdateAnimationEnd", "PreLateUpdate.ParticleSystemBeginUpdateAll",
            "PostLateUpdate.ParticleSystemEndUpdateAll", "FixedUpdate.PhysicsFixedUpdate", "Gfx.WaitForPresentOnGfxThread",
            "Gfx.WaitForGfxCommandsFromMainThread", "Gfx.PresentFrame", "GC.Collect", "Camera.Render",
            "RenderPipelineManager.DoRenderLoop_Internal()", "Inl_UniversalRenderPipeline.RenderSingleCameraInternal",
            "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Triangles Count", "Vertices Count",
            "Shadow Casters Count", "GC Allocated In Frame", "System Used Memory", "GC Reserved Memory",
            "Visible Skinned Meshes Count", "Canvas.SendWillRenderCanvases", "Canvas.BuildBatch",
        };

        sealed class Rec
        {
            public string Name;
            public ProfilerRecorder R;
            public ProfilerMarkerDataUnit Unit;
            public double Sum, Max;
            public long Samples;
        }

        static readonly List<Rec> Recs = new List<Rec>();
        static readonly HashSet<string> RecNames = new HashSet<string>();
        static bool _recordersStarted, _recording;
        static readonly List<float> MatchFrames = new List<float>(65536);
        // DG-54: real CPU and GPU frame times from FrameTimingManager (SessionKeeper captures them once per frame).
        static readonly List<float> MatchGpu = new List<float>(65536);
        static readonly List<float> MatchCpuMain = new List<float>(65536);
        const int MaxRecordedFrames = 1 << 17;   // a hard cap: a 2-hour soak never grows these lists without bound
        static double _recordStart;

        void Awake()
        {
            _instance = this;
            _gui = gameObject.AddComponent<PerfOverlayGui>();
            _gui.Owner = this;
            _gui.enabled = false;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            StopRecorders();
        }

        void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            if (Time.frameCount > 1)
            {
                _ring[_ringHead] = ms;
                _ringHead = (_ringHead + 1) % RingFrames;
                if (_ringCount < RingFrames) _ringCount++;
            }
            if (_phase != null && Time.frameCount > 1) SamplePhase(ms);
            if (_recording && MatchFrames.Count < MaxRecordedFrames)
            {
                MatchFrames.Add(ms);
                if (SessionKeeper.GpuFrameMs > 0f) MatchGpu.Add(SessionKeeper.GpuFrameMs);
                if (SessionKeeper.CpuMainMs > 0f) MatchCpuMain.Add(SessionKeeper.CpuMainMs);
                Sample();
            }

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame)
            {
                Visible = !Visible;
                if (Visible) StartRecorders(false);
            }

            bool title = !Application.isBatchMode && FindTitleCached();
            _gui.ShowBox = Visible;
            _gui.ShowVersion = title || Visible;
            _gui.enabled = _gui.ShowBox || _gui.ShowVersion;
            _textAge += Time.unscaledDeltaTime;
            if (Visible && _textAge >= 0.25f)
            {
                _textAge = 0f;
                _gui.Text = BoxText();
            }
        }

        float _titleCheckAge = 1f;
        bool _titleCached;

        bool FindTitleCached()
        {
            _titleCheckAge += Time.unscaledDeltaTime;
            if (_titleCheckAge >= 0.25f)
            {
                _titleCheckAge = 0f;
                _titleCached = FindAnyObjectByType<TitleScreen>() != null;
            }
            return _titleCached;
        }

        string BoxText()
        {
            var tmp = new float[_ringCount];
            for (int i = 0; i < _ringCount; i++) tmp[i] = _ring[i];
            float now = _ringCount > 0 ? _ring[(_ringHead + RingFrames - 1) % RingFrames] : 0f;
            var sb = new StringBuilder(256);
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F1} ms  {1:F0} fps   p50 {2:F1}  p95 {3:F1}  worst {4:F1} ms (last {5} frames)\n",
                now, now > 0 ? 1000f / now : 0f, Percentile(tmp, tmp.Length, 50), Percentile(tmp, tmp.Length, 95), Max(tmp), _ringCount);
            sb.AppendFormat(CultureInfo.InvariantCulture, "main {0:F1} ms   render {1:F1} ms   scripts {2:F1} ms\n",
                Last("Main Thread"), Last("PostLateUpdate.FinishFrameRendering"), Last("Update.ScriptRunBehaviourUpdate"));
            sb.AppendFormat(CultureInfo.InvariantCulture, "batches {0:F0}  setpass {1:F0}  tris {2:F0}k  gc {3:F1} KB/frame\n",
                Last("Batches Count"), Last("SetPass Calls Count"), Last("Triangles Count") / 1000.0, Last("GC Allocated In Frame") / 1024.0);
            sb.AppendFormat(CultureInfo.InvariantCulture, "cpu {0:F1} ms  gpu {1:F1} ms  scale {2:0.00}  cap {3}  dropped sim {4:F2} s  hud events {5}\n",
                SessionKeeper.CpuMainMs, SessionKeeper.GpuFrameMs, FrameGovernor.AppliedRenderScale, FrameGovernor.AppliedCap,
                RuntimeCounters.SimDroppedSeconds, RuntimeCounters.HudEventsDropped);
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0}x{1}  vsync {2}  {3}", Screen.width, Screen.height, QualitySettings.vSyncCount, BuildInfo.Label);
            return sb.ToString();
        }

        static double Last(string name)
        {
            foreach (Rec r in Recs)
                if (r.Name == name && r.R.Valid) return Scale(r.R.LastValue, r.Unit);
            return double.NaN;
        }

        static double Scale(long v, ProfilerMarkerDataUnit unit) => unit == ProfilerMarkerDataUnit.TimeNanoseconds ? v / 1e6 : v;

        // ── recorders ──

        static void StartRecorders(bool withScripts)
        {
            if (!_recordersStarted)
            {
                _recordersStarted = true;
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var wanted = new HashSet<string>(Markers);
                foreach (ProfilerRecorderHandle h in handles)
                {
                    ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                    if (wanted.Contains(d.Name)) Add(d);
                }
            }
            if (withScripts)
            {
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                int added = 0;
                foreach (ProfilerRecorderHandle h in handles)
                {
                    ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                    if (added < 48 && d.Name != null && d.Name.EndsWith("[Invoke]", StringComparison.Ordinal) && Add(d)) added++;
                }
            }
        }

        static bool Add(ProfilerRecorderDescription d)
        {
            if (RecNames.Contains(d.Name)) return false;
            ProfilerRecorder r = ProfilerRecorder.StartNew(d.Category, d.Name, 1);
            if (!r.Valid)
            {
                r.Dispose();
                return false;
            }
            RecNames.Add(d.Name);
            Recs.Add(new Rec { Name = d.Name, R = r, Unit = d.UnitType });
            return true;
        }

        static void StopRecorders()
        {
            foreach (Rec r in Recs) r.R.Dispose();
            Recs.Clear();
            RecNames.Clear();
            _recordersStarted = false;
            _recording = false;
        }

        static void Sample()
        {
            if (!_recording) return;
            foreach (Rec r in Recs)
            {
                if (!r.R.Valid) continue;
                double v = Scale(r.R.LastValue, r.Unit);
                r.Sum += v;
                if (v > r.Max) r.Max = v;
                r.Samples++;
            }
        }

        // ── per-phase numbers (physics audit m1 / DG-111): the smoke reported run maxima (title and loading frames
        // included) as "the" triangle and batch counts. Each phase (title, match, results) keeps its own frame times, GPU
        // times and counter samples; perf.json gets p50 and p95 per phase and the gate reads the match phase. ──

        /// <summary>One phase's samples, each list capped at MaxPhaseFrames (10 minutes at 60 fps).</summary>
        public sealed class PhaseStats
        {
            public const int MaxPhaseFrames = 36000;
            public readonly string Name;
            public readonly List<float> Frame = new List<float>(4096), Gpu = new List<float>(4096), Triangles = new List<float>(4096),
                Batches = new List<float>(4096), SetPass = new List<float>(4096), Casters = new List<float>(4096), Skinned = new List<float>(4096),
                CanvasMs = new List<float>(4096);
            public PhaseStats(string name) => Name = name;

            public void Clear()
            {
                Frame.Clear(); Gpu.Clear(); Triangles.Clear(); Batches.Clear(); SetPass.Clear(); Casters.Clear(); Skinned.Clear(); CanvasMs.Clear();
            }

            public static void Add(List<float> l, double v)
            {
                if (l.Count < MaxPhaseFrames && !double.IsNaN(v)) l.Add((float)v);
            }
        }

        public const string PhaseTitle = "title", PhaseMatch = "match", PhaseResults = "results";
        static readonly Dictionary<string, PhaseStats> Phases = new Dictionary<string, PhaseStats>();
        static readonly List<string> PhaseOrder = new List<string>();
        static string _phase;
        static PhaseStats _phaseStats;
        static Rec _rTris, _rBatches, _rSetPass, _rCasters, _rSkinned, _rCanvasSend, _rCanvasBatch;

        /// <summary>The phase being sampled now (null = none).</summary>
        public static string Phase => _phase;

        /// <summary>Starts sampling a phase (title, match, results; null stops). Returning to a phase appends to it.</summary>
        public static void SetPhase(string name)
        {
            if (name == _phase) return;
            _phase = name;
            if (name == null) { _phaseStats = null; return; }
            StartRecorders(false);
            FindPhaseRecorders();
            if (!Phases.TryGetValue(name, out _phaseStats))
            {
                _phaseStats = new PhaseStats(name);
                Phases[name] = _phaseStats;
                PhaseOrder.Add(name);
            }
        }

        /// <summary>Forgets a phase's samples (a new match clears "match" and "results"; the title stays from boot).</summary>
        public static void ClearPhase(string name)
        {
            if (Phases.TryGetValue(name, out PhaseStats p)) p.Clear();
        }

        static int _phaseRecCount = -1;

        /// <summary>Caches the counters a phase samples; again whenever the recorder list was rebuilt (StopRecorders).</summary>
        static void FindPhaseRecorders()
        {
            _phaseRecCount = Recs.Count;
            _rTris = Find("Triangles Count"); _rBatches = Find("Batches Count"); _rSetPass = Find("SetPass Calls Count");
            _rCasters = Find("Shadow Casters Count"); _rSkinned = Find("Visible Skinned Meshes Count");
            _rCanvasSend = Find("Canvas.SendWillRenderCanvases"); _rCanvasBatch = Find("Canvas.BuildBatch");
        }

        static Rec Find(string name)
        {
            foreach (Rec r in Recs) if (r.Name == name) return r;
            return null;
        }

        static double LastOf(Rec r) => r != null && r.R.Valid ? Scale(r.R.LastValue, r.Unit) : double.NaN;

        static void SamplePhase(float ms)
        {
            PhaseStats p = _phaseStats;
            if (p == null) return;
            if (Recs.Count == 0) StartRecorders(false);
            if (Recs.Count != _phaseRecCount) FindPhaseRecorders();
            PhaseStats.Add(p.Frame, ms);
            if (SessionKeeper.GpuFrameMs > 0f) PhaseStats.Add(p.Gpu, SessionKeeper.GpuFrameMs);
            PhaseStats.Add(p.Triangles, LastOf(_rTris));
            PhaseStats.Add(p.Batches, LastOf(_rBatches));
            PhaseStats.Add(p.SetPass, LastOf(_rSetPass));
            PhaseStats.Add(p.Casters, LastOf(_rCasters));
            PhaseStats.Add(p.Skinned, LastOf(_rSkinned));
            double canvas = LastOf(_rCanvasSend) + LastOf(_rCanvasBatch);
            PhaseStats.Add(p.CanvasMs, canvas);
        }

        /// <summary>{"p50": a, "p95": b} of a sample list, or null when empty (the gate fails closed on null).</summary>
        public static string P50P95(List<float> l)
        {
            if (l == null || l.Count == 0) return "null";
            var a = l.ToArray();
            Array.Sort(a);
            return string.Format(CultureInfo.InvariantCulture, "{{\"p50\": {0:0.###}, \"p95\": {1:0.###}}}", PercentileSorted(a, 50), PercentileSorted(a, 95));
        }

        /// <summary>Every phase sampled so far as one JSON object (frames, frame_ms, gpu_ms, triangles, batches, setpass,
        /// shadow_casters, visible_skinned_meshes, canvas_ms: p50 and p95 each).</summary>
        public static string PhasesJson()
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');
            for (int i = 0; i < PhaseOrder.Count; i++)
            {
                PhaseStats p = Phases[PhaseOrder[i]];
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(p.Name).Append("\": {\"frames\": ").Append(p.Frame.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"frame_ms\": ").Append(P50P95(p.Frame))
                  .Append(", \"gpu_ms\": ").Append(P50P95(p.Gpu))
                  .Append(", \"triangles\": ").Append(P50P95(p.Triangles))
                  .Append(", \"batches\": ").Append(P50P95(p.Batches))
                  .Append(", \"setpass\": ").Append(P50P95(p.SetPass))
                  .Append(", \"shadow_casters\": ").Append(P50P95(p.Casters))
                  .Append(", \"visible_skinned_meshes\": ").Append(P50P95(p.Skinned))
                  .Append(", \"canvas_ms\": ").Append(P50P95(p.CanvasMs)).Append('}');
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Starts recording every frame (autoplay); clears the previous recording.</summary>
        public static void BeginRecording()
        {
            StartRecorders(true);
            foreach (Rec r in Recs)
            {
                r.Sum = 0;
                r.Max = 0;
                r.Samples = 0;
            }
            MatchFrames.Clear();
            MatchGpu.Clear();
            MatchCpuMain.Clear();
            _recordStart = Time.realtimeSinceStartupAsDouble;
            _recording = true;
        }

        /// <summary>A recorded match: frame times and per-marker means.</summary>
        public sealed class Report
        {
            public int Frames;
            public double Seconds, P50, P95, P99, Mean, Worst, WorstAtSecond;
            public int Over50, Over33, Over20;
            /// <summary>FrameTimingManager GPU and main-thread CPU frame times (ms); NaN when the platform reported none.</summary>
            public double GpuP50 = double.NaN, GpuP95 = double.NaN, CpuMainP50 = double.NaN, CpuMainP95 = double.NaN;
            public List<(string name, string unit, double mean, double max)> MarkerMeans = new List<(string, string, double, double)>();

            public string Summary() => string.Format(CultureInfo.InvariantCulture,
                "frames={0} p50={1:F2}ms p95={2:F2}ms p99={3:F2}ms worst={4:F1}ms at {5:F1}s hitches>50ms={6} >33ms={7}",
                Frames, P50, P95, P99, Worst, WorstAtSecond, Over50, Over33);
        }

        /// <summary>Stops recording and returns the report.</summary>
        public static Report EndRecording()
        {
            _recording = false;
            Report rep = Analyse(MatchFrames);
            rep.Seconds = Time.realtimeSinceStartupAsDouble - _recordStart;
            if (MatchGpu.Count > 0)
            {
                Report g = Analyse(MatchGpu);
                rep.GpuP50 = g.P50;
                rep.GpuP95 = g.P95;
            }
            if (MatchCpuMain.Count > 0)
            {
                Report c = Analyse(MatchCpuMain);
                rep.CpuMainP50 = c.P50;
                rep.CpuMainP95 = c.P95;
            }
            foreach (Rec r in Recs)
            {
                if (r.Samples == 0) continue;
                string unit = r.Unit == ProfilerMarkerDataUnit.TimeNanoseconds ? "ms" : r.Unit == ProfilerMarkerDataUnit.Bytes ? "bytes" : "count";
                rep.MarkerMeans.Add((r.Name, unit, r.Sum / r.Samples, r.Max));
            }
            rep.MarkerMeans.Sort((a, b) =>
            {
                int byUnit = (a.unit == "ms" ? 0 : 1).CompareTo(b.unit == "ms" ? 0 : 1);   // times first, then counts
                return byUnit != 0 ? byUnit : b.mean.CompareTo(a.mean);
            });
            return rep;
        }

        /// <summary>Frame-time statistics of a list of frame times in ms (pure; tests).</summary>
        public static Report Analyse(IList<float> frames)
        {
            var rep = new Report { Frames = frames.Count };
            if (frames.Count == 0) return rep;
            var a = new float[frames.Count];
            double sum = 0, t = 0;
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = frames[i];
                sum += a[i];
                if (a[i] > rep.Worst)
                {
                    rep.Worst = a[i];
                    rep.WorstAtSecond = t / 1000.0;
                }
                if (a[i] > HitchMs) rep.Over50++;
                if (a[i] > 1000.0 / 30.0) rep.Over33++;
                if (a[i] > 20.0) rep.Over20++;
                t += a[i];
            }
            rep.Mean = sum / a.Length;
            Array.Sort(a);
            rep.P50 = PercentileSorted(a, 50);
            rep.P95 = PercentileSorted(a, 95);
            rep.P99 = PercentileSorted(a, 99);
            return rep;
        }

        /// <summary>Nearest-rank percentile of the first count values (unsorted input is copied and sorted).</summary>
        public static double Percentile(float[] values, int count, double p)
        {
            if (values == null || count <= 0) return 0;
            var a = new float[count];
            Array.Copy(values, a, count);
            Array.Sort(a);
            return PercentileSorted(a, p);
        }

        /// <summary>Nearest-rank percentile of an ascending array: the smallest value with at least p% at or below it.</summary>
        public static double PercentileSorted(float[] sorted, double p)
        {
            if (sorted == null || sorted.Length == 0) return 0;
            int rank = (int)Math.Ceiling(p / 100.0 * sorted.Length);
            return sorted[Mathf.Clamp(rank - 1, 0, sorted.Length - 1)];
        }

        static float Max(float[] a)
        {
            float m = 0;
            foreach (float v in a) if (v > m) m = v;
            return m;
        }

        public const int TextureFlagPx = 2048;

        /// <summary>Estimated bytes of a texture (mip 0 times 4/3 with mips; RenderTextures at their format).</summary>
        public static long TextureBytes(int w, int h, GraphicsFormat f, int mips, int depthOrSlices)
        {
            long b;
            try { b = GraphicsFormatUtility.ComputeMipmapSize(w, h, f); }
            catch (Exception) { b = (long)w * h * 4; }
            if (mips > 1) b = b * 4 / 3;
            return b * Math.Max(1, depthOrSlices);
        }

        /// <summary>true when a non-render texture should be flagged: over 2048 px on a side, or not block-compressed.</summary>
        public static bool FlagTexture(int w, int h, bool compressed, bool renderTarget) =>
            !renderTarget && (Math.Max(w, h) > TextureFlagPx || !compressed);

        /// <summary>
        /// Every loaded texture and mesh, largest first (top 12 of each), with flags for textures over 2048 px or
        /// uncompressed, as one JSON object. Heavy (FindObjectsOfTypeAll): called once, at the end of the first match.
        /// </summary>
        public static string AssetReportJson()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            var tex = new List<(string name, int w, int h, string fmt, bool comp, bool rt, long bytes)>();
            foreach (Texture t in Resources.FindObjectsOfTypeAll<Texture>())
            {
                if (t == null) continue;
                GraphicsFormat f = t.graphicsFormat;
                bool rt = t is RenderTexture;
                bool comp = GraphicsFormatUtility.IsCompressedFormat(f);
                int slices = t is Cubemap ? 6 : t is Texture2DArray a ? a.depth : t is Texture3D d ? d.depth : 1;
                tex.Add((t.name, t.width, t.height, f.ToString(), comp, rt, TextureBytes(t.width, t.height, f, t.mipmapCount, slices)));
            }
            tex.Sort((x, y) => y.bytes.CompareTo(x.bytes));
            var meshes = new List<(string name, int verts, long tris, long bytes)>();
            foreach (Mesh m in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                if (m == null) continue;
                long idx = 0;
                for (int i = 0; i < m.subMeshCount; i++) idx += m.GetIndexCount(i);
                long vb = 0;
                for (int i = 0; i < m.vertexBufferCount; i++) vb += (long)m.GetVertexBufferStride(i) * m.vertexCount;
                long bytes = vb + idx * (m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2);
                meshes.Add((m.name, m.vertexCount, idx / 3, bytes));
            }
            meshes.Sort((x, y) => y.bytes.CompareTo(x.bytes));

            long texTotal = 0, meshTotal = 0;
            int flagged = 0;
            foreach (var t in tex)
            {
                texTotal += t.bytes;
                if (FlagTexture(t.w, t.h, t.comp, t.rt)) flagged++;
            }
            foreach (var m in meshes) meshTotal += m.bytes;
            var sb = new StringBuilder(4096);
            sb.AppendFormat(c, "{{\"textures\": {0}, \"texture_mb\": {1:0.##}, \"textures_flagged\": {2}, \"meshes\": {3}, \"mesh_mb\": {4:0.##},\n",
                tex.Count, texTotal / 1048576.0, flagged, meshes.Count, meshTotal / 1048576.0);
            sb.Append("    \"largest_textures\": [");
            for (int i = 0; i < Math.Min(12, tex.Count); i++)
            {
                var t = tex[i];
                sb.AppendFormat(c, "{0}{{\"name\": \"{1}\", \"w\": {2}, \"h\": {3}, \"format\": \"{4}\", \"compressed\": {5}, \"render_target\": {6}, \"mb\": {7:0.###}, \"flag\": {8}}}",
                    i == 0 ? "" : ", ", Esc(t.name), t.w, t.h, t.fmt, t.comp ? "true" : "false", t.rt ? "true" : "false", t.bytes / 1048576.0,
                    FlagTexture(t.w, t.h, t.comp, t.rt) ? "true" : "false");
            }
            sb.Append("],\n    \"flagged_textures\": [");
            int k = 0;
            foreach (var t in tex)
            {
                if (!FlagTexture(t.w, t.h, t.comp, t.rt) || k >= 40) continue;
                sb.AppendFormat(c, "{0}\"{1} {2}x{3} {4}\"", k++ == 0 ? "" : ", ", Esc(t.name), t.w, t.h, t.fmt);
            }
            sb.Append("],\n    \"largest_meshes\": [");
            for (int i = 0; i < Math.Min(12, meshes.Count); i++)
            {
                var m = meshes[i];
                sb.AppendFormat(c, "{0}{{\"name\": \"{1}\", \"verts\": {2}, \"tris\": {3}, \"mb\": {4:0.###}}}",
                    i == 0 ? "" : ", ", Esc(m.name), m.verts, m.tris, m.bytes / 1048576.0);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>The report as one JSON object, with extra pre-encoded fields first (values are JSON literals).</summary>
        public static string ToJson(Report r, (string key, string jsonValue)[] extra)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\n");
            foreach ((string k, string v) in extra) sb.Append("  \"").Append(k).Append("\": ").Append(v).Append(",\n");
            if (r == null)
            {
                sb.Append("  \"frames\": 0\n}\n");
                return sb.ToString();
            }
            CultureInfo c = CultureInfo.InvariantCulture;
            sb.AppendFormat(c, "  \"frames\": {0},\n  \"recorded_seconds\": {1:0.###},\n", r.Frames, r.Seconds);
            sb.AppendFormat(c, "  \"frame_ms\": {{\"p50\": {0:0.###}, \"p95\": {1:0.###}, \"p99\": {2:0.###}, \"mean\": {3:0.###}, \"worst\": {4:0.###}, \"worst_at_s\": {5:0.##}}},\n",
                r.P50, r.P95, r.P99, r.Mean, r.Worst, r.WorstAtSecond);
            sb.AppendFormat(c, "  \"frames_over_ms\": {{\"20\": {0}, \"33.3\": {1}, \"50\": {2}}},\n", r.Over20, r.Over33, r.Over50);
            // null (never 0) when the platform reported no timing: the perf gate fails closed on a missing number.
            sb.Append("  \"gpu_ms\": ").Append(double.IsNaN(r.GpuP95) ? "null" : string.Format(c, "{{\"p50\": {0:0.###}, \"p95\": {1:0.###}}}", r.GpuP50, r.GpuP95)).Append(",\n");
            sb.Append("  \"cpu_main_ms\": ").Append(double.IsNaN(r.CpuMainP95) ? "null" : string.Format(c, "{{\"p50\": {0:0.###}, \"p95\": {1:0.###}}}", r.CpuMainP50, r.CpuMainP95)).Append(",\n");
            sb.Append("  \"markers\": [\n");
            for (int i = 0; i < r.MarkerMeans.Count; i++)
            {
                var m = r.MarkerMeans[i];
                sb.AppendFormat(c, "    {{\"name\": \"{0}\", \"unit\": \"{1}\", \"mean\": {2:0.####}, \"max\": {3:0.####}}}{4}\n",
                    m.name.Replace("\\", "\\\\").Replace("\"", "\\\""), m.unit, m.mean, m.max, i + 1 < r.MarkerMeans.Count ? "," : "");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }
    }

    /// <summary>The IMGUI part of PerfOverlay, enabled only while something is shown (no OnGUI cost otherwise).</summary>
    public sealed class PerfOverlayGui : MonoBehaviour
    {
        public PerfOverlay Owner;
        public bool ShowBox, ShowVersion;
        public string Text = "";
        GUIStyle _box, _ver;
        Font _font;

        void OnGUI()
        {
            float s = Mathf.Max(0.5f, Screen.height / 1080f);
            if (_box == null)
            {
                try { _font = CMTokens.Font(CMTokens.Ui); } catch (Exception) { _font = null; }
                _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = false, richText = false };
                _ver = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight };
                if (_font != null) _ver.font = _font;
                _ver.normal.textColor = CMTokens.WarmCream.Color;
            }
            if (ShowBox)
            {
                _box.fontSize = Mathf.RoundToInt(15 * s);
                _box.padding = new RectOffset(Mathf.RoundToInt(10 * s), Mathf.RoundToInt(10 * s), Mathf.RoundToInt(8 * s), Mathf.RoundToInt(8 * s));
                Vector2 size = _box.CalcSize(new GUIContent(Text));
                GUI.Box(new Rect(Screen.width - size.x - 16 * s, 16 * s, size.x, size.y), Text, _box);
            }
            if (ShowVersion && !ShowBox)
            {
                _ver.fontSize = Mathf.RoundToInt(18 * s);
                GUI.Label(new Rect(0, 0, Screen.width - 24 * s, Screen.height - 18 * s), BuildInfo.Label, _ver);
            }
        }
    }
}

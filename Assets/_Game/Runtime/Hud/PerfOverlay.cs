using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
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
            if (_recording)
            {
                MatchFrames.Add(ms);
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
            _recordStart = Time.realtimeSinceStartupAsDouble;
            _recording = true;
        }

        /// <summary>A recorded match: frame times and per-marker means.</summary>
        public sealed class Report
        {
            public int Frames;
            public double Seconds, P50, P95, P99, Mean, Worst, WorstAtSecond;
            public int Over50, Over33, Over20;
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

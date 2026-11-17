using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CriticalMass.Runtime.Flow;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// b13 match video: records one whole autoplay match (the normal gameplay camera and HUD, music and effects) as a
    /// JPEG frame sequence plus the engine's own audio mix, on a deterministic capture clock, for a shareable gameplay film.
    /// View only: it never writes the sim; the match is the ordinary autoplay match (actor 0, the Azure Warrior, on autopilot).
    /// <list type="bullet">
    /// <item><c>-matchVideo DIR</c> (with <c>-autoplay</c>): Time.captureFramerate is set to <c>-matchVideoFps</c> (default 30), so
    /// every video frame is exactly 1/fps of game time whatever the machine does (the sim driver already follows
    /// Time.captureDeltaTime). The wall clock is paced to the same rate while recording, so the HUD's unscaled-time
    /// animations keep their speed. AudioRenderer takes the mix for the whole run (nothing reaches the speakers), and the
    /// samples of each recorded frame are appended to DIR/audio.f32 (float32 interleaved; rate and channels in
    /// DIR/video.json). Frames are DIR/frames/f000000.jpg..., <c>-matchVideoSize WxH</c> (default 1920x1080),
    /// <c>-matchVideoQuality Q</c> (JPEG, default 92), encoded on worker threads.</item>
    /// <item>Recording starts the first frame the real match (not the title backdrop) exists and ends
    /// <c>-matchVideoResultsSeconds</c> (default 10) after the results screen opens; the results screen does not press
    /// Play again by itself meanwhile. The AUTOMATED TEST RUN plate is not shown in a match-video run (a film for people).
    /// DIR/video.json holds the frame numbers of kick-off, the hatch, the match end and the results screen, the score,
    /// the winner and the capture counters; then the game quits (exit 0).</item>
    /// <item><c>-simScout SEEDS</c> (<c>-batchmode -nographics</c>; SEEDS = <c>A-B</c> or <c>A,B,C</c>): sim-only, no view: each seed's
    /// autoplay match (the same MatchRequest path as -autoplay -seed N) stepped to its end, one
    /// <c>[CM.MatchVideo] SCOUT</c> line each (hill seconds, winner, hatch time, knock-outs per team, lead changes), then quit.</item>
    /// </list>
    /// </summary>
    public sealed class MatchVideo : MonoBehaviour
    {
        public const string LogTag = "[CM.MatchVideo]";
        public const string Flag = "-matchvideo";
        public const string ScoutFlag = "-simscout";

        /// <summary>A match-video run: the automation plate stays off and the results screen waits for the recorder.</summary>
        public static bool Active { get; private set; }

        static string Arg(string[] args, string flag)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
                if ((args[i] ?? "").Trim().ToLowerInvariant() == flag) return args[i + 1];
            return null;
        }

        static double Num(string s, double fallback) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Active = false;
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            string scout = Arg(args, ScoutFlag);
            if (!string.IsNullOrEmpty(scout))
            {
                var sg = new GameObject("CriticalMass SimScout") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(sg);
                sg.AddComponent<Scout>().Seeds = scout;
                return;
            }
            string dir = Arg(args, Flag);
            if (string.IsNullOrEmpty(dir) || Application.isBatchMode) return;
            Active = true;
            var go = new GameObject("CriticalMass MatchVideo") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            MatchVideo mv = go.AddComponent<MatchVideo>();
            mv.Configure(args, dir);
        }

        // ---------------------------------------------------------------- capture

        struct Job
        {
            public int Index;
            public byte[] Rgba;
        }

        string _dir, _frames;
        int _fps = 30, _w = 1920, _h = 1080, _quality = 92;
        double _resultsSeconds = 10, _maxSeconds = 720;
        BlockingCollection<Job> _queue;
        readonly ConcurrentBag<byte[]> _pool = new ConcurrentBag<byte[]>();
        Thread[] _workers;
        int _written, _encodeErrors, _readbackErrors, _inFlight;
        RenderTexture _screenRt, _outRt;
        FileStream _audio;
        bool _audioOn;
        int _channels, _rate;
        long _audioFrames;
        SimDriverBehaviour _driver;
        bool _recording, _done;
        int _frame, _late, _startTick = -1;
        int _kickoffFrame = -1, _hatchFrame = -1, _endFrame = -1, _resultsFrame = -1;
        double _t0, _maxLag;
        readonly List<string> _marks = new List<string>();

        void Configure(string[] args, string dir)
        {
            _dir = Path.GetFullPath(dir);
            _frames = Path.Combine(_dir, "frames");
            Directory.CreateDirectory(_frames);
            _fps = Mathf.Clamp((int)Num(Arg(args, "-matchvideofps"), 30), 10, 120);
            _quality = Mathf.Clamp((int)Num(Arg(args, "-matchvideoquality"), 92), 50, 100);
            _resultsSeconds = Math.Max(1, Num(Arg(args, "-matchvideoresultsseconds"), 10));
            _maxSeconds = Math.Max(30, Num(Arg(args, "-matchvideomaxseconds"), 720));
            string size = Arg(args, "-matchvideosize");
            if (!string.IsNullOrEmpty(size))
            {
                string[] p = size.ToLowerInvariant().Split('x');
                if (p.Length == 2) { _w = Mathf.Clamp((int)Num(p[0], 1920), 320, 3840); _h = Mathf.Clamp((int)Num(p[1], 1080), 180, 2160); }
            }
            Time.captureFramerate = _fps;
            _rate = AudioSettings.outputSampleRate;
            _channels = Channels(AudioSettings.speakerMode);
            try { _audioOn = AudioRenderer.Start(); }
            catch (Exception e) { Debug.LogWarning($"{LogTag} AudioRenderer.Start failed: {e.Message}"); _audioOn = false; }
            int n = Mathf.Clamp(SystemInfo.processorCount - 2, 2, 6);
            _queue = new BlockingCollection<Job>(boundedCapacity: 12);
            _workers = new Thread[n];
            for (int i = 0; i < n; i++)
            {
                _workers[i] = new Thread(Work) { IsBackground = true, Name = "MatchVideo JPEG " + i };
                _workers[i].Start();
            }
            Debug.Log($"{LogTag} armed: {_w}x{_h} at {_fps} fps, JPEG q{_quality}, {n} encoders, audio {(_audioOn ? $"{_rate} Hz x{_channels}" : "OFF")}, results {F(_resultsSeconds)} s, into {_dir}");
        }

        static int Channels(AudioSpeakerMode m)
        {
            switch (m)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                case AudioSpeakerMode.Prologic: return 2;
                default: return 2;
            }
        }

        void Start() => StartCoroutine(Loop());

        IEnumerator Loop()
        {
            var eof = new WaitForEndOfFrame();
            while (!_done)
            {
                yield return eof;
                try { Step(); }
                catch (Exception e)
                {
                    Debug.LogError($"{LogTag} step failed at frame {_frame}: {e}");
                    _done = true;
                }
            }
            yield return Finish();
        }

        void Step()
        {
            // The audio mix of this capture frame (drained every frame, kept only while recording).
            if (_audioOn)
            {
                int n = AudioRenderer.GetSampleCountForCaptureFrame();
                if (n > 0)
                {
                    var buf = new NativeArray<float>(n * _channels, Allocator.Temp);
                    AudioRenderer.Render(buf);
                    if (_recording && _audio != null)
                    {
                        byte[] bytes = buf.Reinterpret<byte>(4).ToArray();
                        _audio.Write(bytes, 0, bytes.Length);
                        _audioFrames += n;
                    }
                    buf.Dispose();
                }
            }

            if (!_recording)
            {
                if (!MatchReady()) return;
                _recording = true;
                _startTick = _driver.World.Tick;
                _t0 = Time.realtimeSinceStartupAsDouble;
                _audio = new FileStream(Path.Combine(_dir, "audio.f32"), FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
                Debug.Log($"{LogTag} recording starts: seed {(_driver.Request != null ? _driver.Request.Seed : 0)}, tick {_startTick}, screen {Screen.width}x{Screen.height}");
            }

            World w = _driver != null ? _driver.World : null;
            if (w != null)
            {
                if (_kickoffFrame < 0 && w.Tick > _startTick) Mark(ref _kickoffFrame, "kickoff");
                if (_hatchFrame < 0 && w.Match.Hatched) Mark(ref _hatchFrame, "hatch");
                if (_endFrame < 0 && w.Match.Phase == MatchPhase.Ended) Mark(ref _endFrame, "end");
            }
            if (_resultsFrame < 0 && GameFlow.State == FlowState.Results) Mark(ref _resultsFrame, "results");

            Capture(_frame);
            _frame++;
            if (_frame % (_fps * 30) == 0)
                Debug.Log($"{LogTag} {F(_frame / (double)_fps)} s recorded, {_written} written, lag {F(_maxLag)} s, late {_late}, match {F(w != null ? w.Match.Time : 0)} s");

            bool resultsDone = _resultsFrame >= 0 && _frame - _resultsFrame >= _resultsSeconds * _fps;
            bool stuckAfterEnd = _endFrame >= 0 && _resultsFrame < 0 && _frame - _endFrame >= (_resultsSeconds + 30) * _fps;
            if (resultsDone || stuckAfterEnd || _frame >= _maxSeconds * _fps)
            {
                _done = true;
                Debug.Log($"{LogTag} recording ends at frame {_frame} ({(resultsDone ? "results shown" : stuckAfterEnd ? "no results screen" : "length cap")})");
                return;
            }
            Pace();
        }

        void Mark(ref int slot, string what)
        {
            slot = _frame;
            World w = _driver != null ? _driver.World : null;
            string line = $"{what} frame={_frame} t={F(_frame / (double)_fps)} tick={(w != null ? w.Tick : -1)} match={F(w != null ? w.Match.Time : 0)}";
            _marks.Add(line);
            Debug.Log($"{LogTag} {line}");
        }

        bool MatchReady()
        {
            if (GameFlow.State != FlowState.Match && GameFlow.State != FlowState.Off) return false;
            if (_driver == null || _driver.World == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) return false;
            if (_driver.Request != null && _driver.Request.Backdrop) { _driver = null; return false; }
            return !View.Dressing.WorldView.Building;
        }

        /// <summary>Paces the wall clock to the capture clock while recording (never faster; a slow frame is counted).</summary>
        void Pace()
        {
            double due = _t0 + _frame / (double)_fps;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < due)
            {
                int ms = (int)((due - now) * 1000.0);
                if (ms > 0) Thread.Sleep(Math.Min(ms, 200));
            }
            else
            {
                double lag = now - due;
                if (lag > 1.0 / _fps) _late++;
                if (lag > _maxLag) _maxLag = lag;
            }
        }

        static bool EnsureRt(ref RenderTexture rt, int w, int h)
        {
            if (rt != null && rt.width == w && rt.height == h) return true;
            if (rt != null) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Match Video " + w + "x" + h };
            return rt.Create();
        }

        void Capture(int index)
        {
            if (!EnsureRt(ref _screenRt, Screen.width, Screen.height) || !EnsureRt(ref _outRt, _w, _h))
            {
                _readbackErrors++;
                return;
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenRt);
            if (MomentRecorder.FlipScreenCapture) Graphics.Blit(_screenRt, _outRt, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(_screenRt, _outRt);
            // Bounded: at most 4 readbacks in flight; the worker queue blocks the main thread when the encoders fall behind
            // (harmless: the capture clock does not move while the frame waits).
            if (_inFlight >= 4) AsyncGPUReadback.WaitAllRequests();
            _inFlight++;
            int w = _w, h = _h;
            AsyncGPUReadback.Request(_outRt, 0, TextureFormat.RGBA32, req =>
            {
                _inFlight--;
                if (req.hasError) { _readbackErrors++; return; }
                int bytes = w * h * 4;
                if (!_pool.TryTake(out byte[] buf) || buf.Length != bytes) buf = new byte[bytes];
                req.GetData<byte>().CopyTo(buf);
                _queue.Add(new Job { Index = index, Rgba = buf });
            });
        }

        void Work()
        {
            foreach (Job j in _queue.GetConsumingEnumerable())
            {
                try
                {
                    byte[] jpg = ImageConversion.EncodeArrayToJPG(j.Rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)_w, (uint)_h, 0, _quality);
                    File.WriteAllBytes(Path.Combine(_frames, "f" + j.Index.ToString("D6", CultureInfo.InvariantCulture) + ".jpg"), jpg);
                    Interlocked.Increment(ref _written);
                }
                catch (Exception e)
                {
                    if (Interlocked.Increment(ref _encodeErrors) <= 3) Debug.LogWarning($"{LogTag} frame {j.Index} not written: {e.Message}");
                }
                _pool.Add(j.Rgba);
            }
        }

        IEnumerator Finish()
        {
            AsyncGPUReadback.WaitAllRequests();
            for (int i = 0; i < 10 && _inFlight > 0; i++) yield return null;
            _queue.CompleteAdding();
            foreach (Thread t in _workers) t.Join(60000);
            if (_audioOn)
            {
                try { AudioRenderer.Stop(); } catch (Exception) { }
            }
            if (_audio != null) { _audio.Flush(); _audio.Dispose(); _audio = null; }
            World w = _driver != null ? _driver.World : null;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"version\": \"{BuildInfo.Version}\",\n  \"commit\": \"{BuildInfo.Commit}\",\n");
            sb.Append($"  \"seed\": {(_driver != null && _driver.Request != null ? _driver.Request.Seed : 0)},\n");
            sb.Append($"  \"fps\": {_fps},\n  \"width\": {_w},\n  \"height\": {_h},\n  \"screen\": \"{Screen.width}x{Screen.height}\",\n");
            sb.Append($"  \"quality\": \"{QualitySettings.names[QualitySettings.GetQualityLevel()]}\",\n");
            sb.Append($"  \"frames\": {_frame},\n  \"written\": {_written},\n  \"encode_errors\": {_encodeErrors},\n  \"readback_errors\": {_readbackErrors},\n");
            sb.Append($"  \"late_frames\": {_late},\n  \"max_lag_s\": {F(_maxLag)},\n");
            sb.Append($"  \"audio\": {(_audioOn ? "true" : "false")},\n  \"audio_rate\": {_rate},\n  \"audio_channels\": {_channels},\n  \"audio_frames\": {_audioFrames},\n");
            sb.Append($"  \"kickoff_frame\": {_kickoffFrame},\n  \"hatch_frame\": {_hatchFrame},\n  \"end_frame\": {_endFrame},\n  \"results_frame\": {_resultsFrame},\n");
            if (w != null)
                sb.Append($"  \"hill_azure\": {F(w.Match.HillSeconds.Azure)},\n  \"hill_crimson\": {F(w.Match.HillSeconds.Crimson)},\n  \"winner\": \"{w.Match.Winner}\",\n  \"hatched\": {(w.Match.Hatched ? "true" : "false")},\n  \"match_seconds\": {F(w.Match.Time)},\n");
            sb.Append("  \"marks\": [");
            for (int i = 0; i < _marks.Count; i++) sb.Append((i > 0 ? ", " : "") + "\"" + _marks[i] + "\"");
            sb.Append("]\n}\n");
            File.WriteAllText(Path.Combine(_dir, "video.json"), sb.ToString());
            Debug.Log($"{LogTag} DONE frames={_frame} written={_written} encode_errors={_encodeErrors} readback_errors={_readbackErrors} late={_late} audio_frames={_audioFrames} -> {_dir}/video.json");
            Time.captureFramerate = 0;
            Application.Quit(_written > 0 ? 0 : 3);
        }

        void OnDestroy()
        {
            if (_screenRt != null) _screenRt.Release();
            if (_outRt != null) _outRt.Release();
        }

        // ---------------------------------------------------------------- scout

        /// <summary>-simScout: sim-only matches on several seeds, one summary line each (no view, nothing written to the sim).</summary>
        sealed class Scout : MonoBehaviour
        {
            public string Seeds;

            void Start()
            {
                int code = 0;
                try
                {
                    foreach (int seed in Parse(Seeds)) Debug.Log($"{LogTag} SCOUT {Run(seed)}");
                }
                catch (Exception e)
                {
                    code = 3;
                    Debug.LogError($"{LogTag} SCOUT failed: {e}");
                }
                Application.Quit(code);
            }

            static IEnumerable<int> Parse(string s)
            {
                foreach (string part in (s ?? "").Split(','))
                {
                    string p = part.Trim();
                    int dash = p.IndexOf('-', 1);
                    if (dash > 0 && int.TryParse(p.Substring(0, dash), out int a) && int.TryParse(p.Substring(dash + 1), out int b))
                    {
                        for (int i = a; i <= b; i++) yield return i;
                    }
                    else if (int.TryParse(p, out int one)) yield return one;
                }
            }

            static string Run(int seed)
            {
                GameConfig cfg = GameConfig.CreateDefault();
                World w = World.CreateWorld(MatchRequest.From(MatchSetupChoice.Default, seed).ToWorldOptions(cfg));
                InputFrame scratch = SimInput.EmptyInput();
                var alive = new bool[w.ActorCount];
                for (int i = 0; i < w.ActorCount; i++) alive[i] = w.Actors[i].Alive;
                int koAzure = 0, koCrimson = 0, leadChanges = 0, lastLead = 0;
                double hatchAt = -1;
                int hz = Math.Max(1, (int)Math.Round(1.0 / SimStep.Dt(w.Config)));
                for (int t = 0; t < 120000 && w.Match.Phase != MatchPhase.Ended; t++)
                {
                    MatchTick.Advance(w, null, scratch);
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        bool a = w.Actors[i].Alive;
                        if (alive[i] && !a) { if (w.Actors[i].Team == TeamId.Azure) koAzure++; else koCrimson++; }
                        alive[i] = a;
                    }
                    if (hatchAt < 0 && w.Match.Hatched) hatchAt = w.Match.Time;
                    if (w.Tick % hz == 0)
                    {
                        double d = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                        int lead = d > 1 ? 1 : d < -1 ? -1 : 0;
                        if (lead != 0 && lastLead != 0 && lead != lastLead) leadChanges++;
                        if (lead != 0) lastLead = lead;
                    }
                }
                return $"seed={seed} azure={F(w.Match.HillSeconds.Azure)} crimson={F(w.Match.HillSeconds.Crimson)} winner={w.Match.Winner} " +
                       $"hatched={(w.Match.Hatched ? 1 : 0)} hatch_at={F(hatchAt)} ko_azure={koAzure} ko_crimson={koCrimson} lead_changes={leadChanges} " +
                       $"match={F(w.Match.Time)} ticks={w.Tick}";
            }
        }
    }
}

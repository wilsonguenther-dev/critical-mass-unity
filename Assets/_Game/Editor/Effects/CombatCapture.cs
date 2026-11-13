using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Effects
{
    /// <summary>
    /// Combat-feel captures and the effects budget, in batch mode WITH a graphics device:
    ///   Unity -batchmode -projectPath . -executeMethod CriticalMass.Editor.Effects.CombatCapture.Run -fxOut DIR
    /// Boot scene, 16v16 bots, wall clock stopped (SimDriverBehaviour.CaptureMode); the match is stepped one tick at a time
    /// until each moment happens (a crowded hit, a death, the Kaiju slam telegraph, the slam), then a few ticks more, and
    /// the gameplay camera frames it. At the fight it also measures 40 frames with effects on and 40 with them off:
    /// wall ms per frame (view sync + effect rebuild + render + 1 px readback) and the Render profiler counters.
    /// Writes fx-*.png and fx-report.json into DIR.
    /// </summary>
    public static class CombatCapture
    {
        const string ActiveKey = "CM.FxCapture.Active", OutKey = "CM.FxCapture.Out";
        const int W = 1920, H = 1080, MaxFrames = 20000, Settle = 10, PerfFrames = 40;

        struct Shot { public string Name; public SimEventType Type; public int After; public int MinTick; }
        static readonly Shot[] Shots =
        {
            new Shot { Name = "fight", Type = SimEventType.Hit, After = 3, MinTick = 1350 },
            new Shot { Name = "death", Type = SimEventType.Death, After = 14, MinTick = 1500 },
            new Shot { Name = "slam-telegraph", Type = SimEventType.KaijuTelegraph, After = 20, MinTick = 0 },
            new Shot { Name = "slam", Type = SimEventType.KaijuSlam, After = 4, MinTick = 0 },
        };

        static int _phase, _frames, _shot, _settle, _head, _perf;
        static bool _found;
        static double _fx, _fz;
        static SimDriverBehaviour _driver;
        static CombatFx _combat;
        static readonly List<string> Report = new List<string>();
        static readonly List<double> Ms = new List<double>();
        static readonly List<long> Draws = new List<long>(), Batches = new List<long>(), SetPass = new List<long>();
        static ProfilerRecorder _draws, _batches, _setpass;
        static RenderTexture _rt;
        static Texture2D _px;

        static CombatCapture()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        public static void Run()
        {
            string outDir = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-fxOut") outDir = args[i + 1];
            if (string.IsNullOrEmpty(outDir)) { Debug.LogError("[FxCapture] -fxOut DIR required"); EditorApplication.Exit(64); return; }
            Directory.CreateDirectory(outDir);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(OutKey, Path.GetFullPath(outDir));
            EditorSceneManager.OpenScene(GameBootstrap.BootScenePath, OpenSceneMode.Single);
            SimDriverBehaviour.CaptureMode = true;
            Hook();
            EditorApplication.EnterPlaymode();
        }

        static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        static void Update()
        {
            try
            {
                if (++_frames > MaxFrames) { Debug.LogError("[FxCapture] timed out in phase " + _phase); Finish(2); return; }
                if (!EditorApplication.isPlaying) return;
                switch (_phase)
                {
                    case 0: Begin(); break;
                    case 1: Hunt(); break;
                    case 2: Frame(); break;
                    case 3: Perf(); break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(1);
            }
        }

        static void Begin()
        {
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            _combat = UnityEngine.Object.FindAnyObjectByType<CombatFx>();
            if (_driver == null || _combat == null || _combat.Feed == null) return;
            _driver.Ticked += OnTick;
            _head = _driver.World.EventHead;
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _rt.Create();
            _px = new Texture2D(1, 1, TextureFormat.RGB24, false);
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            Debug.Log($"[FxCapture] actors={_driver.World.ActorCount} blood={BloodSetting.Enabled}");
            _phase = 1;
        }

        static void OnTick(World w)
        {
            if (_found || _phase != 1 || _shot >= Shots.Length) { _head = w.EventHead; return; }
            Shot s = Shots[_shot];
            for (int n = Math.Max(_head, w.EventHead - World.EventCapacity); n < w.EventHead && !_found; n++)
            {
                SimEvent e = w.Events[n % World.EventCapacity];
                if (e.Type != s.Type || w.Tick < s.MinTick) continue;
                if (s.Type == SimEventType.Hit && Crowd(w, e.X, e.Z) < 6) continue;
                _found = true;
                _fx = e.X;
                _fz = e.Z;
            }
            _head = w.EventHead;
        }

        static int Crowd(World w, double x, double z)
        {
            int n = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                double dx = w.X[i] - x, dz = w.Z[i] - z;
                if (dx * dx + dz * dz < 16) n++;
            }
            return n;
        }

        static void Hunt()
        {
            for (int k = 0; k < 900 && !_found; k++) _driver.StepTicks(1);
            if (_driver.World.Tick > 30 * 470 && !_found)
            {
                Report.Add($"{{\"shot\": \"{Shots[_shot].Name}\", \"missing\": true, \"tick\": {_driver.World.Tick}}}");
                _shot++;
                if (_shot >= Shots.Length) { WriteReport(); Finish(0); }
                return;
            }
            if (!_found) return;
            _driver.StepTicks(Shots[_shot].After);
            CameraFollowTarget follow = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            follow.ClearOverride();
            follow.SetOverride(_fx, _fz);
            _settle = Settle;
            _phase = 2;
        }

        static void Frame()
        {
            if (--_settle > 0) return;
            Shot s = Shots[_shot];
            string dir = SessionState.GetString(OutKey, ".");
            string path = Path.Combine(dir, "fx-" + s.Name + ".png");
            Render(path);
            World w = _driver.World;
            double now = w.Tick / _driver.Config.SimHz;
            Report.Add(string.Format(CultureInfo.InvariantCulture,
                "{{\"shot\": \"{0}\", \"tick\": {1}, \"x\": {2:0.0}, \"z\": {3:0.0}, \"effectsActive\": {4}, \"decals\": {5}, \"verts\": {6}, \"dropped\": {7}, \"decoded\": {8}, \"lapped\": {9}, \"audioPlayed\": {10}, \"audioRefused\": {11}, \"clips\": {12}, \"mixer\": {13}}}",
                s.Name, w.Tick, _fx, _fz, _combat.Scene.ActiveCount(now), _combat.Scene.DecalCount(now), _combat.LastVerts, _combat.LastDropped,
                _combat.Feed.Decoded, _combat.Feed.Lapped, _combat.Audio.Played, _combat.Audio.Refused, _combat.Audio.ClipsLoaded,
                _combat.Audio.Mixer != null ? "true" : "false"));
            Debug.Log("[FxCapture] wrote " + path);
            if (s.Name == "fight") { _phase = 3; _perf = 0; return; }
            NextShot();
        }

        static void NextShot()
        {
            _shot++;
            _found = false;
            if (_shot >= Shots.Length) { WriteReport(); Finish(0); return; }
            _phase = 1;
        }

        static void Perf()
        {
            // Frames 0..PerfFrames-1 effects on, then PerfFrames..2*PerfFrames-1 off; recorders read the previous frame.
            bool on = _perf < PerfFrames + 2;
            if (_perf >= 2 && _perf != PerfFrames + 2 && _perf != PerfFrames + 3)
            {
                Draws.Add(_draws.LastValue);
                Batches.Add(_batches.LastValue);
                SetPass.Add(_setpass.LastValue);
            }
            if (_perf == PerfFrames + 2)
            {
                Report.Add(PerfLine("effects-on"));
                _combat.enabled = false;
            }
            if (_perf >= 2 * PerfFrames + 4)
            {
                Report.Add(PerfLine("effects-off"));
                _combat.enabled = true;
                NextShot();
                return;
            }
            var views = _driver.GetComponent<ActorViews>();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            views.Sync(1f / 60f);
            RenderOnce(cam);
            sw.Stop();
            if (_perf >= 2 && _perf != PerfFrames + 2 && _perf != PerfFrames + 3) Ms.Add(sw.Elapsed.TotalMilliseconds);
            _perf++;
        }

        static string PerfLine(string name)
        {
            Ms.Sort();
            double med = Ms.Count > 0 ? Ms[Ms.Count / 2] : -1, p95 = Ms.Count > 0 ? Ms[(int)(Ms.Count * 0.95)] : -1;
            string line = string.Format(CultureInfo.InvariantCulture,
                "{{\"perf\": \"{0}\", \"frames\": {1}, \"frameMsMedian\": {2:0.00}, \"frameMsP95\": {3:0.00}, \"drawCallsMedian\": {4}, \"batchesMedian\": {5}, \"setPassMedian\": {6}}}",
                name, Ms.Count, med, p95, Median(Draws), Median(Batches), Median(SetPass));
            Ms.Clear(); Draws.Clear(); Batches.Clear(); SetPass.Clear();
            Debug.Log("[FxCapture] " + line);
            return line;
        }

        static long Median(List<long> v)
        {
            if (v.Count == 0) return -1;
            v.Sort();
            return v[v.Count / 2];
        }

        static void RenderOnce(UnityEngine.Camera cam)
        {
            cam.aspect = (float)W / H;
            var req = new RenderPipeline.StandardRequest { destination = _rt };
            RenderPipeline.SubmitRenderRequest(cam, req);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            RenderTexture.active = prev;
        }

        static void Render(string path)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _driver.GetComponent<ActorViews>().Sync(1f / 60f);
            cam.targetTexture = _rt;
            cam.aspect = (float)W / H;
            Canvas.ForceUpdateCanvases();
            var req = new RenderPipeline.StandardRequest { destination = _rt };
            RenderPipeline.SubmitRenderRequest(cam, req);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            cam.ResetAspect();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void WriteReport()
        {
            string dir = SessionState.GetString(OutKey, ".");
            File.WriteAllText(Path.Combine(dir, "fx-report.json"), "[\n  " + string.Join(",\n  ", Report) + "\n]\n");
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Update;
            SessionState.SetBool(ActiveKey, false);
            if (_driver != null) _driver.Ticked -= OnTick;
            _draws.Dispose(); _batches.Dispose(); _setpass.Dispose();
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.Exit(code);
        }
    }
}

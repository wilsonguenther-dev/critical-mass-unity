using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor
{
    /// <summary>
    /// Gameplay-camera capture (art direction 2026-11-12, section 8 checklist renders). Loads the Boot scene, enters Play
    /// Mode, runs the 16v16 bot match (actor 0 on autopilot, GameConfig.CreateDefault seed 1337) for a fixed number of
    /// simulated seconds by StepTicks with the wall clock stopped, then frames three targets with the gameplay camera
    /// (CameraFollowTarget.SetOverride) and writes 1920x1080 PNGs plus capture.json to the output folder. Needs graphics
    /// (no -nographics). Run (tools/unity-capture.sh wraps it):
    /// Unity -batchmode -projectPath . -executeMethod CriticalMass.Editor.CaptureTool.Run -captureOut DIR [-captureSeconds 30]
    /// Exit codes: 0 written, 1 exception, 2 timed out in frames, 64 bad arguments.
    /// </summary>
    [InitializeOnLoad]
    public static class CaptureTool
    {
        public const int Width = 1920;
        public const int Height = 1080;
        public const double DefaultSeconds = 30;
        private const int MaxFrames = 20000;
        private const int SettleFrames = 6;
        private const string ActiveKey = "cm.capture.active";
        private const string OutKey = "cm.capture.out";
        private const string SecondsKey = "cm.capture.seconds";

        private static int _frames;
        private static int _phase;
        private static int _ticksDone;
        private static int _ticksWanted;
        private static int _target;
        private static int _settle;
        private static SimDriverBehaviour _driver;
        private static readonly List<string> Written = new List<string>();

        // Survives a domain reload on entering Play Mode should the project ever turn it back on.
        static CaptureTool()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        /// <summary>Batch entry point.</summary>
        public static void Run()
        {
            try
            {
                string outDir = Arg("-captureOut");
                if (string.IsNullOrEmpty(outDir))
                {
                    Debug.LogError("[CaptureTool] -captureOut DIR is required");
                    EditorApplication.Exit(64);
                    return;
                }
                string secs = Arg("-captureSeconds");
                double seconds = string.IsNullOrEmpty(secs) ? DefaultSeconds : double.Parse(secs, CultureInfo.InvariantCulture);
                Directory.CreateDirectory(outDir);
                SessionState.SetBool(ActiveKey, true);
                SessionState.SetString(OutKey, Path.GetFullPath(outDir));
                SessionState.SetFloat(SecondsKey, (float)seconds);
                EditorSceneManager.OpenScene(GameBootstrap.BootScenePath, OpenSceneMode.Single);
                Hook();
                SimDriverBehaviour.CaptureMode = true;
                Debug.Log($"[CaptureTool] start out={outDir} seconds={seconds}");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            try
            {
                if (++_frames > MaxFrames)
                {
                    Debug.LogError($"[CaptureTool] timed out at phase {_phase} after {MaxFrames} frames");
                    Finish(2);
                    return;
                }
                if (!EditorApplication.isPlaying) return;
                switch (_phase)
                {
                    case 0: Begin(); break;
                    case 1: Simulate(); break;
                    case 2: Shoot(); break;
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void Begin()
        {
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) return;
            // SimDriverBehaviour.CaptureMode (set before Play) already started it on autopilot with the clock stopped, so
            // no frame-time tick ran; the tick check before each shot proves it.
            if (_driver.World.Tick != 0) throw new InvalidOperationException($"world tick {_driver.World.Tick} at start: frame-time ticks ran");
            double seconds = SessionState.GetFloat(SecondsKey, (float)DefaultSeconds);
            _ticksWanted = (int)Math.Round(seconds * _driver.Config.SimHz);
            _ticksDone = 0;
            Debug.Log($"[CaptureTool] seed={_driver.Config.Seed} teamSize={_driver.Config.TeamSize} actors={_driver.World.ActorCount} ticks={_ticksWanted}");
            _phase = 1;
        }

        private static void Simulate()
        {
            int n = Math.Min(150, _ticksWanted - _ticksDone);
            if (n > 0)
            {
                _driver.StepTicks(n);
                _ticksDone += n;
                return;
            }
            _phase = 2;
            _target = 0;
            _settle = -1;
        }

        private static (string Name, double X, double Z)[] Targets(MapDef map)
        {
            PadDef azure = map.Pads[(int)TeamId.Azure];
            return new[]
            {
                ("base-azure", azure.X, azure.Z),
                ("hill", map.Hill.X, map.Hill.Z),
                ("shrine", map.Shrine.X, map.Shrine.Z),
            };
        }

        private static void Shoot()
        {
            var targets = Targets(_driver.World.Map);
            if (_target >= targets.Length)
            {
                WriteManifest(targets);
                Finish(0);
                return;
            }
            CameraFollowTarget follow = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            if (follow == null) throw new InvalidOperationException("no CameraFollowTarget in the scene");
            if (_settle < 0)
            {
                follow.ClearOverride();
                follow.SetOverride(targets[_target].X, targets[_target].Z);
                _settle = SettleFrames;
                return;
            }
            if (--_settle > 0) return;

            UnityEngine.Camera main = UnityEngine.Camera.main;
            GameObject hillGo = GameObject.Find("Hill");
            Debug.Log($"[CaptureTool] {targets[_target].Name}: target={follow.transform.position} cam={main.transform.position} " +
                      $"euler={main.transform.eulerAngles} fov={main.fieldOfView} ortho={main.orthographic} " +
                      $"expectedCam={GameplayCamera.PositionFor(_driver.Config, follow.transform.position)} " +
                      $"hill={(hillGo != null ? hillGo.transform.position + " scale " + hillGo.transform.lossyScale + " active " + hillGo.activeInHierarchy + " mat " + hillGo.GetComponent<Renderer>().sharedMaterial.shader.name : "none")}");
            if (_driver.World.Tick != _ticksWanted)
                throw new InvalidOperationException($"world tick {_driver.World.Tick} != {_ticksWanted}: frame-time ticks leaked in, the capture is not deterministic");
            string path = Path.Combine(SessionState.GetString(OutKey, "."), "capture-" + targets[_target].Name + ".png");
            RenderTo(path);
            Written.Add(path);
            Debug.Log("[CaptureTool] wrote " + path);
            _target++;
            _settle = -1;
        }

        private static void RenderTo(string path)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) throw new InvalidOperationException("no main camera");
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4,
            };
            rt.Create();
            RenderTexture prevActive = RenderTexture.active;
            cam.aspect = (float)Width / Height;
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.ResetAspect();
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static void WriteManifest((string Name, double X, double Z)[] targets)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"seed\": {_driver.Config.Seed},\n  \"teamSize\": {_driver.Config.TeamSize},\n");
            sb.Append($"  \"ticks\": {_ticksDone},\n  \"simSeconds\": {(_ticksDone / _driver.Config.SimHz).ToString(CultureInfo.InvariantCulture)},\n");
            sb.Append($"  \"worldTick\": {_driver.World.Tick},\n  \"width\": {Width},\n  \"height\": {Height},\n");
            sb.Append($"  \"camera\": {{\"projection\": \"perspective\", \"pitchDeg\": {GameplayCamera.PitchDeg.ToString(CultureInfo.InvariantCulture)}, \"fovDeg\": {GameplayCamera.FovDeg.ToString(CultureInfo.InvariantCulture)}, \"distance\": {GameplayCamera.Distance.ToString(CultureInfo.InvariantCulture)}, \"yawDeg\": {_driver.Config.Render.CameraYawDeg.ToString(CultureInfo.InvariantCulture)}}},\n");
            sb.Append("  \"targets\": [\n");
            for (int i = 0; i < targets.Length; i++)
            {
                sb.Append($"    {{\"name\": \"{targets[i].Name}\", \"simX\": {targets[i].X.ToString(CultureInfo.InvariantCulture)}, \"simZ\": {targets[i].Z.ToString(CultureInfo.InvariantCulture)}, \"file\": \"{Path.GetFileName(Written[i])}\"}}");
                sb.Append(i + 1 < targets.Length ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(SessionState.GetString(OutKey, "."), "capture.json"), sb.ToString());
        }

        private static void Fail(Exception e)
        {
            Debug.LogException(e);
            Finish(1);
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Update;
            SimDriverBehaviour.CaptureMode = false;
            SessionState.EraseBool(ActiveKey);
            Debug.Log($"[CaptureTool] exit {code}, {Written.Count} file(s)");
            EditorApplication.Exit(code);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}

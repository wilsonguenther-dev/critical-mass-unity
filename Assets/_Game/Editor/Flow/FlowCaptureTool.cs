using System;
using System.IO;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// DG-01 captures, one per screen, at the gameplay camera, 1920x1080 PNGs: flow-title, flow-menu, flow-setup (Crimson,
    /// Miner, 5 v 5), flow-match (the HUD, 20 sim seconds in) and flow-results (the clock run out). Usage: Unity -batchmode
    /// -projectPath P -executeMethod CriticalMass.Editor.Flow.FlowCaptureTool.Run -flowCaptureOut DIR (needs a GPU: no
    /// -nographics). Exit 0 with five PNGs, non-zero on any failure. Same render path as MenuCaptureTool.
    /// </summary>
    public static class FlowCaptureTool
    {
        const int Width = 1920, Height = 1080, MaxFrames = 6000;
        const float Settle = 0.35f;   // real seconds after a screen change: its 180 ms transition has finished
        static string _out;
        static int _frames, _phase, _shots;
        static double _at;
        static SimDriverBehaviour _matchFrom;

        public static void Run()
        {
            try
            {
                _out = Arg("-flowCaptureOut");
                if (string.IsNullOrEmpty(_out)) throw new ArgumentException("-flowCaptureOut DIR is required");
                Directory.CreateDirectory(_out);
                _frames = _phase = _shots = 0;
                _matchFrom = null;
                EditorSceneManager.OpenScene(GameBootstrap.BootScenePath, OpenSceneMode.Single);
                SimDriverBehaviour.CaptureMode = true;
                GameFlow.ForceEnabled = true;
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("[FlowCaptureTool] " + e);
                EditorApplication.Exit(1);
            }
        }

        static bool Waited() => EditorApplication.timeSinceStartup - _at >= Settle;

        static void Next()
        {
            _phase++;
            _at = EditorApplication.timeSinceStartup;
        }

        static void Update()
        {
            try
            {
                if (++_frames > MaxFrames) throw new TimeoutException($"phase {_phase} after {MaxFrames} frames");
                if (!EditorApplication.isPlaying) return;
                SimDriverBehaviour driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
                FlowDirector d = FlowDirector.Current;
                if (driver == null || driver.World == null || d == null || d.Loading) return;
                switch (_phase)
                {
                    case 0:
                        if (UnityEngine.Object.FindAnyObjectByType<TitleScreen>() == null) return;
                        driver.StepTicks((int)(20 * driver.Config.SimHz));
                        Next();
                        return;
                    case 1:
                        if (!Waited()) return;
                        Shot("flow-title");
                        UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
                        Next();
                        return;
                    case 2:
                        if (!Waited()) return;
                        Shot("flow-menu");
                        d.ShowSetup();
                        d.Setup.SetTeam(TeamId.Crimson);
                        d.Setup.SetClass(ClassId.Miner);
                        d.Setup.SetSize(5);
                        Next();
                        return;
                    case 3:
                        if (!Waited()) return;
                        Shot("flow-setup");
                        _matchFrom = driver;
                        d.Setup.StartMatch();
                        Next();
                        return;
                    case 4:
                        if (driver == _matchFrom || GameFlow.State != FlowState.Match) return;
                        driver.StepTicks((int)(20 * driver.Config.SimHz));
                        Next();
                        return;
                    case 5:
                        if (!Waited()) return;
                        Shot("flow-match");
                        driver.World.Match.Time = driver.Config.MatchSeconds - 2.0 / driver.Config.SimHz;
                        driver.StepTicks(4);
                        Next();
                        return;
                    case 6:
                        if (!d.Results.IsVisible) return;
                        _at = EditorApplication.timeSinceStartup;
                        Next();
                        return;
                    case 7:
                        if (!Waited()) return;
                        Shot("flow-results");
                        Finish(_shots == 5 ? 0 : 1);
                        return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[FlowCaptureTool] " + e);
                Finish(1);
            }
        }

        static void Shot(string name)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) throw new InvalidOperationException("no main camera");
            string path = Path.Combine(_out, name + ".png");
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            RenderTexture prev = RenderTexture.active;
            cam.targetTexture = rt;
            cam.aspect = (float)Width / Height;
            try
            {
                foreach (FlowScreen s in UnityEngine.Object.FindObjectsByType<FlowScreen>(FindObjectsSortMode.None)) s.UpdateScale();
                TitleScreen title = UnityEngine.Object.FindAnyObjectByType<TitleScreen>();
                if (title != null) title.UpdateScale();
                GameHud hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
                if (hud != null) hud.UpdateScale();
                Canvas.ForceUpdateCanvases();
                Canvas.ForceUpdateCanvases();
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                _shots++;
                Debug.Log("[FlowCaptureTool] wrote " + path);
            }
            finally
            {
                RenderTexture.active = prev;
                cam.targetTexture = null;
                cam.ResetAspect();
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Update;
            SimDriverBehaviour.CaptureMode = false;
            GameFlow.ForceEnabled = false;
            Debug.Log($"[FlowCaptureTool] exit {code}, {_shots} shot(s) in {_out}");
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}

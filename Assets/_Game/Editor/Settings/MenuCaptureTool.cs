using System;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CriticalMass.Editor.Settings
{
    /// <summary>
    /// Captures the pause menu and each settings tab at the gameplay camera over a live bot match, 1920x1080 PNGs, for
    /// review. Usage: Unity -batchmode -projectPath P -executeMethod CriticalMass.Editor.Settings.MenuCaptureTool.Run
    /// -menuCaptureOut DIR (needs a GPU: no -nographics). Exit 0 with five PNGs, non-zero on any failure.
    /// </summary>
    public static class MenuCaptureTool
    {
        const int Width = 1920, Height = 1080, MaxFrames = 3000;
        static readonly string[] Shots = { "menu-root", "settings-controls", "settings-audio", "settings-video", "settings-game" };
        static string _out;
        static int _frames, _phase, _shot, _wait;

        public static void Run()
        {
            try
            {
                _out = Arg("-menuCaptureOut");
                if (string.IsNullOrEmpty(_out)) throw new ArgumentException("-menuCaptureOut DIR is required");
                Directory.CreateDirectory(_out);
                _frames = _phase = _shot = _wait = 0;
                EditorSceneManager.OpenScene(GameBootstrap.BootScenePath, OpenSceneMode.Single);
                SimDriverBehaviour.CaptureMode = true;
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("[MenuCaptureTool] " + e);
                EditorApplication.Exit(1);
            }
        }

        static void Update()
        {
            try
            {
                if (++_frames > MaxFrames) throw new TimeoutException($"phase {_phase} after {MaxFrames} frames");
                if (!EditorApplication.isPlaying) return;
                SimDriverBehaviour driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
                PauseMenu menu = UnityEngine.Object.FindAnyObjectByType<PauseMenu>();
                if (driver == null || menu == null) return;
                if (_phase == 0)
                {
                    driver.StepTicks((int)(20 * driver.Config.SimHz));   // 20 sim seconds: the crowd has left the pads
                    menu.Pause();
                    _phase = 1;
                    _wait = 3;
                    return;
                }
                if (_wait-- > 0) return;
                if (_shot >= Shots.Length)
                {
                    Finish(0);
                    return;
                }
                string name = Shots[_shot];
                if (name == "menu-root") menu.ShowRoot();
                else menu.ShowSettings((PauseMenu.Tab)(_shot - 1));
                Render(menu, Path.Combine(_out, name + ".png"));
                _shot++;
                _wait = 2;
            }
            catch (Exception e)
            {
                Debug.LogError("[MenuCaptureTool] " + e);
                Finish(1);
            }
        }

        static void Render(PauseMenu menu, string path)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) throw new InvalidOperationException("no main camera");
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            RenderTexture prev = RenderTexture.active;
            cam.targetTexture = rt;
            cam.aspect = (float)Width / Height;
            try
            {
                menu.UpdateScale();
                CriticalMass.Runtime.Hud.GameHud hud = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Hud.GameHud>();
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
            Debug.Log($"[MenuCaptureTool] exit {code}, {_shot} shot(s) in {_out}");
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

using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// hud-art-1 (2026-11-13): the art review walk for the HUD and the big-moment cut-ins, windowless.
    /// 1. A Quick match (seed 4242, Warrior, Azure); the busy HUD at 3024x1964.
    /// 2. Big moments mid-hold at 3024x1964: Kaiju berserk, the hatch (Azure), allegiance turns (Crimson), and class cards
    ///    for every class on alternating teams, plus the team finisher.
    /// 3. The minimap source: an orthographic, straight-down render of the generated arena at the gameplay camera's yaw,
    ///    framed exactly by MinimapProjection (the projection the HUD markers use), 2x the 4x tier, cleared to magenta,
    ///    written as minimap-world-ortho.png beside minimap-world-ortho.json. Resources/UI/Minimap/_source~/minimap_world.py
    ///    turns it into the toon minimap tiers.
    /// Usage: Unity -batchmode -projectPath P -executeMethod CriticalMass.Editor.Flow.HudArtCaptureTool.Run -walkOut DIR
    /// [-hudArtMoments none] (needs a GPU: no -nographics). Exit 0 when every shot was written.
    /// </summary>
    public static class HudArtCaptureTool
    {
        const int W = 3024, H = 1964;
        const int MaxFrames = 200000;
        static string _out;
        static bool _moments = true;
        static int _frames, _shots, _wanted;
        static IEnumerator<Func<bool>> _walk;
        static Func<bool> _wait;
        static readonly List<string> Log = new List<string>();
        static RenderTexture _rt;

        struct Cut
        {
            public string Name;
            public MomentId Id;
            public ClassId Cls;
            public TeamId Team;
            public KaijuState Kaiju;
            public Cut(string n, MomentId id, ClassId c, TeamId t, KaijuState k) { Name = n; Id = id; Cls = c; Team = t; Kaiju = k; }
        }

        static readonly Cut[] Cuts =
        {
            new Cut("20-berserk", MomentId.Berserk, ClassId.Warrior, TeamId.Azure, KaijuState.Berserk),
            new Cut("21-hatch-azure", MomentId.Hatch, ClassId.Warrior, TeamId.Azure, KaijuState.Azure),
            new Cut("22-sides-crimson", MomentId.AllegianceTurns, ClassId.Warrior, TeamId.Crimson, KaijuState.Crimson),
            new Cut("23-warrior-azure", MomentId.OpeningStrike, ClassId.Warrior, TeamId.Azure, KaijuState.Neutral),
            new Cut("24-farmer-crimson", MomentId.Overcharged, ClassId.Farmer, TeamId.Crimson, KaijuState.Neutral),
            new Cut("25-miner-azure", MomentId.HillCleared, ClassId.Miner, TeamId.Azure, KaijuState.Neutral),
            new Cut("26-demolitions-crimson", MomentId.BlastRadius, ClassId.Demolitions, TeamId.Crimson, KaijuState.Neutral),
            new Cut("27-builder-azure", MomentId.BatteringRam, ClassId.Builder, TeamId.Azure, KaijuState.Neutral),
            new Cut("28-healer-crimson", MomentId.KaijuDown, ClassId.Healer, TeamId.Crimson, KaijuState.Neutral),
            new Cut("29-finisher-azure", MomentId.Finisher, ClassId.Warrior, TeamId.Azure, KaijuState.Neutral),
        };

        public static void Run()
        {
            try
            {
                _out = Arg("-walkOut");
                if (string.IsNullOrEmpty(_out)) throw new ArgumentException("-walkOut DIR is required");
                _moments = Arg("-hudArtMoments") != "none";
                Directory.CreateDirectory(_out);
                _frames = _shots = _wanted = 0;
                Log.Clear();
                EditorSceneManager.OpenScene(GameBootstrap.BootScenePath, OpenSceneMode.Single);
                GameFlow.ForceEnabled = true;
                _walk = Walk();
                _wait = null;
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("[HudArt] " + e);
                EditorApplication.Exit(1);
            }
        }

        static SimDriverBehaviour Driver => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
        static World Wd => Driver != null ? Driver.World : null;
        static FlowDirector D => FlowDirector.Current;

        static Func<bool> Seconds(double s)
        {
            double until = EditorApplication.timeSinceStartup + s;
            return () => EditorApplication.timeSinceStartup >= until;
        }

        static Func<bool> StepFrames(int n)
        {
            int left = n;
            return () =>
            {
                if (left <= 0) return true;
                if (Driver != null) Driver.StepTicks(1);
                left--;
                return false;
            };
        }

        static void Note(string s)
        {
            Log.Add($"{EditorApplication.timeSinceStartup:0.0}s {s}");
            Debug.Log("[HudArt] " + s);
        }

        static void Bind()
        {
            if (_rt == null)
            {
                _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                _rt.Create();
            }
            HudSafeArea.SetOverride(new Vector4(0f, 74f, 0f, 0f));
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null) { cam.targetTexture = _rt; cam.aspect = (float)W / H; }
        }

        static IEnumerator<Func<bool>> Walk()
        {
            yield return () => Driver != null && Wd != null && D != null && !D.Loading &&
                               UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null;
            StoryIntro.Skip();
            yield return Seconds(1.0);
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return Seconds(0.6);
            D.HideFlowScreens();
            LoadingScreen.UseInFlow = false;
            SimDriverBehaviour from = Driver;
            MatchSetupChoice quick = PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 8);
            quick.Class = ClassId.Warrior;
            quick.Team = TeamId.Azure;
            D.ShowReady(new PlayLaunch { Choice = quick, Seed = 4242, BackTo = LobbyScreen.ModeSelect });
            double startUntil = EditorApplication.timeSinceStartup + 60;
            yield return () => (Driver != null && Driver != from && Wd != null && GameFlow.State == FlowState.Match && !D.Loading &&
                                !LoadingScreen.IsOpen) || EditorApplication.timeSinceStartup > startUntil;
            SimDriverBehaviour drv = Driver;
            {
                double rt = EditorApplication.timeSinceStartup + 30;
                yield return () => (Wd.Match.Phase == MatchPhase.Playing && Wd.Actors[0].Alive) || EditorApplication.timeSinceStartup > rt;
            }
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            drv.HumanAutopilot = false;
            drv.TimeScale = 0;
            BigMomentsSettings.ModeOverride = BigMomentsMode.Off;
            BigMoments.StopAll();
            yield return StepFrames(15);

            // 3 first: the minimap source render (no HUD; its own camera).
            WorldOrtho();
            yield return Seconds(0.2);

            Bind();
            Busy(drv);
            yield return StepFrames(3);
            yield return Seconds(0.4);
            Shot("11-busy-hud");

            if (_moments)
            {
                drv.TimeScale = 1;
                BigMomentsSettings.ModeOverride = BigMomentsMode.Full;
                foreach (Cut c in Cuts)
                {
                    BigMoments.StopAll();
                    yield return Seconds(0.3);
                    BigMoments.Play(c.Id, c.Cls, c.Team, c.Kaiju);
                    double t0 = EditorApplication.timeSinceStartup;
                    yield return Seconds(0.75);
                    Shot(c.Name);
                    Note($"{c.Name}: portrait source {BigMoments.LastPortraitSource}, shot at {EditorApplication.timeSinceStartup - t0:0.00}s");
                    double until = EditorApplication.timeSinceStartup + 6;
                    yield return () => !BigMoments.IsPlaying || EditorApplication.timeSinceStartup > until;
                    Note($"{c.Name}: on screen about {EditorApplication.timeSinceStartup - t0:0.00}s wall");
                }
                BigMomentsSettings.ModeOverride = BigMomentsMode.Off;
                BigMoments.StopAll();
            }
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }

        /// <summary>Orthographic straight-down render framed by the minimap projection (2x the 4x tier).</summary>
        static void WorldOrtho()
        {
            _wanted++;
            try
            {
                GameConfig cfg = GameConfig.CreateDefault();
                MinimapProjection p = MinimapProjection.For(MinimapGeometry.BakedMap, cfg.Render.CameraYawDeg);
                const int Tier = 8;
                int pw = p.Width * Tier, ph = p.Height * Tier;
                // pixel centre in (u, v) sim units -> sim (x, z): the rotation is orthonormal, so x = u Rx + v Dx, z = u Rz + v Dz
                double uc = p.U0 + p.Width * 0.5 / MinimapProjection.PxPerUnit, vc = p.V0 + p.Height * 0.5 / MinimapProjection.PxPerUnit;
                double sx = uc * p.Rx + vc * p.Dx, sz = uc * p.Rz + vc * p.Dz;
                Vector3 centre = SimSpace.ToWorld(sx, 0.0, sz);
                // screen up = sim (UpX, UpZ) -> world (UpX, -UpZ)
                Vector3 up = new Vector3((float)p.UpX, 0f, (float)-p.UpZ).normalized;
                var go = new GameObject("HudArt Ortho Camera");
                UnityEngine.Camera cam = go.AddComponent<UnityEngine.Camera>();
                cam.orthographic = true;
                cam.orthographicSize = (float)(p.Height * 0.5 / MinimapProjection.PxPerUnit);
                cam.aspect = (float)p.Width / p.Height;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 600f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(1f, 0f, 1f, 0f);
                cam.cullingMask = ~(1 << 5);
                go.transform.position = centre + Vector3.up * 300f;
                go.transform.rotation = Quaternion.LookRotation(Vector3.down, up);
                var rt = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
                rt.Create();
                cam.targetTexture = rt;
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else cam.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(_out, "minimap-world-ortho.png"), tex.EncodeToPNG());
                File.WriteAllText(Path.Combine(_out, "minimap-world-ortho.json"),
                    "{\"tier\": " + Tier + ", \"size\": [" + pw + ", " + ph + "], \"yawDeg\": " + cfg.Render.CameraYawDeg +
                    ", \"u0\": " + p.U0.ToString("R") + ", \"v0\": " + p.V0.ToString("R") + ", \"pxPerUnit1x\": " + MinimapProjection.PxPerUnit + "}\n");
                UnityEngine.Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(go);
                _shots++;
                Note($"wrote minimap-world-ortho.png {pw}x{ph}");
            }
            catch (Exception e) { Note("world ortho FAILED: " + e); }
        }

        static void Busy(SimDriverBehaviour drv)
        {
            World w = Wd;
            KillFeed kf = UnityEngine.Object.FindAnyObjectByType<KillFeed>();
            if (kf != null)
            {
                int pushed = 0;
                for (int i = 1; i < w.ActorCount && pushed < 5; i++)
                {
                    int killer = pushed == 4 ? 0 : (i + 3) % w.ActorCount;
                    var e = new CombatEvent
                    {
                        Kind = CombatEventKind.Knockout, Actor = i, Other = killer, Blow = 1,
                        ActorTeam = w.Actors[i].Team, OtherTeam = w.Actors[killer].Team,
                        ActorClass = w.Actors[i].Cls, OtherClass = w.Actors[killer].Cls, Time = w.Match.Time,
                    };
                    kf.Push(e, 0, w.Kaiju.State);
                    pushed++;
                }
            }
            drv.HumanAutopilot = false;
        }

        static void Update()
        {
            try
            {
                if (++_frames > MaxFrames) throw new TimeoutException($"after {MaxFrames} frames");
                if (!EditorApplication.isPlaying) return;
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                if (cam != null && _rt != null && cam.targetTexture != _rt) { cam.targetTexture = _rt; cam.aspect = (float)W / H; }
                if (_wait != null)
                {
                    if (!_wait()) return;
                    _wait = null;
                }
                if (_walk.MoveNext()) _wait = _walk.Current;
                else Finish(_shots == _wanted ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError("[HudArt] " + e);
                Note("FAILED: " + e.Message);
                Finish(1);
            }
        }

        static void Shot(string name)
        {
            _wanted++;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null || _rt == null) { Note(name + ": no main camera"); return; }
            string path = Path.Combine(_out, name + "@" + W + "x" + H + ".png");
            RenderTexture prev = RenderTexture.active;
            cam.targetTexture = _rt;
            cam.aspect = (float)W / H;
            try
            {
                GameHud hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
                if (hud != null) hud.UpdateScale();
                Canvas.ForceUpdateCanvases();
                var req = new RenderPipeline.StandardRequest { destination = _rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else cam.Render();
                RenderTexture.active = _rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                _shots++;
                Note("wrote " + name);
            }
            catch (Exception e) { Note(name + " FAILED: " + e.Message); }
            finally { RenderTexture.active = prev; }
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Update;
            GameFlow.ForceEnabled = false;
            HudSafeArea.ClearOverride();
            if (UnityEngine.Camera.main != null) { UnityEngine.Camera.main.targetTexture = null; UnityEngine.Camera.main.ResetAspect(); }
            if (_rt != null) { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); _rt = null; }
            Note($"exit {code}, {_shots} of {_wanted} shot(s)");
            try { File.WriteAllLines(Path.Combine(_out, "walk.log"), Log); }
            catch (Exception) { }
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

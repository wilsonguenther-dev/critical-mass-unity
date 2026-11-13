using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// The integrator's one-run walk through the whole game (batch integration): story card 1, title, main menu, the
    /// first-time card with the mascot, match setup, the match HUD, a fight, a jump in the air, the scoreboard, the pause
    /// menu and its settings page, a Kaiju banner and the results, as 1920x1080 PNGs from the gameplay camera, plus
    /// walk.log (one line per step). One Unity run for every shot (the machine rule: few, long Unity runs). Usage: Unity
    /// -batchmode -projectPath P -executeMethod CriticalMass.Editor.Flow.IntegrationCaptureTool.Run -walkOut DIR (needs a
    /// GPU: no -nographics). Exit 0 when every shot was written, 1 otherwise. Same render path as FlowCaptureTool.
    /// </summary>
    public static class IntegrationCaptureTool
    {
        const int Width = 1920, Height = 1080, MaxFrames = 40000;
        static string _out;
        static int _frames, _shots, _wanted;
        static IEnumerator<Func<bool>> _walk;
        static Func<bool> _wait;
        static readonly List<string> Log = new List<string>();

        public static void Run()
        {
            try
            {
                _out = Arg("-walkOut");
                if (string.IsNullOrEmpty(_out)) throw new ArgumentException("-walkOut DIR is required");
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
                Debug.LogError("[IntegrationCapture] " + e);
                EditorApplication.Exit(1);
            }
        }

        static SimDriverBehaviour Driver => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
        static World W => Driver != null ? Driver.World : null;
        static FlowDirector D => FlowDirector.Current;

        static Func<bool> Seconds(double s)
        {
            double until = EditorApplication.timeSinceStartup + s;
            return () => EditorApplication.timeSinceStartup >= until;
        }

        static void Note(string s)
        {
            Log.Add($"{EditorApplication.timeSinceStartup:0.0}s {s}");
            Debug.Log("[IntegrationCapture] " + s);
        }

        static IEnumerator<Func<bool>> Walk()
        {
            yield return () => Driver != null && W != null && D != null && !D.Loading &&
                               UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null;
            Note($"boot: state {GameFlow.State}, seed {W.Opts.Seed}, backdrop {Driver.Request != null && Driver.Request.Backdrop}");
            yield return Seconds(0.5);

            // Story card 1 (the intro plays itself only on a player's first launch; here it is asked for).
            Note("story: Play() " + StoryIntro.Play());
            yield return Seconds(1.6);
            Shot("01-story-card-1");
            StoryIntro.Skip();
            yield return Seconds(0.8);
            Shot("02-title");

            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return Seconds(0.5);
            Note($"title dismissed: state {GameFlow.State}");
            Shot("03-main-menu");

            // The first-time card (TrialBoot shows it in place of the main menu on a first launch; here asked for).
            TrialOffer offer = Trial.ShowOffer();
            if (offer != null) D.MainMenu.Hide();
            yield return Seconds(0.6);
            Shot("04-first-time-card");
            if (TrialOffer.Current != null) UnityEngine.Object.Destroy(TrialOffer.Current);
            if (MascotGuide.Instance != null) MascotGuide.Instance.Hide();
            yield return Seconds(0.2);

            D.ShowSetup();
            D.Setup.SetTeam(TeamId.Azure);
            D.Setup.SetClass(ClassId.Warrior);
            D.Setup.SetSize(16);
            yield return Seconds(0.5);
            Shot("05-match-setup");

            SimDriverBehaviour from = Driver;
            D.Setup.StartMatch();
            yield return () => Driver != null && Driver != from && W != null && GameFlow.State == FlowState.Match && !D.Loading;
            SimDriverBehaviour drv = Driver;
            Note($"match: seed {W.Opts.Seed}, actors {W.ActorCount}, pace {drv.TimeScale}");
            drv.HumanAutopilot = true;
            drv.StepTicks((int)(20 * drv.Config.SimHz));
            yield return Seconds(0.6);
            Shot("06-match-hud");

            // A fight: let the autopiloted player walk into one (sim at 4x), then shoot at 1x a moment after.
            drv.TimeScale = 4;
            double fightUntil = EditorApplication.timeSinceStartup + 60;
            yield return () => EnemiesNear(W, 6.0) >= 2 || EditorApplication.timeSinceStartup > fightUntil;
            Note($"fight: enemies within 6 u {EnemiesNear(W, 6.0)} at sim {W.Match.Time:0.0}s");
            drv.TimeScale = 1;
            yield return Seconds(0.5);
            Shot("07-fight");

            // A jump: the player's own Space, frozen near the top of the arc.
            drv.HumanAutopilot = false;
            drv.TimeScale = 1;
            HumanInputSource input = drv.GetComponent<HumanInputSource>();
            yield return () => W.Actors[0].Alive;
            input.State.ButtonDown(HumanInputState.HumanButton.Jump);
            double jumpUntil = EditorApplication.timeSinceStartup + 5;
            yield return () => (W.Actors[0].AirLen > 0 && W.Actors[0].AirTick * 2 >= W.Actors[0].AirLen) ||
                               EditorApplication.timeSinceStartup > jumpUntil;
            double saved = drv.TimeScale;
            drv.TimeScale = 0;
            Note($"jump: AirTick {W.Actors[0].AirTick} of AirLen {W.Actors[0].AirLen}");
            yield return Seconds(0.15);
            Shot("08-jump-mid-air");
            drv.TimeScale = saved;
            drv.HumanAutopilot = true;
            yield return Seconds(0.3);

            Scoreboard.ForceOpen = true;
            yield return Seconds(0.5);
            Shot("09-scoreboard");
            Scoreboard.ForceOpen = false;

            PauseMenu pm = drv.GetComponent<PauseMenu>();
            if (pm != null)
            {
                pm.Pause();
                yield return Seconds(0.5);
                Shot("10-pause-menu");
                pm.ShowSettings(PauseMenu.Tab.Video);
                yield return Seconds(0.5);
                Shot("11-settings");
                pm.Resume();
                yield return Seconds(0.3);
            }
            else Note("pause: no PauseMenu on the game");

            // A Kaiju banner: hatch now.
            W.ForceHatch = true;
            yield return () => W.Kaiju.State != KaijuState.Egg;
            Note($"kaiju: hatched {W.Kaiju.State} at sim {W.Match.Time:0.0}s");
            yield return Seconds(0.7);
            Shot("12-kaiju-banner");

            // Facing (owner's field report 2026-11-12: every model walked backwards). The same moves twice: "before" with
            // every ModelSpec.ArtYawDeg set to 0 (the old mapping), "after" with the measured values. Sim directions:
            // screen down (toward the camera) is sim (1, 1)/sqrt2, facing 45 deg; screen right is sim (1, -1)/sqrt2.
            foreach (bool corrected in new[] { false, true })
            {
                string tag = corrected ? "after" : "before";
                SetArtYaw(corrected);
                drv.HumanAutopilot = false;
                drv.TimeScale = 1;
                input.State.ButtonUp(HumanInputState.HumanButton.Attack);
                yield return () => W.Actors[0].Alive;
                input.State.ClearAim();
                input.State.SetMove(0, -1);
                yield return Seconds(2.0);
                drv.TimeScale = 0;
                Note($"facing {tag}: actor 0 walking toward the camera, sim facing {W.Actors[0].Facing * 180 / Math.PI:0} deg (toward camera = 45)");
                yield return Seconds(0.15);
                Shot("14-walk-toward-camera-" + tag);
                drv.TimeScale = 1;
                input.State.SetMove(1, 0);
                yield return Seconds(1.0);
                drv.TimeScale = 0;
                Note($"facing {tag}: actor 0 walking screen-right, sim facing {W.Actors[0].Facing * 180 / Math.PI:0} deg (screen right = -45)");
                yield return Seconds(0.15);
                Shot("15-walk-right-" + tag);
                drv.TimeScale = 1;
                input.State.SetMove(0, 0);
                // Aim at a point 2.5 u screen-left of the player and swing: the cleaver arc and the shield must point there.
                double ax = W.X[0] - 2.5 / Math.Sqrt(2), az = W.Z[0] + 2.5 / Math.Sqrt(2);
                input.State.SetAim(ax, az);
                yield return Seconds(0.3);
                input.State.ButtonDown(HumanInputState.HumanButton.Attack);
                yield return Seconds(0.12);
                drv.TimeScale = 0;
                Note($"facing {tag}: aim screen-left (sim facing wanted 135 deg), actor 0 facing {W.Actors[0].Facing * 180 / Math.PI:0} deg, attack cd {W.Actors[0].AttackCd:0.00}");
                yield return Seconds(0.15);
                Shot("16-aim-left-attack-" + tag);
                input.State.ButtonUp(HumanInputState.HumanButton.Attack);
                input.State.ClearAim();

                // The Kaiju turned toward the camera, the player stood beside it so the camera frames it.
                Kaiju k = W.Kaiju;
                double kdx = W.X[0] - k.X, kdz = W.Z[0] - k.Z;
                double kspeed = Math.Sqrt((k.X - k.PrevX) * (k.X - k.PrevX) + (k.Z - k.PrevZ) * (k.Z - k.PrevZ)) * drv.Config.SimHz;
                Note($"facing {tag}: kaiju {k.State} facing {k.Facing * 180 / Math.PI:0} deg, speed {kspeed:0.00} u/s, heading {(kspeed > 0.05 ? (Math.Atan2(k.Z - k.PrevZ, k.X - k.PrevX) * 180 / Math.PI).ToString("0") : "-")} deg");
                W.X[0] = W.PrevX[0] = k.X + 3.5; W.Z[0] = W.PrevZ[0] = k.Z + 3.5;
                k.Facing = Math.PI / 4;
                yield return Seconds(0.3);
                Note($"facing {tag}: kaiju turned to 45 deg (toward the camera); player moved beside it (was {Math.Sqrt(kdx * kdx + kdz * kdz):0.0} u away)");
                Shot("17-kaiju-toward-camera-" + tag);
                drv.TimeScale = 1;
            }
            SetArtYaw(true);
            drv.HumanAutopilot = true;

            // Results: run the clock out.
            W.Match.Time = drv.Config.MatchSeconds - 2.0 / drv.Config.SimHz;
            drv.StepTicks(4);
            yield return () => GameFlow.State == FlowState.Results && D.Results.IsVisible;
            yield return Seconds(0.6);
            Note($"results: {D.Results.TitleText} / {D.Results.ReasonText}");
            Shot("13-results");

            // Play again must give a fresh match on a new seed.
            int oldSeed = W.Opts.Seed;
            SimDriverBehaviour before = Driver;
            D.PlayAgain();
            yield return () => Driver != null && Driver != before && W != null && GameFlow.State == FlowState.Match && !D.Loading;
            Note($"play again: state {GameFlow.State}, seed {W.Opts.Seed} (was {oldSeed}), tick {W.Tick}, phase {W.Match.Phase}");
        }

        static readonly Dictionary<string, float> MeasuredYaw = new Dictionary<string, float>();

        /// <summary>false: every model's ArtYawDeg is 0 (the mapping before the 2026-11-12 fix); true: the measured values.</summary>
        static void SetArtYaw(bool corrected)
        {
            foreach (CriticalMass.Runtime.View.Art.ModelSpec m in CriticalMass.Runtime.View.Art.ArtMapping.Models)
            {
                if (!MeasuredYaw.ContainsKey(m.Key)) MeasuredYaw[m.Key] = m.ArtYawDeg;
                m.ArtYawDeg = corrected ? MeasuredYaw[m.Key] : 0f;
            }
        }

        static int EnemiesNear(World w, double r)
        {
            if (w == null || !w.Actors[0].Alive) return 0;
            int n = 0;
            TeamId me = w.Actors[0].Team;
            for (int i = 1; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive || w.Actors[i].Team == me) continue;
                double dx = w.X[i] - w.X[0], dz = w.Z[i] - w.Z[0];
                if (dx * dx + dz * dz <= r * r) n++;
            }
            return n;
        }

        static void Update()
        {
            try
            {
                if (++_frames > MaxFrames) throw new TimeoutException($"after {MaxFrames} frames");
                if (!EditorApplication.isPlaying) return;
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
                Debug.LogError("[IntegrationCapture] " + e);
                Note("FAILED: " + e.Message);
                Finish(1);
            }
        }

        static void Shot(string name)
        {
            _wanted++;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                Note(name + ": no main camera");
                return;
            }
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
                Note("wrote " + name);
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
            GameFlow.ForceEnabled = false;
            Scoreboard.ForceOpen = false;
            Note($"exit {code}, {_shots} of {_wanted} shot(s)");
            try
            {
                File.WriteAllLines(Path.Combine(_out, "walk.log"), Log);
            }
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

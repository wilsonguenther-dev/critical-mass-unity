using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// Integration batch 2's one-run walk: main menu, Barracks, match setup, class select, the loading screen, the match
    /// HUD, full map, ping wheel, character panel, a dig and a placed block, a toppled pillar, the effects gallery, kill
    /// feed, scoreboard, spectator, pause menu, Codex, the Controls and Access settings tabs, and the results with their
    /// rewards, highlights and achievements drawers, as 1920x1080 PNGs plus walk.log. One Unity run for every shot.
    /// Usage: Unity -batchmode -projectPath P -executeMethod CriticalMass.Editor.Flow.Batch2CaptureTool.Run -walkOut DIR
    /// (needs a GPU: no -nographics). Exit 0 when every shot was written, 1 otherwise.
    /// </summary>
    public static class Batch2CaptureTool
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
                Debug.LogError("[Batch2Capture] " + e);
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
            Debug.Log("[Batch2Capture] " + s);
        }

        static IEnumerator<Func<bool>> Walk()
        {
            yield return () => Driver != null && W != null && D != null && !D.Loading &&
                               UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null;
            Note($"boot: state {GameFlow.State}, seed {W.Opts.Seed}");
            StoryIntro.Skip();
            yield return Seconds(0.6);
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return Seconds(0.6);
            Shot("01-main-menu");

            // Barracks from the main menu (the menu entry's own path).
            Barracks.Show(ClassId.Miner);
            yield return Seconds(0.8);
            Note($"barracks: open {Barracks.IsOpen}");
            Shot("02-barracks");
            CharacterPanel.Hide();
            yield return Seconds(0.3);

            D.ShowSetup();
            D.Setup.SetTeam(TeamId.Azure);
            D.Setup.SetClass(ClassId.Miner);
            D.Setup.SetSize(16);
            yield return Seconds(0.5);
            Shot("03-match-setup");
            D.Setup.OpenClassDetail(ClassId.Miner);
            yield return Seconds(0.6);
            Note($"class select: open {ClassSelect.IsOpen}");
            Shot("04-class-select");
            ClassSelect.Hide();
            D.ShowSetup();
            D.Setup.SetClass(ClassId.Miner);
            yield return Seconds(0.3);

            // Loading screen: on for this run (it is off in batch mode by default).
            LoadingScreen.UseInFlow = true;
            SimDriverBehaviour from = Driver;
            D.Setup.StartMatch();
            double loadUntil = EditorApplication.timeSinceStartup + 20;
            bool sawLoading = false;
            yield return () => (sawLoading = LoadingScreen.IsOpen && UnityEngine.Camera.main != null) ||
                               (Driver != null && Driver != from && GameFlow.State == FlowState.Match && !D.Loading && !LoadingScreen.IsOpen) ||
                               EditorApplication.timeSinceStartup > loadUntil;
            Note($"loading: seen in the flow {sawLoading}");
            if (sawLoading)
            {
                yield return Seconds(0.25);
                if (LoadingScreen.IsOpen && UnityEngine.Camera.main != null) Shot("05-loading");
                else sawLoading = false;
            }
            double startUntil = EditorApplication.timeSinceStartup + 60;
            yield return () => (Driver != null && Driver != from && W != null && GameFlow.State == FlowState.Match && !D.Loading &&
                                !LoadingScreen.IsOpen) || EditorApplication.timeSinceStartup > startUntil;
            if (LoadingScreen.IsOpen) Note("loading: STILL OPEN 60 s after the match was asked for");
            if (!sawLoading)
            {
                // Fallback: show it by hand over the running match so the screen itself is still judged.
                LoadingScreen.Show(W.Opts.Seed);
                LoadingScreen.Report(LoadStep.World, 1f);
                LoadingScreen.Report(LoadStep.Dressing, 0.5f);
                yield return Seconds(0.5);
                Shot("05-loading");
                LoadingScreen.Finish();
                double hideUntil = EditorApplication.timeSinceStartup + 10;
                yield return () => !LoadingScreen.IsOpen || EditorApplication.timeSinceStartup > hideUntil;
                Note($"loading (by hand): closed {!LoadingScreen.IsOpen}");
            }

            SimDriverBehaviour drv = Driver;
            Note($"match: seed {W.Opts.Seed}, actors {W.ActorCount}, pace {drv.TimeScale}, class {W.Actors[0].Cls}, selected {ClassSelect.SelectedClass}");
            drv.HumanAutopilot = true;
            drv.TimeScale = 1;
            drv.StepTicks((int)(12 * drv.Config.SimHz));
            yield return Seconds(0.8);
            Shot("06-match-hud");

            HudInteract hi = HudInteract.Instance;
            Note($"hud interact: {(hi != null ? "installed" : "MISSING")}");
            FullMap.Show();
            yield return Seconds(0.6);
            Note($"full map: open {FullMap.IsOpen}");
            Shot("07-full-map");
            FullMap.Hide();
            yield return Seconds(0.2);
            if (hi != null)
            {
                Note("ping placed: " + Pings.Place(W, PingKind.Attack, W.X[0] + 3, W.Z[0], 0));
                // 2026-11-12: the wheel closes on the next frame when the ping key is not held, so the capture holds it
                // open through HudInteract's debug hook (capture-only; the hook is off in play).
                HudInteract.DebugHoldWheel = true;
                Note("ping wheel: debug open " + hi.DebugOpenWheel());
                yield return Seconds(0.5);
                Note($"ping wheel: open {hi.Wheel.IsOpen}");
                Shot("08-ping-wheel");
                HudInteract.DebugHoldWheel = false;
                hi.Wheel.Cancel();
            }
            yield return Seconds(0.2);

            Note("portrait click hook: " + ItemHooks.OpenCharacterPanel());
            yield return Seconds(0.7);
            Note($"character panel: open {CharacterPanel.IsOpen}");
            Shot("09-character-panel");
            CharacterPanel.Hide();
            yield return Seconds(0.3);

            // Dig and place (the player is the Miner): one cell dug, then one placed, next to the player.
            drv.HumanAutopilot = false;
            double aliveUntil = EditorApplication.timeSinceStartup + 30;
            yield return () => W.Actors[0].Alive || EditorApplication.timeSinceStartup > aliveUntil;
            int dug = 0, placed = 0;
            // 2026-11-12: batch 2's attempt was refused (-3) because the player stood on its pad, inside the dig keep-out.
            // Find a cell the rules let us dig with a void cell to fill next to it, stand the player between them, dig,
            // wait out the team rate cap, place, and frame the spot.
            if (FindDigSpot(W, out int sx, out int sz, out int dcx, out int dcz, out int pcx, out int pcz))
            {
                W.X[0] = sx + 0.5;
                W.Z[0] = sz + 0.5;
                drv.StepTicks(Math.Max(0, Terraform.WaitTicks(W, W.Actors[0].Team)) + 1);
                dug = Terraform.Dig(W, 0, dcx + 0.5, dcz + 0.5);
                drv.StepTicks(Math.Max(0, Terraform.WaitTicks(W, W.Actors[0].Team)) + 2);
                placed = Terraform.Place(W, 0, pcx + 0.5, pcz + 0.5);
                drv.StepTicks(3);
                CameraFollowTarget digCam = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
                if (digCam != null) digCam.SetOverride(sx + 0.5, sz + 0.5);
                Note($"terraform spot: stand {sx},{sz}, dig {dcx},{dcz}, place {pcx},{pcz}");
            }
            else Note("terraform spot: none found (dig and place skipped)");
            Note($"terraform: dig {dug} (1 = dug), place {placed} (2 = placed), class {W.Actors[0].Cls}");
            yield return Seconds(0.7);
            Shot("10-dig-and-place");
            { CameraFollowTarget c = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>(); if (c != null) c.ClearOverride(); }

            // A toppled pillar, framed by the capture camera override.
            CameraFollowTarget follow = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            DecorationDef[] decs = W.Map.Decorations;
            int fell = 0, which = -1;
            for (int k = 0; decs != null && k < decs.Length && fell <= 0; k++)
            {
                if (W.Topple.Fallen[k] != Topple.Standing) continue;
                drv.StepTicks(Math.Max(0, Terraform.WaitTicks(W, W.Actors[0].Team)) + 1);
                fell = Topple.ToppleDecoration(W, k, Topple.Ramp, -1);
                if (fell <= 0) fell = Topple.ToppleDecoration(W, k, Topple.Block, -1);
                if (fell > 0) which = k;
            }
            Note($"topple: decoration {which} of {(decs != null ? decs.Length : 0)}, result {fell} (1 ramp, 2 block)" +
                 (which >= 0 ? $", kind {decs[which].Kind} at {decs[which].Cx},{decs[which].Cz}" : ""));
            if (which >= 0 && follow != null) follow.SetOverride(decs[which].Cx + 0.5, decs[which].Cz + 0.5);
            yield return Seconds(1.6);
            Shot("11-toppled-pillar");
            if (follow != null) follow.ClearOverride();
            yield return Seconds(0.4);

            // The effects gallery (the F6 key's entry): steps are 1.1 sim seconds apart, in FxGallery.Step order.
            double g0 = W.Match.Time;
            bool gallery = CriticalMass.Runtime.View.Effects.Effects.PlayGallery();
            Note($"gallery: started {gallery}, shake setting {GameSettings.CameraShake}");
            yield return SimAt(g0 + 1.1 * 1 + 0.2);
            Shot("12-fx-heavy-hit");
            drv.TimeScale = 4;
            yield return SimAt(g0 + 1.1 * 15 - 0.5);
            drv.TimeScale = 1;
            yield return SimAt(g0 + 1.1 * 15 + 0.3);
            Shot("13-fx-explosion");
            yield return SimAt(g0 + 1.1 * 13 + 1.1 * 6 + 0.35);
            Shot("14-fx-kaiju-slam");

            // Kill feed and scoreboard: bots fight at 4x until someone is knocked out.
            drv.HumanAutopilot = true;
            drv.TimeScale = 4;
            double koUntil = EditorApplication.timeSinceStartup + 60;
            yield return () => Dead(W) >= 1 || EditorApplication.timeSinceStartup > koUntil;
            drv.TimeScale = 1;
            Note($"kill feed: {Dead(W)} down at sim {W.Match.Time:0.0}s");
            yield return Seconds(0.5);
            Shot("15-kill-feed");
            // 2026-11-12: batch 2 set ForceOpen and then called the score-click toggle, which flipped it straight back off.
            // Open it once through the hook (the player's path), and force it only if the hook is not in this build.
            Scoreboard.ForceOpen = false;
            bool hooked = ItemHooks.ToggleScoreboard();
            if (!hooked) Scoreboard.ForceOpen = true;
            Note("score click hook: " + hooked + ", forced " + Scoreboard.ForceOpen);
            yield return Seconds(0.6);
            Shot("16-scoreboard");
            if (hooked) ItemHooks.ReleaseScoreboard();
            Scoreboard.ForceOpen = false;
            yield return Seconds(0.2);

            Note("spectate: " + SpectatorMode.Enter());
            yield return Seconds(1.0);
            Note($"spectating {SpectatorMode.IsSpectating}");
            Shot("17-spectator");
            Note("rejoin: " + SpectatorMode.Rejoin());
            yield return Seconds(0.5);

            PauseMenu pm = drv.GetComponent<PauseMenu>();
            if (pm != null)
            {
                pm.Pause();
                yield return Seconds(0.5);
                Shot("18-pause-menu");
                int codex = PauseMenu.ExtraPages.FindIndex(p => p.Label == CodexPage.Label);
                pm.ShowExtra(codex);
                yield return Seconds(0.5);
                Note($"codex: page {codex}, count '{CodexPage.CountText}'");
                Shot("19-codex");
                pm.ShowSettings(PauseMenu.Tab.Controls);
                yield return Seconds(0.5);
                Shot("20-controls-tab");
                int access = PauseMenu.ExtraTabs.FindIndex(t => t.Name == SettingsExtras.TabName);
                pm.ShowSettings((PauseMenu.Tab)(4 + access));
                yield return Seconds(0.5);
                Note($"access tab: extra {access}, tabs {pm.TabCount}");
                Shot("21-access-tab");
                pm.Resume();
                yield return Seconds(0.3);
            }
            else Note("pause: no PauseMenu on the game");

            // Results: run the clock out. Rewards open by themselves; then the highlights drawer.
            W.Match.Time = drv.Config.MatchSeconds - 2.0 / drv.Config.SimHz;
            drv.StepTicks(4);
            double resUntil = EditorApplication.timeSinceStartup + 30;
            yield return () => (GameFlow.State == FlowState.Results && D.Results.IsVisible) || EditorApplication.timeSinceStartup > resUntil;
            yield return Seconds(1.2);
            Note($"results: state {GameFlow.State}, {D.Results.TitleText} / {D.Results.ReasonText}, rewards {(MatchRewardTracker.Last != null)}");
            Shot("22-results-summary");
            D.Results.ShowTab(ResultsTab.Rewards);
            yield return Seconds(1.2);
            Shot("23-results-rewards");
            D.Results.ShowTab(ResultsTab.Highlights);
            yield return Seconds(0.6);
            Note($"highlights: pictures {D.Results.TileHasPicture(0)}, {D.Results.TileHasPicture(1)}, {D.Results.TileHasPicture(2)}");
            Shot("24-results-highlights");
            D.Results.ShowTab(ResultsTab.Achievements);
            yield return Seconds(0.6);
            Shot("25-results-achievements");
        }

        static Func<bool> SimAt(double t)
        {
            double until = EditorApplication.timeSinceStartup + 40;
            return () => W == null || W.Match.Time >= t || EditorApplication.timeSinceStartup > until;
        }

        static int Dead(World w)
        {
            int n = 0;
            for (int i = 0; w != null && i < w.ActorCount; i++) if (!w.Actors[i].Alive) n++;
            return n;
        }

        /// <summary>
        /// A floor cell the dig rules accept (DigRefusal 0), a walkable stand cell next to it and a void cell next to the
        /// stand cell that a block may fill (PlaceRefusal 0), nearest the player first. Reads the world only.
        /// </summary>
        static bool FindDigSpot(World w, out int sx, out int sz, out int dcx, out int dcz, out int pcx, out int pcz)
        {
            sx = sz = dcx = dcz = pcx = pcz = -1;
            MapDef map = w.Map;
            int[] n4 = { 1, 0, -1, 0, 0, 1, 0, -1 };
            double best = double.MaxValue;
            for (int cz = 1; cz < map.Height - 1; cz++)
            for (int cx = 1; cx < map.Width - 1; cx++)
            {
                if (!MapGrid.IsWalkable(map, cx, cz)) continue;
                double d2 = (cx + 0.5 - w.X[0]) * (cx + 0.5 - w.X[0]) + (cz + 0.5 - w.Z[0]) * (cz + 0.5 - w.Z[0]);
                if (d2 >= best) continue;
                for (int a = 0; a < 4; a++)
                {
                    int ax = cx + n4[a * 2], az = cz + n4[a * 2 + 1];
                    if (Terraform.DigRefusal(w, ax, az) != 0) continue;
                    for (int b = 0; b < 4; b++)
                    {
                        int bx = cx + n4[b * 2], bz = cz + n4[b * 2 + 1];
                        if (b == a || Terraform.PlaceRefusal(w, bx, bz) != 0) continue;
                        best = d2;
                        sx = cx; sz = cz; dcx = ax; dcz = az; pcx = bx; pcz = bz;
                        goto nextCell;
                    }
                }
                nextCell: ;
            }
            return sx >= 0;
        }

        static void Update()
        {
            try
            {
                if (++_frames > MaxFrames) throw new TimeoutException($"after {MaxFrames} frames");
                if (!EditorApplication.isPlaying) return;
                // One 1920x1080 target for the whole walk, so every screen's own per-frame scaling sees a 1080p camera.
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                if (cam != null)
                {
                    if (_rt == null)
                    {
                        _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                        _rt.Create();
                    }
                    if (cam.targetTexture != _rt) { cam.targetTexture = _rt; cam.aspect = (float)Width / Height; }
                }
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
                Debug.LogError("[Batch2Capture] " + e);
                Note("FAILED: " + e.Message);
                Finish(1);
            }
        }

        static RenderTexture _rt;

        static void Shot(string name)
        {
            _wanted++;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null || _rt == null)
            {
                Note(name + ": no main camera");
                return;
            }
            string path = Path.Combine(_out, name + ".png");
            RenderTexture prev = RenderTexture.active;
            cam.targetTexture = _rt;
            cam.aspect = (float)Width / Height;
            try
            {
                foreach (FlowScreen s in UnityEngine.Object.FindObjectsByType<FlowScreen>(FindObjectsSortMode.None)) s.UpdateScale();
                // 2026-11-12: the loading screen (and every MenuCanvas screen) also re-attaches to the capture camera and
                // takes the 1080p scale; batch 2 drew the loading screen at a third of full size from the batch window.
                foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.isRootCanvas && c.enabled && c.transform.Find("Space") != null) MenuCanvas.UpdateScale(c);
                if (LoadingScreen.Instance != null && LoadingScreen.Instance.Canvas != null) MenuCanvas.UpdateScale(LoadingScreen.Instance.Canvas);
                GameHud hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
                if (hud != null) hud.UpdateScale();
                Canvas.ForceUpdateCanvases();
                Canvas.ForceUpdateCanvases();
                var req = new RenderPipeline.StandardRequest { destination = _rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else cam.Render();
                RenderTexture.active = _rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
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
            Scoreboard.ForceOpen = false;
            if (UnityEngine.Camera.main != null) { UnityEngine.Camera.main.targetTexture = null; UnityEngine.Camera.main.ResetAspect(); }
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

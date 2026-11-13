using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// Creator acceptance (2026-11-13) capture walk, windowless in the editor: Home with the CREATOR'S TOUR line, then the
    /// tour itself as a Miner with real virtual keyboard and gamepad presses: dig (hole, HUD carry), place at the edge, the
    /// pillar's fall arrows and prompt, the ramp and a fighter on it, the reset and the wall, a jump over a dug gap, a
    /// diagonal bridge crossing, corruption carried, delivered (instability, berserk locked) and the berserk hatch.
    /// Usage: Unity -batchmode -projectPath P -executeMethod CriticalMass.Editor.Flow.CreatorCaptureTool.Run -walkOut DIR
    /// (needs a GPU: no -nographics). Exit 0 when every shot was written.
    /// </summary>
    public static class CreatorCaptureTool
    {
        const int Width = 1920, Height = 1080, MaxFrames = 60000;
        static string _out;
        static int _frames, _shots, _wanted;
        static IEnumerator<Func<bool>> _walk;
        static Func<bool> _wait;
        static readonly List<string> Log = new List<string>();
        static RenderTexture _rt;
        static Keyboard _kb;
        static Gamepad _pad;

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
                Debug.LogError("[CreatorCapture] " + e);
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

        static Func<bool> Frames(int n)
        {
            int left = n;
            return () => --left <= 0;
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
            Debug.Log("[CreatorCapture] " + s);
        }

        static void Teleport(double x, double z)
        {
            World w = W;
            w.X[0] = w.PrevX[0] = x;
            w.Z[0] = w.PrevZ[0] = z;
            w.Vx[0] = w.Vz[0] = 0;
        }

        static Vector2 Stick(double dx, double dz)
        {
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-9) return Vector2.zero;
            dx /= len;
            dz /= len;
            return new Vector2((float)(dx * HumanInputState.ScreenRightX + dz * HumanInputState.ScreenRightZ),
                (float)(dx * HumanInputState.ScreenUpX + dz * HumanInputState.ScreenUpZ));
        }

        static void Pad(Vector2 left, Vector2 right, float rt = 0f, GamepadButton? b = null)
        {
            var st = new GamepadState { leftStick = left, rightStick = right, rightTrigger = rt };
            if (b.HasValue) st = st.WithButton(b.Value);
            InputSystem.QueueStateEvent(_pad, st);
        }

        static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_kb, new KeyboardState(keys));

        static byte CellAt(MapDef m, int cx, int cz) => cx < 0 || cz < 0 || cx >= m.Width || cz >= m.Height ? Cell.Void : m.Cells[cz * m.Width + cx];

        static bool OpenSpot(World w, double nx, double nz, out int sx, out int sz)
        {
            MapDef m = w.Map;
            sx = sz = -1;
            double best = double.MaxValue;
            for (int cz = 3; cz < m.Height - 3; cz++)
                for (int cx = 3; cx < m.Width - 3; cx++)
                {
                    bool ok = true;
                    for (int dz = -3; dz <= 3 && ok; dz++)
                        for (int dx = -3; dx <= 3 && ok; dx++)
                            if (CellAt(m, cx + dx, cz + dz) != Cell.Floor) ok = false;
                    if (!ok || Terraform.DigRefusal(w, cx + 1, cz) != 0) continue;
                    double d = (cx + 0.5 - nx) * (cx + 0.5 - nx) + (cz + 0.5 - nz) * (cz + 0.5 - nz);
                    if (d < best) { best = d; sx = cx; sz = cz; }
                }
            return sx >= 0;
        }

        static bool EdgeSpot(World w, double nx, double nz, out int fx, out int fz, out int vx, out int vz)
        {
            MapDef m = w.Map;
            fx = fz = vx = vz = -1;
            double best = double.MaxValue;
            int[] ddx = { 1, -1, 0, 0 }, ddz = { 0, 0, 1, -1 };
            for (int cz = 2; cz < m.Height - 2; cz++)
                for (int cx = 2; cx < m.Width - 2; cx++)
                {
                    if (CellAt(m, cx, cz) != Cell.Floor || !Collide.ActorFits(w, cx + 0.5, cz + 0.5, 0.45)) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int ax = cx + ddx[k], az = cz + ddz[k];
                        if (CellAt(m, ax, az) != Cell.Void || Terraform.PlaceRefusal(w, ax, az) != 0) continue;
                        if (CellAt(m, ax + ddx[k], az + ddz[k]) != Cell.Void) continue;
                        double d = (cx + 0.5 - nx) * (cx + 0.5 - nx) + (cz + 0.5 - nz) * (cz + 0.5 - nz);
                        if (d >= best) continue;
                        best = d; fx = cx; fz = cz; vx = ax; vz = az;
                    }
                }
            return fx >= 0;
        }

        static bool StandFor(World w, int k, int mode, out double x, out double z)
        {
            DecorationDef d = w.Map.Decorations[k];
            double bx = d.Cx + 0.5, bz = d.Cz + 0.5;
            CellDir dir = Topple.Dir(d, mode);
            for (double dist = 1.5; dist <= 2.0; dist += 0.25)
                for (int a = 0; a <= 60; a += 15)
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        double th = Math.Atan2(-dir.Dz, -dir.Dx) + sgn * a * Math.PI / 180;
                        x = bx + Math.Cos(th) * dist;
                        z = bz + Math.Sin(th) * dist;
                        if (Topple.ModeFor(d, x, z) == mode && Collide.ActorFits(w, x, z, 0.45)) return true;
                    }
            x = z = 0;
            return false;
        }

        static IEnumerable<Func<bool>> Swing(double bx, double bz, int k, int max)
        {
            for (int s = 0; s < max && W.Topple.Fallen[k] == Topple.Standing; s++)
            {
                Pad(Vector2.zero, Stick(bx - W.X[0], bz - W.Z[0]));
                yield return Frames(2);
                Pad(Vector2.zero, Vector2.zero, 1f);
                yield return Frames(2);
                yield return StepFrames(1);
                Pad(Vector2.zero, Vector2.zero, 0f);
                yield return Frames(2);
                yield return StepFrames(30);
            }
        }

        static IEnumerator<Func<bool>> Walk()
        {
            yield return () => Driver != null && W != null && D != null && !D.Loading && UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null;
            StoryIntro.Skip();
            yield return Seconds(1.0);
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return Seconds(0.6);
            D.ShowMenu();
            yield return Seconds(1.2);
            Note("home: tour line " + (GameObject.Find("Entry " + CreatorsTour.MenuId) != null));
            Shot("c01-home-creators-tour");

            // The tour, from Home.
            LoadingScreen.UseInFlow = false;
            SimDriverBehaviour from = Driver;
            CreatorsTour.StartFromMenu();
            double until = EditorApplication.timeSinceStartup + 90;
            yield return () => (Driver != null && Driver != from && W != null && GameFlow.State == FlowState.Match && Trial.IsRunning && !D.Loading &&
                                !LoadingScreen.IsOpen) || EditorApplication.timeSinceStartup > until;
            var ins = InputSystem.settings;
            ins.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            ins.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _kb = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
            SimDriverBehaviour drv = Driver;
            World w = W;
            Note($"tour: running {Trial.IsRunning}, class {w.Actors[0].Cls}, step {(Trial.Guide != null && Trial.Guide.Runner.ActiveStep != null ? Trial.Guide.Runner.ActiveStep.Id : "-")}, line {w.Config.KaijuFeed.BerserkThreshold}, hatch {w.Config.HatchSeconds}");
            yield return () => w.Match.Phase == MatchPhase.Playing && w.Actors[0].Alive;
            drv.TimeScale = 0;
            yield return StepFrames(30);
            Shot("c02-tour-step1-dig-prompt");

            // 1. Dig with F, carry, place at the edge, stand on it.
            PadDef pad = w.Map.Pads[(int)TeamId.Azure];
            if (OpenSpot(w, pad.X + 7, pad.Z, out int sx, out int sz))
            {
                Teleport(sx + 0.5, sz + 0.5);
                Pad(Vector2.zero, Stick(1, 0));
                yield return Frames(2);
                Pad(Vector2.zero, Vector2.zero);
                yield return Frames(2);
                Keys(Key.F);
                yield return Frames(2);
                yield return StepFrames(1);
                Keys();
                yield return StepFrames(8);
                Note($"dig: hole at ({sx + 1},{sz}) {CellAt(w.Map, sx + 1, sz)} carried {w.Terraform.Carried[0]}; chip '{TerrainPrompt.Line}'");
                Teleport(sx - 0.6, sz + 0.5);
                yield return StepFrames(6);
                Shot("c03-dig-hole-and-carry");
                ShotClose("c03b-dig-hole-close", sx + 1.5, sz + 0.5, 9f, 52f);
                int holeX = CellAt(w.Map, sx + 1, sz) == Cell.Void ? sx + 1 : sx + 2, holeZ = sz;   // the aim reach (1.6 u) may land one cell further
                if (EdgeSpot(w, w.X[0], w.Z[0], out int fx, out int fz, out int vx, out int vz))
                {
                    Teleport(fx + 0.5, fz + 0.5);
                    yield return StepFrames(45);
                    Pad(Vector2.zero, Vector2.zero, 0f, GamepadButton.RightShoulder);
                    yield return Frames(2);
                    yield return StepFrames(1);
                    Pad(Vector2.zero, Stick(vx - fx, vz - fz));
                    yield return Frames(2);
                    Pad(Vector2.zero, Vector2.zero);
                    yield return Frames(2);
                    Keys(Key.F);
                    yield return Frames(2);
                    yield return StepFrames(1);
                    Keys();
                    yield return StepFrames(4);
                    Note($"place: ({vx},{vz}) now {CellAt(w.Map, vx, vz)}, carried {w.Terraform.Carried[0]}");
                    Pad(Stick(vx - fx, vz - fz), Vector2.zero);
                    yield return Frames(2);
                    yield return StepFrames(12);
                    Pad(Vector2.zero, Vector2.zero);
                    yield return StepFrames(10);
                    Note($"stands on ({(int)Math.Floor(w.X[0])},{(int)Math.Floor(w.Z[0])}); step {(Trial.Guide != null && Trial.Guide.Runner.ActiveStep != null ? Trial.Guide.Runner.ActiveStep.Id : "-")}");
                    Shot("c04-placed-block-new-ground");
                    ShotClose("c04b-placed-block-close", w.X[0], w.Z[0], 9f, 52f);
                }
                // 3. Jump over the dug hole (Space).
                Teleport(holeX - 1.7, holeZ + 0.5);
                yield return StepFrames(2);
                Pad(Stick(1, 0), Vector2.zero);
                yield return Frames(2);
                yield return StepFrames(7);
                Keys(Key.Space);
                yield return Frames(2);
                yield return StepFrames(1);
                Keys();
                yield return StepFrames(7);
                Note($"jump: air {w.Actors[0].AirTick}/{w.Actors[0].AirLen} at x {w.X[0]:0.00} (hole x {holeX})");
                ShotClose("c09-jump-over-dug-gap", holeX + 0.5, holeZ + 0.5, 8f, 35f);
                yield return StepFrames(20);
                Pad(Vector2.zero, Vector2.zero);
                yield return Frames(2);
            }
            else Note("no open spot for the dig");

            // 2. The pillar: fall arrows and the prompt, then knock it into a ramp, walk up it.
            int k = TrialChecks.PillarFor(w, TeamId.Azure);
            DecorationDef d = w.Map.Decorations[k];
            double bx = d.Cx + 0.5, bz = d.Cz + 0.5;
            if (StandFor(w, k, Topple.Ramp, out double rx, out double rz))
            {
                Teleport(rx, rz);
                yield return StepFrames(30);
                Note($"pillar {k} of {w.Map.Decorations.Length}: chip '{TerrainPrompt.Line}', step {(Trial.Guide != null && Trial.Guide.Runner.ActiveStep != null ? Trial.Guide.Runner.ActiveStep.Id : "-")}");
                Shot("c05-pillar-fall-arrows-and-prompt");
                ShotClose("c05b-pillar-arrows-close", bx, bz, 10f, 55f);
                foreach (Func<bool> f in Swing(bx, bz, k, 12)) yield return f;
                yield return StepFrames(10);
                Shot("c06a-pillar-falling");
                yield return StepFrames(30);
                Note($"ramp: fallen {w.Topple.Fallen[k]}");
                Pad(Stick(d.Ramp.Dx, d.Ramp.Dz), Vector2.zero);
                yield return Frames(2);
                yield return StepFrames(26);
                Pad(Vector2.zero, Vector2.zero);
                yield return StepFrames(4);
                Note($"on the ramp at ({w.X[0]:0.0},{w.Z[0]:0.0}) cell {MapGrid.CellAt(w.Map, w.X[0], w.Z[0])}");
                Shot("c06-ramp-fighter-walks-up");
                ShotClose("c06b-ramp-close", bx + d.Ramp.Dx * 2.5, bz + d.Ramp.Dz * 2.5, 11f, 52f);
            }

            // 4. A diagonal bridge crossing with the stick 45 degrees off the axis (B2, next to the pillar).
            BridgeSpan sp = BridgeSpans.All[1];
            double ax = Math.Atan2(sp.Uz, sp.Ux) + Math.PI / 4;
            Teleport(sp.X0 + sp.Ux * 1.2, sp.Z0 + sp.Uz * 1.2);
            yield return StepFrames(4);
            Pad(Stick(Math.Cos(ax), Math.Sin(ax)), Vector2.zero);
            yield return Frames(2);
            yield return StepFrames(55);
            Note($"bridge B2 at 45 deg: at ({w.X[0]:0.0},{w.Z[0]:0.0}) cell {MapGrid.CellAt(w.Map, w.X[0], w.Z[0])}, along {BridgeSpans.Along(sp, w.X[0], w.Z[0]):0.0} of {sp.Len:0.0}");
            Shot("c10-bridge-crossed-at-45-degrees");
            Pad(Vector2.zero, Vector2.zero);
            yield return Frames(2);

            // Reset the world (the pillar stands again), then knock it the other way into a wall.
            World.ResetWorld(w, new WorldOptions { Seed = w.Opts.Seed, TeamSize = w.Opts.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Miner, Config = w.Config });
            yield return StepFrames(60);
            if (StandFor(w, k, Topple.Block, out double wx, out double wz))
            {
                Teleport(wx, wz);
                yield return StepFrames(8);
                Note($"wall side chip '{TerrainPrompt.Line}'");
                foreach (Func<bool> f in Swing(bx, bz, k, 15)) yield return f;
                yield return StepFrames(30);
                Pad(Stick(d.Block.Dx, d.Block.Dz), Vector2.zero);
                yield return Frames(2);
                yield return StepFrames(30);
                Pad(Vector2.zero, Vector2.zero);
                Note($"wall: fallen {w.Topple.Fallen[k]}, fighter at ({w.X[0]:0.0},{w.Z[0]:0.0})");
                Shot("c07-wall-blocks-the-path");
                ShotClose("c07b-wall-close", bx + d.Block.Dx * 2.5, bz + d.Block.Dz * 2.5, 11f, 52f);
            }

            // 5. Corruption: carry it, deliver it (tour line: one load locks berserk), the berserk hatch.
            int ni = TrialChecks.NearestCorruptionNode(w, w.Map.Shrine.X, w.Map.Shrine.Z);
            ResourceNode node = w.Nodes[ni];
            Teleport(node.X + 1.1, node.Z - 0.4);
            if (!Collide.ActorFits(w, w.X[0], w.Z[0], 0.45)) Teleport(node.X - 1.1, node.Z - 0.4);
            yield return StepFrames(2);
            Keys(Key.E);
            yield return Frames(2);
            for (int t = 0; t < 120 && w.Actors[0].Carry != CarryKind.Corruption; t++) yield return StepFrames(1);
            Keys();
            yield return StepFrames(6);
            Note($"corruption carried {w.Actors[0].Carry} x{w.Actors[0].CarryAmount}");
            Shot("c11-carrying-corruption");
            ShrineDef sh = w.Map.Shrine;
            Teleport(sh.X, sh.Z + sh.SolidR + 0.9);
            if (!Collide.ActorFits(w, w.X[0], w.Z[0], 0.45)) Teleport(sh.X + sh.SolidR + 0.9, sh.Z);
            yield return StepFrames(2);
            Keys(Key.E);
            yield return Frames(2);
            for (int t = 0; t < 150 && w.Actors[0].Carry == CarryKind.Corruption; t++) yield return StepFrames(1);
            Keys();
            yield return StepFrames(10);
            Note($"delivered: instability {w.Match.Instability:0.0}, locked {w.Match.BerserkLocked}, banner '{(Banner.IsOpen ? Banner.Current.Headline : "-")}'");
            Shot("c12-egg-instability-berserk-locked");
            yield return Seconds(4.5);
            w.Config.HatchSeconds = w.Match.Time + 1;
            for (int t = 0; t < 120 && w.Kaiju.State == KaijuState.Egg; t++) yield return StepFrames(1);
            yield return Seconds(1.2);
            Note($"hatch: kaiju {w.Kaiju.State}, moment playing {CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying}, banner '{(Banner.IsOpen ? Banner.Current.Headline + " / " + Banner.Current.Reason : "-")}'");
            Shot("c13-berserk-hatch-announced");
            double mUntil = EditorApplication.timeSinceStartup + 12;
            yield return () => !CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying || EditorApplication.timeSinceStartup > mUntil;
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();
            yield return StepFrames(30);
            Note($"after: kaiju {w.Kaiju.State} at ({w.Kaiju.X:0.0},{w.Kaiju.Z:0.0}); banner '{(Banner.IsOpen ? Banner.Current.Headline : "-")}'");
            CameraFollowTarget cf = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            if (cf != null) cf.SetOverride(w.Kaiju.X, w.Kaiju.Z);
            yield return Seconds(0.8);
            Shot("c14-berserk-kaiju");
            if (cf != null) cf.ClearOverride();
        }

        /// <summary>A close shot of sim point (x, z): the camera moved to dist units back at pitch, restored after.</summary>
        static void ShotClose(string name, double x, double z, float dist, float pitch)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) { Shot(name); return; }
            Vector3 pos = cam.transform.position;
            Quaternion rot = cam.transform.rotation;
            Vector3 target = CriticalMass.Runtime.SimSpace.ToWorld(x, CriticalMass.Runtime.GroundHeight.At(W.Map, x, z), z);
            cam.transform.rotation = Quaternion.Euler(pitch, rot.eulerAngles.y, 0f);
            cam.transform.position = target + Vector3.up * 0.5f - cam.transform.forward * dist;
            try { Shot(name); }
            finally { cam.transform.position = pos; cam.transform.rotation = rot; }
        }

        static void Update()
        {
            try
            {
                if (++_frames > MaxFrames) throw new TimeoutException($"after {MaxFrames} frames");
                if (!EditorApplication.isPlaying) return;
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
                Debug.LogError("[CreatorCapture] " + e);
                Note("FAILED: " + e.Message);
                Finish(1);
            }
        }

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
                GameHud hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
                if (hud != null) hud.UpdateScale();
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
            if (_kb != null) { InputSystem.RemoveDevice(_kb); _kb = null; }
            if (_pad != null) { InputSystem.RemoveDevice(_pad); _pad = null; }
            if (UnityEngine.Camera.main != null) { UnityEngine.Camera.main.targetTexture = null; UnityEngine.Camera.main.ResetAspect(); }
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

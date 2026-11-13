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
using CriticalMass.Runtime.View;
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
    /// Integration batch 3's one-run walk: title, match setup with the bot picker and six classes, the Healer's class
    /// screen, the Access tab, the animation pass (sidestep, backpedal, attack while running, jump take-off and landing),
    /// grass around a fighter, a Demolitions TNT blast (two frames) and its crater and marks, a Builder ladder and bridge, a
    /// Healer heal, the big-moments gallery, a death arc, carried cargo, water and the Kaiju claw telegraph.
    /// Usage: Unity -batchmode -projectPath P -executeMethod CriticalMass.Editor.Flow.Batch3CaptureTool.Run -walkOut DIR
    /// (needs a GPU: no -nographics). Exit 0 when every shot was written, 1 otherwise.
    /// </summary>
    public static class Batch3CaptureTool
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
                Debug.LogError("[Batch3Capture] " + e);
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
            Debug.Log("[Batch3Capture] " + s);
        }

        static HumanInputState Inp => Driver != null && Driver.GetComponent<HumanInputSource>() != null ? Driver.GetComponent<HumanInputSource>().State : null;

        /// <summary>Steps one sim tick per editor frame, n times (the views draw every tick).</summary>
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

        static void Teleport(int i, double x, double z)
        {
            World w = W;
            w.X[i] = w.PrevX[i] = x;
            w.Z[i] = w.PrevZ[i] = z;
        }

        static void Frame(double x, double z)
        {
            CameraFollowTarget f = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            if (f != null) f.SetOverride(x, z);
        }

        static void Unframe()
        {
            CameraFollowTarget f = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            if (f != null) f.ClearOverride();
        }

        static byte CellAt(MapDef m, int cx, int cz) => cx < 0 || cz < 0 || cx >= m.Width || cz >= m.Height ? Cell.Void : m.Cells[cz * m.Width + cx];

        /// <summary>A walkable cell with a cell of type t within 2 cells along an axis (for the Builder) nearest (x, z).</summary>
        static bool FindEdge(World w, byte t, double nearX, double nearZ, out double fx, out double fz, out double ax, out double az)
        {
            MapDef m = w.Map;
            fx = fz = ax = az = 0;
            double best = double.MaxValue;
            int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
            for (int cz = 2; cz < m.Height - 2; cz++)
            for (int cx = 2; cx < m.Width - 2; cx++)
            {
                if (!MapGrid.IsWalkableType(CellAt(m, cx, cz))) continue;
                for (int k = 0; k < 4; k++)
                {
                    if (CellAt(m, cx + dx[k], cz + dz[k]) != t) continue;
                    double d = (cx + 0.5 - nearX) * (cx + 0.5 - nearX) + (cz + 0.5 - nearZ) * (cz + 0.5 - nearZ);
                    if (d >= best) continue;
                    best = d;
                    fx = cx + 0.5; fz = cz + 0.5; ax = fx + dx[k] * 2; az = fz + dz[k] * 2;
                }
            }
            return best < double.MaxValue;
        }

        static string MomentNow()
        {
            try
            {
                var bm = CriticalMass.Runtime.View.Moments.BigMoments.Instance;
                if (bm == null) return "no BigMoments";
                const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object q = bm.GetType().GetField("_queue", f)?.GetValue(bm);
                if (q == null) return "no queue";
                object cur = q.GetType().GetProperty("Current")?.GetValue(q);
                object req = cur?.GetType().GetField("Request")?.GetValue(cur) ?? cur?.GetType().GetProperty("Request")?.GetValue(cur);
                object id = req?.GetType().GetField("Id")?.GetValue(req) ?? req?.GetType().GetProperty("Id")?.GetValue(req);
                return "playing " + CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying + ", " + (id ?? "?");
            }
            catch (Exception e) { return "moment? " + e.Message; }
        }

        static IEnumerator<Func<bool>> Walk()
        {
            yield return () => Driver != null && W != null && D != null && !D.Loading &&
                               UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null;
            Note($"boot: state {GameFlow.State}, seed {W.Opts.Seed}");
            StoryIntro.Skip();
            yield return Seconds(1.0);
            Shot("01-title");
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return Seconds(0.6);

            D.ShowSetup();
            D.Setup.SetTeam(TeamId.Azure);
            D.Setup.SetSize(16);
            D.Setup.SetClass(ClassId.Demolitions);
            D.Setup.SetBotDifficulty(BotDifficulty.Hard);
            yield return Seconds(0.5);
            Note($"setup: class {D.Setup.Choice.Class}, bots {D.Setup.Choice.BotDifficulty}, cards {MatchSetup.CardCount}");
            Shot("02-match-setup-bots");
            D.Setup.OpenClassDetail(ClassId.Healer);
            yield return Seconds(0.7);
            Note($"class select (Healer): open {ClassSelect.IsOpen}");
            Shot("03-class-select-healer");
            ClassSelect.Hide();
            D.ShowSetup();
            D.Setup.SetClass(ClassId.Warrior);
            D.Setup.SetBotDifficulty(BotDifficulty.Normal);
            yield return Seconds(0.3);
            LoadingScreen.UseInFlow = false;
            SimDriverBehaviour from = Driver;
            D.Setup.StartMatch();
            double startUntil = EditorApplication.timeSinceStartup + 60;
            yield return () => (Driver != null && Driver != from && W != null && GameFlow.State == FlowState.Match && !D.Loading &&
                                !LoadingScreen.IsOpen) || EditorApplication.timeSinceStartup > startUntil;
            SimDriverBehaviour drv = Driver;
            HumanInputSource src = drv.GetComponent<HumanInputSource>();
            Note($"match: seed {W.Opts.Seed}, actors {W.ActorCount}, class {W.Actors[0].Cls}, enemy bots {W.Opts.BotDifficulty}");
            drv.HumanAutopilot = false;
            drv.TimeScale = 0;
            if (src != null) { src.enabled = false; src.State.Reset(); }
            drv.StepTicks((int)(3 * drv.Config.SimHz));
            yield return Seconds(0.3);

            PauseMenu pm = drv.GetComponent<PauseMenu>();
            if (pm != null)
            {
                pm.Pause();
                int access = PauseMenu.ExtraTabs.FindIndex(t => t.Name == SettingsExtras.TabName);
                pm.ShowSettings((PauseMenu.Tab)(4 + access));
                yield return Seconds(0.6);
                Note($"access tab: extra {access}, shake {GameSettings.ShakeScale:0.00}, reduce motion {GameSettings.ReduceMotion}, big moments {CriticalMass.Runtime.View.Moments.BigMomentsSettings.Mode}");
                Shot("04-access-tab");
                pm.Resume();
                yield return Seconds(0.3);
            }

            // Animation pass, actor 0 the Warrior at its spawn, one tick per frame.
            HumanInputState inp = src != null ? src.State : null;
            double sx = W.X[0], sz = W.Z[0];
            SanityDir(0, 1, out double upX, out double upZ);
            if (inp != null)
            {
                inp.SetMove(1, 0);
                for (int k = 0; k < 24; k++) { inp.SetAim(W.X[0] + 4 * upX, W.Z[0] + 4 * upZ); yield return StepFrames(1); }
                Note($"sidestep: facing {W.Actors[0].Facing * 180 / Math.PI:0} deg, speed {Math.Sqrt(W.Vx[0] * W.Vx[0] + W.Vz[0] * W.Vz[0]):0.00}");
                ShotFramed("05-sidestep", ActorModel(0), 6f, 35f);
                Teleport(0, sx, sz);
                inp.SetMove(0, -1);
                for (int k = 0; k < 24; k++) { inp.SetAim(W.X[0] + 4 * upX, W.Z[0] + 4 * upZ); yield return StepFrames(1); }
                ShotFramed("06-backpedal", ActorModel(0), 6f, 35f);
                Teleport(0, sx, sz);
                inp.SetMove(0, 1);
                for (int k = 0; k < 20; k++) { inp.SetAim(W.X[0] + 4 * upX, W.Z[0] + 4 * upZ); yield return StepFrames(1); }
                inp.ButtonDown(HumanInputState.HumanButton.Attack);
                yield return StepFrames(6);
                inp.ButtonUp(HumanInputState.HumanButton.Attack);
                Note($"attack while running: attackCd {W.Actors[0].AttackCd:0.00}, anim {W.Actors[0].AnimAction:0.00}");
                ShotFramed("07-attack-running", ActorModel(0), 6f, 35f);
                inp.SetMove(0, 0);
                yield return StepFrames(10);
                inp.ButtonDown(HumanInputState.HumanButton.Jump);
                yield return StepFrames(2);
                inp.ButtonUp(HumanInputState.HumanButton.Jump);
                ShotFramed("08-jump-takeoff", ActorModel(0), 6f, 30f);
                int landed = -1;
                for (int k = 0; k < 40 && landed < 0; k++)
                {
                    float y0 = ActorModel(0) != null ? ActorModel(0).position.y : 0f;
                    yield return StepFrames(1);
                    if (k > 6 && ActorModel(0) != null && Mathf.Abs(ActorModel(0).position.y - y0) < 0.002f) landed = k;
                }
                yield return StepFrames(1);
                Note($"jump: landed after about {landed} ticks");
                ShotFramed("09-jump-landing", ActorModel(0), 6f, 30f);
                // A fighter walking through grass: gameplay framing, grass bends around the walker.
                inp.SetMove(1, 1);
                yield return StepFrames(30);
                Shot("10-grass-fighter");
                inp.SetMove(0, 0);
            }

            // Demolitions: TNT through the real input path (Dig / place at the aim point), two frames of the blast, then the crater.
            W.Actors[0].Cls = ClassId.Demolitions;
            Teleport(0, sx, sz);
            yield return StepFrames(2);
            double tx = W.X[0] + 4 * upX, tz = W.Z[0] + 4 * upZ;
            int blast = -1;
            if (inp != null)
            {
                inp.SetAim(tx, tz);
                inp.ButtonDown(HumanInputState.HumanButton.DigPlace);
                yield return StepFrames(2);
                inp.ButtonUp(HumanInputState.HumanButton.DigPlace);
                foreach (Bomb b in W.Demolitions.Bombs) if (b != null && b.Active) { blast = b.BlastTick; tx = b.X; tz = b.Z; }
            }
            Note($"tnt: thrown {blast >= 0}, blast tick {blast}, now {W.Tick}, at {tx:0.0},{tz:0.0}");
            Frame(tx, tz);
            if (blast > W.Tick + 3) drv.StepTicks(blast - W.Tick - 3);
            yield return StepFrames(5);
            Shot("11-tnt-blast-a");
            yield return StepFrames(5);
            Shot("12-tnt-blast-b");
            yield return StepFrames(90);
            Shot("13-crater-marks");

            // Builder: a ladder up a wall and a rope bridge across a gap, through the Ability input (bank topped up for the shot).
            W.Actors[0].Cls = ClassId.Builder;
            foreach (byte t in new[] { Cell.Wall, Cell.Void })
            {
                string name = t == Cell.Wall ? "14-builder-ladder" : "15-builder-bridge";
                W.Builder.Spent[(int)W.Actors[0].Team] = -6;
                if (!FindEdge(W, t, sx, sz, out double fx, out double fz, out double ax, out double az)) { Note(name + ": no spot"); continue; }
                Teleport(0, fx, fz);
                drv.StepTicks(Math.Max(0, Terraform.WaitTicks(W, W.Actors[0].Team)) + 20);
                Teleport(0, fx, fz);
                bool can = BuiltPieces.CanBuild(W, 0, ax, az);
                if (inp != null)
                {
                    inp.SetAim(ax, az);
                    inp.ButtonDown(HumanInputState.HumanButton.Ability);
                    yield return StepFrames(2);
                    inp.ButtonUp(HumanInputState.HumanButton.Ability);
                }
                int pieces = BuiltPieces.OwnedCount(W, 0);
                Note($"{name}: at {fx:0.0},{fz:0.0} aim {ax:0.0},{az:0.0}, can build {can}, owned pieces {pieces}");
                Frame(fx, fz);
                yield return StepFrames(20);
                Shot(name);
            }
            Unframe();

            // Healer: hurt the nearby allies, then Healing Wings on herself.
            W.Actors[0].Cls = ClassId.Healer;
            Teleport(0, sx, sz);
            int hurt = 0;
            for (int i = 1; i < W.ActorCount; i++)
            {
                if (W.Actors[i].Team != W.Actors[0].Team || !W.Actors[i].Alive) continue;
                if (hurt < 3) { Teleport(i, sx + 0.8 * (hurt - 1), sz + 0.9); W.Actors[i].Hp = Math.Max(1, W.Actors[i].Hp * 0.4); hurt++; }
            }
            W.Actors[0].AbilityCd = 0;
            if (inp != null)
            {
                inp.SetAim(sx, sz + 0.5);
                inp.ButtonDown(HumanInputState.HumanButton.Ability);
                yield return StepFrames(2);
                inp.ButtonUp(HumanInputState.HumanButton.Ability);
            }
            Note($"healer: {hurt} hurt allies placed, mends {W.Healer.Stats.Mends}, pulses {W.Healer.Stats.Pulses}");
            yield return StepFrames(6);
            ShotFramed("16-healer-heal", ActorModel(0), 8f, 45f);
            W.Actors[0].Cls = ClassId.Warrior;

            // Big moments gallery (F8): one frame mid-takeover per recipe for the first six.
            drv.TimeScale = 1;
            drv.HumanAutopilot = true;
            CriticalMass.Runtime.View.Moments.BigMoments.PlayGallery();
            for (int k = 0; k < 6; k++)
            {
                yield return Seconds(k == 0 ? 0.45 : 1.6);
                Note($"big moment {k + 1}: {MomentNow()}");
                Shot($"{17 + k}-big-moment-{k + 1}");
            }
            yield return () => !CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying;

            // A death arc mid-flight: bots fight at 2x until someone is knocked out near the hill.
            drv.TimeScale = 2;
            int dead0 = Dead(W);
            double koUntil = EditorApplication.timeSinceStartup + 60;
            yield return () => Dead(W) > dead0 || EditorApplication.timeSinceStartup > koUntil;
            drv.TimeScale = 0;
            yield return StepFrames(4);
            Note($"death arc: {Dead(W) - dead0} new down at sim {W.Match.Time:0.0}s");
            Shot("23-death-arc");

            // Carried cargo: frame a carrier.
            int carrier = -1;
            drv.TimeScale = 2;
            double cUntil = EditorApplication.timeSinceStartup + 40;
            yield return () =>
            {
                for (int i = 1; i < W.ActorCount; i++) if (W.Actors[i].Alive && W.Actors[i].Carry != CarryKind.None) { carrier = i; return true; }
                return EditorApplication.timeSinceStartup > cUntil;
            };
            drv.TimeScale = 0;
            Note($"cargo: carrier {carrier}");
            if (carrier >= 0) { Frame(W.X[carrier], W.Z[carrier]); yield return StepFrames(3); ShotFramed("24-carried-cargo", ActorModel(carrier), 5f, 35f); }
            else Shot("24-carried-cargo");

            // Water: the nearest water cell to the hill.
            if (FindEdge(W, Cell.Water, W.Map.Width * 0.5, W.Map.Height * 0.5, out double wx, out double wz, out _, out _))
            {
                Frame(wx, wz);
                drv.TimeScale = 1;
                yield return Seconds(1.2);
                Note($"water: at {wx:0.0},{wz:0.0}");
                Shot("25-water");
            }
            else Note("water: none on the map");
            Unframe();

            // Kaiju claw telegraph: hatch now, wait for a melee wind-up.
            W.ForceHatch = true;
            drv.TimeScale = 2;
            double hUntil = EditorApplication.timeSinceStartup + 30;
            yield return () => W.Kaiju.State != KaijuState.Egg || EditorApplication.timeSinceStartup > hUntil;
            double wUntil = EditorApplication.timeSinceStartup + 60;
            yield return () => W.Kaiju.MeleeWindupIn > 0 || EditorApplication.timeSinceStartup > wUntil;
            drv.TimeScale = 0;
            Note($"kaiju: state {W.Kaiju.State}, windup {W.Kaiju.MeleeWindupIn}");
            Frame(W.Kaiju.X, W.Kaiju.Z);
            yield return StepFrames(2);
            ShotFramed("26-kaiju-claw", KaijuModel(), 12f, 50f);
            Unframe();
        }

        static void SanityDir(double right, double up, out double dx, out double dz)
        {
            dx = right * HumanInputState.ScreenRightX + up * HumanInputState.ScreenUpX;
            dz = right * HumanInputState.ScreenRightZ + up * HumanInputState.ScreenUpZ;
        }

        static Transform ActorModel(int i)
        {
            string prefix = "Actor " + i + " ";
            foreach (Animator a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                if (a.gameObject.activeInHierarchy && a.name.StartsWith(prefix, StringComparison.Ordinal)) return a.transform;
            GameObject cap = GameObject.Find("Actor " + i);
            return cap != null ? cap.transform : null;
        }

        static Transform KaijuModel()
        {
            GameObject g = GameObject.Find("Kaiju Adult");
            if (g == null) g = GameObject.Find("Kaiju Hatchling");
            return g != null ? g.transform : null;
        }

        static void ShotFramed(string name, Transform target, float dist, float pitch)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null || target == null)
            {
                Note(name + ": no camera or no target, shot at the gameplay framing");
                Shot(name);
                return;
            }
            Vector3 pos = cam.transform.position;
            Quaternion rot = cam.transform.rotation;
            cam.transform.rotation = Quaternion.Euler(pitch, rot.eulerAngles.y, 0f);
            cam.transform.position = target.position + Vector3.up * 1.0f - cam.transform.forward * dist;
            try { Shot(name); }
            finally { cam.transform.position = pos; cam.transform.rotation = rot; }
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

        static void Drawer(string name)
        {
            try
            {
                const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                Type dt = typeof(ResultsScreen).GetNestedType("Drawer", System.Reflection.BindingFlags.NonPublic);
                typeof(ResultsScreen).GetMethod("ToggleDrawer", f).Invoke(D.Results, new[] { Enum.Parse(dt, name) });
                Note("drawer " + name + " toggled");
            }
            catch (Exception e) { Note("drawer " + name + " FAILED: " + (e.InnerException ?? e).Message); }
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
                Debug.LogError("[Batch3Capture] " + e);
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

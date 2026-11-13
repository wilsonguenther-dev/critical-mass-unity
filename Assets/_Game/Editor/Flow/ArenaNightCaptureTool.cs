using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.View.Atmosphere;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// Arena night life 1 capture walk (batch mode, with a GPU, never a window): the READY screen with the TIME button,
    /// then a 16 v 16 match on seed 4242 frozen at its start and again mid-match, each in Day, Dusk and Night, two frames
    /// of ambient life (a creature placed in the void by pinning the ambient clock) and one Night frame at Low quality.
    /// Every shot is written at 3024x1964 and 1920x1080 (name@WxH.png). Also writes walk.log and stats.json (ambient
    /// counts and budgets, world triangles). tools/unity-arena-night-capture.sh OUT_DIR runs it through cm-heavy.
    /// </summary>
    public static class ArenaNightCaptureTool
    {
        const int MaxFrames = 120000;
        static readonly Vector2Int[] Sizes = { new Vector2Int(3024, 1964), new Vector2Int(1920, 1080) };
        static string _out;
        static int _frames, _shots, _wanted;
        static IEnumerator<Func<bool>> _walk;
        static Func<bool> _wait;
        static readonly List<string> Log = new List<string>();
        static readonly Dictionary<string, object> Stats = new Dictionary<string, object>();
        static readonly RenderTexture[] _rts = new RenderTexture[2];
        static bool _hadPref;
        static int _pref;

        public static void Run()
        {
            try
            {
                _out = Arg("-walkOut");
                if (string.IsNullOrEmpty(_out)) throw new ArgumentException("-walkOut DIR is required");
                Directory.CreateDirectory(_out);
                _frames = _shots = _wanted = 0;
                Log.Clear();
                Stats.Clear();
                _hadPref = PlayerPrefs.HasKey(TimeOfDaySettings.Key);
                _pref = PlayerPrefs.GetInt(TimeOfDaySettings.Key, 0);
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
                Debug.LogError("[ArenaNightCapture] " + e);
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
            return () => --left < 0;
        }

        static void Note(string s)
        {
            Log.Add($"{EditorApplication.timeSinceStartup:0.0}s {s}");
            Debug.Log("[ArenaNightCapture] " + s);
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

        static IEnumerator<Func<bool>> SetTime(TimeOfDay t)
        {
            TimeOfDayDirector.Override = t;
            yield return Frames(1);
            if (TimeOfDayDirector.Current != null) TimeOfDayDirector.Current.SnapToTarget();
            yield return Frames(3);
        }

        static readonly TimeOfDay[] Times = { TimeOfDay.Day, TimeOfDay.Dusk, TimeOfDay.Night };

        static IEnumerator<Func<bool>> Walk()
        {
            yield return () => Driver != null && W != null && D != null && !D.Loading &&
                               UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null;
            Note($"boot: state {GameFlow.State}, seed {W.Opts.Seed}, quality level {RenderTier.Level}");
            StoryIntro.Skip();
            yield return Seconds(1.0);
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return Seconds(0.6);
            D.ShowMenu();
            yield return Seconds(1.0);

            // READY with the TIME button on Night: the backdrop world behind it changes at once.
            LoadingScreen.UseInFlow = false;
            SimDriverBehaviour from = Driver;
            TimeOfDaySettings.Choice = TimeOfDayChoice.Dusk;
            MatchSetupChoice full = PlayModes.ToChoice(MatchMode.FullBattle, MatchSetupChoice.Default, 16);
            full.Class = ClassId.Warrior;
            full.Team = TeamId.Azure;
            D.ShowReady(new PlayLaunch { Choice = full, Seed = 4242, BackTo = LobbyScreen.ModeSelect });
            yield return Seconds(1.0);
            D.Ready.CycleTime();   // Dusk -> Night
            yield return Seconds(1.2);
            Note($"ready: time choice {TimeOfDaySettings.Choice}, button '{(D.Ready.TimeItem != null && D.Ready.TimeItem.Label != null ? D.Ready.TimeItem.Label.text : "?")}', director target {(TimeOfDayDirector.Current != null ? TimeOfDayDirector.Current.Target.ToString() : "none")}");
            Shot("00-ready-time-night");
            double startUntil = EditorApplication.timeSinceStartup + 90;
            yield return () => (Driver != null && Driver != from && W != null && GameFlow.State == FlowState.Match && !D.Loading &&
                                !LoadingScreen.IsOpen) || EditorApplication.timeSinceStartup > startUntil;
            SimDriverBehaviour drv = Driver;
            Note($"match: seed {W.Opts.Seed}, actors {W.ActorCount}, timescale {drv.TimeScale}");
            {
                double rt = EditorApplication.timeSinceStartup + 40;
                yield return () => (W.Match.Phase == MatchPhase.Playing && W.Actors[0].Alive) || EditorApplication.timeSinceStartup > rt;
            }
            CriticalMass.Runtime.View.Moments.BigMomentsSettings.ModeOverride = CriticalMass.Runtime.View.Moments.BigMomentsMode.Off;
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();
            yield return Seconds(1.5);
            double ts = drv.TimeScale;
            drv.TimeScale = 0;
            foreach (TimeOfDay t in Times)
            {
                IEnumerator<Func<bool>> st = SetTime(t);
                while (st.MoveNext()) yield return st.Current;
                Shot("10-start-" + t.ToString().ToLowerInvariant());
            }
            RecordWorld("start");

            // Mid-match: autopilot, normal speed, about 70 sim seconds, then frozen at the busiest place (the hill).
            drv.HumanAutopilot = true;
            drv.TimeScale = ts > 0 ? ts : 2;
            TimeOfDayDirector.Override = TimeOfDay.Day;
            double until = EditorApplication.timeSinceStartup + 120;
            int tick0 = W.Tick;
            yield return () => W.Tick - tick0 >= (int)(70 * drv.Config.SimHz) || EditorApplication.timeSinceStartup > until;
            drv.TimeScale = 0;
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();
            double bx, bz;
            Busiest(W, out bx, out bz, out int near);
            Frame(bx, bz);
            Note($"mid-match: tick {W.Tick} ({(W.Tick - tick0) / drv.Config.SimHz:0} s on), {Alive(W)} alive, framed {bx:0.0},{bz:0.0} with {near} fighters within 9 u");
            yield return Seconds(1.0);
            foreach (TimeOfDay t in Times)
            {
                IEnumerator<Func<bool>> st = SetTime(t);
                while (st.MoveNext()) yield return st.Current;
                Shot("11-busy-" + t.ToString().ToLowerInvariant());
            }

            // Ambient life: pin the ambient clock where a skyray crosses open void near the islands, frame it.
            AmbientLife life = AmbientLife.Current;
            if (life != null && life.LastCreatures.Count > 0)
            {
                Stats["ambient_creatures"] = life.Creatures;
                Stats["ambient_sprites"] = life.Sprites;
                Stats["ambient_triangles"] = life.Triangles;
                Stats["ambient_draws"] = life.Draws;
                Stats["ambient_lanterns_found"] = life.LanternsFound;
                int shotN = 0;
                foreach (int kind in new[] { 0, 1 })
                {
                    if (!FindVoidMoment(W, life, kind, out float clock, out Vector3 at)) { Note($"ambient: no void moment for kind {kind}"); continue; }
                    AmbientLife.ClockOverride = clock;
                    double sx = at.x, sz = -at.z;
                    double back = -at.y / Math.Tan(55.0 * Math.PI / 180.0);
                    double fx = sx - HumanInputState.ScreenUpX * back, fz = sz - HumanInputState.ScreenUpZ * back;
                    Frame(fx, fz);
                    Note($"ambient {(kind == 0 ? "skyray" : "cloud-loafer")}: clock {clock:0.0}, at world {at.x:0.0},{at.y:0.0},{at.z:0.0}, framed {fx:0.0},{fz:0.0}");
                    yield return Seconds(0.8);
                    TimeOfDay t = shotN == 0 ? TimeOfDay.Day : TimeOfDay.Night;
                    IEnumerator<Func<bool>> st = SetTime(t);
                    while (st.MoveNext()) yield return st.Current;
                    Shot($"12-life-{(kind == 0 ? "skyray" : "loafer")}-{t.ToString().ToLowerInvariant()}");
                    // The same frame in the other time, for the comparison.
                    TimeOfDay o = t == TimeOfDay.Day ? TimeOfDay.Night : TimeOfDay.Day;
                    st = SetTime(o);
                    while (st.MoveNext()) yield return st.Current;
                    Shot($"12-life-{(kind == 0 ? "skyray" : "loafer")}-{o.ToString().ToLowerInvariant()}");
                    shotN++;
                }
                AmbientLife.ClockOverride = null;
            }
            else Note("ambient: no AmbientLife or no creatures");

            // Night at Low quality (the budgeted path: no creatures, no fireflies, half the lights).
            Frame(bx, bz);
            int lvl = RenderTier.Level;
            RenderTier.Set(FramePolicy.LevelLow);
            yield return Seconds(2.5);
            {
                IEnumerator<Func<bool>> st = SetTime(TimeOfDay.Night);
                while (st.MoveNext()) yield return st.Current;
            }
            if (AmbientLife.Current != null) Stats["ambient_low"] = $"{AmbientLife.Current.Creatures} creatures, {AmbientLife.Current.Sprites} sprites, {AmbientLife.Current.Triangles} tris, {AmbientLife.Current.Draws} draws";
            Shot("13-busy-night-low");
            RenderTier.Set(lvl);
            yield return Seconds(1.0);

            foreach (int q in new[] { FramePolicy.LevelLow, FramePolicy.LevelMedium, FramePolicy.LevelHigh })
            {
                AmbientBudget b = AmbientBudget.For(q);
                int lan = AmbientLife.Current != null ? AmbientLife.Current.LanternsFound : 0;
                Stats["budget_q" + q] = $"tris {b.Triangles(lan)}, draws {b.Draws(lan)}, skyrays {b.Skyrays}, loafers {b.Loafers}, birds {b.Birds}, clouds {b.Clouds}, fireflies {b.Fireflies}, lanterns {b.Lanterns(lan)}";
            }
            Unframe();
            Finish(_shots == _wanted ? 0 : 1);
        }

        static void RecordWorld(string when)
        {
            WorldView v = UnityEngine.Object.FindAnyObjectByType<WorldView>();
            if (v != null) Stats["world_" + when] = $"{v.Renderers} renderers, {v.Triangles} triangles, drawn now {WorldView.LastTriangles}";
        }

        static int Alive(World w)
        {
            int n = 0;
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Alive) n++;
            return n;
        }

        /// <summary>The alive fighter with the most alive fighters within 9 u.</summary>
        static void Busiest(World w, out double x, out double z, out int best)
        {
            best = -1; x = w.Map.Hill != null ? w.Map.Hill.X : w.X[0]; z = w.Map.Hill != null ? w.Map.Hill.Z : w.Z[0];
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                int n = 0;
                for (int j = 0; j < w.ActorCount; j++)
                    if (w.Actors[j].Alive && (w.X[j] - w.X[i]) * (w.X[j] - w.X[i]) + (w.Z[j] - w.Z[i]) * (w.Z[j] - w.Z[i]) < 81.0) n++;
                if (n > best) { best = n; x = w.X[i]; z = w.Z[i]; }
            }
        }

        static bool Solid(MapDef m, int cx, int cz) =>
            cx >= 0 && cz >= 0 && cx < m.Width && cz < m.Height && m.Cells[cz * m.Width + cx] != Cell.Void;

        /// <summary>A clock value at which a creature of the kind is over open void (no land within 3 cells) yet has land
        /// within 12 cells, so the frame shows both the islands and the creature.</summary>
        static bool FindVoidMoment(World w, AmbientLife life, int kind, out float clock, out Vector3 at)
        {
            MapDef m = w.Map;
            for (float t = 5f; t < 1200f; t += 0.5f)
                foreach (AmbientCreature c in life.LastCreatures)
                {
                    if (c.Kind != kind) continue;
                    Vector3 p = AmbientLayout.PositionAt(c, t);
                    int cx = Mathf.FloorToInt(p.x), cz = Mathf.FloorToInt(-p.z);
                    bool clear = true, land = false;
                    for (int dz = -12; dz <= 12 && clear; dz++)
                        for (int dx = -12; dx <= 12; dx++)
                        {
                            if (!Solid(m, cx + dx, cz + dz)) continue;
                            if (Math.Abs(dx) <= 3 && Math.Abs(dz) <= 3) { clear = false; break; }
                            land = true;
                        }
                    if (clear && land) { clock = t; at = p; return true; }
                }
            clock = 0f; at = Vector3.zero;
            return false;
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
                    RenderTexture rt = Rt(1);
                    if (cam.targetTexture != rt) { cam.targetTexture = rt; cam.aspect = (float)Sizes[1].x / Sizes[1].y; }
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
                Debug.LogError("[ArenaNightCapture] " + e);
                Note("FAILED: " + e.Message);
                Finish(1);
            }
        }

        static RenderTexture Rt(int i)
        {
            if (_rts[i] == null)
            {
                _rts[i] = new RenderTexture(Sizes[i].x, Sizes[i].y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                _rts[i].Create();
            }
            return _rts[i];
        }

        static void Shot(string name)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            for (int k = 0; k < Sizes.Length; k++)
            {
                _wanted++;
                if (cam == null) { Note(name + ": no main camera"); continue; }
                Vector2Int size = Sizes[k];
                RenderTexture rt = Rt(k);
                string path = Path.Combine(_out, $"{name}@{size.x}x{size.y}.png");
                RenderTexture prev = RenderTexture.active;
                cam.targetTexture = rt;
                cam.aspect = (float)size.x / size.y;
                try
                {
                    foreach (FlowScreen s in UnityEngine.Object.FindObjectsByType<FlowScreen>(FindObjectsSortMode.None)) s.UpdateScale();
                    GameHud hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
                    if (hud != null) hud.UpdateScale();
                    Canvas.ForceUpdateCanvases();
                    Canvas.ForceUpdateCanvases();
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    else cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    _shots++;
                    Note("wrote " + Path.GetFileName(path));
                }
                catch (Exception e) { Note(name + " FAILED: " + e.Message); }
                finally { RenderTexture.active = prev; }
            }
            // Back to the 1080p target the walk runs at.
            cam = UnityEngine.Camera.main;
            if (cam != null) { cam.targetTexture = Rt(1); cam.aspect = (float)Sizes[1].x / Sizes[1].y; }
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Update;
            TimeOfDayDirector.Override = null;
            AmbientLife.ClockOverride = null;
            if (_hadPref) PlayerPrefs.SetInt(TimeOfDaySettings.Key, _pref); else PlayerPrefs.DeleteKey(TimeOfDaySettings.Key);
            GameFlow.ForceEnabled = false;
            if (UnityEngine.Camera.main != null) { UnityEngine.Camera.main.targetTexture = null; UnityEngine.Camera.main.ResetAspect(); }
            Note($"exit {code}, {_shots} of {_wanted} shot(s)");
            try
            {
                File.WriteAllLines(Path.Combine(_out, "walk.log"), Log);
                var sb = new System.Text.StringBuilder("{\n");
                int i = 0;
                foreach (var kv in Stats)
                    sb.Append("  \"").Append(kv.Key).Append("\": ").Append(kv.Value is string s ? "\"" + s.Replace("\"", "'") + "\"" : Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture))
                      .Append(++i < Stats.Count ? ",\n" : "\n");
                sb.Append("}\n");
                File.WriteAllText(Path.Combine(_out, "stats.json"), sb.ToString());
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

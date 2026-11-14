using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Effects
{
    /// <summary>
    /// GORE setting capture flag (2026-11-14): the same staged moments at FULL, REDUCED and OFF, for close stills side by side.
    ///   Unity -batchmode -projectPath . -executeMethod CriticalMass.Editor.Effects.GoreCapture.Run -goreOut DIR -goreLevel full
    ///   (then again with -goreLevel reduced and -goreLevel off, same DIR; each run writes its own stills, and any run that
    ///   finds all three levels in DIR also writes the side-by-side sheets gore-sheet-SHOT.png, FULL | REDUCED | OFF).
    /// Boot scene, 16v16 bots, fixed seed, wall clock stopped (SimDriverBehaviour.CaptureMode), stepped one tick at a time:
    /// the sim never reads the GORE level, so every run finds the same hit, knock-out and hill fight on the same ticks.
    /// Shots: hit (2 ticks after a hit: the silhouette flash and the burst), knockout (4 ticks after a death), knockout-pop
    /// (the poof at the end of the body's time), hill-fight (a hit with 10 or more fighters within 4 units), hill-later
    /// (10 s wall after that fight: whatever stays on the ground). Each shot: gore-LEVEL-SHOT.png (1920x1080) and
    /// gore-LEVEL-SHOT-close.png (the centre 960x540), plus gore-LEVEL-report.json with the live effect counts, the gore
    /// spawns per registry entry, and a dark-red pixel count in the close crop (info only: team Crimson shadows can count).
    /// Needs a graphics device (not -nographics). The level is pinned with GoreGate.Override; the player's PlayerPrefs are
    /// never written.
    /// </summary>
    [InitializeOnLoad]
    public static class GoreCapture
    {
        const string ActiveKey = "CM.GoreCapture.Active", OutKey = "CM.GoreCapture.Out", LevelKey = "CM.GoreCapture.Level";
        const int W = 1920, H = 1080, MaxFrames = 30000, Settle = 10;

        struct Shot { public string Name; public SimEventType Type; public int After; public int MinTick; public int Crowd; public bool Pop; public double LaterWall; }
        static readonly Shot[] Shots =
        {
            new Shot { Name = "hit", Type = SimEventType.Hit, After = 2, MinTick = 900, Crowd = 0 },
            new Shot { Name = "knockout", Type = SimEventType.Death, After = 4, MinTick = 900 },
            new Shot { Name = "knockout-pop", Type = SimEventType.Death, After = 0, MinTick = 900, Pop = true },
            new Shot { Name = "hill-fight", Type = SimEventType.Hit, After = 3, MinTick = 1350, Crowd = 10 },
            new Shot { Name = "hill-later", Type = SimEventType.Hit, After = 0, MinTick = 1350, Crowd = 10, LaterWall = 10.0 },
        };
        static readonly string[] LevelNames = { "full", "reduced", "off" };

        static int _phase, _frames, _shot, _settle, _head;
        static bool _found;
        static double _fx, _fz;
        static SimDriverBehaviour _driver;
        static CombatFx _combat;
        static RenderTexture _rt;
        static readonly List<string> Report = new List<string>();

        static GoreCapture()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            GoreGate.Override = Parse(SessionState.GetString(LevelKey, "full"));
            Hook();
        }

        static GoreLevel Parse(string s) => s == "off" ? GoreLevel.Off : s == "reduced" ? GoreLevel.Reduced : GoreLevel.Full;
        static string Name(GoreLevel l) => l == GoreLevel.Off ? "off" : l == GoreLevel.Reduced ? "reduced" : "full";

        public static void Run()
        {
            string outDir = null, level = "full";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-goreOut") outDir = args[i + 1];
                if (args[i] == "-goreLevel") level = args[i + 1].ToLowerInvariant();
            }
            if (string.IsNullOrEmpty(outDir)) { Debug.LogError("[GoreCapture] -goreOut DIR required"); EditorApplication.Exit(64); return; }
            if (Array.IndexOf(LevelNames, level) < 0) { Debug.LogError("[GoreCapture] -goreLevel full|reduced|off"); EditorApplication.Exit(64); return; }
            Directory.CreateDirectory(outDir);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(OutKey, Path.GetFullPath(outDir));
            SessionState.SetString(LevelKey, level);
            GoreGate.Override = Parse(level);
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
                if (++_frames > MaxFrames) { Debug.LogError("[GoreCapture] timed out in phase " + _phase); Finish(2); return; }
                if (!EditorApplication.isPlaying) return;
                switch (_phase)
                {
                    case 0: Begin(); break;
                    case 1: Hunt(); break;
                    case 2: Frame(); break;
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
            GoreLevel level = Parse(SessionState.GetString(LevelKey, "full"));
            GoreGate.Override = level;
            _combat.ApplyGore(level);
            _driver.Ticked += OnTick;
            _head = _driver.World.EventHead;
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _rt.Create();
            Debug.Log($"[GoreCapture] level={Name(level)} actors={_driver.World.ActorCount}");
            _phase = 1;
        }

        static void OnTick(World w)
        {
            if (_found || _phase != 1 || _shot >= Shots.Length) { _head = w.EventHead; return; }
            Shot s = Shots[_shot];
            if (s.LaterWall > 0) { _head = w.EventHead; return; }   // a timed follow-up of the previous shot, no new event
            for (int n = Math.Max(_head, w.EventHead - World.EventCapacity); n < w.EventHead && !_found; n++)
            {
                SimEvent e = w.Events[n % World.EventCapacity];
                if (e.Type != s.Type || w.Tick < s.MinTick) continue;
                if (s.Crowd > 0 && Crowd(w, e.X, e.Z) < s.Crowd) continue;
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
            Shot s = Shots[_shot];
            double hz = _driver.Config.SimHz;
            if (s.LaterWall > 0)
            {
                // Same spot as the previous shot, later: what the fight left on the ground.
                _driver.StepTicks(Math.Max(1, (int)Math.Round(_combat.Scene.S(s.LaterWall) * hz)));
            }
            else
            {
                for (int k = 0; k < 900 && !_found; k++) _driver.StepTicks(1);
                if (_driver.World.Tick > 30 * 470 && !_found)
                {
                    Report.Add($"{{\"shot\": \"{s.Name}\", \"missing\": true, \"tick\": {_driver.World.Tick}}}");
                    NextShot();
                    return;
                }
                if (!_found) return;
                int after = s.Pop ? (int)Math.Ceiling((_combat.Scene.CorpseSeconds + _combat.Scene.PopSeconds * 0.4) * hz) : s.After;
                if (after > 0) _driver.StepTicks(after);
            }
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
            string lv = SessionState.GetString(LevelKey, "full");
            string full = Path.Combine(dir, $"gore-{lv}-{s.Name}.png"), close = Path.Combine(dir, $"gore-{lv}-{s.Name}-close.png");
            int red = Render(full, close);
            World w = _driver.World;
            double now = w.Tick / _driver.Config.SimHz;
            FxScene sc = _combat.Scene;
            var spawns = new List<string>();
            foreach (GorePiece p in GoreGate.All) spawns.Add($"\"{p}\": {sc.GoreSpawns(p)}");
            Report.Add(string.Format(CultureInfo.InvariantCulture,
                "{{\"shot\": \"{0}\", \"level\": \"{1}\", \"tick\": {2}, \"x\": {3:0.0}, \"z\": {4:0.0}, \"effectsActive\": {5}, \"bloodPieces\": {6}, \"splats\": {7}, \"gibs\": {8}, \"drips\": {9}, \"sparks\": {10}, \"verts\": {11}, \"darkRedPixelsClose\": {12}, \"goreSpawns\": {{{13}}}}}",
                s.Name, lv, w.Tick, _fx, _fz, sc.ActiveCount(now), sc.BloodCount(now), sc.DecalCount(now), sc.GibCount(now), sc.DripCount(now),
                sc.SparkCount(now), _combat.LastVerts, red, string.Join(", ", spawns)));
            Debug.Log("[GoreCapture] wrote " + full);
            NextShot();
        }

        static void NextShot()
        {
            _shot++;
            _found = false;
            if (_shot >= Shots.Length) { WriteReport(); Sheets(); Finish(0); return; }
            _phase = 1;
        }

        /// <summary>Render the gameplay camera; write the full frame and the centre crop; return the crop's dark-red pixels.</summary>
        static int Render(string path, string closePath)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _driver.GetComponent<ActorViews>().Sync(1f / 60f);
            _combat.Rebuild(cam.transform.right, cam.transform.up);
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
            const int cw = W / 2, ch = H / 2;
            Color32[] px = tex.GetPixels32();
            var crop = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            var cpx = new Color32[cw * ch];
            int red = 0;
            for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                Color32 c = px[(y + ch / 2) * W + x + cw / 2];
                cpx[y * cw + x] = c;
                if (c.r >= 70 && c.g <= 45 && c.b <= 60 && c.r > 2 * c.g) red++;
            }
            crop.SetPixels32(cpx);
            crop.Apply();
            File.WriteAllBytes(closePath, crop.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(crop);
            UnityEngine.Object.DestroyImmediate(tex);
            return red;
        }

        /// <summary>Side-by-side sheets (FULL | REDUCED | OFF, close crops at half size) for every shot all three levels have.</summary>
        static void Sheets()
        {
            string dir = SessionState.GetString(OutKey, ".");
            foreach (Shot s in Shots)
            {
                var tiles = new Texture2D[3];
                bool all = true;
                for (int i = 0; i < 3 && all; i++)
                {
                    string p = Path.Combine(dir, $"gore-{LevelNames[i]}-{s.Name}-close.png");
                    if (!File.Exists(p)) { all = false; break; }
                    tiles[i] = new Texture2D(2, 2);
                    tiles[i].LoadImage(File.ReadAllBytes(p));
                }
                if (all)
                {
                    int tw = tiles[0].width, th = tiles[0].height, gap = 8;
                    var sheet = new Texture2D(tw * 3 + gap * 2, th, TextureFormat.RGB24, false);
                    var fill = new Color32[sheet.width * th];
                    for (int k = 0; k < fill.Length; k++) fill[k] = new Color32(24, 35, 59, 255);
                    sheet.SetPixels32(fill);
                    for (int i = 0; i < 3; i++) sheet.SetPixels(i * (tw + gap), 0, Math.Min(tw, tiles[i].width), Math.Min(th, tiles[i].height), tiles[i].GetPixels(0, 0, Math.Min(tw, tiles[i].width), Math.Min(th, tiles[i].height)));
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(dir, $"gore-sheet-{s.Name}.png"), sheet.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(sheet);
                }
                foreach (Texture2D t in tiles) if (t != null) UnityEngine.Object.DestroyImmediate(t);
            }
        }

        static void WriteReport()
        {
            string dir = SessionState.GetString(OutKey, ".");
            string lv = SessionState.GetString(LevelKey, "full");
            File.WriteAllText(Path.Combine(dir, $"gore-{lv}-report.json"), "[\n  " + string.Join(",\n  ", Report) + "\n]\n");
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Update;
            SessionState.SetBool(ActiveKey, false);
            GoreGate.Override = null;
            if (_driver != null) _driver.Ticked -= OnTick;
            if (_rt != null) { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); }
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.Exit(code);
        }
    }
}

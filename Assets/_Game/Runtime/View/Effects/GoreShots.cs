using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Skills;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// fix/gore-proof-1: the GORE proof in a PLAYER build (the editor GoreCapture needs the editor). Autoplay run with
    /// <c>-goreShots [DIR]</c> (default DIR = GamePaths.DataDir/gore-shots). One run stages every scene at all three levels:
    /// the world is reset to the same seed for each (scene, level), so the sim (which never reads the GORE level) gives the
    /// same fight on the same ticks, and only the view differs. Scenes: hit (a fighter hits YOUR fighter, so the damage edge
    /// shows), knockout (4 ticks after a knock-out), knockout-pop (the body's pop: the sparkle poof at Off), kaiju-hit (the
    /// Kaiju hits a fighter; the egg is force-hatched as in -skillShots), hill-fight (a hit with 10 or more fighters within
    /// 4 u). Per scene: gore-sheet-SCENE.png (FULL | REDUCED | OFF close-ups, 960x540 each) and, per level, the close tile
    /// and the full screen (with the HUD and the damage edge). Log lines ("[CM.Gore]"):
    ///   level=off scene=hit goreSpawned=0 (...per piece...) bloodRedShare=0.00000 crimsonLikeShare=... flash=True sparks=N
    ///   edge=navy alpha=0.62 edgeNavyShare=... edgeRedShare=...
    ///   LEVEL off: goreSpawned=0 across all scenes (expect 0)
    /// "Blood red" = hue within 15 degrees of red, saturation 0.74 or more and value 0.25 or more. Shading multiplies a
    /// colour, so it keeps its saturation: the Crimson team colour (#FF5A5F, saturation 0.65) and its toon shades fall
    /// below the line, every blood colour (FxScene.BloodLight/Mid/Dark, saturation 0.76 to 0.91) is above it. The close
    /// tile is a camera render (no HUD), so HUD reds never count. Reads the sim only through events; the clock is held.
    /// </summary>
    public sealed class GoreShots : MonoBehaviour
    {
        public const string Flag = "-goreShots";
        public const int TileW = 960, TileH = 540, Seed = 1337;
        public const float CloseDistance = 9f;
        public const double MinSat = 0.74, MinVal = 0.25, MaxHueDeg = 15;

        enum Scene { Hit, Knockout, KnockoutPop, KaijuHit, HillFight }
        static readonly string[] SceneNames = { "hit", "knockout", "knockout-pop", "kaiju-hit", "hill-fight" };
        static readonly GoreLevel[] Levels = { GoreLevel.Full, GoreLevel.Reduced, GoreLevel.Off };

        SimDriverBehaviour _driver;
        CombatFx _fx;
        string _dir;
        int _head, _foundA, _foundB;
        bool _found;
        double _fxX, _fxZ;

        static bool? _req;
        /// <summary>True when the command line has -goreShots.</summary>
        public static bool Requested
        {
            get
            {
                if (_req == null)
                {
                    _req = false;
                    foreach (string a in Environment.GetCommandLineArgs())
                        if (string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase)) _req = true;
                }
                return _req.Value;
            }
        }

        /// <summary>Hue (degrees, 0..360), saturation and value of a colour.</summary>
        public static void Hsv(Color32 c, out double h, out double s, out double v)
        {
            double r = c.r / 255.0, g = c.g / 255.0, b = c.b / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max;
            s = max > 0 ? d / max : 0;
            if (d <= 0) { h = 0; return; }
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        /// <summary>A saturated, blood-like red (see the class notes); the team Crimson and its shades are not.</summary>
        public static bool BloodRed(Color32 c)
        {
            Hsv(c, out double h, out double s, out double v);
            return (h <= MaxHueDeg || h >= 360 - MaxHueDeg) && s >= MinSat && v >= MinVal;
        }

        /// <summary>A red of the team Crimson's saturation band (info only: it is excluded from the blood measure).</summary>
        public static bool CrimsonLike(Color32 c)
        {
            Hsv(c, out double h, out double s, out double v);
            return (h <= MaxHueDeg || h >= 360 - MaxHueDeg) && s >= 0.5 && s < MinSat && v >= MinVal;
        }

        /// <summary>The navy damage edge colour family (dark, blue-dominant).</summary>
        public static bool NavyEdge(Color32 c) => c.b > c.r + 12 && c.b >= c.g && c.b < 150 && c.r < 90;

        /// <summary>Near white (the whole-silhouette flash).</summary>
        public static bool NearWhite(Color32 c) => c.r >= 235 && c.g >= 235 && c.b >= 235;

        /// <summary>Share of pixels matching a test, optionally only the outer band (edge share of the shorter side).</summary>
        public static double Share(Color32[] px, int w, int h, Func<Color32, bool> test, float edge = 0f)
        {
            int band = (int)(Math.Min(w, h) * edge), n = 0, all = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (edge > 0f && x >= band && x < w - band && y >= band && y < h - band) continue;
                    all++;
                    if (test(px[y * w + x])) n++;
                }
            return all > 0 ? (double)n / all : 0;
        }

        static void Log(string s) => Debug.Log("[CM.Gore] " + s);
        static string F(double v, string f = "0.00000") => v.ToString(f, CultureInfo.InvariantCulture);

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _fx = GetComponent<CombatFx>();
            if (_driver == null || _fx == null) { enabled = false; return; }
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, s => string.Equals(s, Flag, StringComparison.OrdinalIgnoreCase));
            _dir = i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : Path.Combine(GamePaths.DataDir, "gore-shots");
            Directory.CreateDirectory(_dir);
            StartCoroutine(Run());
        }

        void OnDestroy()
        {
            if (_driver != null) _driver.Ticked -= OnTick;
            AutoplayArgs.ClockHeld = false;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _fx.Scene == null || _driver.World.Match.Phase != MatchPhase.Playing)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); Application.Quit(3); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            GoreLevel? pinned = GoreGate.Override;
            double scale = _driver.TimeScale;
            _driver.Ticked += OnTick;
            var totals = new int[Levels.Length];
            var perPiece = new int[Levels.Length, GoreGate.All.Length];
            for (int sc = 0; sc < SceneNames.Length; sc++)
            {
                var tiles = new Texture2D[Levels.Length];
                for (int lv = 0; lv < Levels.Length; lv++)
                {
                    var holder = new Texture2D[1];
                    IEnumerator e = Capture((Scene)sc, Levels[lv], holder, perPiece, lv);
                    while (e.MoveNext()) yield return e.Current;
                    tiles[lv] = holder[0];
                }
                Sheet(SceneNames[sc], tiles);
                foreach (Texture2D t in tiles) if (t != null) Destroy(t);
            }
            for (int lv = 0; lv < Levels.Length; lv++)
            {
                var sb = new StringBuilder();
                for (int p = 0; p < GoreGate.All.Length; p++)
                {
                    totals[lv] += perPiece[lv, p];
                    if (perPiece[lv, p] > 0) sb.Append(' ').Append(GoreGate.All[p]).Append('=').Append(perPiece[lv, p]);
                }
                Log("LEVEL " + Name(Levels[lv]) + ": goreSpawned=" + totals[lv] + " across all scenes" + (Levels[lv] == GoreLevel.Off ? " (expect 0)" : "") + sb);
            }
            Log("done: " + (totals[2] == 0 ? "OFF spawned no gore" : "OFF SPAWNED GORE") + "; sheets in " + _dir);
            _driver.Ticked -= OnTick;
            GoreGate.Override = pinned;
            AutoplayArgs.ClockHeld = false;
            _driver.TimeScale = scale;
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        static string Name(GoreLevel l) => l == GoreLevel.Off ? "off" : l == GoreLevel.Reduced ? "reduced" : "full";

        Scene _want;
        int _minTick;

        void OnTick(World w)
        {
            if (_found) { _head = w.EventHead; return; }
            for (int n = Math.Max(_head, w.EventHead - World.EventCapacity); n < w.EventHead && !_found; n++)
            {
                SimEvent e = w.Events[n % World.EventCapacity];
                if (w.Tick < _minTick || e.A < 0 || e.A >= w.ActorCount) continue;
                bool hit = e.Type == SimEventType.Hit, death = e.Type == SimEventType.Death;
                bool ok;
                switch (_want)
                {
                    case Scene.Hit: ok = hit && e.A == 0 && e.B >= 0; break;
                    case Scene.Knockout:
                    case Scene.KnockoutPop: ok = death && e.B >= 0; break;
                    case Scene.KaijuHit: ok = (hit || death) && e.B == Damage.KaijuSource; break;
                    default: ok = hit && Crowd(w, e.X, e.Z) >= 10; break;
                }
                if (!ok) continue;
                _found = true;
                _foundA = e.A;
                _foundB = e.B;
                _fxX = e.X;
                _fxZ = e.Z;
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

        void Step(int n)
        {
            for (int t = 0; t < n; t++)
            {
                _driver.HumanAutopilot = true;
                _driver.StepTicks(1);
            }
        }

        IEnumerator Capture(Scene sc, GoreLevel level, Texture2D[] holder, int[,] perPiece, int lv)
        {
            string name = SceneNames[(int)sc], lvName = Name(level);
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = Seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = _driver.Config });
            AutoplayArgs.ClockHeld = true;
            _driver.TimeScale = 0;
            GoreGate.Override = level;
            _fx.ApplyGore(level);
            var paint = FindAnyObjectByType<CriticalMass.Runtime.View.Reactions.GroundPaint>();
            if (paint != null) paint.ClearAll();
            _fx.Scene.Clear();
            Step(1);                       // CombatFx sees the clock go back here and drops the old world's effects
            _fx.Scene.Clear();
            _fx.Scene.ResetGoreCounts();
            bool kaiju = sc == Scene.KaijuHit;
            w.ForceHatch = kaiju;
            _want = sc;
            _minTick = sc == Scene.HillFight ? (int)(30 * w.Config.SimHz) : (int)(10 * w.Config.SimHz);
            _found = false;
            _head = w.EventHead;
            int limit = (int)(240 * w.Config.SimHz), stepped = 0;
            while (!_found && stepped < limit && w.Match.Phase == MatchPhase.Playing)
            {
                if (kaiju && w.Kaiju.State != KaijuState.Egg) SkillCapture.Gather(w);
                Step(1);
                stepped++;
                if (stepped % 300 == 0) yield return null;
            }
            w.ForceHatch = false;
            if (!_found)
            {
                Log("level=" + lvName + " scene=" + name + " NOT FOUND after " + stepped + " ticks");
                yield break;
            }
            int foundTick = w.Tick;
            int after = sc == Scene.KnockoutPop
                ? (int)Math.Ceiling((_fx.Scene.CorpseSeconds + _fx.Scene.PopSeconds * 0.4) * w.Config.SimHz)
                : sc == Scene.Knockout ? 4 : sc == Scene.HillFight ? 3 : 2;
            Step(after);
            Vector3 focus = kaiju ? SkillCapture.KaijuFocus(w) : SimSpace.ToWorld(_fxX, 0, _fxZ);
            yield return null;
            yield return null;             // the views and the effect meshes rebuild at the held clock
            yield return new WaitForEndOfFrame();
            Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();
            Texture2D tile = Close(focus);
            holder[0] = tile;
            File.WriteAllBytes(Path.Combine(_dir, "gore-" + lvName + "-" + name + "-full.png"), full.EncodeToPNG());
            if (tile != null) File.WriteAllBytes(Path.Combine(_dir, "gore-" + lvName + "-" + name + ".png"), tile.EncodeToPNG());

            double now = w.Tick / w.Config.SimHz;
            FxScene s = _fx.Scene;
            var pieces = new StringBuilder();
            int total = 0;
            for (int p = 0; p < GoreGate.All.Length; p++)
            {
                int c = s.GoreSpawns(GoreGate.All[p]);
                perPiece[lv, p] += c;
                total += c;
                if (c > 0) pieces.Append(' ').Append(GoreGate.All[p]).Append('=').Append(c);
            }
            bool flash = _fx.Pose(_foundA, out ActorPoseFx pf) && pf.Flash;
            var line = new StringBuilder();
            line.Append("level=").Append(lvName).Append(" scene=").Append(name).Append(" tick=").Append(foundTick).Append('+').Append(after)
                .Append(" victim=").Append(_foundA).Append(" by=").Append(_foundB)
                .Append(" goreSpawned=").Append(total).Append(" (").Append(pieces.Length > 0 ? pieces.ToString().Trim() : "none").Append(')')
                .Append(" flash=").Append(flash).Append(" sparks=").Append(s.SparkCount(now)).Append(" blood=").Append(s.BloodCount(now))
                .Append(" splats=").Append(s.DecalCount(now)).Append(" gibs=").Append(s.GibCount(now));
            if (tile != null)
            {
                Color32[] px = tile.GetPixels32();
                line.Append(" bloodRedShare=").Append(F(Share(px, TileW, TileH, BloodRed)))
                    .Append(" crimsonLikeShare=").Append(F(Share(px, TileW, TileH, CrimsonLike)))
                    .Append(" whiteShare=").Append(F(Share(px, TileW, TileH, NearWhite)));
            }
            Color32[] fp = full.GetPixels32();
            line.Append(" edge=").Append(_fx.VignetteNavy ? "navy" : "red").Append(" edgeAlpha=").Append(F(_fx.VignetteAlpha, "0.00"))
                .Append(" edgeNavyShare=").Append(F(Share(fp, full.width, full.height, NavyEdge, 0.06f)))
                .Append(" edgeRedShare=").Append(F(Share(fp, full.width, full.height, BloodRed, 0.06f)));
            Log(line.ToString());
            Destroy(full);
        }

        Texture2D Close(Vector3 focus)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return null;
            Transform t = cam.transform;
            Vector3 p = t.position;
            Quaternion r = t.rotation;
            RenderTexture old = cam.targetTexture, prev = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(TileW, TileH, 24);
            Texture2D tex = null;
            try
            {
                t.position = focus - r * Vector3.forward * CloseDistance;
                _fx.Rebuild(t.right, t.up);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(TileW, TileH, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, TileW, TileH), 0, 0);
                tex.Apply();
            }
            catch (Exception ex) { Log("close-up failed: " + ex.Message); }
            finally
            {
                cam.targetTexture = old;
                RenderTexture.active = prev;
                t.position = p;
                RenderTexture.ReleaseTemporary(rt);
            }
            return tex;
        }

        void Sheet(string scene, Texture2D[] tiles)
        {
            const int gap = 8;
            var sheet = new Texture2D(TileW * 3 + gap * 2, TileH, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * TileH];
            for (int k = 0; k < fill.Length; k++) fill[k] = new Color32(24, 35, 59, 255);
            sheet.SetPixels32(fill);
            for (int i = 0; i < tiles.Length; i++)
                if (tiles[i] != null) sheet.SetPixels32(i * (TileW + gap), 0, TileW, TileH, tiles[i].GetPixels32());
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(_dir, "gore-sheet-" + scene + ".png"), sheet.EncodeToPNG());
            Destroy(sheet);
            Log("sheet " + scene + " (FULL | REDUCED | OFF): " + Path.Combine(_dir, "gore-sheet-" + scene + ".png"));
        }
    }
}

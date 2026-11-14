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
        /// <summary>polish-3: close enough that the blood or sparks fill a good part of the tile (was 9 u: the hit was a
        /// speck). The focus is lifted to a fighter's chest so the hit sits in the middle of the frame.</summary>
        public const float CloseDistance = 5.5f, FocusLift = 1.0f;
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

        // polish-3 (the gore sheets proved nothing by eye: the "YOU ARE AZURE" start banner, a knock-out card and the
        // OVERCHARGE / "IT HATCHED" cut-ins covered every close-up): as SkillCapture does, every enabled screen-space root
        // canvas (HUD, cards, banners, moment plates) is hidden for the close-up only and put back after, any big moment in
        // flight is stopped while it stages and shoots, and the first start banner plays out before any shot. Capture only.
        static readonly System.Collections.Generic.List<Canvas> _hiddenHud = new System.Collections.Generic.List<Canvas>();

        static void HideHud()
        {
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();
            _hiddenHud.Clear();
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c != null && c.enabled && c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) { c.enabled = false; _hiddenHud.Add(c); }
        }

        static void ShowHud()
        {
            foreach (Canvas c in _hiddenHud) if (c != null) c.enabled = true;
            _hiddenHud.Clear();
        }

        /// <summary>polish-3: the camera rotation and focus of each scene's first level, reused for the other two so FULL,
        /// REDUCED and OFF are framed identically and compare side by side.</summary>
        Quaternion _sceneRot;
        Vector3 _sceneFocus;
        bool _sceneSet;

        static bool TitleShowing()
        {
            var t = FindAnyObjectByType<CriticalMass.Runtime.Hud.TitleScreen>();
            return t != null && t.isActiveAndEnabled;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            // integration batch 10: the title's live bot match is also Playing; wait for the real match (no title on screen),
            // or every shot is the logo over the attract match
            while (_driver.World == null || _fx.Scene == null || _driver.World.Match.Phase != MatchPhase.Playing
                   || TitleShowing())
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); Application.Quit(3); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            // polish-3: the match's start banner plays out once before any shot (hidden for close-ups in any case)
            yield return new WaitForSecondsRealtime((float)CriticalMass.Runtime.Hud.FirstMinutes.BannerHoldSeconds + CriticalMass.Runtime.Hud.FirstMinutes.FadeSeconds);
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
                    case Scene.Hit: ok = _staged ? hit && e.A == _target : hit && e.A == 0 && e.B >= 0; break;
                    case Scene.Knockout:
                    case Scene.KnockoutPop: ok = _staged ? death && e.A == _target : death && e.B >= 0; break;
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
                _driver.HumanAutopilot = SimDriverBehaviour.ScriptedHumanFrame == null;
                _driver.StepTicks(1);
            }
        }

        // ---- polish-4 staging (third try: the live-fight close-ups were a crowd mash and the HUD still showed) ----
        /// <summary>polish-4: shots are taken this long after the hit (s), so the hit star and white flash have faded and the
        /// blood has landed; side camera height and distance from the pair (u); the wide hill-fight frame's distance.</summary>
        public const double AfterHitSeconds = 0.45;
        public const float SideUp = 2.6f, SideBack = 6.2f, WideDistance = 14f;
        readonly InputFrame _frame = SimInput.EmptyInput();
        int _target = -1;
        double[] _benchX, _benchZ;
        bool _staged;

        static bool Floor(World w, double x, double z)
        {
            CriticalMass.Sim.Grid.MapDef m = w.Map;
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= m.Width || cz >= m.Height) return false;
            return m.Cells[cz * m.Width + cx] == Cell.Floor;
        }

        static void Put(World w, int id, double x, double z)
        {
            w.X[id] = w.PrevX[id] = x;
            w.Z[id] = w.PrevZ[id] = z;
        }

        /// <summary>Flat open floor (a 5 x 3 cell patch) farthest from every fighter, away from the hill.</summary>
        static bool Spot(World w, out double bx, out double bz)
        {
            CriticalMass.Sim.Grid.MapDef m = w.Map;
            double mx = m.Width * 0.5, mz = m.Height * 0.5, best = -1;
            bx = w.X[0]; bz = w.Z[0];
            for (int cz = 4; cz < m.Height - 4; cz += 2)
                for (int cx = 4; cx < m.Width - 4; cx += 2)
                {
                    double x = cx + 0.5, z = cz + 0.5, hx = x - mx, hz = z - mz;
                    if (hx * hx + hz * hz < 81) continue;
                    bool ok = true;
                    for (int dz = -1; dz <= 1 && ok; dz++)
                        for (int dx = -2; dx <= 3 && ok; dx++) ok = Floor(w, x + dx, z + dz);
                    if (!ok) continue;
                    double clear = double.MaxValue;
                    for (int i = 1; i < w.ActorCount; i++)
                    {
                        double ax = w.X[i] - x, az = w.Z[i] - z, d2 = ax * ax + az * az;
                        if (d2 < clear) clear = d2;
                    }
                    if (clear <= best) continue;
                    best = clear; bx = x; bz = z;
                }
            return best >= 0;
        }

        /// <summary>Staged step: actor 0 plays the scripted frame, everyone but the pair is held on its bench spot, the
        /// target stands still and never swings back.</summary>
        void StepStaged(int n)
        {
            World w = _driver.World;
            for (int t = 0; t < n; t++)
            {
                _driver.HumanAutopilot = false;
                if (_target >= 0)
                {
                    Actor d = w.Actors[_target];
                    d.AttackCd = Math.Max(d.AttackCd, 1.0);
                    d.AbilityCd = Math.Max(d.AbilityCd, 1.0);
                    d.AbilityActive = 0;
                    d.SwingWindup = 0;
                }
                _driver.StepTicks(1);
                if (_benchX != null)
                    for (int i = 1; i < w.ActorCount && i < _benchX.Length; i++)
                        if (i != _target && w.Actors[i].Alive) Put(w, i, _benchX[i], _benchZ[i]);
            }
        }

        IEnumerator Capture(Scene sc, GoreLevel level, Texture2D[] holder, int[,] perPiece, int lv)
        {
            string name = SceneNames[(int)sc], lvName = Name(level);
            if (lv == 0) _sceneSet = false;
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = Seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = _driver.Config });
            CriticalMass.Runtime.Hud.FirstMinutes.StartBannerOff = true;   // as -hitFeelShots: no start banner on a restage
            CriticalMass.Runtime.Hud.Banner.ClearAll();
            AutoplayArgs.ClockHeld = true;
            _driver.TimeScale = 0;
            GoreGate.Override = level;
            _fx.ApplyGore(level);
            var paint = FindAnyObjectByType<CriticalMass.Runtime.View.Reactions.GroundPaint>();
            if (paint != null) paint.ClearAll();
            _fx.Scene.Clear();
            _staged = sc == Scene.Hit || sc == Scene.Knockout || sc == Scene.KnockoutPop;
            _target = -1;
            _benchX = null;
            _frame.CopyFrom(SimInput.EmptyInput());
            _frame.AimX = double.NaN;
            _frame.AimZ = double.NaN;
            SimDriverBehaviour.ScriptedHumanFrame = _staged ? _frame : null;
            Step(1);                       // CombatFx sees the clock go back here and drops the old world's effects
            _fx.Scene.Clear();
            _fx.Scene.ResetGoreCounts();
            bool kaiju = sc == Scene.KaijuHit;
            w.ForceHatch = kaiju;
            _want = sc;
            _found = false;
            int stepped = 0;
            Vector3 focus;
            Quaternion rot;
            float dist;
            if (_staged)
            {
                // ONE attacker (YOU, Azure Warrior) and ONE target (the first Crimson fighter) on clear flat ground; every
                // other fighter benched on its spawn spot (held there every tick, far from the pair).
                StepStaged(2);
                if (!Spot(w, out double x, out double z)) Log("scene=" + name + " no open spot; staging at the spawn");
                _benchX = new double[w.ActorCount];
                _benchZ = new double[w.ActorCount];
                for (int i = 0; i < w.ActorCount; i++) { _benchX[i] = w.X[i]; _benchZ[i] = w.Z[i]; }
                for (int i = 1; i < w.ActorCount; i++)
                    if (w.Actors[i].Alive && w.Actors[i].Team != w.Actors[0].Team) { _target = i; break; }
                if (_target < 0) { Log("scene=" + name + " NOT FOUND: no target"); yield break; }
                double tx = x + 1.1, tz = z;
                Put(w, 0, x, z);
                Put(w, _target, tx, tz);
                w.Actors[0].Facing = 0;
                w.Actors[_target].Facing = Math.PI;
                w.Actors[0].SpawnProtect = 0;
                w.Actors[_target].SpawnProtect = 0;
                w.Actors[0].AttackCd = 0;
                if (sc != Scene.Hit) w.Actors[_target].Hp = Math.Min(w.Actors[_target].Hp, 1.0);   // the blow knocks out
                _frame.AimX = tx;
                _frame.AimZ = tz;
                StepStaged(3);
                _fx.Scene.Clear();
                _fx.Scene.ResetGoreCounts();
                _head = w.EventHead;
                _minTick = 0;
                int limit = (int)(4 * w.Config.SimHz);
                while (!_found && stepped < limit)
                {
                    _frame.Attack = (stepped % 2) == 0;   // press, release, press: the first landed blow is the shot
                    StepStaged(1);
                    if (_target >= 0 && w.Actors[_target].Alive) Put(w, _target, tx, tz);
                    stepped++;
                }
                _frame.Attack = false;
                double gy = GroundHeight.At(w.Map, x + 0.55, z);
                Vector3 mid = SimSpace.ToWorld(x + 0.55, gy + 0.7, z);
                Vector3 offset = SimSpace.ToWorld(0, SideUp, -SideBack) - SimSpace.ToWorld(0, 0, 0);
                rot = Quaternion.LookRotation(-offset, Vector3.up);
                dist = offset.magnitude;
                focus = mid;
            }
            else
            {
                _head = w.EventHead;
                _minTick = sc == Scene.HillFight ? (int)(30 * w.Config.SimHz) : (int)(10 * w.Config.SimHz);
                int limit = (int)(240 * w.Config.SimHz);
                while (!_found && stepped < limit && w.Match.Phase == MatchPhase.Playing)
                {
                    if (kaiju && w.Kaiju.State != KaijuState.Egg) SkillCapture.Gather(w);
                    Step(1);
                    stepped++;
                    if (stepped % 300 == 0) { CriticalMass.Runtime.View.Moments.BigMoments.StopAll(); yield return null; }
                }
                UnityEngine.Camera c0 = UnityEngine.Camera.main;
                rot = c0 != null ? c0.transform.rotation : Quaternion.identity;
                dist = sc == Scene.HillFight ? WideDistance : 8f;
                focus = kaiju ? SkillCapture.KaijuFocus(w) : SimSpace.ToWorld(_fxX, 0, _fxZ) + Vector3.up * FocusLift;
            }
            w.ForceHatch = false;
            if (!_found)
            {
                Log("level=" + lvName + " scene=" + name + " NOT FOUND after " + stepped + " ticks");
                SimDriverBehaviour.ScriptedHumanFrame = null;
                yield break;
            }
            int foundTick = w.Tick;
            int after = sc == Scene.KnockoutPop
                ? (int)Math.Ceiling((_fx.Scene.CorpseSeconds + _fx.Scene.PopSeconds * 0.4) * w.Config.SimHz)
                : (int)Math.Ceiling(AfterHitSeconds * w.Config.SimHz);
            if (_staged) StepStaged(after); else Step(after);
            SimDriverBehaviour.ScriptedHumanFrame = null;
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();
            CriticalMass.Runtime.Hud.Banner.ClearAll();
            yield return null;
            yield return null;             // the views and the effect meshes rebuild at the held clock
            yield return new WaitForEndOfFrame();
            Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();   // the full screen keeps the HUD and the damage edge
            // identical framing for FULL, REDUCED and OFF: the first level's camera is reused (same seed, same tick)
            if (!_sceneSet) { _sceneRot = rot; _sceneFocus = focus; _sceneDist = dist; _sceneSet = true; }
            float drift = Vector3.Distance(focus, _sceneFocus);
            Texture2D tile = Close(_sceneFocus, _sceneRot, _sceneDist, out int uiHidden, out int uiStillOn);
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
            line.Append("level=").Append(lvName).Append(" scene=").Append(name).Append(_staged ? " staged=1v1" : " staged=live")
                .Append(" tick=").Append(foundTick).Append('+').Append(after)
                .Append(" victim=").Append(_foundA).Append(" by=").Append(_foundB)
                .Append(" goreSpawned=").Append(total).Append(" (").Append(pieces.Length > 0 ? pieces.ToString().Trim() : "none").Append(')')
                .Append(" flash=").Append(flash).Append(" sparks=").Append(s.SparkCount(now)).Append(" blood=").Append(s.BloodCount(now))
                .Append(" splats=").Append(s.DecalCount(now)).Append(" gibs=").Append(s.GibCount(now))
                .Append(" uiHidden=").Append(uiHidden).Append(" uiStillOn=").Append(uiStillOn).Append(uiStillOn == 0 ? " (ok)" : " (UI LEAK)")
                .Append(" focusDrift=").Append(F(drift, "0.000"));
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
            _target = -1;
            _benchX = null;
        }

        float _sceneDist;

        /// <summary>polish-4: the close-up render with NO UI at all: every Canvas in the scene (root or nested, any mode;
        /// polish-3 hid only root canvases, and nested HUD canvases kept drawing) is disabled in the same call as the render,
        /// so no script can turn one back on in between, and the UI layer is culled from the camera as well. uiStillOn
        /// counts canvases still enabled at the render (must be 0).</summary>
        Texture2D Close(Vector3 focus, Quaternion r, float dist, out int hidden, out int stillOn)
        {
            hidden = 0;
            stillOn = 0;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return null;
            Transform t = cam.transform;
            Vector3 p = t.position;
            Quaternion r0 = t.rotation;
            int mask = cam.cullingMask;
            RenderTexture old = cam.targetTexture, prev = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(TileW, TileH, 24);
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            bool[] was = new bool[canvases.Length];
            Texture2D tex = null;
            try
            {
                for (int k = 0; k < canvases.Length; k++)
                {
                    was[k] = canvases[k].enabled;
                    if (was[k]) { canvases[k].enabled = false; hidden++; }
                }
                foreach (Canvas c in canvases) if (c != null && c.enabled) stillOn++;
                cam.cullingMask = mask & ~(1 << 5);   // the UI layer
                t.rotation = r;
                t.position = focus - r * Vector3.forward * dist;
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
                for (int k = 0; k < canvases.Length; k++) if (canvases[k] != null && was[k]) canvases[k].enabled = true;
                cam.cullingMask = mask;
                cam.targetTexture = old;
                RenderTexture.active = prev;
                t.position = p;
                t.rotation = r0;
                RenderTexture.ReleaseTemporary(rt);
            }
            return tex;
        }

        // 3x5 pixel letters for the column labels (FULL, REDUCED, OFF), one row of bits per line, top first.
        static readonly System.Collections.Generic.Dictionary<char, string> Glyph = new System.Collections.Generic.Dictionary<char, string>
        {
            { 'F', "111100110100100" }, { 'U', "101101101101111" }, { 'L', "100100100100111" }, { 'R', "110101110101101" },
            { 'E', "111100110100111" }, { 'D', "110101101101110" }, { 'C', "111100100100111" }, { 'O', "111101101101111" },
        };

        /// <summary>Writes a label in chunky cream pixels at the top left of a tile on the sheet.</summary>
        static void Label(Texture2D sheet, int x0, string text)
        {
            const int px = 6;
            int y0 = sheet.height - 14 - 5 * px;
            var bg = new Color32(24, 35, 59, 255);
            var fg = new Color32(255, 240, 214, 255);
            int w = text.Length * 4 * px + 2 * px;
            for (int y = y0 - px; y < y0 + 6 * px; y++)
                for (int x = x0 + 8; x < x0 + 8 + w; x++) sheet.SetPixel(x, y, bg);
            for (int i = 0; i < text.Length; i++)
            {
                if (!Glyph.TryGetValue(text[i], out string g)) continue;
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        if (g[row * 3 + col] != '1') continue;
                        int bx = x0 + 8 + px + (i * 4 + col) * px, by = y0 + (4 - row) * px;
                        for (int yy = 0; yy < px; yy++) for (int xx = 0; xx < px; xx++) sheet.SetPixel(bx + xx, by + yy, fg);
                    }
            }
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
            string[] names = { "FULL", "REDUCED", "OFF" };   // polish-4: each column says its level
            for (int i = 0; i < tiles.Length && i < names.Length; i++) Label(sheet, i * (TileW + gap), names[i]);
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(_dir, "gore-sheet-" + scene + ".png"), sheet.EncodeToPNG());
            Destroy(sheet);
            Log("sheet " + scene + " (FULL | REDUCED | OFF): " + Path.Combine(_dir, "gore-sheet-" + scene + ".png"));
        }
    }
}

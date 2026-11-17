using System;
using System.Collections;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.B13;
using CriticalMass.Runtime.View.Fog;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Capture only (b13-k, from b12-r): `-kaijuTypeShots [dir]` in a running match (the usual autoplay capture args). Four staged
    /// hatches in place: the world is reset, both teams are fed the same units of ONE kind (13 each: 26 = power level 3, the
    /// hatch NEUTRAL so the crystals show the type colour), everyone but the player is benched, the player is pinned beside the
    /// hatch point, the egg is force-hatched; stills at the hatch callout and 3 s later. Then MIRE's SHROUD with fog forced on,
    /// in top-down and third person: the local player inside the cloud (an enemy inside past 2.5 u must be hidden) and outside
    /// it (the enemy inside must be hidden). Each step logs `[CM.KaijuTypeShots] type=MIRE REACHED tint=#4E9A94 scale=1.00 ...`
    /// (or MISSED). Writes the sim, so never in a real match: only with the flag. Installs itself (no other file touched).
    /// </summary>
    public sealed class KaijuTypeShots : MonoBehaviour
    {
        public const string Flag = "-kaijuTypeShots";
        /// <summary>Units of the one kind fed per team (both teams: 26, past LevelUnits[2] = 24, so level 3).</summary>
        const double PerTeam = 13;
        /// <summary>The hatch point is HatchSouth south of the shrine centre (Kaiju.HatchOffsetZ); the player stands PlayerOffsetZ further south.</summary>
        const double HatchSouth = 6, PlayerOffsetZ = 6;

        SimDriverBehaviour _driver;
        string _dir;
        bool _pin;
        double _pinX, _pinZ;
        int _keepA = -1, _keepB = -1;
        double _ax, _az, _bx, _bz;
        int _missed;

        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Requested) return;
            var go = new GameObject("KaijuTypeShots");
            DontDestroyOnLoad(go);
            go.AddComponent<KaijuTypeShots>();
        }

        static void Log(string s) => Debug.Log("[CM.KaijuTypeShots] " + s);
        static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        void Start()
        {
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, Flag);
            _dir = i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : Path.Combine(GamePaths.DataDir, "kaiju-types");
            Directory.CreateDirectory(_dir);
            StartCoroutine(Run());
        }

        void OnDestroy() { if (_driver != null) _driver.Ticked -= OnTick; }

        static bool TitleShowing()
        {
            var t = FindAnyObjectByType<CriticalMass.Runtime.Hud.TitleScreen>();
            return t != null && t.isActiveAndEnabled;
        }

        static void Put(World w, int id, double x, double z)
        {
            w.X[id] = x; w.Z[id] = z; w.PrevX[id] = x; w.PrevZ[id] = z; w.Vx[id] = 0; w.Vz[id] = 0;
        }

        void OnTick(World w)
        {
            if (!_pin || w.ActorCount == 0) return;
            Put(w, LocalSeat.Actor, _pinX, _pinZ);
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                if (id == LocalSeat.Actor || id == _keepA || id == _keepB)
                {
                    a.SpawnProtect = 0;
                    a.Hp = a.MaxHp;
                    if (id == _keepA) Put(w, id, _ax, _az);
                    if (id == _keepB) Put(w, id, _bx, _bz);
                    continue;
                }
                if (!a.Alive && a.RespawnIn > 1e8) continue;
                a.Alive = false; a.Hp = 0; a.RespawnIn = 1e9; a.Moving = false;
            }
        }

        IEnumerator WaitTicks(World w, int n)
        {
            int end = w.Tick + n;
            float t0 = Time.realtimeSinceStartup;
            while (w.Tick < end && Time.realtimeSinceStartup - t0 < 30f) yield return null;
        }

        IEnumerator Still(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, name));
        }

        static readonly (string name, KaijuType want, BiomassType kind)[] Runs =
        {
            ("grove", KaijuType.Grove, BiomassType.Plant),
            ("brute", KaijuType.Brute, BiomassType.Meat),
            ("timber", KaijuType.Timber, BiomassType.Wood),
            ("mire", KaijuType.Mire, BiomassType.Water),
        };

        IEnumerator Hatch(World w, int hz, string name, KaijuType want, BiomassType kind)
        {
            _pin = false;
            _keepA = _keepB = -1;
            TeamId me = w.Actors[LocalSeat.Actor].Team;
            World.ResetWorld(w, new WorldOptions
            {
                Seed = 7, TeamSize = Math.Max(1, w.ActorCount / 2), HumanTeam = me, HumanClass = w.Actors[LocalSeat.Actor].Cls, Config = w.Config,
            });
            var shrine = w.Map.Shrine;
            _pinX = shrine.X;
            _pinZ = shrine.Z + HatchSouth + PlayerOffsetZ;
            _pin = true;
            yield return WaitTicks(w, hz);
            foreach (TeamId team in new[] { TeamId.Azure, TeamId.Crimson })
                SimResources.ApplyDelivery(w, team, CarryKind.Biomass, PerTeam, -1, kind);
            w.ForceHatch = true;
            float h0 = Time.realtimeSinceStartup;
            while (!w.Match.Hatched && Time.realtimeSinceStartup - h0 < 20f) yield return null;
            if (!w.Match.Hatched) { Log(name + ": the Kaiju did not hatch"); _missed++; yield break; }
            yield return WaitTicks(w, hz / 3);   // the banner is up, the shell has fallen
            yield return Still("kt-" + name + "-hatch.png");
            LogRun(name, want, "hatch", w);
            yield return WaitTicks(w, hz * 3);
            yield return Still("kt-" + name + "-3s.png");
            LogRun(name, want, "3s", w);
            yield return SideStill(w, "kt-" + name + "-side.png");
        }

        /// <summary>
        /// b13 r2: a side view with the Kaiju centred and nothing between it and the lens: an off-screen camera at the Kaiju's
        /// mid height, 11 u to the side (east or west, whichever line of sight is clear of tall cells), looking at its centre.
        /// </summary>
        IEnumerator SideStill(World w, string name)
        {
            yield return new WaitForEndOfFrame();
            if (w.Kaiju == null) { Log(name + " NOT REACHED (no Kaiju)"); yield break; }
            double kx = w.Kaiju.X, kz = w.Kaiju.Z;
            double ground = GroundHeight.At(w.Map, kx, kz);
            Vector3 at = SimSpace.ToWorld(kx, ground + 2.2, kz);
            double side = SegmentClear.Clear(w.Map, kx, kz, kx + 11, kz) ? 11 : -11;
            Vector3 eye = SimSpace.ToWorld(kx + side, ground + 3.4, kz + 1.5);
            UnityEngine.Camera main = UnityEngine.Camera.main;
            var go = new GameObject("KaijuTypeShots Side Camera");
            UnityEngine.Camera cam = go.AddComponent<UnityEngine.Camera>();
            if (main != null) cam.CopyFrom(main);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.3f;
            cam.transform.position = eye;
            cam.transform.LookAt(at);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_dir, name), tex.EncodeToPNG());
            cam.targetTexture = null;
            Destroy(rt);
            Destroy(tex);
            Destroy(go);
            Log(name + " side view from " + (side > 0 ? "east" : "west") + " at (" + kx.ToString("0.0") + ", " + kz.ToString("0.0") + ")");
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver == null || _driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3 || TitleShowing())
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); Application.Quit(3); yield break; }
                if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            _driver.Ticked += OnTick;
            World w = _driver.World;
            int hz = (int)Math.Round(w.Config.SimHz);
            Log("rule=" + (KaijuTypes.On(w) ? "on" : "off") + " profile=" + w.Config.Rules);
            foreach (var r in Runs) yield return Hatch(w, hz, r.name, r.want, r.kind);

            // ---- MIRE's SHROUD (the last run hatched MIRE level 3), fog forced on, top-down and third person
            if (KaijuTypes.Type(w) == KaijuType.Mire && KaijuTypes.PowerLevel(w) >= 1)
                yield return Shroud(w, hz);
            else { Log("type=MIRE MISSED shroud: no MIRE of level 1+ to stage"); _missed++; }

            _pin = false;
            _driver.Ticked -= OnTick;
            yield return new WaitForSecondsRealtime(1f);
            Log("done missed=" + _missed + " stills in " + _dir);
            Application.Quit(_missed == 0 ? 0 : 5);
        }

        IEnumerator Shroud(World w, int hz)
        {
            int me = LocalSeat.Actor;
            TeamId team = w.Actors[me].Team;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Team == team) continue;
                if (_keepA < 0) _keepA = i;
                else if (_keepB < 0) { _keepB = i; break; }
            }
            if (_keepB < 0) { Log("type=MIRE MISSED shroud: fewer than two enemies"); _missed++; yield break; }
            foreach (int id in new[] { _keepA, _keepB })
            {
                Actor a = w.Actors[id];
                a.Alive = true; a.Hp = a.MaxHp; a.RespawnIn = 0;
            }
            Kaiju k = w.Kaiju;
            KaijuTypesConfig c = w.Config.Depth.KaijuTypes;
            double r = c.ShroudRadius[KaijuTypes.PowerLevel(w) - 1];
            KaijuTypes.ForceShroud(w);
            yield return WaitTicks(w, c.ShroudPuffTicks + 3);
            // inside: you 2 u south of the Kaiju, enemy A 4 u east of you (inside the cloud, past 2.5 u); B on the far side
            double inX = k.X, inZ = k.Z + 2;
            _ax = k.X + 4; _az = k.Z + 2;
            _bx = k.X - 4; _bz = k.Z - 1;
            Put(w, _keepA, _ax, _az);
            Put(w, _keepB, _bx, _bz);
            double scale = _driver.TimeScale;
            AutoplayArgs.ClockHeld = true;
            _driver.TimeScale = 0;   // hold the cloud up while the stills are taken
            FogConfig.CaptureOverride = true;
            ActorViews views = FindAnyObjectByType<ActorViews>();
            KaijuTypeView cloud = FindAnyObjectByType<KaijuTypeView>();
            CameraView[] cams = { CameraView.TopDown, CameraView.ThirdPerson };
            string[] camNames = { "topdown", "thirdperson" };
            for (int where = 0; where < 2; where++)
            {
                bool inside = where == 0;
                double px = inside ? inX : k.X, pz = inside ? inZ : k.Z + r + 3;
                _pinX = px; _pinZ = pz;
                Put(w, me, px, pz);
                w.Actors[me].Facing = Math.Atan2(k.Z - pz, k.X - px);
                for (int v = 0; v < cams.Length; v++)
                {
                    GroundView.ForcePlay = cams[v] != CameraView.TopDown;
                    GroundView.ViewOverride = cams[v];
                    yield return new WaitForSecondsRealtime(1.0f);   // camera settles, the fog updates and fades
                    if (FogOfWar.Current != null) FogOfWar.Current.UpdateNow(w, true);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return new WaitForEndOfFrame();
                    string file = "kt-mire-shroud-" + camNames[v] + (inside ? "-inside" : "-outside") + ".png";
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, file));
                    ModelView mv = views != null ? views.ModelOf(_keepA) : null;
                    bool enemyDrawn = mv != null && mv.Active && !FogOfWar.Hides(_keepA);
                    bool ok = KaijuTypes.CloudActive(w, out _, out _, out double cr) && cloud != null && cloud.Drawn && !enemyDrawn && FogOfWar.Active;
                    if (!ok) _missed++;
                    Log("type=MIRE " + (ok ? "REACHED" : "MISSED") + " shroud view=" + camNames[v] + " you=" + (inside ? "inside" : "outside") +
                        " cloudR=" + F(cr) + " drawn=" + (cloud != null && cloud.Drawn) + " dense=" + (cloud != null && cloud.Dense) +
                        " alpha=" + F(cloud != null ? cloud.Alpha : 0) + " fog=" + FogOfWar.Active + " enemyInsideDrawn=" + enemyDrawn +
                        " sight=" + F(KaijuTypes.SightRadiusFor(w, me, FogConfig.SightTeammate)) + " casts=" + w.KaijuKind.Casts + " still=" + file);
                }
            }
            FogConfig.CaptureOverride = null;
            GroundView.ViewOverride = null;
            GroundView.ForcePlay = false;
            _driver.TimeScale = scale;
            AutoplayArgs.ClockHeld = false;
        }

        /// <summary>One log line for a still; counts a miss when the hatch did not reach the wanted type.</summary>
        void LogRun(string name, KaijuType want, string still, World w)
        {
            ActorViews views = FindAnyObjectByType<ActorViews>();
            KaijuAnimator anim = views != null ? views.KaijuAnimator : null;
            Kaiju k = w.Kaiju;
            KaijuType got = KaijuTypes.Type(w);
            int lv = KaijuTypes.PowerLevel(w);
            bool reached = got == want && lv == 3;
            if (!reached) _missed++;
            KaijuTypeState s = w.KaijuKind;
            Log("type=" + want.ToString().ToUpperInvariant() + " " + (reached ? "REACHED" : "MISSED got=" + got.ToString().ToUpperInvariant() + " level=" + lv) +
                " tint=" + KaijuTypeLook.CrystalHex(got) + " scale=" + F(KaijuTypeLook.Scale(got)) + " level=" + lv +
                " callout=\"" + KaijuTypeLook.Callout(got, lv) + "\" drawn=" + (anim != null ? F(anim.Scale) + " drawnType=" + anim.DrawnType : "none") +
                " still=kt-" + name + "-" + still + ".png state=" + k.State + " mix=" + F(s.Mix[0]) + "/" + F(s.Mix[1]) + "/" + F(s.Mix[2]) + "/" + F(s.Mix[3]) +
                " maxHp=" + F(k.MaxHp) + " hp=" + F(k.Hp) + " speed=" + F(KaijuTypes.MoveSpeed(w)) + " slamR=" + F(KaijuTypes.SlamRadius(w)) + " tick=" + w.Tick);
        }
    }
}

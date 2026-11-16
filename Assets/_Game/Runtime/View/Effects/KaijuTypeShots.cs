using System;
using System.Collections;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Capture only (b12-r, Kaiju types): `-kaijuTypeShots [dir]` in a running match (the usual autoplay capture args) does three
    /// staged runs in place: the world is reset, both teams are fed the same mix (80 % plant, 80 % meat, then 50/50, so the hatch
    /// is NEUTRAL and the crystals show the type colour), everyone but the player is benched, the player is pinned beside the
    /// hatch point (the camera follows it), the egg is force-hatched, and two stills are taken: at the hatch banner and 3 s
    /// later. Each run logs `[CM.KaijuTypeShots] type=GROVE REACHED tint=#5FAE3C scale=1.08 drawn=...` (or `MISSED` with the
    /// type it got). Writes the sim, so never in a real match: only with the flag. Installs itself (no other file touched).
    /// </summary>
    public sealed class KaijuTypeShots : MonoBehaviour
    {
        public const string Flag = "-kaijuTypeShots";
        /// <summary>Units fed per team: 4 + 1 = 80 % (5 units a team, 10 in all).</summary>
        const double Major = 4, Minor = 1, Even = 2.5;
        /// <summary>The hatch point is HatchSouth south of the shrine centre (the Sim's Kaiju.HatchOffsetZ, internal there); the
        /// player stands PlayerOffsetZ further south (in view, clear of the body).</summary>
        const double HatchSouth = 6, PlayerOffsetZ = 6;

        SimDriverBehaviour _driver;
        string _dir;
        bool _pin;
        double _pinX, _pinZ;

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

        void OnTick(World w)
        {
            if (!_pin || w.ActorCount == 0) return;
            w.X[0] = _pinX; w.Z[0] = _pinZ; w.PrevX[0] = _pinX; w.PrevZ[0] = _pinZ;
            w.Vx[0] = 0; w.Vz[0] = 0;
            Actor p = w.Actors[0];
            p.SpawnProtect = 0;
            p.Hp = p.MaxHp;
            for (int id = 1; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
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
            Log("rule=" + (Kaiju.TypesOn(w.Config) ? "on" : "off") + " profile=" + w.Config.Rules);
            var runs = new (string name, KaijuType want, double plant, double meat)[]
            {
                ("grove", KaijuType.Grove, Major, Minor),
                ("brute", KaijuType.Brute, Minor, Major),
                ("standard", KaijuType.Standard, Even, Even),
            };
            int missed = 0;
            foreach (var r in runs)
            {
                _pin = false;
                World.ResetWorld(w, new WorldOptions
                {
                    Seed = 7, TeamSize = Math.Max(1, w.ActorCount / 2), HumanTeam = w.Actors[0].Team, HumanClass = w.Actors[0].Cls, Config = w.Config,
                });
                var shrine = w.Map.Shrine;
                _pinX = shrine.X;
                _pinZ = shrine.Z + HatchSouth + PlayerOffsetZ;
                _pin = true;
                yield return WaitTicks(w, hz);
                foreach (TeamId team in new[] { TeamId.Azure, TeamId.Crimson })
                {
                    SimResources.ApplyDelivery(w, team, CarryKind.Biomass, r.plant, -1, BiomassType.Plant);
                    SimResources.ApplyDelivery(w, team, CarryKind.Biomass, r.meat, -1, BiomassType.Meat);
                }
                w.ForceHatch = true;
                float h0 = Time.realtimeSinceStartup;
                while (!w.Match.Hatched && Time.realtimeSinceStartup - h0 < 20f) yield return null;
                if (!w.Match.Hatched) { Log(r.name + ": the Kaiju did not hatch; giving up"); Application.Quit(4); yield break; }
                Kaiju k = w.Kaiju;
                yield return WaitTicks(w, hz / 3);   // the banner is up, the shell has fallen
                yield return Still("kt-" + r.name + "-hatch.png");
                if (!LogRun(r.name, r.want, k, "hatch", w)) missed++;
                yield return WaitTicks(w, hz * 3);
                yield return Still("kt-" + r.name + "-3s.png");
                if (!LogRun(r.name, r.want, k, "3s", w)) missed++;
            }
            _pin = false;
            _driver.Ticked -= OnTick;
            yield return new WaitForSecondsRealtime(1f);
            Log("done missed=" + missed);
            Application.Quit(missed == 0 ? 0 : 5);
        }

        /// <summary>One log line for a still; true when the hatch reached the wanted type.</summary>
        static bool LogRun(string name, KaijuType want, Kaiju k, string still, World w)
        {
            ActorViews views = FindAnyObjectByType<ActorViews>();
            KaijuAnimator anim = views != null ? views.KaijuAnimator : null;
            bool reached = k.Type == want;
            Log("type=" + want.ToString().ToUpperInvariant() + " " + (reached ? "REACHED" : "MISSED got=" + k.Type.ToString().ToUpperInvariant()) +
                " tint=" + KaijuTypeLook.CrystalHex(k.Type) + " scale=" + F(KaijuTypeLook.Scale(k.Type)) +
                " drawn=" + (anim != null ? F(anim.Scale) + " drawnType=" + anim.DrawnType : "none") +
                " still=kt-" + name + "-" + still + ".png state=" + k.State + " plant=" + F(k.MixPlant) + " meat=" + F(k.MixMeat) +
                " maxHp=" + F(k.MaxHp) + " hp=" + F(k.Hp) + " speed=" + F(Kaiju.TypeMoveSpeed(w)) + " tick=" + w.Tick);
            return reached;
        }
    }
}

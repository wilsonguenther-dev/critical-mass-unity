using System;
using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// feat/light-weapons-2 capture flag, for the integrator: <c>-weaponShots</c> (own file; installs itself after the scene
    /// loads, so no other file changes). For each of the Builder (MALLET), Healer (LANTERN) and Demolitions Expert (PRY BAR
    /// stand-in) it resets the world with actor 0 as that class, stands one enemy dummy 1.0 u in front on open floor, holds
    /// the dummy still until the swing lands, then lets it go so the shove shows, and writes close-ups from the gameplay
    /// camera's own rotation, HUD-free, to the data folder:
    ///   shot-weapon-&lt;class&gt;-0-rest.png      the prop in the hand at rest
    ///   shot-weapon-&lt;class&gt;-1-windup.png    2 ticks after the press (the shared Attack clip winding up)
    ///   shot-weapon-&lt;class&gt;-2-contact.png   the contact tick (arc trail, hit burst, push-back dust)
    ///   shot-weapon-&lt;class&gt;-3-pushback.png  0.4 s later (the dummy moved back)
    /// plus one -wide.png of the contact. Each class logs "[CM.Weapon] &lt;class&gt;: hit dmg D, shove S u, props attached N".
    /// The match clock is held; the game quits when done. Optional: -weaponShotsOnly Builder|Healer|Demolitions.
    /// </summary>
    public sealed class WeaponCapture : MonoBehaviour
    {
        public const string Flag = "-weaponShots", OnlyFlag = "-weaponShotsOnly", Prefix = "shot-weapon-";
        public const float CloseDistance = 7f;
        public const int CloseW = 1600, CloseH = 900;
        public const double DummyAhead = 1.0, PushbackSeconds = 0.4;
        static readonly ClassId[] Classes = { ClassId.Builder, ClassId.Healer, ClassId.Demolitions };

        SimDriverBehaviour _driver;
        readonly InputFrame _frame = SimInput.EmptyInput();
        int _dummy = -1;
        double _dummyX, _dummyZ;
        bool _holdDummy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0) return;
            var go = new GameObject("Weapon Capture");
            DontDestroyOnLoad(go);
            go.AddComponent<WeaponCapture>();
        }

        static void Log(string s) => Debug.Log("[CM.Weapon] " + s);

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        void Start() => StartCoroutine(Run());

        void OnDestroy()
        {
            SimDriverBehaviour.ScriptedHumanFrame = null;
            AutoplayArgs.ClockHeld = false;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver == null || _driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); yield break; }
                if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            string only = Arg(OnlyFlag);
            foreach (ClassId cls in Classes)
            {
                if (only != null && !string.Equals(only, cls.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                IEnumerator f = Fire(cls);
                while (f.MoveNext()) yield return f.Current;
            }
            SimDriverBehaviour.ScriptedHumanFrame = null;
            AutoplayArgs.ClockHeld = false;
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        static bool Floor(World w, double x, double z)
        {
            MapDef m = w.Map;
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= m.Width || cz >= m.Height) return false;
            return m.Cells[cz * m.Width + cx] == Cell.Floor;
        }

        static void Put(World w, int id, double x, double z)
        {
            w.X[id] = w.PrevX[id] = x;
            w.Z[id] = w.PrevZ[id] = z;
        }

        void Step(int n)
        {
            World w = _driver.World;
            AutoplayArgs.ClockHeld = true;
            _driver.TimeScale = 0;
            for (int t = 0; t < n; t++)
            {
                _driver.HumanAutopilot = false;
                _driver.StepTicks(1);
                if (_holdDummy && _dummy >= 0 && w.Actors[_dummy].Alive) Put(w, _dummy, _dummyX, _dummyZ);
            }
        }

        /// <summary>An open floor spot at least 9 u from the hill with floor 1 to 3 u ahead along +x, far from everyone.</summary>
        static bool Spot(World w, out double bx, out double bz)
        {
            MapDef m = w.Map;
            double mx = m.Width * 0.5, mz = m.Height * 0.5, best = -1;
            bx = w.X[0]; bz = w.Z[0];
            for (int cz = 3; cz < m.Height - 3; cz += 2)
                for (int cx = 3; cx < m.Width - 4; cx += 2)
                {
                    double x = cx + 0.5, z = cz + 0.5, hx = x - mx, hz = z - mz;
                    if (hx * hx + hz * hz < 81 || !Floor(w, x, z) || !Floor(w, x + 1, z) || !Floor(w, x + 2, z) || !Floor(w, x + 3, z)) continue;
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

        IEnumerator Fire(ClassId cls)
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = _driver.Config });
            _driver.TimeScale = 0;
            AutoplayArgs.ClockHeld = true;
            _holdDummy = false;
            _dummy = -1;
            _frame.CopyFrom(SimInput.EmptyInput());
            _frame.AimX = double.NaN;
            _frame.AimZ = double.NaN;
            SimDriverBehaviour.ScriptedHumanFrame = _frame;
            Step(3);
            string n = cls.ToString().ToLowerInvariant();
            if (!Spot(w, out double x, out double z)) Log(n + ": no open spot; staging at the spawn");
            Put(w, 0, x, z);
            w.Actors[0].Facing = 0;
            for (int i = 1; i < w.ActorCount; i++)
                if (w.Actors[i].Alive && w.Actors[i].Team != w.Actors[0].Team) { _dummy = i; break; }
            if (_dummy < 0) { Log(n + ": no enemy to use as the dummy"); yield break; }
            _dummyX = x + DummyAhead;
            _dummyZ = z;
            Put(w, _dummy, _dummyX, _dummyZ);
            w.Actors[_dummy].SpawnProtect = 0;
            w.Actors[0].SpawnProtect = 0;
            w.Actors[0].AttackCd = 0;
            _holdDummy = true;
            _frame.AimX = _dummyX;
            _frame.AimZ = _dummyZ;
            Step(4);
            Vector3 focus = SimSpace.ToWorld(x + 0.5, GroundHeight.At(w.Map, x + 0.5, z) + 0.9, z);
            yield return Shot(n + "-0-rest", focus, false);

            double hp0 = w.Actors[_dummy].Hp;
            int press = w.Tick;
            _frame.Attack = true;
            Step(1);
            _frame.Attack = false;
            Step(1);
            yield return Shot(n + "-1-windup", focus, false);
            int windup = w.Actors[0].SwingWindup;
            if (w.Tick < press + windup + 1) Step(press + windup + 1 - w.Tick);
            _holdDummy = false;           // the hit has landed: let the shove move it
            yield return Shot(n + "-2-contact", focus, true);
            int after = (int)Math.Round(PushbackSeconds * w.Config.SimHz);
            Step(after);
            yield return Shot(n + "-3-pushback", focus, false);
            double dx = w.X[_dummy] - _dummyX, dz = w.Z[_dummy] - _dummyZ;
            ActorViews views = _driver.GetComponent<ActorViews>();
            Log(n + ": hit dmg " + (hp0 - w.Actors[_dummy].Hp).ToString("0.#") + ", shove " + Math.Sqrt(dx * dx + dz * dz).ToString("0.00")
                + " u, props attached " + (views != null && views.WeaponProps != null ? views.WeaponProps.Attached : -1)
                + " (Builder and Healer models carry their own: 0 is right for them today)");
        }

        IEnumerator Shot(string name, Vector3 focus, bool wideToo)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            if (wideToo) ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, Prefix + name + "-wide.png"));
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) { Log("no camera for " + name); yield break; }
            Transform t = cam.transform;
            Vector3 p = t.position;
            Quaternion r = t.rotation;
            RenderTexture old = cam.targetTexture, prev = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(CloseW, CloseH, 24);
            Texture2D tex = null;
            try
            {
                t.position = focus - r * Vector3.forward * CloseDistance;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(CloseW, CloseH, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, CloseW, CloseH), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(GamePaths.DataDir, Prefix + name + ".png"), tex.EncodeToPNG());
            }
            catch (Exception ex) { Log("close-up " + name + " failed: " + ex.Message); }
            finally
            {
                cam.targetTexture = old;
                RenderTexture.active = prev;
                t.position = p;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null) Destroy(tex);
            }
            Log("shot " + name + " tick " + _driver.World.Tick);
        }
    }
}

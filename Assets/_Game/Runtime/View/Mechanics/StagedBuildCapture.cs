using System;
using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// feat/staged-building-1: the -buildShots capture director (its own file so the wall-damage and skills capture flags
    /// added on other branches never collide with it). In an autoplay run with -buildShots it resets the world with the
    /// local player forced to each class named by -buildClass (builder, miner or both; default both), stands him at a spot
    /// where his build is valid, waits for the camera, triggers the build and writes into the data folder:
    /// shot-build-&lt;class&gt;-pre.png, -start.png (0.08 s in), -mid.png (0.45 s in) and -end.png (1.3 s in, finished).
    /// The sim is paused (TimeScale 0) as in the -affordanceShots director; the construction plays on the wall clock, so
    /// the stills land inside the sequence. Quits the player when done unless -affordanceShots also runs.
    /// </summary>
    public sealed class StagedBuildCapture : MonoBehaviour
    {
        public const string Flag = "-buildShots", ClassFlag = "-buildClass", Prefix = "shot-build-";
        /// <summary>Seconds after the build lands that each still is taken.</summary>
        public const float StartAt = 0.08f, MidAt = 0.45f, EndAt = 1.3f;

        static int _requested = -1;
        public static bool Requested
        {
            get
            {
                if (_requested < 0) _requested = Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0 ? 1 : 0;
                return _requested == 1;
            }
        }

        SimDriverBehaviour _driver;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) { enabled = false; return; }
            StartCoroutine(Run());
        }

        static void Log(string s) => Debug.Log("[CM.BuildShots] " + s);

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            StagedBuildView.EnabledOverride = true;
            FeelGate.ReduceMotionOverride = false;
            string which = (Arg(ClassFlag) ?? "both").ToLowerInvariant();
            double scale = _driver.TimeScale;
            if (which != "miner") yield return Capture(ClassId.Builder);
            if (which != "builder") yield return Capture(ClassId.Miner);
            _driver.TimeScale = scale;
            StagedBuildView.EnabledOverride = null;
            FeelGate.ReduceMotionOverride = null;
            Log("done");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-affordanceShots") >= 0) yield break;
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, Prefix + name + ".png"));
            StagedBuildView v = StagedBuildView.Active;
            Log("shot " + name + (v != null ? " models=" + v.ModelsReady + " split=" + v.Split + " pieces=" + v.PiecesDrawn
                + " stages=" + v.StagesDrawn + " sequences=" + v.Tracker.Started : " no view"));
        }

        void Stage(ClassId cls)
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = _driver.Config });
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            _driver.StepTicks(3);
        }

        static void Put(World w, int id, double x, double z, double facing)
        {
            w.X[id] = w.PrevX[id] = x;
            w.Z[id] = w.PrevZ[id] = z;
            w.Actors[id].Facing = facing;
        }

        IEnumerator Capture(ClassId cls)
        {
            Stage(cls);
            yield return null;
            World w = _driver.World;
            string n = cls.ToString().ToLowerInvariant();
            if (cls == ClassId.Builder)
            {
                if (!FindBuildSpot(w)) { Log("builder: no valid build spot on this map"); yield break; }
                _driver.StepTicks(1);
                yield return new WaitForSecondsRealtime(1.4f);   // the camera follows actor 0 to the spot
                yield return Shot(n + "-pre");
                Actor a = w.Actors[0];
                int code = BuiltPieces.Build(w, 0, w.X[0] + Math.Cos(a.Facing) * 2, w.Z[0] + Math.Sin(a.Facing) * 2);
                Log("builder: Build returned " + code);
            }
            else
            {
                double sx = w.X[0], sz = w.Z[0];
                _driver.StepTicks(1);
                yield return new WaitForSecondsRealtime(1.4f);
                yield return Shot(n + "-pre");
                bool placed = false;
                for (int f = 0; f < 8 && !placed; f++)
                {
                    double face = f * Math.PI / 4;
                    Put(w, 0, sx, sz, face);
                    placed = Barricades.TryPlaceBarricade(w, 0, sx + Math.Cos(face) * 1.5, sz + Math.Sin(face) * 1.5);
                }
                Log("miner: barricade " + (placed ? "placed" : "refused at the spawn"));
                if (!placed) yield break;
            }
            float b0 = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(StartAt);
            yield return Shot(n + "-start");
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, MidAt - (Time.realtimeSinceStartup - b0)));
            yield return Shot(n + "-mid");
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, EndAt - (Time.realtimeSinceStartup - b0)));
            yield return Shot(n + "-end");
        }

        /// <summary>Stands actor 0 on the first floor cell and facing where the affordance scan offers a valid build.</summary>
        static bool FindBuildSpot(World w)
        {
            MapDef m = w.Map;
            var scan = new AffordanceScan();
            for (int cz = 2; cz < m.Height - 2; cz++)
                for (int cx = 2; cx < m.Width - 2; cx++)
                {
                    if (m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                    for (int f = 0; f < 4; f++)
                    {
                        double face = f * Math.PI / 2;
                        Put(w, 0, cx + 0.5, cz + 0.5, face);
                        scan.Scan(w, 0, false, double.NaN, double.NaN, false);
                        AffTarget t = scan.Primary;
                        if (t.Has && t.Action == AffAction.Build && t.Valid) { Log("builder spot " + cx + "," + cz + " facing " + f); return true; }
                    }
                }
            return false;
        }
    }
}

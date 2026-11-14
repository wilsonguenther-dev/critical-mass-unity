using System;
using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// feat/staged-building-1, fix/build-capture-2: the -buildShots capture director (its own file, so the wall-damage and
    /// skills capture flags on other branches never collide with it). In an autoplay run with -buildShots it resets the
    /// world with the local player forced to the class (-buildClass builder, miner or both; default both), finds legal
    /// sites from the map data in world terms (a real bridge gap and a real ladder wall via BuiltPieces.BuildRefusal, a
    /// barricade tile at least 14 u from the spawn via Barricades.BarricadeRefusal), stands him there facing the site in
    /// sim terms (never from the screen or the camera heading), fills the team bank, builds, and logs ACCEPTED or REFUSED
    /// (a refusal retries at a second site; FAILED if none accepts). Only after ACCEPTED does it shoot, with a temporary
    /// camera framed close on the site (the gameplay camera's own rotation, pulled in): shot-build-&lt;name&gt;-pre.png before
    /// the build, -start (0.08 s), -mid (0.45 s), -end (1.3 s), plus -mid-wide and -end-wide from the gameplay camera.
    /// Names: builder-bridge, builder-ladder, miner. The sim is paused (TimeScale 0); the construction plays on the wall
    /// clock. Quits the player when done unless -affordanceShots also runs.
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
            if (which != "miner")
            {
                yield return CaptureBuild(BuiltPieces.Bridge, "builder-bridge");
                yield return CaptureBuild(BuiltPieces.Ladder, "builder-ladder");
            }
            if (which != "builder") yield return CaptureBarricade();
            CloseOff();
            _driver.TimeScale = scale;
            StagedBuildView.EnabledOverride = null;
            FeelGate.ReduceMotionOverride = null;
            Log("done");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-affordanceShots") >= 0) yield break;
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        UnityEngine.Camera _close;

        /// <summary>
        /// A temporary camera framed close on the site: the gameplay camera's own rotation (whatever the heading is, the
        /// creator's 135 or the classic one), pulled in to dist units from the site. It draws over the gameplay camera
        /// (higher depth), so the screenshot shows it; the HUD overlay stays on top. Off for the wide stills.
        /// </summary>
        void FrameClose(Vector3 site, float dist)
        {
            UnityEngine.Camera main = UnityEngine.Camera.main;
            if (main == null || main == _close) { Log("no gameplay camera to copy; stills use the gameplay framing"); return; }
            if (_close == null)
            {
                var go = new GameObject("BuildShots Close Camera");
                _close = go.AddComponent<UnityEngine.Camera>();
            }
            _close.CopyFrom(main);
            _close.depth = main.depth + 10f;
            Quaternion rot = main.transform.rotation;
            _close.transform.SetPositionAndRotation(site - rot * Vector3.forward * dist, rot);
            _close.enabled = true;
        }

        void CloseOff()
        {
            if (_close != null) _close.enabled = false;
        }

        void OnDestroy()
        {
            if (_close != null) Destroy(_close.gameObject);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, Prefix + name + ".png"));
            StagedBuildView v = StagedBuildView.Active;
            Log("shot " + name + (v != null ? " models=" + v.ModelsReady + " split=" + v.Split + " pieces=" + v.PiecesDrawn
                + " stages=" + v.StagesDrawn + " sequences=" + v.Tracker.Started : " no view"));
        }

        /// <summary>Close stills at start, mid and end of the sequence (from the moment the build was accepted), plus wide ones.</summary>
        IEnumerator Sequence(string n, Vector3 site, float dist)
        {
            float b0 = Time.realtimeSinceStartup;
            FrameClose(site, dist);
            yield return new WaitForSecondsRealtime(StartAt);
            yield return Shot(n + "-start");
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, MidAt - (Time.realtimeSinceStartup - b0)));
            yield return Shot(n + "-mid");
            CloseOff();
            yield return null;
            yield return Shot(n + "-mid-wide");
            FrameClose(site, dist);
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, EndAt - (Time.realtimeSinceStartup - b0)));
            yield return Shot(n + "-end");
            CloseOff();
            yield return null;
            yield return Shot(n + "-end-wide");
        }

        void Stage(ClassId cls)
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = _driver.Config });
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            _driver.StepTicks(3);
            // fixture: the terrain-change gap (BuiltPieces.GapOk) is long past, as after a long match
            w.WallChangeTick = w.Tick - 1000000;
        }

        static void Put(World w, int id, double x, double z, double facing)
        {
            w.X[id] = w.PrevX[id] = x;
            w.Z[id] = w.PrevZ[id] = z;
            w.Actors[id].Facing = facing;
        }

        /// <summary>Sim facing (radians, x = cos, z = sin, as the sim reads it) for a cardinal step.</summary>
        static double FacingOf(int dx, int dz) => Math.Atan2(dz, dx);

        static readonly int[] Dx = { 1, -1, 0, 0 }, Dz = { 0, 0, 1, -1 };

        struct Site { public int Cx, Cz, Dx, Dz; public double Px, Pz; }

        /// <summary>Gives the Builder's team enough material for any build (fixture; the bank is start + delivered - spent + wood).</summary>
        static void FillBank(World w, TeamId team)
        {
            if (w.Builder == null || w.Builder.Spent == null) return;
            w.Builder.Spent[(int)team] = Math.Min(w.Builder.Spent[(int)team], -20.0);
        }

        /// <summary>
        /// Up to two legal build sites of a kind from the map data: a floor cell and a cardinal direction where BuiltPieces
        /// accepts a build (BuildRefusal 0) whose plan is that kind. Px, Pz is the centre of the planned cells.
        /// </summary>
        static int FindSites(World w, int kind, Site[] outSites)
        {
            MapDef m = w.Map;
            int found = 0;
            for (int cz = 1; cz < m.Height - 1 && found < outSites.Length; cz++)
                for (int cx = 1; cx < m.Width - 1 && found < outSites.Length; cx++)
                {
                    if (m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                    for (int d = 0; d < 4 && found < outSites.Length; d++)
                    {
                        Put(w, 0, cx + 0.5, cz + 0.5, FacingOf(Dx[d], Dz[d]));
                        if (BuiltPieces.BuildRefusal(w, 0, Dx[d], Dz[d]) != 0 || BuiltPieces.LastPlanKind != kind) continue;
                        double px = 0, pz = 0;
                        int n = BuiltPieces.LastPlanCount;
                        for (int k = 0; k < n; k++) { int idx = BuiltPieces.LastPlanCell(k); px += idx % m.Width + 0.5; pz += idx / m.Width + 0.5; }
                        if (n > 0) { px /= n; pz /= n; } else { px = cx + 0.5 + Dx[d]; pz = cz + 0.5 + Dz[d]; }
                        // a second site must be somewhere else on the map
                        if (found > 0 && Math.Abs(outSites[0].Cx - cx) + Math.Abs(outSites[0].Cz - cz) < 8) continue;
                        outSites[found++] = new Site { Cx = cx, Cz = cz, Dx = Dx[d], Dz = Dz[d], Px = px, Pz = pz };
                    }
                }
            return found;
        }

        Vector3 WorldAt(World w, double x, double z) => SimSpace.ToWorld(x, CriticalMass.Runtime.GroundHeight.At(w.Map, x, z), z);

        IEnumerator CaptureBuild(int kind, string n)
        {
            Stage(ClassId.Builder);
            yield return null;
            World w = _driver.World;
            TeamId team = w.Actors[0].Team;
            FillBank(w, team);
            var sites = new Site[2];
            int count = FindSites(w, kind, sites);
            Log(n + ": " + count + " legal site(s) on the map");
            for (int i = 0; i < count; i++)
            {
                Site st = sites[i];
                Put(w, 0, st.Cx + 0.5, st.Cz + 0.5, FacingOf(st.Dx, st.Dz));
                w.Actors[0].AbilityCd = 0;
                FillBank(w, team);
                Vector3 site = WorldAt(w, st.Px, st.Pz);
                FrameClose(site, 9f);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot(n + "-pre");
                // re-stand him exactly (nothing ticks while paused, but be certain) and aim in world terms along the axis
                Put(w, 0, st.Cx + 0.5, st.Cz + 0.5, FacingOf(st.Dx, st.Dz));
                int code = BuiltPieces.Build(w, 0, st.Cx + 0.5 + st.Dx * 2.0, st.Cz + 0.5 + st.Dz * 2.0);
                bool ok = code == BuiltPieces.BuiltLadderCode || code == BuiltPieces.BuiltBridgeCode;
                Log(n + ": site " + (i + 1) + " cell " + st.Cx + "," + st.Cz + " step " + st.Dx + "," + st.Dz + " -> Build "
                    + (ok ? "ACCEPTED" : "REFUSED") + " (code " + code + ")");
                if (!ok) { CloseOff(); continue; }
                yield return Sequence(n, site, 9f);
                yield break;
            }
            Log(n + ": FAILED, no site accepted the build");
        }

        IEnumerator CaptureBarricade()
        {
            Stage(ClassId.Miner);
            yield return null;
            World w = _driver.World;
            MapDef m = w.Map;
            double sx = w.X[0], sz = w.Z[0];
            int tries = 0;
            for (int cz = 1; cz < m.Height - 1 && tries < 2; cz++)
                for (int cx = 1; cx < m.Width - 1 && tries < 2; cx++)
                {
                    // the barricade tile: floor, well away from the spawn (spawn and hill ground refuse anyway)
                    if (m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                    double tx = cx + 0.5, tz = cz + 0.5;
                    if ((tx - sx) * (tx - sx) + (tz - sz) * (tz - sz) < 14 * 14) continue;
                    for (int d = 0; d < 4 && tries < 2; d++)
                    {
                        int fx = cx - Dx[d], fz = cz - Dz[d];   // the Miner stands one cell before it, facing it
                        if (m.Cells[fz * m.Width + fx] != Cell.Floor) continue;
                        Put(w, 0, fx + 0.5, fz + 0.5, FacingOf(Dx[d], Dz[d]));
                        if (Barricades.BarricadeRefusal(w, 0, tx, tz) != 0) continue;
                        tries++;
                        Vector3 site = WorldAt(w, tx, tz);
                        FrameClose(site, 7f);
                        yield return new WaitForSecondsRealtime(0.8f);
                        yield return Shot("miner-pre");
                        Put(w, 0, fx + 0.5, fz + 0.5, FacingOf(Dx[d], Dz[d]));
                        bool ok = Barricades.TryPlaceBarricade(w, 0, tx, tz);
                        Log("miner: site " + tries + " tile " + cx + "," + cz + " -> barricade " + (ok ? "ACCEPTED" : "REFUSED"));
                        if (!ok) { CloseOff(); continue; }
                        yield return Sequence("miner", site, 7f);
                        yield break;
                    }
                }
            Log("miner: FAILED, no legal barricade tile accepted");
        }
    }
}

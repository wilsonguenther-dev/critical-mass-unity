using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Affordance
{
    /// <summary>
    /// The -affordanceShots evidence director (feat/terrain-affordances; added by TerrainAffordanceView in an autoplay run,
    /// tools/remote-playtest.sh --affordance-shots). Writes into the data folder:
    /// shot-afford-&lt;class&gt;.png and shot-afford-&lt;class&gt;-all.png (markers, the strong target, the prompt and the ghost,
    /// then the show-all view) for Miner, Builder, Farmer and Demolitions; shot-afford-stage-0..3.png and
    /// shot-afford-stage-break.png (one barricade through every damage stage with the progress bar, then its break);
    /// shot-afford-build-0..2.png (a ladder or bridge being built); shot-afford-fight.png (a 32-player fight with markers on).
    /// This is a capture fixture: it resets the world per class, stands actor 0 on the best-scoring spot the affordance scan
    /// finds and sets barricade health to stage values, exactly as the PlayMode tests stage their scenes. It never runs
    /// outside -affordanceShots, then quits the player.
    /// </summary>
    public sealed class AffordanceCapture : MonoBehaviour
    {
        public const string Prefix = "shot-afford-";
        static readonly ClassId[] Classes = { ClassId.Miner, ClassId.Builder, ClassId.Farmer, ClassId.Demolitions };

        SimDriverBehaviour _driver;
        TerrainAffordanceView _view;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _view = GetComponent<TerrainAffordanceView>();
            if (_driver == null || _view == null) { enabled = false; return; }
            StartCoroutine(Run());
        }

        static void Log(string s) => Debug.Log("[CM.Afford] " + s);

        IEnumerator Shot(string name, float settle)
        {
            yield return new WaitForSecondsRealtime(settle);
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(GamePaths.DataDir, Prefix + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log("shot " + name + " markers=" + _view.MarkersDrawn + " quads=" + _view.QuadsDrawn + " outline=" + _view.OutlineDrawn
                + " ghost=" + _view.GhostCellsDrawn + (_view.GhostValid ? " valid" : " refused") + " prompt=\"" + _view.PromptText.Replace("\n", " | ")
                + "\" bar=" + _view.BarValue.ToString("0.00") + " " + _view.BarLabel + " decals=" + _view.DecalsDrawn);
            yield return null;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            double scale = _driver.TimeScale;
            TerrainAffordanceView.CaptureForce = true;
            for (int k = 0; k < Classes.Length; k++)
            {
                ClassId cls = Classes[k];
                Stage(cls, false);
                yield return null;
                bool found = false;
                IEnumerator pick = PickSpot(cls, ok => found = ok);
                while (pick.MoveNext()) yield return pick.Current;
                Log(cls + (found ? " spot found" : " no good spot; capturing the spawn"));
                string n = cls.ToString().ToLowerInvariant();
                yield return Shot(n, 1.6f);
                TerrainAffordanceView.ShowAllForced = true;
                yield return Shot(n + "-all", 0.3f);
                TerrainAffordanceView.ShowAllForced = false;
                if (cls == ClassId.Miner) yield return DamageStrip();
                if (cls == ClassId.Builder) yield return BuildStrip();
            }
            // The 32-player fight: bots play, actor 0 included; markers stay on for him (capture force).
            Stage(ClassId.Miner, true);
            _driver.TimeScale = scale;
            float f0 = Time.realtimeSinceStartup;
            while (_driver.World.Match.Time < 40 && Time.realtimeSinceStartup - f0 < 60f) yield return new WaitForSecondsRealtime(0.5f);
            MoveToFight(_driver.World);
            yield return Shot("fight", 1.4f);
            TerrainAffordanceView.CaptureForce = false;
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        void Stage(ClassId cls, bool bots)
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = _driver.Config });
            _driver.HumanAutopilot = bots;
            _driver.TimeScale = bots ? _driver.TimeScale : 0;
            if (!bots) _driver.StepTicks(3);
        }

        static void Put(World w, int id, double x, double z, double facing)
        {
            w.X[id] = w.PrevX[id] = x;
            w.Z[id] = w.PrevZ[id] = z;
            w.Actors[id].Facing = facing;
        }

        /// <summary>Stands actor 0 on the floor cell (and facing) where the scan finds the most to show: a valid target first.</summary>
        IEnumerator PickSpot(ClassId cls, System.Action<bool> done)
        {
            World w = _driver.World;
            MapDef m = w.Map;
            var scan = new AffordanceScan();
            double bestScore = -1, bx = w.X[0], bz = w.Z[0], bf = w.Actors[0].Facing;
            int tried = 0;
            for (int cz = 2; cz < m.Height - 2; cz += 3)
            {
                for (int cx = 2; cx < m.Width - 2; cx += 3)
                {
                    if (m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                    for (int f = 0; f < 4; f++)
                    {
                        double face = f * System.Math.PI / 2;
                        Put(w, 0, cx + 0.5, cz + 0.5, face);
                        scan.Scan(w, 0, false, double.NaN, double.NaN, false);
                        double s = System.Math.Min(scan.MarkerCount, 30);
                        if (scan.Primary.Has) s += scan.Primary.Valid ? 60 : 10;
                        if (scan.Secondary.Has && scan.Secondary.Valid) s += 12;
                        for (int i = 0; i < scan.MarkerCount; i++)
                        {
                            AffAction a = scan.Markers[i].Action;
                            if (a == AffAction.BreakWall || a == AffAction.Tnt || a == AffAction.Gather || a == AffAction.GatherSource) s += 2;
                            if (scan.Markers[i].InReach) s += 1;
                        }
                        if (s > bestScore) { bestScore = s; bx = cx + 0.5; bz = cz + 0.5; bf = face; }
                        if (++tried % 24 == 0) yield return null;
                    }
                }
            }
            Put(w, 0, bx, bz, bf);
            Log(cls + " spot (" + bx + ", " + bz + ") facing " + bf.ToString("0.00") + " score " + bestScore + " after " + tried + " scans");
            done(bestScore >= 60);
        }

        IEnumerator DamageStrip()
        {
            World w = _driver.World;
            Actor a = w.Actors[0];
            double ax = w.X[0] + System.Math.Cos(a.Facing) * 1.5, az = w.Z[0] + System.Math.Sin(a.Facing) * 1.5;
            int slot = -1;
            for (int f = 0; f < 4 && slot < 0; f++)
            {
                double face = a.Facing + f * System.Math.PI / 2;
                ax = w.X[0] + System.Math.Cos(face) * 1.5;
                az = w.Z[0] + System.Math.Sin(face) * 1.5;
                if (Barricades.TryPlaceBarricade(w, 0, ax, az))
                    for (int i = 0; i < w.Barricades.Length; i++) if (w.Barricades[i].Active) slot = i;
            }
            if (slot < 0) { Log("damage strip: no barricade could be placed here"); yield break; }
            yield return new WaitForSecondsRealtime(0.8f);
            double max = w.Config.Abilities.MinerBarricade.MaxHp;
            double[] left = { 1.0, 0.8, 0.5, 0.2 };
            for (int k = 0; k < left.Length; k++)
            {
                w.Barricades[slot].Hp = max * left[k];
                _view.Reactions.LocalBarricade = slot;
                _view.Reactions.LocalBarricadeAt = Time.unscaledTimeAsDouble;
                yield return Shot("stage-" + k, k == 0 ? 0.05f : 0.07f);
            }
            w.Barricades[slot].Hp = 0;
            _driver.StepTicks(1);
            yield return Shot("stage-break", 0.12f);
        }

        IEnumerator BuildStrip()
        {
            World w = _driver.World;
            AffTarget t = _view.Scan.Primary;
            if (!t.Has || t.Action != AffAction.Build || !t.Valid) { Log("build strip: no valid build target here (" + (t.Has ? t.Verb + " " + t.Why : "none") + ")"); yield break; }
            Actor a = w.Actors[0];
            int code = BuiltPieces.Build(w, 0, w.X[0] + System.Math.Cos(a.Facing) * 2, w.Z[0] + System.Math.Sin(a.Facing) * 2);
            Log("build strip: Build returned " + code);
            yield return Shot("build-0", 0.06f);
            yield return Shot("build-1", 0.18f);
            yield return Shot("build-2", 0.4f);
        }

        /// <summary>Moves actor 0 to the densest knot of living fighters (the camera follows him).</summary>
        static void MoveToFight(World w)
        {
            int best = -1, bestN = -1;
            for (int i = 1; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                int n = 0;
                for (int j = 0; j < w.ActorCount; j++)
                {
                    if (j == i || !w.Actors[j].Alive) continue;
                    double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i];
                    if (dx * dx + dz * dz < 36) n++;
                }
                if (n > bestN) { bestN = n; best = i; }
            }
            if (best > 0 && w.Actors[0].Alive) Put(w, 0, w.X[best] + 0.8, w.Z[best] + 0.8, w.Actors[0].Facing);
            Log("fight: " + bestN + " fighters within 6 u");
        }
    }
}

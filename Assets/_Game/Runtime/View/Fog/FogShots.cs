using System;
using System.Collections;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Fog
{
    /// <summary>
    /// Batch 12 item P capture: <c>-fogShots [dir]</c>. A staged run (capture only: it freezes the clock and places
    /// fighters, as GoreShots does) that saves top-down and third-person stills with the fog on and off: you in open
    /// ground facing two enemies, one FogShots.OutsideBy outside your 12 u sight and one FogShots.InsideBy inside it, a
    /// teammate behind you whose sight makes the fog edge on that side, the minimap and the full map. One line per still:
    /// <c>[CM.FogShots] NN-name REACHED enemyDrawn=&lt;bool&gt; expected=&lt;bool&gt;</c>, then the fog's update count and
    /// cost. Installed by FogOfWar when the flag is on the command line; the flag also forces the fog on.
    /// </summary>
    public sealed class FogShots : MonoBehaviour
    {
        public const string Flag = FogConfig.ShotsFlag;
        /// <summary>The staged enemies stand this far outside and inside the 12 u sight; the teammate this far behind you.</summary>
        public const double OutsideBy = 1.0, InsideBy = 1.0, AllyBehind = 6.0, Spread = 0.25;

        static bool? _req;
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

        SimDriverBehaviour _driver;
        ActorViews _views;
        FogOfWar _fog;
        string _dir;
        int _shot, _mismatches;

        static void Log(string s) => Debug.Log("[CM.FogShots] " + s);

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _views = GetComponent<ActorViews>();
            _fog = GetComponent<FogOfWar>();
            if (_driver == null || _fog == null) { Log("no sim driver or fog on this object; giving up"); enabled = false; return; }
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, s => string.Equals(s, Flag, StringComparison.OrdinalIgnoreCase));
            _dir = i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : Path.Combine(GamePaths.DataDir, "fog-shots");
            Directory.CreateDirectory(_dir);
            StartCoroutine(Run());
        }

        static bool TitleShowing()
        {
            var t = FindAnyObjectByType<CriticalMass.Runtime.Hud.TitleScreen>();
            return t != null && t.isActiveAndEnabled;
        }

        static bool Floor(MapDef m, double x, double z)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            return cx >= 0 && cz >= 0 && cx < m.Width && cz < m.Height && m.Cells[cz * m.Width + cx] == Cell.Floor;
        }

        static void Put(World w, int id, double x, double z)
        {
            w.X[id] = w.PrevX[id] = x;
            w.Z[id] = w.PrevZ[id] = z;
        }

        static void Points(double sx, double sz, out double ox, out double oz, out double ix, out double iz, out double ax, out double az)
        {
            double ro = FogConfig.SightTeammate + OutsideBy, ri = FogConfig.SightTeammate - InsideBy;
            ox = sx + ro * Math.Cos(Math.PI + Spread); oz = sz + ro * Math.Sin(Math.PI + Spread);
            ix = sx + ri * Math.Cos(Math.PI - Spread); iz = sz + ri * Math.Sin(Math.PI - Spread);
            ax = sx + AllyBehind; az = sz;
        }

        /// <summary>Open floor for the stage: all four spots on floor, both enemies clear of your base (with teammates
        /// parked there) and of the hill.</summary>
        static bool Spot(World w, TeamId team, out double sx, out double sz)
        {
            MapDef m = w.Map;
            PadDef pad = m.Pads[(int)team];
            double baseClear = Math.Max(FogSight.BaseRadius(w, team), FogConfig.SightTeammate) + 2.0;
            double hillClear = FogConfig.SightHeldHill + 2.0;
            for (int cz = 2; cz < m.Height - 2; cz++)
                for (int cx = 2; cx < m.Width - 2; cx++)
                {
                    sx = cx + 0.5; sz = cz + 0.5;
                    Points(sx, sz, out double ox, out double oz, out double ix, out double iz, out double ax, out double az);
                    if (!Floor(m, sx, sz) || !Floor(m, ox, oz) || !Floor(m, ix, iz) || !Floor(m, ax, az)) continue;
                    if (Dist(ox, oz, pad.X, pad.Z) < baseClear || Dist(ix, iz, pad.X, pad.Z) < baseClear) continue;
                    if (Dist(ox, oz, m.Hill.X, m.Hill.Z) < hillClear || Dist(ax, az, pad.X, pad.Z) < baseClear) continue;
                    if (Dist(ox, oz, ax, az) <= FogConfig.SightTeammate + 1.0) continue;
                    return true;
                }
            sx = sz = 0;
            return false;
        }

        static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || TitleShowing())
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); Application.Quit(3); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForSecondsRealtime((float)CriticalMass.Runtime.Hud.FirstMinutes.BannerHoldSeconds + CriticalMass.Runtime.Hud.FirstMinutes.FadeSeconds);

            World w = _driver.World;
            double scale = _driver.TimeScale;
            AutoplayArgs.ClockHeld = true;
            _driver.TimeScale = 0;
            int me = LocalSeat.Actor;
            TeamId team = w.Actors[me].Team;
            int ally = -1, outE = -1, inE = -1;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (i == me || !w.Actors[i].Alive) continue;
                if (w.Actors[i].Team == team) { if (ally < 0) ally = i; }
                else if (outE < 0) outE = i;
                else if (inE < 0) inE = i;
            }
            if (outE < 0 || inE < 0 || !Spot(w, team, out double sx, out double sz))
            {
                Log("no stage (enemies " + outE + ", " + inE + "; or no open spot); giving up");
                Restore(scale);
                Application.Quit(4);
                yield break;
            }
            Points(sx, sz, out double ox, out double oz, out double ix, out double iz, out double ax, out double az);
            PadDef own = w.Map.Pads[(int)team], their = w.Map.Pads[(int)(team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure)];
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                if (i == me) Put(w, i, sx, sz);
                else if (i == ally) Put(w, i, ax, az);
                else if (i == outE) Put(w, i, ox, oz);
                else if (i == inE) Put(w, i, ix, iz);
                else if (w.Actors[i].Team == team) Put(w, i, own.X, own.Z);
                else Put(w, i, their.X, their.Z);
            }
            w.Actors[me].Facing = Math.PI;   // facing both staged enemies (third person looks along the facing)
            Log("stage at (" + F(sx) + ", " + F(sz) + "): you " + me + ", teammate " + ally + " " + F(AllyBehind) + " u behind, enemy " + outE +
                " at " + F(FogConfig.SightTeammate + OutsideBy) + " u (outside), enemy " + inE + " at " + F(FogConfig.SightTeammate - InsideBy) + " u (inside)");

            CameraView[] views = { CameraView.TopDown, CameraView.ThirdPerson };
            string[] viewNames = { "topdown", "thirdperson" };
            for (int v = 0; v < views.Length; v++)
            {
                GroundView.ForcePlay = views[v] != CameraView.TopDown;
                GroundView.ViewOverride = views[v];
                for (int f = 0; f < 2; f++)
                {
                    bool on = f == 0;
                    FogConfig.CaptureOverride = on;
                    yield return new WaitForSecondsRealtime(1.0f);   // camera settles, the fog updates and fades
                    yield return new WaitForEndOfFrame();
                    string tag = viewNames[v] + (on ? "-fogon" : "-fogoff");
                    Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                    Still(shot, tag + "-enemy-outside", Drawn(outE), !on);
                    Still(shot, tag + "-enemy-inside", Drawn(inE), true);
                    Destroy(shot);
                }
            }

            // the minimap (top-down HUD) and the full map, fog on and off: enemyDrawn = the outside enemy's marker shown
            GroundView.ForcePlay = false;
            GroundView.ViewOverride = CameraView.TopDown;
            for (int map = 0; map < 2; map++)
            {
                if (map == 1) CriticalMass.Runtime.Hud.Interact.FullMap.Show();
                for (int f = 0; f < 2; f++)
                {
                    bool on = f == 0;
                    FogConfig.CaptureOverride = on;
                    yield return new WaitForSecondsRealtime(1.0f);
                    yield return new WaitForEndOfFrame();
                    bool oldRule = CriticalMass.Runtime.Hud.Interact.MapRules.Seen(w, outE, team, w.ActorCount);
                    bool marker = FogOfWar.Active ? !FogOfWar.Hides(outE) : oldRule;
                    Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                    Still(shot, (map == 0 ? "minimap" : "fullmap") + (on ? "-fogon" : "-fogoff"), marker, !on && oldRule);
                    Destroy(shot);
                }
                if (map == 1) CriticalMass.Runtime.Hud.Interact.FullMap.Hide();
            }

            int texBytes = FogConfig.Resolution * FogConfig.Resolution;
            Log("fog updates=" + _fog.Updates + " lastUpdateMs=" + _fog.LastUpdateMs.ToString("0.000", CultureInfo.InvariantCulture) +
                " textureBytes=" + texBytes + " sources=" + _fog.Grid.SourceCount);
            Log("done: " + _shot + " stills, " + (_mismatches == 0 ? "every still as expected" : _mismatches + " NOT AS EXPECTED") + "; stills in " + _dir);
            Restore(scale);
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        bool Drawn(int id)
        {
            ModelView mv = _views != null ? _views.ModelOf(id) : null;
            return mv != null && mv.Active && !FogOfWar.Hides(id);
        }

        void Still(Texture2D shot, string name, bool drawn, bool expected)
        {
            _shot++;
            string file = _shot.ToString("00", CultureInfo.InvariantCulture) + "-" + name;
            if (shot != null) File.WriteAllBytes(Path.Combine(_dir, file + ".png"), shot.EncodeToPNG());
            if (drawn != expected) _mismatches++;
            Log(file + " REACHED enemyDrawn=" + (drawn ? "true" : "false") + " expected=" + (expected ? "true" : "false"));
        }

        void Restore(double scale)
        {
            FogConfig.CaptureOverride = null;
            GroundView.ViewOverride = null;
            GroundView.ForcePlay = false;
            _driver.TimeScale = scale;
            AutoplayArgs.ClockHeld = false;
        }

        static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Affordance
{
    /// <summary>
    /// b12-jk: the -grenadeShots evidence director (added by TerrainAffordanceView when the flag is on the command line).
    /// Writes shot-grenade-NN-name.png into the data folder and one log line per shot,
    /// "[CM.GrenadeShots] NN-name REACHED ringRadius=.. fuseLeft=.. ...":
    /// 01-aim (a Demolitions player aiming: the arc and the landing ring), 02-midfuse (his own bomb half way through the
    /// fuse), 03-lastthird (the fast blink near the blast), 04-enemy-inside (an enemy bomb with him inside the ring: the
    /// warning colour and the "!"), then quits. A capture fixture: it resets the world, stops the clock (TimeScale 0) and
    /// stages Bomb slots and the aim exactly as the tests stage their scenes. It never runs without the flag.
    /// </summary>
    public sealed class GrenadeShots : MonoBehaviour
    {
        public const string Flag = "-grenadeShots";
        public const string Prefix = "shot-grenade-";

        SimDriverBehaviour _driver;
        TerrainAffordanceView _view;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _view = GetComponent<TerrainAffordanceView>();
            if (_driver == null || _view == null) { enabled = false; return; }
            StartCoroutine(Run());
        }

        static void Log(string s) => Debug.Log("[CM.GrenadeShots] " + s);

        static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        IEnumerator Shot(string name, float settle)
        {
            yield return new WaitForSecondsRealtime(settle);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, Prefix + name + ".png"));
            GrenadeArcView g = _view.Grenade;
            Log(name + " REACHED ringRadius=" + F(g.RingRadius >= 0 ? g.RingRadius : g.AimRingRadius) + " fuseLeft=" + (g.FuseLeft >= 0 ? F(g.FuseLeft) : "-")
                + " blinkPeriod=" + (g.BlinkPeriod >= 0 ? F(g.BlinkPeriod) + "s" : "-") + " blinkOn=" + (g.BlinkOn ? 1 : 0)
                + " arcPoints=" + g.ArcPointsDrawn + " aimRing=" + F(g.AimRingRadius) + " liveRings=" + g.LiveRings + " warn=" + g.WarnRings
                + " bang=" + g.Bangs + " mineRings=" + g.MineRings + " quads=" + _view.QuadsDrawn);
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
            TerrainAffordanceView.CaptureForce = true;
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Demolitions, Config = _driver.Config });
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            _driver.StepTicks(3);
            yield return null;
            OpenSpot(w);
            DemolitionsTntConfig cfg = w.Config.Abilities.DemolitionsTnt;
            Actor me = w.Actors[0];
            double ax = w.X[0] + Math.Cos(me.Facing) * cfg.ThrowRange * 0.8, az = w.Z[0] + Math.Sin(me.Facing) * cfg.ThrowRange * 0.8;
            TerrainAffordanceView.AimForced = true;
            TerrainAffordanceView.AimForcedX = ax;
            TerrainAffordanceView.AimForcedZ = az;
            w.Demolitions.TntReadyTick[0] = 0;
            yield return Shot("01-aim", 1.2f);

            // His own bomb on the ground, half way through the fuse, then in the last third.
            GrenadeRead.Landing(w.X[0], w.Z[0], ax, az, me.Facing, cfg, out double lx, out double lz);
            Stage(w, 0, 0, me.Team, lx, lz, 0.5);
            yield return Shot("02-midfuse", 0.4f);
            Stage(w, 0, 0, me.Team, lx, lz, 0.2);
            yield return Shot("03-lastthird", 0.4f);

            // An enemy's bomb with him inside the ring: the warning colour and the "!".
            Clear(w);
            int enemy = -1;
            for (int i = 1; i < w.ActorCount && enemy < 0; i++) if (w.Actors[i].Team != me.Team) enemy = i;
            Stage(w, 1, enemy >= 0 ? enemy : 1, me.Team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure, w.X[0] + 0.8, w.Z[0] + 0.5, 0.6);
            yield return Shot("04-enemy-inside", 0.4f);

            Clear(w);
            TerrainAffordanceView.AimForced = false;
            TerrainAffordanceView.CaptureForce = false;
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        /// <summary>Stands actor 0 on the floor cell nearest the map centre with open floor ahead (a clean frame for the arc).</summary>
        static void OpenSpot(World w)
        {
            MapDef m = w.Map;
            int bx = -1, bz = -1;
            double best = double.MaxValue;
            for (int cz = 3; cz < m.Height - 3; cz++)
            for (int cx = 3; cx < m.Width - 7; cx++)
            {
                bool open = true;
                for (int k = 0; k <= 5 && open; k++) open = m.Cells[cz * m.Width + cx + k] == Cell.Floor;
                if (!open) continue;
                double dx = cx - m.Width * 0.5, dz = cz - m.Height * 0.5, d = dx * dx + dz * dz;
                if (d < best) { best = d; bx = cx; bz = cz; }
            }
            if (bx < 0) return;
            w.X[0] = w.PrevX[0] = bx + 0.5;
            w.Z[0] = w.PrevZ[0] = bz + 0.5;
            w.Actors[0].Facing = 0;
            w.Actors[0].SpawnProtect = 0;
            Log("spot (" + (bx + 0.5) + ", " + (bz + 0.5) + ")");
        }

        static void Clear(World w)
        {
            Bomb[] bs = w.Demolitions.Bombs;
            for (int k = 0; k < bs.Length; k++) bs[k].Active = false;
        }

        /// <summary>A landed bomb in slot k at (x, z) with fuseLeft (0..1) of its fuse to go; the clock is stopped, so it never blows.</summary>
        static void Stage(World w, int slot, int owner, TeamId team, double x, double z, double fuseLeft)
        {
            DemolitionsTntConfig cfg = w.Config.Abilities.DemolitionsTnt;
            int fuse = Durations.T30(w.Config, cfg.FuseTicks), flight = Durations.T30(w.Config, cfg.FlightTicks);
            Bomb b = w.Demolitions.Bombs[slot];
            b.Active = true;
            b.Owner = owner;
            b.Team = team;
            b.X = x;
            b.Z = z;
            b.FromX = w.X[0];
            b.FromZ = w.Z[0];
            b.ThrownTick = w.Tick - (int)Math.Round(fuse * (1 - fuseLeft));
            b.BlastTick = b.ThrownTick + fuse;
            b.LandTick = Math.Min(b.BlastTick, b.ThrownTick + flight);
            b.Landed = w.Tick >= b.LandTick;
        }
    }
}

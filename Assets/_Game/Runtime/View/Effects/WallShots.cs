using System;
using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.B13;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// CM b13 item B capture (`-wallShots`, Air list step 3): stages the Builder walls in a 16v16 seed 1337 world with the clock
    /// stopped and saves one still per case to GamePaths.DataDir/shot-wall-*.png, logging `[CM.WallShots] name REACHED/NOT
    /// REACHED` with the measured state. The hut tier is forced through the staged world's config (BuildConfig.TierAtHutLevel),
    /// never through the hut rule's code. Added by WallView when the flag is on the command line; quits when done.
    /// </summary>
    public sealed class WallShots : MonoBehaviour
    {
        public const string Flag = "-wallShots";
        public const string Prefix = "shot-wall-";

        SimDriverBehaviour _driver;

        public static bool Wanted => Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) { enabled = false; return; }
            StartCoroutine(Run());
        }

        static void Log(string s) => Debug.Log("[CM.WallShots] " + s);

        IEnumerator Shot(string name, bool reached, string detail)
        {
            yield return new WaitForSecondsRealtime(0.6f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, Prefix + name + ".png"));
            Log(name + (reached ? " REACHED " : " NOT REACHED ") + detail + " ghostCells=" + WallView.GhostCells + " ghostOk=" + (WallView.GhostOkShown ? 1 : 0)
                + " word=" + BuilderWalls.Word(WallView.GhostWhyShown) + " caps=" + WallView.CapsDrawn + " cracks=" + WallView.CracksDrawn + " warn=" + WallView.WarnDrawn);
            yield return null;
        }

        static void Aim(World w, double x, double z)
        {
            w.Inputs[0].AimX = x;
            w.Inputs[0].AimZ = z;
            w.HasAim[0] = 1;
        }

        static void Tier(World w, int tier)
        {
            int[] t = w.Config.Depth.Build.TierAtHutLevel;
            for (int k = 0; k < t.Length; k++) t[k] = tier;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = _driver.Config });
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            _driver.StepTicks(3);
            yield return null;
            if (!BuilderWalls.On(w)) { Log("rule off in this config (Build.On false or Classic); nothing to stage"); Application.Quit(0); yield break; }
            OpenSpot(w, out double sx, out double sz);
            Actor me = w.Actors[0];
            me.Cls = ClassId.Builder;
            w.Walls.Tool[0] = 1;   // b13 r3: a fresh Builder starts on the DIG tool; the shots stage the pieces
            w.Walls.Credit[(int)me.Team] = 99;
            Tier(w, 0);

            Aim(w, sx + 2.5, sz);
            yield return Shot("01-ghost-ok", BuilderWalls.Ghost(w, 0, out _, out _, out _, out _, out _, out _, out bool ok) && ok, "");
            w.Walls.Credit[(int)me.Team] = 0;
            int stock = Sources.Stock(w, me.Team, Sources.StockWood);
            Sources.SpendUpTo(w, me.Team, Sources.StockWood, stock);
            yield return Shot("02-ghost-refused", BuilderWalls.Ghost(w, 0, out _, out _, out _, out _, out _, out _, out ok) && !ok, "why=" + BuilderWalls.GhostWhy(w, 0));
            w.Walls.Credit[(int)me.Team] = 99;

            int code = BuilderWalls.Build(w, 0, sx + 2.5, sz);
            yield return Shot("03-short-wall", code == BuilderWalls.Built, "code=" + code);

            Tier(w, 1);
            w.Walls.Tall[0] = 1;
            w.Walls.ReadyTick[0] = 0;
            code = BuilderWalls.Build(w, 0, sx - 2.5, sz);
            yield return Shot("04-tall-wall-tier1", code == BuilderWalls.Built, "code=" + code);

            Tier(w, 2);
            w.Walls.ReadyTick[0] = 0;
            code = BuilderWalls.Build(w, 0, sx, sz + 2.8);
            yield return Shot("05-five-cell-wall-tier2", code == BuilderWalls.Built, "code=" + code + " cells=" + BuilderWalls.OwnedCells(w, 0));

            int hx = JsMath.FloorToInt(sx), hz = JsMath.FloorToInt(sz - 2.5);
            MapGrid.SetCell(w, hx, hz, Cell.Void);
            w.Terraform.TeamReadyTick[(int)me.Team] = 0;
            w.WallChangeTick = -100000;
            w.Walls.Piece[0] = (byte)BuildPiece.Block;
            w.Walls.ReadyTick[0] = 0;
            Aim(w, hx + 0.5, hz + 0.5);
            yield return Shot("06a-block-ghost-on-crater", BuilderWalls.Ghost(w, 0, out _, out _, out _, out _, out _, out _, out ok) && BuilderWalls.GhostFill(w, 0), "ok=" + (ok ? 1 : 0));
            code = BuilderWalls.Build(w, 0, hx + 0.5, hz + 0.5);
            yield return Shot("06b-block-filled-crater", code == BuilderWalls.Filled && w.Map.Cells[hz * w.Map.Width + hx] == Cell.Floor, "code=" + code);

            // A fighter mid-jump over the short wall (cell sx+2.5): an ally put beside it with the jump flag; steps until airborne over it.
            int ally = 1;
            w.X[ally] = w.PrevX[ally] = sx + 1.3;
            w.Z[ally] = w.PrevZ[ally] = sz + 0.5;
            w.Actors[ally].Alive = true;
            w.Actors[ally].SpawnProtect = 0;
            bool air = false;
            for (int t = 0; t < 12 && !air; t++)
            {
                w.Inputs[ally].Jump = true;
                w.Inputs[ally].MoveX = 1;
                w.Inputs[ally].MoveZ = 0;
                _driver.StepTicks(1);
                air = w.Actors[ally].AirLen > 0;
            }
            yield return Shot("07-fighter-jumps-short-wall", air, "airLen=" + w.Actors[ally].AirLen + " x=" + w.X[ally].ToString("0.00"));

            // A cracked wall: the first short wall cell at 30% HP.
            bool cracked = false;
            for (int s = 0; s < w.Barricades.Length && !cracked; s++)
            {
                if (BuilderWalls.SlotKind(w, s) != BuilderWalls.KindShort) continue;
                w.Barricades[s].Hp = BuilderWalls.SlotMaxHp(w, s) * 0.3;
                cracked = true;
            }
            yield return Shot("08-cracked-wall", cracked, "");

            // A fighter on built stairs: the Builder's Space ladder toward the nearest wall run (BuiltPieces, unchanged).
            bool stairs = false;
            for (int k = 0; k < 4 && !stairs; k++)
            {
                double ax = w.X[0] + (k == 0 ? 2 : k == 1 ? -2 : 0), az = w.Z[0] + (k == 2 ? 2 : k == 3 ? -2 : 0);
                if (!BuiltPieces.CanBuild(w, 0, ax, az)) continue;
                int c = BuiltPieces.Build(w, 0, ax, az);
                stairs = c == BuiltPieces.BuiltLadderCode || c == BuiltPieces.BuiltBridgeCode;
            }
            yield return Shot("09-fighter-on-built-stairs", stairs, "(a ladder from where he stands; NOT REACHED = no wall run within reach of the staged spot)");

            Tier(w, 0);
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        static void OpenSpot(World w, out double x, out double z)
        {
            MapDef m = w.Map;
            int bx = -1, bz = -1;
            double best = double.MaxValue;
            for (int cz = 6; cz < m.Height - 6; cz++)
            for (int cx = 6; cx < m.Width - 6; cx++)
            {
                if (BuilderWalls.CellRefusal(w, cx, cz) != 0) continue;
                bool open = true;
                for (int dz = -4; dz <= 4 && open; dz++)
                for (int dx = -4; dx <= 4 && open; dx++)
                    open = m.Cells[(cz + dz) * m.Width + cx + dx] == Cell.Floor;
                if (!open) continue;
                double ddx = cx - m.Width * 0.3, ddz = cz - m.Height * 0.5, d = ddx * ddx + ddz * ddz;
                if (d < best) { best = d; bx = cx; bz = cz; }
            }
            if (bx < 0) { bx = m.Width / 2; bz = m.Height / 2; }
            x = bx + 0.5;
            z = bz + 0.5;
            w.X[0] = w.PrevX[0] = x;
            w.Z[0] = w.PrevZ[0] = z;
            w.Actors[0].Facing = 0;
            w.Actors[0].SpawnProtect = 0;
            Log("spot (" + x + ", " + z + ")");
        }
    }
}

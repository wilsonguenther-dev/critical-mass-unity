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
            yield return TerrainCases(w, sx, sz);
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        /// <summary>
        /// b14 terrain look (owner 2026-11-17: "a big hole in the dirt", "I could just see the sky", "a big brick"): a single dug
        /// pit, a 3 x 3 dig (deeper in the middle), fills back into both, three short BLOCK pieces in a row, and two cells
        /// placed out over the island edge; each from the gameplay camera and from a low side camera.
        /// </summary>
        IEnumerator TerrainCases(World w, double sx, double sz)
        {
            Actor me = w.Actors[0];
            if (!OpenArea(w, sx, sz, out int px, out int pz)) { Log("10-pit NOT REACHED (no open 7 x 7 floor area away from the first spot)"); yield break; }
            double g = GroundHeight.At(w.Map, px + 0.5, pz + 0.5);
            Stand(w, px + 0.5, pz + 3.2);
            MapGrid.SetCell(w, px + 3, pz, Cell.Void);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                MapGrid.SetCell(w, px - 1 + dx, pz + dz, Cell.Void);
            yield return Shot("10-pit-gameplay", GridChunkMeshView.EditPits >= 10, Terrain("single pit at (" + (px + 3) + "," + pz + "), 3x3 pit centred (" + (px - 1) + "," + pz + ")"));
            LowStill(Prefix + "11-pit-low-side.png", px + 3.5, g + 1.0, pz - 4.0, px + 0.5, g - 0.7, pz + 0.5);
            Log("11-pit-low-side " + (GridChunkMeshView.EditPits >= 10 ? "REACHED " : "NOT REACHED ") + Terrain("low camera south-east of the pits, 1.0 u above the ground"));

            // Fill both pits' middles (filled blocks), then three BLOCK pieces in a row beside them (short stone blocks).
            MapGrid.SetCell(w, px - 1, pz, Cell.Floor);
            MapGrid.SetCell(w, px + 3, pz, Cell.Floor);
            w.Walls.Piece[0] = (byte)BuildPiece.Block;
            w.Walls.Tall[0] = 0;
            w.Walls.Credit[(int)me.Team] = 99;
            int built = 0;
            for (int k = 0; k < 3; k++)
            {
                w.Walls.ReadyTick[0] = 0;
                if (BuilderWalls.Build(w, 0, px + 0.5 + k, pz + 2.5) == BuilderWalls.Built) built++;
            }
            yield return Shot("12-blocks-gameplay", built == 3 && GridChunkMeshView.EditShortWalls >= 3 && GridChunkMeshView.EditBlocks >= 2,
                Terrain("filled (" + (px - 1) + "," + pz + ") and (" + (px + 3) + "," + pz + "); BLOCK pieces built " + built + "/3 on row z=" + (pz + 2)));
            LowStill(Prefix + "13-blocks-low-side.png", px + 4.5, g + 0.7, pz + 6.0, px + 1.0, g + 0.2, pz + 2.0);
            Log("13-blocks-low-side " + (built == 3 ? "REACHED " : "NOT REACHED ") + Terrain("low camera north-east of the block row, 0.7 u above the ground"));

            // Two cells placed out over the island edge (earth blocks with sides and a bottom).
            if (!EdgeVoid(w, px, pz, out int ex, out int ez, out int ox, out int oz)) { Log("14-placed-edge NOT REACHED (no island edge with two void cells nearby)"); yield break; }
            MapGrid.SetCell(w, ex, ez, Cell.Floor);
            MapGrid.SetCell(w, ex + oz, ez + ox, Cell.Floor);   // its neighbour along the edge
            double ge = GroundHeight.At(w.Map, ex + 0.5, ez + 0.5);
            Stand(w, ex + 0.5 - ox * 2.0, ez + 0.5 - oz * 2.0);
            yield return Shot("14-placed-edge-gameplay", GridChunkMeshView.EditBlocks >= 4, Terrain("placed (" + ex + "," + ez + ") and (" + (ex + oz) + "," + (ez + ox) + ") out over the edge, outward (" + ox + "," + oz + ")"));
            LowStill(Prefix + "15-placed-edge-low-side.png", ex + 0.5 + ox * 4.5 + oz * 2.0, ge - 0.2, ez + 0.5 + oz * 4.5 + ox * 2.0, ex + 0.5 + oz * 0.5, ge - 0.4, ez + 0.5 + ox * 0.5);
            Log("15-placed-edge-low-side REACHED " + Terrain("low camera out over the void, 0.2 u below the ground, looking back at the blocks"));
        }

        static string Terrain(string what) => what + " pits=" + GridChunkMeshView.EditPits + " blocks=" + GridChunkMeshView.EditBlocks + " shortWalls=" + GridChunkMeshView.EditShortWalls
            + " touched=" + TerrainEdits.TouchedCount + " lastRebuildMs=" + GridChunkMeshView.LastRebuildMs.ToString("0.00");

        static void Stand(World w, double x, double z)
        {
            w.X[0] = w.PrevX[0] = x;
            w.Z[0] = w.PrevZ[0] = z;
            w.Actors[0].Facing = -Math.PI / 2;
        }

        /// <summary>A still from an extra camera (eye and target in sim coordinates), 1280 x 720, beside the other shots.</summary>
        static void LowStill(string file, double ex, double ey, double ez, double tx, double ty, double tz)
        {
            UnityEngine.Camera main = UnityEngine.Camera.main;
            var go = new GameObject("WallShots Low Camera");
            UnityEngine.Camera cam = go.AddComponent<UnityEngine.Camera>();
            if (main != null) cam.CopyFrom(main);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.transform.position = SimSpace.ToWorld(ex, ey, ez);
            cam.transform.LookAt(SimSpace.ToWorld(tx, ty, tz));
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(GamePaths.DataDir, file), tex.EncodeToPNG());
            cam.targetTexture = null;
            Destroy(rt);
            Destroy(tex);
            Destroy(go);
        }

        /// <summary>A cell whose 7 x 7 neighbourhood is untouched floor, at least 9 u from (avoidX, avoidZ).</summary>
        static bool OpenArea(World w, double avoidX, double avoidZ, out int bx, out int bz)
        {
            MapDef m = w.Map;
            bx = bz = -1;
            double best = double.MaxValue;
            for (int cz = 6; cz < m.Height - 6; cz++)
            for (int cx = 6; cx < m.Width - 6; cx++)
            {
                double ax = cx + 0.5 - avoidX, az = cz + 0.5 - avoidZ;
                if (ax * ax + az * az < 81) continue;
                bool open = true;
                for (int dz = -3; dz <= 3 && open; dz++)
                for (int dx = -4; dx <= 4 && open; dx++)
                {
                    int i = (cz + dz) * m.Width + cx + dx;
                    open = m.Cells[i] == Cell.Floor && !TerrainEdits.Touched(m, i);
                }
                if (!open || BuilderWalls.CellRefusal(w, cx, cz + 2) != 0) continue;
                double ddx = cx - m.Width * 0.3, ddz = cz - m.Height * 0.5, d = ddx * ddx + ddz * ddz;
                if (d < best) { best = d; bx = cx; bz = cz; }
            }
            return bx >= 0;
        }

        /// <summary>The void cell (ex, ez) nearest (px, pz) with floor behind it (outward = (ox, oz)) and a void neighbour
        /// along the edge that also has floor behind it, so two blocks can go side by side.</summary>
        static bool EdgeVoid(World w, int px, int pz, out int ex, out int ez, out int ox, out int oz)
        {
            MapDef m = w.Map;
            ex = ez = ox = oz = 0;
            double best = double.MaxValue;
            bool found = false;
            int[] dxs = { 1, -1, 0, 0 }, dzs = { 0, 0, 1, -1 };
            for (int cz = 2; cz < m.Height - 2; cz++)
            for (int cx = 2; cx < m.Width - 2; cx++)
            {
                if (m.Cells[cz * m.Width + cx] != Cell.Void) continue;
                for (int k = 0; k < 4; k++)
                {
                    int ux = dxs[k], uz = dzs[k];   // outward: from the floor cell behind toward this void cell
                    int bxx = cx - ux, bzz = cz - uz, nx = cx + uz, nz = cz + ux;
                    if (m.Cells[bzz * m.Width + bxx] != Cell.Floor) continue;
                    if (m.Cells[nz * m.Width + nx] != Cell.Void || m.Cells[(nz - uz) * m.Width + nx - ux] != Cell.Floor) continue;
                    if (m.Cells[(cz + uz) * m.Width + cx + ux] != Cell.Void) continue;   // open void beyond, so the sides show
                    double d = (cx - px) * (cx - px) + (cz - pz) * (cz - pz);
                    if (d < best) { best = d; ex = cx; ez = cz; ox = ux; oz = uz; found = true; }
                }
            }
            return found;
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

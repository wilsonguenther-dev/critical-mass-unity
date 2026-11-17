using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b12-jz item 2 ("the builder only builds stairs which don't even work"). The DG-24 tests only check that a piece's
    /// cells turn walkable; these walk fighters over freshly built pieces with the real MovementSystem (holding one
    /// direction, as a player does), for both teams and another class, and sweep every accepted build on the shipped
    /// Skybreak Basin map. Every case prints one evidence line.
    /// </summary>
    [TestFixture]
    public sealed class BuilderClimbTests
    {
        private const int Bld = 0;

        private static void Paint(World w, int x0, int z0, int x1, int z1, byte t)
        {
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) w.Map.Cells[z * w.Map.Width + x] = t;
        }

        private static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            a.Hp = a.MaxHp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            a.SlowTimer = 0;
            a.RespawnIn = 0;
            w.X[j] = x;
            w.Z[j] = z;
            w.Vx[j] = 0;
            w.Vz[j] = 0;
        }

        /// <summary>The BuilderTests strip: pad island x 2..8, void 9..12, middle island 13..20, a 1-cell cliff at 21, the hill island 22..34.</summary>
        private static World Strip()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 24, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = GameConfig.CreateBrowser5v5() });
            MapDef map = w.Map;
            Array.Fill(map.Cells, Cell.Void);
            Array.Clear(map.WallHp, 0, map.WallHp.Length);
            Paint(w, 2, 10, 8, 16, Cell.Floor);
            Paint(w, 13, 8, 20, 18, Cell.Floor);
            Paint(w, 21, 8, 21, 18, Cell.Wall);
            Paint(w, 22, 8, 34, 18, Cell.Floor);
            map.Shrine.X = 100.5;
            map.Shrine.Z = 60.5;
            foreach (ResourceNode nd in w.Nodes) { nd.X = -100; nd.Z = -100; }
            for (int j = 1; j < w.ActorCount; j++) { w.Actors[j].Alive = false; w.Actors[j].RespawnIn = 1e9; }
            w.Tick = 100;
            return w;
        }

        private static int FirstOf(World w, TeamId team, int skip = -1)
        {
            for (int j = 1; j < w.ActorCount; j++) if (w.Actors[j].Team == team && j != skip) return j;
            throw new InvalidOperationException();
        }

        /// <summary>Holds (mx, mz) for up to maxTicks through Movement.MovementSystem; true when actor j's x passes goalX (dx = +1).</summary>
        private static bool Walk(World w, int j, double mx, double mz, Func<World, bool> arrived, int maxTicks, out int ticks)
        {
            double dt = SimStep.Dt(w.Config);
            for (ticks = 0; ticks < maxTicks; ticks++)
            {
                if (arrived(w)) return true;
                for (int k = 0; k < w.ActorCount; k++) { w.Inputs[k].MoveX = 0; w.Inputs[k].MoveZ = 0; }
                w.Inputs[j].MoveX = mx;
                w.Inputs[j].MoveZ = mz;
                Movement.MovementSystem(w, dt);
            }
            return arrived(w);
        }

        private static int BuildAt(World w, double bx, double bz, double aimX, double aimZ)
        {
            Put(w, Bld, bx, bz);
            w.Actors[Bld].AbilityCd = 0;
            w.HasAim[Bld] = 1;
            w.Inputs[Bld].AimX = aimX;
            w.Inputs[Bld].AimZ = aimZ;
            w.WallChangeTick = World.NeverTick;
            return BuiltPieces.Build(w, Bld, aimX, aimZ);
        }

        [Test]
        public void FightersOfBothTeamsWalkUpAFreshLadderOntoTheHigherGroundAndAcrossABuiltBridge()
        {
            World w = Strip();
            var bad = new List<string>();
            // Ladder: the builder at the cliff foot on the middle island, the cliff at x 21, the higher ground from x 22.
            int code = BuildAt(w, 19.5, 13.5, 30, 13.5);
            Console.WriteLine($"[b12-jz climb] ladder build at the 1-cell cliff: code {code} (want {BuiltPieces.BuiltLadderCode})");
            Assert.AreEqual(BuiltPieces.BuiltLadderCode, code);
            int ally = FirstOf(w, TeamId.Azure), enemy = FirstOf(w, TeamId.Crimson);
            foreach (int j in new[] { Bld, ally, enemy })
            {
                Put(w, j, 18.5, 13.5);
                bool ok = Walk(w, j, 1, 0, ww => ww.X[j] >= 23.0, 150, out int t);
                Console.WriteLine($"[b12-jz climb] ladder: actor {j} {w.Actors[j].Team} {w.Actors[j].Cls} from x 18.5 to the top (x >= 23.0): {(ok ? "reached" : "STUCK")} in {t} ticks at x {w.X[j]:F2} (limit 150)");
                if (!ok) bad.Add($"ladder actor {j} stuck at x {w.X[j]:F2}");
                w.Actors[j].Alive = j == Bld;
                w.X[j] = -50;
            }
            // Bridge: the 4-cell void 9..12 from the pad island.
            w.Builder.Spent[0] = 0;
            code = BuildAt(w, 7.5, 13.5, 20, 13.5);
            Console.WriteLine($"[b12-jz climb] bridge build over the 4-cell void: code {code} (want {BuiltPieces.BuiltBridgeCode})");
            Assert.AreEqual(BuiltPieces.BuiltBridgeCode, code);
            foreach (int j in new[] { Bld, ally, enemy })
            {
                Put(w, j, 6.5, 13.5);
                bool ok = Walk(w, j, 1, 0, ww => ww.X[j] >= 14.0, 150, out int t);
                Console.WriteLine($"[b12-jz climb] bridge: actor {j} {w.Actors[j].Team} {w.Actors[j].Cls} across to x >= 14.0: {(ok ? "reached" : "STUCK")} in {t} ticks at x {w.X[j]:F2} (limit 150)");
                if (!ok) bad.Add($"bridge actor {j} stuck at x {w.X[j]:F2}");
                w.Actors[j].Alive = j == Bld;
                w.X[j] = -50;
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void ANineCellGapNowTakesABridgeAndIsWalked()
        {
            World w = Strip();
            Paint(w, 13, 8, 17, 18, Cell.Void);   // void 9..17 = 9 cells (the hill / shrine-island gap on Skybreak)
            double cap = w.Config.Abilities.BuilderPieces.BridgeMaxCells;
            int code = BuildAt(w, 7.5, 13.5, 20, 13.5);
            Console.WriteLine($"[b12-jz climb] 9-cell gap with BridgeMaxCells {cap}: code {code} (want {BuiltPieces.BuiltBridgeCode}; 1.0.868's cap of 6 gave {BuiltPieces.RefusedLength})");
            Assert.AreEqual(BuiltPieces.BuiltBridgeCode, code);
            bool ok = Walk(w, Bld, 1, 0, ww => ww.X[Bld] >= 19.0, 200, out int t);
            Console.WriteLine($"[b12-jz climb] 9-cell bridge walked to x >= 19.0: {(ok ? "reached" : "STUCK")} in {t} ticks at x {w.X[Bld]:F2} (limit 200)");
            Assert.IsTrue(ok);
        }

        /// <summary>
        /// Every build the Builder can make on the shipped map (every walkable cell, every cardinal direction): place it,
        /// hold that direction for 4 s and count arrivals at the landing. Prints per kind; asserts both kinds exist on the
        /// map and that most built pieces are walked by just holding the direction (the rest print with their cell).
        /// </summary>
        [Test]
        public void SweepEveryBuildOnSkybreakBasinAndWalkIt()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 3, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = GameConfig.CreateBrowser5v5() });
            for (int j = 1; j < w.ActorCount; j++) { w.Actors[j].Alive = false; w.Actors[j].RespawnIn = 1e9; }
            MapDef map = w.Map;
            byte[] cells0 = (byte[])map.Cells.Clone();
            int[] built = new int[3], walked = new int[3];
            var stuck = new List<string>();
            int[] dxs = { 1, -1, 0, 0 }, dzs = { 0, 0, 1, -1 };
            for (int cz = 1; cz < map.Height - 1; cz++)
            for (int cx = 1; cx < map.Width - 1; cx++)
            {
                if (!MapGrid.IsWalkable(map, cx, cz)) continue;
                for (int d = 0; d < 4; d++)
                {
                    int dx = dxs[d], dz = dzs[d];
                    if (MapGrid.IsWalkable(map, cx + dx, cz + dz) && MapGrid.IsWalkable(map, cx + 2 * dx, cz + 2 * dz)) continue;
                    w.Builder.Spent[0] = -100;   // never short of biomass in the sweep
                    double bx = cx + 0.5, bz = cz + 0.5;
                    if (!Collide.ActorFits(w, bx, bz, Collide.ActorRadius(w, Bld))) continue;
                    int code = BuildAt(w, bx, bz, bx + dx * 5, bz + dz * 5);
                    if (code == BuiltPieces.BuiltLadderCode || code == BuiltPieces.BuiltBridgeCode)
                    {
                        int kind = code == BuiltPieces.BuiltLadderCode ? BuiltPieces.Ladder : BuiltPieces.Bridge;
                        BuiltPieces.LastPlanLanding(w, Bld, dx, dz, out double lx, out double lz, out _);
                        built[kind]++;
                        Put(w, Bld, bx, bz);
                        bool ok = Walk(w, Bld, dx, dz, ww => Math.Abs(ww.X[Bld] - lx) < 0.6 && Math.Abs(ww.Z[Bld] - lz) < 0.6
                            || (dx != 0 ? (ww.X[Bld] - lx) * dx >= 0 && Math.Abs(ww.Z[Bld] - lz) < 1.5 : (ww.Z[Bld] - lz) * dz >= 0 && Math.Abs(ww.X[Bld] - lx) < 1.5), 120, out _);
                        if (ok) walked[kind]++;
                        else if (stuck.Count < 12) stuck.Add($"{(kind == BuiltPieces.Ladder ? "ladder" : "bridge")} from ({cx},{cz}) dir ({dx},{dz}) stuck at ({w.X[Bld]:F2},{w.Z[Bld]:F2}), landing ({lx},{lz})");
                    }
                    // Undo: the map, the pieces, the bank.
                    Array.Copy(cells0, map.Cells, cells0.Length);
                    foreach (BuiltPiece p in w.Builder.Pieces) p.Active = false;
                    w.Builder.Spent[0] = 0;
                }
            }
            Console.WriteLine($"[b12-jz climb] Skybreak sweep: ladders built {built[BuiltPieces.Ladder]} walked by holding the direction {walked[BuiltPieces.Ladder]}; " +
                              $"bridges built {built[BuiltPieces.Bridge]} walked {walked[BuiltPieces.Bridge]} (BridgeMaxCells {w.Config.Abilities.BuilderPieces.BridgeMaxCells})");
            foreach (string s in stuck) Console.WriteLine("[b12-jz climb]   " + s);
            Assert.Greater(built[BuiltPieces.Ladder], 0, "a ladder can be built somewhere on the map");
            Assert.Greater(built[BuiltPieces.Bridge], 0, "a bridge can be built somewhere on the map");
            int all = built[1] + built[2], ok2 = walked[1] + walked[2];
            Assert.GreaterOrEqual(ok2, all * 0.8, "at least 80% of built pieces are crossed by just holding the build direction");
        }
    
        /// <summary>
        /// CM b13 B (Jeisil: "his stairs must actually work"): the first ladder the Builder can build on Skybreak Basin (seed 3,
        /// scan order) is walked UP and back DOWN by a fighter of each team by just holding the direction, and a jump started on
        /// the ladder lands on walkable ground with no fall. Prints every walk and the jump landing.
        /// </summary>
        [Test]
        public void SkybreakLadderIsWalkedBothWaysAndAJumpFromItLandsClear()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 3, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = GameConfig.CreateBrowser5v5() });
            for (int j = 1; j < w.ActorCount; j++) { w.Actors[j].Alive = false; w.Actors[j].RespawnIn = 1e9; }
            MapDef map = w.Map;
            int[] dxs = { 1, -1, 0, 0 }, dzs = { 0, 0, 1, -1 };
            int fx = -1, fz = -1, fdx = 0, fdz = 0;
            double lx = 0, lz = 0;
            for (int cz = 1; cz < map.Height - 1 && fx < 0; cz++)
            for (int cx = 1; cx < map.Width - 1 && fx < 0; cx++)
            {
                if (!MapGrid.IsWalkable(map, cx, cz)) continue;
                for (int d = 0; d < 4 && fx < 0; d++)
                {
                    int dx = dxs[d], dz = dzs[d];
                    if (MapGrid.InBounds(map, cx + dx, cz + dz) && map.Cells[(cz + dz) * map.Width + cx + dx] != Cell.Wall) continue;
                    double bx = cx + 0.5, bz = cz + 0.5;
                    if (!Collide.ActorFits(w, bx, bz, Collide.ActorRadius(w, Bld))) continue;
                    w.Builder.Spent[0] = -100;
                    if (BuildAt(w, bx, bz, bx + dx * 5, bz + dz * 5) != BuiltPieces.BuiltLadderCode) continue;
                    BuiltPieces.LastPlanLanding(w, Bld, dx, dz, out lx, out lz, out _);
                    fx = cx; fz = cz; fdx = dx; fdz = dz;
                }
            }
            Console.WriteLine($"[b13-b stairs] ladder on Skybreak from ({fx},{fz}) dir ({fdx},{fdz}) landing ({lx:F1},{lz:F1})");
            Assert.GreaterOrEqual(fx, 0, "a ladder can be built on Skybreak");
            double sx = fx + 0.5, sz = fz + 0.5;
            var bad = new List<string>();
            int ally = FirstOf(w, TeamId.Azure), enemy = FirstOf(w, TeamId.Crimson);
            foreach (int j in new[] { Bld, ally, enemy })
            {
                Put(w, j, sx, sz);
                bool up = Walk(w, j, fdx, fdz, ww => (ww.X[j] - lx) * fdx + (ww.Z[j] - lz) * fdz >= -0.1, 150, out int tUp);
                bool down = Walk(w, j, -fdx, -fdz, ww => (ww.X[j] - sx) * fdx + (ww.Z[j] - sz) * fdz <= 0.1, 150, out int tDown);
                Console.WriteLine($"[b13-b stairs] actor {j} {w.Actors[j].Team}: up {(up ? "reached" : "STUCK")} in {tUp} ticks, back down {(down ? "reached" : "STUCK")} in {tDown} ticks, ends ({w.X[j]:F2},{w.Z[j]:F2}) (limit 150 each)");
                if (!up || !down) bad.Add($"actor {j} up {up} down {down}");
                w.Actors[j].Alive = j == Bld;
                if (j != Bld) w.X[j] = -50;
            }
            // A jump started on the ladder (on its wall cell), along the build direction.
            Put(w, Bld, sx + fdx * 1.0, sz + fdz * 1.0);
            double dt = SimStep.Dt(w.Config);
            int head = w.EventHead;
            for (int t = 0; t < 45; t++)
            {
                w.Inputs[Bld].MoveX = fdx;
                w.Inputs[Bld].MoveZ = fdz;
                w.Inputs[Bld].Jump = t == 0;
                Movement.MovementSystem(w, dt);
            }
            w.Inputs[Bld].MoveX = 0; w.Inputs[Bld].MoveZ = 0; w.Inputs[Bld].Jump = false;
            int falls = 0;
            for (int i = head; i < w.EventHead; i++) if (w.Events[i % World.EventCapacity].Type == SimEventType.Fall) falls++;
            bool landed = MapGrid.IsWalkable(map, JsMath.FloorToInt(w.X[Bld]), JsMath.FloorToInt(w.Z[Bld])) || BuiltPieces.InteractBusy(w, w.X[Bld], w.Z[Bld]);
            Console.WriteLine($"[b13-b stairs] jump from the ladder: ends ({w.X[Bld]:F2},{w.Z[Bld]:F2}) airLen {w.Actors[Bld].AirLen}, on walkable ground or a piece {landed}, falls {falls} (want 0), stand-up {w.Actors[Bld].StandUp:F2}");
            if (falls != 0) bad.Add("the jump from the ladder fell");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
}
}

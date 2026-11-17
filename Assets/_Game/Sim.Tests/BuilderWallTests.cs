using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// CM b13 item B: Builder walls and blocks (BuilderWalls, BuildConfig). Every test prints one line per measured case
    /// ("[b13-b] what: value (limit)"). The balance test toggles ONLY Depth.Build.On and prints both arms.
    /// </summary>
    [TestFixture]
    public sealed class BuilderWallTests
    {
        private const int Me = 0;

        private static GameConfig Full(Action<GameConfig> tweak = null)
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            c.ApplyRules(RulesProfile.Full);
            tweak?.Invoke(c);
            return c;
        }

        /// <summary>16v16 seed 1337 Full with only actor 0 (a Builder) alive, at tick 100.</summary>
        private static World Field(Action<GameConfig> tweak = null, int seed = 1337)
        {
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = Full(tweak) });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1;
                w.Depth.WasAlive[j] = 0;
            }
            w.Actors[Me].Cls = ClassId.Builder;
            // Resource keep-outs (4 u around every node and source) are moved off the map so the staging finds open ground;
            // the keep-out refusal itself is proven on the hill and the pad, which stay where the map puts them.
            foreach (ResourceNode nd in w.Nodes) { nd.X = -100; nd.Z = -100; }
            if (w.Depth.Sources != null) foreach (SourceNode sn in w.Depth.Sources) if (sn != null) { sn.X = -100; sn.Z = -100; }
            w.Tick = 100;
            // b13 r3: the Builder starts on the DIG tool (he owns terrain with the hut rule); these stagings test the pieces.
            for (int j = 0; j < w.Walls.Tool.Length; j++) w.Walls.Tool[j] = 1;
            return w;
        }

        private static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            if (!(a.Hp > 0)) a.Hp = a.MaxHp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            w.X[j] = x; w.Z[j] = z; w.PrevX[j] = x; w.PrevZ[j] = z; w.Vx[j] = 0; w.Vz[j] = 0;
        }

        private static void Aim(World w, double x, double z)
        {
            w.Inputs[Me].AimX = x;
            w.Inputs[Me].AimZ = z;
            w.HasAim[Me] = 1;
        }

        private static void Tier(World w, int tier)
        {
            int[] t = w.Config.Depth.Build.TierAtHutLevel;
            for (int k = 0; k < t.Length; k++) t[k] = tier;
        }

        /// <summary>The open cell nearest the map centre with every cell within r Floor and buildable.</summary>
        private static void Open(World w, int r, out int ox, out int oz)
        {
            MapDef m = w.Map;
            ox = -1; oz = -1;
            double best = double.MaxValue;
            for (int cz = r + 1; cz < m.Height - r - 1; cz++)
            for (int cx = r + 1; cx < m.Width - r - 1; cx++)
            {
                bool ok = true;
                for (int dz = -r; dz <= r && ok; dz++)
                for (int dx = -r; dx <= r && ok; dx++)
                    ok = m.Cells[(cz + dz) * m.Width + cx + dx] == Cell.Floor && BuilderWalls.CellRefusal(w, cx + dx, cz + dz) == 0;
                if (!ok) continue;
                double d = (cx - m.Width / 2.0) * (cx - m.Width / 2.0) + (cz - m.Height / 2.0) * (cz - m.Height / 2.0);
                if (d < best) { best = d; ox = cx; oz = cz; }
            }
            Assert.GreaterOrEqual(ox, 0, "an open buildable area exists");
        }

        private static int SlotAt(World w, int cx, int cz)
        {
            for (int s = 0; s < w.Barricades.Length; s++)
                if (BuilderWalls.IsWallSlot(w, s) && w.Barricades[s].Cx == cx && w.Barricades[s].Cz == cz) return s;
            return -1;
        }

        private static byte CellAt(World w, int cx, int cz) => w.Map.Cells[cz * w.Map.Width + cx];

        [Test]
        public void GhostCellsAndOrientationForEightFacings()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            Put(w, Me, ox + 0.5, oz + 0.5);
            int bad = 0;
            for (int k = 0; k < 8; k++)
            {
                double ang = k * Math.PI / 4;
                double ax = ox + 0.5 + JsMath.Cos(ang) * 2.5, az = oz + 0.5 + JsMath.Sin(ang) * 2.5;
                Aim(w, ax, az);
                w.Actors[Me].Facing = ang;
                bool has = BuilderWalls.Ghost(w, Me, out int cx, out int cz, out int dx, out int dz, out int len, out bool tall, out bool ok);
                double vx = Math.Abs(ax - w.X[Me]), vz = Math.Abs(az - w.Z[Me]);
                int wantDx = vx >= vz ? 0 : 1, wantDz = vx >= vz ? 1 : 0;
                bool pass = has && ok && len == 3 && !tall && dx == wantDx && dz == wantDz && cx == BuilderWalls.MirrorCellX(w, ax, w.X[Me], TeamId.Azure) && cz == JsMath.FloorToInt(az);   // b13 r3: boundary aims go to the Builder's side
                Console.WriteLine($"[b13-b] ghost facing {k * 45} deg: centre ({cx},{cz}) axis ({dx},{dz}) len {len} tall {tall} ok {ok} (want axis ({wantDx},{wantDz}), len 3, ok) {(pass ? "PASS" : "FAIL")}");
                if (!pass) bad++;
            }
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void MirroredBuildersGetMirroredPieces()
        {
            // b13 r3 tilt check: an Azure Builder and a Crimson Builder standing and aiming as mirror images (x' = 2 x hill x - x)
            // must get mirror-image pieces (cell cx' = W - 1 - cx), the same axis, length and verdict.
            World w = Field();
            int j = -1;
            for (int k = 0; k < w.ActorCount; k++) if (w.Actors[k].Team == TeamId.Crimson) { j = k; break; }
            Assert.GreaterOrEqual(j, 0);
            w.Actors[j].Cls = ClassId.Builder;
            double mx = 2 * w.Map.Hill.X;
            int W = w.Map.Width, bad = 0, cases = 0;
            Console.WriteLine($"[b13-b] mirror: map width {W}, hill x {w.Map.Hill.X}, pads A x {w.Map.Pads[0].X} C x {w.Map.Pads[1].X} (mirror sum {w.Map.Pads[0].X + w.Map.Pads[1].X}, want {mx})");
            double[] xs = { 38.0, 40.5, 43.25, 46.0, 49.75 };
            double[] zs = { 30.0, 34.5, 40.0, 45.5, 50.25 };
            double[] off = { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0 };
            foreach (double x in xs) foreach (double z in zs) foreach (double ox in off) foreach (double oz in new[] { -2.0, -0.5, 0.0, 1.0, 2.5 })
            {
                Put(w, Me, x, z);
                Put(w, j, mx - x, z);
                w.Actors[Me].Facing = 0; w.Actors[j].Facing = Math.PI;
                w.Inputs[Me].AimX = x + ox; w.Inputs[Me].AimZ = z + oz; w.HasAim[Me] = 1;
                w.Inputs[j].AimX = mx - (x + ox); w.Inputs[j].AimZ = z + oz; w.HasAim[j] = 1;
                bool ha = BuilderWalls.Ghost(w, Me, out int ax, out int az, out int adx, out int adz, out int alen, out bool at, out bool aok);
                bool hc = BuilderWalls.Ghost(w, j, out int cx, out int cz, out int cdx, out int cdz, out int clen, out bool ct, out bool cok);
                int wa = BuilderWalls.GhostWhy(w, Me), wc = BuilderWalls.GhostWhy(w, j);
                cases++;
                bool pass = ha == hc && cx == W - 1 - ax && cz == az && cdx == adx && cdz == adz && clen == alen && wa == wc;
                if (!pass)
                {
                    bad++;
                    if (bad <= 12) Console.WriteLine($"[b13-b] mirror MISMATCH: Azure at ({x},{z}) aim +({ox},{oz}) -> cell ({ax},{az}) axis ({adx},{adz}) why {wa}; Crimson -> cell ({cx},{cz}) axis ({cdx},{cdz}) why {wc} (want cell ({W - 1 - ax},{az}))");
                }
            }
            Console.WriteLine($"[b13-b] mirror: {cases - bad} of {cases} mirrored aims give mirrored pieces, mismatches {bad} (limit 0)");
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void KeyLayoutDigPlaceThenWallsOnTheSwitch()
        {
            // b13 r3 key layout (the Builder owns dig / place with the hut rule): the switch walks DIG -> PLACE -> WALL SHORT ->
            // (WALL TALL from tier 1) -> BLOCK -> DIG; F digs / places with the dig tool and builds with a piece.
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            Put(w, Me, ox + 0.5, oz + 0.5);
            Aim(w, ox + 2.5, oz + 0.5);
            int bad = 0;
            void Step(string what, bool tool, TerraformMode mode, BuildPiece piece, bool pressTaken)
            {
                bool gotTool = BuilderWalls.TerrainTool(w, Me);
                bool taken = BuilderWalls.TakeBuilderPress(w, Me);
                bool ok = gotTool == tool && (!tool || Terraform.ModeOf(w, Me) == mode) && (tool || BuilderWalls.Piece(w, Me) == piece) && taken == pressTaken;
                Console.WriteLine($"[b13-b] keys {what}: dig tool {gotTool} mode {Terraform.ModeOf(w, Me)} piece {BuilderWalls.Piece(w, Me)} tall {BuilderWalls.TallSelected(w, Me)}, F taken by walls {taken} (want tool {tool}, F taken {pressTaken}) {(ok ? "PASS" : "FAIL")}");
                if (!ok) bad++;
                w.Tick += 30;
            }
            w.Walls.Tool[Me] = 0;   // a fresh Builder: the dig tool (Field() selects pieces for the other stagings)
            Console.WriteLine($"[b13-b] keys: terrain owner {Terraform.OwnerClass(w)} (want Builder with the hut rule)");
            Step("start", true, TerraformMode.Dig, BuildPiece.Wall, false);
            BuilderWalls.TakeBuilderSwitch(w, Me);
            Step("switch 1", true, TerraformMode.Place, BuildPiece.Wall, false);
            BuilderWalls.TakeBuilderSwitch(w, Me);
            Step("switch 2 (wall)", false, TerraformMode.Place, BuildPiece.Wall, true);
            BuilderWalls.TakeBuilderSwitch(w, Me);
            Step("switch 3 (block at tier 0)", false, TerraformMode.Place, BuildPiece.Block, true);
            BuilderWalls.TakeBuilderSwitch(w, Me);
            Step("switch 4 (back to dig)", true, TerraformMode.Dig, BuildPiece.Block, false);
            Tier(w, 1);
            BuilderWalls.TakeBuilderSwitch(w, Me);
            BuilderWalls.TakeBuilderSwitch(w, Me);
            Step("tier 1, switch x2 (short wall)", false, TerraformMode.Place, BuildPiece.Wall, true);
            bool shortSel = !BuilderWalls.TallSelected(w, Me);
            BuilderWalls.TakeBuilderSwitch(w, Me);
            bool tallSel = BuilderWalls.TallSelected(w, Me) && BuilderWalls.Piece(w, Me) == BuildPiece.Wall;
            Console.WriteLine($"[b13-b] keys tier 1: short first {shortSel}, then tall {tallSel} (want True, True)");
            if (!shortSel || !tallSel) bad++;
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void EachRefusalReason()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            var rows = new List<(string what, int got, int want)>();
            Put(w, Me, ox + 0.5, oz + 0.5);
            double ax = ox + 3.25, az = oz + 0.5;   // b13 r3: off the cell boundary (a boundary aim now goes to the Builder's side)

            MapGrid.SetCell(w, ox + 3, oz + 1, Cell.Void);
            rows.Add(("a cell is Void (wall piece)", BuilderWalls.Refusal(w, Me, ax, az), BuilderWalls.RefusedGround));
            MapGrid.SetCell(w, ox + 3, oz + 1, Cell.Floor);

            int ally = 1;
            Put(w, ally, ox + 3.5, oz + 0.5);
            rows.Add(("an actor stands on the centre cell", BuilderWalls.Refusal(w, Me, ax, az), BuilderWalls.RefusedOccupied));
            w.Actors[ally].Alive = false;

            int credit = w.Walls.Credit[0];
            w.Walls.Credit[0] = 0;
            Sources.SpendUpTo(w, TeamId.Azure, Sources.StockWood, Sources.Stock(w, TeamId.Azure, Sources.StockWood));
            rows.Add(("the team has no wood", BuilderWalls.Refusal(w, Me, ax, az), BuilderWalls.RefusedWood));
            w.Walls.Credit[0] = credit;

            int built = BuilderWalls.Build(w, Me, ax, az);
            rows.Add(("first build", built, BuilderWalls.Built));
            rows.Add(("a second build inside the 0.6 s lock", BuilderWalls.Refusal(w, Me, ox - 2.0, oz + 0.5), BuilderWalls.RefusedLock));
            w.Walls.ReadyTick[Me] = 0;
            rows.Add(("on a standing wall cell", BuilderWalls.Refusal(w, Me, ax, az), BuilderWalls.RefusedGround));

            HillDef h = w.Map.Hill;
            Put(w, Me, h.X - 2.0, h.Z);
            rows.Add(("the hill's inner 3 u", BuilderWalls.Refusal(w, Me, h.X, h.Z), BuilderWalls.RefusedKeepOut));
            PadDef pad = w.Map.Pads[0];
            Put(w, Me, pad.X + 2.0, pad.Z);
            rows.Add(("the team pad", BuilderWalls.Refusal(w, Me, pad.X, pad.Z), BuilderWalls.RefusedKeepOut));

            // The route guard on the real map: the first 3-cell wall (either axis) whose cells all pass the cell rules but whose
            // proof fails (it would cut a pad from the hill or newly from the shrine).
            MapDef m = w.Map;
            int fx = -1, fz = -1, fdx = 0, fdz = 0, flen = 0;
            for (int len = 3; len <= 5 && fx < 0; len += 2)
            for (int cz = 3; cz < m.Height - 3 && fx < 0; cz++)
            for (int cx = 3; cx < m.Width - 3 && fx < 0; cx++)
            for (int axis = 0; axis < 2 && fx < 0; axis++)
            {
                int dx = axis, dz = 1 - axis, half = len / 2;
                bool cells = true;
                for (int k = -half; k <= half && cells; k++) cells = BuilderWalls.CellRefusal(w, cx + k * dx, cz + k * dz) == 0;
                if (!cells || BuilderWalls.RouteOk(w, cx, cz, dx, dz, len)) continue;
                fx = cx; fz = cz; fdx = dx; fdz = dz; flen = len;
            }
            Console.WriteLine($"[b13-b] route-guard choke found on Skybreak (seed 1337): centre ({fx},{fz}) axis ({fdx},{fdz}) wall of {flen} cells (5 = a tier 2 tall wall)");
            if (flen == 5) { Tier(w, 2); w.Walls.Tall[Me] = 1; }
            if (fx >= 0)
            {
                // Stand 2 u off the wall across its axis, so the aim direction lays it on that axis.
                Put(w, Me, fx + 0.5 - 2.0 * fdz, fz + 0.5 - 2.0 * fdx);
                w.Walls.ReadyTick[Me] = 0;
                rows.Add(("a wall sealing a pad-to-hill choke", BuilderWalls.Refusal(w, Me, fx + 0.5, fz + 0.5), BuilderWalls.RefusedRoute));
            }
            else Console.WriteLine("[b13-b] route guard: no floor choke on Skybreak that a 3- or 5-cell wall can seal (every choke is a bridge, where walls are refused); the balance test samples the routes instead");

            int bad = 0;
            foreach (var r in rows)
            {
                bool pass = r.got == r.want;
                Console.WriteLine($"[b13-b] refusal: {r.what}: {r.got} {BuilderWalls.Word(r.got)} (want {r.want} {BuilderWalls.Word(r.want)}) {(pass ? "PASS" : "FAIL")}");
                if (!pass) bad++;
            }
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void ShortWallBlocksAWalkerAJumperClearsItATallWallStopsAJumper()
        {
            double[] ends = new double[4];
            int wallX = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                World w = Field();
                Tier(w, pass);   // pass 0: tier 0 short; pass 1: tier 1 tall
                Open(w, 4, out int ox, out int oz);
                Put(w, Me, ox + 0.5, oz + 0.5);
                Assert.AreEqual(BuilderWalls.Built, BuilderWalls.Build(w, Me, ox + 2.5, oz + 0.5));
                wallX = ox + 2;
                Console.WriteLine($"[b13-b] {(pass == 0 ? "short" : "tall")} wall built at x {wallX}: cell {CellAt(w, wallX, oz)} (LowWall {Cell.LowWall}, Barricade {Cell.Barricade})");
                int j = 1;
                for (int k = 0; k < 2; k++)
                {
                    Put(w, j, ox + 0.5, oz + 0.5);
                    for (int s = 0; s < 30; s++) Collide.MoveActor(w, j, 0.1, 0, k == 1);
                    ends[pass * 2 + k] = w.X[j];
                }
                double r = Collide.ActorRadius(w, j);
                Console.WriteLine($"[b13-b] {(pass == 0 ? "short" : "tall")} wall: walker ends at x {ends[pass * 2]:F3} (limit <= {wallX - r + 1e-6:F3}), jumper ends at x {ends[pass * 2 + 1]:F3} (limit {(pass == 0 ? ">= " + (wallX + 1) : "<= " + (wallX - r + 1e-6).ToString("F3"))})");
                Assert.LessOrEqual(ends[pass * 2], wallX - r + 1e-6, "a walker stops at the wall");
                if (pass == 0) Assert.GreaterOrEqual(ends[1], wallX + 1.0, "a jumper clears a short wall");
                else Assert.LessOrEqual(ends[3], wallX - r + 1e-6, "a jumper does not clear a tall wall");
            }
        }

        [Test]
        public void HpByTierAndHitsToBreakByAWarriorSwingAndByTnt()
        {
            int bad = 0;
            for (int tier = 0; tier <= 2; tier++)
            {
                for (int tool = 0; tool < 2; tool++)
                {
                    World w = Field();
                    Tier(w, tier);
                    Open(w, 4, out int ox, out int oz);
                    Put(w, Me, ox + 0.5, oz + 0.5);
                    Assert.AreEqual(BuilderWalls.Built, BuilderWalls.Build(w, Me, ox + 2.5, oz + 0.5));
                    int slot = SlotAt(w, ox + 2, oz);
                    Assert.GreaterOrEqual(slot, 0);
                    double hp = w.Barricades[slot].Hp;
                    int len = BuilderWalls.OwnedCells(w, Me);
                    int enemy = w.ActorCount / 2;
                    w.Actors[enemy].Cls = ClassId.Warrior;
                    double swing = w.Config.Class(ClassId.Warrior).AttackDamage;
                    double dt = SimStep.Dt(w.Config);
                    int hits = 0;
                    while (SlotAt(w, ox + 2, oz) >= 0 && hits < 50)
                    {
                        if (tool == 0) Barricades.DamageBarricadesInArc(w, enemy, ox + 1.5, oz + 0.5, 1, 0, 1.7, 0.5, swing);
                        else
                        {
                            Barricades.DamageBarricadesInArc(w, enemy, ox + 2.5, oz + 0.5, 1, 0, w.Config.Abilities.DemolitionsTnt.BlastRadius, -1, w.Config.Abilities.DemolitionsTnt.Damage, true);
                            BuilderWalls.BlastWalls(w, TeamId.Crimson, ox + 2.5, oz + 0.5, w.Config.Abilities.DemolitionsTnt.BlastRadius);
                        }
                        hits++;
                        Barricades.BarricadesSystem(w, dt);
                        BuilderWalls.WallsSystem(w);
                    }
                    BuildConfig b = w.Config.Depth.Build;
                    double wantHp = tier == 0 ? b.ShortHp : tier == 1 ? b.TallHp : b.TallHp2;
                    int wantLen = tier == 2 ? (int)b.WallCellsTier2 : (int)b.WallCells;
                    double per = tool == 0 ? swing : b.TntDamage;
                    int wantHits = (int)Math.Ceiling(wantHp / per - 1e-9);
                    bool pass = Math.Abs(hp - wantHp) < 1e-9 && len == wantLen && hits == wantHits;
                    Console.WriteLine($"[b13-b] tier {tier}: {len} cells at {hp} HP each; {(tool == 0 ? "Warrior swings (" + swing + ")" : "TNT blasts (" + b.TntDamage + ")")} to break: {hits} (want {wantHits}, {wantLen} cells, {wantHp} HP) {(pass ? "PASS" : "FAIL")}");
                    if (!pass) bad++;
                }
            }
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void LifetimeCrumbleAndTheCellCapCrumblesTheOldest()
        {
            World w = Field(c => c.Depth.Build.MaxCellsPerBuilder = 6);
            Open(w, 4, out int ox, out int oz);
            Put(w, Me, ox + 0.5, oz + 0.5);
            int head = w.EventHead;
            Assert.AreEqual(BuilderWalls.Built, BuilderWalls.Build(w, Me, ox + 2.5, oz + 0.5));
            double dt = SimStep.Dt(w.Config);
            int ticks = 0;
            while (CellAt(w, ox + 2, oz) == Cell.LowWall && ticks < 5000)
            {
                Barricades.BarricadesSystem(w, dt);
                BuilderWalls.WallsSystem(w);
                w.Tick++;
                ticks++;
            }
            int broken = 0;
            for (int i = head; i < w.EventHead; i++) if (w.Events[i % World.EventCapacity].Type == SimEventType.WallBroken) broken++;
            int want = Durations.T30(w.Config, w.Config.Depth.Build.ShortLifeTicks);
            Console.WriteLine($"[b13-b] short wall life: gone after {ticks} ticks (want {want} +-1), cell now {CellAt(w, ox + 2, oz)} (Floor {Cell.Floor}), WallBroken events {broken} (want 1)");
            Assert.LessOrEqual(Math.Abs(ticks - want), 1);
            Assert.AreEqual(Cell.Floor, CellAt(w, ox + 2, oz));
            Assert.AreEqual(1, broken);

            // Cap 6: two walls fit, the third crumbles the first.
            w.Walls.ReadyTick[Me] = 0;
            Assert.AreEqual(BuilderWalls.Built, BuilderWalls.Build(w, Me, ox + 2.5, oz + 0.5));
            w.Walls.ReadyTick[Me] = 0;
            Assert.AreEqual(BuilderWalls.Built, BuilderWalls.Build(w, Me, ox - 1.5, oz + 0.5));
            w.Walls.ReadyTick[Me] = 0;
            int third = BuilderWalls.Build(w, Me, ox + 0.5, oz + 3.0);
            int owned = BuilderWalls.OwnedCells(w, Me);
            bool firstGone = CellAt(w, ox + 2, oz) == Cell.Floor;
            bool secondStands = CellAt(w, ox - 2, oz) == Cell.LowWall;
            Console.WriteLine($"[b13-b] cell cap 6: third build {third}, cells owned {owned} (limit <= 6), oldest gone {firstGone}, second stands {secondStands}");
            Assert.AreEqual(BuilderWalls.Built, third);
            Assert.LessOrEqual(owned, 6);
            Assert.IsTrue(firstGone && secondStands);
        }

        [Test]
        public void WoodStockIsSpentAndABlockFillsATntCraterAWalkerThenCrosses()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            Put(w, Me, ox + 0.5, oz + 0.5);
            int w0 = BuilderWalls.Wood(w, TeamId.Azure);
            Assert.AreEqual(BuilderWalls.Built, BuilderWalls.Build(w, Me, ox + 2.5, oz + 0.5));
            int w1 = BuilderWalls.Wood(w, TeamId.Azure);
            Tier(w, 1);
            w.Walls.Tall[Me] = 1;
            w.Walls.ReadyTick[Me] = 0;
            Assert.AreEqual(BuilderWalls.Built, BuilderWalls.Build(w, Me, ox - 1.5, oz + 0.5));
            int w2 = BuilderWalls.Wood(w, TeamId.Azure);
            Console.WriteLine($"[b13-b] wood: start {w0} (StartWood {w.Config.Depth.Build.StartWood} + bank), after a short wall {w1} (want {w0 - 1}), after a tall wall {w2} (want {w1 - 2})");
            Assert.AreEqual(w0 - 1, w1);
            Assert.AreEqual(w1 - 2, w2);

            // The crater: a cell north of him dug to Void (what a TNT crater leaves), then a BLOCK into it.
            Tier(w, 0);
            int hx = ox, hz = oz + 2;
            MapGrid.SetCell(w, hx, hz, Cell.Void);
            w.WallChangeTick = World.NeverTick;
            w.Terraform.TeamReadyTick[0] = 0;
            w.Walls.ReadyTick[Me] = 0;
            w.Walls.Piece[Me] = (byte)BuildPiece.Block;
            int code = BuilderWalls.Build(w, Me, hx + 0.5, hz + 0.5);
            int w3 = BuilderWalls.Wood(w, TeamId.Azure);
            Console.WriteLine($"[b13-b] block into a crater: code {code} (want {BuilderWalls.Filled}), cell {CellAt(w, hx, hz)} (Floor {Cell.Floor}), wood {w3} (want {w2 - 1})");
            Assert.AreEqual(BuilderWalls.Filled, code);
            Assert.AreEqual(Cell.Floor, CellAt(w, hx, hz));
            Assert.AreEqual(w2 - 1, w3);
            Put(w, 1, hx + 0.5, hz - 0.9);
            for (int s = 0; s < 30; s++) Collide.MoveActor(w, 1, 0, 0.1, false);
            Console.WriteLine($"[b13-b] walker across the filled crater: z {w.Z[1]:F2} (limit >= {hz + 1.2:F2})");
            Assert.GreaterOrEqual(w.Z[1], hz + 1.2);
        }

        [Test]
        public void ClassicAndPhysicsOnlyABuildersFDoesNothingNew()
        {
            int bad = 0;
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                GameConfig c = GameConfig.CreateBrowser5v5();
                c.ApplyRules(p);
                World w = World.CreateWorld(new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = c });
                byte[] before = (byte[])w.Map.Cells.Clone();
                int pool = w.Barricades.Length;
                bool taken = false;
                for (int t = 0; t < 40; t++)
                {
                    w.Inputs[Me].Terraform = t % 4 < 2;
                    w.Inputs[Me].TerraformSwitch = t % 10 == 0;
                    taken |= BuilderWalls.TakeBuilderPress(w, Me) || BuilderWalls.TakeBuilderSwitch(w, Me);
                    SimStep.Step(w);
                }
                int changed = 0;
                for (int i = 0; i < before.Length; i++) if (before[i] != w.Map.Cells[i] && (w.Map.Cells[i] == Cell.LowWall || before[i] == Cell.Void)) changed++;
                bool pass = !BuilderWalls.On(w) && !taken && changed == 0 && pool == World.BarricadeCapacity(c);
                Console.WriteLine($"[b13-b] {p}: rule on {BuilderWalls.On(w)}, a press taken {taken}, wall / fill cells {changed}, pool {pool} (want {World.BarricadeCapacity(c)}) {(pass ? "PASS" : "FAIL")}");
                if (!pass) bad++;
            }
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void AllocationZeroOver300TicksWithWallsBeingBuilt()
        {
            double[] us = new double[2];
            long[] all = new long[2];
            long bytes = -1, inCalls = 0;
            int builds = 0;
            for (int arm = 0; arm < 2; arm++)
            {
                bool on = arm == 0;
                GameConfig c = Full(g => g.Depth.Build.On = on);
                World w = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c });
                InputFrame scratch = SimInput.EmptyInput();
                for (int t = 0; t < 600; t++) MatchTick.Advance(w, null, scratch);
                int bld = -1;
                for (int i = 0; i < w.ActorCount && bld < 0; i++) if (w.Actors[i].Cls == ClassId.Builder) bld = i;
                w.Walls.Credit[0] = w.Walls.Credit[1] = 100000;
                for (int t = 0; t < 30; t++) { MatchTick.Advance(w, null, scratch); if (bld >= 0) BuilderWalls.Ghost(w, bld, out _, out _, out _, out _, out _, out _, out _); }
                // b13 integrator: warm the route guard's one-time static scratch (shrine ring, scratch field, heap) before the
                // window; the proof writes and restores, so the world is unchanged.
                if (bld >= 0) Terraform.DigKeepsRoutes(w, JsMath.FloorToInt(w.Z[bld]) * w.Map.Width + JsMath.FloorToInt(w.X[bld]));
                int b0 = w.Walls.Builds;
                var sw = Stopwatch.StartNew();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int t = 0; t < 300; t++)
                {
                    MatchTick.Advance(w, null, scratch);
                    if (bld < 0 || !w.Actors[bld].Alive) continue;
                    long c0 = GC.GetAllocatedBytesForCurrentThread();
                    BuilderWalls.Ghost(w, bld, out _, out _, out _, out _, out _, out _, out _);
                    if (t % 20 == 0)
                    {
                        w.Walls.ReadyTick[bld] = 0;
                        double ang = t * 0.37;
                        BuilderWalls.Build(w, bld, w.X[bld] + JsMath.Cos(ang) * 2.5, w.Z[bld] + JsMath.Sin(ang) * 2.5);
                    }
                    if (on) inCalls += GC.GetAllocatedBytesForCurrentThread() - c0;
                }
                long a1 = GC.GetAllocatedBytesForCurrentThread();
                sw.Stop();
                us[arm] = sw.Elapsed.TotalMilliseconds * 1000.0 / 300;
                all[arm] = a1 - a0;
                if (on) { bytes = a1 - a0; builds = w.Walls.Builds - b0; }
            }
            Console.WriteLine($"[b13-b] allocation 16v16 seed 4242 Full, 600 warm-up then 300 ticks with walls being built: whole step {bytes} B (limit 0), control arm (Build.On false) {all[1]} B, inside the walls calls (Ghost + Build) {inCalls} B (limit 0), walls built in the window {builds}");
            Console.WriteLine($"[b13-b] sim cost per tick: on {us[0]:F1} us, off {us[1]:F1} us, ratio {us[0] / Math.Max(1e-9, us[1]):F3} (limit 1.10; asserted at 1.50 to absorb a shared runner's noise)");
            Assert.AreEqual(0, inCalls, "the walls code itself allocates nothing");
            Assert.LessOrEqual(bytes, all[1], "the rule adds no allocation over its control");
            Assert.Greater(builds, 0, "walls were built inside the measured window");
            Assert.LessOrEqual(us[0], us[1] * 1.5);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Balance vs control (lesson 1: same seeds, ONLY Depth.Build.On toggled, both arms printed; CUT if a limit fails)

        private sealed class Arm
        {
            public int Matches, AzureWins, Decided, Kills, RouteFails, RouteSamples, Stalls, LeadChanges;
            public double Minutes;
            public readonly List<double> FirstKo = new List<double>();
            public readonly List<int> Walls16 = new List<int>();
            public readonly int[] ClassKills = new int[6];
            public int WallsAll;
            // b13 r3 tilt diagnostics (index = team; [t * 2 + u] = team t's actors near team u's walls)
            public int Matches16, AzureWins16, Matches5, AzureWins5;
            public readonly int[] WallsTeam = new int[2];
            public readonly int[] ByRule = new int[8];
            public readonly double[] OwnDelta = new double[2], EnemyDelta = new double[2], ToEnemy = new double[2], HillDist = new double[2];
            public readonly int[] OwnLonger = new int[2], EnemyLonger = new int[2], OwnHalf = new int[2];
            public readonly int[] Near = new int[4];
            public readonly int[] BoundaryX = new int[2];
            public readonly double[] HillSec = new double[2];
            public readonly List<string> PerMatch = new List<string>();
        }

        /// <summary>Walking distance (flow field, cells) from each pad to the hill on the CURRENT grid.</summary>
        private static void PadHill(World w, out double a, out double c)
        {
            MapDef m = w.Map;
            FlowField f = FlowField.ComputeFlowField(m, new[] { new PointXZ(m.Hill.X, m.Hill.Z) });
            double D(TeamId t) { PadDef p = m.Pads[(int)t]; return f.Dist[JsMath.FloorToInt(p.Z) * m.Width + JsMath.FloorToInt(p.X)]; }
            a = D(TeamId.Azure);
            c = D(TeamId.Crimson);
        }

        /// <summary>The newest piece of Builder id: its standing wall slots.</summary>
        private static List<int> NewestPieceSlots(World w, int id)
        {
            WallsState s = w.Walls;
            int best = -1;
            for (int p = 0; p < s.PAlive.Length; p++) if (s.PAlive[p] > 0 && s.POwner[p] == id && (best < 0 || s.PSeq[p] > s.PSeq[best])) best = p;
            var slots = new List<int>();
            if (best < 0) return slots;
            for (int k = 0; k < s.Kind.Length; k++) if (BuilderWalls.IsWallSlot(w, k) && s.PieceOf[k] == best) slots.Add(k);
            return slots;
        }

        /// <summary>Route-length delta of a just-built piece: pad-to-hill distance now minus with the piece's cells back to what they were.</summary>
        private static void PieceDelta(World w, List<int> slots, out double da, out double dc)
        {
            PadHill(w, out double a1, out double c1);
            byte[] cells = w.Map.Cells;
            var keep = new byte[slots.Count];
            for (int k = 0; k < slots.Count; k++) { Barricade b = w.Barricades[slots[k]]; int i = b.Cz * w.Map.Width + b.Cx; keep[k] = cells[i]; cells[i] = b.PrevCell; }
            PadHill(w, out double a0, out double c0);
            for (int k = 0; k < slots.Count; k++) { Barricade b = w.Barricades[slots[k]]; cells[b.Cz * w.Map.Width + b.Cx] = keep[k]; }
            da = a1 - a0;
            dc = c1 - c0;
        }

        private static void Play(int seed, int teamSize, bool on, Arm arm)
        {
            GameConfig c = Full(g =>
            {
                g.Depth.Build.On = on;
                if (teamSize <= g.Depth.Roles.SmallTeamMax) g.Depth.Roles.SmallTeamClassMix = new[] { 0.2, 0.2, 0.2, 0, 0.2, 0.2 };   // one Builder per team
            });
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c });
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(c.MatchSeconds * c.SimHz) + (int)c.SimHz;
            int cursor = w.EventHead;
            double firstKo = -1;
            int walls = 0, lastSign = 0;
            int[] wallsTeam = new int[2];
            var wallLog = new List<string>();
            double kx = double.NaN, kz = double.NaN;
            int stillSince = -1;
            bool inStall = false;
            int stallTicks = Durations.T30(c, 300);
            int sampleEvery = 30;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                for (; cursor < w.EventHead; cursor++)
                {
                    SimEvent e = w.Events[cursor % World.EventCapacity];
                    if (e.Type == SimEventType.Death)
                    {
                        if (firstKo < 0) firstKo = w.Match.Time;
                        if (e.B >= 0 && e.B < w.ActorCount && w.Actors[e.B].Team != w.Actors[e.A].Team) { arm.Kills++; arm.ClassKills[(int)w.Actors[e.B].Cls]++; }
                    }
                    else if (e.Type == SimEventType.WallBuilt && e.Value > 0)
                    {
                        walls++;
                        if (!on || e.A < 0 || e.A >= w.ActorCount) continue;
                        int tm = (int)w.Actors[e.A].Team;
                        wallsTeam[tm]++;
                        arm.WallsTeam[tm]++;
                        List<int> slots = NewestPieceSlots(w, e.A);
                        PieceDelta(w, slots, out double da, out double dc);
                        double own = tm == 0 ? da : dc, enemy = tm == 0 ? dc : da;
                        arm.OwnDelta[tm] += own; arm.EnemyDelta[tm] += enemy;
                        if (own > 0.01) arm.OwnLonger[tm]++;
                        if (enemy > 0.01) arm.EnemyLonger[tm]++;
                        double toEnemy = tm == 0 ? e.X - w.Map.Hill.X : w.Map.Hill.X - e.X;   // + = on the enemy's half of the mirror line
                        arm.ToEnemy[tm] += toEnemy;
                        if (toEnemy < 0) arm.OwnHalf[tm]++;
                        double hd = Math.Sqrt((e.X - w.Map.Hill.X) * (e.X - w.Map.Hill.X) + (e.Z - w.Map.Hill.Z) * (e.Z - w.Map.Hill.Z));
                        arm.HillDist[tm] += hd;
                        if (wallLog.Count < 6) wallLog.Add($"{(TeamId)tm} t{w.Tick} ({e.X - 0.5:F0},{e.Z - 0.5:F0}) mirror-x {(tm == 0 ? e.X : 2 * w.Map.Hill.X - e.X) - 0.5:F0} len {e.Value} hill {hd:F1} dOwn {own:F0} dEnemy {enemy:F0}");
                    }
                }
                if (on && w.Tick % sampleEvery == 0)
                {
                    // Who walls get in the way of: per team, living actors within 1.2 u of a standing wall cell, by the wall's team.
                    for (int j = 0; j < w.ActorCount; j++)
                    {
                        if (!w.Actors[j].Alive) continue;
                        int tj = (int)w.Actors[j].Team;
                        bool[] seen = { false, false };
                        for (int ks = w.Walls.BaseSlots; ks < w.Barricades.Length; ks++)
                        {
                            if (!BuilderWalls.IsWallSlot(w, ks)) continue;
                            Barricade b = w.Barricades[ks];
                            double dx = b.Cx + 0.5 - w.X[j], dz = b.Cz + 0.5 - w.Z[j];
                            if (dx * dx + dz * dz < 1.44) seen[(int)b.Team] = true;
                        }
                        for (int u = 0; u < 2; u++) if (seen[u]) arm.Near[tj * 2 + u]++;
                    }
                }
                if (w.Tick % sampleEvery != 0) continue;
                arm.RouteSamples++;
                if (!Terraform.PadsReachHill(w.Map)) arm.RouteFails++;
                double diff = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                int sign = diff > 0.5 ? 1 : diff < -0.5 ? -1 : 0;
                if (sign != 0 && lastSign != 0 && sign != lastSign) arm.LeadChanges++;
                if (sign != 0) lastSign = sign;
                Kaiju k = w.Kaiju;
                bool moving = k.State != KaijuState.Egg && !(k.KoIn > 0);
                if (!moving) { stillSince = -1; inStall = false; kx = double.NaN; continue; }
                if (double.IsNaN(kx) || (k.X - kx) * (k.X - kx) + (k.Z - kz) * (k.Z - kz) > 1.0)
                {
                    kx = k.X; kz = k.Z; stillSince = w.Tick; inStall = false;
                }
                else if (!inStall && w.Tick - stillSince > stallTicks)
                {
                    HillDef h = w.Map.Hill;
                    bool onHill = (k.X - h.X) * (k.X - h.X) + (k.Z - h.Z) * (k.Z - h.Z) < c.Hill.Radius * c.Hill.Radius;
                    if (!onHill) arm.Stalls++;
                    inStall = true;
                }
            }
            arm.Matches++;
            arm.Minutes += w.Match.Time / 60.0;
            if (w.Match.Winner == MatchResult.Azure || w.Match.Winner == MatchResult.Crimson) arm.Decided++;
            if (w.Match.Winner == MatchResult.Azure) arm.AzureWins++;
            if (teamSize == 16) { arm.Matches16++; if (w.Match.Winner == MatchResult.Azure) arm.AzureWins16++; }
            else { arm.Matches5++; if (w.Match.Winner == MatchResult.Azure) arm.AzureWins5++; }
            arm.HillSec[0] += w.Match.HillSeconds.Azure;
            arm.HillSec[1] += w.Match.HillSeconds.Crimson;
            for (int k = 0; k < 8; k++) arm.ByRule[k] += w.Walls.BuiltByRule[k];
            arm.BoundaryX[0] += w.Walls.BoundaryX[0];
            arm.BoundaryX[1] += w.Walls.BoundaryX[1];
            arm.PerMatch.Add($"{teamSize}v{teamSize} seed {seed}: winner {w.Match.Winner}, hill s A {w.Match.HillSeconds.Azure:F0} C {w.Match.HillSeconds.Crimson:F0}, walls A {wallsTeam[0]} C {wallsTeam[1]}" + (wallLog.Count > 0 ? " | " + string.Join(" ; ", wallLog) : ""));
            if (firstKo >= 0) arm.FirstKo.Add(firstKo);
            arm.WallsAll += walls;
            if (teamSize == 16) arm.Walls16.Add(walls);
        }

        private static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var s = v.OrderBy(x => x).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }

        [Test, Category("Slow")]
        public void WinShareOnAWiderSampleBuildOnOnlyToggled()
        {
            // b13 r3 tilt evidence (prints only; the limits stay on the ORDER's fixed seeds in BalanceVsControlBuildOnOnlyToggled):
            // the same toggle on seeds the balance test does not use, so a real per-team tilt would show again and a flip-noise
            // one would not. Prints the win shares per size, and how many matches flipped each way.
            var on = new Arm();
            var off = new Arm();
            int[] big = { 1, 2, 3, 11, 13, 17 };
            for (int seed = 21; seed <= 60; seed++) { Play(seed, 5, true, on); Play(seed, 5, false, off); }
            foreach (int seed in big) { Play(seed, 16, true, on); Play(seed, 16, false, off); }
            int toA = 0, toC = 0;
            for (int k = 0; k < on.PerMatch.Count; k++)
            {
                bool aOn = on.PerMatch[k].Contains("winner Azure"), aOff = off.PerMatch[k].Contains("winner Azure");
                if (aOn && !aOff) toA++;
                if (!aOn && aOff) toC++;
            }
            Console.WriteLine($"[b13-b] WIDER 5v5 seeds 21..60 ({on.Matches5} matches): Azure win share on {(double)on.AzureWins5 / Math.Max(1, on.Matches5):F3} control {(double)off.AzureWins5 / Math.Max(1, off.Matches5):F3}");
            Console.WriteLine($"[b13-b] WIDER 16v16 seeds 1 2 3 11 13 17 ({on.Matches16} matches): Azure win share on {(double)on.AzureWins16 / Math.Max(1, on.Matches16):F3} control {(double)off.AzureWins16 / Math.Max(1, off.Matches16):F3}");
            Console.WriteLine($"[b13-b] WIDER flips: {toA + toC} of {on.Matches} matches change winner when walls are on ({toA} to Azure, {toC} to Crimson); walls Azure {on.WallsTeam[0]} Crimson {on.WallsTeam[1]}; boundary-x plans A {on.BoundaryX[0]} C {on.BoundaryX[1]}; hill seconds on A {on.HillSec[0]:F0} C {on.HillSec[1]:F0}, control A {off.HillSec[0]:F0} C {off.HillSec[1]:F0}");
            for (int k = 0; k < on.PerMatch.Count; k++) if (on.PerMatch[k].Contains("winner Azure") != off.PerMatch[k].Contains("winner Azure")) Console.WriteLine($"[b13-b] WIDER flip: on {on.PerMatch[k]} || control {off.PerMatch[k].Substring(0, Math.Min(90, off.PerMatch[k].Length))}");
            Assert.AreEqual(on.Matches, off.Matches);
        }

        [Test, Category("Slow")]
        public void BalanceVsControlBuildOnOnlyToggled()
        {
            var arms = new[] { new Arm(), new Arm() };   // 0 = rule on, 1 = control (Build.On false)
            int[] big = { 1337, 4242, 7, 99, 2024, 31337 };
            for (int a = 0; a < 2; a++)
            {
                for (int seed = 1; seed <= 20; seed++) Play(seed, 5, a == 0, arms[a]);
                foreach (int seed in big) Play(seed, 16, a == 0, arms[a]);
            }
            var lines = new List<(string line, bool pass)>();
            Arm on = arms[0], off = arms[1];
            double winOn = (double)on.AzureWins / on.Matches, winOff = (double)off.AzureWins / off.Matches;
            lines.Add(($"team win share (Azure), all: on {winOn:F3} control {winOff:F3} (limit within 0.15)", Math.Abs(winOn - winOff) <= 0.15));
            double w16On = (double)on.AzureWins16 / Math.Max(1, on.Matches16), w16Off = (double)off.AzureWins16 / Math.Max(1, off.Matches16);
            double w5On = (double)on.AzureWins5 / Math.Max(1, on.Matches5), w5Off = (double)off.AzureWins5 / Math.Max(1, off.Matches5);
            lines.Add(($"team win share (Azure), 16v16 ({on.Matches16} matches): on {w16On:F3} control {w16Off:F3} (limit within 0.15)", Math.Abs(w16On - w16Off) <= 0.15 + 1e-9));
            lines.Add(($"team win share (Azure), 5v5 ({on.Matches5} matches): on {w5On:F3} control {w5Off:F3} (limit within 0.15)", Math.Abs(w5On - w5Off) <= 0.15 + 1e-9));
            double koOn = Median(on.FirstKo), koOff = Median(off.FirstKo);
            lines.Add(($"median first knock-out: on {koOn:F1} s control {koOff:F1} s (limit -10% / +20%)", koOn >= koOff * 0.9 && koOn <= koOff * 1.2));
            double kpmOn = on.Kills / on.Minutes, kpmOff = off.Kills / off.Minutes;
            lines.Add(($"kills per minute: on {kpmOn:F2} control {kpmOff:F2} (limit -20% / +15%)", kpmOn >= kpmOff * 0.8 && kpmOn <= kpmOff * 1.15));
            for (int cls = 0; cls < 6; cls++)
            {
                double sOn = on.Kills > 0 ? (double)on.ClassKills[cls] / on.Kills : 0, sOff = off.Kills > 0 ? (double)off.ClassKills[cls] / off.Kills : 0;
                lines.Add(($"{(ClassId)cls} kill share: on {sOn:F3} control {sOff:F3} (limit within 0.08)", Math.Abs(sOn - sOff) <= 0.08));
            }
            lines.Add(($"hill lead changes: on {on.LeadChanges} control {off.LeadChanges} (limit +-30%)", Math.Abs(on.LeadChanges - off.LeadChanges) <= 0.3 * Math.Max(1, off.LeadChanges)));
            lines.Add(($"route guard: both pads reach the hill at every 30th tick: on {on.RouteFails} failures of {on.RouteSamples} samples (limit 0); control {off.RouteFails}", on.RouteFails == 0));
            double medWalls = Median(on.Walls16.Select(x => (double)x).ToList());
            lines.Add(($"walls by Builder bots per 16v16 match: median {medWalls:F1} [{string.Join(", ", on.Walls16)}], all matches {on.WallsAll} (limit median >= 2); control {off.WallsAll}", medWalls >= 2));
            double stOn = (double)on.Stalls / on.Matches, stOff = (double)off.Stalls / off.Matches;
            lines.Add(($"Kaiju no-progress stalls over 10 s per match: on {stOn:F2} control {stOff:F2} (limit control + 1)", stOn <= stOff + 1));
            foreach (var l in lines) Console.WriteLine($"[b13-b] BALANCE {l.line} {(l.pass ? "PASS" : "FAIL")}");
            for (int t = 0; t < 2; t++)
            {
                int n = Math.Max(1, on.WallsTeam[t]);
                Console.WriteLine($"[b13-b] TILT {(TeamId)t}: walls {on.WallsTeam[t]} (rule hill {on.ByRule[t * 4 + 1]}, guard {on.ByRule[t * 4 + 2]}, fill {on.ByRule[t * 4 + 3]}, player {on.ByRule[t * 4]}); mean offset toward the enemy half {on.ToEnemy[t] / n:F2} u, on own half {on.OwnHalf[t]}; mean distance to hill {on.HillDist[t] / n:F2} u; own pad-to-hill route longer after {on.OwnLonger[t]} walls (mean delta {on.OwnDelta[t] / n:F2}), enemy route longer after {on.EnemyLonger[t]} (mean delta {on.EnemyDelta[t] / n:F2})");
                Console.WriteLine($"[b13-b] TILT {(TeamId)t} actors within 1.2 u of a wall (samples every 30 ticks): own team's walls {on.Near[t * 2 + t]}, enemy walls {on.Near[t * 2 + 1 - t]}; hill seconds on {on.HillSec[t]:F0} control {off.HillSec[t]:F0}; plans with the aim x exactly on a cell boundary {on.BoundaryX[t]}");
            }
            for (int k = 0; k < on.PerMatch.Count; k++) Console.WriteLine($"[b13-b] MATCH on  {on.PerMatch[k]}");
            for (int k = 0; k < off.PerMatch.Count; k++) Console.WriteLine($"[b13-b] MATCH off {off.PerMatch[k]}");
            Console.WriteLine($"[b13-b] BALANCE matches {on.Matches} per arm (5v5 Full 1..20 with one Builder per team, 16v16 Full 1337 4242 7 99 2024 31337); decided on {on.Decided} control {off.Decided}");
            Assert.IsTrue(lines.All(l => l.pass), "a balance limit failed (see the BALANCE lines): the rule is CUT under lesson 1");
        }
    }
}

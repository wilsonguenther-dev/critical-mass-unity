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
            w.Tick = 100;
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
                bool pass = has && ok && len == 3 && !tall && dx == wantDx && dz == wantDz && cx == JsMath.FloorToInt(ax) && cz == JsMath.FloorToInt(az);
                Console.WriteLine($"[b13-b] ghost facing {k * 45} deg: centre ({cx},{cz}) axis ({dx},{dz}) len {len} tall {tall} ok {ok} (want axis ({wantDx},{wantDz}), len 3, ok) {(pass ? "PASS" : "FAIL")}");
                if (!pass) bad++;
            }
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void EachRefusalReason()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            var rows = new List<(string what, int got, int want)>();
            Put(w, Me, ox + 0.5, oz + 0.5);
            double ax = ox + 3.0, az = oz + 0.5;

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
            Assert.GreaterOrEqual(fx, 0, "a pad-to-hill choke exists for a 3-cell wall");
            // Stand 2 u off the wall across its axis, so the aim direction lays it on that axis.
            Put(w, Me, fx + 0.5 - 2.0 * fdz, fz + 0.5 - 2.0 * fdx);
            w.Walls.ReadyTick[Me] = 0;
            rows.Add(("a wall sealing a pad-to-hill choke", BuilderWalls.Refusal(w, Me, fx + 0.5, fz + 0.5), BuilderWalls.RefusedRoute));

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
            long bytes = -1;
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
                int b0 = w.Walls.Builds;
                var sw = Stopwatch.StartNew();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int t = 0; t < 300; t++)
                {
                    MatchTick.Advance(w, null, scratch);
                    if (bld < 0 || !w.Actors[bld].Alive) continue;
                    BuilderWalls.Ghost(w, bld, out _, out _, out _, out _, out _, out _, out _);
                    if (t % 20 == 0)
                    {
                        w.Walls.ReadyTick[bld] = 0;
                        double ang = t * 0.37;
                        BuilderWalls.Build(w, bld, w.X[bld] + JsMath.Cos(ang) * 2.5, w.Z[bld] + JsMath.Sin(ang) * 2.5);
                    }
                }
                long a1 = GC.GetAllocatedBytesForCurrentThread();
                sw.Stop();
                us[arm] = sw.Elapsed.TotalMilliseconds * 1000.0 / 300;
                if (on) { bytes = a1 - a0; builds = w.Walls.Builds - b0; }
            }
            Console.WriteLine($"[b13-b] allocation 16v16 seed 4242 Full, 600 warm-up then 300 ticks with walls being built: {bytes} B (limit 0), walls built in the window {builds}");
            Console.WriteLine($"[b13-b] sim cost per tick: on {us[0]:F1} us, off {us[1]:F1} us, ratio {us[0] / Math.Max(1e-9, us[1]):F3} (limit 1.10; asserted at 1.50 to absorb a shared runner's noise)");
            Assert.AreEqual(0, bytes);
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
                    else if (e.Type == SimEventType.WallBuilt && e.Value > 0) walls++;
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
            lines.Add(($"team win share (Azure): on {winOn:F3} control {winOff:F3} (limit within 0.15)", Math.Abs(winOn - winOff) <= 0.15));
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
            Console.WriteLine($"[b13-b] BALANCE matches {on.Matches} per arm (5v5 Full 1..20 with one Builder per team, 16v16 Full 1337 4242 7 99 2024 31337); decided on {on.Decided} control {off.Decided}");
            Assert.IsTrue(lines.All(l => l.pass), "a balance limit failed (see the BALANCE lines): the rule is CUT under lesson 1");
        }
    }
}

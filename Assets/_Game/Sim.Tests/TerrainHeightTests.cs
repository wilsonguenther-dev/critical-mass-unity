using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 terrain height (RULE CHANGE, docs/b15-terrain-height/DESIGN.md): stacked blocks and walkable pits. Staged fields drive
    /// Movement.MovementSystem directly (one call = one tick) or call the rules; every case PRINTS one line (what, value, limit).
    /// The soak and balance arms play whole bot matches with the rule on (and a mirrored, seeded set of pits and stacks, because
    /// bots never dig or stack on their own) against the same-seed control (TerrainHeight.Enabled false, nothing else).
    /// </summary>
    [TestFixture]
    public sealed class TerrainHeightTests
    {
        private const int Me = 0;

        private static void Line(string what, object value, string limit) =>
            Console.WriteLine("{\"item\":\"b15 height\",\"case\":\"" + what + "\",\"value\":\"" + value + "\",\"limit\":\"" + limit + "\"}");

        private static GameConfig Full(Action<GameConfig> tweak = null)
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            c.ApplyRules(RulesProfile.Full);
            tweak?.Invoke(c);
            return c;
        }

        /// <summary>16v16 seed 1337 Full with only actor 0 (cls) alive at tick 100, resource keep-outs moved off the map.</summary>
        private static World Field(ClassId cls = ClassId.Builder, Action<GameConfig> tweak = null)
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = Full(tweak) });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1;
                w.Depth.WasAlive[j] = 0;
            }
            w.Actors[Me].Cls = cls;
            foreach (ResourceNode nd in w.Nodes) { nd.X = -100; nd.Z = -100; }
            if (w.Depth.Sources != null) foreach (SourceNode sn in w.Depth.Sources) if (sn != null) { sn.X = -100; sn.Z = -100; }
            w.Tick = 100;
            for (int j = 0; j < w.Walls.Tool.Length; j++) w.Walls.Tool[j] = 1;
            w.Walls.Credit[0] = 100;
            return w;
        }

        private static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            if (!(a.Hp > 0)) a.Hp = a.MaxHp;
            a.SpawnProtect = 0; a.StandUp = 0; a.AirLen = 0; a.HitStunTicks = 0; a.KVx = 0; a.KVz = 0;
            w.X[j] = x; w.Z[j] = z; w.PrevX[j] = x; w.PrevZ[j] = z; w.Vx[j] = 0; w.Vz[j] = 0;
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
                    ok = m.Cells[(cz + dz) * m.Width + cx + dx] == Cell.Floor && BuilderWalls.CellRefusal(w, cx + dx, cz + dz) == 0
                         && !TerrainHeight.InStationZone(w, cx + dx, cz + dz);
                if (!ok) continue;
                double d = (cx - m.Width / 2.0) * (cx - m.Width / 2.0) + (cz - m.Height / 2.0) * (cz - m.Height / 2.0);
                if (d < best) { best = d; ox = cx; oz = cz; }
            }
            Assert.GreaterOrEqual(ox, 0, "an open buildable area exists");
        }

        private static void SetCol(World w, int cx, int z0, int z1, int lift)
        {
            for (int cz = z0; cz <= z1; cz++) w.Map.Lift[cz * w.Map.Width + cx] = (sbyte)lift;
        }

        private static int Lift(World w, int cx, int cz) => w.Map.Lift[cz * w.Map.Width + cx];

        private static void Walk(World w, double mx, double mz, bool jump = false)
        {
            w.Tick++;
            InputFrame f = w.Inputs[Me];
            f.MoveX = mx; f.MoveZ = mz; f.Jump = jump; f.Attack = false; f.Dodge = false;
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            Movement.MovementSystem(w, SimStep.Dt(w.Config));
        }

        /// <summary>Walk actor 0 east from (ox - 2.5, oz + 0.5) over a 2-cell-wide strip [ox, ox + 1] of lift `lift`; returns the final x.</summary>
        private static double CrossStrip(int lift, int ticks, out int minLevel, out int maxLevel, out TerrainHeightState s, Action<GameConfig> tweak = null, bool jumpAt3 = false, byte strip = Cell.Floor)
        {
            World w = Field(ClassId.Warrior, tweak);
            Open(w, 4, out int ox, out int oz);
            for (int k = 0; k < 2; k++)
            {
                SetCol(w, ox + k, oz - 4, oz + 4, lift);
                if (strip != Cell.Floor) for (int cz = oz - 4; cz <= oz + 4; cz++) w.Map.Cells[cz * w.Map.Width + ox + k] = strip;
            }
            Put(w, Me, ox - 2.5, oz + 0.5);
            minLevel = int.MaxValue; maxLevel = int.MinValue;
            for (int t = 0; t < ticks; t++)
            {
                Walk(w, 1, 0, jumpAt3 && t == 3);
                int l = TerrainHeight.ActorLevel(w, Me);
                if (l < minLevel) minLevel = l;
                if (l > maxLevel) maxLevel = l;
            }
            s = w.Depth.Height;
            return w.X[Me] - ox;   // relative to the strip's west edge: > 2 = across it
        }

        // ------------------------------------------------------------------ the layer and the switch

        [Test]
        public void TheLayerExistsOnlyInFullRulesWithItsSwitchOn()
        {
            int bad = 0;
            void Case(string what, GameConfig c, bool want)
            {
                World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c });
                bool got = w.Map.Lift != null && w.Depth.Height != null;
                Line(what + ": lift layer exists", got, want.ToString());
                if (got != want) bad++;
            }
            Case("Classic", GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Classic), false);
            Case("PhysicsOnly", GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.PhysicsOnly), false);
            Case("Full", Full(), true);
            Case("Full, TerrainHeight.Enabled false", Full(c => c.Depth.TerrainHeight.Enabled = false), false);
            Assert.AreEqual(0, bad);
        }

        // ------------------------------------------------------------------ movement

        [Test]
        public void ACharacterWalksDownIntoAOneDeepPitAndBackOut()
        {
            double x = CrossStrip(-1, 75, out int lo, out int hi, out TerrainHeightState s);
            Line("1-deep pit, 2 cells wide: x past the pit's west edge after 2.5 s", x.ToString("F2"), "> 2.2 (across)");
            Line("1-deep pit: lowest level on the way", lo, "-1");
            Line("1-deep pit: 1-level steps / climbs", s.Steps + " / " + s.Climbs, "2 / 0");
            double xv = CrossStrip(0, 75, out _, out _, out _, null, false, Cell.Void);
            Line("control: the same strip as Void (the old pit): x", xv.ToString("F2"), "< 0 (pushed back like a wall)");
            Assert.Greater(x, 2.2);
            Assert.AreEqual(-1, lo);
            Assert.GreaterOrEqual(s.Steps, 2);
            Assert.AreEqual(0, s.Climbs);
            Assert.Less(xv, 0.0);
        }

        [Test]
        public void ATwoDeepPitIsADropAndAHalfSecondClimbOut()
        {
            double x = CrossStrip(-2, 90, out int lo, out _, out TerrainHeightState s);
            Line("2-deep pit: x after 3 s", x.ToString("F2"), "> 2.2 (climbed out)");
            Line("2-deep pit: lowest level / drops / climbs", lo + " / " + s.Drops + " / " + s.Climbs, "-2 / 1 / 1");
            double xs = CrossStrip(-2, 90, out _, out _, out TerrainHeightState s2, c => c.Depth.TerrainHeight.ClimbTicks = 100000);
            Line("2-deep pit with the climb switched off: x after 3 s", xs.ToString("F2"), "< 2 (still in the pit: 2 levels is a wall to a walker)");
            Assert.Greater(x, 2.2);
            Assert.AreEqual(-2, lo);
            Assert.AreEqual(1, s.Drops);
            Assert.AreEqual(1, s.Climbs);
            Assert.Less(xs, 2.0);
            Assert.AreEqual(0, s2.Climbs);
        }

        [Test]
        public void AThreeHighStackIsAWallAndATwoHighStackTakesAJump()
        {
            double x3 = CrossStrip(3, 90, out _, out int hi3, out _);
            Line("3-high strip: x after 3 s", x3.ToString("F2"), "<= 0 (a wall)");
            double x1 = CrossStrip(1, 90, out _, out int hi1, out TerrainHeightState s1);
            Line("1-high strip: highest level / x", hi1 + " / " + x1.ToString("F2"), "1 / > 2.2 (walked over)");
            CrossStrip(2, 25, out _, out int hiJ, out TerrainHeightState sj, null, true);
            Line("2-high strip, jump after 3 ticks: highest level within 25 ticks / climbs", hiJ + " / " + sj.Climbs, "2 / 0");
            Assert.LessOrEqual(x3, 0.0);
            Assert.AreEqual(0, hi3);
            Assert.AreEqual(1, hi1);
            Assert.Greater(x1, 2.2);
            Assert.AreEqual(2, hiJ);
            Assert.AreEqual(0, sj.Climbs);
        }

        [Test]
        public void AOneLevelStepSlowsBriefly()
        {
            World w = Field(ClassId.Warrior);
            Open(w, 4, out int ox, out int oz);
            SetCol(w, ox, oz - 4, oz + 4, 1);
            SetCol(w, ox + 1, oz - 4, oz + 4, 1);
            Put(w, Me, ox - 1.5, oz + 0.5);
            double scale = 1;
            int slowAt = -1;
            for (int t = 0; t < 30 && slowAt < 0; t++)
            {
                Walk(w, 1, 0);
                if (w.Depth.Height.SlowTicks[Me] > 0) { slowAt = t; scale = TerrainHeight.SpeedScale(w, Me); }
            }
            Line("step up 1 level: slow starts at tick / speed scale", slowAt + " / " + scale.ToString("F2"), ">= 0 / 0.70");
            Assert.GreaterOrEqual(slowAt, 0);
            Assert.AreEqual(w.Config.Depth.TerrainHeight.StepSlowScale, scale, 1e-12);
        }

        // ------------------------------------------------------------------ stacking and digging

        private static int BuildBlock(World w, int cx, int cz)
        {
            w.Walls.Piece[Me] = (byte)BuildPiece.Block;
            w.Walls.Tool[Me] = 1;
            w.Tick += 120;   // past the build lock, the team cooldown and the world gap
            return BuilderWalls.Build(w, Me, cx + 0.5, cz + 0.5);
        }

        [Test]
        public void BlocksStackToThreeAndTheFourthIsRefusedWithItsReason()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            Put(w, Me, ox + 0.5, oz + 0.5);
            int cx = ox + 2, cz = oz;
            var got = new int[4];
            var lifts = new int[4];
            for (int k = 0; k < 4; k++) { got[k] = BuildBlock(w, cx, cz); lifts[k] = Lift(w, cx, cz); }
            Line("four BLOCK presses on one cell: verdicts", string.Join(",", got), "1,1,1," + TerrainHeight.RefusedTooHigh);
            Line("four BLOCK presses: lift after each", string.Join(",", lifts), "1,2,3,3");
            Line("the fourth press's word", BuilderWalls.Word(got[3]), "TOO HIGH - 3 MAX");
            Assert.AreEqual(new[] { 1, 1, 1, TerrainHeight.RefusedTooHigh }, got);
            Assert.AreEqual(new[] { 1, 2, 3, 3 }, lifts);
            Assert.AreEqual("TOO HIGH - 3 MAX", BuilderWalls.Word(got[3]));
        }

        [Test]
        public void ABlockIsRefusedWhereSomeoneStandsAndAWallOnlyGoesOnLevelGround()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            Put(w, Me, ox + 0.5, oz + 0.5);
            Put(w, 1, ox + 2.5, oz + 0.5);
            int why = BuildBlock(w, ox + 2, oz);
            Line("BLOCK on a cell with a body on it", why + " " + BuilderWalls.Word(why), TerrainHeight.RefusedSomeone + " SOMEONE IS STANDING THERE");
            w.Actors[1].Alive = false;
            w.Map.Lift[oz * w.Map.Width + ox + 2] = 1;
            w.Walls.Piece[Me] = (byte)BuildPiece.Wall;
            w.Walls.Tall[Me] = 0;
            w.Tick += 120;
            int wall = BuilderWalls.Refusal(w, Me, ox + 2.5, oz + 0.5);
            Line("WALL across a raised cell", wall + " " + BuilderWalls.Word(wall), TerrainHeight.RefusedLevelOnly + " LEVEL GROUND ONLY");
            Assert.AreEqual(TerrainHeight.RefusedSomeone, why);
            Assert.AreEqual(TerrainHeight.RefusedLevelOnly, wall);
        }

        private static int Dig(World w, int cx, int cz)
        {
            w.Tick += 120;
            w.Terraform.Carried[Me] = 0;
            return Terraform.Dig(w, Me, cx + 0.5, cz + 0.5);
        }

        [Test]
        public void DigTakesTheTopBlockOffAndDeepensGroundToTwo()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            Put(w, Me, ox + 0.5, oz + 0.5);
            int cx = ox + 1, cz = oz;
            w.Map.Lift[cz * w.Map.Width + cx] = 2;
            var got = new int[5];
            var lifts = new int[5];
            for (int k = 0; k < 5; k++) { got[k] = Dig(w, cx, cz); lifts[k] = Lift(w, cx, cz); }
            Line("five DIG presses on a 2-high stack: verdicts", string.Join(",", got), "1,1,1,1," + TerrainHeight.RefusedTooDeep);
            Line("five DIG presses: lift after each", string.Join(",", lifts), "1,0,-1,-2,-2");
            Line("the fifth press's word", TerrainHeight.Word(got[4], w.Config), "TOO DEEP - 2 MAX");
            Assert.AreEqual(new[] { 1, 1, 1, 1, TerrainHeight.RefusedTooDeep }, got);
            Assert.AreEqual(new[] { 1, 0, -1, -2, -2 }, lifts);
            Assert.AreEqual(Cell.Floor, w.Map.Cells[cz * w.Map.Width + cx], "a dug pit stays walkable floor");
        }

        [Test]
        public void SomeoneOnATowerThatIsDugOutDropsAndNobodyIsDugIntoTheGround()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            Put(w, Me, ox + 0.5, oz + 0.5);
            int cx = ox + 1, cz = oz;
            w.Map.Lift[cz * w.Map.Width + cx] = 2;
            Put(w, 1, cx + 0.5, cz + 0.5);
            Walk(w, 0, 0);
            int before = TerrainHeight.ActorLevel(w, 1);
            int dug = Dig(w, cx, cz);
            Walk(w, 0, 0);
            int after = TerrainHeight.ActorLevel(w, 1);
            Line("a body on a 2-high tower, the top block dug: verdict / its level before -> after", dug + " / " + before + " -> " + after, "1 / 2 -> 1");
            w.Map.Lift[cz * w.Map.Width + cx] = 0;
            int ground = Dig(w, cx, cz);
            Line("DIG into level ground under a body", ground + " " + TerrainHeight.Word(ground, w.Config), TerrainHeight.RefusedSomeone + " SOMEONE IS STANDING THERE");
            Assert.AreEqual(Terraform.Dug, dug);
            Assert.AreEqual(2, before);
            Assert.AreEqual(1, after);
            Assert.AreEqual(TerrainHeight.RefusedSomeone, ground);
        }

        [Test]
        public void TntTakesTwoLevelsOffAStackAndTheKaijuSmashesStacks()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            w.Map.Lift[oz * w.Map.Width + ox] = 3;
            w.Map.Lift[oz * w.Map.Width + ox + 1] = 1;
            bool a = TerrainHeight.TntCell(w, Me, ox, oz);
            bool b = TerrainHeight.TntCell(w, Me, ox + 1, oz);
            bool c = TerrainHeight.TntCell(w, Me, ox + 2, oz);
            Line("TNT on lift 3 / 1 / 0: handled and lift after", a + " " + Lift(w, ox, oz) + " / " + b + " " + Lift(w, ox + 1, oz) + " / " + c + " " + Lift(w, ox + 2, oz), "True 1 / True 0 / False 0 (crater rule)");
            Assert.IsTrue(a); Assert.IsTrue(b); Assert.IsFalse(c);
            Assert.AreEqual(1, Lift(w, ox, oz));
            Assert.AreEqual(0, Lift(w, ox + 1, oz));

            for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) w.Map.Lift[(oz + dz) * w.Map.Width + ox + dx] = 3;
            w.Kaiju.State = KaijuState.Azure;
            w.Kaiju.X = ox + 0.5; w.Kaiju.Z = oz + 0.5;
            TerrainHeight.HeightSystem(w);
            int under = Lift(w, ox, oz), corner = Lift(w, ox + 2, oz + 2);
            Line("Kaiju on a field of 3-high stacks: lift under it / at the 5x5 corner / smashed cells", under + " / " + corner + " / " + w.Depth.Height.Smashed, "1 / 1 or 3 / >= 9");
            Assert.AreEqual(1, under);
            Assert.GreaterOrEqual(w.Depth.Height.Smashed, 9);
        }

        // ------------------------------------------------------------------ routes, fields, ladders

        [Test]
        public void FieldsCostStepsCloseThreeLevelEdgesAndTheRouteGuardRefusesAWallOff()
        {
            World w = Field();
            MapDef m = w.Map;
            PadDef pad = m.Pads[0];
            int pcx = JsMath.FloorToInt(pad.X), pcz = JsMath.FloorToInt(pad.Z);
            Assert.IsTrue(Terraform.PadsReachHill(m), "the untouched map routes");
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dz != 0) m.Lift[(pcz + dz) * m.Width + pcx + dx] = 3;
            int gap = pcz * m.Width + pcx + 1;
            m.Lift[gap] = 0;
            bool open = Terraform.PadsReachHill(m);
            bool closeOk = TerrainHeight.KeepsRoutes(w, gap, 3);
            bool stepOk = TerrainHeight.KeepsRoutes(w, gap, 2);
            m.Lift[gap] = 2;
            FlowField f = FlowField.ComputeFlowField(m, new[] { new PointXZ(m.Hill.X, m.Hill.Z) });
            double viaClimb = f.Dist[pcz * m.Width + pcx];
            m.Lift[gap] = 0;
            f = FlowField.ComputeFlowField(m, new[] { new PointXZ(m.Hill.X, m.Hill.Z) });
            double viaFlat = f.Dist[pcz * m.Width + pcx];
            Line("pad cell ringed by 3-high stacks with a level gap: pads reach the hill", open, "True");
            Line("closing the gap at 3 / raising it to 2: route guard", closeOk + " / " + stepOk, "False / True");
            Line("pad cell field distance through the gap at 2 levels vs level", viaClimb.ToString("F2") + " vs " + viaFlat.ToString("F2"), "at least +2 x PathCostStep2 (" + (2 * m.LiftCost2).ToString("F1") + "), finite");
            Assert.IsTrue(open);
            Assert.IsFalse(closeOk);
            Assert.IsTrue(stepOk);
            Assert.IsTrue(double.IsFinite(viaClimb));
            Assert.GreaterOrEqual(viaClimb, viaFlat + 2 * m.LiftCost2 - 1e-6);
        }

        [Test]
        public void ABuilderInATwoDeepPitLaysALadderOnTheLedgeAndWalksOut()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            MapDef m = w.Map;
            m.Lift[oz * m.Width + ox] = -2;
            Put(w, Me, ox + 0.5, oz + 0.5);
            int why = BuiltPieces.Plan(w, Me, 1, 0, 0);
            int ledge = oz * m.Width + ox + 1;
            Line("LADDER plan from a 2-deep pit facing level ground: verdict / kind / cells / cell", why + " / " + BuiltPieces.LastPlanKind + " / " + BuiltPieces.LastPlanCount + " / " + (BuiltPieces.LastPlanCount > 0 ? BuiltPieces.LastPlanCell(0) : -1), "0 / 1 / 1 / " + ledge);
            Assert.AreEqual(0, why);
            Assert.AreEqual(BuiltPieces.Ladder, BuiltPieces.LastPlanKind);
            Assert.AreEqual(1, BuiltPieces.LastPlanCount);
            Assert.AreEqual(ledge, BuiltPieces.LastPlanCell(0));
            m.Cells[ledge] = Cell.Bridge;   // the laid ladder (BuiltPieces.Lay writes Bridge)
            Walk(w, 0, 0);
            int t = 0;
            while (t < 30 && w.X[Me] < ox + 2.5) { Walk(w, 1, 0); t++; }
            Line("walking out over the ladder cell: ticks to reach the cell past the ledge", t, "< 30 (no 0.5 s climb push needed)");
            Assert.Less(t, 30);
        }

        /// <summary>A real LADDER build by actor 0 (a Builder) toward (aimX, aimZ): cooldown, bank and the world gap cleared first.</summary>
        private static int BuildLadder(World w, double aimX, double aimZ)
        {
            w.Actors[Me].AbilityCd = 0;
            w.HasAim[Me] = 1;
            w.Inputs[Me].AimX = aimX;
            w.Inputs[Me].AimZ = aimZ;
            w.WallChangeTick = World.NeverTick;
            w.Builder.Spent[0] = -1000;   // a full bank for the staging
            return BuiltPieces.Build(w, Me, aimX, aimZ);
        }

        [Test]
        public void ALadderOnTheSideOfAThreeHighTowerClimbsItAndComesDownWithTheTopBlock()
        {
            World w = Field();
            Open(w, 4, out int ox, out int oz);
            MapDef m = w.Map;
            int a = oz * m.Width + ox + 1, b = oz * m.Width + ox + 2;
            m.Lift[a] = 3; m.Lift[b] = 3;
            Put(w, Me, ox + 0.5, oz + 0.5);
            int built = BuildLadder(w, ox + 1.5, oz + 0.5);
            bool ground = TerrainHeight.LadderGround(w, a, out byte prev);
            Line("LADDER facing a 3-high tower from level ground: code / cell / lift / height ladder (ground)", built + " / " + m.Cells[a] + " / " + m.Lift[a] + " / " + ground + " (" + prev + ")",
                 BuiltPieces.BuiltLadderCode + " / " + Cell.Bridge + " / 3 / True (" + Cell.Floor + ")");
            Assert.AreEqual(BuiltPieces.BuiltLadderCode, built);
            Assert.AreEqual(Cell.Bridge, m.Cells[a]);
            Assert.IsTrue(ground);
            Line("path edge ground -> ladder cell (3 levels up)", TerrainHeight.EdgeExtra(m, oz * m.Width + ox, a).ToString("F2"), "PathCostLadder " + m.LiftCostLadder.ToString("F2") + " (open, not -1)");
            Assert.AreEqual(m.LiftCostLadder, TerrainHeight.EdgeExtra(m, oz * m.Width + ox, a), 1e-12);
            int t = 0;
            Walk(w, 0, 0);
            while (t < 45 && !(w.X[Me] > ox + 2.3)) { Walk(w, 1, 0); t++; }
            int top = TerrainHeight.ActorLevel(w, Me);
            Line("walking up the ladder onto the tower: ticks / level on top", t + " / " + top, "< 45 / 3");
            Assert.Less(t, 45);
            Assert.AreEqual(3, top);
            Put(w, Me, ox + 0.5, oz + 0.5);
            w.Tick += 120;
            w.Terraform.Carried[Me] = 0;
            int dug = Terraform.Dig(w, Me, ox + 1.5, oz + 0.5);
            Line("DIG on the ladder cell: verdict / cell / lift / still a ladder", dug + " / " + m.Cells[a] + " / " + m.Lift[a] + " / " + TerrainHeight.LadderGround(w, a, out _),
                 Terraform.Dug + " / " + Cell.Floor + " / 2 / False");
            Assert.AreEqual(Terraform.Dug, dug);
            Assert.AreEqual(Cell.Floor, m.Cells[a]);
            Assert.AreEqual(2, m.Lift[a]);
        }

        [Test]
        public void TntAndTheKaijuTakeAHeightLadderDownWithTheBlocks()
        {
            int bad = 0;
            for (int arm = 0; arm < 2; arm++)
            {
                World w = Field();
                Open(w, 4, out int ox, out int oz);
                MapDef m = w.Map;
                int a = oz * m.Width + ox + 1;
                m.Lift[a] = 3;
                Put(w, Me, ox + 0.5, oz + 0.5);
                Assert.AreEqual(BuiltPieces.BuiltLadderCode, BuildLadder(w, ox + 1.5, oz + 0.5));
                string what;
                if (arm == 0) { TerrainHeight.TntCell(w, Me, ox + 1, oz); what = "TNT"; }
                else
                {
                    w.Actors[Me].Alive = false;
                    w.Kaiju.State = KaijuState.Azure;
                    w.Kaiju.X = ox + 1.5; w.Kaiju.Z = oz + 0.5;
                    TerrainHeight.HeightSystem(w);
                    what = "Kaiju";
                }
                int want = arm == 0 ? 1 : w.Config.Depth.TerrainHeight.SmashTo;
                Line(what + " on a ladder against a 3-high block: cell / lift / ladders lost", m.Cells[a] + " / " + m.Lift[a] + " / " + w.Depth.Height.LaddersLost, Cell.Floor + " / " + want + " / 1");
                if (m.Cells[a] != Cell.Floor || m.Lift[a] != want || w.Depth.Height.LaddersLost != 1) bad++;
            }
            Assert.AreEqual(0, bad);
        }

        /// <summary>
        /// Natural cliff faces: the island's elevation (Height01) is view-only in the sim, so a ladder there would change nothing.
        /// Census of the Full map: orthogonal pairs of walkable cells whose island heights differ by 2+ steps (StepU each), and
        /// the rock-wall cells a ladder can take today. Printed for the owner; not a limit.
        /// </summary>
        [Test]
        public void NaturalCliffFacesOfTwoStepsCensus()
        {
            World w = Field();
            MapDef m = w.Map;
            double step = w.Config.Depth.TerrainHeight.StepU;
            int faces = 0, walls = 0;
            for (int cz = 0; cz < m.Height; cz++)
            for (int cx = 0; cx < m.Width; cx++)
            {
                int i = cz * m.Width + cx;
                if (m.Cells[i] == Cell.Wall) walls++;
                if (!MapGrid.IsWalkableType(m.Cells[i])) continue;
                if (cx + 1 < m.Width && MapGrid.IsWalkableType(m.Cells[i + 1]) && Math.Abs(m.Height01[i + 1] - m.Height01[i]) >= 2 * step - 1e-9) faces++;
                if (cz + 1 < m.Height && MapGrid.IsWalkableType(m.Cells[i + m.Width]) && Math.Abs(m.Height01[i + m.Width] - m.Height01[i]) >= 2 * step - 1e-9) faces++;
            }
            Line("Full map: walkable-to-walkable natural faces of 2+ steps (" + (2 * step).ToString("F2") + " u of island height)", faces, "printed (view-only height: walkable in the sim)");
            Line("Full map: rock-wall cells (Cell.Wall)", walls, "printed");
            Assert.GreaterOrEqual(faces, 0);
        }

        // ------------------------------------------------------------------ whole matches

        /// <summary>
        /// Mirrored, seeded terrain edits for the bot arms (bots never dig or stack on their own): `pairs` 2x2 clusters in the west
        /// half at lift -2..3 (not 0), each mirrored across the map's x mirror line, on cells the stack rules accept at tick 0, each
        /// cell through the route guard. Returns cells changed. Deterministic (DepthRules.HashInt).
        /// </summary>
        internal static int Scatter(World w, int seed, int pairs)
        {
            MapDef m = w.Map;
            if (m.Lift == null) return 0;
            int changed = 0, placed = 0;
            for (int k = 0; placed < pairs && k < pairs * 60; k++)
            {
                int cx = 2 + DepthRules.HashInt(seed, k, 11, m.Width / 2 - 4);
                int cz = 2 + DepthRules.HashInt(seed, k, 12, m.Height - 5);
                int lv = DepthRules.HashInt(seed, k, 13, 5) - 2;
                if (lv >= 0) lv++;
                bool ok = true;
                for (int dz = 0; dz < 2 && ok; dz++)
                for (int dx = 0; dx < 2 && ok; dx++)
                {
                    int ax = cx + dx, bx = m.Width - 1 - (cx + dx), z = cz + dz;
                    ok = TerrainHeight.StackRefusal(w, ax, z) == 0 && TerrainHeight.StackRefusal(w, bx, z) == 0
                         && m.Lift[z * m.Width + ax] == 0 && m.Lift[z * m.Width + bx] == 0 && m.Cells[z * m.Width + ax] == Cell.Floor;
                }
                if (!ok) continue;
                for (int dz = 0; dz < 2; dz++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int z = cz + dz;
                    foreach (int x in new[] { cx + dx, m.Width - 1 - (cx + dx) })
                    {
                        int i = z * m.Width + x;
                        if (!TerrainHeight.KeepsRoutes(w, i, lv)) continue;
                        TerrainHeight.SetLift(w, i, lv);
                        changed++;
                    }
                }
                placed++;
            }
            return changed;
        }

        private static GameConfig MatchConfig(int size, bool on)
        {
            GameConfig c = size >= 16 ? GameConfig.CreateDefault() : GameConfig.CreateBrowser5v5();
            c.ApplyRules(RulesProfile.Full);
            c.Depth.TerrainHeight.Enabled = on;
            return c;
        }

        private sealed class Arm
        {
            public int Kills, LeadChanges, Matches, StuckNearHeight, StuckAll, RiseJumps, HillCut, KaijuStalls, Cells, MaxStillNearHeight;
            public int Drops, Climbs, Steps;
            public double Minutes;
            public readonly int[] Wins = new int[3];
            public readonly List<double> FirstKo = new List<double>();
        }

        private static bool NearHeight(MapDef m, double x, double z)
        {
            if (m.Lift == null) return false;
            int cx = JsMath.FloorToInt(x), cz = JsMath.FloorToInt(z);
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
                if (TerrainHeight.LiftAt(m, cx + dx, cz + dz) != 0) return true;
            return false;
        }

        private static void PlayMatch(int seed, int size, bool on, Arm arm, string tag)
        {
            GameConfig cfg = MatchConfig(size, on);
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = size, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            int cells = Scatter(w, seed, size >= 16 ? 16 : 10);
            arm.Cells += cells;
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz, lastHead = 0, lastLeader = 0;
            double firstKo = -1;
            var ax = new double[w.ActorCount];
            var az = new double[w.ActorCount];
            var still = new int[w.ActorCount];
            var level = new int[w.ActorCount];
            double kx = 0, kz = 0;
            int kStill = 0;
            bool kStalled = false;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                for (int i = 0; i < w.ActorCount; i++) level[i] = w.Depth.Height != null ? w.Depth.Height.Level[i] : TerrainHeightState.NoLevel;
                MatchTick.Advance(w, null, scratch);
                for (int i = 0; i < w.ActorCount; i++)
                {
                    Actor a = w.Actors[i];
                    InputFrame f = w.Inputs[i];
                    bool pushing = a.Alive && a.AirLen == 0 && a.StandUp <= 0 && f.MoveX * f.MoveX + f.MoveZ * f.MoveZ > 0.25;
                    double dx = w.X[i] - ax[i], dz = w.Z[i] - az[i];
                    if (!pushing || dx * dx + dz * dz >= 0.25) { ax[i] = w.X[i]; az[i] = w.Z[i]; still[i] = 0; }
                    else
                    {
                        still[i]++;
                        if (still[i] == 300)
                        {
                            arm.StuckAll++;
                            if (NearHeight(w.Map, w.X[i], w.Z[i])) arm.StuckNearHeight++;
                        }
                        if (NearHeight(w.Map, w.X[i], w.Z[i]) && still[i] > arm.MaxStillNearHeight) arm.MaxStillNearHeight = still[i];
                    }
                    if (a.Alive && w.Depth.Height != null && level[i] != TerrainHeightState.NoLevel && w.Depth.Height.Level[i] != TerrainHeightState.NoLevel
                        && w.Depth.Height.Level[i] - level[i] >= 3) arm.RiseJumps++;
                }
                if (t % 30 == 0 && !Terraform.PadsReachHill(w.Map)) arm.HillCut++;
                Kaiju k = w.Kaiju;
                if (k.State != KaijuState.Egg && !(k.KoIn > 0))
                {
                    double ddx = k.X - kx, ddz = k.Z - kz;
                    if (ddx * ddx + ddz * ddz >= 0.25) { kx = k.X; kz = k.Z; kStill = 0; kStalled = false; }
                    else if (++kStill > 600 && !kStalled) { kStalled = true; arm.KaijuStalls++; }
                }
                else { kx = k.X; kz = k.Z; kStill = 0; kStalled = false; }
                int from = Math.Max(lastHead, w.EventHead - World.EventCapacity);
                for (int q = from; q < w.EventHead; q++)
                {
                    SimEvent ev = w.Events[q % World.EventCapacity];
                    if (ev.Type != SimEventType.Death || ev.A < 0 || ev.A >= w.ActorCount) continue;
                    if (firstKo < 0) firstKo = w.Match.Time;
                    arm.Kills++;
                }
                lastHead = w.EventHead;
                double lead = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                int leader = lead > 1e-9 ? 1 : lead < -1e-9 ? -1 : 0;
                if (leader != 0) { if (lastLeader != 0 && leader != lastLeader) arm.LeadChanges++; lastLeader = leader; }
            }
            arm.Matches++;
            arm.Minutes += w.Match.Time / 60.0;
            if (firstKo >= 0) arm.FirstKo.Add(firstKo);
            arm.Wins[w.Match.Winner == MatchResult.Azure ? 0 : w.Match.Winner == MatchResult.Crimson ? 1 : 2]++;
            if (w.Depth.Height != null) { arm.Drops += w.Depth.Height.Drops; arm.Climbs += w.Depth.Height.Climbs; arm.Steps += w.Depth.Height.Steps; }
            Console.WriteLine("{\"item\":\"b15 height match\",\"tag\":\"" + tag + "\",\"size\":" + size + ",\"seed\":" + seed + ",\"height\":\"" + (on ? "on" : "off (control)") +
                              "\",\"editedCells\":" + cells + ",\"winner\":\"" + w.Match.Winner + "\",\"firstKoAt\":" + firstKo.ToString("F1") + ",\"seconds\":" + w.Match.Time.ToString("F0") +
                              ",\"drops\":" + (w.Depth.Height != null ? w.Depth.Height.Drops : 0) + ",\"climbs\":" + (w.Depth.Height != null ? w.Depth.Height.Climbs : 0) + "}");
        }

        private static double Median(List<double> v)
        {
            if (v.Count == 0) return -1;
            v.Sort();
            return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        private static void Limit(List<string> failures, string what, double on, double control, bool pass, string limit)
        {
            string line = what + ": on " + on.ToString("F3") + ", control " + control.ToString("F3") + ", limit " + limit + " -> " + (pass ? "PASS" : "FAIL");
            Console.WriteLine("{\"item\":\"b15 height limit\",\"line\":\"" + line + "\"}");
            if (!pass) failures.Add(line);
        }

        private static WinShare.Tally TallyOf(Arm a) => new WinShare.Tally { Azure = a.Wins[0], Crimson = a.Wins[1], Draws = a.Wins[2] };

        /// <summary>
        /// SOAK, 40 seeds (5v5 Full 1 to 40) with bots on a seeded, mirrored field of pits and stacks, against the same seeds with
        /// the rule off: no actor stuck pushing for 10 s near height (strict 0), no level rise of 3+ in one tick (inside
        /// geometry), the pads always reach the hill (every 30th tick), Kaiju no-progress stalls over 20 s at most control + 1.
        /// </summary>
        [Test, Category("Slow")]
        public void FortySeedSoakWithBotsOnPitsAndStacks()
        {
            var on = new Arm();
            var off = new Arm();
            for (int seed = 1; seed <= 40; seed++)
            {
                PlayMatch(seed, 5, true, on, "soak");
                PlayMatch(seed, 5, false, off, "soak");
            }
            Console.WriteLine("{\"item\":\"b15 height soak\",\"matches\":" + on.Matches + ",\"editedCells\":" + on.Cells + ",\"drops\":" + on.Drops + ",\"climbs\":" + on.Climbs + ",\"steps\":" + on.Steps +
                              ",\"stuck10sNearHeight\":" + on.StuckNearHeight + ",\"longestStillNearHeightTicks\":" + on.MaxStillNearHeight + ",\"stuck10sAnywhereOn\":" + on.StuckAll + ",\"stuck10sAnywhereControl\":" + off.StuckAll +
                              ",\"riseOf3InOneTick\":" + on.RiseJumps + ",\"hillUnreachableSamples\":" + on.HillCut + ",\"kaijuStalls20sOn\":" + on.KaijuStalls + ",\"kaijuStalls20sControl\":" + off.KaijuStalls + "}");
            var failures = new List<string>();
            if (on.StuckNearHeight != 0) failures.Add("actors stuck pushing 10 s near height: " + on.StuckNearHeight + " (limit 0)");
            if (on.RiseJumps != 0) failures.Add("level rises of 3+ in one tick: " + on.RiseJumps + " (limit 0)");
            if (on.HillCut != 0) failures.Add("samples with a pad cut off from the hill: " + on.HillCut + " (limit 0)");
            if (on.KaijuStalls > off.KaijuStalls + 1) failures.Add("Kaiju 20 s stalls: on " + on.KaijuStalls + " vs control " + off.KaijuStalls + " (limit control + 1)");
            if (on.Drops + on.Climbs == 0) failures.Add("no bot ever dropped into or climbed out of a pit: the soak did not exercise height");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// BALANCE on the shared 24-seed 16v16 helper (WinShare.Seeds16), rule on with the seeded field vs the rule off: win share
        /// (WinShare.Judge), median first knock-out -10% / +20%, kills per minute -20% / +15%, hill lead changes +-30%. Printed in
        /// full; the owner plays before any feel cut.
        /// </summary>
        [Test, Category("Slow")]
        public void HeightKeepsTheControlsBalanceOnTheSharedSixteenSeeds()
        {
            var on = new Arm();
            var off = new Arm();
            foreach (int seed in WinShare.Seeds16)
            {
                PlayMatch(seed, 16, true, on, "balance");
                PlayMatch(seed, 16, false, off, "balance");
            }
            var failures = new List<string>();
            double koOn = Median(on.FirstKo), koOff = Median(off.FirstKo);
            Limit(failures, "16v16 median first knock-out (s)", koOn, koOff, koOff > 0 && koOn >= koOff * 0.90 - 1e-9 && koOn <= koOff * 1.20 + 1e-9, "-10% / +20%");
            double kOn = on.Kills / Math.Max(1e-9, on.Minutes), kOff = off.Kills / Math.Max(1e-9, off.Minutes);
            Limit(failures, "16v16 kills per minute", kOn, kOff, kOff > 0 && kOn >= kOff * 0.80 - 1e-9 && kOn <= kOff * 1.15 + 1e-9, "-20% / +15%");
            Limit(failures, "16v16 hill lead changes", on.LeadChanges, off.LeadChanges, Math.Abs(on.LeadChanges - off.LeadChanges) <= 0.30 * Math.Max(1, off.LeadChanges) + 1e-9, "+-30%");
            Console.WriteLine("{\"item\":\"b15 height balance\",\"editedCellsOn\":" + on.Cells + ",\"drops\":" + on.Drops + ",\"climbs\":" + on.Climbs + ",\"steps\":" + on.Steps + "}");
            if (!WinShare.Judge("b15 terrain height", "16v16 (" + WinShare.Seeds16.Length + " seeds)", TallyOf(on), TallyOf(off), true)) failures.Add("16v16 win share (see the [win share] line)");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>With the rule on but nobody editing terrain (bots never dig or stack), a match plays exactly like the control.</summary>
        [Test]
        public void NoEditsMeansTheSameMatchAsTheControl()
        {
            int bad = 0;
            foreach (int seed in new[] { 1, 2 })
            {
                var r = new string[2];
                for (int arm = 0; arm < 2; arm++)
                {
                    World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = MatchConfig(5, arm == 0) });
                    InputFrame scratch = SimInput.EmptyInput();
                    for (int t = 0; t < 5400 && w.Match.Phase != MatchPhase.Ended; t++) MatchTick.Advance(w, null, scratch);
                    double s = 0;
                    for (int i = 0; i < w.ActorCount; i++) s += w.X[i] * (i + 1) + w.Z[i] * (i + 7) + w.Actors[i].Hp * 0.001;
                    int lifted = 0;
                    if (w.Map.Lift != null) foreach (sbyte v in w.Map.Lift) if (v != 0) lifted++;
                    r[arm] = s.ToString("R") + " hill " + w.Match.HillSeconds.Azure.ToString("R") + "/" + w.Match.HillSeconds.Crimson.ToString("R") + (arm == 0 ? " lifted " + lifted : "");
                }
                Line("5v5 seed " + seed + " 3:00, height on (no edits) vs off: fingerprint", r[0] + " | " + r[1], "equal positions / hp / hill");
                if (!r[0].StartsWith(r[1])) bad++;
            }
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void TheSameSeedTwiceIsIdenticalWithHeight()
        {
            var h = new ulong[2];
            for (int run = 0; run < 2; run++)
            {
                World w = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = MatchConfig(16, true) });
                Scatter(w, 4242, 16);
                InputFrame scratch = SimInput.EmptyInput();
                for (int t = 0; t < 3600; t++) MatchTick.Advance(w, null, scratch);
                h[run] = MatchHash.Of(w);
                if (run == 1)
                {
                    int i = Array.FindIndex(w.Map.Lift, v => v != 0);
                    if (i >= 0)
                    {
                        sbyte keep = w.Map.Lift[i];
                        w.Map.Lift[i] = (sbyte)(keep == 1 ? 2 : 1);
                        ulong moved = MatchHash.Of(w);
                        w.Map.Lift[i] = keep;
                        Line("MatchHash moves when one lift changes", moved != h[1], "True");
                        Assert.AreNotEqual(h[1], moved);
                    }
                }
            }
            Line("16v16 seed 4242 2:00 with seeded height, twice: hash", "0x" + h[0].ToString("X16") + " / 0x" + h[1].ToString("X16"), "equal");
            Assert.AreEqual(h[0], h[1]);
        }

        [Test]
        public void NoAllocationAndTheTickCostOver300TicksOfA16v16WithHeight()
        {
            const int Warm = 600, Ticks = 300;
            var alloc = new long[2];
            var clock = new long[2];
            var worlds = new World[2];
            var scratches = new InputFrame[2];
            for (int arm = 0; arm < 2; arm++)
            {
                worlds[arm] = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = MatchConfig(16, arm == 0) });
                if (arm == 0) Scatter(worlds[arm], 4242, 16);
                scratches[arm] = SimInput.EmptyInput();
                for (int t = 0; t < Warm; t++) MatchTick.Advance(worlds[arm], null, scratches[arm]);
            }
            for (int t = 0; t < Ticks; t++)
            {
                for (int arm = 0; arm < 2; arm++)
                {
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    long c0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    MatchTick.Advance(worlds[arm], null, scratches[arm]);
                    clock[arm] += System.Diagnostics.Stopwatch.GetTimestamp() - c0;
                    alloc[arm] += GC.GetAllocatedBytesForCurrentThread() - a0;
                }
            }
            double usOn = clock[0] * 1e6 / System.Diagnostics.Stopwatch.Frequency / Ticks, usOff = clock[1] * 1e6 / System.Diagnostics.Stopwatch.Frequency / Ticks;
            Console.WriteLine("{\"item\":\"b15 height allocation\",\"wholeTickBytesOn\":" + alloc[0] + ",\"wholeTickBytesControl\":" + alloc[1] + ",\"limit\":\"on <= control\"}");
            Console.WriteLine("{\"item\":\"b15 height sim cost\",\"usPerTickOn\":" + usOn.ToString("F1") + ",\"usPerTickControl\":" + usOff.ToString("F1") + ",\"ratio\":" + (usOn / Math.Max(1e-9, usOff)).ToString("F3") +
                              ",\"limit\":1.05,\"verdict\":\"" + (usOn <= usOff * 1.05 ? "PASS" : "OVER (printed, not asserted: one runner, noisy)") + "\"}");
            Assert.LessOrEqual(alloc[0], alloc[1], "height adds no allocation to the tick");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>
    /// Player-seat gate, tier S, the class verbs and the world (player-agency audit 6.4: S9-S31 verbs; the lead's S32, S36-S40).
    /// Each press is a HumanModel frame through MatchTick.AdvanceHumans. Staging (placing bodies, cells, stock) is test-kit
    /// writes to the world before the press, never during the measured window.
    /// </summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class PlayerSeatWorldTests
    {
        static Seat Pair(ClassId me, ClassId them, double dist)
        {
            Seat s = PlayerSeatRig.Duel(me, them, TargetMode.Passive, 0, 0, distance: dist);
            s.Human.Fight = false;
            return s;
        }

        static void Aim(Seat s, double x, double z)
        {
            s.Human.AimOverrideX = x;
            s.Human.AimOverrideZ = z;
        }

        /// <summary>Steps n ticks holding the aim at (x, z).</summary>
        static void StepsAiming(Seat s, int n, double x, double z)
        {
            for (int i = 0; i < n; i++)
            {
                Aim(s, x, z);
                s.Step();
            }
        }

        /// <summary>An open buildable spot (every cell within r Floor, no wall refusal) at least minHill u from the hill centre, nodes moved off the map.</summary>
        static bool Open(World w, int r, double minHill, out double ox, out double oz)
        {
            foreach (ResourceNode nd in w.Nodes) { nd.X = -100; nd.Z = -100; }
            if (w.Depth.Sources != null) foreach (SourceNode sn in w.Depth.Sources) if (sn != null) { sn.X = -100; sn.Z = -100; }
            MapDef m = w.Map;
            ox = oz = -1;
            double best = double.MaxValue;
            for (int cz = r + 1; cz < m.Height - r - 1; cz++)
                for (int cx = r + 1; cx < m.Width - r - 1; cx++)
                {
                    double hx = cx + 0.5 - m.Hill.X, hz = cz + 0.5 - m.Hill.Z;
                    if (hx * hx + hz * hz < minHill * minHill) continue;
                    bool ok = true;
                    for (int dz = -r; dz <= r && ok; dz++)
                        for (int dx = -r; dx <= r && ok; dx++)
                            ok = m.Cells[(cz + dz) * m.Width + cx + dx] == Cell.Floor && BuilderWalls.CellRefusal(w, cx + dx, cz + dz) == 0;
                    if (!ok) continue;
                    double d = hx * hx + hz * hz;
                    if (d < best) { best = d; ox = cx + 0.5; oz = cz + 0.5; }
                }
            return ox >= 0;
        }

        // ------------------------------------------------------------------ Warrior

        [Test]
        public void S09_Warrior_Shield_CutsFrontalDamage()
        {
            double Hit(bool shield, bool facing)
            {
                Seat s = Pair(ClassId.Warrior, ClassId.Warrior, 1.6);
                World w = s.W;
                double away = w.X[0] - 3;
                if (shield)
                {
                    Aim(s, facing ? w.X[s.Them] : away, w.Z[0]);
                    s.Human.PressAbility = true;
                    s.Step();
                }
                int mark = s.Spy.Mark;
                s.Dummy.PressAttack = true;
                StepsAiming(s, w.Dur.AttackWindup[(int)ClassId.Warrior] + 2, facing ? w.X[s.Them] : away, w.Z[0]);
                return s.Spy.Hits(s.Them, 0, mark).Sum(e => e.Value);
            }
            // lane S3: the rear case is compared with an UNSHIELDED hit from the same rear side: with critical strikes on, a hit
            // from the victim's rear arc is a BACK HIT (x CritDamageMul) whether or not he holds a shield.
            double open = Hit(false, true), front = Hit(true, true), rear = Hit(true, false), openRear = Hit(false, false);
            double share = front / Math.Max(1e-9, open);
            bool ok = PxLog.Line("S09 Warrior_Shield_CutsFrontalDamage", "Warrior hit on a Warrior", "unshielded " + PxLog.F(open, "0.0") + ", shield front " + PxLog.F(front, "0.0") + " (x" + PxLog.F(share) + "), shield rear " + PxLog.F(rear, "0.0") + " (unshielded rear " + PxLog.F(openRear, "0.0") + ")", "front x0.40, rear x1.00 of an unshielded rear hit", open > 0 && Math.Abs(share - 0.4) <= 0.02 && Math.Abs(rear - openRear) <= 0.01 * openRear);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S10_Warrior_HillWeight_Counts2()
        {
            Seat s = Pair(ClassId.Warrior, ClassId.Farmer, 2.0);
            World w = s.W;
            double a0 = w.Match.HillSeconds.Azure, c0 = w.Match.HillSeconds.Crimson;
            s.Steps(s.Ticks(3));
            double da = w.Match.HillSeconds.Azure - a0, dc = w.Match.HillSeconds.Crimson - c0;
            bool ok = PxLog.Line("S10 Warrior_HillWeight_Counts2", "Warrior vs one Farmer on the hill, 3 s", "Azure +" + PxLog.F(da) + " s, Crimson +" + PxLog.F(dc) + " s", "Azure > 0, Crimson 0", da > 0 && dc == 0);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ Farmer

        [Test]
        public void S12_Farmer_Roots_SlowsInRadius()
        {
            Seat s = Pair(ClassId.Farmer, ClassId.Warrior, 4.5);
            World w = s.W;
            PlayerSeatRig.Place(w, s.Them + 1, ClassId.Miner, w.X[0] + 4.0 + 1.0, w.Z[0] + 0.5);
            PlayerSeatRig.Place(w, s.Them + 2, ClassId.Builder, w.X[0] + 4.0 + 3.5, w.Z[0]);
            Aim(s, w.X[0] + 4, w.Z[0]);
            s.Human.PressAbility = true;
            s.Step();
            s.Step();
            double a = w.Actors[s.Them].SlowTimer, b = w.Actors[s.Them + 1].SlowTimer, c = w.Actors[s.Them + 2].SlowTimer;
            double want = w.Config.Abilities.FarmerRoots.SlowSeconds;
            bool ok = PxLog.Line("S12 Farmer_Roots_SlowsInRadius", "roots at 4 u", "slow timers in radius " + PxLog.F(a) + ", " + PxLog.F(b) + "; outside " + PxLog.F(c), "~" + PxLog.F(want) + " inside, 0 outside", a > want - 0.1 && b > want - 0.1 && c == 0);
            Assert.IsTrue(ok);
        }

        static Seat Carrier(ClassId cls)
        {
            Seat s = PlayerSeatRig.Field(cls);
            World w = s.W;
            Actor a = w.Actors[0];
            PlayerSeatRig.Place(w, 0, cls, w.Map.Shrine.X, w.Map.Shrine.Z + 2.3);
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 3;
            return s;
        }

        [Test]
        public void S13_Farmer_Feed_AddsAllegiance()
        {
            Seat s = Carrier(ClassId.Farmer);
            World w = s.W;
            double a0 = w.Match.Allegiance.Azure;
            int mark = s.Spy.Mark;
            for (int t = 0; t < 14; t++) { s.Human.HoldInteract = true; s.Step(); }
            double gain = w.Match.Allegiance.Azure - a0;
            bool ok = PxLog.Line("S13 Farmer_Feed_AddsAllegiance", "Farmer feeds 3 units", "allegiance +" + PxLog.F(gain, "0.0") + ", DeliverBiomass " + s.Spy.Since(mark, SimEventType.DeliverBiomass, e => e.A == 0).Count, "+37.5 within 13 ticks", Math.Abs(gain - 37.5) < 0.01);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S14_Farmer_FeedInterrupted_ByHit()
        {
            Seat s = Carrier(ClassId.Farmer);
            World w = s.W;
            int e = PlayerSeatRig.EnemySlot(w);
            PlayerSeatRig.Place(w, e, ClassId.Healer, w.X[0] + 1.0, w.Z[0]);
            s.Them = e;
            s.Dummy = new DummyModel { Me = e, Around = 0 };
            int mark = s.Spy.Mark;
            s.Dummy.PressAttack = true;
            for (int t = 0; t < 14; t++) { s.Human.HoldInteract = true; s.Step(); }
            var cut = s.Spy.Since(mark, SimEventType.DeliverIntercepted, x => x.A == 0);
            bool ok = PxLog.Line("S14 Farmer_FeedInterrupted_ByHit", "lantern hit during the feed wind-up", "DeliverIntercepted " + cut.Count + ", load " + w.Actors[0].CarryAmount + ", hits taken " + s.Spy.Hits(e, 0, mark).Count, "1 intercept, load 3 kept", cut.Count == 1 && w.Actors[0].CarryAmount == 3);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ Miner

        static Seat AtVein(out int vein)
        {
            Seat s = PlayerSeatRig.Field(ClassId.Miner);
            World w = s.W;
            vein = -1;
            for (int i = 0; i < w.Depth.Sources.Length; i++)
            {
                SourceNode sn = w.Depth.Sources[i];
                if (sn != null && sn.Kind == BiomassType.Ore && sn.Charges > 0) { vein = i; PlayerSeatRig.Place(w, 0, ClassId.Miner, sn.X - 1.0, sn.Z); break; }
            }
            return s;
        }

        [Test]
        public void S15_Miner_F_AtVein_MinesOre()
        {
            Seat s = AtVein(out int vein);
            World w = s.W;
            Assume.That(vein >= 0, "the map has an ore vein");
            SourceNode sn = w.Depth.Sources[vein];
            int mark = s.Spy.Mark;
            Aim(s, sn.X, sn.Z);
            s.Human.PressDigPlace = true;
            s.Step();
            StepsAiming(s, Blacksmith.MineTicks(w.Config) + 3, sn.X, sn.Z);
            var mined = s.Spy.Since(mark, SimEventType.OreMined, e => e.A == 0 && e.Value > 0);
            // control: the same press HELD for the whole channel (what a bot's frame does; the human layer latches F for one tick)
            Seat h = AtVein(out int v2);
            int hm = h.Spy.Mark;
            for (int t = 0; t < Blacksmith.MineTicks(h.W.Config) + 4; t++) { Aim(h, sn.X, sn.Z); h.Human.PressDigPlace = true; h.Step(); }
            int heldMined = h.Spy.Since(hm, SimEventType.OreMined, e => e.A == 0 && e.Value > 0).Count;
            bool ok = PxLog.Line("S15 Miner_F_AtVein_MinesOre", "F pressed once (the human layer's one-tick latch) 1 u from a vein", "OreMined " + mined.Count + ", ore carried " + Blacksmith.OreOf(w.Actors[0]) + "; control with F held every tick: OreMined " + heldMined, "OreMined and ore 1 after " + Blacksmith.MineTicks(w.Config) + " ticks", mined.Count >= 1 && Blacksmith.OreOf(w.Actors[0]) >= 1);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S17_Miner_Pick_BreaksBreakable()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Miner, x: 40.5, z: 40.5);
            World w = s.W;
            if (!Open(w, 3, 9, out double ox, out double oz)) Assert.Inconclusive("no open spot");
            PlayerSeatRig.Place(w, 0, ClassId.Miner, ox, oz);
            int cx = (int)Math.Floor(ox) + 1, cz = (int)Math.Floor(oz);
            MapGrid.SetCell(w, cx, cz, Cell.BreakableIntact);
            int mark = s.Spy.Mark;
            Aim(s, cx + 0.5, cz + 0.5);
            s.Human.PressAttack = true;
            s.Step();
            StepsAiming(s, w.Dur.AttackWindup[(int)ClassId.Miner] + 2, cx + 0.5, cz + 0.5);
            var br = s.Spy.Since(mark, SimEventType.WallBreak, e => e.A == 0);
            byte after = w.Map.Cells[cz * w.Map.Width + cx];
            bool ok = PxLog.Line("S17 Miner_Pick_BreaksBreakable", "pick at a breakable 1 u ahead", "WallBreak " + br.Count + ", cell " + after, "WallBreak and the cell steps", br.Count >= 1 && after != Cell.BreakableIntact);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S40_Miner_BankOre_LevelsHut()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Miner);
            World w = s.W;
            PadDef pad = w.Map.Pads[(int)TeamId.Azure];
            PlayerSeatRig.Place(w, 0, ClassId.Miner, pad.X + 1, pad.Z);
            Actor a = w.Actors[0];
            a.Carry = CarryKind.Ore;
            a.CarryAmount = (int)w.Config.Depth.Hut.LevelCost[0];
            int mark = s.Spy.Mark, lvl0 = Blacksmith.Level(w, TeamId.Azure);
            for (int t = 0; t < 3; t++) { s.Human.HoldInteract = t < 2; s.Step(); }
            var dep = s.Spy.Since(mark, SimEventType.BankDeposit, e => e.A == 0);
            var lv = s.Spy.Since(mark, SimEventType.HutLevel);
            bool ok = PxLog.Line("S40 Miner_BankOre_LevelsHut", "bank " + a.CarryAmount + " ore at the pad", "BankDeposit " + dep.Count + ", HutLevel " + lv.Count + ", level " + lvl0 + " -> " + Blacksmith.Level(w, TeamId.Azure), "a deposit and level +1", dep.Count == 1 && Blacksmith.Level(w, TeamId.Azure) == lvl0 + 1);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ Demolitions

        [Test]
        public void S19_Demolitions_Tnt_Blast()
        {
            Seat s = Pair(ClassId.Demolitions, ClassId.Farmer, 4.0);
            World w = s.W;
            int mark = s.Spy.Mark, press = w.Tick + 1;
            s.Human.PressAttack = true;
            s.Steps(70);
            var blast = s.Spy.Hits(0, s.Them, mark).Where(e => e.Cause == DamageCause.Explosion).ToList();
            bool ok = PxLog.Line("S19 Demolitions_Tnt_Blast", "TNT at 4 u on a passive Farmer", blast.Count > 0 ? "blast hit " + PxLog.F(blast[0].Value, "0") + " at press + " + (blast[0].Tick - press) + " ticks" : "no blast hit", "one blast of 46 within 2 s of the press (the audit read 57 ticks; the measured tick is printed)", blast.Count == 1 && Math.Abs(blast[0].Value - 46) < 0.5 && blast[0].Tick - press <= 60);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S20_Demolitions_Mine_Arms_And_Blows()
        {
            Seat s = Pair(ClassId.Demolitions, ClassId.Farmer, 6.0);
            World w = s.W;
            int mark = s.Spy.Mark;
            Aim(s, w.X[0] + 1.6, w.Z[0]);
            s.Human.PressAbility = true;
            s.Step();
            var placed = s.Spy.Since(mark, SimEventType.MinePlaced, e => e.A == 0);
            if (placed.Count == 0) { Assert.IsTrue(PxLog.Line("S20 Demolitions_Mine_Arms_And_Blows", "mine at 1.6 u", "no MinePlaced", "a mine", false)); return; }
            s.Steps(16);
            PlayerSeatRig.Place(w, s.Them, ClassId.Farmer, placed[0].X, placed[0].Z);
            s.Steps(4);
            var boom = s.Spy.Hits(0, s.Them, mark).Where(e => e.Cause == DamageCause.Explosion).ToList();
            bool ok = PxLog.Line("S20 Demolitions_Mine_Arms_And_Blows", "mine at 1.6 u, enemy steps on after 16 ticks", boom.Count > 0 ? "blast " + PxLog.F(boom[0].Value, "0") : "no blast", "70", boom.Count == 1 && Math.Abs(boom[0].Value - 70) < 0.5);
            Assert.IsTrue(ok);
        }

        static int PlaceEnemyBarricade(Seat s, int cx, int cz)
        {
            // a Crimson Miner's barricade, written as Barricades.TryPlaceBarricade writes one (the press path refuses an
            // owner who is not standing in range; the staging only needs the structure)
            World w = s.W;
            int owner = PlayerSeatRig.EnemySlot(w) + 3;
            MinerBarricadeConfig mb = w.Config.Abilities.MinerBarricade;
            for (int sl = 0; sl < w.Barricades.Length; sl++)
            {
                Barricade b = w.Barricades[sl];
                if (b.Active) continue;
                b.Active = true;
                b.Owner = owner;
                b.Team = TeamId.Crimson;
                b.Cx = cx;
                b.Cz = cz;
                b.Hp = mb.MaxHp;
                b.Ttl = mb.LifetimeSeconds;
                b.PrevCell = w.Map.Cells[cz * w.Map.Width + cx];
                MapGrid.SetCell(w, cx, cz, Cell.Barricade);
                return sl;
            }
            return -1;
        }

        [Test]
        public void S36_Tnt_DamagesStructures()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Demolitions, x: 40.5, z: 40.5);
            World w = s.W;
            if (!Open(w, 4, 9, out double ox, out double oz)) Assert.Inconclusive("no open spot");
            PlayerSeatRig.Place(w, 0, ClassId.Demolitions, ox - 2, oz);
            int cx = (int)Math.Floor(ox) + 2, cz = (int)Math.Floor(oz);
            int slot = PlaceEnemyBarricade(s, cx, cz);
            if (slot < 0) { Assert.IsTrue(PxLog.Line("S36 Tnt_DamagesStructures", "TNT on an enemy barricade", "staging: barricade not placed (" + slot + ")", "n/a", false)); return; }
            double hp0 = w.Barricades[slot].Hp;
            int mark = s.Spy.Mark;
            Aim(s, cx + 0.5, cz + 0.5);
            s.Human.PressAttack = true;
            s.Step();
            StepsAiming(s, 70, cx + 0.5, cz + 0.5);
            double hp1 = w.Barricades[slot].Active ? w.Barricades[slot].Hp : 0;
            var ev = s.Spy.Since(mark, SimEventType.StructureHit, e => e.A == 0);
            bool ok = PxLog.Line("S36 Tnt_DamagesStructures", "TNT at an enemy barricade 4 u away", "barricade hp " + PxLog.F(hp0, "0") + " -> " + PxLog.F(hp1, "0") + ", StructureHit " + ev.Count, "hp drops and a StructureHit event", hp1 < hp0 && ev.Count > 0);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S32: a wall swing is not a whiff

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S32_Swing_AtEnemyWall_IsNotAWhiff_AndRaisesAnEvent(ClassId x)
        {
            // ticks from the press until the next swing could start: a swing that chips an enemy barricade vs one that hits a body
            int ReadyAfter(Seat s, double ax, double az, out int mark)
            {
                mark = s.Spy.Mark;
                Aim(s, ax, az);
                s.Human.PressAttack = true;
                s.Step();
                int n = 0;
                while (!Combat.AttackReady(s.W, 0) && n < 200) { Aim(s, ax, az); s.Step(); n++; }
                return n;
            }
            Seat wall = PlayerSeatRig.Field(x, x: 40.5, z: 40.5);
            World w = wall.W;
            wall.NoTnt = true;
            if (!Open(w, 4, 9, out double ox, out double oz)) Assert.Inconclusive("no open spot");
            PlayerSeatRig.Place(w, 0, x, ox, oz);
            int cx = (int)Math.Floor(ox) + 1, cz = (int)Math.Floor(oz);
            int slot = PlaceEnemyBarricade(wall, cx, cz);
            if (slot < 0) { Assert.IsTrue(PxLog.Line("S32 Swing_AtEnemyWall_IsNotAWhiff_AndRaisesAnEvent", x.ToString(), "staging: barricade not placed (" + slot + ")", "n/a", false)); return; }
            double hp0 = w.Barricades[slot].Hp;
            int wallReady = ReadyAfter(wall, cx + 0.5, cz + 0.5, out int wm);
            double hp1 = w.Barricades[slot].Hp;
            var ev = wall.Spy.Since(wm, SimEventType.StructureHit, e => e.A == 0);
            Seat body = Pair(x, ClassId.Farmer, 0.8 * w.Config.Class(x).AttackRange);
            body.NoTnt = true;
            int bodyReady = ReadyAfter(body, body.W.X[body.Them], body.W.Z[body.Them], out int bm);
            bool chipped = hp1 < hp0;
            bool ok = PxLog.Line("S32 Swing_AtEnemyWall_IsNotAWhiff_AndRaisesAnEvent", x + " swings at an enemy barricade 1 u ahead",
                "chipped " + PxLog.F(hp0 - hp1, "0") + " hp; next swing ready after " + wallReady + " ticks (a hit on a body: " + bodyReady + "); StructureHit " + ev.Count,
                "chip, same recovery as a hit, a StructureHit event", chipped && wallReady <= bodyReady && ev.Count > 0);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ Builder

        static void SelectTool(Seat s, Func<World, bool> picked, int max = 10)
        {
            for (int k = 0; k < max && !picked(s.W); k++)
            {
                s.Human.PressSwitch = true;
                s.Step();
                s.Step();
            }
        }

        static bool TallWall(World w) => !BuilderWalls.TerrainTool(w, 0) && BuilderWalls.Piece(w, 0) == BuildPiece.Wall && BuilderWalls.TallSelected(w, 0);

        [Test]
        public void S21_Builder_Wall_PlacedWithWood()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Builder, x: 40.5, z: 40.5);
            World w = s.W;
            int[] tier = w.Config.Depth.Build.TierAtHutLevel;
            for (int k = 0; k < tier.Length; k++) tier[k] = 1;
            if (!Open(w, 4, 10, out double ox, out double oz)) Assert.Inconclusive("no open spot");
            PlayerSeatRig.Place(w, 0, ClassId.Builder, ox, oz);
            SelectTool(s, TallWall);
            w.Walls.Credit[0] = 2;
            for (int k = 0; k < Sources.StockKinds; k++) w.Depth.Stock[k] = 0;
            int wood0 = BuilderWalls.Wood(w, TeamId.Azure), mark = s.Spy.Mark;
            Aim(s, ox + 2.5, oz);
            s.Human.PressDigPlace = true;
            s.Step();
            StepsAiming(s, 2, ox + 2.5, oz);
            var built = s.Spy.Since(mark, SimEventType.WallBuilt, e => e.A == 0);
            int wood1 = BuilderWalls.Wood(w, TeamId.Azure);
            bool ok = PxLog.Line("S21 Builder_Wall_PlacedWithWood", "TALL WALL 2.5 u off the hill with 2 wood (tool selected " + TallWall(w) + ")", "WallBuilt " + built.Count + (built.Count > 0 ? " value " + built[0].Value : "") + ", wood " + wood0 + " -> " + wood1 + ", owned cells " + BuilderWalls.OwnedCells(w, 0), "a wall, wood -2", built.Count >= 1 && wood1 == wood0 - 2);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S22_Builder_Wall_RefusedNearHill_Reported()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Builder, x: double.NaN);
            World w = s.W;
            int[] tier = w.Config.Depth.Build.TierAtHutLevel;
            for (int k = 0; k < tier.Length; k++) tier[k] = 1;
            PlayerSeatRig.Place(w, 0, ClassId.Builder, w.Map.Hill.X - 3, w.Map.Hill.Z);
            SelectTool(s, TallWall);
            w.Walls.Credit[0] = 5;
            int mark = s.Spy.Mark;
            Aim(s, w.X[0] + 2.5, w.Z[0]);
            s.Human.PressDigPlace = true;
            s.Step();
            StepsAiming(s, T.PressAnswerTicks, w.X[0] + 2.5, w.Z[0]);
            var said = s.Spy.AboutActor(0, mark).Where(e => (e.Type == SimEventType.WallBuilt && e.Value < 0) || e.Type == SimEventType.ActionRefused).ToList();
            bool ok = PxLog.Line("S22 Builder_Wall_RefusedNearHill_Reported", "wall 3 u from the hill centre", said.Count > 0 ? string.Join(" ", said) : "no refusal event (ghost why " + BuilderWalls.GhostWhy(w, 0) + ", events " + string.Join(" ", s.Spy.AboutActor(0, mark)) + ")", "a refusal event with its code", said.Count > 0);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S38_Builder_Dig_ChangesCell_AndRaisesEvent()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Builder, x: 40.5, z: 40.5);
            World w = s.W;
            if (!Open(w, 3, 10, out double ox, out double oz)) Assert.Inconclusive("no open spot");
            PlayerSeatRig.Place(w, 0, ClassId.Builder, ox, oz);
            w.Walls.Tool[0] = 0; // DIG
            int tx = (int)Math.Floor(ox + 1.6), tz = (int)Math.Floor(oz);
            byte c0 = w.Map.Cells[tz * w.Map.Width + tx];
            int h0 = w.Map.Lift != null ? w.Map.Lift[tz * w.Map.Width + tx] : 0;
            int mark = s.Spy.Mark;
            Aim(s, ox + 1.6, oz);
            s.Human.PressDigPlace = true;
            s.Step();
            StepsAiming(s, 2, ox + 1.6, oz);
            var dug = s.Spy.Since(mark, SimEventType.Terraform, e => e.A == 0);
            byte c1 = w.Map.Cells[tz * w.Map.Width + tx];
            int h1 = w.Map.Lift != null ? w.Map.Lift[tz * w.Map.Width + tx] : 0;
            bool ok = PxLog.Line("S38 Builder_Dig_ChangesCell_AndRaisesEvent", "DIG 1.6 u ahead on open floor", "Terraform " + (dug.Count > 0 ? "value " + dug[0].Value : "none") + ", cell " + c0 + "/" + h0 + " -> " + c1 + "/" + h1, "Terraform Dug and the cell changed", dug.Count > 0 && dug[0].Value == Terraform.Dug && (c1 != c0 || h1 != h0));
            Assert.IsTrue(ok);
        }

        static Seat Strip()
        {
            // BuilderTests.Strip's void gap and one-cell cliff, under Full rules, the Builder in the human seat
            Seat s = PlayerSeatRig.Field(ClassId.Builder, x: 7.5, z: 13.5);
            World w = s.W;
            MapDef map = w.Map;
            Array.Fill(map.Cells, Cell.Void);
            Array.Clear(map.WallHp, 0, map.WallHp.Length);
            void Paint(int x0, int z0, int x1, int z1, byte t) { for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) map.Cells[z * map.Width + x] = t; }
            Paint(2, 10, 8, 16, Cell.Floor);
            Paint(13, 8, 20, 18, Cell.Floor);
            Paint(21, 8, 21, 18, Cell.Wall);
            Paint(22, 8, 34, 18, Cell.Floor);
            Paint(2, 20, 34, 22, Cell.Floor);
            Paint(2, 17, 4, 19, Cell.Floor);
            Paint(13, 19, 15, 19, Cell.Floor);
            Paint(30, 19, 32, 19, Cell.Floor);
            for (int t = 0; t < 2; t++) { map.Pads[t].X = 5.5; map.Pads[t].Z = 13.5; }
            map.Hill.X = 28.5; map.Hill.Z = 13.5; map.Hill.R = 3;
            map.Shrine.X = 100.5; map.Shrine.Z = 60.5;
            foreach (ResourceNode nd in w.Nodes) { nd.X = -100; nd.Z = -100; }
            if (w.Depth.Sources != null) foreach (SourceNode sn in w.Depth.Sources) if (sn != null) { sn.X = -100; sn.Z = -100; }
            PlayerSeatRig.Place(w, 0, ClassId.Builder, 7.5, 13.5);
            w.Actors[0].AbilityCd = 0;
            return s;
        }

        [Test]
        public void S39_Builder_Bridge_Ladder_Placed()
        {
            Seat s = Strip();
            int mark = s.Spy.Mark;
            Aim(s, 20, 13.5);
            s.Human.PressAbility = true;
            s.Step();
            var bridge = s.Spy.Since(mark, SimEventType.PiecePlaced, e => e.A == 0);
            Seat l = Strip();
            PlayerSeatRig.Place(l.W, 0, ClassId.Builder, 19.5, 13.5);
            int lm = l.Spy.Mark;
            Aim(l, 30, 13.5);
            l.Human.PressAbility = true;
            l.Step();
            var ladder = l.Spy.Since(lm, SimEventType.PiecePlaced, e => e.A == 0);
            bool ok = PxLog.Line("S39 Builder_Bridge_Ladder_Placed", "ability over a void gap / at a one-cell cliff (Full rules)", "bridge PiecePlaced " + bridge.Count + " (" + string.Join(" ", s.Spy.AboutActor(0, mark)) + "), ladder PiecePlaced " + ladder.Count + " (" + string.Join(" ", l.Spy.AboutActor(0, lm)) + ")", "one each", bridge.Count == 1 && ladder.Count == 1);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ stations (S23 / S24)

        static Seat AtStation(ClassId cls)
        {
            Seat s = PlayerSeatRig.Field(cls);
            World w = s.W;
            PadDef pad = w.Map.Pads[(int)TeamId.Azure];
            PlayerSeatRig.Place(w, 0, cls, pad.X + 1, pad.Z);
            for (int k = 0; k < Sources.StockKinds; k++) w.Depth.Stock[k] = 999;
            return s;
        }

        [Test]
        public void S23_Builder_StationBuy_Levels()
        {
            Seat s = AtStation(ClassId.Builder);
            World w = s.W;
            int lvl0 = Stations.Level(w, TeamId.Azure, ClassId.Warrior), mark = s.Spy.Mark;
            s.Human.StationBuy = (int)ClassId.Warrior + 1;
            s.Step();
            s.Steps(T.PressAnswerTicks);
            var up = s.Spy.Since(mark, SimEventType.Upgrade, e => e.A == 0);
            var refused = s.Spy.Since(mark, SimEventType.ActionRefused, e => e.A == 0);
            bool ok = PxLog.Line("S23 Builder_StationBuy_Levels", "Builder buys a Warrior upgrade in his station (Full rules, stock 999; in station " + Stations.InStation(w, 0) + ", stations on " + Stations.On(w) + ", hut on " + Blacksmith.On(w) + ")", "Upgrade " + (up.Count > 0 ? "value " + up[0].Value : "none") + ", level " + lvl0 + " -> " + Stations.Level(w, TeamId.Azure, ClassId.Warrior) + (refused.Count > 0 ? ", ActionRefused B " + refused[0].B + " V " + refused[0].Value : ""), "Full rules: the hut retired station upgrades: ActionRefused B 8 (ButtonStationBuy) Value 13 (RefusedRuleOff)", refused.Any(e => e.B == 8 && (int)e.Value == 13));
            Assert.IsTrue(ok);
        }

        [Test]
        public void S24_NonBuilder_StationBuy_TellsWhy()
        {
            Seat s = AtStation(ClassId.Warrior);
            World w = s.W;
            int mark = s.Spy.Mark;
            s.Human.StationBuy = (int)ClassId.Warrior + 1;
            s.Step();
            s.Steps(T.PressAnswerTicks);
            var up = s.Spy.Since(mark, SimEventType.Upgrade, e => e.A == 0);
            var refused = s.Spy.Since(mark, SimEventType.ActionRefused, e => e.A == 0);
            bool ok = PxLog.Line("S24 NonBuilder_StationBuy_TellsWhy", "Warrior presses buy in his station", up.Count > 0 ? "Upgrade value " + up[0].Value : refused.Count > 0 ? "ActionRefused B " + refused[0].B + " V " + refused[0].Value : "SILENT", "Full rules: ActionRefused B 8 (ButtonStationBuy) Value 13 (RefusedRuleOff)", refused.Any(e => e.B == 8 && (int)e.Value == 13));
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ Healer / any class

        [Test]
        public void S27_Healer_Pulse_HealsInRadius()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Healer);
            World w = s.W;
            PlayerSeatRig.Place(w, 1, ClassId.Warrior, w.X[0] + 4, w.Z[0] + 1);
            PlayerSeatRig.Place(w, 2, ClassId.Farmer, w.X[0] + 4 + 3.4, w.Z[0] + 2.5);
            w.Actors[1].Hp = w.Actors[1].MaxHp - 50;
            w.Actors[2].Hp = w.Actors[2].MaxHp - 50;
            double h1 = w.Actors[1].Hp, h2 = w.Actors[2].Hp;
            Aim(s, w.X[0] + 4, w.Z[0]);
            s.Human.PressAbility = true;
            s.Step();
            s.Step();
            double g1 = w.Actors[1].Hp - h1, g2 = w.Actors[2].Hp - h2;
            bool ok = PxLog.Line("S27 Healer_Pulse_HealsInRadius", "pulse at 4 u", "ally in radius +" + PxLog.F(g1, "0.0") + ", ally outside +" + PxLog.F(g2, "0.0"), "+30 inside, 0 outside", Math.Abs(g1 - 30) < 0.5 && g2 < 0.5);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S28_AnyClass_Dodge_IFrames()
        {
            Seat s = Pair(ClassId.Farmer, ClassId.Warrior, 1.5);
            World w = s.W;
            int wu = w.Dur.AttackWindup[(int)ClassId.Warrior];
            int mark = s.Spy.Mark;
            s.Dummy.PressAttack = true;
            for (int t = 0; t < wu + 3; t++)
            {
                if (t == wu - 2)
                {
                    // dodge TOWARD the attacker so the body stays in reach: only the i-frames can save it
                    s.Human.PressDodge = true;
                    s.Human.MoveOverrideX = 1;
                    s.Human.MoveOverrideZ = 0;
                }
                s.Step();
            }
            int hits = s.Spy.Hits(s.Them, 0, mark).Count, contacts = s.Spy.Contacts(s.Them, mark).Count, dodges = s.Spy.Since(mark, SimEventType.Dodge, e => e.A == 0).Count;
            bool ok = PxLog.Line("S28 AnyClass_Dodge_IFrames", "dodge 2 ticks before a Warrior's contact", "hits " + hits + " (contact " + contacts + ", dodge " + dodges + ")", "no hit", hits == 0 && contacts == 1 && dodges == 1);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S29_AnyClass_Cancel_FeintRefund()
        {
            Seat s = Pair(ClassId.Warrior, ClassId.Farmer, 1.5);
            World w = s.W;
            int mark = s.Spy.Mark;
            s.Human.PressAttack = true;
            s.Step();
            s.Step();
            s.Human.PressCancel = true;
            s.Step();
            double cd = w.Actors[0].AttackCd, full = w.Config.Class(ClassId.Warrior).AttackCooldown;
            s.Steps(w.Dur.AttackWindup[(int)ClassId.Warrior] + 2);
            int contacts = s.Spy.Contacts(0, mark).Count, feints = s.Spy.Since(mark, SimEventType.Feint, e => e.A == 0).Count;
            bool ok = PxLog.Line("S29 AnyClass_Cancel_FeintRefund", "cancel 2 ticks into a Warrior wind-up", "contacts " + contacts + ", Feint " + feints + ", cooldown left " + PxLog.F(cd) + " of " + PxLog.F(full), "no contact, a Feint, cooldown <= 0.3 x full", contacts == 0 && feints == 1 && cd <= 0.3 * full + 1.0 / w.Config.SimHz + 1e-9);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S31_Respawn_Timer_MatchesCurve()
        {
            var rows = new List<string>();
            bool all = true;
            foreach (double at in new[] { 0.0, 470.0 })
            {
                Seat s = PlayerSeatRig.Field(ClassId.Warrior, teamSize: 16);
                World w = s.W;
                s.Steps(2);
                if (at > 0) w.Tick = (int)(at * w.Config.SimHz);   // the curve reads the match clock in ticks
                double curve = Comeback.RespawnDelay(w, TeamId.Azure);
                Damage.ApplyHit(w, 0, 9999, -1, w.X[0] + 1, w.Z[0], DamageCause.Melee, 0, 0, 0, 0);
                int ticks = 0;
                while (!w.Actors[0].Alive && ticks < 600) { s.Human.MoveOverrideX = 0.5; s.Step(); ticks++; }
                double secs = ticks / w.Config.SimHz;
                double expect = at <= 0 ? 3 : 8 - 5 * (1 - at * at / (480.0 * 480.0));
                bool ok = Math.Abs(secs - curve) <= 2.5 / w.Config.SimHz && curve >= 2.9 && curve <= 8.1 && (at > 0 ? curve >= 7.5 : curve <= 3.1);
                rows.Add("16v16, death at " + at + " s: respawn after " + PxLog.F(secs) + " s (curve " + PxLog.F(curve) + ")");
                all &= ok;
            }
            Assert.IsTrue(PxLog.Line("S31 Respawn_Timer_MatchesCurve", "deaths at 0:00 and 7:50 (16v16)", string.Join("; ", rows), "the respawn equals the curve; ~3 s at 0:00, ~8 s near 8:00", all));
        }

        [Test]
        public void S37_Kaiju_HitByPlayer_RaisesVisibleEvent()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Warrior);
            World w = s.W;
            w.ForceHatch = true;
            s.Step();
            if (!w.Match.Hatched) { Assert.IsTrue(PxLog.Line("S37 Kaiju_HitByPlayer_RaisesVisibleEvent", "Warrior swings at the Kaiju", "staging: no hatch", "n/a", false)); return; }
            Kaiju k = w.Kaiju;
            double kx = w.X[0] + w.Config.Kaiju.Radius + 1.0, kz = w.Z[0];
            s.BeforeTick = q => { k.X = kx; k.Z = kz; k.PrevX = kx; k.PrevZ = kz; k.SlamCd = 1000; k.MeleeCd = 1000; k.MeleeWindupIn = 0; };
            int mark = s.Spy.Mark;
            Aim(s, kx, kz);
            s.Human.PressAttack = true;
            s.Step();
            StepsAiming(s, w.Dur.AttackWindup[(int)ClassId.Warrior] + 2, kx, kz);
            var dmg = s.Spy.Since(mark, SimEventType.KaijuDamaged, e => e.A == 0);
            bool ok = PxLog.Line("S37 Kaiju_HitByPlayer_RaisesVisibleEvent", "Warrior swings at the hatched Kaiju (state " + k.State + ")", dmg.Count > 0 ? "KaijuDamaged value " + PxLog.F(dmg[0].Value, "0.0") : "no KaijuDamaged", "an event carrying the damage", dmg.Count > 0 && dmg[0].Value > 0);
            Assert.IsTrue(ok);
        }
    }
}

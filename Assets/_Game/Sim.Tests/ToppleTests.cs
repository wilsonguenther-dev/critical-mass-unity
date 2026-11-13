using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// CM-31 knock-down decorations: port of src/sim/topple.test.ts (09e0904, 10 cases) on the REAL Skybreak Basin map: the
    /// same pillar knocked one way lands as Bridge cells joining the base island to the overlook ledge; knocked the other
    /// way it walls off the B2 mouth. Then DG-09: the hit / falling / down events with positions, the swing through the
    /// real Combat step, and a restart. Time is in sim ticks.
    /// </summary>
    [TestFixture]
    public sealed class ToppleTests
    {
        private const int AzurePillar = 0;
        private const int CrimsonPillar = 1;
        /// <summary>A base-island cell south of the Azure pillar (the near side of both measurements).</summary>
        private static readonly PointXZ BaseSide = new PointXZ(10.5, 30.5);
        /// <summary>The Azure overlook ledge centre and the NW courtyard centre (the two far sides).</summary>
        private static readonly PointXZ Overlook = new PointXZ(11.5, 18.5);
        private static readonly PointXZ NwCourtyard = new PointXZ(28.5, 18.5);

        private static ToppleConfig TC => GameConfig.CreateBrowser5v5().Abilities.Topple;

        private static World MakeWorld()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 31, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateBrowser5v5() });
            w.Tick = 100;
            return w;
        }

        private static double DistAt(World w, PointXZ goal, PointXZ from)
        {
            FlowField f = FlowField.ComputeFlowField(w.Map, new[] { goal });
            return f.Dist[(int)Math.Floor(from.Z) * w.Map.Width + (int)Math.Floor(from.X)];
        }

        private static SimEvent LastEvent(World w) => w.Events[(w.EventHead - 1 + World.EventCapacity) % World.EventCapacity];

        private static int[] Foot(World w, int k, int mode)
        {
            DecorationDef d = w.Map.Decorations[k];
            var output = new int[Topple.FootprintMax(w.Config)];
            int n = Topple.Footprint(w.Config, w.Map, d, Topple.Dir(d, mode), output);
            return output.Take(n).ToArray();
        }

        private static PointXZ Mirror(World w, PointXZ p) => new PointXZ(w.Map.Width - p.X, p.Z);

        private static double CosHalf(ClassConfig cc) => JsMath.Cos(cc.AttackArcDeg / 2 * Math.PI / 180);

        // ------------------------------------------------------------------------------------------- ported (09e0904)

        [Test]
        public void OnePillarPerBaseMirroredStandingOnItsBaseIslandFloorWithFullHits()
        {
            World w = MakeWorld();
            DecorationDef[] decs = w.Map.Decorations;
            Assert.AreEqual(2, decs.Length);
            DecorationDef a = decs[AzurePillar];
            DecorationDef c = decs[CrimsonPillar];
            Assert.AreEqual(w.Map.Width - 1 - a.Cx, c.Cx);
            Assert.AreEqual(a.Cz, c.Cz);
            Assert.AreEqual(-a.Ramp.Dx, c.Ramp.Dx);
            Assert.AreEqual(a.Ramp.Dz, c.Ramp.Dz);
            Assert.AreEqual(-a.Block.Dx, c.Block.Dx);
            Assert.AreEqual(a.Block.Dz, c.Block.Dz);
            foreach (DecorationDef d in decs) Assert.AreEqual(Cell.Floor, w.Map.Cells[d.Cz * w.Map.Width + d.Cx]);
            CollectionAssert.AreEqual(new[] { (byte)TC.HitsToTopple, (byte)TC.HitsToTopple }, w.Topple.Hp);
            CollectionAssert.AreEqual(new[] { (byte)Topple.Standing, (byte)Topple.Standing }, w.Topple.Fallen);
            Assert.AreEqual(double.PositiveInfinity, DistAt(w, Overlook, BaseSide), "the overlook ledge is joined to nothing before a topple");
        }

        [Test]
        public void DirectionANorthTheFootprintsVoidCellsBecomeBridgeAndTheOverlookGoesFromUnreachableToFinite()
        {
            World w = MakeWorld();
            int[] cells = Foot(w, AzurePillar, Topple.Ramp);
            Assert.AreEqual((int)(TC.LengthCells * TC.WidthCells), cells.Length);
            int[] voids = cells.Where(i => w.Map.Cells[i] == Cell.Void).ToArray();
            int offIsland = cells.Count(i => w.Map.Cells[i] == Cell.Void || w.Map.Cells[i] == Cell.Bridge);
            Assert.GreaterOrEqual(offIsland, (int)TC.WidthCells * 3, "a 3-cell gap as wide as a bridge (counted off-island: CM-33's wider raster)");
            Assert.Greater(voids.Length, 0);
            Assert.AreEqual(double.PositiveInfinity, DistAt(w, Overlook, BaseSide));
            int wv = w.WallVersion;
            Assert.AreEqual(Topple.Ramp, Topple.ToppleDecoration(w, AzurePillar, Topple.Ramp, -1));
            foreach (int i in voids) Assert.AreEqual(Cell.Bridge, w.Map.Cells[i]);
            foreach (int i in cells) Assert.IsTrue(MapGrid.IsWalkableType(w.Map.Cells[i]));
            Assert.IsTrue(double.IsFinite(DistAt(w, Overlook, BaseSide)));
            Assert.Greater(w.WallVersion, wv);
            Assert.AreEqual(w.Tick, w.WallChangeTick);
            Assert.AreEqual(Topple.Ramp, w.Topple.Fallen[AzurePillar]);
            Assert.AreEqual(100, w.Topple.FallTick[AzurePillar]);
            SimEvent e = LastEvent(w);
            Assert.AreEqual(SimEventType.Topple, e.Type);
            Assert.AreEqual(AzurePillar, e.B);
            Assert.AreEqual(Topple.Ramp, e.Value);
        }

        [Test]
        public void DirectionBEastTheFootprintsWalkableCellsBecomeWallAcrossTheB2MouthAndTheDistanceAcrossRises()
        {
            World w = MakeWorld();
            int[] cells = Foot(w, AzurePillar, Topple.Block);
            int[] walk = cells.Where(i => MapGrid.IsWalkableType(w.Map.Cells[i])).ToArray();
            Assert.IsTrue(walk.Any(i => w.Map.Cells[i] == Cell.Bridge), "it lies across the bridge mouth");
            double before = DistAt(w, NwCourtyard, BaseSide);
            Assert.IsTrue(double.IsFinite(before));
            Assert.AreEqual(Topple.Block, Topple.ToppleDecoration(w, AzurePillar, Topple.Block, -1));
            foreach (int i in walk) Assert.AreEqual(Cell.Wall, w.Map.Cells[i]);
            double after = DistAt(w, NwCourtyard, BaseSide);
            Assert.IsTrue(double.IsFinite(after), "the long way round, never cut off");
            Assert.Greater(after, before + 10);
            Assert.IsTrue(Terraform.PadsReachHill(w.Map));
            Assert.AreEqual(Topple.Block, w.Topple.Fallen[AzurePillar]);
            Assert.AreEqual(Topple.Block, LastEvent(w).Value);
        }

        [Test]
        public void TheCrimsonPillarDoesTheSameMirrored()
        {
            World w = MakeWorld();
            Assert.AreEqual(double.PositiveInfinity, DistAt(w, Mirror(w, Overlook), Mirror(w, BaseSide)));
            Assert.AreEqual(Topple.Ramp, Topple.ToppleDecoration(w, CrimsonPillar, Topple.Ramp, -1));
            Assert.IsTrue(double.IsFinite(DistAt(w, Mirror(w, Overlook), Mirror(w, BaseSide))));
            World w2 = MakeWorld();
            double before = DistAt(w2, Mirror(w2, NwCourtyard), Mirror(w2, BaseSide));
            Assert.AreEqual(Topple.Block, Topple.ToppleDecoration(w2, CrimsonPillar, Topple.Block, -1));
            Assert.Greater(DistAt(w2, Mirror(w2, NwCourtyard), Mirror(w2, BaseSide)), before + 10);
        }

        [Test]
        public void ThePushPicksTheWayHitFromTheSouthItFallsNorthRampHitFromTheWestItFallsEastBlock()
        {
            World w = MakeWorld();
            DecorationDef d = w.Map.Decorations[AzurePillar];
            Assert.AreEqual(Topple.Ramp, Topple.ModeFor(d, d.Cx + 0.5, d.Cz + 2));
            Assert.AreEqual(Topple.Block, Topple.ModeFor(d, d.Cx - 1, d.Cz + 0.5));
        }

        [Test]
        public void MeleeSwingsChipItHitsToToppleSwingsInTheArcKnockItOverAwayFromTheAttackerOutOfArcSwingsDoNothing()
        {
            World w = MakeWorld();
            DecorationDef d = w.Map.Decorations[AzurePillar];
            ClassConfig cc = w.Config.Class(ClassId.Warrior);
            double cosHalf = CosHalf(cc);
            double ax = d.Cx + 0.5 - 1;
            double az = d.Cz + 0.5;
            Topple.ToppleInArc(w, 0, ax, az, -1, 0, cc.AttackRange, cosHalf); // facing away (west): never counts
            Assert.AreEqual((byte)TC.HitsToTopple, w.Topple.Hp[AzurePillar]);
            for (int s = 1; s < TC.HitsToTopple; s++)
            {
                Topple.ToppleInArc(w, 0, ax, az, 1, 0, cc.AttackRange, cosHalf);
                Assert.AreEqual(Topple.Standing, w.Topple.Fallen[AzurePillar]);
                Assert.AreEqual(TC.HitsToTopple - s, w.Topple.Hp[AzurePillar]);
            }
            Topple.ToppleInArc(w, 0, ax, az, 1, 0, cc.AttackRange, cosHalf);
            Assert.AreEqual(Topple.Block, w.Topple.Fallen[AzurePillar]);
            Assert.AreEqual(0, LastEvent(w).A);
        }

        [Test]
        public void ABlockThatWouldDisconnectAPadFromTheHillIsRefusedByTheRouteGuardTheGridIsUntouched()
        {
            World w = MakeWorld();
            MapDef map = w.Map;
            // Leave B2 -> NW courtyard -> B3 as Azure's only route to the hill: drop the west bridges south of row 33.
            for (int cz = 33; cz < map.Height; cz++)
                for (int cx = 0; cx < 50; cx++)
                    if (map.Cells[cz * map.Width + cx] == Cell.Bridge) map.Cells[cz * map.Width + cx] = Cell.Void;
            Assert.IsTrue(Terraform.PadsReachHill(map));
            byte[] snapshot = (byte[])map.Cells.Clone();
            int gv = w.GridVersion;
            Assert.AreEqual(Topple.RefusedRoute, Topple.ToppleDecoration(w, AzurePillar, Topple.Block, -1));
            CollectionAssert.AreEqual(snapshot, map.Cells);
            Assert.AreEqual(gv, w.GridVersion);
            Assert.AreEqual(Topple.Standing, w.Topple.Fallen[AzurePillar]);
            Assert.AreEqual(Topple.RefusedRoute, LastEvent(w).Value);
            Assert.AreEqual(Topple.Ramp, Topple.ToppleDecoration(w, AzurePillar, Topple.Ramp, -1), "the ramp only adds ground");
        }

        [Test]
        public void ARefusedLastSwingLeavesThePieceOneHitFromFalling()
        {
            World w = MakeWorld();
            DecorationDef d = w.Map.Decorations[AzurePillar];
            w.WallChangeTick = w.Tick;
            ClassConfig cc = w.Config.Class(ClassId.Warrior);
            double cosHalf = CosHalf(cc);
            for (int s = 0; s < TC.HitsToTopple; s++) Topple.ToppleInArc(w, 0, d.Cx - 0.5, d.Cz + 0.5, 1, 0, cc.AttackRange, cosHalf);
            Assert.AreEqual(Topple.RefusedRate, LastEvent(w).Value);
            Assert.AreEqual(1, w.Topple.Hp[AzurePillar]);
            w.Tick += (int)w.Config.Abilities.MinerTerraform.MinGapTicks;
            Topple.ToppleInArc(w, 0, d.Cx - 0.5, d.Cz + 0.5, 1, 0, cc.AttackRange, cosHalf);
            Assert.AreEqual(Topple.Block, w.Topple.Fallen[AzurePillar]);
        }

        [Test]
        public void ABlockIsRefusedWhileALivingActorStandsInACellItWouldWallInAFallenPieceCannotFallAgain()
        {
            World w = MakeWorld();
            int[] cells = Foot(w, AzurePillar, Topple.Block).Where(i => MapGrid.IsWalkableType(w.Map.Cells[i])).ToArray();
            int c0 = cells[0];
            w.Actors[0].Alive = true;
            w.X[0] = (c0 % w.Map.Width) + 0.5;
            w.Z[0] = c0 / w.Map.Width + 0.5;
            Assert.AreEqual(Topple.RefusedOccupied, Topple.ToppleDecoration(w, AzurePillar, Topple.Block, -1));
            Assert.AreNotEqual(Cell.Wall, w.Map.Cells[c0]);
            w.X[0] = 8;
            w.Z[0] = 36;
            Assert.AreEqual(Topple.Block, Topple.ToppleDecoration(w, AzurePillar, Topple.Block, -1));
            w.Tick += 100;
            Assert.AreEqual(Topple.RefusedFallen, Topple.ToppleDecoration(w, AzurePillar, Topple.Ramp, -1));
        }

        [Test]
        public void ResetWorldStandsEveryPieceBackUpAndRestoresTheCells()
        {
            World w = MakeWorld();
            int[] cells = Foot(w, AzurePillar, Topple.Ramp);
            byte[] before = cells.Select(i => w.Map.Cells[i]).ToArray();
            Assert.AreEqual(Topple.Ramp, Topple.ToppleDecoration(w, AzurePillar, Topple.Ramp, -1));
            World.ResetWorld(w, new WorldOptions { Seed = 31, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior });
            CollectionAssert.AreEqual(before, cells.Select(i => w.Map.Cells[i]).ToArray());
            Assert.AreEqual(Topple.Standing, w.Topple.Fallen[AzurePillar]);
            Assert.AreEqual((byte)TC.HitsToTopple, w.Topple.Hp[AzurePillar]);
        }

        // ------------------------------------------------------------------------------------------- DG-09

        private static List<SimEvent> EventsSince(World w, int head)
        {
            var list = new List<SimEvent>();
            for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                list.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value });
            }
            return list;
        }

        /// <summary>
        /// A Farmer (any class) standing south of the Azure pillar swings through the REAL step (Combat): three ToppleHit
        /// events (2, 1, 0 hits left) at the base, one Topple (Ramp, it falls north, away from her) and, FallTicks later,
        /// one ToppleDown at the far end of the footprint; FallPhase runs 0 to 1 over those ticks.
        /// </summary>
        [Test]
        public void DG09AnyClassSwingingInTheRealStepEmitsHitHitHitFallingThenDownWithPositions()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 31, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Farmer, Config = GameConfig.CreateBrowser5v5() });
            for (int i = 1; i < w.ActorCount; i++)
            {
                w.Actors[i].Alive = false;
                w.Actors[i].RespawnIn = 1e9;
            }
            DecorationDef d = w.Map.Decorations[AzurePillar];
            double bx = d.Cx + 0.5, bz = d.Cz + 0.5;
            w.X[0] = bx;
            w.Z[0] = bz + 1;
            w.PrevX[0] = w.X[0];
            w.PrevZ[0] = w.Z[0];
            InputFrame f = SimInput.EmptyInput();
            f.Attack = true;
            f.AimX = bx;
            f.AimZ = bz - 1; // face north, across the pillar
            int head = w.EventHead;
            int fallTick = -1;
            for (int t = 0; t < 400 && fallTick < 0; t++)
            {
                SimInput.ApplyInput(w, 0, f);
                SimStep.Step(w);
                if (w.Topple.Fallen[AzurePillar] != Topple.Standing) fallTick = w.Tick;
            }
            Assert.Greater(fallTick, 0, "three swings knocked it over");
            Assert.AreEqual(Topple.Ramp, w.Topple.Fallen[AzurePillar], "pushed from the south it falls north: the ramp");
            Assert.AreEqual(fallTick, w.Topple.FallTick[AzurePillar]);
            Assert.AreEqual(0, Topple.FallPhase(w, AzurePillar, 1.0 / TC.FallTicks) * TC.FallTicks, 1e-9);
            f.Attack = false;
            for (int t = 0; t < TC.FallTicks + 2; t++)
            {
                SimInput.ApplyInput(w, 0, f);
                SimStep.Step(w);
            }
            Assert.AreEqual(1, Topple.FallPhase(w, AzurePillar, 0));
            List<SimEvent> ev = EventsSince(w, head);
            List<SimEvent> hits = ev.Where(e => e.Type == SimEventType.ToppleHit && e.B == AzurePillar).ToList();
            CollectionAssert.AreEqual(new double[] { 2, 1, 0 }, hits.Select(e => e.Value).ToArray());
            foreach (SimEvent e in hits)
            {
                Assert.AreEqual(0, e.A);
                Assert.AreEqual(bx, e.X);
                Assert.AreEqual(bz, e.Z);
            }
            SimEvent fall = ev.Single(e => e.Type == SimEventType.Topple);
            Assert.AreEqual(Topple.Ramp, fall.Value);
            Assert.AreEqual(0, fall.A);
            Assert.AreEqual(bx, fall.X);
            Assert.AreEqual(bz, fall.Z);
            SimEvent down = ev.Single(e => e.Type == SimEventType.ToppleDown);
            Assert.AreEqual(AzurePillar, down.B);
            Assert.AreEqual(Topple.Ramp, down.Value);
            Assert.AreEqual(bx + d.Ramp.Dx * TC.LengthCells, down.X);
            Assert.AreEqual(bz + d.Ramp.Dz * TC.LengthCells, down.Z);
            Assert.IsTrue(double.IsFinite(DistAt(w, Overlook, BaseSide)), "the ramp joins the overlook ledge");
        }
    }
}

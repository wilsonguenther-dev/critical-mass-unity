using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/collide.test.ts (09e0904).</summary>
    [TestFixture]
    public sealed class CollideTests
    {
        /// <summary>
        /// DG-02 / CM-33 (D-069 on cm/CM-33): a body standing on a diagonal bridge deck overlaps the VOID notches of the
        /// cell staircase under the drawn deck by design; that is not a blocked-cell overlap. Walls, water, breakables,
        /// barricades and the map edge still count (DeckSpanAt is null when any of them touch the body).
        /// </summary>
        private static bool OnDeckOverVoid(World w, int i, int cx, int cz)
        {
            if (!MapGrid.InBounds(w.Map, cx, cz) || w.Map.Cells[cz * w.Map.Width + cx] != Cell.Void) return false;
            return Collide.DeckSpanAt(w, w.X[i], w.Z[i], Collide.ActorRadius(w, i)) != null;
        }

        private static World MakeWorld(int seed, int teamSize, ClassId humanClass)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = seed,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = humanClass,
                Config = GameConfig.CreateBrowser5v5(),
            });
        }

        private static double Radius(World world, int id)
        {
            return world.Config.Class(world.Actors[id].Cls).Radius;
        }

        /// <summary>Move everyone not under test far away inside the crimson base so they never interfere.</summary>
        private static void Park(World world, int[] keep)
        {
            for (int i = 0; i < world.ActorCount; i++)
            {
                if (Array.IndexOf(keep, i) >= 0) continue;
                world.X[i] = JsMath.F32(100 + (i % 4));
                world.Z[i] = JsMath.F32(30 + JsMath.FloorToInt((double)i / 4));
            }
        }

        private static bool OverlapsSolid(World world, int id)
        {
            double r = Radius(world, id);
            double x = world.X[id];
            double z = world.Z[id];
            for (int cz = JsMath.FloorToInt(z - r); cz <= JsMath.FloorToInt(z + r); cz++)
                for (int cx = JsMath.FloorToInt(x - r); cx <= JsMath.FloorToInt(x + r); cx++)
                {
                    if (MapGrid.IsWalkable(world.Map, cx, cz)) continue;
                    double nx = Math.Max(cx, Math.Min(x, cx + 1));
                    double nz = Math.Max(cz, Math.Min(z, cz + 1));
                    if ((x - nx) * (x - nx) + (z - nz) * (z - nz) < r * r - 1e-6) return true;
                }
            return false;
        }

        [Test]
        public void AnActorPushedIntoAWALLCellFor60StepsNeverEndsInsideANonWalkableCell()
        {
            World w = MakeWorld(9, 5, ClassId.Warrior);
            Park(w, new[] { 0 });
            // Find the nearest WALL cell east of the hill along z = 40.5 + 4.
            double z = 44.5;
            int wallX = -1;
            for (int cx = 56; cx < 70; cx++)
                if (w.Map.Cells[JsMath.FloorToInt(z) * w.Map.Width + cx] == Cell.Wall)
                {
                    wallX = cx;
                    break;
                }
            Assert.Greater(wallX, 56);
            w.X[0] = JsMath.F32(wallX - 2.5);
            w.Z[0] = JsMath.F32(z);
            InputFrame f = SimInput.EmptyInput();
            for (int s = 0; s < 60; s++)
            {
                f.MoveX = 1;
                f.MoveZ = s % 2 == 0 ? 0.3 : -0.3;
                f.AimX = w.X[0] + 5;
                f.AimZ = w.Z[0];
                SimInput.ApplyInput(w, 0, f);
                SimStep.Step(w);
                Assert.AreEqual(true, MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(w.X[0]), JsMath.FloorToInt(w.Z[0])));
                Assert.AreEqual(false, OverlapsSolid(w, 0));
            }
            Assert.Less(w.X[0], wallX);
            Assert.Greater(w.X[0], wallX - 1); // it reached the wall face
        }

        [Test]
        public void AnActorDrivenDiagonallyIntoAWallSlidesAlongIt()
        {
            World w = MakeWorld(9, 5, ClassId.Farmer);
            Park(w, new[] { 0 });
            // B7 is a straight north-south bridge over cells x 54..57 (VOID to the west). Push north-west into its west edge.
            w.X[0] = JsMath.F32(55.5);
            w.Z[0] = JsMath.F32(24.5);
            InputFrame f = SimInput.EmptyInput();
            f.MoveX = -1;
            f.MoveZ = -1;
            f.AimX = double.NaN;
            f.AimZ = double.NaN;
            double r = w.Config.Class(ClassId.Farmer).Radius;
            for (int s = 0; s < 45; s++)
            {
                SimInput.ApplyInput(w, 0, f);
                SimStep.Step(w);
                Assert.AreEqual(false, OverlapsSolid(w, 0));
                Assert.GreaterOrEqual(w.X[0], 54 + r);
            }
            // Pinned against the edge, yet it kept moving north at the full z component of its speed.
            Assert.Less(w.X[0], 54 + r + 0.01);
            Assert.Greater(24.5 - w.Z[0], 4.3);
        }

        [Test]
        public void TwoActorsAtTheSamePointSeparate()
        {
            World w = MakeWorld(11, 5, ClassId.Warrior);
            Park(w, new[] { 0, 1 });
            w.X[0] = JsMath.F32(56.5);
            w.Z[0] = JsMath.F32(40.5);
            w.X[1] = JsMath.F32(56.5);
            w.Z[1] = JsMath.F32(40.5);
            for (int s = 0; s < 30; s++) SimStep.Step(w);
            double dx = w.X[0] - w.X[1];
            double dz = w.Z[0] - w.Z[1];
            double d = Math.Sqrt(dx * dx + dz * dz);
            double rr = Radius(w, 0) + Radius(w, 1);
            Assert.Greater(d, rr * 0.9);
        }

        [Test]
        public void Case16v16RandomSeededPlayFor3000StepsNoCircleEverOverlapsABlockedCellWithZeroTolerance()
        {
            World w = MakeWorld(7, 16, ClassId.Miner);
            Rng r = Rng.CreateRng(99);
            InputFrame f = SimInput.EmptyInput();
            f.AimX = double.NaN;
            f.AimZ = double.NaN;
            string first = "";
            for (int t = 0; t < 3000 && first.Length == 0; t++)
            {
                for (int i = 0; i < w.ActorCount; i++)
                {
                    if (t % 20 == 0 || w.Inputs[i].MoveX == 0)
                    {
                        f.MoveX = Rng.NextFloat(r) * 2 - 1;
                        f.MoveZ = Rng.NextFloat(r) * 2 - 1;
                        SimInput.ApplyInput(w, i, f);
                    }
                }
                SimStep.Step(w);
                for (int i = 0; i < w.ActorCount && first.Length == 0; i++)
                {
                    double rad = Radius(w, i);
                    double x = w.X[i];
                    double z = w.Z[i];
                    if (!double.IsFinite(x) || !double.IsFinite(z)) first = $"t={t} id={i} non-finite";
                    for (int cz = JsMath.FloorToInt(z - rad); cz <= JsMath.FloorToInt(z + rad) && first.Length == 0; cz++)
                        for (int cx = JsMath.FloorToInt(x - rad); cx <= JsMath.FloorToInt(x + rad) && first.Length == 0; cx++)
                        {
                            if (MapGrid.IsWalkable(w.Map, cx, cz) || OnDeckOverVoid(w, i, cx, cz)) continue;
                            double nx = Math.Max(cx, Math.Min(x, cx + 1));
                            double nz = Math.Max(cz, Math.Min(z, cz + 1));
                            if ((x - nx) * (x - nx) + (z - nz) * (z - nz) < rad * rad)
                                first = $"t={t} id={i} x={x:R} z={z:R} cell=({cx},{cz})";
                        }
                }
            }
            Assert.AreEqual("", first);
        }

        [Test]
        public void Float32StorageNeverRoundsAnEdgeIntoABlockedCell16v16WallHuggingSweepsZeroTolerance()
        {
            // Regression for D-012: every actor pushes a constant 8-way direction (rotating every 150 steps).
            int[][] dirs =
            {
                new[] { 1, 0 }, new[] { 1, 1 }, new[] { 0, 1 }, new[] { -1, 1 },
                new[] { -1, 0 }, new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, -1 },
            };
            int[][] seeds = { new[] { 4242, 5 }, new[] { 7, 99 }, new[] { 31337, 1 } };
            foreach (int[] pair in seeds)
            {
                int seed = pair[0];
                int inputSeed = pair[1];
                World w = MakeWorld(seed, 16, ClassId.Warrior);
                for (int cz = 30; cz < 45; cz++)
                    for (int cx = 20; cx <= 24; cx++)
                        if (w.Map.Cells[cz * w.Map.Width + cx] == Cell.Bridge) MapGrid.SetCell(w, cx, cz, Cell.Barricade);
                Rng rng = Rng.CreateRng((uint)inputSeed);
                InputFrame f = SimInput.EmptyInput();
                f.AimX = double.NaN;
                f.AimZ = double.NaN;
                double worst = 0;
                string where = "";
                for (int t = 0; t < 2400; t++)
                {
                    int phase = JsMath.FloorToInt((double)t / 150);
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        int[] d = dirs[(phase + i) % 8];
                        double j = Rng.NextFloat(rng) * 0.2 - 0.1;
                        f.MoveX = d[0] + j;
                        f.MoveZ = d[1] - j;
                        SimInput.ApplyInput(w, i, f);
                    }
                    SimStep.Step(w);
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        double rad = Radius(w, i);
                        double x = w.X[i];
                        double z = w.Z[i];
                        for (int cz = JsMath.FloorToInt(z - rad) - 1; cz <= JsMath.FloorToInt(z + rad) + 1; cz++)
                            for (int cx = JsMath.FloorToInt(x - rad) - 1; cx <= JsMath.FloorToInt(x + rad) + 1; cx++)
                            {
                                if (MapGrid.IsWalkable(w.Map, cx, cz) || OnDeckOverVoid(w, i, cx, cz)) continue;
                                double nx = Math.Max(cx, Math.Min(x, cx + 1));
                                double nz = Math.Max(cz, Math.Min(z, cz + 1));
                                double pen = rad - Math.Sqrt((x - nx) * (x - nx) + (z - nz) * (z - nz));
                                if (pen > worst)
                                {
                                    worst = pen;
                                    where = $"seed={seed} t={t} id={i} x={x:R} z={z:R} cell=({cx},{cz})";
                                }
                            }
                    }
                }
                Assert.LessOrEqual(worst, 0, where);
            }
        }
    }
}

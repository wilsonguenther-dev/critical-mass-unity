using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of the four flow-field route cases of src/sim/map.test.ts (lines 106-190).</summary>
    [TestFixture]
    public sealed class MapRouteTests
    {
        private const int W = 112;
        private const int H = 72;

        private static int CellOf(double x, double z)
        {
            return JsMath.FloorToInt(z) * W + JsMath.FloorToInt(x);
        }

        private static List<PointXZ> ShrineRingGoals(MapDef map, GameConfig config)
        {
            List<PointXZ> output = new List<PointXZ>();
            for (int cz = 0; cz < map.Height; cz++)
                for (int cx = 0; cx < map.Width; cx++)
                {
                    double ax = cx + 0.5 - map.Shrine.X;
                    double az = cz + 0.5 - map.Shrine.Z;
                    double d = Math.Sqrt(ax * ax + az * az);
                    if (d <= config.KaijuFeed.ShrineInteractRange && MapGrid.IsWalkable(map, cx, cz)) output.Add(new PointXZ(cx + 0.5, cz + 0.5));
                }
            return output;
        }

        private static double At(FlowField field, double x, double z)
        {
            return field.Dist[CellOf(x, z)];
        }

        private static double MinOver(FlowField field, List<PointXZ> pts)
        {
            double m = double.PositiveInfinity;
            foreach (PointXZ p in pts) m = Math.Min(m, At(field, p.X, p.Z));
            return m;
        }

        private static FlowField AssertReachability(MapDef map, GameConfig config, out List<PointXZ> ring)
        {
            FlowField hill = FlowField.ComputeFlowField(map, new[] { new PointXZ(map.Hill.X, map.Hill.Z) });
            Assert.AreEqual(0.0, At(hill, map.Hill.X, map.Hill.Z));
            PadDef azure = map.Pads[(int)TeamId.Azure];
            PadDef crimson = map.Pads[(int)TeamId.Crimson];
            Assert.IsTrue(double.IsFinite(At(hill, azure.X, azure.Z)), "azure pad reaches the hill");
            Assert.IsTrue(double.IsFinite(At(hill, crimson.X, crimson.Z)), "crimson pad reaches the hill");
            ring = ShrineRingGoals(map, config);
            Assert.Greater(ring.Count, 0);
            Assert.IsTrue(double.IsFinite(MinOver(hill, ring)), "shrine ring reaches the hill");
            foreach (PointXZ n in map.BiomassNodes) Assert.IsTrue(double.IsFinite(At(hill, n.X, n.Z)), "biomass node " + n.X + "," + n.Z);
            foreach (PointXZ n in map.CorruptionNodes) Assert.IsTrue(double.IsFinite(At(hill, n.X, n.Z)), "corruption node " + n.X + "," + n.Z);
            Assert.IsTrue(double.IsFinite(At(hill, 56, 65)), "south corruption island centre"); // south corruption island centre
            return hill;
        }

        [Test]
        public void ConnectsPadsHillShrineRingEveryNodeAndTheCorruptionIsland()
        {
            GameConfig config = GameConfig.CreateBrowser5v5();
            MapDef map = SkybreakBasinMap.BuildSkybreakBasin(config);
            AssertReachability(map, config, out _);
        }

        [Test]
        public void BaseToHillFlowDistanceIs4476CellsAndTheShrineDetourIs2852()
        {
            GameConfig config = GameConfig.CreateBrowser5v5();
            MapDef map = SkybreakBasinMap.BuildSkybreakBasin(config);
            FlowField hill = AssertReachability(map, config, out List<PointXZ> ring);
            FlowField shrine = FlowField.ComputeFlowField(map, ring);
            foreach (TeamId team in new[] { TeamId.Azure, TeamId.Crimson })
            {
                PadDef pad = map.Pads[(int)team];
                double dPadHill = At(hill, pad.X, pad.Z);
                Assert.GreaterOrEqual(dPadHill, 44);
                Assert.LessOrEqual(dPadHill, 76);
                double dPadShrine = At(shrine, pad.X, pad.Z);
                double dShrineHill = MinOver(hill, ring);
                double detour = dPadShrine + dShrineHill - dPadHill;
                Assert.GreaterOrEqual(detour, 28);
                Assert.LessOrEqual(detour, 52);
            }
        }

        [Test]
        public void KeepsEveryRouteWhenAllBreakableWallsStayIntactForeverShortcutsAreNeverTheOnlyRoute()
        {
            GameConfig config = GameConfig.CreateBrowser5v5();
            MapDef map = SkybreakBasinMap.BuildSkybreakBasin(config);
            MapDef walled = map.WithCells((byte[])map.Cells.Clone());
            int converted = 0;
            for (int i = 0; i < walled.Cells.Length; i++)
                if (walled.Cells[i] == Cell.BreakableIntact)
                {
                    walled.Cells[i] = Cell.Wall;
                    converted++;
                }
            Assert.Greater(converted, 0);
            AssertReachability(walled, config, out _);
        }

        [Test]
        public void KaijuLaneClearanceCellsAtKaijuGridRadiusFormOne8ConnectedRegionReachingHatchHillBothPadsAndTheCorruptionIsland()
        {
            GameConfig config = GameConfig.CreateBrowser5v5();
            MapDef map = SkybreakBasinMap.BuildSkybreakBasin(config);
            double r = config.Kaiju.GridRadius;
            Assert.GreaterOrEqual(r, 0.5);
            int n = W * H;
            byte[] ok = new byte[n];
            for (int cz = 0; cz < H; cz++) for (int cx = 0; cx < W; cx++) ok[cz * W + cx] = MapGrid.ClearanceOk(map, cx, cz, r) ? (byte)1 : (byte)0;
            // Label 8-connected components of clearance cells.
            int[] label = new int[n];
            Array.Fill(label, -1);
            int[] queue = new int[n];
            int comps = 0;
            for (int s = 0; s < n; s++)
            {
                if (ok[s] == 0 || label[s] >= 0) continue;
                int head = 0;
                int tail = 0;
                queue[tail++] = s;
                label[s] = comps;
                while (head < tail)
                {
                    int c = queue[head++];
                    int cx = c % W;
                    int cz = (c - cx) / W; // exact: c - cx is a multiple of W
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx;
                            int nz = cz + dz;
                            if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                            int ni = nz * W + nx;
                            if (ok[ni] != 0 && label[ni] < 0)
                            {
                                label[ni] = comps;
                                queue[tail++] = ni;
                            }
                        }
                }
                comps++;
            }

            HashSet<int> LabelsWithin(double x, double z, double rad, Func<int, int, bool> filter = null)
            {
                HashSet<int> set = new HashSet<int>();
                for (int cz = 0; cz < H; cz++)
                    for (int cx = 0; cx < W; cx++)
                    {
                        int i = cz * W + cx;
                        if (ok[i] == 0) continue;
                        double ax = cx + 0.5 - x;
                        double az = cz + 0.5 - z;
                        if (Math.Sqrt(ax * ax + az * az) > rad) continue;
                        if (filter != null && !filter(cx, cz)) continue;
                        set.Add(label[i]);
                    }
                return set;
            }

            HashSet<int> hatch = LabelsWithin(56, 14, 2);
            HashSet<int> hill = LabelsWithin(map.Hill.X, map.Hill.Z, map.Hill.R);
            PadDef azurePad = map.Pads[(int)TeamId.Azure];
            PadDef crimsonPad = map.Pads[(int)TeamId.Crimson];
            HashSet<int> azure = LabelsWithin(azurePad.X, azurePad.Z, azurePad.R);
            HashSet<int> crimson = LabelsWithin(crimsonPad.X, crimsonPad.Z, crimsonPad.R);
            // South corruption island: ellipse c(56,65) rx 9 rz 5.
            HashSet<int> south = LabelsWithin(56, 65, 9, (cx, cz) =>
            {
                double ex = (cx + 0.5 - 56) / 9;
                double ez = (cz + 0.5 - 65) / 5;
                return ex * ex + ez * ez <= 1;
            });
            int shared = 0;
            foreach (int l in hatch) if (hill.Contains(l) && azure.Contains(l) && crimson.Contains(l) && south.Contains(l)) shared++;
            Assert.AreEqual(1, shared);
        }
    }
}

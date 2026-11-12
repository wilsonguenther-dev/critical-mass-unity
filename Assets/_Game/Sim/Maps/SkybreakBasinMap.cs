using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Maps
{
    /// <summary>Skybreak Basin (browser map.ts): 112 x 72, 1 unit per cell, deterministic, mirrored across x = 56.</summary>
    public static class SkybreakBasinMap
    {
        public const int MapWidth = 112;
        public const int MapHeight = 72;
        /// <summary>x' = MirrorX - x (mirror line x = 56).</summary>
        private const double MirrorX = 112;

        private struct Island
        {
            public double Cx;
            public double Cz;
            public double Rx;
            public double Rz;
            public double H;
            public bool Mirror;

            public Island(double cx, double cz, double rx, double rz, double h, bool mirror)
            {
                Cx = cx;
                Cz = cz;
                Rx = rx;
                Rz = rz;
                H = h;
                Mirror = mirror;
            }
        }

        /// <summary>Islands (FLOOR). Height in units. Those with centre x below 56 are mirrored for Crimson.</summary>
        private static readonly Island[] Islands =
        {
            new Island(10, 36, 9, 11, 0, true), // Azure base
            new Island(28, 18, 8, 6, 0, true), // NW courtyard
            new Island(28, 56, 8, 6, 0, true), // SW courtyard
            new Island(56, 40, 17, 14, 0.75, false), // central island (hill)
            new Island(56, 10, 12, 7, 1.5, false), // shrine island
            new Island(56, 65, 9, 5, 0, false), // south corruption island
        };

        /// <summary>A bridge segment; every VOID cell whose centre is within BridgeHalfWidth of it becomes BRIDGE.</summary>
        public struct Bridge
        {
            public double X0;
            public double Z0;
            public double X1;
            public double Z1;
            public bool Mirror;
        }

        /// <summary>CM-31: the overlook ledges, one per team (mirrored), north of each base across a 3-cell void gap.</summary>
        private static readonly Island Overlook = new Island(11, 18, 4.5, 4, 0, true);

        /// <summary>CM-31 knock-down pillars, WEST copies (each is mirrored for Crimson: cx' = width - 1 - cx).</summary>
        private static readonly DecorationDef[] WestDecorations =
        {
            new DecorationDef { Kind = DecorationKind.Pillar, Cx = 12, Cz = 26, Ramp = new CellDir(0, -1), Block = new CellDir(1, 0) },
        };

        public const double BridgeHalfWidth = 1.5;

        /// <summary>Every bridge, browser order (B1..B9).</summary>
        public static readonly IReadOnlyList<Bridge> Bridges = Array.AsReadOnly(new[]
        {
            new Bridge { X0 = 18, Z0 = 37, X1 = 40, Z1 = 40, Mirror = true }, // B1 base -> central
            new Bridge { X0 = 13, Z0 = 27, X1 = 23, Z1 = 21, Mirror = true }, // B2 base -> NW courtyard
            new Bridge { X0 = 35, Z0 = 20, X1 = 44, Z1 = 30, Mirror = true }, // B3 NW courtyard -> central
            new Bridge { X0 = 13, Z0 = 45, X1 = 23, Z1 = 52, Mirror = true }, // B4 base -> SW courtyard
            new Bridge { X0 = 35, Z0 = 55, X1 = 44, Z1 = 50, Mirror = true }, // B5 SW courtyard -> central
            new Bridge { X0 = 35, Z0 = 15, X1 = 45, Z1 = 11, Mirror = true }, // B6 NW courtyard -> shrine island
            new Bridge { X0 = 56, Z0 = 27, X1 = 56, Z1 = 16, Mirror = false }, // B7 central -> shrine yard gap
            new Bridge { X0 = 56, Z0 = 53, X1 = 56, Z1 = 61, Mirror = false }, // B8 central -> corruption island
            new Bridge { X0 = 35, Z0 = 59, X1 = 48, Z1 = 64, Mirror = true }, // B9 SW courtyard -> corruption island
        });

        // Central ring around the hill.
        private const double HillX = 56;
        private const double HillZ = 40;
        /// <summary>The ring is a thin 8-connected circle of radius RingR (D-020).</summary>
        private const double RingR = 10.5;
        private const double RingGapHalfDeg = 12;
        private static readonly double[] RingBreakableCentresDeg = { 45, 135, 225, 315 };
        private const double RingBreakableHalfDeg = 8;

        // Shrine yard.
        private const double ShrineX = 56;
        private const double ShrineZ = 8;
        private const double ShrineSolidR = 1.5;
        private const double YardInner = 4;
        private const double YardOuter = 5;
        private const double YardGapCentreDeg = 90;
        private const double YardGapHalfDeg = 15;
        private static readonly double[] YardBreakableCentresDeg = { 45, 135 };
        private const double YardBreakableHalfDeg = 8;

        private const double PadR = 3;

        private static readonly PointXZ[] WestBiomass =
        {
            new PointXZ(24, 16),
            new PointXZ(31, 20),
            new PointXZ(24, 58),
            new PointXZ(31, 54),
        };

        private static readonly PointXZ[] Corruption =
        {
            new PointXZ(52, 65),
            new PointXZ(56, 67),
            new PointXZ(60, 65),
        };

        private static bool InsideIsland(Island island, double x, double z)
        {
            double u = (x - island.Cx) / island.Rx;
            double v = (z - island.Cz) / island.Rz;
            return u * u + v * v <= 1;
        }

        /// <summary>Every painted island: the named ones plus the CM-31 overlook ledges (not bridge endpoints).</summary>
        private static readonly Island[] IslandsAll = BuildIslandsAll();

        private static Island[] BuildIslandsAll()
        {
            var all = new Island[Islands.Length + 1];
            Array.Copy(Islands, all, Islands.Length);
            all[Islands.Length] = Overlook;
            return all;
        }

        /// <summary>Height of the island an endpoint sits on (smallest normalised ellipse distance).</summary>
        private static double EndpointHeight(double x, double z)
        {
            double best = double.PositiveInfinity;
            double h = 0;
            for (int k = 0; k < Islands.Length; k++)
            {
                Island island = Islands[k];
                double u = (x - island.Cx) / island.Rx;
                double v = (z - island.Cz) / island.Rz;
                double d = u * u + v * v;
                if (d < best)
                {
                    best = d;
                    h = island.H;
                }
            }
            return h;
        }

        /// <summary>Returns t in [0,1] of the closest point when within the half width of the segment, else -1.</summary>
        private static double BridgeT(Bridge b, double x, double z)
        {
            double ex = b.X1 - b.X0;
            double ez = b.Z1 - b.Z0;
            double len2 = ex * ex + ez * ez;
            double t = ((x - b.X0) * ex + (z - b.Z0) * ez) / len2;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            double px = b.X0 + ex * t - x;
            double pz = b.Z0 + ez * t - z;
            return px * px + pz * pz <= BridgeHalfWidth * BridgeHalfWidth ? t : -1;
        }

        /// <summary>Angle in degrees [0, 360) of (dx, dz); 0 = east, 90 = south.</summary>
        private static double AngleDeg(double dx, double dz)
        {
            double a = (Math.Atan2(dz, dx) * 180) / Math.PI;
            return a < 0 ? a + 360 : a;
        }

        /// <summary>Smallest absolute difference between two angles in degrees.</summary>
        private static double AngDiff(double a, double b)
        {
            double d = Math.Abs(a - b) % 360;
            return d > 180 ? 360 - d : d;
        }

        private static bool NearAny(double a, double[] centres, double half)
        {
            for (int k = 0; k < centres.Length; k++)
                if (AngDiff(a, centres[k]) <= half) return true;
            return false;
        }

        private static bool InRingGap(double a)
        {
            // Nearest multiple of 90 degrees.
            double nearest = JsMath.Round(a / 90) * 90;
            return AngDiff(a, nearest) <= RingGapHalfDeg;
        }

        /// <summary>True when the cell whose centre is (dx, dz) from the hill centre lies on the thin ring.</summary>
        private static bool OnRing(double dx, double dz)
        {
            double ax = Math.Abs(dx);
            double az = Math.Abs(dz);
            double r2 = RingR * RingR;
            if (az <= ax && az < RingR && ax == Math.Floor(Math.Sqrt(r2 - az * az)) + 0.5) return true; // one cell per row
            if (ax <= az && ax < RingR && az == Math.Floor(Math.Sqrt(r2 - ax * ax)) + 0.5) return true; // one cell per column
            return false;
        }

        /// <summary>Paint Skybreak Basin into an existing MapDef's arrays (in place); used by BuildSkybreakBasin and resetWorld.</summary>
        public static void PaintSkybreakBasin(MapDef map, GameConfig config)
        {
            int W = map.Width;
            int H = map.Height;
            byte[] cells = map.Cells;
            double[] hgt = map.Height01;
            Array.Fill(cells, Cell.Void);
            Array.Fill(hgt, 0.0);
            Array.Fill(map.WallHp, (byte)0);

            // 1) Islands (CM-31: plus the two overlook ledges).
            for (int k = 0; k < IslandsAll.Length; k++)
            {
                Island island = IslandsAll[k];
                for (int cz = 0; cz < H; cz++)
                {
                    double zc = cz + 0.5;
                    for (int cx = 0; cx < W; cx++)
                    {
                        double xc = cx + 0.5;
                        if (InsideIsland(island, xc, zc) || (island.Mirror && InsideIsland(island, MirrorX - xc, zc)))
                        {
                            int i = cz * W + cx;
                            cells[i] = Cell.Floor;
                            hgt[i] = JsMath.F32(island.H);
                        }
                    }
                }
            }

            // 2) Bridges (never overwrite non-VOID cells; height lerps between the endpoint islands).
            for (int k = 0; k < Bridges.Count; k++)
            {
                Bridge b = Bridges[k];
                double h0 = EndpointHeight(b.X0, b.Z0);
                double h1 = EndpointHeight(b.X1, b.Z1);
                for (int cz = 0; cz < H; cz++)
                {
                    double zc = cz + 0.5;
                    for (int cx = 0; cx < W; cx++)
                    {
                        int i = cz * W + cx;
                        if (cells[i] != Cell.Void) continue;
                        double xc = cx + 0.5;
                        double t = BridgeT(b, xc, zc);
                        if (t < 0 && b.Mirror) t = BridgeT(b, MirrorX - xc, zc);
                        if (t < 0) continue;
                        cells[i] = Cell.Bridge;
                        hgt[i] = JsMath.F32(h0 + (h1 - h0) * t);
                    }
                }
            }

            // 3) Central ring around the hill (thin WALL with 4 cardinal gaps; the 4 diagonal arcs carry BREAKABLE shortcuts).
            for (int cz = 0; cz < H; cz++)
            {
                double dz = cz + 0.5 - HillZ;
                for (int cx = 0; cx < W; cx++)
                {
                    double dx = Math.Abs(cx + 0.5 - HillX);
                    if (!OnRing(dx, dz)) continue;
                    double a = AngleDeg(dx, dz);
                    if (InRingGap(a)) continue; // stays FLOOR
                    int i = cz * W + cx;
                    cells[i] = NearAny(a, RingBreakableCentresDeg, RingBreakableHalfDeg) ? Cell.BreakableIntact : Cell.Wall;
                }
            }

            // 4) Shrine yard: southern half-ring with a gap due south (where B7 lands) and two breakable panels.
            for (int cz = 0; cz < H; cz++)
            {
                double zc = cz + 0.5;
                if (!(zc > ShrineZ)) continue;
                double dz = zc - ShrineZ;
                for (int cx = 0; cx < W; cx++)
                {
                    double dx = Math.Abs(cx + 0.5 - ShrineX);
                    double d = Math.Sqrt(dx * dx + dz * dz);
                    if (d < YardInner || d >= YardOuter) continue;
                    double a = AngleDeg(dx, dz);
                    int i = cz * W + cx;
                    if (AngDiff(a, YardGapCentreDeg) <= YardGapHalfDeg) cells[i] = Cell.Floor;
                    else if (NearAny(a, YardBreakableCentresDeg, YardBreakableHalfDeg)) cells[i] = Cell.BreakableIntact;
                    else cells[i] = Cell.Wall;
                }
            }

            // 5) Shrine solid.
            for (int cz = 0; cz < H; cz++)
            {
                double dz = cz + 0.5 - ShrineZ;
                for (int cx = 0; cx < W; cx++)
                {
                    double dx = Math.Abs(cx + 0.5 - ShrineX);
                    if (dx * dx + dz * dz <= ShrineSolidR * ShrineSolidR) cells[cz * W + cx] = Cell.Wall;
                }
            }

            // Breakable hit points: intact -> cracked -> destroyed, HitsPerState Miner hits per state.
            double hp = 2 * config.Abilities.MinerWallBreak.HitsPerState;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] == Cell.BreakableIntact) map.WallHp[i] = (byte)hp;
        }

        /// <summary>West pillars then their mirrored Crimson copies (index order is stable: world.Topple slots key on it).</summary>
        private static DecorationDef[] BuildDecorations(int width)
        {
            var output = new List<DecorationDef>();
            for (int k = 0; k < WestDecorations.Length; k++)
            {
                DecorationDef d = WestDecorations[k];
                output.Add(new DecorationDef
                {
                    Kind = d.Kind,
                    Cx = d.Cx,
                    Cz = d.Cz,
                    Ramp = new CellDir(d.Ramp.Dx, d.Ramp.Dz),
                    Block = new CellDir(d.Block.Dx, d.Block.Dz),
                });
            }
            for (int k = 0; k < WestDecorations.Length; k++)
            {
                DecorationDef d = WestDecorations[k];
                output.Add(new DecorationDef
                {
                    Kind = d.Kind,
                    Cx = width - 1 - d.Cx,
                    Cz = d.Cz,
                    Ramp = new CellDir(-d.Ramp.Dx, d.Ramp.Dz),
                    Block = new CellDir(-d.Block.Dx, d.Block.Dz),
                });
            }
            return output.ToArray();
        }

        /// <summary>Build a fresh Skybreak Basin MapDef and paint it.</summary>
        public static MapDef BuildSkybreakBasin(GameConfig config)
        {
            int W = MapWidth;
            int H = MapHeight;
            var biomassNodes = new List<PointXZ>();
            for (int k = 0; k < WestBiomass.Length; k++) biomassNodes.Add(new PointXZ(WestBiomass[k].X, WestBiomass[k].Z));
            for (int k = 0; k < WestBiomass.Length; k++) biomassNodes.Add(new PointXZ(MirrorX - WestBiomass[k].X, WestBiomass[k].Z));
            var corruptionNodes = new PointXZ[Corruption.Length];
            for (int k = 0; k < Corruption.Length; k++) corruptionNodes[k] = new PointXZ(Corruption[k].X, Corruption[k].Z);
            var pads = new PadDef[2];
            pads[(int)TeamId.Azure] = new PadDef { X = 8, Z = 36, R = PadR };
            pads[(int)TeamId.Crimson] = new PadDef { X = MirrorX - 8, Z = 36, R = PadR };
            var map = new MapDef
            {
                Width = W,
                Height = H,
                Cells = new byte[W * H],
                Height01 = new double[W * H],
                WallHp = new byte[W * H],
                Pads = pads,
                Hill = new HillDef { X = HillX, Z = HillZ, R = config.Hill.Radius },
                Shrine = new ShrineDef { X = ShrineX, Z = ShrineZ, SolidR = ShrineSolidR },
                BiomassNodes = biomassNodes.ToArray(),
                CorruptionNodes = corruptionNodes,
                Bounds = new BoundsDef { MinX = 0, MinZ = 0, MaxX = W, MaxZ = H },
                Decorations = BuildDecorations(W),
            };
            PaintSkybreakBasin(map, config);
            return map;
        }
    }
}

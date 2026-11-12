using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>Collision (browser collide.ts): actor squares against the grid, then soft actor-actor separation.</summary>
    public static class Collide
    {
        /// <summary>Push-out margin so a resting actor sits strictly clear of blocked cells (far above the Float32 ulp).</summary>
        private const double Skin = 1e-3;

        /// <summary>Cell-range tolerance, applied outward: an edge within RangeEps of a cell boundary touches the next cell.</summary>
        private const double RangeEps = 1e-6;

        /// <summary>Max distance per collision sub-step (well under one cell, so no tunnelling).</summary>
        private const double MaxSubstep = 0.4;

        /// <summary>Browser MAX_RADIUS: the largest of the warrior, farmer and miner radii, read from the world's config.</summary>
        private static double MaxRadius(GameConfig c)
        {
            return Math.Max(c.Class(ClassId.Warrior).Radius, Math.Max(c.Class(ClassId.Farmer).Radius, c.Class(ClassId.Miner).Radius));
        }

        private static int[] Neighbours = new int[64];

        /// <summary>Collision radius of actor id (its class radius).</summary>
        public static double ActorRadius(World w, int id)
        {
            return w.Config.Class(w.Actors[id].Cls).Radius;
        }

        /// <summary>True when cell (cx, cz) blocks the actor: any non-walkable cell.</summary>
        private static bool CellBlocks(World w, int cx, int cz, bool air)
        {
            MapDef map = w.Map;
            if (MapGrid.IsWalkable(map, cx, cz)) return false;
            // ITEM CM-32: added by its owner (airborne actors pass over in-map VOID)
            return true;
        }

        private static bool RowBlocked(World w, int cx, double z, double r, bool air)
        {
            int z0 = JsMath.FloorToInt(z - r - RangeEps);
            int z1 = JsMath.FloorToInt(z + r + RangeEps);
            for (int cz = z0; cz <= z1; cz++) if (CellBlocks(w, cx, cz, air)) return true;
            return false;
        }

        private static bool ColBlocked(World w, int cz, double x, double r, bool air)
        {
            int x0 = JsMath.FloorToInt(x - r - RangeEps);
            int x1 = JsMath.FloorToInt(x + r + RangeEps);
            for (int cx = x0; cx <= x1; cx++) if (CellBlocks(w, cx, cz, air)) return true;
            return false;
        }

        /// <summary>True when actor id's bounding square centred at (x, z) touches only walkable cells.</summary>
        public static bool FootprintClear(World w, int id, double x, double z)
        {
            double r = ActorRadius(w, id);
            int x0 = JsMath.FloorToInt(x - r - RangeEps);
            int x1 = JsMath.FloorToInt(x + r + RangeEps);
            int z0 = JsMath.FloorToInt(z - r - RangeEps);
            int z1 = JsMath.FloorToInt(z + r + RangeEps);
            for (int cz = z0; cz <= z1; cz++) for (int cx = x0; cx <= x1; cx++) if (!MapGrid.IsWalkable(w.Map, cx, cz)) return false;
            return true;
        }

        /// <summary>Move actor id by (dx, dz) against the grid, sliding along walls. Does not touch other actors.</summary>
        public static void MoveActor(World w, int id, double dx, double dz, bool air = false)
        {
            if (!(dx == dx) || !(dz == dz)) return; // NaN guard
            SweepActor(w, id, w.X[id], w.Z[id], dx, dz, air, out double x, out double z);
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
        }

        /// <summary>The pure core of MoveActor: sweep actor id's square from (x0, z0) by (dx, dz); touches no world state.</summary>
        public static void SweepActor(World w, int id, double x0, double z0, double dx, double dz, bool air, out double x, out double z)
        {
            x = x0;
            z = z0;
            if (!(dx == dx) || !(dz == dz)) return; // NaN guard
            double r = ActorRadius(w, id);
            double big = Math.Max(Math.Abs(dx), Math.Abs(dz));
            int steps = big > MaxSubstep ? (int)Math.Ceiling(big / MaxSubstep) : 1;
            double sx = dx / steps;
            double sz = dz / steps;
            double px = x0;
            double pz = z0;
            for (int s = 0; s < steps; s++)
            {
                if (sx != 0)
                {
                    px = JsMath.F32(px + sx);
                    if (sx > 0)
                    {
                        int cx = JsMath.FloorToInt(px + r + RangeEps);
                        if (RowBlocked(w, cx, pz, r, air)) px = JsMath.F32(cx - r - Skin);
                    }
                    else
                    {
                        int cx = JsMath.FloorToInt(px - r - RangeEps);
                        if (RowBlocked(w, cx, pz, r, air)) px = JsMath.F32(cx + 1 + r + Skin);
                    }
                }
                if (sz != 0)
                {
                    pz = JsMath.F32(pz + sz);
                    if (sz > 0)
                    {
                        int cz = JsMath.FloorToInt(pz + r + RangeEps);
                        if (ColBlocked(w, cz, px, r, air)) pz = JsMath.F32(cz - r - Skin);
                    }
                    else
                    {
                        int cz = JsMath.FloorToInt(pz - r - RangeEps);
                        if (ColBlocked(w, cz, px, r, air)) pz = JsMath.F32(cz + 1 + r + Skin);
                    }
                }
            }
            x = px;
            z = pz;
        }

        /// <summary>Soft actor-actor separation after all moves: each overlapping living pair is pushed apart through MoveActor.</summary>
        public static void SeparateActors(World w)
        {
            int n = w.ActorCount;
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, n);
            if (Neighbours.Length < w.Hash.Capacity) Neighbours = new int[w.Hash.Capacity];
            double reach = 2 * MaxRadius(w.Config);
            for (int i = 0; i < n; i++)
            {
                // ITEM CM-32: added by its owner (airborne actors skip separation)
                if (!w.Actors[i].Alive) continue;
                double ri = ActorRadius(w, i);
                int cnt = SpatialHash.QueryCircle(w.Hash, w.X[i], w.Z[i], reach, Neighbours);
                for (int k = 0; k < cnt; k++)
                {
                    int j = Neighbours[k];
                    // ITEM CM-32: added by its owner (airborne actors skip separation)
                    if (j <= i || !w.Actors[j].Alive) continue;
                    double rr = ri + ActorRadius(w, j);
                    double ddx = w.X[j] - w.X[i];
                    double ddz = w.Z[j] - w.Z[i];
                    double d2 = ddx * ddx + ddz * ddz;
                    if (d2 >= rr * rr) continue;
                    double d = Math.Sqrt(d2);
                    if (d < 1e-6)
                    {
                        double a = (i * 31 + j * 17) * 2.39996;
                        ddx = JsMath.Cos(a);
                        ddz = JsMath.Sin(a);
                        d = 0;
                    }
                    else
                    {
                        ddx /= d;
                        ddz /= d;
                    }
                    // CM-20: an actor still getting up (StandUp > 0) is immovable; the other actor takes the full push.
                    bool iFixed = w.Actors[i].StandUp > 0;
                    bool jFixed = w.Actors[j].StandUp > 0;
                    if (iFixed && jFixed) continue;
                    double overlap = rr - d;
                    double pushI = jFixed ? overlap : iFixed ? 0 : overlap * 0.5;
                    double pushJ = overlap - pushI;
                    if (pushI > 0) MoveActor(w, i, -ddx * pushI, -ddz * pushI);
                    if (pushJ > 0) MoveActor(w, j, ddx * pushJ, ddz * pushJ);
                }
            }
        }
    }
}

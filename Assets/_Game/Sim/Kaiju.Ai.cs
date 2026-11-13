using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>Kaiju behaviour (browser kaiju.ts lines 263-543): slam, melee, grid collider, pathing, kaijuSystem.</summary>
    public sealed partial class Kaiju
    {
        /// <summary>Hostile, living, unprotected actors within `r` of (x, z).</summary>
        private static int CountHostilesWithin(World w, double x, double z, double r)
        {
            int n = w.ActorCount;
            double r2 = r * r;
            int c = 0;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive || a.SpawnProtect > 0 || !KaijuHostileTo(w, id)) continue;
                double dx = w.X[id] - x;
                double dz = w.Z[id] - z;
                if (dx * dx + dz * dz <= r2) c++;
            }
            return c;
        }

        private static void ResolveSlam(World w)
        {
            KaijuConfig kc = w.Config.Kaiju;
            Kaiju k = w.Kaiju;
            int n = w.ActorCount;
            double r2 = kc.SlamRadius * kc.SlamRadius;
            int hits = 0;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive || !KaijuHostileTo(w, id)) continue;
                double dx = w.X[id] - k.TelegraphX;
                double dz = w.Z[id] - k.TelegraphZ;
                if (dx * dx + dz * dz > r2) continue;
                if (Damage.ApplyDamage(w, id, kc.SlamDamage, -1, k.TelegraphX, k.TelegraphZ) > 0) hits++;
            }
            k.SlamCd = kc.SlamCooldown;
            World.PushEvent(w, SimEventType.KaijuSlam, -1, hits, k.TelegraphX, k.TelegraphZ, kc.SlamRadius);
        }

        /// <summary>CM-26: the wound-up swing lands on a still-valid target within meleeRange + MeleeLeash, else a whiff.</summary>
        private static void ResolveMelee(World w)
        {
            KaijuConfig kc = w.Config.Kaiju;
            Kaiju k = w.Kaiju;
            int t = k.MeleeTargetId;
            k.MeleeTargetId = -1;
            if (t < 0 || !KaijuCanTarget(w, t)) return;
            double dx = w.X[t] - k.X;
            double dz = w.Z[t] - k.Z;
            double reach = kc.MeleeRange + MeleeLeash;
            if (dx * dx + dz * dz > reach * reach) return;
            Damage.ApplyDamage(w, t, kc.MeleeDamage, Damage.KaijuSource, k.X, k.Z);
            if (!w.Actors[t].Alive && k.TargetId == t) k.TargetId = -1;
        }

        /// <summary>Push the Kaiju's grid circle out of every non-walkable cell it overlaps (out of the map counts as blocked).</summary>
        private static void ResolveCircle(World w)
        {
            Kaiju k = w.Kaiju;
            MapDef map = w.Map;
            double r = w.Config.Kaiju.GridRadius;
            double r2 = r * r;
            for (int iter = 0; iter < 4; iter++)
            {
                bool pushed = false;
                int x0 = JsMath.FloorToInt(k.X - r);
                int x1 = JsMath.FloorToInt(k.X + r);
                int z0 = JsMath.FloorToInt(k.Z - r);
                int z1 = JsMath.FloorToInt(k.Z + r);
                for (int gz = z0; gz <= z1; gz++)
                {
                    for (int gx = x0; gx <= x1; gx++)
                    {
                        if (MapGrid.IsWalkable(map, gx, gz)) continue;
                        double nx = k.X < gx ? gx : (k.X > gx + 1 ? gx + 1.0 : k.X);
                        double nz = k.Z < gz ? gz : (k.Z > gz + 1 ? gz + 1.0 : k.Z);
                        double ddx = k.X - nx;
                        double ddz = k.Z - nz;
                        double d2 = ddx * ddx + ddz * ddz;
                        if (d2 >= r2) continue;
                        if (d2 > 1e-12)
                        {
                            double d = Math.Sqrt(d2);
                            double push = r - d + Skin;
                            k.X += (ddx / d) * push;
                            k.Z += (ddz / d) * push;
                        }
                        else
                        {
                            // Centre inside the blocked cell: leave through the nearest side.
                            double left = k.X - gx;
                            double right = gx + 1 - k.X;
                            double up = k.Z - gz;
                            double down = gz + 1 - k.Z;
                            double mn = Math.Min(Math.Min(left, right), Math.Min(up, down));
                            if (mn == left) k.X = gx - r - Skin;
                            else if (mn == right) k.X = gx + 1 + r + Skin;
                            else if (mn == up) k.Z = gz - r - Skin;
                            else k.Z = gz + 1 + r + Skin;
                        }
                        pushed = true;
                    }
                }
                if (!pushed) break;
            }
        }

        private static void ClampToBounds(World w)
        {
            Kaiju k = w.Kaiju;
            BoundsDef b = w.Map.Bounds;
            double r = w.Config.Kaiju.GridRadius;
            if (k.X < b.MinX + r) k.X = b.MinX + r;
            else if (k.X > b.MaxX - r) k.X = b.MaxX - r;
            if (k.Z < b.MinZ + r) k.Z = b.MinZ + r;
            else if (k.Z > b.MaxZ - r) k.Z = b.MaxZ - r;
        }

        private static void MoveKaiju(World w, double dx, double dz)
        {
            if (double.IsNaN(dx) || double.IsNaN(dz)) return;
            Kaiju k = w.Kaiju;
            double big = Math.Max(Math.Abs(dx), Math.Abs(dz));
            int steps = big > MaxSubstep ? (int)Math.Ceiling(big / MaxSubstep) : 1;
            double sx = dx / steps;
            double sz = dz / steps;
            for (int s = 0; s < steps; s++)
            {
                k.X += sx;
                k.Z += sz;
                ResolveCircle(w);
                ClampToBounds(w);
            }
        }

        /// <summary>Walk toward (gx, gz) over the clearance flow field at kaiju.moveSpeed.</summary>
        private static void WalkToward(World w, double gx, double gz, double dt)
        {
            Kaiju k = w.Kaiju;
            KaijuNav nav = w.KaijuNav;
            MapDef map = w.Map;
            int W = map.Width;
            RefreshClearance(w);
            // Goal cell: the goal's own cell when it is a clearance cell, else the nearest one.
            int goalCx;
            int goalCz;
            if (NearestClearCell(w, gx, gz, GoalSnapRings, out int snapCx, out int snapCz))
            {
                goalCx = snapCx;
                goalCz = snapCz;
            }
            else
            {
                goalCx = -1;
                goalCz = -1;
            }
            bool goalMoved = goalCx != nav.GoalCx || goalCz != nav.GoalCz;
            if (goalCx >= 0 && (nav.FieldWallVersion != w.WallVersion || nav.GoalCx < 0 || (goalMoved && nav.RepathIn <= 0)))
            {
                GoalScratch[0] = new PointXZ(goalCx + 0.5, goalCz + 0.5);
                FlowField.ComputeFlowField(nav.Clear, GoalScratch, nav.Field, w.WallVersion);
                nav.FieldWallVersion = w.WallVersion;
                nav.GoalCx = goalCx;
                nav.GoalCz = goalCz;
                nav.RepathIn = KaijuRepathSeconds;
            }

            double ax = gx;
            double az = gz;
            int kcx = JsMath.FloorToInt(k.X);
            int kcz = JsMath.FloorToInt(k.Z);
            double[] dist = nav.Field.Dist;
            bool inMap = kcx >= 0 && kcz >= 0 && kcx < W && kcz < map.Height;
            double here = inMap ? dist[kcz * W + kcx] : double.PositiveInfinity;
            if (nav.GoalCx >= 0 && !(kcx == nav.GoalCx && kcz == nav.GoalCz) && here != 0)
            {
                double best = here;
                int bk = -1;
                for (int i = 0; i < 8; i++)
                {
                    int nx = kcx + Ndx[i];
                    int nz = kcz + Ndz[i];
                    if (nx < 0 || nz < 0 || nx >= W || nz >= map.Height) continue;
                    double d = dist[nz * W + nx];
                    if (!(d < best)) continue;
                    if (i >= 4 && (!IsClear(w, kcx + Ndx[i], kcz) || !IsClear(w, kcx, kcz + Ndz[i]))) continue;
                    best = d;
                    bk = i;
                }
                if (bk >= 0)
                {
                    ax = kcx + Ndx[bk] + 0.5;
                    az = kcz + Ndz[bk] + 0.5;
                }
                else if (here == double.PositiveInfinity)
                {
                    // Off the clearance grid with no clear neighbour: step back to the nearest clearance cell.
                    if (NearestClearCell(w, k.X, k.Z, GoalSnapRings, out int backCx, out int backCz))
                    {
                        ax = backCx + 0.5;
                        az = backCz + 0.5;
                    }
                }
            }
            double ddx = ax - k.X;
            double ddz = az - k.Z;
            double len = Math.Sqrt(ddx * ddx + ddz * ddz);
            if (len < 1e-6) return;
            double stepLen = w.Config.Kaiju.MoveSpeed * dt;
            MoveKaiju(w, (ddx / len) * stepLen, (ddz / len) * stepLen);
            k.Facing = JsMath.Atan2(ddz, ddx);
        }

        /// <summary>One step of the Kaiju: hatch, KO, target choice, slam, pathing and the melee wind-up.</summary>
        public static void KaijuSystem(World w, double dt)
        {
            GameConfig cfg = w.Config;
            KaijuConfig kc = cfg.Kaiju;
            MatchState m = w.Match;
            Kaiju k = w.Kaiju;
            if (m.Phase == MatchPhase.Ended) return;
            // CM-36: the shrine cleanse runs first, so a drain this tick counts before the berserk check below.
            Cleanse.CleanseSystem(w);
            if (!m.Hatched)
            {
                if (m.Instability >= cfg.KaijuFeed.BerserkThreshold) SimResources.LockBerserk(w);
                if (w.ForceHatch || m.Time + HatchTimeEps >= cfg.HatchSeconds) Hatch(w);
                return;
            }
            if (k.State == KaijuState.Egg) return;

            KaijuNav nav = w.KaijuNav;
            int n = w.ActorCount;
            for (int id = 0; id < n; id++) if (k.Threat[id] > 0) k.Threat[id] = JsMath.F32(Combat.TickDown(k.Threat[id], dt));
            k.MeleeCd = Combat.TickDown(k.MeleeCd, dt);
            k.SlamCd = Combat.TickDown(k.SlamCd, dt);
            nav.RepathIn = Combat.TickDown(nav.RepathIn, dt);
            nav.RetargetIn = Combat.TickDown(nav.RetargetIn, dt);

            if (k.KoIn > 0)
            {
                k.KoIn = Combat.TickDown(k.KoIn, dt);
                if (k.KoIn <= 0)
                {
                    k.Hp = k.MaxHp;
                    World.PushEvent(w, SimEventType.KaijuRecover, -1, -1, k.X, k.Z, 0);
                }
                return;
            }

            if (k.TargetId >= 0 && !KaijuCanTarget(w, k.TargetId)) k.TargetId = -1;

            if (k.TelegraphIn > 0)
            {
                k.TelegraphIn = Combat.TickDown(k.TelegraphIn, dt);
                if (k.TelegraphIn <= 0)
                {
                    ResolveSlam(w);
                    if (k.TargetId >= 0 && !KaijuCanTarget(w, k.TargetId)) k.TargetId = -1;
                }
                return; // stands still while winding up
            }

            // CM-26: a melee wind-up in flight. Stands still facing the target; the swing resolves on the last tick.
            if (k.MeleeWindupIn > 0)
            {
                k.MeleeWindupIn--;
                if (k.MeleeWindupIn == 0) ResolveMelee(w);
                return;
            }

            if (nav.RetargetIn <= 0 || k.TargetId < 0)
            {
                k.TargetId = SelectTarget(w);
                if (nav.RetargetIn <= 0) nav.RetargetIn = KaijuRetargetSeconds;
            }

            if (k.SlamCd <= 0 && CountHostilesWithin(w, k.X, k.Z, kc.SlamRadius) >= 2)
            {
                k.TelegraphIn = kc.SlamTelegraphSeconds;
                k.TelegraphX = k.X;
                k.TelegraphZ = k.Z;
                World.PushEvent(w, SimEventType.KaijuTelegraph, -1, -1, k.X, k.Z, kc.SlamRadius);
                return;
            }

            // Movement.
            int t = k.TargetId;
            double gx;
            double gz;
            double stop;
            if (t >= 0)
            {
                gx = w.X[t];
                gz = w.Z[t];
                stop = kc.MeleeRange * ChaseStop;
            }
            else if (k.State == KaijuState.Neutral)
            {
                gx = nav.HomeX;
                gz = nav.HomeZ;
                stop = HomeStop;
            }
            else
            {
                gx = w.Map.Hill.X;
                gz = w.Map.Hill.Z;
                stop = HillStop;
            }
            double ex = gx - k.X;
            double ez = gz - k.Z;
            if (ex * ex + ez * ez > stop * stop) WalkToward(w, gx, gz, dt);
            else if (t >= 0) k.Facing = JsMath.Atan2(ez, ex);

            // Melee (CM-26): start the wind-up; the hit lands meleeWindupTicks later (ResolveMelee).
            if (t >= 0 && k.MeleeCd <= 0)
            {
                double dx = w.X[t] - k.X;
                double dz = w.Z[t] - k.Z;
                if (dx * dx + dz * dz <= kc.MeleeRange * kc.MeleeRange)
                {
                    k.Facing = JsMath.Atan2(dz, dx);
                    k.MeleeWindupIn = (int)kc.MeleeWindupTicks;
                    k.MeleeTargetId = t;
                    k.MeleeCd = kc.MeleeCooldown;
                    World.PushEvent(w, SimEventType.KaijuMeleeWindup, t, -1, w.X[t], w.Z[t], kc.MeleeWindupTicks);
                }
            }
        }
    }
}

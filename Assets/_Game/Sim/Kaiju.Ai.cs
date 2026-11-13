using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>Kaiju behaviour (browser kaiju.ts lines 263-543): slam, melee, grid collider, pathing, kaijuSystem.</summary>
    public sealed partial class Kaiju
    {
        /// <summary>DG-86: the Kaiju's wall collider: PhysicsConfig.KaijuGridRadius while SolidKaiju (its body no longer sinks into walls), else KaijuConfig.GridRadius.</summary>
        public static double GridR(World w)
        {
            return Phys.On(w, w.Config.Physics.SolidKaiju) ? w.Config.Physics.KaijuGridRadius : w.Config.Kaiju.GridRadius;
        }

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
            if (Phys.On(w, w.Config.Physics.SlamShockwave))
            {
                ResolveShockwave(w);
                return;
            }
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
                if (Damage.ApplyDamage(w, id, kc.SlamDamage, Damage.KaijuSlamSource, k.TelegraphX, k.TelegraphZ) > 0) hits++;
            }
            k.SlamCd = kc.SlamCooldown;
            World.PushEvent(w, SimEventType.KaijuSlam, -1, hits, k.TelegraphX, k.TelegraphZ, kc.SlamRadius);
        }

        /// <summary>
        /// DG-85 (S6, OD-8): the slam as a shockwave from the telegraph point: full SlamDamage inside SlamFullRadiusShare x R,
        /// falling linearly to SlamEdgeDamageShare at R; stopped by walls (SegmentClear); an actor in the air on the resolve
        /// tick is untouched (AirDodgesSlam); everyone hit is thrown outward (SlamKnockCentre to SlamKnockEdge) and knocked
        /// down (SlamStunSeconds).
        /// </summary>
        private static void ResolveShockwave(World w)
        {
            KaijuConfig kc = w.Config.Kaiju;
            PhysicsConfig p = w.Config.Physics;
            Kaiju k = w.Kaiju;
            double R = kc.SlamRadius;
            double full = p.SlamFullRadiusShare * R;
            bool walls = Phys.On(w, p.WallsBlockHits);
            bool knock = Phys.On(w, p.Knockback);
            int hits = 0;
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive || !KaijuHostileTo(w, id)) continue;
                double dx = w.X[id] - k.TelegraphX;
                double dz = w.Z[id] - k.TelegraphZ;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d > R) continue;
                if (p.AirDodgesSlam && a.AirLen > 0) continue;
                if (walls && !SegmentClear.Clear(w.Map, k.TelegraphX, k.TelegraphZ, w.X[id], w.Z[id])) continue;
                double share = d <= full ? 1 : 1 - (1 - p.SlamEdgeDamageShare) * (d - full) / (R - full);
                double ux = d > 1e-6 ? dx / d : JsMath.Cos(id * 2.39996);
                double uz = d > 1e-6 ? dz / d : JsMath.Sin(id * 2.39996);
                double kd = p.SlamKnockCentre + (p.SlamKnockEdge - p.SlamKnockCentre) * (d / R);
                if (Damage.ApplyHit(w, id, kc.SlamDamage * share, Damage.KaijuSlamSource, k.TelegraphX, k.TelegraphZ, DamageCause.KaijuSlam,
                    ux, uz, knock ? kd : 0, knock ? w.Dur.SlamStun : 0) > 0) hits++;
            }
            k.SlamCd = kc.SlamCooldown;
            World.PushEvent(w, SimEventType.KaijuSlam, -1, hits, k.TelegraphX, k.TelegraphZ, R, null, DamageCause.None, R, 0, 0, knock ? p.SlamKnockCentre : 0, 2, 0, 0);
        }

        /// <summary>
        /// CM-26: the wound-up swing lands on a still-valid target within meleeRange + MeleeLeash, else a whiff. DG-84 (S7,
        /// OD-7, KaijuClawCone): it hits the cone locked at wind-up start instead (every hostile within reach and the cone with
        /// line of sight, the locked target or not), throws them back (KaijuClawKnock) and stuns them (KaijuStunSeconds).
        /// </summary>
        private static void ResolveMelee(World w)
        {
            KaijuConfig kc = w.Config.Kaiju;
            Kaiju k = w.Kaiju;
            int t = k.MeleeTargetId;
            k.MeleeTargetId = -1;
            if (Phys.On(w, w.Config.Physics.KaijuClawCone))
            {
                ResolveClawCone(w);
                if (t >= 0 && !w.Actors[t].Alive && k.TargetId == t) k.TargetId = -1;
                return;
            }
            if (t < 0 || !KaijuCanTarget(w, t)) return;
            double dx = w.X[t] - k.X;
            double dz = w.Z[t] - k.Z;
            double reach = kc.MeleeRange + MeleeLeash;
            if (dx * dx + dz * dz > reach * reach) return;
            if (Phys.On(w, w.Config.Physics.Knockback))
            {
                Knock.Dir(w, t, k.X, k.Z, JsMath.Cos(k.Facing), JsMath.Sin(k.Facing), out double kx, out double kz);
                Damage.ApplyHit(w, t, kc.MeleeDamage, Damage.KaijuSource, k.X, k.Z, DamageCause.KaijuMelee, kx, kz, w.Config.Physics.KaijuClawKnock, w.Dur.KaijuStun);
            }
            else Damage.ApplyDamage(w, t, kc.MeleeDamage, Damage.KaijuSource, k.X, k.Z);
            if (!w.Actors[t].Alive && k.TargetId == t) k.TargetId = -1;
        }

        /// <summary>DG-84: true when actor id stands inside the locked claw cone (reach and angle).</summary>
        public static bool InClawCone(World w, int id)
        {
            Kaiju k = w.Kaiju;
            double reach = w.Config.Kaiju.MeleeRange + MeleeLeash;
            double dx = w.X[id] - k.ClawX;
            double dz = w.Z[id] - k.ClawZ;
            double d2 = dx * dx + dz * dz;
            if (d2 > reach * reach) return false;
            if (d2 < 1e-12) return true;
            double cosHalf = JsMath.Cos(w.Config.Physics.KaijuClawConeDeg / 2 * Math.PI / 180);
            return (dx * JsMath.Cos(k.ClawFacing) + dz * JsMath.Sin(k.ClawFacing)) / Math.Sqrt(d2) >= cosHalf - 1e-9;
        }

        private static void ResolveClawCone(World w)
        {
            KaijuConfig kc = w.Config.Kaiju;
            PhysicsConfig p = w.Config.Physics;
            Kaiju k = w.Kaiju;
            bool walls = Phys.On(w, p.WallsBlockHits);
            bool knock = Phys.On(w, p.Knockback);
            double fx = JsMath.Cos(k.ClawFacing);
            double fz = JsMath.Sin(k.ClawFacing);
            for (int id = 0; id < w.ActorCount; id++)
            {
                if (!w.Actors[id].Alive || !KaijuHostileTo(w, id) || !InClawCone(w, id)) continue;
                if (walls && !SegmentClear.Clear(w.Map, k.ClawX, k.ClawZ, w.X[id], w.Z[id])) continue;
                Knock.Dir(w, id, k.ClawX, k.ClawZ, fx, fz, out double dx, out double dz);
                Damage.ApplyHit(w, id, kc.MeleeDamage, Damage.KaijuSource, k.ClawX, k.ClawZ, DamageCause.KaijuMelee, dx, dz,
                    knock ? p.KaijuClawKnock : 0, knock ? w.Dur.KaijuStun : 0);
            }
        }

        /// <summary>Push the Kaiju's grid circle out of every non-walkable cell it overlaps (out of the map counts as blocked).</summary>
        private static void ResolveCircle(World w)
        {
            Kaiju k = w.Kaiju;
            MapDef map = w.Map;
            double r = GridR(w);
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
            double r = GridR(w);
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
            bool guard = Phys.On(w, w.Config.Physics.SolidKaiju);
            for (int s = 0; s < steps; s++)
            {
                double px = k.X;
                double pz = k.Z;
                k.X += sx;
                k.Z += sz;
                ResolveCircle(w);
                ClampToBounds(w);
                if (guard)
                {
                    // DG-100 (S19a): Float32 positions like the actors', so arm64 / x64 / Rosetta agree bit for bit.
                    k.X = JsMath.F32(k.X);
                    k.Z = JsMath.F32(k.Z);
                }
                // DG-86: a concave corner the 4-pass push-out cannot resolve for the wider collider: stay put this sub-step.
                if (guard && !CircleClear(w))
                {
                    k.X = px;
                    k.Z = pz;
                }
            }
        }

        /// <summary>DG-86: move the Kaiju by (dx, dz) through its wall collider (a fighter wedged against a wall holds it back).</summary>
        internal static void ShoveKaiju(World w, double dx, double dz)
        {
            Kaiju k = w.Kaiju;
            double x0 = k.X;
            double z0 = k.Z;
            MoveKaiju(w, dx, dz);
            if (!CircleClear(w))
            {
                k.X = x0; // a corner the collider cannot resolve: the body does not give way
                k.Z = z0;
            }
        }

        /// <summary>DG-86: true when the Kaiju's wall collider (GridR) overlaps no blocked cell.</summary>
        public static bool CircleClear(World w)
        {
            Kaiju k = w.Kaiju;
            MapDef map = w.Map;
            double r = GridR(w) - 1e-3;
            for (int gz = JsMath.FloorToInt(k.Z - r); gz <= JsMath.FloorToInt(k.Z + r); gz++)
            {
                for (int gx = JsMath.FloorToInt(k.X - r); gx <= JsMath.FloorToInt(k.X + r); gx++)
                {
                    if (MapGrid.IsWalkable(map, gx, gz)) continue;
                    double nx = k.X < gx ? gx : (k.X > gx + 1 ? gx + 1.0 : k.X);
                    double nz = k.Z < gz ? gz : (k.Z > gz + 1 ? gz + 1.0 : k.Z);
                    if ((k.X - nx) * (k.X - nx) + (k.Z - nz) * (k.Z - nz) < r * r) return false;
                }
            }
            return true;
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
                if (m.Instability >= SimResources.BerserkLine(w)) SimResources.LockBerserk(w); // DG-41
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
                k.TelegraphX = k.X;
                k.TelegraphZ = k.Z;
                if (Phys.On(w, true))
                {
                    // DG-85 / OD-7: at least KaijuSlamMinRealSeconds on screen at the match pace (48 ticks at pace 2).
                    k.TelegraphIn = w.Dur.SlamTelegraph / cfg.SimHz;
                    World.PushEvent(w, SimEventType.KaijuTelegraph, -1, -1, k.X, k.Z, kc.SlamRadius, null, DamageCause.None, kc.SlamRadius, 0, 0, 0, 0, w.Dur.SlamTelegraph, 0);
                }
                else
                {
                    k.TelegraphIn = kc.SlamTelegraphSeconds;
                    World.PushEvent(w, SimEventType.KaijuTelegraph, -1, -1, k.X, k.Z, kc.SlamRadius);
                }
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
                    k.MeleeWindupIn = w.Dur.KaijuClawWindup;
                    k.MeleeWindupLen = w.Dur.KaijuClawWindup;
                    k.MeleeTargetId = t;
                    k.MeleeCd = kc.MeleeCooldown;
                    k.ClawX = k.X;
                    k.ClawZ = k.Z;
                    k.ClawFacing = k.Facing;
                    // DG-84: the event carries the cone (Radius = reach, DirX / DirZ = facing, Windup = ticks; angle = KaijuClawConeDeg).
                    if (Phys.On(w, true))
                        World.PushEvent(w, SimEventType.KaijuMeleeWindup, t, -1, w.X[t], w.Z[t], w.Dur.KaijuClawWindup, null, DamageCause.None,
                            kc.MeleeRange + MeleeLeash, JsMath.Cos(k.Facing), JsMath.Sin(k.Facing), 0, 0, w.Dur.KaijuClawWindup, 0);
                    else World.PushEvent(w, SimEventType.KaijuMeleeWindup, t, -1, w.X[t], w.Z[t], w.Dur.KaijuClawWindup);
                }
            }
        }
    }
}

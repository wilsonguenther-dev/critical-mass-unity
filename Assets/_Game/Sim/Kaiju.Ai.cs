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
        private static int CountHostilesWithin(World w, double x, double z, double r, bool seen = false)
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
                if (dx * dx + dz * dz > r2) continue;
                // fix/movement-shiver-1: with `seen`, a fighter behind a wall does not count (the shockwave would not reach it).
                if (seen && !SegmentClear.Clear(w.Map, x, z, w.X[id], w.Z[id])) continue;
                c++;
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
            double r2 = KaijuTypes.SlamRadius(w) * KaijuTypes.SlamRadius(w);
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
            k.SlamCd = KaijuTypes.SlamCooldown(w);
            World.PushEvent(w, SimEventType.KaijuSlam, -1, hits, k.TelegraphX, k.TelegraphZ, KaijuTypes.SlamRadius(w));
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
            double R = KaijuTypes.SlamRadius(w);
            double full = p.SlamFullRadiusShare * R;
            bool walls = Phys.On(w, p.WallsBlockHits);
            bool knock = Damage.Knocks(w, DamageCause.KaijuSlam);
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
                    ux, uz, knock ? kd * Damage.KaijuThrow(w) : 0, knock ? w.Dur.SlamStun : 0) > 0) hits++;
            }
            k.SlamCd = KaijuTypes.SlamCooldown(w);
            World.PushEvent(w, SimEventType.KaijuSlam, -1, hits, k.TelegraphX, k.TelegraphZ, R, null, DamageCause.None, R, 0, 0, knock ? p.SlamKnockCentre : 0, 2, 0, 0);
            // fix/kaiju-alive-1: the slam ring knocks structures down (after the KaijuSlam event, so the view lands both).
            if (Phys.On(w, p.KaijuTopples)) Topple.ToppleInArc(w, -1, k.TelegraphX, k.TelegraphZ, 1, 0, Math.Max(0, R * p.KaijuSlamToppleShare - w.Config.Abilities.Topple.Reach), -1);
            if (Phys.On(w, p.KaijuHillSiege)) SmashBarricades(w, k.TelegraphX, k.TelegraphZ, 1, 0, R * p.KaijuSlamToppleShare, -1);
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
            if (Damage.Knocks(w, DamageCause.KaijuMelee))
            {
                Knock.Dir(w, t, k.X, k.Z, JsMath.Cos(k.Facing), JsMath.Sin(k.Facing), out double kx, out double kz);
                Damage.ApplyHit(w, t, KaijuTypes.MeleeDamage(w, kc.MeleeDamage), Damage.KaijuSource, k.X, k.Z, DamageCause.KaijuMelee, kx, kz, w.Config.Physics.KaijuClawKnock, w.Dur.KaijuStun);
            }
            else Damage.ApplyDamage(w, t, KaijuTypes.MeleeDamage(w, kc.MeleeDamage), Damage.KaijuSource, k.X, k.Z);
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
            bool knock = Damage.Knocks(w, DamageCause.KaijuMelee);
            double fx = JsMath.Cos(k.ClawFacing);
            double fz = JsMath.Sin(k.ClawFacing);
            // fix/kaiju-alive-1: the claw arc takes one hit off every standing structure in the cone (reach as for actors).
            if (Phys.On(w, p.KaijuTopples))
                Topple.ToppleInArc(w, -1, k.ClawX, k.ClawZ, fx, fz, Math.Max(0, kc.MeleeRange + MeleeLeash - w.Config.Abilities.Topple.Reach),
                    JsMath.Cos(p.KaijuClawConeDeg / 2 * Math.PI / 180));
            if (Phys.On(w, p.KaijuHillSiege))
                SmashBarricades(w, k.ClawX, k.ClawZ, fx, fz, kc.MeleeRange + MeleeLeash, JsMath.Cos(p.KaijuClawConeDeg / 2 * Math.PI / 180));
            for (int id = 0; id < w.ActorCount; id++)
            {
                if (!w.Actors[id].Alive || !KaijuHostileTo(w, id) || !InClawCone(w, id)) continue;
                if (walls && !SegmentClear.Clear(w.Map, k.ClawX, k.ClawZ, w.X[id], w.Z[id])) continue;
                Knock.Dir(w, id, k.ClawX, k.ClawZ, fx, fz, out double dx, out double dz);
                Damage.ApplyHit(w, id, KaijuTypes.MeleeDamage(w, kc.MeleeDamage), Damage.KaijuSource, k.ClawX, k.ClawZ, DamageCause.KaijuMelee, dx, dz,
                    knock ? p.KaijuClawKnock * Damage.KaijuThrow(w) : 0, knock ? w.Dur.KaijuStun : 0);
            }
        }

        /// <summary>Push the Kaiju's grid circle out of every non-walkable cell it overlaps (out of the map counts as blocked).</summary>
        private static void ResolveCircle(World w)
        {
            Kaiju k = w.Kaiju;
            MapDef map = w.Map;
            double r = WallR(w);
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
            double r = WallR(w);
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
            bool squeeze = SqueezeOn(w);
            for (int s = 0; s < steps; s++)
            {
                double px = k.X;
                double pz = k.Z;
                if (squeeze)
                {
                    // feat/kaiju-bridges-2: on a diagonal deck the Kaiju moves by the actors' deck rule (CM-33), not the raster
                    // push-out, which would shove it a cell sideways off the stair-steps. A step that lands on the deck is taken;
                    // from the deck, it steps only to where it may stand (deck or clear circle), else the axis parts, else stays.
                    double fx = JsMath.F32(px + sx);
                    double fz = JsMath.F32(pz + sz);
                    if (OnDeck(w, fx, fz)) { k.X = fx; k.Z = fz; continue; }
                    if (OnDeck(w, px, pz))
                    {
                        if (KaijuFits(w, fx, fz)) { k.X = fx; k.Z = fz; }
                        else if (KaijuFits(w, fx, pz)) k.X = fx;
                        else if (KaijuFits(w, px, fz)) k.Z = fz;
                        continue;
                    }
                }
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

        /// <summary>DG-86: true when the Kaiju's wall collider (WallR) overlaps no blocked cell.</summary>
        public static bool CircleClear(World w)
        {
            Kaiju k = w.Kaiju;
            return CircleClearAt(w, k.X, k.Z, WallR(w)) || (SqueezeOn(w) && OnDeck(w, k.X, k.Z));
        }

        /// <summary>feat/kaiju-bridges-2: the squeezed Kaiju's centre at (x, z) is on a diagonal bridge's deck (Collide.DeckSpanAt).</summary>
        private static bool OnDeck(World w, double x, double z)
        {
            double r = WallR(w);
            return r <= w.Config.Physics.KaijuSqueezeRadius + 1e-9 && Collide.DeckSpanAt(w, x, z, r) != null;
        }

        /// <summary>feat/kaiju-bridges-2: the Kaiju may stand at (x, z): its wall circle is clear, or it is on a deck.</summary>
        private static bool KaijuFits(World w, double x, double z) => CircleClearAt(w, x, z, WallR(w)) || OnDeck(w, x, z);

        /// <summary>
        /// fix/kaiju-hill-1: KaijuBarricadeDamage off every barricade (any team) whose cell centre is within `range` of (ax, az)
        /// inside the cone (cosHalf -1 = all round), not through another wall (the DG-83 rule the fighters' chip uses).
        /// </summary>
        private static void SmashBarricades(World w, double ax, double az, double fx, double fz, double range, double cosHalf)
        {
            Barricade[] list = w.Barricades;
            if (list == null) return;
            double r2 = range * range;
            double dmg = w.Config.Physics.KaijuBarricadeDamage;
            bool walls = Phys.On(w, w.Config.Physics.WallsBlockHits);
            for (int s = 0; s < list.Length; s++)
            {
                Barricade b = list[s];
                if (!b.Active || b.Hp <= 0) continue;
                double dx = b.Cx + 0.5 - ax;
                double dz = b.Cz + 0.5 - az;
                double d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                if (d2 > 1e-12 && (dx * fx + dz * fz) / Math.Sqrt(d2) < cosHalf - 1e-9) continue;
                if (walls && !SegmentClear.Clear(w.Map, ax, az, b.Cx + 0.5, b.Cz + 0.5, b.Cx, b.Cz)) continue;
                b.Hp -= dmg;
            }
        }

        /// <summary>fix/kaiju-hill-1 round 3: KaijuBarricadeCrushPerSecond x dt off every barricade whose cell is within its wall
        /// collider + 0.25 m (pressing on it), any team.</summary>
        private static void CrushBarricades(World w, double dt)
        {
            Barricade[] list = w.Barricades;
            if (list == null) return;
            Kaiju k = w.Kaiju;
            double r = WallR(w) + 0.25;
            double dmg = w.Config.Physics.KaijuBarricadeCrushPerSecond * dt;
            for (int s = 0; s < list.Length; s++)
            {
                Barricade b = list[s];
                if (!b.Active || b.Hp <= 0) continue;
                double nx = k.X < b.Cx ? b.Cx : (k.X > b.Cx + 1 ? b.Cx + 1.0 : k.X);
                double nz = k.Z < b.Cz ? b.Cz : (k.Z > b.Cz + 1 ? b.Cz + 1.0 : k.Z);
                if ((k.X - nx) * (k.X - nx) + (k.Z - nz) * (k.Z - nz) > r * r) continue;
                b.Hp -= dmg;
            }
        }

        /// <summary>
        /// fix/kaiju-hill-1: with no target and the hill cut off from its clearance region, pick (every 1 s) the active barricade
        /// within KaijuSiegeRadius of the hill nearest to it that has a cell of its own region within claw reach, and the clear cell
        /// of its region nearest that barricade to claw from. None in the way: the cell of its region nearest the hill (it walks
        /// as close as it can instead of standing still). Returns false when the hill is reachable (walk there as before).
        /// </summary>
        private static bool PickSiege(World w)
        {
            Kaiju k = w.Kaiju;
            KaijuNav nav = w.KaijuNav;
            MapDef map = w.Map;
            HillDef hill = map.Hill;
            RefreshClearance(w);
            int mine = RegionAt(w, k.X, k.Z, out _, out _);
            if (mine == 0 || RegionAt(w, hill.X, hill.Z, out _, out _) == mine)
            {
                nav.SiegeCx = nav.SiegeCz = nav.StandCx = nav.StandCz = -1;
                return false;
            }
            if (nav.SiegeIn > 0 && nav.StandCx >= 0) return true;
            nav.SiegeIn = 1;
            int W = map.Width;
            double reach = w.Config.Kaiju.MeleeRange;
            int rc = (int)Math.Ceiling(reach);
            double sr2 = w.Config.Physics.KaijuSiegeRadius * w.Config.Physics.KaijuSiegeRadius;
            double best = double.PositiveInfinity;
            nav.SiegeCx = nav.SiegeCz = nav.StandCx = nav.StandCz = -1;
            Barricade[] list = w.Barricades;
            for (int s = 0; list != null && s < list.Length; s++)
            {
                Barricade b = list[s];
                if (!b.Active || b.Hp <= 0) continue;
                double bx = b.Cx + 0.5, bz = b.Cz + 0.5;
                if ((bx - hill.X) * (bx - hill.X) + (bz - hill.Z) * (bz - hill.Z) > sr2) continue;
                // The clear cell of its own region nearest the barricade, within claw reach of it.
                int sx = -1, sz = -1;
                double sd = double.PositiveInfinity;
                for (int dz = -rc; dz <= rc; dz++)
                    for (int dx = -rc; dx <= rc; dx++)
                    {
                        int cx = b.Cx + dx, cz = b.Cz + dz;
                        if (!MapGrid.InBounds(map, cx, cz) || nav.Region[cz * W + cx] != mine) continue;
                        double d = (cx + 0.5 - bx) * (cx + 0.5 - bx) + (cz + 0.5 - bz) * (cz + 0.5 - bz);
                        if (d > reach * reach || d >= sd) continue;
                        sd = d; sx = cx; sz = cz;
                    }
                if (sx < 0) continue;
                double dk = (bx - k.X) * (bx - k.X) + (bz - k.Z) * (bz - k.Z);
                if (dk >= best) continue;
                best = dk;
                nav.SiegeCx = b.Cx; nav.SiegeCz = b.Cz; nav.StandCx = sx; nav.StandCz = sz;
            }
            if (nav.StandCx >= 0) return true;
            // Nothing to break: the cell of its own region closest to the hill centre.
            double bh = double.PositiveInfinity;
            for (int i = 0; i < W * map.Height; i++)
            {
                if (nav.Region[i] != mine) continue;
                double cx = i % W + 0.5, cz = i / W + 0.5;
                double d = (cx - hill.X) * (cx - hill.X) + (cz - hill.Z) * (cz - hill.Z);
                if (d >= bh) continue;
                bh = d; nav.StandCx = i % W; nav.StandCz = i / W;
            }
            return nav.StandCx >= 0;
        }

        /// <summary>feat/kaiju-bridges-1: the live wall collider radius: GridR, or the squeezed radius on and near a bridge.</summary>
        public static double WallR(World w)
        {
            double full = GridR(w);
            if (!SqueezeOn(w)) return full;
            double r = w.KaijuNav.WallR;
            return r > 0 && r < full ? r : full;
        }

        /// <summary>
        /// feat/kaiju-bridges-1: squeeze the wall collider to KaijuSqueezeRadius while a squeeze cell (a bridge cell it fits only
        /// squeezed) is within reach of its body, so it can step onto the bridge; shrinking never overlaps a wall. Away from them it
        /// grows back at KaijuUnsqueezeRate, and only by as much as fits, so it is never pushed out with a pop.
        /// </summary>
        private static void UpdateWallR(World w, double dt)
        {
            Kaiju k = w.Kaiju;
            KaijuNav nav = w.KaijuNav;
            PhysicsConfig pc = w.Config.Physics;
            double full = GridR(w);
            if (nav.WallR <= 0) nav.WallR = full;
            RefreshClearance(w);
            MapDef map = w.Map;
            int reach = (int)Math.Ceiling(full) + 1;
            int kx = JsMath.FloorToInt(k.X);
            int kz = JsMath.FloorToInt(k.Z);
            bool near = false;
            for (int dz = -reach; dz <= reach && !near; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = kx + dx, z = kz + dz;
                    if (MapGrid.InBounds(map, x, z) && nav.Squeeze[z * map.Width + x] != 0) { near = true; break; }
                }
            if (near)
            {
                nav.WallR = Math.Min(nav.WallR, pc.KaijuSqueezeRadius);
                return;
            }
            if (nav.WallR >= full) return;
            double grown = JsMath.F32(Math.Min(full, nav.WallR + pc.KaijuUnsqueezeRate * dt));
            if (CircleClearAt(w, k.X, k.Z, grown)) nav.WallR = grown;
        }

        /// <summary>
        /// feat/kaiju-bridges-1, for the view only (no Sim meaning): 0 = full wall collider, 1 = fully squeezed onto a bridge.
        /// The animator tucks the arms and narrows the stance by it so the wide-stance model does not stand on air.
        /// </summary>
        public static double SqueezeAmount(World w)
        {
            if (!SqueezeOn(w)) return 0;
            double full = GridR(w);
            double sr = w.Config.Physics.KaijuSqueezeRadius;
            if (full - sr < 1e-6) return 0;
            double a = (full - WallR(w)) / (full - sr);
            return a < 0 ? 0 : (a > 1 ? 1 : a);
        }

        /// <summary>True when a wall collider of radius `radius` at the Kaiju overlaps no blocked cell.</summary>
        private static bool CircleClearAt(World w, double x, double z, double radius)
        {
            MapDef map = w.Map;
            double r = radius - 1e-3;
            for (int gz = JsMath.FloorToInt(z - r); gz <= JsMath.FloorToInt(z + r); gz++)
            {
                for (int gx = JsMath.FloorToInt(x - r); gx <= JsMath.FloorToInt(x + r); gx++)
                {
                    if (MapGrid.IsWalkable(map, gx, gz)) continue;
                    double nx = x < gx ? gx : (x > gx + 1 ? gx + 1.0 : x);
                    double nz = z < gz ? gz : (z > gz + 1 ? gz + 1.0 : z);
                    if ((x - nx) * (x - nx) + (z - nz) * (z - nz) < r * r) return false;
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
            // fix/movement-shiver-1: the target has no cell the body fits in nearby: hold, face it, let the give-up rule act.
            bool guard = Phys.On(w, w.Config.Physics.KaijuNavGuard);
            if (guard && goalCx < 0)
            {
                k.Facing = JsMath.Atan2(gz - k.Z, gx - k.X);
                return;
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
                else if (guard)
                {
                    ax = nav.GoalCx + 0.5;
                    az = nav.GoalCz + 0.5;
                }
            }
            else if (guard && nav.GoalCx >= 0 && !(JsMath.FloorToInt(gx) == nav.GoalCx && JsMath.FloorToInt(gz) == nav.GoalCz))
            {
                // fix/movement-shiver-1 (the shiver at the bridge mouth): in the goal cell but the target stands where the body
                // does not fit. The old code walked straight at it; the collider pushed the body back out of the cell, the field
                // steered it back in, and it flipped direction every tick. Now it settles on the goal cell's centre and holds.
                ax = nav.GoalCx + 0.5;
                az = nav.GoalCz + 0.5;
                double hx = ax - k.X;
                double hz = az - k.Z;
                if (hx * hx + hz * hz < HoldEps * HoldEps)
                {
                    k.Facing = JsMath.Atan2(gz - k.Z, gx - k.X);
                    return;
                }
            }
            double ddx = ax - k.X;
            double ddz = az - k.Z;
            double len = Math.Sqrt(ddx * ddx + ddz * ddz);
            if (len < 1e-6) return;
            double stepLen = KaijuTypes.MoveSpeed(w) * dt; // b13-k: KaijuConfig.MoveSpeed itself for ELDER
            if (guard && len < stepLen) stepLen = len; // arrive on the point, do not overshoot and come back
            double x0 = k.X;
            double z0 = k.Z;
            MoveKaiju(w, (ddx / len) * stepLen, (ddz / len) * stepLen);
            if (!guard)
            {
                k.Facing = JsMath.Atan2(ddz, ddx);
                return;
            }
            // fix/movement-shiver-1: face the way the body actually went; pressed against a wall, face the target (a steady
            // direction) instead of the steering vector the collider just refused.
            double mx = k.X - x0;
            double mz = k.Z - z0;
            if (mx * mx + mz * mz >= (0.25 * stepLen) * (0.25 * stepLen)) k.Facing = JsMath.Atan2(mz, mx);
            else k.Facing = JsMath.Atan2(gz - k.Z, gx - k.X);
        }

        /// <summary>
        /// One step of the Kaiju: hatch, KO, target choice, slam, pathing and the melee wind-up (KaijuStep), then b14-c's
        /// end-of-tick guarantee that no grounded fighter is left inside its body (Collide.KaijuDepenetrate, Full only).
        /// </summary>
        public static void KaijuSystem(World w, double dt)
        {
            // b16 [8]: the sim facing turns at most PhysicsFeel.KaijuTurnStep a tick (it flipped up to 179 degrees in one tick and
            // the claw cone followed it); a claw starts only once the Kaiju faces its target (TurnReady). [2]: the blast slide.
            Kaiju k = w.Kaiju;
            bool cap = PhysicsFeel.On(w) && w.Match.Hatched && k.State != KaijuState.Egg;
            double f0 = k.Facing;
            _turnCap = cap;
            _turnFrom = f0;
            _turnMax = cap ? PhysicsFeel.KaijuTurnStep(w) : 0;
            if (cap) PhysicsFeel.KaijuPushStep(w, dt);
            KaijuStep(w, dt);
            if (cap) k.Facing = PhysicsFeel.CapTurn(f0, k.Facing, _turnMax);
            _turnCap = false;
            Collide.KaijuDepenetrate(w);
        }

        /// <summary>b16 [8]: this tick's turn cap (set and cleared by KaijuSystem around KaijuStep; scratch, not state).</summary>
        private static bool _turnCap;
        private static double _turnFrom, _turnMax;

        /// <summary>b16 [8]: true when a claw toward facing `want` may start this tick (no turn cap, or within one tick's turn).</summary>
        private static bool TurnReady(double want) => !_turnCap || Math.Abs(PhysicsFeel.AngleDelta(want, _turnFrom)) <= _turnMax + 1e-9;

        private static void KaijuStep(World w, double dt)
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
                if (w.ForceHatch || m.Time + HatchTimeEps >= cfg.HatchSeconds)
                {
                    Hatch(w);
                    // U-074: the body appears on top of whoever stands there; push them out on the hatch tick itself (the
                    // separation pass of this step already ran while the Kaiju was still an egg). No-op with SolidKaiju off.
                    Collide.KaijuBody(w);
                }
                return;
            }
            if (k.State == KaijuState.Egg) return;
            KaijuTypes.Step(w, dt); // b13-k: REGROWTH and SHROUD (no-op for ELDER)

            KaijuNav nav = w.KaijuNav;
            int n = w.ActorCount;
            for (int id = 0; id < n; id++) if (k.Threat[id] > 0) k.Threat[id] = JsMath.F32(Combat.TickDown(k.Threat[id], dt));
            k.MeleeCd = Combat.TickDown(k.MeleeCd, dt);
            k.SlamCd = Combat.TickDown(k.SlamCd, dt);
            nav.RepathIn = Combat.TickDown(nav.RepathIn, dt);
            nav.RetargetIn = Combat.TickDown(nav.RetargetIn, dt);
            PhysicsConfig pc = cfg.Physics;
            bool guard = Phys.On(w, pc.KaijuNavGuard);
            bool seen = guard && Phys.On(w, pc.WallsBlockHits);
            if (guard) for (int id = 0; id < n; id++) if (nav.Ignore[id] > 0) nav.Ignore[id] = Combat.TickDown(nav.Ignore[id], dt);
            if (SqueezeOn(w)) UpdateWallR(w, dt);
            nav.SiegeIn = Combat.TickDown(nav.SiegeIn, dt);
            if (Phys.On(w, pc.KaijuHillSiege)) for (int id = 0; id < n; id++) if (nav.HitBy[id] > 0) nav.HitBy[id] = Combat.TickDown(nav.HitBy[id], dt);

            if (k.KoIn > 0)
            {
                k.KoIn = Combat.TickDown(k.KoIn, dt);
                if (k.KoIn <= 0)
                {
                    k.Hp = k.MaxHp;
                    Corruption.AtGetUp(w);   // b13-hook
                    Comeback.OnKaijuRecover(w);   // b15-hook: TURNCOAT (no-op off)
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

            if (k.SlamCd <= 0 && CountHostilesWithin(w, k.X, k.Z, KaijuTypes.SlamRadius(w), seen) >= 2)
            {
                k.TelegraphX = k.X;
                k.TelegraphZ = k.Z;
                if (Phys.On(w, true))
                {
                    // DG-85 / OD-7: at least KaijuSlamMinRealSeconds on screen at the match pace (48 ticks at pace 2).
                    k.TelegraphIn = w.Dur.SlamTelegraph / cfg.SimHz;
                    World.PushEvent(w, SimEventType.KaijuTelegraph, -1, -1, k.X, k.Z, KaijuTypes.SlamRadius(w), null, DamageCause.None, KaijuTypes.SlamRadius(w), 0, 0, 0, 0, w.Dur.SlamTelegraph, 0);
                }
                else
                {
                    k.TelegraphIn = kc.SlamTelegraphSeconds;
                    World.PushEvent(w, SimEventType.KaijuTelegraph, -1, -1, k.X, k.Z, KaijuTypes.SlamRadius(w));
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
                // fix/kaiju-hill-1: the hill is cut off (barricades in the ring's gaps): break through, or get as close as it can.
                if (guard && Phys.On(w, pc.KaijuHillSiege) && PickSiege(w))
                {
                    gx = nav.StandCx + 0.5;
                    gz = nav.StandCz + 0.5;
                    stop = HoldEps;
                    if (nav.SiegeCx >= 0 && k.MeleeCd <= 0 && k.MeleeWindupIn <= 0)
                    {
                        double bx = nav.SiegeCx + 0.5 - k.X;
                        double bz = nav.SiegeCz + 0.5 - k.Z;
                        if (bx * bx + bz * bz <= kc.MeleeRange * kc.MeleeRange && !TurnReady(JsMath.Atan2(bz, bx))) k.Facing = JsMath.Atan2(bz, bx);   // b16 [8]: turn first
                        else if (bx * bx + bz * bz <= kc.MeleeRange * kc.MeleeRange)
                        {
                            // Claw the barricade (no actor target): ResolveClawCone smashes it and hits any fighter in the cone.
                            k.Facing = JsMath.Atan2(bz, bx);
                            k.MeleeWindupIn = w.Dur.KaijuClawWindup;
                            k.MeleeWindupLen = w.Dur.KaijuClawWindup;
                            k.MeleeTargetId = -1;
                            k.MeleeCd = KaijuTypes.MeleeCooldown(w);
                            k.ClawX = k.X;
                            k.ClawZ = k.Z;
                            k.ClawFacing = k.Facing;
                            World.PushEvent(w, SimEventType.KaijuMeleeWindup, -1, -1, nav.SiegeCx + 0.5, nav.SiegeCz + 0.5, w.Dur.KaijuClawWindup, null,
                                DamageCause.None, kc.MeleeRange + MeleeLeash, JsMath.Cos(k.Facing), JsMath.Sin(k.Facing), 0, 0, w.Dur.KaijuClawWindup, 0);
                        }
                    }
                }
            }
            double ex = gx - k.X;
            double ez = gz - k.Z;
            if (ex * ex + ez * ez > stop * stop) WalkToward(w, gx, gz, dt);
            else if (t >= 0) k.Facing = JsMath.Atan2(ez, ex);
            // fix/movement-shiver-2: close to its target (claw reach + 1.5) it faces the target on walking ticks too. A fighter
            // circling at the chase stop made it walk one tick (facing its path cell) and stand the next (facing the fighter):
            // the facing swung between the two every tick on the hill.
            if (guard && t >= 0 && ex * ex + ez * ez <= (kc.MeleeRange + 1.5) * (kc.MeleeRange + 1.5)) k.Facing = JsMath.Atan2(ez, ex);

            // fix/kaiju-hill-1 round 3: its body crushes the barricades it presses against (its plan goes through them).
            if (guard && Phys.On(w, pc.KaijuHillSiege)) CrushBarricades(w, dt);

            // fix/movement-shiver-1: give a target up after KaijuGiveUpSeconds without progress (net move under
            // KaijuProgressRadius while not in claw reach of it with a clear line); it is skipped for KaijuIgnoreSeconds.
            if (guard)
            {
                double mx = k.X - nav.ProgressX;
                double mz = k.Z - nav.ProgressZ;
                bool striking = false;
                if (t >= 0)
                {
                    double sx = w.X[t] - k.X;
                    double sz = w.Z[t] - k.Z;
                    striking = sx * sx + sz * sz <= kc.MeleeRange * kc.MeleeRange && (!seen || SegmentClear.Clear(w.Map, k.X, k.Z, w.X[t], w.Z[t]));
                }
                if (t < 0 || striking || mx * mx + mz * mz >= pc.KaijuProgressRadius * pc.KaijuProgressRadius)
                {
                    nav.ProgressX = k.X;
                    nav.ProgressZ = k.Z;
                    nav.NoProgress = 0;
                }
                else
                {
                    nav.NoProgress += dt;
                    if (nav.NoProgress >= pc.KaijuGiveUpSeconds)
                    {
                        nav.Ignore[t] = pc.KaijuIgnoreSeconds;
                        k.TargetId = -1;
                        nav.RetargetIn = 0;
                        nav.ProgressX = k.X;
                        nav.ProgressZ = k.Z;
                        nav.NoProgress = 0;
                        return;
                    }
                }
            }

            // Melee (CM-26): start the wind-up; the hit lands meleeWindupTicks later (ResolveMelee).
            if (t >= 0 && k.MeleeCd <= 0)
            {
                double dx = w.X[t] - k.X;
                double dz = w.Z[t] - k.Z;
                // fix/movement-shiver-1: no swing at a fighter behind a wall (the claw would be blocked: a whiff every 1.6 s).
                bool canClaw = dx * dx + dz * dz <= kc.MeleeRange * kc.MeleeRange && (!seen || SegmentClear.Clear(w.Map, k.X, k.Z, w.X[t], w.Z[t]));
                // b16 [8]: under the turn cap it turns toward the target first and claws once it faces it (the cone = the facing).
                if (canClaw && !TurnReady(JsMath.Atan2(dz, dx))) k.Facing = JsMath.Atan2(dz, dx);
                else if (canClaw)
                {
                    k.Facing = JsMath.Atan2(dz, dx);
                    k.MeleeWindupIn = w.Dur.KaijuClawWindup;
                    k.MeleeWindupLen = w.Dur.KaijuClawWindup;
                    k.MeleeTargetId = t;
                    k.MeleeCd = KaijuTypes.MeleeCooldown(w);
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

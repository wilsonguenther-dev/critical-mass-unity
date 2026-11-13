using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Class abilities (CM-05, port of src/sim/abilities.ts on 09e0904). Runs first in SimStep.Step, before movement;
    /// combat then ticks this step's timers once. Miner wall break is part of the miner's basic attack (Combat). Then
    /// Terraform.TerraformSystem (CM-30): miner dig / place on InputFrame.Terraform, independent of AbilityCd.
    /// Allocation-free: static scratch only.
    /// </summary>
    public static class Abilities
    {
        /// <summary>Tolerance of the +/-45 degree wall-break arc test (browser ANGLE_EPS).</summary>
        private const double AngleEps = 1e-9;

        /// <summary>Cosine of the +/-45 degree arc in front of the miner (browser WALL_BREAK_COS).</summary>
        private static readonly double WallBreakCos = JsMath.Cos(Math.PI / 4);

        /// <summary>Scratch ability target point (browser module-scope `target`).</summary>
        private static double TargetX;
        private static double TargetZ;

        /// <summary>
        /// The point (aimX, aimZ) pulled back toward (ox, oz) so it is at most `range` away.
        /// Non-finite aim gives the point `range` ahead along `facing`.
        /// </summary>
        public static void ClampToRange(double ox, double oz, double aimX, double aimZ, double facing, double range, out double x, out double z)
        {
            if (!double.IsFinite(aimX) || !double.IsFinite(aimZ))
            {
                x = ox + JsMath.Cos(facing) * range;
                z = oz + JsMath.Sin(facing) * range;
                return;
            }
            double dx = aimX - ox;
            double dz = aimZ - oz;
            double d2 = dx * dx + dz * dz;
            if (d2 > range * range)
            {
                double k = range / Math.Sqrt(d2);
                x = ox + dx * k;
                z = oz + dz * k;
            }
            else
            {
                x = aimX;
                z = aimZ;
            }
        }

        /// <summary>Where actor `id`'s ability would land this step (clamped to `range`; facing when it has no aim).</summary>
        private static void AbilityTarget(World world, int id, double range, out double x, out double z)
        {
            InputFrame inp = world.Inputs[id];
            bool hasAim = world.HasAim[id] == 1;
            ClampToRange(world.X[id], world.Z[id], hasAim ? inp.AimX : double.NaN, hasAim ? inp.AimZ : double.NaN, world.Actors[id].Facing, range, out x, out z);
        }

        private static void CastWarriorShield(World world, int id)
        {
            Actor a = world.Actors[id];
            a.AbilityActive = world.Config.Abilities.WarriorShield.DurationSeconds;
            a.AbilityCd = world.Config.Class(ClassId.Warrior).AbilityCooldown;
            a.AnimAction = 0;
            World.PushEvent(world, SimEventType.Ability, id, -1, world.X[id], world.Z[id], 1);
        }

        private static void CastFarmerRoots(World world, int id)
        {
            Actor a = world.Actors[id];
            FarmerRootsConfig r = world.Config.Abilities.FarmerRoots;
            AbilityTarget(world, id, r.CastRange, out TargetX, out TargetZ);
            double r2 = r.Radius * r.Radius;
            for (int j = 0; j < world.ActorCount; j++)
            {
                Actor t = world.Actors[j];
                if (!t.Alive || t.Team == a.Team) continue;
                double dx = world.X[j] - TargetX;
                double dz = world.Z[j] - TargetZ;
                if (dx * dx + dz * dz <= r2) t.SlowTimer = r.SlowSeconds;
            }
            a.AbilityCd = world.Config.Class(ClassId.Farmer).AbilityCooldown;
            a.AnimAction = 0;
            World.PushEvent(world, SimEventType.Ability, id, -1, TargetX, TargetZ, 1);
        }

        private static void CastMinerBarricade(World world, int id)
        {
            // The aim is clamped (and the cell picked) inside TryPlaceBarricade; only the no-aim fallback is resolved here.
            if (world.HasAim[id] == 1)
            {
                Barricades.TryPlaceBarricade(world, id, world.Inputs[id].AimX, world.Inputs[id].AimZ);
            }
            else
            {
                AbilityTarget(world, id, world.Config.Abilities.MinerBarricade.PlaceRange, out TargetX, out TargetZ);
                Barricades.TryPlaceBarricade(world, id, TargetX, TargetZ);
            }
        }

        /// <summary>Casts each living actor's class ability on InputFrame.Ability when its cooldown is spent.</summary>
        public static void AbilitiesSystem(World w, double dt)
        {
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.StandUp > 0 || !w.Inputs[i].Ability || a.AbilityCd > 0) continue;
                if (a.HitStunTicks > 0 && Phys.On(w, true)) continue; // DG-70: no cast while staggered
                if (a.Cls == ClassId.Warrior) CastWarriorShield(w, i);
                else if (a.Cls == ClassId.Farmer) CastFarmerRoots(w, i);
                else if (a.Cls == ClassId.Miner) CastMinerBarricade(w, i);
                else if (a.Cls == ClassId.Demolitions) Demolitions.CastMine(w, i); // DG-22 (CM-38): land mine on Space
                else if (a.Cls == ClassId.Builder) BuiltPieces.CastBuild(w, i); // DG-24 (U-060): a ladder or a rope bridge on Space
                else if (a.Cls == ClassId.Healer) Healer.CastPulse(w, i); // DG-40 (U-061): the healing pulse on Space
                // DG-69: an accepted cast starts its action on this tick (the view's per-kind clip); casts have no wind-up.
                if (a.AbilityCd > 0 && Phys.On(w, true))
                {
                    int kind = a.Cls == ClassId.Warrior ? Combat.KindShield : a.Cls == ClassId.Farmer ? Combat.KindRoots
                        : a.Cls == ClassId.Miner ? Combat.KindBarricade : a.Cls == ClassId.Demolitions ? Combat.KindMine
                        : a.Cls == ClassId.Builder ? Combat.KindBuild : Combat.KindPulse;
                    double fx = JsMath.Cos(a.Facing);
                    double fz = JsMath.Sin(a.Facing);
                    World.PushEvent(w, SimEventType.AttackStart, i, kind, w.X[i], w.Z[i], a.Facing, null, DamageCause.None, 0, fx, fz, 0, 0, 0, 0);
                }
            }
            // CM-30: miner dig / place (InputFrame.Terraform, its own key, so Space stays the barricade). DG-09: and the mode switch.
            Terraform.TerraformSystem(w);
            // DG-22 (CM-38): a Demolitions Expert's TNT throw on the same key (DigPlace: F, Y on a controller), its own cooldown.
            Demolitions.DemolitionsInputSystem(w);
            // DG-24: any class lifts / puts down a piece of its team on the Interact edge (E: "Interact / carry").
            BuiltPieces.CarryInputSystem(w);
            // DG-40: a Healer's purify at the egg on the DigPlace key (F, Y on a controller), its own cooldown.
            Healer.HealerInputSystem(w);
        }

        /// <summary>
        /// Miner pick vs breakable walls (called by Combat for a miner's swing): every BreakableIntact or
        /// BreakableCracked cell within reach and +/-45 degrees of facing loses one hit, applied with MapGrid.SetCell.
        /// </summary>
        public static void MinerWallBreak(World w, int minerId, double ax, double az, double fx, double fz)
        {
            MapDef map = w.Map;
            double reach = w.Config.Abilities.MinerWallBreak.Reach;
            double hps = w.Config.Abilities.MinerWallBreak.HitsPerState;
            double r2 = reach * reach;
            int x0 = Math.Max(0, JsMath.FloorToInt(ax - reach));
            int x1 = Math.Min(map.Width - 1, JsMath.FloorToInt(ax + reach));
            int z0 = Math.Max(0, JsMath.FloorToInt(az - reach));
            int z1 = Math.Min(map.Height - 1, JsMath.FloorToInt(az + reach));
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    int i = cz * map.Width + cx;
                    byte t = map.Cells[i];
                    if (t != Cell.BreakableIntact && t != Cell.BreakableCracked) continue;
                    double dx = cx + 0.5 - ax;
                    double dz = cz + 0.5 - az;
                    double d2 = dx * dx + dz * dz;
                    if (d2 > r2) continue;
                    if (d2 > 1e-12 && (dx * fx + dz * fz) / Math.Sqrt(d2) < WallBreakCos - AngleEps) continue;
                    // DG-83: a pick cannot break a wall behind another wall.
                    if (Phys.On(w, w.Config.Physics.WallsBlockHits) && !SegmentClear.Clear(map, ax, az, cx + 0.5, cz + 0.5, cx, cz)) continue;
                    // A breakable painted without hit points (e.g. by a test or a gate helper) gets the ones its state implies.
                    double hp = map.WallHp[i];
                    if (hp == 0) hp = t == Cell.BreakableIntact ? 2 * hps : hps;
                    hp--;
                    map.WallHp[i] = unchecked((byte)(int)hp); // Uint8Array store
                    byte next = hp > hps ? Cell.BreakableIntact : hp > 0 ? Cell.BreakableCracked : Cell.BreakableDestroyed;
                    if (next != t) MapGrid.SetCell(w, cx, cz, next);
                    World.PushEvent(w, SimEventType.WallBreak, minerId, i, cx + 0.5, cz + 0.5, next);
                }
            }
        }
    }
}

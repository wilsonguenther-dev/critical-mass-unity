using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// DG-70 (S1, OD-3, not in the browser): the deterministic impulse layer. A hit gives its victim a knock velocity
    /// (Actor.KVx / KVz) sized so the body travels the knock distance / ClassConfig.Mass in total (a Warrior's raised shield
    /// takes ShieldFrontKnockShare from the front), decaying KnockDecayPer30Hz per 1/30 s and moved through
    /// Collide.MoveActor, so walls stop it with no tunnelling. A hit at or over the victim's Poise (or any Kaiju hit, slam
    /// or blast) staggers it for HitStunTicks: input ignored, no attack, ability or jump, a wind-up cancelled
    /// (AttackCancel). Airborne bodies keep their knock until they land. Events: Knock and HitStun (world-force bus, C1).
    /// </summary>
    public static class Knock
    {
        /// <summary>Hit tier, PR #30's table: 2 critical (CriticalHitDamage+ or a finishing blow), 1 heavy (HeavyHitDamage+), 0 light.</summary>
        public static int Tier(GameConfig c, double dealt, bool finishing)
        {
            if (finishing || dealt >= c.Physics.CriticalHitDamage) return 2;
            return dealt >= c.Physics.HeavyHitDamage ? 1 : 0;
        }

        /// <summary>Knock velocity kept per tick at this SimHz.</summary>
        public static double DecayPerTick(GameConfig c) => Math.Pow(c.Physics.KnockDecayPer30Hz, Durations.ReferenceHz / c.SimHz);

        /// <summary>The distance target actually travels for a knock of dist (u, mass 1) from (srcX, srcZ): / mass, x shield share from the front.</summary>
        public static double Effective(World w, int target, double dist, double srcX, double srcZ)
        {
            Actor t = w.Actors[target];
            double d = dist / Math.Max(0.1, w.Config.Class(t.Cls).Mass);
            if (t.Cls == ClassId.Warrior && t.AbilityActive > 0 && Damage.IsFrontal(w, target, srcX, srcZ)) d *= w.Config.Physics.ShieldFrontKnockShare;
            return d;
        }

        /// <summary>Unit direction from (fromX, fromZ) to actor id; falls back to (fbX, fbZ) when they coincide.</summary>
        public static void Dir(World w, int id, double fromX, double fromZ, double fbX, double fbZ, out double dx, out double dz)
        {
            dx = w.X[id] - fromX;
            dz = w.Z[id] - fromZ;
            double l = Math.Sqrt(dx * dx + dz * dz);
            if (l > 1e-6)
            {
                dx /= l;
                dz /= l;
                return;
            }
            dx = fbX;
            dz = fbZ;
        }

        /// <summary>
        /// Give living actor target a knock of travelled distance eff along (dirX, dirZ) and stunTicks of stagger (the longer
        /// stun wins). Pushes Knock (and HitStun / AttackCancel) events. No-op while getting up.
        /// </summary>
        public static void Push(World w, int target, int source, double dirX, double dirZ, double eff, int stunTicks, int tier, DamageCause cause)
        {
            Actor t = w.Actors[target];
            if (!t.Alive || t.StandUp > 0) return;
            double dt = 1.0 / w.Config.SimHz;
            if (eff > 0)
            {
                double v0 = eff * (1 - DecayPerTick(w.Config)) / dt;
                t.KVx = JsMath.F32(t.KVx + dirX * v0);
                t.KVz = JsMath.F32(t.KVz + dirZ * v0);
                World.PushEvent(w, SimEventType.Knock, target, source, w.X[target], w.Z[target], 0, null, cause, 0, dirX, dirZ, eff, tier, 0, 0);
            }
            if (stunTicks > 0 && stunTicks > t.HitStunTicks)
            {
                t.HitStunTicks = stunTicks;
                t.HitStunLen = stunTicks;
                World.PushEvent(w, SimEventType.HitStun, target, source, w.X[target], w.Z[target], stunTicks, null, cause, 0, dirX, dirZ, 0, tier, 0, 0);
                if (t.SwingPending)
                {
                    t.SwingPending = false;
                    World.PushEvent(w, SimEventType.AttackCancel, target, source, w.X[target], w.Z[target], 0);
                }
            }
        }

        /// <summary>One movement step of actor i's knock velocity (grounded, standing): move through the grid, then decay.</summary>
        public static void Step(World w, int i, double dt)
        {
            Actor a = w.Actors[i];
            if (a.KVx == 0 && a.KVz == 0) return;
            Collide.MoveActor(w, i, a.KVx * dt, a.KVz * dt);
            double k = DecayPerTick(w.Config);
            double vx = a.KVx * k;
            double vz = a.KVz * k;
            if (vx * vx + vz * vz < w.Config.Physics.KnockStopSpeed * w.Config.Physics.KnockStopSpeed)
            {
                vx = 0;
                vz = 0;
            }
            a.KVx = JsMath.F32(vx);
            a.KVz = JsMath.F32(vz);
        }

        /// <summary>No knock, no stun (death, respawn, reset).</summary>
        public static void Clear(Actor a)
        {
            a.KVx = 0;
            a.KVz = 0;
            a.HitStunTicks = 0;
            a.HitStunLen = 0;
        }
    }
}

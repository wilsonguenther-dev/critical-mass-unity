using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Port of src/sim/damage.ts (09e0904): the one entry point for damaging an actor (CM-06).</summary>
    public static class Damage
    {
        /// <summary>CM-26: source id of a Kaiju MELEE hit (the slam keeps -1); kept in lastAttacker and the hit / death event.</summary>
        public const int KaijuSource = -2;

        /// <summary>
        /// DG-16: source id the Kaiju slam passes (the view's CombatLog.KaijuSlam). The hit / death event B and LastAttacker
        /// still read -1 for it (ported tests pin that); the slam is told apart by DamageCause.KaijuSlam.
        /// </summary>
        public const int KaijuSlamSource = -3;

        /// <summary>DG-16: the cause a source id implies: an actor = Melee, KaijuSource = KaijuMelee, KaijuSlamSource = KaijuSlam, else None.</summary>
        public static DamageCause CauseOf(World w, int sourceId)
        {
            if (sourceId >= 0 && sourceId < w.ActorCount) return DamageCause.Melee;
            if (sourceId == KaijuSource) return DamageCause.KaijuMelee;
            if (sourceId == KaijuSlamSource) return DamageCause.KaijuSlam;
            return DamageCause.None;
        }

        /// <summary>Half-angle of the shield's frontal cone.</summary>
        public const double ShieldHalfArcDeg = 60;

        /// <summary>Slack on the cone test so a source exactly on the 60 degree edge counts as frontal despite float rounding.</summary>
        private const double ConeEps = 1e-9;

        /// <summary>True when (srcX, srcZ) lies inside the +/-60 degree cone around the target's facing. A source on top counts as frontal.</summary>
        public static bool IsFrontal(World w, int targetId, double srcX, double srcZ)
        {
            double shieldCos = JsMath.Cos(ShieldHalfArcDeg * Math.PI / 180);
            double dx = srcX - w.X[targetId];
            double dz = srcZ - w.Z[targetId];
            double d2 = dx * dx + dz * dz;
            if (!(d2 > 1e-12)) return true;
            double f = w.Actors[targetId].Facing;
            double dot = (dx * JsMath.Cos(f) + dz * JsMath.Sin(f)) / Math.Sqrt(d2);
            return dot >= shieldCos - ConeEps;
        }

        /// <summary>Damage actor targetId by amount from sourceId standing at (srcX, srcZ); returns the damage dealt (0 when ignored).</summary>
        public static double ApplyDamage(World w, int targetId, double amount, int sourceId, double srcX, double srcZ)
        {
            return ApplyDamage(w, targetId, amount, sourceId, srcX, srcZ, CauseOf(w, sourceId));
        }

        /// <summary>DG-16: ApplyDamage with an explicit cause (written to the Hit / Death event Cause and Actor.LastCause), for later sources such as DG-22 blasts.</summary>
        public static double ApplyDamage(World w, int targetId, double amount, int sourceId, double srcX, double srcZ, DamageCause cause)
        {
            return ApplyHit(w, targetId, amount, sourceId, srcX, srcZ, cause, 0, 0, 0, 0);
        }

        /// <summary>
        /// DG-70: a melee hit by actor src standing at (srcX, srcZ) with the swing facing (fbX, fbZ): damage, plus (Knockback on)
        /// a push away from the attacker of MeleeKnockPerDamage x amount and a stagger when it reaches the victim's Poise.
        /// </summary>
        public static double ApplyMelee(World w, int targetId, double amount, int sourceId, double srcX, double srcZ)
        {
            if (!Phys.On(w, w.Config.Physics.Knockback)) return ApplyDamage(w, targetId, amount, sourceId, srcX, srcZ);
            Actor s = sourceId >= 0 && sourceId < w.ActorCount ? w.Actors[sourceId] : null;
            double fbX = s != null ? JsMath.Cos(s.SwingFacing) : 1;
            double fbZ = s != null ? JsMath.Sin(s.SwingFacing) : 0;
            Knock.Dir(w, targetId, srcX, srcZ, fbX, fbZ, out double dx, out double dz);
            int stun = amount >= w.Config.Class(w.Actors[targetId].Cls).Poise ? w.Dur.HeavyStun : 0;
            return ApplyHit(w, targetId, amount, sourceId, srcX, srcZ, CauseOf(w, sourceId), dx, dz, amount * w.Config.Physics.MeleeKnockPerDamage, stun);
        }

        /// <summary>fix/kaiju-alive-1: true when a hit of this cause knocks and staggers: Knockback on, or a Kaiju hit with
        /// PhysicsConfig.KaijuKnockback on.</summary>
        /// <summary>fix/kaiju-alive-1: the share of the Kaiju's designed throw it applies (PhysicsConfig.KaijuKnockScale while
        /// the fighters' Knockback is off).</summary>
        public static double KaijuThrow(World w) => Phys.On(w, w.Config.Physics.Knockback) ? 1 : w.Config.Physics.KaijuKnockScale;

        public static bool Knocks(World w, DamageCause cause) =>
            Phys.On(w, w.Config.Physics.Knockback) ||
            cause == DamageCause.Melee && CombatFeel.On(w) ||   // b15 combat feel: melee hits knock, stagger and carry their tier
            (cause == DamageCause.KaijuMelee || cause == DamageCause.KaijuSlam) && Phys.On(w, w.Config.Physics.KaijuKnockback) ||
            cause == DamageCause.Explosion && PhysicsFeel.BlastOn(w);   // b16 [2]: blasts shove and stagger under their own switch

        /// <summary>
        /// DG-70: the one entry point (ApplyDamage(..., cause) calls it with no knock): damage, then, when the victim lives and
        /// Knockback is on, a knock of knock u (mass 1) along (dirX, dirZ) and stunTicks of stagger (Knock.Push). The Hit event
        /// carries the direction, the travelled knock distance (Impulse) and the tier for the world-force bus (C1).
        /// </summary>
        public static double ApplyHit(World w, int targetId, double amount, int sourceId, double srcX, double srcZ, DamageCause cause,
            double dirX, double dirZ, double knock, int stunTicks)
        {
            if (!(targetId >= 0 && targetId < w.ActorCount)) return 0;
            if (!(amount > 0) || !double.IsFinite(amount)) return 0;
            if (w.Match.Phase == MatchPhase.Ended) return 0;
            Actor t = w.Actors[targetId];
            if (!t.Alive) return 0;
            bool fromActor = sourceId >= 0 && sourceId < w.ActorCount;
            // lane S2: a swing, blast or mine that reaches an enemy body and deals nothing says why (HitBlocked, view only).
            bool report = (cause == DamageCause.Melee || cause == DamageCause.Explosion) && fromActor && w.Actors[sourceId].Team != t.Team && PlayerSeat.On(w);
            if (t.SpawnProtect > 0)
            {
                if (report) Blocked(w, targetId, sourceId, PlayerReasons.BlockedSpawnProtect, 0);
                return 0;
            }
            if (fromActor && w.Actors[sourceId].Team == t.Team) return 0;
            if (B13Hooks.Immune(w, targetId, cause))   // b13-hook
            {
                if (report) Blocked(w, targetId, sourceId, Dodge.Immune(w, targetId, cause) ? PlayerReasons.BlockedDodge : PlayerReasons.BlockedImmune, 0);
                return 0;
            }
            // The id written to lastAttacker and the events: the actor, KaijuSource, else -1.
            int credit = fromActor || sourceId == KaijuSource ? sourceId : -1;

            amount *= B13Hooks.DamageScale(w, sourceId, targetId, cause);   // b13-hook
            double dmg = amount;
            double shieldSaved = 0;
            if (t.Cls == ClassId.Warrior && t.AbilityActive > 0 && IsFrontal(w, targetId, srcX, srcZ))
            {
                dmg *= 1 - w.Config.Abilities.WarriorShield.DamageReduction;
                shieldSaved = amount - dmg;
            }

            // U-066 Ward: soaks the hit (an explosion goes through and breaks it). A fully soaked hit still marks the attacker
            // and still interrupts a delivery wind-up, but costs no HP and raises no Hit event (WardAbsorb instead).
            dmg = Pickups.Absorb(w, targetId, dmg, cause, credit);
            if (!(dmg > 0))
            {
                if (report) Blocked(w, targetId, sourceId, PlayerReasons.BlockedWard, 0);   // lane S2 (WardAbsorb is kept)
                t.GatherProgress = 0;
                t.LastAttacker = credit;
                t.LastCause = cause;
                Delivery.InterruptDelivery(w, targetId, credit);
                return 0;
            }

            t.Hp -= dmg;
            t.GatherProgress = 0;
            t.LastAttacker = credit;
            t.LastCause = cause;
            double tx = w.X[targetId];
            double tz = w.Z[targetId];
            bool physics = Knocks(w, cause);
            double eff = physics && (knock > 0) ? Knock.Effective(w, targetId, knock, srcX, srcZ) : 0;
            int tier = Knock.Tier(w.Config, dmg, t.Hp <= 0);
            if (physics) World.PushEvent(w, SimEventType.Hit, targetId, credit, tx, tz, dmg, null, cause, 0, dirX, dirZ, eff, tier, 0, 0);
            else World.PushEvent(w, SimEventType.Hit, targetId, credit, tx, tz, dmg, null, cause);
            if (report)
            {
                // lane S2: the hit landed but something cut it: the shield (Radius = damage saved) or the Farmer's staff inside
                // her close range ("TOO CLOSE", Radius = damage lost). Informational, after the Hit.
                if (shieldSaved > 0) Blocked(w, targetId, sourceId, PlayerReasons.BlockedShield, shieldSaved);
                double lost = GlancingLost(w, targetId, sourceId, srcX, srcZ, cause, dmg);
                if (lost > 0) Blocked(w, targetId, sourceId, PlayerReasons.BlockedGlancing, lost);
            }
            // CM-64: a hit during a delivery wind-up at the shrine loses that delivery (DeliverIntercepted).
            Delivery.InterruptDelivery(w, targetId, credit);

            if (t.Hp <= 0)
            {
                t.Hp = 0;
                t.Alive = false;
                t.RespawnIn = Comeback.RespawnDelay(w, t.Team);   // b15-hook: the respawn curve and QUICK RETURN (exactly DelaySeconds with both b15 rules off)
                t.Deaths++;
                if (fromActor) w.Actors[sourceId].Kills++;
                // Cargo is lost (no dropped orb in the demo). CM-28: what was lost is recorded for the respawn card and the toast.
                t.LostCargo.Kind = t.CarryAmount > 0 ? t.Carry : CarryKind.None;
                t.LostCargo.Amount = t.CarryAmount > 0 ? t.CarryAmount : 0;
                t.Carry = CarryKind.None;
                t.CarryAmount = 0;
                t.GatherProgress = 0;
                t.GatherNodeId = -1;
                // A corpse has no shield, slow or swing in flight.
                t.AbilityActive = 0;
                t.SlowTimer = 0;
                t.Moving = false;
                if (Phys.On(w, true)) World.ClearSwing(t); // DG-69: a corpse has no swing in flight
                if (physics) Knock.Clear(t);
                if (physics) World.PushEvent(w, SimEventType.Death, targetId, credit, tx, tz, dmg, null, cause, 0, dirX, dirZ, eff, tier, 0, 0);
                else World.PushEvent(w, SimEventType.Death, targetId, credit, tx, tz, dmg, null, cause);
                return dmg;
            }
            // px S3: TNT and mine blasts (DamageCause.Explosion) open the "wounded" window too, as a landed melee hit does in
            // CombatFeel.ApplyStrike (heal and fountain cut, low-HP slow); WoundedSeconds 0 = off. Full rules only.
            if (cause == DamageCause.Explosion && dmg > 0 && fromActor && w.Actors[sourceId].Team != t.Team && CombatFeel.On(w))
            {
                double ws = CombatFeel.Cfg(w).WoundedSeconds;
                if (ws > 0) w.Depth.Feel.WoundedUntil[targetId] = w.Tick + CombatFeel.Ticks(w, ws);
            }
            if (physics && (eff > 0 || stunTicks > 0)) Knock.Push(w, targetId, sourceId, dirX, dirZ, eff, stunTicks, tier, cause);
            return dmg;
        }

        /// <summary>Lane S2: HitBlocked: A = the target, B = the attacker, (X, Z) = the target, Value = PlayerReasons.Blocked*,
        /// Radius = the damage the cut saved (BlockedShield) or lost (BlockedGlancing), else 0.</summary>
        static void Blocked(World w, int target, int source, int why, double amount)
        {
            World.PushEvent(w, SimEventType.HitBlocked, target, source, w.X[target], w.Z[target], why, null, DamageCause.None, amount);
        }

        /// <summary>Lane S2: the damage a melee hit of dealt lost to the Farmer's close-range rule (CombatFeel.DamageMul: share
        /// FarmerCloseShare inside FarmerCloseRange of the swing's origin), else 0.</summary>
        static double GlancingLost(World w, int target, int source, double srcX, double srcZ, DamageCause cause, double dealt)
        {
            if (cause != DamageCause.Melee || w.Actors[source].Cls != ClassId.Farmer) return 0;
            CombatFeelConfig f = CombatFeel.Cfg(w);
            if (!(f.FarmerCloseShare > 0) || !(f.FarmerCloseShare < 1)) return 0;
            double dx = w.X[target] - srcX, dz = w.Z[target] - srcZ;
            if (dx * dx + dz * dz >= f.FarmerCloseRange * f.FarmerCloseRange) return 0;
            return dealt * (1 - f.FarmerCloseShare) / f.FarmerCloseShare;
        }
    }
}

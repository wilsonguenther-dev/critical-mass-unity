using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>Port of src/sim/combat.ts (09e0904): timers, spawn protection, melee cleave (CM-06).</summary>
    public static class Combat
    {
        /// <summary>Timers that land within this of 0 snap to 0, so a 0.7 s cooldown at 30 Hz is exactly 21 steps despite float drift.</summary>
        public const double TimerEps = 1e-6;

        /// <summary>Module-scope scratch of QueryCircle candidates (browser Int32Array(ACTOR_CAPACITY)).</summary>
        private static int[] Candidates = new int[0];

        /// <summary>v - dt, floored at 0 (and snapped to 0 within TimerEps).</summary>
        public static double TickDown(double v, double dt)
        {
            double r = v - dt;
            return r > TimerEps ? r : 0;
        }

        public static void CombatSystem(World w, double dt)
        {
            GameConfig cfg = w.Config;
            int n = w.ActorCount;
            PadDef[] pads = w.Map.Pads;
            double maxTargetRadius = Math.Max(
                cfg.Class(ClassId.Warrior).Radius,
                Math.Max(cfg.Class(ClassId.Farmer).Radius, cfg.Class(ClassId.Miner).Radius));
            int capacity = World.ActorCapacity(cfg);
            if (Candidates.Length < capacity) Candidates = new int[capacity];
            int[] candidates = Candidates;

            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                a.AttackCd = TickDown(a.AttackCd, dt);
                a.AbilityCd = TickDown(a.AbilityCd, dt);
                a.AbilityActive = TickDown(a.AbilityActive, dt);
                a.SlowTimer = TickDown(a.SlowTimer, dt);
                a.SpawnProtect = TickDown(a.SpawnProtect, dt);
                // Protection cannot be carried out of base: it ends as soon as the actor is beyond pad.r + 1 of its pad centre.
                if (a.SpawnProtect > 0 && a.Alive)
                {
                    PadDef pad = pads[(int)a.Team];
                    double dx = w.X[i] - pad.X;
                    double dz = w.Z[i] - pad.Z;
                    double lim = pad.R + 1;
                    if (dx * dx + dz * dz > lim * lim) a.SpawnProtect = 0;
                }
                a.AnimAction += dt;
            }

            // DG-67 (S4): with FairTrades every swing of this tick is decided first (hits queued in w.Hits, no actor written),
            // then all are applied in queue order, so a lower id never cancels a higher id's swing by killing it first.
            bool fair = Phys.On(w, cfg.Physics.FairTrades);
            w.Hits.Count = 0;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.StandUp > 0 || !w.Inputs[i].Attack || a.AttackCd > 0) continue;
                if (a.Cls == ClassId.Demolitions) continue; // DG-22 (CM-38): no regular attack (Travis 2026-11-06)
                if (a.Cls == ClassId.Healer)
                {
                    Healer.TryMend(w, i); // DG-40 (U-061): no weapon; the attack press mends a hurt ally
                    continue;
                }
                ClassConfig cc = cfg.Class(a.Cls);
                a.AttackCd = cc.AttackCooldown;
                a.AnimAction = 0;
                a.SpawnProtect = 0; // attacking drops protection

                double ax = w.X[i];
                double az = w.Z[i];
                double fx = JsMath.Cos(a.Facing);
                double fz = JsMath.Sin(a.Facing);
                double cosHalf = JsMath.Cos(cc.AttackArcDeg / 2 * Math.PI / 180);
                // DG-16 (view request): the swing starts: attacker, reach point in front of it, facing.
                World.PushEvent(w, SimEventType.AttackStart, i, -1, ax + fx * cc.AttackRange, az + fz * cc.AttackRange, a.Facing);
                int count = SpatialHash.QueryCircle(w.Hash, ax, az, cc.AttackRange + maxTargetRadius, candidates);
                for (int k = 0; k < count; k++)
                {
                    int j = candidates[k];
                    if (j == i) continue;
                    Actor t = w.Actors[j];
                    if (!t.Alive || t.Team == a.Team) continue;
                    // DG-16 passive window: a bot's swing never hurts the human slot yet.
                    if (!a.IsHuman && t.IsHuman && w.Match.Time < cfg.Bots.PassiveSeconds) continue;
                    double dx = w.X[j] - ax;
                    double dz = w.Z[j] - az;
                    double reach = cc.AttackRange + cfg.Class(t.Cls).Radius;
                    double d2 = dx * dx + dz * dz;
                    if (d2 > reach * reach) continue;
                    if (d2 > 1e-12)
                    {
                        double dot = (dx * fx + dz * fz) / Math.Sqrt(d2);
                        if (dot < cosHalf - 1e-9) continue;
                    }
                    if (fair) w.Hits.Add(j, i, cc.AttackDamage, ax, az);
                    else Damage.ApplyDamage(w, j, cc.AttackDamage, i, ax, az);
                }
                // Structures (CM-05): the same swing chips enemy barricades in its arc; a Miner's pick also breaks breakables.
                Barricades.DamageBarricadesInArc(w, i, ax, az, fx, fz, cc.AttackRange, cosHalf, cc.AttackDamage);
                if (a.Cls == ClassId.Miner) Abilities.MinerWallBreak(w, i, ax, az, fx, fz);
                // CM-31: the same swing chips knock-down decorations in its arc (any class, bots included).
                Topple.ToppleInArc(w, i, ax, az, fx, fz, cc.AttackRange, cosHalf);

                // CM-08: the same cleave hits the hatched Kaiju within attackRange + kaiju.radius, inside the arc.
                Kaiju kj = w.Kaiju;
                if (kj.State != KaijuState.Egg && kj.KoIn <= 0)
                {
                    double dx = kj.X - ax;
                    double dz = kj.Z - az;
                    double reach = cc.AttackRange + cfg.Kaiju.Radius;
                    double d2 = dx * dx + dz * dz;
                    if (d2 <= reach * reach && (d2 <= 1e-12 || (dx * fx + dz * fz) / Math.Sqrt(d2) >= cosHalf - 1e-9))
                    {
                        Kaiju.DamageKaiju(w, i, cc.AttackDamage);
                    }
                }
            }
            if (fair) ApplyPendingHits(w);
            // DG-22 (CM-38): mines trigger and due TNT blows on this step's positions, before the meat drop so a blast kill leaves meat.
            Demolitions.DemolitionsBlastSystem(w);
            // CM-35: the last thing combat does: every body killed this step (or since last step's combat) drops its meat once.
            DropCorpseMeat(w);
        }

        /// <summary>DG-67: phase 2, every queued melee hit of the tick in queue order (attacker id, then candidate order).</summary>
        public static void ApplyPendingHits(World w)
        {
            PendingHits h = w.Hits;
            for (int k = 0; k < h.Count; k++) Damage.ApplyDamage(w, h.Target[k], h.Amount[k], h.Source[k], h.SrcX[k], h.SrcZ[k]);
            h.Count = 0;
        }

        /// <summary>
        /// CM-35: meat biomass from killed bodies. Every dead actor that has not dropped yet leaves one loose meat orb of
        /// Resources.MeatUnitsPerKill at its corpse (X / Z hold the death position until respawn), lasting
        /// Resources.MeatOrbLifetimeTicks, only while the Kaiju is an egg. MeatDropped makes it once per death whatever killed
        /// the actor; it clears while alive. Allocation-free (a slot of the fixed World.Orbs pool).
        /// </summary>
        public static void DropCorpseMeat(World w)
        {
            int n = w.ActorCount;
            ResourcesConfig res = w.Config.Resources;
            bool egg = w.Kaiju.State == KaijuState.Egg && !w.Match.Hatched;
            byte[] dropped = w.MeatDropped;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (a.Alive)
                {
                    dropped[id] = 0;
                    continue;
                }
                if (dropped[id] != 0) continue;
                dropped[id] = 1;
                if (!egg || w.Match.Phase == MatchPhase.Ended || res.MeatUnitsPerKill <= 0) continue;
                World.SpawnLooseOrb(w, w.X[id], w.Z[id], (int)res.MeatUnitsPerKill, Durations.T30(w.Config, res.MeatOrbLifetimeTicks), id);
            }
        }
    }
}

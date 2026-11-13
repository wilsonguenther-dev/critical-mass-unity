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

            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.StandUp > 0 || !w.Inputs[i].Attack || a.AttackCd > 0) continue;
                // ITEM CM-38: added by its owner (Demolitions has no regular attack)
                ClassConfig cc = cfg.Class(a.Cls);
                a.AttackCd = cc.AttackCooldown;
                a.AnimAction = 0;
                a.SpawnProtect = 0; // attacking drops protection

                double ax = w.X[i];
                double az = w.Z[i];
                double fx = JsMath.Cos(a.Facing);
                double fz = JsMath.Sin(a.Facing);
                double cosHalf = JsMath.Cos(cc.AttackArcDeg / 2 * Math.PI / 180);
                int count = SpatialHash.QueryCircle(w.Hash, ax, az, cc.AttackRange + maxTargetRadius, candidates);
                for (int k = 0; k < count; k++)
                {
                    int j = candidates[k];
                    if (j == i) continue;
                    Actor t = w.Actors[j];
                    if (!t.Alive || t.Team == a.Team) continue;
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
                    Damage.ApplyDamage(w, j, cc.AttackDamage, i, ax, az);
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
            // ITEM CM-38: added by its owner (Demolitions mines and TNT blasts: demolitionsBlastSystem)
            // ITEM CM-35: added by its owner (corpse meat drop)
        }

        // ITEM CM-35: added by its owner (corpse meat drop: dropCorpseMeat)
    }
}

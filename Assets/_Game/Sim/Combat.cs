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
            bool lane = Phys.On(w, true);
            w.Hits.Count = 0;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                // DG-69: a wound-up swing lands on its contact tick, along the facing locked at the press.
                if (a.SwingPending && w.Tick >= a.SwingTick + a.SwingWindup)
                {
                    a.SwingPending = false;
                    if (a.Alive && a.StandUp <= 0) ResolveSwing(w, i, a.SwingFacing, fair, lane, maxTargetRadius, candidates);
                }
                if (!a.Alive || a.StandUp > 0 || !w.Inputs[i].Attack || a.AttackCd > 0 || a.SwingPending) continue;
                if (lane && a.HitStunTicks > 0) continue; // DG-70: a staggered actor cannot start a swing
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
                int windup = w.Dur.AttackWindup[(int)a.Cls];
                a.SwingTick = w.Tick;
                a.SwingWindup = windup;
                a.SwingRecovery = w.Dur.Recovery[(int)a.Cls];
                a.SwingFacing = a.Facing;
                double fx = JsMath.Cos(a.Facing);
                double fz = JsMath.Sin(a.Facing);
                // DG-16 (view request), DG-69: the swing starts on the press tick: reach point, locked facing, wind-up length.
                if (lane) World.PushEvent(w, SimEventType.AttackStart, i, KindAttack, w.X[i] + fx * cc.AttackRange, w.Z[i] + fz * cc.AttackRange, a.Facing, null, DamageCause.None, 0, fx, fz, 0, 0, windup, a.SwingRecovery);
                else World.PushEvent(w, SimEventType.AttackStart, i, -1, w.X[i] + fx * cc.AttackRange, w.Z[i] + fz * cc.AttackRange, a.Facing);
                if (windup > 0) a.SwingPending = true;
                else ResolveSwing(w, i, a.Facing, fair, lane, maxTargetRadius, candidates);
            }
            if (fair) ApplyPendingHits(w);
            // DG-22 (CM-38): mines trigger and due TNT blows on this step's positions, before the meat drop so a blast kill leaves meat.
            Demolitions.DemolitionsBlastSystem(w);
            // CM-35: the last thing combat does: every body killed this step (or since last step's combat) drops its meat once.
            DropCorpseMeat(w);
        }

        /// <summary>Melee kind on AttackStart B (the PR #33 value); abilities use the Kind* codes below.</summary>
        public const int KindAttack = -1;
        public const int KindShield = 1;
        public const int KindRoots = 2;
        public const int KindBarricade = 3;
        public const int KindMine = 4;
        public const int KindBuild = 5;
        public const int KindPulse = 6;
        public const int KindTnt = 7;

        /// <summary>
        /// DG-69 / DG-70 (contract C2): the sim's own answer to "would an attack press held for the NEXT step be accepted", read
        /// between steps: alive, standing, cooldown spent after this step's countdown, no swing still winding up, not staggered
        /// after this step's countdown, a class with an attack or mend. PR #25's InputReadiness mirrors exactly this.
        /// </summary>
        public static bool AttackReady(World w, int id)
        {
            Actor a = w.Actors[id];
            if (!a.Alive || a.StandUp > 0 || a.Cls == ClassId.Demolitions) return false;
            if (TickDown(a.AttackCd, SimStep.Dt(w.Config)) > 0) return false;
            if (a.SwingPending && w.Tick + 1 < a.SwingTick + a.SwingWindup) return false;
            if (Phys.On(w, true) && a.HitStunTicks > 1) return false;
            return true;
        }

        /// <summary>
        /// One melee swing of actor i along facing, from where it stands now: enemies in reach and arc (queued when fair),
        /// barricades, the Miner's wall break, decorations and the hatched Kaiju. Browser order (CM-06, CM-05, CM-31, CM-08).
        /// </summary>
        private static void ResolveSwing(World w, int i, double facing, bool fair, bool lane, double maxTargetRadius, int[] candidates)
        {
            GameConfig cfg = w.Config;
            Actor a = w.Actors[i];
            ClassConfig cc = cfg.Class(a.Cls);
            double ax = w.X[i];
            double az = w.Z[i];
            double fx = JsMath.Cos(facing);
            double fz = JsMath.Sin(facing);
            double cosHalf = JsMath.Cos(cc.AttackArcDeg / 2 * Math.PI / 180);
            double halfRad = cc.AttackArcDeg / 2 * Math.PI / 180;
            bool body = Phys.On(w, cfg.Physics.ArcCoversBody); // DG-100 (S13): the arc reaches the target's body, not just its centre
            int cap = Phys.On(w, true) ? cfg.Physics.CleaveMaxTargets : 0; // OD-10: 0 = no cap
            int hits = 0;
            bool walls = Phys.On(w, cfg.Physics.WallsBlockHits); // DG-83: no hits through walls or barricades
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
                    if (dot < (body ? WidenedCos(halfRad, cfg.Class(t.Cls).Radius, Math.Sqrt(d2)) : cosHalf) - 1e-9) continue;
                }
                if (walls && !SegmentClear.Clear(w.Map, ax, az, w.X[j], w.Z[j])) continue;
                if (cap > 0 && hits >= cap) break;
                hits++;
                if (fair) w.Hits.Add(j, i, cc.AttackDamage, ax, az);
                else Damage.ApplyMelee(w, j, cc.AttackDamage, i, ax, az);
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
                double kcos = body && d2 > 1e-12 ? WidenedCos(halfRad, cfg.Kaiju.Radius, Math.Sqrt(d2)) : cosHalf;
                if (d2 <= reach * reach && (d2 <= 1e-12 || (dx * fx + dz * fz) / Math.Sqrt(d2) >= kcos - 1e-9)
                    && (!walls || SegmentClear.Clear(w.Map, ax, az, kj.X, kj.Z)))
                {
                    Kaiju.DamageKaiju(w, i, cc.AttackDamage);
                }
            }
            // DG-69: the contact tick, even on a whiff.
            if (lane) World.PushEvent(w, SimEventType.AttackContact, i, -1, ax + fx * cc.AttackRange, az + fz * cc.AttackRange, hits, null, DamageCause.None, 0, fx, fz, 0, 0, 0, 0);
        }

        /// <summary>DG-100 (S13): cosine of the arc half-angle widened by asin(min(1, r / d)) for a target of radius r at distance d.</summary>
        public static double WidenedCos(double halfRad, double r, double d)
        {
            double a = halfRad + Math.Asin(Math.Min(1, r / d));
            return a >= Math.PI ? -1 : JsMath.Cos(a);
        }

        /// <summary>DG-67: phase 2, every queued melee hit of the tick in queue order (attacker id, then candidate order).</summary>
        public static void ApplyPendingHits(World w)
        {
            PendingHits h = w.Hits;
            for (int k = 0; k < h.Count; k++) Damage.ApplyMelee(w, h.Target[k], h.Amount[k], h.Source[k], h.SrcX[k], h.SrcZ[k]);
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
                double ox = w.X[id];
                double oz = w.Z[id];
                double hx = 0;
                double hz = 0;
                bool pop = Phys.On(w, w.Config.Physics.MeatPop);
                if (pop)
                {
                    // DG-100 (S16): a deterministic hop (angle from id and tick) onto walkable ground, so orbs never stack.
                    double dist = w.Config.Physics.MeatPopDistance;
                    double ang = id * 2.39996 + w.Tick;
                    for (int q = 0; q < 4; q++, ang += Math.PI / 2)
                    {
                        double tx = ox + JsMath.Cos(ang) * dist;
                        double tz = oz + JsMath.Sin(ang) * dist;
                        if (!MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(tx), JsMath.FloorToInt(tz))) continue;
                        hx = JsMath.Cos(ang);
                        hz = JsMath.Sin(ang);
                        ox = JsMath.F32(tx);
                        oz = JsMath.F32(tz);
                        break;
                    }
                }
                int slot = World.SpawnLooseOrb(w, ox, oz, (int)res.MeatUnitsPerKill, Durations.T30(w.Config, res.MeatOrbLifetimeTicks), id);
                if (pop) World.PushEvent(w, SimEventType.MeatPop, id, slot, ox, oz, 0, null, DamageCause.None, w.Config.Physics.MeatPopDistance, hx, hz, 0, 0, 0, 0);
            }
        }
    }
}

// Class moves build (2026-11-22): LANE 2 (Defence) owns this file: T1-2 lunge, T1-3 dodge refund, T1-4a feint bait (sim side),
// T2-1 guard and parry, T2-7 the hit test (BlockedAir), T2-8 second wind. Every rule acts only while its own MovesConfig switch
// is on (MovesHooks.On(w, Cfg(w).<Switch>)); with World.Moves null (every switch off) nothing here is ever reached, so the
// goldens are the base branch's. Contract: cm-incidents/class-moves-audit-2026-11-22/LANE1-CONTRACT.md; numbers: MovesConfig.
// Deterministic: plain arithmetic in actor-id order, no RNG, no allocation per tick.
using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Lane 2's class-moves state (World.Moves.Defence): primitive arrays sized once in Init, cleared in Reset.</summary>
    public sealed class DefenceMovesState
    {
        // ---- T2-1 guard and parry
        /// <summary>1 while the guard is held up (raised; it blocks once GuardRaiseTicks have passed since GuardSince).</summary>
        public byte[] GuardOn = new byte[0];
        /// <summary>The tick the current guard went up (CombatFeel.NoTick = none).</summary>
        public int[] GuardSince = new int[0];
        /// <summary>A Guard press holds the guard (the Warrior: his attack) until this tick at least (the whiffed-parry hold).</summary>
        public int[] GuardHoldUntil = new int[0];
        /// <summary>After the guard drops: no attack or cast before this tick (GuardReleaseTicks).</summary>
        public int[] GuardReleaseUntil = new int[0];
        /// <summary>The guard meter left (damage it may still absorb before it breaks).</summary>
        public double[] GuardMeter = new double[0];
        /// <summary>The tick of the last hit the guard took (the refill waits GuardRefillDelaySeconds after it).</summary>
        public int[] GuardLastBlock = new int[0];
        /// <summary>The tick of the last VALID parry press edge (CombatFeel.NoTick = none or locked out).</summary>
        public int[] ParryPress = new int[0];
        /// <summary>No new parry attempt before this tick (ParryLockoutSeconds after an attempt; a parry that lands clears it).</summary>
        public int[] ParryLockUntil = new int[0];
        // ---- T1-2 lunge
        /// <summary>1 while the current dodge is a dodge-in (it was clipped short of LungeTarget).</summary>
        public byte[] LungeArmed = new byte[0];
        public int[] LungeTarget = new int[0];
        /// <summary>The first tick after the dodge-in's dash (its first recover tick).</summary>
        public int[] LungeDashEnd = new int[0];
        /// <summary>1 once Attack was seen during the dodge-in's dash (the press the lunge fires).</summary>
        public byte[] LungePressed = new byte[0];
        /// <summary>Integrator: the SwingTick of actor i's current LUNGE swing (LungeArmour), CombatFeel.NoTick = none.</summary>
        public int[] LungeSwingTick = new int[0];
        /// <summary>Integrator: the tick actor i took the knock of an enemy's melee LIGHT strike (ComboDodgeOutOfKnock).</summary>
        public int[] LightKnockTick = new int[0];
        // ---- T2-7 jump evade (lane 3 reads JumpRelockUntil for its jump lock; the landing recovery is LandRecoverUntil)
        /// <summary>1 when the current flight evaded a ground melee swing (BlockedAir).</summary>
        public byte[] FlightEvaded = new byte[0];
        /// <summary>After an evading jump lands: no dodge, attack or guard before this tick (JumpEvadeLandingTicks).</summary>
        public int[] LandRecoverUntil = new int[0];
        /// <summary>After an evading jump lands: no new jump before this tick (JumpEvadeRelockSeconds). Lane 3's MovementJumpLocked
        /// reads it (see the lane 2 report).</summary>
        public int[] JumpRelockUntil = new int[0];
        // ---- T2-8 second wind
        /// <summary>1 once this life's second wind was spent (cleared at death).</summary>
        public byte[] SecondWindUsed = new byte[0];
        // ---- T1-4a feint bait
        /// <summary>The tick of actor i's last feint, and 1 once that feint reported (or its after-window closed).</summary>
        public int[] FeintTick = new int[0];
        public byte[] FeintDone = new byte[0];
        // ---- counters (tests and balance prints)
        public int Parries, ParryWhiffs, ParryLocked, GuardBlocks, GuardBreaks, AirEvades, Lunges, LungeDashes, Refunds, SecondWinds, FeintBaits;
        public double GuardSaved;

        public void Init(World w)
        {
            int n = World.ActorCapacity(w.Config);
            GuardOn = new byte[n];
            GuardSince = new int[n];
            GuardHoldUntil = new int[n];
            GuardReleaseUntil = new int[n];
            GuardMeter = new double[n];
            GuardLastBlock = new int[n];
            ParryPress = new int[n];
            ParryLockUntil = new int[n];
            LungeArmed = new byte[n];
            LungeTarget = new int[n];
            LungeDashEnd = new int[n];
            LungePressed = new byte[n];
            LungeSwingTick = new int[n];
            LightKnockTick = new int[n];
            FlightEvaded = new byte[n];
            LandRecoverUntil = new int[n];
            JumpRelockUntil = new int[n];
            SecondWindUsed = new byte[n];
            FeintTick = new int[n];
            FeintDone = new byte[n];
        }

        public void Reset(World w)
        {
            for (int i = 0; i < GuardOn.Length; i++)
            {
                GuardOn[i] = 0;
                GuardSince[i] = CombatFeel.NoTick;
                GuardHoldUntil[i] = CombatFeel.NoTick;
                GuardReleaseUntil[i] = CombatFeel.NoTick;
                GuardMeter[i] = -1;   // -1 = full (filled from the class row on the first tick it is read)
                GuardLastBlock[i] = CombatFeel.NoTick;
                ParryPress[i] = CombatFeel.NoTick;
                ParryLockUntil[i] = CombatFeel.NoTick;
                LungeArmed[i] = 0;
                LungeTarget[i] = -1;
                LungeDashEnd[i] = CombatFeel.NoTick;
                LungePressed[i] = 0;
                LungeSwingTick[i] = CombatFeel.NoTick;
                LightKnockTick[i] = CombatFeel.NoTick;
                FlightEvaded[i] = 0;
                LandRecoverUntil[i] = CombatFeel.NoTick;
                JumpRelockUntil[i] = CombatFeel.NoTick;
                SecondWindUsed[i] = 0;
                FeintTick[i] = CombatFeel.NoTick;
                FeintDone[i] = 1;
            }
            Parries = ParryWhiffs = ParryLocked = GuardBlocks = GuardBreaks = AirEvades = Lunges = LungeDashes = Refunds = SecondWinds = FeintBaits = 0;
            GuardSaved = 0;
        }
    }

    public static partial class MovesHooks
    {
        // Integrator (combine 2026-11-22): the heavy questions are lane 4's public queries (MovesHooks.Offence.cs):
        // OffenceHeavyBreaksGuard (a full-charge Warrior overhead), OffenceGuardDrainMul (the Miner's armour breaker) and
        // OffenceHitsAirborne (the Builder's ground slam). The partial DefenceAskHeavy placeholder they replace is gone.

        static DefenceMovesState DefenceS(World w) => w.Moves.Defence;

        static int DefenceTicks(World w, double seconds) => CombatFeel.Ticks(w, seconds);

        static int DefenceCls(World w, int i) => (int)w.Actors[i].Cls;

        static double DefenceMeterMax(World w, int i)
        {
            double[] row = Cfg(w).GuardMeter;
            int c = DefenceCls(w, i);
            return row != null && c < row.Length ? row[c] : 0;
        }

        static double DefenceCut(World w, int i)
        {
            double[] row = Cfg(w).GuardCut;
            int c = DefenceCls(w, i);
            return row != null && c < row.Length ? row[c] : 0;
        }

        /// <summary>True when actor i has a guard at all (the Warrior parries only: his SHIELD is his block).</summary>
        static bool DefenceHasGuard(World w, int i) => w.Actors[i].Cls != ClassId.Warrior && DefenceCut(w, i) > 0;

        static double DefenceMeter(World w, int i)
        {
            DefenceMovesState d = DefenceS(w);
            if (d.GuardMeter[i] < 0) d.GuardMeter[i] = DefenceMeterMax(w, i);
            return d.GuardMeter[i];
        }

        /// <summary>Busy: actor i may not raise a guard or attempt a parry now (own wind-up, staggered, knocked, dodging,
        /// airborne, getting up, landing recovery).</summary>
        static bool DefenceBusy(World w, int i)
        {
            Actor a = w.Actors[i];
            if (!a.Alive || a.StandUp > 0 || a.AirLen > 0 || a.SwingPending || a.HitStunTicks > 0) return true;
            if (Dodge.Active(w, i)) return true;
            if (MovementLanding(w, i)) return true;   // integrator: lane 3's landing recovery blocks the guard too
            return w.Tick < DefenceS(w).LandRecoverUntil[i];
        }

        /// <summary>True when the guard of actor i blocks now: raised GuardRaiseTicks ago or more, with meter left.</summary>
        static bool DefenceGuardUp(World w, int i)
        {
            DefenceMovesState d = DefenceS(w);
            return d.GuardOn[i] != 0 && w.Tick - d.GuardSince[i] >= Cfg(w).GuardRaiseTicks && DefenceMeter(w, i) > 0;
        }

        /// <summary>True when (srcX, srcZ) lies inside actor i's frontal cone of GuardFrontalDeg (a source on top is frontal).</summary>
        static bool DefenceFrontal(World w, int i, double srcX, double srcZ)
        {
            double dx = srcX - w.X[i], dz = srcZ - w.Z[i];
            double d2 = dx * dx + dz * dz;
            if (!(d2 > 1e-12)) return true;
            double f = w.Actors[i].Facing;
            double dot = (dx * JsMath.Cos(f) + dz * JsMath.Sin(f)) / Math.Sqrt(d2);
            return dot >= JsMath.Cos(Cfg(w).GuardFrontalDeg * Math.PI / 180) - 1e-9;
        }

        static bool DefenceEnemyActor(World w, int target, int source) =>
            source >= 0 && source < w.ActorCount && source != target && w.Actors[source].Team != w.Actors[target].Team;

        static int DefenceParryWindow(World w, int i)
        {
            MovesConfig m = Cfg(w);
            Actor a = w.Actors[i];
            if (a.Cls == ClassId.Warrior && !(a.AbilityActive > 0)) return m.WarriorParryWindowShieldDown;
            int[] row = m.ParryWindowTicks;
            int c = (int)a.Cls;
            return row != null && c < row.Length ? row[c] : 0;
        }

        /// <summary>The melee hit by source on target landing now is parried (no side effects): Guard on, an enemy actor's melee
        /// swing, target's last valid Guard press edge 1..ParryWindowTicks ticks before this contact tick, source in front.</summary>
        static bool DefenceParries(World w, int target, int source, double srcX, double srcZ, DamageCause cause)
        {
            if (!On(w, Cfg(w).Guard) || cause != DamageCause.Melee || !DefenceEnemyActor(w, target, source)) return false;
            DefenceMovesState d = DefenceS(w);
            int p = d.ParryPress[target];
            if (p == CombatFeel.NoTick) return false;
            int before = w.Tick - p;
            if (before < 1 || before > DefenceParryWindow(w, target)) return false;
            return w.Actors[target].Alive && DefenceFrontal(w, target, srcX, srcZ);
        }

        /// <summary>The melee hit by source on target landing now passes under an airborne body (no side effects): JumpEvade
        /// on, a grounded enemy actor's melee swing, the target on air ticks JumpEvadeFromAirTick..JumpEvadeToAirTick, and not
        /// the Builder's ground slam (a heavy: lane 4).</summary>
        static bool DefenceAirEvades(World w, int target, int source, DamageCause cause)
        {
            MovesConfig m = Cfg(w);
            if (!On(w, m.JumpEvade) || cause != DamageCause.Melee || !DefenceEnemyActor(w, target, source)) return false;
            if (w.Actors[source].AirLen > 0) return false;
            // integrator: lane 3's window (air ticks JumpEvadeFromAirTick..ToAirTick, a glider is never inside it)
            if (!MovementInAirEvadeWindow(w, target)) return false;
            if (OffenceHitsAirborne(w, source)) return false;   // the Builder's ground slam hits airborne bodies (lane 4)
            return true;
        }

        /// <summary>The guard of target cuts this hit (no side effects): Guard on, a melee or blast hit from an enemy actor, the
        /// guard up with meter left, the source in front. The Kaiju, falls and every other cause are never cut.</summary>
        static bool DefenceGuardBlocks(World w, int target, int source, double srcX, double srcZ, DamageCause cause)
        {
            if (!On(w, Cfg(w).Guard)) return false;
            if (cause != DamageCause.Melee && cause != DamageCause.Explosion) return false;
            if (!DefenceEnemyActor(w, target, source) || !DefenceHasGuard(w, target)) return false;
            return DefenceGuardUp(w, target) && DefenceFrontal(w, target, srcX, srcZ);
        }

        /// <summary>T2-8: once per life, below SecondWindBelowShare of max HP, a successful dodge, parry or guard block restores
        /// SecondWindHealShare of max HP and clears WOUNDED (event SecondWind, Value = HP restored).</summary>
        static void DefenceSecondWind(World w, int i)
        {
            MovesConfig m = Cfg(w);
            if (!On(w, m.SecondWind)) return;
            DefenceMovesState d = DefenceS(w);
            Actor a = w.Actors[i];
            if (d.SecondWindUsed[i] != 0 || !a.Alive || !(a.MaxHp > 0) || !(a.Hp < m.SecondWindBelowShare * a.MaxHp)) return;
            double heal = m.SecondWindHealShare * a.MaxHp;
            if (a.Hp + heal > a.MaxHp) heal = a.MaxHp - a.Hp;
            a.Hp += heal;
            d.SecondWindUsed[i] = 1;
            d.SecondWinds++;
            if (CombatFeel.On(w)) w.Depth.Feel.WoundedUntil[i] = CombatFeel.NoTick;
            World.PushEvent(w, SimEventType.SecondWind, i, -1, w.X[i], w.Z[i], heal);
        }

        // ================================================================== per tick

        /// <summary>Per tick, before Abilities: guard raise / hold / release, meter refill, parry press edges and lockout, the
        /// lunge's press, per-life resets, the feint bait's after-window.</summary>
        static void DefenceStep(World w, double dt)
        {
            MovesConfig m = Cfg(w);
            DefenceMovesState d = DefenceS(w);
            MovesState s = w.Moves;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive)
                {
                    // a new life: full meter, no guard, second wind ready again
                    d.GuardOn[i] = 0;
                    d.GuardMeter[i] = -1;
                    d.ParryPress[i] = CombatFeel.NoTick;
                    d.LungeArmed[i] = 0;
                    d.FlightEvaded[i] = 0;
                    d.SecondWindUsed[i] = 0;
                    continue;
                }
                if (m.Guard) DefenceGuardTick(w, d, s, m, i);
                if (m.DodgeLunge && d.LungeArmed[i] != 0)
                {
                    if (Dodge.Active(w, i) && w.Inputs[i].Attack) d.LungePressed[i] = 1;
                    if (w.Tick > d.LungeDashEnd[i] + 2) d.LungeArmed[i] = 0;
                }
                if (m.FeintBait && d.FeintDone[i] == 0)
                {
                    if (w.Tick - d.FeintTick[i] > m.FeintBaitAfterTicks) d.FeintDone[i] = 1;
                    else DefenceFeintScan(w, i, false);
                }
            }
        }

        static void DefenceGuardTick(World w, DefenceMovesState d, MovesState s, MovesConfig m, int i)
        {
            Actor a = w.Actors[i];
            bool held = s.GuardHeldTicks[i] > 0;
            bool busy = DefenceBusy(w, i);
            bool guardClass = DefenceHasGuard(w, i);
            if (s.GuardPressed[i] != 0 && DefenceParryWindow(w, i) <= 0)
            {
                // integrator: a class with no parry window (the Healer): the press is only a guard; a tap holds it
                // ParryWhiffHoldTicks, with no parry attempt and no lockout
                d.ParryPress[i] = CombatFeel.NoTick;
                if (!busy) d.GuardHoldUntil[i] = w.Tick + m.ParryWhiffHoldTicks;
            }
            else if (s.GuardPressed[i] != 0)
            {
                // the PARRY press edge (a parry attempt, locked out for ParryLockoutSeconds so mashing does not work)
                if (busy) d.ParryPress[i] = CombatFeel.NoTick;
                else if (w.Tick < d.ParryLockUntil[i])
                {
                    d.ParryPress[i] = CombatFeel.NoTick;
                    d.ParryLocked++;
                    PlayerSeat.Refuse(w, i, PlayerReasons.ButtonGuard, PlayerReasons.RefusedParryLockout);
                }
                else
                {
                    d.ParryPress[i] = w.Tick;
                    d.ParryLockUntil[i] = w.Tick + DefenceTicks(w, m.ParryLockoutSeconds);
                    // a press holds the guard (the Warrior: his swing) ParryWhiffHoldTicks at least; a parry that lands clears it
                    d.GuardHoldUntil[i] = w.Tick + m.ParryWhiffHoldTicks;
                }
            }
            else if (d.ParryPress[i] != CombatFeel.NoTick && w.Tick - d.ParryPress[i] == DefenceParryWindow(w, i) + 1 && d.ParryLockUntil[i] > w.Tick)
                d.ParryWhiffs++;   // the window passed with no parry (counter only; a parry that landed cleared the lockout)
            if (guardClass)
            {
                if (d.GuardOn[i] != 0)
                {
                    // dodge cancels the guard (no release cost); a stagger, a flight or getting up drops it
                    if (Dodge.Active(w, i) || a.AirLen > 0 || a.StandUp > 0 || a.HitStunTicks > 0) d.GuardOn[i] = 0;
                    else if (!held && w.Tick >= d.GuardHoldUntil[i])
                    {
                        d.GuardOn[i] = 0;
                        int[] rel = m.GuardReleaseTicksByClass;   // integrator: per-class release (the Healer's is slow)
                        int rc = DefenceCls(w, i);
                        d.GuardReleaseUntil[i] = w.Tick + (rel != null && rc < rel.Length && rel[rc] >= 0 ? rel[rc] : m.GuardReleaseTicks);
                    }
                }
                else if ((held || w.Tick < d.GuardHoldUntil[i]) && !busy && w.Tick >= d.GuardReleaseUntil[i])
                {
                    d.GuardOn[i] = 1;
                    d.GuardSince[i] = w.Tick;
                }
                // the meter refills GuardRefillPerSecond after GuardRefillDelaySeconds without a blocked hit
                double max = DefenceMeterMax(w, i);
                double now = DefenceMeter(w, i);
                if (now < max && w.Tick - d.GuardLastBlock[i] >= DefenceTicks(w, m.GuardRefillDelaySeconds))
                {
                    int perSecond = DefenceTicks(w, 1.0);
                    now += perSecond > 0 ? m.GuardRefillPerSecond / perSecond : m.GuardRefillPerSecond;
                    d.GuardMeter[i] = now > max ? max : now;
                }
            }
        }

        // ================================================================== locks and speed

        static bool DefenceLocksAttack(World w, int i)
        {
            MovesConfig m = Cfg(w);
            DefenceMovesState d = DefenceS(w);
            if (m.JumpEvade && w.Tick < d.LandRecoverUntil[i])
            {
                PlayerSeat.Refuse(w, i, PlayerReasons.ButtonAttack, PlayerReasons.RefusedLanding);
                return true;
            }
            if (!m.Guard) return false;
            bool guarding = d.GuardOn[i] != 0 || w.Tick < d.GuardReleaseUntil[i]
                || (w.Actors[i].Cls == ClassId.Warrior && w.Tick < d.GuardHoldUntil[i]);
            if (guarding) PlayerSeat.Refuse(w, i, PlayerReasons.ButtonAttack, PlayerReasons.RefusedGuarding);
            return guarding;
        }

        static bool DefenceLocksCast(World w, int i)
        {
            MovesConfig m = Cfg(w);
            DefenceMovesState d = DefenceS(w);
            if (m.JumpEvade && w.Tick < d.LandRecoverUntil[i]) return true;
            if (!m.Guard) return false;
            bool guarding = d.GuardOn[i] != 0 || w.Tick < d.GuardReleaseUntil[i];
            if (guarding && w.Inputs[i].Ability) PlayerSeat.Refuse(w, i, PlayerReasons.ButtonAbility, PlayerReasons.RefusedGuarding);
            return guarding;
        }

        /// <summary>Dodge.CanStart (lane 2's file): true = no dodge now (the landing recovery after an evading jump). A guard
        /// never blocks the dodge: the dodge cancels it.</summary>
        internal static bool DefenceLocksDodge(World w, int i)
        {
            if (w.Moves == null || !Cfg(w).JumpEvade) return false;
            return w.Tick < DefenceS(w).LandRecoverUntil[i];
        }

        static double DefenceSpeedScale(World w, int i)
        {
            if (!Cfg(w).Guard || DefenceS(w).GuardOn[i] == 0) return 1.0;
            double[] row = Cfg(w).GuardMoveMul;
            int c = DefenceCls(w, i);
            return row != null && c < row.Length ? row[c] : 1.0;
        }

        // ================================================================== T1-2 lunge (Dodge.cs calls these)

        /// <summary>Dodge.Begin, once the dash direction (dx, dz) is known: the dash distance. A dodge within LungeConeDeg of an
        /// enemy whose body is within the dash distance plus the dodger's reach is a dodge-in: the dash is clipped to end
        /// LungeStopShare x reach (plus the enemy's body) from it, and an Attack seen during the dash fires as a LUNGE. Returns
        /// distance unchanged otherwise (and always while the switch is off).</summary>
        internal static double DefenceLungeDash(World w, int id, double dx, double dz, double distance)
        {
            if (w.Moves == null || !Cfg(w).DodgeLunge) return distance;
            MovesConfig m = Cfg(w);
            DefenceMovesState d = DefenceS(w);
            d.LungeArmed[id] = 0;
            d.LungePressed[id] = 0;
            Actor a = w.Actors[id];
            double reach = w.Config.Class(a.Cls).AttackRange;
            double cone = JsMath.Cos(m.LungeConeDeg * Math.PI / 180);
            int best = -1;
            double bestD = double.MaxValue;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (j == id || !t.Alive || t.Team == a.Team) continue;
                double ex = w.X[j] - w.X[id], ez = w.Z[j] - w.Z[id];
                double ed = Math.Sqrt(ex * ex + ez * ez);
                if (!(ed > 1e-6)) continue;
                double body = ed - w.Config.Class(t.Cls).Radius;
                if (body > distance + reach) continue;
                if ((ex * dx + ez * dz) / ed < cone - 1e-9) continue;
                if (ed < bestD) { bestD = ed; best = j; }
            }
            if (best < 0) return distance;
            // the stop point is measured from where the enemy will be at the lunge's contact (its velocity over the dash and the
            // lunge wind-up), so a dodge-in on a runner ends in reach at contact, not where the runner was at the press
            DodgeConfig dc = w.Config.Depth.Dodge;
            int lead = (dc != null ? dc.DashTicks : 6) + (int)Math.Floor(w.Dur.AttackWindup[(int)a.Cls] * DefenceLungeMul(w, id) + 1e-9);
            double leadSec = lead / w.Config.SimHz;
            double rx = w.X[best] + w.Vx[best] * leadSec - w.X[id], rz = w.Z[best] + w.Vz[best] * leadSec - w.Z[id];
            double stop = m.LungeStopShare * reach + w.Config.Class(w.Actors[best].Cls).Radius;
            double along = rx * dx + rz * dz;
            double perp2 = rx * rx + rz * rz - along * along;
            double go = perp2 >= stop * stop ? along : along - Math.Sqrt(stop * stop - perp2);
            double min = distance * DefenceLungeMinShare;
            if (go < min) go = min;
            if (go > distance) go = distance;
            d.LungeArmed[id] = 1;
            d.LungeTarget[id] = best;
            d.LungeDashEnd[id] = CombatFeel.NoTick;
            return go;
        }

        /// <summary>Integrator: actor i's lunge wind-up multiplier (LungeWindupMulByClass, else LungeWindupMul).</summary>
        static double DefenceLungeMul(World w, int i)
        {
            MovesConfig m = Cfg(w);
            double[] row = m.LungeWindupMulByClass;
            int c = DefenceCls(w, i);
            return row != null && c < row.Length && row[c] >= 0 ? row[c] : m.LungeWindupMul;
        }

        /// <summary>The shortest dodge-in dash, as a share of the dodge distance (an enemy already inside the stop distance).</summary>
        const double DefenceLungeMinShare = 0.2;

        /// <summary>Dodge.Begin, after a dodge started (s.Start and s.Len set): remembers the dodge-in's first recover tick.</summary>
        internal static void DefenceDodgeBegan(World w, int id, int start, int len)
        {
            if (w.Moves == null) return;
            OffenceVictimDodged(w, id);   // integrator (decision 2): a dodge breaks the chains that last hit this body
            Actor ka = w.Actors[id];
            if ((ka.KVx != 0 || ka.KVz != 0) && DefenceDodgeOutOfKnock(w, id))
            {
                ka.KVx = 0;   // integrator (decision 2): the dodge ends the light knock slide it started out of
                ka.KVz = 0;
                DefenceS(w).LightKnockTick[id] = CombatFeel.NoTick;
            }
            if (!Cfg(w).DodgeLunge) return;
            DefenceMovesState d = DefenceS(w);
            if (d.LungeArmed[id] == 0) return;
            d.LungeDashEnd[id] = start + len;
            d.LungeDashes++;
        }

        static int DefenceSwingWindup(World w, int i, int windup)
        {
            MovesConfig m = Cfg(w);
            if (!m.DodgeLunge) return windup;
            DefenceMovesState d = DefenceS(w);
            if (d.LungeArmed[i] == 0) return windup;
            bool pressed = d.LungePressed[i] != 0 || w.Inputs[i].Attack;
            if (!pressed || w.Tick < d.LungeDashEnd[i] || w.Tick > d.LungeDashEnd[i] + 2) return windup;
            int lunge = (int)Math.Floor(windup * DefenceLungeMul(w, i) + 1e-9);   // x0.75, rounded down (Warrior 12 to 9, Farmer 9 to 6); the Miner x0.6
            if (lunge < 1) lunge = 1;
            d.LungeArmed[i] = 0;
            d.LungeSwingTick[i] = w.Tick;   // integrator: Combat sets SwingTick = w.Tick right after this hook (LungeArmour)
            d.Lunges++;
            World.PushEvent(w, SimEventType.Lunge, i, d.LungeTarget[i], w.X[i], w.Z[i], lunge);
            return lunge;
        }

        static void DefenceAfterContact(World w, int i, int hits) { }

        // ================================================================== T1-3 refund, T2-8 on a dodge (Dodge.SwingDodges calls it)

        /// <summary>Dodge.SwingDodges: actor j dodged attacker i's swing (one HitBlocked BlockedDodge). T1-3 gives back
        /// AvoidRefundShare of the cooldown left; T2-8's second wind may fire.</summary>
        internal static void DefenceDodgedSwing(World w, int j, int i)
        {
            if (w.Moves == null) return;
            MovesConfig m = Cfg(w);
            if (m.DodgeAvoidRefund && m.AvoidRefundShare > 0)
            {
                DodgeState s = w.Dodges;
                int left = s.Ready[j] - w.Tick;
                if (left > 0)
                {
                    int back = (int)Math.Floor(left * m.AvoidRefundShare + 0.5);
                    if (back > 0)
                    {
                        s.Ready[j] -= back;
                        DefenceS(w).Refunds++;
                    }
                }
            }
            DefenceSecondWind(w, j);
        }

        // ================================================================== Damage.cs and CombatFeel.cs

        /// <summary>The melee strike's knock and stagger: a parried or airborne-evaded strike neither knocks nor staggers (the hit
        /// is negated next); a guard-blocked one does not stagger and knocks by the share that got through.</summary>
        static void DefenceStrikeKnock(World w, int source, int target, ref double knock, ref int stun)
        {
            if (source < 0 || source >= w.ActorCount) return;
            double sx = w.X[source], sz = w.Z[source];
            if (DefenceParries(w, target, source, sx, sz, DamageCause.Melee) || DefenceAirEvades(w, target, source, DamageCause.Melee))
            {
                knock = 0;
                stun = 0;
                return;
            }
            MovesConfig m = Cfg(w);
            if (DefenceGuardBlocks(w, target, source, sx, sz, DamageCause.Melee))
            {
                bool[] footing = m.GuardKeepsFooting;
                int gc = DefenceCls(w, target);
                if (footing == null || gc >= footing.Length || footing[gc])   // integrator: the Healer's guard only cuts damage
                {
                    stun = 0;
                    knock *= 1 - DefenceCut(w, target);
                }
            }
            Actor t = w.Actors[target];
            // integrator (decision 6): a LUNGE wind-up shrugs off a hit under the lunger's Poise (the swing goes on)
            if (m.DodgeLunge && m.LungeArmour && t.SwingPending && t.SwingTick == DefenceS(w).LungeSwingTick[target]
                && w.Moves.Offence.StrikeAmount[0] < w.Config.Class(t.Cls).Poise) stun = 0;
            // integrator (decision 2): the knock of an enemy's melee light strike may be dodged out of (Dodge.CanStart)
            if (m.Combos && m.ComboDodgeOutOfKnock && !OffenceIsHeavy(w, source) && DefenceEnemyActor(w, target, source)) DefenceS(w).LightKnockTick[target] = w.Tick;
        }

        /// <summary>Dodge.CanStart (integrator, decision 2): actor id slides on a knock but may dodge out of it: Combos and
        /// ComboDodgeOutOfKnock on, the stagger over, grounded, and the knock is an enemy's melee light strike taken in the last
        /// ComboKnockDodgeTicks. DefenceDodgeBegan ends the slide when that dodge starts.</summary>
        internal static bool DefenceDodgeOutOfKnock(World w, int id)
        {
            if (w.Moves == null) return false;
            MovesConfig m = Cfg(w);
            if (!m.Combos || !m.ComboDodgeOutOfKnock) return false;
            Actor a = w.Actors[id];
            if (a.HitStunTicks > 0 || a.AirLen > 0) return false;
            int k = DefenceS(w).LightKnockTick[id];
            return k != CombatFeel.NoTick && w.Tick - k <= m.ComboKnockDodgeTicks;
        }

        static bool DefenceHitNegated(World w, int target, int source, double srcX, double srcZ, DamageCause cause, bool report)
        {
            DefenceMovesState d = DefenceS(w);
            MovesConfig m = Cfg(w);
            if (DefenceParries(w, target, source, srcX, srcZ, cause))
            {
                d.Parries++;
                d.ParryLockUntil[target] = w.Tick;   // a parry that lands frees the next attempt
                d.GuardHoldUntil[target] = w.Tick;   // and the whiff hold (answer with the PUNISH)
                if (report) World.PushEvent(w, SimEventType.HitBlocked, target, source, w.X[target], w.Z[target], PlayerReasons.BlockedParried, null, DamageCause.None, 0);
                // the attacker: staggered ParryStaggerTicks (never the Warrior's armoured cleave) and open to the existing PUNISH
                // critical for ParryPunishSeconds (no new crit kind or multiplier)
                if (w.Actors[source].Cls != ClassId.Warrior) Knock.Push(w, source, target, 0, 0, 0, m.ParryStaggerTicks, 0, DamageCause.Melee);
                if (CombatFeel.On(w))
                {
                    int until = w.Tick + DefenceTicks(w, m.ParryPunishSeconds);
                    if (w.Depth.Feel.WhiffRecoverUntil[source] < until) w.Depth.Feel.WhiffRecoverUntil[source] = until;
                }
                DefenceSecondWind(w, target);
                return true;
            }
            if (DefenceAirEvades(w, target, source, cause))
            {
                d.AirEvades++;
                d.FlightEvaded[target] = 1;
                MovementNoteAirEvade(w, target);   // integrator: lane 3's landing lock sees the evade even when HitBlocked is not raised
                if (report) World.PushEvent(w, SimEventType.HitBlocked, target, source, w.X[target], w.Z[target], PlayerReasons.BlockedAir, null, DamageCause.None, 0);
                return true;
            }
            return false;
        }

        static double DefenceHitCut(World w, int target, int source, double srcX, double srcZ, DamageCause cause, double dmg, bool report)
        {
            if (!(dmg > 0) || !DefenceGuardBlocks(w, target, source, srcX, srcZ, cause)) return dmg;
            MovesConfig m = Cfg(w);
            DefenceMovesState d = DefenceS(w);
            double cut = DefenceCut(w, target);
            if (cause == DamageCause.Explosion) cut *= m.GuardBlastCutShare;
            double saved = dmg * cut;
            double drain = 1;
            bool breakNow = false;
            if (cause == DamageCause.Melee)
            {
                ClassId sc = w.Actors[source].Cls;
                if (sc == ClassId.Warrior) drain = m.GuardDrainWarriorCleave;
                else if (sc == ClassId.Miner) drain = m.GuardDrainMinerPick;
                double heavyDrain = OffenceGuardDrainMul(w, source);   // lane 4: the Miner's armour breaker (0 = no rule)
                if (heavyDrain > 0) drain = heavyDrain;
                if (OffenceHeavyBreaksGuard(w, source)) breakNow = true;   // lane 4: a full-charge Warrior overhead
            }
            double meter = DefenceMeter(w, target);
            bool broke = breakNow;
            if (breakNow) saved = 0;
            else if (saved * drain >= meter)
            {
                saved = drain > 0 ? meter / drain : saved;
                broke = true;
            }
            d.GuardMeter[target] = broke ? 0 : meter - saved * drain;
            d.GuardLastBlock[target] = w.Tick;
            dmg -= saved;
            d.GuardSaved += saved;
            if (report && saved > 0) World.PushEvent(w, SimEventType.HitBlocked, target, source, w.X[target], w.Z[target], PlayerReasons.BlockedGuard, null, DamageCause.None, saved);
            if (broke)
            {
                d.GuardBreaks++;
                d.GuardOn[target] = 0;
                d.GuardHoldUntil[target] = CombatFeel.NoTick;
                World.PushEvent(w, SimEventType.GuardBreak, target, source, w.X[target], w.Z[target], m.GuardBreakStaggerTicks);
                Knock.Push(w, target, source, 0, 0, 0, m.GuardBreakStaggerTicks, 0, cause);
            }
            else
            {
                d.GuardBlocks++;
                DefenceSecondWind(w, target);
            }
            return dmg;
        }

        static void DefenceDamaged(World w, int target, int source, DamageCause cause, double dmg) { }

        static void DefenceLanded(World w, int id)
        {
            DefenceMovesState d = DefenceS(w);
            if (d.FlightEvaded[id] == 0) return;
            d.FlightEvaded[id] = 0;
            MovesConfig m = Cfg(w);
            if (!m.JumpEvade) return;
            d.LandRecoverUntil[id] = w.Tick + m.JumpEvadeLandingTicks;
            d.JumpRelockUntil[id] = w.Tick + DefenceTicks(w, m.JumpEvadeRelockSeconds);
        }

        // ================================================================== T1-4a feint bait

        static void DefenceFeinted(World w, int i)
        {
            if (!Cfg(w).FeintBait) return;
            DefenceMovesState d = DefenceS(w);
            d.FeintTick[i] = w.Tick;
            d.FeintDone[i] = 0;
            DefenceFeintScan(w, i, true);
        }

        /// <summary>FeintBaited (A = the feinter, B = the victim, (X, Z) = the victim, Value 1 dodge / 2 guard / 3 parry attempt)
        /// when an enemy near feinter i started a dodge or a guard press FeintBaitBeforeTicks before the feint (before = true)
        /// or after it, up to FeintBaitAfterTicks (before = false, called each tick). One per feint.</summary>
        static void DefenceFeintScan(World w, int i, bool before)
        {
            MovesConfig m = Cfg(w);
            DefenceMovesState d = DefenceS(w);
            int f = d.FeintTick[i];
            Actor a = w.Actors[i];
            double reach = w.Config.Class(a.Cls).AttackRange + (w.Config.Depth.Dodge != null ? w.Config.Depth.Dodge.Distance : 0);
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (j == i || !t.Alive || t.Team == a.Team) continue;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i], r = reach + w.Config.Class(t.Cls).Radius;
                if (dx * dx + dz * dz > r * r) continue;
                int kind = 0;
                if (Dodge.On(w))
                {
                    int ds = Dodge.LastStart(w, j);
                    if (before ? ds >= f - m.FeintBaitBeforeTicks && ds <= f : ds > f && ds <= w.Tick) kind = 1;
                }
                if (kind == 0 && m.Guard)
                {
                    int gp = w.Moves.LastGuardPressTick[j];
                    if (gp >= 0 && (before ? gp >= f - m.FeintBaitBeforeTicks && gp <= f : gp > f && gp <= w.Tick))
                        kind = w.Moves.GuardHeldTicks[j] > 0 ? 2 : 3;   // still held: a guard; already let go: a parry attempt
                }
                if (kind == 0) continue;
                d.FeintDone[i] = 1;
                d.FeintBaits++;
                World.PushEvent(w, SimEventType.FeintBaited, i, j, w.X[j], w.Z[j], kind);
                return;
            }
        }
    }
}

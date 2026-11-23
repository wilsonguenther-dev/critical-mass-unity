// Class moves build (2026-11-22): LANE 3 (Movement) owns this file: T2-4 travel, T2-5 slide, T2-6 glide, T2-7 the air window
// and the landing lock. Every rule acts only while its own switch is on (MovesHooks.On(w, MovesHooks.Cfg(w).<Switch>)), so with
// every switch off each hook below is the identity lane 1 stubbed (and World.Moves is null anyway). Contract: LANE1-CONTRACT.md.
// Deterministic: integer ticks, actor-id order, no RNG, no allocation per tick (every array is sized once in Init).
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Lane 3's class-moves state (World.Moves.Movement): public primitive arrays sized in Init, cleared in Reset.</summary>
    public sealed class MovementMovesState
    {
        /// <summary>T2-4: 1 while TRAVEL is lit.</summary>
        public byte[] Travel = new byte[0];
        /// <summary>T2-4: ticks TRAVEL has been lit, including this one (the ramp to TravelSpeedMul over TravelRampTicks).</summary>
        public int[] TravelTicks = new int[0];
        /// <summary>T2-4: the last tick this actor took or dealt a hit (TravelNoCombatSeconds blocks travel after it).</summary>
        public int[] CombatTick = new int[0];
        /// <summary>T2-5: TRAVEL ended because an enemy came within TravelEnemyRadius: the slide stays the Dodge until this
        /// tick (exclusive), so the slide can still reach a body (see MovementSlideCarryTicks). -1 = none.</summary>
        public int[] SlideCarryUntil = new int[0];
        /// <summary>T2-5: the tick the slide started, -1 = not sliding.</summary>
        public int[] SlideStart = new int[0];
        public double[] SlideDirX = new double[0], SlideDirZ = new double[0];
        /// <summary>T2-5: the SLIDE TRIP recovery of the slider runs until this tick (exclusive).</summary>
        public int[] SlideRecoverUntil = new int[0];
        /// <summary>T2-6: 1 while gliding (AirTick is held, so the view's jump arc holds its height).</summary>
        public byte[] Gliding = new byte[0];
        /// <summary>T2-6: glide ticks used in this glide.</summary>
        public int[] GlideTicks = new int[0];
        /// <summary>T2-6: the next glide may start at this tick (GlideCooldownSeconds after the last glide ended).</summary>
        public int[] GlideReady = new int[0];
        /// <summary>T2-6: 1 = the takeoff was planned with a glide (the plain jump had no clear landing): the glide starts at the
        /// apex even if Jump was let go, and lasts until the descent lands on walkable ground.</summary>
        public byte[] GlidePlanned = new byte[0];
        /// <summary>T2-6: 1 = a hit ended the glide: no steering, the body drops at the first tick its descent lands clear.</summary>
        public byte[] GlideHit = new byte[0];
        /// <summary>T2-7: 1 = a ground melee hit passed under this flight (HitBlocked BlockedAir, or lane 2's MovementNoteAirEvade).</summary>
        public byte[] AirEvaded = new byte[0];
        /// <summary>T2-7: the landing recovery after an evading jump runs until this tick (exclusive): no dodge, attack or guard.</summary>
        public int[] LandRecoverUntil = new int[0];
        /// <summary>T2-7: the next jump is locked until this tick (exclusive).</summary>
        public int[] JumpRelockUntil = new int[0];
        /// <summary>T2-7: [0] = the event ring position read so far (World.EventHead), -1 = not started.</summary>
        public int[] EventCursor = new int[1];

        public void Init(World w)
        {
            int n = World.ActorCapacity(w.Config);
            Travel = new byte[n];
            TravelTicks = new int[n];
            CombatTick = new int[n];
            SlideCarryUntil = new int[n];
            SlideStart = new int[n];
            SlideDirX = new double[n];
            SlideDirZ = new double[n];
            SlideRecoverUntil = new int[n];
            Gliding = new byte[n];
            GlideTicks = new int[n];
            GlideReady = new int[n];
            GlidePlanned = new byte[n];
            GlideHit = new byte[n];
            AirEvaded = new byte[n];
            LandRecoverUntil = new int[n];
            JumpRelockUntil = new int[n];
            EventCursor = new int[1];
        }

        public void Reset(World w)
        {
            System.Array.Clear(Travel, 0, Travel.Length);
            System.Array.Clear(TravelTicks, 0, TravelTicks.Length);
            for (int i = 0; i < CombatTick.Length; i++) CombatTick[i] = int.MinValue / 2;
            for (int i = 0; i < SlideCarryUntil.Length; i++) SlideCarryUntil[i] = -1;
            for (int i = 0; i < SlideStart.Length; i++) SlideStart[i] = -1;
            System.Array.Clear(SlideDirX, 0, SlideDirX.Length);
            System.Array.Clear(SlideDirZ, 0, SlideDirZ.Length);
            System.Array.Clear(SlideRecoverUntil, 0, SlideRecoverUntil.Length);
            System.Array.Clear(Gliding, 0, Gliding.Length);
            System.Array.Clear(GlideTicks, 0, GlideTicks.Length);
            System.Array.Clear(GlideReady, 0, GlideReady.Length);
            System.Array.Clear(GlidePlanned, 0, GlidePlanned.Length);
            System.Array.Clear(GlideHit, 0, GlideHit.Length);
            System.Array.Clear(AirEvaded, 0, AirEvaded.Length);
            System.Array.Clear(LandRecoverUntil, 0, LandRecoverUntil.Length);
            System.Array.Clear(JumpRelockUntil, 0, JumpRelockUntil.Length);
            EventCursor[0] = -1;
        }
    }

    public static partial class MovesHooks
    {
        /// <summary>
        /// T2-5 (lane 3 decision, knob requested from the integrator): after TRAVEL ends because an enemy came within
        /// TravelEnemyRadius (8 u), Dodge stays the SLIDE for this many ticks (1.0 s), unless a hit, an attack, cast, guard or
        /// interact press ends it first. Without it no slide could ever reach a body: travel ends 8 u out and the slide covers 4.5 u.
        /// 1.0 s lets the slowest class (the Warrior, 3.61 u/s) close from 8 u to slide range with a 0.2 s reaction.
        /// </summary>
        public const int MovementSlideCarryTicks = 30;

        /// <summary>T2-5: extra slack (u) on the slide's body-contact test (bodies touch at the sum of the two radii).</summary>
        const double MovementTripSlack = 0.05;

        // ================================================================== read-only queries (lane 6's HUD and views, lane 2)

        /// <summary>T2-4: TRAVEL is lit for actor i (the dodge slot reads TRAVEL / SLIDE).</summary>
        public static bool MovementTravelling(World w, int i) =>
            w.Moves != null && On(w, Cfg(w).Travel) && i >= 0 && i < w.Moves.Movement.Travel.Length && w.Moves.Movement.Travel[i] != 0;

        /// <summary>T2-4: the travel ramp 0..1 (0 = walking; the view's stride and dust read it).</summary>
        public static double MovementTravelShare(World w, int i)
        {
            if (!MovementTravelling(w, i)) return 0;
            int ramp = Durations.T30(w.Config, Cfg(w).TravelRampTicks);
            int t = w.Moves.Movement.TravelTicks[i];
            return ramp <= 0 || t >= ramp ? 1 : t / (double)ramp;
        }

        /// <summary>T2-5: a Dodge press by actor i right now would be a SLIDE (the HUD slot reads SLIDE).</summary>
        public static bool MovementSlideArmed(World w, int i)
        {
            if (w.Moves == null || !On(w, Cfg(w).Slide) || i < 0 || i >= w.Moves.Movement.Travel.Length) return false;
            MovementMovesState m = w.Moves.Movement;
            return m.Travel[i] != 0 && On(w, Cfg(w).Travel) || w.Tick < m.SlideCarryUntil[i];
        }

        /// <summary>T2-5: actor i is mid-slide.</summary>
        public static bool MovementSliding(World w, int i) =>
            w.Moves != null && i >= 0 && i < w.Moves.Movement.SlideStart.Length && w.Moves.Movement.SlideStart[i] >= 0;

        /// <summary>T2-6: actor i glides (HEALING WINGS).</summary>
        public static bool MovementGliding(World w, int i) =>
            w.Moves != null && i >= 0 && i < w.Moves.Movement.Gliding.Length && w.Moves.Movement.Gliding[i] != 0;

        /// <summary>T2-6: the share of the glide time left, 1..0 (the shrinking ring); 0 when not gliding.</summary>
        public static double MovementGlideShareLeft(World w, int i)
        {
            if (!MovementGliding(w, i)) return 0;
            int max = Durations.T30(w.Config, Cfg(w).GlideExtraTicks);
            return max <= 0 ? 0 : System.Math.Max(0, max - w.Moves.Movement.GlideTicks[i]) / (double)max;
        }

        /// <summary>T2-7 (for lane 2's hit test): actor i is in the jump's evade window (air ticks JumpEvadeFromAirTick to
        /// JumpEvadeToAirTick). A glider is not: the glide holds AirTick at the apex, so a glider would otherwise be immune to
        /// ground melee for the whole glide, and "a hit ends the glide" (T2-6) needs her hittable.</summary>
        public static bool MovementInAirEvadeWindow(World w, int i)
        {
            if (w.Moves == null || !On(w, Cfg(w).JumpEvade) || i < 0 || i >= w.ActorCount) return false;
            Actor a = w.Actors[i];
            if (a.AirLen <= 0 || MovementGliding(w, i)) return false;
            MovesConfig c = Cfg(w);
            return a.AirTick >= Durations.T30(w.Config, c.JumpEvadeFromAirTick) && a.AirTick <= Durations.T30(w.Config, c.JumpEvadeToAirTick);
        }

        /// <summary>T2-7 (for lane 2): a ground melee hit passed under actor i's flight. Its landing then costs the landing
        /// recovery and the jump relock. Lane 3 also reads HitBlocked BlockedAir from the event ring, so this call only matters
        /// where that event is not raised (HitBlocked is a reporting event: PlayerSeat on, enemy melee or blast).</summary>
        public static void MovementNoteAirEvade(World w, int i)
        {
            if (w.Moves == null || !On(w, Cfg(w).JumpEvade) || i < 0 || i >= w.ActorCount) return;
            if (w.Actors[i].AirLen > 0) w.Moves.Movement.AirEvaded[i] = 1;
        }

        /// <summary>T2-7 (for lane 2's guard lock): actor i is in the landing recovery after an evading jump.</summary>
        public static bool MovementLanding(World w, int i) =>
            w.Moves != null && On(w, Cfg(w).JumpEvade) && i >= 0 && i < w.Moves.Movement.LandRecoverUntil.Length
            && w.Tick < w.Moves.Movement.LandRecoverUntil[i];

        // ================================================================== hooks (lane 1's stubs, filled)

        /// <summary>Per tick, before Abilities (travel eligibility and ramp, glide bookkeeping, the evade event scan).</summary>
        static void MovementStep(World w, double dt)
        {
            MovesConfig c = Cfg(w);
            MovementMovesState m = w.Moves.Movement;
            if (On(w, c.JumpEvade)) MovementScanEvades(w, m);
            bool travel = On(w, c.Travel);
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (m.Gliding[i] != 0 && (a.AirLen <= 0 || !a.Alive)) MovementGlideEnd(w, m, i, 0);
                if (a.AirLen <= 0)
                {
                    m.GlidePlanned[i] = 0;
                    m.AirEvaded[i] = 0;
                }
                if (!a.Alive) m.SlideStart[i] = -1;
                if (travel) MovementTravelUpdate(w, c, m, i);
            }
        }

        /// <summary>True = actor i may not start a swing (gliding, mid-slide, slide-trip recovery, evade landing recovery).</summary>
        static bool MovementLocksAttack(World w, int i)
        {
            MovementMovesState m = w.Moves.Movement;
            int why = 0;
            if (m.Gliding[i] != 0) why = PlayerReasons.RefusedGliding;
            else if (m.SlideStart[i] >= 0 || w.Tick < m.SlideRecoverUntil[i]) why = PlayerReasons.RefusedStaggered;
            else if (MovementLanding(w, i)) why = PlayerReasons.RefusedLanding;
            if (why == 0) return false;
            if (w.Moves.AttackHeldTicks[i] == 1) PlayerSeat.Refuse(w, i, PlayerReasons.ButtonAttack, why);
            return true;
        }

        /// <summary>True = actor i may not cast (gliding, mid-slide).</summary>
        static bool MovementLocksCast(World w, int i)
        {
            MovementMovesState m = w.Moves.Movement;
            if (m.Gliding[i] != 0)
            {
                PlayerSeat.Refuse(w, i, PlayerReasons.ButtonAbility, PlayerReasons.RefusedGliding);
                return true;
            }
            return m.SlideStart[i] >= 0 || w.Tick < m.SlideRecoverUntil[i];
        }

        /// <summary>A living grounded actor's tick, before the dodge: true = moved here (a slide or its trip recovery), skip the
        /// walk update. Takes InputFrame.Dodge while the slide is armed (TRAVEL lit, or the short carry after an enemy came near).</summary>
        static bool MovementGroundStep(World w, int i, double dt)
        {
            MovementMovesState m = w.Moves.Movement;
            InputFrame inp = w.Inputs[i];
            if (m.SlideStart[i] >= 0)
            {
                int k = w.Tick - m.SlideStart[i];
                if (k >= 0 && k < Durations.T30(w.Config, Cfg(w).SlideTicks))
                {
                    inp.Dodge = false;   // the shared cooldown was just spent: a press mid-slide is spent with it
                    // a slide stopped by a wall, a ledge or the edge hands over to the walk in the same tick (no dead tick)
                    return MovementSlideTick(w, m, i, k, dt);
                }
                m.SlideStart[i] = -1;
            }
            if (w.Tick < m.SlideRecoverUntil[i])
            {
                inp.Dodge = false;
                w.Vx[i] = JsMath.F32(0);
                w.Vz[i] = JsMath.F32(0);
                w.Actors[i].Moving = false;
                return true;
            }
            if (MovementLanding(w, i))
            {
                // T2-7: the landing recovery after an evading jump: walking is allowed, the dodge is not
                if (inp.Dodge)
                {
                    inp.Dodge = false;
                    PlayerSeat.Refuse(w, i, PlayerReasons.ButtonDodge, PlayerReasons.RefusedLanding);
                }
                return false;
            }
            if (!inp.Dodge || !MovementSlideArmed(w, i) || !Dodge.On(w) || !Dodge.CanStart(w, i)) return false;
            return MovementSlideBegin(w, m, i, dt);
        }

        /// <summary>Walk-speed multiplier: the TRAVEL ramp to TravelSpeedMul over TravelRampTicks. Exactly 1.0 when not travelling.</summary>
        static double MovementSpeedScale(World w, int i)
        {
            MovesConfig c = Cfg(w);
            if (!On(w, c.Travel)) return 1.0;
            MovementMovesState m = w.Moves.Movement;
            if (m.Travel[i] == 0) return 1.0;
            int ramp = Durations.T30(w.Config, c.TravelRampTicks);
            int t = m.TravelTicks[i];
            if (ramp <= 0 || t >= ramp) return c.TravelSpeedMul;
            return 1.0 + (c.TravelSpeedMul - 1.0) * t / ramp;
        }

        /// <summary>First thing of an airborne tick (before AirTick++): the glide (start at the apex, steering, the held apex,
        /// and the landing guard that never lets a glide end over the void).</summary>
        static void MovementAirTick(World w, int i, double dt)
        {
            MovesConfig c = Cfg(w);
            if (!On(w, c.Glide)) return;
            MovementMovesState m = w.Moves.Movement;
            Actor a = w.Actors[i];
            if (m.Gliding[i] == 0)
            {
                if (!MovementGlideClass(w, a) || a.AirTick >= a.AirLen) return;
                if (a.AirTick < Durations.T30(w.Config, c.GlideFromAirTick)) return;
                bool want = m.GlidePlanned[i] != 0 || w.Inputs[i].JumpHeld && w.Tick >= m.GlideReady[i] && MovementGlideFree(a);
                if (!want) return;
                m.Gliding[i] = 1;
                m.GlideTicks[i] = 0;
                m.GlideHit[i] = 0;
                World.PushEvent(w, SimEventType.Glide, i, -1, w.X[i], w.Z[i], 1);
            }
            int maxTicks = Durations.T30(w.Config, c.GlideExtraTicks);
            int left = maxTicks - m.GlideTicks[i];
            int descent = a.AirLen - a.AirTick;
            double vx = w.Vx[i], vz = w.Vz[i];
            double x = w.X[i], z = w.Z[i];
            bool hold = w.Inputs[i].JumpHeld && m.GlideHit[i] == 0 && left > 0;
            bool endClear = MovementDescentClear(w, i, x, z, vx, vz, descent, dt, out double ex, out double ez);
            if (left <= 0 || !hold && endClear)
            {
                MovementGlideEnd(w, m, i, m.GlideHit[i]);
                if (endClear)
                {
                    a.AirToX = ex;
                    a.AirToZ = ez;
                }
                return;
            }
            // continue the glide this tick: steer toward the move input at GlideSpeedMul x walk, but only along a line whose
            // glide-then-descent still lands on walkable ground within the glide time left (else keep the line it had)
            double speed = c.GlideSpeedMul * w.Config.Class(a.Cls).MoveSpeed;
            double cx = vx, cz = vz;
            InputFrame inp = w.Inputs[i];
            double ml = System.Math.Sqrt(inp.MoveX * inp.MoveX + inp.MoveZ * inp.MoveZ);
            if (hold && ml > 1e-4)
            {
                cx = JsMath.F32(inp.MoveX / ml * speed);
                cz = JsMath.F32(inp.MoveZ / ml * speed);
            }
            else if (hold || m.GlidePlanned[i] != 0)
            {
                double vl = System.Math.Sqrt(vx * vx + vz * vz);
                if (vl > 1e-6)
                {
                    cx = JsMath.F32(vx / vl * speed);
                    cz = JsMath.F32(vz / vl * speed);
                }
            }
            double gx, gz;
            if (m.GlideHit[i] == 0 && MovementGlideSafe(w, i, x, z, cx, cz, left, descent, dt, out gx, out gz)) { vx = cx; vz = cz; }
            else if (MovementGlideSafe(w, i, x, z, vx, vz, left, descent, dt, out gx, out gz)) { }
            else if (endClear)
            {
                MovementGlideEnd(w, m, i, m.GlideHit[i]);
                a.AirToX = ex;
                a.AirToZ = ez;
                return;
            }
            else { gx = a.AirToX; gz = a.AirToZ; }   // the grid changed under the plan: keep the line (LandJump's guard is the net)
            w.Vx[i] = JsMath.F32(vx);
            w.Vz[i] = JsMath.F32(vz);
            a.AirToX = gx;
            a.AirToZ = gz;
            a.AirTick--;   // FlyTick's AirTick++ follows: the flight clock holds at the apex while gliding
            m.GlideTicks[i]++;
        }

        /// <summary>True = refuse a jump start (the relock after an evading jump; it covers the landing recovery too).</summary>
        static bool MovementJumpLocked(World w, int id)
        {
            if (!On(w, Cfg(w).JumpEvade)) return false;
            if (w.Tick >= w.Moves.Movement.JumpRelockUntil[id]) return false;
            PlayerSeat.Refuse(w, id, PlayerReasons.ButtonJump, PlayerReasons.RefusedLanding);
            return true;
        }

        /// <summary>A jump landed: the landing recovery and relock after an evading jump; a glide that is still on ends.</summary>
        static void MovementLanded(World w, int id)
        {
            MovesConfig c = Cfg(w);
            MovementMovesState m = w.Moves.Movement;
            if (m.Gliding[id] != 0) MovementGlideEnd(w, m, id, 0);
            m.GlidePlanned[id] = 0;
            if (On(w, c.JumpEvade) && m.AirEvaded[id] != 0)
            {
                m.LandRecoverUntil[id] = w.Tick + 1 + Durations.T30(w.Config, c.JumpEvadeLandingTicks);
                m.JumpRelockUntil[id] = w.Tick + 1 + MovementTicks(w, c.JumpEvadeRelockSeconds);
            }
            m.AirEvaded[id] = 0;
        }

        /// <summary>HP was taken: a hit ends travel (both sides, and blocks it for TravelNoCombatSeconds) and ends a glide.</summary>
        static void MovementDamaged(World w, int target, int source, DamageCause cause, double dmg)
        {
            MovesConfig c = Cfg(w);
            MovementMovesState m = w.Moves.Movement;
            if (On(w, c.Travel) || On(w, c.Slide))
            {
                MovementCombat(w, m, target);
                if (source >= 0 && source < w.ActorCount) MovementCombat(w, m, source);
            }
            if (On(w, c.Glide) && m.Gliding[target] != 0)
            {
                m.GlideHit[target] = 1;   // the body drops at the first tick its descent lands clear (never over the void)
                m.GlidePlanned[target] = 0;
            }
        }

        // ================================================================== T2-4 travel

        static void MovementTravelUpdate(World w, MovesConfig c, MovementMovesState m, int i)
        {
            Actor a = w.Actors[i];
            InputFrame f = w.Inputs[i];
            bool lit = m.Travel[i] != 0;
            bool press = f.Attack || f.Ability || f.Interact || f.Guard || f.Terraform;
            bool enemy = false;
            bool ok = a.Alive && a.StandUp <= 0 && !press
                && w.Tick - m.CombatTick[i] >= MovementTicks(w, c.TravelNoCombatSeconds)
                && a.Carry != CarryKind.Corruption
                && w.Moves.MoveHeldTicks[i] > 0
                && a.HitStunTicks <= 0 && a.KVx == 0 && a.KVz == 0;
            if (ok)
            {
                enemy = MovementEnemyNear(w, i, c.TravelEnemyRadius);
                ok = !enemy;
            }
            if (press || !a.Alive) m.SlideCarryUntil[i] = -1;
            if (lit)
            {
                if (ok)
                {
                    if (m.TravelTicks[i] < int.MaxValue / 2) m.TravelTicks[i]++;
                    return;
                }
                m.Travel[i] = 0;
                m.TravelTicks[i] = 0;
                m.SlideCarryUntil[i] = enemy && On(w, c.Slide) ? w.Tick + MovementSlideCarryTicks : -1;
                World.PushEvent(w, SimEventType.TravelChanged, i, -1, w.X[i], w.Z[i], 0);
                return;
            }
            if (!ok || w.Moves.MoveHeldTicks[i] < Durations.T30(w.Config, c.TravelAfterMoveTicks)) return;
            if (a.AirLen > 0 || m.SlideStart[i] >= 0 || Dodge.Active(w, i)) return;
            m.Travel[i] = 1;
            m.TravelTicks[i] = 1;
            m.SlideCarryUntil[i] = -1;
            World.PushEvent(w, SimEventType.TravelChanged, i, -1, w.X[i], w.Z[i], 1);
        }

        /// <summary>A hit taken or dealt: travel ends at once and the slide carry is gone.</summary>
        static void MovementCombat(World w, MovementMovesState m, int id)
        {
            m.CombatTick[id] = w.Tick;
            m.SlideCarryUntil[id] = -1;
            if (m.Travel[id] == 0) return;
            m.Travel[id] = 0;
            m.TravelTicks[id] = 0;
            World.PushEvent(w, SimEventType.TravelChanged, id, -1, w.X[id], w.Z[id], 0);
        }

        /// <summary>A living enemy body (or the hatched Kaiju hostile to i, measured to its body edge) within r of actor i.</summary>
        static bool MovementEnemyNear(World w, int i, double r)
        {
            TeamId team = w.Actors[i].Team;
            double x = w.X[i], z = w.Z[i], r2 = r * r;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor o = w.Actors[j];
                if (j == i || !o.Alive || o.Team == team) continue;
                double dx = w.X[j] - x, dz = w.Z[j] - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            Kaiju k = w.Kaiju;
            if (k != null && k.State != KaijuState.Egg && Kaiju.KaijuHostileTo(w, i))
            {
                double kr = r + w.Config.Kaiju.Radius, dx = k.X - x, dz = k.Z - z;
                if (dx * dx + dz * dz <= kr * kr) return true;
            }
            return false;
        }

        // ================================================================== T2-5 slide

        static bool MovementSlideBegin(World w, MovementMovesState m, int i, double dt)
        {
            MovesConfig c = Cfg(w);
            Actor a = w.Actors[i];
            InputFrame inp = w.Inputs[i];
            inp.Dodge = false;   // the press is the slide's (the dodge never sees it)
            double mx = inp.MoveX, mz = inp.MoveZ, ml = System.Math.Sqrt(mx * mx + mz * mz), dx, dz;
            if (ml > w.Config.Depth.Dodge.MinMoveInput) { dx = mx / ml; dz = mz / ml; }
            else { dx = JsMath.Cos(a.Facing); dz = JsMath.Sin(a.Facing); }
            if (!MovementSlideStepClear(w, i, dx, dz, out _, out _))
            {
                PlayerSeat.Refuse(w, i, PlayerReasons.ButtonDodge, PlayerReasons.RefusedBlockedPlace);
                return false;
            }
            m.SlideStart[i] = w.Tick;
            m.SlideDirX[i] = dx;
            m.SlideDirZ[i] = dz;
            m.SlideCarryUntil[i] = -1;
            w.Dodges.Ready[i] = w.Tick + Durations.T30(w.Config, w.Config.Depth.Dodge.CooldownTicks);   // the dodge's cooldown, shared
            a.Facing = JsMath.Atan2(dz, dx);
            World.PushEvent(w, SimEventType.Slide, i, -1, w.X[i], w.Z[i], 0);
            return MovementSlideTick(w, m, i, 0, dt);
        }

        /// <summary>The slide's one-tick step from where actor i stands: false = a wall, no ground or the Kaiju's body (it ends).</summary>
        static bool MovementSlideStepClear(World w, int i, double dx, double dz, out double nx, out double nz)
        {
            MovesConfig c = Cfg(w);
            int ticks = System.Math.Max(1, Durations.T30(w.Config, c.SlideTicks));
            double step = c.SlideDistance / ticks;
            Collide.SweepActor(w, i, w.X[i], w.Z[i], dx * step, dz * step, false, out nx, out nz);
            nx = JsMath.F32(nx);
            nz = JsMath.F32(nz);
            double along = (nx - w.X[i]) * dx + (nz - w.Z[i]) * dz;
            if (!(along > step * 0.25) || !Collide.FootprintClear(w, i, nx, nz)) return false;
            Kaiju k = w.Kaiju;
            if (k != null && k.State != KaijuState.Egg && Phys.On(w, w.Config.Physics.SolidKaiju))
            {
                double rr = w.Config.Physics.KaijuBodyRadius + Collide.ActorRadius(w, i);
                double ex = nx - k.X, ez = nz - k.Z, cx = w.X[i] - k.X, cz = w.Z[i] - k.Z, e2 = ex * ex + ez * ez;
                if (e2 < rr * rr && e2 < cx * cx + cz * cz) return false;
            }
            return true;
        }

        /// <summary>One slide tick; false = the slide stopped before moving (a wall, a ledge, the edge): the walk takes this tick.</summary>
        static bool MovementSlideTick(World w, MovementMovesState m, int i, int k, double dt)
        {
            MovesConfig c = Cfg(w);
            Actor a = w.Actors[i];
            double dx = m.SlideDirX[i], dz = m.SlideDirZ[i];
            int ticks = System.Math.Max(1, Durations.T30(w.Config, c.SlideTicks));
            double walk = w.Config.Class(a.Cls).MoveSpeed * MovementSpeedScale(w, i);
            if (!MovementSlideStepClear(w, i, dx, dz, out _, out _))
            {
                // a wall, a ledge or the edge: the slide ends where it stands (never into the void) at the walk speed along it,
                // and the walk update runs this same tick (it climbs, slides along the wall or stops at the edge as walking does)
                m.SlideStart[i] = -1;
                w.Vx[i] = JsMath.F32(dx * walk);
                w.Vz[i] = JsMath.F32(dz * walk);
                return false;
            }
            double step = c.SlideDistance / ticks;
            double x0 = w.X[i], z0 = w.Z[i];
            Collide.MoveActor(w, i, dx * step, dz * step, false);
            double vx = (w.X[i] - x0) / dt, vz = (w.Z[i] - z0) / dt;
            a.Moving = true;
            a.UnsupportedTicks = 0;
            int victim = MovementSlideVictim(w, i);
            if (victim >= 0)
            {
                // SLIDE TRIP: 8 damage, a 10-tick stagger and a 0.5 u knock along the slide; the slider stops for 6 ticks
                double dmg = Damage.ApplyHit(w, victim, c.SlideTripDamage, i, w.X[i], w.Z[i], DamageCause.Melee, dx, dz,
                    c.SlideTripKnock, Durations.T30(w.Config, c.SlideTripStaggerTicks));
                m.SlideStart[i] = -1;
                m.SlideRecoverUntil[i] = w.Tick + 1 + Durations.T30(w.Config, c.SlideTripRecoveryTicks);
                w.Vx[i] = JsMath.F32(0);
                w.Vz[i] = JsMath.F32(0);
                World.PushEvent(w, SimEventType.Slide, i, victim, w.X[victim], w.Z[victim], dmg);
                return true;
            }
            if (k >= ticks - 1)
            {
                // the last slide tick: leave at the walk speed along the slide (travel's speed when it is still lit)
                m.SlideStart[i] = -1;
                vx = dx * walk;
                vz = dz * walk;
            }
            w.Vx[i] = JsMath.F32(vx);
            w.Vz[i] = JsMath.F32(vz);
            return true;
        }

        /// <summary>The nearest living grounded enemy body actor i's body touches (lowest id on a tie), or -1.</summary>
        static int MovementSlideVictim(World w, int i)
        {
            TeamId team = w.Actors[i].Team;
            double ri = Collide.ActorRadius(w, i), best = double.MaxValue;
            int pick = -1;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor o = w.Actors[j];
                if (j == i || !o.Alive || o.Team == team || o.AirLen > 0) continue;
                double rr = ri + Collide.ActorRadius(w, j) + MovementTripSlack;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i], d2 = dx * dx + dz * dz;
                if (d2 <= rr * rr && d2 < best)
                {
                    best = d2;
                    pick = j;
                }
            }
            return pick;
        }

        // ================================================================== T2-6 glide

        static bool MovementGlideClass(World w, Actor a)
        {
            bool[] g = Cfg(w).GlideClasses;
            int ci = (int)a.Cls;
            return g != null && ci >= 0 && ci < g.Length && g[ci];
        }

        /// <summary>No glide on a knocked or staggered body (only the Kaiju throws fighters, and it never launches a glide).</summary>
        static bool MovementGlideFree(Actor a) => a.KVx == 0 && a.KVz == 0 && a.HitStunTicks <= 0;

        static void MovementGlideEnd(World w, MovementMovesState m, int i, int byHit)
        {
            m.Gliding[i] = 0;
            m.GlidePlanned[i] = 0;
            m.GlideHit[i] = 0;
            m.GlideReady[i] = w.Tick + MovementTicks(w, Cfg(w).GlideCooldownSeconds);
            World.PushEvent(w, SimEventType.Glide, i, -1, w.X[i], w.Z[i], 0, null, DamageCause.None, byHit);
        }

        /// <summary>The flight from (x, z) at (vx, vz) for ticks more air ticks ends on a clear footprint; (ex, ez) = the end.
        /// The same sweep and Float32 rounding as FlyTick, so the planned landing IS the landing.</summary>
        static bool MovementDescentClear(World w, int i, double x, double z, double vx, double vz, int ticks, double dt, out double ex, out double ez)
        {
            double sx = vx * dt, sz = vz * dt;
            for (int k = 0; k < ticks; k++)
            {
                if (sx == 0 && sz == 0) break;
                Collide.SweepActor(w, i, x, z, sx, sz, true, out x, out z);
                x = JsMath.F32(x);
                z = JsMath.F32(z);
            }
            ex = x;
            ez = z;
            return Collide.FootprintClear(w, i, x, z);
        }

        /// <summary>Gliding this tick at (vx, vz) keeps a safe way down: some g in 1..left glide ticks, then the descent, lands on
        /// a clear footprint. (gx, gz) = the first such landing (the view's AirTo).</summary>
        static bool MovementGlideSafe(World w, int i, double x, double z, double vx, double vz, int left, int descent, double dt, out double gx, out double gz)
        {
            double sx = vx * dt, sz = vz * dt;
            for (int g = 1; g <= left; g++)
            {
                if (sx != 0 || sz != 0)
                {
                    Collide.SweepActor(w, i, x, z, sx, sz, true, out x, out z);
                    x = JsMath.F32(x);
                    z = JsMath.F32(z);
                }
                if (MovementDescentClear(w, i, x, z, vx, vz, descent, dt, out gx, out gz)) return true;
                if (sx == 0 && sz == 0) break;   // hovering in place: every g lands on the same spot
            }
            gx = x;
            gz = z;
            return false;
        }

        /// <summary>
        /// Jump.StartJump (lane 3's own file): a Healer holding Jump at the takeoff whose plain jump has no clear landing at full
        /// length (shortened or refused) may take off with a glide plan instead: the ascent to the apex at the takeoff velocity,
        /// then up to GlideExtraTicks of glide at GlideSpeedMul x walk along that line, then the descent. The shortest such glide
        /// that lands on walkable ground is the plan; none = the plain plan stands (the landing guard, as JumpRefused works).
        /// Returns the air ticks to use (full length) and the landing, or false.
        /// </summary>
        internal static bool MovementGlidePlan(World w, int id, double vx, double vz, double dt, out int airLen, out double toX, out double toZ)
        {
            airLen = 0;
            toX = toZ = 0;
            if (w.Moves == null) return false;
            MovesConfig c = Cfg(w);
            if (!On(w, c.Glide)) return false;
            MovementMovesState m = w.Moves.Movement;
            Actor a = w.Actors[id];
            if (!MovementGlideClass(w, a) || !w.Inputs[id].JumpHeld || w.Tick < m.GlideReady[id] || !MovementGlideFree(a)) return false;
            int air = Jump.AirTicks(w.Config);
            int apex = Durations.T30(w.Config, c.GlideFromAirTick);
            if (apex < 1 || apex >= air) return false;
            double vl = System.Math.Sqrt(vx * vx + vz * vz);
            double dx, dz;
            if (vl > 1e-6) { dx = vx / vl; dz = vz / vl; }
            else { dx = JsMath.Cos(a.Facing); dz = JsMath.Sin(a.Facing); }
            double x = w.X[id], z = w.Z[id], sx = vx * dt, sz = vz * dt;
            for (int k = 0; k < apex; k++)
            {
                if (sx == 0 && sz == 0) break;
                Collide.SweepActor(w, id, x, z, sx, sz, true, out x, out z);
                x = JsMath.F32(x);
                z = JsMath.F32(z);
            }
            double speed = c.GlideSpeedMul * w.Config.Class(a.Cls).MoveSpeed;
            double gvx = JsMath.F32(dx * speed), gvz = JsMath.F32(dz * speed);
            int left = Durations.T30(w.Config, c.GlideExtraTicks);
            if (!MovementGlideSafe(w, id, x, z, gvx, gvz, left, air - apex, dt, out double gx, out double gz)) return false;
            m.GlidePlanned[id] = 1;
            airLen = air;
            toX = gx;
            toZ = gz;
            return true;
        }

        /// <summary>
        /// Jump.StartJump (lane 3's own file), before the plan: a travelling actor takes off at its walk speed, not the travel
        /// speed (the plan's velocity; written to the actor only when the jump starts). Without this a x1.30 jump would carry 3.2 u instead of 2.5 u and open 2-cell void gaps to every class, which
        /// is the glide's job (Healer only). Identity unless TRAVEL is lit.
        /// </summary>
        internal static void MovementJumpTakeoff(World w, int id, ref double vx, ref double vz)
        {
            if (!MovementTravelling(w, id)) return;
            double s = MovementSpeedScale(w, id);
            if (!(s > 1.0)) return;
            vx = JsMath.F32(vx / s);
            vz = JsMath.F32(vz / s);
        }

        // ================================================================== T2-7 the evade event scan

        /// <summary>HitBlocked BlockedAir on an airborne actor since the last scan marks that flight as an evading one.</summary>
        static void MovementScanEvades(World w, MovementMovesState m)
        {
            int head = w.EventHead;
            int from = m.EventCursor[0];
            if (from < 0 || from > head) from = head;
            if (head - from > World.EventCapacity) from = head - World.EventCapacity;
            for (int e = from; e < head; e++)
            {
                SimEvent ev = w.Events[e % World.EventCapacity];
                if (ev.Type != SimEventType.HitBlocked || (int)ev.Value != PlayerReasons.BlockedAir) continue;
                if (ev.A >= 0 && ev.A < w.ActorCount && w.Actors[ev.A].AirLen > 0) m.AirEvaded[ev.A] = 1;
            }
            m.EventCursor[0] = head;
        }

        /// <summary>Seconds to whole sim ticks (JS round, as the Jump buffer and coyote windows).</summary>
        static int MovementTicks(World w, double seconds) => (int)JsMath.Round(seconds * w.Config.SimHz);
    }
}

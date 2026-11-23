using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Movement system (browser movement.ts): input to velocity to grid collision, then soft separation. Dead actors do not move.
    /// <para>CM-32: InputFrame.Jump (cleared here when read) starts a jump (Jump.cs). Airborne actors keep their takeoff
    /// velocity (no air control), move with the airborne collision rules (in-map VOID passes) and land at the end of tick
    /// AirLen. DG-02: the press is buffered for Jump.BufferTicks ticks and coyote time counts unsupported ticks. DG-09: past the
    /// coyote window with no ground under its centre, the actor falls and climbs out (Terraform.FallIfUnsupported).</para>
    /// </summary>
    public static class Movement
    {
        private const double AimDeadzone = 0.05;
        private const double MoveDeadzone = 1e-4;

        /// <summary>CM-21 (1): velocity moves linearly toward inputDir * speed each step (accel / brake limits); the actor moves by velocity * dt.</summary>
        public static void MovementSystem(World w, double dt)
        {
            GameConfig cfg = w.Config;
            int bufferTicks = Jump.BufferTicks(cfg);
            bool feel = CombatFeel.On(w); // b15 combat feel
            if (feel) CombatFeel.BeginMovement(w);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                InputFrame inp = w.Inputs[i];
                // CM-32: the jump flag is one-shot: read (and cleared) every step, so a stored true never repeats a jump.
                // DG-02 buffer: a press stays wanted for this tick and the next bufferTicks ticks.
                if (inp.Jump) a.JumpBuffer = bufferTicks + 1;
                inp.Jump = false;
                bool wantJump = a.JumpBuffer > 0;
                if (wantJump) a.JumpBuffer--;
                if (!a.Alive || a.StandUp > 0)
                {
                    a.Moving = false;
                    w.Vx[i] = JsMath.F32(0);
                    w.Vz[i] = JsMath.F32(0);
                    Jump.ClearJump(a);
                    a.UnsupportedTicks = 0;
                    if (!a.Alive) a.JumpBuffer = 0;
                    if (a.KVx != 0 || a.KVz != 0 || a.HitStunTicks != 0) Knock.Clear(a); // DG-70
                    continue;
                }
                if (a.AirLen > 0)
                {
                    FlyTick(w, i, dt);
                    continue;
                }
                // DG-02 coyote time: count the ticks this grounded actor has stood where its footprint no longer fits.
                if (Collide.FootprintClear(w, i, w.X[i], w.Z[i])) a.UnsupportedTicks = 0;
                else a.UnsupportedTicks++;
                // DG-09 fall rule (U-053): ground gone under the centre for longer than the coyote window = fall in, climb out.
                if (Terraform.FallIfUnsupported(w, i)) continue;
                if (MovesHooks.GroundStep(w, i, dt)) continue;   // mv-hook: SLIDE (lane 3), before the dodge takes the press
                if (B13Hooks.DodgeStep(w, i, dt)) continue;   // b13-hook
                double mx = inp.MoveX;
                double mz = inp.MoveZ;
                // DG-70: a staggered actor ignores its input (no move, no jump) while the stun counts down.
                bool stunned = a.HitStunTicks > 0;
                if (stunned)
                {
                    a.HitStunTicks--;
                    mx = 0;
                    mz = 0;
                    wantJump = false;
                }
                double len2 = mx * mx + mz * mz;
                bool moving = len2 > MoveDeadzone * MoveDeadzone;
                if (len2 > 1)
                {
                    double inv = 1 / System.Math.Sqrt(len2);
                    mx *= inv;
                    mz *= inv;
                }
                double baseSpeed = cfg.Class(a.Cls).MoveSpeed;
                double tx = 0;
                double tz = 0;
                if (moving)
                {
                    double speed = baseSpeed;
                    if (a.SlowTimer > 0) speed *= cfg.Abilities.FarmerRoots.SlowMultiplier;
                    if (a.Cls == ClassId.Warrior && a.AbilityActive > 0) speed *= cfg.Abilities.WarriorShield.MoveSpeedMultiplier;
                    // DG-69: slowed through the wind-up and the recovery (no skating swings).
                    int slowLen = a.SwingWindup + (cfg.Physics.SwingSlowsRecovery ? a.SwingRecovery : 0);
                    // b15 combat feel: its own wind-up / recovery / stagger-slow / ladder scale replaces SwingMoveMultiplier.
                    if (feel) speed *= CombatFeel.SpeedScale(w, i);
                    else if (slowLen > 0 && w.Tick > a.SwingTick && w.Tick <= a.SwingTick + slowLen)
                        speed *= cfg.Physics.SwingMoveMultiplier;
                    speed *= Pickups.SpeedScale(w, i); // U-066 Haste (exactly 1 without it)
                    speed *= B13Hooks.SpeedScale(w, i);   // b13-hook
                    speed *= TerrainHeight.SpeedScale(w, i); // b15: the short slow after a level change (exactly 1 without the rule)
                    speed *= MovesHooks.SpeedScale(w, i);   // mv-hook: travel (lane 3) x guard (lane 2) x charge (lane 4); exactly 1 when off
                    tx = mx * speed;
                    tz = mz * speed;
                }
                double vx = w.Vx[i];
                double vz = w.Vz[i];
                bool braking = !moving || vx * tx + vz * tz < 0;
                double maxDv = (baseSpeed * dt) / (braking ? cfg.Movement.BrakeSeconds : cfg.Movement.AccelSeconds);
                double dvx = tx - vx;
                double dvz = tz - vz;
                double dv2 = dvx * dvx + dvz * dvz;
                if (dv2 <= maxDv * maxDv)
                {
                    vx = tx;
                    vz = tz;
                }
                else
                {
                    double k = maxDv / System.Math.Sqrt(dv2);
                    vx += dvx * k;
                    vz += dvz * k;
                }
                w.Vx[i] = JsMath.F32(vx);
                w.Vz[i] = JsMath.F32(vz);
                // CM-32: a jump takes off with this step's velocity; tick 1 of the flight is this step's move.
                if (wantJump && Jump.StartJump(w, i, dt))
                {
                    a.JumpBuffer = 0;
                    FlyTick(w, i, dt);
                    continue;
                }
                // b16 [11]: a press the sim refused for its whole buffer (no clear landing, or past the coyote window) tells the view
                // once, so a refused jump at the island edge is not silent. Value: 2 = past the coyote window, 1 = no landing.
                if (wantJump && a.JumpBuffer == 0 && PhysicsFeel.On(w) && PhysicsFeel.Cfg(w).JumpRefusedEvent)
                    World.PushEvent(w, SimEventType.JumpRefused, i, -1, w.X[i], w.Z[i], a.UnsupportedTicks > Jump.CoyoteTicks(cfg) ? 2 : 1);
                if (vx != 0 || vz != 0)
                {
                    double sx = vx * dt, sz = vz * dt;
                    KaijuWalkSlide(w, i, ref sx, ref sz);   // px DM: walk round the Kaiju's body, not into it
                    Collide.MoveActor(w, i, sx, sz, false, true);
                }
                Knock.Step(w, i, dt); // DG-70: the knock velocity on top, through the same wall-sliding mover
                a.Moving = moving;
                if (stunned) continue;
                // Facing: toward a real aim point (from the post-move position), else toward the move direction.
                if (w.HasAim[i] == 1)
                {
                    double ax = inp.AimX - w.X[i];
                    double az = inp.AimZ - w.Z[i];
                    if (ax * ax + az * az > AimDeadzone * AimDeadzone) a.Facing = JsMath.Atan2(az, ax);
                    else if (moving) a.Facing = JsMath.Atan2(mz, mx);
                }
                else if (moving)
                {
                    a.Facing = JsMath.Atan2(mz, mx);
                }
            }
            Collide.SeparateActors(w);
            TerrainHeight.MovementEnd(w); // b15 terrain height: levels, slows, climb counters (no-op without the rule)
        }

        /// <summary>px DM (PhysicsFeelConfig.KaijuWalkSlide, Full rules): a walker in contact with the hatched solid Kaiju loses
        /// the into-body share of its walking step (slides along the body's tangent).</summary>
        private static void KaijuWalkSlide(World w, int i, ref double sx, ref double sz)
        {
            if (!PhysicsFeel.On(w) || !PhysicsFeel.Cfg(w).KaijuWalkSlide) return;
            Kaiju k = w.Kaiju;
            if (k == null || k.State == KaijuState.Egg || !Phys.On(w, w.Config.Physics.SolidKaiju)) return;
            double dx = w.X[i] - k.X, dz = w.Z[i] - k.Z, d2 = dx * dx + dz * dz;
            double rr = w.Config.Physics.KaijuBodyRadius + Collide.ActorRadius(w, i) + PhysicsFeel.Cfg(w).KaijuWalkSlideSlack;
            if (d2 >= rr * rr || d2 < 1e-12) return;
            double d = System.Math.Sqrt(d2), ux = dx / d, uz = dz / d, into = sx * ux + sz * uz;
            if (into >= 0) return;
            sx -= ux * into;
            sz -= uz * into;
        }

        /// <summary>
        /// CM-32: one airborne tick of actor i: the locked takeoff velocity moves it under the airborne collision rules,
        /// facing follows a real aim point only (move input is ignored mid-air), and the last tick lands it (LandJump).
        /// </summary>
        private static void FlyTick(World w, int i, double dt)
        {
            Actor a = w.Actors[i];
            MovesHooks.AirTick(w, i, dt);   // mv-hook: GLIDE (lane 3)
            double vx = w.Vx[i];
            double vz = w.Vz[i];
            a.AirTick++;
            if (vx != 0 || vz != 0) Collide.MoveActor(w, i, vx * dt, vz * dt, true);
            a.Moving = vx != 0 || vz != 0;
            if (w.HasAim[i] == 1)
            {
                InputFrame inp = w.Inputs[i];
                double ax = inp.AimX - w.X[i];
                double az = inp.AimZ - w.Z[i];
                if (ax * ax + az * az > AimDeadzone * AimDeadzone) a.Facing = JsMath.Atan2(az, ax);
            }
            if (a.AirTick >= a.AirLen) Jump.LandJump(w, i);
        }
    }
}

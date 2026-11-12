using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Movement system (browser movement.ts): input to velocity to grid collision, then soft separation. Dead actors do not move.</summary>
    public static class Movement
    {
        private const double AimDeadzone = 0.05;
        private const double MoveDeadzone = 1e-4;

        /// <summary>CM-21 (1): velocity moves linearly toward inputDir * speed each step (accel / brake limits); the actor moves by velocity * dt.</summary>
        public static void MovementSystem(World w, double dt)
        {
            GameConfig cfg = w.Config;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                InputFrame inp = w.Inputs[i];
                // ITEM CM-32: added by its owner (jump read/clear)
                if (!a.Alive || a.StandUp > 0)
                {
                    a.Moving = false;
                    w.Vx[i] = JsMath.F32(0);
                    w.Vz[i] = JsMath.F32(0);
                    // ITEM CM-32: added by its owner (clearJump on a dead or getting-up actor)
                    continue;
                }
                // ITEM CM-32: added by its owner (FlyTick airborne step)
                double mx = inp.MoveX;
                double mz = inp.MoveZ;
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
                // ITEM CM-32: added by its owner (jump takeoff)
                if (vx != 0 || vz != 0) Collide.MoveActor(w, i, vx * dt, vz * dt, false);
                a.Moving = moving;
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
        }

        // ITEM CM-32: added by its owner (FlyTick airborne step)
    }
}

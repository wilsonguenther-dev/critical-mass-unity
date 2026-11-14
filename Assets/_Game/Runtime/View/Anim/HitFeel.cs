using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// feat/hit-feel-1 (owner, voice message 2026-11-14: "the arm actually swings ... they can see the physical hit and a
    /// reaction, the pain, the groan"). View-only rules for the swing and the hit, all pure and read-only on the world:
    /// <list type="bullet">
    /// <item>SwingFacing: for the swing window the drawn body turns to the enemy it is swinging at (the nearest living enemy
    /// inside the weapon's reach and arc around the sim's SwingFacing), else to SwingFacing itself.</item>
    /// <item>Lunge: a short step toward the swing that peaks on the damage tick and is absorbed after it (the drawn body
    /// only; the sim position never moves).</item>
    /// <item>Nudge: a small camera nudge for hits on or by the local player (through CameraShake, so the shake setting
    /// and reduce-motion still decide).</item>
    /// <item>HoldWallSeconds: hit-stop per tier, 40 to 70 ms of view time on both fighters, 0 with reduce motion.</item>
    /// </list>
    /// No allocation: everything is arithmetic on arrays the sim already has.
    /// </summary>
    public static class HitFeel
    {
        /// <summary>Peak lunge (world units) and the share of the gap to the target it may use.</summary>
        public const double LungeMax = 0.16, LungeGapShare = 0.4;
        /// <summary>The lunge's anticipation (a small lean back early in the wind-up) as a share of the peak.</summary>
        public const double LungeBack = 0.2;
        /// <summary>Sim seconds the lunge takes to come back after contact.</summary>
        public const double LungeReturn = 0.2;
        /// <summary>Sim seconds after contact the body keeps facing the target (the follow-through).</summary>
        public const double FacingHold = 0.18;
        /// <summary>Extra reach (u) and extra arc (degrees each side) when picking the enemy to face.</summary>
        public const double FaceReachSlack = 0.6, FaceArcSlackDeg = 25;
        /// <summary>fix/hit-feel-3: the most the swing may turn the body off the sim facing (degrees).</summary>
        public const double FaceConeDeg = 20;
        /// <summary>
        /// fix/hit-feel-3: the most the drawn body may sit off its sim position horizontally (step-in, flinch and stagger
        /// together), so the ring and the placement test (0.35 u) still hold.
        /// </summary>
        public const float MaxViewOffset = 0.25f;

        /// <summary>Clamps the horizontal part of a view offset to MaxViewOffset (vertical untouched; pure).</summary>
        public static Vector3 ClampOffset(Vector3 v)
        {
            float h = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            if (h <= MaxViewOffset) return v;
            float k = MaxViewOffset / h;
            return new Vector3(v.x * k, v.y, v.z * k);
        }
        /// <summary>Hit-stop in wall seconds per tier (light, heavy, critical or finishing).</summary>
        public const double HoldLight = 0.045, HoldHeavy = 0.06, HoldCritical = 0.07;
        /// <summary>Camera nudge (trauma before the shake setting's scale) for a hit on, and by, the local player.</summary>
        public const float NudgeOnLocal = 0.45f, NudgeByLocal = 0.3f;   // feat/hit-feel-2: trauma is squared (0.45 at scale 0.7 = 0.032 u)

        // ---------------------------------------------------------------- feat/hit-feel-2

        /// <summary>
        /// Weapon reach at the contact frame per class (u from the body centre along the swing: hand forward plus the
        /// weapon), ESTIMATED from the rigs and props; HitFeelCapture logs the measured hand reach per class to replace
        /// these. Sim ranges: Warrior 1.7, Farmer 1.4, Miner 1.5, Builder 1.5, Demolitions 1.3, Healer 1.2.
        /// </summary>
        public static double ViewReach(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Warrior: return 1.3;      // one-hand slice, sword
                case ClassId.Farmer: return 1.25;      // two-hand slice, long tool
                case ClassId.Miner: return 1.15;       // two-hand chop, pick
                case ClassId.Builder: return 1.05;     // one-hand chop, mallet 0.62
                case ClassId.Demolitions: return 1.05; // one-hand chop, pry bar 0.6
                case ClassId.Healer: return 0.8;       // one-hand swing, lantern hangs 0.42
                default: return 1.0;
            }
        }

        /// <summary>Most the step-in may move to close a reach gap (u), and the room always left between the two bodies.</summary>
        public const double ReachStepMax = 0.25, BodyGap = 0.05;   // fix/hit-feel-3: was 0.5 (models drawn 0.43 u off)

        /// <summary>
        /// Peak step-in (u) for a target `dist` away: enough that the weapon (reach) meets the target's body surface
        /// (dist - targetRadius), at least LungeMax for feel, at most ReachStepMax, and never into the target's body.
        /// </summary>
        public static double StepPeak(double dist, double reach, double selfRadius, double targetRadius)
        {
            double need = dist - targetRadius - reach;
            double want = Math.Min(ReachStepMax, Math.Max(LungeMax, need + 0.05));
            double room = dist - selfRadius - targetRadius - BodyGap;
            return Math.Max(0, Math.Min(want, room));
        }

        /// <summary>Sim seconds after the press at which the weapon smear starts: with the strike, AttackTiming.StrikeSeconds before contact.</summary>
        public static double SmearDelay(double windupSeconds) => Math.Max(0, windupSeconds - AttackTiming.StrikeSeconds);

        /// <summary>Damage share 0..1 for reactions (30 damage = full).</summary>
        static double Dmg01(double dmg) => Math.Max(0, Math.Min(1, dmg / 30));

        /// <summary>Flinch lean away from the blow at full envelope: 6 degrees plus 7 x damage share (6 to 13).</summary>
        public static double FlinchLeanDeg(double dmg) => 6 + 7 * Dmg01(dmg);   // fix/hit-feel-3: 6 to 13 (24 tipped the body box to 1.5x tall)

        /// <summary>Flinch shift along the blow at full envelope (u): 0.07 plus 0.07 x damage share.</summary>
        public static double FlinchShift(double dmg) => 0.07 + 0.07 * Dmg01(dmg);

        /// <summary>Damage from which a hit also staggers (a step back along the blow).</summary>
        public const double StaggerFrom = 12;

        /// <summary>
        /// Stagger step (u along the blow) `t` wall seconds after a hit of dmg: none below StaggerFrom; 0.18 to 0.32 u by
        /// damage, out over 0.08 s, held to 0.2 s, back by 0.45 s (the view absorbs it; the sim's shove is separate).
        /// </summary>
        public static double StaggerStep(double t, double dmg)
        {
            if (dmg < StaggerFrom || t < 0 || t >= 0.45) return 0;
            double amp = 0.1 + 0.06 * Dmg01(dmg);
            if (t < 0.08) { double s = t / 0.08; return amp * s * (2 - s); }
            if (t < 0.2) return amp;
            double r = (t - 0.2) / 0.25;
            return amp * (1 - r * r * (3 - 2 * r));
        }

        /// <summary>Hit-stop for a tier, wall seconds; 0 under reduce motion.</summary>
        public static double HoldWallSeconds(byte tier, bool reduceMotion) =>
            reduceMotion ? 0 : tier == 0 ? HoldLight : tier == 1 ? HoldHeavy : HoldCritical;

        /// <summary>Camera nudge for a hit: on the local player, by the local player, else 0.</summary>
        public static float Nudge(bool onLocal, bool byLocal) => onLocal ? NudgeOnLocal : byLocal ? NudgeByLocal : 0f;

        /// <summary>Seconds since actor a's current swing was pressed (fractional with alpha), or -1 when it has no swing.</summary>
        public static double SwingAge(Actor a, int tick, double alpha, double hz)
        {
            if (a == null || hz <= 0 || a.SwingTick <= 0 || a.SwingTick > tick) return -1;
            return (tick - a.SwingTick + alpha) / hz;
        }

        /// <summary>
        /// The lunge's share of its peak at `age` sim seconds into a swing whose wind-up is `windup` seconds: a small lean
        /// back early in the wind-up, forward to 1 on the contact tick (smoothstep), back to 0 over LungeReturn. 0 outside.
        /// </summary>
        public static double LungeShape(double age, double windup)
        {
            if (age < 0) return 0;
            if (windup <= 1e-9)
            {
                // No wind-up (classic rules): the step is all follow-through.
                double r0 = age / LungeReturn;
                return r0 >= 1 ? 0 : 1 - Smooth(r0);
            }
            if (age <= windup)
            {
                double s = age / windup;
                if (s < 0.35) return -LungeBack * Smooth(s / 0.35);
                return -LungeBack + (1 + LungeBack) * Smooth((s - 0.35) / 0.65);
            }
            double r = (age - windup) / LungeReturn;
            return r >= 1 ? 0 : 1 - Smooth(r);
        }

        static double Smooth(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            return x * x * (3 - 2 * x);
        }

        /// <summary>The enemy actor i is swinging at (nearest living enemy inside reach and arc around SwingFacing), or -1.</summary>
        public static int SwingTarget(World w, int i)
        {
            if (w == null || i < 0 || i >= w.ActorCount) return -1;
            Actor a = w.Actors[i];
            double reach = LightWeaponView.Range(w.Config, a.Cls) + FaceReachSlack;
            double half = (LightWeaponView.ArcDeg(w.Config, a.Cls) * 0.5 + FaceArcSlackDeg) * Math.PI / 180;
            double fx = Math.Cos(a.SwingFacing), fz = Math.Sin(a.SwingFacing), cosHalf = Math.Cos(Math.Min(Math.PI, half));
            int best = -1;
            double bestD2 = reach * reach;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor b = w.Actors[j];
                if (j == i || !b.Alive || b.Team == a.Team) continue;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i], d2 = dx * dx + dz * dz;
                if (d2 >= bestD2 || d2 < 1e-9) continue;
                double d = Math.Sqrt(d2);
                if ((dx * fx + dz * fz) / d < cosHalf) continue;
                best = j;
                bestD2 = d2;
            }
            return best;
        }

        /// <summary>
        /// The sim-radian facing the drawn body turns to: during a swing (press to contact plus FacingHold) the target it is
        /// swinging at, else SwingFacing; outside a swing, the sim's own Facing.
        /// </summary>
        public static double Facing(World w, int i, double alpha)
        {
            Actor a = w.Actors[i];
            double hz = w.Config.SimHz;
            double age = SwingAge(a, w.Tick, alpha, hz);
            double windup = Math.Max(0, a.SwingWindup) / hz;
            if (!a.Alive || age < 0 || age > windup + FacingHold) return a.Facing;
            // fix/hit-feel-3: turn only toward a target inside FaceConeDeg of the sim facing (the facing test allows 30), and
            // otherwise keep the sim facing (hit-feel-1 fell back to SwingFacing, which fought the movement facing).
            int t = SwingTarget(w, i);
            if (t < 0) return a.Facing;
            double want = Math.Atan2(w.Z[t] - w.Z[i], w.X[t] - w.X[i]);
            double d = want - a.Facing;
            d = Math.Atan2(Math.Sin(d), Math.Cos(d));
            return Math.Abs(d) <= FaceConeDeg * Math.PI / 180 ? want : a.Facing;
        }

        /// <summary>
        /// The drawn lunge offset (world, Unity XZ) for actor i now: toward its swing target (or along SwingFacing), the
        /// peak from StepPeak (the weapon reaches the target's body, never into it; feat/hit-feel-2). Zero outside a swing.
        /// </summary>
        public static Vector3 Lunge(World w, int i, double alpha)
        {
            if (w == null || i < 0 || i >= w.ActorCount) return Vector3.zero;
            Actor a = w.Actors[i];
            if (!a.Alive) return Vector3.zero;
            double hz = w.Config.SimHz;
            double age = SwingAge(a, w.Tick, alpha, hz);
            if (age < 0) return Vector3.zero;
            double windup = Math.Max(0, a.SwingWindup) / hz;
            double k = LungeShape(age, windup);
            if (k == 0) return Vector3.zero;
            double dirX = Math.Cos(a.SwingFacing), dirZ = Math.Sin(a.SwingFacing), peak = LungeMax;
            int t = SwingTarget(w, i);
            if (t >= 0)
            {
                double dx = w.X[t] - w.X[i], dz = w.Z[t] - w.Z[i], d = Math.Sqrt(dx * dx + dz * dz);
                if (d > 1e-6) { dirX = dx / d; dirZ = dz / d; }
                // feat/hit-feel-2: step in far enough that the weapon reaches the target's body (ViewReach), never into it.
                peak = StepPeak(d, ViewReach(a.Cls), w.Config.Class(a.Cls).Radius, w.Config.Class(w.Actors[t].Cls).Radius);
            }
            double m = peak * k;
            return SimSpace.ToWorld(dirX * m, 0, dirZ * m);
        }
    }
}

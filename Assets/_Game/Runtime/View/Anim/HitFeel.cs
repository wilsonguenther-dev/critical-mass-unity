using System;
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
        /// <summary>Hit-stop in wall seconds per tier (light, heavy, critical or finishing).</summary>
        public const double HoldLight = 0.045, HoldHeavy = 0.06, HoldCritical = 0.07;
        /// <summary>Camera nudge (trauma before the shake setting's scale) for a hit on, and by, the local player.</summary>
        public const float NudgeOnLocal = 0.18f, NudgeByLocal = 0.1f;

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
            int t = SwingTarget(w, i);
            return t >= 0 ? Math.Atan2(w.Z[t] - w.Z[i], w.X[t] - w.X[i]) : a.SwingFacing;
        }

        /// <summary>
        /// The drawn lunge offset (world, Unity XZ) for actor i now: toward its swing target (or along SwingFacing), the
        /// peak capped to LungeMax and to LungeGapShare of the gap left between the two bodies. Zero outside a swing.
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
                double gap = d - w.Config.Class(a.Cls).Radius - w.Config.Class(w.Actors[t].Cls).Radius;
                peak = Math.Min(peak, Math.Max(0, gap) * LungeGapShare);
            }
            double m = peak * k;
            return SimSpace.ToWorld(dirX * m, 0, dirZ * m);
        }
    }
}

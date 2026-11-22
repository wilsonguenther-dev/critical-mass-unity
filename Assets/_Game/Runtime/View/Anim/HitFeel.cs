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
        /// <summary>Hit-stop in wall seconds per tier (light, heavy, critical or finishing). px lane C: 60 / 80 / 110 ms
        /// (HitFeelTuning; was 45 / 60 / 70, measured 35 ms on the victim only).</summary>
        public const double HoldLight = HitFeelTuning.HoldLight, HoldHeavy = HitFeelTuning.HoldHeavy, HoldCritical = HitFeelTuning.HoldCritical;
        /// <summary>Camera nudge (trauma before the shake setting's scale) for a hit on, and by, the local player.</summary>
        public const float NudgeOnLocal = 0.45f, NudgeByLocal = 0.3f;   // feat/hit-feel-2: trauma is squared (0.45 at scale 0.7 = 0.032 u)

        // ---------------------------------------------------------------- feat/hit-feel-2

        /// <summary>
        /// Weapon reach at the contact frame per class (u from the body centre along the swing: hand forward plus the
        /// weapon), ESTIMATED from the rigs and props; HitFeelCapture logs the measured hand reach per class to replace
        /// these. Sim ranges: Warrior 1.7, Farmer 1.4, Miner 1.5, Builder 1.5, Demolitions 1.3, Healer 1.2.
        /// </summary>
        /// <summary>
        /// fix/hit-feel-5: the weapon TIP's horizontal reach from the root at the contact pose and the tip's height above
        /// the spine (the lean's lever), measured on the rig per class by ActorAnimator on the first swing (0: not yet).
        /// </summary>
        public static readonly double[] MeasuredReach = new double[16], MeasuredLever = new double[16];

        public static void SetMeasured(ClassId cls, double reach, double lever)
        {
            int k = (int)cls;
            if (k < 0 || k >= MeasuredReach.Length || !(reach > 0.05)) return;
            MeasuredReach[k] = reach;
            MeasuredLever[k] = lever;
        }

        /// <summary>The tip reach used by the step-in: measured on the rig, else the estimate table.</summary>
        public static double ViewReach(ClassId cls)
        {
            int k = (int)cls;
            return k >= 0 && k < MeasuredReach.Length && MeasuredReach[k] > 0 ? MeasuredReach[k] : EstimatedReach(cls);
        }

        /// <summary>hit-feel-2's estimate (u from the root to the tip), used until the rig is measured.</summary>
        public static double EstimatedReach(ClassId cls)
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
        public static double SmearDelay(double windupSeconds) => Math.Max(0, windupSeconds - AttackTiming.StrikeFor(windupSeconds));

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
        public static double HoldWallSeconds(byte tier, bool reduceMotion) => HitFeelTuning.HoldWallSeconds(tier, reduceMotion);

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
            // px lane C (2026-11-22): the sim's own hit shape (ResolveSwing: centre distance <= reach + target radius, angle off
            // the LIVE SwingFacing <= arc / 2 + asin(r / d)), no view slack: the body never steps or leans at an enemy the
            // swing will miss (was reach + 0.6 u and arc + 25 degrees: "turns toward an enemy it will miss").
            double range = LightWeaponView.Range(w.Config, a.Cls);
            double halfArc = LightWeaponView.ArcDeg(w.Config, a.Cls) * 0.5 * Math.PI / 180;
            double fx = Math.Cos(a.SwingFacing), fz = Math.Sin(a.SwingFacing);
            int best = -1;
            double bestD2 = double.MaxValue;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor b = w.Actors[j];
                if (j == i || !b.Alive || b.Team == a.Team) continue;
                double r = w.Config.Class(b.Cls).Radius, reach = range + r;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i], d2 = dx * dx + dz * dz;
                if (d2 > reach * reach || d2 >= bestD2 || d2 < 1e-9) continue;
                double d = Math.Sqrt(d2);
                double half = Math.Min(Math.PI, halfArc + Math.Asin(Math.Min(1, r / d)));
                if ((dx * fx + dz * fz) / d < Math.Cos(half) - 1e-9) continue;
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
            // px lane C (2026-11-22): through the swing the drawn body faces the sim's LIVE SwingFacing (lane S1 lets it track
            // the aim during the wind-up), never a view-chosen target: the picture turns where the rule swings. Kept inside
            // FaceConeDeg of the sim facing so the legs (which run along Facing) never twist against it.
            double sf = a.SwingFacing;
            double d = sf - a.Facing;
            d = Math.Atan2(Math.Sin(d), Math.Cos(d));
            double cone = FaceConeDeg * Math.PI / 180;
            return Math.Abs(d) <= cone ? sf : a.Facing + Math.Sign(d) * cone;
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
                // px lane C: the step runs along the live swing (the body faces it); the honest target sets only its length.
                double dx = w.X[t] - w.X[i], dz = w.Z[t] - w.Z[i], d = Math.Sqrt(dx * dx + dz * dz);
                // feat/hit-feel-2: step in far enough that the weapon reaches the target's body (ViewReach), never into it.
                peak = StepPeak(d, ViewReach(a.Cls), w.Config.Class(a.Cls).Radius, w.Config.Class(w.Actors[t].Cls).Radius);
            }
            double m = peak * k;
            return SimSpace.ToWorld(dirX * m, 0, dirZ * m);
        }

        /// <summary>
        /// fix/hit-feel-5: the upper-body lean toward the swing target that closes what the step-in leaves (gap = target
        /// surface minus tip reach minus step-in), shaped like the lunge (peak on the damage tick, absorbed after).
        /// False when there is no swing or no target. Pure on the world.
        /// </summary>
        public static bool StrikeLean(World w, int i, double alpha, out double deg, out double dirX, out double dirZ)
        {
            deg = 0; dirX = dirZ = 0;
            if (w == null || i < 0 || i >= w.ActorCount || !w.Actors[i].Alive) return false;
            Actor a = w.Actors[i];
            double hz = w.Config.SimHz;
            double age = SwingAge(a, w.Tick, alpha, hz);
            if (age < 0) return false;
            double windup = Math.Max(0, a.SwingWindup) / hz;
            double k = Math.Max(0, LungeShape(age, windup));
            if (k <= 0) return false;
            int t = SwingTarget(w, i);
            if (t < 0) return false;
            double dx = w.X[t] - w.X[i], dz = w.Z[t] - w.Z[i], d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6) return false;
            dirX = dx / d; dirZ = dz / d;
            double rA = w.Config.Class(a.Cls).Radius, rT = w.Config.Class(w.Actors[t].Cls).Radius, reach = ViewReach(a.Cls);
            double gap = d - rT - reach - StepPeak(d, reach, rA, rT);
            int c = (int)a.Cls;
            double lever = c >= 0 && c < MeasuredLever.Length && MeasuredLever[c] > 0 ? MeasuredLever[c] : 0.5;
            deg = HitPose.StrikeLeanDeg(gap, lever) * k;
            return deg > 0;
        }
    }
}

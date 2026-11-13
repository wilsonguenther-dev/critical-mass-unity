using System;
using CriticalMass.Runtime.View.Art;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// Locomotion maths (DG-75, DG-76), pure and allocation-free so EditMode tests can prove it.
    /// <para><b>Directional blend (no moonwalking).</b> The KayKit pack has a forward run, two 60-degree strafe runs and a
    /// backwards walk. Played backwards, an in-place clip moves its body the opposite way with the feet still planted, so the
    /// run band has six directions 60 degrees apart: forward (Run), left 60 (StrafeL), left 120 (StrafeR reversed), back
    /// (Run reversed), right 120 (StrafeL reversed), right 60 (StrafeR). The walk band swaps forward and back for Walk and
    /// WalkBack. The angle between the rendered facing and the rendered velocity picks the two neighbours and blends them
    /// linearly (a 1D circular blend space, like a Unity 2D Freeform Directional tree restricted to the unit circle).</para>
    /// <para><b>Sideways sectors (fix/animation-reds).</b> The KayKit strafes are runs with the whole lower body turned 60
    /// degrees (forward kinematics on Knight.glb: the planted toe and the hip line point +60 in Running_Strafe_Left, -60 in
    /// Running_Strafe_Right). Clips mix per bone ROTATION, so StrafeL (hips +60) and StrafeR reversed (hips -60) average to
    /// hips 0 with opposite stride motions: the legs march in place and the planted foot slides with the body (0.98 u per
    /// u at exactly 90 degrees, reproduced by the same kinematics). Neighbours whose hips agree (0 and 60, 120 and 180, ...)
    /// still blend; between 60 and 120 (and 240 and 300) ONE strafe plays and the legs are turned by the remainder
    /// (orientation warping: ActorAnimator turns the hips by LegWarpDeg and the spine back, so the chest keeps the aim).
    /// The strafe is kept until the travel angle passes the pure-sideways line by SideHysteresisDeg (no flip-flop).</para>
    /// <para><b>Speed matching.</b> One phase per actor (a sync group): it advances by distance travelled divided by the
    /// blended stride (ground speed x length of each weighted clip), so every clip completes a cycle exactly when the body
    /// has covered that clip's stride: the planted foot stays put. Distance comes from the RENDERED displacement, not the
    /// sim velocity, so legs idle against a wall and step when shoved. Each clip is sampled at its own left-mid-stance
    /// offset, so walk and run (and forward and reversed) plant the same foot at the same moment: no scissoring.</para>
    /// </summary>
    public static class Locomotion
    {
        /// <summary>One direction of the blend space: the clip, whether it plays reversed, and its travel angle (+ left).</summary>
        public readonly struct Dir
        {
            public readonly string Clip;
            public readonly bool Reversed;
            public readonly float AngleDeg;
            public Dir(string clip, bool reversed, float angleDeg) { Clip = clip; Reversed = reversed; AngleDeg = angleDeg; }
        }

        public const int DirCount = 6;
        public const float DirStepDeg = 60f;

        /// <summary>Run band, index k at k x 60 degrees to the left (0 forward, 3 back).</summary>
        public static readonly Dir[] RunBand =
        {
            new Dir(ArtMapping.Run, false, 0f), new Dir(ArtMapping.StrafeL, false, 60f), new Dir(ArtMapping.StrafeR, true, 120f),
            new Dir(ArtMapping.Run, true, 180f), new Dir(ArtMapping.StrafeL, true, 240f), new Dir(ArtMapping.StrafeR, false, 300f),
        };

        /// <summary>Walk band: forward Walk and backwards WalkBack; the sideways rows reuse the strafe runs (the pack has no walking strafe).</summary>
        public static readonly Dir[] WalkBand =
        {
            new Dir(ArtMapping.Walk, false, 0f), new Dir(ArtMapping.StrafeL, false, 60f), new Dir(ArtMapping.StrafeR, true, 120f),
            new Dir(ArtMapping.WalkBack, false, 180f), new Dir(ArtMapping.StrafeL, true, 240f), new Dir(ArtMapping.StrafeR, false, 300f),
        };

        /// <summary>Playback-rate clamp (clip rate = speed / clip ground speed): beyond it the foot slides rather than the clip racing.</summary>
        public const double MinRate = 0.25, MaxRate = 2.2;
        /// <summary>Below MoveStart the body idles; at MoveFull locomotion has full weight (u/s).</summary>
        public const double MoveStart = 0.12, MoveFull = 0.55;
        /// <summary>Walk-to-run band blend (u/s): the walk is natural at 0.86, the run at 4.57; shield walk 2.66 is a slow run.</summary>
        public const double RunStart = 1.3, RunFull = 2.3;

        /// <summary>
        /// Signed angle in degrees from the facing to the velocity, + when the velocity points to the character's LEFT.
        /// Arguments are Unity XZ vectors (SimSpace.ToWorld already mirrored the sim's Z).
        /// </summary>
        public static double LeftAngleDeg(double fx, double fz, double vx, double vz)
        {
            // Unity is left-handed with Y up: cross(f, v).y > 0 means v is to the right of f.
            double crossY = fz * vx - fx * vz;
            double dot = fx * vx + fz * vz;
            return -Math.Atan2(crossY, dot) * 180.0 / Math.PI;
        }

        /// <summary>Degrees past the pure-sideways line (90 or 270) before the legs flip to the other strafe.</summary>
        public const double SideHysteresisDeg = 15.0;
        /// <summary>The side state before any sideways motion (or after leaving the sideways sectors).</summary>
        public const int NoSide = -1;

        /// <summary>
        /// Weights of the six directions (sum 1) for a travel angle (+ left), without history (see the stateful overload).
        /// </summary>
        public static void DirectionWeights(double leftDeg, float[] w6)
        {
            int side = NoSide;
            DirectionWeights(leftDeg, w6, ref side);
        }

        /// <summary>
        /// Weights of the six directions (sum 1) for a travel angle (+ left) and the leg warp in degrees (+ left).
        /// Outside the sideways sectors: the two neighbours of the angle, linear in angle, warp 0. Inside (60..120,
        /// 240..300, open): one strafe at weight 1 and warp = angle - that strafe's angle (at most 45 degrees). side is the
        /// strafe index chosen last time (NoSide at first): it is kept until the angle passes 90 (270) by SideHysteresisDeg.
        /// With no history, exactly sideways picks the strafe that runs toes-first (StrafeL going left, StrafeR going right).
        /// </summary>
        public static double DirectionWeights(double leftDeg, float[] w6, ref int side)
        {
            for (int i = 0; i < DirCount; i++) w6[i] = 0f;
            double a = leftDeg % 360.0;
            if (a < 0) a += 360.0;
            int lo = a > 60.0 && a < 120.0 ? 1 : a > 240.0 && a < 300.0 ? 4 : -1;
            if (lo < 0)
            {
                side = NoSide;
                int k = (int)Math.Floor(a / DirStepDeg) % DirCount;
                float u = (float)((a - k * DirStepDeg) / DirStepDeg);
                w6[k] += 1f - u;
                w6[(k + 1) % DirCount] += u;
                return 0.0;
            }
            int hi = lo + 1;
            double mid = (lo + 0.5) * DirStepDeg; // 90 or 270
            if (side != lo && side != hi) side = lo == 1 ? (a <= mid ? lo : hi) : (a < mid ? lo : hi);
            else if (side == lo && a >= mid + SideHysteresisDeg) side = hi;
            else if (side == hi && a <= mid - SideHysteresisDeg) side = lo;
            w6[side] = 1f;
            return a - side * DirStepDeg;
        }

        /// <summary>
        /// Where a locomotion clip's lower body points relative to the facing (degrees, + left), from its measured travel
        /// angle: the forward-running clips (Run, the strafes) point where they travel, a backwards clip (WalkBack) points
        /// opposite. Playing a clip reversed does not turn its legs.
        /// </summary>
        public static double LegYawDeg(double clipMoveDeg)
        {
            double m = Wrap180(clipMoveDeg);
            return Math.Abs(m) <= 90.0 ? m : Wrap180(m + 180.0);
        }

        /// <summary>Signed angle in (-180, 180].</summary>
        public static double Wrap180(double deg)
        {
            deg %= 360.0;
            if (deg > 180.0) deg -= 360.0;
            else if (deg <= -180.0) deg += 360.0;
            return deg;
        }

        /// <summary>Weight of locomotion over idle by speed (smoothstep MoveStart..MoveFull).</summary>
        public static float MoveWeight(double speed) => Smooth(MoveStart, MoveFull, speed);

        /// <summary>Weight of the run band over the walk band by speed.</summary>
        public static float RunWeight(double speed) => Smooth(RunStart, RunFull, speed);

        /// <summary>Playback rate that matches a clip's ground speed to the body's speed, clamped to MinRate..MaxRate.</summary>
        public static double MatchRate(double speed, double clipGroundSpeed)
        {
            if (clipGroundSpeed <= 1e-6) return 1.0;
            return Math.Max(MinRate, Math.Min(MaxRate, speed / clipGroundSpeed));
        }

        /// <summary>
        /// Phase advance for one frame: distance over the blended stride, clamped so the equivalent clip rate stays within
        /// MinRate..MaxRate of the blended clip length. Returns the new phase in [0, 1).
        /// </summary>
        public static double Advance(double phase, double distance, double blendedStride, double blendedLength, double dSim)
        {
            if (dSim <= 0 || blendedStride <= 1e-6 || blendedLength <= 1e-6) return phase;
            double d = distance / blendedStride;
            double lo = MinRate * dSim / blendedLength, hi = MaxRate * dSim / blendedLength;
            d = Math.Max(lo, Math.Min(hi, d));
            return Frac(phase + d);
        }

        /// <summary>
        /// Clip time for the shared phase: the clip's own left-mid-stance offset aligns its planted foot with the reference
        /// clip's (refPlant). Reversed clips mirror the phase about that marker, so their planted foot also lines up.
        /// </summary>
        public static double SampleTime(double phase, double length, double plant, double refPlant, bool reversed)
        {
            double n = reversed ? plant + refPlant - phase : phase - refPlant + plant;
            return Frac(n) * length;
        }

        public static double Frac(double x) => x - Math.Floor(x);

        /// <summary>True when the phase crossed the marker going from p0 to p1 (forward, wrapping at 1).</summary>
        public static bool Crossed(double p0, double p1, double marker)
        {
            if (marker < 0) return false;
            if (p1 >= p0) return marker > p0 && marker <= p1;
            return marker > p0 || marker <= p1;
        }

        public static float Smooth(double e0, double e1, double v)
        {
            double t = Math.Max(0, Math.Min(1, (v - e0) / (e1 - e0)));
            return (float)(t * t * (3 - 2 * t));
        }
    }
}

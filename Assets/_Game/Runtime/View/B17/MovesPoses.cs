// Class moves build (2026-11-22), lane 6: the pose numbers of the new verbs (pure) and the parry hit-stop latch (view only).
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.B17
{
    /// <summary>
    /// Lane 6 (class moves): what ActorAnimator draws for the new verbs. GUARD: the Blocking clip on the upper body, full
    /// within GuardRiseSeconds (two 60 Hz frames). CHARGE: the class attack clip held in its wind-up, deeper as the charge
    /// grows. SLIDE: legs held (no running in place), body low and leaning back along the slide. GLIDE: arms out (the Cast
    /// clip held), and the drawn height held at the jump's peak (GlideLift). PARRY: an 80 ms pose hold on the parrier and the
    /// attacker (the existing "fake hit-stop": the loops stop, nothing in the sim or Time.timeScale changes).
    /// Pure functions plus one per-actor wall-clock latch; nothing here writes the sim.
    /// </summary>
    public static class MovesPoses
    {
        public const double GuardRiseSeconds = 0.033, GuardFallSeconds = 0.08, ChargeRiseSeconds = 0.05;
        /// <summary>The charge pose samples the attack clip at this share of its length (the raised wind-up), from the
        /// first share at the charge start to the last at full charge.</summary>
        public const float ChargeClipShareStart = 0.18f, ChargeClipShareFull = 0.32f;
        public const float SlideLeanDeg = -18f, SlideSquashY = 0.78f, GlideArmsShare = 0.42f, GlideArmsWeight = 0.85f;
        /// <summary>Parry hit-stop on both bodies (SPEC T2-1: 80 ms).</summary>
        public const double ParryHoldSeconds = 0.08;

        static double[] _holdUntil = new double[64];

        /// <summary>Readability item 1: the local fighter whose Guard press is shown before the sim takes it, and its
        /// strength (MovesViews.PredictGuard writes both each frame; ActorAnimator reads them). -1 / 0 = none.</summary>
        public static int PredictActor = -1;
        public static float PredictGuard;

        /// <summary>The guard pose shows for actor i: the sim holds it, or it is the local press being predicted.</summary>
        public static bool GuardShown(World w, int i) => MovesRead.GuardHeld(w, i) || (i == PredictActor && PredictGuard > 0.05f);

        /// <summary>Hold actor i's pose until wall time now + seconds (the parry clang's 80 ms).</summary>
        public static void Hold(int i, double now, double seconds)
        {
            if (i < 0) return;
            if (i >= _holdUntil.Length) System.Array.Resize(ref _holdUntil, System.Math.Max(i + 1, _holdUntil.Length * 2));
            double until = now + seconds;
            if (until > _holdUntil[i]) _holdUntil[i] = until;
        }

        /// <summary>True while actor i's parry hold runs at wall time now.</summary>
        public static bool Holding(int i, double now) => i >= 0 && i < _holdUntil.Length && now < _holdUntil[i];

        /// <summary>Wall seconds of hold left (tests, ledger).</summary>
        public static double HoldLeft(int i, double now) => i >= 0 && i < _holdUntil.Length ? System.Math.Max(0, _holdUntil[i] - now) : 0;

        public static void ClearHolds() { for (int i = 0; i < _holdUntil.Length; i++) _holdUntil[i] = 0; }

        /// <summary>Linear approach of a weight toward target over seconds.</summary>
        public static float Approach(float v, float target, double dt, double seconds)
        {
            if (seconds <= 0 || dt <= 0) return dt > 0 ? target : v;
            float step = (float)(dt / seconds);
            return v < target ? System.Math.Min(target, v + step) : System.Math.Max(target, v - step);
        }

        /// <summary>The charge pose's clip time share for a charge share 0..1.</summary>
        public static float ChargeClipShare(float share) => ChargeClipShareStart + (ChargeClipShareFull - ChargeClipShareStart) * System.Math.Max(0f, System.Math.Min(1f, share));

        /// <summary>Slide body: (lean degrees, height scale) over the slide's phase 0..1 (in fast, out over the last 30%).</summary>
        public static void Slide(float phase, out float leanDeg, out float squashY)
        {
            if (phase < 0f) { leanDeg = 0f; squashY = 1f; return; }
            float k = phase < 0.15f ? phase / 0.15f : phase > 0.7f ? (1f - phase) / 0.3f : 1f;
            k = System.Math.Max(0f, System.Math.Min(1f, k));
            leanDeg = SlideLeanDeg * k;
            squashY = 1f - (1f - SlideSquashY) * k;
        }

        /// <summary>
        /// T2-6 apex hold: the glide holds the sim's AirTick at the apex, so the drawn jump arc (JumpLift.ViewLift, skewed)
        /// sits a little under its peak and wobbles with the render alpha (the ~1% bob). While gliding this is the extra
        /// height (u) that draws the body at the arc's peak instead. 0 when not gliding. Pure.
        /// INTEGRATOR: add "+ Vector3.up * B17.MovesPoses.GlideLift(w, i, a)" beside JumpLift.Offset at ActorViews.cs 419
        /// and 469 (ActorViews is not lane 6's file).
        /// </summary>
        public static float GlideLift(World w, int i, double alpha)
        {
            if (!MovesRead.Gliding(w, i)) return 0f;
            Actor a = w.Actors[i];
            double u = Jump.JumpPhase(a, alpha);
            if (u < 0) return 0f;
            double peak = Jump.JumpLift(w.Config, a, 0.5);
            double now = JumpLift.ViewLift(w.Config, a, u);
            return (float)System.Math.Max(0, peak - now);
        }
    }
}

// Class moves build (2026-11-22), lane 6: the view's ONE reader of the class-moves sim state. Pure reads, never a write.
// Every reader returns the "nothing" answer while World.Moves is null (every switch off, or not the Full rules), so with all
// switches off no view, HUD word or pose in lane 6 can change what the screen shows.
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.B17
{
    /// <summary>
    /// Lane 6 (class moves): what the screen needs from lanes 2 to 4, in one place. GUARD (raised, blocking, meter share),
    /// CHARGE (share, the heavy's shape), COMBO (the step of the swing in flight, the chain window), TRAVEL / SLIDE, GLIDE
    /// and the jump-evade landing. Pure C#, allocation-free, unit-tested (MovesFeedbackWordsTests in PlayerSeatMovesTests).
    /// </summary>
    public static class MovesRead
    {
        /// <summary>The class-moves state exists (at least one switch on under the Full rules at world creation).</summary>
        public static bool On(World w) => w != null && w.Moves != null && w.Config != null;

        static bool Ok(World w, int i, int len) => On(w) && i >= 0 && i < w.ActorCount && i < len;

        // ------------------------------------------------------------------ guard / parry (lane 2)

        /// <summary>The guard is held (raised or raising; the pose shows from the first held tick).</summary>
        public static bool GuardHeld(World w, int i)
        {
            if (!On(w) || !MovesHooks.Cfg(w).Guard) return false;
            DefenceMovesState d = w.Moves.Defence;
            return Ok(w, i, d.GuardOn.Length) && d.GuardOn[i] != 0 && w.Actors[i].Alive;
        }

        /// <summary>The guard blocks now (raised GuardRaiseTicks ago or more).</summary>
        public static bool GuardBlocking(World w, int i)
        {
            if (!GuardHeld(w, i)) return false;
            DefenceMovesState d = w.Moves.Defence;
            return w.Tick - d.GuardSince[i] >= MovesHooks.Cfg(w).GuardRaiseTicks && GuardMeter01(w, i) > 0;
        }

        /// <summary>The class has a guard meter (every class but the Warrior, whose SHIELD is his block).</summary>
        public static bool HasGuardMeter(World w, ClassId cls)
        {
            if (!On(w)) return false;
            double[] row = MovesHooks.Cfg(w).GuardMeter;
            int c = (int)cls;
            return cls != ClassId.Warrior && row != null && c < row.Length && row[c] > 0;
        }

        /// <summary>The guard meter share left, 0..1 (1 = full; -1 in the sim means full and is read as 1).</summary>
        public static float GuardMeter01(World w, int i)
        {
            if (!On(w)) return 1f;
            DefenceMovesState d = w.Moves.Defence;
            if (!Ok(w, i, d.GuardMeter.Length)) return 1f;
            double[] row = MovesHooks.Cfg(w).GuardMeter;
            int c = (int)w.Actors[i].Cls;
            double max = row != null && c < row.Length ? row[c] : 0;
            double now = d.GuardMeter[i];
            if (now < 0 || max <= 0) return 1f;
            return (float)System.Math.Max(0, System.Math.Min(1, now / max));
        }

        /// <summary>The parry lock-out (1.0 s after an attempt) is running: a Guard tap now cannot parry.</summary>
        public static bool ParryLocked(World w, int i)
        {
            if (!On(w) || !MovesHooks.Cfg(w).Guard) return false;
            DefenceMovesState d = w.Moves.Defence;
            return Ok(w, i, d.ParryLockUntil.Length) && w.Tick < d.ParryLockUntil[i];
        }

        // ------------------------------------------------------------------ heavy / combo (lane 4)

        /// <summary>Charging a heavy, with the charge share 0..1.</summary>
        public static bool Charging(World w, int i, out float share)
        {
            share = 0f;
            if (!On(w) || !MovesHooks.Cfg(w).Heavy || i < 0 || i >= w.ActorCount) return false;
            if (!MovesHooks.OffenceCharging(w, i, out double s)) return false;
            share = (float)System.Math.Max(0, System.Math.Min(1, s));
            return true;
        }

        /// <summary>The charge glow step: 0 none, 1 started, 2 two thirds, 3 full. Steps only rise during one charge (no
        /// flashing: at most three changes in the 0.53 s from the charge start to full, under 3 Hz... see GlowStepHz).</summary>
        public static int ChargeStep(float share) => share <= 0f ? 0 : share < 0.5f ? 1 : share < 0.999f ? 2 : 3;

        /// <summary>The fastest the charge glow can change step (Hz): 3 steps over (ChargeFullTick - ChargeStartTick) ticks.</summary>
        public static double GlowStepHz(World w)
        {
            if (!On(w)) return 0;
            MovesConfig m = MovesHooks.Cfg(w);
            double span = (m.ChargeFullTick - m.ChargeStartTick) / (double)w.Config.SimHz;
            return span > 0 ? 2.0 / span : 0;   // two changes after the first one, over the span
        }

        /// <summary>The heavy's ground shape for a class (reach u, full arc in degrees).</summary>
        public static void HeavyShape(World w, ClassId cls, out float reach, out float arcDeg)
        {
            MovesHooks.OffenceHeavyShape(w, cls, out double r, out double a);
            reach = (float)r;
            arcDeg = (float)a;
        }

        /// <summary>The combo step of the swing in flight (2 or 3 = a chain step; 0 or 1 = none / the opener).</summary>
        public static int ComboStep(World w, int i) => On(w) && MovesHooks.Cfg(w).Combos ? MovesHooks.OffenceComboStep(w, i) : 0;

        /// <summary>The next Attack press is chain step n (2 or 3) inside the chain window; 0 = none.</summary>
        public static int ChainNext(World w, int i)
        {
            if (!On(w) || !MovesHooks.Cfg(w).Combos) return 0;
            OffenceMovesState s = w.Moves.Offence;
            if (!Ok(w, i, s.ChainNext.Length) || w.Tick > s.ChainUntil[i]) return 0;   // inclusive, as OffenceSwingWindup reads it
            int n = s.ChainNext[i];
            return n >= 2 ? n : 0;
        }

        /// <summary>The class's combo length (Warrior 2, Farmer 3, ...).</summary>
        public static int ComboSteps(World w, ClassId cls)
        {
            if (!On(w)) return 0;
            int[] row = MovesHooks.Cfg(w).ComboSteps;
            int c = (int)cls;
            return row != null && c < row.Length ? row[c] : 0;
        }

        // ------------------------------------------------------------------ travel / slide / glide / landing (lane 3)

        public static bool Travelling(World w, int i) => On(w) && MovesHooks.MovementTravelling(w, i);
        public static float TravelShare(World w, int i) => On(w) ? (float)MovesHooks.MovementTravelShare(w, i) : 0f;
        public static bool SlideArmed(World w, int i) => On(w) && MovesHooks.MovementSlideArmed(w, i);
        public static bool Sliding(World w, int i) => On(w) && MovesHooks.MovementSliding(w, i);
        public static bool Gliding(World w, int i) => On(w) && MovesHooks.MovementGliding(w, i);
        public static float GlideLeft01(World w, int i) => On(w) ? (float)MovesHooks.MovementGlideShareLeft(w, i) : 0f;

        /// <summary>The slide's progress 0..1 (0 at its first tick); -1 when not sliding.</summary>
        public static float SlidePhase(World w, int i, double alpha)
        {
            if (!Sliding(w, i)) return -1f;
            MovementMovesState m = w.Moves.Movement;
            int len = System.Math.Max(1, MovesHooks.Cfg(w).SlideTicks);
            double t = (w.Tick - m.SlideStart[i]) + alpha;
            return (float)System.Math.Max(0, System.Math.Min(1, t / len));
        }

        /// <summary>The jump-evade landing recovery runs (4 ticks: no dodge, attack or guard).</summary>
        public static bool LandingLocked(World w, int i) => On(w) && MovesHooks.MovementLanding(w, i);

        /// <summary>The 1.0 s jump relock after an evading jump runs, with the seconds left (the refused jump's words).</summary>
        public static bool JumpRelocked(World w, int i, out float seconds)
        {
            seconds = 0f;
            if (!On(w) || !MovesHooks.Cfg(w).JumpEvade) return false;
            MovementMovesState m = w.Moves.Movement;
            if (!Ok(w, i, m.JumpRelockUntil.Length) || w.Tick >= m.JumpRelockUntil[i]) return false;
            seconds = (float)((m.JumpRelockUntil[i] - w.Tick) / (double)w.Config.SimHz);
            return true;
        }

        /// <summary>Second wind is still available this life (the HUD may say so once below 25%).</summary>
        public static bool SecondWindReady(World w, int i)
        {
            if (!On(w) || !MovesHooks.Cfg(w).SecondWind) return false;
            DefenceMovesState d = w.Moves.Defence;
            return Ok(w, i, d.SecondWindUsed.Length) && d.SecondWindUsed[i] == 0;
        }

        // ------------------------------------------------------------------ HUD slot words (pure, tested)

        public const string SlideWord = "SLIDE", TravelWord = "TRAVEL", ChargeWord = "CHARGE", ChargedWord = "CHARGED";

        static readonly string[] ChainWords = { "", "", "x2", "x3", "x4" };

        /// <summary>The combo count word: x2, x3 (empty for step 0 or 1).</summary>
        public static string ComboWord(int step) => step >= 2 && step < ChainWords.Length ? ChainWords[step] : "";

        /// <summary>
        /// The attack slot's word under the class moves, or null to keep the slot's own word: CHARGE / CHARGED while the
        /// local fighter charges a heavy, the chain step (x2 / x3) while the next press would be a chain step. A charge wins
        /// over a chain (the two never both apply to one press: the hold decides).
        /// </summary>
        public static string AttackSlotWord(bool charging, float chargeShare, int chainNext)
        {
            if (charging) return chargeShare >= 0.999f ? ChargedWord : ChargeWord;
            if (chainNext >= 2) return ComboWord(chainNext);
            return null;
        }

        /// <summary>The dodge chip's word: SLIDE while a Dodge press would slide (TRAVEL lit), else null (the chip's own
        /// "DODGE key"). The slide and the dodge never both apply: travel only exists with no enemy within 8 u.</summary>
        public static string DodgeSlotWord(bool slideArmed, string key) => slideArmed ? (string.IsNullOrEmpty(key) ? SlideWord : SlideWord + " " + key) : null;

        /// <summary>The local fighter's dodge chip word for this world (null = keep the chip's word).</summary>
        public static string DodgeSlotWord(World w, int me, string key) => DodgeSlotWord(SlideArmed(w, me), key);

        /// <summary>The local fighter's attack slot word for this world (null = keep the slot's word).</summary>
        public static string AttackSlotWord(World w, int me)
        {
            bool c = Charging(w, me, out float s);
            return AttackSlotWord(c, s, c ? 0 : ChainNext(w, me));
        }
    }
}

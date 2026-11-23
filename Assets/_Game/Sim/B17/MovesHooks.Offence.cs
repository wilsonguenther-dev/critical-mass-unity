// Class moves build (2026-11-22): LANE 4 (Offence) owns this file: T1-1 TNT on its own key, T2-2 heavy / charged, T2-3 combos.
// Stubbed by lane 1: every method is an identity, so the game is the base branch's until lane 4 fills them. Keep every
// signature; test your own switch with MovesHooks.On(w, MovesHooks.Cfg(w).<Switch>). Contract: LANE1-CONTRACT.md.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Lane 4's class-moves state (World.Moves.Offence). Add fields here: public, primitive arrays sized in Init.</summary>
    public sealed class OffenceMovesState
    {
        public void Init(World w) { }
        public void Reset(World w) { }
    }

    public static partial class MovesHooks
    {
        /// <summary>Per tick, before Abilities (charge level, auto-release at ChargeAutoReleaseTick, combo reset timer, fuse blasts).</summary>
        static void OffenceStep(World w, double dt) { }

        /// <summary>The attack want after the buffer: hold = charge (false while charging), release = the heavy (true once),
        /// combo press in the chain window. Return wants unchanged when off.</summary>
        static bool OffenceWantsAttack(World w, int i, bool wants) => wants;

        /// <summary>The wind-up of the swing starting now (combo step x ComboStepWindupMul). Return windup when not a step.</summary>
        static int OffenceSwingWindup(World w, int i, int windup) => windup;

        /// <summary>The contact tick's reach, arc and damage (heavy shape, charge and combo damage). Leave them when off.</summary>
        static void OffenceSwingShape(World w, int i, ref double range, ref double arcDeg, ref double dmg) { }

        /// <summary>After actor i's swing contact (hits 0 = whiff): combo advance or reset, HeavyWhiffExtraRecoveryTicks.</summary>
        static void OffenceAfterContact(World w, int i, int hits) { }

        /// <summary>A melee strike's knock and stagger (heavy x charge, finisher x FinisherKnockMul, charge armour on the target).</summary>
        static void OffenceStrikeKnock(World w, int source, int target, ref double knock, ref int stun) { }

        /// <summary>HP was taken (dmg &gt; 0).</summary>
        static void OffenceDamaged(World w, int target, int source, DamageCause cause, double dmg) { }

        /// <summary>Walk-speed multiplier (charging x ChargeMoveMul). Exactly 1.0 when not charging.</summary>
        static double OffenceSpeedScale(World w, int i) => 1.0;
    }
}

// Class moves build (2026-11-22): LANE 2 (Defence) owns this file: T1-2 lunge, T1-3 dodge refund, T1-4a feint bait (sim side),
// T2-1 guard and parry, T2-7 the hit test (BlockedAir), T2-8 second wind. Stubbed by lane 1: every method is an identity, so
// the game is the base branch's until lane 2 fills them. Keep every signature (lane 1's call sites use them); test your own
// switch with MovesHooks.On(w, MovesHooks.Cfg(w).<Switch>) and return the identity when it is off. Contract: LANE1-CONTRACT.md.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Lane 2's class-moves state (World.Moves.Defence). Add fields here: public, primitive arrays sized in Init.</summary>
    public sealed class DefenceMovesState
    {
        public void Init(World w) { }
        public void Reset(World w) { }
    }

    public static partial class MovesHooks
    {
        /// <summary>Per tick, before Abilities (guard raise / release, meter refill, parry lockout, second wind timers).</summary>
        static void DefenceStep(World w, double dt) { }

        /// <summary>True = actor i may not start a swing this tick (guard held, whiffed-parry hold, landing recovery).</summary>
        static bool DefenceLocksAttack(World w, int i) => false;

        /// <summary>True = actor i may not cast this tick (guard held).</summary>
        static bool DefenceLocksCast(World w, int i) => false;

        /// <summary>The wind-up of the swing actor i starts this tick (LUNGE x LungeWindupMul). Return windup when not a lunge.</summary>
        static int DefenceSwingWindup(World w, int i, int windup) => windup;

        /// <summary>After actor i's swing contact (hits 0 = whiff).</summary>
        static void DefenceAfterContact(World w, int i, int hits) { }

        /// <summary>A melee strike's knock and stagger (guard break stagger on the guard, parry stagger on the attacker).</summary>
        static void DefenceStrikeKnock(World w, int source, int target, ref double knock, ref int stun) { }

        /// <summary>Actor i just feinted (Feint Value 1 pushed): raise FeintBaited when a victim answered (switch FeintBait).</summary>
        static void DefenceFeinted(World w, int i) { }

        /// <summary>True = negate this hit (parry: BlockedParried; airborne on air ticks 5..12: BlockedAir).</summary>
        static bool DefenceHitNegated(World w, int target, int source, double srcX, double srcZ, DamageCause cause, bool report) => false;

        /// <summary>The damage after the frontal guard cut (BlockedGuard, GuardBreak). Return dmg unchanged when not guarding.</summary>
        static double DefenceHitCut(World w, int target, int source, double srcX, double srcZ, DamageCause cause, double dmg, bool report) => dmg;

        /// <summary>HP was taken (dmg &gt; 0).</summary>
        static void DefenceDamaged(World w, int target, int source, DamageCause cause, double dmg) { }

        /// <summary>Walk-speed multiplier (guard x GuardMoveMul). Exactly 1.0 when not guarding.</summary>
        static double DefenceSpeedScale(World w, int i) => 1.0;

        /// <summary>A jump landed.</summary>
        static void DefenceLanded(World w, int id) { }
    }
}

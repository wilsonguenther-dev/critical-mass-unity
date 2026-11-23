// Class moves build (2026-11-22): LANE 3 (Movement) owns this file: T2-4 travel, T2-5 slide, T2-6 glide, T2-7 the air window
// and the landing lock. Stubbed by lane 1: every method is an identity, so the game is the base branch's until lane 3 fills
// them. Keep every signature; test your own switch with MovesHooks.On(w, MovesHooks.Cfg(w).<Switch>). Contract: LANE1-CONTRACT.md.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Lane 3's class-moves state (World.Moves.Movement). Add fields here: public, primitive arrays sized in Init.</summary>
    public sealed class MovementMovesState
    {
        public void Init(World w) { }
        public void Reset(World w) { }
    }

    public static partial class MovesHooks
    {
        /// <summary>Per tick, before Abilities (travel eligibility and ramp, glide cooldown).</summary>
        static void MovementStep(World w, double dt) { }

        /// <summary>True = actor i may not start a swing (gliding, mid-slide, slide-trip recovery).</summary>
        static bool MovementLocksAttack(World w, int i) => false;

        /// <summary>True = actor i may not cast (gliding).</summary>
        static bool MovementLocksCast(World w, int i) => false;

        /// <summary>A living grounded actor's tick, before the dodge: true = moved here (a slide), skip the walk update.</summary>
        static bool MovementGroundStep(World w, int i, double dt) => false;

        /// <summary>Walk-speed multiplier (TRAVEL ramp to TravelSpeedMul). Exactly 1.0 when not travelling.</summary>
        static double MovementSpeedScale(World w, int i) => 1.0;

        /// <summary>First thing of an airborne tick (glide: steering, held apex, AirLen extension).</summary>
        static void MovementAirTick(World w, int i, double dt) { }

        /// <summary>True = refuse a jump start (the relock after an evading jump, landing recovery).</summary>
        static bool MovementJumpLocked(World w, int id) => false;

        /// <summary>A jump landed (landing recovery after an evade, glide end).</summary>
        static void MovementLanded(World w, int id) { }

        /// <summary>HP was taken (a hit ends travel and glide; no travel for TravelNoCombatSeconds for both sides).</summary>
        static void MovementDamaged(World w, int target, int source, DamageCause cause, double dmg) { }
    }
}

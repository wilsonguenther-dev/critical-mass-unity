// Class moves build (2026-11-22): LANE 5 (Kits and bots) owns this file: T1-4b bot step-out, T1-5 the Builder's quick wall
// (its rule lives in BuilderWalls.cs / BuildConfig.cs), and the bots' use of guard, parry, charge, combo and travel. Stubbed by
// lane 1: every method is an identity. Keep every signature; test the switch with MovesHooks.On(w, MovesHooks.Cfg(w).<Switch>).
// Bots are seat fillers: their use of a new verb is capped against a human seat the way call 18 does it. Contract: LANE1-CONTRACT.md.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Lane 5's class-moves state (World.Moves.Bots). Add fields here: public, primitive arrays sized in Init.</summary>
    public sealed class BotsMovesState
    {
        public void Init(World w) { }
        public void Reset(World w) { }
    }

    public static partial class MovesHooks
    {
        /// <summary>Per tick, before Abilities (bot reads of human wind-ups, step-out timers).</summary>
        static void BotsStep(World w, double dt) { }

        /// <summary>Bot id's frame, last in BotBrain.FillBotInput: set output.Guard (hold), output.Attack (charge hold / release),
        /// a step-out move, or move a TNT request to output.Terraform (T1-1). output.Guard and output.JumpHeld arrive false.</summary>
        static void BotsFill(World w, int id, InputFrame output) { }
    }
}

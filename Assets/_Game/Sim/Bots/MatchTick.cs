using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>The one per-tick order (browser SimDriver.tsx lines 30-60, outcomes.ts tick()), shared by the Runtime driver, the soak harness and the PlayMode smoke test.</summary>
    public static class MatchTick
    {
        /// <summary>One sim tick. humanFrame: the human's frame for actor 0; null = actor 0 is bot-driven (autopilot).</summary>
        public static void Advance(World w, InputFrame humanFrame, InputFrame scratch)
        {
            // (1) actor 0: the human's frame, or the bots on autopilot.
            if (humanFrame != null) SimInput.ApplyInput(w, 0, humanFrame);
            else
            {
                BotBrain.FillBotInput(w, 0, scratch);
                SimInput.ApplyInput(w, 0, scratch);
            }
            // (2) bot thinking.
            BotBrain.BotsThink(w, SimStep.Dt(w.Config));
            // (3) every other actor.
            for (int id = 1; id < w.ActorCount; id++)
            {
                BotBrain.FillBotInput(w, id, scratch);
                SimInput.ApplyInput(w, id, scratch);
            }
            // (4) the sim step.
            SimStep.Step(w);
        }

        /// <summary>Steps (2), (3), (4) only: actor 0 receives no input at all (outcomes.ts tick() with human === "passive").</summary>
        public static void AdvanceNoHuman(World w, InputFrame scratch)
        {
            BotBrain.BotsThink(w, SimStep.Dt(w.Config));
            for (int id = 1; id < w.ActorCount; id++)
            {
                BotBrain.FillBotInput(w, id, scratch);
                SimInput.ApplyInput(w, id, scratch);
            }
            SimStep.Step(w);
        }
    }
}

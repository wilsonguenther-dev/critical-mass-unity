using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// b13-replay: a read-only listener on the single point where inputs enter the sim (Advance, AdvanceHumans,
    /// AdvanceNoHuman and HandToBot). The replay recorder implements it. A tap is called BEFORE the tick's inputs are applied,
    /// with exactly the frames the sim is about to consume, and must never write the world or the frames: the sim's results
    /// are identical with or without a tap (ReplayTests.RecordingOnOrOffGivesTheSameGoldenHashes).
    /// </summary>
    public interface IMatchInputTap
    {
        /// <summary>Advance: human is actor 0's frame, or null (actor 0 on autopilot).</summary>
        void OnAdvance(World w, InputFrame human);
        /// <summary>AdvanceHumans: frames[id] per actor (null = bot), possibly shorter than ActorCount, possibly null.</summary>
        void OnAdvanceHumans(World w, InputFrame[] frames);
        /// <summary>AdvanceNoHuman: actor 0 receives no input this tick.</summary>
        void OnAdvanceNoHuman(World w);
        /// <summary>HandToBot(w, id) is about to run.</summary>
        void OnHandToBot(World w, int id);
    }

    /// <summary>The one per-tick order (browser SimDriver.tsx lines 30-60, outcomes.ts tick()), shared by the Runtime driver, the soak harness and the PlayMode smoke test.</summary>
    public static class MatchTick
    {
        /// <summary>
        /// b13-replay: the input listener (null = none; the cost when null is one static load per tick). Set by the
        /// Runtime's replay capture; never read by any rule.
        /// </summary>
        public static IMatchInputTap Tap;

        /// <summary>One sim tick. humanFrame: the human's frame for actor 0; null = actor 0 is bot-driven (autopilot).</summary>
        public static void Advance(World w, InputFrame humanFrame, InputFrame scratch)
        {
            Tap?.OnAdvance(w, humanFrame);
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

        /// <summary>
        /// online-rooms-2: one tick with several humans (an online room). frames[id] is human id's frame for this tick, or
        /// null when a bot drives that actor (a bot slot, a human on autopilot, a player who left). The order is exactly
        /// Advance's: actor 0 first, then bot thinking, then every other actor in id order, then the step, so with only
        /// frames[0] set (or none) this is bit-for-bit Advance. frames may be shorter than ActorCount (missing = bot).
        /// </summary>
        public static void AdvanceHumans(World w, InputFrame[] frames, InputFrame scratch)
        {
            Tap?.OnAdvanceHumans(w, frames);
            InputFrame f0 = frames != null && frames.Length > 0 ? frames[0] : null;
            if (f0 != null) SimInput.ApplyInput(w, 0, f0);
            else
            {
                BotBrain.FillBotInput(w, 0, scratch);
                SimInput.ApplyInput(w, 0, scratch);
            }
            BotBrain.BotsThink(w, SimStep.Dt(w.Config));
            for (int id = 1; id < w.ActorCount; id++)
            {
                InputFrame f = frames != null && id < frames.Length ? frames[id] : null;
                if (f != null) SimInput.ApplyInput(w, id, f);
                else
                {
                    BotBrain.FillBotInput(w, id, scratch);
                    SimInput.ApplyInput(w, id, scratch);
                }
            }
            SimStep.Step(w);
        }

        /// <summary>
        /// online-rooms-2: a human who left an online room becomes a plain bot from this tick on. Every machine calls it at the
        /// same tick (the host announces the tick in the input bundle), so the worlds stay identical. Actor 0 keeps IsHuman
        /// (its autopilot is the single-player autopilot); any other actor becomes an ordinary bot.
        /// </summary>
        public static void HandToBot(World w, int id)
        {
            if (id <= 0 || id >= w.ActorCount) return;
            Tap?.OnHandToBot(w, id);
            w.Actors[id].IsHuman = false;
        }

        /// <summary>Steps (2), (3), (4) only: actor 0 receives no input at all (outcomes.ts tick() with human === "passive").</summary>
        public static void AdvanceNoHuman(World w, InputFrame scratch)
        {
            Tap?.OnAdvanceNoHuman(w);
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

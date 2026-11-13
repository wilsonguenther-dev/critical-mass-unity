using System;
using System.Runtime.CompilerServices;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>Bot brains (CM-09): FSM, team director, shared flow fields and steering into InputFrames. Port of src/bots/index.ts (09e0904), U-031.</summary>
    public static class BotBrain
    {
        /// <summary>Bot think period in sim ticks (round(simHz / thinkHz)).</summary>
        public static int BotThinkPeriod(GameConfig c) => Math.Max(1, (int)JsMath.Round((double)c.SimHz / c.Bots.ThinkHz));

        private static readonly ConditionalWeakTable<World, BotsState> States = new ConditionalWeakTable<World, BotsState>();

        /// <summary>The bot state for `world`, created on first use and reset when the world restarted or changed size.</summary>
        /// <summary>
        /// DG-02: true while actor 0 is on autopilot (FillBotInput was asked for it this tick or the previous one). Looks
        /// the state up without creating it, so a world no bot ever thought about answers false.
        /// </summary>
        public static bool ActorZeroOnAutopilot(World w)
        {
            return States.TryGetValue(w, out BotsState st) && BotsState.HumanDriven(st, w);
        }

        public static BotsState GetBotsState(World w)
        {
            BotsState st;
            if (!States.TryGetValue(w, out st))
            {
                st = BotsState.CreateBotsState(w);
                States.Add(w, st);
                BotsState.ResetBotsState(st, w);
                return st;
            }
            if (st.Epoch != w.Epoch || st.ActorCount != w.ActorCount || w.Tick < st.LastTick) BotsState.ResetBotsState(st, w);
            st.LastTick = w.Tick;
            return st;
        }

        /// <summary>One bot tick (once per sim step, before Step): at most one stale field rebuilt, the director on its period, each bot's FSM think staggered by id.</summary>
        public static void BotsThink(World w, double dt)
        {
            BotsState st = GetBotsState(w);
            if (w.Match.Phase == MatchPhase.Ended) return;
            BotFields.MaintainFields(st.Fields, w);
            BotDirector.DirectorTick(w, st);
            int period = st.ThinkPeriod;
            int n = w.ActorCount;
            int tick = w.Tick;
            bool human = BotsState.HumanDriven(st, w);
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                // CM-35: only Kaiju-role bots walk-pick meat (hill and Kaiju-fight bots leave the bodies to the feeders); actor 0
                // always can while a person drives it. Set every step, before ResourcesSystem. Meat rides in its own load.
                a.MeatPickup = (id == 0 && !human) || (st.Role[id] == BotsState.RoleKaiju && Roles.MayFeed(w, a.Cls)); // U-063: bot escorts leave meat to feeders
                if (id == 0 && !human) continue;
                if (!a.Alive) continue;
                // DG-21 reaction delay: Easy / Hard scale the think period (Normal keeps ThinkPeriod).
                int p = BotSkill.ThinkPeriod(w, id, period);
                if ((tick + id) % p == 0 || a.AiState == AiState.None) BotFsm.ThinkBot(w, st, id);
            }
        }

        /// <summary>Write bot `id`'s InputFrame for this step into `output` (no allocation). Asking for id 0 puts the human on autopilot.</summary>
        public static void FillBotInput(World w, int id, InputFrame output)
        {
            BotsState st = GetBotsState(w);
            if (id == 0) st.HumanDrivenTick = w.Tick;
            // fix-cm3-1: one scratch frame is shared by the human and every bot; a human jump / terraform press never reaches a bot.
            output.Terraform = false;
            output.Jump = false;
            output.TerraformSwitch = false; // DG-09: bots never switch the dig / place mode (they never dig: oracle CM-30)
            BotSteer.SteerBot(w, st, id, output);
            // DG-22 (CM-38): a Demolitions bot's queued TNT throw (BotFsm.DecideDemolitions) is its one press, aimed at the throw point.
            Demolitions.TakeBotTnt(w, id, output);
            // DG-40: a Healer bot's queued purify (BotFsm.DecideHealer) is its one DigPlace press.
            Healer.TakeBotPurify(w, id, output);
            // U-065: a Builder bot in its station buys a class upgrade the team stock can pay.
            Stations.TakeBotBuy(w, id, output);
        }
    }
}

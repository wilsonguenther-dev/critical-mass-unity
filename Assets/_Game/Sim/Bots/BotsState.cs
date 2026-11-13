using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>Per-world bot state (CM-09), kept out of World; per-actor arrays allocated once at ActorCapacity, reset in place.</summary>
    public sealed class BotsState
    {
        public const int BotSeedOffset = 7919;

        public const int RoleHill = 0, RoleKaiju = 1, RoleFightKaiju = 2;

        /// <summary>Target value meaning "the Kaiju" (actor ids are >= 0, -1 = none).</summary>
        public const int TargetKaiju = -2;

        /// <summary>World.Epoch / ActorCount this state was reset for; a mismatch (or the clock going back) resets it.</summary>
        public int Epoch;
        public int ActorCount;
        public int LastTick;
        public Rng Rng;
        public BotFields Fields;
        /// <summary>World.Tick of the last FillBotInput(w, 0) call (autopilot); actor 0 is a bot only while this is fresh.</summary>
        public int HumanDrivenTick;
        public double NextDirectorTime;
        /// <summary>Ticks between two thinks of one bot: round(simHz / thinkHz).</summary>
        public int ThinkPeriod;
        public byte[] Role;
        /// <summary>Match.Time of the last role change (-Infinity = never assigned).</summary>
        public double[] RoleSince;
        /// <summary>1 = this kaiju-role bot fetches corruption instead of biomass.</summary>
        public byte[] Corrupt;
        /// <summary>Match.Time the current AiState was entered.</summary>
        public double[] StateSince;
        /// <summary>Float32 storage (browser Float32Array).</summary>
        public double[] Aggression;
        /// <summary>Float32 storage (browser Float32Array).</summary>
        public double[] Greed;
        /// <summary>Stuck detector: last position that moved >= 0.3 away (Float32 storage), and when.</summary>
        public double[] AnchorX;
        public double[] AnchorZ;
        public double[] AnchorTime;
        /// <summary>CM-30 progress detector: last think position of a travelling bot that moved >= 1.0 away (Float32 storage), and when.</summary>
        public double[] ProgressX;
        public double[] ProgressZ;
        public double[] ProgressTime;
        /// <summary>1 when the last FillBotInput asked this bot to move.</summary>
        public byte[] WantMove;
        /// <summary>Float32 storage (browser Float32Array).</summary>
        public double[] UnstickX;
        public double[] UnstickZ;
        public double[] UnstickUntil;
        /// <summary>Fight target: actor id, TargetKaiju, or -1.</summary>
        public int[] Target;
        /// <summary>Gather / corrupt node id (-1 = none).</summary>
        public int[] TargetNode;
        /// <summary>Hill hold post (Float32 storage).</summary>
        public double[] PostX;
        public double[] PostZ;
        public double[] PostUntil;
        /// <summary>Pending ability press (decided at think time, sent by the next FillBotInput) and its aim point (Float32 storage).</summary>
        public byte[] AbilityReq;
        public double[] AbilityX;
        public double[] AbilityZ;
        /// <summary>Count of AiState changes written by the FSM (debug / tests).</summary>
        public int StateChanges;
        /// <summary>U-063: the allied carrier an AiState.Escort bot walks with (-1 = none).</summary>
        public int[] EscortOf;

        /// <summary>Browser createBotsState: allocate every per-actor array at ActorCapacity (no reset).</summary>
        public static BotsState CreateBotsState(World w)
        {
            int cap = World.ActorCapacity(w.Config);
            return new BotsState
            {
                Epoch = -1,
                ActorCount = -1,
                LastTick = 0,
                Rng = Rng.CreateRng(0),
                Fields = BotFields.CreateFields(w),
                HumanDrivenTick = -10,
                NextDirectorTime = 0,
                ThinkPeriod = Math.Max(1, (int)JsMath.Round((double)w.Config.SimHz / w.Config.Bots.ThinkHz)),
                Role = new byte[cap],
                RoleSince = new double[cap],
                Corrupt = new byte[cap],
                StateSince = new double[cap],
                Aggression = new double[cap],
                Greed = new double[cap],
                AnchorX = new double[cap],
                AnchorZ = new double[cap],
                AnchorTime = new double[cap],
                ProgressX = new double[cap],
                ProgressZ = new double[cap],
                ProgressTime = new double[cap],
                WantMove = new byte[cap],
                UnstickX = new double[cap],
                UnstickZ = new double[cap],
                UnstickUntil = new double[cap],
                Target = new int[cap],
                TargetNode = new int[cap],
                PostX = new double[cap],
                PostZ = new double[cap],
                PostUntil = new double[cap],
                AbilityReq = new byte[cap],
                AbilityX = new double[cap],
                AbilityZ = new double[cap],
                StateChanges = 0,
                EscortOf = new int[cap],
            };
        }

        /// <summary>Restart in place for the world's current match: reseed, redraw personalities, clear roles, rebuild every field.</summary>
        public static void ResetBotsState(BotsState st, World w)
        {
            st.Epoch = w.Epoch;
            st.ActorCount = w.ActorCount;
            st.LastTick = w.Tick;
            Rng.SeedRng(st.Rng, unchecked((uint)(w.Opts.Seed + BotSeedOffset)));
            st.HumanDrivenTick = -10;
            st.NextDirectorTime = w.Match.Time;
            st.StateChanges = 0;
            int cap = World.ActorCapacity(w.Config);
            for (int id = 0; id < cap; id++)
            {
                // Personality: drawn once per match, in id order, from the bot Rng (0.8..1.2 each).
                st.Aggression[id] = JsMath.F32(0.8 + 0.4 * Rng.NextFloat(st.Rng));
                st.Greed[id] = JsMath.F32(0.8 + 0.4 * Rng.NextFloat(st.Rng));
                st.Role[id] = RoleHill;
                st.RoleSince[id] = double.NegativeInfinity;
                st.Corrupt[id] = 0;
                st.StateSince[id] = double.NegativeInfinity;
                st.AnchorX[id] = JsMath.F32(w.X[id]);
                st.AnchorZ[id] = JsMath.F32(w.Z[id]);
                st.AnchorTime[id] = w.Match.Time;
                st.ProgressX[id] = JsMath.F32(w.X[id]);
                st.ProgressZ[id] = JsMath.F32(w.Z[id]);
                st.ProgressTime[id] = w.Match.Time;
                st.WantMove[id] = 0;
                st.UnstickX[id] = JsMath.F32(0);
                st.UnstickZ[id] = JsMath.F32(0);
                st.UnstickUntil[id] = 0;
                st.Target[id] = -1;
                st.TargetNode[id] = -1;
                st.PostX[id] = JsMath.F32(w.Map.Hill.X);
                st.PostZ[id] = JsMath.F32(w.Map.Hill.Z);
                st.PostUntil[id] = 0;
                st.AbilityReq[id] = 0;
                st.EscortOf[id] = -1;
                st.AbilityX[id] = JsMath.F32(0);
                st.AbilityZ[id] = JsMath.F32(0);
            }
            BotFields.ResetFields(st.Fields, w);
            BotFields.BuildAllFields(st.Fields, w);
        }

        /// <summary>Actor 0 is bot-driven only while the autopilot asked FillBotInput for it this tick or the previous one.</summary>
        public static bool HumanDriven(BotsState st, World w)
        {
            return st.HumanDrivenTick >= w.Tick - 1;
        }

        /// <summary>True when actor id is driven by the bots right now.</summary>
        public static bool IsBot(BotsState st, World w, int id)
        {
            return id != 0 || HumanDriven(st, w);
        }
    }
}

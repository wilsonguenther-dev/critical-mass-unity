using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>
    /// DG-21: a whole-match state hash (FNV-1a) for "same behaviour as before" checks. Mixes the tick, the grid, every actor's
    /// position, hp, life, class, FSM state, kills and deaths, both teams' hill seconds, delivered biomass and the event count,
    /// so any changed decision of any bot shows. Actor 0 runs on autopilot (MatchTick.Advance with no human frame).
    /// </summary>
    public static class MatchHash
    {
        /// <summary>Run seed at teamSize for ticks sim ticks from GameConfig.CreateBrowser5v5() and return the hash; tweak edits the options first.</summary>
        public static ulong Run(int seed, int teamSize, int ticks, ClassId human, Action<WorldOptions> tweak = null, Action<GameConfig> cfgTweak = null)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            cfgTweak?.Invoke(cfg);
            var opts = new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = human, Config = cfg };
            tweak?.Invoke(opts);
            World w = World.CreateWorld(opts);
            InputFrame scratch = SimInput.EmptyInput();
            for (int t = 0; t < ticks && w.Match.Phase != MatchPhase.Ended; t++) MatchTick.Advance(w, null, scratch);
            return Of(w);
        }

        /// <summary>The hash of the world as it stands.</summary>
        public static ulong Of(World w)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v)
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }
            Mix(w.Tick);
            Mix(w.GridVersion);
            Mix(w.WallVersion);
            Mix(w.EventHead);
            for (int i = 0; i < w.Map.Cells.Length; i++) Mix(w.Map.Cells[i]);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                Mix(BitConverter.DoubleToInt64Bits(w.X[i]));
                Mix(BitConverter.DoubleToInt64Bits(w.Z[i]));
                Mix(BitConverter.DoubleToInt64Bits(a.Hp));
                Mix(a.Alive ? 1 : 0);
                Mix((long)a.Cls);
                Mix((long)a.AiState);
                Mix(a.Kills);
                Mix(a.Deaths);
            }
            Mix(BitConverter.DoubleToInt64Bits(w.Match.HillSeconds.Azure));
            Mix(BitConverter.DoubleToInt64Bits(w.Match.HillSeconds.Crimson));
            Mix(BitConverter.DoubleToInt64Bits(w.Match.Delivered[0].Biomass));
            Mix(BitConverter.DoubleToInt64Bits(w.Match.Delivered[1].Biomass));
            Mix((long)w.Match.Winner);
            return h;
        }
    }
}

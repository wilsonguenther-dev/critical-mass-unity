using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net
{
    /// <summary>
    /// The fingerprint two machines compare to prove they hold the same world (FNV-1a, 64 bit, over raw double bits). It
    /// mixes everything a divergence shows up in within a tick or two: tick, rng state, grid, every actor's position,
    /// velocity, hp, life, class, team, human flag, kills and deaths, the kaiju, the hill and delivery tallies. Read-only,
    /// no allocation. Cost: about 8 k map cells plus 32 actors, run once every HashEvery ticks.
    /// </summary>
    public static class StateHash
    {
        public static ulong Of(World w)
        {
            ulong h = 14695981039346656037UL;
            Mix(ref h, w.Tick);
            Mix(ref h, w.Rng.State);
            Mix(ref h, w.GridVersion);
            Mix(ref h, w.WallVersion);
            Mix(ref h, w.EventHead);
            Mix(ref h, Bits(w.Match.Time));
            Mix(ref h, (long)w.Match.Phase);
            Mix(ref h, (long)w.Match.Winner);
            Mix(ref h, Bits(w.Match.HillSeconds.Azure));
            Mix(ref h, Bits(w.Match.HillSeconds.Crimson));
            Mix(ref h, Bits(w.Match.Delivered[0].Biomass));
            Mix(ref h, Bits(w.Match.Delivered[1].Biomass));
            byte[] cells = w.Map.Cells;
            for (int i = 0; i < cells.Length; i++) Mix(ref h, cells[i]);
            // b15 terrain height: the lift layer is sim state (null, so not mixed, in Classic and PhysicsOnly).
            sbyte[] lift = w.Map.Lift;
            if (lift != null) for (int i = 0; i < lift.Length; i++) Mix(ref h, lift[i]);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                Mix(ref h, Bits(w.X[i]));
                Mix(ref h, Bits(w.Z[i]));
                Mix(ref h, Bits(w.Vx[i]));
                Mix(ref h, Bits(w.Vz[i]));
                Mix(ref h, Bits(a.Hp));
                Mix(ref h, Bits(a.Facing));
                Mix(ref h, a.Alive ? 1 : 0);
                Mix(ref h, a.IsHuman ? 1 : 0);
                Mix(ref h, (long)a.Cls);
                Mix(ref h, (long)a.Team);
                Mix(ref h, a.Kills);
                Mix(ref h, a.Deaths);
            }
            if (w.Kaiju != null)
            {
                Mix(ref h, Bits(w.Kaiju.X));
                Mix(ref h, Bits(w.Kaiju.Z));
                Mix(ref h, Bits(w.Kaiju.Hp));
            }
            return h;
        }

        private static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);

        private static void Mix(ref ulong h, long v)
        {
            unchecked
            {
                for (int k = 0; k < 8; k++)
                {
                    h ^= (byte)(v >> (8 * k));
                    h *= 1099511628211UL;
                }
            }
        }
    }
}

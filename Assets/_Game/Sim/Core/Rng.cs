using System.Collections.Generic;

namespace CriticalMass.Sim.Core
{
    /// <summary>Seeded mulberry32 RNG. The state is a plain number so it serialises with the world. The sim uses no other randomness.</summary>
    public sealed class Rng
    {
        public uint State;

        public static Rng CreateRng(uint seed)
        {
            return new Rng { State = seed };
        }

        /// <summary>Reseed in place (used by ResetWorld so the Rng object identity survives a restart).</summary>
        public static void SeedRng(Rng r, uint seed)
        {
            r.State = seed;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public static double NextFloat(Rng r)
        {
            unchecked
            {
                r.State += 0x6D2B79F5u;
                uint t = r.State;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        /// <summary>Uniform integer in [minIncl, maxExcl). Returns minIncl when the range is empty.</summary>
        public static int NextInt(Rng r, int minIncl, int maxExcl)
        {
            int span = maxExcl - minIncl;
            if (span <= 0) return minIncl;
            return minIncl + JsMath.FloorToInt(NextFloat(r) * span);
        }

        /// <summary>Uniformly pick one element. The list must be non-empty.</summary>
        public static T Pick<T>(Rng r, IReadOnlyList<T> arr)
        {
            return arr[NextInt(r, 0, arr.Count)];
        }
    }
}

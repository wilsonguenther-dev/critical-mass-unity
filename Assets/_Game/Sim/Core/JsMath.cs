using System;

namespace CriticalMass.Sim.Core
{
    /// <summary>JavaScript Math semantics the port needs where System.Math differs (conventions 6.1, 6.2, 6.3).</summary>
    public static class JsMath
    {
        /// <summary>JS Math.round: floor, then +1 when the remainder is at least 0.5. NaN and infinities pass through.</summary>
        public static double Round(double v)
        {
            double f = Math.Floor(v);
            return (v - f >= 0.5) ? f + 1 : f;
        }

        /// <summary>Math.floor used as an integer: int.MinValue for NaN, infinities and values outside int range.</summary>
        public static int FloorToInt(double v)
        {
            double f = Math.Floor(v);
            if (!(f >= int.MinValue && f <= int.MaxValue)) return int.MinValue;
            return (int)f;
        }

        /// <summary>JS Math.fround: round to the nearest float32 (ties to even), held as a double.</summary>
        public static double F32(double v)
        {
            return (double)(float)v;
        }
    }
}

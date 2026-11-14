using CriticalMass.Sim.Core;

namespace CriticalMass.Net
{
    /// <summary>
    /// A scripted stand-in for a human (headless online tests and the -onlineAutoplay flag). It only READS the world: it
    /// steers toward the hill with a slow seeded weave, aims ahead, and presses attack and ability on a rhythm. It never
    /// calls the bot brain, whose state lives in the world and would change only on the machine that called it.
    /// </summary>
    public sealed class AutoplayPilot
    {
        private readonly uint _seed;

        public AutoplayPilot(uint seed) { _seed = seed == 0 ? 1u : seed; }

        public void Fill(World w, int actor, int tick, InputFrame f)
        {
            double x = w.X[actor], z = w.Z[actor];
            double hx = w.Map.Hill.X, hz = w.Map.Hill.Z;
            double dx = hx - x, dz = hz - z;
            double len = System.Math.Sqrt(dx * dx + dz * dz);
            uint h = Hash((uint)(tick / 45) ^ _seed);
            // A weave of up to +-0.7 sideways that changes every 1.5 s.
            double side = ((h & 0xFFFF) / 65535.0 - 0.5) * 1.4;
            double mx = len > 1e-6 ? dx / len : 0, mz = len > 1e-6 ? dz / len : 0;
            f.MoveX = mx - mz * side;
            f.MoveZ = mz + mx * side;
            if (len < 4) { f.MoveX *= 0.3; f.MoveZ *= 0.3; }
            f.AimX = x + f.MoveX * 6;
            f.AimZ = z + f.MoveZ * 6;
            f.Attack = (tick % 20) < 6;
            f.Ability = (h >> 20) % 7 == 0 && tick % 45 == 0;
            f.ClassPick = -1;
        }

        private static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352d;
                x ^= x >> 15; x *= 0x846ca68b;
                x ^= x >> 16;
                return x;
            }
        }
    }
}

using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>One barricade slot (browser Barricade).</summary>
    public sealed class Barricade
    {
        public bool Active;
        public int Owner;
        public TeamId Team;
        public int Cx;
        public int Cz;
        public double Hp;
        public double Ttl;
        /// <summary>Cell type under the barricade (Floor or Bridge), restored when it expires or breaks (CM-05).</summary>
        public byte PrevCell;
    }
}

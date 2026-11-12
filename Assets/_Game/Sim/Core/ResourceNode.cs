namespace CriticalMass.Sim.Core
{
    /// <summary>A biomass or corruption node on the map (browser ResourceNode).</summary>
    public sealed class ResourceNode
    {
        public int Id;
        public NodeKind Kind;
        public double X;
        public double Z;
        public int Charges;
        public int MaxCharges;
        public double RegrowIn;
    }

    // ITEM CM-35: added by its owner (LooseOrb)
}

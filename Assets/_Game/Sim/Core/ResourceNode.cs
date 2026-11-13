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

    /// <summary>
    /// CM-35: a loose meat orb on the ground (browser LooseOrb). A killed actor leaves one at its corpse (Combat.DropCorpseMeat);
    /// a living actor with MeatPickup, not carrying corruption and with room in its meat load, walking within
    /// Resources.MeatPickupRange of it picks it up (no E). Fixed pool World.Orbs; inactive slots have Active = false.
    /// Orbs are not ResourceNodes, so bots allocate no flow field for them.
    /// </summary>
    public sealed class LooseOrb
    {
        public bool Active;
        public BiomassType Kind;
        public double X;
        public double Z;
        /// <summary>Biomass units left in the orb.</summary>
        public int Units;
        /// <summary>Sim ticks left before the orb rots away.</summary>
        public int Ttl;
        /// <summary>Actor id of the body it dropped from (-1 none).</summary>
        public int Source;
    }
}

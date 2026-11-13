namespace CriticalMass.Sim.Core
{
    /// <summary>One entry of the world event ring (browser SimEvent).</summary>
    public sealed class SimEvent
    {
        public SimEventType Type;
        public int A;
        public int B;
        public double X;
        public double Z;
        public double Value;
        /// <summary>CM-35: the biomass type of a DeliverBiomass event (a mixed load raises one event per type); null on every other event.</summary>
        public BiomassType? Bio;
        /// <summary>DG-16: what dealt the blow on Hit and Death events (None on every other event).</summary>
        public DamageCause Cause;
        /// <summary>DG-22: the effect radius (world units) of a MinePlaced / MineArmed / MineTriggered / MineRemoved / TntThrown / TntLanded / Explosion event; 0 on every other event.</summary>
        public double Radius;
        /// <summary>U-066: the World.Depth.Pickups slot of a PickupSpawned / PickupTaken / PickupExpired event; -1 on every other event.</summary>
        public int Slot = -1;
    }
}

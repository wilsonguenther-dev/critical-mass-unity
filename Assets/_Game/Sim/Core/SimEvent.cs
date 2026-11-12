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
        // ITEM CM-35: added by its owner (Bio: biomass type of a DeliverBiomass event)
    }
}

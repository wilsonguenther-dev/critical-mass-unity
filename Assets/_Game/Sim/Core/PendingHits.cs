namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// DG-67 (S4, not in the browser): the melee hits of one tick, collected before any is applied so every actor alive at
    /// the start of the tick gets its swing and two lethal swings on one tick kill both. Fixed capacity (ActorCapacity
    /// squared: every actor hitting every other once), allocated at world creation, never grown.
    /// </summary>
    public sealed class PendingHits
    {
        public int Count;
        public int[] Target;
        public int[] Source;
        public double[] Amount;
        public double[] SrcX;
        public double[] SrcZ;

        public static PendingHits Create(int actorCapacity)
        {
            int n = actorCapacity * actorCapacity;
            return new PendingHits
            {
                Count = 0,
                Target = new int[n],
                Source = new int[n],
                Amount = new double[n],
                SrcX = new double[n],
                SrcZ = new double[n],
            };
        }

        /// <summary>Queue one hit; false (dropped) only if the buffer is full, which ActorCapacity squared rules out.</summary>
        public bool Add(int target, int source, double amount, double srcX, double srcZ)
        {
            if (Count >= Target.Length) return false;
            Target[Count] = target;
            Source[Count] = source;
            Amount[Count] = amount;
            SrcX[Count] = srcX;
            SrcZ[Count] = srcZ;
            Count++;
            return true;
        }
    }
}

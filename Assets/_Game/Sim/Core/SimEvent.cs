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
        // ---- Sim-physics lane (appended): the world-force fields (PHYSICS-BUILD-LIST contract C1).
        /// <summary>Unit push direction of a Hit / Knock / AttackStart / AttackContact / MeatPop event; (0, 0) = radial (slam, blast).</summary>
        public double DirX;
        public double DirZ;
        /// <summary>The sim's knock distance in u on Hit / Knock / KaijuSlam / Explosion / PillarCrush / BodyShove events (0 = none).</summary>
        public double Impulse;
        /// <summary>Hit tier on Hit and HitStun events: 0 light, 1 heavy, 2 critical or finishing (PR #30's thresholds).</summary>
        public int Tier;
        /// <summary>Wind-up ticks of an AttackStart / KaijuMeleeWindup / KaijuTelegraph event.</summary>
        public int Windup;
        /// <summary>Recovery ticks of an AttackStart event.</summary>
        public int Recovery;
    }
}

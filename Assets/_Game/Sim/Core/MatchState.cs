namespace CriticalMass.Sim.Core
{
    /// <summary>Units delivered by one team (browser MatchState.delivered[team]).</summary>
    public sealed class DeliveredTally
    {
        public double Biomass;
        public double Corruption;
        /// <summary>CM-35: Biomass = Plant + Meat; these split it by type.</summary>
        public double Plant;
        public double Meat;
    }

    /// <summary>Match-level state (browser MatchState).</summary>
    public sealed class MatchState
    {
        /// <summary>Seconds elapsed.</summary>
        public double Time;
        public MatchPhase Phase;
        public PerTeam HillSeconds;
        public HillState HillState;
        public PerTeam HillCount;
        public PerTeam Allegiance;
        /// <summary>Indexed by (int)TeamId.</summary>
        public DeliveredTally[] Delivered;
        public double Instability;
        public bool BerserkLocked;
        public bool Hatched;
        public MatchResult Winner;
    }
}

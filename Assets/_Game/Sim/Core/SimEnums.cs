namespace CriticalMass.Sim.Core
{
    /// <summary>What an actor carries in its class cargo (browser CarryKind).</summary>
    public enum CarryKind { None, Biomass, Corruption }

    // ITEM CM-35: added by its owner (BiomassType { Plant, Meat })

    /// <summary>Kaiju state (browser KaijuState).</summary>
    public enum KaijuState { Egg, Neutral, Azure, Crimson, Berserk }

    /// <summary>Hill ownership state (browser HillState).</summary>
    public enum HillState { Neutral, Azure, Crimson, Contested }

    /// <summary>Bot FSM state shown on the actor (browser AiState).</summary>
    public enum AiState { None, ToHill, HoldHill, Fight, Gather, Deliver, Corrupt, Retreat, Unstick }

    /// <summary>Match phase (browser MatchState.phase).</summary>
    public enum MatchPhase { Playing, Ended }

    /// <summary>Match winner (browser TeamId | "draw" | null; None = null).</summary>
    public enum MatchResult { None, Azure, Crimson, Draw }

    /// <summary>Resource node kind (browser ResourceNode.kind).</summary>
    public enum NodeKind { Biomass, Corruption }

    /// <summary>Sim event type (browser SimEventType). Never stored or compared by integer value: items append members.</summary>
    public enum SimEventType
    {
        Hit,
        Death,
        Respawn,
        Gather,
        DeliverBiomass,
        DeliverCorruption,
        Hatch,
        KaijuTelegraph,
        /// <summary>CM-26: a Kaiju melee wind-up started: A = target id, (X, Z) = the target, Value = kaiju.meleeWindupTicks.</summary>
        KaijuMeleeWindup,
        KaijuSlam,
        KaijuKO,
        KaijuRecover,
        WallBreak,
        BarricadePlaced,
        BarricadeBroken,
        HillOwnerChange,
        /// <summary>CM-28: the berserk lock was set before the hatch, raised once per match: (X, Z) = the shrine, Value = instability.</summary>
        BerserkLocked,
        Ability,
        // ITEM CM-30: added by its owner (Terraform)
        // ITEM CM-31: added by its owner (Topple)
        // ITEM CM-84: added by its owner (ClassSwitch, ports CM-54)
        // ITEM CM-37: added by its owner (Heal)
        MatchEnd,
    }
}

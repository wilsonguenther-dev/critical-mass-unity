namespace CriticalMass.Sim.Core
{
    /// <summary>What an actor carries in its class cargo (browser CarryKind).</summary>
    public enum CarryKind { None, Biomass, Corruption }

    /// <summary>
    /// CM-35: the two biomass types (browser BiomassType). Plant comes from the map's biomass nodes (gathered with E) and
    /// rides in the class cargo; meat drops from killed actors as a loose orb and rides in its own load (Actor.CarryMeat).
    /// </summary>
    public enum BiomassType { Plant, Meat }

    /// <summary>
    /// DG-16: what dealt a hit (SimEvent.Cause on Hit and Death events, Actor.LastCause). Not in the browser. Never stored or
    /// compared by integer value: later items append members (Explosion is reserved for DG-22).
    /// </summary>
    public enum DamageCause { None, Melee, KaijuMelee, KaijuSlam, Fall, Explosion }

    /// <summary>
    /// DG-21 bot skill level (WorldOptions.BotDifficulty / AllyBotDifficulty). Normal is first so a WorldOptions that never
    /// sets it plays exactly the browser bots. Never compared by integer value.
    /// </summary>
    public enum BotDifficulty { Normal, Easy, Hard }

    /// <summary>Kaiju state (browser KaijuState).</summary>
    public enum KaijuState { Egg, Neutral, Azure, Crimson, Berserk }

    /// <summary>Hill ownership state (browser HillState).</summary>
    public enum HillState { Neutral, Azure, Crimson, Contested }

    /// <summary>Bot FSM state shown on the actor (browser AiState).</summary>
    public enum AiState
    {
        None, ToHill, HoldHill, Fight, Gather, Deliver, Corrupt, Retreat, Unstick,
        /// <summary>CM-36: walk to the shrine empty-handed and hold interact for the shrine cleanse (appended: the browser lists it before Retreat).</summary>
        Cleanse,
    }

    /// <summary>Match phase (browser MatchState.phase).</summary>
    public enum MatchPhase { Playing, Ended }

    /// <summary>Match winner (browser TeamId | "draw" | null; None = null).</summary>
    public enum MatchResult { None, Azure, Crimson, Draw }

    /// <summary>DG-09: what a miner's F press does (U-053): dig into the carry, or place from it. Never compared by integer value.</summary>
    public enum TerraformMode { Dig, Place }

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
        /// <summary>
        /// CM-30 miner dig / place, one per attempt: A = miner, B = cell index (-1 = none), (X, Z) = the cell centre (the aim
        /// point when there is no cell), Value = Terraform.Dug / Terraform.Placed or a negative Terraform.Refused* code.
        /// </summary>
        Terraform,
        /// <summary>
        /// CM-31 a decoration starts to fall (the grid changes this step; the view animates Topple.FallTicks): A = attacker
        /// (-1 = none), B = decoration index, (X, Z) = base cell centre, Value = Topple.Ramp / Topple.Block or a negative
        /// Topple.Refused* code (it did not fall).
        /// </summary>
        Topple,
        /// <summary>DG-09: a swing chipped a standing decoration: A = attacker, B = decoration, (X, Z) = base cell centre, Value = hits left.</summary>
        ToppleHit,
        /// <summary>DG-09: a fallen decoration finished falling (Topple.FallTicks after its Topple event): B = decoration, (X, Z) = the far end of its footprint, Value = mode.</summary>
        ToppleDown,
        /// <summary>DG-09 fall rule: a grounded actor lost the ground under its centre: A = actor, B = cell fallen into, (X, Z) = where it climbs out, Value = get-up seconds.</summary>
        Fall,
        /// <summary>DG-16 (view request): an actor started a melee swing: A = attacker, B = -1, (X, Z) = the swing's reach point (attacker + facing x AttackRange), Value = facing (radians).</summary>
        AttackStart,
        /// <summary>DG-16 (view request): a swing hurt the hatched Kaiju: A = attacker, B = -1, (X, Z) = the contact point on the Kaiju's hit radius toward the attacker, Value = damage dealt.</summary>
        KaijuDamaged,
        /// <summary>
        /// CM-64: an enemy hit landed on a carrier during its delivery wind-up (Delivery.InterruptDelivery): A = the carrier,
        /// B = the attacker (an actor id, else -1), (X, Z) = the carrier, Value = the wind-up ticks it had reached.
        /// </summary>
        DeliverIntercepted,
        /// <summary>CM-36: a shrine cleanse started (Cleanse.StartCleanse): A = the cleanser (-1 scripted), B = team index (0 azure, 1 crimson), (X, Z) = the shrine, Value = instability it will drain.</summary>
        Cleanse,
        /// <summary>CM-36: the cleanse in flight finished: A = cleanser (-1 scripted), B = team index, (X, Z) = the shrine, Value = instability drained.</summary>
        CleanseEnd,
        // ITEM CM-84: added by its owner (ClassSwitch, ports CM-54)
        /// <summary>CM-37: one tick of fountain healing (Heal.HealSystem): A = the healed actor, B = -1, (X, Z) = the actor, Value = HP restored this tick (&gt; 0).</summary>
        Heal,
        MatchEnd,
    }
}

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
    public enum DamageCause { None, Melee, KaijuMelee, KaijuSlam, Fall, Explosion, Crush }

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
        /// <summary>
        /// DG-16 (view request), moved by DG-69 to the PRESS tick: an attack or ability press was accepted: A = attacker, B = kind
        /// (Combat.KindAttack = -1 for a melee swing, else an ability Combat.Kind*), (X, Z) = the reach point (attacker + facing x
        /// AttackRange), Value = locked facing (radians), Windup / Recovery = ticks (0 = the hit is this tick), DirX / DirZ = facing.
        /// </summary>
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
        /// <summary>DG-22: a land mine went down: A = owner, B = mine slot, (X, Z) = its cell centre, Value = the tick it arms, Radius = blast radius.</summary>
        MinePlaced,
        /// <summary>DG-22: a mine armed (the ring warning): A = owner, B = mine slot, (X, Z) = cell centre, Value = the tick it expires, Radius = blast radius.</summary>
        MineArmed,
        /// <summary>DG-22: an enemy stepped on an armed mine: A = that actor, B = the owner, (X, Z) = cell centre, Value = mine slot, Radius = blast radius (an Explosion follows).</summary>
        MineTriggered,
        /// <summary>DG-22: a mine left without a blast: A = owner, B = mine slot, (X, Z) = cell centre, Value = Demolitions.MineExpired / MineGroundGone / MineRecycled, Radius = blast radius.</summary>
        MineRemoved,
        /// <summary>DG-22: TNT left the thrower's hand: A = thrower, B = bomb slot, (X, Z) = where it will land, Value = the tick it lands, Radius = blast radius.</summary>
        TntThrown,
        /// <summary>DG-22: thrown TNT touched down (fuse burning): A = thrower, B = bomb slot, (X, Z) = where it lies, Value = the tick it blows, Radius = blast radius.</summary>
        TntLanded,
        /// <summary>DG-22: an explosion (Effects.Explode): A = owner, B = Demolitions.ExplosionMine / ExplosionTnt, (X, Z) = centre, Value = actors hurt, Radius = blast radius, Cause = Explosion.</summary>
        Explosion,
        /// <summary>
        /// DG-24: a ladder or rope bridge went down (built by a Builder or put down by a carrier): A = that actor, B = piece slot
        /// (World.Builder.Pieces: Kind, cells, direction), (X, Z) = the span's centre, Value = the tick it crumbles, Radius = half
        /// its length in units.
        /// </summary>
        PiecePlaced,
        /// <summary>DG-24: a piece starts to crumble (the warning, BuilderPieces.WarnTicks before the end): A = owner, B = slot, (X, Z) = centre, Value = ticks left, Radius = half length.</summary>
        PieceCrumbling,
        /// <summary>DG-24: a piece left the map: A = owner, B = slot, (X, Z) = centre, Value = BuiltPieces.EndExpired / EndDropped / EndBroken, Radius = half length.</summary>
        PieceExpired,
        /// <summary>DG-24: a piece's cell was dug or blown out (it breaks; the rest follows as PieceExpired EndBroken): A = owner, B = slot, (X, Z) = the broken cell, Value = cells it had, Radius = half length.</summary>
        PieceDestroyed,
        /// <summary>DG-24: an actor (any class) picked a piece up to carry it: A = carrier, B = slot, (X, Z) = the span's centre, Value = ticks it has left, Radius = half length.</summary>
        PiecePickedUp,
        /// <summary>DG-40: a Healer cast: A = the Healer, B = Healer.KindMend / KindPulse, (X, Z) = the mended ally or the pulse centre, Value = HP it restored in all, Radius = the pulse radius (0 for a mend).</summary>
        HealCast,
        /// <summary>DG-40: Healer healing landed on one actor: A = that actor, B = the Healer, (X, Z) = the actor, Value = HP restored (&gt; 0).</summary>
        HealLanded,
        /// <summary>DG-40: a Healer purified the egg: A = the Healer, B = team index (0 azure, 1 crimson), (X, Z) = the shrine, Value = instability removed, Radius = Healer.PurifyRange.</summary>
        Purify,
        /// <summary>
        /// DG-69: a swing reached its hit tick (press tick + Windup), even on a whiff (whoosh, trail, hit-stop key off it):
        /// A = attacker, B = -1, (X, Z) = the reach point along the locked facing, Value = actors hit, DirX / DirZ = locked facing.
        /// </summary>
        AttackContact,
        /// <summary>DG-70: a hit-stun during the wind-up cancelled a swing (no damage): A = actor, B = who stunned it (-2 Kaiju), (X, Z) = the actor.</summary>
        AttackCancel,
        /// <summary>DG-70: a victim was staggered: A = victim, B = source, (X, Z) = victim, Value = stun ticks, Tier = hit tier, Cause.</summary>
        HitStun,
        /// <summary>
        /// DG-70: a body was pushed (melee knock, claw, slam, blast, crush): A = victim, B = source, (X, Z) = victim, DirX / DirZ =
        /// push direction, Impulse = knock distance in u after mass and shield, Value = 0, Cause = what pushed it.
        /// </summary>
        Knock,
        /// <summary>DG-86: the Kaiju's body shoved fighters this tick: A = -2, B = -1, (X, Z) = the Kaiju, Value = bodies shoved, Radius = body radius, DirX / DirZ = its heading, Impulse = largest shove (u).</summary>
        BodyShove,
        /// <summary>DG-99: a landing pillar crushed an actor: A = victim, B = decoration, (X, Z) = where it ends, DirX / DirZ = push out of the strip, Impulse = shove (u), Value = damage.</summary>
        PillarCrush,
        /// <summary>DG-100: a meat orb popped from a corpse: A = corpse actor, B = orb slot, (X, Z) = where it lands, DirX / DirZ = hop direction, Radius = hop length.</summary>
        MeatPop,
        // ITEM CM-84: added by its owner (ClassSwitch, ports CM-54)
        /// <summary>CM-37: one tick of fountain healing (Heal.HealSystem): A = the healed actor, B = -1, (X, Z) = the actor, Value = HP restored this tick (&gt; 0).</summary>
        Heal,
        MatchEnd,
    }
}

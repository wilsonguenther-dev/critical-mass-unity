using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>The cargo destroyed at an actor's last death (Kind None, Amount 0 when it carried nothing).</summary>
    public sealed class LostCargo
    {
        public CarryKind Kind;
        public int Amount;
    }

    /// <summary>One actor (browser Actor). Positions are not here; they live in arrays on World indexed by actor id.</summary>
    public sealed class Actor
    {
        public int Id;
        public TeamId Team;
        public ClassId Cls;
        public bool IsHuman;
        public bool Alive;
        public double Hp;
        public double MaxHp;
        /// <summary>Radians, 0 = +x, atan2(dz, dx).</summary>
        public double Facing;
        public double AttackCd;
        public double AbilityCd;
        /// <summary>Seconds remaining of shield etc.</summary>
        public double AbilityActive;
        public double SlowTimer;
        public double SpawnProtect;
        public double RespawnIn;
        public CarryKind Carry;
        public int CarryAmount;
        /// <summary>
        /// CM-35: meat units in the actor's own meat load (0..Resources.MeatCarryCapacity), separate from the class cargo:
        /// Carry / CarryAmount never include meat, so every cargo-driven bot decision is unchanged by it. Zeroed while dead,
        /// after the hatch (ResourcesSystem) and on reset.
        /// </summary>
        public int CarryMeat;
        /// <summary>CM-35: may this actor take meat by walking over a loose orb? True by default; bots set it each step to "on the Kaiju role" (BotBrain).</summary>
        public bool MeatPickup;
        public double GatherProgress;
        /// <summary>-1 = none.</summary>
        public int GatherNodeId;
        /// <summary>Id of the last source that damaged this actor: an actor id, KaijuSource (-2) for the Kaiju melee, else -1.</summary>
        public int LastAttacker;
        /// <summary>CM-28: the cargo destroyed at this actor's last death. One object per actor, mutated in place.</summary>
        public LostCargo LostCargo;
        public int Kills;
        public int Deaths;
        public double HillSeconds;
        public AiState AiState;
        /// <summary>Seconds since last attack/ability, for the view.</summary>
        public double AnimAction;
        public bool Moving;
        /// <summary>Class to respawn as (CM-06). Set only while dead, via InputFrame.ClassPick; applied and cleared on respawn.</summary>
        public ClassId? PendingClass;
        // ITEM CM-84: added by its owner (ClassSwitchCd, ClassSwitches)
        /// <summary>CM-20: sim seconds left of the post-respawn get-up; while > 0 the actor ignores input and is never pushed.</summary>
        public double StandUp;
        /// <summary>
        /// CM-32: length in sim ticks of the current jump, 0 on the ground. Set at takeoff to Jump.AirTicks, or shorter when
        /// the landing guard (Jump.cs) shortens a jump that would land on a blocked cell. Cleared on landing, death and reset.
        /// </summary>
        public int AirLen;
        /// <summary>CM-32: ticks flown so far, 1..AirLen while airborne (the takeoff step is tick 1), 0 on the ground.</summary>
        public int AirTick;
        /// <summary>CM-32: takeoff point (the landing-blocked fallback) and planned landing point of the current jump.</summary>
        public double AirFromX;
        public double AirFromZ;
        public double AirToX;
        public double AirToZ;
        /// <summary>
        /// DG-02 jump buffer (not in the browser): sim ticks a jump press stays wanted, set to Jump.BufferTicks + 1 on a
        /// press and counted down once per movement step, so a press up to BufferTicks early (landing, getting up, a
        /// refused takeoff) fires on the first tick it can. 0 = no press pending.
        /// </summary>
        public int JumpBuffer;
        /// <summary>
        /// DG-02 coyote time (not in the browser): consecutive ticks this grounded actor has stood where its footprint no
        /// longer fits (the ground was removed or blocked under it). 0 = supported. A jump may start while this is at most
        /// Jump.CoyoteTicks.
        /// </summary>
        public int UnsupportedTicks;
        /// <summary>DG-16: what dealt the last blow (Melee, KaijuMelee, KaijuSlam...); None until hit, cleared at respawn with LastAttacker.</summary>
        public DamageCause LastCause;
        /// <summary>
        /// CM-64: sim ticks of the current delivery wind-up at the shrine (0 = not winding; delivers when it reaches
        /// KaijuFeed.DeliverWindupTicks). Delivery.Interrupted (-1) for the rest of the tick an enemy hit interrupted it.
        /// Written only by Delivery (Damage calls InterruptDelivery).
        /// </summary>
        public int DeliverTicks;

        // ---- Sim-physics lane (appended). Read by the view (animation, input buffer) without events.
        /// <summary>DG-69: World.Tick of the last accepted attack press (World.NeverTick = none yet).</summary>
        public int SwingTick;
        /// <summary>DG-69: wind-up ticks of that swing (the hit lands on SwingTick + SwingWindup; 0 = on the press tick).</summary>
        public int SwingWindup;
        /// <summary>DG-69: slowed recovery ticks after the hit.</summary>
        public int SwingRecovery;
        /// <summary>DG-69: facing locked at the press; the hit resolves along it.</summary>
        public double SwingFacing;
        /// <summary>DG-69: a swing is winding up and will resolve on SwingTick + SwingWindup.</summary>
        public bool SwingPending;
        /// <summary>DG-70: ticks of hit-stun left (input ignored, no attack, ability or jump; a wind-up is cancelled).</summary>
        public int HitStunTicks;
        /// <summary>DG-70: length of the current stagger (ticks) for the view's hit-reaction timing.</summary>
        public int HitStunLen;
        /// <summary>DG-70: knock velocity (u/s, Float32), added on top of the input velocity and decaying each tick.</summary>
        public double KVx;
        public double KVz;
    }
}

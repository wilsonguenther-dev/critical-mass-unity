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
        // ITEM CM-35: added by its owner (CarryMeat, MeatPickup)
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
        // ITEM CM-32: added by its owner (AirLen, AirTick, AirFromX, AirFromZ, AirToX, AirToZ)
    }
}

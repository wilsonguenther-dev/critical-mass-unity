namespace CriticalMass.Sim.Core
{
    /// <summary>One step of intent for one actor (browser InputFrame).</summary>
    public sealed class InputFrame
    {
        /// <summary>Move intent, each component in [-1, 1]; the magnitude is clamped to 1 by ApplyInput.</summary>
        public double MoveX;
        public double MoveZ;
        /// <summary>Aim point in world space.</summary>
        public double AimX;
        public double AimZ;
        public bool Attack;
        public bool Ability;
        public bool Interact;
        /// <summary>-1 = no pick, otherwise an index into ClassIds.All (consumed by CM-06 inside the sim).</summary>
        public int ClassPick;
        /// <summary>CM-30: miner dig / place (F). Held state; Terraform.TerraformSystem acts on its rising edge only.</summary>
        public bool Terraform;
        /// <summary>CM-32: start a jump this step (edge-latched by the human input layer; bots never set it). Read and cleared by Movement (Jump.cs).</summary>
        public bool Jump;
        /// <summary>
        /// DG-09 (U-053): switch the miner's dig / place mode this step (mouse wheel, LB / RB; edge-latched by the human input
        /// layer; bots never set it). Read and cleared by Terraform.TerraformSystem, so a stored true never switches twice.
        /// </summary>
        public bool TerraformSwitch;
        /// <summary>
        /// U-065: buy the next upgrade level of a class at the base station: class index + 1 (0 = no press, so a frame that
        /// never sets it buys nothing). One-shot: read and cleared by Stations.StationSystem. Only a Builder in its station succeeds.
        /// </summary>
        public int StationBuy;
        /// <summary>
        /// U-067: a ping, for ONE tick: (int)PingCall + 1 (0 = none) at (PingX, PingZ) in sim units. One-shot: read and
        /// cleared by PingRules.PingSystem, which enforces the rate limit.
        /// </summary>
        public int PingKind;
        public double PingX;
        public double PingZ;
        /// <summary>
        /// CM b13 [J]: dodge this step. One-shot: edge-latched by the human input layer, read and cleared by the dodge system
        /// (Sim/B13/Dodge.cs); bots set it through BotReflex. Wire bit 128 (InputCodec), replay button bit 64.
        /// </summary>
        public bool Dodge;   // b13-hook
        /// <summary>
        /// b16 CANCEL: cancel this step (a feint during the wind-up, a hit-cancel during the recovery after a landed hit).
        /// One-shot: edge-latched by the human input layer, read and cleared by Combat (CombatFeel.TakeCancel); bots set it only
        /// with BotBrain.UsesCancel. Wire bit 128 of the ping byte (InputCodec), replay button bit 128.
        /// </summary>
        public bool Cancel;
        /// <summary>
        /// Class moves T2-1 (lane 1): GUARD is held this step (hold = guard, a press edge inside the window = parry). HELD state,
        /// like Attack: the human input layer writes it every step (a tap shorter than a tick is latched for one step); the
        /// sim derives the press edge itself (MovesState.GuardPressed). Read only while MovesConfig.Guard is on. Wire bit 64 of
        /// the ping byte (InputCodec), replay picks byte bit 64 (ReplayInputStream). Hashed only when true (StateHash).
        /// </summary>
        public bool Guard;   // mv-hook
        /// <summary>
        /// Class moves T2-6 (lane 1): the Jump button is HELD this step (Jump itself is a one-shot press cleared by Movement).
        /// The Healer's glide reads it. Wire bit 32 of the ping byte, replay picks byte bit 32. Hashed only when true.
        /// </summary>
        public bool JumpHeld;   // mv-hook

        public void CopyFrom(InputFrame other)
        {
            MoveX = other.MoveX;
            MoveZ = other.MoveZ;
            AimX = other.AimX;
            AimZ = other.AimZ;
            Attack = other.Attack;
            Ability = other.Ability;
            Interact = other.Interact;
            ClassPick = other.ClassPick;
            Terraform = other.Terraform;
            Jump = other.Jump;
            TerraformSwitch = other.TerraformSwitch;
            StationBuy = other.StationBuy;
            PingKind = other.PingKind;
            PingX = other.PingX;
            PingZ = other.PingZ;
            Dodge = other.Dodge;   // b13-hook
            Cancel = other.Cancel;   // b16 cancel
            Guard = other.Guard;   // mv-hook
            JumpHeld = other.JumpHeld;   // mv-hook
        }
    }
}

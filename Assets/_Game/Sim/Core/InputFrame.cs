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
        /// <summary>CM-30: miner dig / place. Data only in Phase 0 (no system reads it).</summary>
        public bool Terraform;
        /// <summary>CM-32: start a jump this step. Data only in Phase 0 (no system reads it).</summary>
        public bool Jump;

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
        }
    }
}

using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>The netcode seam (browser input.ts): ApplyInput is the only way humans or bots influence the sim.</summary>
    public static class SimInput
    {
        /// <summary>A neutral frame: all zero, ClassPick -1, every flag false. Its aim (0, 0) is finite (conventions 6.14).</summary>
        public static InputFrame EmptyInput()
        {
            return new InputFrame
            {
                MoveX = 0,
                MoveZ = 0,
                AimX = 0,
                AimZ = 0,
                Attack = false,
                Ability = false,
                Interact = false,
                ClassPick = -1,
                Terraform = false,
                Jump = false,
                TerraformSwitch = false,
                StationBuy = 0,
                PingKind = 0,
            };
        }

        /// <summary>Copies a frame into world.Inputs[actorId] (no allocation), clamps the move magnitude to 1, sanitises non-finite numbers.</summary>
        public static void ApplyInput(World w, int actorId, InputFrame frame)
        {
            if (!(actorId >= 0 && actorId < w.Inputs.Length)) return;
            InputFrame dst = w.Inputs[actorId];
            bool finiteMove = double.IsFinite(frame.MoveX) && double.IsFinite(frame.MoveZ);
            double mx = finiteMove ? frame.MoveX : 0;
            double mz = finiteMove ? frame.MoveZ : 0;
            double len2 = mx * mx + mz * mz;
            if (len2 > 1)
            {
                double inv = 1 / System.Math.Sqrt(len2);
                mx *= inv;
                mz *= inv;
            }
            dst.MoveX = mx;
            dst.MoveZ = mz;
            bool aimOk = double.IsFinite(frame.AimX) && double.IsFinite(frame.AimZ);
            w.HasAim[actorId] = aimOk ? (byte)1 : (byte)0;
            dst.AimX = aimOk ? frame.AimX : w.X[actorId];
            dst.AimZ = aimOk ? frame.AimZ : w.Z[actorId];
            dst.Attack = frame.Attack == true;
            dst.Ability = frame.Ability == true;
            dst.Interact = frame.Interact == true;
            dst.Terraform = frame.Terraform == true;
            dst.Jump = frame.Jump == true;
            dst.TerraformSwitch = frame.TerraformSwitch == true;
            dst.StationBuy = frame.StationBuy >= 1 && frame.StationBuy <= ClassIds.All.Count ? frame.StationBuy : 0;
            dst.PingKind = frame.PingKind >= 1 && frame.PingKind <= 4 && !double.IsNaN(frame.PingX) && !double.IsNaN(frame.PingZ) ? frame.PingKind : 0;
            dst.PingX = frame.PingX;
            dst.PingZ = frame.PingZ;
            int cp = frame.ClassPick;
            dst.ClassPick = cp >= 0 && cp < ClassIds.All.Count ? cp : -1;
        }
    }
}

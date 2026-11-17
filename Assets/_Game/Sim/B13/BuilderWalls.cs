// CM b13 item B (Builder walls and blocks). S0 stub: identity until item B fills it.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>CM b13 item B: the Builder walls state on the World (World.Walls). S0 stub: identity until item B fills it.</summary>
    public sealed class WallsState
    {
        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w) { }

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w) { }
    }

    /// <summary>CM b13 item B: the Builder builds walls and blocks at a ghost. S0 stub: identity until item B fills it.</summary>
    public static class BuilderWalls
    {
        /// <summary>The rule is on (item B: DepthRules.On(w, w.Config.Depth.Build.On)).</summary>
        public static bool On(World w) => false;
        /// <summary>The team's wall tier, 0..2.</summary>
        public static int Tier(World w, TeamId team) => 0;
        /// <summary>The piece Builder id has selected.</summary>
        public static BuildPiece Piece(World w, int id) => BuildPiece.Wall;
        /// <summary>A Builder's F press: true = taken (build), so Terraform skips it.</summary>
        public static bool TakeBuilderPress(World w, int id) => false;
        /// <summary>A Builder's switch press: true = taken (switch piece), so Terraform skips it.</summary>
        public static bool TakeBuilderSwitch(World w, int id) => false;
        /// <summary>Per tick, after Barricades.BarricadesSystem.</summary>
        public static void WallsSystem(World w) { }

        /// <summary>The build ghost for Builder id: cell, direction, length, tall, will build. False = no ghost.</summary>
        public static bool Ghost(World w, int id, out int cx, out int cz, out int dx, out int dz, out int len, out bool tall, out bool ok)
        {
            cx = 0; cz = 0; dx = 0; dz = 0; len = 0; tall = false; ok = false;
            return false;
        }
    }
}

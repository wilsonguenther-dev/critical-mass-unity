using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// b12-jz item 2 (view only, never the sim): a laid LADDER is drawn by StagedBuildView as an A-frame of two leaning
    /// ladders that rise <see cref="Rise"/> over the wall, but the sim is flat, so a fighter crossing it walked straight
    /// through the steps at ground height ("stairs which don't even work"). <see cref="Lift"/> is the height of that A-frame
    /// under a sim point: 0 at each foot, Rise over the wall, linear between, within the piece's lanes. ActorViews adds it
    /// to the fighter's ground height (see the PR's outside-my-files patch), so the fighter goes up one side and down the
    /// other. Pure, allocation-free, O(pieces).
    /// </summary>
    public static class LadderClimb
    {
        /// <summary>The A-frame's top above the ground (StagedBuildView: scale.y = 1.6 / rise).</summary>
        public const float Rise = 1.6f;
        /// <summary>Each foot sits this far beyond the wall run's end (StagedBuildView: d = len / 2 + 0.35).</summary>
        public const double FootPad = 0.35;
        /// <summary>Half the lane width the lift applies across (WidthCells 3 / 2).</summary>
        public const double HalfWidth = 1.5;

        /// <summary>Height of the profile `along` metres from a ladder's centre whose wall run is halfLen long (pure; tests).</summary>
        public static float Profile(double along, double halfLen)
        {
            double d = halfLen + FootPad;
            double u = Math.Abs(along) / Math.Max(1e-6, d);
            return u >= 1.0 ? 0f : (float)(Rise * (1.0 - u));
        }

        /// <summary>The A-frame height under sim point (x, z): the highest active, laid ladder covering it, else 0.</summary>
        public static float Lift(World w, double x, double z)
        {
            BuiltPiece[] ps = w != null && w.Builder != null ? w.Builder.Pieces : null;
            if (ps == null) return 0f;
            float best = 0f;
            for (int k = 0; k < ps.Length; k++)
            {
                BuiltPiece p = ps[k];
                if (p == null || !p.Active || p.Carried || p.Kind != BuiltPieces.Ladder) continue;
                // b15: a height ladder (on a pit ledge or a stack) has no A-frame: GroundHeight.Body already stands the body on its level
                if (p.Count == 1 && p.Cells != null && w.Map.Lift != null && TerrainHeight.LadderGround(w, p.Cells[0], out _)) continue;
                double ox = x - p.X, oz = z - p.Z;
                double along = ox * p.Dx + oz * p.Dz;
                double across = -ox * p.Dz + oz * p.Dx;
                if (Math.Abs(across) > HalfWidth) continue;
                float h = Profile(along, p.HalfLen);
                if (h > best) best = h;
            }
            return best;
        }
    }
}

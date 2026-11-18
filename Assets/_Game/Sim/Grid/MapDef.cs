namespace CriticalMass.Sim.Grid
{
    /// <summary>A world point on the ground plane (browser <c>{ x, z }</c>).</summary>
    public struct PointXZ
    {
        public double X;
        public double Z;

        public PointXZ(double x, double z)
        {
            X = x;
            Z = z;
        }
    }

    /// <summary>A cardinal fall direction as a unit cell step: (1,0) east, (-1,0) west, (0,1) south, (0,-1) north.</summary>
    public struct CellDir
    {
        public int Dx;
        public int Dz;

        public CellDir(int dx, int dz)
        {
            Dx = dx;
            Dz = dz;
        }
    }

    /// <summary>Model family of a knock-down decoration, picked by the view (pillar = env_ruin_column).</summary>
    public enum DecorationKind
    {
        Pillar,
    }

    /// <summary>CM-31 knock-down decoration: falling along Ramp bridges the void, falling along Block walls a path.</summary>
    public sealed class DecorationDef
    {
        /// <summary>Model family, picked by the view.</summary>
        public DecorationKind Kind;
        /// <summary>Base cell x.</summary>
        public int Cx;
        /// <summary>Base cell z.</summary>
        public int Cz;
        public CellDir Ramp;
        public CellDir Block;
    }

    /// <summary>A team's spawn pad.</summary>
    public sealed class PadDef
    {
        public double X;
        public double Z;
        public double R;
    }

    /// <summary>The central hill.</summary>
    public sealed class HillDef
    {
        public double X;
        public double Z;
        public double R;
    }

    /// <summary>The Kaiju shrine.</summary>
    public sealed class ShrineDef
    {
        public double X;
        public double Z;
        public double SolidR;
    }

    /// <summary>World bounds of the map.</summary>
    public sealed class BoundsDef
    {
        public double MinX;
        public double MinZ;
        public double MaxX;
        public double MaxZ;
    }

    /// <summary>A ground grid map (browser <c>MapDef</c>): cell types, per-cell heights and breakable hp, plus the named features.</summary>
    public sealed class MapDef
    {
        public int Width;
        public int Height;
        public byte[] Cells;
        /// <summary>Sim-owned height per cell in world units (U-003); no base rule reads it.</summary>
        public double[] Height01;
        /// <summary>Per cell, breakable hits remaining.</summary>
        public byte[] WallHp;
        /// <summary>Spawn pads, indexed by <c>(int)TeamId</c>.</summary>
        public PadDef[] Pads;
        public HillDef Hill;
        public ShrineDef Shrine;
        public PointXZ[] BiomassNodes;
        public PointXZ[] CorruptionNodes;
        public BoundsDef Bounds;
        /// <summary>CM-31: knock-down decoration pieces. Fixed per map; world.Topple holds their state.</summary>
        public DecorationDef[] Decorations;
        /// <summary>
        /// b15 terrain height: integer level offset per cell on top of Height01 (+ stacked blocks, - dug pits), only on Floor and
        /// BreakableDestroyed cells. Null unless DepthConfig.TerrainHeight is on (Full rules): every reader checks for null, so
        /// Classic and PhysicsOnly are bit-identical. Sim state (StateHash mixes it). Not copied by WithCells (the Kaiju's
        /// clearance map ignores height).
        /// </summary>
        public sbyte[] Lift;
        /// <summary>b15: extra path cost of a 1-level and a 2-level edge (TerrainHeightConfig.PathCostStep1 / 2), set with Lift.</summary>
        public double LiftCost1, LiftCost2;

        /// <summary>A copy sharing every field but <c>Cells</c> (browser <c>{ ...map, cells }</c>).</summary>
        public MapDef WithCells(byte[] cells)
        {
            return new MapDef
            {
                Width = Width,
                Height = Height,
                Cells = cells,
                Height01 = Height01,
                WallHp = WallHp,
                Pads = Pads,
                Hill = Hill,
                Shrine = Shrine,
                BiomassNodes = BiomassNodes,
                CorruptionNodes = CorruptionNodes,
                Bounds = Bounds,
                Decorations = Decorations,
            };
        }
    }
}

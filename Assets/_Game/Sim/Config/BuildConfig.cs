namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// CM b13 item B (Builder walls and blocks, Jeisil: "similar to Fortnite and easy"): every number of the rule lives here.
    /// Tick counts are written at 30 Hz (Durations.ReferenceHz) like the rest of the config. Clone deep-clones every array.
    /// </summary>
    public sealed class BuildConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = true;

        // ---- Placement (the ghost)
        /// <summary>The aim point is clamped to this distance from the Builder; the cell under it is the piece's centre.</summary>
        public double Reach = 3.0;
        /// <summary>Below this aim distance the wall's axis follows his facing instead of the aim direction.</summary>
        public double AimAxisMin = 0.3;
        /// <summary>Cells in a wall (tier 0 and 1, and every short wall).</summary>
        public double WallCells = 3;
        /// <summary>Cells in a tall wall at tier 2 (hut level 4 to 5).</summary>
        public double WallCellsTier2 = 5;
        /// <summary>Tier by the team's hut level 0..5 (Blacksmith.Level): 0 = short pieces only, 1 = tall pieces, 2 = 5-cell tall walls at TallHp2.</summary>
        public int[] TierAtHutLevel = { 0, 0, 1, 1, 2, 2 };
        /// <summary>No piece cell within this distance of the hill centre (the hill's inner circle).</summary>
        public double HillKeepout = 3.0;
        /// <summary>No piece cell where a living actor or the hatched Kaiju is within this distance + its radius of the cell centre (MinerBarricade.MinDistanceFromActor).</summary>
        public double ActorClearance = 0.9;

        // ---- Cells
        /// <summary>Short pieces (Cell.LowWall: a jump clears them): HP per cell and life.</summary>
        public double ShortHp = 60;
        public double ShortLifeTicks = 1350;
        /// <summary>Tall pieces (Cell.Barricade: a jump does not clear them): HP per cell at tier 1, at tier 2, and life.</summary>
        public double TallHp = 120;
        public double TallHp2 = 150;
        public double TallLifeTicks = 1800;
        /// <summary>Wall cells one Builder may have standing; building past it crumbles his oldest piece first.</summary>
        public double MaxCellsPerBuilder = 15;
        /// <summary>Ticks between two builds of one Builder (0.6 s).</summary>
        public double LockTicks = 18;
        /// <summary>A TNT blast takes this much HP off every enemy piece cell whose centre is within BlastRadius.</summary>
        public double TntDamage = 60;
        /// <summary>The barricade pool grows at world creation by MaxCellsPerBuilder x this x 2 teams slots (class switching can add Builders; past it a build is refused "full").</summary>
        public double PoolBuildersPerTeam = 4;

        // ---- Costs (team wood stock: StartWood credit first, then the Sources wood bank)
        public double CostShortWall = 1;
        public double CostTallWall = 2;
        public double CostBlock = 1;
        public double CostFill = 1;
        /// <summary>Wood each team starts with for walls (its own credit, so BuilderPieces.StartBiomass is not counted twice).</summary>
        public double StartWood = 4;

        // ---- Bots (Sim/Bots/BotBuild.cs)
        /// <summary>Rule 1: the team holds the hill and this many living enemies are within BotHillEnemyRange of its centre.</summary>
        public double BotHillEnemies = 3;
        public double BotHillEnemyRange = 14;
        /// <summary>Rule 1: the wall goes at hill radius + this toward the enemy centroid, at most once per BotHillEveryTicks.</summary>
        public double BotHillEdgeExtra = 1.5;
        public double BotHillEveryTicks = 180;
        /// <summary>Rule 2: own HP under this share of max and an enemy within BotGuardRange: a short wall BotGuardDistance toward it.</summary>
        public double BotGuardHpShare = 0.5;   // b13 integrator: 0.4 -> 0.5 (bot walls per match)
        public double BotGuardRange = 5;   // b13 integrator: 4 -> 5
        public double BotGuardDistance = 1.5;

        public BuildConfig Clone()
        {
            BuildConfig c = (BuildConfig)MemberwiseClone();
            c.TierAtHutLevel = (int[])TierAtHutLevel.Clone();
            return c;
        }
    }
}

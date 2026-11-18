namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// b15 terrain height (owner: "do it"): blocks STACK and characters walk DOWN INTO a dug pit and back out. Every number of
    /// the rule lives here (docs/b15-terrain-height/DESIGN.md). Integer levels per cell (MapDef.Lift); Full profile only: the
    /// systems read Enabled through DepthRules.On, so Classic and PhysicsOnly never allocate the layer. Ticks at 30 Hz.
    /// </summary>
    public sealed class TerrainHeightConfig
    {
        /// <summary>The ONE switch of the rule (default on in Full rules).</summary>
        public bool Enabled = true;
        /// <summary>Highest stack (levels above the island) and deepest pit (levels below).</summary>
        public int MaxStack = 3;
        public int MaxDepth = 2;
        /// <summary>One level in view units (the view only; the sim works in whole levels).</summary>
        public double StepU = 0.45;
        /// <summary>Levels a grounded walker steps up without help; an airborne (jumping) actor rises this many.</summary>
        public int WalkUp = 1;
        public int JumpUp = 2;
        /// <summary>CLIMB: walking into a ledge exactly JumpUp levels above for this many ticks climbs it.</summary>
        public int ClimbTicks = 15;
        /// <summary>Slows after a level change: a 1-level step, a drop of 2 or more, a climb of 2.</summary>
        public int StepSlowTicks = 6;
        public double StepSlowScale = 0.7;
        public int DropSlowTicks = 12;
        public double DropSlowScale = 0.5;
        public int ClimbSlowTicks = 10;
        public double ClimbSlowScale = 0.6;
        /// <summary>Bot fields: extra cost of an edge with a 1-level and a 2-level difference (3 or more: closed).</summary>
        public double PathCostStep1 = 0.5;
        public double PathCostStep2 = 3.0;
        /// <summary>TNT: levels a raised cell in CarveRadius loses (people on it drop).</summary>
        public int TntLevels = 2;
        /// <summary>Kaiju: a cell its body overlaps at this lift or more is smashed down to SmashTo at once.</summary>
        public int KaijuSmashFrom = 2;
        public int SmashTo = 1;
        /// <summary>Kaiju body overlap test: its wall collider radius (PhysicsConfig.KaijuBodyRadius) plus this margin, u.</summary>
        public double KaijuSmashMargin = 0.25;

        public TerrainHeightConfig Clone() => (TerrainHeightConfig)MemberwiseClone();
    }
}

namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// CM b13 item C (corruption crystal and the wild Kaiju): every number of the rule lives here (owner rule choices, Travis's
    /// design; defaults from BATCH-13-ORDER item C). Ticks are at the 30 Hz reference rate (Durations.T30 converts them).
    /// </summary>
    public sealed class CorruptionConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = false;   // b13 integrator: item C is OUT this batch (stub only); off so the meter and roll banner never show

        // --- crystal veins (the existing corruption nodes on the south island) ---
        /// <summary>Charges per crystal vein while the rule is on.</summary>
        public int VeinCharges = 2;
        /// <summary>One charge regrows per this many ticks (1350 = 45 s).</summary>
        public int VeinRegrowTicks = 1350;
        /// <summary>Gather time per crystal (90 = 3.0 s).</summary>
        public int GatherTicks = 90;
        /// <summary>The Miner's gather time per crystal (30 = 1.0 s).</summary>
        public int MinerGatherTicks = 30;
        /// <summary>Crystals one carrier holds.</summary>
        public int CarryAmount = 1;
        /// <summary>Crystals the Miner holds.</summary>
        public int MinerCarry = 2;
        /// <summary>A crystal carrier's walk speed multiplier.</summary>
        public double CarrierSpeed = 0.85;

        // --- loose crystals (dropped on a knock-out) ---
        /// <summary>Pooled loose-crystal slots.</summary>
        public int DropSlots = 12;
        /// <summary>A loose crystal lasts this many ticks (600 = 20 s).</summary>
        public int DropTicks = 600;
        /// <summary>A hands-free MayCorrupt actor walking within this distance (u, centre to crystal) picks it up.</summary>
        public double PickupRange = 1.0;

        // --- feeding after the hatch (into a knocked-out Kaiju) ---
        /// <summary>Feed reach from the Kaiju's body edge (KaijuConfig.Radius + this, centre distance).</summary>
        public double FeedRange = 3.0;
        /// <summary>Hold E this many ticks to feed (30 = 1.0 s); an enemy hit interrupts (Delivery.InterruptDelivery).</summary>
        public int FeedTicks = 30;
        /// <summary>A team behind on HillSeconds by this many seconds or more is the trailing team.</summary>
        public double TrailingMargin = 15;
        /// <summary>Each crystal fed by the trailing team counts this much in the pool.</summary>
        public double TrailingBonus = 1.5;
        /// <summary>The owner team's Healer holding E near the downed Kaiju removes one crystal from the pool per this many ticks.</summary>
        public int PurifyTicks = 45;
        /// <summary>The Healer's purify reach from the Kaiju's body edge (u).</summary>
        public double PurifyRange = 3.0;

        // --- the chance and the roll ---
        /// <summary>The chance never exceeds this (never 1).</summary>
        public double Cap = 0.70;
        /// <summary>Chance = min(Cap, sqrt(pool / Full)).</summary>
        public double Full = 20;
        /// <summary>Roll hash: u = SeedUniform(seed x SeedMul + RollMul x (rollIndex + 1)), unchecked int arithmetic.</summary>
        public int SeedMul = 7919;
        public int RollMul = 104729;

        // --- the wild Kaiju ---
        /// <summary>A wild Kaiju's target score bias (u) toward the team leading on hill seconds (replaces BigFeederBias 4 while wild; K's patch).</summary>
        public double WildLeaderBias = 6;

        // --- bots ---
        /// <summary>Most runner-class bots per team on a crystal run at a time.</summary>
        public int MaxRunners = 2;
        /// <summary>Most owner-team guards on a downed Kaiju at a time.</summary>
        public int MaxGuards = 3;
        /// <summary>Owner-team bots guard when an enemy crystal carrier is within this distance (u) of the downed Kaiju.</summary>
        public double GuardRadius = 15;
        public double RunnerUtility = 0.86;
        public double FeedUtility = 0.95;
        public double GuardUtility = 0.9;
        public double PurifyUtility = 0.9;
        /// <summary>A task offer lasts this many seconds unless offered again (BotsState.TaskUntil).</summary>
        public double TaskSeconds = 8;

        public CorruptionConfig Clone() => (CorruptionConfig)MemberwiseClone();
    }
}

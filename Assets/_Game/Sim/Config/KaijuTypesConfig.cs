namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// CM b13 item K (five Kaiju types, RULE CHANGE, Full profile only): every number of the rule lives here (owner rule choices,
    /// defaults from the batch 13 order). At the hatch the MIX of plant / meat / wood / water both teams fed picks the type
    /// (each kind's share divided by its RefShare, largest wins; a tie or nothing fed = ELDER, today's Kaiju). Each type has an
    /// inherent trait (always) and a fed power whose level comes from the units of its own kind fed (LevelUnits). No type changes
    /// max HP by more than 10 % (batch 12 lesson 5). Arrays are deep-cloned in Clone.
    /// Kind order in every per-kind array: 0 plant (GROVE), 1 meat (BRUTE), 2 wood (TIMBER), 3 water (MIRE).
    /// </summary>
    public sealed class KaijuTypesConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = true;

        // ---- the picker
        /// <summary>
        /// Reference share per kind (plant, meat, wood, water): a kind's share of the feed divided by this is its score. Started
        /// at {0.55, 0.20, 0.15, 0.10}; PINNED to the control arm's measured mean mix over the 26 balance seeds (sim run
        /// 44012448675: plant 0.900, meat 0.100, wood 0.000, water 0.000), with a floor of RefFloor on wood and water (a
        /// reference of 0 would let a single unit win). KaijuTypeTests prints the measured mean beside this.
        /// </summary>
        public double[] RefShare = { 0.90, 0.10, 0.02, 0.02 };
        /// <summary>The floor applied when pinning RefShare to a kind the control never feeds (wood, water).</summary>
        public double RefFloor = 0.02;
        /// <summary>Two best scores within this of each other = a tie = ELDER.</summary>
        public double TieEps = 1e-9;
        /// <summary>
        /// b13 r3 (ELDER reachable): a BALANCED feed hatches ELDER. When the best score is below this (no kind fed more than
        /// 3 % over its reference share) the mix is "today's mix" and today's Kaiju hatches. Before r3 ELDER needed an exact
        /// tie or nothing fed, which real play never gives (0 of 24 balance seeds). Measured on the 24 balance seeds (sim run
        /// printed by KaijuTypeTests): 1.03 gives ELDER on the plant-led mixes 0.902 / 0.098 and 0.915 / 0.085 and the meat-led
        /// 0.898 / 0.102 (3 of 24). 0 = the old rule (ties only).
        /// </summary>
        public double ElderBelowScore = 1.03;
        /// <summary>Units of the type's own kind fed (both teams) for power level 1, 2, 3 (below the first: level 0, trait only).</summary>
        public double[] LevelUnits = { 8, 16, 24 };

        // ---- GROVE (plant): inherent HP x1.10, speed x0.95; fed REGROWTH
        /// <summary>
        /// b13 r3 tuning (first Kaiju knock-out +24 % against the control, limit 20 %): GROVE was the cause. Forced on all 26
        /// balance seeds with the same feed plans (diag sim runs on ci/b13r3-tune-diag), median time to the first Kaiju
        /// knock-out: ELDER 76.3 s, GROVE as batch 13 shipped it (HP x1.10, speed x0.95, REGROWTH 0.4 / 0.7 / 1.0 % per s after
        /// 5 s) 106.3 s (+39 %), BRUTE 71.0 s, MIRE 74.0 s, TIMBER 85.9 s. Every bot feed makes plant the largest share, so a
        /// GROVE is nearly always level 3. Single changes: speed x1.0 84.3 s, no REGROWTH 94.7 s, HP x1.0 97.7 s. Shipped: HP
        /// x1.05, speed x0.98, REGROWTH 0.2 / 0.35 / 0.5 % per s after 8 s: 82.9 s (+8.6 %), still the tank of the five.
        /// </summary>
        public double GroveHpScale = 1.05;
        public double GroveSpeedScale = 0.98;
        /// <summary>REGROWTH starts after this many Kaiju ticks without damage (240 = 8 s at 30 Hz; batch 13 shipped 150).</summary>
        public int RegrowthDelayTicks = 240;
        /// <summary>REGROWTH per level 1..3: percent of max HP healed per second (never above max, not while knocked out;
        /// batch 13 shipped 0.4 / 0.7 / 1.0).</summary>
        public double[] RegrowthPctPerSecond = { 0.2, 0.35, 0.5 };

        // ---- BRUTE (meat): inherent melee damage x1.15, speed x1.08, HP x0.95; fed FRENZY
        public double BruteMeleeDamageScale = 1.15;
        public double BruteSpeedScale = 1.08;
        public double BruteHpScale = 0.95;
        /// <summary>FRENZY applies below this share of max HP.</summary>
        public double FrenzyBelowHpShare = 0.5;
        /// <summary>FRENZY per level 1..3: melee (claw) cooldown multiplier.</summary>
        public double[] FrenzyMeleeCooldownScale = { 0.85, 0.75, 0.65 };

        // ---- TIMBER (wood): inherent HP x1.05, slam radius +0.75 u; fed BARKSKIN
        public double TimberHpScale = 1.05;
        public double TimberSlamRadiusAdd = 0.75;
        /// <summary>BARKSKIN per level 1..3: multiplier on explosion damage to the Kaiju (DamageCause.Explosion: TNT).</summary>
        public double[] BarkskinExplosionScale = { 0.8, 0.7, 0.6 };

        // ---- MIRE (water): inherent slam cooldown x0.8, speed x0.92; fed SHROUD (Travis's blinding power)
        public double MireSlamCooldownScale = 0.8;
        public double MireSpeedScale = 0.92;
        /// <summary>SHROUD triggers when at least this many affected fighters are within ShroudTriggerRange of the Kaiju.</summary>
        public int ShroudTriggerCount = 3;
        public double ShroudTriggerRange = 8;
        /// <summary>The puff telegraph (event KaijuPower) before the cloud, in ticks (15 = 0.5 s).</summary>
        public int ShroudPuffTicks = 15;
        /// <summary>Cloud radius per level 1..3 (u), centred on the Kaiju and following it.</summary>
        public double[] ShroudRadius = { 5, 6, 7 };
        /// <summary>Cloud duration per level 1..3 in ticks (4 / 5 / 6 s).</summary>
        public int[] ShroudTicks = { 120, 150, 180 };
        /// <summary>Cooldown from the cloud's end, in ticks (20 s).</summary>
        public int ShroudCooldownTicks = 600;
        /// <summary>An affected fighter inside the cloud sees (and bots target) only this far.</summary>
        public double SightInCloud = 2.5;
        /// <summary>False: an owned Kaiju's cloud blinds only the other team (a wild or neutral one blinds everyone).</summary>
        public const bool KaijuBlindHitsOwnTeam = false;

        /// <summary>b13 r3: bots the SHROUD would blind leave the cloud and skirt its rim unless contesting the hill
        /// (BotKaijuFeed.AvoidCloud).</summary>
        public bool BotsAvoidCloud = true;

        // ---- bot feed plans (Sim/Bots/BotKaijuFeed.cs): plan = floor(SeedUniform(seed x 3 + teamIndex) x 4), pre-hatch only
        /// <summary>Per plan (0 plant, 1 meat, 2 wood, 3 water): multiplier on the bots' plant-gather utility (plan 0 = today).</summary>
        public double[] FeedPlanGatherScale = { 1.0, 0.6, 0.8, 0.8 };

        public KaijuTypesConfig Clone()
        {
            var c = (KaijuTypesConfig)MemberwiseClone();
            c.RefShare = (double[])RefShare.Clone();
            c.LevelUnits = (double[])LevelUnits.Clone();
            c.RegrowthPctPerSecond = (double[])RegrowthPctPerSecond.Clone();
            c.FrenzyMeleeCooldownScale = (double[])FrenzyMeleeCooldownScale.Clone();
            c.BarkskinExplosionScale = (double[])BarkskinExplosionScale.Clone();
            c.ShroudRadius = (double[])ShroudRadius.Clone();
            c.ShroudTicks = (int[])ShroudTicks.Clone();
            c.FeedPlanGatherScale = (double[])FeedPlanGatherScale.Clone();
            return c;
        }
    }
}

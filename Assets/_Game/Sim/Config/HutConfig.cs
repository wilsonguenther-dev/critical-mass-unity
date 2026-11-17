namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// CM b13 item H (Miner and the Blacksmith hut): every number of the rule lives here (owner rule choices, Jeisil's play-test
    /// review); arrays are deep-cloned in Clone. Ticks are at Durations.ReferenceHz (30), converted with Durations.T30.
    /// </summary>
    public sealed class HutConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = true;

        // ---- the hut
        /// <summary>Ore each level costs, levels 1 to 5 (72 in all). The hut starts at level 0.</summary>
        public double[] LevelCost = { 6, 10, 14, 18, 24 };
        /// <summary>+5% max HP, damage dealt and healing per level, every class of the team (move speed and cooldowns are not scaled).</summary>
        public double StatPerLevel = 0.05;

        // ---- MINE (the Miner's F)
        /// <summary>One channel per ore (30 ticks = 1.0 s); a hit restarts it.</summary>
        public double MineTicks = 30;
        /// <summary>Ore a Miner carries (his class CarryCapacity row stays 1 for every other kind).</summary>
        public double OreCarry = 3;

        // ---- HAUL (the Miner's Space, on his class AbilityCooldown)
        /// <summary>150 ticks = 5 s of HaulSpeed and no carry slowdown.</summary>
        public double HaulTicks = 150;
        public double HaulSpeed = 1.2;

        // ---- ore veins (SourceNodes of Kind BiomassType.Ore, placed by Sources.Place, west copies mirrored)
        /// <summary>(31,13) NW courtyard north, (31,59) SW courtyard south, (42,40) central island west rim (contested).</summary>
        public double[] OreAnchors = { 31, 13, 31, 59, 42, 40 };
        public double OreCharges = 6;
        /// <summary>One charge regrows per 450 ticks (15 s).</summary>
        public double OreRegrowTicks = 450;
        /// <summary>More trees for the Builder (Jeisil): added to the groves, mirrored, so 10 groves instead of 6.</summary>
        public double[] ExtraWoodAnchors = { 40, 27, 40, 53 };

        // ---- Terrain (owner decision, b13 r2: terrain play belongs to the Builder and the Demolitionist, never the Miner)
        /// <summary>While the hut rule is on, the dig / place terraform (F, Terraform.cs: same costs, reach, carry and rate cap)
        /// belongs to the BUILDER instead of the Miner (the Miner's F is MINE). False = nobody digs or places (round 1).
        /// Bots never dig or place (BotBrain clears the press, CM-30), so bot matches are unchanged either way.</summary>
        public bool BuilderTerraform = true;

        // ---- Miner bots (Sim/Bots/BotMine.cs)
        /// <summary>Task MineOre utility (supply is 0.8, corrupt 0.85 x greed).</summary>
        public double BotMineUtility = 0.82;
        /// <summary>
        /// Task utility once the bot carries ore (mining on, or the hut run): above the shrine delivery utility (0.9) so a Miner
        /// bot never walks ore to the egg.
        /// </summary>
        public double BotCarryUtility = 0.92;
        /// <summary>HAUL when carrying ore and an enemy is this close (u).</summary>
        public double BotHaulEnemyRange = 8;
        /// <summary>HAUL on the way to the hut with this much ore.</summary>
        public double BotHaulHomeOre = 2;
        /// <summary>An empty-handed Miner bot with an enemy this close fights (today's logic) instead of walking to a vein (u).</summary>
        public double BotFightRange = 8;   // b13 r2: back from 14 (round 1 tuning pass): 14 u cost a fifth of the ore and moved no kill share
        /// <summary>b13 integrator tuning pass: a Miner bot carrying ore still fights back when an enemy is this close (u).</summary>
        public double BotFightWithOreRange = 4;
        /// <summary>A Miner bot that ended up in Deliver with ore (the shrine would refuse it) is pulled back to the hut at this utility.</summary>
        public double BotEscapeUtility = 2;
        /// <summary>A task lasts this long without a fresh think (s).</summary>
        public double BotTaskSeconds = 20;

        public HutConfig Clone()
        {
            var c = (HutConfig)MemberwiseClone();
            c.LevelCost = (double[])LevelCost.Clone();
            c.OreAnchors = (double[])OreAnchors.Clone();
            c.ExtraWoodAnchors = (double[])ExtraWoodAnchors.Clone();
            return c;
        }
    }
}

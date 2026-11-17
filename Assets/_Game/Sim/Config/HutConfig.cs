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
        /// <summary>
        /// b13 r3 (Miner bots fight): the Fight utility BotMine offers when an empty-handed Miner bot sees an enemy within
        /// BotFightRange (with ore: BotFightWithOreRange), and at the top level when he stands on the hill (radius plus
        /// BotTopFightHillMargin). 1.0 beats holding the hill (0.7 + 0.25 inertia) and loses to nothing a hurt bot needs (he
        /// offers it only above BotFsm.LowHp, where Retreat does not apply). 0 = round 2 (he walked to the hill instead).
        /// </summary>
        public double BotDefendUtility = 1.0;
        public double BotTopFightHillMargin = 2;
        /// <summary>
        /// b13 r3: the HILL SHIFT (BotMine): from this hut level a Miner bot that has just banked his ore spends
        /// BotHillShiftSeconds on the hill before mining again (0 s = off). Measured on the hut balance seeds (diag runs on
        /// ci/b13r3-tune-diag; Miner kill share / 16v16 median end hut level / 16v16 seeds with a team at L5): no shift
        /// 0.020 / 4 / 3 of 6; from L3 20 s one Miner per team 0.067 / 4 / 1; from L2 20 s 0.081 / 4 / 0; from L1 20 s
        /// 0.096 / 4 / 1. Shipped: from L3, 15 s, one per team.
        /// </summary>
        public int BotHillShiftFromLevel = 3;
        public double BotHillShiftSeconds = 15;
        /// <summary>
        /// b13 r3 integrator (snowball guard, "higher-level team at 6:00 wins at most 75%"): the shift when his team's hut is
        /// AHEAD of the enemy's (longer: the leader's Miner mines less) and when it is BEHIND (0 s: the trailing Miner keeps
        /// mining to catch up). Level teams use BotHillShiftSeconds.
        /// </summary>
        public double BotHillShiftAheadSeconds = 30;
        public double BotHillShiftBehindSeconds = 0;
        /// <summary>At most this many of a team's Miners are on a hill shift at once (the others keep mining).</summary>
        public int BotHillShiftMaxPerTeam = 1;
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

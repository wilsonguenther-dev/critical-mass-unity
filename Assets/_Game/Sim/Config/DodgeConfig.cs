namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// CM b13 item J (jump and dodge, Travis: reaction play): every number of the rule lives here. Reasoning (BATCH-13
    /// ORDER, item J): a 0.2 s dash covers the 0.4-0.8 s readable wind-ups (Kaiju slam 1.2 s, claw 11 ticks, TNT fuse 45
    /// ticks) with room to decide; 0.17 s of invulnerability rewards timing without letting a spam dodge erase a 2-4 s
    /// time-to-kill; a 3 s cooldown means at most one dodge per engagement beat. Timing in sim ticks at 30 Hz.
    /// </summary>
    public sealed class DodgeConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = true;
        /// <summary>Dash length (u) and its ticks (0.2 s); the per-tick shares are an ease-out (Dodge.Share).</summary>
        public double Distance = 3.0;
        public int DashTicks = 6;
        /// <summary>Invulnerable on dash ticks 1 to IFrameTicks (0.17 s), every cause but Fall.</summary>
        public int IFrameTicks = 5;
        /// <summary>Then this many ticks of walking at RecoverSpeedScale of the class speed.</summary>
        public int RecoverTicks = 2;
        public double RecoverSpeedScale = 0.5;
        /// <summary>From the press to the next dodge (3.0 s).</summary>
        public int CooldownTicks = 90;
        /// <summary>The move input decides the direction when its length is over this; else the facing.</summary>
        public double MinMoveInput = 0.2;
        /// <summary>A planned path with fewer clear ticks than this is refused.</summary>
        public int MinClearTicks = 2;

        // ---- bots (Sim/Bots/BotReflex.cs). Arrays: Easy, Normal, Hard.
        /// <summary>A bot reacts to a threat only once it has existed this many ticks.</summary>
        public int[] ReactionTicks = { 12, 8, 5 };
        /// <summary>Chance a bot dodges a threat (one decision per threat per bot, a stateless hash of seed, tick and id).</summary>
        public double[] DodgeChance = { 0.15, 0.35, 0.60 };
        /// <summary>Kaiju slam: the telegraph circle covers the bot with this many ticks or fewer left.</summary>
        public int SlamTicksLeft = 15;
        /// <summary>Enemy TNT: landed, this many fuse ticks or fewer left, the bot within BlastRadius + TntSlack.</summary>
        public int TntFuseTicksLeft = 20;
        public double TntSlack = 0.3;
        /// <summary>Enemy melee wind-up at the bot within the attacker's reach + MeleeSlack (+ the bot's body).</summary>
        public double MeleeSlack = 0.3;
        /// <summary>Jump: a LowWall within JumpProbe u ahead of the steering and the cell past it walkable; at most once per JumpEveryTicks.</summary>
        public double JumpProbe = 1.0;
        public int JumpEveryTicks = 30;

        public DodgeConfig Clone()
        {
            var c = (DodgeConfig)MemberwiseClone();
            c.ReactionTicks = (int[])ReactionTicks.Clone();
            c.DodgeChance = (double[])DodgeChance.Clone();
            return c;
        }
    }
}

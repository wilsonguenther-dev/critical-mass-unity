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

        // ---- px DM (player-experience lane DM, 2026-11-22): the timed-dodge reward (lead's call 14). Full rules only (Dodge.On).
        /// <summary>The reward rule's own switch: a melee swing dodged by an enemy (it started its dodge during the wind-up from
        /// a spot the swing would have hit, or took the contact inside its i-frames) leaves the WHIFFING attacker open to a PUNISH
        /// critical strike for DodgePunishSeconds after the contact tick (instead of only its own recovery), and a PERFECT dodge
        /// gives back PerfectDodgeCooldownRefund of the dodge cooldown. False = no reward (the DODGED event is raised either way).</summary>
        public bool DodgeReward = true;
        /// <summary>Real seconds after a dodged whiff's contact tick in which a melee hit on the attacker is a PUNISH critical
        /// (CombatFeel crit kind 2). The attacker's own whiff recovery (0.33 to 0.47 s) was too short: a 0.2 s human who dodged
        /// landed the punish 2% (side dodge) to 16% (dodge through) of the time; data/dm-defence/VERBS.txt.</summary>
        public double DodgePunishSeconds = 0.8;
        /// <summary>A dodge started this many ticks or fewer before the contact tick is PERFECT (HitBlocked Tier 1).</summary>
        public int PerfectDodgeTicks = 2;
        /// <summary>Share of the dodge cooldown a PERFECT dodge gives back (0 = none).</summary>
        public double PerfectDodgeCooldownRefund = 0;

        // ---- bots (Sim/Bots/BotReflex.cs). Arrays: Easy, Normal, Hard.
        /// <summary>A bot reacts to a threat only once it has existed this many ticks (the ORDER's numbers).</summary>
        public int[] ReactionTicks = { 12, 8, 5 };
        /// <summary>Chance a bot dodges a slam, claw or melee threat (one decision per threat per bot, a stateless hash of seed,
        /// tick and id). b13 r3: back to the ORDER's numbers; round 2's {0.30, 0.70, 0.90} did not fix the rate because the
        /// cause was the window, not the chance (see MeleeThreat below).</summary>
        public double[] DodgeChance = { 0.15, 0.35, 0.60 };
        /// <summary>b13 r3: chance a bot dodges an enemy TNT it stands next to (its own roll, lower than the others). The landed
        /// bundle and its 33-tick fuse are the telegraph: a bot that reads it walks or dashes out, one that does not takes the
        /// blast. Round 2 rolled the 0.70 melee chance on every TNT with the reaction always met (the fuse window opens 13 ticks
        /// after landing), so 70% of throws missed and the Demolitions kill share fell 0.189 to 0.103.</summary>
        public double[] TntChance = { 0.10, 0.25, 0.40 };
        /// <summary>Kaiju slam: the telegraph circle covers the bot with this many ticks or fewer left.</summary>
        public int SlamTicksLeft = 15;
        /// <summary>Enemy TNT: landed, this many fuse ticks or fewer left, the bot within BlastRadius + TntSlack.</summary>
        public int TntFuseTicksLeft = 20;
        public double TntSlack = 0.3;
        /// <summary>Enemy melee at the bot within the attacker's reach + MeleeSlack (+ the bot's body).</summary>
        public double MeleeSlack = 0.3;
        /// <summary>b13 r3: a melee threat is the attacker's whole swing CYCLE aimed at the bot (wind-up, then its attack
        /// cooldown until the next swing), not only the wind-up. On this base every light weapon winds up 3 or 4 ticks
        /// (0.10-0.12 s), shorter than every reaction delay, so a wind-up-only threat was over before any Normal bot could
        /// react (the cause of 0.38-0.68 dodges per bot-minute). Reading the attacker's rhythm and leaving before the next
        /// swing is the counterplay a person has too.</summary>
        public bool MeleeThreatIsTheCycle = true;
        /// <summary>b13 r3 hill-aware dodges: a bot standing inside the hill ring only takes a dodge whose end stays at least
        /// HillMargin inside the ring (it tries the threat direction, then turned 45, 90, 135 and 180 degrees, never toward
        /// the threat); none fits = it holds the hill and takes the hit, unless the hit would knock it out.</summary>
        public bool HillAware = true;
        public double HillMargin = 0.5;
        /// <summary>Jump: a LowWall within JumpProbe u ahead of the steering and the cell past it walkable; at most once per JumpEveryTicks.</summary>
        public double JumpProbe = 1.0;
        public int JumpEveryTicks = 30;

        public DodgeConfig Clone()
        {
            var c = (DodgeConfig)MemberwiseClone();
            c.ReactionTicks = (int[])ReactionTicks.Clone();
            c.DodgeChance = (double[])DodgeChance.Clone();
            c.TntChance = (double[])TntChance.Clone();
            return c;
        }
    }
}

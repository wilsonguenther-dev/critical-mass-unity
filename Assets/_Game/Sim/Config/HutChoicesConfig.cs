namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// b15 rule B, HUT AS A TEAM CHOICE (owner approved 2026-11-17, systems audit finding 4). Every number of the rule lives
    /// here. Full profile only (HutChoices.On = DepthRules.On(Enabled) and the Blacksmith hut on). With the rule on, a hut level
    /// gives NO flat +5%: each level opens a team vote between two side-grades (HutChoices.Options), and the winner holds for
    /// the rest of the match. Jeisil's level costs (HutConfig.LevelCost 6/10/14/18/24) are unchanged, and the Builder's wall
    /// tiers still follow the hut level.
    /// <para>THE VOTE: VoteSeconds long; every member of the team, alive or waiting to respawn, has one vote (a human with the
    /// two card keys, sent as InputFrame.StationBuy 1 or 2; a bot by team need after a short think). A strict majority of the
    /// whole team closes it at once. At the end of the window the option with more votes wins; a tie or no votes = the seeded
    /// default (DepthRules.Hash01 of the seed, the team and the level). The match never pauses for it.</para>
    /// </summary>
    public sealed class HutChoicesConfig
    {
        /// <summary>The rule's own switch (default on; Full profile only, and only while the hut rule is on).</summary>
        public bool Enabled = true;

        // ---- the vote
        /// <summary>b16: 10 s (was 8). Two cards are a name and one line each: about 2 s of reading each plus the choice. With
        /// MechRulesConfig.HutBotsWaitForHumans the people have the first 7 s to themselves (the bots vote in the last 3 s or once
        /// every person has voted); before, 15 bots closed the vote in about 3 s, before a person had read both cards.</summary>
        public double VoteSeconds = 10;
        /// <summary>A bot votes this many seconds after the vote opens, plus a seeded 0..BotThinkSpreadSeconds.</summary>
        public double BotThinkSeconds = 1;
        public double BotThinkSpreadSeconds = 3;
        /// <summary>Share of bot votes that go against the team-need choice (seeded per bot and level): the picks vary and a
        /// human's vote can swing a split team.</summary>
        public double BotContrarianShare = 0.2;

        // ---- level 1: QUICK RETURN or TOUGH WALLS
        /// <summary>QUICK RETURN: the team's knock-out wait is this much shorter (s), never under RespawnFloorSeconds.</summary>
        public double QuickReturnSeconds = 1.5;
        public double RespawnFloorSeconds = 2;
        /// <summary>TOUGH WALLS: the team's Builder walls and blocks get this HP and life multiplier.</summary>
        public double ToughWallScale = 1.5;

        // ---- level 2: RICH VEINS or SHIELD WALL
        /// <summary>RICH VEINS: one ore takes this share of the mining channel (0.6 = 40% faster).</summary>
        public double RichVeinsMineScale = 0.6;
        /// <summary>SHIELD WALL: a Warrior of the team with his shield up takes this share of every hit, from any side, before the
        /// shield's own frontal cut.</summary>
        public double ShieldWallScale = 0.8;

        // ---- level 3: SHORT FUSE or BLAST GUARD
        /// <summary>SHORT FUSE: the team's TNT blows after this share of the fuse (0.7 = 45 ticks to 32).</summary>
        public double ShortFuseScale = 0.7;
        /// <summary>BLAST GUARD: the team takes this share of explosion damage (TNT and mines).</summary>
        public double BlastGuardScale = 0.75;

        // ---- level 4: WIDE MEND or KAIJU HUNTERS
        /// <summary>WIDE MEND: the team's Healers reach this much further with MEND (x MendRange: 5.5 u to 7.7 u).</summary>
        public double WideMendScale = 1.4;
        /// <summary>KAIJU HUNTERS: the team deals this much damage to the Kaiju.</summary>
        public double KaijuHunterScale = 1.25;

        // ---- level 5: HILL ANCHOR or SWIFT FEET
        /// <summary>SWIFT FEET: the team moves this much faster while off the hill (beyond the hill radius + SwiftHillMargin).</summary>
        public double SwiftFeetScale = 1.10;
        public double SwiftHillMargin = 2;
        // HILL ANCHOR has no number: with equal bodies on the hill, the team with HILL ANCHOR owns it (both or neither: contested).

        public HutChoicesConfig Clone() => (HutChoicesConfig)MemberwiseClone();
    }
}

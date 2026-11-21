namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// b16 mechanics fixes (the 2026-11-21 mechanics audit's "next build" list and the design advisor's 2026-11-21 rulings).
    /// Every number of those rules lives here. Full profile only: each reader goes through DepthRules.On, so Classic and
    /// PhysicsOnly never see any of it (their golden checksums do not move). Sim/B16/MechRules.cs reads it.
    /// </summary>
    public sealed class MechRulesConfig
    {
        // ---- KAIJU WAITS OFF THE CIRCLE (audit ranked #3)
        /// <summary>With no target, a sided Kaiju waits on the hill's edge OUTSIDE the scoring ring (hill radius + its own
        /// radius + KaijuIdleMargin), on the side facing the other team's pad, facing in, instead of 1.5 u from the centre
        /// where KAIJU HOLDS NOTHING zeroed its own team (audit: 60 s a match). It still targets enemies that step on the hill.</summary>
        public bool KaijuIdleOffHill = true;
        /// <summary>Extra distance outside the scoring ring (u).</summary>
        public double KaijuIdleMargin = 1.0;
        /// <summary>The idle point is reached when the body is this close to it (u); it then stands and faces the hill.</summary>
        public double KaijuIdleStop = 0.75;

        // ---- IDLE PLAYERS (audit ranked #6, exploit 1)
        /// <summary>A human whose frames carry no input for AfkSeconds is played by the autopilot (the same bot input a dropped
        /// player gets) until his next input, which hands the fighter back on that tick. Off while the bots are passive
        /// (Bots.PassiveSeconds over 0: the guided match, practice and the demo, where reading without input is the point).</summary>
        public bool AfkAutopilot = true;
        public double AfkSeconds = 15;
        /// <summary>The director's two extra Kaiju feeders (ahead, or 10 s behind) become a share of the team:
        /// ceil(FeedExtraShare x team size) = 2 at 16, 1 at 8, 1 at 5 (before: 2 at every size, 3 of 5 bots off the hill at 5v5).</summary>
        public bool FeedExtrasShare = true;
        public double FeedExtraShare = 0.12;

        // ---- HUT VOTE: bots wait for humans (audit ranked #7)
        /// <summary>On a team with at least one human at the controls, bots hold their votes until HutBotLastSeconds are left in
        /// the window or every such human has voted, whichever is first, and then follow the humans' majority; when the humans
        /// are split evenly the bots' team-need card breaks the tie (one card, never a bot count). All-bot teams vote as before.</summary>
        public bool HutBotsWaitForHumans = true;
        public double HutBotLastSeconds = 3;

        // ---- THE HILL COUNT (design advisor 2026-11-21: "I like the fighter counting as 2 ... capping the units that
        // contribute to the hill at 10")
        /// <summary>A Warrior standing on the hill counts this many bodies for holding and contesting it (1 = off).</summary>
        public int WarriorHoldWeight = 2;
        /// <summary>At most this many WEIGHTED bodies per team count toward holding or contesting the hill at a team size of
        /// HillCapReferenceTeam; other sizes scale it: round(cap x size / reference), at least 1 (16: 10, 8: 5, 5: 3). Weights
        /// apply first, so a Warrior fills 2 of the 10. Bodies past the cap add nothing. 0 = no cap.</summary>
        public int HillContributorCap = 10;
        public int HillCapReferenceTeam = 16;
        /// <summary>Bots on a team already at the cap do something else: hill-job bots past it take the INTERCEPT job (stand on
        /// the enemy's approach, InterceptAlong u of path from the hill toward the enemy pad, and fight whoever comes).</summary>
        public bool HillCapBotsOffHill = true;
        public double InterceptAlong = 12;
        /// <summary>A bot's intercept / hill switch holds at least this long (no flip-flop at the disc edge).</summary>
        public double HillCapLockSeconds = 3;
        /// <summary>Off-disc hill bots still walk in while the team's counted bodies plus this are under the cap.</summary>
        public int HillCapWalkInSlack = 1;

        // ---- HEALER FOCUS (audit ranked #5; behind a switch, measured)
        /// <summary>true: bots score a Healer like any other target minus HealerTieBonus (a tie-breaker) instead of as if she
        /// stood at 1 % of her distance (an override that made her die 18 times a match).</summary>
        public bool HealerTieBreak = false;
        public double HealerTieBonus = 2;

        public MechRulesConfig Clone() => (MechRulesConfig)MemberwiseClone();
    }
}

namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// b15 rule A, the COMEBACK rule (owner approved 2026-11-17, systems audit findings 1 and 2). Every number of the rule lives
    /// here. Full profile only: Comeback.On reads Enabled through DepthRules.On, so Classic and PhysicsOnly never see it.
    /// <para>RALLY: while a team trails on hill seconds by MORE than TrailThresholdSeconds, each tick it OWNS the hill counts
    /// RallyPerMille / 1000 of a tick extra (250 = x1.25), until the team has banked BonusCapSeconds of extra credit this match.
    /// It is earned by holding, never handed over (Riot's rule for comebacks).</para>
    /// <para>THE NUMBERS (b16, 2026-11-21): the owner approved x1.25 capped at 45 s and a 3 s to 8 s respawn on 2026-11-17
    /// (growth decision A); b15 shipped x1.5 / 60 s / 2 to 5 s instead. The mechanics audit (section 5c, "combo") measured the
    /// approved multiplier, cap and respawn with FORWARD RETURN for the trailing team only and found it fixes the 16v16 arc
    /// (leading at 2:00 wins 0.59, leader-at-half 0.634, Kaiju owner 0.627, blowouts 0.097), so those are the defaults now.
    /// The approved 15 s threshold is NOT used: it raised blowouts to 0.194 (same table), so the threshold stays 5 s.</para>
    /// <para>Why the threshold is 5 s and still safe: the bonus switches OFF the moment the gap closes to 5 s, so it can never
    /// carry a team into the lead by itself; a team that throws the hill on purpose to farm it gets back at most what it gave
    /// away minus 5 s, and at most BonusCapSeconds in all, so deliberately trailing always loses against playing straight.</para>
    /// <para>SMALL TEAMS keep the b15 numbers (x1.5, 60 s, 2 to 5 s, forward return also when the hill is lost): at 5 a side
    /// the stronger RALLY is what keeps a 4-minute Quick Match alive (audit 5c: the combo there settles matches at 27 s
    /// instead of 172 s). The world's team size picks the set (Comeback.Rally / CapMilli / Respawn* / ForwardWhenLost), so
    /// replays and online rooms need nothing extra.</para>
    /// <para>RESPAWN CURVE: the knock-out wait starts at RespawnStartSeconds and grows to RespawnEndSeconds at the end of the
    /// match (short early so players get back in, longer late so a wipe matters). CurvePower 1 = a straight line, 2 = slow
    /// start (stays near 3 s through the first half). Integer ticks: wait = start + (end - start) x (tick / matchTicks)^power.</para>
    /// </summary>
    public sealed class ComebackConfig
    {
        /// <summary>The rule's own switch (default on; Full profile only).</summary>
        public bool Enabled = true;

        // ---- RALLY (trailing team's hill time); the last-resort score lever
        /// <summary>RALLY on / off (each lever has its own switch; the rule's Enabled turns them all off).</summary>
        public bool RallyOn = true;
        /// <summary>Behind on hill seconds by more than this (s) = trailing (RALLY and FORWARD RETURN).</summary>
        public double TrailThresholdSeconds = 5;
        /// <summary>Extra credit per owned tick while trailing, per mille of a tick (250 = the hill counts x1.25).</summary>
        public int RallyPerMille = 250;
        /// <summary>Extra hill seconds a team can bank from RALLY in one match.</summary>
        public double BonusCapSeconds = 45;

        // ---- RESPAWN CURVE (both teams)
        public bool RespawnCurveOn = true;
        public double RespawnStartSeconds = 3;
        public double RespawnEndSeconds = 8;

        // ---- SMALL TEAMS (b16): a world with at most SmallTeamMaxSize a side reads these instead (0 = never)
        public int SmallTeamMaxSize = 5;
        public int SmallRallyPerMille = 500;
        public double SmallBonusCapSeconds = 60;
        public double SmallRespawnStartSeconds = 2;
        public double SmallRespawnEndSeconds = 5;
        public bool SmallForwardWhenHillLost = true;
        /// <summary>1 = linear, 2 = quadratic (slow start). Integers only.</summary>
        public int CurvePower = 1;

        // Defaults (b16, 2026-11-21) = set D0 with the audit's combo: KAIJU HOLDS NOTHING x0, HOLD FATIGUE 20/40/0.25, FORWARD
        // RETURN 0.4 for the TRAILING team only (b15 also gave it to any team whose hill was lost: on 285 of 480 s, so it was the
        // normal respawn, not a comeback), RALLY x1.25 past 5 s capped at 45 s, respawn 3 -> 8 s; small teams keep b15's x1.5 /
        // 60 s / 2 -> 5 s / forward when lost (above). TURNCOAT off (design advisor: the feeder keeps it). WAVES and MAJORITY stay
        // off (both made matches worse: waves 6-10 s raised blowouts to 0.46-0.67, majority 2-3 did not lower them).

        // ---- b15 pass 2 levers (each its own switch; all symmetric, read only match state)
        /// <summary>WAVES: a knocked-out fighter comes back at his team's next wave (every WaveSeconds of match time, both teams
        /// on the same beat), never sooner than his own wait: the dead regroup and walk back together. 0 = off.</summary>
        public double WaveSeconds = 0;

        /// <summary>TURNCOAT KAIJU: a sided Kaiju knocked out by the other team gets up on the side of the team that knocked it
        /// down ("it becomes what beat it"); a neutral or berserk Kaiju never turns. OFF by default (2026-11-20): the design
        /// advisor holds that the team that fed it more keeps it unless it is corrupted, and the owner has not ruled; the switch
        /// stays for that ruling (B15RuleSetsTests arm E measures it).</summary>
        public bool KaijuTurncoat = false;

        /// <summary>TRAILING FEED (arm C): while a team trails on hill seconds by more than TrailThresholdSeconds, its deliveries
        /// to the egg count this many per mille EXTRA toward allegiance (500 = x1.5). 0 = off. Before the hatch only (allegiance
        /// means nothing after it). The trailing team already feeds more (the bots' gambit), so B15RuleSetsTests prints whether
        /// the team that trailed at the hatch got the Kaiju and whether the Kaiju side then won (the snowball check).</summary>
        public int FeedTrailingPerMille = 0;

        /// <summary>KAIJU HOLDS NOTHING (arm D): while a sided Kaiju is up and standing in the hill ring (hill radius plus its own
        /// radius), its team's hill seconds count this many per mille (500 = half). 1000 = off. The Kaiju still fights for its
        /// team; it only stops being the thing that holds the hill for it (the diagnosis: the Kaiju side owned 74-89% of the
        /// second half because the enemy left the hill to fight it).</summary>
        public int KaijuHillPerMille = 0;   // 2026-11-21 default ON (D0, coordinator decision, advisor agreed for now; the owner feel-tests it)
        /// <summary>KAIJU HOLDS NOTHING applies wherever the sided Kaiju is while it is up (not only while it stands on the hill).</summary>
        public bool KaijuHillAnywhere = false;

        /// <summary>HOLD FATIGUE: after HoldGraceSeconds of unbroken ownership the owner's hill seconds count less and less, down
        /// to HoldMinPerMille / 1000 after HoldSpanSeconds more; a lost, contested or empty hill resets the streak.</summary>
        public bool HoldFatigueOn = true;
        public double HoldGraceSeconds = 20;
        public double HoldSpanSeconds = 40;
        public int HoldMinPerMille = 250;

        /// <summary>MAJORITY: the hill is owned only with at least this many more bodies than the other team (1 = today's rule);
        /// fewer is contested and nobody scores.</summary>
        public int MajorityMargin = 1;

        /// <summary>FORWARD RETURN: a team trailing by more than TrailThresholdSeconds respawns at a rally point ForwardShare of
        /// the way from its pad to the hill (on open floor; the pad when the point is blocked).</summary>
        public bool ForwardReturnOn = true;
        public double ForwardShare = 0.4;
        /// <summary>FORWARD RETURN also for a team whose hill the other team holds (not only a trailing team). b16: off (the
        /// audit's "forward for the trailing team only": blowouts 0.042 alone, 0.097 in the combo); small teams: SmallForwardWhenHillLost.</summary>
        public bool ForwardWhenHillLost = false;

        public ComebackConfig Clone() => (ComebackConfig)MemberwiseClone();
    }
}

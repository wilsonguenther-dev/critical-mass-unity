namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// b15 rule A, the COMEBACK rule (owner approved 2026-11-17, systems audit findings 1 and 2). Every number of the rule lives
    /// here. Full profile only: Comeback.On reads Enabled through DepthRules.On, so Classic and PhysicsOnly never see it.
    /// <para>RALLY: while a team trails on hill seconds by MORE than TrailThresholdSeconds, each tick it OWNS the hill counts
    /// RallyPerMille / 1000 of a tick extra (250 = x1.25), until the team has banked BonusCapSeconds of extra credit this match.
    /// It is earned by holding, never handed over (Riot's rule for comebacks).</para>
    /// <para>Why the threshold is 15 s: (1) it is the same "behind" the corruption lever already uses (CorruptionConfig, the
    /// trailing team's crystals count 1.5 at 15 s), so the game has one meaning of "behind"; (2) abuse guard: the bonus switches
    /// OFF the moment the gap closes to 15 s, so it can never carry a team into the lead by itself. A team that throws the hill
    /// on purpose to farm the bonus gets back at most what it gave away minus 15 s, and at most 45 s in all: deliberately
    /// trailing always loses at least 15 s against playing straight. (3) Bot matches swing 10 to 20 s inside a single hill hold,
    /// so a smaller threshold would switch the tag on and off during ordinary back-and-forth.</para>
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
        /// <summary>Extra credit per owned tick while trailing, per mille of a tick (500 = the hill counts x1.5).</summary>
        public int RallyPerMille = 500;
        /// <summary>Extra hill seconds a team can bank from RALLY in one match.</summary>
        public double BonusCapSeconds = 60;

        // ---- RESPAWN CURVE (both teams)
        public bool RespawnCurveOn = true;
        public double RespawnStartSeconds = 2;
        public double RespawnEndSeconds = 5;
        /// <summary>1 = linear, 2 = quadratic (slow start). Integers only.</summary>
        public int CurvePower = 1;

        // Defaults = the pass-2 sweep's set (B15SweepTests rounds 1-3, confirmed on 72 seeds) WITHOUT TURNCOAT (off 2026-11-20 until
        // the owner rules on Kaiju allegiance): HOLD FATIGUE 20/40/0.25 + FORWARD RETURN 0.4 (also when the hill is lost) + RALLY
        // x1.5 past 5 s, cap 60 s + respawn 2 -> 5 s (B15RuleSetsTests arm B). WAVES and MAJORITY stay off (both made matches
        // worse: waves 6-10 s raised blowouts to 0.46-0.67, majority 2-3 did not lower them).

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
        public int KaijuHillPerMille = 1000;

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
        /// <summary>FORWARD RETURN also for a team whose hill the other team holds (not only a trailing team).</summary>
        public bool ForwardWhenHillLost = true;

        public ComebackConfig Clone() => (ComebackConfig)MemberwiseClone();
    }
}

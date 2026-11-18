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

        // ---- RALLY (trailing team's hill time)
        /// <summary>Behind on hill seconds by more than this (s) = trailing.</summary>
        public double TrailThresholdSeconds = 15;
        /// <summary>Extra credit per owned tick while trailing, per mille of a tick (250 = the hill counts x1.25).</summary>
        public int RallyPerMille = 250;
        /// <summary>Extra hill seconds a team can bank from RALLY in one match.</summary>
        public double BonusCapSeconds = 45;

        // ---- RESPAWN CURVE (both teams)
        public double RespawnStartSeconds = 3;
        public double RespawnEndSeconds = 8;
        /// <summary>1 = linear, 2 = quadratic (slow start). Integers only.</summary>
        public int CurvePower = 1;

        public ComebackConfig Clone() => (ComebackConfig)MemberwiseClone();
    }
}

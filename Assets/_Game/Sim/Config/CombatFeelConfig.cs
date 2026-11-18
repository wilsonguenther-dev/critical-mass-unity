namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// b15 combat feel (owner 2026-11-18, after the in-engine audit: "check all these actions, hitting, how they attack, the
    /// physical damage players take, the mathematical engine"). EVERY number of the rule lives in this one block so the owner
    /// can tune it without code. Full rules only: the rule runs when Enabled is true, DepthConfig.Classic is false and
    /// PhysicsConfig.Classic is false (CombatFeel.On), so the Classic and PhysicsOnly world hashes never move. Pull the whole
    /// rule with <c>Enabled = false</c>: the game is then exactly the batch 13/14 candidate.
    /// <para>What it changes (each point is one rule in Sim/CombatFeel.cs, each with its own test in CombatFeelTests):</para>
    /// <para>A. Attack phases: every class has its own wind-up (telegraphed by the view as a ground arc of the real hit shape:
    /// AttackStart carries Radius = reach and Impulse = arc degrees), active tick, and recovery. A swing that hits nobody adds
    /// WhiffRecoverySeconds (a whiff is punishable). An attack press that arrives up to BufferSeconds before the swing would be
    /// accepted is kept and fires on the first tick it can (input buffering).</para>
    /// <para>B. Hit reactions: every melee hit staggers (StaggerSeconds; HeavyStaggerSeconds at or over the victim's Poise)
    /// with diminishing returns inside StaggerWindowSeconds and StaggerImmuneSeconds of immunity after StaggerMaxInWindow, so
    /// no stun-lock; a short slow after it; and a knock of Strike.Knock (u at mass 1, divided by the victim's Mass) through the
    /// wall-sliding mover (walls, pits and edges stop it: no ring-outs). The Warrior's heavy swing is armoured: a hit under his
    /// Poise does not interrupt his wind-up.</para>
    /// <para>C. Class identity: the six rows below (GameConfig.Class returns them while the rule is on; HP is unchanged) plus
    /// the per-class strike extras.</para>
    /// <para>F. Ladders: movement over a built ladder lane is LadderSpeedMultiplier of ground speed (a readable climb),
    /// teammates pass through each other on ladder cells (one bot cannot block the lane), and a hit on a ladder knocks the
    /// climber back LadderFallKnock the way it came.</para>
    /// <para>G/H. Bots: in narrow spots (a non-walkable cell among the 8 neighbours) a bot follows the raw next-cell step
    /// instead of the blended flow (the blend steered into void corners at diagonal bridge ends and into the hill rock ring);
    /// the Healer keeps HealerBotStandOff from the enemy and mends the most hurt ally in range.</para>
    /// </summary>
    public sealed class CombatFeelConfig
    {
        /// <summary>The one switch (default ON in Full). False = the game before b15 combat feel, bit for bit.</summary>
        public bool Enabled = true;

        // ---- C. class rows (HP, move, radius, ability, gather and carry are the shipped rows; the attack and weight change).
        /// <summary>One full row per class, indexed by (int)ClassId; GameConfig.Class returns these while the rule is on.</summary>
        public ClassConfig[] Classes =
        {
            // WARRIOR: frontline. Slow heavy cleave (0.40 s wind-up, armoured), shield block (ability, unchanged). 27 every 1.05 s
            // (was 22 every 0.70 s: 26 DPS instead of 31) so he no longer wins every duel; heaviest body (mass 1.6).
            new ClassConfig
            {
                MaxHp = 160, MoveSpeed = 3.8, Radius = 0.45, AttackDamage = 27, AttackRange = 1.8, AttackArcDeg = 110,
                AttackCooldown = 1.05, AbilityCooldown = 10, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.40, RecoverySeconds = 0.30, Mass = 1.6, Poise = 30,
            },
            // FARMER: long staff (2.3 u reach, narrow 60 degree thrust) that pushes the target back out of its own reach; weak up
            // close (FarmerCloseShare inside FarmerCloseRange). 20 every 0.65 s.
            new ClassConfig
            {
                MaxHp = 100, MoveSpeed = 4.2, Radius = 0.4, AttackDamage = 20, AttackRange = 2.3, AttackArcDeg = 60,
                AttackCooldown = 0.65, AbilityCooldown = 9, GatherSeconds = 1, CarryCapacity = 3,
                AttackWindupSeconds = 0.27, RecoverySeconds = 0.22, Mass = 0.8, Poise = 20,
            },
            // MINER: fast pick (0.20 s wind-up, 0.55 s cycle). Armour break: his hits leave SUNDER (+25% melee damage taken for
            // 3 s) and pierce heavy bodies (x1.4 on Mass >= 1.3: the Warrior, the Builder); x2 on structures and walls.
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 12, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.55, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.20, RecoverySeconds = 0.18, Mass = 1.1, Poise = 24,
            },
            // DEMOLITIONS: weak pry bar (12 every 0.8 s; 10 measured 6.3 s on every class, over the 3-6 s band); her damage is the thrown TNT (telegraphed fuse, area), every
            // TntCooldownTicks instead of 150; her own blast hurts her (TntSelfShare): self-risk.
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 12, AttackRange = 1.4, AttackArcDeg = 90,
                AttackCooldown = 0.80, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.23, RecoverySeconds = 0.25, Mass = 1.0, Poise = 22,
            },
            // BUILDER: hammer (26 every 0.9 s, 0.33 s wind-up; 23 measured 6.1 s on the Warrior and 2.4% of all damage in 16v16) with the biggest knockback; x1.5 within BuilderHomeRadius of his
            // team's walls and barricades (strong at home, weak in the open).
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 26, AttackRange = 1.6, AttackArcDeg = 100,
                AttackCooldown = 0.90, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.33, RecoverySeconds = 0.30, Mass = 1.3, Poise = 26,
            },
            // HEALER: her row now says what her strike does (the audit: config 4.5 u / 0 damage, real strike 1.6 u / 8). Her
            // attack press MENDS the hurt ally in MendRange first (a visible beam, MendHeal, on her AttackCooldown); with nobody
            // to mend it swings her lantern: 15 at 1.5 u. Bots keep HealerBotStandOff from the enemy.
            new ClassConfig
            {
                MaxHp = 110, MoveSpeed = 4.1, Radius = 0.42, AttackDamage = 15, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.80, AbilityCooldown = 9, GatherSeconds = 1.5, CarryCapacity = 2,
                AttackWindupSeconds = 0.23, RecoverySeconds = 0.22, Mass = 0.9, Poise = 20,
            },
        };

        /// <summary>Per-class strike extras, indexed by (int)ClassId.</summary>
        public CombatFeelStrike[] Strikes =
        {
            new CombatFeelStrike { Knock = 0.9, ArmouredWindup = true },  // Warrior
            new CombatFeelStrike { Knock = 0.7 },                          // Farmer
            new CombatFeelStrike { Knock = 0.3 },                          // Miner
            new CombatFeelStrike { Knock = 0.4 },                          // Demolitions
            new CombatFeelStrike { Knock = 1.3 },                          // Builder
            new CombatFeelStrike { Knock = 0.6 },                          // Healer
        };

        // ---- A. attack phases
        /// <summary>An attack press this early (real seconds) before it could be accepted fires on the first tick it can.</summary>
        public double BufferSeconds = 0.20;
        /// <summary>Move speed while winding up and while recovering (no skating swings; the shipped physics value is 1.0).</summary>
        /// OWNER FEEL-TEST: 1.0 (no slow) by default. At 0.6 / 0.55 (with StaggerSlowMultiplier 0.6) the bots' fight strafing
        /// turned into back-and-forth on the hill (shiver gate: 37 hard flags at 0.6/0.55/0.6, 0 at 1/1/1, sim run 44237698220).
        public double WindupMoveMultiplier = 1.0;
        public double RecoveryMoveMultiplier = 1.0;
        /// <summary>Extra recovery (real seconds) after a swing that hit nothing: the whiff is punishable.</summary>
        public double WhiffRecoverySeconds = 0.15;

        // ---- B. hit reactions
        /// <summary>Every melee hit staggers this long (real seconds): input ignored, no attack; a non-armoured wind-up is cancelled.</summary>
        public double StaggerSeconds = 0.10;
        /// <summary>A hit at or over the victim's Poise staggers this long instead.</summary>
        public double HeavyStaggerSeconds = 0.25;
        /// <summary>Diminishing returns: each earlier stagger inside the window multiplies the next by StaggerDecay.</summary>
        public double StaggerWindowSeconds = 1.2;
        public double StaggerDecay = 0.5;
        /// <summary>After this many staggers inside one window the victim is immune to stagger for StaggerImmuneSeconds.</summary>
        public int StaggerMaxInWindow = 3;
        public double StaggerImmuneSeconds = 0.8;
        /// <summary>After a stagger the victim moves at StaggerSlowMultiplier for StaggerSlowSeconds.</summary>
        public double StaggerSlowSeconds = 0.30;
        public double StaggerSlowMultiplier = 1.0; // OWNER FEEL-TEST: 0.6 slows after a stagger (see WindupMoveMultiplier)

        // ---- C. class extras
        public double FarmerCloseRange = 1.2;
        public double FarmerCloseShare = 0.6;
        public double SunderSeconds = 3;
        public double SunderDamageTaken = 0.25;
        public double MinerHeavyPierce = 1.3;
        public double MinerHeavyMass = 1.3;
        public double MinerStructureMultiplier = 2;
        public double BuilderHomeRadius = 3.5;
        public double BuilderHomeMultiplier = 1.5;
        /// <summary>TNT cooldown (ticks at 30 Hz) while the rule is on (DemolitionsTntConfig.CooldownTicks is 150).</summary>
        public double TntCooldownTicks = 135; // 120 put her kill share at 0.29 (DemolitionsAttackThrowTests limit 0.25); 150 is the shipped value
        /// <summary>Share of her own TNT's blast damage the thrower takes when she stands inside its radius.</summary>
        public double TntSelfShare = 0.35;
        /// <summary>The Healer's mend while the rule is on (HealerConfig MendHeal 6 / MendRange 5.5).</summary>
        public double MendHeal = 10;
        public double MendRange = 6;
        /// <summary>Bots: a Healer keeps this far from the enemy she is near (she mends from there, swings only when caught).</summary>
        public double HealerBotStandOff = 4;

        // ---- F. ladders
        /// <summary>Speed over a built ladder's cells (a ladder is at most LadderMaxCells = 2 long: about 0.9 s to climb at 0.55).</summary>
        public double LadderSpeedMultiplier = 0.55;
        public bool LadderAllyPass = true;
        public double LadderFallKnock = 1.2;

        // ---- G. bots
        /// <summary>In narrow spots bots follow the raw next-cell step, not the blended flow. OFF by default: the shiver gate's
        /// match (seed 7) had 4 hard flags with it and 0 without (sim run 44233001957, CombatFeelTests shiver-by-part).</summary>
        public bool BotNarrowRawStep = false;
        /// <summary>Bots lead a moving target by at most this much of their wind-up (real seconds) when they chase and aim.</summary>
        public double BotLeadSeconds = 0.13;

        public CombatFeelConfig Clone()
        {
            var c = (CombatFeelConfig)MemberwiseClone();
            c.Classes = new ClassConfig[Classes.Length];
            for (int i = 0; i < Classes.Length; i++) c.Classes[i] = Classes[i].Clone();
            c.Strikes = new CombatFeelStrike[Strikes.Length];
            for (int i = 0; i < Strikes.Length; i++) c.Strikes[i] = Strikes[i].Clone();
            return c;
        }
    }

    /// <summary>b15 combat feel: the per-class strike extras (CombatFeelConfig.Strikes).</summary>
    public sealed class CombatFeelStrike
    {
        /// <summary>Knock of a landed hit (u travelled by a mass-1 body; Knock.Effective divides by the victim's Mass).</summary>
        public double Knock;
        /// <summary>Hits under this class's Poise do not stagger it (or cancel its swing) while it winds up.</summary>
        public bool ArmouredWindup;

        public CombatFeelStrike Clone() => (CombatFeelStrike)MemberwiseClone();
    }
}

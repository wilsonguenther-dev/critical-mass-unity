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
            // WARRIOR: frontline. Slow heavy cleave (0.40 s wind-up, armoured), shield block (ability, unchanged). The numbers of
            // every row below come from CombatFeelTuneTests' coordinate search (run printed in the PR), not hand-guessing.
            new ClassConfig
            {
                // pass 4: 140 -> 120 HP (Warrior on Warrior 6.10 s and Healer on Warrior 7.80 s were over their limits; his share of all damage 0.44).
                // pass 4b: 120 -> 115 HP, the tune search BEST (slow run 44841109806: total 9.40 -> 3.04, Demolitions gate 0.257 -> 0.224).
                MaxHp = 115, MoveSpeed = 3.8, Radius = 0.45, AttackDamage = 21, AttackRange = 1.8, AttackArcDeg = 110,
                AttackCooldown = 0.90, AbilityCooldown = 10, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.40, RecoverySeconds = 0.30, Mass = 1.6, Poise = 30,
            },
            // FARMER: long staff (2.3 u reach, narrow 60 degree thrust) that pushes the target back out of its own reach; weak up
            // close (FarmerCloseShare inside FarmerCloseRange). 23 every 0.65 s (pass 4b: 20 -> 23, tune search BEST).
            new ClassConfig
            {
                MaxHp = 100, MoveSpeed = 4.2, Radius = 0.4, AttackDamage = 23, AttackRange = 2.3, AttackArcDeg = 60,
                AttackCooldown = 0.65, AbilityCooldown = 9, GatherSeconds = 1, CarryCapacity = 3,
                AttackWindupSeconds = 0.27, RecoverySeconds = 0.22, Mass = 0.8, Poise = 20,
            },
            // MINER: fast pick (0.20 s wind-up, 0.65 s cycle), light body (110 HP). Armour break: his hits leave SUNDER (+15% for
            // 3 s) and pierce heavy bodies (x1.4 on Mass >= 1.3: the Warrior, the Builder); x2 on structures and walls.
            // pass 4: 95 HP / 14 -> 110 HP / 16 (his kill share had halved against control: 0.088 vs 0.168); pass 4b: 100 HP / 17
            // (tune search BEST; kill share 0.091 on vs 0.069 control in run 44841109806).
            new ClassConfig
            {
                MaxHp = 100, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 17, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.65, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.20, RecoverySeconds = 0.18, Mass = 1.1, Poise = 24,
            },
            // DEMOLITIONS: weak pry bar (12 every 0.7 s; 10 measured 6.3 s on every class, over the 3-6 s band); her damage is the thrown TNT (telegraphed fuse, area), every
            // TntCooldownTicks instead of 150; her own blast hurts her (TntSelfShare): self-risk.
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 12, AttackRange = 1.4, AttackArcDeg = 90,
                AttackCooldown = 0.70, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
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
        /// <summary>No stun-lock: a run of melee staggers (each new one landing before the last ends) never lasts longer than
        /// this (real seconds); a hit that would extend it past the cap is clipped and gives StaggerImmuneSeconds at once.</summary>
        public double StaggerMaxLockSeconds = 0.4;
        /// <summary>Pass 3: no stun from any source (melee, Kaiju, blast) carries one unbroken stagger run past this; a stun that
        /// lands inside a run is shortened to fit (Knock.Push). StaggerMaxLockSeconds caps the melee share inside it.</summary>
        public double StaggerChainCapSeconds = 0.5;
        /// <summary>Bots side-step when jammed (BotSteer.Unjam). False = the FSM's own unstick only.</summary>
        public bool BotUnjam = true;
        /// <summary>No side-step within this many u of the Kaiju (0 = no such rule).</summary>
        // Pass 4 default 6 (the bisect, 24 seeds, run 44836535142: see the PR).
        public double BotUnjamKaijuClear = 6;
        /// <summary>No side-step within the hill disc grown by this many u (negative = no such rule).</summary>
        public double BotUnjamHillClear = -1;
        /// <summary>Pass 4: no side-step within this many u of the hill centre while the Kaiju is hatched (the Kaiju's fight zone,
        /// KaijuHillFightRadius 13 plus its leash; 0 = no such rule).</summary>
        public double BotUnjamHillZone = 0;
        /// <summary>Pass 4: the side-step budget: at most BotUnjamBudget side-steps per bot in any BotUnjamBudgetSeconds window
        /// (0 = unbounded). Past it the FSM's own slower unstick (StuckSeconds) takes over.</summary>
        public int BotUnjamBudget = 3;   // pass 4: 3 per 6 s (unbounded: 24-seed on-disc 0.185, 3/6 s: 0.228; off: 0.278)
        public double BotUnjamBudgetSeconds = 6;
        /// <summary>After a stagger the victim moves at StaggerSlowMultiplier for StaggerSlowSeconds.</summary>
        public double StaggerSlowSeconds = 0.30;
        public double StaggerSlowMultiplier = 1.0; // OWNER FEEL-TEST: 0.6 slows after a stagger (see WindupMoveMultiplier)

        // ---- C. class extras
        public double FarmerCloseRange = 1.2;
        public double FarmerCloseShare = 0.6;
        public double SunderSeconds = 3;
        public double SunderDamageTaken = 0.15;
        public double MinerHeavyPierce = 1.4;
        public double MinerHeavyMass = 1.3;
        public double MinerStructureMultiplier = 2;
        public double BuilderHomeRadius = 3.5;
        public double BuilderHomeMultiplier = 1.5;

        // ---- D. b16 duel order (design advisor 2026-11-21: "fighter class is all about fighting ... they should be winning all
        // 1v1 fights based on equal skill"; "healer class should be killable by all other classes one on one"). Each lever is
        // one switch; the values below are the ones the 72-seed arms chose (docs/balance, PR body).
        /// <summary>True (b15): a SUNDERED body (a Miner hit it in the last SunderSeconds) loses its armoured wind-up. False: the
        /// Warrior's armour holds against every hit under his Poise, SUNDER or not (SUNDER still adds its damage).</summary>
        public bool SunderBreaksArmour = true;
        /// <summary>True (b15): the Miner's heavy pierce (MinerHeavyPierce on Mass at or over MinerHeavyMass) applies to the
        /// Warrior too. False: it pierces the Builder (and any other heavy non-fighter) but not the Warrior.</summary>
        public bool MinerPierceWarrior = true;
        /// <summary>Share of the attack cooldown given back when a stagger cancels a wind-up (0 = b15: a cancelled swing
        /// keeps its full cooldown, so the faster wind-up always wins the exchange).</summary>
        public double StaggerCancelRefund = 0;
        /// <summary>Per class (indexed by ClassId): does a landed melee hit of this class stagger (and so cancel a wind-up)?
        /// null = every class staggers (b15). The knock and the damage are unchanged either way.</summary>
        public bool[] StrikeStaggers = null;
        /// <summary>Share of her own HEALING WINGS pulse the Healer gets herself (1 = b15; her allies always get all of it).</summary>
        public double PulseSelfShare = 1;
        /// <summary>Per class: extra recovery (real seconds) on top of WhiffRecoverySeconds after a swing that hit nothing
        /// (null = none). The trade for a strong 1v1 Warrior: a miss in a crowd is punished.</summary>
        public double[] WhiffExtraSeconds = null;
        /// <summary>Outnumbered: an armoured class hit by a SECOND enemy within OutnumberedSeconds of another enemy's hit loses
        /// its armoured wind-up for OutnumberedSeconds (one on one it never triggers). False = b15.</summary>
        public bool OutnumberedBreaksArmour = false;
        public double OutnumberedSeconds = 1.0;

        // ---- E. b16 CANCEL button (test feature for high-level play; design advisor 2026-11-21: "it should cancel the attack
        // animation and allows for feints. also cancelling the attack animation after a hit lands would allow people to
        // potentially attack a little bit faster"). InputFrame.Cancel, one press = one cancel (Sim/CombatFeel.cs TakeCancel).
        /// <summary>The one switch (default ON in Full so the owner and the advisor can feel it; the settings toggle turns it
        /// off for the local player, the rule stays in the config so every machine agrees).</summary>
        public bool CancelEnabled = true;
        /// <summary>FEINT: a press during the wind-up drops the swing (no hit). The attacker cannot swing again for
        /// FeintRecoverySeconds; FeintRefund of the attack cooldown left is given back (the rest is the price).</summary>
        public double FeintRecoverySeconds = 0.10;
        public double FeintRefund = 0.70;
        /// <summary>No second feint within this many real seconds of a feint (a feint cannot be chained for free).</summary>
        public double FeintLockoutSeconds = 0.5;
        /// <summary>HIT-CANCEL: a press during the recovery after a swing that LANDED (never before the hit tick, never on a
        /// whiff) cuts this share of the recovery left, and the cooldown by the same time.</summary>
        public double HitCancelShare = 0.40;
        /// <summary>TNT cooldown (ticks at 30 Hz) while the rule is on (DemolitionsTntConfig.CooldownTicks is 150).</summary>
        // pass 4: 120 -> 180 (with the Warrior at 120 HP her knock-out share in DemolitionsAttackThrowTests was 0.309 at 120 and
        // 0.269 at 150 with TNT 46, limit 0.25; runs 44834829109, 44837951367). A duel still sees one throw.
        public double TntCooldownTicks = 180;
        /// <summary>Share of her own TNT's blast damage the thrower takes when she stands inside its radius.</summary>
        public double TntSelfShare = 0.35;
        /// <summary>Thrown TNT blast damage while the rule is on (DemolitionsTntConfig.Damage is 55).</summary>
        public double TntDamage = 46;   // pass 4: 55 -> 46 (her share, see TntCooldownTicks: 0.259 at 50, run 44837670714)
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
            if (StrikeStaggers != null) c.StrikeStaggers = (bool[])StrikeStaggers.Clone();
            if (WhiffExtraSeconds != null) c.WhiffExtraSeconds = (double[])WhiffExtraSeconds.Clone();
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

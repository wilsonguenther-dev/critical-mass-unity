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
                // b16 duel order (design advisor 2026-11-21, lever sweep runs 44921976456 + 44924217345): 21 -> 24 damage and
                // 3.8 -> 3.61 move (5% slower, "so the non fighter has a chance of escape"); the one-on-one edge is SoloGuard.
                MaxHp = 115, MoveSpeed = 3.61, Radius = 0.45, AttackDamage = 24, AttackRange = 1.8, AttackArcDeg = 110,
                AttackCooldown = 0.90, AbilityCooldown = 10, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.40, RecoverySeconds = 0.30, Mass = 1.6, Poise = 30,
            },
            // FARMER: long staff (2.3 u reach, narrow 60 degree thrust) that pushes the target back out of its own reach; weak up
            // close (FarmerCloseShare inside FarmerCloseRange). 23 every 0.65 s (pass 4b: 20 -> 23, tune search BEST).
            new ClassConfig
            {
                // px S3: cooldown 0.65 -> 0.70 (a lag-6 human Farmer beat the middle three 0.75 with the bots at human limits;
                // data/s3-tune/sweep/t10-t13).
                MaxHp = 100, MoveSpeed = 4.2, Radius = 0.4, AttackDamage = 22, AttackRange = 2.3, AttackArcDeg = 60,   // px S3: 23 -> 22 (middle three 0.79 -> 0.75)
                AttackCooldown = 0.70, AbilityCooldown = 9, GatherSeconds = 1, CarryCapacity = 3,
                AttackWindupSeconds = 0.31, RecoverySeconds = 0.22, Mass = 0.8, Poise = 20,   // b16: 0.27 -> 0.31 wind-up (no middle-class king)
            },
            // MINER: fast pick (0.20 s wind-up, 0.65 s cycle), light body (110 HP). Armour break: his hits leave SUNDER (+15% for
            // 3 s) and pierce heavy bodies (x1.4 on Mass >= 1.3: the Warrior, the Builder); x2 on structures and walls.
            // pass 4: 95 HP / 14 -> 110 HP / 16 (his kill share had halved against control: 0.088 vs 0.168); pass 4b: 100 HP / 17
            // (tune search BEST; kill share 0.091 on vs 0.069 control in run 44841109806).
            new ClassConfig
            {
                // px S1 [3]: 17 -> 19 (19, not 20: 20 reaches the Poise 20 of the Farmer and Healer, a heavy stagger; human-seat probe: a lag-6 human Miner won 10 of 48 duels vs Normal bots and a human Healer beat
                // a Miner bot 4 of 4; at 20: 20 of 24 and 2 of 4, non-mirror share 0.52 -> 0.58; data/s1-combat/sweep/t1_*).
                // px S3: 100 -> 115 HP (a human Healer beat the Miner bot 5 of 8; at 115 her 14 lantern needs 9 hits, not 7;
                // middle-three share 0.25 -> 0.63; data/s3-tune/sweep/t1, t13).
                MaxHp = 115, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 19, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.65, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.35, RecoverySeconds = 0.18, Mass = 1.1, Poise = 24,   // b16: 0.20 -> 0.35 wind-up (the swing only; mining is GatherSeconds and the terraform rules)
            },
            // DEMOLITIONS: weak pry bar (12 every 0.7 s; 10 measured 6.3 s on every class, over the 3-6 s band); her damage is the thrown TNT (telegraphed fuse, area), every
            // TntCooldownTicks instead of 150; her own blast hurts her (TntSelfShare): self-risk.
            new ClassConfig
            {
                // px S3: pry bar 15 -> 18 and cooldown 0.70 -> 0.65 (a lag-6 human Demolitionist won 0.21 of her duels vs Normal
                // bots, 3 of 8 vs the Healer bot, middle three 0.25; at 18 she needs 7 hits on the Builder, Healer and Miner, not 8).
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 18, AttackRange = 1.4, AttackArcDeg = 90,   // b16: 12 -> 15; px S3: 18
                AttackCooldown = 0.65, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.23, RecoverySeconds = 0.25, Mass = 1.0, Poise = 22,
            },
            // BUILDER: hammer (26 every 0.9 s, 0.33 s wind-up; 23 measured 6.1 s on the Warrior and 2.4% of all damage in 16v16) with the biggest knockback; x1.5 within BuilderHomeRadius of his
            // team's walls and barricades (strong at home, weak in the open).
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 26, AttackRange = 1.6, AttackArcDeg = 100,
                AttackCooldown = 0.90, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                // px S3: wind-up 0.33 -> 0.35 (a human Builder won 0.83 of the middle three; 0.38 swung it the other way, 0.50)
                AttackWindupSeconds = 0.35, RecoverySeconds = 0.30, Mass = 1.3, Poise = 26,
            },
            // HEALER: her row now says what her strike does (the audit: config 4.5 u / 0 damage, real strike 1.6 u / 8). Her
            // attack press MENDS the hurt ally in MendRange first (a visible beam, MendHeal, on her AttackCooldown); with nobody
            // to mend it swings her lantern: 15 at 1.5 u. Bots keep HealerBotStandOff from the enemy.
            new ClassConfig
            {
                // px S3: lantern 15 -> 14 (the Healer loses every 1v1: with the Miner at 115 HP she needs 9 hits, not 8)
                MaxHp = 110, MoveSpeed = 4.1, Radius = 0.42, AttackDamage = 14, AttackRange = 1.5, AttackArcDeg = 90,
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
        public double BufferSeconds = 0.35;   // px S1 [8]: 0.20 -> 0.35 (hit-feel audit P2: taps in rhythm were eaten; a spell queue is ~0.4 s)
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
        public double MinerHeavyPierce = 1.25;   // px S1 [3]: 1.4 -> 1.25 with the Miner at 19 (Miner on Builder stays 6 hits, 3+ s)
        public double MinerHeavyMass = 1.3;
        public double MinerStructureMultiplier = 2;
        public double BuilderHomeRadius = 3.5;
        public double BuilderHomeMultiplier = 1.5;

        // ---- D. b16 duel order (design advisor 2026-11-21: "fighter class is all about fighting ... they should be winning all
        // 1v1 fights based on equal skill"; "healer class should be killable by all other classes one on one"). Each lever is
        // one switch; the values below are the ones the 72-seed arms chose (docs/balance, PR body).
        /// <summary>True (b15): a SUNDERED body (a Miner hit it in the last SunderSeconds) loses its armoured wind-up. False: the
        /// Warrior's armour holds against every hit under his Poise, SUNDER or not (SUNDER still adds its damage).</summary>
        public bool SunderBreaksArmour = false;   // b16 (b15: true)
        /// <summary>True (b15): the Miner's heavy pierce (MinerHeavyPierce on Mass at or over MinerHeavyMass) applies to the
        /// Warrior too. False: it pierces the Builder (and any other heavy non-fighter) but not the Warrior.</summary>
        public bool MinerPierceWarrior = true;
        /// <summary>Share of the attack cooldown given back when a stagger cancels a wind-up (0 = b15: a cancelled swing
        /// keeps its full cooldown, so the faster wind-up always wins the exchange).</summary>
        public double StaggerCancelRefund = 0.6;   // b16 (b15: 0)
        /// <summary>Per class (indexed by ClassId): does a landed melee hit of this class stagger (and so cancel a wind-up)?
        /// null = every class staggers (b15). The knock and the damage are unchanged either way.</summary>
        public bool[] StrikeStaggers = { true, true, true, true, true, false };   // b16: the Healer's lantern does not stagger (b15: null)
        /// <summary>Share of her own HEALING WINGS pulse the Healer gets herself (1 = b15; her allies always get all of it).</summary>
        public double PulseSelfShare = 1;
        /// <summary>Per class: extra recovery (real seconds) on top of WhiffRecoverySeconds after a swing that hit nothing
        /// (null = none). The trade for a strong 1v1 Warrior: a miss in a crowd is punished.</summary>
        public double[] WhiffExtraSeconds = { 0, 0, 0, 0, 0, 0 };   // px S1 [3]: Warrior 0.25 -> 0 (a 50%-landing human Warrior paid 0.40 s per miss; with it
        // 0 and SoloGuard 0.45 he wins 6-8 of 8 against every other class's Normal bot, probe pass3). b16: 0.25; b15: null
        /// <summary>Outnumbered: an armoured class hit by a SECOND enemy within OutnumberedSeconds of another enemy's hit loses
        /// its armoured wind-up for OutnumberedSeconds (one on one it never triggers). False = b15.</summary>
        public bool OutnumberedBreaksArmour = true;   // b16 trade (b15: false)
        public double OutnumberedSeconds = 1.0;
        /// <summary>Per target class: share of melee damage it shrugs off while ONE enemy fights it (no other enemy struck it
        /// within OutnumberedSeconds; null = none). The fighter's one-on-one edge that does not carry into a crowd.</summary>
        public double[] SoloGuard = { 0.45, 0, 0, 0, 0, 0 };   // px S1 [3]: 0.2 -> 0.45 (the one-on-one edge only; probe pass2 -> pass3: 50% Warrior 26 -> 36 of 48). b16: 0.2; b15: null

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

        // ---- H. px S1 player-seat combat (owner 2026-11-22 after 1.0.1409: "I can be in a player's face, swing, and I see nothing
        // to indicate I've hit ... This is for real players. The bots are fillers"). Measured with the human-seat probe
        // (~/cm-incidents/player-experience-rebuild-2026-11-22/data/s1-combat/), not bot-vs-bot statistics.
        /// <summary>[1] Swing tracking: during the wind-up the swing turns toward the current aim at up to this many degrees a
        /// second (every actor, bots too). Audit fork: a 0.2 s-lag human landed 37% -> 78% at 360. 0 = locked at the press (b16).</summary>
        public double SwingTrackDegPerSecond = 360;
        /// <summary>[1] Per class override of SwingTrackDegPerSecond (indexed by ClassId; 0 or null = the shared value).</summary>
        public double[] SwingTrackDegPerSecondByClass = null;
        /// <summary>[1] The swing stops turning this many ticks before its contact tick (a late side-step or dodge still beats it).</summary>
        public int SwingTrackLockTicks = 2;

        /// <summary>[6] A hit under the victim's Poise does not stagger (so does not cancel) a wind-up of any class; heavy hits
        /// still do. Baseline: 378 of 2,628 human swing starts (14%) were cancelled by a 0.10 s light stagger (target at most 10%).</summary>
        public bool LightHitsKeepWindup = true;
        /// <summary>[6] ... from this share of the wind-up on (0 = the whole wind-up). Light hits earlier still cancel.</summary>
        public double LightHitKeepFromShare = 0;
        /// <summary>[6] Per attacking class (by ClassId): its light hits still interrupt a wind-up (null = none). The Farmer's quick
        /// long thrust keeps its interrupt: without it she lost every bot-vs-bot middle duel (DuelOrderTests "Farmer winless";
        /// raising her damage instead broke the 3 s time-to-kill floor; sweeps in data/s1-combat/sweep/duelorder-sweep*.txt).</summary>
        public bool[] LightHitInterrupts = { false, true, false, false, false, false };
        /// <summary>[4][5] A landed melee hit leaves the victim WOUNDED for this many real seconds (0 = no such rule).</summary>
        public double WoundedSeconds = 2.0;
        /// <summary>[4] Share of mend and pulse healing a wounded body receives. Baseline: one Healer bot (15.8 HP/s) out-healed
        /// a 50%-landing human in 25 of 36 match-ups (audit); probe healed mode 29/288 kills within 20 s.</summary>
        public double WoundedHealShare = 0.3;   // pass 2: 0.5 -> 0.3
        /// <summary>[4] Share of the fountain's 30 HP/s a wounded body receives in its own fountain.</summary>
        public double WoundedFountainShare = 0.25;   // pass 2: 0.5 -> 0.25 (30 HP/s -> 7.5 while wounded: under a landing Warrior's ~19 HP/s)
        /// <summary>[5] A wounded body below this share of its HP moves at WoundedSlowMultiplier (catchable when it flees).
        /// Baseline: off the hill a 29%-HP bot fled from a lag-6 human Warrior and was caught within 4 s 5 of 48 times.</summary>
        public double WoundedSlowHpShare = 0.3;
        public double WoundedSlowMultiplier = 0.75;
        /// <summary>[9] Critical strikes, earned by skill and never rolled (co-founder ask, PLAN call 12): a melee hit on a body in
        /// its own wind-up (counter), in the recovery of its whiff (punish) or from its rear arc (back hit). Measured in
        /// data/s1-combat/crit/. OFF by default: the ask's bar "the strong model gains >= 5 points" is not shown yet. Measured on
        /// (288 duels each): lag-6 0.50 -> 0.50, 50%-player 0.28 -> 0.30, first-match floor 0.27 -> 0.27, a 0.13 s human 0.53 ->
        /// 0.54 (+1; the probe has no feint / back-circling model yet). Turn on once that model shows the gain.
        /// px S3: ON. The gate's strong model (feints, dodges, hit-cancels) gains 0.68 -> 0.73 vs Normal (S48b passes) and
        /// 0.51 -> 0.53 vs Hard; the first-match floor holds (0.30 -> 0.30); Warrior wins all (39/40), Healer loses all (3/40);
        /// S43, S44, S35 stay green; the only band move is the Demolitions middle-three share 0.50 -> 0.75 (5 points over).
        /// Same-tick trades are not COUNTERs (CritKindOf). Data: data/s3-tune/sweep/crit1.25.txt, crit1.15.txt.</summary>
        public bool CritEnabled = true;
        /// <summary>[9] Damage of a critical hit, applied after every other multiplier (kinds do not stack). Start value of the ask.</summary>
        public double CritDamageMul = 1.25;
        /// <summary>[9] The rear arc (degrees, centred on the back) a back hit comes from: 120 = more than 120 deg off his facing.</summary>
        public double CritBackArcDeg = 120;   // pass 2: 0.85 -> 0.75 (lag-6 Warrior catch within 4 s 17/48 -> 22/48)
        /// <summary>[2] Bots fight by human limits (BotAim): aim delay, aim error and a class-arc press cone. False = b16 bots.</summary>
        public bool BotHumanLimits = true;
        /// <summary>px S3: share of 1/3-s windows in which a bot holds a ready swing, by BotDifficulty (Normal, Easy, Hard); the
        /// larger of this and the BotSkillConfig.SwingHesitation profile wins (Easy's profile is 0.4). Full rules only (BotAim.On).
        /// null = off (S1). Measured in data/s3-tune/.</summary>
        public double[] BotSwingHesitation = { 0.3, 0.85, 0 };   // px S3: S48 floor vs Easy 0.36 -> 0.77, vs Normal 0.25 -> 0.29; ceiling 0.63 -> 0.68-0.72
        /// <summary>px DM (slow triage R1): BotSwingHesitation holds only a swing at a HUMAN seat. Off = every bot swing (at the
        /// Kaiju, structures and other bots too), which cut bot damage on the Kaiju: own-Kaiju blocking 13.8 -> 42.9 s a match.</summary>
        public bool BotSwingHesitationHumansOnly = true;
        /// <summary>[2] Melee aim delay (ticks at 30 Hz) by BotDifficulty (Normal, Easy, Hard): the bot aims at where its target
        /// was this long ago (a 0.2 s human = 6). Baseline: Normal bots landed 94% on a circle-strafing human (target 40-75%).</summary>
        public int[] BotMeleeAimDelayTicks = { 5, 12, 2 };   // px S3: Easy 8 -> 12 (S48 floor vs Easy, data/s3-tune)
        /// <summary>[2] Melee aim error (degrees, uniform +-) by BotDifficulty, redrawn every 1/3 s.</summary>
        public double[] BotMeleeAimErrorDeg = { 20, 45, 5 };   // px S3: Easy 35 -> 45 (S48 floor vs Easy, data/s3-tune)
        /// <summary>[2] A bot's swing tracks at this share of SwingTrackDegPerSecond, by BotDifficulty (Normal, Easy, Hard).
        /// Measured: with full tracking a Normal bot landed 95% on an evasive circle-strafing human (target 40-75%).</summary>
        public double[] BotSwingTrackScale = { 0.1, 0, 0.5 };   // Normal 0.1 -> 0.62-0.76 measured (sweep in FINDINGS)
        /// <summary>[3] Melee dodge chance of a bot (Easy, Normal, Hard: DodgeConfig order) while BotHumanLimits is on; null =
        /// DodgeConfig.DodgeChance (0.15 / 0.35 / 0.60). Measured: Normal 0.35 -> 0.10 lifts the lag-6 human's non-mirror duel
        /// share 0.46 -> 0.52 (each dodged swing costs the attacker its whiff recovery, 0.40 s for the Warrior).</summary>
        public double[] BotMeleeDodgeChance = { 0.05, 0.10, 0.60 };
        /// <summary>[2] The bot presses only with the target inside its own class arc (b16: a flat 60 degrees each side for all).</summary>
        public bool BotPressConeIsClassArc = true;

        public CombatFeelConfig Clone()
        {
            var c = (CombatFeelConfig)MemberwiseClone();
            c.Classes = new ClassConfig[Classes.Length];
            for (int i = 0; i < Classes.Length; i++) c.Classes[i] = Classes[i].Clone();
            c.Strikes = new CombatFeelStrike[Strikes.Length];
            for (int i = 0; i < Strikes.Length; i++) c.Strikes[i] = Strikes[i].Clone();
            if (StrikeStaggers != null) c.StrikeStaggers = (bool[])StrikeStaggers.Clone();
            if (WhiffExtraSeconds != null) c.WhiffExtraSeconds = (double[])WhiffExtraSeconds.Clone();
            if (SoloGuard != null) c.SoloGuard = (double[])SoloGuard.Clone();
            if (BotMeleeAimDelayTicks != null) c.BotMeleeAimDelayTicks = (int[])BotMeleeAimDelayTicks.Clone();
            if (LightHitInterrupts != null) c.LightHitInterrupts = (bool[])LightHitInterrupts.Clone();
            if (BotMeleeDodgeChance != null) c.BotMeleeDodgeChance = (double[])BotMeleeDodgeChance.Clone();
            if (BotSwingTrackScale != null) c.BotSwingTrackScale = (double[])BotSwingTrackScale.Clone();
            if (BotMeleeAimErrorDeg != null) c.BotMeleeAimErrorDeg = (double[])BotMeleeAimErrorDeg.Clone();
            if (BotSwingHesitation != null) c.BotSwingHesitation = (double[])BotSwingHesitation.Clone();
            if (SwingTrackDegPerSecondByClass != null) c.SwingTrackDegPerSecondByClass = (double[])SwingTrackDegPerSecondByClass.Clone();
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

namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// The sim-physics lane (PHYSICS-BUILD-LIST DG-67..DG-100, owner decisions OD-1..OD-11): every number of the lane in one
    /// block, so the owner can change any of them without code. Per-class numbers (wind-up, recovery, mass, poise) sit in the
    /// class table (ClassConfig, appended fields). Not in the browser.
    /// <para>Classic = true is browser parity: every rule below is off and every timing is today's, so the world hash is the
    /// pre-physics one bit for bit (DG21NormalIsTodaysBotsBitForBitOnTheGoldenSeeds runs with it). Each rule also has its own
    /// switch where the build list asks for one.</para>
    /// <para>Units: "real seconds" are seconds the player sees at the match pace (WorldOptions.Pace, default
    /// GameConfig.DefaultSpeed); "sim seconds" scale with the pace. Durations.cs converts both to ticks once per world.</para>
    /// </summary>
    public sealed class PhysicsConfig
    {
        /// <summary>Browser parity master switch: all physics rules off, today's timings (the "classic" setting).</summary>
        public bool Classic = false;

        // ---- DG-67 fair trades
        /// <summary>All melee hits of a tick are collected first and applied together, so actor id order never decides a trade.</summary>
        public bool FairTrades = true;

        // ---- DG-68 / OD-1 reaction floors (real seconds, whatever the pace)
        /// <summary>OD-7: the Kaiju claw wind-up lasts at least this long on screen.</summary>
        public double KaijuClawMinRealSeconds = 0.45;
        /// <summary>OD-7: the Kaiju slam telegraph lasts at least this long on screen.</summary>
        public double KaijuSlamMinRealSeconds = 0.8;

        // ---- DG-69 / OD-2 attack wind-up (per-class length: ClassConfig.AttackWindupSeconds, real seconds)
        /// <summary>Melee swings wind up before they land (false = damage on the press tick, the browser).</summary>
        public bool AttackWindup = true;
        /// <summary>Move speed multiplier while winding up and recovering (no skating swings).</summary>
        public double SwingMoveMultiplier = 0.4;

        // ---- DG-70 / OD-3 knockback, hit-stun, weight (per-class: ClassConfig.Mass, ClassConfig.Poise)
        /// <summary>Hits shove, heavy hits stagger. Ships ON only while the soak bands hold (see the PR's balance table).</summary>
        public bool Knockback = true;
        /// <summary>Knock velocity kept per 1/30 s (exponential decay, re-derived for other SimHz).</summary>
        public double KnockDecayPer30Hz = 0.82;
        /// <summary>Knock velocity below this (u/s) is zeroed.</summary>
        public double KnockStopSpeed = 0.1;
        /// <summary>Melee knock distance (u, for mass 1) per point of damage: the Warrior's 22 cleave = 0.66 u.</summary>
        public double MeleeKnockPerDamage = 0.03;
        /// <summary>Kaiju claw knock distance (u, mass 1).</summary>
        public double KaijuClawKnock = 2.0;
        /// <summary>Slam knock distance at the centre and at the edge (u, mass 1), outward.</summary>
        public double SlamKnockCentre = 2.4;
        public double SlamKnockEdge = 0.6;
        /// <summary>TNT / mine knock distance at the centre and at the edge (u, mass 1), outward.</summary>
        public double BlastKnockCentre = 1.8;
        public double BlastKnockEdge = 0.5;
        /// <summary>DG-99: a landing pillar shoves anyone under it this far (u, mass 1), out of its strip.</summary>
        public double CrushKnock = 1.0;
        /// <summary>A Warrior's raised shield takes the push from the front: only this share of the knock reaches him.</summary>
        public double ShieldFrontKnockShare = 0.3;
        /// <summary>Hit tiers (PR #30's table): light under HeavyHitDamage, heavy, critical at CriticalHitDamage or a finishing blow.</summary>
        public double HeavyHitDamage = 20;
        public double CriticalHitDamage = 40;
        /// <summary>Hit-stun (real seconds): a hit at or over the victim's Poise staggers; Kaiju hits stun; the slam knocks down.</summary>
        public double HeavyStunSeconds = 0.1;
        public double KaijuStunSeconds = 0.3;
        public double SlamStunSeconds = 0.5;
        public double BlastStunSeconds = 0.2;
        public double CrushStunSeconds = 0.3;

        // ---- DG-83 / OD-4
        /// <summary>Walls, barricades and intact breakables block melee, Kaiju hits, slam, blasts and wall breaks.</summary>
        public bool WallsBlockHits = true;

        // ---- DG-84 / OD-7 claw
        /// <summary>The claw hits the cone it showed at wind-up start (not the locked target), every hostile in it.</summary>
        public bool KaijuClawCone = true;
        public double KaijuClawConeDeg = 90;

        // ---- DG-85 / OD-8 slam
        /// <summary>Slam damage: full inside SlamFullRadiusShare x R, falling linearly to SlamEdgeDamageShare at R.</summary>
        public bool SlamShockwave = true;
        public double SlamFullRadiusShare = 0.35; // 1.75 u: anyone touching the solid body (1.3 + r) takes the full slam
        public double SlamEdgeDamageShare = 0.38;
        /// <summary>An actor in the air on the resolve tick takes no slam damage and no push.</summary>
        public bool AirDodgesSlam = true;

        // ---- DG-86 / OD-5 solid Kaiju
        /// <summary>The Kaiju is an immovable disc fighters cannot enter; it shoves them as it walks; no trample damage.</summary>
        public bool SolidKaiju = true;
        public double KaijuBodyRadius = 1.3;
        /// <summary>The Kaiju's wall collider while SolidKaiju (KaijuConfig.GridRadius 0.7 otherwise): its body no longer sinks into walls.</summary>
        public double KaijuGridRadius = 1.1;

        // ---- DG-97 / OD-9 crowds
        /// <summary>Order-free relaxed separation: pushes accumulated, Iterations passes at Relax, capped per tick, split by mass.</summary>
        public bool RelaxedSeparation = true;
        public int SeparationIterations = 3;
        public double SeparationRelax = 0.6;
        public double SeparationMaxStep = 0.15;
        /// <summary>Teammates push each other this much softer than enemies (a carrier squeezes past).</summary>
        public double AllyPushShare = 0.5;
        /// <summary>Bodies slide round convex wall corners instead of snagging (S10, every actor).</summary>
        public bool CornerRounding = true;
        /// <summary>Largest overlap with a corner cell that is slid round (u); deeper overlaps still stop.</summary>
        public double CornerNudgeMax = 0.25;

        // ---- DG-98 / OD-8 blasts
        /// <summary>Blast damage falls from full at the centre to BlastEdgeDamageShare at the edge; push outward; air = push only.</summary>
        public bool BlastShockwave = true;
        public double BlastEdgeDamageShare = 0.35;
        /// <summary>A blast arms every live TNT or armed mine in its radius to blow on the next tick.</summary>
        public bool ChainReaction = true;

        // ---- DG-99 pillars
        /// <summary>A toppling pillar changes the ground when it lands (Topple.FallTicks later), crushing and pushing anyone under it.</summary>
        public bool PillarFallsOnLanding = true;
        public double PillarCrushDamage = 25;

        // ---- DG-100 polish
        /// <summary>The cleave arc is widened per target by asin(r / d), so a swing at a big body's flank connects.</summary>
        public bool ArcCoversBody = true;
        /// <summary>Meat orbs pop a short deterministic hop from the corpse onto walkable ground instead of stacking.</summary>
        public bool MeatPop = true;
        public double MeatPopDistance = 0.6;

        // ---- OD-10 / OD-11
        /// <summary>Most enemies one swing can hit (0 = no cap; OD-10 says no cap for now).</summary>
        public int CleaveMaxTargets = 0;

        public PhysicsConfig Clone() => (PhysicsConfig)MemberwiseClone();
    }
}

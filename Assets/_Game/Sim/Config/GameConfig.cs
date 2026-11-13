namespace CriticalMass.Sim.Config
{
    /// <summary>Every gameplay number (browser GAME_CONFIG, src/config/game.config.ts); Sim code reads it and never writes it.</summary>
    public sealed class GameConfig
    {
        /// <summary>Default RNG seed.</summary>
        public int Seed = 1337;
        /// <summary>Fixed simulation rate (Hz).</summary>
        public double SimHz = 30;

        /// <summary>Players per team including the human (U-004 default 16; browser main 5).</summary>
        public int TeamSize = 16;
        /// <summary>Team sizes the menu and debug overlay offer.</summary>
        public int[] TeamSizeOptions = { 5, 16 };
        /// <summary>Largest team size; sizes actor storage.</summary>
        public int TeamSizeMax = 16;
        /// <summary>Initial match pace (driver time scale, sim ticks per wall second / SimHz); CM-66 default 2.</summary>
        public double DefaultSpeed = 2;

        /// <summary>Match clock (sim seconds); hill time is the only win condition.</summary>
        public double MatchSeconds = 480;
        /// <summary>Egg hatches at this many seconds elapsed (4:00 of 8:00).</summary>
        public double HatchSeconds = 240;
        /// <summary>Hill totals within this many seconds of each other at the final whistle are a draw.</summary>
        public double DrawEpsilonSeconds = 0.1;

        public HillConfig Hill = new HillConfig();
        public MovementConfig Movement = new MovementConfig();
        public JumpConfig Jump = new JumpConfig();
        public RespawnConfig Respawn = new RespawnConfig();
        public HealConfig Heal = new HealConfig();
        public KaijuFeedConfig KaijuFeed = new KaijuFeedConfig();
        public ResourcesConfig Resources = new ResourcesConfig();
        public KaijuConfig Kaiju = new KaijuConfig();

        /// <summary>One stat row per class, indexed by (int)ClassId.</summary>
        public ClassConfig[] Classes =
        {
            new ClassConfig
            {
                MaxHp = 160, MoveSpeed = 3.8, Radius = 0.45, AttackDamage = 22, AttackRange = 1.7, AttackArcDeg = 120,
                AttackCooldown = 0.7, AbilityCooldown = 10, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.12, RecoverySeconds = 0.08,
            },
            new ClassConfig
            {
                MaxHp = 100, MoveSpeed = 4.2, Radius = 0.4, AttackDamage = 12, AttackRange = 1.4, AttackArcDeg = 90,
                AttackCooldown = 0.6, AbilityCooldown = 9, GatherSeconds = 1, CarryCapacity = 3,
                AttackWindupSeconds = 0.10, RecoverySeconds = 0.08,
            },
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 16, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.65, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.10, RecoverySeconds = 0.08,
            },
            // CM-38: the Demolitions Expert has no regular attack; the attack fields keep the CM-56 miner copy.
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 16, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.65, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.10, RecoverySeconds = 0.08,
            },
            // CM-56 placeholder: the miner row until CM-39 tunes it. DG-24 keeps it (the mallet swing; Space builds, 12 s).
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 16, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.65, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
                AttackWindupSeconds = 0.10, RecoverySeconds = 0.08,
            },
            // DG-40 (U-061, not in the browser): the Healer. No weapon, no damage: her attack press is the MEND (Healer.cs);
            // AttackRange is how close bots keep to a fight (they stop there and press attack); AttackCooldown paces the mend.
            new ClassConfig
            {
                MaxHp = 110, MoveSpeed = 4.1, Radius = 0.42, AttackDamage = 0, AttackRange = 4.5, AttackArcDeg = 90,
                AttackCooldown = 0.8, AbilityCooldown = 9, GatherSeconds = 1.5, CarryCapacity = 2,
                AttackWindupSeconds = 0, RecoverySeconds = 0, // the mend is instant
            },
        };

        public AbilitiesConfig Abilities = new AbilitiesConfig();
        public BotsConfig Bots = new BotsConfig();
        public RenderConfig Render = new RenderConfig();
        public DebugConfig Debug = new DebugConfig();
        /// <summary>The sim-physics lane (DG-67..DG-100): every rule and number in one block (PhysicsConfig.Classic = browser parity).</summary>
        public PhysicsConfig Physics = new PhysicsConfig();

        /// <summary>The game's config: 16v16 at 2x pace (U-004), every other value the browser main value.</summary>
        public static GameConfig CreateDefault()
        {
            var c = new GameConfig();
            c.TeamSize = 16;
            c.DefaultSpeed = 2;
            return c;
        }

        /// <summary>The browser main defaults (teamSize 5, defaultSpeed 1); ported tests build worlds from it.</summary>
        public static GameConfig CreateBrowser5v5()
        {
            var c = CreateDefault();
            c.TeamSize = 5;
            c.DefaultSpeed = 1;
            return c;
        }

        /// <summary>Deep copy: every group object and array is copied.</summary>
        public GameConfig Clone()
        {
            var c = (GameConfig)MemberwiseClone();
            c.TeamSizeOptions = (int[])TeamSizeOptions.Clone();
            c.Hill = Hill.Clone();
            c.Movement = Movement.Clone();
            c.Jump = Jump.Clone();
            c.Respawn = Respawn.Clone();
            c.Heal = Heal.Clone();
            c.KaijuFeed = KaijuFeed.Clone();
            c.Resources = Resources.Clone();
            c.Kaiju = Kaiju.Clone();
            c.Classes = new ClassConfig[Classes.Length];
            for (int i = 0; i < Classes.Length; i++) c.Classes[i] = Classes[i].Clone();
            c.Abilities = Abilities.Clone();
            c.Bots = Bots.Clone();
            c.Render = Render.Clone();
            c.Debug = Debug.Clone();
            c.Physics = Physics.Clone();
            return c;
        }

        /// <summary>The stat row of one class (browser GAME_CONFIG.classes[id]).</summary>
        public ClassConfig Class(ClassId id)
        {
            return Classes[(int)id];
        }

        /// <summary>The bot roster fraction of one class (browser GAME_CONFIG.bots.classMix[id]).</summary>
        public double ClassMix(ClassId id)
        {
            return Bots.ClassMix[(int)id];
        }
    }

    /// <summary>King-of-the-hill zone.</summary>
    public sealed class HillConfig
    {
        public double Radius = 6;

        public HillConfig Clone() => (HillConfig)MemberwiseClone();
    }

    /// <summary>CM-21 sim acceleration: rest to full speed in AccelSeconds, full speed to rest in BrakeSeconds.</summary>
    public sealed class MovementConfig
    {
        public double AccelSeconds = 0.12;
        public double BrakeSeconds = 0.08;

        public MovementConfig Clone() => (MovementConfig)MemberwiseClone();
    }

    /// <summary>CM-32 jumping; all timing in sim ticks at SimHz 30.</summary>
    public sealed class JumpConfig
    {
        /// <summary>Airborne ticks of a full jump.</summary>
        public double AirTicks = 18;
        /// <summary>Apex lift of a full jump in world units.</summary>
        public double ApexUnits = 0.8;
        /// <summary>Shortest jump the landing guard may shorten to (ticks).</summary>
        public double MinAirTicks = 6;
        /// <summary>
        /// DG-02 coyote time in sim seconds (not in the browser; Travis's "Jumping", gap list row DG-02): a grounded actor
        /// whose footing was removed may still take off for this long. Counted in ticks (Jump.CoyoteTicks, 3 at 30 Hz).
        /// </summary>
        public double CoyoteSeconds = 0.1;
        /// <summary>
        /// DG-02 jump buffer in sim seconds (not in the browser): a press this early (before landing, before the get-up
        /// ends, or refused by the landing guard) fires on the first tick it can. Counted in ticks (Jump.BufferTicks, 3).
        /// </summary>
        public double BufferSeconds = 0.1;

        public JumpConfig Clone() => (JumpConfig)MemberwiseClone();
    }

    /// <summary>Respawn timing, spawn protection, stand-up and class-switch cooldown.</summary>
    public sealed class RespawnConfig
    {
        public double DelaySeconds = 5;
        /// <summary>Spawn protection ends after this many seconds or when the actor leaves the base pad.</summary>
        public double ProtectionSeconds = 3;
        /// <summary>CM-20: sim seconds an actor takes to get up off the pad after a respawn.</summary>
        public double StandUpSeconds = 0.8;
        /// <summary>CM-54: sim ticks after an accepted class switch before another is accepted.</summary>
        public double ClassSwitchCooldownTicks = 150;

        public RespawnConfig Clone() => (RespawnConfig)MemberwiseClone();
    }

    /// <summary>CM-37 healing fountain at each base.</summary>
    public sealed class HealConfig
    {
        /// <summary>HP restored per sim tick inside the own base fountain zone, up to maxHp.</summary>
        public double HpPerTick = 1;
        /// <summary>Zone radius around the pad centre.</summary>
        public double ZoneRadius = 3.75;
        /// <summary>Bot retreat threshold as a share of maxHp (read by BotFsm.LowHp).</summary>
        public double SeekHpShare = 0.3;

        public HealConfig Clone() => (HealConfig)MemberwiseClone();
    }

    /// <summary>Kaiju allegiance and instability from deliveries at the shrine.</summary>
    public sealed class KaijuFeedConfig
    {
        /// <summary>Allegiance per plant biomass unit.</summary>
        public double BiomassAllegiancePerUnit = 10;
        /// <summary>CM-35: allegiance per meat biomass unit.</summary>
        public double MeatAllegiancePerUnit = 6;
        public double CorruptionAllegiancePerUnit = 4;
        /// <summary>CM-34: instability per corruption unit.</summary>
        public double CorruptionInstabilityPerUnit = 9;
        /// <summary>CM-85: instability per biomass unit delivered (plant and meat alike).</summary>
        public double BiomassInstabilityPerUnit = 0.125;
        /// <summary>Instability at or above this before hatch locks berserk.</summary>
        public double BerserkThreshold = 100;
        /// <summary>
        /// DG-41 (U-062, not in the browser): when true the line is random per match instead of BerserkThreshold:
        /// u^2 x BerserkCurveMax with u uniform in [0, 1) from the match seed (SimResources.BerserkLine), so the chance that
        /// instability I has locked berserk is sqrt(I / BerserkCurveMax), Travis's y = sqrt(x). Off by default: the owner turns
        /// it on after reading the berserk-share soak (it changes match outcomes, so the golden Normal hashes move).
        /// </summary>
        public bool RandomBerserk = false;
        /// <summary>
        /// DG-41: the instability at which the random line has surely been crossed (the top of the sqrt curve). 800 keeps the
        /// berserk share near today's (measured on 100 5v5 seeds: fixed 0.19; random with top 160: 0.68, 250: 0.51; see U-062).
        /// </summary>
        public double BerserkCurveMax = 800;
        public double ShrineInteractRange = 3;
        /// <summary>
        /// CM-64: sim ticks a carrier stands in the delivery zone holding interact before its load goes in; an enemy hit during
        /// the wind-up interrupts it and it restarts from zero. U-056 (departs from the browser's 30): 12, SHORTER than the fastest
        /// swing (18), so one defender alone can delay a carrier but never pin it; two still can. Tuned on the DG-16b soaks.
        /// </summary>
        public double DeliverWindupTicks = 12;
        /// <summary>CM-36: the shrine cleanse.</summary>
        public CleanseConfig Cleanse = new CleanseConfig();

        public KaijuFeedConfig Clone()
        {
            var c = (KaijuFeedConfig)MemberwiseClone();
            c.Cleanse = Cleanse.Clone();
            return c;
        }
    }

    /// <summary>CM-36 shrine cleanse (browser kaijuFeed.cleanse). All timing in sim ticks at SimHz 30.</summary>
    public sealed class CleanseConfig
    {
        /// <summary>Corruption units one cleanse drains (x CorruptionInstabilityPerUnit = 18 instability at 9).</summary>
        public double Units = 2;
        /// <summary>Ticks the drain is spread over (3 s).</summary>
        public double Ticks = 90;
        /// <summary>Ticks the cleansing team waits from the start (90 s).</summary>
        public double CooldownTicks = 2700;
        /// <summary>Ticks of held interact that start it (0.5 s).</summary>
        public double HoldTicks = 15;
        /// <summary>The human slot on autopilot cleanses once this many net corruption units sit in the egg; pure bots never cleanse.</summary>
        public double BotNetUnits = 8; // U-057 (browser 10): with CM-85 biomass instability, net corruption never reached 10 before the lock

        public CleanseConfig Clone() => (CleanseConfig)MemberwiseClone();
    }

    /// <summary>Resource nodes and CM-35 meat orbs.</summary>
    public sealed class ResourcesConfig
    {
        public double NodeInteractRange = 1.8;
        public double BiomassNodeCharges = 6;
        public double BiomassRegrowSeconds = 20;
        public double CorruptionNodeCharges = 3;
        public double CorruptionRegrowSeconds = 30;
        /// <summary>CM-35: biomass units in the meat orb a killed actor drops.</summary>
        public double MeatUnitsPerKill = 2;
        /// <summary>CM-35: sim ticks a meat orb lies on the ground before it rots away.</summary>
        public double MeatOrbLifetimeTicks = 600;
        /// <summary>CM-35: walk-over pickup reach from the orb centre, added to the actor's radius.</summary>
        public double MeatPickupRange = 1.2;
        /// <summary>CM-35: meat units one actor can carry, outside the class cargo.</summary>
        public double MeatCarryCapacity = 2;

        public ResourcesConfig Clone() => (ResourcesConfig)MemberwiseClone();
    }

    /// <summary>The Kaiju's body, melee, slam and knock-out.</summary>
    public sealed class KaijuConfig
    {
        /// <summary>Kaiju max HP = HpPerTeamMember x TeamSize, computed at world creation and reset.</summary>
        public double HpPerTeamMember = 400;
        public double MoveSpeed = 2.6;
        /// <summary>Hit radius against actors (also the cleave and slam radius).</summary>
        public double Radius = 1.64;
        /// <summary>Grid collision radius (walls and pathing).</summary>
        public double GridRadius = 0.7;
        public double MeleeDamage = 30;
        public double MeleeRange = 2.6;
        public double MeleeCooldown = 1.6;
        /// <summary>CM-26: sim ticks a melee swing winds up before it lands.</summary>
        public double MeleeWindupTicks = 11;
        public double SlamDamage = 45;
        public double SlamRadius = 5;
        public double SlamTelegraphSeconds = 1.2;
        public double SlamCooldown = 8;
        /// <summary>A neutral Kaiju only retaliates against actors that damaged it in the last N seconds.</summary>
        public double NeutralThreatMemorySeconds = 8;
        /// <summary>At 0 HP the Kaiju is knocked out for this long, then recovers to full HP.</summary>
        public double KoSeconds = 60;

        public KaijuConfig Clone() => (KaijuConfig)MemberwiseClone();
    }

    /// <summary>One class's stat row (browser ClassConfig).</summary>
    public sealed class ClassConfig
    {
        public double MaxHp;
        /// <summary>Units per second.</summary>
        public double MoveSpeed;
        /// <summary>Collision circle.</summary>
        public double Radius;
        public double AttackDamage;
        /// <summary>Melee reach from the actor centre.</summary>
        public double AttackRange;
        /// <summary>Cleave arc, centred on facing.</summary>
        public double AttackArcDeg;
        public double AttackCooldown;
        public double AbilityCooldown;
        /// <summary>Time to pull one unit from a node.</summary>
        public double GatherSeconds;
        /// <summary>Max units carried.</summary>
        public double CarryCapacity;
        // Sim-physics lane (appended; not in SameStats, which compares the ten browser fields).
        /// <summary>DG-69 / OD-2: real seconds from the attack press to the hit (0 = the browser's press-tick hit).</summary>
        public double AttackWindupSeconds;
        /// <summary>DG-69: real seconds of slowed follow-through after the hit (the view's recovery; movement stays slowed).</summary>
        public double RecoverySeconds;

        public ClassConfig Clone() => (ClassConfig)MemberwiseClone();

        /// <summary>True when all ten stat fields are equal.</summary>
        public bool SameStats(ClassConfig other)
        {
            return other != null
                && MaxHp == other.MaxHp
                && MoveSpeed == other.MoveSpeed
                && Radius == other.Radius
                && AttackDamage == other.AttackDamage
                && AttackRange == other.AttackRange
                && AttackArcDeg == other.AttackArcDeg
                && AttackCooldown == other.AttackCooldown
                && AbilityCooldown == other.AbilityCooldown
                && GatherSeconds == other.GatherSeconds
                && CarryCapacity == other.CarryCapacity;
        }
    }

    /// <summary>Per-class ability tunables.</summary>
    public sealed class AbilitiesConfig
    {
        public WarriorShieldConfig WarriorShield = new WarriorShieldConfig();
        public FarmerRootsConfig FarmerRoots = new FarmerRootsConfig();
        public MinerWallBreakConfig MinerWallBreak = new MinerWallBreakConfig();
        public MinerBarricadeConfig MinerBarricade = new MinerBarricadeConfig();
        public MinerTerraformConfig MinerTerraform = new MinerTerraformConfig();
        public ToppleConfig Topple = new ToppleConfig();
        public DemolitionsMineConfig DemolitionsMine = new DemolitionsMineConfig();
        public DemolitionsTntConfig DemolitionsTnt = new DemolitionsTntConfig();
        /// <summary>DG-24: the Builder's ladders and rope bridges (BuiltPieces.cs).</summary>
        public BuilderPiecesConfig BuilderPieces = new BuilderPiecesConfig();
        /// <summary>DG-40: the Healer's mend, healing pulse and purify (Healer.cs).</summary>
        public HealerConfig Healer = new HealerConfig();

        public AbilitiesConfig Clone()
        {
            var c = (AbilitiesConfig)MemberwiseClone();
            c.WarriorShield = WarriorShield.Clone();
            c.FarmerRoots = FarmerRoots.Clone();
            c.MinerWallBreak = MinerWallBreak.Clone();
            c.MinerBarricade = MinerBarricade.Clone();
            c.MinerTerraform = MinerTerraform.Clone();
            c.Topple = Topple.Clone();
            c.DemolitionsMine = DemolitionsMine.Clone();
            c.DemolitionsTnt = DemolitionsTnt.Clone();
            c.BuilderPieces = BuilderPieces.Clone();
            c.Healer = Healer.Clone();
            return c;
        }
    }

    /// <summary>Warrior raised shield: frontal damage reduced, move speed reduced while up.</summary>
    public sealed class WarriorShieldConfig
    {
        public double DurationSeconds = 2.5;
        public double DamageReduction = 0.6;
        public double MoveSpeedMultiplier = 0.7;

        public WarriorShieldConfig Clone() => (WarriorShieldConfig)MemberwiseClone();
    }

    /// <summary>Farmer root seed thrown at the cursor; enemies inside the radius are slowed.</summary>
    public sealed class FarmerRootsConfig
    {
        public double CastRange = 6;
        public double Radius = 2.5;
        public double SlowMultiplier = 0.5;
        public double SlowSeconds = 3;

        public FarmerRootsConfig Clone() => (FarmerRootsConfig)MemberwiseClone();
    }

    /// <summary>Miner pick hits drop a breakable wall one state.</summary>
    public sealed class MinerWallBreakConfig
    {
        public double HitsPerState = 1;
        public double Reach = 1.6;

        public MinerWallBreakConfig Clone() => (MinerWallBreakConfig)MemberwiseClone();
    }

    /// <summary>Miner temporary barricade; placement may never disconnect a base from the hill or shrine.</summary>
    public sealed class MinerBarricadeConfig
    {
        public double MaxActivePerMiner = 2;
        public double LifetimeSeconds = 20;
        public double MaxHp = 120;
        public double MinDistanceFromActor = 0.9;
        public double PlaceRange = 2;

        public MinerBarricadeConfig Clone() => (MinerBarricadeConfig)MemberwiseClone();
    }

    /// <summary>CM-30 Miner dig and place.</summary>
    public sealed class MinerTerraformConfig
    {
        public double Reach = 1.6;
        public double TeamCooldownTicks = 30;
        public double MinGapTicks = 15;
        public double ActorMargin = 0.05;
        /// <summary>
        /// DG-09 (U-053, not in the browser): blocks a miner can carry. The browser carries exactly one (CarryMax 1 replays it
        /// exactly: dig, place, dig...). [guess: 3, so the dig / place mode switch (wheel, LB / RB) has something to choose:
        /// dig a short trench, then fill a gap three cells long.]
        /// </summary>
        public double CarryMax = 3;
        /// <summary>
        /// DG-09 fall rule (U-053, not in the browser): square cell rings searched around a fallen actor for ground to climb
        /// out onto [guess: 6, wider than any hole the rate cap lets one team dig in a few seconds]; none = back to its pad.
        /// </summary>
        public double FallSearchRings = 6;

        public MinerTerraformConfig Clone() => (MinerTerraformConfig)MemberwiseClone();
    }

    /// <summary>CM-31 knock-down decorations.</summary>
    public sealed class ToppleConfig
    {
        public double HitsToTopple = 3;
        public double LengthCells = 4;
        public double WidthCells = 3;
        public double FallTicks = 18;
        public double Reach = 0.5;

        public ToppleConfig Clone() => (ToppleConfig)MemberwiseClone();
    }

    /// <summary>CM-38 Demolitions Expert land mine.</summary>
    public sealed class DemolitionsMineConfig
    {
        public double PlaceRange = 1.6;
        public double MaxActivePerOwner = 2;
        public double ArmTicks = 15;
        public double Damage = 70;
        public double BlastRadius = 1.2;
        public double LifetimeTicks = 1800;

        public DemolitionsMineConfig Clone() => (DemolitionsMineConfig)MemberwiseClone();
    }

    /// <summary>CM-38 Demolitions Expert TNT.</summary>
    public sealed class DemolitionsTntConfig
    {
        public double ThrowRange = 5;
        public double FuseTicks = 45;
        public double BlastRadius = 2;
        public double Damage = 55;
        public double CarveRadius = 1;
        public double CooldownTicks = 150;
        /// <summary>DG-22 (not in the browser, U-059): ticks from the throw until the bundle touches down (the TntLanded event; the fuse still counts from the throw) [guess: 0.4 s].</summary>
        public double FlightTicks = 12;

        public DemolitionsTntConfig Clone() => (DemolitionsTntConfig)MemberwiseClone();
    }

    /// <summary>Bot brain and director tunables.</summary>
    public sealed class BotsConfig
    {
        public double ThinkHz = 4;
        public double DirectorPeriodSeconds = 2;
        /// <summary>Utility bonus for staying in the current state.</summary>
        public double InertiaBonus = 0.25;
        /// <summary>Minimum time in a state before a non-urgent switch.</summary>
        public double MinStateSeconds = 2;
        /// <summary>Director keeps at least this fraction of living bots on hill duty.</summary>
        public double HillMinFraction = 0.3;
        /// <summary>Director sends this fraction (rounded, min 1) to biomass / Kaiju duty as the base quota.</summary>
        public double KaijuGroupFraction = 0.25;
        /// <summary>A team behind in allegiance by at least this many points may send corruption runs.</summary>
        public double CorruptWhenBehindBy = 10;
        /// <summary>Fraction of bots per team per class when filling the roster, indexed by (int)ClassId.</summary>
        public double[] ClassMix = { 0.4, 0.3, 0.3, 0, 0, 0 };
        public double StuckSeconds = 1.5;
        /// <summary>CM-21: a moving bot's move direction turns toward its flow direction at most this fast (deg / s).</summary>
        public double TurnRateDegPerSec = 300;
        /// <summary>CM-64: an enemy carrier inside this radius of the shrine centre is contestable by Kaiju-duty bots. U-056 (departs
        /// from the browser's 11 = 3 + FightKeepRange 8): 14, one more shrine range, so the shorter wind-up stays contestable.</summary>
        public double InterceptShrineRadius = 14;
        /// <summary>
        /// DG-16 (the guided first match): for the first PassiveSeconds of match time bots neither target nor damage the
        /// human slot (actor 0). 0 = off (the default; every bot decision is the browser's).
        /// </summary>
        public double PassiveSeconds = 0;
        /// <summary>DG-21: the Easy profile (WorldOptions.BotDifficulty). Normal has no profile: it is the browser code path.</summary>
        public BotSkillConfig Easy = BotSkillConfig.EasyDefaults();
        /// <summary>DG-21: the Hard profile.</summary>
        public BotSkillConfig Hard = BotSkillConfig.HardDefaults();

        public BotsConfig Clone()
        {
            var c = (BotsConfig)MemberwiseClone();
            c.ClassMix = (double[])ClassMix.Clone();
            c.Easy = Easy.Clone();
            c.Hard = Hard.Clone();
            return c;
        }
    }

    /// <summary>
    /// DG-21 bot skill profile (not in the browser; U-058). Every field changes one thing against the Normal bots; Normal itself
    /// has no profile, so its decisions are the browser's bit for bit. All rolls are a stateless hash of seed, actor and tick
    /// (BotSkill.Roll): no shared bot RNG draw moves, so the profile of one team never changes the other team's choices.
    /// </summary>
    public sealed class BotSkillConfig
    {
        /// <summary>Reaction delay: the think period is round(Normal period x this) ticks (Normal 8 at 30 Hz: Easy 12, Hard 4).</summary>
        public double ThinkPeriodScale = 1;
        /// <summary>Aim error: thrown or cast ability points (roots, mines, TNT) land up to this many units off, uniform over a disc.</summary>
        public double AimErrorUnits = 0;
        /// <summary>Aim: TNT aimed at a moving enemy leads it by velocity x fuse time x this (0 = aim where it stands, as Normal).</summary>
        public double TntLeadFraction = 0;
        /// <summary>Hesitation: share of 1/3-second windows in which the bot holds its melee swing.</summary>
        public double SwingHesitation = 0;
        /// <summary>Ability use: chance per think that a decided ability press (or TNT throw) is sent; else it waits for the next think.</summary>
        public double AbilityChance = 1;
        /// <summary>Ability use: a Warrior raises its shield with this many enemies within 3 (Normal 2).</summary>
        public int ShieldCrowd = 2;
        /// <summary>Target choice: weight enemies by carried cargo or meat (x CarrierWeight on distance squared) and by missing HP.</summary>
        public bool FocusCarriers = false;
        /// <summary>Target choice: distance-squared multiplier for a carrier when FocusCarriers is on.</summary>
        public double CarrierWeight = 0.25;
        /// <summary>Objective discipline: a hill holder's fight utility vs an enemy off the hill is multiplied by this (Normal 0.5).</summary>
        public double OffHillChaseFactor = 0.5;
        /// <summary>Objective discipline: a hurt bot retreats when an enemy is within this range (Normal 6).</summary>
        public double RetreatRange = 6;

        public static BotSkillConfig EasyDefaults() => new BotSkillConfig
        {
            ThinkPeriodScale = 1.5, AimErrorUnits = 1.5, TntLeadFraction = 0, SwingHesitation = 0.4, AbilityChance = 0.5,
            ShieldCrowd = 3, FocusCarriers = false, OffHillChaseFactor = 1, RetreatRange = 6,
        };

        public static BotSkillConfig HardDefaults() => new BotSkillConfig
        {
            ThinkPeriodScale = 0.5, AimErrorUnits = 0, TntLeadFraction = 0.6, SwingHesitation = 0, AbilityChance = 1,
            ShieldCrowd = 1, FocusCarriers = true, CarrierWeight = 0.25, OffHillChaseFactor = 0.25, RetreatRange = 3,
        };

        public BotSkillConfig Clone() => (BotSkillConfig)MemberwiseClone();
    }

    /// <summary>Camera and render tunables.</summary>
    public sealed class RenderConfig
    {
        public double CameraPitchDeg = 38;
        public double CameraYawDeg = 45;
        public double CameraDistance = 60;
        /// <summary>Ortho framing: every screen shows this many world units vertically.</summary>
        public double ViewHeightUnits = 20;
        public double WheelFactorMin = 0.8;
        public double WheelFactorMax = 1.25;
        public double WheelSensitivity = 0.0015;
        /// <summary>Camera target is clamped to map bounds inset by this many units.</summary>
        public double CameraBoundsInset = 6;
        /// <summary>World height of a character model.</summary>
        public double CharacterHeight = 2.0;
        /// <summary>World height of the Kaiju.</summary>
        public double KaijuHeight = 5.0;
        public double CameraFollowSharpness = 8;
        public double DprMin = 1;
        public double DprMax = 2;
        /// <summary>Sun shadow map size (px, square).</summary>
        public double ShadowMapSize = 2048;
        /// <summary>Below this fps the postprocessing chain degrades one step.</summary>
        public double AutoDropFps = 50;
        /// <summary>CM-20: Lie_StandUp is posed from this clip time (seconds) to its end.</summary>
        public double StandUpClipStart = 1.45;

        public RenderConfig Clone() => (RenderConfig)MemberwiseClone();
    }

    /// <summary>Debug overlay options.</summary>
    public sealed class DebugConfig
    {
        public double[] ClockSpeedOptions = { 1, 2, 4, 8 };

        public DebugConfig Clone()
        {
            var c = (DebugConfig)MemberwiseClone();
            c.ClockSpeedOptions = (double[])ClockSpeedOptions.Clone();
            return c;
        }
    }

    /// <summary>
    /// DG-24 (U-060, not in the browser): the Builder's temporary ladders and rope bridges, paid from the team's biomass
    /// bank (StartBiomass + biomass delivered - biomass spent). Timing in sim ticks.
    /// </summary>
    public sealed class BuilderPiecesConfig
    {
        /// <summary>Biomass a ladder costs (it spans a wall / cliff cell run up to LadderMaxCells long).</summary>
        public double LadderCost = 1;
        /// <summary>Biomass a rope bridge costs (it spans a void / water run up to BridgeMaxCells long).</summary>
        public double BridgeCost = 2;
        /// <summary>Spendable biomass each team starts a match with (so a Builder can act before the first delivery).</summary>
        public double StartBiomass = 2;
        public double LadderMaxCells = 2;
        public double BridgeMaxCells = 6;
        /// <summary>Lanes across the span, centred on the line the builder looks along (Topple.WidthCells: wide enough for bots).</summary>
        public double WidthCells = 3;
        /// <summary>The gap or wall must start within this many cells ahead of the builder's cell.</summary>
        public double ReachCells = 2;
        /// <summary>A piece lasts this long (60 s; Travis: "temporary ladders/bridges"), carried time included.</summary>
        public double LifetimeTicks = 1800;
        /// <summary>The PieceCrumbling warning goes out this long before the end (3 s).</summary>
        public double WarnTicks = 90;
        /// <summary>Pieces one Builder may own at once (on the ground or carried).</summary>
        public double MaxActivePerBuilder = 2;
        /// <summary>An actor picks a piece up (Interact) within this distance of one of its cells (cell edge, plus its radius).</summary>
        public double PickupRange = 1.0;
        /// <summary>A refused build locks Space this long (so a held key never floods the event ring).</summary>
        public double RefuseLockSeconds = 0.5;

        public BuilderPiecesConfig Clone() => (BuilderPiecesConfig)MemberwiseClone();
    }

    /// <summary>DG-40 (U-061, not in the browser): the Healer's abilities. Timing in sim ticks.</summary>
    public sealed class HealerConfig
    {
        /// <summary>MEND (the attack press, class AttackCooldown): HP to the hurt ally nearest the aim within MendRange of her (not herself).</summary>
        public double MendHeal = 6;
        public double MendRange = 5.5;
        /// <summary>HEALING PULSE (Space, class AbilityCooldown): at the aim pulled back to PulseCastRange, PulseHeal to every ally within PulseRadius, her included.</summary>
        public double PulseCastRange = 6;
        public double PulseRadius = 3.5;
        public double PulseHeal = 30;
        /// <summary>PURIFY (DigPlace): within PurifyRange of the shrine while a cleanse could run, removes PurifyUnits corruption units at once.</summary>
        public double PurifyUnits = 1;
        public double PurifyRange = 4;
        public double PurifyCooldownTicks = 900;

        public HealerConfig Clone() => (HealerConfig)MemberwiseClone();
    }
}

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
            },
            new ClassConfig
            {
                MaxHp = 100, MoveSpeed = 4.2, Radius = 0.4, AttackDamage = 12, AttackRange = 1.4, AttackArcDeg = 90,
                AttackCooldown = 0.6, AbilityCooldown = 9, GatherSeconds = 1, CarryCapacity = 3,
            },
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 16, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.65, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
            },
            // CM-38: the Demolitions Expert has no regular attack; the attack fields keep the CM-56 miner copy.
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 16, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.65, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
            },
            // CM-56 placeholder: the miner row until CM-39 tunes it.
            new ClassConfig
            {
                MaxHp = 120, MoveSpeed = 4.0, Radius = 0.45, AttackDamage = 16, AttackRange = 1.5, AttackArcDeg = 90,
                AttackCooldown = 0.65, AbilityCooldown = 12, GatherSeconds = 2, CarryCapacity = 1,
            },
        };

        public AbilitiesConfig Abilities = new AbilitiesConfig();
        public BotsConfig Bots = new BotsConfig();
        public RenderConfig Render = new RenderConfig();
        public DebugConfig Debug = new DebugConfig();

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
        public double ShrineInteractRange = 3;

        public KaijuFeedConfig Clone() => (KaijuFeedConfig)MemberwiseClone();
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
        public double[] ClassMix = { 0.4, 0.3, 0.3, 0, 0 };
        public double StuckSeconds = 1.5;
        /// <summary>CM-21: a moving bot's move direction turns toward its flow direction at most this fast (deg / s).</summary>
        public double TurnRateDegPerSec = 300;

        public BotsConfig Clone()
        {
            var c = (BotsConfig)MemberwiseClone();
            c.ClassMix = (double[])ClassMix.Clone();
            return c;
        }
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
}

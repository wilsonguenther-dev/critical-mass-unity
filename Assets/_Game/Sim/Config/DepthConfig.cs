namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// The mechanics-depth lane (U-063 onward): class roles, biomass sources, base stations and upgrades, pickups, pings and
    /// map events. One block per rule set; every number of a rule lives in its block.
    /// <para>Classic = true is the game as it was before this lane (the browser rules plus DG-02..DG-41): every rule below is
    /// off, so the world hash of a Normal bot match is unchanged (the DG-21 golden test). GameConfig.CreateBrowser5v5() sets
    /// it; GameConfig.CreateDefault() (what the game runs) leaves the rules on. A rule is on when Classic is false and its
    /// own Enabled switch is true (DepthRules.On).</para>
    /// </summary>
    public sealed class DepthConfig
    {
        /// <summary>Master switch: true = none of the mechanics-depth rules run (the "classic" setting).</summary>
        public bool Classic = false;
        /// <summary>U-063: who may feed, corrupt and hand off; the six-class bot lineup.</summary>
        public RolesConfig Roles = new RolesConfig();
        /// <summary>U-064: wood and water on the map, the team stock at the base, supply bots.</summary>
        public SourcesConfig Sources = new SourcesConfig();
        /// <summary>U-065: the base station: live class switching and class upgrades.</summary>
        public StationsConfig Stations = new StationsConfig();
        /// <summary>U-066: coins, power-ups and their deterministic spawner.</summary>
        public PickupsConfig Pickups = new PickupsConfig();
        /// <summary>U-067: pings the bots answer, the bots' own calls, bot names and play styles.</summary>
        public PingsConfig Pings = new PingsConfig();
        /// <summary>U-068: map events: supply drop, egg exposed, bridge collapse.</summary>
        public MapEventsConfig Events = new MapEventsConfig();

        public DepthConfig Clone()
        {
            var c = (DepthConfig)MemberwiseClone();
            c.Roles = Roles.Clone();
            c.Sources = Sources.Clone();
            c.Stations = Stations.Clone();
            c.Pickups = Pickups.Clone();
            c.Pings = Pings.Clone();
            c.Events = Events.Clone();
            return c;
        }
    }

    /// <summary>U-068 map events (our rule). Timing in sim ticks at 30 Hz.</summary>
    public sealed class MapEventsConfig
    {
        public bool Enabled = true;
        /// <summary>First event at 50 s, then one every 60 s; the three kinds rotate from a seed-fixed start.</summary>
        public double FirstEventTicks = 1500;
        public double PeriodTicks = 1800;
        /// <summary>Supply drop: centre-line spots (their z), warning 8 s, claim by standing 3 s uncontested within range, gone after 30 s.</summary>
        public double[] DropSpotsZ = { 34, 46, 22, 58 };
        public double DropWarnTicks = 240;
        public double DropClaimRange = 2.5;
        public double DropClaimTicks = 90;
        public double DropLifetimeTicks = 900;
        public double DropWood = 4;
        public double DropWater = 2;
        public double DropCoins = 10;
        public double DropPowerUpDelayTicks = 30;
        /// <summary>Bots: a bot within this range and in line of sight walks to the drop, at this utility (a healthy hill bot's fight still wins).</summary>
        public double BotDropRange = 12;
        public double BotDropUtility = 0.85;
        /// <summary>Egg exposed: 20 s in which every delivery counts this many times toward allegiance.</summary>
        public double EggWindowTicks = 600;
        public double EggFeedScale = 2;
        /// <summary>Bridge collapse: warning 8 s; the middle third within this half width falls; a span with fewer bridge cells there is skipped.</summary>
        public double BridgeWarnTicks = 240;
        public double BridgeHalfWidth = 3;
        public double BridgeMinCells = 2;
        /// <summary>A Builder within this range of a broken end holds interact 3 s and pays this much wood; else it rebuilds itself after 45 s.</summary>
        public double BridgeRepairRange = 3;
        public double BridgeRepairTicks = 90;
        public double BridgeRepairWood = 2;
        public double BridgeAutoRepairTicks = 1350;

        public MapEventsConfig Clone()
        {
            var c = (MapEventsConfig)MemberwiseClone();
            c.DropSpotsZ = (double[])DropSpotsZ.Clone();
            return c;
        }
    }

    /// <summary>U-067 pings, bot calls and play styles (our rule, to the HUD lane's spec). Timing in sim ticks at 30 Hz. Style arrays: Brawler, Guard, Harvester, Sapper.</summary>
    public sealed class PingsConfig
    {
        public bool Enabled = true;
        /// <summary>At most RateCount pings per actor in any RateWindowTicks (3 per 5 s, the HUD's limit).</summary>
        public double RateCount = 3;
        public double RateWindowTicks = 150;
        /// <summary>Ticks the marker lives (6 s; the view fades it). An answer already taken runs its own timer.</summary>
        public double PingLifeTicks = 180;
        public double AnswerRange = 40;
        public double MaxAttack = 3;
        public double MaxDefend = 3;
        public double MaxHelp = 2;
        /// <summary>A bot holding the hill answers only with at least this many teammates on the hill.</summary>
        public double HillMinMates = 2;
        public double AttackSeekRange = 8;
        public double AttackTicks = 360;
        public double ArriveRange = 1.5;
        public double DefendHoldRange = 5;
        public double DefendReach = 3;
        public double DefendTicks = 360;
        public double HelpTicks = 240;
        public double HelpKeepDistance = 2.5;
        /// <summary>Utility of following an order (over the hill's 0.7 + inertia, under a retreat's 0.9 only for the hurt, who drop the order first).</summary>
        public double OrderUtility = 0.98;
        /// <summary>Route slots per team (one per answered ping, round robin; a bot whose slot was reused walks straight).</summary>
        public double RouteSlotsPerTeam = 4;
        /// <summary>Bots call too.</summary>
        public bool BotCalls = true;
        public double BotHelpHpFraction = 0.35;
        public double BotPingCooldownTicks = 600;
        public double BotTeamPingGapTicks = 240;
        public double BotPingResponders = 1;
        /// <summary>Play styles: share of Warriors that are Guards, how readily each style fights, and its distance multiplier when answering Attack, and Defend or Help.</summary>
        public double WarriorGuardShare = 0.4;
        public double[] StyleFightScale = { 1.15, 0.95, 0.8, 1 };
        public double[] StyleAttackBias = { 0.6, 1, 1.5, 1 };
        public double[] StyleDefendBias = { 1, 0.6, 1.5, 1 };

        public PingsConfig Clone()
        {
            var c = (PingsConfig)MemberwiseClone();
            c.StyleFightScale = (double[])StyleFightScale.Clone();
            c.StyleAttackBias = (double[])StyleAttackBias.Clone();
            c.StyleDefendBias = (double[])StyleDefendBias.Clone();
            return c;
        }
    }

    /// <summary>U-066 pickups and drops (DG-39; our rule). Timing in sim ticks at 30 Hz.</summary>
    public sealed class PickupsConfig
    {
        public bool Enabled = true;
        /// <summary>Walk-over reach from the pickup centre, added to the actor's radius.</summary>
        public double PickupRange = 1.2;
        /// <summary>Slots of the fixed pickup pool.</summary>
        public double Capacity = 96;
        /// <summary>Coins a knocked-out actor leaves where it fell, and how long they lie there (20 s).</summary>
        public double CoinsPerKnockOut = 3;
        public double CoinDropLifetimeTicks = 600;
        /// <summary>Coin piles on the map: west-half (x, z) anchors, each mirrored; value, first spawn and respawn after a pick-up (30 s).</summary>
        public double[] CoinAnchors = { 31, 17, 31, 56, 46, 29, 46, 51, 52, 11 };
        public double CoinPileValue = 5;
        public double CoinFirstTicks = 150;
        public double CoinRespawnTicks = 900;
        /// <summary>Power-up spots on the centre line (x = half the map width): their z.</summary>
        public double[] PowerSpotsZ = { 30, 50, 14, 66 };
        /// <summary>First power-up at 20 s, then one every 45 s; announced 5 s before it can be taken; gone 30 s after that.</summary>
        public double FirstPowerUpTicks = 600;
        public double PowerUpPeriodTicks = 1350;
        public double PowerUpArmTicks = 150;
        public double PowerUpLifetimeTicks = 900;
        public double HasteSpeed = 1.3;
        public double HasteTicks = 300;
        public double MightDamage = 1.5;
        public double MightTicks = 300;
        public double WardHp = 60;
        public double WardTicks = 450;
        /// <summary>A knocked-out carrier drops its power-up when at least this much time was left; the drop lies 15 s.</summary>
        public double DropMinTicks = 60;
        public double DropLifetimeTicks = 450;
        /// <summary>Bots: a bot in transit or in a fight goes for a power-up within this range and in line of sight, at this utility.</summary>
        public double BotGrabRange = 9;
        public double BotGrabUtility = 0.97;

        public PickupsConfig Clone()
        {
            var c = (PickupsConfig)MemberwiseClone();
            c.CoinAnchors = (double[])CoinAnchors.Clone();
            c.PowerSpotsZ = (double[])PowerSpotsZ.Clone();
            return c;
        }
    }

    /// <summary>
    /// U-065 base stations. The switch cooldown is Respawn.ClassSwitchCooldownTicks (CM-54, 150). The ladder rows are
    /// cm/CM-39's (cost 3 then 5, +15% then +30% max HP); the water column and paying in wood are ours.
    /// </summary>
    public sealed class StationsConfig
    {
        public bool Enabled = true;
        /// <summary>The station zone: within this distance of the own pad centre.</summary>
        public double Range = 4.5;
        /// <summary>Wood to go from level L to L + 1 (index L - 1).</summary>
        public double[] UpgradeWood = { 3, 5 };
        /// <summary>Water to go from level L to L + 1.</summary>
        public double[] UpgradeWater = { 1, 2 };
        /// <summary>Max HP bonus at level L + 1 (index L - 1), over the class row.</summary>
        public double[] MaxHpBonus = { 0.15, 0.3 };
        /// <summary>Seconds the ability stays locked after a live class switch.</summary>
        public double SwitchAbilityLockSeconds = 3;
        /// <summary>Bots: a Builder bot in its station tries to buy once per this many ticks.</summary>
        public double BotBuyPeriodTicks = 30;

        public StationsConfig Clone()
        {
            var c = (StationsConfig)MemberwiseClone();
            c.UpgradeWood = (double[])UpgradeWood.Clone();
            c.UpgradeWater = (double[])UpgradeWater.Clone();
            c.MaxHpBonus = (double[])MaxHpBonus.Clone();
            return c;
        }
    }

    /// <summary>
    /// U-064 biomass sources (DG-42; our rule from Travis's list "Meat / Wood / Water / Corruption"). Timing in sim ticks.
    /// Per-class arrays are indexed by (int)ClassId: Warrior, Farmer, Miner, Demolitions, Builder, Healer.
    /// </summary>
    public sealed class SourcesConfig
    {
        public bool Enabled = true;
        /// <summary>Wood groves: (x, z) anchors on the west half, each mirrored to the east; placed on the nearest open floor.</summary>
        public double[] WoodAnchors = { 8, 29, 27, 14, 27, 59 };
        /// <summary>Water springs: west-half anchors, mirrored; an anchor on the centre line (x = 56) is a single spring.</summary>
        public double[] WaterAnchors = { 43, 45, 50, 6, 56, 64 };
        public double WoodCharges = 4;
        public double WaterCharges = 3;
        /// <summary>Ticks for one charge to grow back (wood 20 s, water 15 s).</summary>
        public double WoodRegrowTicks = 600;
        public double WaterRegrowTicks = 450;
        /// <summary>Gather time = class GatherSeconds x this. Builders and Miners chop fastest.</summary>
        public double[] WoodGatherScale = { 1, 1.5, 0.5, 0.75, 0.5, 1 };
        /// <summary>Healers and Farmers draw water fastest.</summary>
        public double[] WaterGatherScale = { 1, 1, 1, 1, 1, 0.67 };
        /// <summary>Allegiance per unit fed to the egg (plant is 10, meat 6).</summary>
        public double WoodAllegiancePerUnit = 7;
        public double WaterAllegiancePerUnit = 4;
        /// <summary>Instability one unit of water removes when fed to the egg (a corruption unit adds 9).</summary>
        public double WaterCoolPerUnit = 3;
        /// <summary>The base zone: a press of interact within this distance of the own pad centre banks the load.</summary>
        public double BankRange = 4.5;
        /// <summary>Placement: rings searched around an anchor, and keep-outs from other landmarks.</summary>
        public double PlaceSearchRings = 8;
        public double NodeKeepOut = 4;
        public double ShrineKeepOut = 5.5;
        public double HillKeepOut = 3;
        public double PadKeepOut = 2;
        /// <summary>Bots: floor(living bots x this) per team run supply before the hatch (16: 2, 5: 0).</summary>
        public double SupplyFraction = 0.125;
        /// <summary>Bots: which classes run supply, lowest first; 9 = never.</summary>
        public double[] SupplyClassRank = { 9, 9, 1, 2, 0, 9 };
        /// <summary>Bots: utility of the supply run (under a hill bot's fight at full health, over the hill's 0.7).</summary>
        public double SupplyUtility = 0.8;

        public SourcesConfig Clone()
        {
            var c = (SourcesConfig)MemberwiseClone();
            c.WoodAnchors = (double[])WoodAnchors.Clone();
            c.WaterAnchors = (double[])WaterAnchors.Clone();
            c.WoodGatherScale = (double[])WoodGatherScale.Clone();
            c.WaterGatherScale = (double[])WaterGatherScale.Clone();
            c.SupplyClassRank = (double[])SupplyClassRank.Clone();
            return c;
        }
    }

    /// <summary>
    /// U-063 class roles (our rule; Travis names the classes and their jobs, not who may feed). Indexed by (int)ClassId:
    /// Warrior, Farmer, Miner, Demolitions, Builder, Healer.
    /// </summary>
    public sealed class RolesConfig
    {
        public bool Enabled = true;
        /// <summary>May this class hand biomass (plant, meat, wood, water) to the egg? Only the Farmer.</summary>
        public bool[] MayFeed = { false, true, false, false, false, false };
        /// <summary>May this class gather, carry and deliver corruption? The raiders: Warrior, Miner, Demolitions.</summary>
        public bool[] MayCorrupt = { true, false, true, true, false, false };
        /// <summary>A carrier that may not feed hands its biomass to an allied feeder within this distance (edge to edge) by holding interact.</summary>
        public double HandoffRange = 1.6;
        /// <summary>Sim ticks between two DeliverRefused events for one actor (a held key never floods the event ring).</summary>
        public double RefuseLockTicks = 45;
        /// <summary>
        /// The bot lineup when the rules are on (replaces Bots.ClassMix): every class on the field so every role exists.
        /// At 16: 5 Warriors, 4 Farmers, 2 Miners, 2 Demolitions, 1 Builder, 2 Healers. At 5: one each of Warrior, Farmer,
        /// Miner, Demolitions, Healer.
        /// </summary>
        public double[] ClassMix = { 0.3125, 0.25, 0.125, 0.125, 0.0625, 0.125 };
        /// <summary>Bots: an escort (an egg-duty bot that may not feed) follows the nearest allied carrier within this many units.</summary>
        public double EscortSeekRange = 30;
        /// <summary>Bots: an escort stops this close to the carrier it guards.</summary>
        public double EscortKeepDistance = 2.5;
        /// <summary>Bots: utility of escorting (over the 0.6 x hp fight of an egg-duty bot, under an intercept at 1).</summary>
        public double EscortUtility = 0.75;

        public RolesConfig Clone()
        {
            var c = (RolesConfig)MemberwiseClone();
            c.MayFeed = (bool[])MayFeed.Clone();
            c.MayCorrupt = (bool[])MayCorrupt.Clone();
            c.ClassMix = (double[])ClassMix.Clone();
            return c;
        }
    }
}

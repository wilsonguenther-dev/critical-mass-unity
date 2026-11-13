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

        public DepthConfig Clone()
        {
            var c = (DepthConfig)MemberwiseClone();
            c.Roles = Roles.Clone();
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

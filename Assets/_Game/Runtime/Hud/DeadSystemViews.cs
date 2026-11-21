using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// b15 item 4 (systems audit defect 5 / W4, player-facing honesty only): five systems are in the code but do nothing in
    /// the default game. Their HUD, menu and prompt views are hidden so the player is never shown a number that buys
    /// nothing. NO sim code is touched or deleted: each view sits behind one named flag here, false by default; set a flag
    /// to true to show that system's views again (the owner's call: revive or remove). Each view also comes back by itself
    /// when the rule that makes the system live is switched on (for example the hut rule off), so a custom or classic
    /// config still shows what works there.
    /// <list type="bullet">
    /// <item><b>ShowCoins</b>: coins (knock-outs, piles, drops) have no sink anywhere: no code subtracts them. Hidden: the
    /// in-match coin counter and its fly-in, the progression HUD coin chip, the results' coin column and total, the home
    /// card's coin count and landing line, the character panel's coin total. The coin piles in the world still show and
    /// are still picked up (the sim is unchanged); the profile still counts them, so a future shop starts from the real total.</item>
    /// <item><b>ShowBankedWater</b>: water banked at the base has one spender, the station class ladder, which is off while
    /// the hut rule is on. Hidden: the WATER chip of the team stock (the WOOD and FOOD chips close up), the water count in
    /// the station panel, and the BANK prompt for a water load. Carrying water to the egg still works and still shows.</item>
    /// <item><b>ShowStationLadder</b>: the station's class-upgrade ladder (+15% / +30% HP for wood and water) is off while
    /// the hut rule is on; the station panel shows the hut block instead (b13), now through this flag.</item>
    /// <item><b>ShowBridgeRepair</b>: bridge collapse is off by default (MapEventsConfig.BridgeCollapse), so no bridge ever
    /// needs a repair. Hidden: the Builder's REPAIR prompt and its progress.</item>
    /// <item><b>ShowMinerBarricades</b>: with the hut rule on the Miner's Space is HAUL and he never places a barricade
    /// (audit: "Miner barricades placed: on 0"). Hidden: "Barricade" in the Miner's attack-and-ability line on the match
    /// setup card (it reads Haul); the class card already shows Haul (b13).</item>
    /// </list>
    /// </summary>
    public static class DeadSystemViews
    {
        public static bool ShowCoins;
        public static bool ShowBankedWater;
        public static bool ShowStationLadder;
        public static bool ShowBridgeRepair;
        public static bool ShowMinerBarricades;

        static GameConfig _default;
        static GameConfig Default => _default ?? (_default = GameConfig.CreateDefault());

        static bool HutOn(World w) => w != null ? Blacksmith.On(w) : HutOn(Default);
        static bool HutOn(GameConfig c) => c != null && c.Depth.Hut.On && !c.Depth.Classic;

        /// <summary>Coins have no sink in any config today.</summary>
        public static bool CoinsShown() => ShowCoins;

        /// <summary>Banked water is live only where the station ladder is (the hut rule off).</summary>
        public static bool BankedWaterShown(World w) => ShowBankedWater || !HutOn(w);

        /// <summary>The station's class-upgrade rows (the hut block stands in their place while the hut rule is on).</summary>
        public static bool StationLadderShown(World w) => ShowStationLadder || !HutOn(w);

        /// <summary>The Builder's repair prompt: live only when bridges can collapse.</summary>
        public static bool BridgeRepairShown(World w) =>
            ShowBridgeRepair || (w != null ? w.Config.Depth.Events.BridgeCollapse : Default.Depth.Events.BridgeCollapse);

        /// <summary>Miner barricades: live only with the hut rule off (no world: the default game's rules).</summary>
        public static bool MinerBarricadesShown(World w = null) => ShowMinerBarricades || !HutOn(w);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Mechanics
{
    // =====================================================================================================================
    // Mechanics depth (rules PR #58, U-063 to U-068): the view's logic, words and the ONE compile switch.
    //
    // This base does not contain the rules lane's fields or events. Events are found BY NAME (DepthEvents, the MechEvents
    // pattern); every direct read of a new field sits in DepthRead behind the scripting define CM_SIM_MECHANICS, which the
    // integrator turns on once feat/mechanics-depth merges (Player Settings scripting define, or -define:CM_SIM_MECHANICS in
    // a csc.rsp next to CriticalMass.Runtime.asmdef). With it off everything compiles, every read returns "rule off", and
    // the view shows only what the base can drive (the objective line, the station ring prompt words, the ping wheel).
    // =====================================================================================================================

    /// <summary>
    /// The ONE words table for every on-screen word of the mechanics-depth view. Built-in fallbacks equal the sim's *Names
    /// tables; with CM_SIM_MECHANICS on they are read from the sim (RoleNames, SourceNames, StationNames, PickupNames,
    /// BotNames, MapEventNames) so the owner's renames there show here; then the words pack (Resources/Lore/depth.json,
    /// <c>{"words":[{"id":"ObjFeedEgg","text":"..."}]}</c>) overrides any id. Never names a technology or vendor.
    /// </summary>
    public static class DepthWords
    {
        // ---- class roles (U-063)
        public static string[] RoleLine =
        {
            "Front line. Guard the carriers and take the hill.",
            "The egg's best feeder. Gather food and deliver it.",
            "Digs paths and walls. Chops wood fast.",
            "Mines and TNT. Opens the map.",
            "Builds ladders, bridges and the base upgrades.",
            "Keeps the team standing. Purifies the egg.",
        };
        public static string RefusedNotFeeder = "Pass it to a Farmer, or bank it at your base.";
        public static string RefusedNotRaider = "This class leaves corruption alone.";
        public static string Escort = "ESCORT";
        public static string PassToFarmer = "Pass to Farmer (bonus)";
        public static string HandedTo = " handed ";
        public static string HandedToWord = " to ";

        // ---- sources (U-064)
        public static string Wood = "WOOD", Water = "WATER", Food = "FOOD", Stock = "TEAM STOCK", Grove = "GROVE", Spring = "SPRING";
        public static string Bank = "BANK IT", Withdraw = "TAKE FOOD", Gathering = "GATHERING";

        // ---- stations (U-065)
        public static string Station = "BASE STATION", SwitchClass = "SWITCH CLASS", Upgrade = "UPGRADE", Level = "LEVEL";
        public static string OnlyBuilders = "Only a Builder can build upgrades. Switch to Builder here.";
        public static string NeedStock = "Not enough wood or water in the team stock.";
        public static string GoToStation = "Go to your base station to switch class.";
        public static string SwitchWait = "Switch ready in ";
        public static string OpenStation = "Base station";
        public static string MaxLevel = "MAX";
        public static string SwitchLimit = "ONE SWITCH EVERY ";
        public static string Current = "CURRENT";
        public static string LevelUpSuffix = " LEVEL ";
        public static string StationClose = "Close";

        // ---- pickups (U-066)
        public static string[] PickupName = { "COINS", "HASTE", "MIGHT", "WARD" };
        public static string[] PickupCounter =
        {
            "",
            "Roots and slows strip it.",
            "Ward and distance blunt it.",
            "Burst damage breaks it.",
        };
        public static string Blocked = "BLOCKED";
        public static string PowerIn = " in ";
        public static string Took = " took ";

        // ---- pings and bots (U-067)
        public static string[] CallShort = { "Attack!", "On my way", "Defend!", "Help!" };
        public static string[] StyleName = { "BRAWLER", "GUARD", "HARVESTER", "SAPPER" };
        public static string PingWait = "Wait ";

        // ---- map events (U-068)
        public static string DropIncoming = "SUPPLY DROP INCOMING", DropLanded = "SUPPLY DROP LANDED", DropClaimed = "SUPPLY DROP CLAIMED", DropContested = "CONTESTED";
        public static string ClaimedTheDrop = " claimed the supply drop";
        public static string EggExposed = "THE EGG IS EXPOSED. EVERY DELIVERY COUNTS DOUBLE.", EggSealed = "THE EGG HAS SEALED";
        public static string BridgeWarning = "A BRIDGE IS GIVING WAY", BridgeCollapsed = "BRIDGE DOWN", BridgeRepaired = "BRIDGE REBUILT";
        public static string RepairPrompt = "Builder: hold interact to rebuild", RepairNeedWood = "Not enough wood in the team stock.";
        public static string RepairCost = "2 WOOD";

        // ---- the "your job now" line (owner rule 2026-11-12: ANY class may feed; the Farmer is best at it)
        public static string ObjRespawn = "Pick your class while you wait";
        public static string ObjFeedEgg = "Feed the egg at the shrine";
        public static string ObjFeedOrPass = "Feed the egg, or pass to a Farmer for a bonus";
        public static string ObjPassOrBank = "Pass it to a Farmer, or bank it at your base";
        public static string ObjFeedExposed = "Feed the egg now: every delivery counts double";
        public static string ObjCorruption = "Take the corruption to the egg";
        public static string ObjBankWood = "Bring the wood to your base and bank it";
        public static string ObjWater = "Water cools the egg: feed it, or bank it at base";
        public static string ObjTakeFood = "Take food from the team stock";
        public static string ObjBuyUpgrade = "Buy an upgrade at the station";
        public static string ObjRepair = "Rebuild the bridge: hold interact";
        public static string ObjRepairWood = "Bank 2 wood to rebuild the bridge";
        public static string ObjClaimDrop = "Claim the supply drop";
        public static string ObjFollowOrder = "Your call is out: lead the way";
        public static string[] ObjClassIdle =
        {
            "Guard your carriers and push the hill",
            "Gather food and feed the egg",
            "Dig paths and chop wood for your team",
            "Mine the routes and blast the walls",
            "Fetch wood and water for upgrades",
            "Keep your team standing",
        };
        public static string[] ObjClassHatched =
        {
            "Take and hold the hill",
            "Keep the food moving",
            "Open paths to the hill",
            "Blast the enemy off the hill",
            "Build your team onto the hill",
            "Heal the fighters on the hill",
        };

        // ---- legend rows
        public static string LegendGrove = "Grove (wood)", LegendSpring = "Spring (water)", LegendPower = "Power-up";
        public static string LegendDrop = "Supply drop", LegendBridge = "Bridge falling", LegendStation = "Base station";

        static bool _loaded;

        /// <summary>Loads the sim tables (when compiled in) and the words pack once. Safe to call every frame.</summary>
        public static void Ensure()
        {
            if (_loaded) return;
            _loaded = true;
            FromSim();
            LoadPack();
        }

        static void FromSim()
        {
#if CM_SIM_MECHANICS
            if (RoleNames.RoleLine != null && RoleNames.RoleLine.Length >= RoleLine.Length) RoleLine = (string[])RoleNames.RoleLine.Clone();
            Escort = RoleNames.Escort;
            Wood = SourceNames.Wood; Water = SourceNames.Water; Food = SourceNames.Food; Stock = SourceNames.Stock;
            Grove = SourceNames.Grove; Spring = SourceNames.Spring; Bank = SourceNames.Bank; Withdraw = SourceNames.Withdraw;
            Station = StationNames.Station; SwitchClass = StationNames.SwitchClass; Upgrade = StationNames.Upgrade; Level = StationNames.Level;
            OnlyBuilders = StationNames.OnlyBuilders; NeedStock = StationNames.NeedStock;
            if (PickupNames.Name != null && PickupNames.Name.Length >= 4) PickupName = (string[])PickupNames.Name.Clone();
            if (PickupNames.Counter != null && PickupNames.Counter.Length >= 4) PickupCounter = (string[])PickupNames.Counter.Clone();
            if (BotNames.StyleName != null && BotNames.StyleName.Length >= 4) StyleName = (string[])BotNames.StyleName.Clone();
            DropIncoming = MapEventNames.DropIncoming; DropLanded = MapEventNames.DropLanded; DropClaimed = MapEventNames.DropClaimed;
            DropContested = MapEventNames.DropContested; EggExposed = MapEventNames.EggExposed; EggSealed = MapEventNames.EggSealed;
            BridgeWarning = MapEventNames.BridgeWarning; BridgeCollapsed = MapEventNames.BridgeCollapsed; BridgeRepaired = MapEventNames.BridgeRepaired;
            RepairPrompt = MapEventNames.RepairPrompt; RepairNeedWood = MapEventNames.RepairNeedWood;
            // The owner made every class a feeder (2026-11-12): the sim's "only Farmers" refusal words are replaced by the
            // softer built-in line above unless the words pack sets RefusedNotFeeder.
#endif
        }

        [Serializable] sealed class PackWord { public string id; public string text; }
        [Serializable] sealed class PackWords { public PackWord[] words; }

        static void LoadPack()
        {
            try
            {
                TextAsset ta = Resources.Load<TextAsset>("Lore/depth");
                if (ta == null) return;
                ApplyPack(ta.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Depth words not read, using built-in words: " + ex.Message);
            }
        }

        /// <summary>Applies a words pack (ids are the field names above; arrays take "Name.N"). Returns words applied.</summary>
        public static int ApplyPack(string json)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            PackWords p = JsonUtility.FromJson<PackWords>(json);
            if (p == null || p.words == null) return 0;
            int n = 0;
            const System.Reflection.BindingFlags S = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            foreach (PackWord w in p.words)
            {
                if (w == null || string.IsNullOrEmpty(w.id) || w.text == null) continue;
                string id = w.id;
                int dot = id.IndexOf('.');
                int ix = -1;
                if (dot > 0 && int.TryParse(id.Substring(dot + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out ix)) id = id.Substring(0, dot);
                System.Reflection.FieldInfo f = typeof(DepthWords).GetField(id, S);
                if (f == null) continue;
                if (f.FieldType == typeof(string) && ix < 0) { f.SetValue(null, w.text); n++; }
                else if (f.FieldType == typeof(string[]) && ix >= 0)
                {
                    var arr = (string[])f.GetValue(null);
                    if (arr != null && ix < arr.Length) { arr[ix] = w.text; n++; }
                }
            }
            return n;
        }

        public static string Role(ClassId c) { Ensure(); int i = (int)c; return i >= 0 && i < RoleLine.Length ? RoleLine[i] : string.Empty; }
        public static string Pickup(int kind) { Ensure(); return kind >= 0 && kind < PickupName.Length ? PickupName[kind] : string.Empty; }
        public static string Counter(int kind) { Ensure(); return kind >= 0 && kind < PickupCounter.Length ? PickupCounter[kind] : string.Empty; }
        public static string Style(int style) { Ensure(); return style >= 0 && style < StyleName.Length ? StyleName[style] : string.Empty; }

        /// <summary>The class card's role text with the class role line under it (allocates; once per card open).</summary>
        public static string WithRoleLine(string role, ClassId c)
        {
            string line = Role(c);
            if (string.IsNullOrEmpty(line)) return role ?? string.Empty;
            return string.IsNullOrEmpty(role) ? line : role + "\n" + line;
        }
    }

    /// <summary>What the view draws for one mechanics-depth event. Unmapped = nobody decided (the table test fails on it).</summary>
    public enum DepthVisual
    {
        Unmapped = -1,
        None = 0,
        RefusalPrompt,
        HandoffArc,
        GatherChop,
        GatherSplash,
        BankFly,
        WithdrawFly,
        SwitchFlash,
        SwitchRefusal,
        UpgradeBanner,
        UpgradeRefusal,
        CoinScatter,
        PowerBeam,
        CoinPop,
        PowerAura,
        PickupFade,
        AuraEnd,
        WardShatter,
        WardRipple,
        PingMarker,
        PingGrey,
        PingAck,
        DropIncoming,
        DropLanded,
        DropContest,
        DropClaimed,
        DropFade,
        EggOpen,
        EggSeal,
        BridgeWarn,
        BridgeFall,
        BridgeRepair,
        RepairRefusal,
        CoolPulse,
    }

    /// <summary>The event codes (looked up by name: this base lacks them) and the event-to-visual table.</summary>
    public static class DepthEvents
    {
        /// <summary>Every event type the view spec (briefs/mechanics-depth-view.md) lists, in spec order.</summary>
        public static readonly string[] SpecNames =
        {
            "DeliverRefused", "Handoff", "SourceGather", "BankDeposit", "BankWithdraw", "ClassSwitch", "Upgrade",
            "PickupSpawned", "PickupTaken", "PickupExpired", "PowerUpEnded", "WardAbsorb", "Ping", "PingRefused", "PingAnswered",
            "SupplyDropIncoming", "SupplyDropLanded", "SupplyDropContest", "SupplyDropClaimed", "SupplyDropExpired",
            "EggExposed", "EggExposedEnded", "BridgeWarning", "BridgeCollapsed", "BridgeRepaired",
        };

        public const int IxDeliverRefused = 0, IxHandoff = 1, IxSourceGather = 2, IxBankDeposit = 3, IxBankWithdraw = 4, IxClassSwitch = 5,
            IxUpgrade = 6, IxPickupSpawned = 7, IxPickupTaken = 8, IxPickupExpired = 9, IxPowerUpEnded = 10, IxWardAbsorb = 11, IxPing = 12,
            IxPingRefused = 13, IxPingAnswered = 14, IxDropIncoming = 15, IxDropLanded = 16, IxDropContest = 17, IxDropClaimed = 18,
            IxDropExpired = 19, IxEggExposed = 20, IxEggEnded = 21, IxBridgeWarning = 22, IxBridgeCollapsed = 23, IxBridgeRepaired = 24;

        /// <summary>SimEventType codes parallel to SpecNames (-1 = not in this build of the sim).</summary>
        public static readonly int[] Codes = BuildCodes();

        /// <summary>BiomassType.Wood / Water and CarryKind.Wood / Water as ints (-1 when this base lacks them).</summary>
        public static readonly int BioWood = EnumCode(typeof(BiomassType), "Wood"), BioWater = EnumCode(typeof(BiomassType), "Water");
        public static readonly int CarryWood = EnumCode(typeof(CarryKind), "Wood"), CarryWater = EnumCode(typeof(CarryKind), "Water");

        static int[] BuildCodes()
        {
            var a = new int[SpecNames.Length];
            for (int i = 0; i < a.Length; i++) a[i] = MechEvents.Code(SpecNames[i]);
            return a;
        }

        public static int EnumCode(Type t, string name)
        {
            try { return Enum.IsDefined(t, name) ? Convert.ToInt32(Enum.Parse(t, name), CultureInfo.InvariantCulture) : -1; }
            catch (ArgumentException) { return -1; }
        }

        /// <summary>True when this build of the sim has any of the depth events.</summary>
        public static bool AnyPresent
        {
            get
            {
                for (int i = 0; i < Codes.Length; i++) if (Codes[i] >= 0) return true;
                return false;
            }
        }

        /// <summary>The SpecNames index of a raw event code, or -1. 25 int compares: no allocation.</summary>
        public static int IndexOf(int code)
        {
            if (code < 0) return -1;
            for (int i = 0; i < Codes.Length; i++) if (Codes[i] == code) return i;
            return -1;
        }

        /// <summary>The visual of a depth event by spec index; b and v are the event's B and Value.</summary>
        public static DepthVisual VisualFor(int ix, int b, double v, int bio = -1)
        {
            switch (ix)
            {
                case IxDeliverRefused: return DepthVisual.RefusalPrompt;            // local prompt only, no world effect
                case IxHandoff: return DepthVisual.HandoffArc;
                case IxSourceGather: return bio >= 0 && bio == BioWater ? DepthVisual.GatherSplash : DepthVisual.GatherChop;
                case IxBankDeposit: return DepthVisual.BankFly;
                case IxBankWithdraw: return DepthVisual.WithdrawFly;
                case IxClassSwitch: return b == 0 ? DepthVisual.SwitchFlash : DepthVisual.SwitchRefusal;
                case IxUpgrade: return v >= 2 ? DepthVisual.UpgradeBanner : DepthVisual.UpgradeRefusal;
                case IxPickupSpawned: return b == 0 ? DepthVisual.CoinScatter : DepthVisual.PowerBeam;
                case IxPickupTaken: return b == 0 ? DepthVisual.CoinPop : DepthVisual.PowerAura;
                case IxPickupExpired: return DepthVisual.PickupFade;
                case IxPowerUpEnded: return v == 1 ? DepthVisual.WardShatter : DepthVisual.AuraEnd;
                case IxWardAbsorb: return DepthVisual.WardRipple;
                case IxPing: return DepthVisual.PingMarker;
                case IxPingRefused: return DepthVisual.PingGrey;
                case IxPingAnswered: return DepthVisual.PingAck;
                case IxDropIncoming: return DepthVisual.DropIncoming;
                case IxDropLanded: return DepthVisual.DropLanded;
                case IxDropContest: return DepthVisual.DropContest;
                case IxDropClaimed: return DepthVisual.DropClaimed;
                case IxDropExpired: return DepthVisual.DropFade;
                case IxEggExposed: return DepthVisual.EggOpen;
                case IxEggEnded: return DepthVisual.EggSeal;
                case IxBridgeWarning: return DepthVisual.BridgeWarn;
                // the grid already changed (those cells are void): GridChunkMeshView rebuilds from World.GridVersion as for a dig;
                // this view adds the falling planks.
                case IxBridgeCollapsed: return DepthVisual.BridgeFall;
                case IxBridgeRepaired: return v == -2 ? DepthVisual.RepairRefusal : v == -1 ? DepthVisual.None : DepthVisual.BridgeRepair;
                default: return DepthVisual.Unmapped;
            }
        }

        /// <summary>The visual of a depth event by its name (tests; b and v as above).</summary>
        public static DepthVisual VisualForName(string name, int b, double v, int bio = -1) => VisualFor(Array.IndexOf(SpecNames, name), b, v, bio);

        /// <summary>DeliverBiomass with Bio = Water cools the egg (a short blue pulse); any other delivery: none here.</summary>
        public static DepthVisual DeliveryVisual(BiomassType? bio) =>
            bio.HasValue && BioWater >= 0 && (int)bio.Value == BioWater ? DepthVisual.CoolPulse : DepthVisual.None;
    }

    /// <summary>
    /// Every read of a mechanics-depth sim field, behind CM_SIM_MECHANICS (the only place the view touches them). With the
    /// define off each read returns the "rule off" answer, so callers need no #if of their own.
    /// </summary>
    public static class DepthRead
    {
#if CM_SIM_MECHANICS
        public const bool Compiled = true;
#else
        public const bool Compiled = false;
#endif
        public const int StockWood = 0, StockWater = 1, StockFood = 2;
        public const int KindCoin = 0, KindHaste = 1, KindMight = 2, KindWard = 3;
        public const int DropNone = 0, DropIncoming = 1, DropLanded = 2;
        public const int BridgeNone = 0, BridgeWarned = 1, BridgeBroken = 2;
        public const int ContestNobody = 0, ContestAzure = 1, ContestCrimson = 2, ContestBoth = 3;

        /// <summary>The lane is compiled in and on for this world (not Classic).</summary>
        public static bool On(World w)
        {
#if CM_SIM_MECHANICS
            return w != null && w.Depth != null && !w.Config.Depth.Classic;
#else
            return false;
#endif
        }

#if CM_SIM_MECHANICS
        static bool Valid(World w, int id) => On(w) && id >= 0 && id < w.ActorCount;
#endif

        // ---- roles
        public static bool RolesOn(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) && Roles.On(w);
#else
            return false;
#endif
        }

        /// <summary>May this class feed the egg (sim flag; true with the rule off). The owner's 2026-11-12 rule: every class.</summary>
        public static bool MayFeed(World w, ClassId cls)
        {
#if CM_SIM_MECHANICS
            return !On(w) || Roles.MayFeed(w, cls);
#else
            return true;
#endif
        }

        public static bool MayCorrupt(World w, ClassId cls)
        {
#if CM_SIM_MECHANICS
            return !On(w) || Roles.MayCorrupt(w, cls);
#else
            return true;
#endif
        }

        /// <summary>The ally a carrier would hand to right now (-1 none or rule off).</summary>
        public static int HandoffTarget(World w, int id)
        {
#if CM_SIM_MECHANICS
            return RolesOn(w) && Valid(w, id) ? Roles.HandoffTarget(w, id) : -1;
#else
            return -1;
#endif
        }

        public static bool Escorting(World w, int id)
        {
#if CM_SIM_MECHANICS
            return Valid(w, id) && w.Actors[id].AiState == AiState.Escort;
#else
            return false;
#endif
        }

        // ---- sources and the team stock
        public static bool SourcesOn(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) && Sources.On(w);
#else
            return false;
#endif
        }

        public static int Stock(World w, TeamId team, int slot)
        {
#if CM_SIM_MECHANICS
            return On(w) ? w.Depth.Stock[(int)team * Sources.StockKinds + slot] : 0;
#else
            return 0;
#endif
        }

        public static int SourceCount(World w)
        {
#if CM_SIM_MECHANICS
            return SourcesOn(w) && w.Depth.Sources != null ? w.Depth.Sources.Length : 0;
#else
            return 0;
#endif
        }

        /// <summary>Source i: water (else wood), where, charges left, most charges, ticks to regrow.</summary>
        public static bool Source(World w, int i, out bool water, out double x, out double z, out int charges, out int max, out int regrowIn)
        {
            water = false; x = 0; z = 0; charges = 0; max = 0; regrowIn = 0;
#if CM_SIM_MECHANICS
            if (i < 0 || i >= SourceCount(w)) return false;
            SourceNode s = w.Depth.Sources[i];
            water = s.Kind == BiomassType.Water;
            x = s.X; z = s.Z; charges = s.Charges; max = s.MaxCharges; regrowIn = s.RegrowIn;
            return true;
#else
            return false;
#endif
        }

        /// <summary>Actor id's gather progress at a source, 0..1 (0 when not gathering).</summary>
        public static float GatherProgress01(World w, int id)
        {
#if CM_SIM_MECHANICS
            if (!SourcesOn(w) || !Valid(w, id)) return 0f;
            int si = w.Depth.SourceOf[id];
            if (si < 0 || si >= w.Depth.Sources.Length) return 0f;
            Actor a = w.Actors[id];
            double need = w.Config.Class(a.Cls).GatherSeconds * Sources.GatherScale(w.Config, a.Cls, w.Depth.Sources[si].Kind);
            return need > 0 ? Mathf.Clamp01((float)(w.Depth.SourceProgress[id] / need)) : 0f;
#else
            return 0f;
#endif
        }

        public static double BankRange(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) ? w.Config.Depth.Sources.BankRange : 4.5;
#else
            return 4.5;
#endif
        }

        // ---- stations
        public static bool StationsOn(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) && Stations.On(w);
#else
            return false;
#endif
        }

        public static double StationRange(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) ? w.Config.Depth.Stations.Range : 4.5;
#else
            return 4.5;
#endif
        }

        public static bool InStation(World w, int id)
        {
#if CM_SIM_MECHANICS
            return StationsOn(w) && Valid(w, id) && Stations.InStation(w, id);
#else
            return false;
#endif
        }

        public static int Level(World w, TeamId team, ClassId cls)
        {
#if CM_SIM_MECHANICS
            return On(w) ? Stations.Level(w, team, cls) : 1;
#else
            return 1;
#endif
        }

        public static int MaxLevel(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) ? Stations.MaxLevel(w.Config) : 1;
#else
            return 1;
#endif
        }

        public static bool NextCost(World w, TeamId team, ClassId cls, out int wood, out int water)
        {
            wood = 0; water = 0;
#if CM_SIM_MECHANICS
            return On(w) && Stations.NextCost(w, team, cls, out wood, out water);
#else
            return false;
#endif
        }

        /// <summary>Ticks until actor id's next class switch is accepted (0 = ready).</summary>
        public static int SwitchCdTicks(World w, int id)
        {
#if CM_SIM_MECHANICS
            return Valid(w, id) ? Math.Max(0, w.Actors[id].ClassSwitchCd) : 0;
#else
            return 0;
#endif
        }

        // ---- pickups and power-ups
        public static int PickupCapacity(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) && w.Depth.Pickups != null ? w.Depth.Pickups.Length : 0;
#else
            return 0;
#endif
        }

        public static bool PickupAt(World w, int slot, out int kind, out double x, out double z, out int value, out int readyTick, out int endTick)
        {
            kind = 0; x = 0; z = 0; value = 0; readyTick = 0; endTick = 0;
#if CM_SIM_MECHANICS
            if (slot < 0 || slot >= PickupCapacity(w)) return false;
            Pickup p = w.Depth.Pickups[slot];
            if (!p.Active) return false;
            kind = (int)p.Kind; x = p.X; z = p.Z; value = p.Value; readyTick = p.ReadyTick; endTick = p.EndTick;
            return true;
#else
            return false;
#endif
        }

        public static int PowerKind(World w, int id)
        {
#if CM_SIM_MECHANICS
            return Valid(w, id) ? w.Depth.PowerKind[id] : 0;
#else
            return 0;
#endif
        }

        public static int PowerEndTick(World w, int id)
        {
#if CM_SIM_MECHANICS
            return Valid(w, id) ? w.Depth.PowerEndTick[id] : 0;
#else
            return 0;
#endif
        }

        public static double WardLeft(World w, int id)
        {
#if CM_SIM_MECHANICS
            return Valid(w, id) ? w.Depth.WardLeft[id] : 0;
#else
            return 0;
#endif
        }

        public static double WardFull(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) ? w.Config.Depth.Pickups.WardHp : 60;
#else
            return 60;
#endif
        }

        public static int FullTicks(World w, int kind)
        {
#if CM_SIM_MECHANICS
            return On(w) && kind > 0 ? Pickups.FullTicks(w.Config, (PickupKind)kind) : 0;
#else
            return 0;
#endif
        }

        public static int CoinsPicked(World w, int id)
        {
#if CM_SIM_MECHANICS
            return Valid(w, id) ? w.Actors[id].CoinsPicked : 0;
#else
            return 0;
#endif
        }

        // ---- pings and bots
        public static bool PingsOn(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) && PingRules.On(w);
#else
            return false;
#endif
        }

        public static bool HasOrder(World w, int id)
        {
#if CM_SIM_MECHANICS
            return PingsOn(w) && Valid(w, id) && PingRules.HasOrder(w, id);
#else
            return false;
#endif
        }

        public static int OrderCall(World w, int id)
        {
#if CM_SIM_MECHANICS
            return HasOrder(w, id) ? (int)PingRules.OrderCall(w, id) : -1;
#else
            return -1;
#endif
        }

        public static int PingWaitTicks(World w, int id)
        {
#if CM_SIM_MECHANICS
            return PingsOn(w) && Valid(w, id) ? PingRules.WaitTicks(w, id) : 0;
#else
            return 0;
#endif
        }

        public static bool BotNamesOn(World w)
        {
#if CM_SIM_MECHANICS
            return On(w);
#else
            return false;
#endif
        }

        public static string BotName(World w, int id)
        {
#if CM_SIM_MECHANICS
            return Valid(w, id) ? BotNames.NameOf(w, id) : null;
#else
            return null;
#endif
        }

        public static int BotStyle(World w, int id)
        {
#if CM_SIM_MECHANICS
            return Valid(w, id) ? BotNames.StyleOf(w, id) : -1;
#else
            return -1;
#endif
        }

        // ---- map events
        public static bool EventsOn(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) && MapEvents.On(w);
#else
            return false;
#endif
        }

        public static int DropState(World w, out double x, out double z, out int tick)
        {
            x = 0; z = 0; tick = 0;
#if CM_SIM_MECHANICS
            if (!EventsOn(w)) return DropNone;
            x = w.Depth.DropX; z = w.Depth.DropZ; tick = w.Depth.DropTick;
            return w.Depth.DropState;
#else
            return DropNone;
#endif
        }

        public static float DropProgress01(World w, int team)
        {
#if CM_SIM_MECHANICS
            if (!EventsOn(w) || team < 0 || team > 1) return 0f;
            double full = w.Config.Depth.Events.DropClaimTicks;
            return full > 0 ? Mathf.Clamp01((float)(w.Depth.DropProgress[team] / full)) : 0f;
#else
            return 0f;
#endif
        }

        public static int DropContest(World w)
        {
#if CM_SIM_MECHANICS
            return EventsOn(w) ? w.Depth.DropContest : ContestNobody;
#else
            return ContestNobody;
#endif
        }

        public static double DropRange(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) ? w.Config.Depth.Events.DropClaimRange : 2.5;
#else
            return 2.5;
#endif
        }

        public static int EggExposedUntil(World w)
        {
#if CM_SIM_MECHANICS
            return EventsOn(w) ? w.Depth.EggExposedUntil : 0;
#else
            return 0;
#endif
        }

        public static int EggWindowTicks(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) ? (int)w.Config.Depth.Events.EggWindowTicks : 600;
#else
            return 600;
#endif
        }

        public static int BridgeState(World w, out int tick, out int brokenN)
        {
            tick = 0; brokenN = 0;
#if CM_SIM_MECHANICS
            if (!EventsOn(w)) return BridgeNone;
            tick = w.Depth.BridgeTick;
            brokenN = Math.Min(w.Depth.BrokenN, w.Depth.BrokenIdx.Length);
            return w.Depth.BridgeState;
#else
            return BridgeNone;
#endif
        }

        /// <summary>The grid cell index of the k-th cell of the falling or fallen bridge part.</summary>
        public static int BrokenCell(World w, int k)
        {
#if CM_SIM_MECHANICS
            return EventsOn(w) && k >= 0 && k < w.Depth.BrokenIdx.Length ? w.Depth.BrokenIdx[k] : -1;
#else
            return -1;
#endif
        }

        public static bool BridgeEnds(World w, out double ax, out double az, out double bx, out double bz)
        {
            ax = az = bx = bz = 0;
#if CM_SIM_MECHANICS
            if (!EventsOn(w) || w.Depth.BridgeState != MapEvents.BridgeBroken) return false;
            ax = w.Depth.BridgeAx; az = w.Depth.BridgeAz; bx = w.Depth.BridgeBx; bz = w.Depth.BridgeBz;
            return true;
#else
            return false;
#endif
        }

        public static float RepairProgress01(World w)
        {
#if CM_SIM_MECHANICS
            if (!EventsOn(w)) return 0f;
            double full = w.Config.Depth.Events.BridgeRepairTicks;
            return full > 0 ? Mathf.Clamp01((float)(w.Depth.RepairProgress / full)) : 0f;
#else
            return 0f;
#endif
        }

        public static double RepairRange(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) ? w.Config.Depth.Events.BridgeRepairRange : 3;
#else
            return 3;
#endif
        }

        public static int RepairWood(World w)
        {
#if CM_SIM_MECHANICS
            return On(w) ? (int)w.Config.Depth.Events.BridgeRepairWood : 2;
#else
            return 2;
#endif
        }

        /// <summary>SimEvent.Slot (the pickup pool slot) when compiled in, else -1.</summary>
        public static int Slot(SimEvent e)
        {
#if CM_SIM_MECHANICS
            return e != null ? e.Slot : -1;
#else
            return -1;
#endif
        }

        // ---- input (the only writes, into the human's InputFrame before the tick; never into the World)

        /// <summary>Writes the one-tick station purchase and ping into the frame (no-op with the define off).</summary>
        public static void WriteInput(InputFrame f, int stationBuy, int pingKind, double px, double pz)
        {
#if CM_SIM_MECHANICS
            f.StationBuy = stationBuy;
            f.PingKind = pingKind;
            f.PingX = pingKind > 0 ? px : 0;
            f.PingZ = pingKind > 0 ? pz : 0;
#endif
        }

        /// <summary>Reads back what WriteInput wrote (tests); false with the define off.</summary>
        public static bool ReadInput(InputFrame f, out int stationBuy, out int pingKind, out double px, out double pz)
        {
#if CM_SIM_MECHANICS
            stationBuy = f.StationBuy; pingKind = f.PingKind; px = f.PingX; pz = f.PingZ;
            return true;
#else
            stationBuy = 0; pingKind = 0; px = 0; pz = 0;
            return false;
#endif
        }
    }

    /// <summary>
    /// The one-tick latches the view hands to the sim: a ping (InputFrame.PingKind = call + 1, PingX, PingZ) and a station
    /// purchase (InputFrame.StationBuy = class index + 1). HumanInputState.Fill calls WriteInto once per tick, so each queued
    /// press reaches exactly ONE tick and is then cleared. Class switches reuse HumanInputState.QueueClassPick (ClassPick).
    /// </summary>
    public static class DepthInputLatch
    {
        static int _pingKind, _stationBuy;
        static double _px, _pz;

        /// <summary>While true (the station panel holds focus), Attack, Ability and Jump are dropped from the frame.</summary>
        public static bool SuppressButtons;

        public static bool PingPending => _pingKind > 0;
        public static int StationBuyPending => _stationBuy;

        /// <summary>Queues a ping of call (0 Attack, 1 On my way, 2 Defend, 3 Help) at sim (x, z). Bad values are ignored.</summary>
        public static bool QueuePing(int call, double x, double z)
        {
            if (call < 0 || call > 3 || double.IsNaN(x) || double.IsNaN(z) || double.IsInfinity(x) || double.IsInfinity(z)) return false;
            _pingKind = call + 1;
            _px = x;
            _pz = z;
            return true;
        }

        /// <summary>Queues a purchase of the next level of class index ci (0..5).</summary>
        public static bool QueueStationBuy(int classIndex)
        {
            if (classIndex < 0 || classIndex >= ClassIds.All.Count) return false;
            _stationBuy = classIndex + 1;
            return true;
        }

        /// <summary>Takes this tick's latches (cleared after): station buy and ping kind (0 = none), ping point.</summary>
        public static void Take(out int stationBuy, out int pingKind, out double px, out double pz)
        {
            stationBuy = _stationBuy;
            pingKind = _pingKind;
            px = _px;
            pz = _pz;
            _stationBuy = 0;
            _pingKind = 0;
        }

        public static void Clear()
        {
            _stationBuy = 0;
            _pingKind = 0;
        }

        /// <summary>HumanInputState.Fill: this tick's latches into the human frame (exactly one tick each).</summary>
        public static void WriteInto(InputFrame f)
        {
            if (f == null) return;
            Take(out int sb, out int pk, out double x, out double z);
            DepthRead.WriteInput(f, sb, pk, x, z);
            if (SuppressButtons)
            {
                f.Attack = false;
                f.Ability = false;
                f.Jump = false;
            }
        }
    }

    /// <summary>What the "your job now" line says. Pure: tests drive it with facts, the HUD fills the facts from the world.</summary>
    public enum ObjectiveId
    {
        None, Respawn, FeedEgg, FeedOrPass, PassOrBank, FeedExposed, Corruption, BankWood, Water, TakeFood, BuyUpgrade, Repair,
        RepairWood, ClaimDrop, ClassIdle, ClassHatched,
    }

    public struct ObjectiveFacts
    {
        public ClassId Cls;
        public bool Alive, Hatched;
        /// <summary>The lane's rules are on (compiled and not Classic).</summary>
        public bool RulesOn;
        public CarryKind Carry;
        public int Units, Meat;
        /// <summary>From the sim's role flags (Roles.MayFeed); true with the rule off.</summary>
        public bool MayFeed;
        public bool InBase, InStation;
        public int FoodStock, WoodStock;
        public bool BuilderCanBuy;
        public bool NearBrokenBridge;
        public bool EggExposed, DropActive;
    }

    public static class Objective
    {
        static bool IsWood(CarryKind k) => DepthEvents.CarryWood >= 0 && (int)k == DepthEvents.CarryWood;
        static bool IsWater(CarryKind k) => DepthEvents.CarryWater >= 0 && (int)k == DepthEvents.CarryWater;

        /// <summary>The objective for the facts. Never tells a class it cannot feed (owner rule 2026-11-12).</summary>
        public static ObjectiveId For(in ObjectiveFacts f)
        {
            if (!f.Alive) return ObjectiveId.Respawn;
            bool cargo = f.Units > 0 && f.Carry != CarryKind.None;
            if (cargo && f.Carry == CarryKind.Corruption) return ObjectiveId.Corruption;
            if (cargo && IsWood(f.Carry)) return ObjectiveId.BankWood;
            if (cargo && IsWater(f.Carry)) return ObjectiveId.Water;
            bool food = (cargo && f.Carry == CarryKind.Biomass) || f.Meat > 0;
            if (food)
            {
                if (!f.MayFeed) return ObjectiveId.PassOrBank;
                if (f.EggExposed) return ObjectiveId.FeedExposed;
                return f.RulesOn && f.Cls != ClassId.Farmer ? ObjectiveId.FeedOrPass : ObjectiveId.FeedEgg;
            }
            if (f.RulesOn)
            {
                if (f.Cls == ClassId.Builder && f.NearBrokenBridge) return f.WoodStock >= 2 ? ObjectiveId.Repair : ObjectiveId.RepairWood;
                if (f.Cls == ClassId.Builder && f.InStation && f.BuilderCanBuy) return ObjectiveId.BuyUpgrade;
                if (f.Cls == ClassId.Farmer && f.InBase && f.FoodStock > 0) return ObjectiveId.TakeFood;
                if (f.DropActive) return ObjectiveId.ClaimDrop;
            }
            return f.Hatched ? ObjectiveId.ClassHatched : ObjectiveId.ClassIdle;
        }

        /// <summary>The words of an objective (from DepthWords; never allocates).</summary>
        public static string Words(ObjectiveId id, ClassId cls)
        {
            DepthWords.Ensure();
            int c = Mathf.Clamp((int)cls, 0, DepthWords.ObjClassIdle.Length - 1);
            switch (id)
            {
                case ObjectiveId.Respawn: return DepthWords.ObjRespawn;
                case ObjectiveId.FeedEgg: return DepthWords.ObjFeedEgg;
                case ObjectiveId.FeedOrPass: return DepthWords.ObjFeedOrPass;
                case ObjectiveId.PassOrBank: return DepthWords.ObjPassOrBank;
                case ObjectiveId.FeedExposed: return DepthWords.ObjFeedExposed;
                case ObjectiveId.Corruption: return DepthWords.ObjCorruption;
                case ObjectiveId.BankWood: return DepthWords.ObjBankWood;
                case ObjectiveId.Water: return DepthWords.ObjWater;
                case ObjectiveId.TakeFood: return DepthWords.ObjTakeFood;
                case ObjectiveId.BuyUpgrade: return DepthWords.ObjBuyUpgrade;
                case ObjectiveId.Repair: return DepthWords.ObjRepair;
                case ObjectiveId.RepairWood: return DepthWords.ObjRepairWood;
                case ObjectiveId.ClaimDrop: return DepthWords.ObjClaimDrop;
                case ObjectiveId.ClassHatched: return DepthWords.ObjClassHatched[c];
                case ObjectiveId.ClassIdle: return DepthWords.ObjClassIdle[c];
                default: return string.Empty;
            }
        }

        /// <summary>Fills the facts for actor id from the world (read only).</summary>
        public static ObjectiveFacts Facts(World w, int id)
        {
            var f = new ObjectiveFacts();
            if (w == null || id < 0 || id >= w.ActorCount) return f;
            Actor a = w.Actors[id];
            f.Cls = a.Cls;
            f.Alive = a.Alive;
            f.Hatched = w.Match.Hatched;
            f.RulesOn = DepthRead.On(w);
            f.Carry = a.Carry;
            f.Units = a.CarryAmount;
            f.Meat = a.CarryMeat;
            f.MayFeed = DepthRead.MayFeed(w, a.Cls);
            if (f.RulesOn)
            {
                CriticalMass.Sim.Grid.PadDef pad = w.Map.Pads[(int)a.Team];
                double dx = w.X[id] - pad.X, dz = w.Z[id] - pad.Z, r = DepthRead.BankRange(w);
                f.InBase = dx * dx + dz * dz <= r * r;
                f.InStation = DepthRead.InStation(w, id);
                f.FoodStock = DepthRead.Stock(w, a.Team, DepthRead.StockFood);
                f.WoodStock = DepthRead.Stock(w, a.Team, DepthRead.StockWood);
                f.EggExposed = DepthRead.EggExposedUntil(w) > w.Tick;
                f.DropActive = DepthRead.DropState(w, out _, out _, out _) != DepthRead.DropNone;
                if (a.Cls == ClassId.Builder)
                {
                    int wood = f.WoodStock, water = DepthRead.Stock(w, a.Team, DepthRead.StockWater);
                    for (int k = 0; k < ClassIds.All.Count && !f.BuilderCanBuy; k++)
                        if (DepthRead.NextCost(w, a.Team, ClassIds.All[k], out int cw, out int cwa) && wood >= cw && water >= cwa) f.BuilderCanBuy = true;
                    if (DepthRead.BridgeEnds(w, out double ax, out double az, out double bx, out double bz))
                    {
                        double rr = DepthRead.RepairRange(w);
                        double da = (w.X[id] - ax) * (w.X[id] - ax) + (w.Z[id] - az) * (w.Z[id] - az);
                        double db = (w.X[id] - bx) * (w.X[id] - bx) + (w.Z[id] - bz) * (w.Z[id] - bz);
                        f.NearBrokenBridge = da <= rr * rr || db <= rr * rr;
                    }
                }
            }
            else f.EggExposed = false;
            return f;
        }
    }

    /// <summary>The station panel's rules, mirroring Stations.UpgradeRefusal and the class-switch answer codes.</summary>
    public static class StationRules
    {
        public enum Buy { Ok = 0, NotBuilder, NotAtStation, MaxLevel, NeedStock }
        public enum Switch { Ok = 0, Current, NotAtStation, Cooldown }

        /// <summary>May this player buy the next level now? Same order as the sim: buyer, place, ladder top, stock.</summary>
        public static Buy CanBuy(bool alive, bool isBuilder, bool inStation, bool hasNext, int wood, int water, int costWood, int costWater)
        {
            if (!alive || !isBuilder) return Buy.NotBuilder;
            if (!inStation) return Buy.NotAtStation;
            if (!hasNext) return Buy.MaxLevel;
            if (wood < costWood || water < costWater) return Buy.NeedStock;
            return Buy.Ok;
        }

        /// <summary>The sim's Upgrade refusal code (Value -2..-5) as a panel state.</summary>
        public static Buy FromSimCode(int value) =>
            value == -2 ? Buy.MaxLevel : value == -3 ? Buy.NeedStock : value == -4 ? Buy.NotBuilder : value == -5 ? Buy.NotAtStation : Buy.Ok;

        /// <summary>The refusal words of a buy state (null when Ok).</summary>
        public static string Words(Buy b)
        {
            DepthWords.Ensure();
            switch (b)
            {
                case Buy.NotBuilder: return DepthWords.OnlyBuilders;
                case Buy.NotAtStation: return DepthWords.GoToStation;
                case Buy.MaxLevel: return DepthWords.MaxLevel;
                case Buy.NeedStock: return DepthWords.NeedStock;
                default: return null;
            }
        }

        /// <summary>May the player switch to target now? Alive needs the station and no cooldown; dead always may (respawn pick).</summary>
        public static Switch CanSwitch(bool alive, bool inStation, ClassId current, ClassId target, int cdTicks)
        {
            if (alive && current == target) return Switch.Current;
            if (!alive) return Switch.Ok;
            if (!inStation) return Switch.NotAtStation;
            if (cdTicks > 0) return Switch.Cooldown;
            return Switch.Ok;
        }

        /// <summary>Whole seconds of a tick count (ceil; 0 at or under 0).</summary>
        public static int Seconds(int ticks, double simHz) => ticks <= 0 || simHz <= 0 ? 0 : (int)Math.Ceiling(ticks / simHz - 1e-9);
    }

    /// <summary>The HUD's power-up timers: what is left of a Haste, Might or Ward and how it is drawn.</summary>
    public static class PowerTimers
    {
        /// <summary>Time left 0..1 of a power-up ending at endTick that lasts fullTicks, at a fractional tick.</summary>
        public static float Left01(int tick, double alpha, int endTick, int fullTicks)
        {
            if (fullTicks <= 0) return 0f;
            double left = endTick - (tick + Math.Max(0, Math.Min(1, alpha)));
            return Mathf.Clamp01((float)(left / fullTicks));
        }

        /// <summary>Whole seconds left (ceil), 0 when over.</summary>
        public static int SecondsLeft(int tick, int endTick, double simHz) => StationRules.Seconds(endTick - tick, simHz);

        /// <summary>True while a power-up of a real kind (1..3) has time left.</summary>
        public static bool Active(int kind, int tick, int endTick) => kind > 0 && kind <= 3 && endTick > tick;

        /// <summary>The ward shell's thickness 0..1 from the HP it still soaks.</summary>
        public static float WardShell01(double left, double full) => full > 0 ? Mathf.Clamp01((float)(left / full)) : 0f;

        /// <summary>The HUD timer row shape per kind: Haste a forward arrow, Might a diamond, Ward a shield.</summary>
        public static readonly CriticalMass.Runtime.Hud.Ui.ShapeKind[] Icon =
        {
            CriticalMass.Runtime.Hud.Ui.ShapeKind.Circle, CriticalMass.Runtime.Hud.Ui.ShapeKind.TriangleRight,
            CriticalMass.Runtime.Hud.Ui.ShapeKind.Diamond, CriticalMass.Runtime.Hud.Ui.ShapeKind.Shield,
        };

        /// <summary>Aura colours, outside both team hues (spec 4): coin gold, Haste white, Might warm orange, Ward pale blue.</summary>
        public static readonly Color32[] Colour =
        {
            new Color32(255, 206, 84, 255), new Color32(246, 246, 240, 255), new Color32(255, 142, 52, 255), new Color32(170, 220, 255, 255),
        };

        /// <summary>A coin flying to the counter: progress 0..1 over seconds (ease in), 1 = arrived.</summary>
        public static float Fly01(float age, float seconds) => seconds <= 0 ? 1f : Mathf.Clamp01(age / seconds) * Mathf.Clamp01(age / seconds);
    }

    /// <summary>
    /// The bots' names for the scoreboard, kill feed, ping feed and the name tags (BotNames.NameOf / StyleOf through DepthRead).
    /// Strings are built once per match (World.Epoch), so the per-frame callers never allocate.
    /// </summary>
    public static class DepthNames
    {
        static World _w;
        static int _epoch = int.MinValue;
        static string[] _name = new string[0], _row = new string[0];

        /// <summary>Binds the match (call once per tick or frame; rebuilds only on a new world or epoch).</summary>
        public static void Bind(World w)
        {
            if (w == null) return;
            if (ReferenceEquals(w, _w) && w.Epoch == _epoch && _name.Length >= w.ActorCount) return;
            _w = w;
            _epoch = w.Epoch;
            int n = w.ActorCount;
            if (_name.Length < n) { _name = new string[n]; _row = new string[n]; }
            bool on = DepthRead.BotNamesOn(w);
            for (int i = 0; i < _name.Length; i++)
            {
                string nm = on && i < n ? DepthRead.BotName(w, i) : null;
                _name[i] = nm != null ? nm.ToUpperInvariant() : null;
                _row[i] = null;
            }
        }

        /// <summary>Forgets the bound match (tests that bind a throwaway world; a new match binds again on its first tick).</summary>
        public static void Unbind()
        {
            _w = null;
            _epoch = int.MinValue;
            for (int i = 0; i < _name.Length; i++) { _name[i] = null; _row[i] = null; }
        }

        /// <summary>The bound match's name of actor id (upper case), or null without names.</summary>
        public static string Name(int id) => id >= 0 && id < _name.Length ? _name[id] : null;

        /// <summary>A name, else the fallback (class word).</summary>
        public static string OrFallback(int id, string fallback) => Name(id) ?? fallback;

        /// <summary>
        /// Integration batch 4: the name actor id WILL have in a match created on seed with cfg (the READY screen, before the
        /// world exists). Same table as the match (BotNames.NameIndex(seed, id)), upper case; null with the depth rules off.
        /// </summary>
        public static string Preview(GameConfig cfg, int seed, int id)
        {
#if CM_SIM_MECHANICS
            if (cfg == null || cfg.Depth == null || cfg.Depth.Classic || id < 0) return null;
            return BotNames.Pool[BotNames.NameIndex(seed, id)].ToUpperInvariant();
#else
            return null;
#endif
        }

        /// <summary>The scoreboard's first cell: "YOU" for the local player, "THORN  BRAWLER" for a named bot, else the class word.</summary>
        public static string RowLabel(int id, bool own, string you, string classWord)
        {
            if (own) return you;
            string nm = Name(id);
            if (nm == null) return classWord;
            if (_row[id] == null && _w != null)
            {
                int style = DepthRead.BotStyle(_w, id);
                string tag = DepthWords.Style(style);
                _row[id] = string.IsNullOrEmpty(tag) ? nm : nm + "  " + tag;
            }
            return _row[id] ?? nm;
        }

        /// <summary>Forget the cached style tags (a class switch changes the style).</summary>
        public static void StyleChanged(int id)
        {
            if (id >= 0 && id < _row.Length) _row[id] = null;
        }

        public static void Clear()
        {
            _w = null;
            _epoch = int.MinValue;
            _name = new string[0];
            _row = new string[0];
        }
    }

    /// <summary>Feed line words, cached per (actor, call) so a ping or answer allocates nothing after the first.</summary>
    public static class DepthLines
    {
        static readonly Dictionary<long, string> Cache = new Dictionary<long, string>(256);
        static int _epoch = int.MinValue;

        static string Get(int epoch, int a, int b, int kind, Func<string> make)
        {
            if (epoch != _epoch) { Cache.Clear(); _epoch = epoch; }
            long key = ((long)kind << 48) | ((long)(a & 0xFFFF) << 24) | (long)(b & 0xFFFFFF);
            if (Cache.TryGetValue(key, out string s)) return s;
            if (Cache.Count > 1024) Cache.Clear();
            s = make();
            Cache[key] = s;
            return s;
        }

        static string Who(int id, int local, string classWord) => id == local ? "You" : TitleCase(DepthNames.OrFallback(id, classWord));

        static string TitleCase(string s) => string.IsNullOrEmpty(s) ? s : s.Substring(0, 1).ToUpperInvariant() + s.Substring(1).ToLowerInvariant();

        /// <summary>"Brook: Help!" (a call by a teammate or a bot).</summary>
        public static string Call(int epoch, int who, int local, int call, string classWord) =>
            Get(epoch, who, call, 1, () => Who(who, local, classWord) + ": " + DepthWords.CallShort[Mathf.Clamp(call, 0, 3)]);

        /// <summary>"Thorn handed 2 wood to Iris".</summary>
        public static string Handoff(int epoch, int giver, int receiver, int local, int units, string kind, string giverClass, string receiverClass) =>
            Get(epoch, giver * 64 + receiver, units, 2 + (kind.Length << 4), () =>
                Who(giver, local, giverClass) + DepthWords.HandedTo + units.ToString(CultureInfo.InvariantCulture) + " " + kind.ToLowerInvariant() +
                DepthWords.HandedToWord + Who(receiver, local, receiverClass));

        /// <summary>"Brook took HASTE".</summary>
        public static string Took(int epoch, int who, int local, int kind, string classWord) =>
            Get(epoch, who, kind, 3, () => Who(who, local, classWord) + DepthWords.Took + DepthWords.Pickup(kind));

        /// <summary>"Thorn claimed the supply drop".</summary>
        public static string Claimed(int epoch, int who, int local, string classWord) =>
            Get(epoch, who, 0, 4, () => Who(who, local, classWord) + DepthWords.ClaimedTheDrop);

        /// <summary>"AZURE WARRIORS LEVEL 2".</summary>
        public static string LevelUp(int epoch, int team, int cls, int level, string classWord) =>
            Get(epoch, team * 8 + cls, level, 5, () => classWord + "S" + DepthWords.LevelUpSuffix + level.ToString(CultureInfo.InvariantCulture));

        /// <summary>"HASTE in 5".</summary>
        public static string PowerIn(int epoch, int kind, int seconds) =>
            Get(epoch, kind, seconds, 6, () => DepthWords.Pickup(kind) + DepthWords.PowerIn + seconds.ToString(CultureInfo.InvariantCulture));

        /// <summary>"Switch ready in 3 s" / "Wait 2 s".</summary>
        public static string Wait(int prefixKind, int seconds) =>
            Get(0, prefixKind, seconds, 7, () => (prefixKind == 0 ? DepthWords.SwitchWait : DepthWords.PingWait) + seconds.ToString(CultureInfo.InvariantCulture) + " s");

        /// <summary>"+3".</summary>
        public static string Plus(int n) => Get(0, 0, n, 8, () => "+" + n.ToString(CultureInfo.InvariantCulture));

        /// <summary>A plain integer as text, cached.</summary>
        public static string Int(int n) => Get(0, 0, n & 0xFFFFFF, 9, () => n.ToString(CultureInfo.InvariantCulture));
    }
}

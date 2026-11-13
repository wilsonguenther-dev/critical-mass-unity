using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// U-063 class roles (our rule; not in the browser). The owner after playing: "the warriors are feeding the eggs, it's
    /// not even proper". With the rule on:
    /// <para>FEED. Only a class with Roles.MayFeed (the Farmer) hands biomass of any type to the egg. Any class may gather and
    /// carry biomass; a carrier that may not feed presses interact beside an allied feeder to HAND IT OFF (Handoff event), or
    /// banks it at its base (U-064). At the shrine it gets one DeliverRefused event (per RefuseLockTicks) and keeps its load.</para>
    /// <para>CORRUPT. Only a class with Roles.MayCorrupt (Warrior, Miner, Demolitions: the raiders) gathers, carries and
    /// delivers corruption. The Farmer, Builder and Healer cannot take it from a node.</para>
    /// <para>Bots obey: only feeders gather and deliver biomass, only raiders run corruption; an egg-duty bot that may do
    /// neither escorts the nearest allied carrier (AiState.Escort) and still contests enemy carriers at the shrine.</para>
    /// Deterministic (actor-id order, no RNG), allocation-free.
    /// </summary>
    public static class Roles
    {
        /// <summary>DeliverRefused codes (SimEvent.B).</summary>
        public const int RefusedNotFeeder = 1, RefusedNotRaider = 2;

        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Roles.Enabled);
        public static bool On(GameConfig c) => DepthRules.On(c, c.Depth.Roles.Enabled);

        /// <summary>May cls hand biomass to the egg? Always true with the rule off.</summary>
        public static bool MayFeed(World w, ClassId cls)
        {
            if (!On(w)) return true;
            bool[] m = w.Config.Depth.Roles.MayFeed;
            int k = (int)cls;
            return k >= 0 && k < m.Length && m[k];
        }

        /// <summary>May cls gather, carry and deliver corruption? Always true with the rule off.</summary>
        public static bool MayCorrupt(World w, ClassId cls)
        {
            if (!On(w)) return true;
            bool[] m = w.Config.Depth.Roles.MayCorrupt;
            int k = (int)cls;
            return k >= 0 && k < m.Length && m[k];
        }

        /// <summary>May actor a deliver cargo of this kind at the shrine?</summary>
        public static bool MayDeliver(World w, Actor a, CarryKind kind)
        {
            if (kind == CarryKind.Corruption) return MayCorrupt(w, a.Cls);
            return MayFeed(w, a.Cls);
        }

        /// <summary>
        /// The shrine refused actor id's load (ResourcesSystem): one DeliverRefused event per RefuseLockTicks: A = the carrier,
        /// B = RefusedNotFeeder / RefusedNotRaider, (X, Z) = the shrine, Value = units it holds.
        /// </summary>
        public static void Refuse(World w, int id, int code, int units)
        {
            DepthState d = w.Depth;
            if (w.Tick < d.RefuseReadyTick[id]) return;
            d.RefuseReadyTick[id] = w.Tick + Durations.T30(w.Config, w.Config.Depth.Roles.RefuseLockTicks);
            d.Refusals++;
            Grid.ShrineDef sh = w.Map.Shrine;
            World.PushEvent(w, SimEventType.DeliverRefused, id, code, sh.X, sh.Z, units);
        }

        /// <summary>Units of a's class cargo a receiver r can still take (0 when its cargo is of another kind or full).</summary>
        private static int CargoRoom(World w, Actor r, CarryKind kind)
        {
            if (r.Carry != CarryKind.None && r.CarryAmount > 0 && r.Carry != kind) return 0;
            int cap = (int)w.Config.Class(r.Cls).CarryCapacity;
            return cap > r.CarryAmount ? cap - r.CarryAmount : 0;
        }

        /// <summary>
        /// The allied feeder actor id would hand its biomass to right now, or -1: alive, standing, same team, a class that may
        /// feed, within HandoffRange (edge to edge) and with room for the cargo or the meat; nearest first, lowest id on a tie.
        /// </summary>
        public static int HandoffTarget(World w, int id)
        {
            Actor a = w.Actors[id];
            bool cargo = a.CarryAmount > 0 && a.Carry != CarryKind.None && a.Carry != CarryKind.Corruption;
            if (!cargo && a.CarryMeat <= 0) return -1;
            GameConfig cfg = w.Config;
            double range = cfg.Depth.Roles.HandoffRange + cfg.Class(a.Cls).Radius;
            int meatCap = (int)cfg.Resources.MeatCarryCapacity;
            int best = -1;
            double bestD2 = double.PositiveInfinity;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == id) continue;
                Actor r = w.Actors[j];
                if (!r.Alive || r.StandUp > 0 || r.Team != a.Team || !MayFeed(w, r.Cls)) continue;
                bool room = (cargo && CargoRoom(w, r, a.Carry) > 0) || (a.CarryMeat > 0 && r.CarryMeat < meatCap && r.Carry != CarryKind.Corruption);
                if (!room) continue;
                double reach = range + cfg.Class(r.Cls).Radius;
                double dx = w.X[j] - w.X[id];
                double dz = w.Z[j] - w.Z[id];
                double d2 = dx * dx + dz * dz;
                if (d2 > reach * reach || d2 >= bestD2) continue;
                best = j;
                bestD2 = d2;
            }
            return best;
        }

        /// <summary>
        /// Hand-offs, once per step after ResourcesSystem: every living, standing actor that PRESSES interact (rising edge), may NOT feed and
        /// carries biomass (class cargo that is not corruption, or meat) gives as much as fits to HandoffTarget. One Handoff
        /// event per type moved: A = giver, B = receiver, (X, Z) = the receiver, Value = units, Bio = the type.
        /// </summary>
        public static void HandoffSystem(World w)
        {
            if (!On(w)) return;
            if (w.Kaiju.State != KaijuState.Egg || w.Match.Hatched) return;
            int n = w.ActorCount;
            int meatCap = (int)w.Config.Resources.MeatCarryCapacity;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive || a.StandUp > 0 || !w.Inputs[id].Interact || w.Depth.PrevInteract[id] != 0 || MayFeed(w, a.Cls)) continue;
                int j = HandoffTarget(w, id);
                if (j < 0) continue;
                Actor r = w.Actors[j];
                if (a.CarryAmount > 0 && a.Carry != CarryKind.None && a.Carry != CarryKind.Corruption)
                {
                    int room = CargoRoom(w, r, a.Carry);
                    int give = a.CarryAmount < room ? a.CarryAmount : room;
                    if (give > 0)
                    {
                        CarryKind kind = a.Carry;
                        r.Carry = kind;
                        r.CarryAmount += give;
                        a.CarryAmount -= give;
                        if (a.CarryAmount <= 0) a.Carry = CarryKind.None;
                        a.GatherProgress = 0;
                        a.GatherNodeId = -1;
                        Count(w, give);
                        World.PushEvent(w, SimEventType.Handoff, id, j, w.X[j], w.Z[j], give, BioOf(kind), DamageCause.None);
                    }
                }
                if (a.CarryMeat > 0 && r.CarryMeat < meatCap && r.Carry != CarryKind.Corruption)
                {
                    int room = meatCap - r.CarryMeat;
                    int give = a.CarryMeat < room ? a.CarryMeat : room;
                    r.CarryMeat += give;
                    a.CarryMeat -= give;
                    Count(w, give);
                    World.PushEvent(w, SimEventType.Handoff, id, j, w.X[j], w.Z[j], give, BiomassType.Meat, DamageCause.None);
                }
            }
        }

        private static void Count(World w, int units)
        {
            w.Depth.Handoffs++;
            w.Depth.HandoffUnits += units;
        }

        /// <summary>The biomass type a cargo kind is counted as (Biomass cargo is plant; U-064 wood and water are their own).</summary>
        public static BiomassType BioOf(CarryKind kind)
        {
            if (kind == CarryKind.Wood) return BiomassType.Wood;
            if (kind == CarryKind.Water) return BiomassType.Water;
            return BiomassType.Plant;
        }
    }

    /// <summary>
    /// U-063 on-screen role lines, in ONE place for the owner to reword (class cards, the respawn screen and the refusal
    /// prompt read these). Indexed by (int)ClassId. Data only: the sim never shows text.
    /// </summary>
    public static class RoleNames
    {
        public static readonly string[] RoleLine =
        {
            "FRONT LINE. Holds the hill, guards carriers, may carry corruption.",
            "FEEDER. The only class the egg takes food from.",
            "SAPPER. Digs and places ground, breaks walls, may carry corruption.",
            "BLASTER. TNT and mines, no weapon, may carry corruption.",
            "ENGINEER. Ladders, bridges, repairs and class upgrades at the base.",
            "MEDIC. Heals allies and purifies the egg, no weapon.",
        };
        public const string RefusedNotFeeder = "Only Farmers feed the egg. Press interact beside a Farmer to hand it over, or bank it at your base.";
        public const string RefusedNotRaider = "This class will not touch corruption.";
        public const string Escort = "ESCORT";
    }
}

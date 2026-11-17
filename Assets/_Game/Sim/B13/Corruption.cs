// CM b13 item C (corruption crystal and the wild Kaiju). Travis: corruption as its own carried resource, a capped sqrt chance
// rolled only at the hatch and at a knock-out get-up, and a wild Kaiju loyal to neither side as the trailing team's lever.
using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>CM b13 item C: the corruption pool, rolls, loose crystals and per-actor counters (World.Corrupt). Fixed arrays, no per-tick allocation.</summary>
    public sealed class CorruptionState
    {
        /// <summary>The crystal veins got their charges (first ResourcesSystem pass of a match with the rule on).</summary>
        public bool VeinsReady;
        /// <summary>Weighted crystals fed to the egg before the hatch (the pre-hatch pool before cleanse / purify).</summary>
        public double PreFed;
        /// <summary>Weighted crystals fed into a knocked-out Kaiju since the last roll.</summary>
        public double PostPool;
        /// <summary>Rolls made this match (the roll hash index).</summary>
        public int RollIndex;
        public int Successes;
        /// <summary>The Kaiju went wild (rest of the match; never rolled again).</summary>
        public bool Wild;
        /// <summary>The last roll: tick (-1 none), chance, u, result.</summary>
        public int LastRollTick = -1;
        public double LastChance;
        public double LastU;
        public bool LastSuccess;
        /// <summary>The hill-seconds trailing team at the last roll (0 azure, 1 crimson, -1 none).</summary>
        public int LastRollTrailing = -1;
        /// <summary>The tick of the successful roll (-1 none).</summary>
        public int WildTick = -1;
        /// <summary>b13 r3: the team a wild Kaiju is going after (0 azure, 1 crimson, -1 none yet); sticky until the other team leads by WildSwitchMargin.</summary>
        public int WildLeader = -1;
        /// <summary>Crystals mined per team (0 azure, 1 crimson).</summary>
        public int[] Mined = new int[2];
        /// <summary>Crystals fed per team (unweighted, both phases).</summary>
        public int[] Fed = new int[2];
        /// <summary>The last tick some actor was winding up a feed into the downed Kaiju, and who (view).</summary>
        public int FeedTick = -1;
        public int FeedActor = -1;
        /// <summary>Crystals each actor carried at the end of the last pass (knock-out drop detection).</summary>
        public int[] PrevCrystals;
        /// <summary>Hold counter of the owner Healer's post-hatch purify.</summary>
        public int[] PurifyHold;
        /// <summary>Loose crystals (pooled slots).</summary>
        public bool[] DropActive;
        public double[] DropX;
        public double[] DropZ;
        public int[] DropUnits;
        public int[] DropTtl;

        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w)
        {
            int cap = w.Actors.Length;
            PrevCrystals = new int[cap];
            PurifyHold = new int[cap];
            int slots = Math.Max(1, w.Config.Depth.Corruption.DropSlots);
            DropActive = new bool[slots];
            DropX = new double[slots];
            DropZ = new double[slots];
            DropUnits = new int[slots];
            DropTtl = new int[slots];
            Reset(w);
        }

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w)
        {
            VeinsReady = false;
            PreFed = 0;
            PostPool = 0;
            RollIndex = 0;
            Successes = 0;
            Wild = false;
            LastRollTick = -1;
            LastChance = 0;
            LastU = 0;
            LastSuccess = false;
            LastRollTrailing = -1;
            WildTick = -1;
            WildLeader = -1;
            Mined[0] = Mined[1] = 0;
            Fed[0] = Fed[1] = 0;
            FeedTick = -1;
            FeedActor = -1;
            if (PrevCrystals != null) Array.Clear(PrevCrystals, 0, PrevCrystals.Length);
            if (PurifyHold != null) Array.Clear(PurifyHold, 0, PurifyHold.Length);
            if (DropActive != null)
            {
                Array.Clear(DropActive, 0, DropActive.Length);
                Array.Clear(DropUnits, 0, DropUnits.Length);
                Array.Clear(DropTtl, 0, DropTtl.Length);
            }
        }
    }

    /// <summary>
    /// CM b13 item C: crystals, the pool, the sqrt chance, the roll and the wild Kaiju (numbers in CorruptionConfig).
    /// With the rule off (Classic, PhysicsOnly, or Depth.Corruption.On false) every entry point is an identity, so today's
    /// corruption deliveries and the instability berserk line work exactly as before.
    /// </summary>
    public static class Corruption
    {
        /// <summary>The rule is on (Full profile only).</summary>
        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Corruption.On);

        private const double TicksPerSecondRef = 30.0;

        // ---------------------------------------------------------------- pure helpers (tests call these)

        /// <summary>Chance = min(Cap, sqrt(pool / Full)); 0 for a pool of 0 or less. Never 1 (Cap &lt; 1).</summary>
        public static double ChanceOf(double pool, CorruptionConfig c)
        {
            if (!(pool > 0) || !(c.Full > 0)) return 0;
            double p = Math.Sqrt(pool / c.Full);
            double cap = c.Cap < 0.999 ? c.Cap : 0.999;
            return p < cap ? p : cap;
        }

        /// <summary>The roll value for roll rollIndex of a match: SeedUniform(seed x SeedMul + RollMul x (rollIndex + 1)), unchecked int arithmetic.</summary>
        public static double RollU(int seed, int rollIndex, CorruptionConfig c)
        {
            unchecked
            {
                int k = seed * c.SeedMul + c.RollMul * (rollIndex + 1);
                return SimResources.SeedUniform(k);
            }
        }

        /// <summary>The team behind on HillSeconds by TrailingMargin or more (0 azure, 1 crimson), else -1.</summary>
        public static int TrailingTeam(World w)
        {
            double a = w.Match.HillSeconds.Azure;
            double c = w.Match.HillSeconds.Crimson;
            double margin = w.Config.Depth.Corruption.TrailingMargin;
            if (c - a >= margin) return 0;
            if (a - c >= margin) return 1;
            return -1;
        }

        /// <summary>The team leading on HillSeconds (0 azure, 1 crimson), -1 when level.</summary>
        public static int LeadingTeam(World w)
        {
            double a = w.Match.HillSeconds.Azure;
            double c = w.Match.HillSeconds.Crimson;
            return a > c ? 0 : c > a ? 1 : -1;
        }

        /// <summary>The Kaiju went wild through this rule (or is berserk while the rule is on).</summary>
        public static bool Wild(World w)
        {
            if (!On(w)) return false;
            return w.Corrupt.Wild || (w.Match.Hatched && w.Kaiju.State == KaijuState.Berserk);
        }

        /// <summary>Actor id holds crystals (rule on).</summary>
        public static bool Carrying(World w, int id)
        {
            if (!On(w) || id < 0 || id >= w.ActorCount) return false;
            Actor a = w.Actors[id];
            return a.Alive && a.Carry == CarryKind.Corruption && a.CarryAmount > 0;
        }

        /// <summary>Crystals a class carries (the Miner MinerCarry, everyone else CarryAmount).</summary>
        public static int CrystalCap(World w, ClassId cls)
        {
            CorruptionConfig c = w.Config.Depth.Corruption;
            return cls == ClassId.Miner ? c.MinerCarry : c.CarryAmount;
        }

        /// <summary>Gather time per crystal in seconds (GatherTicks, the Miner MinerGatherTicks, at the 30 Hz reference).</summary>
        public static double GatherSeconds(World w, ClassId cls)
        {
            CorruptionConfig c = w.Config.Depth.Corruption;
            return (cls == ClassId.Miner ? c.MinerGatherTicks : c.GatherTicks) / TicksPerSecondRef;
        }

        /// <summary>A vein's regrow period in seconds (VeinRegrowTicks at the 30 Hz reference).</summary>
        public static double VeinRegrowSeconds(World w) => w.Config.Depth.Corruption.VeinRegrowTicks / TicksPerSecondRef;

        /// <summary>The team that owns the hatched Kaiju: 0 azure, 1 crimson, -1 neutral (nobody owns it), -2 wild / egg (nobody may feed).</summary>
        public static int OwnerTeam(World w)
        {
            KaijuState s = w.Kaiju.State;
            if (!w.Match.Hatched || s == KaijuState.Egg || s == KaijuState.Berserk) return -2;
            if (s == KaijuState.Azure) return 0;
            if (s == KaijuState.Crimson) return 1;
            return -1;
        }

        /// <summary>May team teamIdx feed the hatched Kaiju now? Only a team that does not own it, never a wild one.</summary>
        public static bool MayFeedTeam(World w, int teamIdx)
        {
            if (Wild(w)) return false;
            int owner = OwnerTeam(w);
            if (owner == -2) return false;
            return owner != teamIdx;
        }

        /// <summary>The Kaiju's target score bias for the Berserk branch: WildLeaderBias while wild under this rule, else the given one (K's BigFeederBias).</summary>
        public static double TargetBias(World w, double normal) => Wild(w) ? w.Config.Depth.Corruption.WildLeaderBias : normal;

        /// <summary>A wild Kaiju, standing (not knocked out), inside the hill radius: the hill counts for neither team.</summary>
        public static bool WildHoldsHill(World w)
        {
            if (!Wild(w)) return false;
            Kaiju k = w.Kaiju;
            if (k.KoIn > 0) return false;
            double r = w.Config.Hill.Radius;
            double dx = k.X - w.Map.Hill.X;
            double dz = k.Z - w.Map.Hill.Z;
            return dx * dx + dz * dz < r * r;
        }

        // ---------------------------------------------------------------- scaffold API

        /// <summary>The Kaiju state at the hatch given the resolved one: rolls when the pool is over 0; Berserk on success.</summary>
        public static KaijuState AtHatch(World w, KaijuState resolved)
        {
            if (!On(w)) return resolved;
            CorruptionState s = w.Corrupt;
            double pool = PrePool(w);
            s.PreFed = 0;
            if (pool > 0 && Roll(w, pool, w.Map.Shrine.X, w.Map.Shrine.Z))
            {
                s.Wild = true;
                return KaijuState.Berserk;
            }
            return resolved;
        }

        /// <summary>The tick a knock-out ends: rolls when the pool is over 0 and the Kaiju is not already wild; the pool is spent either way.</summary>
        public static void AtGetUp(World w)
        {
            if (!On(w)) return;
            CorruptionState s = w.Corrupt;
            double pool = s.PostPool;
            s.PostPool = 0;
            if (Wild(w) || !(pool > 0)) return;
            Kaiju k = w.Kaiju;
            if (Roll(w, pool, k.X, k.Z))
            {
                s.Wild = true;
                k.State = KaijuState.Berserk;
                k.TargetId = -1;
                k.MeleeTargetId = -1;
                w.KaijuNav.RetargetIn = 0;
            }
        }

        /// <summary>The team a wild Kaiju is biased toward: the hill-seconds LEADER (-1 level); the bigger feeder otherwise.</summary>
        public static int WildTargetTeam(World w, int bigFeeder)
        {
            if (!Wild(w)) return bigFeeder;
            // b13 r3: hysteresis. The Kaiju keeps after the team it picked until the other team leads by WildSwitchMargin; a
            // pure "current leader" flipped its target at every near-tie and with it the hill lead (round 1: 92 vs 67).
            CorruptionState s = w.Corrupt;
            double d = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
            double margin = w.Config.Depth.Corruption.WildSwitchMargin;
            if (s.WildLeader < 0) s.WildLeader = LeadingTeam(w);
            else if (s.WildLeader == 0 && -d >= margin) s.WildLeader = 1;
            else if (s.WildLeader == 1 && d >= margin) s.WildLeader = 0;
            return s.WildLeader;
        }

        /// <summary>Crystals in the pool since the last roll (weighted by TrailingBonus; before the hatch net of cleanse and purify).</summary>
        public static double Pool(World w)
        {
            if (!On(w)) return 0;
            if (!w.Match.Hatched) return PrePool(w);
            return w.Corrupt.PostPool;
        }

        /// <summary>The chance the next roll succeeds, 0..Cap.</summary>
        public static double Chance(World w) => On(w) ? ChanceOf(Pool(w), w.Config.Depth.Corruption) : 0;

        /// <summary>The tick of the next roll: the hatch tick before the hatch, the get-up tick while knocked out, else -1.</summary>
        public static int NextRollTick(World w)
        {
            if (!On(w) || w.Match.Phase == MatchPhase.Ended) return -1;
            GameConfig c = w.Config;
            if (!w.Match.Hatched) return (int)Math.Ceiling(c.HatchSeconds * c.SimHz - 1e-6) + 1;
            if (Wild(w)) return -1;
            Kaiju k = w.Kaiju;
            if (k.KoIn > 0) return w.Tick + (int)Math.Ceiling(k.KoIn * c.SimHz - 1e-6);
            return -1;
        }

        /// <summary>The carrier's speed multiplier for actor id (CarrierSpeed while it holds crystals, else 1).</summary>
        public static double SpeedScale(World w, int id)
        {
            if (!Carrying(w, id)) return 1.0;
            return w.Config.Depth.Corruption.CarrierSpeed;
        }

        /// <summary>Per tick, after SimResources.ResourcesSystem: knock-out drops, loose crystals, the owner Healer's purify.</summary>
        public static void CorruptionSystem(World w, double dt)
        {
            if (!On(w) || w.Match.Phase == MatchPhase.Ended) return;
            CorruptionState s = w.Corrupt;
            CorruptionConfig c = w.Config.Depth.Corruption;
            int n = w.ActorCount;

            // Knock-out: crystals carried at the end of the last pass and a dead body now -> a loose crystal where it fell.
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive && s.PrevCrystals[id] > 0) SpawnDrop(w, w.X[id], w.Z[id], s.PrevCrystals[id]);
            }

            // Loose crystals: age, then walk-over pick-up by a hands-free MayCorrupt actor (actor-id order, slot order).
            int live = 0;
            for (int i = 0; i < s.DropActive.Length; i++)
            {
                if (!s.DropActive[i]) continue;
                s.DropTtl[i]--;
                if (s.DropTtl[i] <= 0 || s.DropUnits[i] <= 0)
                {
                    s.DropActive[i] = false;
                    s.DropUnits[i] = 0;
                    s.DropTtl[i] = 0;
                    continue;
                }
                live++;
            }
            if (live > 0)
            {
                double r2 = c.PickupRange * c.PickupRange;
                for (int id = 0; id < n; id++)
                {
                    Actor a = w.Actors[id];
                    if (!a.Alive || a.StandUp > 0 || a.AirLen > 0 || a.Carry != CarryKind.None || a.CarryAmount > 0 || a.CarryMeat > 0) continue;
                    if (!Roles.MayCorrupt(w, a.Cls)) continue;
                    for (int i = 0; i < s.DropActive.Length; i++)
                    {
                        if (!s.DropActive[i]) continue;
                        double dx = s.DropX[i] - w.X[id];
                        double dz = s.DropZ[i] - w.Z[id];
                        if (dx * dx + dz * dz > r2) continue;
                        int room = CrystalCap(w, a.Cls) - a.CarryAmount;
                        int take = s.DropUnits[i] < room ? s.DropUnits[i] : room;
                        if (take <= 0) break;
                        a.Carry = CarryKind.Corruption;
                        a.CarryAmount += take;
                        s.DropUnits[i] -= take;
                        if (s.DropUnits[i] <= 0)
                        {
                            s.DropActive[i] = false;
                            s.DropTtl[i] = 0;
                        }
                        a.GatherProgress = 0;
                        a.GatherNodeId = -1;
                        if (a.CarryAmount >= CrystalCap(w, a.Cls)) break;
                    }
                }
            }

            // After the hatch: the OWNER team's Healer holding E near the downed Kaiju purifies the pool.
            Kaiju k = w.Kaiju;
            int owner = OwnerTeam(w);
            bool down = w.Match.Hatched && k.KoIn > 0 && owner >= 0 && s.PostPool > 0;
            double pr = w.Config.Kaiju.Radius + c.PurifyRange;
            int purifyTicks = Math.Max(1, Durations.T30(w.Config, c.PurifyTicks));
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                bool ok = down && a.Alive && a.StandUp <= 0 && a.Cls == ClassId.Healer && TeamIdx(a.Team) == owner && w.Inputs[id].Interact;
                if (ok)
                {
                    double dx = w.X[id] - k.X;
                    double dz = w.Z[id] - k.Z;
                    ok = dx * dx + dz * dz <= pr * pr;
                }
                if (!ok)
                {
                    s.PurifyHold[id] = 0;
                    continue;
                }
                s.PurifyHold[id]++;
                if (s.PurifyHold[id] >= purifyTicks && s.PostPool > 0)
                {
                    s.PurifyHold[id] = 0;
                    s.PostPool = s.PostPool > 1 ? s.PostPool - 1 : 0;
                    World.PushEvent(w, SimEventType.Purify, id, owner, k.X, k.Z, 1);
                }
            }

            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                s.PrevCrystals[id] = a.Alive && a.Carry == CarryKind.Corruption ? a.CarryAmount : 0;
            }
        }

        // ---------------------------------------------------------------- called by SimResources (C's file)

        /// <summary>First ResourcesSystem pass with the rule on: every corruption node becomes a crystal vein of VeinCharges.</summary>
        public static void EnsureVeins(World w)
        {
            CorruptionState s = w.Corrupt;
            if (s.VeinsReady) return;
            s.VeinsReady = true;
            int charges = Math.Max(1, w.Config.Depth.Corruption.VeinCharges);
            ResourceNode[] nodes = w.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                ResourceNode nd = nodes[i];
                if (nd.Kind != NodeKind.Corruption) continue;
                nd.MaxCharges = charges;
                if (nd.Charges > charges) nd.Charges = charges;
                nd.RegrowIn = 0;
            }
        }

        /// <summary>A crystal was gathered at node ni by actor id (one CrystalMined event).</summary>
        public static void OnMined(World w, int id, int ni)
        {
            Actor a = w.Actors[id];
            w.Corrupt.Mined[TeamIdx(a.Team)]++;
            ResourceNode nd = w.Nodes[ni];
            World.PushEvent(w, SimEventType.CrystalMined, id, ni, nd.X, nd.Z, a.CarryAmount);
        }

        /// <summary>Crystals delivered to the egg (SimResources.ApplyDelivery while the rule is on): no allegiance, no instability, into the pool.</summary>
        public static void OnEggFeed(World w, TeamId team, double units)
        {
            CorruptionState s = w.Corrupt;
            int t = TeamIdx(team);
            s.PreFed += units * Weight(w, t);
            s.Fed[t] += (int)units;
        }

        /// <summary>Actor id carries crystals after the hatch and is within feed reach of the Kaiju (standing or down).</summary>
        public static bool InFeedReach(World w, int id)
        {
            Kaiju k = w.Kaiju;
            double r = w.Config.Kaiju.Radius + w.Config.Depth.Corruption.FeedRange;
            double dx = w.X[id] - k.X;
            double dz = w.Z[id] - k.Z;
            return dx * dx + dz * dz <= r * r;
        }

        /// <summary>
        /// One E-held pass of a crystal carrier within feed reach after the hatch (SimResources). The wind-up is Actor.DeliverTicks,
        /// so an enemy hit interrupts it through Delivery.InterruptDelivery (Damage.ApplyDamage) and the next pass only re-arms.
        /// Refused (wind-up cleared) while the Kaiju stands or when the carrier's team may not feed it. True when the crystals went in.
        /// </summary>
        public static bool StepFeed(World w, int id)
        {
            Actor a = w.Actors[id];
            Kaiju k = w.Kaiju;
            int t = TeamIdx(a.Team);
            if (!(k.KoIn > 0) || !MayFeedTeam(w, t))
            {
                Delivery.CancelDelivery(a);
                return false;
            }
            if (a.DeliverTicks < 0)
            {
                a.DeliverTicks = 0;
                return false;
            }
            CorruptionState s = w.Corrupt;
            a.DeliverTicks++;
            s.FeedTick = w.Tick;
            s.FeedActor = id;
            if (a.DeliverTicks < Math.Max(1, Durations.T30(w.Config, w.Config.Depth.Corruption.FeedTicks))) return false;
            Delivery.CancelDelivery(a);
            int units = a.CarryAmount;
            s.PostPool += units * Weight(w, t);
            s.Fed[t] += units;
            a.Carry = CarryKind.None;
            a.CarryAmount = 0;
            return true;
        }

        // ---------------------------------------------------------------- internals

        private static int TeamIdx(TeamId t) => t == TeamId.Azure ? 0 : 1;

        /// <summary>TrailingBonus for a crystal fed by the trailing team, else 1.</summary>
        private static double Weight(World w, int teamIdx) => TrailingTeam(w) == teamIdx ? w.Config.Depth.Corruption.TrailingBonus : 1.0;

        /// <summary>The pre-hatch pool: weighted crystals fed minus units cleansed at the shrine and purified by Healers (never negative).</summary>
        private static double PrePool(World w)
        {
            double removed = w.Cleanse.Count * w.Config.KaijuFeed.Cleanse.Units + w.Healer.PurifiedUnits;
            double p = w.Corrupt.PreFed - removed;
            return p > 0 ? p : 0;
        }

        /// <summary>One roll on the pool: u from the seeded hash, success when u &lt; chance; one CorruptionRoll event (A = roll index, B = 1 / 0, Value = chance).</summary>
        private static bool Roll(World w, double pool, double x, double z)
        {
            CorruptionState s = w.Corrupt;
            CorruptionConfig c = w.Config.Depth.Corruption;
            double p = ChanceOf(pool, c);
            int idx = s.RollIndex++;
            double u = RollU(w.Opts.Seed, idx, c);
            bool ok = u < p;
            s.LastRollTick = w.Tick;
            s.LastChance = p;
            s.LastU = u;
            s.LastSuccess = ok;
            s.LastRollTrailing = TrailingTeam(w);
            if (ok)
            {
                s.Successes++;
                s.WildTick = w.Tick;
                s.WildLeader = LeadingTeam(w);
            }
            World.PushEvent(w, SimEventType.CorruptionRoll, idx, ok ? 1 : 0, x, z, p);
            return ok;
        }

        private static void SpawnDrop(World w, double x, double z, int units)
        {
            CorruptionState s = w.Corrupt;
            int slot = -1;
            int oldest = int.MaxValue;
            for (int i = 0; i < s.DropActive.Length; i++)
            {
                if (!s.DropActive[i])
                {
                    slot = i;
                    break;
                }
                if (s.DropTtl[i] < oldest)
                {
                    oldest = s.DropTtl[i];
                    slot = i;
                }
            }
            if (slot < 0) return;
            s.DropActive[slot] = true;
            s.DropX[slot] = x;
            s.DropZ[slot] = z;
            s.DropUnits[slot] = units;
            s.DropTtl[slot] = Math.Max(1, Durations.T30(w.Config, w.Config.Depth.Corruption.DropTicks));
        }
    }
}

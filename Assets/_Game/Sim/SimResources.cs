using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>Resources (CM-08): node regrow, gathering at biomass / corruption nodes, and delivery at the Kaiju shrine (browser src/sim/resources.ts, 09e0904).</summary>
    public static class SimResources
    {
        /// <summary>The delivery math, shared by ResourcesSystem and the debug feed (CM-10 DebugAddFeed); does nothing after the hatch or for a non-positive unit count.</summary>
        /// <remarks>
        /// CM-35: biomass pays by type (Plant BiomassAllegiancePerUnit, Meat MeatAllegiancePerUnit), tallies Delivered[team].Plant /
        /// .Meat beside .Biomass, and tags the DeliverBiomass event with its type. CM-85 (D-068): every biomass unit, plant or
        /// meat, also adds KaijuFeed.BiomassInstabilityPerUnit instability (corruption from feeding, slower).
        /// </remarks>
        public static void ApplyDelivery(World w, TeamId team, CarryKind kind, double units, int actorId = -1, BiomassType bio = BiomassType.Plant)
        {
            if (kind != CarryKind.Biomass && kind != CarryKind.Corruption)
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "ApplyDelivery takes Biomass or Corruption.");
            MatchState m = w.Match;
            if (!(units > 0) || !double.IsFinite(units)) return;
            if (m.Hatched || w.Kaiju.State != KaijuState.Egg) return;
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            ShrineDef sh = w.Map.Shrine;
            int teamIdx = team == TeamId.Azure ? 0 : 1;
            if (kind == CarryKind.Biomass)
            {
                bool meat = bio == BiomassType.Meat;
                m.Allegiance[team] += units * (meat ? feed.MeatAllegiancePerUnit : feed.BiomassAllegiancePerUnit);
                m.Delivered[(int)team].Biomass += units;
                if (meat) m.Delivered[(int)team].Meat += units;
                else m.Delivered[(int)team].Plant += units;
                m.Instability += units * feed.BiomassInstabilityPerUnit;
                World.PushEvent(w, SimEventType.DeliverBiomass, actorId, teamIdx, sh.X, sh.Z, units, meat ? BiomassType.Meat : BiomassType.Plant, DamageCause.None);
            }
            else
            {
                m.Allegiance[team] += units * feed.CorruptionAllegiancePerUnit;
                m.Instability += units * feed.CorruptionInstabilityPerUnit;
                m.Delivered[(int)team].Corruption += units;
                World.PushEvent(w, SimEventType.DeliverCorruption, actorId, teamIdx, sh.X, sh.Z, units);
            }
            if (m.Instability >= feed.BerserkThreshold) LockBerserk(w);
        }

        /// <summary>CM-28: set the berserk lock and raise the one BerserkLocked event of the match; false (and nothing pushed) when the lock was already set.</summary>
        public static bool LockBerserk(World w)
        {
            MatchState m = w.Match;
            if (m.BerserkLocked) return false;
            m.BerserkLocked = true;
            ShrineDef sh = w.Map.Shrine;
            World.PushEvent(w, SimEventType.BerserkLocked, -1, -1, sh.X, sh.Z, m.Instability);
            return true;
        }

        /// <summary>Regrow one charge of a node per regrow period while it is below its max charges.</summary>
        private static void Regrow(GameConfig config, ResourceNode node, double dt)
        {
            if (node.Charges >= node.MaxCharges)
            {
                node.RegrowIn = 0;
                return;
            }
            double period =
                node.Kind == NodeKind.Biomass ? config.Resources.BiomassRegrowSeconds : config.Resources.CorruptionRegrowSeconds;
            if (node.RegrowIn <= 0) node.RegrowIn = period;
            node.RegrowIn = Combat.TickDown(node.RegrowIn, dt);
            if (node.RegrowIn <= 0)
            {
                node.Charges++;
                node.RegrowIn = node.Charges < node.MaxCharges ? period : 0;
            }
        }

        /// <summary>The cargo kind a node of this kind gives (browser carry and node kind share the strings "biomass" / "corruption").</summary>
        private static CarryKind CarryOf(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Biomass: return CarryKind.Biomass;
                case NodeKind.Corruption: return CarryKind.Corruption;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>Index of the node actor id can gather from right now, or -1. Prefers the node it is already working.</summary>
        private static int GatherNode(World w, int id)
        {
            Actor a = w.Actors[id];
            double cap = w.Config.Class(a.Cls).CarryCapacity;
            if (a.CarryAmount >= cap) return -1;
            double reach = w.Config.Resources.NodeInteractRange + Collide.ActorRadius(w, id);
            double r2 = reach * reach;
            double x = w.X[id];
            double z = w.Z[id];
            int best = -1;
            double bestD2 = double.PositiveInfinity;
            ResourceNode[] nodes = w.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                ResourceNode n = nodes[i];
                if (n.Charges <= 0) continue;
                if (a.Carry != CarryKind.None && a.Carry != CarryOf(n.Kind)) continue;
                double dx = n.X - x;
                double dz = n.Z - z;
                double d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                if (i == a.GatherNodeId) return i;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>CM-35: age every loose orb by one sim tick; an orb at ttl 0 (or emptied) goes inactive. Returns how many are left.</summary>
        private static int AgeOrbs(World w)
        {
            LooseOrb[] orbs = w.Orbs;
            int live = 0;
            for (int i = 0; i < orbs.Length; i++)
            {
                LooseOrb o = orbs[i];
                if (!o.Active) continue;
                o.Ttl -= 1;
                if (o.Ttl <= 0 || o.Units <= 0)
                {
                    o.Active = false;
                    o.Units = 0;
                    o.Ttl = 0;
                    continue;
                }
                live++;
            }
            return live;
        }

        /// <summary>
        /// CM-35: walk-over meat pickup for actor id (alive, not getting up, egg). Takes as many units as fit under
        /// Resources.MeatCarryCapacity from every orb in reach, in slot order (deterministic), into Actor.CarryMeat only.
        /// A corruption carrier takes none. True when anything was picked up.
        /// </summary>
        private static bool PickUpMeat(World w, int id)
        {
            Actor a = w.Actors[id];
            if (a.Carry == CarryKind.Corruption) return false;
            int cap = (int)w.Config.Resources.MeatCarryCapacity;
            if (a.CarryMeat >= cap) return false;
            double reach = w.Config.Resources.MeatPickupRange + Collide.ActorRadius(w, id);
            double r2 = reach * reach;
            double x = w.X[id];
            double z = w.Z[id];
            LooseOrb[] orbs = w.Orbs;
            bool took = false;
            for (int i = 0; i < orbs.Length && a.CarryMeat < cap; i++)
            {
                LooseOrb o = orbs[i];
                if (!o.Active || o.Units <= 0) continue;
                double dx = o.X - x;
                double dz = o.Z - z;
                if (dx * dx + dz * dz > r2) continue;
                int take = Math.Min(o.Units, cap - a.CarryMeat);
                o.Units -= take;
                if (o.Units <= 0)
                {
                    o.Active = false;
                    o.Units = 0;
                    o.Ttl = 0;
                }
                a.CarryMeat += take;
                took = true;
            }
            return took;
        }

        /// <summary>Regrow every node, then per actor: deliver at the shrine first, else gather at the nearest valid node.</summary>
        public static void ResourcesSystem(World w, double dt)
        {
            ResourceNode[] nodes = w.Nodes;
            for (int i = 0; i < nodes.Length; i++) Regrow(w.Config, nodes[i], dt);
            int liveOrbs = AgeOrbs(w);

            int n = w.ActorCount;
            ShrineDef shrine = w.Map.Shrine;
            double shrineR = w.Config.KaijuFeed.ShrineInteractRange;
            bool egg = w.Kaiju.State == KaijuState.Egg && !w.Match.Hatched;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                // CM-35: Damage empties the cargo at death and the meat load goes with it; after the hatch meat feeds nothing.
                if (!a.Alive || !egg) a.CarryMeat = 0;
                if (liveOrbs > 0 && egg && a.Alive && a.MeatPickup && a.StandUp <= 0) PickUpMeat(w, id);
                if (!a.Alive || a.StandUp > 0 || !w.Inputs[id].Interact)
                {
                    a.GatherProgress = 0;
                    a.GatherNodeId = -1;
                    Delivery.CancelDelivery(a);
                    continue;
                }

                // Delivery first: an actor at the shrine with cargo or meat hands it all in (CM-35: meat rides with any load).
                if ((a.CarryAmount > 0 || a.CarryMeat > 0) && egg)
                {
                    double dx = w.X[id] - shrine.X;
                    double dz = w.Z[id] - shrine.Z;
                    if (dx * dx + dz * dz <= shrineR * shrineR)
                    {
                        a.GatherProgress = 0;
                        a.GatherNodeId = -1;
                        // CM-64: the wind-up; the load goes in only on the pass it completes.
                        if (!Delivery.StepDeliveryWindup(w, a)) continue;
                        Delivery.CancelDelivery(a);
                        if (a.CarryAmount > 0)
                        {
                            if (a.Carry == CarryKind.Corruption) ApplyDelivery(w, a.Team, CarryKind.Corruption, a.CarryAmount, id);
                            else ApplyDelivery(w, a.Team, CarryKind.Biomass, a.CarryAmount, id, BiomassType.Plant);
                        }
                        if (a.CarryMeat > 0) ApplyDelivery(w, a.Team, CarryKind.Biomass, a.CarryMeat, id, BiomassType.Meat);
                        a.Carry = CarryKind.None;
                        a.CarryAmount = 0;
                        a.CarryMeat = 0;
                        continue;
                    }
                }
                Delivery.CancelDelivery(a);

                int ni = GatherNode(w, id);
                if (ni < 0)
                {
                    a.GatherProgress = 0;
                    a.GatherNodeId = -1;
                    continue;
                }
                if (ni != a.GatherNodeId)
                {
                    a.GatherProgress = 0;
                    a.GatherNodeId = ni;
                }
                a.GatherProgress += dt;
                double need = w.Config.Class(a.Cls).GatherSeconds;
                if (a.GatherProgress >= need - Combat.TimerEps)
                {
                    ResourceNode node = nodes[ni];
                    node.Charges -= 1;
                    a.Carry = CarryOf(node.Kind);
                    a.CarryAmount += 1;
                    a.GatherProgress = 0;
                    World.PushEvent(w, SimEventType.Gather, id, ni, node.X, node.Z, a.CarryAmount);
                }
            }
        }
    }
}

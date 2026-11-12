using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>Resources (CM-08): node regrow, gathering at biomass / corruption nodes, and delivery at the Kaiju shrine (browser src/sim/resources.ts, 09e0904).</summary>
    public static class SimResources
    {
        /// <summary>The delivery math, shared by ResourcesSystem and the debug feed (CM-10 DebugAddFeed); does nothing after the hatch or for a non-positive unit count.</summary>
        public static void ApplyDelivery(World w, TeamId team, CarryKind kind, double units, int actorId = -1)
        {
            // ITEM CM-35: added by its owner (bio parameter: BiomassType bio = plant, after actorId)
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
                // ITEM CM-35: added by its owner (meat = bio == meat; meat units credit MeatAllegiancePerUnit instead)
                m.Allegiance[team] += units * feed.BiomassAllegiancePerUnit;
                m.Delivered[(int)team].Biomass += units;
                // ITEM CM-35: added by its owner (delivered[team][bio] += units: the Meat / Plant tally)
                // ITEM CM-85: added by its owner (biomass instability: m.Instability += units * KaijuFeed.BiomassInstabilityPerUnit)
                World.PushEvent(w, SimEventType.DeliverBiomass, actorId, teamIdx, sh.X, sh.Z, units);
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

        // ITEM CM-35: added by its owner (AgeOrbs: age every loose orb by one sim tick; an orb at ttl 0 or emptied goes inactive; returns how many are left)

        // ITEM CM-35: added by its owner (PickUpMeat: walk-over meat pickup into Actor.CarryMeat up to Resources.MeatCarryCapacity, slot order, no corruption carrier)

        /// <summary>Regrow every node, then per actor: deliver at the shrine first, else gather at the nearest valid node.</summary>
        public static void ResourcesSystem(World w, double dt)
        {
            ResourceNode[] nodes = w.Nodes;
            for (int i = 0; i < nodes.Length; i++) Regrow(w.Config, nodes[i], dt);
            // ITEM CM-35: added by its owner (int liveOrbs = AgeOrbs(w))

            int n = w.ActorCount;
            ShrineDef shrine = w.Map.Shrine;
            double shrineR = w.Config.KaijuFeed.ShrineInteractRange;
            bool egg = w.Kaiju.State == KaijuState.Egg && !w.Match.Hatched;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                // ITEM CM-35: added by its owner (meat-at-death note: if (!a.Alive || !egg) a.CarryMeat = 0)
                // ITEM CM-35: added by its owner (if (liveOrbs > 0 && egg && a.Alive && a.MeatPickup && a.StandUp <= 0) PickUpMeat(w, id))
                if (!a.Alive || a.StandUp > 0 || !w.Inputs[id].Interact)
                {
                    a.GatherProgress = 0;
                    a.GatherNodeId = -1;
                    continue;
                }

                // Delivery first: an actor at the shrine with cargo hands it all in.
                if (a.CarryAmount > 0 && egg) // ITEM CM-35: added by its owner (|| a.CarryMeat > 0: meat rides with any load)
                {
                    double dx = w.X[id] - shrine.X;
                    double dz = w.Z[id] - shrine.Z;
                    if (dx * dx + dz * dz <= shrineR * shrineR)
                    {
                        if (a.CarryAmount > 0)
                        {
                            if (a.Carry == CarryKind.Corruption) ApplyDelivery(w, a.Team, CarryKind.Corruption, a.CarryAmount, id);
                            else ApplyDelivery(w, a.Team, CarryKind.Biomass, a.CarryAmount, id);
                        }
                        // ITEM CM-35: added by its owner (meat delivery: if (a.CarryMeat > 0) ApplyDelivery(w, a.Team, Biomass, a.CarryMeat, id, meat))
                        a.Carry = CarryKind.None;
                        a.CarryAmount = 0;
                        // ITEM CM-35: added by its owner (a.CarryMeat = 0)
                        a.GatherProgress = 0;
                        a.GatherNodeId = -1;
                        continue;
                    }
                }

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

// CM b13 item K (per-team feed plans from the seed). RULE CHANGE (part of KaijuTypes, Full profile only, pre-hatch only).
using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// CM b13 item K: each team's bots follow a FEED PLAN drawn from the seed, so the Kaiju type varies match to match (batch 12
    /// R: bots fed plant every match and 22 of 22 hatched GROVE). plan = floor(SeedUniform(seed x 3 + teamIndex) x 4):
    /// 0 plant (today), 1 meat (plant gather x0.6, so carried meat dominates), 2 wood and 3 water (plant gather x0.8 and a supply
    /// bot holding that kind feeds the egg instead of banking). Scales in KaijuTypesConfig.FeedPlanGatherScale. Live only with
    /// KaijuTypes.On (Full) and before the hatch; otherwise every getter is today's identity.
    /// </summary>
    public static class BotKaijuFeed
    {
        public const int PlanPlant = 0, PlanMeat = 1, PlanWood = 2, PlanWater = 3;

        /// <summary>Pure: a team's feed plan for a match seed (0 plant, 1 meat, 2 wood, 3 water).</summary>
        public static int PlanFor(int seed, TeamId team)
        {
            int teamIndex = team == TeamId.Azure ? 0 : 1;
            int p = (int)Math.Floor(SimResources.SeedUniform(unchecked(seed * 3 + teamIndex)) * 4);
            return p < 0 ? 0 : p > 3 ? 3 : p;
        }

        /// <summary>This match's plan for a team.</summary>
        public static int Plan(World w, TeamId team) => PlanFor(w.Opts.Seed, team);

        private static bool Live(World w) => KaijuTypes.On(w) && !w.Match.Hatched;

        /// <summary>The plant-gather utility multiplier for bot id (1 = today).</summary>
        public static double GatherScale(World w, BotsState st, int id)
        {
            if (!Live(w) || !(id >= 0 && id < w.ActorCount)) return 1.0;
            TeamId team = w.Actors[id].Team;
            int plan = Plan(w, team);
            // A wood or water plan needs a supply runner, and a 5v5 team has none (floor(5 x SupplyFraction 0.125) = 0): the
            // first egg feeder that asks enlists itself (st.Supply, re-asserted each think because the director resets it), so it
            // fetches at the grove or spring and SupplyFeedsEgg sends the planned kind to the egg. Actor-id order, no RNG.
            if ((plan == PlanWood || plan == PlanWater) && st != null && st.Supply != null && st.Supply[id] == 0 && !TeamHasSupply(w, st, team)) st.Supply[id] = 1;
            return w.Config.Depth.KaijuTypes.FeedPlanGatherScale[plan];
        }

        private static bool TeamHasSupply(World w, BotsState st, TeamId team)
        {
            for (int j = 0; j < w.ActorCount && j < st.Supply.Length; j++)
                if (st.Supply[j] != 0 && w.Actors[j].Team == team && w.Actors[j].Alive) return true;
            return false;
        }

        /// <summary>True when supply bot id holding kind feeds the egg instead of banking (false = today).</summary>
        public static bool SupplyFeedsEgg(World w, BotsState st, int id, BiomassType kind)
        {
            if (!Live(w) || !(id >= 0 && id < w.ActorCount)) return false;
            int plan = Plan(w, w.Actors[id].Team);
            return (plan == PlanWood && kind == BiomassType.Wood) || (plan == PlanWater && kind == BiomassType.Water);
        }

        /// <summary>b13 r3: how far outside the SHROUD's edge (u) a bot already stops stepping inward.</summary>
        public const double CloudMargin = 1.0;
        /// <summary>b13 r3: a bot this close to the hill (radius plus this, u) is contesting it and may stay in a cloud.</summary>
        public const double ContestMargin = 0.5;

        /// <summary>
        /// b13 r3 [K]: a bot the SHROUD would blind (KaijuTypes.Affected) leaves the cloud and does not walk into it, unless it is
        /// contesting the hill (standing within the hill radius plus ContestMargin). Inside the cloud its step points straight
        /// out from the cloud's centre (the Kaiju); within CloudMargin of the edge the inward part of its step is dropped, so it
        /// slides round the rim instead of turning back and forth on it. Rewrites o.MoveX / o.MoveZ; true = it steered. Full
        /// profile only (KaijuTypes.On), no allocation, no RNG.
        /// </summary>
        public static bool AvoidCloud(World w, int id, InputFrame o)
        {
            if (!KaijuTypes.On(w) || !(id >= 0 && id < w.ActorCount)) return false;
            if (!KaijuTypes.CloudActive(w, out double cx, out double cz, out double r)) return false;
            if (!w.Actors[id].Alive || !KaijuTypes.Affected(w, id)) return false;
            double x = w.X[id], z = w.Z[id];
            HillDef hill = w.Map.Hill;
            double hx = x - hill.X, hz = z - hill.Z, hr = hill.R + ContestMargin;
            if (hx * hx + hz * hz <= hr * hr) return false;   // contesting the hill: it holds its ground in the cloud
            double dx = x - cx, dz = z - cz, d2 = dx * dx + dz * dz, band = r + CloudMargin;
            if (d2 > band * band) return false;
            double d = Math.Sqrt(d2);
            double ux = d > 1e-6 ? dx / d : ((id & 1) == 0 ? 1 : -1), uz = d > 1e-6 ? dz / d : 0;
            if (d < r)
            {
                o.MoveX = ux;
                o.MoveZ = uz;
                return true;
            }
            double mx = o.MoveX, mz = o.MoveZ, inward = -(mx * ux + mz * uz);
            if (!(inward > 0)) return false;   // already going out or round
            mx += inward * ux;
            mz += inward * uz;
            double l = Math.Sqrt(mx * mx + mz * mz);
            if (l < 1e-6)
            {
                // straight at the centre: round the rim, the side by id parity
                double side = (id & 1) == 0 ? 1 : -1;
                mx = -uz * side;
                mz = ux * side;
                l = 1;
            }
            o.MoveX = mx / l;
            o.MoveZ = mz / l;
            return true;
        }

        /// <summary>"plant" / "meat" / "wood" / "water", for logs.</summary>
        public static string PlanName(int plan) => plan == PlanMeat ? "meat" : plan == PlanWood ? "wood" : plan == PlanWater ? "water" : "plant";
    }
}

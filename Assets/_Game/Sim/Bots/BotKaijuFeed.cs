// CM b13 item K (per-team feed plans from the seed). RULE CHANGE (part of KaijuTypes, Full profile only, pre-hatch only).
using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

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
            return w.Config.Depth.KaijuTypes.FeedPlanGatherScale[Plan(w, w.Actors[id].Team)];
        }

        /// <summary>True when supply bot id holding kind feeds the egg instead of banking (false = today).</summary>
        public static bool SupplyFeedsEgg(World w, BotsState st, int id, BiomassType kind)
        {
            if (!Live(w) || !(id >= 0 && id < w.ActorCount)) return false;
            int plan = Plan(w, w.Actors[id].Team);
            return (plan == PlanWood && kind == BiomassType.Wood) || (plan == PlanWater && kind == BiomassType.Water);
        }

        /// <summary>"plant" / "meat" / "wood" / "water", for logs.</summary>
        public static string PlanName(int plan) => plan == PlanMeat ? "meat" : plan == PlanWood ? "wood" : plan == PlanWater ? "water" : "plant";
    }
}

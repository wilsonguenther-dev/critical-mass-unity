using CriticalMass.Sim.Config;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>What a step waits for. Every check reads sim state only (TrialChecks); none of them writes it.</summary>
    public enum TrialGoal
    {
        /// <summary>Actor 0 moved at least Param world units from where it stood at the step's start (alive).</summary>
        Move,
        /// <summary>Actor 0 within Param world units of the step's marker.</summary>
        ReachMarker,
        /// <summary>Actor 0's Kills rose above its count at the step's start.</summary>
        KnockOut,
        /// <summary>Actor 0 carries biomass in its class cargo.</summary>
        GatherBiomass,
        /// <summary>Actor 0's team delivered more biomass than at the step's start.</summary>
        DeliverBiomass,
        /// <summary>Actor 0 earned Param more seconds of personal hill time (only counts while its team holds the hill).</summary>
        HoldHill,
        /// <summary>Actor 0's ability cooldown started (it rose since the last observation).</summary>
        UseAbility,
        /// <summary>Actor 0 was seen alive over a gap cell and then alive on walkable ground again (needs jumping).</summary>
        JumpGap,
        /// <summary>A walkable cell within Param of actor 0 became a gap since the step's start (needs dig and place).</summary>
        Dig,
        /// <summary>A gap cell within Param of actor 0 became walkable since the step's start (needs dig and place).</summary>
        Place,
        /// <summary>The full map is open (asked through TrialFeatures.MapOpenProbe; needs the HUD's interact layer).</summary>
        OpenMap,
        /// <summary>Creator acceptance: a knock-down pillar fell (World.Topple) since the step's start.</summary>
        Topple,
        /// <summary>Creator acceptance: actor 0 was seen on a bridge cell, then on ground at least Param units from where it stepped on.</summary>
        CrossBridge,
        /// <summary>Creator acceptance: actor 0 within Param world units of the step's (moving) target.</summary>
        ReachTarget,
        /// <summary>Creator acceptance: a spoken step, done after Param sim seconds (the arrow still points at the target).</summary>
        Timer,
    }

    /// <summary>Where a step's world arrow points.</summary>
    public enum TrialTarget
    {
        /// <summary>A marker on walkable ground, MarkerFraction of the way from the team pad to the hill (fixed at step start).</summary>
        Marker,
        /// <summary>The living enemy with the lowest (distance + health) score, re-picked every few ticks.</summary>
        WeakestEnemy,
        /// <summary>The nearest living enemy.</summary>
        NearestEnemy,
        /// <summary>The nearest biomass node with charges left.</summary>
        BiomassNode,
        /// <summary>The Kaiju shrine (the egg).</summary>
        Shrine,
        /// <summary>The hill centre.</summary>
        Hill,
        /// <summary>The nearest gap in jump reach (fixed at step start).</summary>
        Gap,
        /// <summary>The ground at actor 0's feet.</summary>
        Self,
        /// <summary>Creator acceptance: the standing knock-down pillar nearest the player's pad (any pillar once all fell).</summary>
        Pillar,
        /// <summary>Creator acceptance: the bridge cell nearest actor 0 (fixed at step start).</summary>
        Bridge,
        /// <summary>Creator acceptance: the nearest corruption node with charges left.</summary>
        CorruptionNode,
    }

    /// <summary>A mechanic a step needs from the sim. None = on main today.</summary>
    public enum TrialFeature
    {
        None,
        /// <summary>CM-32 jumping (branch feat/dg-02-jump-bridges).</summary>
        Jump,
        /// <summary>CM-30 dig and place (DG-09, not yet built).</summary>
        Terraform,
        /// <summary>The full map (the HUD's interact layer sets TrialFeatures.MapOpenProbe).</summary>
        Map,
    }

    /// <summary>One step of the guided match. Plain data: the runner and the checks interpret it.</summary>
    public sealed class TrialStep
    {
        /// <summary>Stable id (log lines, words-pack key "trial.&lt;id&gt;", tests).</summary>
        public string Id;
        /// <summary>
        /// The one-line prompt (built-in fallback). {Attack}, {Ability}, {Interact}, {Jump} and {DigPlace} are replaced by
        /// the key or button of that action on the device used last; {Move} by the movement keys (WASD) or the stick.
        /// </summary>
        public string Prompt;
        public TrialGoal Goal;
        public TrialTarget Target;
        /// <summary>Goal parameter: radius (ReachMarker, Dig, Place) or seconds (HoldHill).</summary>
        public double Param;
        /// <summary>Marker target: fraction of the way from the own pad to the hill.</summary>
        public double MarkerFraction;
        /// <summary>The step runs only when the player plays this class (null = every class).</summary>
        public ClassId? OnlyClass;
        /// <summary>Creator acceptance: the step is skipped when the player plays this class (null = never).</summary>
        public ClassId? NotClass;
        public TrialFeature Needs;
    }

    /// <summary>
    /// The default step list of "Trial of the Basin" (DG-12, gap list 5.1 row 7), in play order. Data only: add, remove or
    /// reorder rows here; nothing else changes. Steps whose mechanic the sim does not expose yet stay in the list and switch
    /// themselves off at the trial's start with a log line (TrialRunner.Begin).
    /// </summary>
    public static class TrialSteps
    {
        public static readonly TrialStep[] Default =
        {
            // Step one teaches the movement keys of the device in use and waits until the player has actually moved
            // (the owner's first match: stuck, with no idea of the controls).
            new TrialStep
            {
                Id = "move", Prompt = "{Move} to move. Go on, stretch your legs.", Goal = TrialGoal.Move,
                Target = TrialTarget.Self, Param = 2,
            },
            new TrialStep
            {
                Id = "marker", Prompt = "See the marker? Walk to it. I'll wait.", Goal = TrialGoal.ReachMarker, Target = TrialTarget.Marker,
                Param = 1.6, MarkerFraction = 0.3,
            },
            new TrialStep
            {
                Id = "knockout", Prompt = "That one looks soft. {Attack} until it naps.", Goal = TrialGoal.KnockOut,
                Target = TrialTarget.WeakestEnemy,
            },
            new TrialStep
            {
                Id = "ability", Prompt = "Now your trick: {Ability}. Show off a little.", Goal = TrialGoal.UseAbility,
                Target = TrialTarget.NearestEnemy,
            },
            new TrialStep
            {
                Id = "jump", Prompt = "Mind the gap. {Jump} clears it.", Goal = TrialGoal.JumpGap, Target = TrialTarget.Gap,
                Needs = TrialFeature.Jump,
            },
            new TrialStep
            {
                Id = "dig", Prompt = "Dig in. {DigPlace} scoops up the ground.", Goal = TrialGoal.Dig, Target = TrialTarget.Self,
                Param = 2.5, OnlyClass = ClassId.Miner, Needs = TrialFeature.Terraform,
            },
            new TrialStep
            {
                Id = "place", Prompt = "Now fill the gap. {DigPlace} again.", Goal = TrialGoal.Place, Target = TrialTarget.Gap,
                Param = 2.5, OnlyClass = ClassId.Miner, Needs = TrialFeature.Terraform,
            },
            // Creator acceptance (2026-11-13): the creator pitches terrain manipulation and corruption, so every first match
            // shows both. Non-miners hear that miners reshape the ground; everyone knocks a pillar down.
            new TrialStep
            {
                Id = "terrain", Prompt = "Miners dig the ground up and drop it anywhere. Try one next match.", Goal = TrialGoal.Timer,
                Target = TrialTarget.Self, Param = 5, NotClass = ClassId.Miner,
            },
            new TrialStep
            {
                Id = "topple", Prompt = "{Attack} the stone pillar. It falls away from you: a ramp or a wall.", Goal = TrialGoal.Topple,
                Target = TrialTarget.Pillar,
            },
            // 2026-11-12: the first match teaches move, attack, ability, jump, map and the objective, in that order.
            new TrialStep
            {
                Id = "map", Prompt = "Lost? {Map} opens the full map. The hill, the egg and both bases are on it.", Goal = TrialGoal.OpenMap,
                Target = TrialTarget.Self, Needs = TrialFeature.Map,
            },
            new TrialStep
            {
                Id = "hill", Prompt = "The objective: stand on the hill. Outnumber them there and the clock is yours.", Goal = TrialGoal.HoldHill,
                Target = TrialTarget.Hill, Param = 3,
            },
            new TrialStep
            {
                Id = "gather", Prompt = "Hold {Interact} on the biomass. Snack time.", Goal = TrialGoal.GatherBiomass,
                Target = TrialTarget.BiomassNode,
            },
            new TrialStep
            {
                Id = "deliver", Prompt = "Take it to the egg and hold {Interact}. It remembers who feeds it.", Goal = TrialGoal.DeliverBiomass,
                Target = TrialTarget.Shrine,
            },
            new TrialStep
            {
                Id = "corruption", Prompt = "Violet ore is corruption. Feed the egg enough and it hatches berserk.", Goal = TrialGoal.Timer,
                Target = TrialTarget.CorruptionNode, Param = 6,
            },
        };
    }
}

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CriticalMass.Sim.Config
{
    /// <summary>Player class; member order is the browser's canonical CLASS_IDS order (InputFrame.ClassPick indexes it).</summary>
    public enum ClassId
    {
        Warrior = 0,
        Farmer = 1,
        Miner = 2,
        Demolitions = 3,
        Builder = 4,
    }

    /// <summary>Team; index 0 = Azure (west base), index 1 = Crimson (east base), the browser's TEAM_IDS order.</summary>
    public enum TeamId
    {
        Azure = 0,
        Crimson = 1,
    }

    /// <summary>Canonical class order (browser CLASS_IDS); roster fill and tie-breaks follow it.</summary>
    public static class ClassIds
    {
        /// <summary>The five classes in enum order.</summary>
        public static readonly IReadOnlyList<ClassId> All = new ReadOnlyCollection<ClassId>(new[]
        {
            ClassId.Warrior,
            ClassId.Farmer,
            ClassId.Miner,
            ClassId.Demolitions,
            ClassId.Builder,
        });
    }

    /// <summary>Canonical team order (browser TEAM_IDS).</summary>
    public static class TeamIds
    {
        /// <summary>The two teams in enum order.</summary>
        public static readonly IReadOnlyList<TeamId> All = new ReadOnlyCollection<TeamId>(new[]
        {
            TeamId.Azure,
            TeamId.Crimson,
        });
    }
}

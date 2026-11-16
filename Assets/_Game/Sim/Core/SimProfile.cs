using System;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// fix/frame-cost-1: timing hooks around Sim systems so the player can attribute frame cost (the Sim has no engine
    /// references, so it cannot hold a ProfilerMarker itself). Runtime installs the delegates; null (tests, headless) =
    /// nothing. They read and write no Sim state: the checksum cannot change.
    /// </summary>
    public static class SimProfile
    {
        public const int KaijuHill = 0;
        /// <summary>b11-dg: Kaiju.RefreshClearance when the grid changed ("CM.KaijuClear" in the player, inside CM.KaijuHill).</summary>
        public const int KaijuClearance = 1;
        public static Action<int> Begin, End;

        /// <summary>
        /// b11-dg: Kaiju shadow-grid rebuild counters (whole grid / changed cells only / region labelling), for tests and the
        /// capture. Counted only; nothing in the Sim reads them.
        /// </summary>
        public static int ClearanceFullRebuilds, ClearancePatches, ClearanceRegionBuilds;

        /// <summary>
        /// b11-dg, tests only: true = every clearance refresh is a full rebuild (the path before b11-dg). Both paths give
        /// byte-identical shadow cells, squeeze flags and regions, so the Sim result is the same either way.
        /// </summary>
        public static bool ForceFullClearance;

        /// <summary>Zero the counters (tests; b12-t: also the player at every recorded match start).</summary>
        public static void ResetCounters()
        {
            ClearanceFullRebuilds = 0;
            ClearancePatches = 0;
            ClearanceRegionBuilds = 0;
        }

        /// <summary>
        /// b12-t: Kaiju ownership tally, plain integer counters fed one KaijuState per sim tick by the player's
        /// KaijuOwnershipLog (Runtime) and by the Sim tests. Nothing in the Sim reads or writes it, so no checksum can move.
        /// The owner is Azure or Crimson; Egg, Neutral and Berserk have none. A switch is a change of owner between two
        /// samples (none to Azure, Azure to none, ...), so egg to neutral is not one. Ticks only; seconds = ticks / SimHz.
        /// </summary>
        public struct KaijuOwnTally
        {
            public int EggTicks, NeutralTicks, AzureTicks, CrimsonTicks, BerserkTicks;
            /// <summary>Owner changes between consecutive samples.</summary>
            public int Switches;
            /// <summary>Samples taken (one per tick).</summary>
            public int Samples;
            /// <summary>True once an Azure or Crimson sample was seen; FirstOwnedTick = samples before it.</summary>
            public bool EverOwned;
            public int FirstOwnedTick;
            /// <summary>The longest unbroken run of one owner, in ticks, and that owner's state (Azure or Crimson).</summary>
            public int LongestHoldTicks;
            public KaijuState LongestHoldState;
            int _owner, _run;

            /// <summary>Back to an empty tally.</summary>
            public void Reset() => this = default;

            /// <summary>Count one tick in state s. Allocates nothing.</summary>
            public void Sample(KaijuState s)
            {
                switch (s)
                {
                    case KaijuState.Egg: EggTicks++; break;
                    case KaijuState.Neutral: NeutralTicks++; break;
                    case KaijuState.Azure: AzureTicks++; break;
                    case KaijuState.Crimson: CrimsonTicks++; break;
                    default: BerserkTicks++; break;
                }
                int owner = s == KaijuState.Azure ? 1 : s == KaijuState.Crimson ? 2 : 0;
                if (owner != 0 && !EverOwned)
                {
                    EverOwned = true;
                    FirstOwnedTick = Samples;
                }
                if (Samples > 0 && owner != _owner) Switches++;
                _run = Samples > 0 && owner == _owner ? _run + 1 : 1;
                _owner = owner;
                if (owner != 0 && _run > LongestHoldTicks)
                {
                    LongestHoldTicks = _run;
                    LongestHoldState = s;
                }
                Samples++;
            }

            /// <summary>Ticks spent in one state.</summary>
            public int TicksIn(KaijuState s) =>
                s == KaijuState.Egg ? EggTicks : s == KaijuState.Neutral ? NeutralTicks : s == KaijuState.Azure ? AzureTicks
                : s == KaijuState.Crimson ? CrimsonTicks : BerserkTicks;
        }
    }
}

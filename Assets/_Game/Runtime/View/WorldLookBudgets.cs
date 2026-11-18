using CriticalMass.Runtime.Game;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// b15 world look: every air / atmosphere / frame-cost number of the branch in ONE file (owner rule: one config file,
    /// named in the PR). Per quality level (FramePolicy: 0 Very Low, 1 Low, 2 Medium = the 8 GB M1 Air's first-launch pick,
    /// 3 High and above). Very Low costs nothing new: no post, no particles, MSAA off. The costs are estimates for a
    /// 1080p frame on the M1 Air (tile GPU: MSAA resolves on tile), to be confirmed by Editor/WorldLook "Frame probe" and
    /// a build run; they are stated so the reviewer can check them, not measured here.
    /// </summary>
    public static class WorldLookBudgets
    {
        /// <summary>One quality level's air settings.</summary>
        public readonly struct AirTier
        {
            /// <summary>URP MSAA samples (set on the level's pipeline asset: CriticalMass_URP_Low / _Medium / CriticalMass_URP).</summary>
            public readonly int Msaa;
            /// <summary>The post volume (colour grade per time of day) and its bloom on self-lit glows (HDR levels only).</summary>
            public readonly bool Post, Bloom;
            /// <summary>Ambient particles: hard caps (dust motes, brazier embers, falling leaves). 0 = the system is not built.</summary>
            public readonly int Motes, Embers, Leaves;
            /// <summary>Estimated added GPU ms at 1080p on the M1 Air (MSAA + post + particles).</summary>
            public readonly float CostMs;
            public readonly string Note;

            public AirTier(int msaa, bool post, bool bloom, int motes, int embers, int leaves, float costMs, string note)
            {
                Msaa = msaa; Post = post; Bloom = bloom; Motes = motes; Embers = embers; Leaves = leaves; CostMs = costMs; Note = note;
            }

            public int Particles => Motes + Embers + Leaves;
        }

        public static readonly AirTier VeryLow = new AirTier(1, false, false, 0, 0, 0, 0f, "nothing new: no post, no particles, no MSAA");
        public static readonly AirTier Low = new AirTier(1, false, false, 0, 24, 0, 0.05f, "embers at braziers only (24, one draw)");
        public static readonly AirTier Medium = new AirTier(2, true, false, 48, 48, 16, 0.9f,
            "MSAA 2x at the Air's 0.75 render scale (~0.4 ms), grade-only post (~0.4 ms), 112 particles in 3 draws (~0.1 ms)");
        public static readonly AirTier High = new AirTier(4, true, true, 96, 64, 32, 1.8f,
            "MSAA 4x (~0.8 ms), grade + bloom on glows (~0.8 ms), 192 particles in 3 draws (~0.15 ms)");

        public static AirTier For(int level) =>
            level <= FramePolicy.LevelVeryLow ? VeryLow : level == FramePolicy.LevelLow ? Low : level == FramePolicy.LevelMedium ? Medium : High;

        /// <summary>Bloom: only HDR pixels above this (self-lit glows boosted at night); never the lit world (max 1.0).</summary>
        public const float BloomThreshold = 1.05f, BloomIntensity = 0.35f, BloomScatter = 0.35f;

        /// <summary>Frame budgets (WORLD-AUDIT #7, #8; skill budgets, no bible rule before this branch).</summary>
        public const int SetPassBudget = 150, ShadowCasterBudget = 40;
        /// <summary>The worst single-frame managed allocation allowed from the hatch on (the audit saw 3.5 and 24 MB).</summary>
        public const long HatchFrameAllocBytes = 512 * 1024;
        /// <summary>No frame after the hatch above this (ms, build on the M1 Air).</summary>
        public const float HatchFrameMs = 33f;
        /// <summary>Distinct world materials the dressed world may resolve (WorldView groups by material per chunk, so this
        /// bounds the scenery SetPass count). See WorldMaterialBudgetTests for the measured count.</summary>
        public const int WorldMaterialBudget = 40;
    }
}

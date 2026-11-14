using System;

namespace CriticalMass.Runtime.Game
{
    /// <summary>How a fighter (class model) casts its shadow at a quality level.</summary>
    public enum FighterShadow
    {
        /// <summary>No shadow-map shadow; a flat blob on the ground (FxScene ground layer, no extra draw call).</summary>
        Blob,
        /// <summary>The shadow-only proxy mesh when the model has one (the art LOD contract), else the LOD0 mesh.</summary>
        Proxy,
        /// <summary>The drawn mesh casts (today's behaviour).</summary>
        Mesh,
    }

    /// <summary>
    /// The per-quality cuts of the 2026-11-12 physics audit (runtime seat, findings B1, M4, M8, M10): one row per quality
    /// level, read by RenderBudget (shadow distance, LOD bias), WorldView (prop shadows, through RenderTier), ModelView and
    /// ActorViews (fighter shadows, outline LOD, animation throttle) and FxScene (blob shadows). Pure: EditMode tests cover it.
    /// <para>Scenery outline and scenery shadow casting stay in <see cref="RenderTier.Policy"/> (PR #39); this table adds
    /// what that one did not cover and never repeats it.</para>
    /// </summary>
    public readonly struct QualityRow
    {
        /// <summary>Main-light shadow distance in metres; 0 = no shadow map at this level.</summary>
        public readonly float ShadowDistance;
        /// <summary>Small props (WorldLook.Prop: barrels, crates, braziers, light pools, pebbles) cast shadows.</summary>
        public readonly bool PropShadows;
        public readonly FighterShadow FighterShadows;
        /// <summary>Flat blob shadows under fighters and the Kaiju (only where there is no shadow map).</summary>
        public readonly bool BlobShadows;
        /// <summary>Fighters within this many metres of the camera target draw LOD0, beyond it LOD1 (0 = LOD1 for everyone
        /// except the local player). Contract C4 point 6.</summary>
        public readonly float Lod0Metres;
        /// <summary>The outline hull is drawn on LOD1 too (High only; C4 point 6).</summary>
        public readonly bool Lod1Outline;
        /// <summary>Fighters beyond this many metres from the camera evaluate their pose every AnimFarInterval frames.</summary>
        public readonly float AnimFarMetres;
        public readonly int AnimFarInterval;
        /// <summary>perf-60hz-32p crowd budget: at most this many fighters besides your own draw LOD0 (the nearest to the
        /// camera target within Lod0Metres); the rest draw LOD1. -1 = no budget (distance only).</summary>
        public readonly int Lod0Budget;

        public QualityRow(float shadowDistance, bool propShadows, FighterShadow fighterShadows, bool blobShadows, float lod0Metres,
            bool lod1Outline, float animFarMetres, int animFarInterval, int lod0Budget = -1)
        {
            Lod0Budget = lod0Budget;
            ShadowDistance = shadowDistance;
            PropShadows = propShadows;
            FighterShadows = fighterShadows;
            BlobShadows = blobShadows;
            Lod0Metres = lod0Metres;
            Lod1Outline = lod1Outline;
            AnimFarMetres = animFarMetres;
            AnimFarInterval = animFarInterval;
        }

        public bool ShadowMap => ShadowDistance > 0f;
    }

    public static class QualityTable
    {
        // The camera sees ground from about 7.4 m below to 11.6 m above the player at 23.4 m distance (GameplayCamera:
        // pitch 55, FOV 35), so the farthest visible ground is about 31.5 m from the camera; 35 m covers it with a margin
        // and 30 m covers all but the top strip (seat finding M8; the old value was 60 m on a 112 x 72 map).
        public const float HighShadowMetres = 35f;
        public const float MediumShadowMetres = 30f;
        /// <summary>C4 point 6: LOD0 within 25 u of the camera target at Medium and High.</summary>
        public const float Lod0Metres = 25f;
        /// <summary>DG-108: half-rate poses beyond 15 u (Low: every third frame beyond 12 u).</summary>
        public const float AnimFarMetres = 15f;
        /// <summary>perf-60hz-32p crowd budgets (fighters besides your own at LOD0): with all 32 on screen at the hill, Medium
        /// skins 1 + 10 LOD0 bodies (at most 4,500 triangles each) and 21 LOD1 bodies (1,500) instead of 32 LOD0 ones.</summary>
        public const int MediumLod0Budget = 10, HighLod0Budget = 16;

        /// <summary>The row for a quality level (0 Very Low, 1 Low, 2 Medium, 3 High, 4 Very High, 5 Ultra).</summary>
        public static QualityRow For(int level)
        {
            if (level <= FramePolicy.LevelLow)
                return new QualityRow(0f, false, FighterShadow.Blob, true, 0f, false, 12f, 3);
            if (level == FramePolicy.LevelMedium)
                return new QualityRow(MediumShadowMetres, false, FighterShadow.Proxy, false, Lod0Metres, false, AnimFarMetres, 2, MediumLod0Budget);
            return new QualityRow(HighShadowMetres, false, FighterShadow.Proxy, false, Lod0Metres, true, AnimFarMetres, 2, HighLod0Budget);
        }

        /// <summary>
        /// Environment v4 extra clutter (art landing, 2026-11-12: about 525 ground patches, clumps, fences, torches, trees,
        /// bones, anchors and lanterns, WorldDresser.Plan extraClutter) is drawn at High and above only: the scene was at
        /// 4.36 M triangles against a budget near 2 M on High, so Medium and Low keep the v3 dressing. Pure; tests.
        /// </summary>
        public static bool ExtraClutter(int level) => level >= FramePolicy.LevelHigh;

        /// <summary>The row in force now (RenderTier.Level; the editor and tests stay at High unless they set it).</summary>
        public static QualityRow Current => FrameGovernor.IdleTitle ? ForTitle(RenderTier.Level) : For(RenderTier.Level);

        /// <summary>
        /// perf-60hz-32p: the title screen's backdrop match (nobody plays it, the logo and menu sit over it): every fighter but
        /// the followed one at LOD1 and posed every other frame (every third at Low), so the title stays inside its idle CPU
        /// budget (35% of a core; 59 to 61% measured on the 8 GB Air at batch 6). Pure; tests.
        /// </summary>
        public static QualityRow ForTitle(int level)
        {
            QualityRow r = For(level);
            return new QualityRow(r.ShadowDistance, r.PropShadows, r.FighterShadows, r.BlobShadows, r.Lod0Metres, r.Lod1Outline,
                0f, Math.Max(2, r.AnimFarInterval), 0);
        }
    }

    /// <summary>
    /// Animation culling and throttling (seat finding M4) for one fighter this frame (pure). The local player always
    /// evaluates. A model that became active this frame always evaluates once (its renderer has no visibility yet). An
    /// off-screen model (no renderer visible to any camera, shadows included) holds its pose. A visible model farther than
    /// the row's AnimFarMetres evaluates on every AnimFarInterval-th frame, staggered by actor index so the cost is spread.
    /// </summary>
    public static class AnimLod
    {
        /// <summary>C4 point 6: which LOD a fighter draws (0 or 1) at a distance from the camera target (pure; tests).</summary>
        public static int PickLod(bool self, float distanceToTarget, in QualityRow row)
        {
            if (self) return 0;
            if (row.Lod0Metres <= 0f) return 1;
            return distanceToTarget <= row.Lod0Metres ? 0 : 1;
        }

        /// <summary>perf-60hz-32p: PickLod with the crowd budget. budgetCut = the distance of the Lod0Budget-th nearest other
        /// fighter (CrowdCut); farther ones draw LOD1 even inside Lod0Metres (pure; tests).</summary>
        public static int PickLod(bool self, float distanceToTarget, float budgetCut, in QualityRow row)
        {
            int lod = PickLod(self, distanceToTarget, row);
            return lod == 0 && !self && distanceToTarget > budgetCut ? 1 : lod;
        }

        /// <summary>The crowd budget's cut distance: the budget-th smallest of the first n distances (others than your own),
        /// or +infinity when the budget covers them all or is off. Sorts scratch in place (no allocation; pure; tests).</summary>
        public static float CrowdCut(float[] scratch, int n, int budget)
        {
            if (budget < 0 || n <= budget || scratch == null) return float.PositiveInfinity;
            if (budget == 0) return float.NegativeInfinity;
            Array.Sort(scratch, 0, n);
            return scratch[budget - 1];
        }

        public static bool ShouldEvaluate(bool self, bool forced, bool visible, float distance, int frame, int index, in QualityRow row)
        {
            if (self || forced) return true;
            if (!visible) return false;
            if (row.AnimFarInterval <= 1 || distance <= row.AnimFarMetres) return true;
            int k = (frame + index) % row.AnimFarInterval;
            return (k < 0 ? k + row.AnimFarInterval : k) == 0;
        }
    }
}

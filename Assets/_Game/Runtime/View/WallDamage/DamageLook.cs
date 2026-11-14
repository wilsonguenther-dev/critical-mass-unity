using System;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.WallDamage
{
    /// <summary>What a damaged thing is made of (picks the hit and break sounds, chip colour and rubble).</summary>
    public enum DamageMaterial : byte { Wood, Stone }

    /// <summary>How one breakable wall cell is drawn, from its own Sim cell type only (never its neighbours').</summary>
    public enum CellLook : byte { None, Intact, Cracked, Rubble }

    /// <summary>
    /// wall-damage-1 (owner, 2026-11-13: "When they're hitting the walls, is it cracking? Is it breaking?"): the pure rules
    /// behind the damage look. Stages come from the health share left, piece poses from a fixed seed (the same damage always
    /// looks the same), rubble lives 20 s (ART-DIRECTION: debris persists 20 s, at most 60 pieces). Presentation only.
    /// </summary>
    public static class DamageLook
    {
        /// <summary>Visible stages: 0 intact, 1 cracked, 2 heavily damaged, 3 nearly broken (WorldReactions.StageOf).</summary>
        public static int Stage(double left01) => left01 < 0 ? 0 : WorldReactions.StageOf(left01);

        /// <summary>Per-hit shake length (s) and amplitude (u).</summary>
        public const float ShakeSeconds = 0.12f, ShakeAmp = 0.07f;

        /// <summary>Rubble: stays RubbleSeconds, the last FadeSeconds of it sinking out; never more than MaxRubble pieces.</summary>
        public const float RubbleSeconds = 20f, FadeSeconds = 1.5f;
        public const int MaxRubble = 60;
        /// <summary>Pieces one break throws (barricade planks, wall chunks).</summary>
        public const int MaxPiecesPerBreak = 8;

        /// <summary>The look of breakable cell type t (Sim Cell.*).</summary>
        public static CellLook LookOf(byte t)
        {
            if (t == Cell.BreakableIntact) return CellLook.Intact;
            if (t == Cell.BreakableCracked) return CellLook.Cracked;
            if (t == Cell.BreakableDestroyed) return CellLook.Rubble;
            return CellLook.None;
        }

        /// <summary>True for any of the three breakable types.</summary>
        public static bool IsBreakable(byte t) => t == Cell.BreakableIntact || t == Cell.BreakableCracked || t == Cell.BreakableDestroyed;

        /// <summary>
        /// The dressed world still covers cell i when it was planned as one type and is now another: a breakable stays covered
        /// through every breakable state (its own per-cell piece draws it), so the grey-box view never draws a bare brass box
        /// or a cream floor tile under the wall art. Any other change hands the cell back to the grey-box view.
        /// </summary>
        public static bool DressedStillCovers(byte plannedType, byte currentType) =>
            plannedType == currentType || (IsBreakable(plannedType) && IsBreakable(currentType));

        public static DamageMaterial MaterialOf(ReactKind k) => k == ReactKind.Barricade || k == ReactKind.Piece || k == ReactKind.Bridge ? DamageMaterial.Wood : DamageMaterial.Stone;

        /// <summary>The SoundTable cue for a hit (broke = false) or a break of a material.</summary>
        public static string CueFor(DamageMaterial m, bool broke) =>
            m == DamageMaterial.Wood ? (broke ? "barricade_break" : "barricade_hit") : (broke ? "wall_break" : "wall_crack");

        /// <summary>0..1 hash of a seed (stable across runs and platforms).</summary>
        public static float Hash01(int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u;
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        /// <summary>
        /// Pose of piece k of a damaged object at a stage (seeded): a small tilt (degrees, local X/Z), a slide (u) and whether
        /// it is knocked out (missing). Stage 0 is exactly the authored pose. Missing pieces: none at 1, about a fifth at 2,
        /// about two fifths at 3; never the first piece (so something always stands until the break).
        /// </summary>
        public static void PiecePose(int seed, int k, int stage, out Vector3 tiltDeg, out Vector3 slide, out bool missing)
        {
            tiltDeg = Vector3.zero;
            slide = Vector3.zero;
            missing = false;
            if (stage <= 0) return;
            float a = Hash01(seed + k * 131), b = Hash01(seed + k * 197 + 11), c = Hash01(seed + k * 263 + 29);
            float deg = stage == 1 ? 3f : stage == 2 ? 9f : 16f;
            tiltDeg = new Vector3((a - 0.5f) * 2f * deg, 0f, (b - 0.5f) * 2f * deg);
            float s = stage == 1 ? 0.01f : stage == 2 ? 0.035f : 0.07f;
            slide = new Vector3((b - 0.5f) * 2f * s, -Mathf.Abs(c - 0.5f) * s, (a - 0.5f) * 2f * s);
            float missShare = stage == 2 ? 0.2f : stage >= 3 ? 0.4f : 0f;
            missing = k > 0 && c < missShare;
        }

        /// <summary>Whole-object lean at a stage (degrees): only the last stage leans, about 6 degrees.</summary>
        public static float LeanDeg(int stage) => stage >= 3 ? 6f : stage == 2 ? 1.5f : 0f;

        /// <summary>Shake offset along the hit direction at age t (s): a damped wiggle that is zero after ShakeSeconds.</summary>
        public static float Shake(float t, float strength)
        {
            if (t < 0f || t >= ShakeSeconds) return 0f;
            float u = t / ShakeSeconds;
            return Mathf.Sin(t * 95f) * (1f - u) * (1f - u) * ShakeAmp * (0.6f + 0.4f * Mathf.Clamp01(strength));
        }

        /// <summary>Rubble sink at age t (s): 0 while it rests, 0..1 over the last FadeSeconds; above 1 = gone.</summary>
        public static float RubbleSink(float age)
        {
            float start = RubbleSeconds - FadeSeconds;
            if (age <= start) return 0f;
            return (age - start) / FadeSeconds;
        }

        public static bool RubbleAlive(float age) => age >= 0f && age < RubbleSeconds;

        /// <summary>Crack lines drawn at a stage (cumulative): 0, 4, 9, 16 per piece share (capture-gaps-1: was 0, 3, 7, 12).</summary>
        public static int CrackLines(int stage) => stage <= 0 ? 0 : stage == 1 ? 4 : stage == 2 ? 9 : 16;

        /// <summary>
        /// <summary>
        /// capture-gaps-1 (owner: the crack stage before a wall breaks was too weak to see at gameplay distance): crack stroke
        /// segment length and HALF-width bounds, in world units (a stroke is twice the half-width across: 0.17 to 0.30 u, about
        /// 12 px or more at 1080p square-on from the default gameplay camera, GameplayCamera.Distance 23.4 u; it was 0.08 to
        /// 0.15 u, about 6 px, and was lost in the stone's own shading). A test holds the pixel size.
        /// </summary>
        public const float CrackSegMin = 0.16f, CrackSegMax = 0.5f, CrackWidthMin = 0.085f, CrackWidthMax = 0.15f;

        /// <summary>Resting lean of a cracked wall block (degrees) at the heaviest stage, and at stage 2 (was 7 and 4).</summary>
        public const float CrackedLeanDeg = 10f, DamagedLeanDeg = 5f;

        /// <summary>On-screen size in pixels of a world size seen square-on at a distance with a vertical field of view.</summary>
        public static float PixelsAt(float worldSize, float distance, float fovDeg, int screenH) =>
            worldSize / (2f * distance * Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad)) * screenH;
    }

    /// <summary>Slot picker for a fixed ring of pooled pieces: the free slot, else the oldest (so the cap is never passed).</summary>
    public static class PoolPick
    {
        /// <summary>startTimes[i] = when slot i was spawned (NegativeInfinity = free). Returns the slot to reuse.</summary>
        public static int Pick(double[] startTimes, double now, double life)
        {
            int oldest = 0;
            double oldestT = double.PositiveInfinity;
            for (int i = 0; i < startTimes.Length; i++)
            {
                double t = startTimes[i];
                if (double.IsNegativeInfinity(t) || now - t >= life) return i;
                if (t < oldestT) { oldestT = t; oldest = i; }
            }
            return oldest;
        }
    }
}

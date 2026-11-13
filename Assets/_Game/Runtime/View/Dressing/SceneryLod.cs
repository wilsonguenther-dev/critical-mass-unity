using System;
using CriticalMass.Runtime.Game;
using UnityEngine;

namespace CriticalMass.Runtime.View.Dressing
{
    /// <summary>
    /// Scenery levels of detail (2026-11-12 triangle budget, scenery lane). Pure rules, read by WorldView and the EditMode
    /// tests:
    /// <list type="bullet">
    /// <item>Naming: a kit piece "env_rocks" has lighter files "env_rocks_lod1" (about 45% of the triangles, silhouette
    /// overlap at least 0.95 at the gameplay camera) and "env_rocks_lod2" (about 15%, overlap at least 0.80) beside it,
    /// built by Art/Environment/_source~/build_scenery_lods.py. SceneryLodTable (generated) lists every piece.</item>
    /// <item>Level by quality and by distance: the ground distance from the camera's look point to a chunk's bounds picks
    /// level 0, 1 or 2 (<see cref="Pick"/>); a level a piece has no file for falls back to the next finer one, except that a
    /// piece tiny on screen (FarCull) is not drawn at all at level 2.</item>
    /// <item>Small clutter (grass, flowers, pebbles, stumps, bushes) is thinned by quality (<see cref="ClutterDensity"/>),
    /// deterministically per position so the same tufts stay at every rebuild.</item>
    /// <item>Shadow casting only for large pieces (<see cref="LargeEnoughToCast"/>), on top of the look rule.</item>
    /// </list>
    /// </summary>
    public static class SceneryLod
    {
        public const int Levels = 3;
        public const string Lod1Suffix = "_lod1", Lod2Suffix = "_lod2";

        /// <summary>Metres of hysteresis: a chunk moves to a finer level only this far inside the band edge (no flicker
        /// when the camera rests on a boundary).</summary>
        public const float Hysteresis = 1f;

        /// <summary>A piece casts shadows only with a footprint (largest horizontal size) or height at least this big.</summary>
        public const float CastFootprintMetres = 1.2f, CastHeightMetres = 1.5f;

        /// <summary>The level each chunk's shadow-only proxy is combined from (one draw per chunk in the shadow pass).
        /// Level 1 stays within the 0.95 silhouette overlap, so self-shadowing on the drawn level 0 stays clean; set 0 if
        /// the capture shows shadow speckles on large pieces, 2 for the cheapest shadows.</summary>
        public const int ShadowProxyLevel = 1;

        /// <summary>"env_rocks_lod1" -> "env_rocks"; a name without a level suffix is returned as is.</summary>
        public static string BasePiece(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.EndsWith(Lod1Suffix, StringComparison.Ordinal)) return name.Substring(0, name.Length - Lod1Suffix.Length);
            if (name.EndsWith(Lod2Suffix, StringComparison.Ordinal)) return name.Substring(0, name.Length - Lod2Suffix.Length);
            return name;
        }

        /// <summary>The level of a file name: 0 for the source piece, 1 or 2 for a lighter file.</summary>
        public static int LevelOf(string name) =>
            name == null ? 0 : name.EndsWith(Lod1Suffix, StringComparison.Ordinal) ? 1 : name.EndsWith(Lod2Suffix, StringComparison.Ordinal) ? 2 : 0;

        /// <summary>The file name of a piece at a level ("env_rocks", 2 -> "env_rocks_lod2").</summary>
        public static string LevelPiece(string piece, int level) =>
            level <= 0 ? piece : piece + (level == 1 ? Lod1Suffix : Lod2Suffix);

        /// <summary>
        /// Distance bands per quality level, in metres on the ground from the camera's look point (the gameplay camera
        /// sees about 7.4 m below to 11.6 m above it and about 13 to 17 m to each side): level 1 from lod1From, level 2
        /// from lod2From. Very Low and Low never draw level 0; High never draws level 2.
        /// </summary>
        public static (float lod1From, float lod2From) Bands(int quality)
        {
            if (quality <= FramePolicy.LevelLow) return (0f, 6f);
            if (quality == FramePolicy.LevelMedium) return (6f, 14f);
            return (16f, float.PositiveInfinity);
        }

        /// <summary>The level a chunk at this ground distance draws at this quality (no hysteresis).</summary>
        public static int Pick(int quality, float distance)
        {
            (float l1, float l2) = Bands(quality);
            if (distance >= l2) return 2;
            if (distance >= l1) return 1;
            return 0;
        }

        /// <summary>
        /// Pick with hysteresis: a chunk showing a coarser level than Pick gives keeps it until it is Hysteresis metres
        /// inside the finer band; moving to a coarser level happens at the edge.
        /// </summary>
        public static int Pick(int quality, float distance, int current)
        {
            int want = Pick(quality, distance);
            if (current < 0 || want >= current) return want;
            int inner = Pick(quality, distance + Hysteresis);
            return inner < current ? inner : current;
        }

        /// <summary>The levels WorldView builds at a quality level (the ones Pick can return there).</summary>
        public static bool Builds(int quality, int level)
        {
            (float l1, float l2) = Bands(quality);
            if (level == 0) return l1 > 0f;
            if (level == 1) return l2 > l1;
            return !float.IsPositiveInfinity(l2);
        }

        /// <summary>The level a piece is actually drawn with when its chunk shows "level": the finest file at or below
        /// the level, or -1 (not drawn) for a FarCull piece at level 2.</summary>
        public static int Resolve(string piece, int level, Func<string, bool> hasFile)
        {
            if (level >= 2)
            {
                if (hasFile(LevelPiece(piece, 2))) return 2;
                if (SceneryLodTable.Rows.TryGetValue(piece, out SceneryLodRow row) && row.FarCull) return -1;
            }
            if (level >= 1 && hasFile(LevelPiece(piece, 1))) return 1;
            return 0;
        }

        /// <summary>The small clutter pieces thinned by quality.</summary>
        public static bool IsClutter(string piece) =>
            piece == "env_grass_tuft" || piece == "env_flowers" || piece == "env_pebbles" || piece == "env_stump" || piece == "env_bush";

        /// <summary>Share of small clutter kept per quality level (Very Low, Low, Medium, High and up).</summary>
        public static float ClutterDensity(int quality)
        {
            if (quality <= 0) return 0.25f;
            if (quality <= FramePolicy.LevelLow) return 0.4f;
            if (quality == FramePolicy.LevelMedium) return 0.7f;
            return 1f;
        }

        /// <summary>True when the clutter piece at this position is drawn at this quality (stable per position: a tuft
        /// kept at a lower density is kept at every higher one).</summary>
        public static bool KeepClutter(string piece, double simX, double simZ, int quality)
        {
            if (!IsClutter(piece)) return true;
            float keep = ClutterDensity(quality);
            if (keep >= 1f) return true;
            uint h = WorldDresser.H32((long)Math.Floor(simX * 4.0), (long)Math.Floor(simZ * 4.0), 977);
            return (h & 0xFFFF) / 65536f < keep;
        }

        /// <summary>Large pieces cast shadows (where the look and RenderTier allow); small ones never do. A piece missing
        /// from the table (new art) casts, as before.</summary>
        public static bool LargeEnoughToCast(string piece)
        {
            if (piece == null || !SceneryLodTable.Rows.TryGetValue(BasePiece(piece), out SceneryLodRow row)) return true;
            return row.Footprint >= CastFootprintMetres || row.Height >= CastHeightMetres;
        }

        /// <summary>Ground distance (x and z only) from a point to an axis-aligned box: 0 inside it.</summary>
        public static float GroundDistance(Vector3 point, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(b.min.x - point.x, point.x - b.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(b.min.z - point.z, point.z - b.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Where the camera looks on the ground plane y = groundY (the camera's own position when it looks
        /// level or up).</summary>
        public static Vector3 LookPoint(Vector3 camPos, Vector3 camForward, float groundY = 0f)
        {
            if (camForward.y > -0.01f) return camPos;
            float t = (groundY - camPos.y) / camForward.y;
            return camPos + camForward * t;
        }
    }
}

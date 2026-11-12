using System;

namespace CriticalMass.Runtime.View.Dressing
{
    /// <summary>
    /// The material "looks" of the world art (environment kit v3 and bases kit v1, 2026-11-12). Each look is one
    /// material asset under Resources/Toon/World (made by ProjectSetup.ApplyWorldMaterials) with its own constant-pixel
    /// outline width: 2 px structures, 2.5 px props, 4 px the ledge and rim fringe, none on ground, water or decals.
    /// </summary>
    public enum WorldLook
    {
        Ground, Structure, Fringe, Prop, Flat, Sky, Water, Foliage, Crystal, GlowDecal, ShadowDecal, Base, BaseFlat, Pool,
    }

    /// <summary>Which look each kit piece and FBX material slot takes, and the outline width of each look.</summary>
    public static class WorldKit
    {
        public const string EnvDir = "Assets/_Game/Art/Environment/";
        public const string BasesDir = "Assets/_Game/Art/Bases/";
        public const string MaterialResourceDir = "Toon/World/";
        public const string MaterialAssetDir = "Assets/_Game/Resources/" + MaterialResourceDir;

        public static readonly WorldLook[] AllLooks = (WorldLook[])Enum.GetValues(typeof(WorldLook));

        public static string ResourcePath(WorldLook look) => MaterialResourceDir + "World" + look;
        public static string AssetPath(WorldLook look) => MaterialAssetDir + "World" + look + ".mat";

        public static bool IsEnvironment(string assetPath) =>
            assetPath.StartsWith(EnvDir, StringComparison.Ordinal) && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        public static bool IsBases(string assetPath) =>
            assetPath.StartsWith(BasesDir, StringComparison.Ordinal) && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        /// <summary>Constant screen width at 1080p of each look's inverted hull (0 = no hull).</summary>
        public static float OutlinePx(WorldLook look)
        {
            switch (look)
            {
                case WorldLook.Structure:
                case WorldLook.Base:
                    return 2f;
                case WorldLook.Prop:
                case WorldLook.Foliage:
                case WorldLook.Crystal:
                    return 2.5f;
                case WorldLook.Fringe:
                    return 4f;
                case WorldLook.Sky:
                    return 1f;
                default:
                    return 0f;
            }
        }

        /// <summary>True for looks whose materials swap the team channels (Crimson clones at run time).</summary>
        public static bool TeamSwapped(WorldLook look) =>
            look == WorldLook.Crystal || look == WorldLook.GlowDecal || look == WorldLook.Base || look == WorldLook.BaseFlat;

        /// <summary>True for looks that cast shadows.</summary>
        public static bool CastsShadows(WorldLook look) =>
            look == WorldLook.Structure || look == WorldLook.Prop || look == WorldLook.Foliage || look == WorldLook.Crystal ||
            look == WorldLook.Base || look == WorldLook.Fringe;

        /// <summary>
        /// The look of one environment piece's FBX material slot (kit README "Materials": CM_Toon, CM_Ground, CM_GlowDecal,
        /// CM_ShadowDecal, CM_Toon_Foliage, CM_Toon_Crystal). CM_Toon slots take the piece's outline class.
        /// </summary>
        public static WorldLook EnvLook(string piece, string slot)
        {
            string s = slot ?? "";
            if (s.StartsWith("CM_Ground", StringComparison.Ordinal)) return WorldLook.Ground;
            if (s.StartsWith("CM_GlowDecal", StringComparison.Ordinal)) return WorldLook.GlowDecal;
            if (s.StartsWith("CM_ShadowDecal", StringComparison.Ordinal)) return WorldLook.ShadowDecal;
            if (s.StartsWith("CM_Toon_Foliage", StringComparison.Ordinal)) return WorldLook.Foliage;
            if (s.StartsWith("CM_Toon_Crystal", StringComparison.Ordinal)) return WorldLook.Crystal;
            return EnvToonLook(piece);
        }

        /// <summary>The CM_Toon slot look of an environment piece (its outline class).</summary>
        public static WorldLook EnvToonLook(string piece)
        {
            if (piece.StartsWith("env_lip_", StringComparison.Ordinal)) return WorldLook.Fringe;
            if (piece.StartsWith("env_ledge_", StringComparison.Ordinal) || piece == "env_bridge_cell" || piece == "env_grass_tuft" ||
                piece == "env_flowers" || piece.StartsWith("env_cloud_", StringComparison.Ordinal) || piece == "env_ground_cell" ||
                piece == "env_ground_fill_tri") return WorldLook.Flat;
            if (piece == "env_waterfall") return WorldLook.Water;
            if (piece.StartsWith("env_sky_rock_", StringComparison.Ordinal)) return WorldLook.Sky;
            if (piece == "env_pebbles" || piece == "env_stump" || piece == "env_bush") return WorldLook.Prop;
            return WorldLook.Structure;
        }

        /// <summary>The look of one bases-kit renderer: ground pools are flat decals (no hull), the rest 2 px structures.</summary>
        public static WorldLook BasesLook(string rendererName)
        {
            string n = rendererName ?? "";
            if (n.Contains("light_pool") || n.Contains("egg_pool")) return WorldLook.Pool; // translucent ground light (35%)
            return n.Contains("flagstones") ? WorldLook.BaseFlat : WorldLook.Base;
        }

        /// <summary>Bases kit authored team colours (Azure, sRGB hex; bases_art/pal.py TEAM_SETS) and the swap targets.</summary>
        public const string BaseAuthoredCloth = "#16C7D9", BaseAuthoredDark = "#0E8E9E", BaseAuthoredGlow = "#7FF0FA";
        public const string BaseCrimsonCloth = "#FF5A5F", BaseCrimsonDark = "#B83A44", BaseCrimsonGlow = "#FF7C7C";
        public const string BaseNeutralCloth = "#6F7282", BaseNeutralDark = "#55586A", BaseNeutralGlow = "#C79245";
        /// <summary>Environment kit team channels: authored Azure, Crimson swap (env_art/palette.py TEAM_SWAP); the Crimson
        /// crystal glow decal is #FF7461 (kit README "Materials").</summary>
        public const string EnvAuthoredCloth = "#127EC0", EnvAuthoredGlow = "#4FE3F0", EnvAuthoredDark = "#1D6A8C";
        public const string EnvCrimsonCloth = "#FC725A", EnvCrimsonGlow = "#FF7461", EnvCrimsonDark = "#9B4132";
        public const string CrimsonGlowDecal = "#FF7461";
    }
}

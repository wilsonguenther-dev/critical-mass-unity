using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>What a model is, for import settings, materials and the views.</summary>
    public enum ArtKind { Character, Kaiju, Prop }

    /// <summary>
    /// One team-channel swatch (sRGB hex): the colours CM/Toon's vertex-colour mode swaps to (code 8 cloth, codes 5 and 6
    /// glow, code 4 dark), or, as a model's Authored swatch, the colours the Blender art baked into those channels.
    /// </summary>
    public readonly struct TeamSwatch
    {
        public readonly string Cloth, Glow, Dark;
        public TeamSwatch(string cloth, string glow, string dark) { Cloth = cloth; Glow = glow; Dark = dark; }
    }

    /// <summary>One imported Blender model: where it lives, how it imports, what it must contain, its team colours.</summary>
    public sealed class ModelSpec
    {
        public string Key;
        /// <summary>Project path of the FBX (Assets/_Game/Art/**).</summary>
        public string AssetPath;
        public ArtKind Kind;
        /// <summary>Generic rig with an avatar; false imports a static mesh with no rig and no clips.</summary>
        public bool Rigged;
        /// <summary>Bones (transforms under the rig root) the FBX must carry; 0 for static meshes.</summary>
        public int Bones;
        /// <summary>Clips the model's own FBX must carry (after the importer's renaming).</summary>
        public string[] OwnClips = new string[0];
        /// <summary>FBX whose clips drive this model (same rig, bound by path), or null for its own clips.</summary>
        public string ClipSource;
        /// <summary>Renderer object names (FBX model nodes) the import must produce, all on the one CM/Toon art material; the first is the body measured for Height.</summary>
        public string[] Meshes = new string[0];
        /// <summary>Mask codes (eighths of vertex alpha) the art must carry at least once.</summary>
        public int[] MaskCodes = new int[0];
        public float OutlinePx;
        public float Rim;
        /// <summary>Team channel colours as authored in the FBX (the Azure values; the Kaiju's stage default glow).</summary>
        public TeamSwatch Authored;
        /// <summary>The Crimson swap values (characters and props; the Kaiju uses KaijuGlow).</summary>
        public TeamSwatch Crimson;
        /// <summary>Body height in metres (bind-pose mesh bounds of Meshes[0]) the art reports, with tolerance HeightTolerance.</summary>
        public float Height;
        public const float HeightTolerance = 0.12f;
        /// <summary>Painted face decal (PNG next to the FBX, UV0 of Meshes[0]), or null.</summary>
        public string FaceTexture;
        /// <summary>-1: the decal is mixed over the main material (every face that shows no face maps to a transparent
        /// texel; Warrior, Kaiju). 0 or more: the decal is its own material slot (submesh) of Meshes[0], drawn by a face
        /// material with no hull (Miner: CM_FaceDecal is slot 1).</summary>
        public int FaceSlot = -1;
        /// <summary>Sidecar of unlined vertices (tools/art/outline_weights.py from the .blend's outline_w group), or null.</summary>
        public string OutlineWeights;
        /// <summary>True when the importer bakes UV4.x outline weights (0 on the face) and the material reads them.</summary>
        public bool BakesOutlineWeights => OutlineWeights != null || (FaceTexture != null && FaceSlot < 0);
    }

    /// <summary>
    /// THE mapping table from sim entities to Blender models (art import, 2026-11-12). Every sim-to-model choice the views
    /// make is a lookup here: class to character model (null = the grey-box capsule), Kaiju state to stage model, resource
    /// node to prop, barricade to the Miner barricade. Swatches are the art branches' palettes (warrior cm_art/palette.py,
    /// miner miner_art/mpal.py, props props_art/core.py, kaiju kaiju_palette.py), copied as data with their source.
    /// Re-importing a newer art file needs no change here as long as its file name and contract stay the same.
    /// </summary>
    public static class ArtMapping
    {
        public const string Warrior = "Warrior";
        public const string Miner = "Miner";
        public const string Farmer = "Farmer";
        public const string KaykitClips = "KayKit_Clips";
        public const string MinerBarricade = "MinerBarricade";
        public const string KaijuEgg = "Kaiju_Egg";
        public const string KaijuHatchling = "Kaiju_Hatchling";
        public const string KaijuAdult = "Kaiju_Adult";
        public const string KaijuShards = "Kaiju_BerserkShards";
        public const string KaijuSpikes = "Kaiju_TelegraphSpikes";
        public const string BiomassS = "prop_biomass_plant_s";
        public const string BiomassM = "prop_biomass_plant_m";
        public const string BiomassL = "prop_biomass_plant_l";
        public const string BiomassSprout = "prop_biomass_sprout";
        public const string CorruptionS = "prop_corruption_growth_s";
        public const string CorruptionL = "prop_corruption_growth_l";

        /// <summary>Barricade states: child mesh names of MinerBarricade.fbx.</summary>
        public const string BarricadeFolded = "Miner_Barricade_Folded";
        public const string BarricadeDeployed = "Miner_Barricade_Deployed";
        public const string BarricadeDamaged = "Miner_Barricade_Damaged";

        /// <summary>Clip names after import (ArtImportPostprocessor strips the FBX take prefix "Rig|").</summary>
        public const string Idle = "Idle", Walk = "Walk", Run = "Run", Attack = "Attack", Gather = "Gather", Hit = "Hit",
            Death = "Death", Cheer = "Cheer", Shake = "Shake", Roar = "Roar", SlamWindup = "SlamWindup", Slam = "Slam",
            Berserk = "Berserk", KO = "KO";

        /// <summary>Clips that loop (Loop Time on import).</summary>
        public static readonly string[] LoopingClips = { Idle, Walk, Run, Gather, Shake, Berserk };

        /// <summary>Seconds after the hatch the view shows the hatchling before the adult (view only: the sim has no stage).</summary>
        public const double HatchlingSeconds = 8.0;

        /// <summary>
        /// Inverted-hull width (px at 1080p) of characters and Kaiju (art refresh 2): the art previews draw a thin hull for
        /// inner creases (Farmer and Warrior 1.5 px, Kaiju 2.0 px) plus a silhouette-only dilation to 3 px outer. CM/Toon has
        /// no silhouette pass, so a 3 px hull drew every inner crease at 3 px and the characters read busy at the gameplay
        /// camera; 2 px matches the Kaiju's authored hull and keeps the silhouette readable.
        /// </summary>
        public const float CharacterHullPx = 2f;

        // Swatches (sRGB). Warrior: cm_art/palette.py team_cloth / team_glow / team_dark and TEAM_SWAP.
        static readonly TeamSwatch WarriorAzure = new TeamSwatch("#2A95AE", "#4FE3F0", "#2A374D");
        static readonly TeamSwatch WarriorCrimson = new TeamSwatch("#E46753", "#FF7461", "#652D2F");
        // Miner: miner_art/mpal.py kerchief / crystal (team dark unused: navy stays) and TEAM_SWAP.
        static readonly TeamSwatch MinerAzure = new TeamSwatch("#3E78C6", "#2E9BE6", "#23375F");
        static readonly TeamSwatch MinerCrimson = new TeamSwatch("#EF674E", "#FA743D", "#23375F");
        // Farmer: farmer_art/fpalette.py team cloth teal #4999A5 -> coral #E9705F (README "Conventions"); no team glow or
        // team dark on the Farmer (the 0.25 biomass glow is fixed, never swapped), so those channels keep the Warrior's.
        static readonly TeamSwatch FarmerAzure = new TeamSwatch("#4999A5", "#4FE3F0", "#2A374D");
        static readonly TeamSwatch FarmerCrimson = new TeamSwatch("#E9705F", "#FF7461", "#652D2F");
        // Props: props_art/core.py TEAM_CLOTH / TEAM_GLOW / TEAM_DARK and TEAM_SWAP (1.0 / 0.75 / 0.5).
        static readonly TeamSwatch PropAzure = new TeamSwatch("#127EC0", "#4FE3F0", "#1D6A8C");
        static readonly TeamSwatch PropCrimson = new TeamSwatch("#FC725A", "#FF7461", "#9B4132");
        // Kaiju: kaiju_palette.py GLOW (allegiance) and STAGE_DEFAULT_GLOW (egg and hatchling lime, adult and shards Azure).
        public const string GlowNeutral = "#B8E833", GlowAzure = "#13F1F4", GlowCrimson = "#F2563F", GlowBerserk = "#EE2CC0";
        static TeamSwatch KaijuDefault(string glow) => new TeamSwatch(glow, glow, glow);
        /// <summary>One 256 px atlas for both creature stages (adult top half, hatchling bottom half) on UVMap.</summary>
        public const string KaijuFace = "Assets/_Game/Art/Kaiju/Kaiju_Face.png";

        public static readonly ModelSpec[] Models =
        {
            new ModelSpec { Key = Warrior, AssetPath = "Assets/_Game/Art/Characters/Warrior.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, ClipSource = "Assets/_Game/Art/Characters/KayKit_Clips.fbx",
                Meshes = new[] { "Warrior_Body", "Warrior_Cleaver", "Warrior_Shield" }, MaskCodes = new[] { 0, 6, 8 },
                OutlinePx = CharacterHullPx, Rim = 0.6f, Authored = WarriorAzure, Crimson = WarriorCrimson, Height = 2.0f,
                FaceTexture = "Assets/_Game/Art/Characters/Warrior_Face.png" },
            new ModelSpec { Key = Miner, AssetPath = "Assets/_Game/Art/Miner/Miner.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, ClipSource = "Assets/_Game/Art/Characters/KayKit_Clips.fbx",
                Meshes = new[] { "Miner_Body", "Miner_PickHammer", "Miner_PickHammer_Hammer" }, MaskCodes = new[] { 0, 6, 8 },
                OutlinePx = CharacterHullPx, Rim = 0.6f, Authored = MinerAzure, Crimson = MinerCrimson, Height = 2.0f,
                FaceTexture = "Assets/_Game/Art/Miner/MinerFace.png", FaceSlot = 1 },
            new ModelSpec { Key = Farmer, AssetPath = "Assets/_Game/Art/Farmer/Farmer.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, ClipSource = "Assets/_Game/Art/Characters/KayKit_Clips.fbx",
                Meshes = new[] { "Farmer_Body", "Farmer_Staff" }, MaskCodes = new[] { 0, 2, 8 },
                OutlinePx = CharacterHullPx, Rim = 0.6f, Authored = FarmerAzure, Crimson = FarmerCrimson, Height = 2.0f,
                OutlineWeights = "Assets/_Game/Art/Farmer/Farmer_OutlineW.json" },
            new ModelSpec { Key = KaykitClips, AssetPath = "Assets/_Game/Art/Characters/KayKit_Clips.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, OwnClips = new[] { Idle, Walk, Run, Attack, Gather, Hit, Death, Cheer } },
            new ModelSpec { Key = MinerBarricade, AssetPath = "Assets/_Game/Art/Miner/MinerBarricade.fbx", Kind = ArtKind.Prop,
                Meshes = new[] { BarricadeDamaged, BarricadeDeployed, BarricadeFolded }, MaskCodes = new[] { 0 },
                OutlinePx = 2.5f, Authored = MinerAzure, Crimson = MinerCrimson },
            new ModelSpec { Key = KaijuEgg, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_Egg.fbx", Kind = ArtKind.Kaiju, Rigged = true,
                Bones = 2, OwnClips = new[] { Idle, Shake }, Meshes = new[] { "KaijuMesh" }, MaskCodes = new[] { 0, 6 },
                OutlinePx = CharacterHullPx, Authored = KaijuDefault(GlowNeutral), Height = 1.7f },
            new ModelSpec { Key = KaijuHatchling, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_Hatchling.fbx", Kind = ArtKind.Kaiju, Rigged = true,
                Bones = 20, OwnClips = new[] { Idle, Walk, Roar, SlamWindup, Slam, Berserk, KO }, Meshes = new[] { "KaijuMesh" },
                MaskCodes = new[] { 0, 6 }, OutlinePx = CharacterHullPx, Authored = KaijuDefault(GlowNeutral), Height = 2.235f, FaceTexture = KaijuFace },
            new ModelSpec { Key = KaijuAdult, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_Adult.fbx", Kind = ArtKind.Kaiju, Rigged = true,
                Bones = 20, OwnClips = new[] { Idle, Walk, Roar, SlamWindup, Slam, Berserk, KO }, Meshes = new[] { "KaijuMesh" },
                MaskCodes = new[] { 0, 6 }, OutlinePx = CharacterHullPx, Authored = KaijuDefault(GlowAzure), Height = 5.0f, FaceTexture = KaijuFace },
            new ModelSpec { Key = KaijuShards, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_BerserkShards.fbx", Kind = ArtKind.Kaiju,
                Meshes = new[] { "Kaiju_BerserkShards" }, MaskCodes = new[] { 6 }, OutlinePx = 0f, Authored = KaijuDefault(GlowAzure) },
            new ModelSpec { Key = KaijuSpikes, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_TelegraphSpikes.fbx", Kind = ArtKind.Kaiju,
                Meshes = new[] { "Kaiju_TelegraphSpikes" }, OutlinePx = 0f, Authored = KaijuDefault(GlowAzure) },
            Prop(BiomassS, 0.5f), Prop(BiomassM, 0.8f), Prop(BiomassL, 1.2f), Prop(BiomassSprout, 0.25f),
            Prop(CorruptionS, 0.6f), Prop(CorruptionL, 1.2f),
        };

        static ModelSpec Prop(string key, float height) => new ModelSpec
        {
            Key = key, AssetPath = "Assets/_Game/Art/Props/" + key + ".fbx", Kind = ArtKind.Prop, Meshes = new[] { key },
            OutlinePx = 2.5f, Authored = PropAzure, Crimson = PropCrimson, Height = height,
        };

        static Dictionary<string, ModelSpec> _byKey;

        public static ModelSpec Spec(string key)
        {
            if (_byKey == null)
            {
                var d = new Dictionary<string, ModelSpec>();
                foreach (ModelSpec m in Models) d[m.Key] = m;
                _byKey = d;
            }
            return key != null && _byKey.TryGetValue(key, out ModelSpec s) ? s : null;
        }

        /// <summary>Character model per class; null keeps the grey-box capsule (Demolitions, Builder: not built yet).</summary>
        public static string ClassModel(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Warrior: return Warrior;
                case ClassId.Miner: return Miner;
                case ClassId.Farmer: return Farmer;
                default: return null;
            }
        }

        /// <summary>Kaiju stage model: the egg until the hatch, the hatchling for HatchlingSeconds after it, then the adult.</summary>
        public static string KaijuModel(KaijuState state, double secondsSinceHatch)
        {
            if (state == KaijuState.Egg) return KaijuEgg;
            return secondsSinceHatch < HatchlingSeconds ? KaijuHatchling : KaijuAdult;
        }

        /// <summary>Allegiance glow per Kaiju state (Egg and Neutral: the lime the egg and hatchling are authored in).</summary>
        public static string KaijuGlow(KaijuState state)
        {
            switch (state)
            {
                case KaijuState.Azure: return GlowAzure;
                case KaijuState.Crimson: return GlowCrimson;
                case KaijuState.Berserk: return GlowBerserk;
                default: return GlowNeutral;
            }
        }

        /// <summary>Resource node prop by kind and charges; null hides the node (a spent corruption node).</summary>
        public static string NodeModel(NodeKind kind, int charges)
        {
            if (kind == NodeKind.Biomass) return charges >= 3 ? BiomassL : charges == 2 ? BiomassM : charges == 1 ? BiomassS : BiomassSprout;
            return charges >= 2 ? CorruptionL : charges == 1 ? CorruptionS : null;
        }

        /// <summary>Barricade state mesh: deployed, damaged under half of MaxHp.</summary>
        public static string BarricadeState(double hp, double maxHp) => hp < 0.5 * maxHp ? BarricadeDamaged : BarricadeDeployed;

        /// <summary>The team swatch a model wears.</summary>
        public static TeamSwatch Swatch(ModelSpec spec, TeamId team) => team == TeamId.Azure ? spec.Authored : spec.Crimson;
    }
}

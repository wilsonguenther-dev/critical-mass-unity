using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>What a model is, for import settings, materials and the views.</summary>
    public enum ArtKind { Character, Kaiju, Prop }

    /// <summary>Kaiju v4 face expressions (one decal atlas each, ArtMapping.KaijuExpressionTextures; order is the index).</summary>
    public enum KaijuExpression { Neutral, Happy, Angry, Berserk, Hurt, Sleepy, Blink }

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
        /// <summary>Leg length of this model's own joints over the KayKit skeleton's (hip to ankle): the locomotion stride
        /// scales by it so the planted foot holds. 1 for every model on the plain KayKit joints.</summary>
        /// <summary>The face is painted in the vertex colour (HY exports): no decal texture, every vertex lined.</summary>
        public bool VertexColourFace;
        public float StrideScale = 1f;
        /// <summary>Painted face decal (PNG next to the FBX, UV0 of Meshes[0]), or null.</summary>
        public string FaceTexture;
        /// <summary>-1: the decal is mixed over the main material (every face that shows no face maps to a transparent
        /// texel; Warrior, Kaiju). 0 or more: the decal is its own material slot (submesh) of Meshes[0], drawn by a face
        /// material with no hull (Miner: CM_FaceDecal is slot 1).</summary>
        public int FaceSlot = -1;
        /// <summary>
        /// Yaw in degrees that turns this model's art so its face is on the root's +Z, the side the view turns toward the
        /// sim facing (ModelView.SetFacing). 0 when the art already faces +Z. Measured per model by ModelFacingTests (face
        /// decal and toes), never assumed per family. Measured 2026-11-12 (idle pose): Warrior face 179 / toes 176, Miner face
        /// 179 / toes 176, Farmer toes 176 (no face decal), Kaiju_Hatchling face 178, Kaiju_Adult face 178: each art file
        /// faces the root's -Z, so each carries 180.
        /// Art landing (2026-11-12): every fighter FBX now comes from the LOD build (art/fighter-lods d536d30), which re-exports
        /// with the class recipe unchanged (cm_art/export.py: authored facing Blender -Y, axis_forward -Z, axis_up Y,
        /// FBX_SCALE_ALL, bake_space_transform True), and the Kaiju with build_kaiju.py export() (same axes, but
        /// bake_space_transform False; with bakeAxisConversion on it should land the same way, the one thing to check in a
        /// facing capture). Demolitions, Builder and Healer use that same recipe, so they arrive facing the root's -Z like
        /// the Warrior and carry 180 too.
        /// </summary>
        public float ArtYawDeg;
        /// <summary>Sidecar of unlined vertices (tools/art/outline_weights.py from the .blend's outline_w group), or null.</summary>
        public string OutlineWeights;
        /// <summary>
        /// Face expression atlases indexed by <see cref="KaijuExpression"/> (Kaiju v4: same UV layout as FaceTexture, which
        /// is entry 0), or null. The view swaps the material's face texture per state (ArtMaterials.ForGlow).
        /// </summary>
        public string[] Expressions;
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
        public const string Demolitions = "Demolitions";
        public const string Builder = "Builder";
        public const string Healer = "Healer";
        /// <summary>Class props (authored static meshes; one child mesh per state).</summary>
        public const string DemolitionsProps = "DemolitionsProps", HealerProps = "HealerProps", BuilderLadder = "BuilderLadder",
            BuilderRopeBridge = "BuilderRopeBridge", BuilderBuildGlow = "BuilderBuildGlow";
        public const string MineBuried = "Demolitions_Mine_Buried", MineArmed = "Demolitions_Mine_Armed",
            MineOwnerRing = "Demolitions_Mine_Armed_OwnerRing", MineTriggered = "Demolitions_Mine_Triggered",
            TntFlight = "Demolitions_TNT_Flight", CraterRim = "Demolitions_CraterRim", Explosion = "Demolitions_Explosion";
        public const string LadderCarried = "BuilderLadder_Carried", LadderPlaced = "BuilderLadder_Placed", LadderDamaged = "BuilderLadder_Damaged";
        public const string RopeBridgeCarried = "BuilderRopeBridge_Carried", RopeBridgePlaced = "BuilderRopeBridge_Placed",
            RopeBridgeDamaged = "BuilderRopeBridge_Damaged";
        public const string HealerGauntlet = "Healer_BloomGauntlet_Prop", HealerLantern = "Healer_Lantern_Prop";
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
        /// <summary>The carried cargo prop (DG-71, CarryProp): props_art p14, 0.55 u tall.</summary>
        public const string CarriedBundle = "prop_carried_bundle";

        /// <summary>Barricade states: child mesh names of MinerBarricade.fbx.</summary>
        public const string BarricadeFolded = "Miner_Barricade_Folded";
        public const string BarricadeDeployed = "Miner_Barricade_Deployed";
        public const string BarricadeDamaged = "Miner_Barricade_Damaged";

        /// <summary>Clip names after import (ArtImportPostprocessor strips the FBX take prefix "Rig|").</summary>
        public const string Idle = "Idle", Walk = "Walk", Run = "Run", Attack = "Attack", Gather = "Gather", Hit = "Hit",
            Death = "Death", Cheer = "Cheer", Shake = "Shake", Roar = "Roar", SlamWindup = "SlamWindup", Slam = "Slam",
            Berserk = "Berserk", KO = "KO";

        /// <summary>
        /// Character clips added by the animation pass (2026-11-12, DG-72): tools/art/kaykit_clip_map.tsv maps each KayKit
        /// clip to these names, build_kaykit_clips.py exports them into KayKit_Clips.fbx, measure_clips.py writes their facts
        /// (Resources/Anim/clip_facts.json). Every view falls back to the original 8 clips when one is missing.
        /// </summary>
        public const string StrafeL = "StrafeL", StrafeR = "StrafeR", WalkBack = "WalkBack", JumpStart = "JumpStart",
            JumpAir = "JumpAir", JumpLand = "JumpLand", KnockDown = "KnockDown", GetUp = "GetUp", HitHeavy = "HitHeavy",
            Block = "Block", Blocking = "Blocking", BlockHit = "BlockHit", DeathB = "DeathB", AttackSlice = "AttackSlice",
            Attack2HChop = "Attack2HChop", Attack2HSlice = "Attack2HSlice", Cast = "Cast", UseItem = "UseItem", Throw = "Throw",
            PickUp = "PickUp", Carry = "Carry", IdleLook = "IdleLook", SitDown = "SitDown", SitIdle = "SitIdle";

        /// <summary>Kaiju clips the Kaiju art branch adds (art/kaiju-blender, DG-91); used when present, else the old poses.</summary>
        public const string Swipe = "Swipe", SwipeWindup = "SwipeWindup", Flinch = "Flinch";

        /// <summary>The 24 clips beyond the original 8 that KayKit_Clips.fbx carries once build_kaykit_clips.py is re-run.</summary>
        public static readonly string[] KaykitExtraClips =
        {
            StrafeL, StrafeR, WalkBack, JumpStart, JumpAir, JumpLand, KnockDown, GetUp, HitHeavy, Block, Blocking, BlockHit, DeathB,
            AttackSlice, Attack2HChop, Attack2HSlice, Cast, UseItem, Throw, PickUp, Carry, IdleLook, SitDown, SitIdle,
        };

        /// <summary>Clips that loop (Loop Time on import).</summary>
        public static readonly string[] LoopingClips =
        {
            Idle, Walk, Run, Gather, Shake, Berserk, StrafeL, StrafeR, WalkBack, JumpAir, Blocking, Carry, IdleLook, SitIdle,
        };

        /// <summary>
        /// Clips that may play on the upper-body layer (ModelView, AvatarMask from "spine" up): attacks, abilities, light and
        /// blocked hits, the carry pose and the cheer. Each gets two playables there so a re-trigger can cross-fade.
        /// </summary>
        public static readonly string[] UpperBodyClips =
        {
            Attack, AttackSlice, Attack2HChop, Attack2HSlice, Block, Blocking, BlockHit, Cast, UseItem, Throw, Hit, Carry, Cheer,
        };

        /// <summary>Per-class clip choice (DG-74): attack, ability (and its hold loop), gather and its rate. Missing clips fall back.</summary>
        public readonly struct ClassClipSet
        {
            public readonly string Attack, Ability, AbilityHold, Gather;
            public readonly float GatherRate;
            public ClassClipSet(string attack, string ability, string abilityHold, string gather, float gatherRate)
            {
                Attack = attack; Ability = ability; AbilityHold = abilityHold; Gather = gather; GatherRate = gatherRate;
            }
        }

        /// <summary>
        /// Warrior: horizontal cleave (matches the 120 degree crescent), shield = Block then Blocking. Miner: two-hand chop,
        /// barricade = Use_Item, gather = the chop at 0.7 rate on rock. Farmer: two-hand staff sweep, roots = Spellcast_Shoot,
        /// gather = PickUp on plants. Art landing (2026-11-12), the three new classes on the same 32-clip KayKit set (no new
        /// clips): Demolitions has no melee weapon, so her attack is the TNT Throw and the mine plant is PickUp (the crouch to
        /// the ground her plant pose shows); Builder swings the one-hand mallet (Attack, the 1H chop) and builds with Use_Item;
        /// Healer mends on attack with the gauntlet orb (Cast, Spellcast_Shoot) and raises the lantern for the healing pulse
        /// (Use_Item). All three gather with the reach (Gather).
        /// </summary>
        public static ClassClipSet ClassClips(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Warrior: return new ClassClipSet(AttackSlice, Block, Blocking, Gather, 1f);
                case ClassId.Miner: return new ClassClipSet(Attack2HChop, UseItem, null, Attack2HChop, 0.7f);
                case ClassId.Farmer: return new ClassClipSet(Attack2HSlice, Cast, null, PickUp, 1f);
                // feat/light-weapons-1: her attack is the pry-bar swing on the shared one-hand Attack clip (the TNT throw plays
                // Throw from its own skill cue, SkillAnimCues; it never used the attack slot's clip).
                case ClassId.Demolitions: return new ClassClipSet(Attack, PickUp, null, Gather, 1f);
                case ClassId.Builder: return new ClassClipSet(Attack, UseItem, null, Gather, 1f);
                case ClassId.Healer: return new ClassClipSet(Cast, UseItem, null, Gather, 1f);
                default: return new ClassClipSet(Attack, null, null, Gather, 1f);
            }
        }

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
        // Demolitions: demo_art/dpal.py reuses the Miner's team values ("so both classes swap identically"). Healer:
        // healer_art/hpal.py "same contract and team values as the Miner". Builder: builder_art/bpal.py team #2A9DBA,
        // team_glow #4FE3F0, TEAM_SWAP cloth #E46753 / glow #FF7461; no team dark, so the Warrior's dark channels.
        static readonly TeamSwatch BuilderAzure = new TeamSwatch("#2A9DBA", "#4FE3F0", "#2A374D");
        static readonly TeamSwatch BuilderCrimson = new TeamSwatch("#E46753", "#FF7461", "#652D2F");
        // Props: props_art/core.py TEAM_CLOTH / TEAM_GLOW / TEAM_DARK and TEAM_SWAP (1.0 / 0.75 / 0.5).
        static readonly TeamSwatch PropAzure = new TeamSwatch("#127EC0", "#4FE3F0", "#1D6A8C");
        static readonly TeamSwatch PropCrimson = new TeamSwatch("#FC725A", "#FF7461", "#9B4132");
        // Kaiju: kaiju_palette.py GLOW (allegiance) and STAGE_DEFAULT_GLOW (egg and hatchling lime, adult and shards Azure).
        public const string GlowNeutral = "#B8E833", GlowAzure = "#13F1F4", GlowCrimson = "#F2563F", GlowBerserk = "#EE2CC0";
        static TeamSwatch KaijuDefault(string glow) => new TeamSwatch(glow, glow, glow);
        /// <summary>One 256 px atlas for both creature stages (adult top half, hatchling bottom half) on UVMap.</summary>
        public const string KaijuFace = "Assets/_Game/Art/Kaiju/Kaiju_Face.png";

        /// <summary>
        /// Kaiju v4 expression atlases (512 px, adult top half, hatchling bottom half, same UVs as Kaiju_Face.png), indexed by
        /// KaijuExpression. Neutral is the default goofy face; Blink is the default with the lids shut.
        /// </summary>
        public static readonly string[] KaijuExpressionTextures =
        {
            KaijuFace, "Assets/_Game/Art/Kaiju/Kaiju_Face_Happy.png", "Assets/_Game/Art/Kaiju/Kaiju_Face_Angry.png",
            "Assets/_Game/Art/Kaiju/Kaiju_Face_Berserk.png", "Assets/_Game/Art/Kaiju/Kaiju_Face_Hurt.png",
            "Assets/_Game/Art/Kaiju/Kaiju_Face_Sleepy.png", "Assets/_Game/Art/Kaiju/Kaiju_Face_Blink.png",
        };

        /// <summary>The first seconds of the hatchling stage show the sleepy face (egg-fresh).</summary>
        public const double HatchlingSleepySeconds = 3.0;
        /// <summary>The hurt face holds this long after the Kaiju loses health or is knocked out.</summary>
        public const double KaijuHurtSeconds = 0.5;
        /// <summary>The Kaiju blinks (Blink atlas) for BlinkSeconds every BlinkPeriodSeconds while its face is neutral.</summary>
        public const double BlinkPeriodSeconds = 3.7, BlinkSeconds = 0.125;

        /// <summary>
        /// The Kaiju face per state (pure; view only, nothing feeds back into the sim). Priority: berserk, then hurt (a recent
        /// health loss or a knock-out), then angry while it attacks (slam telegraph, melee wind-up, the strike itself), then
        /// sleepy for the first HatchlingSleepySeconds after the hatch, then happy when it is allied to a team or was just
        /// fed, else neutral with a blink on a timer. The egg shows no face (Neutral).
        /// </summary>
        public static KaijuExpression KaijuExpressionFor(KaijuState state, double secondsSinceHatch, bool attacking, bool hurt, bool fed, double viewSeconds)
        {
            if (state == KaijuState.Egg) return KaijuExpression.Neutral;
            if (state == KaijuState.Berserk) return KaijuExpression.Berserk;
            if (hurt) return KaijuExpression.Hurt;
            if (attacking) return KaijuExpression.Angry;
            if (secondsSinceHatch >= 0 && secondsSinceHatch < HatchlingSleepySeconds) return KaijuExpression.Sleepy;
            if (fed || state == KaijuState.Azure || state == KaijuState.Crimson) return KaijuExpression.Happy;
            return Blinking(viewSeconds) ? KaijuExpression.Blink : KaijuExpression.Neutral;
        }

        /// <summary>True during the blink window of the timer (pure).</summary>
        public static bool Blinking(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;
            double t = seconds % BlinkPeriodSeconds;
            if (t < 0) t += BlinkPeriodSeconds;
            return t >= BlinkPeriodSeconds - BlinkSeconds;
        }

        /// <summary>Project path of an expression atlas.</summary>
        public static string KaijuFaceOf(KaijuExpression e) => KaijuExpressionTextures[(int)e];

        public static readonly ModelSpec[] Models =
        {
            new ModelSpec { Key = Warrior, AssetPath = "Assets/_Game/Art/Characters/Warrior.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, ClipSource = "Assets/_Game/Art/Characters/KayKit_Clips.fbx",
                Meshes = new[] { "Warrior_Hero", "Warrior_LOD0", "Warrior_LOD1", "Warrior_Shadow" }, MaskCodes = new[] { 0, 6, 8 },
                OutlinePx = CharacterHullPx, Rim = 0.6f, Authored = WarriorAzure, Crimson = WarriorCrimson, Height = 2.0f,
                FaceTexture = "Assets/_Game/Art/Characters/Warrior_Face.png", ArtYawDeg = 180f },
            new ModelSpec { Key = Miner, AssetPath = "Assets/_Game/Art/Miner/Miner.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, ClipSource = "Assets/_Game/Art/Characters/KayKit_Clips.fbx",
                Meshes = new[] { "Miner_Hero", "Miner_LOD0", "Miner_LOD1", "Miner_Shadow" }, MaskCodes = new[] { 0, 6, 8 },
                OutlinePx = CharacterHullPx, Rim = 0.6f, Authored = MinerAzure, Crimson = MinerCrimson, Height = 2.0f,
                FaceTexture = "Assets/_Game/Art/Miner/MinerFace.png", FaceSlot = 1, ArtYawDeg = 180f },
            new ModelSpec { Key = Farmer, AssetPath = "Assets/_Game/Art/Farmer/Farmer.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, ClipSource = "Assets/_Game/Art/Farmer/Farmer_Clips.fbx",
                Meshes = new[] { "Farmer_Hero", "Farmer_LOD0", "Farmer_LOD1", "Farmer_Shadow" }, MaskCodes = new[] { 0, 2, 8 },
                OutlinePx = CharacterHullPx, Rim = 0.6f, Authored = FarmerAzure, Crimson = FarmerCrimson, Height = 2.0f,
                VertexColourFace = true, StrideScale = 0.731f, ArtYawDeg = 180f },   // rigs-2 (leg ratio 0.731, bake_clips.py): clips baked per class with foot IK (Farmer_Clips.fbx, bake_clips.py). HY (2026-11-13; legs 0.267 vs KayKit 0.374 hip to ankle); pre-HY: OutlineWeights Farmer_OutlineW.json + FaceTexture Farmer_Face.png (Farmer/_fallback~)
            // Art landing (2026-11-12): the three new classes, LOD files from art/fighter-lods d536d30 (weapons folded into
            // every level). Each body carries CM_Toon plus CM_FaceDecal on slot 1, the Miner's decal method (class READMEs).
            new ModelSpec { Key = Demolitions, AssetPath = "Assets/_Game/Art/Demolitions/Demolitions.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, ClipSource = "Assets/_Game/Art/Characters/KayKit_Clips.fbx",
                Meshes = new[] { "Demolitions_Hero", "Demolitions_LOD0", "Demolitions_LOD1", "Demolitions_Shadow" }, MaskCodes = new[] { 0, 6, 8 },
                OutlinePx = CharacterHullPx, Rim = 0.6f, Authored = MinerAzure, Crimson = MinerCrimson, Height = 2.0f,
                FaceTexture = "Assets/_Game/Art/Demolitions/DemolitionsFace.png", FaceSlot = 1, ArtYawDeg = 180f },
            new ModelSpec { Key = Builder, AssetPath = "Assets/_Game/Art/Builder/Builder.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, ClipSource = "Assets/_Game/Art/Characters/KayKit_Clips.fbx",
                Meshes = new[] { "Builder_Hero", "Builder_LOD0", "Builder_LOD1", "Builder_Shadow" }, MaskCodes = new[] { 0, 8 },
                // Height: the Hero mesh's bind-pose top is the ladder corner on his back (2.329 m in the FBX; cap crown 1.895).
                OutlinePx = CharacterHullPx, Rim = 0.6f, Authored = BuilderAzure, Crimson = BuilderCrimson, Height = 2.33f,
                FaceTexture = "Assets/_Game/Art/Builder/BuilderFace.png", FaceSlot = 1, ArtYawDeg = 180f },
            new ModelSpec { Key = Healer, AssetPath = "Assets/_Game/Art/Healer/Healer.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, ClipSource = "Assets/_Game/Art/Characters/KayKit_Clips.fbx",
                Meshes = new[] { "Healer_Hero", "Healer_LOD0", "Healer_LOD1", "Healer_Shadow" }, MaskCodes = new[] { 0, 6, 8 },
                OutlinePx = CharacterHullPx, Rim = 0.6f, Authored = MinerAzure, Crimson = MinerCrimson, Height = 2.0f,
                FaceTexture = "Assets/_Game/Art/Healer/HealerFace.png", FaceSlot = 1, ArtYawDeg = 180f },
            new ModelSpec { Key = DemolitionsProps, AssetPath = "Assets/_Game/Art/Demolitions/DemolitionsProps.fbx", Kind = ArtKind.Prop,
                Meshes = new[] { MineBuried, MineArmed, MineOwnerRing, MineTriggered, TntFlight, CraterRim, Explosion },
                OutlinePx = 2.5f, Authored = MinerAzure, Crimson = MinerCrimson },
            new ModelSpec { Key = BuilderLadder, AssetPath = "Assets/_Game/Art/Builder/BuilderLadder.fbx", Kind = ArtKind.Prop,
                Meshes = new[] { LadderCarried, LadderPlaced, LadderDamaged }, OutlinePx = 2.5f, Authored = BuilderAzure, Crimson = BuilderCrimson },
            new ModelSpec { Key = BuilderRopeBridge, AssetPath = "Assets/_Game/Art/Builder/BuilderRopeBridge.fbx", Kind = ArtKind.Prop,
                Meshes = new[] { RopeBridgeCarried, RopeBridgePlaced, RopeBridgeDamaged }, OutlinePx = 2.5f, Authored = BuilderAzure, Crimson = BuilderCrimson },
            new ModelSpec { Key = BuilderBuildGlow, AssetPath = "Assets/_Game/Art/Builder/BuilderBuildGlow.fbx", Kind = ArtKind.Prop,
                Meshes = new[] { BuilderBuildGlow }, OutlinePx = 0f, Authored = BuilderAzure, Crimson = BuilderCrimson },
            new ModelSpec { Key = HealerProps, AssetPath = "Assets/_Game/Art/Healer/HealerProps.fbx", Kind = ArtKind.Prop,
                Meshes = new[] { HealerGauntlet, HealerLantern }, OutlinePx = 2.5f, Authored = MinerAzure, Crimson = MinerCrimson },
            new ModelSpec { Key = KaykitClips, AssetPath = "Assets/_Game/Art/Characters/KayKit_Clips.fbx", Kind = ArtKind.Character, Rigged = true,
                Bones = 41, OwnClips = new[] { Idle, Walk, Run, Attack, Gather, Hit, Death, Cheer } },
            new ModelSpec { Key = MinerBarricade, AssetPath = "Assets/_Game/Art/Miner/MinerBarricade.fbx", Kind = ArtKind.Prop,
                Meshes = new[] { BarricadeDamaged, BarricadeDeployed, BarricadeFolded }, MaskCodes = new[] { 0 },
                OutlinePx = 2.5f, Authored = MinerAzure, Crimson = MinerCrimson },
            new ModelSpec { Key = KaijuEgg, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_Egg.fbx", Kind = ArtKind.Kaiju, Rigged = true,
                Bones = 2, OwnClips = new[] { Idle, Shake }, Meshes = new[] { "KaijuMesh" }, MaskCodes = new[] { 0, 6 },
                OutlinePx = CharacterHullPx, Authored = KaijuDefault(GlowNeutral), Height = 1.7f },
            new ModelSpec { Key = KaijuHatchling, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_Hatchling.fbx", Kind = ArtKind.Kaiju, Rigged = true,
                Bones = 20, OwnClips = new[] { Idle, Walk, Roar, SlamWindup, Slam, Swipe, Berserk, KO }, Meshes = new[] { "Kaiju_Hero", "Kaiju_LOD0", "Kaiju_LOD1", "Kaiju_Shadow" },
                MaskCodes = new[] { 0, 6 }, OutlinePx = CharacterHullPx, Authored = KaijuDefault(GlowNeutral), Height = 2.235f, FaceTexture = KaijuFace,
                Expressions = KaijuExpressionTextures, ArtYawDeg = 180f },
            new ModelSpec { Key = KaijuAdult, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_Adult.fbx", Kind = ArtKind.Kaiju, Rigged = true,
                Bones = 22,   // kaiju-walk-1: each hind leg Thigh > Shin > Foot (Foot_L/Foot_R = the planted foot at the sole)
                OwnClips = new[] { Idle, Walk, Roar, SlamWindup, Slam, Swipe, Berserk, KO }, Meshes = new[] { "Kaiju_Hero", "Kaiju_LOD0", "Kaiju_LOD1", "Kaiju_Shadow" },
                MaskCodes = new[] { 0, 6 }, OutlinePx = CharacterHullPx, Authored = KaijuDefault(GlowAzure), Height = 5.0f, FaceTexture = KaijuFace,
                Expressions = KaijuExpressionTextures, ArtYawDeg = 180f },
            new ModelSpec { Key = KaijuShards, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_BerserkShards.fbx", Kind = ArtKind.Kaiju,
                Meshes = new[] { "Kaiju_BerserkShards" }, MaskCodes = new[] { 6 }, OutlinePx = 0f, Authored = KaijuDefault(GlowAzure) },
            new ModelSpec { Key = KaijuSpikes, AssetPath = "Assets/_Game/Art/Kaiju/Kaiju_TelegraphSpikes.fbx", Kind = ArtKind.Kaiju,
                Meshes = new[] { "Kaiju_TelegraphSpikes" }, OutlinePx = 0f, Authored = KaijuDefault(GlowAzure) },
            Prop(BiomassS, 0.5f), Prop(BiomassM, 0.8f), Prop(BiomassL, 1.2f), Prop(BiomassSprout, 0.25f),
            Prop(CorruptionS, 0.6f), Prop(CorruptionL, 1.2f), Prop(CarriedBundle, 0.55f),
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

        /// <summary>Character model per class (all six have art since the art landing); null keeps the grey-box capsule.</summary>
        public static string ClassModel(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Warrior: return Warrior;
                case ClassId.Miner: return Miner;
                case ClassId.Farmer: return Farmer;
                case ClassId.Demolitions: return Demolitions;
                case ClassId.Builder: return Builder;
                case ClassId.Healer: return Healer;
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

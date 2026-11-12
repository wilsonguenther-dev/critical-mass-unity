using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.View.Art;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// The import contract for Blender art under Assets/_Game/Art/** (2026-11-12). Every FBX there imports with the same
    /// settings whatever its .meta said before, so a re-import can never drift: scale 1 with file units and baked axis
    /// conversion (the art exports FBX_SCALE_ALL, -Z forward, Y up), normals and vertex colours from the file, no cameras,
    /// lights, colliders, blend shapes or tangents, no imported materials (every renderer gets the shared CM/Toon art
    /// material, Resources/Toon/CMToonArt.mat, vertex-colour mode), Generic rig with its own avatar for the rigged models
    /// in ArtMapping and no rig for the rest, clip names without the FBX take prefix ("Rig|Walk" becomes "Walk"), Loop
    /// Time on ArtMapping.LoopingClips, clips sorted by name.
    /// </summary>
    public sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/_Game/Art/";
        public const string ArtMaterialPath = "Assets/_Game/Resources/Toon/CMToonArt.mat";

        /// <summary>Bump when the contract changes: Unity re-imports every model this postprocessor touches.</summary>
        public override uint GetVersion() => 3;

        public static bool IsArt(string path) =>
            path.StartsWith(ArtRoot, StringComparison.Ordinal) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        /// <summary>The ArtMapping spec of an FBX path, or null (an unmapped prop: static mesh rules).</summary>
        public static ModelSpec SpecFor(string path) => ArtMapping.Models.FirstOrDefault(m => m.AssetPath == path);

        private void OnPreprocessModel()
        {
            if (!IsArt(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            ModelSpec spec = SpecFor(assetPath);
            bool rigged = spec != null && spec.Rigged;

            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.useFileUnits = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.importConstraints = false;
            mi.addCollider = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.generateSecondaryUV = false;
            mi.preserveHierarchy = true;
            mi.sortHierarchyByName = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;

            mi.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            mi.avatarSetup = rigged ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.NoAvatar;
            mi.optimizeGameObjects = false;
            mi.importAnimation = rigged;
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            mi.resampleCurves = true;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsArt(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            if (!mi.importAnimation) return;
            ModelImporterClipAnimation[] clips = mi.defaultClipAnimations;
            var looping = new HashSet<string>(ArtMapping.LoopingClips);
            foreach (ModelImporterClipAnimation c in clips)
            {
                string name = ClipName(c.takeName);
                c.name = name;
                c.loopTime = looping.Contains(name);
                c.loopPose = false;
                c.lockRootRotation = false;
                c.lockRootHeightY = false;
                c.lockRootPositionXZ = false;
            }
            mi.clipAnimations = clips.OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
        }

        /// <summary>"Rig|Walk" (Blender all-actions take) -> "Walk"; a plain take name stays.</summary>
        public static string ClipName(string take)
        {
            int bar = take.LastIndexOf('|');
            return bar >= 0 ? take.Substring(bar + 1) : take;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsArt(assetPath)) return;
            context.DependsOnSourceAsset(ArtMaterialPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ArtMaterialPath);
            if (mat == null)
            {
                Debug.LogWarning($"[ArtImport] {ArtMaterialPath} missing while importing {assetPath}: run ProjectSetup.Apply (renderers keep no material)");
                return;
            }
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                int n = Math.Max(1, mesh != null ? mesh.subMeshCount : 1);
                var mats = new Material[n];
                for (int i = 0; i < n; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }
    }
}

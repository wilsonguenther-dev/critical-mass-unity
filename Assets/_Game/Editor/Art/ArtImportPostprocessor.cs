using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Dressing;
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
    /// World art (2026-11-12): the environment kit (Art/Environment) and the bases kit (Art/Bases) are static meshes the
    /// world view combines per chunk, so they import readable; environment FBX keep their material slots by name
    /// (CM_Toon, CM_Ground, CM_GlowDecal, CM_ShadowDecal, CM_Toon_Foliage, CM_Toon_Crystal) and each slot gets its
    /// WorldLook material (Resources/Toon/World); bases renderers get WorldBase (or WorldBaseFlat for ground pools).
    /// Every world mesh gets a smoothed outline direction in UV3 (averaged normals of coincident vertices), so the
    /// constant-pixel hull does not tear at the hard edges, and the waterfall's pool foam band gets its constant-pixel
    /// extrusion direction in UV2 (WorldFoam.Bake).
    /// Art refresh 2 (2026-11-12): face decal PNGs (ModelSpec.FaceTexture) import as clamped sRGB textures with alpha, and
    /// every model with ModelSpec.BakesOutlineWeights gets a per-vertex outline weight in UV4.x (1 lined, 0 no hull): from
    /// the decal's alpha at the vertex's UV0 (Warrior, Kaiju: the painted face carries no hull) or from the outline_w
    /// sidecar (Farmer_OutlineW.json, rest positions of the unlined vertices, matched within OutlineMatchMetres).
    /// </summary>
    public sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/_Game/Art/";
        public const string ArtMaterialPath = "Assets/_Game/Resources/Toon/CMToonArt.mat";

        /// <summary>Bump when the contract changes: Unity re-imports every model this postprocessor touches.</summary>
        public override uint GetVersion() => 13;   // 13: KayKit deform-bone translations always dropped

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
            mi.materialImportMode = WorldKit.IsEnvironment(assetPath)
                ? ModelImporterMaterialImportMode.ImportViaMaterialDescription
                : ModelImporterMaterialImportMode.None;
            if (WorldKit.IsEnvironment(assetPath) || WorldKit.IsBases(assetPath))
            {
                mi.isReadable = true;
                mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                mi.materialName = ModelImporterMaterialName.BasedOnMaterialName;
                mi.materialSearch = ModelImporterMaterialSearch.Local;
            }

            mi.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            mi.avatarSetup = rigged ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.NoAvatar;
            mi.optimizeGameObjects = false;
            mi.importAnimation = rigged;
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            mi.resampleCurves = true;
        }

        public const float OutlineMatchMetres = 0.002f;

        /// <summary>True for a face decal PNG some ModelSpec names.</summary>
        public static bool IsFaceTexture(string path) =>
            ArtMapping.Models.Any(m => m.FaceTexture == path || (m.Expressions != null && Array.IndexOf(m.Expressions, path) >= 0));

        private void OnPreprocessTexture()
        {
            if (!IsFaceTexture(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.isReadable = false;
        }

        /// <summary>
        /// UV4.x outline weight per vertex of each renderer mesh of a model (ModelSpec.BakesOutlineWeights). Returns, per
        /// mesh name, how many vertices were set unlined (logged; the import tests check the counts).
        /// </summary>
        private void BakeOutlineWeights(GameObject root, ModelSpec spec)
        {
            Texture2D decal = null;
            OutlineSidecar side = null;
            if (spec.OutlineWeights != null)
            {
                context.DependsOnSourceAsset(spec.OutlineWeights);
                if (System.IO.File.Exists(spec.OutlineWeights))
                    side = JsonUtility.FromJson<OutlineSidecar>(System.IO.File.ReadAllText(spec.OutlineWeights));
                if (side == null) Debug.LogWarning($"[ArtImport] {spec.OutlineWeights} missing: {assetPath} keeps every vertex lined");
            }
            else if (spec.FaceTexture != null)
            {
                context.DependsOnSourceAsset(spec.FaceTexture);
                if (System.IO.File.Exists(spec.FaceTexture))
                {
                    decal = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!decal.LoadImage(System.IO.File.ReadAllBytes(spec.FaceTexture))) decal = null;
                }
                if (decal == null) Debug.LogWarning($"[ArtImport] {spec.FaceTexture} unreadable: {assetPath} keeps every vertex lined");
            }
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var w = new List<Vector2>(new Vector2[mesh.vertexCount]);
                for (int i = 0; i < w.Count; i++) w[i] = Vector2.one;
                int unlined = 0;
                if (decal != null)
                {
                    var uv = new List<Vector2>();
                    mesh.GetUVs(0, uv);
                    for (int i = 0; i < uv.Count; i++)
                        if (decal.GetPixelBilinear(uv[i].x, uv[i].y).a > 0.05f) { w[i] = Vector2.zero; unlined++; }
                }
                else if (side != null)
                {
                    OutlineSidecar.Entry e = side.meshes?.FirstOrDefault(x => x.name == r.name);
                    if (e != null && e.unlined != null && e.unlined.Length >= 3)
                    {
                        Vector3[] v = mesh.vertices;
                        // The sidecar is in Unity axes for the art's FBX settings (-x, z, -y). Should an export ever
                        // change the axes or units, the candidate frame that matches the most points wins, and it is logged.
                        int best = -1;
                        bool[] bestHit = null;
                        string bestFrame = null;
                        foreach (var (name, f) in SidecarFrames)
                        {
                            bool[] hit = MatchPoints(v, e.unlined, f, out int listedHit);
                            if (listedHit > best) { best = listedHit; bestHit = hit; bestFrame = name; }
                        }
                        for (int i = 0; i < v.Length; i++) if (bestHit[i]) { w[i] = Vector2.zero; unlined++; }
                        Debug.Log($"[ArtImport] {spec.Key}/{r.name}: sidecar frame {bestFrame}, {best} of {e.unlined.Length / 3} outline_w points matched");
                    }
                }
                mesh.SetUVs(4, w);
                Debug.Log($"[ArtImport] {spec.Key}/{r.name}: outline weights baked, {unlined} of {mesh.vertexCount} vertices unlined");
            }
            if (decal != null) UnityEngine.Object.DestroyImmediate(decal);
        }

        /// <summary>Frames a sidecar point (sx, sy, sz) may need to reach mesh space: the expected one first.</summary>
        static readonly (string, Func<Vector3, Vector3>)[] SidecarFrames =
        {
            ("as-written", p => p),
            ("mirror-x", p => new Vector3(-p.x, p.y, p.z)),
            ("mirror-z", p => new Vector3(p.x, p.y, -p.z)),
            ("mirror-xz", p => new Vector3(-p.x, p.y, -p.z)),
            ("blender-zup", p => new Vector3(-p.x, -p.z, p.y)),
            ("as-written-cm", p => p * 100f),
            ("mirror-x-cm", p => new Vector3(-p.x, p.y, p.z) * 100f),
            ("mirror-xz-cm", p => new Vector3(-p.x, p.y, -p.z) * 100f),
        };

        /// <summary>Mesh vertices within OutlineMatchMetres (scaled with the frame) of a sidecar point; listedHit counts matched points.</summary>
        static bool[] MatchPoints(Vector3[] v, float[] pts, Func<Vector3, Vector3> frame, out int listedHit)
        {
            Vector3 probe = frame(Vector3.one);
            float tol = OutlineMatchMetres * Mathf.Max(1f, Mathf.Abs(probe.y)), cell = tol * 4f;
            var cells = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = Vector3Int.FloorToInt(v[i] / cell);
                if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<int>();
                l.Add(i);
            }
            var hit = new bool[v.Length];
            listedHit = 0;
            for (int k = 0; k + 2 < pts.Length; k += 3)
            {
                Vector3 p = frame(new Vector3(pts[k], pts[k + 1], pts[k + 2]));
                Vector3Int c0 = Vector3Int.FloorToInt(p / cell);
                bool any = false;
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (cells.TryGetValue(c0 + new Vector3Int(dx, dy, dz), out var l))
                        foreach (int i in l)
                            if ((v[i] - p).sqrMagnitude < tol * tol) { hit[i] = true; any = true; }
                if (any) listedHit++;
            }
            return hit;
        }

        [Serializable]
        public sealed class OutlineSidecar
        {
            [Serializable] public sealed class Entry { public string name; public int vertices; public float[] unlined; }
            public string source;
            public Entry[] meshes;
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

        /// <summary>
        /// HY characters (2026-11-13, art/characters-hy): the shared KayKit clips drop every bone translation curve that is
        /// constant over the clip AND equal to the rest position. Measured on all 76 KayKit actions: only root, hips, the hand slots and the IK helpers move
        /// in translation; every deform bone's translation is a constant equal to the KayKit rest. Without those constant
        /// curves each class keeps its OWN joint positions from its FBX bind pose (the Farmer and Miner HY models place
        /// knees, ankles, neck and arm lengths per class), while models on the plain KayKit skeleton look exactly as before
        /// (their bind pose equals the constants that were removed). Rotations are untouched.
        /// </summary>
        public const string KaykitClipsPath = "Assets/_Game/Art/Characters/KayKit_Clips.fbx";

        /// <summary>True when a translation curve set (x, y, z) of one bone is constant over the clip (pure; tests).</summary>
        public static bool IsConstant(AnimationCurve c, float eps = 1e-5f)
        {
            if (c == null || c.length == 0) return true;
            float v0 = c.keys[0].value;
            foreach (Keyframe k in c.keys)
                if (Math.Abs(k.value - v0) > eps) return false;
            return true;
        }

        /// <summary>The KayKit skeleton's deform bones (they carry skin weight; KayKit animates them in rotation only).</summary>
        public static readonly HashSet<string> KaykitDeformBones = new HashSet<string>(StringComparer.Ordinal)
        {
            "spine", "chest", "head", "upperarm.l", "lowerarm.l", "wrist.l", "hand.l", "upperarm.r", "lowerarm.r", "wrist.r", "hand.r",
            "upperleg.l", "lowerleg.l", "foot.l", "toes.l", "upperleg.r", "lowerleg.r", "foot.r", "toes.r",
        };

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (assetPath != KaykitClipsPath || clip == null) return;
            var byPath = new Dictionary<string, List<EditorCurveBinding>>();
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Transform) || !b.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal)) continue;
                if (!byPath.TryGetValue(b.path, out List<EditorCurveBinding> l)) byPath[b.path] = l = new List<EditorCurveBinding>();
                l.Add(b);
            }
            foreach (KeyValuePair<string, List<EditorCurveBinding>> kv in byPath)
            {
                Transform t = root != null ? root.transform.Find(kv.Key) : null;
                if (t == null) continue;
                bool same = true;
                // A KayKit deform bone never moves in translation; some takes (Attack2HChop, ...) still carry a translation a
                // hair off the rest value or jittering by round-off, which the strict test kept: a partly stripped clip then
                // mixes KayKit and per-class joints; DeathB even moves upperleg.l a little. The per-class joint contract wins:
                // every deform-bone translation curve is dropped (rotation carries all KayKit deform-bone motion).
                bool deform = KaykitDeformBones.Contains(kv.Key.Substring(kv.Key.LastIndexOf('/') + 1));
                foreach (EditorCurveBinding b in kv.Value)
                {
                    AnimationCurve c = AnimationUtility.GetEditorCurve(clip, b);
                    if (c == null || c.length == 0) { same = false; break; }
                    if (deform) continue;   // deform bones: always dropped (see above)
                    if (!IsConstant(c)) { same = false; break; }
                    char axis = b.propertyName[b.propertyName.Length - 1];
                    float rest = axis == 'x' ? t.localPosition.x : axis == 'y' ? t.localPosition.y : t.localPosition.z;
                    if (Math.Abs(c.keys[0].value - rest) > 1e-4f) { same = false; break; }   // a held offset is motion: keep
                }
                if (!same) continue;
                foreach (EditorCurveBinding b in kv.Value) AnimationUtility.SetEditorCurve(clip, b, null);
            }
        }

        /// <summary>World art: per-slot look materials, smoothed outline directions (UV3), foam extrusion (UV2).</summary>
        private void PostprocessWorld(GameObject root)
        {
            bool env = WorldKit.IsEnvironment(assetPath);
            // Scenery LODs: env_bridge_span_lod1.fbx is baked like env_bridge_span (looks, wood browns, foam).
            string piece = SceneryLod.BasePiece(System.IO.Path.GetFileNameWithoutExtension(assetPath));
            foreach (WorldLook look in WorldKit.AllLooks) context.DependsOnSourceAsset(WorldKit.AssetPath(look));
            bool warned = false;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                var r = mf.GetComponent<MeshRenderer>();
                if (mesh == null || r == null) continue;
                Material[] src = r.sharedMaterials;
                var mats = new Material[Math.Max(1, mesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++)
                {
                    WorldLook look = env ? WorldKit.EnvLook(piece, i < src.Length && src[i] != null ? src[i].name : null) : WorldKit.BasesLook(mf.name);
                    mats[i] = AssetDatabase.LoadAssetAtPath<Material>(WorldKit.AssetPath(look));
                    if (mats[i] == null && !warned && (warned = true)) Debug.LogWarning($"[ArtImport] {WorldKit.AssetPath(look)} missing while importing {assetPath}: run ProjectSetup.Apply");
                }
                r.sharedMaterials = mats;
                if (piece.StartsWith("env_bridge_", StringComparison.Ordinal)) WorldMeshBake.KitWoodBrowns(mesh);
                WorldMeshBake.SmoothOutlineDirections(mesh);
                if (piece == "env_waterfall") WorldMeshBake.FoamExtrusion(mesh);
            }
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
            if (WorldKit.IsEnvironment(assetPath) || WorldKit.IsBases(assetPath))
            {
                PostprocessWorld(root);
                return;
            }
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
            ModelSpec spec = SpecFor(assetPath);
            if (spec != null && spec.BakesOutlineWeights) BakeOutlineWeights(root, spec);
            // Smoothed hull directions in the (skinned) tangent channel: no torn hull or inner crease lines on the
            // bevelled characters and Kaiju at the gameplay camera (CM/Toon _OutlineSource 3).
            if (spec != null && spec.Rigged && spec.OutlinePx > 0f)
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh != null) WorldMeshBake.SmoothOutlineTangents(mesh);
                }
        }
    }
}

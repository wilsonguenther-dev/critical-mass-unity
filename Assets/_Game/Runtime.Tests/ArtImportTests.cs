using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.View.Art;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// The import contract for the Blender art (2026-11-12): every FBX ArtMapping names imports with the expected settings,
    /// bones, clips, mesh renderers, vertex colour with its alpha mask codes, the shared CM/Toon art material, and its size,
    /// and the KayKit clips bind to the Warrior and Miner rigs by path. Reads assets only.
    /// </summary>
    public sealed class ArtImportTests
    {
        const string ArtMaterialPath = "Assets/_Game/Resources/Toon/CMToonArt.mat";

        static IEnumerable<string> Keys() => ArtMapping.Models.Select(m => m.Key);

        static GameObject Model(ModelSpec spec)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            Assert.IsNotNull(go, spec.AssetPath + " is not imported (run tools/art-sync.sh)");
            return go;
        }

        [TestCaseSource(nameof(Keys))]
        public void ImportsWithTheContractSettings(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            var mi = AssetImporter.GetAtPath(spec.AssetPath) as ModelImporter;
            Assert.IsNotNull(mi, spec.AssetPath + " has no ModelImporter");
            Assert.AreEqual(1f, mi.globalScale, "scale factor");
            Assert.IsTrue(mi.useFileUnits, "convert units");
            Assert.IsTrue(mi.bakeAxisConversion, "bake axis conversion");
            Assert.IsFalse(mi.importCameras, "cameras");
            Assert.IsFalse(mi.importLights, "lights");
            Assert.IsFalse(mi.isReadable, "read/write");
            Assert.AreEqual(ModelImporterNormals.Import, mi.importNormals, "normals");
            Assert.AreEqual(ModelImporterMaterialImportMode.None, mi.materialImportMode, "no imported materials");
            Assert.AreEqual(spec.Rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None, mi.animationType, "rig");
        }

        [TestCaseSource(nameof(Keys))]
        public void HasTheExpectedBones(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            GameObject go = Model(spec);
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int bones;
            if (smrs.Length > 0) bones = smrs.SelectMany(s => s.bones).Where(b => b != null).Distinct().Count();
            else
            {
                Transform rig = go.transform.Find("Rig");
                bones = rig != null ? rig.GetComponentsInChildren<Transform>(true).Length - 1 : 0;
            }
            Assert.AreEqual(spec.Bones, bones, "bones");
        }

        [TestCaseSource(nameof(Keys))]
        public void CarriesItsClipsWithLoopTimeOnTheLoopingOnes(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            Model(spec);
            AnimationClip[] own = AssetDatabase.LoadAllAssetsAtPath(spec.AssetPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            CollectionAssert.IsSubsetOf(spec.OwnClips, own.Select(c => c.name).ToArray(), "own clips");
            if (!spec.Rigged) Assert.AreEqual(0, own.Length, "a static mesh imports no clips");
            foreach (AnimationClip c in own)
                Assert.AreEqual(ArtMapping.LoopingClips.Contains(c.name), c.isLooping, c.name + " loop time");
            if (spec.ClipSource != null)
            {
                string[] shared = AssetDatabase.LoadAllAssetsAtPath(spec.ClipSource).OfType<AnimationClip>().Select(c => c.name).ToArray();
                CollectionAssert.IsSubsetOf(new[] { ArtMapping.Idle, ArtMapping.Walk }, shared, "clips from " + spec.ClipSource);
            }
        }

        [TestCaseSource(nameof(Keys))]
        public void MeshesCarryVertexColourWithTheirMaskCodesOnTheArtMaterial(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            GameObject go = Model(spec);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ArtMaterialPath);
            Assert.IsNotNull(mat, ArtMaterialPath);
            Assert.AreEqual(1f, mat.GetFloat("_ColorMode"), "the art material reads vertex colour");
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            CollectionAssert.AreEquivalent(spec.Meshes, rs.Select(r => r.name).ToArray(), "mesh renderers");
            var codes = new HashSet<int>();
            foreach (Renderer r in rs)
            {
                CollectionAssert.IsNotEmpty(r.sharedMaterials, r.name + " materials");
                foreach (Material m in r.sharedMaterials) Assert.AreSame(mat, m, r.name + " material");
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh, r.name + " mesh");
                Assert.IsTrue(mesh.HasVertexAttribute(VertexAttribute.Color), r.name + " has no vertex colour");
                Assert.IsTrue(mesh.HasVertexAttribute(VertexAttribute.Normal), r.name + " has no normals");
                foreach (Color c in mesh.colors) codes.Add(Mathf.RoundToInt(c.a * 8f));
            }
            CollectionAssert.IsSubsetOf(spec.MaskCodes, codes.ToArray(), "mask codes present (eighths of alpha): " + string.Join(",", codes));
            foreach (int code in codes) CollectionAssert.Contains(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, code, "unknown mask code");
        }

        [TestCaseSource(nameof(Keys))]
        public void IsTheBibleHeightWithItsFeetOnTheGround(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            if (spec.Height <= 0f) Assert.Pass("no height target");
            GameObject go = Model(spec);
            Renderer body = go.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == spec.Meshes[0]);
            Assert.IsNotNull(body, spec.Meshes[0]);
            Mesh mesh = body is SkinnedMeshRenderer smr ? smr.sharedMesh : body.GetComponent<MeshFilter>().sharedMesh;
            // Bind-pose mesh bounds through the renderer's transform (the T-pose / rest pose, not an animated frame).
            Matrix4x4 m = body.transform.localToWorldMatrix;
            Bounds lb = mesh.bounds;
            var b = new Bounds(m.MultiplyPoint3x4(lb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(m.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            float tol = Mathf.Max(ModelSpec.HeightTolerance, 0.06f * spec.Height);
            Assert.AreEqual(spec.Height, b.size.y, tol, "height (m) of " + body.name);
            Assert.AreEqual(0f, b.min.y, 0.1f, "pivot on the ground");
        }

        [TestCase(ArtMapping.Warrior)]
        [TestCase(ArtMapping.Miner)]
        public void TheKaykitClipsBindToTheRigByPath(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            GameObject go = Model(spec);
            foreach (AnimationClip c in AssetDatabase.LoadAllAssetsAtPath(spec.ClipSource).OfType<AnimationClip>()
                         .Where(c => c.name == ArtMapping.Idle || c.name == ArtMapping.Walk))
            {
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(c);
                Assert.Greater(bindings.Length, 40, c.name + " curves");
                string[] missing = bindings.Select(b => b.path).Distinct().Where(p => p.Length > 0 && go.transform.Find(p) == null).ToArray();
                CollectionAssert.IsEmpty(missing, c.name + " paths missing on " + key);
            }
        }

        [Test]
        public void TheCatalogMapsEveryImportedModelWithItsClips()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath);
            Assert.IsNotNull(cat, ArtCatalog.AssetPath + " (run ProjectSetup.Apply)");
            foreach (ModelSpec spec in ArtMapping.Models)
            {
                ArtEntry e = cat.Find(spec.Key);
                Assert.IsNotNull(e, spec.Key + " not in the catalog");
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath), e.Model, spec.Key + " model");
                string[] need = spec.ClipSource != null ? new[] { ArtMapping.Idle, ArtMapping.Walk } : spec.OwnClips;
                CollectionAssert.IsSubsetOf(need, e.Clips.Select(c => c.name).ToArray(), spec.Key + " clips");
            }
        }
    }
}

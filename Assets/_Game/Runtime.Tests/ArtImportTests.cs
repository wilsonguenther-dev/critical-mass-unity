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
            // feat/staged-building-1: the ladder, bridge and barricade are cut into parts at match start (CPU vertices)
            Assert.AreEqual(CriticalMass.Runtime.View.Mechanics.StagedBuildCore.NeedsReadable(key), mi.isReadable, "read/write");
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
        [TestCase(ArtMapping.Farmer)]
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

        static IEnumerable<string> FaceKeys() => ArtMapping.Models.Where(m => m.FaceTexture != null).Select(m => m.Key);
        static IEnumerable<string> OutlineKeys() => ArtMapping.Models.Where(m => m.BakesOutlineWeights).Select(m => m.Key);

        static Mesh BodyMesh(ModelSpec spec)
        {
            Renderer body = Model(spec).GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == spec.Meshes[0]);
            Assert.IsNotNull(body, spec.Meshes[0]);
            return body is SkinnedMeshRenderer smr ? smr.sharedMesh : body.GetComponent<MeshFilter>().sharedMesh;
        }

        static Texture2D ReadPng(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(t.LoadImage(System.IO.File.ReadAllBytes(path)), path);
            return t;
        }

        /// <summary>Art refresh 2: every face decal imports clamped sRGB with alpha and rides in the catalog entry.</summary>
        [TestCaseSource(nameof(FaceKeys))]
        public void FaceDecalImportsClampedSrgbAndIsInTheCatalog(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            var ti = AssetImporter.GetAtPath(spec.FaceTexture) as TextureImporter;
            Assert.IsNotNull(ti, spec.FaceTexture + " is not imported (run tools/art-sync.sh)");
            Assert.IsTrue(ti.sRGBTexture, "sRGB");
            Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapMode, "clamped");
            Assert.IsTrue(ti.DoesSourceTextureHaveAlpha(), "the decal mixes by its alpha");
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Texture2D>(spec.FaceTexture), cat.Find(key).Face, "catalog face");
            CollectionAssert.IsNotEmpty(GetUv(BodyMesh(spec), 0), spec.Meshes[0] + " carries the decal UVs");
        }

        /// <summary>Art refresh 2: UV4.x is 0 on the face (decal alpha or the outline_w sidecar) and 1 elsewhere.</summary>
        [TestCaseSource(nameof(OutlineKeys))]
        public void FaceVerticesCarryNoOutlineWeight(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            Mesh mesh = BodyMesh(spec);
            List<Vector2> w = GetUv(mesh, 4);
            Assert.AreEqual(mesh.vertexCount, w.Count, "UV4 outline weights baked on " + spec.Meshes[0]);
            int unlined = w.Count(v => v.x < 0.5f);
            Assert.Greater(unlined, 0, "the face is unlined");
            Assert.Less(unlined, w.Count / 4, "only the face is unlined");
            CollectionAssert.IsSubsetOf(w.Select(v => v.x).Distinct().ToArray(), new[] { 0f, 1f }, "weights are 0 or 1");
            if (spec.OutlineWeights != null)
            {
                var side = JsonUtility.FromJson<Sidecar>(System.IO.File.ReadAllText(spec.OutlineWeights));
                int listed = side.meshes.First(m => m.name == spec.Meshes[0]).unlined.Length / 3;
                // FBX splits a Blender vertex per face corner normal, so Unity has at least as many unlined vertices.
                Assert.GreaterOrEqual(unlined, listed, "every outline_w 0 vertex of the .blend matched");
            }
            else
            {
                Texture2D decal = ReadPng(spec.FaceTexture);
                List<Vector2> uv = GetUv(mesh, 0);
                for (int i = 0; i < uv.Count; i++)
                    Assert.AreEqual(decal.GetPixelBilinear(uv[i].x, uv[i].y).a > 0.05f ? 0f : 1f, w[i].x, "vertex " + i + " weight follows the decal alpha");
                Object.DestroyImmediate(decal);
            }
        }

        /// <summary>The Miner's decal is its own material slot (CM_FaceDecal): that submesh is the painted face.</summary>
        [Test]
        public void TheMinerFaceSlotIsTheDecalSubmesh()
        {
            ModelSpec spec = ArtMapping.Spec(ArtMapping.Miner);
            Mesh mesh = BodyMesh(spec);
            Assert.AreEqual(2, mesh.subMeshCount, "Miner_Body: CM_Toon + CM_FaceDecal");
            Texture2D decal = ReadPng(spec.FaceTexture);
            List<Vector2> uv = GetUv(mesh, 0);
            int[] idx = mesh.GetTriangles(spec.FaceSlot).Distinct().ToArray();
            int painted = 0;
            float minX = 1, minY = 1, maxX = 0, maxY = 0;
            foreach (int i in idx)
            {
                minX = Mathf.Min(minX, uv[i].x); maxX = Mathf.Max(maxX, uv[i].x);
                minY = Mathf.Min(minY, uv[i].y); maxY = Mathf.Max(maxY, uv[i].y);
            }
            // The slab's UVs span the decal (the painted features sit inside it, not on every corner).
            Assert.Greater(maxX - minX, 0.5f, "face slot UVs span the decal");
            for (int k = 0; k < 400; k++)
                if (decal.GetPixelBilinear(minX + (maxX - minX) * (k % 20 + 0.5f) / 20f, minY + (maxY - minY) * (k / 20 + 0.5f) / 20f).a > 0.05f) painted++;
            Assert.Greater(painted, 40, "the decal paints the face slot");
            Object.DestroyImmediate(decal);
        }

        /// <summary>The outline_w sidecar's JsonUtility form (tools/art/outline_weights.py).</summary>
        [System.Serializable] sealed class Sidecar
        {
            [System.Serializable] public sealed class Entry { public string name; public int vertices; public float[] unlined; }
            public Entry[] meshes;
        }

        static List<Vector2> GetUv(Mesh mesh, int channel)
        {
            var l = new List<Vector2>();
            mesh.GetUVs(channel, l);
            return l;
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

using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// The HY character swap (art/characters-hy, 2026-11-13): every model marked ModelSpec.VertexColourFace is the Tencent
    /// HY 3D Global export. Its class (or Kaiju stage) resolves to it through ArtMapping, it carries the rig's bone count, the
    /// LOD contract's four meshes within their triangle budgets (fighters: Hero 15k to 25k, LOD0 4.5k, LOD1 1.5k, Shadow 800;
    /// Kaiju adult: Hero 40k, LOD0 9k, LOD1 4.5k, Shadow 1.5k), vertex colour, and UV0 on the one transparent decal texel
    /// that marks the HY export. The shared KayKit clips no longer pin deform-bone translations, so per-class joints hold.
    /// Reads assets only.
    /// </summary>
    public sealed class HyCharacterSwapTests
    {
        static IEnumerable<string> HyKeys() => ArtMapping.Models.Where(m => m.VertexColourFace).Select(m => m.Key);

        [Test]
        public void TheWarriorIsSwapped()
        {
            CollectionAssert.Contains(HyKeys().ToArray(), ArtMapping.Warrior);
            Assert.AreEqual(ArtMapping.Warrior, ArtMapping.ClassModel(ClassId.Warrior));
        }

        static int Tris(Mesh m)
        {
            int t = 0;
            for (int i = 0; i < m.subMeshCount; i++) t += (int)m.GetIndexCount(i) / 3;
            return t;
        }

        [TestCaseSource(nameof(HyKeys))]
        public void ResolvesToTheHyModelWithItsBonesAndTriangleBudgets(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            Assert.IsNull(spec.FaceTexture, key + ": an HY face is vertex colour, no decal");
            Assert.AreEqual(-1, spec.FaceSlot, key + ": no decal slot");
            Assert.IsNull(spec.OutlineWeights, key + ": no outline sidecar (its vertex indices belong to the pre-HY mesh)");
            bool kaiju = spec.Kind == ArtKind.Kaiju;
            if (!kaiju)
                Assert.IsTrue(((ClassId[])Enum.GetValues(typeof(ClassId))).Any(c => ArtMapping.ClassModel(c) == key), key + " is a class model");
            else
                Assert.AreEqual(key, ArtMapping.KaijuModel(KaijuState.Neutral, ArtMapping.HatchlingSeconds + 1.0), key + " is the adult stage");

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            Assert.IsNotNull(go, spec.AssetPath + " is not imported");
            SkinnedMeshRenderer[] smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.AreEqual(spec.Bones, smrs.SelectMany(s => s.bones).Where(b => b != null).Distinct().Count(), key + " bones");

            Mesh MeshOf(string name)
            {
                SkinnedMeshRenderer r = smrs.FirstOrDefault(s => s.name == name);
                Assert.IsNotNull(r, key + " renderer " + name);
                return r.sharedMesh;
            }

            string b = FighterLod.BaseName(spec.Meshes[0]);
            Mesh hero = MeshOf(b + FighterLod.HeroSuffix), lod0 = MeshOf(b + FighterLod.Lod0Suffix),
                lod1 = MeshOf(b + FighterLod.Lod1Suffix), shadow = MeshOf(b + FighterLod.ShadowSuffix);
            if (kaiju)
            {
                Assert.LessOrEqual(Tris(hero), 40000, "Kaiju Hero");
                Assert.LessOrEqual(Tris(lod0), 9000, "Kaiju match mesh");
                Assert.LessOrEqual(Tris(lod1), 4500, "Kaiju LOD1");
                Assert.LessOrEqual(Tris(shadow), FighterLod.KaijuShadowTriangles, "Kaiju shadow");
            }
            else
            {
                Assert.GreaterOrEqual(Tris(hero), FighterLod.HeroMinTriangles, "Hero");
                Assert.LessOrEqual(Tris(hero), FighterLod.HeroMaxTriangles, "Hero");
                Assert.LessOrEqual(Tris(lod0), FighterLod.Lod0Triangles, "match mesh (LOD0)");
                Assert.LessOrEqual(Tris(lod1), FighterLod.Lod1Triangles, "LOD1");
                Assert.LessOrEqual(Tris(shadow), FighterLod.ShadowTriangles, "shadow");
            }
            foreach (Mesh m in new[] { hero, lod0, lod1 })
            {
                Assert.IsTrue(m.HasVertexAttribute(VertexAttribute.Color), m.name + " vertex colour");
                var uv = new List<Vector2>();
                m.GetUVs(0, uv);
                Assert.IsTrue(uv.Count > 0 && uv.All(u => Mathf.Abs(u.x - 0.01f) < 1e-3f && Mathf.Abs(u.y - 0.01f) < 1e-3f),
                    m.name + ": UV0 on the transparent texel (the HY export, not the pre-HY mesh)");
            }
        }

        [Test]
        public void KaykitClipsLeaveDeformBoneTranslationToEachModel()
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ArtImportPostprocessorPath.Kaykit).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            Assert.IsNotEmpty(clips, "KayKit_Clips.fbx clips");
            string[] deform = { "spine", "chest", "head", "upperarm.l", "lowerarm.r", "upperleg.l", "lowerleg.r", "foot.l" };
            foreach (AnimationClip c in clips)
                foreach (EditorCurveBinding bnd in AnimationUtility.GetCurveBindings(c))
                {
                    if (!bnd.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal)) continue;
                    string bone = bnd.path.Substring(bnd.path.LastIndexOf('/') + 1);
                    CollectionAssert.DoesNotContain(deform, bone, c.name + ": constant translation of " + bnd.path + " pins the KayKit joint");
                }
        }

        // ---- art/characters-hy-rig (2026-11-13): the rigging pass ------------------------------------------------------
        static IEnumerable<string> RiggedHyKeys() => HyKeys().Concat(new[] { ArtMapping.KaijuAdult }).Distinct();

        static SkinnedMeshRenderer[] Renderers(ModelSpec spec)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            Assert.IsNotNull(go, spec.AssetPath + " is not imported");
            return go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        /// <summary>The rigging pass keeps every vertex at most four bone influences that sum to one (Unity's default skin
        /// weights setting), on every LOD and the shadow.</summary>
        [TestCaseSource(nameof(RiggedHyKeys))]
        public void AtMostFourNormalisedBoneInfluencesPerVertex(string key)
        {
            foreach (SkinnedMeshRenderer r in Renderers(ArtMapping.Spec(key)))
            {
                Mesh m = r.sharedMesh;
                var perVertex = m.GetBonesPerVertex();
                var weights = m.GetAllBoneWeights();
                Assert.AreEqual(m.vertexCount, perVertex.Length, r.name + " carries skin weights");
                int at = 0;
                for (int v = 0; v < perVertex.Length; v++)
                {
                    int n = perVertex[v];
                    Assert.LessOrEqual(n, 4, r.name + " vertex " + v + " influences");
                    Assert.Greater(n, 0, r.name + " vertex " + v + " is unweighted");
                    float s = 0f;
                    for (int k = 0; k < n; k++) s += weights[at + k].weight;
                    at += n;
                    Assert.AreEqual(1f, s, 2e-3f, r.name + " vertex " + v + " weights sum");
                }
            }
        }

        /// <summary>Bind-pose height of the Hero matches the spec's height convention (fighters 2.0 u, the adult 5.0 u).</summary>
        [TestCaseSource(nameof(RiggedHyKeys))]
        public void HeroBindPoseHeightMatchesTheSpec(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            GameObject inst = UnityEngine.Object.Instantiate(go);
            try
            {
                SkinnedMeshRenderer hero = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .First(s => s.name == FighterLod.BaseName(spec.Meshes[0]) + FighterLod.HeroSuffix);
                var baked = new Mesh();
                hero.BakeMesh(baked, true);
                Matrix4x4 m = Matrix4x4.TRS(hero.transform.position, hero.transform.rotation, Vector3.one);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (Vector3 v in baked.vertices)
                {
                    float y = m.MultiplyPoint3x4(v).y;
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                }
                UnityEngine.Object.DestroyImmediate(baked);
                Assert.AreEqual(spec.Height, hi - lo, 0.08f * spec.Height, key + " Hero bind-pose height");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        /// <summary>The Kaiju adult is the HY model (38k-triangle Hero; the pre-HY adult had 12k) on its own 20-bone rig
        /// with the joints refit to the HY body, clips re-baked from Kaiju.blend. It keeps the face-decal path: a decal UV fit
        /// on the snout (the expression atlases and the ModelFacing face check read it), every other corner on the
        /// transparent texel.</summary>
        [Test]
        public void TheKaijuAdultIsTheHyModelWithAFaceDecalFit()
        {
            ModelSpec spec = ArtMapping.Spec(ArtMapping.KaijuAdult);
            Assert.IsFalse(spec.VertexColourFace, "the adult keeps the face decal (expressions)");
            Assert.IsNotNull(spec.FaceTexture, "Kaiju_Face.png");
            Assert.IsNotNull(spec.Expressions, "expression atlases");
            SkinnedMeshRenderer[] smrs = Renderers(spec);
            Assert.AreEqual(spec.Bones, smrs.SelectMany(s => s.bones).Where(b => b != null).Distinct().Count(), "Kaiju bones");
            string b = FighterLod.BaseName(spec.Meshes[0]);
            Mesh MeshOf(string suffix)
            {
                SkinnedMeshRenderer r = smrs.FirstOrDefault(s => s.name == b + suffix);
                Assert.IsNotNull(r, "Kaiju renderer " + b + suffix);
                return r.sharedMesh;
            }
            Mesh hero = MeshOf(FighterLod.HeroSuffix), lod0 = MeshOf(FighterLod.Lod0Suffix),
                lod1 = MeshOf(FighterLod.Lod1Suffix), shadow = MeshOf(FighterLod.ShadowSuffix);
            Assert.Greater(Tris(hero), 20000, "HY Hero");
            Assert.LessOrEqual(Tris(hero), 40000, "Kaiju Hero");
            Assert.LessOrEqual(Tris(lod0), 9000, "Kaiju match mesh");
            Assert.LessOrEqual(Tris(lod1), 4500, "Kaiju LOD1");
            Assert.LessOrEqual(Tris(shadow), FighterLod.KaijuShadowTriangles, "Kaiju shadow");
            foreach (Mesh m in new[] { hero, lod0, lod1 })
            {
                Assert.IsTrue(m.HasVertexAttribute(VertexAttribute.Color), m.name + " vertex colour");
                var uv = new List<Vector2>();
                m.GetUVs(0, uv);
                int face = uv.Count(u => Mathf.Abs(u.x - 0.01f) > 1e-3f || Mathf.Abs(u.y - 0.01f) > 1e-3f);
                Assert.Greater(face, 20, m.name + ": decal UVs on the snout");
                Assert.Less(face, uv.Count / 4, m.name + ": only the face carries decal UVs");
            }
        }

        static class ArtImportPostprocessorPath
        {
            public const string Kaykit = "Assets/_Game/Art/Characters/KayKit_Clips.fbx";
        }
    }
}

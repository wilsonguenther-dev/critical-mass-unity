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

        static class ArtImportPostprocessorPath
        {
            public const string Kaykit = "Assets/_Game/Art/Characters/KayKit_Clips.fbx";
        }
    }
}

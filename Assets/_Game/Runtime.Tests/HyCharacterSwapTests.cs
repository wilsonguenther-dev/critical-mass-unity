using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.View.Anim;
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
        static IEnumerable<string> RiggedHyKeys() => HyKeys();   // the HY Kaiju adult is still staged (Kaiju/_hy~)

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

        /// <summary>Bind-pose height of the Hero matches the spec's height convention (fighters 2.0 u).</summary>
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

        /// <summary>
        /// rigs-2 (2026-11-13): feet on the ground over a full cycle of the clips a class plays most, measured on the
        /// imported model under the shared KayKit clips exactly as Unity applies them (SampleAnimation, absolute curves).
        /// Ground error per frame = the lowest Hero vertex (BakeMesh) in the model root's space; the root sits on the ground.
        /// Slide = planted-foot slip per unit of travel at the class's in-game ground speed (ClipFacts x StrideScale), over
        /// 8 full cycles at 97 samples per cycle (a long sample: short ones passed falsely twice).
        /// </summary>
        public const float MaxGroundError = 0.03f;
        public const float MaxClipSlide = 0.3f;

        [Test]
        public void FeetStayOnTheGroundAndPlantedFeetHoldUnderTheSharedClips()
        {
            ArtCatalog cat = ArtCatalog.Load();
            Assert.IsNotNull(cat, "no ArtCatalog in Resources");
            var keys = new List<string>(HyKeys().Where(k => ArtMapping.Spec(k) != null && ArtMapping.Spec(k).Kind == ArtKind.Character));
            keys.Insert(0, ArtMapping.Builder);   // KayKit-joint reference, first: its hips rest is the clips' rest
            Vector3 kkHips = Vector3.zero;
            var bad = new List<string>();
            var rep = new System.Text.StringBuilder();
            foreach (string key in keys)
            {
                ArtEntry e = cat.Find(key);
                ModelSpec spec = ArtMapping.Spec(key);
                if (e == null || spec == null) { bad.Add(key + ": no model"); continue; }
                GameObject go = UnityEngine.Object.Instantiate(e.Model);
                try
                {
                    SkinnedMeshRenderer hero = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == spec.Meshes[0]);
                    Transform hips = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "hips");
                    Transform tl = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "toes.l");
                    Transform tr = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "toes.r");
                    if (hero == null || hips == null || tl == null || tr == null) { bad.Add(key + ": no Hero/hips/toes"); continue; }
                    Vector3 hipsRest = hips.localPosition;
                    if (key == ArtMapping.Builder) kkHips = hipsRest;
                    float ratio = spec.StrideScale > 0 ? spec.StrideScale : 1f;
                    float restLow = LowestY(go.transform, hero);
                    rep.Append($"{key}: rest lowest vertex {restLow:0.000}, hips rest local {hipsRest.x:0.000},{hipsRest.y:0.000},{hipsRest.z:0.000}, root scale {go.transform.lossyScale.y:0.###}\n");
                    foreach (string c in new[] { ArtMapping.Idle, ArtMapping.Walk, ArtMapping.Run })
                    {
                        AnimationClip clip = e.Clip(c);
                        if (clip == null) { bad.Add($"{key} {c}: no clip"); continue; }
                        const int N = 97;
                        float lo = float.MaxValue, hi = float.MinValue, loB = float.MaxValue, hiB = float.MinValue;
                        var pl = new Vector3[N + 1]; var pr = new Vector3[N + 1];
                        for (int k = 0; k <= N; k++)
                        {
                            clip.SampleAnimation(go, clip.length * k / N);
                            float y = LowestY(go.transform, hero);
                            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                            pl[k] = go.transform.InverseTransformPoint(tl.position);
                            pr[k] = go.transform.InverseTransformPoint(tr.position);
                            hips.localPosition = hipsRest + (hips.localPosition - kkHips) * ratio;   // what-if: hips as a scaled delta
                            float yB = LowestY(go.transform, hero);
                            loB = Mathf.Min(loB, yB); hiB = Mathf.Max(hiB, yB);
                        }
                        clip.SampleAnimation(go, 0f);
                        Vector3 hips0 = hips.localPosition;
                        float err = Mathf.Max(Mathf.Abs(lo), Mathf.Abs(hi));
                        string line = $"  {c} ({clip.length:0.00} s): lowest vertex per frame {lo:0.000}..{hi:0.000} (worst |err| {err:0.000}), hips@0 {hips0.x:0.000},{hips0.y:0.000},{hips0.z:0.000}; hips as scaled delta: {loB:0.000}..{hiB:0.000}";
                        if (err > MaxGroundError) bad.Add($"{key} {c}: feet off the ground by up to {err:0.000} (lowest vertex {lo:0.000}..{hi:0.000})");
                        if (c != ArtMapping.Idle)
                        {
                            float v = ClipFacts.GroundSpeed(c, c == ArtMapping.Run ? 4.57f : 0.86f) * (spec.StrideScale > 0 ? spec.StrideScale : 1f);
                            float slide = Slide(pl, pr, clip.length, v, 8);
                            line += $", ground speed {v:0.00} m/s, planted slide {slide:0.000} u/u";
                            if (slide > MaxClipSlide) bad.Add($"{key} {c}: planted foot slides {slide:0.000} u/u at {v:0.00} m/s");
                        }
                        rep.Append(line).Append('\n');
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            Debug.Log("[HyFeet]\n" + rep);
            Assert.IsEmpty(bad, string.Join("\n", bad) + "\n---\n" + rep);
        }

        static float LowestY(Transform root, SkinnedMeshRenderer r)
        {
            var baked = new Mesh();
            r.BakeMesh(baked, true);
            Matrix4x4 m = root.worldToLocalMatrix * Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
            float lo = float.MaxValue;
            foreach (Vector3 v in baked.vertices) lo = Mathf.Min(lo, m.MultiplyPoint3x4(v).y);
            UnityEngine.Object.DestroyImmediate(baked);
            return lo;
        }

        /// <summary>Planted-foot slip per unit travelled over `cycles` loops: the body moves at v along the direction the
        /// planted toes move backward; a toe is planted while within 0.03 of its own lowest height in the cycle.</summary>
        static float Slide(Vector3[] pl, Vector3[] pr, float len, float v, int cycles)
        {
            int n = pl.Length - 1;
            float dt = len / n;
            Vector3 back = Vector3.zero;
            foreach (Vector3[] p in new[] { pl, pr })
            {
                float min = p.Min(q => q.y);
                for (int k = 0; k < n; k++) if (p[k].y < min + 0.03f && p[k + 1].y < min + 0.03f) back += new Vector3(p[k + 1].x - p[k].x, 0, p[k + 1].z - p[k].z);
            }
            if (back.sqrMagnitude < 1e-10f) return 99f;
            Vector3 fwd = -back.normalized;
            double slip = 0;
            for (int cyc = 0; cyc < cycles; cyc++)
                foreach (Vector3[] p in new[] { pl, pr })
                {
                    float min = p.Min(q => q.y);
                    for (int k = 0; k < n; k++)
                        if (p[k].y < min + 0.03f && p[k + 1].y < min + 0.03f)
                        {
                            Vector3 d = new Vector3(p[k + 1].x - p[k].x, 0, p[k + 1].z - p[k].z) + fwd * (v * dt);
                            slip += d.magnitude;
                        }
                }
            return (float)(slip / (v * len * cycles));
        }

        static class ArtImportPostprocessorPath
        {
            public const string Kaykit = "Assets/_Game/Art/Characters/KayKit_Clips.fbx";
        }
    }
}

using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b12 r3 (Air pictures of a07ee1d): the textured Warrior and Builder pilots (-warriorTex 1 / -builderTex 1) drew at
    /// gameplay distance and in the line-up but were EMPTY in every close-up still. Cause: they ship LOD0, LOD1 and Shadow
    /// only, and a close camera (ModelView.SetHero) drew the _Hero mesh and nothing else, so no renderer was on; the capture
    /// then measured a 0.5 u, zero-width model and framed the walk strips on the shins. Each test prints one line per case.
    /// </summary>
    public sealed class CloseUpDrawTests
    {
        static IEnumerable<string> CharacterKeys()
        {
            foreach (ModelSpec s in ArtMapping.Models)
                if (s.Kind == ArtKind.Character && s.Meshes != null && s.Meshes.Length > 0) yield return s.Key;
        }

        /// <summary>Every level the close-up path asks for (SetHero true) draws at least one body mesh, for every character spec.</summary>
        [TestCaseSource(nameof(CharacterKeys))]
        public void TheCloseUpPathDrawsABodyForEveryCharacter(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            bool hasHero = false, hasLod0 = false, hasLod1 = false;
            foreach (string mesh in spec.Meshes)
            {
                FighterLod.Role r = FighterLod.RoleOf(mesh);
                hasHero |= r == FighterLod.Role.Hero;
                hasLod0 |= r == FighterLod.Role.Lod0;
                hasLod1 |= r == FighterLod.Role.Lod1;
            }
            var drawn = new List<string>();
            foreach (string mesh in spec.Meshes)
            {
                FighterLod.Role r = FighterLod.RoleOf(mesh);
                if (r == FighterLod.Role.Shadow) continue;
                if (FighterLod.Draws(r, true, 1, hasLod0, hasLod1, hasHero)) drawn.Add(mesh);
            }
            Debug.Log("[CloseUpDraw] " + key + ": meshes " + string.Join(",", spec.Meshes) + "; Hero " + hasHero + "; close-up draws "
                      + (drawn.Count > 0 ? string.Join(",", drawn) : "NOTHING") + " (limit: at least 1)");
            Assert.Greater(drawn.Count, 0, key + ": a close camera draws no body mesh");
            if (!hasHero && hasLod0) CollectionAssert.Contains(drawn, spec.Meshes[System.Array.FindIndex(spec.Meshes, m => FighterLod.RoleOf(m) == FighterLod.Role.Lod0)],
                key + ": without a Hero mesh the close-up shows LOD0");
        }

        [Test]
        public void AModelWithAHeroStillShowsOnlyTheHeroUpClose()
        {
            Assert.IsTrue(FighterLod.Draws(FighterLod.Role.Hero, true, 0, true, true, true));
            Assert.IsFalse(FighterLod.Draws(FighterLod.Role.Lod0, true, 0, true, true, true));
            Assert.IsFalse(FighterLod.Draws(FighterLod.Role.Lod1, true, 1, true, true, true));
            Assert.IsTrue(FighterLod.Draws(FighterLod.Role.Lod0, true, 1, true, true, false), "no Hero: LOD0 up close even when PickLod said 1");
            Assert.IsFalse(FighterLod.Draws(FighterLod.Role.Lod1, true, 1, true, true, false));
            Debug.Log("[CloseUpDraw] Hero model: Hero on, LOD0/LOD1 off up close; Hero-less model: LOD0 on, LOD1 off up close");
        }

        static IEnumerable<string> PilotKeys() => new[] { ArtMapping.WarriorTex, ArtMapping.BuilderTex, ArtMapping.Warrior, ArtMapping.Builder };

        /// <summary>
        /// The imported renderer bounds of each body mesh are not degenerate and contain every skin bone at the rest pose (a
        /// bounds error would cull the model when the camera is near). Height limit: 0.6x..1.4x ModelSpec.Height.
        /// </summary>
        [TestCaseSource(nameof(PilotKeys))]
        public void BodyBoundsHoldTheSkeletonAtRest(string key)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            Assert.IsNotNull(model, spec.AssetPath + " is not imported");
            GameObject inst = Object.Instantiate(model);
            try
            {
                int checkedBodies = 0;
                foreach (SkinnedMeshRenderer smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    FighterLod.Role role = FighterLod.RoleOf(smr.name);
                    if (role == FighterLod.Role.Shadow || role == FighterLod.Role.Shared) continue;
                    Bounds b = smr.bounds;
                    Bounds grown = b;
                    grown.Expand(0.3f);
                    int outside = 0, bones = 0;
                    foreach (Transform bone in smr.bones)
                    {
                        if (bone == null) continue;
                        bones++;
                        if (!grown.Contains(bone.position)) outside++;
                    }
                    Debug.Log("[CloseUpDraw] " + key + " " + smr.name + ": bounds centre " + b.center.ToString("F2") + " size " + b.size.ToString("F2")
                              + " (height limit " + (0.6f * spec.Height).ToString("F2") + ".." + (1.4f * spec.Height).ToString("F2") + "), bones outside bounds+0.3 u: "
                              + outside + " of " + bones + " (limit 0), sharedMesh " + (smr.sharedMesh != null ? smr.sharedMesh.vertexCount + " verts" : "MISSING"));
                    Assert.IsNotNull(smr.sharedMesh, key + " " + smr.name + ": no mesh");
                    Assert.GreaterOrEqual(b.size.y, 0.6f * spec.Height, key + " " + smr.name + ": bounds too short");
                    Assert.LessOrEqual(b.size.y, 1.4f * spec.Height, key + " " + smr.name + ": bounds too tall");
                    Assert.Greater(b.size.x, 0.2f, key + " " + smr.name + ": bounds have no width");
                    Assert.AreEqual(0, outside, key + " " + smr.name + ": skin bones lie outside the renderer bounds");
                    checkedBodies++;
                }
                Assert.Greater(checkedBodies, 0, key + ": no skinned body mesh found");
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }
    }
}

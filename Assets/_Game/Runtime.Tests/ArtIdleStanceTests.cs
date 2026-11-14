using System;
using System.Linq;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// rig-miner-2 / rig-warrior-2 (2026-11-14): bake checks seen in the Unity stills. The Miner's idle stood on tiptoe
    /// (feet pitched toes-down against the bind) and leaned forward while the walk was planted; the Warrior's sword tip
    /// dipped to the floor in the walk. Each class model is posed by its own clips, sampled the way the game binds them
    /// (by bone path from the model root), in model units with the root at the origin.
    /// </summary>
    public sealed class ArtIdleStanceTests
    {
        const float MaxIdleFootPitchChangeDeg = 20f;   // idle foot (ankle to toes) pitch against the bind pose
        const float MaxIdleLeanDeg = 12f;              // hips-to-head line against straight up in idle
        const float MinWeaponTipClearance = 0.15f;     // the Warrior's held weapon above the ground in walk and run

        static ClassId[] Classes() => (ClassId[])Enum.GetValues(typeof(ClassId));

        static GameObject Spawn(ClassId cls, out ArtEntry entry, out ModelSpec spec)
        {
            string key = ArtMapping.ClassModel(cls);
            spec = ArtMapping.Spec(key);
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath);
            Assert.IsNotNull(cat, ArtCatalog.AssetPath + " (run ProjectSetup.Apply)");
            entry = cat.Find(key);
            Assert.IsNotNull(entry, key + " not in the catalog");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            Assert.IsNotNull(prefab, spec.AssetPath);
            GameObject go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return go;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = Find(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static float Pitch(Transform root, Transform foot, Transform toes)
        {
            Vector3 d = root.InverseTransformPoint(toes.position) - root.InverseTransformPoint(foot.position);
            float h = new Vector2(d.x, d.z).magnitude;
            return Mathf.Atan2(d.y, Mathf.Max(1e-6f, h)) * Mathf.Rad2Deg;
        }

        [TestCaseSource(nameof(Classes))]
        public void EveryClassStandsUprightOnFlatFeetInIdle(ClassId cls)
        {
            GameObject go = Spawn(cls, out ArtEntry e, out ModelSpec spec);
            try
            {
                Transform root = go.transform;
                Transform hips = Find(root, "hips"), head = Find(root, "head");
                Transform fl = Find(root, "foot.l"), fr = Find(root, "foot.r"), tl = Find(root, "toes.l"), tr = Find(root, "toes.r");
                Assert.IsTrue(hips != null && head != null && fl != null && fr != null && tl != null && tr != null, spec.Key + ": hips, head, feet, toes");
                float bindL = Pitch(root, fl, tl), bindR = Pitch(root, fr, tr);
                AnimationClip idle = e.Clip(ArtMapping.Idle);
                Assert.IsNotNull(idle, spec.Key + " Idle clip");
                idle.SampleAnimation(go, 0f);
                float dl = Mathf.Abs(Pitch(root, fl, tl) - bindL), dr = Mathf.Abs(Pitch(root, fr, tr) - bindR);
                Assert.LessOrEqual(Mathf.Max(dl, dr), MaxIdleFootPitchChangeDeg,
                    $"{spec.Key}: idle feet pitched {dl:0.0} / {dr:0.0} degrees from the bind (tiptoe or hanging feet)");
                Vector3 up = root.InverseTransformPoint(head.position) - root.InverseTransformPoint(hips.position);
                float lean = Vector3.Angle(up, Vector3.up);
                Assert.LessOrEqual(lean, MaxIdleLeanDeg, $"{spec.Key}: idle leans {lean:0.0} degrees (hips to head against straight up)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheWarriorsSwordTipClearsTheGroundInWalkAndRun()
        {
            GameObject go = Spawn(ClassId.Warrior, out ArtEntry e, out ModelSpec spec);
            try
            {
                Transform root = go.transform;
                SkinnedMeshRenderer hero = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == spec.Meshes[0]);
                Assert.IsNotNull(hero, spec.Meshes[0] + " skinned");
                int slot = Array.FindIndex(hero.bones, b => b != null && b.name == "handslot.r");
                Assert.GreaterOrEqual(slot, 0, "handslot.r bone on the Hero");
                BoneWeight[] bw = hero.sharedMesh.boneWeights;
                int[] prop = Enumerable.Range(0, bw.Length).Where(i => bw[i].boneIndex0 == slot && bw[i].weight0 > 0.5f).ToArray();
                if (prop.Length < 20)
                    Assert.Inconclusive($"{spec.Key}: no weapon skinned to handslot.r ({prop.Length} vertices)");
                var baked = new Mesh();
                foreach (string clipName in new[] { ArtMapping.Walk, ArtMapping.Run })
                {
                    AnimationClip clip = e.Clip(clipName);
                    Assert.IsNotNull(clip, spec.Key + " " + clipName);
                    const int n = 24;
                    for (int k = 0; k < n; k++)
                    {
                        clip.SampleAnimation(go, clip.length * k / n);
                        hero.BakeMesh(baked, true);
                        Vector3[] v = baked.vertices;
                        float low = float.MaxValue;
                        foreach (int i in prop)
                            low = Mathf.Min(low, root.InverseTransformPoint(hero.transform.position + hero.transform.rotation * v[i]).y);
                        Assert.GreaterOrEqual(low, MinWeaponTipClearance,
                            $"{spec.Key}: weapon tip {low:0.000} above the ground in {clipName} at {k}/{n} of the cycle");
                    }
                }
                Object.DestroyImmediate(baked);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

using System;
using System.Collections.Generic;
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
    /// rig-farmer-3: the class clips move the WHOLE body, not only the legs. The HY Farmer's first pack kept her arms in
    /// the bind T-pose through idle and walk (the bake copied rotations relative to the shared pack's rest), her staff
    /// stuck out sideways at shoulder height, she measured 2.56 u wide in the game, and every test passed. Each class model
    /// is posed by its own Idle and Walk clips (sampled the way the game binds them: by bone path from the model root) and
    /// measured on its skinned Hero mesh.
    /// </summary>
    public sealed class ArtPoseTests
    {
        const float MinArmSwingDeg = 5f;           // upper-arm swing over the walk (KayKit's pack swings far more)
        const float MaxIdleWidthToHeight = 0.8f;   // front silhouette width over height in idle (the shipped classes: about 0.5)
        const float MinWristDropToHeight = 0.08f;  // idle wrists below the shoulders (a T-pose is 0)

        static ClassId[] Classes() => (ClassId[])Enum.GetValues(typeof(ClassId));

        [TestCaseSource(nameof(Classes))]
        public void EveryClassSwingsItsArmsInTheWalkAndStandsNarrowWithArmsDownInIdle(ClassId cls)
        {
            string key = ArtMapping.ClassModel(cls);
            ModelSpec spec = ArtMapping.Spec(key);
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath);
            Assert.IsNotNull(cat, ArtCatalog.AssetPath + " (run ProjectSetup.Apply)");
            ArtEntry e = cat.Find(key);
            Assert.IsNotNull(e, key + " not in the catalog");
            AnimationClip idle = e.Clip(ArtMapping.Idle), walk = e.Clip(ArtMapping.Walk);
            Assert.IsNotNull(idle, key + " Idle clip");
            Assert.IsNotNull(walk, key + " Walk clip");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            Assert.IsNotNull(prefab, spec.AssetPath);
            GameObject go = Object.Instantiate(prefab);
            try
            {
                Transform root = go.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform ual = Find(root, "upperarm.l"), uar = Find(root, "upperarm.r"), wl = Find(root, "wrist.l"), wr = Find(root, "wrist.r");
                Assert.IsTrue(ual != null && uar != null && wl != null && wr != null, key + ": arm bones");

                // Walk: the upper arms swing.
                const int n = 24;
                var l = new Quaternion[n];
                var r = new Quaternion[n];
                for (int i = 0; i < n; i++)
                {
                    walk.SampleAnimation(go, walk.length * i / n);
                    l[i] = ual.localRotation;
                    r[i] = uar.localRotation;
                }
                float swingL = Range(l), swingR = Range(r);
                Assert.Greater(Mathf.Min(swingL, swingR), MinArmSwingDeg,
                    $"{key}: the upper arms barely move over the walk (left {swingL:0.0}, right {swingR:0.0} degrees): the clips do not reach the arms");

                // Idle: arms down, a narrow front silhouette.
                idle.SampleAnimation(go, 0f);
                SkinnedMeshRenderer hero = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == spec.Meshes[0]);
                Assert.IsNotNull(hero, spec.Meshes[0] + " skinned");
                var baked = new Mesh();
                hero.BakeMesh(baked, true);
                Vector3[] v = baked.vertices;
                Object.DestroyImmediate(baked);
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (Vector3 p in v)
                {
                    Vector3 w = root.InverseTransformPoint(hero.transform.position + hero.transform.rotation * p);
                    x0 = Mathf.Min(x0, w.x); x1 = Mathf.Max(x1, w.x); y0 = Mathf.Min(y0, w.y); y1 = Mathf.Max(y1, w.y);
                }
                float width = x1 - x0, height = y1 - y0;
                Assert.Greater(height, 0f, key + " posed height");
                Assert.LessOrEqual(width / height, MaxIdleWidthToHeight,
                    $"{key}: idle silhouette {width:0.00} wide for {height:0.00} tall (arms or a carried prop sticking out sideways)");
                float drop = Mathf.Min(ual.position.y - wl.position.y, uar.position.y - wr.position.y);
                Assert.Greater(drop / height, MinWristDropToHeight,
                    $"{key}: idle wrists only {drop:0.00} below the shoulders ({height:0.00} tall): the arms stay in the bind T-pose");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static float Range(IReadOnlyList<Quaternion> q)
        {
            float worst = 0f;
            for (int i = 0; i < q.Count; i++)
                for (int j = i + 1; j < q.Count; j++)
                    worst = Mathf.Max(worst, Quaternion.Angle(q[i], q[j]));
            return worst;
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
    }
}

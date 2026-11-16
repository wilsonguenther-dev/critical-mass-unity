using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Batch 12 textured Builder pilot (art/builder-tex-1, _source~/builder_tex/build_char_tex.py): the switch, the mallet
    /// plan, the import contract (mirrors ToonShaderTests' WarriorTex test) and the rest-pose joint-cut test that the old
    /// pipeline failed: with the shoulder and hip joints cut out, no forearm/glove or shin/boot shell may share a connected
    /// component with the torso (the welds that became the stretched sheet under the raised arm). EditMode; every test
    /// prints its evidence.
    /// </summary>
    [TestFixture]
    public sealed class BuilderTexTests
    {
        // Joint-cut sphere radii as fractions of the rig's own bone lengths (upper arm 0.284 m, upper leg 0.237 m), so the
        // test does not depend on import units: the Blender build's radii (report joint_cut_scale x 0.17 / 0.22 m).
        const float ShoulderCutOfUpperArm = 1.05f;
        const float HipCutOfUpperLeg = 1.62f;
        const float ArmProbeOfUpperArm = 0.56f;          // forearm/glove probe: within 0.16 m of lowerarm/wrist/hand
        const float AboveHipsOfUpperLeg = 0.21f;         // torso probe: hips/spine/chest-nearest, 0.05 m above the hip joint

        [TearDown]
        public void ReadTheCommandLineAgain() => ArtMapping.SetBuilderTexForTests(null);

        [Test]
        public void TheFlagSelectsTheTexturedBuilderAndTheOldBuilderStaysTheDefault()
        {
            bool none = ArtMapping.BuilderTexArg(new[] { "game" }), one = ArtMapping.BuilderTexArg(new[] { "game", "-builderTex", "1" }),
                zero = ArtMapping.BuilderTexArg(new[] { "-builderTex", "0" }), bare = ArtMapping.BuilderTexArg(new[] { "-builderTex" });
            ArtMapping.SetBuilderTexForTests(false);
            string off = ArtMapping.ClassModel(ClassId.Builder);
            ArtMapping.SetBuilderTexForTests(true);
            string on = ArtMapping.ClassModel(ClassId.Builder);
            string warrior = ArtMapping.ClassModel(ClassId.Warrior);
            Debug.Log($"[BuilderTexTests] -builderTex: none {none}, '1' {one}, '0' {zero}, bare {bare} (expected False True False False); " +
                      $"Builder model off {off}, on {on}; Warrior with the Builder switch on {warrior}");
            Assert.IsFalse(none); Assert.IsTrue(one); Assert.IsFalse(zero); Assert.IsFalse(bare);
            Assert.AreEqual(ArtMapping.Builder, off);
            Assert.AreEqual(ArtMapping.BuilderTex, on);
            Assert.AreNotEqual(ArtMapping.BuilderTex, warrior);
        }

        [Test]
        public void TheTexturedBuilderCarriesNoMalletSoTheGameHangsOneOnTheHandSlot()
        {
            ModelSpec spec = ArtMapping.Spec(ArtMapping.BuilderTex);
            HeldWeapon plan = WeaponProps.Plan(ClassId.Builder, spec);
            Debug.Log($"[BuilderTexTests] BuilderTex HeldWeaponInMesh {spec.HeldWeaponInMesh} (expected False), plan {plan} (expected Mallet), " +
                      $"base map {ArtMaterials.BaseMapPathOf(spec)}, clips {spec.ClipSource}, stride {spec.StrideScale}");
            Assert.IsFalse(spec.HeldWeaponInMesh);
            Assert.AreEqual(HeldWeapon.Mallet, plan);
            Assert.AreEqual(ArtMapping.BuilderTexBaseMap, ArtMaterials.BaseMapPathOf(spec));
        }

        static GameObject LoadOrIgnore(ModelSpec spec)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            if (root == null)
            {
                Debug.Log("[BuilderTexTests] skipped, " + spec.AssetPath + " is absent");
                Assert.Ignore(spec.AssetPath + " absent");
            }
            return root;
        }

        static int Tris(Mesh m)
        {
            long idx = 0;
            for (int i = 0; i < m.subMeshCount; i++) idx += m.GetIndexCount(i);
            return (int)(idx / 3);
        }

        [Test]
        public void TheTexturedBuilderImportsWithUvsItsTextureBonesWeightsAndBoundClips()
        {
            ModelSpec spec = ArtMapping.Spec(ArtMapping.BuilderTex);
            GameObject root = LoadOrIgnore(spec);
            SkinnedMeshRenderer[] smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var range = new Dictionary<string, (int lo, int hi)>
            {
                { "BuilderTex_LOD0", (6000, 8000) }, { "BuilderTex_LOD1", (1400, 2100) }, { "BuilderTex_Shadow", (300, 800) },
            };
            foreach (var kv in range)
            {
                SkinnedMeshRenderer r = smrs.FirstOrDefault(s => s.name == kv.Key);
                Assert.IsNotNull(r, kv.Key + " renderer");
                Mesh mesh = r.sharedMesh;
                int tris = Tris(mesh);
                bool uv0 = mesh.HasVertexAttribute(VertexAttribute.TexCoord0);
                byte[] perVertex = mesh.GetBonesPerVertex().ToArray();
                int maxInf = perVertex.Length > 0 ? perVertex.Max(b => (int)b) : 0, unweighted = perVertex.Count(b => b == 0);
                Debug.Log($"[BuilderTexTests] {kv.Key}: {tris} triangles (expected {kv.Value.lo} to {kv.Value.hi}), UV0 {uv0}, " +
                          $"{mesh.vertexCount} vertices, max influences {maxInf} (expected <= 4), unweighted {unweighted} (expected 0), {r.bones.Length} skin bones");
                Assert.That(tris, Is.InRange(kv.Value.lo, kv.Value.hi), kv.Key);
                Assert.IsTrue(uv0, kv.Key + " has no UV0");
                Assert.LessOrEqual(maxInf, 4, kv.Key);
                Assert.AreEqual(0, unweighted, kv.Key + " unweighted vertices");
            }
            Transform rig = root.transform.Find("Rig");
            Assert.IsNotNull(rig, "Rig");
            int bones = rig.GetComponentsInChildren<Transform>(true).Length - 1;
            bool slot = rig.GetComponentsInChildren<Transform>(true).Any(t => t.name == "handslot.r");
            Debug.Log($"[BuilderTexTests] transforms under Rig: {bones} (expected {spec.Bones}), handslot.r present {slot}");
            Assert.AreEqual(spec.Bones, bones);
            Assert.IsTrue(slot, "handslot.r (the mallet's slot)");

            Texture2D tex = Resources.Load<Texture2D>(ArtMapping.BuilderTexBaseMap);
            Assert.IsNotNull(tex, "Resources/" + ArtMapping.BuilderTexBaseMap);
            var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
            bool alpha = ti != null && ti.DoesSourceTextureHaveAlpha();
            Debug.Log($"[BuilderTexTests] BuilderTex_Base: {tex.width} x {tex.height} (expected 1024 x 1024), source alpha {alpha} (expected True: the team mask), " +
                      $"sRGB {ti?.sRGBTexture}, mipmaps {tex.mipmapCount}, format {tex.format}");
            Assert.AreEqual(1024, tex.width);
            Assert.AreEqual(1024, tex.height);
            Assert.IsTrue(alpha, "the base map has no alpha (team mask)");
            Material m = ArtMaterials.ForTeam(spec, TeamId.Azure);
            Debug.Log($"[BuilderTexTests] material keyword {ArtMaterials.BaseMapKeyword} on: {m.IsKeywordEnabled(ArtMaterials.BaseMapKeyword)} (expected True)");
            Assert.IsTrue(m.IsKeywordEnabled(ArtMaterials.BaseMapKeyword));

            int clips = 0, unbound = 0;
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(spec.ClipSource))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                int miss = 0;
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(c))
                    if (!string.IsNullOrEmpty(b.path) && root.transform.Find(b.path) == null) miss++;
                clips++;
                unbound += miss;
                if (miss > 0) Debug.Log($"[BuilderTexTests] clip {c.name}: {miss} curve paths not found on BuilderTex");
            }
            Debug.Log($"[BuilderTexTests] {clips} clips from {spec.ClipSource}, {unbound} unbound curve paths (expected 0)");
            Assert.Greater(clips, 0, "no clips");
            Assert.AreEqual(0, unbound, "clip curves that bind to nothing on BuilderTex");
        }

        // ------------------------------------------------------------------------------------ watertight + stretch table

        [Test]
        public void EveryLodIsWatertightOnceSeamSplitsAreWelded()
        {
            ModelSpec spec = ArtMapping.Spec(ArtMapping.BuilderTex);
            GameObject root = LoadOrIgnore(spec);
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.StartsWith("BuilderTex_", StringComparison.Ordinal)))
            {
                Vector3[] v = r.sharedMesh.vertices;
                int[] tri = r.sharedMesh.triangles;
                var key = new Dictionary<Vector3, int>();
                int[] node = new int[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    if (!key.TryGetValue(v[i], out int k)) { k = key.Count; key[v[i]] = k; }
                    node[i] = k;
                }
                var edges = new Dictionary<long, int>();
                for (int t = 0; t < tri.Length; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = node[tri[t + e]], b = node[tri[t + (e + 1) % 3]];
                        long id = (long)Math.Min(a, b) * 1000003L + Math.Max(a, b);
                        edges[id] = edges.TryGetValue(id, out int c) ? c + 1 : 1;
                    }
                int open = edges.Values.Count(c => c == 1), over = edges.Values.Count(c => c > 2);
                Debug.Log($"[BuilderTexTests] {r.name}: {key.Count} welded vertices, {edges.Count} edges, open {open} (expected 0), shared by more than two faces {over}");
                Assert.AreEqual(0, open, r.name + " has open edges");
            }
        }

        /// <summary>Prints the clip stretch table from the final Blender build (_source~/builder_tex/BuilderTex_stretch.csv):
        /// longest-edge stretch over every frame of every clip, LOD0/LOD1 vs the current Builder LOD0. Deliberately NOT
        /// asserted: the single worst edge is stricter than the eye at gameplay size, and the known-imperfect spots (inner
        /// thigh above the knee, front of the shin, right armpit) are documented as a limitation of this experimental model.</summary>
        [Test]
        public void PrintsTheClipStretchTable()
        {
            string path = System.IO.Path.Combine(Application.dataPath, "_Game/Art/Characters/_source~/builder_tex/BuilderTex_stretch.csv");
            if (!System.IO.File.Exists(path)) { Debug.Log("[BuilderTexTests] stretch table absent: " + path); Assert.Ignore("stretch table absent"); }
            string[] lines = System.IO.File.ReadAllLines(path);
            Debug.Log("[BuilderTexTests] clip stretch (x rest edge length; max = single worst edge, p999 = 99.9th percentile; not asserted)\n" + string.Join("\n", lines));
            Assert.Greater(lines.Length, 1);
        }

        // ------------------------------------------------------------------------------------ rest-pose joint cut

        static readonly string[] Deform =
        {
            "hips", "spine", "chest", "head", "upperarm.l", "lowerarm.l", "wrist.l", "hand.l", "upperarm.r", "lowerarm.r", "wrist.r", "hand.r",
            "upperleg.l", "lowerleg.l", "foot.l", "toes.l", "upperleg.r", "lowerleg.r", "foot.r", "toes.r",
        };

        /// <summary>Bone segments (head to child head) in the renderer's mesh space at bind time.</summary>
        static Dictionary<string, (Vector3 a, Vector3 b)> Segments(SkinnedMeshRenderer r, Transform root)
        {
            var pos = new Dictionary<string, Vector3>();
            Mesh mesh = r.sharedMesh;
            Matrix4x4[] bind = mesh.bindposes;
            for (int i = 0; i < r.bones.Length && i < bind.Length; i++)
                if (r.bones[i] != null) pos[r.bones[i].name] = bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!pos.ContainsKey(t.name)) pos[t.name] = r.transform.InverseTransformPoint(t.position);
            Vector3 P(string n) => pos[n];
            var seg = new Dictionary<string, (Vector3, Vector3)>
            {
                ["hips"] = (P("hips"), P("spine")), ["spine"] = (P("spine"), P("chest")), ["chest"] = (P("chest"), P("head")),
                ["head"] = (P("head"), P("head") + (P("head") - P("chest"))),
            };
            foreach (string s in new[] { "l", "r" })
            {
                seg["upperarm." + s] = (P("upperarm." + s), P("lowerarm." + s));
                seg["lowerarm." + s] = (P("lowerarm." + s), P("wrist." + s));
                seg["wrist." + s] = (P("wrist." + s), P("hand." + s));
                seg["hand." + s] = (P("hand." + s), P("handslot." + s));
                seg["upperleg." + s] = (P("upperleg." + s), P("lowerleg." + s));
                seg["lowerleg." + s] = (P("lowerleg." + s), P("foot." + s));
                seg["foot." + s] = (P("foot." + s), P("toes." + s));
                seg["toes." + s] = (P("toes." + s), P("toes." + s) + (P("toes." + s) - P("foot." + s)));
            }
            return seg;
        }

        static float SegDist(Vector3 p, (Vector3 a, Vector3 b) s)
        {
            Vector3 ab = s.b - s.a;
            float t = Mathf.Clamp01(Vector3.Dot(p - s.a, ab) / Mathf.Max(1e-9f, ab.sqrMagnitude));
            return Vector3.Distance(p, s.a + t * ab);
        }

        static int Find(int[] p, int i)
        {
            while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; }
            return i;
        }

        [Test]
        public void AtRestNoArmOrLegShellIsWeldedToTheTorso()
        {
            ModelSpec spec = ArtMapping.Spec(ArtMapping.BuilderTex);
            GameObject root = LoadOrIgnore(spec);
            foreach (string name in new[] { "BuilderTex_LOD0", "BuilderTex_LOD1" })
            {
                SkinnedMeshRenderer r = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == name);
                Mesh mesh = r.sharedMesh;
                Vector3[] v = mesh.vertices;
                int[] tri = mesh.triangles;
                // weld the import's seam splits by exact position (never by distance): one node per position
                var key = new Dictionary<Vector3, int>();
                int[] node = new int[v.Length];
                var P = new List<Vector3>();
                for (int i = 0; i < v.Length; i++)
                {
                    if (!key.TryGetValue(v[i], out int k)) { k = P.Count; key[v[i]] = k; P.Add(v[i]); }
                    node[i] = k;
                }
                var seg = Segments(r, root.transform);
                float ua = Vector3.Distance(seg["upperarm.l"].a, seg["upperarm.l"].b), ul = Vector3.Distance(seg["upperleg.l"].a, seg["upperleg.l"].b);
                int n = P.Count;
                bool[] cut = new bool[n];
                int[] near = new int[n];
                float[] dmin = new float[n];
                for (int i = 0; i < n; i++)
                {
                    foreach (string s in new[] { "l", "r" })
                        if (Vector3.Distance(P[i], seg["upperarm." + s].a) < ShoulderCutOfUpperArm * ua ||
                            Vector3.Distance(P[i], seg["upperleg." + s].a) < HipCutOfUpperLeg * ul) cut[i] = true;
                    float best = float.MaxValue; int bi = 0;
                    for (int b = 0; b < Deform.Length; b++)
                    {
                        float d = SegDist(P[i], seg[Deform[b]]);
                        if (d < best) { best = d; bi = b; }
                    }
                    near[i] = bi; dmin[i] = best;
                }
                int[] par = Enumerable.Range(0, n).ToArray();
                for (int t = 0; t < tri.Length; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = node[tri[t + e]], b = node[tri[t + (e + 1) % 3]];
                        if (!cut[a] && !cut[b]) par[Find(par, a)] = Find(par, b);
                    }
                float hipsY = seg["hips"].a.y;      // mesh space is Y-up after the FBX axis conversion
                var torsoComps = new HashSet<int>();
                int torsoN = 0;
                for (int i = 0; i < n; i++)
                    if (!cut[i] && near[i] <= 2 && P[i].y > hipsY + AboveHipsOfUpperLeg * ul) { torsoComps.Add(Find(par, i)); torsoN++; }
                var probes = new Dictionary<string, HashSet<int>>();
                var counts = new Dictionary<string, int>();
                foreach (string s in new[] { "l", "r" })
                {
                    int[] arm = { Array.IndexOf(Deform, "lowerarm." + s), Array.IndexOf(Deform, "wrist." + s), Array.IndexOf(Deform, "hand." + s) };
                    int[] leg = { Array.IndexOf(Deform, "lowerleg." + s), Array.IndexOf(Deform, "foot." + s), Array.IndexOf(Deform, "toes." + s) };
                    probes["arm." + s] = new HashSet<int>(); probes["leg." + s] = new HashSet<int>();
                    counts["arm." + s] = counts["leg." + s] = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (cut[i]) continue;
                        if (arm.Contains(near[i]) && dmin[i] < ArmProbeOfUpperArm * ua) { probes["arm." + s].Add(Find(par, i)); counts["arm." + s]++; }
                        if (leg.Contains(near[i])) { probes["leg." + s].Add(Find(par, i)); counts["leg." + s]++; }
                    }
                }
                Debug.Log($"[BuilderTexTests] {name} rest joint cut: {v.Length} vertices welded by position to {n}, {cut.Count(c => c)} cut " +
                          $"(spheres {ShoulderCutOfUpperArm * ua:0.000} at the shoulders, {HipCutOfUpperLeg * ul:0.000} at the hips), torso probe {torsoN} in {torsoComps.Count} components");
                foreach (var kv in probes)
                {
                    bool welded = kv.Value.Overlaps(torsoComps);
                    string other = kv.Key.StartsWith("arm") ? "leg" : "arm";
                    bool limb = probes[other + ".l"].Overlaps(kv.Value) || probes[other + ".r"].Overlaps(kv.Value);
                    Debug.Log($"[BuilderTexTests] {name} {kv.Key}: probe {counts[kv.Key]} vertices in {kv.Value.Count} components, shares a component with the torso {welded}, with an {other} {limb} (expected False False)");
                    Assert.Greater(counts[kv.Key], 0, name + " " + kv.Key + " probe empty");
                    Assert.IsFalse(welded, name + " " + kv.Key + " shell welded to the torso");
                    Assert.IsFalse(limb, name + " " + kv.Key + " shell welded to an " + other);
                }
            }
        }
    }
}

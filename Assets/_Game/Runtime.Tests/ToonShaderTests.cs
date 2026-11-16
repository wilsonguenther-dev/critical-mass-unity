using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// V-01: the CM/Toon contract (ARCHITECTURE.md section 5, U-008). New, no vitest twin. EditMode only.
    /// </summary>
    [TestFixture]
    public sealed class ToonShaderTests
    {
        private const string ShaderName = "CM/Toon";
        private const string ShaderPath = "_Game/Art/Shaders/CMToon.shader";
        private const string CommonPath = "_Game/Art/Shaders/CMToonCommon.hlsl";
        private const string GroundName = "CM/ToonGround";
        private const string GroundPath = "_Game/Art/Shaders/CMToonGround.shader";
        private const float ColorTolerance = 1e-4f;
        private const float FloatTolerance = 1e-5f;

        private static readonly (string Name, ShaderPropertyType Type, Vector4 Default)[] ColorProperties =
        {
            ("_BaseColor", ShaderPropertyType.Color, new Vector4(0.968627f, 0.941176f, 0.898039f, 1f)),
            ("_TeamColor", ShaderPropertyType.Color, new Vector4(0.086275f, 0.780392f, 0.850980f, 1f)),
            ("_OutlineColor", ShaderPropertyType.Color, new Vector4(0.094118f, 0.137255f, 0.231373f, 1f)),
            ("_ShadowTint", ShaderPropertyType.Color, new Vector4(0.094118f, 0.137255f, 0.231373f, 1f)),
        };

        // Art direction 2026-11-12, 2.2 and 2.3: three bands (1.00 / 0.84 / 0.66 mixed 12 % to Void Navy), thresholds
        // 0.35 and -0.05 in N.L with a 0.02 hard edge, top faces one band lighter, 3 px constant-screen outline default.
        private static readonly (string Name, ShaderPropertyType Type, float Default)[] RangeProperties =
        {
            ("_MidMultiplier", ShaderPropertyType.Range, 0.84f),
            ("_ShadowMultiplier", ShaderPropertyType.Range, 0.66f),
            ("_ShadowTintAmount", ShaderPropertyType.Range, 0.12f),
            ("_LitThreshold", ShaderPropertyType.Range, 0.35f),
            ("_ShadowThreshold", ShaderPropertyType.Range, -0.05f),
            ("_BandSoftness", ShaderPropertyType.Range, 0.02f),
            ("_TopBandLift", ShaderPropertyType.Range, 1f),
            ("_OutlinePx", ShaderPropertyType.Range, 3f),
            ("_OutlineSource", ShaderPropertyType.Range, 0f),
            ("_RimWidth", ShaderPropertyType.Range, 0.25f),
            ("_RimStrength", ShaderPropertyType.Range, 0.6f),
        };

        private static readonly string[] ForbiddenPropertyFragments = { "Metallic", "Smoothness", "Gloss", "Spec", "Reflect" };
        private static readonly string[] ForbiddenSourceTokens = { "_SPECULAR", "LightingSpecular", "GlossyEnvironment", "SampleSH" };

        private static Shader FindToon()
        {
            Shader shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, "Shader.Find(\"" + ShaderName + "\") returned null");
            return shader;
        }

        [Test]
        public void CMToonIsFoundAndCompilesWithoutErrors()
        {
            Shader shader = FindToon();
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "CM/Toon has compile errors: " +
                string.Join(" | ", System.Linq.Enumerable.Select(ShaderUtil.GetShaderMessages(shader), m => m.message + " (" + m.file + ":" + m.line + ")")));
            Assert.IsTrue(shader.isSupported, "CM/Toon is not supported");
        }

        [Test]
        public void HasTheContractPropertiesWithTheirDefaults()
        {
            Shader shader = FindToon();

            foreach (var (name, type, expected) in ColorProperties)
            {
                int index = shader.FindPropertyIndex(name);
                Assert.GreaterOrEqual(index, 0, name + " is missing");
                Assert.AreEqual(type, shader.GetPropertyType(index), name + " type");
                Vector4 actual = shader.GetPropertyDefaultVectorValue(index);
                for (int c = 0; c < 4; c++)
                {
                    Assert.AreEqual(expected[c], actual[c], ColorTolerance, name + " default channel " + c);
                }
            }

            foreach (var (name, type, expected) in RangeProperties)
            {
                int index = shader.FindPropertyIndex(name);
                Assert.GreaterOrEqual(index, 0, name + " is missing");
                Assert.AreEqual(type, shader.GetPropertyType(index), name + " type");
                Assert.AreEqual(expected, shader.GetPropertyDefaultFloatValue(index), FloatTolerance, name + " default");
            }
        }

        [Test]
        public void HasForwardOutlineShadowCasterDepthOnlyAndDepthNormalsPasses()
        {
            Shader shader = FindToon();
            Assert.AreEqual(5, shader.passCount, "pass count (art import 2026-11-12 added DepthOnly and DepthNormals)");

            // ShaderTagId values are interned case-insensitively: .name returns the spelling first registered in the
            // editor session (the gate saw "SRPDEFAULTUNLIT" for pass 1), so compare tag ids, the way URP's
            // DrawRenderers matches LightMode, not the string spelling.
            var lightMode = new ShaderTagId("LightMode");
            AssertLightMode(shader, 0, lightMode, "UniversalForward");
            AssertLightMode(shader, 1, lightMode, "SRPDefaultUnlit");
            AssertLightMode(shader, 2, lightMode, "ShadowCaster");
            AssertLightMode(shader, 3, lightMode, "DepthOnly");
            AssertLightMode(shader, 4, lightMode, "DepthNormals");
        }

        private static void AssertLightMode(Shader shader, int pass, ShaderTagId lightMode, string expected)
        {
            ShaderTagId actual = shader.FindPassTagValue(pass, lightMode);
            Assert.IsTrue(actual == new ShaderTagId(expected),
                "pass " + pass + " LightMode: expected " + expected + ", was " + actual.name);
        }

        [Test]
        public void DeclaresNoGlossyMetallicSpecularOrReflectionProperty()
        {
            Shader shader = FindToon();

            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                foreach (string fragment in ForbiddenPropertyFragments)
                {
                    Assert.IsFalse(
                        name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0,
                        "property " + name + " contains " + fragment);
                }
            }

            foreach (string relative in new[] { ShaderPath, CommonPath, GroundPath })
            {
                string path = Path.Combine(Application.dataPath, relative);
                Assert.IsTrue(File.Exists(path), path + " is missing");
                string source = File.ReadAllText(path);
                foreach (string token in ForbiddenSourceTokens)
                {
                    Assert.IsFalse(source.Contains(token), relative + " contains " + token);
                }
            }
        }

        [Test]
        public void TheShadowBandIsNeverBlackAndTheBandsStepDown()
        {
            // The shader's own band maths on Warm Cream: lit > mid > shadow, and the shadow band stays well above the ink.
            Vector3 cream = new Vector3(0.968627f, 0.941176f, 0.898039f);
            Vector3 navy = new Vector3(0.094118f, 0.137255f, 0.231373f);
            Vector3 mid = cream * 0.84f;
            Vector3 shadow = Vector3.Lerp(cream * 0.66f, navy, 0.12f);
            Assert.Greater(Luma(cream), Luma(mid));
            Assert.Greater(Luma(mid), Luma(shadow));
            Assert.Greater(Luma(shadow), Luma(navy) + 0.3f, "the shadow band is far from black and from the ink");
        }

        private static float Luma(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;

        [Test]
        public void TheGroundVariantCompilesHasNoOutlinePassAndPaintsTheGrassCells()
        {
            Shader shader = Shader.Find(GroundName);
            Assert.IsNotNull(shader, GroundName + " not found");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), GroundName + " has compile errors");
            Assert.AreEqual(2, shader.passCount, "ForwardLit and ShadowCaster only: no hull on ground cells");
            var lightMode = new ShaderTagId("LightMode");
            AssertLightMode(shader, 0, lightMode, "UniversalForward");
            AssertLightMode(shader, 1, lightMode, "ShadowCaster");

            AssertColor(shader, "_GrassLight", "#ACCB56");
            AssertColor(shader, "_GrassMid", "#A3C150");
            AssertColor(shader, "_GrassDark", "#96B249");
            AssertColor(shader, "_SeamColor", "#7E9A3F");
            AssertColor(shader, "_CliffColor", "#5B5E58");
            int cell = shader.FindPropertyIndex("_CellSize");
            Assert.GreaterOrEqual(cell, 0, "_CellSize missing");
            Assert.AreEqual(2.5f, shader.GetPropertyDefaultFloatValue(cell), FloatTolerance, "mean cell size 2.5 u");
        }

        // ------------------------------------------------------------------------- batch 12 AB: sharpness + textured pilot

        [Test]
        public void HasTheSmoothNormalsAndBaseMapPropertiesAndTheBaseMapKeyword()
        {
            Shader shader = FindToon();
            int sn = shader.FindPropertyIndex("_SmoothNormals");
            if (sn < 0) Debug.Log("[ToonShaderTests] _SmoothNormals absent (the normals switch commit is reverted)");
            else
            {
                float d = shader.GetPropertyDefaultFloatValue(sn);
                Debug.Log($"[ToonShaderTests] _SmoothNormals default {d} (expected 0: only character materials turn it on)");
                Assert.AreEqual(0f, d, FloatTolerance);
            }
            int bm = shader.FindPropertyIndex("_BaseMap");
            Debug.Log($"[ToonShaderTests] _BaseMap property index {bm}, type {(bm >= 0 ? shader.GetPropertyType(bm).ToString() : "missing")} (expected a Texture)");
            Assert.GreaterOrEqual(bm, 0, "_BaseMap missing");
            Assert.AreEqual(ShaderPropertyType.Texture, shader.GetPropertyType(bm));
            LocalKeyword kw = shader.keywordSpace.FindKeyword(CriticalMass.Runtime.View.Art.ArtMaterials.BaseMapKeyword);
            Debug.Log($"[ToonShaderTests] keyword {CriticalMass.Runtime.View.Art.ArtMaterials.BaseMapKeyword} declared: {kw.isValid} (expected True)");
            Assert.IsTrue(kw.isValid, "the base map keyword is not declared");
        }

        [Test]
        public void UntexturedArtMaterialsNeverEnableTheBaseMapKeyword()
        {
            int n = 0;
            foreach (CriticalMass.Runtime.View.Art.ModelSpec spec in CriticalMass.Runtime.View.Art.ArtMapping.Models)
            {
                if (spec.Authored == null) continue;
                Material m = CriticalMass.Runtime.View.Art.ArtMaterials.ForTeam(spec, CriticalMass.Sim.Config.TeamId.Azure);
                bool textured = CriticalMass.Runtime.View.Art.ArtMaterials.BaseMapOf(spec) != null;
                bool on = m.IsKeywordEnabled(CriticalMass.Runtime.View.Art.ArtMaterials.BaseMapKeyword);
                Debug.Log($"[ToonShaderTests] {spec.Key}: base map {(textured ? "yes" : "no")}, keyword {(on ? "on" : "off")} (expected {(textured ? "on" : "off")})");
                Assert.AreEqual(textured, on, spec.Key);
                if (spec.Key != CriticalMass.Runtime.View.Art.ArtMapping.WarriorTex && spec.Key != CriticalMass.Runtime.View.Art.ArtMapping.BuilderTex)
                    Assert.IsFalse(textured, spec.Key + " has a base map");
                n++;
            }
            Assert.Greater(n, 5, "art materials checked");
        }

        [Test]
        public void TheTexturedWarriorImportsWithUvsItsTextureBonesAndBoundClips()
        {
            var spec = CriticalMass.Runtime.View.Art.ArtMapping.Spec(CriticalMass.Runtime.View.Art.ArtMapping.WarriorTex);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            if (root == null)
            {
                Debug.Log("[ToonShaderTests] WarriorTex import: skipped, " + spec.AssetPath + " is absent");
                Assert.Ignore(spec.AssetPath + " absent");
            }
            SkinnedMeshRenderer[] smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer lod0 = System.Linq.Enumerable.FirstOrDefault(smrs, r => r.name == spec.Meshes[0]);
            Assert.IsNotNull(lod0, spec.Meshes[0] + " renderer");
            Mesh mesh = lod0.sharedMesh;
            long idx = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) idx += mesh.GetIndexCount(i);
            int tris = (int)(idx / 3);
            bool uv0 = mesh.HasVertexAttribute(VertexAttribute.TexCoord0), uv3 = mesh.HasVertexAttribute(VertexAttribute.TexCoord3);
            int bones = System.Linq.Enumerable.Count(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Where(
                System.Linq.Enumerable.SelectMany(smrs, s => s.bones), b => b != null)));
            Debug.Log($"[ToonShaderTests] WarriorTex LOD0: {tris} triangles (expected 5,000 to 7,000), UV0 {uv0} (expected True), " +
                      $"UV3 smoothed normals {uv3}, {bones} bones (expected 41), {mesh.vertexCount} vertices");
            Assert.That(tris, Is.InRange(5000, 7000));
            Assert.IsTrue(uv0, "no UV0");
            Assert.AreEqual(41, bones);

            Texture2D tex = Resources.Load<Texture2D>(CriticalMass.Runtime.View.Art.ArtMapping.WarriorTexBaseMap);
            Assert.IsNotNull(tex, "Resources/" + CriticalMass.Runtime.View.Art.ArtMapping.WarriorTexBaseMap);
            var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
            bool alpha = ti != null && ti.DoesSourceTextureHaveAlpha();
            Debug.Log($"[ToonShaderTests] WarriorTex_Base: {tex.width} x {tex.height} (expected 1024 x 1024), source alpha {alpha} (expected True: the team mask), " +
                      $"sRGB {ti?.sRGBTexture}, mipmaps {tex.mipmapCount}, format {tex.format}");
            Assert.AreEqual(1024, tex.width);
            Assert.AreEqual(1024, tex.height);
            Assert.IsTrue(alpha, "the base map has no alpha (team mask)");

            int clips = 0, unbound = 0;
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(spec.ClipSource))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                int miss = 0;
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(c))
                    if (!string.IsNullOrEmpty(b.path) && root.transform.Find(b.path) == null) miss++;
                clips++;
                unbound += miss;
                if (miss > 0) Debug.Log($"[ToonShaderTests] WarriorTex clip {c.name}: {miss} curve paths not found on the model");
            }
            Debug.Log($"[ToonShaderTests] WarriorTex: {clips} clips from {spec.ClipSource}, {unbound} unbound curve paths (expected 0)");
            Assert.Greater(clips, 0, "no clips");
            Assert.AreEqual(0, unbound, "clip curves that bind to nothing on WarriorTex");
        }

        private static void AssertColor(Shader shader, string name, string hex)
        {
            int i = shader.FindPropertyIndex(name);
            Assert.GreaterOrEqual(i, 0, name + " missing");
            Assert.IsTrue(ColorUtility.TryParseHtmlString(hex, out Color c));
            Vector4 v = shader.GetPropertyDefaultVectorValue(i);
            Assert.AreEqual(c.r, v.x, 2e-3f, name + " r");
            Assert.AreEqual(c.g, v.y, 2e-3f, name + " g");
            Assert.AreEqual(c.b, v.z, 2e-3f, name + " b");
        }
    }
}

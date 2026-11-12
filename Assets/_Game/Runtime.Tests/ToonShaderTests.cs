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
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "CM/Toon has compile errors");
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
        public void HasAForwardPassAnOutlinePassAndAShadowCasterPass()
        {
            Shader shader = FindToon();
            Assert.AreEqual(3, shader.passCount, "pass count");

            // ShaderTagId values are interned case-insensitively: .name returns the spelling first registered in the
            // editor session (the gate saw "SRPDEFAULTUNLIT" for pass 1), so compare tag ids, the way URP's
            // DrawRenderers matches LightMode, not the string spelling.
            var lightMode = new ShaderTagId("LightMode");
            AssertLightMode(shader, 0, lightMode, "UniversalForward");
            AssertLightMode(shader, 1, lightMode, "SRPDefaultUnlit");
            AssertLightMode(shader, 2, lightMode, "ShadowCaster");
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

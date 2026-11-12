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
        private const float ColorTolerance = 1e-4f;
        private const float FloatTolerance = 1e-5f;

        private static readonly (string Name, ShaderPropertyType Type, Vector4 Default)[] ColorProperties =
        {
            ("_BaseColor", ShaderPropertyType.Color, new Vector4(0.968627f, 0.941176f, 0.898039f, 1f)),
            ("_TeamColor", ShaderPropertyType.Color, new Vector4(0.086275f, 0.780392f, 0.850980f, 1f)),
            ("_OutlineColor", ShaderPropertyType.Color, new Vector4(0.094118f, 0.137255f, 0.231373f, 1f)),
        };

        private static readonly (string Name, ShaderPropertyType Type, float Default)[] RangeProperties =
        {
            ("_ShadeMultiplier", ShaderPropertyType.Range, 0.62f),
            ("_RampThreshold", ShaderPropertyType.Range, 0.0f),
            ("_OutlineWidth", ShaderPropertyType.Range, 0.02f),
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

            var lightMode = new ShaderTagId("LightMode");
            Assert.AreEqual("UniversalForward", shader.FindPassTagValue(0, lightMode).name, "pass 0 LightMode");
            Assert.AreEqual("SRPDefaultUnlit", shader.FindPassTagValue(1, lightMode).name, "pass 1 LightMode");
            Assert.AreEqual("ShadowCaster", shader.FindPassTagValue(2, lightMode).name, "pass 2 LightMode");
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

            foreach (string relative in new[] { ShaderPath, CommonPath })
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
    }
}

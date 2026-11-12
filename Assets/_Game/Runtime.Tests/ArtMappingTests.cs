using System.Linq;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>The one sim-to-model mapping table (ArtMapping) and the team materials it hands the views (2026-11-12).</summary>
    public sealed class ArtMappingTests
    {
        [Test]
        public void WarriorMinerAndFarmerHaveModelsEveryOtherClassKeepsTheCapsule()
        {
            Assert.AreEqual(ArtMapping.Warrior, ArtMapping.ClassModel(ClassId.Warrior));
            Assert.AreEqual(ArtMapping.Miner, ArtMapping.ClassModel(ClassId.Miner));
            Assert.AreEqual(ArtMapping.Farmer, ArtMapping.ClassModel(ClassId.Farmer), "Farmer v1 replaces the capsule (art refresh 2)");
            Assert.IsNull(ArtMapping.ClassModel(ClassId.Demolitions));
            Assert.IsNull(ArtMapping.ClassModel(ClassId.Builder));
        }

        [Test]
        public void EveryKeyTheViewsCanAskForIsAModelSpecUnderTheArtFolder()
        {
            var keys = ClassIds.All.Select(ArtMapping.ClassModel).ToList();
            foreach (KaijuState s in System.Enum.GetValues(typeof(KaijuState)))
            {
                keys.Add(ArtMapping.KaijuModel(s, 0));
                keys.Add(ArtMapping.KaijuModel(s, 100));
            }
            for (int c = 0; c < 6; c++)
            {
                keys.Add(ArtMapping.NodeModel(NodeKind.Biomass, c));
                keys.Add(ArtMapping.NodeModel(NodeKind.Corruption, c));
            }
            keys.AddRange(new[] { ArtMapping.MinerBarricade, ArtMapping.KaijuShards, ArtMapping.KaijuSpikes });
            foreach (string k in keys.Where(k => k != null))
            {
                ModelSpec spec = ArtMapping.Spec(k);
                Assert.IsNotNull(spec, k);
                StringAssert.StartsWith("Assets/_Game/Art/", spec.AssetPath);
                StringAssert.EndsWith(".fbx", spec.AssetPath);
            }
            Assert.AreEqual(ArtMapping.Models.Length, ArtMapping.Models.Select(m => m.Key).Distinct().Count(), "unique keys");
        }

        [Test]
        public void KaijuStagesFollowTheSimStateAndTheHatchlingWindow()
        {
            Assert.AreEqual(ArtMapping.KaijuEgg, ArtMapping.KaijuModel(KaijuState.Egg, double.NegativeInfinity));
            Assert.AreEqual(ArtMapping.KaijuHatchling, ArtMapping.KaijuModel(KaijuState.Neutral, 0));
            Assert.AreEqual(ArtMapping.KaijuHatchling, ArtMapping.KaijuModel(KaijuState.Azure, ArtMapping.HatchlingSeconds - 0.01));
            Assert.AreEqual(ArtMapping.KaijuAdult, ArtMapping.KaijuModel(KaijuState.Crimson, ArtMapping.HatchlingSeconds));
            Assert.AreEqual(ArtMapping.KaijuAdult, ArtMapping.KaijuModel(KaijuState.Berserk, double.PositiveInfinity));
            Assert.AreEqual(ArtMapping.GlowNeutral, ArtMapping.KaijuGlow(KaijuState.Egg));
            Assert.AreEqual(ArtMapping.GlowNeutral, ArtMapping.KaijuGlow(KaijuState.Neutral));
            Assert.AreEqual(ArtMapping.GlowAzure, ArtMapping.KaijuGlow(KaijuState.Azure));
            Assert.AreEqual(ArtMapping.GlowCrimson, ArtMapping.KaijuGlow(KaijuState.Crimson));
            Assert.AreEqual(ArtMapping.GlowBerserk, ArtMapping.KaijuGlow(KaijuState.Berserk));
        }

        [Test]
        public void NodesAndBarricadesPickTheirPropByChargesAndHp()
        {
            Assert.AreEqual(ArtMapping.BiomassL, ArtMapping.NodeModel(NodeKind.Biomass, 3));
            Assert.AreEqual(ArtMapping.BiomassM, ArtMapping.NodeModel(NodeKind.Biomass, 2));
            Assert.AreEqual(ArtMapping.BiomassS, ArtMapping.NodeModel(NodeKind.Biomass, 1));
            Assert.AreEqual(ArtMapping.BiomassSprout, ArtMapping.NodeModel(NodeKind.Biomass, 0));
            Assert.AreEqual(ArtMapping.CorruptionL, ArtMapping.NodeModel(NodeKind.Corruption, 2));
            Assert.AreEqual(ArtMapping.CorruptionS, ArtMapping.NodeModel(NodeKind.Corruption, 1));
            Assert.IsNull(ArtMapping.NodeModel(NodeKind.Corruption, 0), "a spent corruption node is hidden");
            Assert.AreEqual(ArtMapping.BarricadeDeployed, ArtMapping.BarricadeState(120, 120));
            Assert.AreEqual(ArtMapping.BarricadeDeployed, ArtMapping.BarricadeState(60, 120));
            Assert.AreEqual(ArtMapping.BarricadeDamaged, ArtMapping.BarricadeState(59.9, 120));
        }

        [Test]
        public void TeamMaterialsAreSharedPerTeamAndCarryTheSwapContract()
        {
            ModelSpec w = ArtMapping.Spec(ArtMapping.Warrior);
            Material azure = ArtMaterials.ForTeam(w, TeamId.Azure), crimson = ArtMaterials.ForTeam(w, TeamId.Crimson);
            Assert.AreSame(azure, ArtMaterials.ForTeam(w, TeamId.Azure), "one material per (model, team): no per-instance copies");
            Assert.AreNotSame(azure, crimson);
            Assert.AreEqual(1f, crimson.GetFloat("_ColorMode"));
            // Azure is the authored allegiance: the swap is the identity. Crimson targets the art's TEAM_SWAP values.
            Near(azure.GetColor("_DefaultGlow"), azure.GetColor("_TeamGlow"));
            Near(ArtMaterials.Hex("#FF7461"), crimson.GetColor("_TeamGlow"));
            Near(ArtMaterials.Hex("#E46753"), crimson.GetColor("_TeamCloth"));
            Near(ArtMaterials.Hex("#4FE3F0"), crimson.GetColor("_DefaultGlow"));
            Assert.AreEqual(ArtMapping.CharacterHullPx, crimson.GetFloat("_OutlinePx"));
            Assert.AreEqual(3f, crimson.GetFloat("_OutlineSource"), "rigged models read the smoothed hull direction from TANGENT");
            Assert.IsTrue(crimson.GetShaderPassEnabled("SRPDefaultUnlit"), "characters keep their hull");
            Material shards = ArtMaterials.ForGlow(ArtMapping.Spec(ArtMapping.KaijuShards), ArtMapping.GlowBerserk);
            Assert.IsFalse(shards.GetShaderPassEnabled("SRPDefaultUnlit"), "all-glow pieces draw no hull");
        }

        /// <summary>Art refresh 2: decals, outline weights, the Farmer's team swap and the Kaiju berserk reach the materials.</summary>
        [Test]
        public void FaceDecalsOutlineWeightsFarmerSwapAndBerserkReachTheMaterials()
        {
            Material w = ArtMaterials.ForTeam(ArtMapping.Spec(ArtMapping.Warrior), TeamId.Azure);
            Assert.AreEqual(1f, w.GetFloat("_DecalOn"), "Warrior face decal mixed over the body");
            Assert.IsNotNull(w.GetTexture("_FaceTex"), "Warrior_Face.png from the catalog");
            Assert.AreEqual(1f, w.GetFloat("_OutlineWeightUV4"), "Warrior reads its UV4 outline weights");

            Material f = ArtMaterials.ForTeam(ArtMapping.Spec(ArtMapping.Farmer), TeamId.Crimson);
            Near(ArtMaterials.Hex("#4999A5"), f.GetColor("_DefaultCloth"));
            Near(ArtMaterials.Hex("#E9705F"), f.GetColor("_TeamCloth"));
            Assert.AreEqual(0f, f.GetFloat("_DecalOn"), "the Farmer has no painted decal");
            Assert.AreEqual(1f, f.GetFloat("_OutlineWeightUV4"), "Farmer face unlined through UV4 (outline_w sidecar)");

            ModelSpec minerSpec = ArtMapping.Spec(ArtMapping.Miner);
            Material miner = ArtMaterials.ForTeam(minerSpec, TeamId.Azure), face = ArtMaterials.Face(minerSpec, miner);
            Assert.AreEqual(0f, miner.GetFloat("_DecalOn"), "the Miner body slot carries no decal");
            Assert.IsNotNull(face, "the Miner's CM_FaceDecal slot has its own material");
            Assert.AreEqual(1f, face.GetFloat("_DecalOn"));
            Assert.IsFalse(face.GetShaderPassEnabled("SRPDefaultUnlit"), "per-material outline suppression on the Miner face");
            Assert.AreSame(face, ArtMaterials.Face(minerSpec, miner), "one face material per body material");

            ModelSpec adult = ArtMapping.Spec(ArtMapping.KaijuAdult);
            Assert.AreEqual(1f, ArtMaterials.ForGlow(adult, ArtMapping.GlowBerserk).GetFloat("_Berserk"), "berserk darkens the plates");
            Material calm = ArtMaterials.ForGlow(adult, ArtMapping.GlowAzure);
            Assert.AreEqual(0f, calm.GetFloat("_Berserk"));
            Assert.AreEqual(1f, calm.GetFloat("_DecalOn"), "Kaiju face atlas");
            Assert.AreEqual(0f, ArtMaterials.ForGlow(ArtMapping.Spec(ArtMapping.KaijuShards), ArtMapping.GlowBerserk).GetFloat("_Berserk"),
                "shards are all glow: nothing to darken, and only Kaiju creature stages are switched");
        }

        /// <summary>The Farmer's new alpha 0.25 code is code 2 in eighths: fixed colour, self-lit, no hull, never swapped.</summary>
        [Test]
        public void MaskCodeQuarterIsTheFixedSelfLitUnlinedGlow()
        {
            Assert.AreEqual(2, Mathf.RoundToInt(0.25f * 8f));
            string hlsl = System.IO.File.ReadAllText("Assets/_Game/Art/Shaders/CMToonCommon.hlsl");
            StringAssert.Contains("selfLit = (code == 1 || code == 2 ||", hlsl, "code 2 is self-lit");
            StringAssert.Contains("return (code == 2 ||", hlsl, "code 2 draws no hull");
            StringAssert.DoesNotContain("code == 2) rgb = CMToonSwap", hlsl, "code 2 is never team-swapped");
        }

        /// <summary>CPU replica of the berserk darkening (kaiju v3 report): fixed' = lerp(fixed * (0.82, 0.56, 0.78), #8E3FD1, 0.15).</summary>
        [Test]
        public void BerserkDarkeningMatchesTheArtFormula()
        {
            Color plate = ArtMaterials.Hex("#8C8C8C"), corruption = ArtMaterials.Hex("#8E3FD1");
            Color dark = Color.Lerp(new Color(plate.r * 0.82f, plate.g * 0.56f, plate.b * 0.78f), corruption, 0.15f);
            Assert.Less(dark.g, plate.g * 0.7f, "green drops most: the plates go purple-dark");
            Assert.Less(dark.maxColorComponent, plate.maxColorComponent, "darker, never brighter");
            Material m = ArtMaterials.ForGlow(ArtMapping.Spec(ArtMapping.KaijuAdult), ArtMapping.GlowBerserk);
            Vector4 mul = m.GetVector("_BerserkMul");
            Assert.AreEqual(0.82f, mul.x, 1e-4f);
            Assert.AreEqual(0.56f, mul.y, 1e-4f);
            Assert.AreEqual(0.78f, mul.z, 1e-4f);
            Assert.AreEqual(0.15f, m.GetFloat("_BerserkTintAmount"), 1e-4f);
        }

        /// <summary>CPU replica of CMToonSwap (sRGB): an authored-colour pixel becomes exactly the target, darker facets keep their ratio.</summary>
        [Test]
        public void TheTeamGlowContractKeepsFacetBrightness()
        {
            Color def = ArtMaterials.Hex(ArtMapping.GlowAzure), target = ArtMaterials.Hex(ArtMapping.GlowCrimson);
            Color full = Swap(def, target, def), half = Swap(def * 0.5f, target, def);
            Assert.AreEqual(target.r, full.r, 1e-4f);
            Assert.AreEqual(target.g, full.g, 1e-4f);
            Assert.AreEqual(target.r * 0.5f, half.r, 1e-4f);
        }

        static void Near(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, "r");
            Assert.AreEqual(expected.g, actual.g, 1e-3f, "g");
            Assert.AreEqual(expected.b, actual.b, 1e-3f, "b");
        }

        static Color Swap(Color rgb, Color target, Color authored)
        {
            float k = rgb.maxColorComponent / Mathf.Max(1e-4f, authored.maxColorComponent);
            return new Color(Mathf.Clamp01(target.r * k), Mathf.Clamp01(target.g * k), Mathf.Clamp01(target.b * k));
        }
    }
}

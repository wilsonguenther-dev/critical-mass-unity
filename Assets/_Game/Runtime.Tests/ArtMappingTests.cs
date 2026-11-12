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
        public void WarriorAndMinerHaveModelsEveryOtherClassKeepsTheCapsule()
        {
            Assert.AreEqual(ArtMapping.Warrior, ArtMapping.ClassModel(ClassId.Warrior));
            Assert.AreEqual(ArtMapping.Miner, ArtMapping.ClassModel(ClassId.Miner));
            Assert.IsNull(ArtMapping.ClassModel(ClassId.Farmer), "Farmer is not built yet: capsule");
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
            Assert.AreEqual(3f, crimson.GetFloat("_OutlinePx"));
            Assert.IsTrue(crimson.GetShaderPassEnabled("SRPDefaultUnlit"), "characters keep their hull");
            Material shards = ArtMaterials.ForGlow(ArtMapping.Spec(ArtMapping.KaijuShards), ArtMapping.GlowBerserk);
            Assert.IsFalse(shards.GetShaderPassEnabled("SRPDefaultUnlit"), "all-glow pieces draw no hull");
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

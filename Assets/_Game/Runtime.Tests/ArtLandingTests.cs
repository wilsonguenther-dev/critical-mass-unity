using System;
using System.IO;
using System.Linq;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// The art landing (2026-11-12): every class and both Kaiju creature stages resolve a model, their clips and their face
    /// textures by name; the Kaiju expression lookup covers every state; every mascot expression portrait loads; the
    /// environment v4 clutter stays behind the quality table. The pure checks run without an import; the asset checks need
    /// tools/art-sync.sh (or the committed files) imported and ProjectSetup.Apply run (the catalogs).
    /// </summary>
    public sealed class ArtLandingTests
    {
        static readonly string[] KaykitClipNames = new[]
        {
            ArtMapping.Idle, ArtMapping.Walk, ArtMapping.Run, ArtMapping.Attack, ArtMapping.Gather, ArtMapping.Hit, ArtMapping.Death, ArtMapping.Cheer,
        }.Concat(ArtMapping.KaykitExtraClips).ToArray();

        static string ProjectPath(string assetPath) => Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);

        static ClassId[] Classes() => (ClassId[])Enum.GetValues(typeof(ClassId));

        // ------------------------------------------------------------------------------------------- pure mapping

        [TestCaseSource(nameof(Classes))]
        public void EveryClassResolvesARiggedModelOnTheSharedClipsWithAFace(ClassId cls)
        {
            string key = ArtMapping.ClassModel(cls);
            Assert.IsNotNull(key, cls + " has no model (capsule)");
            ModelSpec spec = ArtMapping.Spec(key);
            Assert.IsNotNull(spec, key);
            Assert.IsTrue(spec.Rigged, key + " rigged");
            Assert.AreEqual(41, spec.Bones, key + " KayKit rig");
            Assert.AreEqual("Assets/_Game/Art/Characters/KayKit_Clips.fbx", spec.ClipSource, key + " clip source");
            Assert.IsTrue(spec.FaceTexture != null || spec.VertexColourFace, key + " face decal (or an HY vertex-colour face)");
            Assert.AreEqual(180f, spec.ArtYawDeg, key + ": every class FBX arrives facing the root's -Z (the per-model 180 degree correction)");
            Assert.AreEqual(4, spec.Meshes.Length, key + ": Hero, LOD0, LOD1, Shadow");
            Assert.AreEqual(key + FighterLod.HeroSuffix, spec.Meshes[0], key + ": the Hero mesh is measured first");
            CollectionAssert.AreEquivalent(new[] { FighterLod.Role.Hero, FighterLod.Role.Lod0, FighterLod.Role.Lod1, FighterLod.Role.Shadow },
                spec.Meshes.Select(FighterLod.RoleOf).ToArray(), key + " LOD roles");
        }

        [TestCaseSource(nameof(Classes))]
        public void EveryClassClipIsAClipTheKaykitFileCarries(ClassId cls)
        {
            ArtMapping.ClassClipSet c = ArtMapping.ClassClips(cls);
            foreach (string clip in new[] { c.Attack, c.Ability, c.AbilityHold, c.Gather }.Where(n => n != null))
                CollectionAssert.Contains(KaykitClipNames, clip, cls + " clip " + clip);
            Assert.IsNotNull(c.Attack, cls + " attack");
            Assert.IsNotNull(c.Gather, cls + " gather");
            Assert.Greater(c.GatherRate, 0f);
        }

        [Test]
        public void TheNewClassesUseTheirOwnClipsNotTheFallback()
        {
            Assert.AreEqual(ArtMapping.Throw, ArtMapping.ClassClips(ClassId.Demolitions).Attack, "TNT throw");
            Assert.AreEqual(ArtMapping.PickUp, ArtMapping.ClassClips(ClassId.Demolitions).Ability, "mine plant");
            Assert.AreEqual(ArtMapping.UseItem, ArtMapping.ClassClips(ClassId.Builder).Ability, "build");
            Assert.AreEqual(ArtMapping.Cast, ArtMapping.ClassClips(ClassId.Healer).Attack, "mend");
            Assert.AreEqual(ArtMapping.UseItem, ArtMapping.ClassClips(ClassId.Healer).Ability, "healing pulse");
        }

        [Test]
        public void BothKaijuStagesCarrySwipeAndSevenExpressions()
        {
            foreach (string key in new[] { ArtMapping.KaijuHatchling, ArtMapping.KaijuAdult })
            {
                ModelSpec spec = ArtMapping.Spec(key);
                CollectionAssert.Contains(spec.OwnClips, ArtMapping.Swipe, key + " Swipe (Kaiju v4)");
                CollectionAssert.DoesNotContain(ArtMapping.LoopingClips, ArtMapping.Swipe, "Swipe is a one-shot");
                Assert.AreEqual(ArtMapping.KaijuFace, spec.FaceTexture, key);
                Assert.AreEqual(Enum.GetValues(typeof(KaijuExpression)).Length, spec.Expressions.Length, key + " expressions");
                Assert.AreEqual(spec.FaceTexture, spec.Expressions[(int)KaijuExpression.Neutral], key + ": Neutral is the default face");
                Assert.AreEqual(180f, spec.ArtYawDeg, key);
            }
        }

        [Test]
        public void EveryMappedArtFileIsOnDiskByName()
        {
            foreach (ModelSpec spec in ArtMapping.Models)
            {
                Assert.IsTrue(File.Exists(ProjectPath(spec.AssetPath)), spec.AssetPath);
                if (spec.FaceTexture != null) Assert.IsTrue(File.Exists(ProjectPath(spec.FaceTexture)), spec.FaceTexture);
                if (spec.OutlineWeights != null) Assert.IsTrue(File.Exists(ProjectPath(spec.OutlineWeights)), spec.OutlineWeights);
                if (spec.Expressions != null)
                    foreach (string e in spec.Expressions) Assert.IsTrue(File.Exists(ProjectPath(e)), e);
            }
        }

        // ------------------------------------------------------------------------------------------- Kaiju expressions

        [Test]
        public void ExpressionLookupCoversEveryStateAndEveryExpressionIsReachable()
        {
            var reached = new System.Collections.Generic.HashSet<KaijuExpression>();
            foreach (KaijuState s in Enum.GetValues(typeof(KaijuState)))
            foreach (double since in new[] { double.NegativeInfinity, 0.5, 100.0 })
            foreach (bool attacking in new[] { false, true })
            foreach (bool hurt in new[] { false, true })
            foreach (bool fed in new[] { false, true })
            foreach (double t in new[] { 0.0, ArtMapping.BlinkPeriodSeconds - ArtMapping.BlinkSeconds * 0.5 })
            {
                KaijuExpression e = ArtMapping.KaijuExpressionFor(s, since, attacking, hurt, fed, t);
                Assert.IsTrue(Enum.IsDefined(typeof(KaijuExpression), e), s + " -> " + e);
                Assert.IsTrue(File.Exists(ProjectPath(ArtMapping.KaijuFaceOf(e))), ArtMapping.KaijuFaceOf(e));
                reached.Add(e);
            }
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(KaijuExpression)), reached.ToArray(), "every expression is used by some state");
        }

        [Test]
        public void ExpressionsFollowTheStateWithTheirPriority()
        {
            const double calm = 100.0, t = 0.0;
            Assert.AreEqual(KaijuExpression.Neutral, ArtMapping.KaijuExpressionFor(KaijuState.Egg, calm, true, true, true, t), "the egg has no face");
            Assert.AreEqual(KaijuExpression.Neutral, ArtMapping.KaijuExpressionFor(KaijuState.Neutral, calm, false, false, false, t), "default");
            Assert.AreEqual(KaijuExpression.Happy, ArtMapping.KaijuExpressionFor(KaijuState.Azure, calm, false, false, false, t), "allied Azure");
            Assert.AreEqual(KaijuExpression.Happy, ArtMapping.KaijuExpressionFor(KaijuState.Crimson, calm, false, false, false, t), "allied Crimson");
            Assert.AreEqual(KaijuExpression.Happy, ArtMapping.KaijuExpressionFor(KaijuState.Neutral, calm, false, false, true, t), "fed");
            Assert.AreEqual(KaijuExpression.Angry, ArtMapping.KaijuExpressionFor(KaijuState.Azure, calm, true, false, false, t), "attacking beats allied");
            Assert.AreEqual(KaijuExpression.Hurt, ArtMapping.KaijuExpressionFor(KaijuState.Azure, calm, true, true, false, t), "hurt beats attacking");
            Assert.AreEqual(KaijuExpression.Berserk, ArtMapping.KaijuExpressionFor(KaijuState.Berserk, calm, true, true, true, t), "berserk beats all");
            Assert.AreEqual(KaijuExpression.Sleepy, ArtMapping.KaijuExpressionFor(KaijuState.Neutral, 0.5, false, false, false, t), "fresh hatchling");
            Assert.AreEqual(KaijuExpression.Angry, ArtMapping.KaijuExpressionFor(KaijuState.Neutral, 0.5, true, false, false, t), "a hatchling that attacks wakes up");
            Assert.AreEqual(KaijuExpression.Neutral,
                ArtMapping.KaijuExpressionFor(KaijuState.Neutral, ArtMapping.HatchlingSleepySeconds, false, false, false, t), "sleepy ends");
            Assert.AreEqual(KaijuExpression.Blink,
                ArtMapping.KaijuExpressionFor(KaijuState.Neutral, calm, false, false, false, ArtMapping.BlinkPeriodSeconds * 3 - 0.01), "blink on the timer");
            Assert.IsFalse(ArtMapping.Blinking(0.0));
            Assert.IsFalse(ArtMapping.Blinking(double.NaN));
            Assert.IsTrue(ArtMapping.Blinking(-0.01), "negative times wrap");
        }

        // ------------------------------------------------------------------------------------------- imported assets

        static ArtCatalog Catalog()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath);
            Assert.IsNotNull(cat, ArtCatalog.AssetPath + " (run ProjectSetup.Apply)");
            return cat;
        }

        [TestCaseSource(nameof(Classes))]
        public void EveryClassResolvesItsImportedModelClipsAndFaceByName(ClassId cls)
        {
            string key = ArtMapping.ClassModel(cls);
            ArtEntry e = Catalog().Find(key);
            Assert.IsNotNull(e, key + " not in the catalog (import, then ProjectSetup.Apply)");
            if (!ArtMapping.Spec(key).VertexColourFace) Assert.IsNotNull(e.Face, key + " face texture");
            ArtMapping.ClassClipSet c = ArtMapping.ClassClips(cls);
            foreach (string clip in new[] { ArtMapping.Idle, ArtMapping.Walk, ArtMapping.Run, c.Attack, c.Gather })
                Assert.IsNotNull(e.Clip(clip), key + " clip " + clip + " (KayKit_Clips.fbx must carry the animation pass clips)");
            if (c.Ability != null) Assert.IsNotNull(e.Clip(c.Ability), key + " ability clip " + c.Ability);
            Renderer[] rs = e.Model.GetComponentsInChildren<Renderer>(true);
            foreach (string mesh in ArtMapping.Spec(key).Meshes) Assert.IsTrue(rs.Any(r => r.name == mesh), key + " renderer " + mesh);
        }

        [Test]
        public void BothKaijuStagesResolveSwipeAndEveryExpressionTexture()
        {
            foreach (string key in new[] { ArtMapping.KaijuHatchling, ArtMapping.KaijuAdult })
            {
                ArtEntry e = Catalog().Find(key);
                Assert.IsNotNull(e, key);
                Assert.IsNotNull(e.Clip(ArtMapping.Swipe), key + " Swipe clip");
                foreach (KaijuExpression x in Enum.GetValues(typeof(KaijuExpression)))
                {
                    Texture2D tex = e.Expression((int)x);
                    Assert.IsNotNull(tex, key + " " + x);
                    Assert.AreSame(AssetDatabase.LoadAssetAtPath<Texture2D>(ArtMapping.KaijuFaceOf(x)), tex, key + " " + x);
                    var ti = (TextureImporter)AssetImporter.GetAtPath(ArtMapping.KaijuFaceOf(x));
                    Assert.IsTrue(ti.sRGBTexture, x + " sRGB");
                    Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapMode, x + " clamped like the default face");
                }
                Assert.AreSame(e.Face, e.Expression((int)KaijuExpression.Neutral), key + ": neutral is the default face");
            }
        }

        // ------------------------------------------------------------------------------------------- mascot

        static MascotReaction[] Reactions() => (MascotReaction[])Enum.GetValues(typeof(MascotReaction));

        [TestCaseSource(nameof(Reactions))]
        public void EveryMascotExpressionLoadsAtBothSizesWithItsImportRules(MascotReaction r)
        {
            foreach (int tier in new[] { 2, 4 })
            {
                string res = MascotGuide.PortraitResource(r, tier);
                var tex = Resources.Load<Texture2D>(res);
                Assert.IsNotNull(tex, res + " (Resources/UI/Mascot)");
                Assert.AreEqual(tier == 4 ? 512 : 256, tex.width, res + " width");
                var ti = (TextureImporter)AssetImporter.GetAtPath("Assets/_Game/Resources/" + res + ".png");
                Assert.IsNotNull(ti, res);
                Assert.IsTrue(ti.sRGBTexture, res + " sRGB");
                Assert.IsTrue(ti.mipmapEnabled, res + " mipmaps");
                Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapMode, res + " clamp");
                Assert.AreEqual(FilterMode.Trilinear, ti.filterMode, res + " trilinear");
                Assert.IsTrue(ti.alphaIsTransparency, res + " alpha");
            }
        }

        [Test]
        public void MascotPointsTowardTheSideOfTheTarget()
        {
            Assert.AreEqual(MascotReaction.Point, MascotGuide.PointFace(new Vector2(1f, 0f), MascotReaction.Neutral));
            Assert.AreEqual(MascotReaction.PointLeft, MascotGuide.PointFace(new Vector2(-1f, 0.2f), MascotReaction.Neutral));
            Assert.AreEqual(MascotReaction.PointLeft, MascotGuide.PointFace(new Vector2(-0.1f, 1f), MascotReaction.PointLeft), "dead zone keeps the side");
            Assert.IsTrue(MascotGuide.IsPointing(MascotReaction.PointLeft));
            Assert.IsFalse(MascotGuide.IsPointing(MascotReaction.Thinking));
            Assert.AreEqual(Enum.GetValues(typeof(MascotReaction)).Length, MascotGuide.Expressions.Distinct().Count(), "one portrait per expression");
        }

        // ------------------------------------------------------------------------------------------- environment v4

        [Test]
        public void ExtraClutterIsHighAndUpOnly()
        {
            Assert.IsFalse(QualityTable.ExtraClutter(FramePolicy.LevelVeryLow));
            Assert.IsFalse(QualityTable.ExtraClutter(FramePolicy.LevelLow));
            Assert.IsFalse(QualityTable.ExtraClutter(FramePolicy.LevelMedium));
            Assert.IsTrue(QualityTable.ExtraClutter(FramePolicy.LevelHigh));
            Assert.IsTrue(QualityTable.ExtraClutter(FramePolicy.LevelHigh + 1));
        }

        [Test]
        public void ClutterAddsTheV4PiecesWithoutMovingTheBasePlan()
        {
            WorldKitCatalog cat = WorldKitCatalog.Load();
            Assert.IsNotNull(cat, "WorldKitCatalog (ProjectSetup.Apply)");
            var map = SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateDefault());
            WorldPlan plain = WorldDresser.Plan(map, BasesFile.Load(), cat.Centroid, cat.Body);
            WorldPlan rich = WorldDresser.Plan(map, BasesFile.Load(), cat.Centroid, cat.Body, true);
            Assert.AreEqual(0, plain.ClutterCount, "off by default: tools/art/world-plan.tsv is unchanged");
            Assert.AreEqual(plain.Placements.Count + rich.ClutterCount, rich.Placements.Count);
            // The proposed rows count 525 placements and 12 damaged tiles on this map; the C# dresser's base plan differs a
            // little from layout2 (edge trees, keep-outs), so allow a margin.
            Assert.That(rich.ClutterCount, Is.InRange(350, 750), "about 525 v4 placements");
            Assert.That(rich.DamagedSpans, Is.InRange(4, 24), "about 12 damaged deck tiles");
            for (int i = 0; i < plain.Placements.Count; i++)
            {
                WorldPlacement a = plain.Placements[i], b = rich.Placements[i];
                Assert.AreEqual(a.Matrix, b.Matrix, "base placement " + i + " moved");
                if (a.Piece != b.Piece)
                    Assert.IsTrue(a.Piece == "env_bridge_span" && b.Piece == "env_bridge_span_damaged", a.Piece + " -> " + b.Piece);
            }
            foreach (string piece in rich.Counts().Keys)
                Assert.IsNotNull(cat.Find(piece), piece + " is not in the WorldKitCatalog (import Art/Environment, ProjectSetup.Apply)");
            Assert.AreEqual(rich.Hash(), WorldDresser.Plan(map, BasesFile.Load(), cat.Centroid, cat.Body, true).Hash(), "deterministic");
        }
    }
}

using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Physics audit 2026-11-12, runtime and feel seat (PR feat/perf-and-feel): the quality table, the character LOD
    /// contract, telegraph timing, the effect gating table, the "not ready" cue, HUD dirty logic and the shake model.
    /// </summary>
    public sealed class PerfAndFeelTests
    {
        static World NewWorld(GameConfig cfg) => World.CreateWorld(new WorldOptions
        {
            Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
        });

        // ---------------------------------------------------------------- quality table

        [Test]
        public void ShadowDistanceIs35At30AndOffByLevel()
        {
            Assert.AreEqual(0f, QualityTable.For(FramePolicy.LevelVeryLow).ShadowDistance);
            Assert.AreEqual(0f, QualityTable.For(FramePolicy.LevelLow).ShadowDistance);
            Assert.AreEqual(30f, QualityTable.For(FramePolicy.LevelMedium).ShadowDistance);
            Assert.AreEqual(35f, QualityTable.For(FramePolicy.LevelHigh).ShadowDistance);
            Assert.AreEqual(35f, QualityTable.For(5).ShadowDistance);
            Assert.IsFalse(QualityTable.For(FramePolicy.LevelLow).ShadowMap);
        }

        [Test]
        public void SmallPropsNeverCastAndLowUsesBlobShadows()
        {
            for (int level = 0; level <= 5; level++)
            {
                QualityRow r = QualityTable.For(level);
                Assert.IsFalse(r.PropShadows, "props cast at level " + level);
                Assert.AreEqual(!r.ShadowMap, r.BlobShadows, "blob shadows exactly where there is no shadow map, level " + level);
                Assert.AreEqual(r.ShadowMap ? FighterShadow.Proxy : FighterShadow.Blob, r.FighterShadows, "level " + level);
            }
        }

        [Test]
        public void LodPickFollowsContractC4()
        {
            QualityRow low = QualityTable.For(FramePolicy.LevelLow), med = QualityTable.For(FramePolicy.LevelMedium),
                high = QualityTable.For(FramePolicy.LevelHigh);
            Assert.AreEqual(0, AnimLod.PickLod(true, 100f, low), "your own character is always LOD0");
            Assert.AreEqual(1, AnimLod.PickLod(false, 1f, low), "Low: LOD1 for everyone else");
            Assert.AreEqual(0, AnimLod.PickLod(false, 24.9f, med));
            Assert.AreEqual(1, AnimLod.PickLod(false, 25.1f, med));
            Assert.AreEqual(0, AnimLod.PickLod(false, 25f, high));
            Assert.IsTrue(high.Lod1Outline);
            Assert.IsFalse(med.Lod1Outline);
            Assert.IsFalse(low.Lod1Outline);
        }

        [Test]
        public void AnimationCullsOffScreenAndThrottlesDistantFighters()
        {
            QualityRow high = QualityTable.For(FramePolicy.LevelHigh);
            Assert.IsTrue(AnimLod.ShouldEvaluate(true, false, false, 99f, 7, 3, high), "self always evaluates");
            Assert.IsTrue(AnimLod.ShouldEvaluate(false, true, false, 99f, 7, 3, high), "just activated evaluates once");
            Assert.IsFalse(AnimLod.ShouldEvaluate(false, false, false, 5f, 7, 3, high), "off screen holds its pose");
            Assert.IsTrue(AnimLod.ShouldEvaluate(false, false, true, 10f, 7, 3, high), "near and visible every frame");
            int evaluated = 0;
            for (int f = 0; f < 60; f++) if (AnimLod.ShouldEvaluate(false, false, true, 40f, f, 5, high)) evaluated++;
            Assert.AreEqual(30, evaluated, "half rate beyond 15 u");
            // staggered: two neighbours at the same distance do not evaluate on the same frames
            Assert.AreNotEqual(AnimLod.ShouldEvaluate(false, false, true, 40f, 10, 4, high), AnimLod.ShouldEvaluate(false, false, true, 40f, 10, 5, high));
        }

        // ---------------------------------------------------------------- LOD contract

        [Test]
        public void LodRolesComeFromTheContractNames()
        {
            Assert.AreEqual(FighterLod.Role.Hero, FighterLod.RoleOf("Warrior_Hero"));
            Assert.AreEqual(FighterLod.Role.Lod0, FighterLod.RoleOf("Warrior_LOD0"));
            Assert.AreEqual(FighterLod.Role.Lod1, FighterLod.RoleOf("Warrior_LOD1"));
            Assert.AreEqual(FighterLod.Role.Shadow, FighterLod.RoleOf("Warrior_Shadow"));
            Assert.AreEqual(FighterLod.Role.Shared, FighterLod.RoleOf("Warrior_Body"));
            Assert.AreEqual(FighterLod.Role.Shared, FighterLod.RoleOf("Warrior_Cleaver"));
            Assert.AreEqual("Miner", FighterLod.BaseName("Miner_LOD1"));
        }

        [Test]
        public void TodaysSingleMeshModelsFallBackToDrawingEverything()
        {
            // no suffixed meshes at all: every renderer is Shared, draws at both LODs and casts at Medium and High
            Assert.IsTrue(FighterLod.Draws(FighterLod.Role.Shared, false, 1, false, false));
            Assert.AreEqual(ShadowCastingMode.On, FighterLod.ShadowMode(FighterLod.Role.Shared, FighterShadow.Proxy, false));
            Assert.AreEqual(ShadowCastingMode.Off, FighterLod.ShadowMode(FighterLod.Role.Shared, FighterShadow.Blob, false));
            // LOD0 without LOD1 stays drawn far away; a Hero-only export still shows in a match
            Assert.IsTrue(FighterLod.Draws(FighterLod.Role.Lod0, false, 1, true, false));
            Assert.IsTrue(FighterLod.Draws(FighterLod.Role.Hero, false, 0, false, false));
        }

        [Test]
        public void FullChainDrawsOneLodAndOnlyTheProxyCasts()
        {
            Assert.IsTrue(FighterLod.Draws(FighterLod.Role.Lod0, false, 0, true, true));
            Assert.IsFalse(FighterLod.Draws(FighterLod.Role.Lod1, false, 0, true, true));
            Assert.IsTrue(FighterLod.Draws(FighterLod.Role.Lod1, false, 1, true, true));
            Assert.IsFalse(FighterLod.Draws(FighterLod.Role.Lod0, false, 1, true, true));
            Assert.IsFalse(FighterLod.Draws(FighterLod.Role.Hero, false, 0, true, true), "the Hero is never drawn in a match");
            Assert.IsTrue(FighterLod.Draws(FighterLod.Role.Hero, true, 0, true, true), "close cameras show the Hero");
            Assert.IsFalse(FighterLod.Draws(FighterLod.Role.Lod0, true, 0, true, true));
            Assert.AreEqual(ShadowCastingMode.ShadowsOnly, FighterLod.ShadowMode(FighterLod.Role.Shadow, FighterShadow.Proxy, true));
            Assert.AreEqual(ShadowCastingMode.Off, FighterLod.ShadowMode(FighterLod.Role.Lod0, FighterShadow.Proxy, true));
            Assert.AreEqual(ShadowCastingMode.Off, FighterLod.ShadowMode(FighterLod.Role.Lod1, FighterShadow.Proxy, true));
            Assert.AreEqual(ShadowCastingMode.Off, FighterLod.ShadowMode(FighterLod.Role.Shadow, FighterShadow.Blob, true));
            Assert.IsFalse(FighterLod.ProxyEnabled(FighterShadow.Blob));
        }

        [Test]
        public void ContractBudgetsMatchTheBuildList()
        {
            Assert.AreEqual(4500, FighterLod.Lod0Triangles);
            Assert.AreEqual(1500, FighterLod.Lod1Triangles);
            Assert.AreEqual(800, FighterLod.ShadowTriangles);
            Assert.AreEqual(15000, FighterLod.HeroMinTriangles);
            Assert.AreEqual(25000, FighterLod.HeroMaxTriangles);
        }

        // ---------------------------------------------------------------- telegraphs

        [Test]
        public void MeleeTelegraphFillsOverTheWholeWindup()
        {
            var cfg = new GameConfig();
            double ticks = cfg.Kaiju.MeleeWindupTicks;
            Assert.AreEqual(0f, Telegraphs.MeleeProgress((int)ticks, ticks, 0), 1e-6);
            float last = -1f;
            for (int left = (int)ticks; left >= 1; left--)
                for (double a = 0; a < 1; a += 0.25)
                {
                    float p = Telegraphs.MeleeProgress(left, ticks, a);
                    Assert.GreaterOrEqual(p, last);
                    last = p;
                }
            Assert.AreEqual(1f, Telegraphs.MeleeProgress(0, ticks, 0), 1e-6);
        }

        [Test]
        public void ReactionWindowsMeetTheOwnerMinimumsOnScreen()
        {
            var cfg = new GameConfig();
            foreach (double pace in new[] { 1.0, 2.0 })
            {
                Assert.GreaterOrEqual(Telegraphs.ClawWarningSeconds(cfg, pace), Telegraphs.ClawMinSeconds - 1e-9, "claw at pace " + pace);
                Assert.GreaterOrEqual(Telegraphs.SlamWarningSeconds(cfg, pace), Telegraphs.SlamMinSeconds - 1e-9, "slam at pace " + pace);
            }
            // today's sim wind-up alone (11 ticks at 30 Hz, pace 2) is 0.18 s on screen: the pre-warning makes up the rest
            Assert.AreEqual(11.0 / 30.0 / 2.0, Telegraphs.ClawWindupSeconds(cfg, 2), 1e-9);
            Assert.Greater(Telegraphs.ClawLeadSim(cfg, 2), 0);
            // a sim wind-up already long enough needs no pre-warning
            cfg.Kaiju.MeleeWindupTicks = 30;
            Assert.AreEqual(0, Telegraphs.ClawLeadSim(cfg, 2), 1e-9);
        }

        /// <summary>DG-87's audit on the sim wind-ups alone; holds once DG-84/DG-85 lengthen them (sim lane).</summary>
        [Test, Explicit("waits on the sim lane's DG-84/DG-85 wind-up lengths; the view pre-warning covers the gap meanwhile")]
        public void EveryWindupLastsAtLeastFourTenthsOfASecondOnScreen()
        {
            var cfg = new GameConfig();
            Assert.GreaterOrEqual(Telegraphs.ClawWindupSeconds(cfg, cfg.DefaultSpeed), Telegraphs.AuditFloorSeconds);
            Assert.GreaterOrEqual(Telegraphs.SlamWindupSeconds(cfg, cfg.DefaultSpeed), Telegraphs.AuditFloorSeconds);
        }

        [Test]
        public void ClawPreWarningAppearsOnlyWhenASwingCanStart()
        {
            var cfg = new GameConfig();
            World w = NewWorld(cfg);
            Kaiju k = w.Kaiju;
            k.State = KaijuState.Neutral;
            k.KoIn = 0;
            k.MeleeWindupIn = 0;
            k.TelegraphIn = 0;
            int t = 1;
            w.Actors[t].Alive = true;
            k.X = w.X[t] - 1.0;
            k.Z = w.Z[t];
            k.TargetId = t;
            k.MeleeCd = 5;
            Assert.AreEqual(-1, Telegraphs.ClawPreWarning(w, 2), "cooldown far from spent");
            k.MeleeCd = Telegraphs.ClawLeadSim(cfg, 2) * 0.5;
            Assert.AreEqual(t, Telegraphs.ClawPreWarning(w, 2));
            k.X = w.X[t] - cfg.Kaiju.MeleeRange - 1.0;
            Assert.AreEqual(-1, Telegraphs.ClawPreWarning(w, 2), "target out of reach");
            k.X = w.X[t] - 1.0;
            k.MeleeWindupIn = 3;
            Assert.AreEqual(-1, Telegraphs.ClawPreWarning(w, 2), "the wind-up itself takes over");
        }

        // ---------------------------------------------------------------- gating and pulses

        [Test]
        public void EveryEffectDeclaresItsGating()
        {
            foreach (FxKind kind in Enum.GetValues(typeof(FxKind)))
                Assert.IsTrue(FeelGate.TryGating(kind, out _), "no gating entry for FxKind." + kind);
            foreach (FeelEffect e in Enum.GetValues(typeof(FeelEffect)))
                Assert.IsTrue(FeelGate.TryGating(e, out _), "no gating entry for FeelEffect." + e);
        }

        [Test]
        public void PulsesStayAtOrUnderThreePerSecondAndTwoWithReduceFlashing()
        {
            foreach (float hz in new[] { FxScene.SlamPulseHz, FxScene.BerserkPulseHz, FxScene.ShieldPulseHz, FxScene.MeleePulseHz })
            {
                Assert.LessOrEqual(FeelGate.PulseHz(hz, false), FeelGate.MaxPulseHz);
                Assert.LessOrEqual(FeelGate.PulseHz(hz, true), FeelGate.ReducedPulseHz);
            }
            Assert.LessOrEqual(FxScene.SlamPulseHz, 2f, "DG-87: slam pulse at or under 2 Hz");
            // measured: count rising crossings of 0.5 over 10 wall seconds at pace 2, sampled at 240 Hz
            FeelGate.ReduceFlashingOverride = false;
            try
            {
                int rises = 0;
                float prev = FeelGate.Pulse(0, 2, 18f);
                for (int i = 1; i <= 2400; i++)
                {
                    double wall = i / 240.0;
                    float v = FeelGate.Pulse(wall * 2, 2, 18f);   // ask for 18 Hz: clamped to the ceiling
                    if (prev < 0.5f && v >= 0.5f) rises++;
                    prev = v;
                }
                Assert.LessOrEqual(rises, 31, "at most 3 pulses per second on screen");
            }
            finally { FeelGate.ReduceFlashingOverride = null; }
        }

        [Test]
        public void ReduceMotionTurnsOffShakeZoomAndSpotlight()
        {
            FeelGate.ReduceMotionOverride = true;
            FeelGate.ShakeOverride = 1f;
            try
            {
                Assert.AreEqual(0f, FeelGate.ShakeScale);
                Assert.IsFalse(FeelGate.Allowed(FeelEffect.CameraShake));
                Assert.IsFalse(FeelGate.Allowed(FeelEffect.CombatZoom));
                Assert.IsFalse(FeelGate.Allowed(FeelEffect.KaijuSpotlight));
                Assert.IsTrue(FeelGate.Allowed(FeelEffect.BlobShadow));
                FeelGate.ReduceMotionOverride = false;
                Assert.AreEqual(1f, FeelGate.ShakeScale);
                Assert.IsTrue(FeelGate.Allowed(FeelEffect.CombatZoom));
            }
            finally
            {
                FeelGate.ReduceMotionOverride = null;
                FeelGate.ShakeOverride = null;
            }
        }

        [Test]
        public void ShakeSettingDefaultsOffAndRoundTrips()
        {
            string[] keys = { GameSettings.ShakeScaleKey, GameSettings.ShakeKey };
            var saved = new Dictionary<string, (bool has, int v)>();
            foreach (string k in keys) saved[k] = (PlayerPrefs.HasKey(k), PlayerPrefs.GetInt(k, 0));
            try
            {
                foreach (string k in keys) PlayerPrefs.DeleteKey(k);
                Assert.AreEqual(0f, GameSettings.ShakeScale, "owner decision OD-6 / PR #28: shake off by default");
                Assert.IsFalse(GameSettings.CameraShake);
                GameSettings.ShakeScale = 0.5f;
                Assert.AreEqual(50, PlayerPrefs.GetInt(GameSettings.ShakeScaleKey), "same 0..100 int as PR #28");
                Assert.IsTrue(GameSettings.CameraShake);
                GameSettings.CameraShake = false;
                Assert.AreEqual(0f, GameSettings.ShakeScale);
            }
            finally
            {
                foreach (string k in keys)
                    if (saved[k].has) PlayerPrefs.SetInt(k, saved[k].v);
                    else PlayerPrefs.DeleteKey(k);
            }
        }

        [Test]
        public void ShakeOnlyForBigThingsNearYouAndStaysSmall()
        {
            Assert.Greater(CombatFx.ShakeAmount(FxKind.KaijuSlam, false, 45, 3, 5), 0f);
            Assert.AreEqual(0f, CombatFx.ShakeAmount(FxKind.KaijuSlam, false, 45, 30, 5));
            Assert.AreEqual(0f, CombatFx.ShakeAmount(FxKind.Hit, false, 40, 1, 5), "someone else's hit");
            Assert.AreEqual(0f, CombatFx.ShakeAmount(FxKind.Hit, true, 10, 0, 5), "a light hit on you");
            Assert.AreEqual(0f, CombatFx.ShakeAmount(FxKind.Swing, true, 0, 0, 5));
            for (float t = 0; t < 2f; t += 0.013f)
                Assert.LessOrEqual(CameraShake.Offset(1f, t).magnitude, CameraShake.MaxOffset * 1.4143f);
            Assert.AreEqual(Vector2.zero, CameraShake.Offset(0f, 0.5f));
        }

        // ---------------------------------------------------------------- not-ready cue

        [Test]
        public void AbilityPressOnCooldownCountsOnceAndBufferedPressesDoNot()
        {
            var cfg = new GameConfig();
            World w = NewWorld(cfg);
            double dt = 1.0 / cfg.SimHz;
            Actor a = w.Actors[0];
            a.Alive = true;
            a.StandUp = 0;
            AbilityDenial.ResetForTests();
            a.AbilityCd = 3.0;
            AbilityDenial.Observe(true, w, 0, dt);
            AbilityDenial.Observe(true, w, 0, dt);   // held: still one press
            Assert.AreEqual(1, AbilityDenial.Count);
            AbilityDenial.Observe(false, w, 0, dt);
            a.AbilityCd = 2 * dt;                    // ready inside PR #25's 4-tick buffer: it will fire, no cue
            AbilityDenial.Observe(true, w, 0, dt);
            Assert.AreEqual(1, AbilityDenial.Count);
            AbilityDenial.Observe(false, w, 0, dt);
            a.AbilityCd = 0;
            a.Alive = false;                         // dead: no cue
            AbilityDenial.Observe(true, w, 0, dt);
            Assert.AreEqual(1, AbilityDenial.Count);
            AbilityDenial.ResetForTests();
        }

        [Test]
        public void DeniedShakeIsFourPixelsAndSettles()
        {
            float max = 0f;
            for (float t = 0; t < GameHud.DeniedSeconds; t += 0.004f) max = Mathf.Max(max, Mathf.Abs(GameHud.DeniedOffset(t)));
            Assert.LessOrEqual(max, GameHud.DeniedShakePx + 1e-4f);
            Assert.Greater(max, 2f);
            Assert.AreEqual(0f, GameHud.DeniedOffset(GameHud.DeniedSeconds));
            Assert.AreEqual(0f, GameHud.DeniedOffset(-1f));
        }

        // ---------------------------------------------------------------- HUD split and dirty logic

        [Test]
        public void HudHasSeparateCanvasesForWorldMarksAndTheMinimap()
        {
            var go = new GameObject("HUD split test");
            try
            {
                GameHud hud = go.AddComponent<GameHud>();
                hud.Build();
                Assert.IsNotNull(hud.WorldCanvas);
                Assert.IsNotNull(hud.MinimapCanvas);
                Assert.AreNotSame(hud.Canvas, hud.WorldCanvas);
                Assert.AreNotSame(hud.Canvas, hud.MinimapCanvas);
                Assert.IsFalse(hud.WorldCanvas.isRootCanvas);
                Assert.IsFalse(hud.MinimapCanvas.isRootCanvas);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void FadeWritesOnlyOnAVisibleChange()
        {
            var go = new GameObject("fade test");
            try
            {
                CanvasGroup g = go.AddComponent<CanvasGroup>();
                Assert.IsFalse(GameHud.SetAlpha(g, 1f), "already 1");
                Assert.IsTrue(GameHud.SetAlpha(g, 0.5f));
                Assert.IsFalse(GameHud.SetAlpha(g, 0.5f + 0.001f), "under one 8-bit step");
                Assert.IsTrue(GameHud.SetAlpha(g, 0.2f));
                Assert.AreEqual(0.2f, g.alpha, 1e-6f);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void HudProjectsAfterTheCinemachineBrain()
        {
            var order = (DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(GameHud), typeof(DefaultExecutionOrder));
            Assert.IsNotNull(order);
            Assert.Greater(order.order, 100, "CinemachineBrain runs at 100");
            var shake = (DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(CameraShake), typeof(DefaultExecutionOrder));
            Assert.Greater(order.order, shake.order, "after the shake, so marks follow the rendered view");
            Assert.Greater(shake.order, 100);
        }

        // ---------------------------------------------------------------- perf report

        [Test]
        public void PhaseNumbersAreP50AndP95()
        {
            var l = new List<float>();
            for (int i = 1; i <= 100; i++) l.Add(i);
            StringAssert.Contains("\"p50\": 50", PerfOverlay.P50P95(l));
            StringAssert.Contains("\"p95\": 95", PerfOverlay.P50P95(l));
            Assert.AreEqual("null", PerfOverlay.P50P95(new List<float>()));
        }

        [Test]
        public void SunCastsOnlyInAMatchWithAShadowMap()
        {
            Assert.IsFalse(RenderBudget.SunCasts(true, QualityTable.For(FramePolicy.LevelHigh)), "title");
            Assert.IsFalse(RenderBudget.SunCasts(false, QualityTable.For(FramePolicy.LevelLow)), "Low");
            Assert.IsTrue(RenderBudget.SunCasts(false, QualityTable.For(FramePolicy.LevelMedium)));
        }
    }
}

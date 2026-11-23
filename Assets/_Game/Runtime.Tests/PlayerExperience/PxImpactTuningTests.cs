using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Player-experience wave, lane C (impact), 2026-11-22: the hit-feel numbers the owner's complaint turned into ("I can be
    /// in a player's face, swing, and I see nothing to indicate I've hit"), held without eyes. Pure functions and the
    /// CombatFeed decode only; the Editor frame strips judge the look.
    /// </summary>
    public sealed class PxImpactTuningTests
    {
        static readonly ClassId[] All = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        /// <summary>The shipped cameras: third person (GroundView Play) and the fresh-install top-down rig.</summary>
        static readonly (string name, double dist, double fov)[] Views =
        {
            ("third person", FollowCamera.ThirdDistance, FollowCamera.FovDefault),
            ("third person, widest FOV", FollowCamera.ThirdDistance, FollowCamera.FovMax),
            ("third person, narrowest FOV", FollowCamera.ThirdDistance, FollowCamera.FovMin),
            ("top-down", GameplayCamera.Distance, GameplayCamera.FovDeg),
        };

        // ---------------------------------------------------------------- 4. camera kick

        [Test]
        public void TheHitKickMeetsItsSizeTargetsInBothViews()
        {
            foreach (var v in Views)
            {
                float byMe = HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, v.dist, v.fov);
                float onMe = HitFeelTuning.KickU(HitFeelTuning.KickKind.OnMe, v.dist, v.fov);
                float kill = HitFeelTuning.KickU(HitFeelTuning.KickKind.MyKill, v.dist, v.fov);
                float death = HitFeelTuning.KickU(HitFeelTuning.KickKind.MyDeath, v.dist, v.fov);
                TestContext.Progress.WriteLine($"[px kick] {v.name}: by me {byMe:0.000} u = {HitFeelTuning.KickPx(byMe, v.dist, v.fov):0.0} px, " +
                                               $"on me {onMe:0.000} u = {HitFeelTuning.KickPx(onMe, v.dist, v.fov):0.0} px, kill {kill:0.000}, death {death:0.000} (1080p)");
                Assert.GreaterOrEqual(byMe, 0.03f, v.name + ": a hit by me moves the camera at least 0.03 u");
                Assert.GreaterOrEqual(onMe, 0.06f, v.name + ": a hit on me at least 0.06 u");
                Assert.Greater(onMe, byMe, v.name + ": being hit kicks harder than hitting");
                Assert.Greater(kill, byMe, v.name + ": my kill kicks harder than my hit");
                Assert.Greater(death, onMe, v.name + ": my death kicks hardest");
                float pxBy = HitFeelTuning.KickPx(byMe, v.dist, v.fov), pxOn = HitFeelTuning.KickPx(onMe, v.dist, v.fov);
                Assert.GreaterOrEqual(pxBy, 10.8f * 0.66f, v.name + ": readable (about 1% of a 1080 px screen)");
                Assert.LessOrEqual(pxOn, 1080f * 0.03f, v.name + ": never a yank (at most 3% of the screen)");
            }
            float third = HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, FollowCamera.ThirdDistance, FollowCamera.FovDefault);
            Assert.That(third, Is.InRange(0.04f, 0.07f), "third person: the brief's 0.04-0.06 u target band (+ rounding)");
            Assert.AreEqual(0f, HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, 4.4, 65, 0f), "HIT KICK 0 = off");
        }

        [Test]
        public void TheKickSpringsOutAndBackInAboutAHundredAndFiftyMilliseconds()
        {
            Assert.AreEqual(1f, HitFeelTuning.KickEnvelope(0), 1e-6f, "fix round: full ON the impact frame");
            Assert.AreEqual(1f, HitFeelTuning.KickEnvelope(HitFeelTuning.KickRise), 1e-4f, "full at the rise");
            Assert.AreEqual(0f, HitFeelTuning.KickEnvelope(HitFeelTuning.KickReturn), 1e-6f, "home again");
            Assert.That(HitFeelTuning.KickReturn, Is.InRange(0.12, 0.18));
            Assert.LessOrEqual(HitFeelTuning.KickRise, 0.034, "out within 2 frames (inside the 100 ms impact window)");
            Assert.AreEqual(0f, HitFeelTuning.KickEnvelope(-0.01), "nothing before");
        }

        // ---------------------------------------------------------------- fix round (Editor review 2026-11-22)

        static readonly ClassId[] Six = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        /// <summary>Defect 2: at melee range the old third-person camera drew the enemy behind my fighter; the duel framing
        /// must put the enemy's chest centre outside my drawn screen rectangle for every class at 1.15 u and 2 u, 16:9.</summary>
        [Test]
        public void TheDuelFramingShowsTheEnemyBesideMyFighterForEveryClass()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            double dist = FollowCamera.ThirdDistance * (1 + HitFeelTuning.FightDistanceShare);
            double piv = FollowCamera.ThirdPivotHeight + GroundView.FightLift;
            foreach (ClassId c in Six)
            {
                FightFrame.Silhouette(c, cfg.Class(c).Radius, out double hw, out double ht);
                double old = FightFrame.Margin(hw, ht, 1.15, FollowCamera.ThirdDistance, FollowCamera.ThirdPivotHeight, FollowCamera.ThirdPitch, FollowCamera.FovDefault, 0, 0);
                Assert.Less(old, 0, c + ": the old camera hid the enemy (the defect reproduced in numbers)");
                foreach (double d in new[] { 1.15, 2.0 })
                    foreach (int fov in new[] { FollowCamera.FovMin, FollowCamera.FovDefault, FollowCamera.FovMax })
                    {
                        double m = FightFrame.Margin(hw, ht, d, dist, piv, FollowCamera.ThirdPitch, fov + HitFeelTuning.FightFovAdd,
                            FightFrame.YawOffsetDeg, FightFrame.Shoulder(1));
                        TestContext.Progress.WriteLine($"[px duel] {c} at {d} u, FOV {fov}: margin old {old * 960:0} px -> {m * 960:0} px (1920 wide)");
                        Assert.Greater(m, 0, $"{c} at {d} u, FOV {fov}: the enemy's centre is clear of my fighter");
                    }
            }
            Assert.LessOrEqual(FightFrame.InSeconds, 0.3, "eases in under 0.3 s");
            Assert.Greater(FightFrame.OutSeconds, FightFrame.InSeconds, "eases out slower");
        }

        [Test]
        public void TheDuelYawOnlyTurnsAsFarAsNeededAndKeepsItsSide()
        {
            int side = 0;
            Assert.AreEqual(FightFrame.YawOffsetDeg, FightFrame.YawOffset(100, 100, ref side), 1e-9, "on the line: swing the full offset");
            Assert.AreEqual(1, side);
            Assert.AreEqual(FightFrame.YawOffsetDeg - 10, FightFrame.YawOffset(110, 100, ref side), 1e-9, "part way: only the rest");
            Assert.AreEqual(0, FightFrame.YawOffset(150, 100, ref side), 1e-9, "already side-on: nothing");
            Assert.AreEqual(FightFrame.YawOffsetDeg + 5, FightFrame.YawOffset(95, 100, ref side), 1e-9, "the side is kept (no flip-flop)");
            int s2 = 0;
            Assert.AreEqual(-FightFrame.YawOffsetDeg + 10, FightFrame.YawOffset(90, 100, ref s2), 1e-9, "the nearer side is chosen");
            Assert.AreEqual(FightFrame.ShoulderU, FightFrame.Shoulder(-1), 1e-9);
            Assert.AreEqual(1.0, FightFrame.Step(0, true, FightFrame.InSeconds), 1e-9);
            Assert.AreEqual(0.0, FightFrame.Step(1, false, FightFrame.OutSeconds), 1e-9);
        }

        /// <summary>Defect 3: the kick must reach the lens through CameraShake's real LateUpdate, full on the impact frame,
        /// independent of the shake slider, and come off again with Reset.</summary>
        [Test]
        public void TheHitKickMovesTheRealCameraOnTheImpactFrame()
        {
            Assume.That(!FeelGate.ReduceMotion, "reduce motion is on on this machine: the kick is rightly zero");
            var go = new UnityEngine.GameObject("px-kick-cam");
            float? keep = GameSettings.HitKickOverride;
            var offCams = new System.Collections.Generic.List<UnityEngine.Camera>();
            try
            {
                go.tag = "MainCamera";
                var cam = go.AddComponent<UnityEngine.Camera>();
                cam.fieldOfView = (float)GameplayCamera.FovDeg;
                go.transform.position = new UnityEngine.Vector3(3, 20, -10);
                go.transform.rotation = UnityEngine.Quaternion.Euler(60, 0, 0);
                // review pass 7: Camera.main must be THIS camera (CameraShake.Kick reads Camera.main's FOV); the test scene's own
                // Main Camera (FOV 60) won, so the kick was sized for another lens (0.297 u vs 0.162 u expected).
                foreach (UnityEngine.Camera other in UnityEngine.Camera.allCameras) if (other != cam) { other.enabled = false; offCams.Add(other); }
                Assert.AreSame(cam, UnityEngine.Camera.main, "the test camera is the main camera");
                // Editor only: AddComponent in edit mode sends Unity's Reset message, which meets CameraShake's static Reset().
                UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, "Failed to call static function Reset because an object was provided");
                var shake = go.AddComponent<CameraShake>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(CameraShake).GetMethod("OnEnable", flags).Invoke(shake, null);
                GameSettings.HitKickOverride = 1f;
                UnityEngine.Vector3 home = go.transform.position;
                float u = CameraShake.Kick(HitFeelTuning.KickKind.ByMe, UnityEngine.Vector3.right, 1);
                Assert.Greater(u, 0f);
                Assert.AreEqual(HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, CameraShake.FocusDistance(), cam.fieldOfView), u, 1e-4f);
                typeof(CameraShake).GetMethod("LateUpdate", flags).Invoke(shake, null);
                float moved = (go.transform.position - home).magnitude;
                TestContext.Progress.WriteLine($"[px kick] real LateUpdate moved the lens {moved:0.000} u = {HitFeelTuning.KickPx(moved, CameraShake.FocusDistance(), cam.fieldOfView):0.0} px at 1080p");
                Assert.AreEqual(u, moved, 1e-3f, "full kick on the impact frame");
                Assert.Greater(UnityEngine.Vector3.Dot(go.transform.position - home, UnityEngine.Vector3.right), 0f, "along the hit");
                CameraShake.Reset();
                Assert.AreEqual(0f, (go.transform.position - home).magnitude, 1e-5f, "taken off before the next frame's input");
                Assert.Greater(CameraShake.KickFrames, 0, "the frame counter the Editor review reads saw it");
            }
            finally
            {
                foreach (UnityEngine.Camera other in offCams) if (other != null) other.enabled = true;
                GameSettings.HitKickOverride = keep;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var s = go.GetComponent<CameraShake>();
                if (s != null) typeof(CameraShake).GetMethod("OnDisable", flags).Invoke(s, null);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- fix round 2 (review pass 2)

        [Test]
        public void TheThirdPersonKickReadsAboutFourteenAndTwentyPixelsAtTheEnemy()
        {
            // measured in pass 2 at the enemy's chest: my hit 7.5-9.1 px, my kill 12.1 px (sized at the 4.4 u focus, read ~6 u deep)
            double fov = FollowCamera.FovDefault + HitFeelTuning.FightFovAdd;
            double enemyDepth = FollowCamera.ThirdDistance * (1 + HitFeelTuning.FightDistanceShare) + 1.15 * Math.Cos(FightFrame.YawOffsetDeg * Math.PI / 180);
            float by = HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, FollowCamera.ThirdDistance, fov, 1f, true);
            float kill = HitFeelTuning.KickU(HitFeelTuning.KickKind.MyKill, FollowCamera.ThirdDistance, fov, 1f, true);
            float onMe = HitFeelTuning.KickU(HitFeelTuning.KickKind.OnMe, FollowCamera.ThirdDistance, fov, 1f, true);
            double pxBy = HitFeelTuning.KickPx(by, enemyDepth, fov), pxKill = HitFeelTuning.KickPx(kill, enemyDepth, fov);
            TestContext.Progress.WriteLine($"[px kick 3P] at the enemy ({enemyDepth:0.0} u deep): my hit {pxBy:0.0} px, my kill {pxKill:0.0} px");
            Assert.That(pxBy, Is.InRange(13.0, 17.0), "my hit about 14 px");
            Assert.That(pxKill, Is.InRange(19.0, 24.0), "my kill about 20 px");
            Assert.AreEqual(HitFeelTuning.KickU(HitFeelTuning.KickKind.OnMe, FollowCamera.ThirdDistance, fov), onMe, 1e-6f, "the hit on me is unchanged");
            Assert.AreEqual(HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, GameplayCamera.Distance, GameplayCamera.FovDeg),
                HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, GameplayCamera.Distance, GameplayCamera.FovDeg, 1f, false), 1e-6f, "top-down unchanged");
            Assert.Less(HitFeelTuning.Burst3PShare, 1f, "the third-person burst is smaller");
            Assert.LessOrEqual(HitFeelTuning.Burst3PLife, 0.2, "and shorter");
        }

        [Test]
        public void TheDuelYawReachesThirtySixDegreesNeverJumpsAndReleasesWithoutChasing()
        {
            const double dt = 1.0 / 60;
            int side = 0;
            double y = 100, enemy = 100, off = 0, maxStep = 0;
            for (int f = 0; f < 18; f++)   // 0.3 s
            {
                double ny = FightFrame.StepYaw(y, 100, true, enemy, ref side, dt, out off);
                maxStep = Math.Max(maxStep, Math.Abs(GroundCamera.DeltaAngle(y, ny)));
                y = ny;
            }
            Assert.AreEqual(FightFrame.YawOffsetDeg, Math.Abs(GroundCamera.DeltaAngle(enemy, y)), 1e-6, "36 deg off the line within 0.3 s");
            // the target lurches 20 deg in one frame (a knock): the lens follows at most MaxStepDeg per frame
            enemy = 120;
            for (int f = 0; f < 30; f++)
            {
                double ny = FightFrame.StepYaw(y, 100, true, enemy, ref side, dt, out off);
                maxStep = Math.Max(maxStep, Math.Abs(GroundCamera.DeltaAngle(y, ny)));
                y = ny;
            }
            Assert.LessOrEqual(maxStep, FightFrame.MaxStepDeg + 1e-9, "never a one-frame jump");
            // released (the target died or left 3.2 u): the offset eases to 0 over the follow yaw; the target is ignored
            double before = off;
            for (int f = 0; f < 60; f++) y = FightFrame.StepYaw(y, 100, false, 300, ref side, dt, out off);
            Assert.AreEqual(0, off, 1e-9, "home within 1 s");
            Assert.AreEqual(100, y, 1e-6, "on the follow yaw, not on the target");
            Assert.Greater(Math.Abs(before), 0);
        }

        /// <summary>Gate F03, stated honestly: the drawn victim may sit at most MaxDrawnOffset (0.34 u) off its interpolated sim
        /// spot (the 0.35 u placement check), so at 100 ms the drawn knock is min(0.8 x knock, sim body + 0.34 u): every knock
        /// up to about 0.56 u (worst-case interpolation) reaches 0.8x; bigger ones read 0.53-0.72x at 100 ms and catch up.</summary>
        [Test]
        public void TheDrawnKnockPromiseIsTheEightyPercentOrTheSimBodyPlusTheLead()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            double decay = Knock.DecayPerTick(cfg), hz = cfg.SimHz, t = HitFeelTuning.KnockCheckSeconds;
            foreach (double k in new[] { 0.38, 0.56, 0.9, 1.2, 1.63 })
            {
                double sim = k * HitFeelTuning.SimKnockShare(t * hz - 1, decay);
                double drawn = sim + Math.Min(HitFeelTuning.KnockLead(t, k, sim), HitFeelTuning.MaxDrawnOffset);
                double promise = Math.Min(HitFeelTuning.KnockShare * k, sim + HitFeelTuning.MaxDrawnOffset);
                TestContext.Progress.WriteLine($"[px knock promise] {k:0.00} u: drawn {drawn:0.00} u ({drawn / k:0.00}x), promise {promise:0.00} u");
                Assert.GreaterOrEqual(drawn, promise - 1e-9, $"knock {k} u");
            }
        }

        /// <summary>Round 3 (review pass 3: no HitFlash / HitStop in the ledger): a hit by the local player, frame by frame as
        /// ActorViews draws it (flash on 8 frames, hold 5 frames, knock check once, a skipped frame in the middle), reports
        /// HitFlash, HitStop and KnockVisual each exactly once, with a non-null source; a second hit reports again.</summary>
        [Test]
        public void AHitByMeReportsFlashHitStopAndKnockExactlyOnceWithASource()
        {
            var go = new UnityEngine.GameObject("px-cue-source");
            try
            {
                var src = go.AddComponent<UnityEngine.MeshRenderer>();
                CriticalMass.Runtime.Game.PlayerFeedback.Clear();
                int cursor = CriticalMass.Runtime.Game.PlayerFeedback.Head;
                var seen = ImpactCueLedger.Make(4);
                const int victim = 2;
                double flashStart = 10.0;
                for (int f = 0; f < 12; f++)
                {
                    if (f == 4) continue;   // a frame the victim was not drawn (never loses the cue)
                    ImpactCueLedger.Frame(ref seen[victim], victim, src,
                        f < 8, true, flashStart, HitFeelTuning.FlashStrength(true, true),
                        f < 5, true, 300, 80f,
                        f == 6, true, 0.5f);
                }
                foreach (var ch in new[] { CriticalMass.Runtime.Game.FeedbackChannel.HitFlash, CriticalMass.Runtime.Game.FeedbackChannel.HitStop,
                                           CriticalMass.Runtime.Game.FeedbackChannel.KnockVisual })
                {
                    Assert.AreEqual(1, CriticalMass.Runtime.Game.PlayerFeedback.Count(ch, cursor, victim), ch + " exactly once");
                    Assert.IsTrue(CriticalMass.Runtime.Game.PlayerFeedback.TryFind(ch, cursor, victim, out var entry));
                    Assert.IsNotNull(entry.Source, ch + " has a source");
                }
                // the next hit (new flash start, new hold tick) reports again
                ImpactCueLedger.Frame(ref seen[victim], victim, src, true, true, 10.5, 0.85f, true, true, 315, 80f, false, true, 0f);
                Assert.AreEqual(2, CriticalMass.Runtime.Game.PlayerFeedback.Count(CriticalMass.Runtime.Game.FeedbackChannel.HitFlash, cursor, victim));
                Assert.AreEqual(2, CriticalMass.Runtime.Game.PlayerFeedback.Count(CriticalMass.Runtime.Game.FeedbackChannel.HitStop, cursor, victim));
                // someone else's hit (not own) reports nothing
                ImpactCueLedger.Frame(ref seen[1], 1, src, true, false, 11.0, 0.62f, true, false, 330, 60f, true, false, 0.3f);
                Assert.AreEqual(0, CriticalMass.Runtime.Game.PlayerFeedback.Count(CriticalMass.Runtime.Game.FeedbackChannel.HitFlash, cursor, 1));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>Round 3: the third-person own flash spans the impact frame and the hold, stays short, and the starts stay
        /// under 3 a second; the near-side contact spark is a small part of the victim.</summary>
        [Test]
        public void TheThirdPersonOwnFlashCoversTheHoldAndTheNearSparkIsSmall()
        {
            Assert.GreaterOrEqual(HitFeelTuning.FlashSeconds(true, true), 0.15);
            Assert.LessOrEqual(HitFeelTuning.FlashSeconds(true, true), HitFeelTuning.FlashMinGapSeconds * 0.6);
            Assert.AreEqual(HitFeelTuning.FlashSeconds(true), HitFeelTuning.FlashSeconds(true, false));
            Assert.AreEqual(HitFeelTuning.FlashSeconds(false), HitFeelTuning.FlashSeconds(false, true));
            Assert.That(HitFeelTuning.NearSpark3PLines, Is.InRange(3, 4));
        }

        /// <summary>Fix round 5 (impact pop): the third-person burst sits on the victim's camera-near side, beside his body
        /// centre across the view by more than its own radius (his centre stays clear), on the attacker's weapon side, and
        /// within a burst of him (never floating off); bigger than the pass-4 0.6 share, smaller than the pass-2 full size.</summary>
        [Test]
        public void TheThirdPersonBurstSitsNearSideBesideTheVictimNeverOverHisCentre()
        {
            float r = 0.4f;
            float size = HitFeelTuning.BurstSize(0, true, 20) * HitFeelTuning.Burst3PShare;
            Assert.That(HitFeelTuning.Burst3PShare, Is.InRange(0.7f, 0.9f));
            var chest = new UnityEngine.Vector3(0f, 0.95f, 0f);
            foreach (float camDeg in new[] { 0f, 36f, -36f, 90f, 180f })
            {
                float a = camDeg * UnityEngine.Mathf.Deg2Rad;
                var lens = chest + new UnityEngine.Vector3(UnityEngine.Mathf.Sin(a), 0.8f, -UnityEngine.Mathf.Cos(a)) * 3.2f;   // a 3P lens 3.2 u off, any yaw
                var hitDir = new UnityEngine.Vector3(UnityEngine.Mathf.Sin(a + 0.4f), 0f, UnityEngine.Mathf.Cos(a + 0.4f));   // attacker roughly between lens and him
                UnityEngine.Vector3 p = HitFeelTuning.ContactSpark3P(chest, lens, hitDir, r, size);
                UnityEngine.Vector3 toLens = lens - chest; toLens.y = 0f; toLens.Normalize();
                UnityEngine.Vector3 across = new UnityEngine.Vector3(toLens.z, 0f, -toLens.x);
                UnityEngine.Vector3 off = p - chest;
                Assert.Greater(UnityEngine.Vector3.Dot(off, toLens), 0.9f * r, "near side at " + camDeg);
                float side = UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(off, across));
                Assert.Greater(side, size * 0.5f, "his centre clear of the burst at " + camDeg);
                Assert.Less(side, size * 0.5f + r, "the burst still touches his edge at " + camDeg);
                UnityEngine.Vector3 weapon = new UnityEngine.Vector3(hitDir.z, 0f, -hitDir.x);
                Assert.Greater(UnityEngine.Vector3.Dot(off, across) * UnityEngine.Vector3.Dot(weapon, across), 0f, "on the weapon side at " + camDeg);
                Assert.AreEqual(chest.y, p.y, 1e-4f, "chest height");
            }
        }

        /// <summary>Fix round 5 (item 4): the predicted wind-up commits on the press frame (>= 0.8 of the pose by 2 frames of
        /// 60 fps after it), holds short of the strike while the sim's tick is pending, and gives up after the timeout.</summary>
        [Test]
        public void APredictedPressCommitsTheWindupWithinTwoFrames()
        {
            double windup = 12 / 30.0;
            Assert.IsTrue(HitFeelTuning.PredictedSwing(0, windup, out double a0, out float w0));
            Assert.Greater(w0, 0.3f, "the press frame already moves");
            Assert.IsTrue(HitFeelTuning.PredictedSwing(2 / 60.0, windup, out _, out float w2));
            Assert.GreaterOrEqual(w2, 0.8f, "committed by 2 frames");
            Assert.IsTrue(HitFeelTuning.PredictedSwing(0.05, windup, out double a6, out _));
            Assert.LessOrEqual(a6, windup * HitFeelTuning.PredictHoldShare + 1e-9, "never the strike before the sim swings");
            Assert.IsFalse(HitFeelTuning.PredictedSwing(HitFeelTuning.PredictTimeout + 0.01, windup, out _, out _), "a refused press fades");
        }

        /// <summary>Fix round 5 (item 9): a missing or empty input-actions asset is named (so OnEnable logs one error and
        /// hands the seat to the autopilot) instead of throwing; a complete Player map passes.</summary>
        [Test]
        public void AMissingOrEmptyInputActionsAssetIsNamedNotThrown()
        {
            Assert.IsNotNull(CriticalMass.Runtime.Input.HumanInputSource.ActionsProblem(null));
            var asset = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.InputSystem.InputActionAsset>();
            try
            {
                StringAssert.Contains("no Player map", CriticalMass.Runtime.Input.HumanInputSource.ActionsProblem(asset));
                var map = UnityEngine.InputSystem.InputActionSetupExtensions.AddActionMap(asset, "Player");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddAction(map, "Move");
                StringAssert.Contains("'Point'", CriticalMass.Runtime.Input.HumanInputSource.ActionsProblem(asset));
                foreach (string a in CriticalMass.Runtime.Input.HumanInputSource.RequiredActions)
                    if (map.FindAction(a, false) == null) UnityEngine.InputSystem.InputActionSetupExtensions.AddAction(map, a);
                Assert.IsNull(CriticalMass.Runtime.Input.HumanInputSource.ActionsProblem(asset));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        /// <summary>Fix round 5 (item 8): the first-match class-pick card's body line is 26 px at 1080p (at least 22 if it must
        /// shrink), three lines fit its box, and DIFFICULTY is above the 16 px floor.</summary>
        [Test]
        public void TheClassPickCardBodyTextIsReadableAt1080p()
        {
            Assert.GreaterOrEqual(CriticalMass.Runtime.Menus.StarterPick.BodyType, 26);
            Assert.GreaterOrEqual(CriticalMass.Runtime.Menus.StarterPick.BodyTypeMin, 22);
            Assert.LessOrEqual(3 * CriticalMass.Runtime.Menus.StarterPick.BodyType * CriticalMass.Runtime.Menus.StarterPick.BodyLineSpacing,
                CriticalMass.Runtime.Menus.StarterPick.BodyBoxH, "three lines fit the box");
            Assert.Greater(CriticalMass.Runtime.Menus.StarterPick.DifficultyType, CriticalMass.Runtime.Hud.Style.UiScale.TypeMin);
        }

        static int OrderOf(Type t)
        {
            var a = (UnityEngine.DefaultExecutionOrder)Attribute.GetCustomAttribute(t, typeof(UnityEngine.DefaultExecutionOrder));
            return a != null ? a.order : 0;
        }

        /// <summary>Fix round 5 (item 6): the enemy name tags (DepthHud) are placed after the hit kick (CameraShake) and after
        /// the health bars (GameHud), through the same camera, so the name never parts from his bar on an impact frame; and
        /// before LowViewTags, which scales and hides them afterwards.</summary>
        [Test]
        public void NameTagsArePlacedAfterTheKickLikeTheHealthBars()
        {
            // fix round 6: the DepthHud itself stays at the default order (the bar tests need it there); its late tag pass moves
            Assert.AreEqual(0, OrderOf(typeof(CriticalMass.Runtime.Hud.Mechanics.DepthHud)), "the HUD itself keeps the default order");
            int tags = OrderOf(typeof(CriticalMass.Runtime.Hud.Mechanics.DepthHudTagsLate));
            Assert.Greater(tags, OrderOf(typeof(CameraShake)), "after the kick is applied");
            Assert.GreaterOrEqual(tags, OrderOf(typeof(CriticalMass.Runtime.Hud.GameHud)), "with the bars' camera");
            Assert.Less(tags, OrderOf(typeof(CriticalMass.Runtime.View.LowViewTags)), "before the low-view tag pass");
        }

        /// <summary>Fix round 6 (item 1, review pass 6: TNT thrown, predicted wind-up held at 0.56 for 566 ms+): a predicted
        /// wind-up hands over to the sim's melee swing; any other action (TNT throw, ability, skill cue, verb) or no swing
        /// within the timeout aborts it, and the abort's fade ends by 0.12 s after the press.</summary>
        [Test]
        public void APredictedWindupHandsOverOrFadesWithinPoint12Seconds()
        {
            Assert.AreEqual(HitFeelTuning.PredictStep.HandOver, HitFeelTuning.StepPrediction(true, false, 0.02), "the sim swung");
            Assert.AreEqual(HitFeelTuning.PredictStep.HandOver, HitFeelTuning.StepPrediction(true, true, 0.02), "a swing wins over its own AnimAction reset");
            Assert.AreEqual(HitFeelTuning.PredictStep.Abort, HitFeelTuning.StepPrediction(false, true, 0.02), "TNT / ability / verb started instead");
            Assert.AreEqual(HitFeelTuning.PredictStep.Continue, HitFeelTuning.StepPrediction(false, false, 0.03), "waiting for the tick");
            Assert.AreEqual(HitFeelTuning.PredictStep.Abort, HitFeelTuning.StepPrediction(false, false, HitFeelTuning.PredictTimeout + 0.001), "refused: no swing");
            Assert.LessOrEqual(HitFeelTuning.PredictTimeout + AttackTiming.Retrigger, 0.12 + 1e-9, "faded by 0.12 s after the press, worst case");
            Assert.Greater(HitFeelTuning.PredictTimeout, 1.0 / 30.0, "longer than one sim tick, so an accepted press is not aborted");
        }

        /// <summary>Fix round 7 (review pass 7: Warrior 0.56 at press + 2): after the hand-over the floor keeps rising on the
        /// press's clock, so two 60 fps frames after the press the drawn weight is >= 0.8 whatever tick the sim started on.</summary>
        [Test]
        public void TheHandedOverSwingReachesPoint8TwoFramesAfterThePress()
        {
            float atPlus2 = HitFeelTuning.RisingFloor(2.0 / 60.0 - 1.0 / 60.0);   // the press frame is +0 and already drew one frame
            Assert.GreaterOrEqual(atPlus2, HitFeelTuning.CommitAtCheck, "press + 2: " + atPlus2);
            Assert.GreaterOrEqual(HitFeelTuning.RisingFloor(2.0 / 60.0), HitFeelTuning.CommitAtCheck, "press + 2, press seen a frame early");
            Assert.AreEqual(0f, HitFeelTuning.RisingFloor(HitFeelTuning.FloorSeconds + 0.01), "the floor ends (never holds the pose)");
            Assert.AreEqual(0f, HitFeelTuning.RisingFloor(-1), "no floor before the press");
        }

        /// <summary>Fix round 7 (ModelsStandOnTheirGround): inside the 100 ms check the knock lead is untouched (F03); from
        /// KnockCheckSeconds + KnockCapEaseSeconds the model is within MaxDrawnOffset of the interpolated spot (its ring).</summary>
        [Test]
        public void AfterTheFirstTenthTheKnockStaysWithinTheRingTolerance()
        {
            var off = new UnityEngine.Vector3(0.34f, 0.1f, 0f);
            var shift = new UnityEngine.Vector3(0.13f, 0f, 0f);   // the current-tick shift (0.47 u in all, the measured worst)
            UnityEngine.Vector3 at100 = HitFeelTuning.CapFromInterpolated(off, shift, HitFeelTuning.KnockCheckSeconds);
            Assert.AreEqual(off, at100, "the 100 ms frame keeps the full lead");
            UnityEngine.Vector3 late = HitFeelTuning.CapFromInterpolated(off, shift, HitFeelTuning.KnockCheckSeconds + HitFeelTuning.KnockCapEaseSeconds);
            float total = new UnityEngine.Vector2(late.x + shift.x, late.z + shift.z).magnitude;
            Assert.LessOrEqual(total, HitFeelTuning.MaxDrawnOffset + 1e-5f, "capped from the interpolated spot");
            Assert.AreEqual(0.1f, late.y, 1e-6f, "the height is not touched");
            UnityEngine.Vector3 mid = HitFeelTuning.CapFromInterpolated(off, shift, HitFeelTuning.KnockCheckSeconds + HitFeelTuning.KnockCapEaseSeconds * 0.5);
            float midTotal = new UnityEngine.Vector2(mid.x + shift.x, mid.z + shift.z).magnitude;
            Assert.That(midTotal, Is.InRange(HitFeelTuning.MaxDrawnOffset, 0.47f), "eased, no pop");
            Assert.LessOrEqual(HitFeelTuning.MaxDrawnOffset, 0.35f, "inside the grounding test's 0.35 u");
        }

        /// <summary>Fix round 7 (review pass 7 (a): the TNT throw pose held to +24 frames): the throw cue is down to 0 by 8 frames
        /// after the throw, full for the first 3, and its clip has played to the end by then (the pose is released).</summary>
        [Test]
        public void TheThrowPoseIsReleasedEightFramesAfterTheThrow()
        {
            HitFeelTuning.ThrowPose(0, 0.9, out double t0, out float w0);
            Assert.AreEqual(1f, w0, "full on the throw frame");
            HitFeelTuning.ThrowPose(2.0 / 60.0, 0.9, out _, out float w2);
            Assert.AreEqual(1f, w2, "full 2 frames on");
            HitFeelTuning.ThrowPose(6.0 / 60.0, 0.9, out _, out float w6);
            Assert.Less(w6, 0.5f, "mostly down by +6: " + w6);
            HitFeelTuning.ThrowPose(8.0 / 60.0, 0.9, out double t8, out float w8);
            Assert.AreEqual(0f, w8, "released by +8");
            Assert.AreEqual(0.9, t8, 1e-9, "the clip has played out by +8 (the animator drops it)");
            Assert.AreEqual(0.0, t0, 1e-9);
        }

        /// <summary>Fix round 7 (review pass 7 (c)): a press during the cooldown is acknowledged by a capped wind-up that fades by
        /// 0.12 s: visible (>= 0.3) but clearly not a swing (<= 0.5), and on the predicted-press timing (gone by 0.12 s).</summary>
        [Test]
        public void ACooldownPressShowsAShortCappedWindup()
        {
            Assert.That(HitFeelTuning.RefusedPressWeight, Is.InRange(0.3f, 0.5f));
            Assert.IsTrue(HitFeelTuning.PredictedSwing(1.0 / 60.0, 0.4, out double age, out float w));
            Assert.Greater(Math.Min(w, HitFeelTuning.RefusedPressWeight), 0.3f, "seen one frame after the press");
            Assert.LessOrEqual(age, 0.4 * HitFeelTuning.PredictHoldShare + 1e-9, "held short of the strike");
            Assert.IsFalse(HitFeelTuning.PredictedSwing(HitFeelTuning.PredictTimeout + 0.001, 0.4, out _, out _), "ends at the timeout");
        }

        /// <summary>Fix round 6 (advisory 4): the reduce-flashing flash (the default) is clearly gentler than the intense one
        /// (at most 30% of its unshaded share, 60% of its length) and still visible (share >= 0.2, >= 4 frames at 60 fps).</summary>
        [Test]
        public void TheReducedFlashIsGentlerButStillVisible()
        {
            UnityEngine.Vector4 safe = HitFeelTuning.HitFlashVector(true), full = HitFeelTuning.HitFlashVector(false);
            Assert.LessOrEqual(safe.w, 0.3f * full.w + 1e-4f, "dimmer");
            Assert.GreaterOrEqual(safe.w, 0.2f, "still visible");
            foreach (bool own in new[] { true, false })
                foreach (bool third in new[] { true, false })
                {
                    double s = HitFeelTuning.FlashSeconds(own, third, true), f = HitFeelTuning.FlashSeconds(own, third, false);
                    Assert.Less(s, f, "shorter");
                    Assert.GreaterOrEqual(s, 4 / 60.0 - 1e-9, "at least 4 frames");
                }
        }

        [Test]
        public void TheTopDownAimLeanIsCappedAndUsesTheRawAim()
        {
            double px = HitFeelTuning.KickPx((float)CameraFollow.AimLeanMax, GameplayCamera.Distance, GameplayCamera.FovDeg);
            TestContext.Progress.WriteLine($"[px lean] top-down aim lean cap {CameraFollow.AimLeanMax} u = {px:0} px at 1080p (was up to 51 px measured)");
            Assert.LessOrEqual(px, 30, "the aim lean never drifts the view more than about 30 px");
        }

        [Test]
        public void TheHitKickSettingDefaultsOnAndIsSeparateFromTheShakeSlider()
        {
            Assert.AreEqual(1f, GameSettings.HitKickDefault, "on at full by default");
            Assert.AreEqual(1.5f, GameSettings.HitKickMax);
            CollectionAssert.Contains(GameSettings.AllKeys, GameSettings.HitKickKey, "reset and the test backups know the key");
            Assert.AreNotEqual(GameSettings.ShakeScaleKey, GameSettings.HitKickKey);
            Assert.AreNotEqual(GameSettings.ShakeKey, GameSettings.HitKickKey);
        }

        // ---------------------------------------------------------------- 1a. flash

        [Test]
        public void TheSafeFlashIsObviousAndStaysUnderThreeFlashesASecond()
        {
            Assert.GreaterOrEqual(HitFeelTuning.FlashStrength(true, true), 0.6f, "reduce flashing ON, my hit: at least 60%");
            Assert.GreaterOrEqual(HitFeelTuning.FlashStrength(true, false), 0.6f, "reduce flashing ON, others: at least 60%");
            Assert.AreEqual(1f, HitFeelTuning.FlashStrength(false, true), "reduce flashing OFF: full white");
            foreach (bool own in new[] { true, false })
                Assert.That(HitFeelTuning.FlashSeconds(own), Is.InRange(0.1, 0.13), "100-130 ms");
            Assert.Less(HitFeelTuning.MaxFlashesPerSecond, 3.0, "WCAG 2.3.1");
            // a victim hit every sim tick for 3 s: count the flash starts in every 1 s window
            const double hz = 30;
            double last = double.NegativeInfinity;
            var starts = new System.Collections.Generic.List<double>();
            for (int k = 0; k < 90; k++)
            {
                double t = k / hz;
                if (HitFeelTuning.FlashAllowed(t - last)) { last = t; starts.Add(t); }
            }
            foreach (double s in starts)
            {
                int inWindow = 0;
                foreach (double u in starts) if (u >= s && u < s + 1.0) inWindow++;
                Assert.LessOrEqual(inWindow, 3, $"at most 3 flashes in the second from {s:0.00} s");
            }
            Assert.IsTrue(HitFeelTuning.FlashAllowed(double.PositiveInfinity), "the first hit always flashes");
        }

        // ---------------------------------------------------------------- 1b. hit-stop

        [Test]
        public void HitStopIsSixtyToOneHundredTenMillisecondsOnBothFightersAndOffUnderReduceMotion()
        {
            double[] want = { 0.06, 0.08, 0.11 };
            for (byte t = 0; t <= 2; t++)
            {
                Assert.AreEqual(want[t], HitFeelTuning.HoldWallSeconds(t, false), 1e-9);
                Assert.AreEqual(want[t], HitFeel.HoldWallSeconds(t, false), 1e-9, "HitFeel delegates to the tuning");
                Assert.AreEqual(0, HitFeelTuning.HoldWallSeconds(t, true), "reduce motion: no hit-stop");
                Assert.GreaterOrEqual(want[t] * 60, 3.0, "at least 3 frames at 60 fps");
            }
        }

        // ---------------------------------------------------------------- 1c. knock

        [Test]
        public void TheDrawnVictimTravelsEightyPercentOfTheKnockWithinAHundredMilliseconds()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            double decay = Knock.DecayPerTick(cfg), hz = cfg.SimHz;
            double t = HitFeelTuning.KnockCheckSeconds;
            // fix round 5 (item 7): a fresh knock is drawn at the CURRENT tick (KnockAlphaLead 1), no longer up to a tick
            // behind it, so the sim body's share at 100 ms is the full 3 ticks
            Assert.AreEqual(1.0, HitFeelTuning.KnockAlphaLead(t), 1e-9, "no interpolation lag at 100 ms");
            foreach (double k in new[] { 0.19, 0.3, 0.44, 0.56 })
            {
                double sim = k * HitFeelTuning.SimKnockShare(t * hz, decay);
                double drawn = sim + HitFeelTuning.KnockLead(t, k, sim);
                TestContext.Progress.WriteLine($"[px knock] {k:0.00} u: sim alone {sim:0.000} u, drawn {drawn:0.000} u ({drawn / k:0.00}x) at {t * 1000:0} ms");
                Assert.GreaterOrEqual(drawn, HitFeelTuning.KnockShare * k - 1e-9, $"knock {k} u: at least 0.8x drawn by 100 ms");
                Assert.LessOrEqual(HitFeelTuning.KnockLead(t, k, sim), HitFeelTuning.MaxKnockLead + 1e-9, "the lead is capped");
            }
            // bigger knocks at the typical 3 ticks in: 0.8x up to about 0.95 u; past that the placement cap rules (FINDINGS: S1)
            foreach (double k in new[] { 0.75, 0.9 })
            {
                double sim = k * HitFeelTuning.SimKnockShare(t * hz, decay);
                Assert.GreaterOrEqual(sim + HitFeelTuning.KnockLead(t, k, sim), HitFeelTuning.KnockShare * k - 1e-9, $"knock {k} u at 3 ticks");
            }
            // tiny knocks still recoil visibly
            double tiny = 0.19, tinySim = tiny * HitFeelTuning.SimKnockShare(0.07 * hz, decay);
            Assert.GreaterOrEqual(tinySim + HitFeelTuning.KnockLead(0.07, tiny, tinySim), 0.27, "a Miner-on-Warrior knock recoils ~0.3 u");
            // an overshoot and a settle, then nothing left once the sim body has arrived
            Assert.Greater(HitFeelTuning.KnockWant(HitFeelTuning.KnockFrontSeconds, 1.0), 1.0, "overshoots");
            Assert.AreEqual(1.0, HitFeelTuning.KnockWant(0.5, 1.0), 1e-6, "settles on the knock");
            Assert.AreEqual(0, HitFeelTuning.KnockLead(HitFeelTuning.KnockLeadFadeTo, 1.0, 0.0), "the lead is gone by 0.6 s even against a wall");
            Assert.AreEqual(0, HitFeelTuning.KnockLead(0.05, 0.5, 0.6), "no lead once the body is past the want");
            Assert.LessOrEqual(HitFeelTuning.MaxDrawnOffset, 0.35f, "inside the play-mode placement check (0.35 u)");
        }

        /// <summary>Fix round 5 (item 7): big knocks (up to 1.6 u) draw at least the sim body's own travel at 100 ms (the old
        /// one-tick interpolation lag drew 0.53-0.72 of it), and the lag returns smoothly once the slide is over.</summary>
        [Test]
        public void BigKnocksDrawAtLeastTheSimBodyTravelAtAHundredMilliseconds()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            double decay = Knock.DecayPerTick(cfg), hz = cfg.SimHz, t = HitFeelTuning.KnockCheckSeconds;
            foreach (double k in new[] { 0.9, 1.2, 1.6 })
            {
                double sim = k * HitFeelTuning.SimKnockShare(t * hz, decay);          // the sim body at 100 ms
                double lagged = k * HitFeelTuning.SimKnockShare(t * hz - 1, decay);   // the old draw: one tick behind
                double drawn = lagged + (sim - lagged) * HitFeelTuning.KnockAlphaLead(t) + HitFeelTuning.KnockLead(t, k, sim);
                TestContext.Progress.WriteLine($"[px big knock] {k:0.00} u: sim body {sim:0.000}, old draw {lagged / sim:0.00}x of it, now {drawn / sim:0.00}x");
                Assert.GreaterOrEqual(drawn, sim - 1e-9, $"knock {k} u drawn at least the sim body's travel");
            }
            Assert.AreEqual(0.0, HitFeelTuning.KnockAlphaLead(HitFeelTuning.KnockAlphaFade), 1e-9, "plain interpolation after the slide");
            double prev = 1;
            for (double a = HitFeelTuning.KnockAlphaHold; a <= HitFeelTuning.KnockAlphaFade; a += 1 / 60.0)
            {
                double v = HitFeelTuning.KnockAlphaLead(a);
                Assert.LessOrEqual(prev - v, 0.12 + 1e-9, "fades over several frames, no step");
                prev = v;
            }
        }

        // ---------------------------------------------------------------- 1d. burst

        [Test]
        public void OwnHitBurstsAreSizedForTheCamerasAndOtherPeoplesStayQuieter()
        {
            for (byte tier = 0; tier <= 2; tier++)
            {
                float own = HitFeelTuning.BurstSize(tier, true, 10), other = HitFeelTuning.BurstSize(tier, false, 10);
                Assert.Greater(own, other, $"tier {tier}: my hits read above the crowd's");
                Assert.GreaterOrEqual(HitFeelTuning.BurstLife(tier, true), 0.22, "lasts at least 0.22 s");
            }
            Assert.GreaterOrEqual(HitFeelTuning.BurstSize(0, true, 0), 0.9f, "my light hit: at least 0.9 u");
            Assert.GreaterOrEqual(HitFeelTuning.BurstSize(1, true, 20), 1.3f, "my heavy hit: at least 1.3 u");
            Assert.GreaterOrEqual(HitFeelTuning.OwnDustPuffs, 1, "a dust kick at every tier for my hits");
            Assert.AreEqual(1f, HitFeelTuning.ReactionWeightOwn, "the stagger pose at full weight for my hits");
        }

        // ---------------------------------------------------------------- 2. the committed swing

        /// <summary>Every wind-up the classes use (the audit: 7 to 12 ticks) plus whatever the config row says.</summary>
        static int[] Windups(GameConfig cfg)
        {
            var list = new System.Collections.Generic.List<int> { 7, 9, 10, 11, 12 };
            foreach (ClassId c in All)
            {
                int w = AttackTiming.WindupTicks(cfg, c);
                if (w > 0 && !list.Contains(w)) list.Add(w);
            }
            return list.ToArray();
        }

        [Test]
        public void TheStrikePlaysOverFourToSixFramesAndEndsOnTheContactTick()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            double hz = cfg.SimHz;
            foreach (int ticks in Windups(cfg))
            {
                double w = ticks / hz, strike = AttackTiming.StrikeFor(w), frames = strike * HitFeelTuning.RenderHz;
                Assert.That(frames, Is.InRange(4.0 - 1e-9, 6.0 + 1e-9), $"W {ticks} ticks: the strike is {frames:0.0} frames");
                const double c = 0.5, e = 0.2;
                Assert.AreEqual(c, AttackTiming.ClipTime(w, c, 2, w, e), 1e-9, "contact frame on the damage tick");
                Assert.AreEqual(e, AttackTiming.ClipTime(w - strike, c, 2, w, e), 1e-9, "the wind-up extreme held until the strike");
                Assert.Less(AttackTiming.ClipTime(w - strike * 0.5, c, 2, w, e), e + (c - e) * 0.5, "ease-in: fastest on contact");
                Assert.Greater(AttackTiming.ClipTime(w + 0.05, c, 2, w, e), c, "follow-through after contact");
            }
        }

        [Test]
        public void TheSwingPoseCommitsWithinTwoFramesForMeAndThreeForEveryoneElse()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            foreach (int ticks in Windups(cfg))
            {
                double w = ticks / cfg.SimHz;
                float mine = AttackTiming.Envelope(2.0 / 60, 0.01, 1.0, w, true);
                float theirs = AttackTiming.Envelope(3.0 / 60, 0.01, 1.0, w, false);
                Assert.GreaterOrEqual(mine, HitFeelTuning.CommitAtCheck, $"W {ticks}: my pose at 2 frames is {mine:0.00}");
                Assert.GreaterOrEqual(theirs, HitFeelTuning.CommitAtCheck, $"W {ticks}: others' pose at 3 frames is {theirs:0.00}");
                Assert.AreEqual(0f, AttackTiming.Envelope(0, 0, 1.0, w, true), 1e-6f, "from nothing on the press frame (no pop before it)");
            }
        }

        [Test]
        public void AWhiffLooksDifferentAndAFeintAborts()
        {
            Assert.Greater(HitFeelTuning.WhiffRate, 1.0, "a miss swings through faster");
            Assert.Greater(HitFeelTuning.WhiffOverRotateDeg, 10, "and over-rotates visibly");
            Assert.Greater(HitFeelTuning.WhiffShape(0.08), 0.9);
            Assert.AreEqual(0, HitFeelTuning.WhiffShape(HitFeelTuning.WhiffSeconds));
            Assert.That(HitFeelTuning.FeintAbortSeconds, Is.InRange(0.06, 0.2));
        }

        // ---------------------------------------------------------------- 3. the picture matches the rule

        [Test]
        public void TheSlashAndTheWedgeReachExactlyWhereTheSimHits()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            foreach (ClassId c in All)
            {
                double range = LightWeaponView.Range(cfg, c), reach = HitFeelTuning.SimReach(cfg, c);
                Assert.AreEqual(range + HitFeelTuning.BodyWidening, reach, 1e-9, c + ": slash / wedge radius = range + body widening");
                foreach (ClassId t in All)
                    Assert.GreaterOrEqual(reach + 1e-9, range + Math.Min(cfg.Class(t).Radius, HitFeelTuning.BodyWidening), $"{c} on {t}: covers the hit centre distance");
                double half = HitFeelTuning.DrawnHalfArc(cfg, c), arc = LightWeaponView.ArcDeg(cfg, c) * 0.5 * Math.PI / 180;
                Assert.Greater(half, arc, c + ": the drawn arc includes the body widening");
                TestContext.Progress.WriteLine($"[px reach] {c}: range {range:0.00} -> drawn reach {reach:0.00} u, half arc {arc * 180 / Math.PI:0.0} -> {half * 180 / Math.PI:0.0} deg");
            }
        }

        static World NewWorld(ClassId cls) => World.CreateWorld(new WorldOptions
        {
            Seed = 7, TeamSize = 4, HumanTeam = TeamId.Azure, HumanClass = cls, Config = GameConfig.CreateDefault(),
        });

        static int Enemy(World w)
        {
            for (int i = 1; i < w.ActorCount; i++) if (w.Actors[i].Team != w.Actors[0].Team && w.Actors[i].Alive) return i;
            return -1;
        }

        [Test]
        public void TheBodyOnlyStepsAtAnEnemyTheSimWouldHitAndFacesTheLiveSwing()
        {
            World w = NewWorld(ClassId.Warrior);
            w.Tick = 200;
            int e = Enemy(w);
            Assert.GreaterOrEqual(e, 0);
            Actor a = w.Actors[0];
            a.SwingTick = w.Tick - 2;
            a.SwingWindup = 12;
            a.SwingPending = true;
            a.SwingFacing = 0.3;
            a.Facing = 0.2;
            double range = LightWeaponView.Range(w.Config, ClassId.Warrior), r = w.Config.Class(w.Actors[e].Cls).Radius;
            w.X[e] = w.X[0] + range + r - 0.05;
            w.Z[e] = w.Z[0] + 0.3 * 0.1;
            Assert.AreEqual(e, HitFeel.SwingTarget(w, 0), "inside the sim's hit shape: the target");
            w.X[e] = w.X[0] + range + r + 0.1;
            Assert.AreEqual(-1, HitFeel.SwingTarget(w, 0), "just outside the sim reach: no view target (no lie)");
            Assert.AreEqual(0.3, HitFeel.Facing(w, 0, 0), 1e-9, "the body faces the LIVE SwingFacing");
            a.SwingFacing = 0.25;   // lane S1: the swing tracks the aim during the wind-up
            Assert.AreEqual(0.25, HitFeel.Facing(w, 0, 0), 1e-9, "and follows it when it moves");
        }

        // ---------------------------------------------------------------- 4b/c. fight framing and the hatch pull

        [Test]
        public void FightFramingEasesInOverPointFourSecondsAndOutOverOneSecond()
        {
            double f = 0;
            for (int k = 0; k < 24; k++) f = HitFeelTuning.StepFight(f, true, 1.0 / 60);
            Assert.AreEqual(1.0, f, 1e-6, "in by 0.4 s");
            for (int k = 0; k < 30; k++) f = HitFeelTuning.StepFight(f, false, 1.0 / 60);
            Assert.That(f, Is.InRange(0.45, 0.55), "half-way home after 0.5 s");
            for (int k = 0; k < 30; k++) f = HitFeelTuning.StepFight(f, false, 1.0 / 60);
            Assert.AreEqual(0.0, f, 1e-6, "home by 1 s");
            Assert.That(HitFeelTuning.FightDistanceShare, Is.InRange(0.1, 0.15));
            Assert.AreEqual(3.0, HitFeelTuning.FightFovAdd, 1e-9);
        }

        [Test]
        public void TheHatchPullShrinksWhileIAmInAFight()
        {
            var calm = new CameraDirector();
            var fight = new CameraDirector();
            var input = new CameraDirectorInput { Dt = 1.0 / 60, SimTime = 1.0, HumanX = 0, HumanZ = 0, Hatched = true, HatchSimTime = 0, HatchX = 20, HatchZ = 0 };
            calm.Step(input);
            input.InFight = true;
            fight.Step(input);
            Assert.Greater(calm.OffsetX, 1.0, "out of a fight the view leans toward the hatch");
            Assert.LessOrEqual(Math.Abs(fight.OffsetX), Math.Abs(calm.OffsetX) * HitFeelTuning.HatchShareInFight + 1e-9, "in a fight it is only a hint");
        }

        [Test]
        public void AnEnemyCloseInFrontCountsAsAFight()
        {
            World w = NewWorld(ClassId.Warrior);
            int e = Enemy(w);
            w.Actors[0].Facing = 0;
            w.X[e] = w.X[0] + 3;
            w.Z[e] = w.Z[0];
            Assert.IsTrue(FightState.EnemyInFront(w, 0, HitFeelTuning.FightRange, HitFeelTuning.FightConeDeg), "3 u straight ahead");
            w.X[e] = w.X[0] - 3;
            bool behindOnly = true;
            for (int j = 1; j < w.ActorCount; j++)
                if (j != e && w.Actors[j].Team != w.Actors[0].Team) { w.X[j] = w.X[0] - 30; }
            Assert.AreEqual(!behindOnly, FightState.EnemyInFront(w, 0, HitFeelTuning.FightRange, HitFeelTuning.FightConeDeg), "behind me is not 'in front'");
        }

        // ---------------------------------------------------------------- critical hits (lead's call 12)

        [Test]
        public void ACriticalHitReadsClearlyAboveAHeavyHit()
        {
            foreach (var v in Views)
            {
                float by = HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, v.dist, v.fov), cby = HitFeelTuning.KickU(HitFeelTuning.KickKind.CritByMe, v.dist, v.fov);
                float on = HitFeelTuning.KickU(HitFeelTuning.KickKind.OnMe, v.dist, v.fov), con = HitFeelTuning.KickU(HitFeelTuning.KickKind.CritOnMe, v.dist, v.fov);
                Assert.AreEqual(by * HitFeelTuning.CritKickMul, cby, 1e-4f, v.name + ": my critical kicks 1.6x my hit");
                Assert.AreEqual(on * HitFeelTuning.CritKickMul, con, 1e-4f, v.name + ": a critical on me kicks 1.6x a hit on me");
                Assert.LessOrEqual(HitFeelTuning.KickPx(con, v.dist, v.fov), 1080f * 0.04f, v.name + ": still not a yank");
            }
            Assert.That(HitFeelTuning.HoldCritWallSeconds(false), Is.InRange(0.13, 0.15), "130-150 ms hit-stop");
            Assert.AreEqual(0, HitFeelTuning.HoldCritWallSeconds(true), "none under reduce motion");
            Assert.Greater(HitFeelTuning.HoldCritWallSeconds(false), HitFeelTuning.HoldWallSeconds(2, false));
            Assert.Greater(HitFeelTuning.BurstCritStage2, HitFeelTuning.BurstSize(2, true, 60), "a bigger burst than the top plain tier");
            Assert.Greater(HitFeelTuning.BurstCritStage2Delay, 0.0, "two stages");
            Assert.AreEqual(2f, HitFeelTuning.CritFovPunchDeg, 1e-6f);
            Assert.AreEqual(1f, HitFeelTuning.FovPunchEnvelope(HitFeelTuning.KickRise), 1e-4f);
            Assert.AreEqual(0f, HitFeelTuning.FovPunchEnvelope(HitFeelTuning.CritFovPunchSeconds), 1e-6f, "the lens is home again");
            Assert.LessOrEqual(HitFeelTuning.CritMinVisualKnock, HitFeelTuning.MaxDrawnOffset, "the bigger recoil still fits the placement check");
            Assert.Greater(HitFeelTuning.KnockWant(0.07, 0.1, HitFeelTuning.CritMinVisualKnock), HitFeelTuning.KnockWant(0.07, 0.1), "a critical recoils further");
        }

        // ---------------------------------------------------------------- decode: whiff vs contact, knock

        sealed class Sink : IFxSink
        {
            public readonly System.Collections.Generic.List<FxEvent> Got = new System.Collections.Generic.List<FxEvent>();
            public void OnFx(ref FxEvent e, World w) => Got.Add(e);
        }

        /// <summary>Defect 6: a shield-cut hit decodes as a Block (block look, no flash), in either event order.</summary>
        [Test]
        public void AShieldCutHitDecodesAsABlockAndADigAsADig()
        {
            foreach (bool blockedFirst in new[] { true, false })
            {
                World w = NewWorld(ClassId.Warrior);
                w.Tick = 60;
                var feed = new CombatFeed(World.ActorCapacity(w.Config));
                var sink = new Sink();
                feed.OnTick(w, sink);
                sink.Got.Clear();
                int e = Enemy(w);
                if (blockedFirst) World.PushEvent(w, SimEventType.HitBlocked, 0, e, w.X[0], w.Z[0], PlayerReasons.BlockedShield);
                World.PushEvent(w, SimEventType.Hit, 0, e, w.X[0], w.Z[0], 3);
                if (!blockedFirst) World.PushEvent(w, SimEventType.HitBlocked, 0, e, w.X[0], w.Z[0], PlayerReasons.BlockedShield);
                feed.OnTick(w, sink);
                Assert.IsTrue(sink.Got.Exists(x => x.Kind == FxKind.Block && x.A == 0), $"blocked first {blockedFirst}: the hit reads as a block");
                Assert.IsFalse(sink.Got.Exists(x => x.Kind == FxKind.Hit && x.A == 0), "and not as a hit");
                Assert.AreEqual(60, feed.BlockTick[0], "no flash for it");
            }
            {
                World w = NewWorld(ClassId.Miner);
                w.Tick = 70;
                var feed = new CombatFeed(World.ActorCapacity(w.Config));
                var sink = new Sink();
                feed.OnTick(w, sink);
                World.PushEvent(w, SimEventType.Terraform, 0, 5, w.X[0] + 1, w.Z[0], 1);    // Terraform.Dug
                World.PushEvent(w, SimEventType.Terraform, 0, -1, w.X[0] + 1, w.Z[0], -2);  // refused: lane D words it
                feed.OnTick(w, sink);
                Assert.AreEqual(1, sink.Got.FindAll(x => x.Kind == FxKind.Dig).Count, "the dig hits the cell once; a refusal draws nothing");
            }
            foreach (FxKind k in new[] { FxKind.Critical, FxKind.Dig, FxKind.Blocked, FxKind.StructureHit, FxKind.Contact })
                Assert.IsTrue(FeelGate.TryGating(k, out _), "gating entry for " + k);
        }

        /// <summary>Defect 12: first person is hidden (the owner's ruling): the setting refuses it while the switch is off.</summary>
        [Test]
        public void TheSettingRefusesFirstPersonWhileItIsHidden()
        {
            bool sw = FollowCamera.FirstPersonEnabled;
            bool hadView = UnityEngine.PlayerPrefs.HasKey(GameSettings.CameraViewKey);
            int view = UnityEngine.PlayerPrefs.GetInt(GameSettings.CameraViewKey, 1);
            bool ground = GameSettings.GroundCamera;
            try
            {
                FollowCamera.FirstPersonEnabled = false;
                GameSettings.CameraView = CameraView.FirstPerson;
                Assert.AreEqual(CameraView.ThirdPerson, GameSettings.CameraView);
                Assert.AreEqual((int)CameraView.ThirdPerson, UnityEngine.PlayerPrefs.GetInt(GameSettings.CameraViewKey, -1), "stored as third person");
            }
            finally
            {
                FollowCamera.FirstPersonEnabled = sw;
                if (hadView) UnityEngine.PlayerPrefs.SetInt(GameSettings.CameraViewKey, view); else UnityEngine.PlayerPrefs.DeleteKey(GameSettings.CameraViewKey);
                GameSettings.GroundCamera = ground;
            }
        }

        [Test]
        public void AWallSwingIsAContactNotAWhiffAndTheKnockIsRecorded()
        {
            World w = NewWorld(ClassId.Warrior);
            w.Tick = 50;
            var feed = new CombatFeed(World.ActorCapacity(w.Config));
            var sink = new Sink();
            feed.OnTick(w, sink);
            sink.Got.Clear();
            // a whiff: AttackContact with nothing hit
            World.PushEvent(w, SimEventType.AttackContact, 0, -1, w.X[0] + 1, w.Z[0], 0);
            feed.OnTick(w, sink);
            FxEvent c = sink.Got.Find(x => x.Kind == FxKind.Contact);
            Assert.AreEqual(FxKind.Contact, c.Kind);
            Assert.AreEqual(0, c.Value, "nothing hit: a whiff");
            Assert.AreEqual(0, feed.ContactHits[0]);
            // a wall swing: AttackContact 0 bodies, then (same tick, later in the ring) a StructureHit by the same attacker
            w.Tick = 51;
            sink.Got.Clear();
            World.PushEvent(w, SimEventType.AttackContact, 0, -1, w.X[0] + 1, w.Z[0], 0);
            World.PushEvent(w, SimEventType.StructureHit, 0, PlayerReasons.StructureBarricade, w.X[0] + 1.5, w.Z[0], 24, null, DamageCause.None, 0.6);
            feed.OnTick(w, sink);
            c = sink.Got.Find(x => x.Kind == FxKind.Contact);
            Assert.AreEqual(1, c.Value, "a structure hit the same tick makes it a contact");
            Assert.IsTrue(feed.StructureEventsSeen);
            FxEvent sh = sink.Got.Find(x => x.Kind == FxKind.StructureHit);
            Assert.AreEqual(0.6, sh.Extra, 1e-9, "health share left is carried");
            // a knock: recorded for the drawn lead
            int e = Enemy(w);
            w.Tick = 52;
            Knock.Push(w, e, 0, 1, 0, 0.56, 0, 0, DamageCause.Melee);
            feed.OnTick(w, sink);
            Assert.AreEqual(52, feed.KnockTick[e]);
            Assert.AreEqual(0.56, feed.KnockU[e], 1e-9);
        }
    }
}

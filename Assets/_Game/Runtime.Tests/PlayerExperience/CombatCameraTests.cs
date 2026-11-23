using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// fix/px-combat-camera (2026-11-23): the third-person combat framing's design numbers (CombatFrame.Model, the analytic
    /// GroundFollow pose) and its behaviour (CombatFramer: ease in / hold / out, side hysteresis, walls, the steering cap,
    /// strength, the switch). Pure; the play-mode D2 / D2b seat tests measure the live camera.
    /// </summary>
    public sealed class CombatCameraTests
    {
        static readonly ClassId[] Six = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };
        /// <summary>Centre-to-centre distances (u): 0.8 x and 1.0 x every class's melee reach in both rule sets (1.4 to 2.3).</summary>
        static readonly double[] Distances = { 1.12, 1.2, 1.28, 1.36, 1.4, 1.44, 1.5, 1.6, 1.7, 1.8, 1.84, 2.0, 2.3 };

        [Test]
        public void TheFramingShowsTheEnemyBesideMeBiggerAndKeepsMeOnScreenForEveryClass()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            FightFrame.Silhouette(ClassId.Farmer, cfg.Class(ClassId.Farmer).Radius, out double ew, out double eh);
            foreach (int fov in new[] { FollowCamera.FovMin, FollowCamera.FovDefault, FollowCamera.FovMax })
            {
                double minShare = 9, minGain = 9, worstOld = 9;
                foreach (ClassId c in Six)
                {
                    FightFrame.Silhouette(c, cfg.Class(c).Radius, out double mw, out double mh);
                    foreach (double d in Distances)
                    {
                        CombatFrame.Model(mw, mh, ew, eh, d, fov, 0, out CombatFrame.Box me0, out CombatFrame.Box en0);
                        CombatFrame.Model(mw, mh, ew, eh, d, fov, 1, out CombatFrame.Box me, out CombatFrame.Box en);
                        double share = CombatFrame.OutsideShare(me, en), gain = en.Height / en0.Height;
                        minShare = Math.Min(minShare, share);
                        minGain = Math.Min(minGain, gain);
                        worstOld = Math.Min(worstOld, CombatFrame.OutsideShare(me0, en0));
                        Assert.GreaterOrEqual(share, 0.6, $"{c} at {d} u, FOV {fov}: at least 60% of the enemy's box clear of mine");
                        Assert.IsTrue(me.OnScreen, $"{c} at {d} u, FOV {fov}: my own fighter wholly on screen ({me.MinX:0.00}..{me.MaxX:0.00}, {me.MinY:0.00}..{me.MaxY:0.00})");
                        Assert.IsTrue(en.OnScreen, $"{c} at {d} u, FOV {fov}: the enemy wholly on screen");
                        // a narrower lens than the default pulls the framing back to keep me whole (FovFit): never smaller than unframed
                        Assert.GreaterOrEqual(gain, fov >= FollowCamera.FovDefault ? 1.4 : 1.0, $"{c} at {d} u, FOV {fov}: the enemy drawn bigger");
                    }
                }
                TestContext.Progress.WriteLine($"[combat camera] FOV {fov}: enemy box clear share >= {minShare:0.00} (unframed worst {worstOld:0.00}), enemy height x{minGain:0.00} or more");
            }
        }

        [Test]
        public void TheDesignIsCloserLowerAndEasesInsideTheBrief()
        {
            Assert.Less(CombatFrame.Distance, FollowCamera.ThirdDistance, "closer");
            Assert.Less(CombatFrame.PitchDeg, FollowCamera.ThirdPitch, "a lower pitch");
            Assert.Greater(CombatFrame.PitchDeg, FollowCamera.ThirdPitch - 10, "only a little lower");
            Assert.That(CombatFrame.InSeconds, Is.InRange(0.25, 0.35), "eases in over 0.25-0.35 s");
            Assert.That(CombatFrame.HoldSeconds + CombatFrame.OutSeconds, Is.InRange(1.0, 2.0), "home about a second after the fight");
            Assert.AreEqual(1.0, CombatFrame.FovFit(FollowCamera.FovDefault), 1e-12);
            Assert.AreEqual(1.0, CombatFrame.FovFit(FollowCamera.FovMax), 1e-12);
            Assert.Greater(CombatFrame.FovFit(FollowCamera.FovMin), 1.0);
        }

        static CombatFrameInput In(bool engaged, double own, double line, double dt = 1.0 / 60) => new CombatFrameInput
        {
            Dt = dt, Enabled = true, Strength = 1, Engaged = engaged, TargetAlive = true, OwnYaw = own, LineYaw = line,
            ClearPlus = 1, ClearMinus = 1,
        };

        static void Run(CombatFramer f, CombatFrameInput i, double seconds)
        {
            int n = (int)Math.Round(seconds * 60);
            for (int k = 0; k < n; k++) f.Step(i);
        }

        [Test]
        public void ItEasesInHoldsThenEasesOutWithoutASnap()
        {
            var f = new CombatFramer();
            double last = 0, maxStep = 0;
            for (int k = 0; k < 18; k++)
            {
                f.Step(In(true, 100, 100));
                maxStep = Math.Max(maxStep, Math.Abs(f.YawOffset - last));
                last = f.YawOffset;
            }
            Assert.AreEqual(1.0, f.Weight, 1e-9, "full after 0.3 s");
            Run(f, In(true, 100, 100), 0.5);
            Assert.AreEqual(CombatFrame.YawDeg, f.YawOffset, 0.5, "the full orbit on the nearer side");
            Assert.LessOrEqual(maxStep, CombatFrame.MaxYawRate / 60 + 1e-9, "never more than the rate limit in one frame");
            Run(f, In(false, 100, 100), CombatFrame.HoldSeconds - 0.05);
            Assert.AreEqual(1.0, f.Weight, 1e-9, "held after the fight ends");
            Run(f, In(false, 100, 100), 0.05 + CombatFrame.OutSeconds + 0.05);
            Assert.AreEqual(0.0, f.Weight, 1e-9, "home after hold + out");
            Assert.AreEqual(0.0, f.YawOffset, 1e-9);
            Assert.AreEqual(0.0, f.Shoulder, 1e-9);
        }

        [Test]
        public void ADownedTargetIsLetGoAtOnce()
        {
            var f = new CombatFramer();
            Run(f, In(true, 100, 100), 0.6);
            CombatFrameInput gone = In(false, 100, 100);
            gone.TargetAlive = false;
            Run(f, gone, CombatFrame.OutSeconds + 0.05);
            Assert.AreEqual(0.0, f.Weight, 1e-9, "no hold for a body that went down");
        }

        [Test]
        public void TheSideIsTheOneThatAlreadyShowsMoreAndNeverFlipFlops()
        {
            var f = new CombatFramer();
            Run(f, In(true, 110, 100), 0.5);
            Assert.AreEqual(1, f.Turn, "my view already right of the line: turn right");
            Assert.Less(f.Shoulder, 0, "the lens shifts to the camera's side");
            Run(f, In(true, 100 - CombatFrame.SideHysteresisDeg + 5, 100), 3);
            Assert.AreEqual(1, f.Turn, "inside the hysteresis band: kept");
            Run(f, In(true, 100 - CombatFrame.SideHysteresisDeg - 10, 100), CombatFrame.SideHoldSeconds - 0.1);
            Assert.AreEqual(1, f.Turn, "not before the hold");
            Run(f, In(true, 100 - CombatFrame.SideHysteresisDeg - 10, 100), 0.2);
            Assert.AreEqual(-1, f.Turn, "the player's view moved well to the other side: follow it");
            var g = new CombatFramer();
            Run(g, In(true, 90, 100), 0.1);
            Assert.AreEqual(-1, g.Turn, "the nearer side, the other way");
        }

        [Test]
        public void AWallOnTheChosenSideMovesItToTheOtherThenToNoOrbit()
        {
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 110, 100);
            i.ClearPlus = 0.3;
            Run(f, i, 0.6);
            Assert.AreEqual(-1, f.Turn, "a wall on the nearer side: start on the other");
            i.ClearMinus = 0.2;
            Run(f, i, 1.0);
            Assert.AreEqual(0.0, f.WallKeep, 1e-9, "both sides blocked: no orbit");
            Assert.AreEqual(0.0, f.Shoulder, 0.01, "and no shoulder");
            Assert.That(Math.Abs(GroundCamera.DeltaAngle(110 + f.YawOffset, 100)), Is.LessThan(1.0), "the view eases onto the line itself");
            Assert.Less(f.PitchOffset, 0, "the closer, lower framing stays");
            i.ClearPlus = 1;
            Run(f, i, 1.0);
            Assert.AreEqual(1, f.Turn, "a side clears: framing moves there");
            Assert.AreEqual(1.0, f.WallKeep, 1e-9);
        }

        [Test]
        public void WhileThePlayerSteersTheFramingOnlyAddsABoundedOffset()
        {
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 100, 100);
            Run(f, i, 0.6);
            i.Steering = true;
            Run(f, i, 0.6);
            Assert.LessOrEqual(Math.Abs(f.YawOffset), CombatFrame.SteerCapDeg + 1e-9, "his input wins: at most the steering cap");
            // he swings his own view around: the drawn view follows his yaw exactly, plus the (constant, capped) offset
            double before = 100 + f.YawOffset;
            i.OwnYaw = 160;
            f.Step(i);
            Assert.AreEqual(60, GroundCamera.DeltaAngle(before, 160 + f.YawOffset), CombatFrame.SteerCapDeg * 2 + 1e-9, "the view moves with his input");
            Assert.LessOrEqual(Math.Abs(f.YawOffset), CombatFrame.SteerCapDeg + 1e-9);
        }

        [Test]
        public void StrengthScalesItAndTheSwitchTurnsItOff()
        {
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 100, 100);
            i.Strength = 0.5;
            Run(f, i, 1.0);
            Assert.AreEqual(0.5, f.K, 1e-9);
            Assert.AreEqual(CombatFrame.YawDeg * 0.5, f.YawOffset, 0.5);
            Assert.AreEqual((CombatFrame.PitchDeg - FollowCamera.ThirdPitch) * 0.5, f.PitchOffset, 1e-9);
            var g = new CombatFramer();
            CombatFrameInput off = In(true, 100, 100);
            off.Enabled = false;
            Run(g, off, 1);
            Assert.AreEqual(0.0, g.K, 1e-12, "COMBAT CAMERA off: nothing");
            Assert.AreEqual(0.0, g.YawOffset, 1e-12);
            Assert.AreEqual(0.0, g.Lift, 1e-12);
            Assert.IsFalse(g.Active);
        }

        [Test]
        public void ASnapCutsStraightToTheGoal()
        {
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 100, 100);
            i.Snap = true;
            f.Step(i);
            Assert.AreEqual(1.0, f.K, 1e-9);
            Assert.AreEqual(CombatFrame.YawDeg, f.YawOffset, 1e-9);
            i.Engaged = false;
            i.TargetAlive = false;
            f.Step(i);
            Assert.AreEqual(0.0, f.K, 1e-9);
        }
    }
}

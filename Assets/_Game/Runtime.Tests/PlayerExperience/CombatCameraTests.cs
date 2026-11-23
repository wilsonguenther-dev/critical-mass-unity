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
            Assert.Greater(f.PitchOffset, 0, "pass 3: the closer-and-higher fallback (no swing, a higher pitch)");
            Assert.Less(f.Reach, 1.0, "... and nearer");
            i.ClearPlus = 1;
            Run(f, i, 1.0);
            Assert.AreEqual(1, f.Turn, "a side clears: framing moves there");
            Assert.AreEqual(1.0, f.WallKeep, 1e-9);
        }

        [Test]
        public void HisLookTurnsTheViewOneToOneAndTheFramingNeverEatsIt()
        {
            // pass-2 review: an 80 deg mouse turn moved the view ~5 deg. Now the offset is held while he looks.
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 100, 100);
            Run(f, i, 1.0);
            Assert.AreEqual(CombatFrame.YawDeg, f.YawOffset, 0.5, "framed");
            i.Looking = true;
            double own = 100, view = own + f.YawOffset;
            for (int k = 0; k < 30; k++)   // 1.25 deg a frame (75 deg/s) for 0.5 s: +37.5 deg, still inside the release
            {
                own += 1.25;
                i.OwnYaw = own;
                f.Step(i);
                own += f.TakeFold();   // GroundView folds what the framing gives up into his own yaw
                i.OwnYaw = own;
                double v = own + f.YawOffset;
                Assert.AreEqual(1.25, GroundCamera.DeltaAngle(view, v), 1e-9, "frame " + k + ": the view turns exactly as far as he does");
                view = v;
            }
            Assert.AreEqual(1.0, f.K, 1e-9, "inside ReleaseDeg the framing stays");
        }

        [Test]
        public void TurnedAwayFromTheEnemyTheFramingLetsGoAndComesBackOnlyWhenHeFacesHim()
        {
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 100, 100);
            Run(f, i, 0.6);
            i.Looking = true;
            i.OwnYaw = 100 + 80;   // his 80 deg turn
            f.Step(i);
            Assert.IsTrue(f.TurnedAway, "more than ReleaseDeg off the line: let go");
            Run(f, i, CombatFrame.OutSeconds + 0.05);
            Assert.AreEqual(0.0, f.K, 1e-9, "eased out with no hold");
            Assert.AreEqual(0.0, f.YawOffset, 1e-9, "the view is his own yaw: the screen shows where he swings");
            i.Looking = false;
            i.OwnYaw = 100 + (CombatFrame.ReleaseDeg + CombatFrame.ReacquireDeg) / 2;
            Run(f, i, 0.5);
            Assert.AreEqual(0.0, f.K, 1e-9, "between the two angles: still let go (no flicker at the edge)");
            i.OwnYaw = 100 + CombatFrame.ReacquireDeg - 5;
            Run(f, i, 0.6);
            Assert.IsFalse(f.TurnedAway);
            Assert.AreEqual(1.0, f.K, 1e-9, "faces the fight again: framed again");
            var g = new CombatFramer();
            Run(g, In(true, 100 + CombatFrame.ReleaseDeg + 10, 100), 0.6);
            Assert.AreEqual(0.0, g.K, 1e-9, "an enemy far off his own yaw never pulls the view round");
        }

        // ---------------------------------------------------------------- scenery (pass-2: the camera parked in a tree)

        const double MeX = 0, MeZ = 0, EnX = 1.12, EnZ = 0, Chest = CombatFrame.ChestHeight;

        static double Clear(int turn, SeeThroughCylinder[] cyl, int n, out double lx, out double ly, out double lz)
        {
            double line = GroundCamera.YawToward(MeX, MeZ, EnX, EnZ, 0);
            double fx = MeX + CombatFrame.FocusShare * (EnX - MeX), fz = MeZ + CombatFrame.FocusShare * (EnZ - MeZ);
            return CombatFrame.SideClear(null, fx, CombatFrame.FocusHeight, fz, line, turn, 1, 1, MeX, Chest, MeZ, EnX, Chest, EnZ, cyl, n, out lx, out ly, out lz);
        }

        static SeeThroughCylinder CrownAt(double x, double z) => new SeeThroughCylinder { X = x, Z = z, R = 1.0, Y0 = 0.8, Y1 = 3.4 };

        [Test]
        public void ATreeOnEitherSideBlocksThatSideAndTheFramingTakesTheOther()
        {
            Assert.AreEqual(1.0, Clear(1, null, 0, out double px, out _, out double pz), 1e-9, "open ground: the + side fits");
            Assert.AreEqual(1.0, Clear(-1, null, 0, out double mx, out _, out double mz), 1e-9, "open ground: the - side fits");
            var plusTree = new[] { CrownAt(px, pz) };
            var minusTree = new[] { CrownAt(mx, mz) };
            Assert.Less(Clear(1, plusTree, 1, out _, out _, out _), CombatFrame.WallShare, "a tree crown at the + lens blocks +");
            Assert.AreEqual(1.0, Clear(-1, plusTree, 1, out _, out _, out _), 1e-9, "... and leaves - clear");
            Assert.Less(Clear(-1, minusTree, 1, out _, out _, out _), CombatFrame.WallShare, "a tree crown at the - lens blocks -");
            Assert.AreEqual(1.0, Clear(1, minusTree, 1, out _, out _, out _), 1e-9, "... and leaves + clear");

            // the framer, fed those numbers, never parks on the tree's side
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 110, 100);   // his view already right of the line: + would be the pick
            i.ClearPlus = Clear(1, plusTree, 1, out _, out _, out _);
            i.ClearMinus = Clear(-1, plusTree, 1, out _, out _, out _);
            Run(f, i, 0.6);
            Assert.AreEqual(-1, f.Turn, "tree on +: the other side");
            var g = new CombatFramer();
            CombatFrameInput j = In(true, 90, 100);    // - would be the pick
            j.ClearPlus = Clear(1, minusTree, 1, out _, out _, out _);
            j.ClearMinus = Clear(-1, minusTree, 1, out _, out _, out _);
            Run(g, j, 0.6);
            Assert.AreEqual(1, g.Turn, "tree on -: the other side");

            // trees on both sides: no orbit (the no-orbit pose is clear here)
            var both = new[] { CrownAt(px, pz), CrownAt(mx, mz) };
            var h = new CombatFramer();
            CombatFrameInput b = In(true, 110, 100);
            b.ClearPlus = Clear(1, both, 2, out _, out _, out _);
            b.ClearMinus = Clear(-1, both, 2, out _, out _, out _);
            double none = Clear(0, both, 2, out _, out _, out _);
            b.NoClearFrame = b.ClearPlus < CombatFrame.WallShare && b.ClearMinus < CombatFrame.WallShare && none < CombatFrame.WallShare;
            Run(h, b, 1.0);
            Assert.AreEqual(0.0, h.WallKeep, 1e-9, "both sides blocked: no swing");
            Assert.IsFalse(b.NoClearFrame, "the no-orbit pose itself is clear");
            Assert.That(Math.Abs(GroundCamera.DeltaAngle(110 + h.YawOffset, 100)), Is.LessThan(1.0));
        }

        [Test]
        public void ACrownBetweenTheLensAndEitherFightersChestBlocksThatFraming()
        {
            Clear(1, null, 0, out double lx, out double ly, out double lz);
            foreach (bool enemy in new[] { true, false })
            {
                double tx = enemy ? EnX : MeX, tz = enemy ? EnZ : MeZ;
                // a thin crown 80% of the way from the lens to that chest, off the camera's own pull-back path
                var c = new[] { new SeeThroughCylinder { X = lx + (tx - lx) * 0.8, Z = lz + (tz - lz) * 0.8, R = 0.12, Y0 = 0.9, Y1 = 3.0 } };
                Assert.IsFalse(GroundCollision.Inside(null, lx, ly, lz, GroundCollision.Radius, c, 1), "the lens itself is clear");
                Assert.IsTrue(GroundCollision.Occluded(null, lx, ly, lz, tx, Chest, tz, CombatFrame.SightStopU, c, 1), "the sight line meets it");
                Assert.AreEqual(0.0, Clear(1, c, 1, out _, out _, out _), 1e-9, (enemy ? "the enemy's" : "my") + " chest hidden: that framing is blocked");
            }
            Assert.IsFalse(GroundCollision.Occluded(null, lx, ly, lz, EnX, Chest, EnZ, CombatFrame.SightStopU, null, 0), "open ground: seen");
        }

        [Test]
        public void NoClearFramingAtAllLetsGo()
        {
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 100, 100);
            Run(f, i, 0.6);
            i.ClearPlus = i.ClearMinus = 0;
            i.NoClearFrame = true;
            Run(f, i, CombatFrame.OcclusionGraceSeconds - 0.1);
            Assert.AreEqual(1.0, f.K, 1e-9, "pass 3: brief occlusion of every pose is tolerated");
            Run(f, i, 0.1 + CombatFrame.OutSeconds + 0.05);
            Assert.AreEqual(0.0, f.K, 1e-9, "scenery hides a fighter from every framing for longer: back to the normal camera");
        }

        // ---------------------------------------------------------------- tags (pass-2: name and bar under the banner)

        [Test]
        public void FramedTagsSitJustAboveTheHeadAndBelowTheTopHudRows()
        {
            const double H = 1080;
            double gap = CombatFrame.TagGapShare * H, top = (1 - CombatFrame.TagTopShare) * H;
            Assert.AreEqual(700 + gap, CombatFrame.TagScreenY(900, 700, H), 1e-9, "the close camera's tall gap shrinks to just above the head");
            Assert.AreEqual(top, CombatFrame.TagScreenY(1000, 850, H), 1e-9, "never into the top HUD rows");
            Assert.AreEqual(520, CombatFrame.TagScreenY(520, 510, H), 1e-9, "a tag already close to the head stays where it is");
            Assert.Less(top, H * 0.8, "the clamp clears the resource row (about 22% from the top in the pass-2 strips)");
        }

        // ---------------------------------------------------------------- pass 3

        [Test]
        public void OnceFramedItIsStickyWhileAutoFollowDriftsAndTheLadderTriesSmallerSwings()
        {
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 100, 100);
            Run(f, i, 0.6);
            i.OwnYaw = 100 + 70;   // auto follow behind a fighter running round the enemy: not his look
            Run(f, i, 2.0);
            Assert.IsFalse(f.TurnedAway, "only his own look lets go by turning");
            Assert.AreEqual(1.0, f.K, 1e-9, "still framed");
            // the full swing on this side is blocked, the 24 deg one is clear: keep the side at half the swing
            var g = new CombatFramer();
            CombatFrameInput j = In(true, 110, 100);
            j.ClearPlus = 0.3; j.ClearPlusHalf = 1; j.ClearMinus = 0.2; j.ClearMinusHalf = 0.2;
            Run(g, j, 1.0);
            Assert.AreEqual(1, g.Turn, "the side with the bigger clear swing");
            Assert.AreEqual(0.5, g.WallKeep, 1e-9, "24 deg, not nothing");
            Assert.AreEqual(CombatFrame.PosePitch(0.5) - FollowCamera.ThirdPitch, g.PitchOffset, 0.05);
            j.ClearPlusHalf = 0.2; j.ClearPlusQuarter = 1;
            Run(g, j, 1.0);
            Assert.AreEqual(0.25, g.WallKeep, 1e-9, "then 12 deg");
            Assert.AreEqual(0.5, CombatFrame.BestSwing(0.1, 0.9, 0.9), 1e-12);
            Assert.AreEqual(0.0, CombatFrame.BestSwing(0.1, 0.1, 0.1), 1e-12);
        }

        [Test]
        public void ReFramingAfterHisLookRotatesTheViewAtMostTenDegreesAndSlowly()
        {
            // he looked away and back (manual look): the re-framing may shift the lens but turns the view <= 10 deg
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 100, 100);
            i.Manual = true;
            double own = 100, view0 = own, maxStep = 0, last = own;
            for (int k = 0; k < 120; k++)
            {
                i.OwnYaw = own;
                f.Step(i);
                own += f.TakeFold();
                double v = own + f.YawOffset;
                maxStep = Math.Max(maxStep, Math.Abs(GroundCamera.DeltaAngle(last, v)));
                last = v;
            }
            Assert.LessOrEqual(Math.Abs(GroundCamera.DeltaAngle(view0, last)), CombatFrame.RespectYawDeg + 1e-6, "at most 10 deg on its own");
            Assert.LessOrEqual(maxStep, CombatFrame.RespectYawRate / 60 + 1e-6, "and slowly");
            Assert.Greater(Math.Abs(f.Shoulder), CombatFrame.ShoulderU, "it frames with a wider lens shift instead");
        }

        [Test]
        public void WhenTheFramingLetsGoTheViewNeverUnwindsOnItsOwn()
        {
            // pass 3: a 57 deg drag netted +10 because the 48 deg offset unwound. Now the drawn view stays where he left it.
            var f = new CombatFramer();
            CombatFrameInput i = In(true, 100, 100);
            Run(f, i, 1.0);
            double own = 100 + f.TakeFold();
            i.Looking = true;
            for (int k = 0; k < 46; k++) { own += 1.25; i.OwnYaw = own; f.Step(i); own += f.TakeFold(); }   // +57 deg
            double view = own + f.YawOffset;
            i.Looking = false;
            for (int k = 0; k < 120; k++)
            {
                i.OwnYaw = own;
                f.Step(i);
                own += f.TakeFold();
                Assert.AreEqual(0, GroundCamera.DeltaAngle(view, own + f.YawOffset), CombatFrame.RespectYawDeg + 1e-6, "frame " + k + ": no swing back");
            }
        }

        [Test]
        public void TheFollowPullsInAtOnceAndLetsOutOnlyAfterTheWayStaysClear()
        {
            var g = new GroundFollow();
            var tree = new[] { new SeeThroughCylinder { X = 0, Z = 2.5, R = 1.0, Y0 = 0, Y1 = 4 } };
            GroundFollowInput Step(bool blocked) => new GroundFollowInput
            {
                Dt = 1.0 / 60, TargetX = 0, TargetZ = 0, GroundY = 0, Rig = FollowCamera.ThirdRig, SlowOut = true,
                Cylinders = blocked ? tree : null, CylinderCount = blocked ? 1 : 0,
            };
            g.SetYaw(0);   // looking north (sim -z): the lens stands south of the pivot, toward +z
            GroundFollowInput a = Step(false); a.Snap = true; g.Step(a);
            double open = g.Dist;
            g.Step(Step(true));
            Assert.Less(g.Dist, open - 0.5, "a crown behind the lens: in at once");
            double inside = g.Dist;
            for (int k = 0; k < 20; k++) g.Step(k % 10 < 5 ? Step(false) : Step(true));   // flickering clear / blocked
            Assert.AreEqual(inside, g.Dist, 1e-9, "no pumping while the way flickers");
            for (int k = 0; k < 20; k++) g.Step(Step(false));   // clear for 0.33 s
            Assert.AreEqual(inside, g.Dist, 1e-9, "not before ClearHoldSeconds");
            for (int k = 0; k < 10; k++) g.Step(Step(false));
            Assert.Less(g.Dist, open - 0.3, "then slowly, not in a snap");
            for (int k = 0; k < 90; k++) g.Step(Step(false));
            Assert.AreEqual(open, g.Dist, 0.1, "home in about a second");
        }

        [Test]
        public void AScreenFillingCrownBetweenTheLensAndThePlayerPullsTheLensPastIt()
        {
            // pivot at (0, 2.2, 0); lens 4 u back toward +z; a crown 1 u in front of the lens on the sight line
            var crown = new[] { new SeeThroughCylinder { X = 0, Z = 2.6, R = 0.9, Y0 = 1.0, Y1 = 3.6 } };
            double dy = Math.Sin(14 * Math.PI / 180), dz = Math.Cos(14 * Math.PI / 180);
            double t = GroundCollision.SightLimit(0, 2.2, 0, 0, dy, dz, 4.0, 0, 1.2, 0, 65, 16.0 / 9, crown, 1, out bool blocked, out _);
            Assert.IsFalse(blocked);
            Assert.Less(t, 4.0, "pulled in");
            Assert.IsFalse(GroundCollision.Crosses(0, 2.2 + dy * t, dz * t, 0, 1.2, 0, CombatFrame.SightStopU, crown[0]), "the crown is behind the lens now");
            Assert.IsTrue(GroundCollision.Crosses(0, 2.2 + dy * 4, dz * 4, 0, 1.2, 0, CombatFrame.SightStopU, crown[0]), "it stood on the sight line");
            Assert.Greater(GroundCollision.OccluderShare(0, 2.2 + dy * 4, dz * 4, crown[0], 65, 16.0 / 9), GroundFollow.MaxOccluderShare, "and would have filled the screen");
            Assert.AreEqual(1.0, GroundCollision.OccluderShare(0, 2.0, 2.6, crown[0], 65, 16.0 / 9), 1e-9, "inside it: all of it");
        }

        [Test]
        public void MyTagIsPushedClearOfAnotherTag()
        {
            const float W = 1920, H = 1080, sw = GroundView.TagSepW * W, sh = GroundView.TagSepH * H, ceil = 810;
            Assert.AreEqual(600f + sh, GroundView.SeparateY(960, 600, 970, 600, sw, sh, ceil), 1e-3f, "on top of his: pushed above it");
            Assert.AreEqual(790f - sh, GroundView.SeparateY(960, 790, 960, 790, sw, sh, ceil), 1e-3f, "no room under the HUD rows: below it");
            Assert.AreEqual(600f, GroundView.SeparateY(960, 600, 960 + sw + 1, 600, sw, sh, ceil), 1e-3f, "apart already: unchanged");
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

    /// <summary>fix/px-combat-camera-2 (coordinator): the animation throttle's focus in the low views is the followed body,
    /// not the camera's ray on y = 0 (at the framed 8 deg pitch that lands 10 to 20 u ahead and throttled my own fighter).</summary>
    public class CombatCameraAnimLodTests
    {
        [Test]
        public void TheLowViewThrottleCentresOnTheFollowedFighterNotTheGroundHit()
        {
            var cam = new UnityEngine.Vector3(0f, 3.75f, -3.2f);   // framed lens over ground at y = 2 (a raised island)
            float p = 8f * UnityEngine.Mathf.Deg2Rad;
            var fwd = new UnityEngine.Vector3(0f, -UnityEngine.Mathf.Sin(p), UnityEngine.Mathf.Cos(p));
            var me = new UnityEngine.Vector3(0f, 0f, 0f);
            UnityEngine.Vector3 old = CriticalMass.Runtime.View.Anim.AnimLod.FocusPoint(cam, fwd, false, me);
            float oldD = UnityEngine.Mathf.Sqrt((old.x - me.x) * (old.x - me.x) + (old.z - me.z) * (old.z - me.z));
            Assert.Greater(oldD, CriticalMass.Runtime.View.Anim.AnimLod.NearRadius, "the old ground-hit rule put my own fighter outside the every-frame radius");
            UnityEngine.Vector3 now = CriticalMass.Runtime.View.Anim.AnimLod.FocusPoint(cam, fwd, true, me);
            Assert.AreEqual(0f, now.x, 1e-6f);
            Assert.AreEqual(0f, now.z, 1e-6f, "low view: the followed fighter's look point");
            UnityEngine.Vector3 td = CriticalMass.Runtime.View.Anim.AnimLod.FocusPoint(new UnityEngine.Vector3(0f, 20f, -10f), new UnityEngine.Vector3(0f, -0.8f, 0.6f), false, me);
            Assert.AreEqual(5f, td.z, 1e-4f, "top-down keeps the ground-hit rule");
            Assert.IsFalse(CriticalMass.Runtime.View.Anim.AnimLod.Pinned(-1), "no id is never pinned");
        }
    }
}

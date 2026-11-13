using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>DG-20: one test per camera rule (combat framing, hatch focus, hold-to-look, see-through probe).</summary>
    [TestFixture]
    public sealed class CameraPolishTests
    {
        const double Dt = 1.0 / 60;

        static CameraDirectorInput Frame(int enemies = 0) => new CameraDirectorInput { Dt = Dt, EnemiesNear = enemies };

        static void Run(CameraDirector d, CameraDirectorInput f, double seconds)
        {
            for (double t = 0; t < seconds; t += Dt) d.Step(f);
        }

        // ---- combat framing

        [Test]
        public void ThreeEnemiesWithinEightUnitsZoomOutTwelvePercent()
        {
            var d = new CameraDirector();
            Run(d, Frame(3), 3);
            Assert.IsTrue(d.CombatFraming);
            Assert.AreEqual(1.12, d.Zoom, 1e-3);
        }

        [Test]
        public void TwoEnemiesDoNotZoom()
        {
            var d = new CameraDirector();
            Run(d, Frame(2), 3);
            Assert.IsFalse(d.CombatFraming);
            Assert.AreEqual(1.0, d.Zoom, 1e-9);
        }

        [Test]
        public void TheCombatZoomHoldsOneSecondThenEasesBack()
        {
            var d = new CameraDirector();
            Run(d, Frame(4), 3);
            Run(d, Frame(0), 0.9);
            Assert.IsTrue(d.CombatFraming, "held");
            Assert.AreEqual(1.12, d.ZoomGoal, 1e-9);
            Run(d, Frame(0), 3);
            Assert.IsFalse(d.CombatFraming);
            Assert.AreEqual(1.0, d.Zoom, 1e-3);
        }

        [Test]
        public void EnemiesNearCountsOnlyLivingEnemiesInsideTheRadius()
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 5, TeamSize = 4, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
            });
            double hx = w.X[0], hz = w.Z[0];
            int placed = 0;
            w.Actors[0].Alive = true;
            for (int i = 1; i < w.ActorCount; i++)
            {
                w.Actors[i].Alive = true;
                if (w.Actors[i].Team == w.Actors[0].Team) { w.X[i] = hx; w.Z[i] = hz; continue; }   // allies never count
                w.X[i] = hx + (placed < 3 ? 7.5 : 8.5);
                w.Z[i] = hz;
                placed++;
            }
            Assert.AreEqual(3, CameraFollowTarget.EnemiesNear(w, hx, hz));
        }

        // ---- hatch focus

        [Test]
        public void HatchFocusLeansTowardTheHatchCappedAtSixUnits()
        {
            var d = new CameraDirector();
            var f = new CameraDirectorInput { Dt = Dt, Hatched = true, HatchSimTime = 100, SimTime = 101, HatchX = 40, HatchZ = 0 };
            d.Step(f);
            Assert.AreEqual(1.0, d.HatchFocusWeight, 1e-9);
            Assert.AreEqual(6.0, d.OffsetX, 1e-9);
            Assert.AreEqual(0.0, d.OffsetZ, 1e-9);
            f.HatchX = 4;
            d.Step(f);
            Assert.AreEqual(2.0, d.OffsetX, 1e-9, "half the way when the hatch is near");
        }

        [Test]
        public void HatchFocusEndsAfterTwoSimSeconds()
        {
            Assert.AreEqual(0, CameraDirector.HatchFocusAt(-0.1));
            Assert.AreEqual(0, CameraDirector.HatchFocusAt(0));
            Assert.AreEqual(1, CameraDirector.HatchFocusAt(1.0), 1e-9);
            Assert.AreEqual(0, CameraDirector.HatchFocusAt(CameraDirector.HatchFocusSeconds));
            var d = new CameraDirector();
            d.Step(new CameraDirectorInput { Dt = Dt, Hatched = true, HatchSimTime = 0, SimTime = 2.5, HatchX = 10 });
            Assert.AreEqual(0, d.OffsetX, 1e-12);
        }

        // ---- hold-to-look (D5)

        [Test]
        public void HoldToLookReachesTwelveUnitsTowardTheHeldScreenDirection()
        {
            var d = new CameraDirector();
            d.Step(new CameraDirectorInput { Dt = Dt, Looking = true, LookRight = 0, LookUp = 2 });
            Assert.IsTrue(d.Looking);
            Assert.AreEqual(12 * HumanInputState.ScreenUpX, d.OffsetX, 1e-9);
            Assert.AreEqual(12 * HumanInputState.ScreenUpZ, d.OffsetZ, 1e-9);
            Assert.AreEqual(12, Math.Sqrt(d.OffsetX * d.OffsetX + d.OffsetZ * d.OffsetZ), 1e-9);
            d.Step(new CameraDirectorInput { Dt = Dt });
            Assert.IsFalse(d.Looking);
            Assert.AreEqual(0, d.OffsetX, 1e-12, "release: the offset goes home and the follow spring brings the view back");
        }

        [Test]
        public void TheFollowTakesTheOffsetAndDropsTheCursorLookAheadWhileLooking()
        {
            var plain = new CameraFollow(HumanInputState.ScreenUpX, HumanInputState.ScreenUpZ);
            var look = new CameraFollow(HumanInputState.ScreenUpX, HumanInputState.ScreenUpZ);
            var f = new CameraFollowInput { Dt = Dt, MinX = -100, MaxX = 100, MinZ = -100, MaxZ = 100, HasAim = true, AimX = 10, AimZ = 0 };
            plain.Step(f);
            look.Step(f);
            f.ExtraX = 5;
            f.SuppressAim = true;
            for (int i = 0; i < 300; i++) look.Step(f);
            f.ExtraX = 0;
            f.SuppressAim = false;
            for (int i = 0; i < 300; i++) plain.Step(f);
            Assert.AreEqual(0, look.LaX, 1e-6, "no cursor look-ahead while looking");
            Assert.AreEqual(5 + 2 * HumanInputState.ScreenUpX, look.SX.X, 1e-3);
            Assert.Greater(plain.LaX, 0.5, "the plain follow still leans toward the cursor");
        }

        [Test]
        public void ARightStickClickLatchesLookAndALongPressOnlyHolds()
        {
            var l = new LookLatch();
            l.Update(true, 0);
            Assert.IsTrue(l.Active);
            l.Update(false, 0.1);
            Assert.IsTrue(l.Active, "click latches");
            l.Update(true, 1.0);
            Assert.IsFalse(l.Active, "the next press ends the latch");
            l.Update(false, 1.1);
            Assert.IsFalse(l.Active);
            l.Update(true, 2.0);
            l.Update(false, 2.0 + LookLatch.ClickSeconds + 0.1);
            Assert.IsFalse(l.Active, "a long press was a hold");
        }

        [Test]
        public void TheOverrideStandsEveryRuleDown()
        {
            var d = new CameraDirector();
            Run(d, Frame(5), 2);
            d.Step(new CameraDirectorInput { Dt = Dt, EnemiesNear = 5, Looking = true, LookUp = 1, Override = true });
            Assert.AreEqual(1.0, d.Zoom, 1e-12);
            Assert.AreEqual(0, d.OffsetX, 1e-12);
        }

        // ---- see-through probe

        static MapDef Flat(int n)
        {
            var cells = new byte[n * n];
            for (int i = 0; i < cells.Length; i++) cells[i] = Cell.Floor;
            return new MapDef { Width = n, Height = n, Cells = cells, Height01 = new double[n * n] };
        }

        [Test]
        public void OpenGroundDoesNotCoverThePlayer()
        {
            MapDef m = Flat(40);
            Assert.IsFalse(SeeThroughProbe.Covered(m, 10, 19, 10, 20, 0, 20, null, 0));
        }

        [Test]
        public void AWallBetweenTheCameraAndThePlayerCoversHim()
        {
            MapDef m = Flat(40);
            // a wall line two cells toward the camera from the player, seen along a low sight line (the gameplay camera
            // is steep, so a 1.5 u wall only covers a player tight behind it; the rule itself is what is tested here)
            for (int z = 0; z < 40; z++) m.Cells[z * 40 + 18] = Cell.Wall;
            Assert.IsTrue(SeeThroughProbe.Covered(m, 10, 4, 20, 20, 0, 20, null, 0));
            // the same wall behind him does not
            Assert.IsFalse(SeeThroughProbe.Covered(m, 30, 4, 20, 20, 0, 20, null, 0));
            // nor does it from high above
            Assert.IsFalse(SeeThroughProbe.Covered(m, 10, 40, 20, 20, 0, 20, null, 0));
        }

        [Test]
        public void ATreeOrTheKaijuInTheSightLineCoversHim()
        {
            MapDef m = Flat(40);
            var cyl = new[] { new SeeThroughCylinder { X = 18, Z = 20, R = 1.6, Y0 = 0, Y1 = 5 } };
            Assert.IsTrue(SeeThroughProbe.Covered(m, 10, 19, 20, 20, 0, 20, cyl, 1));
            cyl[0].Z = 30;
            Assert.IsFalse(SeeThroughProbe.Covered(m, 10, 19, 20, 20, 0, 20, cyl, 1));
        }

        [Test]
        public void HisOwnLedgeNeverCoversHim()
        {
            MapDef m = Flat(40);
            m.Cells[20 * 40 + 20] = Cell.Wall; // he stands on top of a wall block
            Assert.IsFalse(SeeThroughProbe.Covered(m, 10, 19, 10, 20.5, 1.5, 20.5, null, 0));
        }
    }
}

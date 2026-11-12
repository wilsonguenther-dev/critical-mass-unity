using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/movement.test.ts (09e0904), CM-21 (1) sim acceleration and braking. Only actor 0 gets input;
    /// every other actor's input stays zero (no bots).
    /// </summary>
    [TestFixture]
    public sealed class MovementTests
    {
        private static readonly GameConfig Cfg = GameConfig.CreateBrowser5v5();
        private static readonly double Ms = Cfg.Class(ClassId.Warrior).MoveSpeed;
        private static readonly double Dt = SimStep.Dt(Cfg);
        private static readonly InputFrame Frame = SimInput.EmptyInput();

        private static WorldOptions Opts(int seed)
        {
            return new WorldOptions
            {
                Seed = seed,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        private static World NewWorld(int seed = 1)
        {
            return World.CreateWorld(Opts(seed));
        }

        private static double Speed(World w, int id = 0)
        {
            return Math.Sqrt(w.Vx[id] * w.Vx[id] + w.Vz[id] * w.Vz[id]);
        }

        private static void Move(World w, double mx, double mz)
        {
            Frame.MoveX = mx;
            Frame.MoveZ = mz;
            SimInput.ApplyInput(w, 0, Frame);
            SimStep.Step(w, Dt);
        }

        [Test]
        public void AccelFromRestWithFullInputSpeedReachesGe90PercentOfMoveSpeedAfter008020SOfStepsNeverAboveIt()
        {
            World w = NewWorld();
            Assert.AreEqual(0.0, Speed(w));
            int steps = 0;
            while (Speed(w) < 0.9 * Ms && steps < 60)
            {
                Move(w, 1, 0);
                steps++;
                Assert.LessOrEqual(Speed(w), Ms + 1e-5);
            }
            double t = steps * Dt;
            Assert.GreaterOrEqual(t, 0.08);
            Assert.LessOrEqual(t, 0.2);
            // The first step adds moveSpeed / (accelSeconds * simHz), not the whole speed (main was instant).
            World w2 = NewWorld();
            Move(w2, 1, 0);
            Assert.AreEqual((Ms * Dt) / Cfg.Movement.AccelSeconds, Speed(w2), 0.5e-4);
            Assert.Less(Speed(w2), 0.5 * Ms);
        }

        [Test]
        public void AccelReleaseToSpeedBelow10PercentOfMoveSpeedWithin015SThenExactly0()
        {
            World w = NewWorld();
            for (int s = 0; s < 15; s++) Move(w, 1, 0);
            Assert.AreEqual(Ms, Speed(w), 0.5e-4);
            int steps = 0;
            while (Speed(w) >= 0.1 * Ms && steps < 60)
            {
                Move(w, 0, 0);
                steps++;
            }
            Assert.LessOrEqual(steps * Dt, 0.15);
            for (int s = 0; s < 5; s++) Move(w, 0, 0);
            Assert.AreEqual(0.0, Speed(w));
            // The position stops moving with the velocity (no drift once braked).
            double x = w.X[0];
            Move(w, 0, 0);
            Assert.AreEqual(x, w.X[0]);
        }

        [Test]
        public void AccelDiagonalTopSpeedEqualsCardinalWithin1PercentDiagonalsStayNormalised()
        {
            World a = NewWorld();
            World b = NewWorld();
            for (int s = 0; s < 15; s++)
            {
                Move(a, 1, 0);
                Move(b, 1, 1);
            }
            double card = Speed(a);
            double diag = Speed(b);
            Assert.LessOrEqual(Math.Abs(diag - card) / card, 0.01);
            Assert.Less(Math.Abs(b.Vx[0] - b.Vz[0]), 1e-4);
        }

        [Test]
        public void AccelDeterministicForASeedSameInputsToIdenticalVelocityAndPositionArrays()
        {
            World a = NewWorld(7);
            World b = NewWorld(7);
            double[] pattern = { 1, 0, 0.5, -1, 0, 1, 1, 0 };
            for (int s = 0; s < 90; s++)
            {
                int k = JsMath.FloorToInt((double)s / 6) % pattern.Length;
                Move(a, pattern[k], pattern[(k + 3) % pattern.Length]);
                Move(b, pattern[k], pattern[(k + 3) % pattern.Length]);
            }
            CollectionAssert.AreEqual(b.Vx, a.Vx);
            CollectionAssert.AreEqual(b.Vz, a.Vz);
            CollectionAssert.AreEqual(b.X, a.X);
        }

        [Test]
        public void AccelVelocityIsZeroAtSpawnAndAfterResetWorldADeadActorSVelocityIsZero()
        {
            World w = NewWorld();
            for (int s = 0; s < 10; s++) Move(w, 1, 0);
            Assert.Greater(Speed(w), 0.0);
            World.ResetWorld(w, w.Opts);
            for (int i = 0; i < w.Vx.Length; i++)
            {
                Assert.AreEqual(0.0, w.Vx[i]);
                Assert.AreEqual(0.0, w.Vz[i]);
            }
            for (int s = 0; s < 10; s++) Move(w, 1, 0);
            w.Actors[0].SpawnProtect = 0;
            Damage.ApplyDamage(w, 0, 9999, -1, 0, 0);
            Move(w, 1, 0);
            Assert.AreEqual(false, w.Actors[0].Alive);
            Assert.AreEqual(0.0, Speed(w));
        }
    }
}

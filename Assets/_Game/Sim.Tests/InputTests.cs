using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/input.test.ts (09e0904).</summary>
    [TestFixture]
    public sealed class InputTests
    {
        private static WorldOptions Opts(int seed, int teamSize, TeamId team, ClassId cls)
        {
            return new WorldOptions
            {
                Seed = seed,
                TeamSize = teamSize,
                HumanTeam = team,
                HumanClass = cls,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        /// <summary>One warrior alone on open floor south of the hill centre (the other actor is dead).</summary>
        private static World Lone()
        {
            World w = World.CreateWorld(Opts(1, 1, TeamId.Azure, ClassId.Warrior));
            w.Actors[1].Alive = false;
            w.X[0] = 56;
            w.Z[0] = 40;
            return w;
        }

        /// <summary>{ ...emptyInput(), moveX, moveZ, aimX, aimZ }.</summary>
        private static InputFrame With(double moveX, double moveZ, double aimX, double aimZ)
        {
            InputFrame f = SimInput.EmptyInput();
            f.MoveX = moveX;
            f.MoveZ = moveZ;
            f.AimX = aimX;
            f.AimZ = aimZ;
            return f;
        }

        private const double D6 = 0.5e-6;

        [Test]
        public void EmptyInputIsNeutralWithClassPick1()
        {
            InputFrame e = SimInput.EmptyInput();
            Assert.AreEqual(0.0, e.MoveX);
            Assert.AreEqual(0.0, e.MoveZ);
            Assert.AreEqual(0.0, e.AimX);
            Assert.AreEqual(0.0, e.AimZ);
            Assert.AreEqual(false, e.Attack);
            Assert.AreEqual(false, e.Ability);
            Assert.AreEqual(false, e.Interact);
            Assert.AreEqual(-1, e.ClassPick);
            Assert.AreEqual(false, e.Terraform);
            Assert.AreEqual(false, e.Jump);
        }

        [Test]
        public void ApplyInputCopiesIntoTheWorldSlotClampsMoveMagnitudeTo1AndIsNaNSafe()
        {
            World w = World.CreateWorld(Opts(1, 5, TeamId.Azure, ClassId.Warrior));
            InputFrame slot = w.Inputs[3];
            SimInput.ApplyInput(w, 3, new InputFrame
            {
                MoveX = 3, MoveZ = 4, AimX = 10, AimZ = 20, Attack = true, Ability = false, Interact = true, ClassPick = 2, Jump = true,
            });
            Assert.AreSame(slot, w.Inputs[3]);
            Assert.AreEqual(0.6, slot.MoveX, D6);
            Assert.AreEqual(0.8, slot.MoveZ, D6);
            Assert.AreEqual(10.0, slot.AimX);
            Assert.AreEqual(20.0, slot.AimZ);
            Assert.AreEqual(true, slot.Attack);
            Assert.AreEqual(true, slot.Interact);
            Assert.AreEqual(2, slot.ClassPick);
            SimInput.ApplyInput(w, 3, new InputFrame
            {
                MoveX = double.NaN, MoveZ = double.PositiveInfinity, AimX = double.NaN, AimZ = 5, Attack = false, Ability = true,
                Interact = false, ClassPick = 7, Jump = false,
            });
            Assert.AreEqual(0.0, slot.MoveX);
            Assert.AreEqual(0.0, slot.MoveZ);
            Assert.AreEqual(true, double.IsFinite(slot.AimX));
            Assert.AreEqual(-1, slot.ClassPick);
            // Out-of-range actor ids are ignored, never thrown on.
            Assert.DoesNotThrow(() => SimInput.ApplyInput(w, 999, SimInput.EmptyInput()));
        }

        [Test]
        public void ANonFiniteAimMeansNoAimFacingFollowsMovementWhenTheNaNAimIsReAppliedEveryStep()
        {
            World w = Lone();
            InputFrame f = With(0, 1, double.NaN, double.NaN);
            for (int t = 0; t < 5; t++)
            {
                SimInput.ApplyInput(w, 0, f);
                Assert.AreEqual(0, w.HasAim[0]);
                SimStep.Step(w);
                Assert.Greater(w.Z[0], w.PrevZ[0]); // it really moved south
                Assert.AreEqual(Math.PI / 2, w.Actors[0].Facing, D6); // south, never back toward where it stood
            }
        }

        [Test]
        public void NoAimHeldAcrossStepsInputAppliedOnceKeepsFollowingMovementAndTurnsWithIt()
        {
            World w = Lone();
            SimInput.ApplyInput(w, 0, With(0, 1, double.NaN, double.PositiveInfinity));
            for (int t = 0; t < 10; t++)
            {
                SimStep.Step(w);
                Assert.AreEqual(Math.PI / 2, w.Actors[0].Facing, D6);
            }
            SimInput.ApplyInput(w, 0, With(-1, 0, double.NaN, double.NaN));
            SimStep.Step(w);
            Assert.AreEqual(Math.PI, Math.Abs(w.Actors[0].Facing), D6); // west
            // Stopping with no aim keeps the last facing.
            SimInput.ApplyInput(w, 0, With(0, 0, double.NaN, double.NaN));
            SimStep.Step(w);
            Assert.AreEqual(false, w.Actors[0].Moving);
            Assert.AreEqual(Math.PI, Math.Abs(w.Actors[0].Facing), D6);
        }

        [Test]
        public void AFiniteAimWinsOverMovementAndIsMeasuredFromThePostMovePosition()
        {
            World w = Lone();
            SimInput.ApplyInput(w, 0, With(0, 1, 66, 40)); // aim east while walking south
            Assert.AreEqual(1, w.HasAim[0]);
            SimStep.Step(w);
            Assert.AreEqual(JsMath.Atan2(40 - w.Z[0], 66 - w.X[0]), w.Actors[0].Facing, D6);
            // Switching to no aim hands facing back to movement.
            SimInput.ApplyInput(w, 0, With(0, 1, double.NaN, 3));
            Assert.AreEqual(0, w.HasAim[0]);
            SimStep.Step(w);
            Assert.AreEqual(Math.PI / 2, w.Actors[0].Facing, D6);
        }

        [Test]
        public void ResetWorldClearsEveryAimFlagFreshSpawnsHaveNoAim()
        {
            World w = World.CreateWorld(Opts(2, 4, TeamId.Crimson, ClassId.Miner));
            for (int i = 0; i < w.ActorCount; i++) Assert.AreEqual(0, w.HasAim[i]);
            SimInput.ApplyInput(w, 0, With(0, 0, 1, 1));
            Assert.AreEqual(1, w.HasAim[0]);
            World.ResetWorld(w, Opts(2, 4, TeamId.Crimson, ClassId.Miner));
            for (int i = 0; i < w.HasAim.Length; i++) Assert.AreEqual(0, w.HasAim[i]);
        }
    }
}

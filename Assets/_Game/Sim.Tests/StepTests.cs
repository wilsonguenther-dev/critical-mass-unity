using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/step.test.ts (09e0904).</summary>
    [TestFixture]
    public sealed class StepTests
    {
        private static WorldOptions Opts(int seed, ClassId humanClass)
        {
            return new WorldOptions
            {
                Seed = seed,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = humanClass,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        /// <summary>300 rows of 10 scripted input frames from one seeded Rng.</summary>
        private static InputFrame[][] Scripted(uint seed)
        {
            Rng rng = Rng.CreateRng(seed);
            var frames = new InputFrame[300][];
            for (int t = 0; t < 300; t++)
            {
                var row = new InputFrame[10];
                for (int i = 0; i < 10; i++)
                {
                    InputFrame f = SimInput.EmptyInput();
                    f.MoveX = Rng.NextFloat(rng) * 2 - 1;
                    f.MoveZ = Rng.NextFloat(rng) * 2 - 1;
                    f.AimX = Rng.NextFloat(rng) * 112;
                    f.AimZ = Rng.NextFloat(rng) * 72;
                    f.Attack = Rng.NextFloat(rng) < 0.2;
                    row[i] = f;
                }
                frames[t] = row;
            }
            return frames;
        }

        [Test]
        public void DTIs1SimHz()
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            Assert.AreEqual(1 / c.SimHz, SimStep.Dt(c), 0.5 * Math.Pow(10, -12));
        }

        [Test]
        public void IsDeterministicSameSeedPlusSame300ScriptedFramesGivesIdenticalXZ()
        {
            InputFrame[][] frames = Scripted(99);
            World a = World.CreateWorld(Opts(2024, ClassId.Farmer));
            World b = World.CreateWorld(Opts(2024, ClassId.Farmer));
            for (int t = 0; t < 300; t++)
            {
                for (int i = 0; i < 10; i++)
                {
                    SimInput.ApplyInput(a, i, frames[t][i]);
                    SimInput.ApplyInput(b, i, frames[t][i]);
                }
                SimStep.Step(a);
                SimStep.Step(b);
            }
            double dt = SimStep.Dt(a.Config);
            Assert.AreEqual(300, a.Tick);
            CollectionAssert.AreEqual(b.X, a.X);
            CollectionAssert.AreEqual(b.Z, a.Z);
            Assert.AreEqual(b.Rng.State, a.Rng.State);
            Assert.AreEqual(300 * dt, a.Match.Time, 0.5 * Math.Pow(10, -6));
            // Something actually moved.
            int moved = 0;
            for (int i = 0; i < 10; i++) if (a.X[i] != a.PrevX[i] || a.Z[i] != a.PrevZ[i]) moved++;
            Assert.Greater(moved, 0);
        }

        [Test]
        public void CopiesXZIntoPrevXPrevZAtTheStartOfEachStep()
        {
            World w = World.CreateWorld(Opts(5, ClassId.Warrior));
            InputFrame f = SimInput.EmptyInput();
            f.MoveX = 1;
            SimInput.ApplyInput(w, 0, f);
            double x0 = w.X[0];
            SimStep.Step(w);
            Assert.AreEqual(x0, w.PrevX[0], 1e-4);
            Assert.Greater(w.X[0], x0);
            Assert.AreEqual(true, w.Actors[0].Moving);
            // CM-21: one step from rest moves at the first acceleration step's speed, moveSpeed * DT / accelSeconds.
            double dt = SimStep.Dt(w.Config);
            double v1 = Math.Min(1, dt / w.Config.Movement.AccelSeconds) * w.Config.Class(ClassId.Warrior).MoveSpeed;
            Assert.AreEqual(v1 * dt, w.X[0] - x0, 0.5 * Math.Pow(10, -3));
        }

        [Test]
        public void DoesNothingWhenMatchPhaseIsEnded()
        {
            World w = World.CreateWorld(Opts(5, ClassId.Warrior));
            w.Match.Phase = MatchPhase.Ended;
            w.Match.Time = 123;
            InputFrame f = SimInput.EmptyInput();
            f.MoveX = 1;
            SimInput.ApplyInput(w, 0, f);
            double x0 = w.X[0];
            SimStep.Step(w);
            Assert.AreEqual(123.0, w.Match.Time);
            Assert.AreEqual(0, w.Tick);
            Assert.AreEqual(x0, w.X[0], 1e-4);
        }
    }
}

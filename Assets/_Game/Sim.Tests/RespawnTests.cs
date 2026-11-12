using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of the 3 CM-20 standUp cases of src/sim/respawn.test.ts (09e0904); the 7 CM-54 cases are CM-84's.</summary>
    [TestFixture]
    public sealed class RespawnTests
    {
        /// <summary>5v5, human azure warrior, seed 7 (browser OPTS).</summary>
        private static WorldOptions Opts()
        {
            return new WorldOptions
            {
                Seed = 7,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        /// <summary>Browser RESPAWN_STEPS.</summary>
        private static int RespawnSteps(GameConfig c)
        {
            return (int)JsMath.Round(c.Respawn.DelaySeconds * c.SimHz);
        }

        /// <summary>Browser STAND_STEPS.</summary>
        private static int StandSteps(GameConfig c)
        {
            return (int)JsMath.Round(c.Respawn.StandUpSeconds * c.SimHz);
        }

        /// <summary>Kill id (spawn protection cleared) and step until it is back.</summary>
        private static void KillAndRespawn(World w, int id)
        {
            w.Actors[id].SpawnProtect = 0;
            Damage.ApplyDamage(w, id, 9999, -1, w.X[id], w.Z[id]);
            Assert.AreEqual(false, w.Actors[id].Alive);
            int respawnSteps = RespawnSteps(w.Config);
            for (int i = 0; i < respawnSteps && !w.Actors[id].Alive; i++) SimStep.Step(w);
            Assert.AreEqual(true, w.Actors[id].Alive);
        }

        /// <summary>Full move input along (dirX, dirZ) with every action button held, aimed 10 along it.</summary>
        private static void FullInput(World w, int id, double dirX = 1, double dirZ = 0)
        {
            InputFrame f = SimInput.EmptyInput();
            f.MoveX = dirX;
            f.MoveZ = dirZ;
            f.Attack = true;
            f.Ability = true;
            f.Interact = true;
            f.AimX = w.X[id] + dirX * 10;
            f.AimZ = w.Z[id] + dirZ * 10;
            SimInput.ApplyInput(w, id, f);
        }

        [Test]
        public void StandUpAKilledActorRespawnsHoldsItsXZExactlyForStandUpSecondsOfStepsWithAFullMoveInputThenMoves()
        {
            WorldOptions opts = Opts();
            GameConfig cfg = opts.Config;
            int standSteps = StandSteps(cfg);
            Assert.AreEqual(24, standSteps);
            World w = World.CreateWorld(opts);
            int id = 6;
            KillAndRespawn(w, id);
            Actor a = w.Actors[id];
            Assert.AreEqual(cfg.Respawn.StandUpSeconds, a.StandUp);
            double x0 = w.X[id];
            double z0 = w.Z[id];
            HillDef hill = w.Map.Hill;
            double facing0 = JsMath.Atan2(hill.Z - z0, hill.X - x0);
            Assert.AreEqual(facing0, a.Facing);
            double cdBefore = a.AbilityCd;
            for (int s = 1; s <= standSteps; s++)
            {
                // StandUp > 0 at the start of this step: every input is ignored.
                Assert.Greater(a.StandUp, 0);
                FullInput(w, id);
                SimStep.Step(w);
                Assert.AreEqual(x0, w.X[id]);
                Assert.AreEqual(z0, w.Z[id]);
                Assert.AreEqual(facing0, a.Facing);
                Assert.AreEqual(false, a.Moving);
                Assert.AreEqual(0, a.AttackCd);
                Assert.AreEqual(cdBefore, a.AbilityCd);
                Assert.AreEqual(0, a.GatherProgress);
            }
            Assert.AreEqual(0, a.StandUp);
            FullInput(w, id);
            SimStep.Step(w);
            Assert.Greater(w.X[id], x0);
            Assert.AreEqual(true, a.Moving);
        }

        [Test]
        public void StandUpResetWorldLeavesStandUp0ForEveryActorTheMatchStartHasNoStandUp()
        {
            World w = World.CreateWorld(Opts());
            for (int i = 0; i < w.ActorCount; i++) Assert.AreEqual(0, w.Actors[i].StandUp);
            KillAndRespawn(w, 3);
            Assert.Greater(w.Actors[3].StandUp, 0);
            World.ResetWorld(w, WithTeamSize(w.Opts, 16));
            for (int i = 0; i < w.Actors.Length; i++) Assert.AreEqual(0, w.Actors[i].StandUp);
            World.ResetWorld(w, WithTeamSize(w.Opts, 5));
            for (int i = 0; i < w.Actors.Length; i++) Assert.AreEqual(0, w.Actors[i].StandUp);
        }

        /// <summary>Browser { ...w.opts, teamSize }.</summary>
        private static WorldOptions WithTeamSize(WorldOptions o, int teamSize)
        {
            return new WorldOptions
            {
                Seed = o.Seed,
                TeamSize = teamSize,
                HumanTeam = o.HumanTeam,
                HumanClass = o.HumanClass,
                Config = o.Config,
            };
        }

        [Test]
        public void StandUpALivingActorWalkedThroughAStandingUpActorLeavesItsXZBitIdenticalOnEveryStepWhileStandUpGt0()
        {
            World w = World.CreateWorld(Opts());
            GameConfig cfg = w.Config;
            int standSteps = StandSteps(cfg);
            int id = 6;
            KillAndRespawn(w, id);
            double x0 = w.X[id];
            double z0 = w.Z[id];
            // A teammate walks straight through the body along +x, starting just behind it.
            int mate = 7;
            w.Actors[mate].SpawnProtect = 0;
            w.X[mate] = w.PrevX[mate] = JsMath.F32(x0 - 1.2);
            w.Z[mate] = w.PrevZ[mate] = z0;
            bool overlapped = false;
            int steps = 0;
            // The step cap only turns a never-ending stand-up into a failed assertion instead of a hung run.
            while (w.Actors[id].StandUp > 0 && steps <= 10 * standSteps)
            {
                FullInput(w, mate);
                w.Inputs[mate].Attack = false;
                w.Inputs[mate].Ability = false;
                SimStep.Step(w);
                steps++;
                Assert.AreEqual(x0, w.X[id]);
                Assert.AreEqual(z0, w.Z[id]);
                double r = cfg.Class(w.Actors[id].Cls).Radius + cfg.Class(w.Actors[mate].Cls).Radius;
                double dx = w.X[mate] - x0;
                double dz = w.Z[mate] - z0;
                if (Math.Sqrt(dx * dx + dz * dz) <= r + 1e-4) overlapped = true;
            }
            Assert.AreEqual(standSteps, steps);
            Assert.AreEqual(true, overlapped);
            // The walker took the whole push: in line with the body it is held behind it, never through it.
            Assert.Less(w.X[mate], x0);
        }
    }
}

using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/bots/bots.test.ts (09e0904): describe "bots: director, human slot, state reset" (2 cases) and the fix-cm3-1 fillBotInput case.</summary>
    [TestFixture]
    public sealed class BotsDirectorTests
    {
        private static readonly InputFrame Frame = SimInput.EmptyInput();

        private static World MakeWorld(int seed, int teamSize)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = seed,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            });
        }

        /// <summary>One SimDriver step with every actor (human included) on bots.</summary>
        private static void TickOnce(World world)
        {
            double dt = SimStep.Dt(world.Config);
            BotBrain.FillBotInput(world, 0, Frame);
            SimInput.ApplyInput(world, 0, Frame);
            BotBrain.BotsThink(world, dt);
            for (int id = 1; id < world.ActorCount; id++)
            {
                BotBrain.FillBotInput(world, id, Frame);
                SimInput.ApplyInput(world, id, Frame);
            }
            SimStep.Step(world, dt);
        }

        private static void Run(World world, double seconds)
        {
            int steps = (int)JsMath.Round(seconds * world.Config.SimHz);
            for (int s = 0; s < steps; s++) TickOnce(world);
        }

        [Test]
        public void Director16v16SendsRound16X0254PerTeamToKaijuDutyFarmersFirst5v5Sends1Farmer()
        {
            int[][] cases = { new[] { 16, 4 }, new[] { 5, 1 } };
            foreach (int[] row in cases)
            {
                int size = row[0];
                int quota = row[1];
                World w = MakeWorld(3, size);
                Run(w, 0.5);
                BotsState st = BotBrain.GetBotsState(w);
                foreach (TeamId team in TeamIds.All)
                {
                    int kaiju = 0;
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        if (w.Actors[i].Team != team) continue;
                        if (st.Role[i] != BotsState.RoleKaiju) continue;
                        kaiju++;
                        Assert.AreEqual(ClassId.Farmer, w.Actors[i].Cls, "size " + size + " " + team + " actor " + i);
                    }
                    Assert.AreEqual(quota, kaiju, "size " + size + " " + team);
                }
            }
        }

        [Test]
        public void TheHumanActor0IsSkippedUnlessFillBotInputIsAskedForId0StateResetsOnResetWorld()
        {
            World w = MakeWorld(4, 5);
            double dt = SimStep.Dt(w.Config);
            InputFrame f = SimInput.EmptyInput();
            for (int s = 0; s < 60; s++)
            {
                BotBrain.BotsThink(w, dt);
                for (int id = 1; id < w.ActorCount; id++)
                {
                    BotBrain.FillBotInput(w, id, f);
                    SimInput.ApplyInput(w, id, f);
                }
                SimStep.Step(w, dt);
            }
            Assert.AreEqual(AiState.None, w.Actors[0].AiState);
            Assert.AreNotEqual(AiState.None, w.Actors[1].AiState);
            BotsState st = BotBrain.GetBotsState(w);
            Assert.AreEqual(0, st.Role[0]);
            // Autopilot: one FillBotInput(0) makes the next think include actor 0.
            for (int s = 0; s < 10; s++) TickOnce(w);
            Assert.AreNotEqual(AiState.None, w.Actors[0].AiState);
            int builds = st.Fields.Builds;
            w.Tick = 0; // a restart (ResetWorld) brings the clock back to 0
            BotBrain.GetBotsState(w);
            Assert.AreEqual(builds + st.Fields.Count, st.Fields.Builds);
        }

        [Test]
        public void FillBotInputLeavesOutJumpFalseAndOutTerraformFalseWhateverTheFrameHeldBefore()
        {
            World w = MakeWorld(1, 5);
            InputFrame shared = SimInput.EmptyInput();
            BotBrain.BotsThink(w, SimStep.Dt(w.Config));
            for (int id = 1; id < w.ActorCount; id++)
            {
                shared.Jump = true;
                shared.Terraform = true;
                BotBrain.FillBotInput(w, id, shared);
                Assert.IsFalse(shared.Jump, "bot " + id + " jump");
                Assert.IsFalse(shared.Terraform, "bot " + id + " terraform");
            }
        }

        [Test]
        public void ASimDriverStyleStepWithTheHumanPressingJumpLiftsOnlyActor0EveryBotStaysOnTheGround()
        {
            World w = MakeWorld(1, 5);
            // Every body can jump this step: alive, not getting up, grounded. Without the fix every bot would take off too.
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                a.Alive = true;
                a.StandUp = 0;
                Assert.AreEqual(0, a.AirLen, "actor " + i + " grounded before");
            }
            InputFrame shared = SimInput.EmptyInput();
            // The human frame, as the human input layer writes it on the step Space is pressed: standing still, jump edge true.
            shared.MoveX = 0;
            shared.MoveZ = 0;
            shared.AimX = double.NaN;
            shared.AimZ = double.NaN;
            shared.Jump = true;
            SimInput.ApplyInput(w, 0, shared);
            BotBrain.BotsThink(w, SimStep.Dt(w.Config));
            for (int id = 1; id < w.ActorCount; id++)
            {
                BotBrain.FillBotInput(w, id, shared);
                SimInput.ApplyInput(w, id, shared);
            }
            SimStep.Step(w, SimStep.Dt(w.Config));
            Assert.Greater(w.Actors[0].AirLen, 0, "human airborne");
            for (int id = 1; id < w.ActorCount; id++) Assert.AreEqual(0, w.Actors[id].AirLen, "bot " + id + " airLen");
        }
    }
}

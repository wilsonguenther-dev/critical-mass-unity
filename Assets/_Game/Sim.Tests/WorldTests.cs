using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/world.test.ts (09e0904).</summary>
    [TestFixture]
    public sealed class WorldTests
    {
        private static WorldOptions Opts5(int teamSize = 5)
        {
            return new WorldOptions
            {
                Seed = 1,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Miner,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        /// <summary>Per-class counts of actors [from, to), one slot per ClassId (CM-56: a seated placeholder class shows up).</summary>
        private static int[] ClassCounts(World world, int from, int to)
        {
            var c = new int[ClassIds.All.Count];
            for (int i = from; i < to; i++) c[(int)world.Actors[i].Cls]++;
            return c;
        }

        /// <summary>toEqual({ warrior, farmer, miner }) with no other class key present.</summary>
        private static void AssertCounts(int[] c, int warrior, int farmer, int miner)
        {
            Assert.AreEqual(warrior, c[(int)ClassId.Warrior]);
            Assert.AreEqual(farmer, c[(int)ClassId.Farmer]);
            Assert.AreEqual(miner, c[(int)ClassId.Miner]);
            Assert.AreEqual(0, c[(int)ClassId.Demolitions]);
            Assert.AreEqual(0, c[(int)ClassId.Builder]);
        }

        [Test]
        public void CreateWorld5v5Gives10Actors5PerTeamActor0IsTheHumanOnTheChosenTeamAndClass()
        {
            WorldOptions opts = Opts5();
            World w = World.CreateWorld(opts);
            GameConfig cfg = opts.Config;
            Assert.AreEqual(10, w.ActorCount);
            int azure = 0;
            int crimson = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                Assert.AreEqual(i, a.Id);
                Assert.AreEqual(true, a.Alive);
                Assert.AreEqual(a.MaxHp, a.Hp);
                Assert.AreEqual(cfg.Respawn.ProtectionSeconds, a.SpawnProtect);
                Assert.AreEqual(i == 0, a.IsHuman);
                if (a.Team == TeamId.Azure) azure++;
                else crimson++;
                Assert.AreEqual(true, MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(w.X[i]), JsMath.FloorToInt(w.Z[i])));
                Assert.AreEqual(true, a.Team == TeamId.Azure ? i < 5 : i >= 5);
            }
            Assert.AreEqual(5, azure);
            Assert.AreEqual(5, crimson);
            Assert.AreEqual(TeamId.Azure, w.Actors[0].Team);
            Assert.AreEqual(ClassId.Miner, w.Actors[0].Cls);
            // Bot team: 2 warrior, 2 farmer, 1 miner (largest remainder of 0.4/0.3/0.3 x 5).
            AssertCounts(ClassCounts(w, 5, 10), 2, 2, 1);
            // Human team: the human's miner takes the miner slot.
            AssertCounts(ClassCounts(w, 0, 5), 2, 2, 1);
            Assert.AreEqual(KaijuState.Egg, w.Kaiju.State);
            Assert.AreEqual(56, w.Kaiju.X, 1e-4);
            Assert.AreEqual(8, w.Kaiju.Z, 1e-4);
            Assert.AreEqual(cfg.Kaiju.HpPerTeamMember * 5, w.Kaiju.Hp);
            Assert.AreEqual(11, w.Nodes.Length);
            Assert.AreEqual(MatchPhase.Playing, w.Match.Phase);
            Assert.AreEqual(MatchResult.None, w.Match.Winner);
        }

        [Test]
        public void HumanOnCrimsonAsWarriorKeepsIds0N1OnTheHumanTeam()
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 3,
                TeamSize = 5,
                HumanTeam = TeamId.Crimson,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            });
            for (int i = 0; i < 5; i++) Assert.AreEqual(TeamId.Crimson, w.Actors[i].Team);
            for (int i = 5; i < 10; i++) Assert.AreEqual(TeamId.Azure, w.Actors[i].Team);
            Assert.AreEqual(ClassId.Warrior, w.Actors[0].Cls);
            AssertCounts(ClassCounts(w, 0, 5), 2, 2, 1);
        }

        [Test]
        public void CreateWorld16v16Gives32ActorsAndTeamSizeIsClampedTo1TeamSizeMax()
        {
            World w = World.CreateWorld(Opts5(16));
            Assert.AreEqual(32, w.ActorCount);
            AssertCounts(ClassCounts(w, 16, 32), 6, 5, 5);
            WorldOptions big = Opts5(99);
            Assert.AreEqual(2 * big.Config.TeamSizeMax, World.CreateWorld(big).ActorCount);
            Assert.AreEqual(2, World.CreateWorld(Opts5(0)).ActorCount);
        }

        [Test]
        public void ResetWorldReusesEveryPerActorArrayBumpsEpochAndClearsEventHead()
        {
            World w = World.CreateWorld(Opts5());
            double[] x = w.X;
            double[] z = w.Z;
            double[] prevX = w.PrevX;
            double[] prevZ = w.PrevZ;
            Actor[] actors = w.Actors;
            InputFrame[] inputs = w.Inputs;
            double[] threat = w.Kaiju.Threat;
            World.PushEvent(w, SimEventType.Hit, 1, 2, 3, 4, 5);
            Assert.AreEqual(1, w.EventHead);
            World.ResetWorld(w, Opts5(16));
            Assert.AreEqual(32, w.ActorCount);
            World.ResetWorld(w, Opts5());
            Assert.AreEqual(10, w.ActorCount);
            Assert.AreSame(x, w.X);
            Assert.AreSame(z, w.Z);
            Assert.AreSame(prevX, w.PrevX);
            Assert.AreSame(prevZ, w.PrevZ);
            Assert.AreSame(actors, w.Actors);
            Assert.AreSame(inputs, w.Inputs);
            Assert.AreSame(threat, w.Kaiju.Threat);
            Assert.AreEqual(2, w.Epoch);
            Assert.AreEqual(0, w.EventHead);
        }

        [Test]
        public void PushEventWritesIntoAFixedRingWithoutGrowingIt()
        {
            World w = World.CreateWorld(Opts5());
            SimEvent slot0 = w.Events[0];
            for (int i = 0; i < World.EventCapacity + 3; i++) World.PushEvent(w, SimEventType.Death, i, -1, i, -i, 7);
            Assert.AreEqual(World.EventCapacity, w.Events.Length);
            Assert.AreSame(slot0, w.Events[0]);
            Assert.AreEqual(World.EventCapacity + 3, w.EventHead);
            SimEvent last = w.Events[(w.EventHead - 1) % World.EventCapacity];
            Assert.AreEqual(SimEventType.Death, last.Type);
            Assert.AreEqual(World.EventCapacity + 2, last.A);
            Assert.AreEqual(-1, last.B);
            Assert.AreEqual(World.EventCapacity + 2, last.X, 1e-4);
            Assert.AreEqual(-(World.EventCapacity + 2), last.Z, 1e-4);
            Assert.AreEqual(7, last.Value);
            // ITEM CM-35: added by its owner (bio: undefined in the expected event)
        }
    }
}

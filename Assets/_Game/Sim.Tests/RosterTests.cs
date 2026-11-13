using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of the roster case of src/config/game.config.test.ts (09e0904, line 54).</summary>
    [TestFixture]
    public sealed class RosterTests
    {
        private static int Count(ClassId[] output, int n, ClassId c)
        {
            int k = 0;
            for (int i = 0; i < n; i++) if (output[i] == c) k++;
            return k;
        }

        [Test]
        public void CM56DefaultRostersAreTheThreeClassRostersAHumanMayStillTakeAPlaceholderSeat()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            var output = new ClassId[cfg.TeamSizeMax];
            for (int i = 0; i < output.Length; i++) output[i] = ClassId.Warrior;
            var humans = new List<ClassId?> { null };
            foreach (ClassId c in ClassIds.All) humans.Add(c);
            foreach (int n in cfg.TeamSizeOptions)
            {
                foreach (ClassId? human in humans)
                {
                    World.BuildRoster(cfg, n, human, output);
                    int extra = human == ClassId.Demolitions || human == ClassId.Builder || human == ClassId.Healer ? 1 : 0;
                    string label = n + "v" + n + " human " + (human.HasValue ? human.Value.ToString() : "null");
                    Assert.AreEqual(extra, Count(output, n, ClassId.Demolitions) + Count(output, n, ClassId.Builder) + Count(output, n, ClassId.Healer), label);
                    if (human.HasValue) Assert.AreEqual(human.Value, output[0]);
                }
            }
            // The CM-08 table in world.ts: 5 => 2 warrior, 2 farmer, 1 miner (bot team, no human).
            World.BuildRoster(cfg, 5, null, output);
            var expected = new[] { ClassId.Warrior, ClassId.Warrior, ClassId.Farmer, ClassId.Farmer, ClassId.Miner };
            for (int i = 0; i < 5; i++) Assert.AreEqual(expected[i], output[i]);
        }
    }
}

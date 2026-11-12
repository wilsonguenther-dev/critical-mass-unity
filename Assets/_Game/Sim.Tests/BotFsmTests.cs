using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>F-06d: pins the base wiring of the bot retreat threshold (new, no vitest twin).</summary>
    [TestFixture]
    public sealed class BotFsmTests
    {
        [Test]
        public void LowHpIsTheConfiguredSeekHpShare()
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            Assert.AreEqual(0.3, BotFsm.LowHp(c));

            GameConfig clone = c.Clone();
            clone.Heal.SeekHpShare = 0.5;
            Assert.AreEqual(0.5, BotFsm.LowHp(clone));
        }
    }
}

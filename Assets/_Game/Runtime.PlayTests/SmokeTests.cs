using System.Collections;
using CriticalMass.Runtime.Game;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>PlayMode smoke (new, no vitest twin): Boot builds the game and 16v16 bots play 60 sim seconds cleanly.</summary>
    public sealed class SmokeTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        [UnityTest]
        public IEnumerator BootPlays16v16BotsFor60SimulatedSecondsWithZeroExceptions()
        {
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;

            SimDriverBehaviour driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(driver, "Boot did not build a SimDriverBehaviour");
            driver.HumanAutopilot = true;
            Assert.AreEqual(32, driver.World.ActorCount);

            for (int i = 0; i < 60; i++)
            {
                driver.StepTicks(30);
                yield return null;
            }

            // 1800 ticks from StepTicks plus whatever Update ran during the 60 rendered frames.
            Assert.GreaterOrEqual(driver.World.Tick, 1800);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

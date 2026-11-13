using System.Collections;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>Combat feel in the real Boot scene: CombatFx is installed, decodes the 16v16 match and stays inside its pools.</summary>
    public sealed class CombatFxPlayTests
    {
        [UnityTest]
        public IEnumerator CombatFxRunsInBootAndStaysInsideItsPools()
        {
            SceneManager.LoadScene(GameBootstrap.BootScenePath);
            yield return null;
            yield return null;
            SimDriverBehaviour driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(driver);
            CombatFx fx = driver.GetComponent<CombatFx>();
            Assert.IsNotNull(fx, "GameBootstrap installs CombatFx");
            driver.HumanAutopilot = true;
            for (int i = 0; i < 60; i++)
            {
                driver.StepTicks(30);
                yield return null;
                double now = driver.World.Tick / driver.Config.SimHz;
                Assert.LessOrEqual(fx.Scene.ActiveCount(now), FxScene.Capacity);
            }
            fx.Rebuild(Vector3.right, Vector3.up);
            Assert.Greater(fx.Feed.Decoded, 100, "events decoded in 60 sim seconds");
            Assert.AreEqual(0, fx.Feed.Lapped, "no tick produced more than the event ring holds");
            Assert.AreEqual(0, fx.LastDropped, "effect meshes inside their vertex budgets");
            LogAssert.NoUnexpectedReceived();
        }
    }
}

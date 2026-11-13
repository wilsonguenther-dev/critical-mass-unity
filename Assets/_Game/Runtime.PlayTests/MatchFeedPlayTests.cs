using System.Collections;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>DG-11 / DG-38 in the booted game: the layer installs itself, follows the live world and logs nothing.</summary>
    public sealed class MatchFeedPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        [UnityTest]
        public IEnumerator MatchFeedInstallsAndFollowsALiveMatch()
        {
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;

            SimDriverBehaviour driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(driver, "Boot did not build a SimDriverBehaviour");
            driver.HumanAutopilot = true;
            CombatLog log = Object.FindAnyObjectByType<CombatLog>();
            Assert.IsNotNull(log, "the Match Feed object was not installed");
            Assert.IsNotNull(log.GetComponent<KillFeed>());
            Assert.IsNotNull(log.GetComponent<Scoreboard>());
            Assert.IsNotNull(log.GetComponent<DeathRecap>());
            Assert.IsNotNull(log.GetComponent<AchievementToast>());

            Scoreboard.ForceOpen = true;
            try
            {
                // 90 s of 16v16 at 30 ticks per frame: knock-outs happen, the feed and the board run on live data.
                for (int i = 0; i < 90; i++)
                {
                    driver.StepTicks(30);
                    yield return null;
                }
                Assert.AreSame(driver.World, CombatLog.Current.World, "the tally follows the live world");
                Assert.IsTrue(log.GetComponent<Scoreboard>().IsOpen || log.Suppressed);
                Assert.AreEqual(16, log.GetComponent<Scoreboard>().RowCount(TeamId.Azure));
                Assert.IsNotNull(Achievements.Current);
                int kills = 0;
                for (int id = 0; id < driver.World.ActorCount; id++) kills += CombatLog.Current.Tally(id).Knockouts;
                TestContext.WriteLine("knock-outs after 90 s: " + kills);
            }
            finally { Scoreboard.ForceOpen = false; }
            LogAssert.NoUnexpectedReceived();
        }
    }
}

using System.Collections;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.View.Moments;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>Big moments in the booted game: the director installs itself and every recipe plays without errors.</summary>
    public sealed class BigMomentsPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        [TearDown]
        public void TearDown()
        {
            BigMomentsSettings.ClearOverrides();
            BigMoments.StopAll();
            Banner.ClearAll();
        }

        static IEnumerator Boot()
        {
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;
            SimDriverBehaviour driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(driver, "Boot did not build a SimDriverBehaviour");
            driver.HumanAutopilot = true;
            Assert.IsNotNull(Object.FindAnyObjectByType<BigMoments>(), "BigMoments was not installed by the overlay registry");
            Assert.IsNotNull(BigMoments.Instance);
        }

        [UnityTest]
        public IEnumerator EveryRecipePlaysWithoutErrors()
        {
            yield return Boot();
            // natural moments off so only the forced ones play (Play ignores the setting)
            BigMomentsSettings.ModeOverride = BigMomentsMode.Off;
            int started = 0, ended = 0;
            System.Action<ActiveMoment> onStart = _ => started++;
            System.Action<MomentId> onEnd = _ => ended++;
            BigMoments.Started += onStart;
            BigMoments.Ended += onEnd;
            try
            {
                foreach (MomentId id in MomentRecipes.Gallery())
                {
                    BigMoments.Play(id, MomentScale.Takeover);
                    float until = Time.realtimeSinceStartup;   // integration batch 3: wait by wall clock, batch-mode ticks are about 1 ms
                    yield return null;
                    Assert.IsTrue(BigMoments.IsPlaying, id + " did not start");
                    until += 4f; while (BigMoments.IsPlaying && Time.realtimeSinceStartup < until) yield return null;
                    Assert.IsFalse(BigMoments.IsPlaying, id + " never ended");
                    BigMoments.Play(id, MomentScale.Stinger);
                    yield return null;
                    yield return null;
                }
            }
            finally
            {
                BigMoments.Started -= onStart;
                BigMoments.Ended -= onEnd;
            }
            int n = MomentRecipes.Gallery().Length;
            Assert.GreaterOrEqual(started, n * 2, "every takeover and stinger started");
            Assert.GreaterOrEqual(ended, n, "every takeover ended");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReducedMotionAndFlashStillPlayAndBannerIsHeld()
        {
            yield return Boot();
            BigMomentsSettings.ModeOverride = BigMomentsMode.Off;
            BigMomentsSettings.ReducedMotionOverride = true;
            BigMomentsSettings.ReduceFlashingOverride = true;
            Banner.Show(new BannerSpec { Headline = "TEST", Reason = "held", Priority = 3 });
            yield return null;
            BigMoments.Play(MomentId.Hatch);
            yield return null;
            Assert.IsTrue(BigMoments.IsPlaying);
            Assert.IsTrue(BigMoments.HoldBanner(), "the banner is held while the takeover plays");
            float until = Time.realtimeSinceStartup;   // integration batch 3: wait by wall clock, batch-mode ticks are about 1 ms
            until += 4f; while (BigMoments.IsPlaying && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsFalse(BigMoments.HoldBanner());
            Assert.IsTrue(Banner.IsOpen, "the banner comes back after the takeover");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator GalleryRunsToTheEnd()
        {
            yield return Boot();
            BigMomentsSettings.ModeOverride = BigMomentsMode.Full;
            BigMoments.PlayGallery();
            float until = Time.realtimeSinceStartup;   // integration batch 3: wait by wall clock, batch-mode ticks are about 1 ms
            yield return null;
            Assert.IsTrue(BigMoments.GalleryRunning);
            until += 60f; while (BigMoments.GalleryRunning && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsFalse(BigMoments.GalleryRunning, "the gallery finished");
            LogAssert.NoUnexpectedReceived();
        }
    }
}

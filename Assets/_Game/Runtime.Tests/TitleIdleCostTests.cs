using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b12-x1 (smoke: title idle CPU 37% against the 35% budget). The idle title's backdrop, once its still is up, takes no
    /// sim tick, syncs no fighter view and lets the world camera draw only the UI layer; the still comes 2 s into the title
    /// (after the world art is built), so the smoke's idle window (title + 2 s onward) is all still. The backdrop is its own
    /// world, never the real match's and never an online session's. Every case prints its value and limit.
    /// </summary>
    public sealed class TitleIdleCostTests
    {
        /// <summary>The live backdrop (before the still): at most this many sim ticks per wall second.</summary>
        const double LiveTicksPerSecondLimit = 30.0;

        [Test]
        public void TheIdleBackdropTakesNoTickAndSyncsNoViewOnceItsStillIsUp()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            int actors = 2 * GameFlow.BackdropTeamSize;
            double live = SimDriverBehaviour.TicksPerWallSecond(GameFlow.BackdropPace, cfg.SimHz, SimDriverBehaviour.BackdropHeld(true, false));
            double idle = SimDriverBehaviour.TicksPerWallSecond(GameFlow.BackdropPace, cfg.SimHz, SimDriverBehaviour.BackdropHeld(true, true));
            // An automation run's -autoplaySpeed rewrites TimeScale every frame; the hold does not depend on it.
            double idleFast = SimDriverBehaviour.TicksPerWallSecond(4.0, cfg.SimHz, SimDriverBehaviour.BackdropHeld(true, true));
            int liveViews = TitleBackdrop.SyncedViews(false, actors), idleViews = TitleBackdrop.SyncedViews(true, actors);
            Debug.Log($"[b12-x1] backdrop {GameFlow.BackdropTeamSize}v{GameFlow.BackdropTeamSize} pace {GameFlow.BackdropPace} at {cfg.SimHz} Hz: " +
                      $"live {live:0.#} ticks/s (limit {LiveTicksPerSecondLimit}), still {idle:0.#} ticks/s (limit {TitleBackdrop.FrozenTicksPerSecondLimit}), " +
                      $"still at speed 4 {idleFast:0.#} ticks/s");
            Debug.Log($"[b12-x1] fighter views synced per frame: live {liveViews}, still {idleViews} (limit {TitleBackdrop.FrozenSyncedViewsLimit})");
            Assert.LessOrEqual(live, LiveTicksPerSecondLimit, "the live backdrop stays slow");
            Assert.LessOrEqual(idle, TitleBackdrop.FrozenTicksPerSecondLimit, "no tick behind the still");
            Assert.LessOrEqual(idleFast, TitleBackdrop.FrozenTicksPerSecondLimit, "no tick behind the still, whatever the time scale");
            Assert.LessOrEqual(idleViews, TitleBackdrop.FrozenSyncedViewsLimit, "no fighter view synced behind the still");
            Assert.Greater(liveViews, 0, "the live backdrop does sync its views (the check is not vacuous)");
            Assert.IsFalse(SimDriverBehaviour.BackdropHeld(false, true), "a real match is never held by the title still");

            int mask = TitleBackdrop.FrozenCullingMask(5);
            Debug.Log($"[b12-x1] world camera mask behind the still: 0x{mask:X} (UI layer 5 only; Default layer drawn: {(mask & 1) != 0})");
            Assert.AreEqual(1 << 5, mask, "the camera draws the UI layer only");
        }

        [Test]
        public void TheStillComesTwoSecondsIntoTheTitleOnceTheWorldIsBuilt()
        {
            float s = TitleBackdrop.SettleSeconds;
            bool early = TitleBackdrop.ShouldFreeze(true, true, false, s - 0.05f, false, false);
            bool onTime = TitleBackdrop.ShouldFreeze(true, true, false, s, false, false);
            bool building = TitleBackdrop.ShouldFreeze(true, true, false, s + 5f, true, false);
            bool notIdle = TitleBackdrop.ShouldFreeze(true, false, false, s + 5f, false, false);
            Debug.Log($"[b12-x1] still at {s:0.##} s (limit 2 s, the smoke's idle window starts at title + 2 s): before {early}, at {onTime}, " +
                      $"while the world builds {building}, tour or film over the title {notIdle}");
            Assert.LessOrEqual(s, 2f, "the smoke's idle window is all still");
            Assert.IsFalse(early);
            Assert.IsTrue(onTime);
            Assert.IsFalse(building, "never a still of a half-built world");
            Assert.IsFalse(notIdle, "the attract tour and the story film keep the live scene");
            Assert.IsFalse(FirstRunWarm.WarmsWorld(true, true), "the automated warm-up never runs on a backdrop world (inside the idle window)");
            Assert.IsTrue(FirstRunWarm.WarmsWorld(true, false), "a match world is still warmed");
        }

        [Test]
        public void TheBackdropNeverSharesTheRealMatchWorld()
        {
            var backdropReq = new MatchRequest { Backdrop = true, Seed = 7, Team = TeamId.Azure, Class = ClassId.Warrior, TeamSize = GameFlow.BackdropTeamSize, Pace = GameFlow.BackdropPace };
            var matchReq = new MatchRequest { Backdrop = false, Seed = 7, Team = TeamId.Azure, Class = ClassId.Warrior, TeamSize = 16, Pace = 1 };
            bool bOnline = SimDriverBehaviour.ShowsOnlineWorld(true, backdropReq), mOnline = SimDriverBehaviour.ShowsOnlineWorld(true, matchReq);
            Debug.Log($"[b12-x1] with an online session open: backdrop takes the session world {bOnline} (limit false), a match takes it {mOnline}");
            Assert.IsFalse(bOnline, "a backdrop never shows the online match's world");
            Assert.IsTrue(mOnline);
            Assert.IsFalse(SimDriverBehaviour.ShowsOnlineWorld(false, matchReq), "no session, no session world");

            GameConfig bc = GameConfig.CreateDefault(), mc = GameConfig.CreateDefault();
            World b = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = GameFlow.BackdropTeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = bc });
            World m = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = mc });
            Debug.Log($"[b12-x1] backdrop world {b.ActorCount} actors, match world {m.ActorCount} actors, same world {ReferenceEquals(b, m)}, " +
                      $"same config {ReferenceEquals(b.Config, m.Config)} (limits false, false)");
            Assert.IsFalse(ReferenceEquals(b, m), "two worlds");
            Assert.IsFalse(ReferenceEquals(b.Config, m.Config), "two configs (the backdrop never writes the match's rules)");
            Assert.LessOrEqual(b.ActorCount, m.ActorCount, "the backdrop is no bigger than a match");
        }
    }
}

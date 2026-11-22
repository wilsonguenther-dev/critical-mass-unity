using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Player-experience wave, lane C (impact), 2026-11-22: the hit-feel numbers the owner's complaint turned into ("I can be
    /// in a player's face, swing, and I see nothing to indicate I've hit"), held without eyes. Pure functions and the
    /// CombatFeed decode only; the Editor frame strips judge the look.
    /// </summary>
    public sealed class PxImpactTuningTests
    {
        static readonly ClassId[] All = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        /// <summary>The shipped cameras: third person (GroundView Play) and the fresh-install top-down rig.</summary>
        static readonly (string name, double dist, double fov)[] Views =
        {
            ("third person", FollowCamera.ThirdDistance, FollowCamera.FovDefault),
            ("third person, widest FOV", FollowCamera.ThirdDistance, FollowCamera.FovMax),
            ("third person, narrowest FOV", FollowCamera.ThirdDistance, FollowCamera.FovMin),
            ("top-down", GameplayCamera.Distance, GameplayCamera.FovDeg),
        };

        // ---------------------------------------------------------------- 4. camera kick

        [Test]
        public void TheHitKickMeetsItsSizeTargetsInBothViews()
        {
            foreach (var v in Views)
            {
                float byMe = HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, v.dist, v.fov);
                float onMe = HitFeelTuning.KickU(HitFeelTuning.KickKind.OnMe, v.dist, v.fov);
                float kill = HitFeelTuning.KickU(HitFeelTuning.KickKind.MyKill, v.dist, v.fov);
                float death = HitFeelTuning.KickU(HitFeelTuning.KickKind.MyDeath, v.dist, v.fov);
                TestContext.Progress.WriteLine($"[px kick] {v.name}: by me {byMe:0.000} u = {HitFeelTuning.KickPx(byMe, v.dist, v.fov):0.0} px, " +
                                               $"on me {onMe:0.000} u = {HitFeelTuning.KickPx(onMe, v.dist, v.fov):0.0} px, kill {kill:0.000}, death {death:0.000} (1080p)");
                Assert.GreaterOrEqual(byMe, 0.03f, v.name + ": a hit by me moves the camera at least 0.03 u");
                Assert.GreaterOrEqual(onMe, 0.06f, v.name + ": a hit on me at least 0.06 u");
                Assert.Greater(onMe, byMe, v.name + ": being hit kicks harder than hitting");
                Assert.Greater(kill, byMe, v.name + ": my kill kicks harder than my hit");
                Assert.Greater(death, onMe, v.name + ": my death kicks hardest");
                float pxBy = HitFeelTuning.KickPx(byMe, v.dist, v.fov), pxOn = HitFeelTuning.KickPx(onMe, v.dist, v.fov);
                Assert.GreaterOrEqual(pxBy, 10.8f * 0.66f, v.name + ": readable (about 1% of a 1080 px screen)");
                Assert.LessOrEqual(pxOn, 1080f * 0.03f, v.name + ": never a yank (at most 3% of the screen)");
            }
            float third = HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, FollowCamera.ThirdDistance, FollowCamera.FovDefault);
            Assert.That(third, Is.InRange(0.04f, 0.07f), "third person: the brief's 0.04-0.06 u target band (+ rounding)");
            Assert.AreEqual(0f, HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, 4.4, 65, 0f), "HIT KICK 0 = off");
        }

        [Test]
        public void TheKickSpringsOutAndBackInAboutAHundredAndFiftyMilliseconds()
        {
            Assert.AreEqual(0f, HitFeelTuning.KickEnvelope(0), 1e-6f, "starts at rest");
            Assert.AreEqual(1f, HitFeelTuning.KickEnvelope(HitFeelTuning.KickRise), 1e-4f, "full at the rise");
            Assert.AreEqual(0f, HitFeelTuning.KickEnvelope(HitFeelTuning.KickReturn), 1e-6f, "home again");
            Assert.That(HitFeelTuning.KickReturn, Is.InRange(0.12, 0.18));
            Assert.LessOrEqual(HitFeelTuning.KickRise, 0.034, "out within 2 frames (inside the 100 ms impact window)");
            Assert.AreEqual(0f, HitFeelTuning.KickEnvelope(-0.01), "nothing before");
        }

        [Test]
        public void TheHitKickSettingDefaultsOnAndIsSeparateFromTheShakeSlider()
        {
            Assert.AreEqual(1f, GameSettings.HitKickDefault, "on at full by default");
            Assert.AreEqual(1.5f, GameSettings.HitKickMax);
            CollectionAssert.Contains(GameSettings.AllKeys, GameSettings.HitKickKey, "reset and the test backups know the key");
            Assert.AreNotEqual(GameSettings.ShakeScaleKey, GameSettings.HitKickKey);
            Assert.AreNotEqual(GameSettings.ShakeKey, GameSettings.HitKickKey);
        }

        // ---------------------------------------------------------------- 1a. flash

        [Test]
        public void TheSafeFlashIsObviousAndStaysUnderThreeFlashesASecond()
        {
            Assert.GreaterOrEqual(HitFeelTuning.FlashStrength(true, true), 0.6f, "reduce flashing ON, my hit: at least 60%");
            Assert.GreaterOrEqual(HitFeelTuning.FlashStrength(true, false), 0.6f, "reduce flashing ON, others: at least 60%");
            Assert.AreEqual(1f, HitFeelTuning.FlashStrength(false, true), "reduce flashing OFF: full white");
            foreach (bool own in new[] { true, false })
                Assert.That(HitFeelTuning.FlashSeconds(own), Is.InRange(0.1, 0.13), "100-130 ms");
            Assert.Less(HitFeelTuning.MaxFlashesPerSecond, 3.0, "WCAG 2.3.1");
            // a victim hit every sim tick for 3 s: count the flash starts in every 1 s window
            const double hz = 30;
            double last = double.NegativeInfinity;
            var starts = new System.Collections.Generic.List<double>();
            for (int k = 0; k < 90; k++)
            {
                double t = k / hz;
                if (HitFeelTuning.FlashAllowed(t - last)) { last = t; starts.Add(t); }
            }
            foreach (double s in starts)
            {
                int inWindow = 0;
                foreach (double u in starts) if (u >= s && u < s + 1.0) inWindow++;
                Assert.LessOrEqual(inWindow, 3, $"at most 3 flashes in the second from {s:0.00} s");
            }
            Assert.IsTrue(HitFeelTuning.FlashAllowed(double.PositiveInfinity), "the first hit always flashes");
        }

        // ---------------------------------------------------------------- 1b. hit-stop

        [Test]
        public void HitStopIsSixtyToOneHundredTenMillisecondsOnBothFightersAndOffUnderReduceMotion()
        {
            double[] want = { 0.06, 0.08, 0.11 };
            for (byte t = 0; t <= 2; t++)
            {
                Assert.AreEqual(want[t], HitFeelTuning.HoldWallSeconds(t, false), 1e-9);
                Assert.AreEqual(want[t], HitFeel.HoldWallSeconds(t, false), 1e-9, "HitFeel delegates to the tuning");
                Assert.AreEqual(0, HitFeelTuning.HoldWallSeconds(t, true), "reduce motion: no hit-stop");
                Assert.GreaterOrEqual(want[t] * 60, 3.0, "at least 3 frames at 60 fps");
            }
        }

        // ---------------------------------------------------------------- 1c. knock

        [Test]
        public void TheDrawnVictimTravelsEightyPercentOfTheKnockWithinAHundredMilliseconds()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            double decay = Knock.DecayPerTick(cfg), hz = cfg.SimHz;
            double t = HitFeelTuning.KnockCheckSeconds;
            // the interpolated sim body alone, one tick behind at worst
            foreach (double k in new[] { 0.19, 0.3, 0.44, 0.56, 0.75 })
            {
                double sim = k * HitFeelTuning.SimKnockShare(t * hz - 1, decay);
                double drawn = sim + HitFeelTuning.KnockLead(t, k, sim);
                TestContext.Progress.WriteLine($"[px knock] {k:0.00} u: sim alone {sim:0.000} u, drawn {drawn:0.000} u ({drawn / k:0.00}x) at {t * 1000:0} ms");
                Assert.GreaterOrEqual(drawn, HitFeelTuning.KnockShare * k - 1e-9, $"knock {k} u: at least 0.8x drawn by 100 ms");
                Assert.LessOrEqual(HitFeelTuning.KnockLead(t, k, sim), HitFeelTuning.MaxKnockLead + 1e-9, "the lead is capped");
            }
            // tiny knocks still recoil visibly
            double tiny = 0.19, tinySim = tiny * HitFeelTuning.SimKnockShare(0.07 * hz, decay);
            Assert.GreaterOrEqual(tinySim + HitFeelTuning.KnockLead(0.07, tiny, tinySim), 0.27, "a Miner-on-Warrior knock recoils ~0.3 u");
            // an overshoot and a settle, then nothing left once the sim body has arrived
            Assert.Greater(HitFeelTuning.KnockWant(HitFeelTuning.KnockFrontSeconds, 1.0), 1.0, "overshoots");
            Assert.AreEqual(1.0, HitFeelTuning.KnockWant(0.5, 1.0), 1e-6, "settles on the knock");
            Assert.AreEqual(0, HitFeelTuning.KnockLead(HitFeelTuning.KnockLeadFadeTo, 1.0, 0.0), "the lead is gone by 0.6 s even against a wall");
            Assert.AreEqual(0, HitFeelTuning.KnockLead(0.05, 0.5, 0.6), "no lead once the body is past the want");
            Assert.LessOrEqual(HitFeelTuning.MaxDrawnOffset, 0.35f, "inside the play-mode placement check (0.35 u)");
        }

        // ---------------------------------------------------------------- 1d. burst

        [Test]
        public void OwnHitBurstsAreSizedForTheCamerasAndOtherPeoplesStayQuieter()
        {
            for (byte tier = 0; tier <= 2; tier++)
            {
                float own = HitFeelTuning.BurstSize(tier, true, 10), other = HitFeelTuning.BurstSize(tier, false, 10);
                Assert.Greater(own, other, $"tier {tier}: my hits read above the crowd's");
                Assert.GreaterOrEqual(HitFeelTuning.BurstLife(tier, true), 0.22, "lasts at least 0.22 s");
            }
            Assert.GreaterOrEqual(HitFeelTuning.BurstSize(0, true, 0), 0.9f, "my light hit: at least 0.9 u");
            Assert.GreaterOrEqual(HitFeelTuning.BurstSize(1, true, 20), 1.3f, "my heavy hit: at least 1.3 u");
            Assert.GreaterOrEqual(HitFeelTuning.OwnDustPuffs, 1, "a dust kick at every tier for my hits");
            Assert.AreEqual(1f, HitFeelTuning.ReactionWeightOwn, "the stagger pose at full weight for my hits");
        }

        // ---------------------------------------------------------------- 2. the committed swing

        /// <summary>Every wind-up the classes use (the audit: 7 to 12 ticks) plus whatever the config row says.</summary>
        static int[] Windups(GameConfig cfg)
        {
            var list = new System.Collections.Generic.List<int> { 7, 9, 10, 11, 12 };
            foreach (ClassId c in All)
            {
                int w = AttackTiming.WindupTicks(cfg, c);
                if (w > 0 && !list.Contains(w)) list.Add(w);
            }
            return list.ToArray();
        }

        [Test]
        public void TheStrikePlaysOverFourToSixFramesAndEndsOnTheContactTick()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            double hz = cfg.SimHz;
            foreach (int ticks in Windups(cfg))
            {
                double w = ticks / hz, strike = AttackTiming.StrikeFor(w), frames = strike * HitFeelTuning.RenderHz;
                Assert.That(frames, Is.InRange(4.0 - 1e-9, 6.0 + 1e-9), $"W {ticks} ticks: the strike is {frames:0.0} frames");
                const double c = 0.5, e = 0.2;
                Assert.AreEqual(c, AttackTiming.ClipTime(w, c, 2, w, e), 1e-9, "contact frame on the damage tick");
                Assert.AreEqual(e, AttackTiming.ClipTime(w - strike, c, 2, w, e), 1e-9, "the wind-up extreme held until the strike");
                Assert.Less(AttackTiming.ClipTime(w - strike * 0.5, c, 2, w, e), e + (c - e) * 0.5, "ease-in: fastest on contact");
                Assert.Greater(AttackTiming.ClipTime(w + 0.05, c, 2, w, e), c, "follow-through after contact");
            }
        }

        [Test]
        public void TheSwingPoseCommitsWithinTwoFramesForMeAndThreeForEveryoneElse()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            foreach (int ticks in Windups(cfg))
            {
                double w = ticks / cfg.SimHz;
                float mine = AttackTiming.Envelope(2.0 / 60, 0.01, 1.0, w, true);
                float theirs = AttackTiming.Envelope(3.0 / 60, 0.01, 1.0, w, false);
                Assert.GreaterOrEqual(mine, HitFeelTuning.CommitAtCheck, $"W {ticks}: my pose at 2 frames is {mine:0.00}");
                Assert.GreaterOrEqual(theirs, HitFeelTuning.CommitAtCheck, $"W {ticks}: others' pose at 3 frames is {theirs:0.00}");
                Assert.AreEqual(0f, AttackTiming.Envelope(0, 0, 1.0, w, true), 1e-6f, "from nothing on the press frame (no pop before it)");
            }
        }

        [Test]
        public void AWhiffLooksDifferentAndAFeintAborts()
        {
            Assert.Greater(HitFeelTuning.WhiffRate, 1.0, "a miss swings through faster");
            Assert.Greater(HitFeelTuning.WhiffOverRotateDeg, 10, "and over-rotates visibly");
            Assert.Greater(HitFeelTuning.WhiffShape(0.08), 0.9);
            Assert.AreEqual(0, HitFeelTuning.WhiffShape(HitFeelTuning.WhiffSeconds));
            Assert.That(HitFeelTuning.FeintAbortSeconds, Is.InRange(0.06, 0.2));
        }

        // ---------------------------------------------------------------- 3. the picture matches the rule

        [Test]
        public void TheSlashAndTheWedgeReachExactlyWhereTheSimHits()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            foreach (ClassId c in All)
            {
                double range = LightWeaponView.Range(cfg, c), reach = HitFeelTuning.SimReach(cfg, c);
                Assert.AreEqual(range + HitFeelTuning.BodyWidening, reach, 1e-9, c + ": slash / wedge radius = range + body widening");
                foreach (ClassId t in All)
                    Assert.GreaterOrEqual(reach + 1e-9, range + Math.Min(cfg.Class(t).Radius, HitFeelTuning.BodyWidening), $"{c} on {t}: covers the hit centre distance");
                double half = HitFeelTuning.DrawnHalfArc(cfg, c), arc = LightWeaponView.ArcDeg(cfg, c) * 0.5 * Math.PI / 180;
                Assert.Greater(half, arc, c + ": the drawn arc includes the body widening");
                TestContext.Progress.WriteLine($"[px reach] {c}: range {range:0.00} -> drawn reach {reach:0.00} u, half arc {arc * 180 / Math.PI:0.0} -> {half * 180 / Math.PI:0.0} deg");
            }
        }

        static World NewWorld(ClassId cls) => World.CreateWorld(new WorldOptions
        {
            Seed = 7, TeamSize = 4, HumanTeam = TeamId.Azure, HumanClass = cls, Config = GameConfig.CreateDefault(),
        });

        static int Enemy(World w)
        {
            for (int i = 1; i < w.ActorCount; i++) if (w.Actors[i].Team != w.Actors[0].Team && w.Actors[i].Alive) return i;
            return -1;
        }

        [Test]
        public void TheBodyOnlyStepsAtAnEnemyTheSimWouldHitAndFacesTheLiveSwing()
        {
            World w = NewWorld(ClassId.Warrior);
            w.Tick = 200;
            int e = Enemy(w);
            Assert.GreaterOrEqual(e, 0);
            Actor a = w.Actors[0];
            a.SwingTick = w.Tick - 2;
            a.SwingWindup = 12;
            a.SwingPending = true;
            a.SwingFacing = 0.3;
            a.Facing = 0.2;
            double range = LightWeaponView.Range(w.Config, ClassId.Warrior), r = w.Config.Class(w.Actors[e].Cls).Radius;
            w.X[e] = w.X[0] + range + r - 0.05;
            w.Z[e] = w.Z[0] + 0.3 * 0.1;
            Assert.AreEqual(e, HitFeel.SwingTarget(w, 0), "inside the sim's hit shape: the target");
            w.X[e] = w.X[0] + range + r + 0.1;
            Assert.AreEqual(-1, HitFeel.SwingTarget(w, 0), "just outside the sim reach: no view target (no lie)");
            Assert.AreEqual(0.3, HitFeel.Facing(w, 0, 0), 1e-9, "the body faces the LIVE SwingFacing");
            a.SwingFacing = 0.25;   // lane S1: the swing tracks the aim during the wind-up
            Assert.AreEqual(0.25, HitFeel.Facing(w, 0, 0), 1e-9, "and follows it when it moves");
        }

        // ---------------------------------------------------------------- 4b/c. fight framing and the hatch pull

        [Test]
        public void FightFramingEasesInOverPointFourSecondsAndOutOverOneSecond()
        {
            double f = 0;
            for (int k = 0; k < 24; k++) f = HitFeelTuning.StepFight(f, true, 1.0 / 60);
            Assert.AreEqual(1.0, f, 1e-6, "in by 0.4 s");
            for (int k = 0; k < 30; k++) f = HitFeelTuning.StepFight(f, false, 1.0 / 60);
            Assert.That(f, Is.InRange(0.45, 0.55), "half-way home after 0.5 s");
            for (int k = 0; k < 30; k++) f = HitFeelTuning.StepFight(f, false, 1.0 / 60);
            Assert.AreEqual(0.0, f, 1e-6, "home by 1 s");
            Assert.That(HitFeelTuning.FightDistanceShare, Is.InRange(0.1, 0.15));
            Assert.AreEqual(3.0, HitFeelTuning.FightFovAdd, 1e-9);
        }

        [Test]
        public void TheHatchPullShrinksWhileIAmInAFight()
        {
            var calm = new CameraDirector();
            var fight = new CameraDirector();
            var input = new CameraDirectorInput { Dt = 1.0 / 60, SimTime = 1.0, HumanX = 0, HumanZ = 0, Hatched = true, HatchSimTime = 0, HatchX = 20, HatchZ = 0 };
            calm.Step(input);
            input.InFight = true;
            fight.Step(input);
            Assert.Greater(calm.OffsetX, 1.0, "out of a fight the view leans toward the hatch");
            Assert.LessOrEqual(Math.Abs(fight.OffsetX), Math.Abs(calm.OffsetX) * HitFeelTuning.HatchShareInFight + 1e-9, "in a fight it is only a hint");
        }

        [Test]
        public void AnEnemyCloseInFrontCountsAsAFight()
        {
            World w = NewWorld(ClassId.Warrior);
            int e = Enemy(w);
            w.Actors[0].Facing = 0;
            w.X[e] = w.X[0] + 3;
            w.Z[e] = w.Z[0];
            Assert.IsTrue(FightState.EnemyInFront(w, 0, HitFeelTuning.FightRange, HitFeelTuning.FightConeDeg), "3 u straight ahead");
            w.X[e] = w.X[0] - 3;
            bool behindOnly = true;
            for (int j = 1; j < w.ActorCount; j++)
                if (j != e && w.Actors[j].Team != w.Actors[0].Team) { w.X[j] = w.X[0] - 30; }
            Assert.AreEqual(!behindOnly, FightState.EnemyInFront(w, 0, HitFeelTuning.FightRange, HitFeelTuning.FightConeDeg), "behind me is not 'in front'");
        }

        // ---------------------------------------------------------------- critical hits (lead's call 12)

        [Test]
        public void ACriticalHitReadsClearlyAboveAHeavyHit()
        {
            foreach (var v in Views)
            {
                float by = HitFeelTuning.KickU(HitFeelTuning.KickKind.ByMe, v.dist, v.fov), cby = HitFeelTuning.KickU(HitFeelTuning.KickKind.CritByMe, v.dist, v.fov);
                float on = HitFeelTuning.KickU(HitFeelTuning.KickKind.OnMe, v.dist, v.fov), con = HitFeelTuning.KickU(HitFeelTuning.KickKind.CritOnMe, v.dist, v.fov);
                Assert.AreEqual(by * HitFeelTuning.CritKickMul, cby, 1e-4f, v.name + ": my critical kicks 1.6x my hit");
                Assert.AreEqual(on * HitFeelTuning.CritKickMul, con, 1e-4f, v.name + ": a critical on me kicks 1.6x a hit on me");
                Assert.LessOrEqual(HitFeelTuning.KickPx(con, v.dist, v.fov), 1080f * 0.04f, v.name + ": still not a yank");
            }
            Assert.That(HitFeelTuning.HoldCritWallSeconds(false), Is.InRange(0.13, 0.15), "130-150 ms hit-stop");
            Assert.AreEqual(0, HitFeelTuning.HoldCritWallSeconds(true), "none under reduce motion");
            Assert.Greater(HitFeelTuning.HoldCritWallSeconds(false), HitFeelTuning.HoldWallSeconds(2, false));
            Assert.Greater(HitFeelTuning.BurstCritStage2, HitFeelTuning.BurstSize(2, true, 60), "a bigger burst than the top plain tier");
            Assert.Greater(HitFeelTuning.BurstCritStage2Delay, 0.0, "two stages");
            Assert.AreEqual(2f, HitFeelTuning.CritFovPunchDeg, 1e-6f);
            Assert.AreEqual(1f, HitFeelTuning.FovPunchEnvelope(HitFeelTuning.KickRise), 1e-4f);
            Assert.AreEqual(0f, HitFeelTuning.FovPunchEnvelope(HitFeelTuning.CritFovPunchSeconds), 1e-6f, "the lens is home again");
            Assert.LessOrEqual(HitFeelTuning.CritMinVisualKnock, HitFeelTuning.MaxDrawnOffset, "the bigger recoil still fits the placement check");
            Assert.Greater(HitFeelTuning.KnockWant(0.07, 0.1, HitFeelTuning.CritMinVisualKnock), HitFeelTuning.KnockWant(0.07, 0.1), "a critical recoils further");
        }

        // ---------------------------------------------------------------- decode: whiff vs contact, knock

        sealed class Sink : IFxSink
        {
            public readonly System.Collections.Generic.List<FxEvent> Got = new System.Collections.Generic.List<FxEvent>();
            public void OnFx(ref FxEvent e, World w) => Got.Add(e);
        }

        [Test]
        public void AWallSwingIsAContactNotAWhiffAndTheKnockIsRecorded()
        {
            World w = NewWorld(ClassId.Warrior);
            w.Tick = 50;
            var feed = new CombatFeed(World.ActorCapacity(w.Config));
            var sink = new Sink();
            feed.OnTick(w, sink);
            sink.Got.Clear();
            // a whiff: AttackContact with nothing hit
            World.PushEvent(w, SimEventType.AttackContact, 0, -1, w.X[0] + 1, w.Z[0], 0);
            feed.OnTick(w, sink);
            FxEvent c = sink.Got.Find(x => x.Kind == FxKind.Contact);
            Assert.AreEqual(FxKind.Contact, c.Kind);
            Assert.AreEqual(0, c.Value, "nothing hit: a whiff");
            Assert.AreEqual(0, feed.ContactHits[0]);
            // a wall swing: AttackContact 0 bodies, then (same tick, later in the ring) a StructureHit by the same attacker
            w.Tick = 51;
            sink.Got.Clear();
            World.PushEvent(w, SimEventType.AttackContact, 0, -1, w.X[0] + 1, w.Z[0], 0);
            World.PushEvent(w, SimEventType.StructureHit, 0, PlayerReasons.StructureBarricade, w.X[0] + 1.5, w.Z[0], 24, null, DamageCause.None, 0.6);
            feed.OnTick(w, sink);
            c = sink.Got.Find(x => x.Kind == FxKind.Contact);
            Assert.AreEqual(1, c.Value, "a structure hit the same tick makes it a contact");
            Assert.IsTrue(feed.StructureEventsSeen);
            FxEvent sh = sink.Got.Find(x => x.Kind == FxKind.StructureHit);
            Assert.AreEqual(0.6, sh.Extra, 1e-9, "health share left is carried");
            // a knock: recorded for the drawn lead
            int e = Enemy(w);
            w.Tick = 52;
            Knock.Push(w, e, 0, 1, 0, 0.56, 0, 0, DamageCause.Melee);
            feed.OnTick(w, sink);
            Assert.AreEqual(52, feed.KnockTick[e]);
            Assert.AreEqual(0.56, feed.KnockU[e], 1e-9);
        }
    }
}

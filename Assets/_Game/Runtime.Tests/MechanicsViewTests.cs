using System;
using CriticalMass.Runtime.Hud.Mechanics;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>feat/new-mechanics-view: the event-to-visual table and every indicator's logic (pure; no scene, no Unity run).</summary>
    public sealed class MechanicsViewTests
    {
        static World NewWorld(ClassId cls) => World.CreateWorld(new WorldOptions
        {
            Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = GameConfig.CreateDefault(),
        });

        static int Enemy(World w)
        {
            for (int i = 1; i < w.ActorCount; i++) if (w.Actors[i].Team != w.Actors[0].Team) return i;
            return -1;
        }

        // ---------------------------------------------------------------- event-to-visual table

        [Test]
        public void EveryClassEventOfPR37And41MapsToAVisualOrAnExplicitNone()
        {
            foreach (string name in MechEvents.ClassEventNames)
            {
                int code = MechEvents.Code(name);
                Assert.GreaterOrEqual(code, 0, name + " is missing from SimEventType on this base");
                Assert.AreNotEqual(MechVisual.Unmapped, MechEvents.VisualFor(code, 0), name);
                Assert.AreNotEqual(MechVisual.Unmapped, MechEvents.VisualFor(code, Healer.KindPulse), name);
            }
        }

        [Test]
        public void EveryEnumMemberFromMinePlacedToPurifyIsMapped()
        {
            int lo = (int)SimEventType.MinePlaced, hi = (int)SimEventType.Purify;
            Assert.Less(lo, hi);
            for (int c = lo; c <= hi; c++)
                Assert.AreNotEqual(MechVisual.Unmapped, MechEvents.VisualFor(c, 0), ((SimEventType)c).ToString());
        }

        [Test]
        public void HealCastKindPicksBeamOrPulseAndExplosionIsDrawnElsewhere()
        {
            Assert.AreEqual(MechVisual.HealBeam, MechEvents.VisualFor(SimEventType.HealCast, Healer.KindMend));
            Assert.AreEqual(MechVisual.HealPulse, MechEvents.VisualFor(SimEventType.HealCast, Healer.KindPulse));
            Assert.AreEqual(MechVisual.None, MechEvents.VisualFor(SimEventType.Explosion, Demolitions.ExplosionTnt));
            Assert.AreEqual(MechVisual.None, MechEvents.VisualFor(SimEventType.Terraform, 0));
            Assert.AreEqual(MechVisual.PieceBlink, MechEvents.VisualFor(SimEventType.PieceCrumbling, 0));
        }

        [Test]
        public void PhysicsEventsArePickedUpByNameWhenPresentAndAbsentOnesAreMinusOne()
        {
            Assert.AreEqual(-1, MechEvents.Code("NoSuchEventAnywhere"));
            Assert.AreEqual(MechVisual.Unmapped, MechEvents.VisualFor(-1, 0));
            foreach (string name in MechEvents.PhysicsEventNames)
            {
                int code = MechEvents.Code(name);
                if (code < 0) continue;   // not on this base: guarded
                Assert.AreNotEqual(MechVisual.Unmapped, MechEvents.VisualFor(code, 0), name);
            }
        }

        [Test]
        public void PhysicsFieldSwitchIsOffOnThisBaseAndPR49NamesMapWhenPresent()
        {
            bool push = MechPhysics.Push(new SimEvent(), out double dx, out double dz, out double impulse);
            Assert.IsFalse(push, "an event with no direction never reports a push");
            int knock = MechEvents.Code("Knock"), pop = MechEvents.Code("MeatPop"), stun = MechEvents.Code("HitStun");
            if (knock >= 0) Assert.AreEqual(MechVisual.KnockStreak, MechEvents.VisualFor(knock, 0));
            if (pop >= 0) Assert.AreEqual(MechVisual.DropPop, MechEvents.VisualFor(pop, 0));
            if (stun >= 0) Assert.AreEqual(MechVisual.StunBadge, MechEvents.VisualFor(stun, 0));
        }

        // ---------------------------------------------------------------- ladders and bridges

        [Test]
        public void PieceTimerCountsDownWarnsAndBlinksUnlessReduceFlashing()
        {
            Assert.AreEqual(1f, PieceTimer.Left01(100, 0, 100, 1900), 1e-5f);
            Assert.AreEqual(0.5f, PieceTimer.Left01(1000, 0, 100, 1900), 1e-5f);
            Assert.AreEqual(0f, PieceTimer.Left01(1900, 0.5, 100, 1900), 1e-5f);
            Assert.IsFalse(PieceTimer.Warning(1809, 1900, 90));
            Assert.IsTrue(PieceTimer.Warning(1810, 1900, 90));
            Assert.AreEqual(3, PieceTimer.SecondsLeft(1810, 1900, 30));
            Assert.AreEqual(1f, PieceTimer.Blink(0.3, 1.2, true));
            Assert.AreEqual(1f, PieceTimer.Blink(0.1, 1.4, false));      // first half of a 2 Hz beat
            Assert.Less(PieceTimer.Blink(0.3, 1.4, false), 1f);           // second half
            Assert.Less(PieceTimer.Blink(0.15, 0.8, false), 1f);          // 4 Hz in the last second
        }

        // ---------------------------------------------------------------- TNT

        [Test]
        public void TntArcRisesAndFallsAndTheShadowShrinksWithHeight()
        {
            Assert.AreEqual(0f, TntArc.Progress(10, 10, 22));
            Assert.AreEqual(0.5f, TntArc.Progress(16, 10, 22), 1e-5f);
            Assert.AreEqual(1f, TntArc.Progress(40, 10, 22));
            Assert.AreEqual(0f, TntArc.Height(0f, 5f), 1e-5f);
            Assert.AreEqual(0f, TntArc.Height(1f, 5f), 1e-5f);
            Assert.AreEqual(TntArc.Apex(5f), TntArc.Height(0.5f, 5f), 1e-4f);
            Assert.LessOrEqual(TntArc.Apex(100f), TntArc.MaxApex);
            Assert.Greater(TntArc.ShadowScale(0f), TntArc.ShadowScale(2f));
            Assert.AreEqual(1f, TntArc.FuseLeft01(22, 22, 67), 1e-5f);
            Assert.AreEqual(0f, TntArc.FuseLeft01(67, 22, 67), 1e-5f);
        }

        // ---------------------------------------------------------------- mines

        [Test]
        public void MinesShowTheMoundToEveryoneAndThePlateAndRingToTheOwnersTeamOnly()
        {
            Assert.AreEqual(1f, MineSight.MoundAlpha(TeamId.Azure, TeamId.Azure));
            float enemy = MineSight.MoundAlpha(TeamId.Azure, TeamId.Crimson);
            Assert.Greater(enemy, 0.5f);
            Assert.Less(enemy, 1f);
            Assert.IsTrue(MineSight.ShowRing(TeamId.Azure, TeamId.Azure, true));
            Assert.IsFalse(MineSight.ShowRing(TeamId.Azure, TeamId.Azure, false));
            Assert.IsFalse(MineSight.ShowRing(TeamId.Azure, TeamId.Crimson, true));
            Assert.IsFalse(MineSight.ShowPlate(TeamId.Crimson, TeamId.Azure, true));
            Assert.IsTrue(MineSight.OnMap(TeamId.Crimson, TeamId.Crimson));
            Assert.IsFalse(MineSight.OnMap(TeamId.Crimson, TeamId.Azure));
        }

        // ---------------------------------------------------------------- stun, knock-down, throw

        [Test]
        public void KnockDownFallsHoldsAndGetsUpAndShortStunsOnlyStagger()
        {
            Assert.AreEqual(0f, KnockPose.Tilt01(-0.1, 1.0));
            Assert.AreEqual(0f, KnockPose.Tilt01(1.0, 1.0));
            Assert.AreEqual(1f, KnockPose.Tilt01(0.5, 1.0));
            Assert.Less(KnockPose.Tilt01(0.05, 1.0), 1f);
            Assert.Less(KnockPose.Tilt01(0.95, 1.0), 1f);
            Assert.AreEqual(KnockPose.DownDeg, KnockPose.TiltDeg(0.5, 1.0), 1e-4f);
            Assert.LessOrEqual(KnockPose.TiltDeg(0.1, 0.3), KnockPose.StaggerDeg + 1e-4f);
            Assert.AreEqual(0f, KnockPose.Throw01(-0.01));
            Assert.AreEqual(1f, KnockPose.Throw01(KnockPose.ThrowSeconds * 0.35), 1e-4f);
            Assert.AreEqual(0f, KnockPose.Throw01(KnockPose.ThrowSeconds));
        }

        // ---------------------------------------------------------------- damage direction

        [Test]
        public void DamageChevronSitsOnTheScreenEdgeTowardTheSourceAndFades()
        {
            Assert.IsFalse(DamageDirection.OffScreen(new Vector3(0.5f, 0.5f, 10f)));
            Assert.IsTrue(DamageDirection.OffScreen(new Vector3(1.4f, 0.5f, 10f)));
            Assert.IsTrue(DamageDirection.OffScreen(new Vector3(0.5f, 0.5f, -3f)));
            Vector2 right = DamageDirection.ScreenDir(new Vector3(2f, 0.5f, 10f));
            Assert.Greater(right.x, 0.99f);
            Vector2 behind = DamageDirection.ScreenDir(new Vector3(0.9f, 0.5f, -5f));
            Assert.Less(behind.x, 0f, "a source behind the camera is mirrored");
            Vector2 half = new Vector2(960f, 540f);
            Vector2 p = DamageDirection.EdgePoint(Vector2.right, half, 40f);
            Assert.AreEqual(920f, p.x, 1e-3f);
            Vector2 q = DamageDirection.EdgePoint(new Vector2(1f, 1f).normalized, half, 40f);
            Assert.AreEqual(500f, q.y, 1e-2f);
            Assert.AreEqual(0f, DamageDirection.AngleDeg(Vector2.up), 1e-3f);
            Assert.AreEqual(-90f, DamageDirection.AngleDeg(Vector2.right), 1e-3f);
            Assert.AreEqual(1f, DamageDirection.Alpha(0.3f));
            Assert.AreEqual(0f, DamageDirection.Alpha(DamageDirection.LifeSeconds));
            Assert.Greater(DamageDirection.Scale(50), DamageDirection.Scale(10));
        }

        // ---------------------------------------------------------------- "you were hit by"

        [Test]
        public void RecapNamesTheBlowFromTheDeathCause()
        {
            Assert.AreEqual(DemolitionsNames.Mine, HitRecapWords.Blow(DamageCause.Explosion, ClassId.Demolitions, true, Demolitions.ExplosionMine));
            Assert.AreEqual(DemolitionsNames.Tnt, HitRecapWords.Blow(DamageCause.Explosion, ClassId.Demolitions, true, Demolitions.ExplosionTnt));
            Assert.AreEqual("KAIJU SLAM", HitRecapWords.Blow(DamageCause.KaijuSlam, ClassId.Warrior, false, 0));
            Assert.AreEqual("SWORD", HitRecapWords.Blow(DamageCause.Melee, ClassId.Warrior, true, 0));
            Assert.AreEqual("THE KAIJU", HitRecapWords.Who(DamageCause.KaijuMelee, Damage.KaijuSource, TeamId.Azure, ClassId.Warrior, HitRecapWords.ClassNames));
            Assert.AreEqual("CRIMSON " + HealerNames.ClassName, HitRecapWords.Who(DamageCause.Melee, 20, TeamId.Crimson, ClassId.Healer, HitRecapWords.ClassNames));
            var h = new MechFeed.LocalHit { Attacker = 20, Team = TeamId.Crimson, Cls = ClassId.Demolitions, Cause = DamageCause.Explosion, BlastKind = Demolitions.ExplosionTnt, Dmg = 55, Fatal = true };
            string line = MechanicsHud.RecapLine(h);
            StringAssert.StartsWith(DemolitionsNames.Tnt, line);
            StringAssert.Contains("CRIMSON " + DemolitionsNames.ClassName, line);
            StringAssert.EndsWith("55  KO", line);
        }

        // ---------------------------------------------------------------- slam falloff and jump dodge

        [Test]
        public void SlamFalloffSafeEdgeAndDodge()
        {
            Assert.AreEqual(1f, SlamRead.Falloff(0, 5), 1e-5f);
            Assert.AreEqual(0.5f, SlamRead.Falloff(5, 5), 1e-5f);
            Assert.IsTrue(SlamRead.Inside(1, 0, 0, 0, 5));
            Assert.IsFalse(SlamRead.Inside(6, 0, 0, 0, 5));
            Assert.AreEqual(4.0, SlamRead.ToSafeEdge(1, 0, 0, 0, 5), 1e-9);
            Assert.AreEqual(0.0, SlamRead.ToSafeEdge(7, 0, 0, 0, 5), 1e-9);
            Assert.IsTrue(SlamRead.Dodged(true, true, false));
            Assert.IsFalse(SlamRead.Dodged(true, false, false));
            Assert.IsFalse(SlamRead.Dodged(true, true, true));
            Assert.IsFalse(SlamRead.Dodged(false, true, false));
        }

        // ---------------------------------------------------------------- emotes, cooldown labels, hit-stop tiers

        [Test]
        public void EmoteSectorsEnvelopeAndCooldownLabels()
        {
            Assert.AreEqual(-1, Emotes.Sector(new Vector2(3f, 3f), 26f));
            Assert.AreEqual(0, Emotes.Sector(new Vector2(0f, 80f), 26f));
            Assert.AreEqual(1, Emotes.Sector(new Vector2(80f, 10f), 26f));
            Assert.AreEqual(2, Emotes.Sector(new Vector2(0f, -80f), 26f));
            Assert.AreEqual(3, Emotes.Sector(new Vector2(-80f, 0f), 26f));
            Assert.AreEqual(Emotes.Count, Emotes.Names.Length);
            Assert.AreEqual(Emotes.Count, Emotes.Bubble.Length);
            Assert.AreEqual(1f, Emotes.Envelope(0.8f));
            Assert.AreEqual(0f, Emotes.Envelope(Emotes.Seconds));
            Assert.AreEqual(string.Empty, MechanicsHud.CdLabel(0f));
            Assert.AreEqual("1.3", MechanicsHud.CdLabel(1.25f));
            Assert.AreEqual("30.0", MechanicsHud.CdLabel(30f));
        }

        [Test]
        public void HitStopHoldsGrowWithTheHitTier()
        {
            var go = new GameObject("hold test");
            try
            {
                CombatFx fx = go.AddComponent<CombatFx>();
                Assert.Less(fx.HoldFor(0), fx.HoldFor(1));
                Assert.Less(fx.HoldFor(1), fx.HoldFor(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- class kit (real worlds)

        [Test]
        public void ClassKitRowsForTheThreeNewClasses()
        {
            string[] numbers = { "BANK 0", "BANK 1", "BANK 2", "BANK 3" };
            var row = new KitRow();
            World d = NewWorld(ClassId.Demolitions);
            Assert.AreEqual(2, ClassKit.RowCount(ClassId.Demolitions));
            Assert.IsTrue(ClassKit.Read(d, 0, 0, ref row, numbers));
            Assert.AreEqual(DemolitionsNames.Mine, row.Name);
            Assert.AreEqual((int)d.Config.Abilities.DemolitionsMine.MaxActivePerOwner, row.MaxCharges);
            Assert.AreEqual(row.MaxCharges, row.Charges);
            Assert.IsTrue(ClassKit.Read(d, 0, 1, ref row, numbers));
            Assert.AreEqual(DemolitionsNames.Tnt, row.Name);
            Assert.AreEqual("DigPlace", row.Action);

            World b = NewWorld(ClassId.Builder);
            Assert.IsTrue(ClassKit.Read(b, 0, 0, ref row, numbers));
            Assert.AreEqual(BuilderNames.Build, row.Name);
            Assert.AreEqual("BANK " + BuilderBank.Whole(b, TeamId.Azure), row.Note);
            Assert.IsTrue(BuilderBank.TeamHasBuilder(b, TeamId.Azure));
            Assert.IsTrue(ClassKit.Read(b, 0, 1, ref row, numbers));
            Assert.AreEqual("Interact", row.Action);

            World h = NewWorld(ClassId.Healer);
            Assert.IsTrue(ClassKit.Read(h, 0, 1, ref row, numbers));
            Assert.AreEqual(HealerNames.Purify, row.Name);
            Assert.IsFalse(row.Usable, "the Healer spawns away from the egg");
            Assert.AreEqual("GO TO THE EGG", row.Note);

            World war = NewWorld(ClassId.Warrior);
            Assert.AreEqual(0, ClassKit.RowCount(ClassId.Warrior));
            Assert.IsFalse(ClassKit.Read(war, 0, 0, ref row, numbers));
        }

        // ---------------------------------------------------------------- the feed (event reader)

        [Test]
        public void FeedRecordsLocalHitsGlintsAndPointsBlastHitsAtTheBlast()
        {
            World w = NewWorld(ClassId.Warrior);
            var f = new MechFeed { LocalActor = 0, Pace = 2 };
            f.Read(w);
            int e = Enemy(w);
            World.PushEvent(w, SimEventType.Hit, 0, e, w.X[0], w.Z[0], 12, null, DamageCause.Melee);
            World.PushEvent(w, SimEventType.AttackStart, 0, -1, w.X[0] + 1, w.Z[0], 0);
            f.Read(w);
            Assert.AreEqual(1, f.HitCount);
            Assert.AreEqual(w.X[e], f.HitRing[0].SrcX, 1e-9);
            Assert.AreEqual(DamageCause.Melee, f.HitRing[0].Cause);
            bool glint = false;
            for (int i = 0; i < MechFeed.Glints; i++) if (f.GlintRing[i].Actor == 0 && f.GlintRing[i].T > -1) glint = true;
            Assert.IsTrue(glint);

            World.PushEvent(w, SimEventType.Hit, 0, e, w.X[0], w.Z[0], 55, null, DamageCause.Explosion);
            World.PushEvent(w, SimEventType.Explosion, e, Demolitions.ExplosionTnt, 7.5, 9.5, 1, null, DamageCause.Explosion, 2);
            f.Read(w);
            Assert.AreEqual(2, f.HitCount);
            Assert.AreEqual(7.5, f.HitRing[1].SrcX, 1e-9);
            Assert.AreEqual(Demolitions.ExplosionTnt, f.HitRing[1].BlastKind);
        }

        [Test]
        public void FeedMarksADodgeOnlyForAnAirborneUnhurtPlayerInsideTheSlam()
        {
            World w = NewWorld(ClassId.Warrior);
            var f = new MechFeed { LocalActor = 0, Pace = 2 };
            f.Read(w);
            w.Actors[0].AirLen = 0;
            World.PushEvent(w, SimEventType.KaijuSlam, -1, 0, w.X[0], w.Z[0], 5);
            f.Read(w);
            Assert.AreEqual(0, f.Dodges, "on the ground: no dodge");

            w.Actors[0].AirLen = 18;
            World.PushEvent(w, SimEventType.KaijuSlam, -1, 0, w.X[0], w.Z[0], 5);
            World.PushEvent(w, SimEventType.Hit, 0, Damage.KaijuSource, w.X[0], w.Z[0], 45, null, DamageCause.KaijuSlam);
            f.Read(w);
            Assert.AreEqual(0, f.Dodges, "hurt by the slam: no dodge");

            w.Tick += 30;   // a later tick (MarkDodge ignores repeats within 0.05 s)
            World.PushEvent(w, SimEventType.KaijuSlam, -1, 0, w.X[0], w.Z[0], 5);
            f.Read(w);
            Assert.AreEqual(1, f.Dodges);
            w.Actors[0].AirLen = 0;
        }
    }
}

using System;
using CriticalMass.Net;
using CriticalMass.Net.Replay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b16 CANCEL (test feature; design advisor 2026-11-21: "it should cancel the attack animation and allows for feints. also
    /// cancelling the attack animation after a hit lands would allow people to potentially attack a little bit faster").
    /// Rules in Sim/CombatFeel.cs TakeCancel, numbers in CombatFeelConfig section E. One test per rule, the wire and replay
    /// bits, the switch-off, and the Classic / PhysicsOnly profiles untouched.
    /// </summary>
    [TestFixture]
    public sealed class CancelButtonTests
    {
        static InputFrame Frame(World w, int me, int foe, bool attack, bool cancel)
        {
            InputFrame f = SimInput.EmptyInput();
            f.AimX = w.X[foe];
            f.AimZ = w.Z[foe];
            f.Attack = attack;
            f.Cancel = cancel;
            return f;
        }

        static int Feints(World w) => w.Depth.Feel.Feints;

        /// <summary>Starts a Warrior swing at a passive Farmer (in reach, 1.7 u away, unless near is false); returns the press tick.</summary>
        static int StartSwing(World w, bool near = true)
        {
            int foe = CombatFeelTests.Foe(w);
            if (near)
            {
                CombatFeelTests.Put(w, foe, w.X[0] + 1.7, w.Z[0]);
                SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            }
            CombatFeelTests.Tick(w, Frame(w, 0, foe, true, false), null);
            Assert.IsTrue(w.Actors[0].SwingPending, "the press started a wind-up");
            return w.Actors[0].SwingTick;
        }

        [Test]
        public void AFeintDropsTheWindupTheSameTickWithNoHitAndMostOfTheCooldownBack()
        {
            World w = CombatFeelTests.Arena(ClassId.Warrior, ClassId.Farmer);
            int foe = CombatFeelTests.Foe(w);
            CombatFeelConfig f = w.Config.Depth.CombatFeel;
            Assert.IsTrue(f.CancelEnabled, "the cancel is on by default in Full");
            StartSwing(w);
            for (int k = 0; k < 3; k++) CombatFeelTests.Tick(w, Frame(w, 0, foe, false, false), null);
            double cdBefore = w.Actors[0].AttackCd;
            int head = w.EventHead;
            CombatFeelTests.Tick(w, Frame(w, 0, foe, false, true), null);
            Actor a = w.Actors[0];
            Assert.IsFalse(a.SwingPending, "the wind-up is gone the tick the cancel is read (the view's cue follows SwingPending)");
            Assert.AreEqual(1, Feints(w));
            bool feintEvent = false;
            for (int e = head; e < w.EventHead; e++)
            {
                SimEvent ev = w.Events[e % World.EventCapacity];
                if (ev != null && ev.Type == SimEventType.Feint && ev.A == 0 && ev.Value == 1) feintEvent = true;
            }
            Assert.IsTrue(feintEvent, "a Feint event (Value 1) for the view's flick and sound");
            double dt = 1.0 / w.Config.SimHz;
            double expect = Math.Max((cdBefore - dt) * (1 - f.FeintRefund), CombatFeel.Ticks(w, f.FeintRecoverySeconds) / w.Config.SimHz);
            Assert.AreEqual(expect, a.AttackCd, 1.5 * dt, "70% of the cooldown left comes back, never under the 0.10 s recovery");
            double hp = w.Actors[foe].Hp;
            for (int k = 0; k < 30; k++) CombatFeelTests.Tick(w, Frame(w, 0, foe, false, false), null);
            Assert.AreEqual(hp, w.Actors[foe].Hp, "a feinted swing never lands");
            Console.WriteLine("{\"what\":\"b16 feint\",\"cdBefore\":" + cdBefore.ToString("0.###") + ",\"cdAfter\":" + expect.ToString("0.###") + "}");
        }

        [Test]
        public void AFeintHoldsTheSwingAndTheDodgeForItsRecoveryAndAllowsNoSecondFeintInsideTheLockout()
        {
            World w = CombatFeelTests.Arena(ClassId.Warrior, ClassId.Farmer);
            int foe = CombatFeelTests.Foe(w);
            CombatFeelConfig f = w.Config.Depth.CombatFeel;
            StartSwing(w);
            CombatFeelTests.Tick(w, Frame(w, 0, foe, false, true), null);
            Assert.IsTrue(CombatFeel.FeintRecovering(w, 0), "in the feint's fixed recovery");
            Assert.IsFalse(Dodge.CanStart(w, 0), "no dodge out of the feint's recovery");
            // swing again as soon as allowed, then try a second feint inside the 0.5 s lockout
            int guard = 0;
            while (!w.Actors[0].SwingPending && guard++ < 60) CombatFeelTests.Tick(w, Frame(w, 0, foe, true, false), null);
            Assert.IsTrue(w.Actors[0].SwingPending, "a new swing after the short recovery");
            int since = w.Tick - (w.Depth.Feel.FeintLockUntil[0] - CombatFeel.Ticks(w, f.FeintLockoutSeconds));
            Assert.Less(since, CombatFeel.Ticks(w, f.FeintLockoutSeconds), "the new swing started inside the lockout");
            CombatFeelTests.Tick(w, Frame(w, 0, foe, false, true), null);
            Assert.AreEqual(1, Feints(w), "no second feint inside FeintLockoutSeconds");
            Assert.IsTrue(w.Actors[0].SwingPending || w.Tick >= w.Actors[0].SwingTick + w.Actors[0].SwingWindup, "the swing carries on");
        }

        [Test]
        public void AHitCancelCutsTheRecoveryOnlyAfterALandedHitAndOncePerAttack()
        {
            World w = CombatFeelTests.Arena(ClassId.Warrior, ClassId.Farmer);
            int foe = CombatFeelTests.Foe(w);
            CombatFeelConfig f = w.Config.Depth.CombatFeel;
            StartSwing(w);
            Actor a = w.Actors[0];
            int contact = a.SwingTick + a.SwingWindup;
            double hp = w.Actors[foe].Hp;
            while (w.Tick <= contact) CombatFeelTests.Tick(w, Frame(w, 0, foe, false, false), null);
            Assert.Less(w.Actors[foe].Hp, hp, "the swing landed");
            int remaining = contact + a.SwingRecovery - w.Tick;
            Assert.Greater(remaining, 0, "still recovering");
            double cd = a.AttackCd;
            CombatFeelTests.Tick(w, Frame(w, 0, foe, false, true), null);
            int cut = (int)Math.Floor((remaining - 0) * f.HitCancelShare + 0.5);
            Assert.AreEqual(1, w.Depth.Feel.HitCancels, "one hit-cancel");
            Assert.AreEqual(0, Feints(w), "not a feint");
            Assert.AreEqual(cd - 1.0 / w.Config.SimHz - cut / w.Config.SimHz, a.AttackCd, 1.5 / w.Config.SimHz, "the cooldown shrank by the cut recovery");
            double cd2 = a.AttackCd;
            CombatFeelTests.Tick(w, Frame(w, 0, foe, false, true), null);
            Assert.AreEqual(1, w.Depth.Feel.HitCancels, "one cancel per attack");
            Assert.AreEqual(cd2 - 1.0 / w.Config.SimHz, a.AttackCd, 1e-6, "the second press changed nothing");
        }

        [Test]
        public void AWhiffCannotBeHitCancelled()
        {
            World w = CombatFeelTests.Arena(ClassId.Warrior, ClassId.Farmer);
            int foe = CombatFeelTests.Foe(w);
            CombatFeelTests.Put(w, foe, CombatFeelTests.X0 + 6, CombatFeelTests.Z0);   // out of reach: the swing whiffs
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            StartSwing(w, false);
            Actor a = w.Actors[0];
            int contact = a.SwingTick + a.SwingWindup;
            while (w.Tick <= contact) CombatFeelTests.Tick(w, Frame(w, 0, foe, false, false), null);
            double cd = a.AttackCd;
            CombatFeelTests.Tick(w, Frame(w, 0, foe, false, true), null);
            Assert.AreEqual(0, w.Depth.Feel.HitCancels + Feints(w), "a whiff has no cancel");
            Assert.AreEqual(cd - 1.0 / w.Config.SimHz, a.AttackCd, 1e-6);
        }

        [Test]
        public void WithTheSwitchOffOrOutsideTheFullRulesACancelPressChangesNothing()
        {
            // the switch off
            GameConfig off = CombatFeelTests.Full(true);
            off.Depth.CombatFeel.CancelEnabled = false;
            World w = CombatFeelTests.Arena(ClassId.Warrior, ClassId.Farmer, true, off);
            int foe = CombatFeelTests.Foe(w);
            StartSwing(w);
            CombatFeelTests.Tick(w, Frame(w, 0, foe, false, true), null);
            Assert.IsTrue(w.Actors[0].SwingPending, "CancelEnabled false: the swing carries on");
            Assert.AreEqual(0, Feints(w));
            // Classic and PhysicsOnly: a match with a cancel press every tick hashes as the one without
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                ulong plain = Play(p, false), pressed = Play(p, true);
                Assert.AreEqual(plain, pressed, p + ": the cancel press is inert");
            }
        }

        static ulong Play(RulesProfile p, bool cancel)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(p);
            World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame f = SimInput.EmptyInput(), scratch = SimInput.EmptyInput();
            for (int t = 0; t < 900; t++)
            {
                // actor 0 swings now and then and presses cancel every tick (or never); the bots play the rest
                f.MoveX = 0.3;
                f.Attack = t % 20 < 10;
                f.Cancel = cancel;
                MatchTick.Advance(w, f, scratch);
            }
            return MatchHash.Of(w);
        }

        [Test]
        public void TheCancelBitRidesTheWireAndTheReplayAndOldBytesReadNoCancel()
        {
            var buf = new byte[InputCodec.Size];
            InputFrame f = SimInput.EmptyInput(), g = SimInput.EmptyInput();
            f.Cancel = true;
            f.PingKind = 3;
            f.PingX = 10;
            f.PingZ = 20;
            InputCodec.Encode(f, buf, 0);
            InputCodec.Decode(buf, 0, g);
            Assert.IsTrue(g.Cancel);
            Assert.AreEqual(3, g.PingKind, "the ping kind survives beside the cancel bit");
            f.Cancel = false;
            InputCodec.Encode(f, buf, 0);
            InputCodec.Decode(buf, 0, g);
            Assert.IsFalse(g.Cancel);
            Assert.AreEqual(3, g.PingKind);
            Assert.AreEqual(14, InputCodec.Size, "the frame stays 14 bytes");
            // replay buttons: bit 128; a byte from before b16 (bits 0..64 only) reads Cancel false
            f.Cancel = true;
            f.Dodge = true;
            int b = ReplayInputStream.Buttons(f);
            Assert.AreEqual(128 | 64, b & (128 | 64));
            ReplayInputStream.SetButtons(g, b);
            Assert.IsTrue(g.Cancel && g.Dodge);
            ReplayInputStream.SetButtons(g, 1 | 64);
            Assert.IsFalse(g.Cancel, "an older replay's button byte never cancels");
        }

        [Test]
        public void BotsNeverPressCancel()
        {
            Assert.IsFalse(CriticalMass.Sim.Bots.BotBrain.UsesCancel);
            GameConfig cfg = CombatFeelTests.Full(true, false);
            World w = World.CreateWorld(new WorldOptions { Seed = 11, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame f = SimInput.EmptyInput();
            for (int t = 0; t < 1800; t++)
            {
                MatchTick.Advance(w, null, f);
                for (int j = 0; j < w.ActorCount; j++) Assert.IsFalse(w.Inputs[j].Cancel, "bot " + j + " pressed cancel at tick " + w.Tick);
            }
            Assert.AreEqual(0, w.Depth.Feel.Feints + w.Depth.Feel.HitCancels);
        }
    }
}

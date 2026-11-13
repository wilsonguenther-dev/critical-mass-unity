using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using B = CriticalMass.Runtime.Input.HumanInputState.HumanButton;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// DG-14 input buffer (sim-tick counted, delivers a refused press on the first ready tick only), DG-14 controls card
    /// glyphs (current bindings, per device), DG-10 class words and glyph mapping, DG-13 progress arithmetic.
    /// </summary>
    public sealed class ClassLoadingControlsTests
    {
        static World NewWorld(GameConfig cfg)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = cfg.Seed,
                TeamSize = cfg.TeamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = cfg,
            });
        }

        // ---------------------------------------------------------------- DG-14 input buffer

        [Test]
        public void TheWindowIsFifteenHundredthsOfASecondInWholeTicks()
        {
            Assert.AreEqual(4, InputBuffer.MaxEarlyTicks(30));
            Assert.AreEqual(9, InputBuffer.MaxEarlyTicks(60));
            Assert.AreEqual(0, InputBuffer.MaxEarlyTicks(0));
        }

        [Test]
        public void BufferStepDeliversOnPressAndOnceMoreOnTheFirstReadyStep()
        {
            var b = new InputBuffer();
            Assert.IsTrue(b.Step(true, true, false, 4), "the press step is unchanged");
            Assert.IsTrue(b.Pending);
            Assert.IsFalse(b.Step(false, true, false, 4));
            Assert.IsFalse(b.Step(false, true, false, 4));
            Assert.IsTrue(b.Step(false, true, true, 4), "first ready step");
            Assert.IsFalse(b.Pending);
            Assert.IsFalse(b.Step(false, true, true, 4), "delivered once");

            Assert.IsTrue(b.Step(true, true, true, 4), "ready at the press: plain latch");
            Assert.IsFalse(b.Pending);
            Assert.IsTrue(b.Step(true, false, false, 4), "no world: plain latch");
            Assert.IsFalse(b.Pending);
        }

        [Test]
        public void AttackPressedFourTicksBeforeTheCooldownEndsFiresOnTheReadyTick()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            double dt = SimStep.Dt(cfg);
            double cooldown = cfg.Class(ClassId.Warrior).AttackCooldown;
            Actor a = w.Actors[0];
            a.StandUp = 0;
            // Ready on step 5 (Combat ticks AttackCd down by one step before its check): the press before step 1 is
            // four ticks early.
            a.AttackCd = 5 * dt;
            var st = new HumanInputState { BufferWorld = w };
            st.ButtonDown(B.Attack);
            st.ButtonUp(B.Attack);
            InputFrame f = SimInput.EmptyInput(), scratch = SimInput.EmptyInput();
            var sent = new List<bool>();
            int firedAt = -1, fires = 0;
            for (int step = 1; step <= 12; step++)
            {
                st.Fill(f);
                sent.Add(f.Attack);
                MatchTick.Advance(w, f, scratch);
                if (Math.Abs(a.AttackCd - cooldown) < 1e-9)
                {
                    fires++;
                    if (firedAt < 0) firedAt = step;
                }
            }
            Assert.AreEqual(5, firedAt, "fires on the first ready tick");
            Assert.AreEqual(1, fires, "exactly once");
            CollectionAssert.AreEqual(new[] { true, false, false, false, true, false }, sent.Take(6).ToArray(),
                "the sim sees the press step as before, then the press again on the ready step only");
        }

        [Test]
        public void APressFiveTicksEarlyIsDroppedAsBefore()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            double dt = SimStep.Dt(cfg);
            double cooldown = cfg.Class(ClassId.Warrior).AttackCooldown;
            Actor a = w.Actors[0];
            a.StandUp = 0;
            a.AttackCd = 6 * dt;
            var st = new HumanInputState { BufferWorld = w };
            st.ButtonDown(B.Attack);
            st.ButtonUp(B.Attack);
            InputFrame f = SimInput.EmptyInput(), scratch = SimInput.EmptyInput();
            for (int step = 1; step <= 12; step++)
            {
                st.Fill(f);
                if (step > 1) Assert.IsFalse(f.Attack, "step " + step);
                MatchTick.Advance(w, f, scratch);
                Assert.AreNotEqual(cooldown, a.AttackCd, "never fired");
            }
        }

        [Test]
        public void AbilityPressedThreeTicksBeforeItIsReadyCastsOnTheReadyTick()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            double dt = SimStep.Dt(cfg);
            Actor a = w.Actors[0];
            a.StandUp = 0;
            // Abilities checks AbilityCd before anything ticks it: 3 dt before step 1 is 0 before step 4.
            a.AbilityCd = 3 * dt;
            var st = new HumanInputState { BufferWorld = w };
            st.ButtonDown(B.Ability);
            InputFrame f = SimInput.EmptyInput(), scratch = SimInput.EmptyInput();
            int castAt = -1;
            for (int step = 1; step <= 10 && castAt < 0; step++)
            {
                st.Fill(f);
                MatchTick.Advance(w, f, scratch);
                if (a.AbilityActive > 0) castAt = step;
            }
            Assert.AreEqual(4, castAt);
        }

        [Test]
        public void PausingDropsABufferedPress()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            w.Actors[0].StandUp = 0;
            w.Actors[0].AttackCd = 3 * SimStep.Dt(cfg);
            var st = new HumanInputState { BufferWorld = w };
            InputFrame f = SimInput.EmptyInput();
            st.ButtonDown(B.Attack);
            st.ButtonUp(B.Attack);
            st.Fill(f);
            Assert.IsTrue(st.AttackBuffered);
            st.ButtonDown(B.Pause);
            st.Fill(f);
            Assert.IsFalse(st.AttackBuffered);
            st.ButtonDown(B.Pause);
            w.Actors[0].AttackCd = 0;
            st.Fill(f);
            Assert.IsFalse(f.Attack);
        }

        [Test]
        public void TheSamePressesOnTheSameTicksGiveTheSameWorld()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World a = Run(cfg), b = Run(cfg);
            Assert.AreEqual(a.Tick, b.Tick);
            for (int i = 0; i < a.ActorCount; i++)
            {
                Assert.AreEqual(a.X[i], b.X[i], "x " + i);
                Assert.AreEqual(a.Z[i], b.Z[i], "z " + i);
                Assert.AreEqual(a.Actors[i].AttackCd, b.Actors[i].AttackCd, "attack cd " + i);
                Assert.AreEqual(a.Actors[i].AbilityCd, b.Actors[i].AbilityCd, "ability cd " + i);
            }
        }

        static World Run(GameConfig cfg)
        {
            World w = NewWorld(cfg);
            var st = new HumanInputState { BufferWorld = w };
            InputFrame f = SimInput.EmptyInput(), scratch = SimInput.EmptyInput();
            for (int step = 0; step < 120; step++)
            {
                if (step % 7 == 0)
                {
                    st.ButtonDown(B.Attack);
                    st.ButtonUp(B.Attack);
                }
                if (step % 31 == 3) st.ButtonDown(B.Ability);
                st.SetMove(step % 40 < 20 ? 1 : -1, 0.5);
                st.Fill(f);
                MatchTick.Advance(w, f, scratch);
            }
            return w;
        }

        // ---------------------------------------------------------------- DG-14 controls card

        [Test]
        public void TheControlsCardShowsTheCurrentBindingsPerDevice()
        {
            InputActionAsset asset = InputSystem.actions;
            if (asset == null) Assert.Ignore("no project-wide actions in this editor");
            string saved = asset.SaveBindingOverridesAsJson();
            try
            {
                asset.RemoveAllBindingOverrides();
                List<string> kb = ControlsCard.Describe(ControlDevice.KeyboardMouse);
                CollectionAssert.Contains(kb, "Attack: LMB");
                CollectionAssert.Contains(kb, "Class ability: RMB");
                CollectionAssert.Contains(kb, "Move: W A S D / ARROWS");
                CollectionAssert.Contains(kb, "Pause menu: ESC");
                List<string> pad = ControlsCard.Describe(ControlDevice.Gamepad);
                CollectionAssert.Contains(pad, "Attack: RT");
                CollectionAssert.Contains(pad, "Class ability: LT / B");
                CollectionAssert.Contains(pad, "Move: L STICK");
                Assert.AreEqual(ControlsCard.LineLabels.Length, kb.Count);

                // After a rebind the card shows the new key.
                InputAction attack = asset.FindActionMap("Player").FindAction("Attack");
                int i = ControlBindings.FindBindingIndex(attack, new RebindSlot("Attack", ControlDevice.KeyboardMouse));
                attack.ApplyBindingOverride(i, "<Keyboard>/q");
                CollectionAssert.Contains(ControlsCard.Describe(ControlDevice.KeyboardMouse), "Attack: Q");
            }
            finally
            {
                asset.RemoveAllBindingOverrides();
                if (!string.IsNullOrEmpty(saved)) asset.LoadBindingOverridesFromJson(saved);
            }
        }

        [Test]
        public void TheFooterLineIsTheOwnersWording()
        {
            Assert.AreEqual("Change these in Settings, Controls", ControlsCard.Footer);
        }

        // ---------------------------------------------------------------- DG-10 words and glyphs

        [Test]
        public void EveryClassHasACardWithThreeResolvedAbilities()
        {
            LoreWords.Reload();
            foreach (ClassId c in ClassIds.All)
            {
                ClassWords w = LoreWords.Class(c);
                Assert.AreEqual(c, w.Id);
                Assert.IsFalse(string.IsNullOrEmpty(w.Name), c.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(w.Role), c + " role");
                Assert.IsFalse(string.IsNullOrEmpty(w.Bio), c + " bio");
                Assert.That(w.Difficulty, Is.InRange(1, 3));
                Assert.AreEqual(3, w.Abilities.Length, c + " abilities");
                foreach (AbilityWords a in w.Abilities)
                {
                    StringAssert.DoesNotContain("{", a.Text, c + " " + a.Name);
                    StringAssert.DoesNotContain("{", a.Cooldown, c + " " + a.Name);
                }
            }
            Assert.AreEqual("Demolitions Expert", LoreWords.Class(ClassId.Demolitions).Name);
            StringAssert.Contains("he hands you", LoreWords.Class(ClassId.Builder).Bio, "the Builder is a man");
            StringAssert.Contains(cfgNumber(GameConfig.CreateDefault().Class(ClassId.Warrior).MaxHp), LoreWords.Class(ClassId.Warrior).Abilities[2].Text,
                "numbers come from GameConfig");
        }

        static string cfgNumber(double v) => Math.Round(v, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        [Test]
        public void EveryBuiltInTokenResolvesFromGameConfig()
        {
            LoreWords.Reload();
            foreach (string id in LoreWords.BuiltinTokenIds())
                Assert.IsNotNull(LoreWords.Resolve("{cfg:" + id + "}"), id);
            Assert.IsNull(LoreWords.Resolve("{cfg:noSuchToken}"));
            Assert.IsNull(LoreWords.Resolve("{run:winner}"));
        }

        [Test]
        public void TipsAndLoreLinesExistAndAreResolved()
        {
            LoreWords.Reload();
            Assert.Greater(LoreWords.Tips().Count, 0);
            Assert.Greater(LoreWords.LoreLines().Count, 0);
            foreach (string t in LoreWords.Tips()) StringAssert.DoesNotContain("{", t);
        }

        [Test]
        public void AbilityInputsMapToTheirActions()
        {
            Assert.AreEqual("Attack", ClassGlyphs.ActionFor("attack"));
            Assert.AreEqual("Ability", ClassGlyphs.ActionFor("ability"));
            Assert.AreEqual("Interact", ClassGlyphs.ActionFor("interact"));
            Assert.AreEqual("DigPlace", ClassGlyphs.ActionFor("throw"));
            Assert.AreEqual("", ClassGlyphs.ActionFor("passive"));
            Assert.AreEqual("PASSIVE", ClassGlyphs.Glyph("passive", ControlDevice.Gamepad));
        }

        [Test]
        public void IconTierNeverStretchesATextureUp()
        {
            Assert.AreEqual(1, ClassCard.TierFor(100f, 106f, 1f));
            Assert.AreEqual(2, ClassCard.TierFor(196f, 106f, 1f));
            Assert.AreEqual(4, ClassCard.TierFor(196f, 106f, 2f));
        }

        // ---------------------------------------------------------------- DG-13 progress

        [Test]
        public void ProgressWeightsSumToOneAndCountFinishedSteps()
        {
            Assert.AreEqual(1f, LoadingScreen.Weights.Sum(), 1e-5f);
            Assert.AreEqual(4, LoadingScreen.StepLabels.Length);
            var done = new bool[4];
            var frac = new float[4];
            Assert.AreEqual(0f, LoadingScreen.Progress(done, frac));
            frac[0] = 0.5f;
            Assert.AreEqual(0.175f, LoadingScreen.Progress(done, frac), 1e-5f);
            done[0] = done[1] = done[2] = done[3] = true;
            Assert.AreEqual(1f, LoadingScreen.Progress(done, frac), 1e-5f);
            foreach (string s in LoadingScreen.StepLabels) StringAssert.DoesNotContain("SHADER", s);
        }
    }
}

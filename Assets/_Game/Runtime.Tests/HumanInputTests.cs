using System;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using B = CriticalMass.Runtime.Input.HumanInputState.HumanButton;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Port of src/game/humanInput.test.ts (09e0904), pure state only. Key map of the port (F-07): Space -> Ability in
    /// the two Space tests, KeyW/A/S/D -> SetMove, mouse button 0 -> Attack, KeyE -> Interact, Escape -> Pause.
    /// </summary>
    [TestFixture]
    public sealed class HumanInputTests
    {
        private static readonly double Sqrt1_2 = Math.Sqrt(0.5);
        private const double Close6 = 0.5e-6; // toBeCloseTo(v, 6)

        private HumanInputState _input;
        private InputFrame _out;

        [SetUp]
        public void SetUp()
        {
            _input = new HumanInputState();
            _input.Reset();
            _out = SimInput.EmptyInput();
        }

        [TearDown]
        public void TearDown()
        {
            _input.Reset();
        }

        private InputFrame Fill()
        {
            _input.Fill(_out);
            var copy = new InputFrame();
            copy.CopyFrom(_out);
            return copy;
        }

        /// <summary>The WASD composite as the browser's held-key set: right = D - A, up = W - S.</summary>
        private void Keys(bool w, bool a, bool s, bool d)
        {
            _input.SetMove((d ? 1 : 0) - (a ? 1 : 0), (w ? 1 : 0) - (s ? 1 : 0));
        }

        [Test]
        public void SpaceDownPlusUpBetweenTwoSimStepsYieldsExactlyOneAbilityFrame()
        {
            Assert.AreEqual(false, Fill().Ability);
            _input.ButtonDown(B.Ability);
            _input.ButtonUp(B.Ability);
            Assert.AreEqual(true, Fill().Ability);
            Assert.AreEqual(false, Fill().Ability);
            Assert.AreEqual(false, Fill().Ability);

            // Unity binds Space to Jump: the jump latch follows the same rule.
            Assert.AreEqual(false, Fill().Jump);
            _input.ButtonDown(B.Jump);
            _input.ButtonUp(B.Jump);
            Assert.AreEqual(true, Fill().Jump);
            Assert.AreEqual(false, Fill().Jump);
            Assert.AreEqual(false, Fill().Jump);
        }

        [Test]
        public void HoldingSpaceIsStillOnePressAutoRepeatKeydownsDoNotReLatch()
        {
            _input.ButtonDown(B.Ability);
            Assert.AreEqual(true, Fill().Ability);
            _input.ButtonDown(B.Ability, true);
            _input.ButtonDown(B.Ability, true);
            Assert.AreEqual(false, Fill().Ability);
            _input.ButtonUp(B.Ability);
            Assert.AreEqual(false, Fill().Ability);
        }

        [Test]
        public void AClickPointerDownPlusUpBetweenTwoStepsYieldsExactlyOneAttackFrame()
        {
            _input.ButtonDown(B.Attack);
            _input.ButtonUp(B.Attack);
            Assert.AreEqual(true, Fill().Attack);
            Assert.AreEqual(false, Fill().Attack);
            Assert.AreEqual(false, Fill().Attack);
        }

        [Test]
        public void AHeldLeftButtonKeepsAttackTrueEveryFrameUntilReleasedOtherButtonsDoNothing()
        {
            _input.ButtonDown(B.Attack);
            Assert.AreEqual(true, Fill().Attack);
            Assert.AreEqual(true, Fill().Attack);
            _input.ButtonUp(B.Attack);
            Assert.AreEqual(false, Fill().Attack);
            // Mouse button 2 (right) is bound to Ability: it never reaches Attack.
            _input.ButtonDown(B.Ability);
            _input.ButtonUp(B.Ability);
            Assert.AreEqual(false, Fill().Attack);
        }

        [Test]
        public void QueueClassPickReachesInputFrameClassPickExactlyOnce()
        {
            Assert.AreEqual(-1, Fill().ClassPick);
            _input.QueueClassPick(ClassId.Miner);
            Assert.AreEqual(2, Fill().ClassPick);
            Assert.AreEqual(-1, Fill().ClassPick);
            _input.QueueClassPick(ClassId.Farmer);
            Assert.AreEqual(1, Fill().ClassPick);
            Assert.AreEqual(-1, Fill().ClassPick);
        }

        [Test]
        public void ScreenUpIsTheCameraForwardProjectedOnXZWorld11Sqrt2ForYaw45()
        {
            Assert.AreEqual(-Sqrt1_2, HumanInputState.ScreenUpX, Close6);
            Assert.AreEqual(-Sqrt1_2, HumanInputState.ScreenUpZ, Close6);
            Assert.AreEqual(Sqrt1_2, HumanInputState.ScreenRightX, Close6);
            Assert.AreEqual(-Sqrt1_2, HumanInputState.ScreenRightZ, Close6);
        }

        [Test]
        public void WASDAndArrowsMapToWorldSpaceDiagonalsAreNormalisedReleaseStops()
        {
            Keys(w: true, a: false, s: false, d: false);
            InputFrame f = Fill();
            Assert.AreEqual(-Sqrt1_2, f.MoveX, Close6);
            Assert.AreEqual(-Sqrt1_2, f.MoveZ, Close6);
            Keys(w: true, a: false, s: false, d: true);
            f = Fill();
            Assert.AreEqual(0, f.MoveX, Close6);
            Assert.AreEqual(-1, f.MoveZ, Close6);
            Assert.AreEqual(1, Math.Sqrt(f.MoveX * f.MoveX + f.MoveZ * f.MoveZ), Close6);
            Keys(w: false, a: false, s: false, d: false);
            // NOT PORTED: ArrowDown, ArrowLeft have no binding (U-006)
            // (browser: ArrowDown + ArrowLeft -> world (0, +1), length 1; then released).
            f = Fill();
            Assert.AreEqual(0, f.MoveX);
            Assert.AreEqual(0, f.MoveZ);
            // Opposite keys cancel.
            Keys(w: false, a: true, s: false, d: true);
            f = Fill();
            Assert.AreEqual(0, f.MoveX);
            Assert.AreEqual(0, f.MoveZ);
        }

        [Test]
        public void EIsAHeldInteract()
        {
            _input.ButtonDown(B.Interact);
            Assert.AreEqual(true, Fill().Interact);
            Assert.AreEqual(true, Fill().Interact);
            _input.ButtonUp(B.Interact);
            Assert.AreEqual(false, Fill().Interact);
        }

        [Test]
        public void WindowBlurClearsEveryHeldKeyAndTheHeldButton()
        {
            Keys(w: true, a: false, s: false, d: false);
            _input.ButtonDown(B.Interact);
            _input.ButtonDown(B.Attack);
            Fill();
            _input.Blur();
            InputFrame f = Fill();
            Assert.AreEqual(0, f.MoveX);
            Assert.AreEqual(0, f.MoveZ);
            Assert.AreEqual(false, f.Interact);
            Assert.AreEqual(false, f.Attack);
        }

        [Test]
        public void WithNoPointerOverTheCanvasTheAimIsNoAimNonFiniteD011()
        {
            InputFrame f = Fill();
            Assert.AreEqual(false, double.IsFinite(f.AimX));
            Assert.AreEqual(false, double.IsFinite(f.AimZ));
        }

        [Test]
        public void EscTogglesUiPausedBacktickTogglesUiDebugOpen()
        {
            _input.ButtonDown(B.Pause);
            _input.ButtonUp(B.Pause);
            Assert.AreEqual(true, _input.Paused);
            _input.ButtonDown(B.Pause);
            Assert.AreEqual(false, _input.Paused);
            // NOT PORTED: Backquote has no binding (U-006)
            // (browser: Backquote toggles ui.debugOpen true, then false).
        }

        [Test]
        public void InputIsIgnoredWhilePausedAndPressesMadeWhilePausedNeverReplay()
        {
            _input.ButtonDown(B.Pause);
            Assert.AreEqual(true, _input.Paused);
            Keys(w: true, a: false, s: false, d: false);
            _input.ButtonDown(B.Ability);
            _input.ButtonDown(B.Attack);
            _input.QueueClassPick(ClassId.Farmer);
            InputFrame f = Fill();
            Assert.AreEqual(0, f.MoveX);
            Assert.AreEqual(0, f.MoveZ);
            Assert.AreEqual(false, f.Ability);
            Assert.AreEqual(false, f.Attack);
            Assert.AreEqual(-1, f.ClassPick);
            _input.ButtonDown(B.Pause);
            Assert.AreEqual(false, _input.Paused);
            _input.ButtonUp(B.Attack);
            Keys(w: false, a: false, s: false, d: false);
            f = Fill();
            Assert.AreEqual(false, f.Ability);
            Assert.AreEqual(false, f.Attack);
        }
    }
}

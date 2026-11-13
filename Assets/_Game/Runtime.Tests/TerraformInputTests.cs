using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using B = CriticalMass.Runtime.Input.HumanInputState.HumanButton;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// DG-09 (U-053): F (DigPlace) and the dig / place switch (wheel, LB / RB) reach the sim through the human input path:
    /// one InputFrame.Terraform per F press and one InputFrame.TerraformSwitch per switch press, nothing while paused.
    /// </summary>
    [TestFixture]
    public sealed class TerraformInputTests
    {
        private HumanInputState _input;
        private InputFrame _out;

        [SetUp]
        public void SetUp()
        {
            _input = new HumanInputState();
            _input.Reset();
            _out = SimInput.EmptyInput();
        }

        [Test]
        public void DG09FLatchesOneTerraformFramePerPressAndAHeldKeyNeverRepeats()
        {
            _input.ButtonDown(B.DigPlace);
            _input.ButtonDown(B.DigPlace, repeat: true);
            _input.Fill(_out);
            Assert.IsTrue(_out.Terraform);
            _input.Fill(_out);
            Assert.IsFalse(_out.Terraform, "held: no second press");
        }

        [Test]
        public void DG09TheSwitchLatchesOneTerraformSwitchFramePerPress()
        {
            _input.SwitchDigPlace();
            _input.Fill(_out);
            Assert.IsTrue(_out.TerraformSwitch);
            Assert.IsFalse(_out.Terraform);
            _input.Fill(_out);
            Assert.IsFalse(_out.TerraformSwitch);
        }

        [Test]
        public void DG09WhilePausedOrAfterBlurTheSwitchIsDropped()
        {
            _input.ButtonDown(B.Pause);
            Assert.IsTrue(_input.Paused);
            _input.SwitchDigPlace();
            _input.Fill(_out);
            Assert.IsFalse(_out.TerraformSwitch);
            _input.ButtonDown(B.Pause);
            Assert.IsFalse(_input.Paused);
            _input.Fill(_out);
            Assert.IsFalse(_out.TerraformSwitch, "a switch pressed while paused is never replayed");
            _input.SwitchDigPlace();
            _input.Blur();
            _input.Fill(_out);
            Assert.IsFalse(_out.TerraformSwitch);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// CM b13 item J: the Dodge binding and latch. Prints the FULL binding table of the Player map (after
    /// HumanInputSource.EnsureDodgeAction, so it holds whether or not the asset already carries Dodge), proves no other
    /// action shares a dodge default, proves the Dodge row exists in ControlBindings.Rows, and proves the latch: one
    /// InputFrame.Dodge per press, none from an OS repeat, none while paused. Also: DigPlace and SwitchDigPlace latch with
    /// no class gate in the input layer (every class's F and wheel reach the sim).
    /// </summary>
    public sealed class DodgeInputTests
    {
        const string AssetPath = "Assets/_Game/Settings/Controls.inputactions";

        [Test]
        public void TheDodgeKeysClashWithNothingAndTheTableIsPrinted()
        {
            InputActionAsset asset = InputActionAsset.FromJson(File.ReadAllText(AssetPath));
            InputActionMap map = asset.FindActionMap("Player", true);
            HumanInputSource.EnsureDodgeAction(map);
            var owners = new Dictionary<string, List<string>>();
            foreach (InputBinding b in map.bindings)
            {
                if (b.isComposite) continue;
                Console.WriteLine("[CM.DodgeInput] binding: " + b.action + " | " + b.effectivePath + " | " + b.groups + " | " + b.name);
                if (string.IsNullOrEmpty(b.effectivePath)) continue;
                if (!owners.TryGetValue(b.effectivePath, out List<string> list)) owners[b.effectivePath] = list = new List<string>();
                if (!list.Contains(b.action)) list.Add(b.action);
            }
            Assert.IsNotNull(map.FindAction("Dodge", false), "the Dodge action exists");
            foreach (string path in HumanInputSource.DodgeDefaults)
            {
                List<string> who = owners.TryGetValue(path, out List<string> l) ? l : new List<string>();
                Console.WriteLine("[CM.DodgeInput] dodge key " + path + ": used by " + string.Join(", ", who));
                CollectionAssert.AreEqual(new[] { "Dodge" }, who, path + " is the dodge's alone");
            }
            bool row = false;
            foreach (ControlRow r in ControlBindings.Rows)
            {
                Console.WriteLine("[CM.DodgeInput] row: " + r.Label + " (" + (r.Keyboard != null ? r.Keyboard.Length : 0) + " key slots, " + (r.Gamepad != null ? r.Gamepad.Length : 0) + " pad slots)");
                if (r.Label == "Dodge") row = true;
            }
            Assert.IsTrue(row, "ControlBindings has a Dodge row");
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void DodgeLatchesOncePerPressAndNeverWhilePaused()
        {
            var s = new HumanInputState();
            InputFrame f = SimInput.EmptyInput();
            s.ButtonDown(HumanInputState.HumanButton.Dodge);
            s.Fill(f);
            bool first = f.Dodge;
            s.Fill(f);
            bool second = f.Dodge;
            s.ButtonDown(HumanInputState.HumanButton.Dodge, true);
            s.Fill(f);
            bool repeat = f.Dodge;
            s.ButtonUp(HumanInputState.HumanButton.Dodge);
            s.ButtonDown(HumanInputState.HumanButton.Pause);
            s.ButtonDown(HumanInputState.HumanButton.Dodge);
            s.Fill(f);
            bool paused = f.Dodge;
            s.ButtonDown(HumanInputState.HumanButton.Pause);
            s.Fill(f);
            bool afterUnpause = f.Dodge;
            s.ButtonDown(HumanInputState.HumanButton.DigPlace);
            s.SwitchDigPlace();
            s.Fill(f);
            Console.WriteLine("[CM.DodgeInput] latch: first " + first + ", next step " + second + ", OS repeat " + repeat + ", while paused " + paused + ", after unpause " + afterUnpause +
                              "; DigPlace " + f.Terraform + ", SwitchDigPlace " + f.TerraformSwitch + " (no class gate in the input layer)");
            Assert.IsTrue(first);
            Assert.IsFalse(second, "one dodge per press");
            Assert.IsFalse(repeat, "an OS repeat never re-latches");
            Assert.IsFalse(paused, "dropped while paused");
            Assert.IsFalse(afterUnpause, "never replayed on unpause");
            Assert.IsTrue(f.Terraform && f.TerraformSwitch, "F and the wheel reach the sim for every class");
        }
    }
}

using System.Linq;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Easy movement wiring that needs the editor: the MoveToPointer action and its Controls row, the settings keys, and
    /// the input-before-sim update order (no added frame of latency). The movement maths is EasyMovementTests.
    /// </summary>
    [TestFixture]
    public sealed class EasyMovementWiringTests
    {
        const string AssetPath = "Assets/_Game/Settings/Controls.inputactions";

        [Test]
        public void WalkToPointerIsARebindableKeyboardMouseRowOnTheMouseBackButtonByDefault()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            Assert.IsNotNull(asset, "Controls.inputactions not imported");
            InputAction a = asset.FindActionMap(ControlBindings.MapName, true).FindAction("MoveToPointer", false);
            Assert.IsNotNull(a, "MoveToPointer action");
            Assert.AreEqual(InputActionType.Button, a.type);
            // Integration batch 2: two defaults, the mouse side button and L SHIFT (for trackpads and two-button mice).
            Assert.AreEqual(2, a.bindings.Count);
            Assert.AreEqual("<Mouse>/backButton", a.bindings[0].path);
            Assert.AreEqual("<Keyboard>/leftShift", a.bindings[1].path);
            Assert.AreEqual(2, ControlBindings.Rows.Single(r => r.Label == "Walk to pointer").Keyboard.Length);
            ControlRow row = ControlBindings.Rows.Single(r => r.Label == "Walk to pointer");
            Assert.AreEqual("MoveToPointer", row.Keyboard[0].Action);
            Assert.IsTrue(row.Keyboard[0].Rebindable);
            Assert.AreEqual("MOUSE 4", ControlBindings.DisplayPath("<Mouse>/backButton"));
            Assert.AreEqual("MOUSE 5", ControlBindings.DisplayPath("<Mouse>/forwardButton"));
        }

        [Test]
        public void TheMovementSettingsAreResetWithTheOthers()
        {
            CollectionAssert.Contains(GameSettings.AllKeys, GameSettings.MoveAssistKey);
            CollectionAssert.Contains(GameSettings.AllKeys, GameSettings.KeysFollowMapKey);
        }

        [Test]
        public void InputIsSampledBeforeTheSimStepsInTheSameFrame()
        {
            int Order(System.Type t)
            {
                var attr = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(t, typeof(DefaultExecutionOrder));
                return attr != null ? attr.order : 0;
            }
            Assert.Less(Order(typeof(HumanInputSource)), Order(typeof(SimDriverBehaviour)));
        }
    }
}

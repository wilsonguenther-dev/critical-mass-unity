using System;
using System.Reflection;
using CriticalMass.Runtime.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests.PlayerExperience
{
    /// <summary>
    /// Review defect 0: after about a dozen match starts the real keyboard's left Shift (Dodge) hit a stale InputActionState
    /// ("Map/Control/Binding index out of range", then a NullReferenceException in BindingState.actionIndex). The input
    /// source is built and torn down 15 times here, with binding overrides loaded and cleared while it is live (what the
    /// menus' presets and rebinding do), then left Shift is pressed: Dodge must fire and nothing may be logged.
    /// </summary>
    public class PxInputLifetimeTests
    {
        const int Cycles = 15;

        static void Call(HumanInputSource s, string method)
        {
            MethodInfo m = typeof(HumanInputSource).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(m, method);
            m.Invoke(s, null);
        }

        static void Tap(Keyboard kb, Key key)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            InputSystem.Update();
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            InputSystem.Update();
        }

        [Test]
        public void FifteenInputSourcesUpAndDownThenLeftShiftDodgesWithNothingLogged()
        {
            InputActionAsset asset = InputSystem.actions;
            if (asset == null) Assert.Ignore("no project-wide actions in this editor");
            string saved = asset.SaveBindingOverridesAsJson();
            bool wasEnabled = asset.enabled;
            KeyboardHand hand = ControlBindings.Hand;
            bool keyboardOnly = ControlBindings.KeyboardOnly;
            int holdersBefore = PlayerActionsLifetime.Holders;

            // An override that moves an unrelated key, as JSON, the way a preset or a rebind stores it.
            string moved;
            InputActionMap player = ControlBindings.Map(asset);
            Assert.IsNotNull(player, "the Player map");
            InputAction ping = player.FindAction("Ping", false) ?? player.FindAction("Scoreboard", true);
            PlayerActionsLifetime.ChangeWhileDisabled(asset, () =>
            {
                asset.RemoveAllBindingOverrides();
                ping.ApplyBindingOverride(0, "<Keyboard>/f12");
            });
            moved = asset.SaveBindingOverridesAsJson();
            ControlBindings.LoadJson(asset, saved);

            Keyboard kb = InputSystem.AddDevice<Keyboard>("PxLifetimeKeyboard");
            int dodges = 0;
            Action<InputAction.CallbackContext> onDodge = _ => dodges++;
            InputAction dodge = null;
            try
            {
                for (int i = 0; i < Cycles; i++)
                {
                    var go = new GameObject("PxLifetimeInput" + i);
                    try
                    {
                        HumanInputSource src = go.AddComponent<HumanInputSource>();
                        Call(src, "OnEnable");
                        Assert.IsTrue(ControlBindings.Map(asset).enabled, $"cycle {i}: the Player map is on while a source lives");
                        ControlBindings.LoadJson(asset, i % 2 == 0 ? moved : string.Empty);
                        Assert.IsTrue(ControlBindings.Map(asset).enabled, $"cycle {i}: an override change keeps the map on");
                        Tap(kb, Key.LeftShift);
                        Call(src, "OnDisable");
                    }
                    finally
                    {
                        Object.DestroyImmediate(go);
                    }
                }
                Assert.AreEqual(holdersBefore, PlayerActionsLifetime.Holders, "every source gave the map back");

                player = ControlBindings.Map(asset);
                player.Enable();
                dodge = player.FindAction("Dodge", true);
                dodge.started += onDodge;
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.LeftShift));
                InputSystem.Update();
                Assert.IsTrue(kb.leftShiftKey.isPressed, "the keyboard took the press");
                Assert.IsTrue(dodges > 0 || dodge.IsPressed(), "left Shift starts Dodge after 15 sources");
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                InputSystem.Update();
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (dodge != null) dodge.started -= onDodge;
                InputSystem.RemoveDevice(kb);
                ControlBindings.LoadJson(asset, saved);
                if (!wasEnabled) asset.Disable();
                ControlBindings.SetHand(hand);
                ControlBindings.SetKeyboardOnly(keyboardOnly);
            }
        }

        [Test]
        public void ChangeWhileDisabledPutsBackExactlyTheActionsThatWereOn()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                InputActionMap map = asset.AddActionMap("Player");
                InputAction a = map.AddAction("A", InputActionType.Button, "<Keyboard>/a");
                InputAction b = map.AddAction("B", InputActionType.Button, "<Keyboard>/b");
                a.Enable();   // B stays off, as a gate (InteractCore) leaves it
                bool insideOff = true;
                PlayerActionsLifetime.ChangeWhileDisabled(asset, () =>
                {
                    insideOff = !a.enabled && !b.enabled;
                    a.ApplyBindingOverride(0, "<Keyboard>/c");
                });
                Assert.IsTrue(insideOff, "nothing is enabled while bindings change");
                Assert.IsTrue(a.enabled, "A is on again");
                Assert.IsFalse(b.enabled, "B stays off");
                Assert.AreEqual("<Keyboard>/c", a.bindings[0].effectivePath);
            }
            finally
            {
                asset.Disable();
                Object.DestroyImmediate(asset);
            }
        }
    }
}

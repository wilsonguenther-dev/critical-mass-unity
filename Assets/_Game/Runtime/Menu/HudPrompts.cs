using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// Switches the HUD's three ability-button key prompts (GameHud's "Key Label 0..2": attack, class ability, interact)
    /// between keyboard glyph text (LMB, RMB, E) and controller glyph text (RT, LT, X) with the device used last, and
    /// after a rebind. The HUD file is not edited: the labels are found by name under GameHud.Canvas. If the HUD renames
    /// them this does nothing (no error); the clean hook is for GameHud to call ControlBindings.PromptLabel itself.
    /// </summary>
    public sealed class HudPrompts : MonoBehaviour
    {
        /// <summary>The Player actions behind the HUD's key labels 0, 1 and 2.</summary>
        public static readonly string[] Actions = { "Attack", "Ability", "Interact" };

        readonly Text[] _labels = new Text[Actions.Length];
        bool _found;
        int _nextLook;
        bool _dirty = true;

        /// <summary>The label texts found under the HUD (null entries until found).</summary>
        public Text Label(int i) => _labels[i];

        void OnEnable()
        {
            ControlBindings.ActiveDeviceChanged += OnDevice;
            ControlBindings.BindingsChanged += MarkDirty;
            _dirty = true;
        }

        void OnDisable()
        {
            ControlBindings.ActiveDeviceChanged -= OnDevice;
            ControlBindings.BindingsChanged -= MarkDirty;
        }

        void OnDevice(ControlDevice d) => _dirty = true;
        void MarkDirty() => _dirty = true;

        void LateUpdate()
        {
            if (!_found)
            {
                if (Time.frameCount < _nextLook) return;
                _nextLook = Time.frameCount + 30;
                Find();
                if (!_found) return;
            }
            ControlBindings.Sample();
            if (!_dirty) return;
            _dirty = false;
            Apply(ControlBindings.ActiveDevice);
        }

        void Find()
        {
            GameHud hud = GetComponent<GameHud>();
            if (hud == null || hud.Canvas == null) return;
            Text[] all = hud.Canvas.GetComponentsInChildren<Text>(true);
            int n = 0;
            for (int i = 0; i < all.Length; i++)
                for (int k = 0; k < Actions.Length; k++)
                    if (_labels[k] == null && all[i].name == "Key Label " + k)
                    {
                        _labels[k] = all[i];
                        n++;
                    }
            _found = n == Actions.Length;
        }

        /// <summary>Writes the prompts for a device now.</summary>
        public void Apply(ControlDevice device)
        {
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            if (map == null) return;
            for (int k = 0; k < Actions.Length; k++)
            {
                if (_labels[k] == null) continue;
                HudText.Set(_labels[k], ControlBindings.PromptLabel(map.FindAction(Actions[k], false), device));
            }
        }
    }
}

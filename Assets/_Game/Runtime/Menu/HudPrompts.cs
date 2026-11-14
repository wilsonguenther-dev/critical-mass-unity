using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// Readable prompts on the HUD (2026-11-12, owner: "I don't know what you mean by these control buttons, MMB LMB
    /// RMB"). The three ability-button key plates of GameHud ("Key 0..2" with "Key Label 0..2": attack, class ability,
    /// interact) show a drawn glyph from InputGlyphs instead of text: a mouse with the left or right button lit, a keycap
    /// with the key's name, or the controller's trigger / face button, for the device used last and the current bindings.
    /// The plate widens for a long key name ("Space"). The hold-to-look hint (Camera/LookHint, "HOLD [key] TO LOOK
    /// AROUND") gets the same treatment in its key chip: a mouse with the middle button lit, or the right stick press.
    /// Neither file is edited: the objects are found by name, as before; if they are renamed this does nothing (no error).
    /// The label Texts keep the plain words (InputGlyphs.Words) while hidden, so a text scan never finds an abbreviation.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after LookHint and GameHud write their own frame
    public sealed class HudPrompts : MonoBehaviour
    {
        /// <summary>The Player actions behind the HUD's key labels 0, 1 and 2.</summary>
        public static readonly string[] Actions = { "Attack", "Ability", "Interact" };

        /// <summary>Glyph height inside the HUD's 48 x 28 key plates.</summary>
        public const float PlateGlyphH = 24f;
        public const float PlateMinW = 48f;
        public const float LookGlyphH = 30f;

        readonly Text[] _labels = new Text[Actions.Length];
        readonly RectTransform[] _plates = new RectTransform[Actions.Length];
        readonly InputGlyphView[] _glyphs = new InputGlyphView[Actions.Length];
        bool _found;
        int _nextLook;
        bool _dirty = true;

        LookHint _lookHint;
        int _nextHintLook;
        InputGlyphView _lookGlyph;
        Text _lookKey;
        bool _lookSpecs;
        GlyphSpec _lookMouse, _lookPad;

        /// <summary>The label texts found under the HUD (null entries until found).</summary>
        public Text Label(int i) => _labels[i];

        /// <summary>The glyph shown on key plate i (null until the HUD is found).</summary>
        public InputGlyphView Glyph(int i) => _glyphs[i];

        /// <summary>The glyph in the hold-to-look hint's chip (null until the hint has been shown).</summary>
        public InputGlyphView LookGlyph => _lookGlyph;

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
            ControlBindings.Sample();
            PatchLookHint();
            if (!_found)
            {
                if (Time.frameCount < _nextLook) return;
                _nextLook = Time.frameCount + 30;
                Find();
                if (!_found) return;
            }
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
                        Transform plate = all[i].transform.parent != null ? all[i].transform.parent.Find("Key " + k) : null;
                        _plates[k] = plate as RectTransform;
                        n++;
                    }
            _found = n == Actions.Length;
            if (!_found) return;
            for (int k = 0; k < Actions.Length; k++)
            {
                Transform host = _plates[k] != null ? _plates[k] : _labels[k].transform.parent;
                InputGlyphView v = InputGlyphView.Make(host, "Prompt Glyph", PlateGlyphH);
                RectTransform rt = v.Rect;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                if (_plates[k] == null)
                {
                    // No plate found: sit where the label sits.
                    rt.anchoredPosition = _labels[k].rectTransform.anchoredPosition;
                }
                _glyphs[k] = v;
                _labels[k].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// fix/hud-containment-3 (walk: mixed key-hint styles): the ONE key-hint style, for any key plate on the HUD. A drawn
        /// glyph centred on a navy plate, like the three ability plates; the label keeps the plain words, hidden.
        /// </summary>
        public static InputGlyphView PlateGlyph(Transform plate)
        {
            InputGlyphView v = InputGlyphView.Make(plate, "Prompt Glyph", PlateGlyphH);
            RectTransform rt = v.Rect;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return v;
        }

        /// <summary>Shows an action's glyph on a plate (the words stay in the hidden label). Returns the plate width it needs.</summary>
        public static float SetPlate(InputGlyphView glyph, Text label, string action, ControlDevice device)
        {
            bool on = !string.IsNullOrEmpty(action);
            if (glyph != null && glyph.gameObject.activeSelf != on) glyph.gameObject.SetActive(on);
            if (!on) { if (label != null) label.enabled = true; return PlateMinW; }
            GlyphSpec g = InputGlyphs.ForAction(action, device);
            if (label != null) { HudText.Set(label, g.Words); label.enabled = glyph == null; }
            return glyph != null ? Mathf.Max(PlateMinW, glyph.Set(g) + 12f) : PlateMinW;
        }

        /// <summary>Writes the prompts for a device now (glyphs on the plates, plain words in the hidden labels).</summary>
        public void Apply(ControlDevice device)
        {
            for (int k = 0; k < Actions.Length; k++)
            {
                GlyphSpec g = InputGlyphs.ForAction(Actions[k], device);
                if (_labels[k] != null) HudText.Set(_labels[k], g.Words);
                if (_glyphs[k] == null) continue;
                float w = _glyphs[k].Set(g);
                if (_plates[k] != null)
                {
                    Vector2 sd = _plates[k].sizeDelta;
                    float want = Mathf.Max(PlateMinW, w + 12f);
                    if (Mathf.Abs(sd.x - want) > 0.5f) _plates[k].sizeDelta = new Vector2(want, sd.y);
                }
            }
        }

        /// <summary>LookHint builds its chip when it first shows; put a glyph in it then and keep it on the device in use.</summary>
        void PatchLookHint()
        {
            if (_lookHint == null)
            {
                if (Time.frameCount < _nextHintLook) return;
                _nextHintLook = Time.frameCount + 30;
                _lookHint = FindAnyObjectByType<LookHint>();
                if (_lookHint == null) return;
            }
            if (_lookGlyph == null)
            {
                if (!_lookHint.IsOpen || Time.frameCount < _nextHintLook) return;
                _nextHintLook = Time.frameCount + 15;
                Transform chip = FindDeep(_lookHint.transform, "Key Chip");
                if (chip == null) return;
                Transform key = chip.Find("Key");
                _lookKey = key != null ? key.GetComponent<Text>() : null;
                if (_lookKey != null) _lookKey.gameObject.SetActive(false);
                _lookGlyph = InputGlyphView.Make(chip, "Prompt Glyph", LookGlyphH);
                RectTransform rt = _lookGlyph.Rect;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }
            bool pad = ControlBindings.ActiveDevice == ControlDevice.Gamepad;
            if (!_lookSpecs)
            {
                _lookSpecs = true;
                _lookMouse = InputGlyphs.ForPath("<Mouse>/middleButton");
                _lookPad = InputGlyphs.ForPath("<Gamepad>/rightStickPress");
            }
            GlyphSpec g = pad ? _lookPad : _lookMouse;
            _lookGlyph.Set(g);
            if (_lookKey != null) HudText.Set(_lookKey, g.Words);
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = FindDeep(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }
    }
}

using System.Text;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// The guided match's words: each step's line comes from the words pack by id "trial.&lt;step id&gt;" (through
    /// MascotLines) with the step's own Prompt as the built-in fallback, and its action tokens become the glyphs of the
    /// device used last. Formatting runs only when a step starts or the device changes, never per frame.
    /// </summary>
    public static class TrialText
    {
        public const string Offer = "trial.offer";
        public const string OfferTitle = "trial.offer.title";
        public const string OfferSub = "trial.offer.sub";
        public const string OfferPlay = "trial.offer.play";
        public const string OfferSkip = "trial.offer.skip";
        public const string Done = "trial.done";
        public const string DoneHint = "trial.done.hint";
        public const string Left = "trial.left";

        static bool _registered;

        /// <summary>Registers the built-in fallbacks (idempotent).</summary>
        public static void RegisterFallbacks(TrialStep[] steps)
        {
            if (!_registered)
            {
                _registered = true;
                MascotLines.Register(Offer, "First time in the Basin? I'm " + MascotGuide.MascotName + ". Give me three minutes.");
                MascotLines.Register(OfferTitle, "FIRST TIME?");
                MascotLines.Register(OfferSub, "A 3-minute guided match. 5 v 5, normal pace.");
                MascotLines.Register(OfferPlay, "PLAY THE GUIDED MATCH");
                MascotLines.Register(OfferSkip, "SKIP, I KNOW HOW TO PLAY");
                MascotLines.Register(Done, "That's the lot. You're dangerous now. Go win one for real.");
                MascotLines.Register(DoneHint, "{Confirm}  REAL MATCH, 16 V 16    {Toggle}  KEEP PLAYING");
                MascotLines.Register(Left, "Fine, learn by doing. I'll be in the pause menu if you need me.");
            }
            if (steps == null) return;
            for (int i = 0; i < steps.Length; i++) MascotLines.Register(LineId(steps[i]), steps[i].Prompt);
        }

        public static string LineId(TrialStep s) => "trial." + s.Id;

        /// <summary>A step's line for the device, tokens filled in.</summary>
        public static string StepLine(TrialStep s, ControlDevice device) => Fill(MascotLines.Resolve(LineId(s)), device);

        static readonly StringBuilder Sb = new StringBuilder(160);

        /// <summary>Replaces {Move} {Attack} {Ability} {Interact} {Jump} {DigPlace} {Map} {Skip} {Toggle} {Confirm} with glyph text.</summary>
        public static string Fill(string text, ControlDevice device)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? string.Empty;
            Sb.Clear();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int close = c == '{' ? text.IndexOf('}', i + 1) : -1;
                if (close < 0)
                {
                    Sb.Append(c);
                    i++;
                    continue;
                }
                string token = text.Substring(i + 1, close - i - 1);
                string glyph = Glyph(token, device);
                if (glyph == null) Sb.Append(text, i, close - i + 1);
                else Sb.Append(glyph);
                i = close + 1;
            }
            return Sb.ToString();
        }

        /// <summary>The glyph text of a token (null = not a token, left as written).</summary>
        public static string Glyph(string token, ControlDevice device)
        {
            bool pad = device == ControlDevice.Gamepad;
            switch (token)
            {
                case "Move": return MoveGlyph(device);
                case "Skip": return pad ? "D-PAD DOWN" : "BACKSPACE";
                case "Toggle": return MascotGuide.ControlDeviceKey();
                case "Confirm": return pad ? "D-PAD RIGHT" : "ENTER";
                case "Attack": return Action("Attack", device, pad ? "RT" : "LEFT CLICK");
                case "Ability": return Action("Ability", device, pad ? "LT" : "RIGHT CLICK");
                case "Interact": return Action("Interact", device, pad ? "X" : "E");
                case "Jump": return Action("Jump", device, pad ? "A" : "SPACE");
                case "DigPlace": return Action("DigPlace", device, pad ? "Y" : "F");
                case "Map": return Action("Map", device, pad ? "D-PAD UP" : "M");
                default: return null;
            }
        }

        static string Action(string name, ControlDevice device, string fallback)
        {
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            InputAction a = map != null ? map.FindAction(name, false) : null;
            if (a == null) return fallback;
            string s = ControlBindings.PromptLabel(a, device);
            return string.IsNullOrEmpty(s) || s == "-" ? fallback : s;
        }

        static readonly string[] MoveParts = { "up", "left", "down", "right" };

        /// <summary>Keyboard: the four WASD keys as bound (rebinds show); controller: the stick.</summary>
        public static string MoveGlyph(ControlDevice device)
        {
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            InputAction a = map != null ? map.FindAction("Move", false) : null;
            if (device == ControlDevice.Gamepad)
            {
                if (a == null) return "L STICK";
                string g = ControlBindings.PromptLabel(a, ControlDevice.Gamepad);
                return string.IsNullOrEmpty(g) || g == "-" ? "L STICK" : g;
            }
            if (a == null) return "WASD";
            var keys = new string[MoveParts.Length];
            bool single = true;
            for (int k = 0; k < MoveParts.Length; k++)
            {
                // controls-1: the composite of the hand position in use (W A S D, or the arrows).
                string comp = ControlBindings.HandName(ControlBindings.Hand);
                int bi = ControlBindings.FindBindingIndex(a, new RebindSlot("Move", ControlDevice.KeyboardMouse, 0, comp, MoveParts[k]));
                string key = bi >= 0 ? ControlBindings.Display(a, bi) : "-";
                if (key == "-" || key.Length == 0) return "WASD";
                keys[k] = key;
                if (key.Length > 1) single = false;
            }
            if (ControlBindings.Hand == KeyboardHand.Arrows && string.Join(" ", keys) == "UP LEFT DOWN RIGHT") return "ARROWS";
            // W A S D order (up, left, down, right) reads as "WASD"; multi-letter keys get spaces.
            return single ? string.Concat(keys) : string.Join(" ", keys);
        }
    }
}

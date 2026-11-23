using System.Text;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// The guided match's words: each step's line comes from the words pack by id "trial.&lt;step id&gt;" (through
    /// MascotLines) with the step's own Prompt as the built-in fallback, and its action tokens become the glyphs of the
    /// device used last. Formatting runs only when a step starts or the device changes, never per frame.
    /// </summary>
    /// <summary>
    /// b13-v: the first-match hints for the batch 13 actions: one line each the first time the player can dodge, mine (a
    /// Miner) and build (a Builder), with the bound key, shown once ever (PlayerPrefs) and dismissed by doing it. Pure
    /// reads of the S0 getters, so with the rules off none is ever due.
    /// </summary>
    public static class B13Hints
    {
        public const int Dodge = 0, Mine = 1, Build = 2, /* b16 */ Cancel = 3, Count = 4;
        public const string PrefsKey = "cm.b13.hints";
        public static readonly string[] Lines =
        {
            "{Dodge} to dodge. A quick dash; you cannot be hit for a moment.",
            "Hold {DigPlace} near an ore vein to MINE, then bank the ore at your hut.",
            "{DigPlace} uses your tool on the outlined spot. {Switch} or 1-7 picks the tool: dig, place, ladder, bridge, walls, block.",
            // b16 CANCEL (test feature): shown the first time the player winds up a swing with the cancel on
            "{Cancel} during your swing to FEINT it. Right after a hit lands, {Cancel} recovers faster.",
        };

        public static string Line(int i, ControlDevice device) => TrialText.Fill(Lines[Mathf.Clamp(i, 0, Count - 1)], device);

        public static int ShownMask() => CmPrefs.GetInt(PrefsKey, 0);
        public static void MarkShown(int i)
        {
            CmPrefs.SetInt(PrefsKey, ShownMask() | (1 << i));
            CmPrefs.Save();
        }

        /// <summary>The player can do it now (the rule is on, the class fits, the player is up).</summary>
        public static bool Available(int i, World w, int id)
        {
            if (w == null || id < 0 || id >= w.ActorCount || !w.Actors[id].Alive) return false;
            Actor a = w.Actors[id];
            switch (i)
            {
                case Dodge: return CriticalMass.Sim.Core.Dodge.On(w) && CriticalMass.Sim.Core.Dodge.CooldownTicks(w, id) <= 0;
                case Mine: return a.Cls == ClassId.Miner && Blacksmith.On(w);
                case Build: return a.Cls == ClassId.Builder && BuilderWalls.On(w) && BuilderWalls.Ghost(w, id, out _, out _, out _, out _, out _, out _, out _);
                case Cancel: return CriticalMass.Runtime.Menu.GameSettings.CancelButton && CombatFeel.On(w) && w.Config.Depth.CombatFeel.CancelEnabled && a.SwingPending;
                default: return false;
            }
        }

        /// <summary>The player did it (pressedF: the F action was pressed this frame).</summary>
        public static bool Done(int i, World w, int id, bool pressedF)
        {
            if (w == null || id < 0 || id >= w.ActorCount) return true;
            Actor a = w.Actors[id];
            switch (i)
            {
                case Dodge: return CriticalMass.Sim.Core.Dodge.Active(w, id);
                case Mine: return a.Carry == CarryKind.Ore || a.Cls != ClassId.Miner;
                case Build: return pressedF || a.Cls != ClassId.Builder;
                case Cancel: return w.Depth.Feel.CancelUsed[id] != CombatFeel.NoTick || !CriticalMass.Runtime.Menu.GameSettings.CancelButton;
                default: return true;
            }
        }

        /// <summary>The first hint not yet shown that the player can act on now, or -1.</summary>
        public static int Due(int shownMask, World w, int id)
        {
            for (int i = 0; i < Count; i++) if ((shownMask & (1 << i)) == 0 && Available(i, w, id)) return i;
            return -1;
        }
    }

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
                case "Switch": return pad ? Action("SwitchDigPlace", device, "LB") : "WHEEL";   // b14: the Builder's next-tool button (left bumper)
                case "Dodge": return Action("Dodge", device, pad ? "RB" : "SHIFT");   // b14: Left Shift / right bumper
                case "Cancel": return Action("Cancel", device, pad ? "LB" : "MOUSE 5");   // b16: CANCEL (mouse forward side button / LB)
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

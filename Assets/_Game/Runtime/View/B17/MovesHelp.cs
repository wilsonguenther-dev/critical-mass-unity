// Class moves build (2026-11-22), lane 6: on-screen help for the new verbs. The Controls card lines and the first-use hints.
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.B17
{
    /// <summary>
    /// Lane 6 (class moves): how a player finds the new verbs without a manual.
    /// CARD: extra Controls-card lines for the verbs that ride an existing button (heavy, combo, slide, glide, TNT). Each line
    /// borrows the row of the button it uses, so the chip shows that button on every layout and on the controller, with
    /// HOLD in front where the verb is a hold. The lines exist only in the play build (CM_MOVES_ON), where every switch is
    /// on; Guard / parry has its own binding row (lane 1) and its own line already.
    /// HINTS: a short first-use hint over the player's fighter, the first time each verb becomes useful in a match with its
    /// switch on. Each hint shows at most HintShows times ever (PlayerPrefs), so it never nags. Pure tables here; MovesViews
    /// decides when and shows them.
    /// </summary>
    public static class MovesHelp
    {
        public const string HeavyLine = "Heavy (hold attack)", ComboLine = "Combo (attack on a hit)", SlideLine = "Slide (dodge while travelling)",
            GlideLine = "Glide, Healer (hold jump)", TntLine = "TNT, Demolitions";

        /// <summary>Card lines in order (after Guard / parry).</summary>
        public static readonly string[] CardLines = { HeavyLine, ComboLine, SlideLine, GlideLine, TntLine };

        /// <summary>The card lines show (the play build turns every class-moves switch on).</summary>
        public static bool CardOn => CriticalMass.Runtime.Input.MovesInput.PlayBuildAllOn || CardOverride;
        /// <summary>Tests: show the lines in an ordinary build.</summary>
        public static bool CardOverride;

        /// <summary>The binding row a card line borrows (ControlBindings row label), or null when it is not a moves line.</summary>
        public static string CardRow(string line)
        {
            switch (line)
            {
                case HeavyLine: return "Attack";
                case ComboLine: return "Attack";
                case SlideLine: return "Dodge";
                case GlideLine: return "Jump";
                case TntLine: return "Build / mine";
                default: return null;
            }
        }

        /// <summary>The words in front of the borrowed button ("HOLD " for a hold), or "" .</summary>
        public static string CardPrefix(string line) => line == HeavyLine || line == GlideLine ? "HOLD " : line == ComboLine ? "AGAIN " : "";

        // ------------------------------------------------------------------ first-use hints

        public enum Verb { Guard, Heavy, Combo, Slide, Glide, JumpEvade, Tnt, Count }

        /// <summary>A hint shows at most this many times on this machine (one per match until then).</summary>
        public const int HintShows = 2;
        public const float HintSeconds = 2.6f;

        /// <summary>The action whose binding words the hint names (InputGlyphs.Words).</summary>
        public static string HintAction(Verb v)
        {
            switch (v)
            {
                case Verb.Guard: return "Guard";
                case Verb.Heavy: case Verb.Combo: return "Attack";
                case Verb.Slide: return "Dodge";
                case Verb.Glide: case Verb.JumpEvade: return "Jump";
                case Verb.Tnt: return "DigPlace";
                default: return null;
            }
        }

        /// <summary>The hint words for a verb, with the button's words (key or pad button) in front.</summary>
        public static string HintText(Verb v, string key)
        {
            string k = string.IsNullOrEmpty(key) ? "" : key.ToUpperInvariant() + ": ";
            switch (v)
            {
                case Verb.Guard: return k + "HOLD TO GUARD, TAP AS HE SWINGS TO PARRY";
                case Verb.Heavy: return "HOLD " + k + "CHARGE A HEAVY";
                case Verb.Combo: return k + "AGAIN ON A HIT: COMBO";
                case Verb.Slide: return "TRAVELLING  " + k + "SLIDE";
                case Verb.Glide: return "HOLD " + k + "IN THE AIR: GLIDE";
                case Verb.JumpEvade: return k + "JUMP OVER A GROUND SWING";
                case Verb.Tnt: return k + "THROW TNT  (ATTACK IS YOUR PRY BAR)";
                default: return "";
            }
        }

        /// <summary>The verb's switch is on in this world (no hint for a verb that is off).</summary>
        public static bool SwitchOn(World w, Verb v, ClassId cls)
        {
            if (!MovesRead.On(w)) return false;
            MovesConfig m = MovesHooks.Cfg(w);
            switch (v)
            {
                case Verb.Guard: return m.Guard;
                case Verb.Heavy: return m.Heavy;
                case Verb.Combo: return m.Combos;
                case Verb.Slide: return m.Travel && m.Slide;
                case Verb.Glide: return m.Glide && (int)cls < m.GlideClasses.Length && m.GlideClasses[(int)cls];
                case Verb.JumpEvade: return m.JumpEvade;
                case Verb.Tnt: return m.DemoTntOnDigOnly && cls == ClassId.Demolitions;
                default: return false;
            }
        }

        public static string PrefKey(Verb v) => "cm.moves.hint." + v;
    }
}

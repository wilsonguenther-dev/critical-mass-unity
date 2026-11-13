using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using UnityEngine;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// feat/terrain-affordances: the "World feedback" group, its own WORLD tab on the settings page (PauseMenu.AddTab,
    /// registered before the menu builds, like SettingsExtras' ACCESS tab). Each element of the terrain affordances and the
    /// world hit reactions has its own switch, default ON; the hint level is off, the first two matches per class
    /// (default) or always. Every reader goes through these properties (spectator and capture modes included).
    /// </summary>
    public static class WorldFeedbackSettings
    {
        public const string PromptsKey = "cm.world.prompts";
        public const string OutlineKey = "cm.world.outline";
        public const string MarkersKey = "cm.world.markers";
        public const string ProgressKey = "cm.world.progress";
        public const string ShakeKey = "cm.world.shake";
        public const string ParticlesKey = "cm.world.particles";
        public const string HintsKey = "cm.world.hints";
        /// <summary>Per class: matches started with the first-minute hint shown (cm.world.hintSeen.&lt;class index&gt;).</summary>
        public const string HintSeenPrefix = "cm.world.hintSeen.";
        public const string TabName = "WORLD";

        public static readonly string[] AllKeys = { PromptsKey, OutlineKey, MarkersKey, ProgressKey, ShakeKey, ParticlesKey, HintsKey };

        /// <summary>Hint levels (HintsKey).</summary>
        public const int HintsOff = 0, HintsFirstMatches = 1, HintsAlways = 2;
        /// <summary>Matches per class that show the first-minute hint at HintsFirstMatches.</summary>
        public const int HintMatches = 2;

        public static event Action Changed;

        /// <summary>The live-target prompt ("F  DIG", "SPACE  BUILD LADDER  ·  bank empty").</summary>
        public static bool Prompts { get => GetB(PromptsKey, true); set => SetB(PromptsKey, value); }
        /// <summary>The strong outline on the single target and the placement ghost.</summary>
        public static bool Outline { get => GetB(OutlineKey, true); set => SetB(OutlineKey, value); }
        /// <summary>The quiet markers on every usable cell or object nearby.</summary>
        public static bool Markers { get => GetB(MarkersKey, true); set => SetB(MarkersKey, value); }
        /// <summary>Health and progress bars on objects being worked on or hit.</summary>
        public static bool Progress { get => GetB(ProgressKey, true); set => SetB(ProgressKey, value); }
        /// <summary>The shake and squash of a hit object (also off under Reduce motion).</summary>
        public static bool Shake { get => GetB(ShakeKey, true); set => SetB(ShakeKey, value); }
        /// <summary>Hit flash, chips and dust (the flash also obeys Reduce flashing).</summary>
        public static bool Particles { get => GetB(ParticlesKey, true); set => SetB(ParticlesKey, value); }
        /// <summary>HintsOff, HintsFirstMatches (default) or HintsAlways.</summary>
        public static int Hints
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(HintsKey, HintsFirstMatches), HintsOff, HintsAlways);
            set { PlayerPrefs.SetInt(HintsKey, Mathf.Clamp(value, HintsOff, HintsAlways)); Changed?.Invoke(); }
        }

        public static string HintsLabel(int level) => level == HintsOff ? "OFF" : level == HintsAlways ? "ALWAYS" : "FIRST TWO MATCHES";

        /// <summary>Matches of class index ci already started with the hint.</summary>
        public static int HintSeen(int ci) => PlayerPrefs.GetInt(HintSeenPrefix + ci, 0);

        /// <summary>Should a match of class index ci that starts now show the first-minute hint?</summary>
        public static bool HintDue(int ci)
        {
            int level = Hints;
            if (level == HintsOff) return false;
            if (level == HintsAlways) return true;
            return HintSeen(ci) < HintMatches;
        }

        /// <summary>Counts a match of class index ci as hinted (once per match, by the view).</summary>
        public static void NoteHinted(int ci) => PlayerPrefs.SetInt(HintSeenPrefix + ci, HintSeen(ci) + 1);

        public static void ResetAll()
        {
            for (int i = 0; i < AllKeys.Length; i++) PlayerPrefs.DeleteKey(AllKeys[i]);
            for (int i = 0; i < 6; i++) PlayerPrefs.DeleteKey(HintSeenPrefix + i);
            Changed?.Invoke();
        }

        static string OnOff(bool b) => b ? "ON" : "OFF";

        /// <summary>The WORLD tab: seven lines, a reset button and a note.</summary>
        public static List<MenuItem> BuildTab(PauseMenu menu, RectTransform tab)
        {
            var items = new List<MenuItem>
            {
                menu.Stepper(tab, 0, "INTERACT PROMPTS", () => OnOff(Prompts), d => Prompts = !Prompts),
                menu.Stepper(tab, 1, "TARGET OUTLINE AND GHOST", () => OnOff(Outline), d => Outline = !Outline),
                menu.Stepper(tab, 2, "USABLE-TERRAIN MARKERS", () => OnOff(Markers), d => Markers = !Markers),
                menu.Stepper(tab, 3, "PROGRESS AND HEALTH BARS", () => OnOff(Progress), d => Progress = !Progress),
                menu.Stepper(tab, 4, "OBJECT SHAKE", () => OnOff(Shake), d => Shake = !Shake),
                menu.Stepper(tab, 5, "HIT PARTICLES", () => OnOff(Particles), d => Particles = !Particles),
                menu.Stepper(tab, 6, "CLASS HINTS", () => HintsLabel(Hints), d => Hints = (Hints + (d >= 0 ? 1 : 2)) % 3),
            };
            float y = PauseMenu.ContentY + items.Count * (PauseMenu.LineH + PauseMenu.LineGap) + 10f;
            MenuItem reset = MenuUi.Item(tab, "Reset World Feedback", PauseMenu.ContentX, y, 460f, 56f, "RESET THESE OPTIONS",
                MenuItem.Kind.Button, 24, true, CMTokens.Display);
            reset.Submitted = () =>
            {
                ResetAll();
                menu.RefreshAll();
                menu.SetStatus("World feedback options are back to their defaults.", false);
            };
            items.Add(reset);
            menu.Note(tab, items.Count, "Hold the show-terrain key in a match to see everything your class can use nearby.");
            return items;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            PauseMenu.AddTab(new PauseMenu.ExtraTab { Name = TabName, Build = BuildTab, Order = 10 });
        }

        static bool GetB(string k, bool d) => PlayerPrefs.GetInt(k, d ? 1 : 0) != 0;

        static void SetB(string k, bool v)
        {
            PlayerPrefs.SetInt(k, v ? 1 : 0);
            Changed?.Invoke();
        }
    }
}

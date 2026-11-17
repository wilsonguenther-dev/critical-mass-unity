using System.IO;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Menus.Characters
{
    /// <summary>One slide of a class's HOW IT WORKS strip: a title, one sentence and the still it shows.</summary>
    public sealed class ShowcaseSlide
    {
        public string Title;
        public string Line;
    }

    /// <summary>One class on the CHARACTERS screen.</summary>
    public sealed class ShowcaseClass
    {
        public ClassId Id;
        /// <summary>File id (warrior, farmer, miner, demolitions, builder, healer): clip and still names.</summary>
        public string FileId;
        public string Name;
        /// <summary>The tile's one-line role (five words at most).</summary>
        public string Role;
        /// <summary>The sentence over the right pane (what the class is for).</summary>
        public string Summary;
        /// <summary>The class's F action word on the CONTROLS tab ("" = not used by this class).</summary>
        public string FAction;
        /// <summary>The class's Space ability word on the CONTROLS tab.</summary>
        public string Ability;
        public ShowcaseSlide[] Slides;
    }

    /// <summary>One CONTROLS row: what it does and the Player action whose CURRENT binding it shows.</summary>
    public struct ShowcaseControl
    {
        public string Label;
        public string Action;
        public string What;
    }

    /// <summary>
    /// b13-v: the CHARACTERS screen's data (one place for every class's role line, four slides, clip path and control
    /// rows) and its pure helpers (paths, flipbook timing, layout rectangles, navigation). No Unity objects are made here.
    /// </summary>
    public static class CharacterShowcaseData
    {
        public const int SlideCount = 4;
        /// <summary>Flipbook: each slide holds this long, the last FadeSeconds of it cross-fading into the next.</summary>
        public const float SlideSeconds = 2f, FadeSeconds = 0.4f;
        /// <summary>A clip that is not ready by then plays as the flipbook instead.</summary>
        public const float PrepareTimeout = 2f;
        public const int ClipW = 640, ClipH = 360, StillW = 1280, StillH = 720, ClipFrames = 240, ClipFps = 30;
        public const string Folder = "Characters";
        /// <summary>Default dodge keys while no Dodge binding exists yet (J adds it: L SHIFT and the east face button).</summary>
        public const string DodgeKeyFallback = "Left Shift", DodgePadFallback = "B";

        public static readonly ShowcaseClass[] All =
        {
            new ShowcaseClass
            {
                Id = ClassId.Warrior, FileId = "warrior", Name = "WARRIOR", Role = "Holds the hill.",
                Summary = "The front line: a wide shield, the most health, and no plans to leave the hill.",
                FAction = "", Ability = "SHIELD UP",
                Slides = new[]
                {
                    S("CLEAVE", "Your attack is a wide swing across the front."),
                    S("SHIELD UP", "Your ability raises the shield and takes most of the sting out of hits from the front."),
                    S("HOLD THE HILL", "Stand on the hill with more friends than they have and the clock is yours."),
                    S("JUMP AND DODGE", "Jump clears gaps and short walls; a dodge dashes you out of a hit."),
                },
            },
            new ShowcaseClass
            {
                Id = ClassId.Farmer, FileId = "farmer", Name = "FARMER", Role = "Feeds the egg.",
                Summary = "Gathers fastest, carries the most, and decides what the egg grows into.",
                FAction = "", Ability = "ROOT SEEDS",
                Slides = new[]
                {
                    S("GATHER", "Hold E on plants. The Farmer gathers fastest and carries three."),
                    S("FEED THE EGG", "Take biomass to the egg and hold E. It remembers who feeds it."),
                    S("ROOT SEEDS", "Your ability lobs seeds that slow every enemy near the landing."),
                    S("JUMP AND DODGE", "Jump clears gaps and short walls; a dodge dashes you out of a hit."),
                },
            },
            new ShowcaseClass
            {
                Id = ClassId.Miner, FileId = "miner", Name = "MINER", Role = "Mines ore for the hut.",
                Summary = "Mines ore to upgrade your team's Blacksmith hut, and dark crystals that can turn the Kaiju wild.",
                FAction = "MINE", Ability = "HAUL",
                Slides = new[]
                {
                    S("MINE", "MINE: hold near an ore vein (up to 3 ore)."),
                    S("HAUL", "HAUL: run faster with your load for 5 s."),
                    S("BANK AT THE HUT", "Bank ore at the hut. Enough ore raises its level."),
                    S("HUT LEVELS", "Each hut level gives your whole team +5% health, damage and healing."),
                },
            },
            new ShowcaseClass
            {
                Id = ClassId.Demolitions, FileId = "demolitions", Name = "DEMOLITIONIST", Role = "Rewrites the map.",
                Summary = "Her attack throws TNT that blows holes in the ground; her ability lays mines.",
                FAction = "THROW TNT", Ability = "LAND MINE",
                Slides = new[]
                {
                    S("TNT", "Your attack lobs TNT at your aim. After a short fuse it blows."),
                    S("CRATER", "The blast opens a crater. Nobody walks across a hole."),
                    S("LAND MINE", "Your ability lays a mine that arms in a moment and hits whoever steps on it."),
                    S("JUMP AND DODGE", "Jump clears gaps and short walls; a dodge dashes you out of a hit."),
                },
            },
            new ShowcaseClass
            {
                Id = ClassId.Builder, FileId = "builder", Name = "BUILDER", Role = "Builds walls, ladders and bridges.",
                Summary = "One tool wheel: dig, place, ladder, bridge, short wall, tall wall, block. F uses the tool on the outlined spot.",
                FAction = "USE TOOL", Ability = "USE TOOL",
                Slides = new[]
                {
                    S("USE TOOL", "F uses the selected tool where the outline shows. Right click does the same."),
                    S("TOOL WHEEL", "Dig, place, ladder, bridge, short wall, tall wall (hut level 2), block. The wheel or 1-7 picks one."),
                    S("LADDERS AND BRIDGES", "A ladder lies flat against the cliff you face; a bridge spans straight out from your edge."),
                    S("JUMP AND DODGE", "Jump clears gaps and short walls; a dodge dashes you out of a hit."),
                },
            },
            new ShowcaseClass
            {
                Id = ClassId.Healer, FileId = "healer", Name = "HEALER", Role = "Keeps the team up.",
                Summary = "Mends allies, heals in a burst and purifies the egg. She never deals damage.",
                FAction = "", Ability = "HEALING PULSE",
                Slides = new[]
                {
                    S("MEND", "Your attack mends an ally in front of you."),
                    S("HEALING PULSE", "Your ability heals everyone close to you in one burst."),
                    S("PURIFY", "Hold E at the egg to clear corruption out of it."),
                    S("JUMP AND DODGE", "Jump clears gaps and short walls; a dodge dashes you out of a hit."),
                },
            },
        };

        static ShowcaseSlide S(string title, string line) => new ShowcaseSlide { Title = title, Line = line };

        public static ShowcaseClass Of(ClassId c)
        {
            int i = (int)c;
            return i >= 0 && i < All.Length ? All[i] : All[0];
        }

        // ---------------------------------------------------------------- paths

        /// <summary>StreamingAssets/Characters/&lt;classid&gt;.webm (VP8, 640x360, 8 s, no audio, looping).</summary>
        public static string ClipPath(ClassId c) => Path.Combine(Application.streamingAssetsPath, Folder, Of(c).FileId + ".webm");

        /// <summary>The n-th slide still (1..4), StreamingAssets/Characters/still-&lt;classid&gt;-&lt;n&gt;.png.</summary>
        public static string StillPath(ClassId c, int n) => Path.Combine(Application.streamingAssetsPath, Folder, StillName(Of(c).FileId, n));

        public static string StillName(string fileId, int n) => "still-" + fileId + "-" + n + ".png";
        public static string ClipFolderName(string fileId) => "clip-" + fileId;
        public static string FrameName(int i) => i.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + ".png";

        public static bool FileThere(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path); }
            catch (System.Exception) { return false; }
        }

        // ---------------------------------------------------------------- watch: clip or flipbook

        /// <summary>WATCH plays the clip only when the file is there; a clip that fails or is not ready in time falls back.</summary>
        public static bool UseClip(bool fileThere, bool failed, float waited, bool prepared) =>
            fileThere && !failed && (prepared || waited <= PrepareTimeout);

        /// <summary>The flipbook at time t: the slide shown, the one fading in, and the blend 0..1 (pure).</summary>
        public static void Flipbook(float t, out int slide, out int next, out float blend)
        {
            if (t < 0f) t = 0f;
            float cycle = SlideSeconds * SlideCount;
            t %= cycle;
            slide = Mathf.Clamp((int)(t / SlideSeconds), 0, SlideCount - 1);
            next = (slide + 1) % SlideCount;
            float into = t - slide * SlideSeconds;
            blend = into <= SlideSeconds - FadeSeconds ? 0f : Mathf.Clamp01((into - (SlideSeconds - FadeSeconds)) / FadeSeconds);
        }

        // ---------------------------------------------------------------- controls

        /// <summary>The CONTROLS rows of a class, in screen order (Next tool for the Builder only).</summary>
        public static ShowcaseControl[] ControlsFor(ClassId c)
        {
            ShowcaseClass k = Of(c);
            bool builder = c == ClassId.Builder;
            var rows = new ShowcaseControl[builder ? 8 : 7];   // b13 r3 integrator: Dodge row (dodge merged) and the Builder's Switch piece row (walls merged)
            rows[0] = new ShowcaseControl { Label = "Move", Action = "Move", What = "Walk" };
            rows[1] = new ShowcaseControl { Label = "Attack", Action = "Attack", What = c == ClassId.Healer ? "Mend an ally" : c == ClassId.Demolitions ? "Throw TNT" : "Swing" };
            rows[2] = new ShowcaseControl { Label = "Ability", Action = "Ability", What = Sentence(k.Ability) };
            rows[3] = new ShowcaseControl { Label = "F action", Action = "DigPlace", What = k.FAction.Length > 0 ? Sentence(k.FAction) : "Not used by this class" };
            rows[4] = new ShowcaseControl { Label = "Interact", Action = "Interact", What = c == ClassId.Miner ? "Gather, carry, bank ore" : "Gather, carry, feed" };
            rows[5] = new ShowcaseControl { Label = "Jump", Action = "Jump", What = "Clears gaps and short walls" };
            rows[6] = new ShowcaseControl { Label = "Dodge", Action = "Dodge", What = "Quick dash, 3 s to recharge" };
            if (builder) rows[7] = new ShowcaseControl { Label = "Next tool", Action = "SwitchDigPlace", What = "Dig, place, ladder, bridge, short wall, tall wall, block (or 1-7)" };
            return rows;
        }

        static string Sentence(string upper)
        {
            if (string.IsNullOrEmpty(upper)) return string.Empty;
            string lower = upper.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        /// <summary>The key words of an action on a device from the CURRENT bindings (rebinds show), with the dodge defaults
        /// while no Dodge binding exists. Allocates; called when the tab opens or the bindings change, never per frame.</summary>
        public static string KeyWords(string action, ControlDevice device)
        {
            string s = action == "Move" ? InputGlyphs.MoveWords(device) : InputGlyphs.Words(action, device);
            if (string.IsNullOrEmpty(s) || s == "-")
            {
                if (action == "Dodge") return device == ControlDevice.Gamepad ? DodgePadFallback : DodgeKeyFallback;
                return "-";
            }
            return s;
        }

        // ---------------------------------------------------------------- layout (1920 x 1080 reference, top-left origin)

        public static readonly Rect Title = new Rect(460f, 40f, 1000f, 80f);
        public const float TileX = 120f, TileY = 150f, TileW = 520f, TileH = 118f, TileGap = 12f;
        public static readonly Rect Pane = new Rect(680f, 230f, 1120f, 688f);
        public const float TabY = 150f, TabW = 360f, TabH = 64f, TabGap = 20f;
        public static readonly Rect Summary = new Rect(710f, 246f, 1060f, 60f);
        public static readonly Rect Video = new Rect(760f, 316f, 960f, 540f);
        public static readonly Rect VideoCaption = new Rect(710f, 866f, 1060f, 40f);
        public static readonly Rect Still = new Rect(840f, 316f, 800f, 450f);
        public static readonly Rect SlideTitle = new Rect(710f, 780f, 1060f, 44f);
        public static readonly Rect SlideLine = new Rect(710f, 826f, 1060f, 50f);
        public const float DotY = 884f, DotSize = 18f, DotGap = 14f;
        public static readonly Rect PrevArrow = new Rect(716f, 501f, 80f, 80f), NextArrow = new Rect(1684f, 501f, 80f, 80f);
        public const float CtrlY = 316f, CtrlRowH = 64f, CtrlLabelX = 710f, CtrlLabelW = 420f, CtrlKeyX = 1150f, CtrlPadX = 1470f, CtrlKeyW = 300f;
        public static readonly Rect Back = new Rect(120f, 950f, 300f, 70f);
        public static readonly Rect Hint = new Rect(460f, 960f, 1340f, 50f);

        public static Rect Tile(int i) => new Rect(TileX, TileY + i * (TileH + TileGap), TileW, TileH);
        public static Rect Tab(int i) => new Rect(Pane.x + i * (TabW + TabGap), TabY, TabW, TabH);
        public static Rect CtrlRow(int i) => new Rect(CtrlLabelX, CtrlY + 48f + i * CtrlRowH, CtrlKeyX + CtrlKeyW * 2f + 20f - CtrlLabelX, CtrlRowH - 6f);

        /// <summary>A reference rect in screen pixels for a w x h screen (the canvas is the 1920 x 1080 space, centred and
        /// scaled by HudLayout.Scale); y measured from the top.</summary>
        public static Rect ToPixels(Rect r, float w, float h)
        {
            float s = HudLayout.Scale(w, h);
            float ox = (w - HudLayout.RefW * s) * 0.5f, oy = (h - HudLayout.RefH * s) * 0.5f;
            return new Rect(ox + r.x * s, oy + r.y * s, r.width * s, r.height * s);
        }

        /// <summary>Every fixed rect of the screen with its name (tests print and check them).</summary>
        public static (string name, Rect rect)[] LayoutRects()
        {
            var list = new System.Collections.Generic.List<(string, Rect)>
            {
                ("title", Title), ("pane", Pane), ("summary", Summary), ("video", Video), ("video caption", VideoCaption),
                ("still", Still), ("slide title", SlideTitle), ("slide line", SlideLine), ("prev", PrevArrow), ("next", NextArrow),
                ("back", Back), ("hint", Hint),
            };
            for (int i = 0; i < All.Length; i++) list.Add(("tile " + i, Tile(i)));
            for (int i = 0; i < 3; i++) list.Add(("tab " + i, Tab(i)));
            for (int i = 0; i < 8; i++) list.Add(("controls row " + i, CtrlRow(i)));
            return list.ToArray();
        }
    }

    public enum ShowcaseTab { Watch = 0, HowItWorks = 1, Controls = 2 }

    /// <summary>One press on the CHARACTERS screen, from any device.</summary>
    public enum ShowcaseInput { None, Up, Down, Left, Right, PrevTab, NextTab, Select, Back }

    /// <summary>The screen's focus: the class, the tab, the slide, and whether the focus is in the right pane.</summary>
    public struct ShowcaseNav
    {
        public int Class;
        public ShowcaseTab Tab;
        public int Slide;
        public bool InPane;
        public bool Closed;

        static int Wrap(int i, int n) => ((i % n) + n) % n;

        /// <summary>
        /// The navigation rule (pure; tests): up / down pick the class (from the tiles, or from the pane too, so the tab
        /// stays and the class changes), Q / E or LB / RB switch the tab anywhere, left / right step the slides on HOW IT
        /// WORKS and WATCH, Enter / A on a tile moves into the pane, Escape / B leaves the pane, then the screen.
        /// </summary>
        public static ShowcaseNav Step(ShowcaseNav s, ShowcaseInput input, int classes)
        {
            switch (input)
            {
                case ShowcaseInput.Up: s.Class = Wrap(s.Class - 1, classes); s.Slide = 0; break;
                case ShowcaseInput.Down: s.Class = Wrap(s.Class + 1, classes); s.Slide = 0; break;
                case ShowcaseInput.PrevTab: s.Tab = (ShowcaseTab)Wrap((int)s.Tab - 1, 3); s.Slide = 0; break;
                case ShowcaseInput.NextTab: s.Tab = (ShowcaseTab)Wrap((int)s.Tab + 1, 3); s.Slide = 0; break;
                case ShowcaseInput.Left: if (s.Tab != ShowcaseTab.Controls) s.Slide = Wrap(s.Slide - 1, CharacterShowcaseData.SlideCount); break;
                case ShowcaseInput.Right: if (s.Tab != ShowcaseTab.Controls) s.Slide = Wrap(s.Slide + 1, CharacterShowcaseData.SlideCount); break;
                case ShowcaseInput.Select: s.InPane = true; break;
                case ShowcaseInput.Back: if (s.InPane) s.InPane = false; else s.Closed = true; break;
            }
            return s;
        }
    }
}

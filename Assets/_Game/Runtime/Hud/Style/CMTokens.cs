using System;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Style
{
    /// <summary>One named colour of the bible (p05) or a value sampled from a bible page.</summary>
    public readonly struct Swatch
    {
        public readonly string Name;
        public readonly string Hex;
        /// <summary>Where the hex comes from: "bible p05 #01" or "sampled p15 (U-021, pending Jeisil)".</summary>
        public readonly string Source;
        public readonly Color Color;

        public Swatch(string name, string hex, string source)
        {
            Name = name;
            Hex = hex;
            Source = source;
            Color = CMTokens.ParseHex(hex);
        }
    }

    /// <summary>A typeface the game ships, with its licence.</summary>
    public readonly struct Typeface
    {
        public readonly string Role;
        public readonly string Family;
        /// <summary>Resources path (no extension) of the shipped TTF.</summary>
        public readonly string Resource;
        public readonly string Licence;

        public Typeface(string role, string family, string resource, string licence)
        {
            Role = role;
            Family = family;
            Resource = resource;
            Licence = licence;
        }
    }

    /// <summary>
    /// Colour and type tokens from the Visual Asset Bible p05, used by the HUD, the title screen and (as shader globals,
    /// PushShaderGlobals) by shaders. The eight bible hexes are the same strings as Sim/Config/Palette.cs (EditMode test
    /// CMTokensTests holds both to the bible). Fonts are the bible's own faces; all three are SIL Open Font License 1.1,
    /// which allows bundling in a sold game (the licence text ships next to each TTF in Resources/UI/Fonts).
    /// </summary>
    public static class CMTokens
    {
        // ---- bible p05, exact hex ----
        public static readonly Swatch VoidNavy = new Swatch("Void Navy", Palette.VoidNavy, "bible p05 #01");
        public static readonly Swatch Azure = new Swatch("Azure Team", Palette.Azure, "bible p05 #02");
        public static readonly Swatch Crimson = new Swatch("Crimson Team", Palette.Crimson, "bible p05 #03");
        public static readonly Swatch WarmCream = new Swatch("Warm Cream", Palette.Cream, "bible p05 #04");
        public static readonly Swatch Biomass = new Swatch("Biomass", Palette.Biomass, "bible p05 #05");
        public static readonly Swatch Corruption = new Swatch("Corruption", Palette.Corruption, "bible p05 #06");
        public static readonly Swatch Stone = new Swatch("Stone", Palette.Stone, "bible p05 #07");
        public static readonly Swatch ToolBrass = new Swatch("Tool Brass", Palette.Brass, "bible p05 #08");

        // ---- world-only, art direction 2.4 ----
        public static readonly Swatch SkyTop = new Swatch("Sky Top", Palette.SkyTop, "art direction 2.4 (CM-24)");
        public static readonly Swatch SkyHorizon = new Swatch("Sky Horizon", Palette.SkyHorizon, "art direction 2.4 (CM-24)");
        public static readonly Swatch Meat = new Swatch("Meat", Palette.Meat, "art direction 2.4 (CM-62)");
        public static readonly Swatch EggSeam = new Swatch("Egg Seam", "#4FDDC3", "art direction 2.4 item 5");

        // ---- HUD values the bible draws but prints no hex for (U-021: sampled, pending Jeisil's confirmation) ----
        public static readonly Swatch MeterOrange = new Swatch("Kaiju Meter Orange", "#F9A858", "sampled p15 kaiju meter (U-021, pending Jeisil)");
        public static readonly Swatch MeterEmpty = new Swatch("Meter Empty", "#2E304A", "sampled p15 kaiju meter (U-021, pending Jeisil)");
        public static readonly Swatch PanelNavy = new Swatch("Panel Navy", "#1B314D", "sampled p15 team panel (U-021, pending Jeisil)");

        // ---- text inks (polish pass 1): lighter steps of a bible colour for TEXT on Panel Navy / Void Navy, so every
        // line reads at 4.5 : 1 or better (Crimson #FF5A5F is 4.32 on Panel Navy, Stone #6F7282 is 2.77). Fills, rims and
        // the world keep the bible colours; only text uses these. UiContrast and PolishPassTests hold the ratios. ----
        public static readonly Swatch CrimsonInk = new Swatch("Crimson Ink", "#FF7276", "polish pass 1: Crimson lifted to 4.97 : 1 on Panel Navy");
        public static readonly Swatch StoneInk = new Swatch("Stone Ink", "#9A9DAE", "polish pass 1: Stone lifted to 4.90 : 1 on Panel Navy");

        /// <summary>A team's colour for TEXT on the navy panels (Azure as is, Crimson lifted).</summary>
        public static Color TeamInk(TeamId t) => ColourBlindTeams ? Team(t) : t == TeamId.Azure ? Azure.Color : CrimsonInk.Color;

        // ---- controls-1 round 2: the colour-blind team palette (Settings, Accessibility, COLOUR-BLIND TEAMS) ----
        // A blue / amber pair: amber and blue stay apart under all three colour-vision deficiencies (red-weak, green-weak,
        // blue-weak), where the bible's cyan / coral pair is weakest for red-weak players. Both read at 4.5 : 1 or better on
        // Panel Navy and Void Navy, so they double as text inks. Numbers: ControlPresetsTests (Machado 2009 simulation, CIE
        // Lab distance, WCAG contrast). The rings keep their shape cue as well: allies solid, enemies dashed, yours ticked.
        public static readonly Swatch AzureColourSafe = new Swatch("Azure (colour-blind)", "#3D9BFF", "controls-1: blue of the blue / amber pair");
        public static readonly Swatch CrimsonColourSafe = new Swatch("Crimson (colour-blind)", "#FFB000", "controls-1: amber of the blue / amber pair");

        /// <summary>The colour-blind team palette is on (GameSettings.ColourBlindTeams keeps it in step with the setting).</summary>
        public static bool ColourBlindTeams { get; set; }

        /// <summary>Every token, bible first (order is the p05 order).</summary>
        public static readonly Swatch[] All =
        {
            VoidNavy, Azure, Crimson, WarmCream, Biomass, Corruption, Stone, ToolBrass,
            SkyTop, SkyHorizon, Meat, EggSeam, MeterOrange, MeterEmpty, PanelNavy,
        };

        /// <summary>The eight p05 swatches.</summary>
        public static readonly Swatch[] Bible = { VoidNavy, Azure, Crimson, WarmCream, Biomass, Corruption, Stone, ToolBrass };

        // ---- type, bible p05 type stack ----
        public const string OflLicence = "SIL Open Font License 1.1";
        public static readonly Typeface Display = new Typeface("display", "Bungee", "UI/Fonts/Bungee-Regular", OflLicence);
        public static readonly Typeface Ui = new Typeface("ui", "Nunito Sans ExtraBold", "UI/Fonts/NunitoSans-ExtraBold", OflLicence);
        public static readonly Typeface Mono = new Typeface("mono", "JetBrains Mono ExtraBold", "UI/Fonts/JetBrainsMono-ExtraBold", OflLicence);
        public static readonly Typeface[] Typefaces = { Display, Ui, Mono };

        // ---- HUD craft (art direction 6.5) ----
        /// <summary>Outer Void Navy outline in px at 1080p.</summary>
        public const float OutlinePx = 3f;
        /// <summary>Warm Cream inner rim in px at 1080p.</summary>
        public const float RimPx = 2f;

        public static Color Team(TeamId t) => ColourBlindTeams
            ? (t == TeamId.Azure ? AzureColourSafe.Color : CrimsonColourSafe.Color)
            : (t == TeamId.Azure ? Azure.Color : Crimson.Color);

        public static Color ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length != 7) throw new FormatException("expected #RRGGBB: " + hex);
            int v = Convert.ToInt32(hex.Substring(1), 16);
            return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        }

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        static Font[] _fonts;

        /// <summary>The shipped font of a role (loaded once from Resources).</summary>
        public static Font Font(Typeface t)
        {
            if (_fonts == null) _fonts = new Font[Typefaces.Length];
            int i = Array.IndexOf(Typefaces, t);
            if (i < 0) i = 1;
            if (_fonts[i] == null) _fonts[i] = Resources.Load<Font>(Typefaces[i].Resource);
            return _fonts[i];
        }

        /// <summary>Shader property id of a token's global colour: "_CM_" + name without spaces.</summary>
        public static string GlobalName(Swatch s) => "_CM_" + s.Name.Replace(" ", string.Empty);

        /// <summary>Publishes every token as a global shader colour (_CM_VoidNavy, _CM_AzureTeam, ...).</summary>
        public static void PushShaderGlobals()
        {
            for (int i = 0; i < All.Length; i++) Shader.SetGlobalColor(GlobalName(All[i]), All[i].Color);
        }
    }
}

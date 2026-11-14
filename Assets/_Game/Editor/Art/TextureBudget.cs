using UnityEditor;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// perf/app-size-1 (2026-11-14): one table of texture rules for everything the game loads from Resources, so the
    /// Mac app stays small. The 1.0 GB Mac build carried 785 MB of textures (resources.assets.resS 817 MB); the build
    /// report showed every NPOT texture with mipmaps at 4 bytes a pixel (the 76 story stills, the logos, the icons): the
    /// high-quality BC7 setting fell back to uncompressed RGBA32 on those, while the same images at normal quality (DXT)
    /// or without mips came out at 1 byte a pixel (the story-card placeholders, the big-moment art, the mascot).
    /// The rules: DXT1 (opaque, 0.5 byte a pixel) or DXT5 (alpha, 1 byte a pixel) on desktop, a size cap per use, mips
    /// only where the art is drawn scaled down, never readable, crunched where the art is a fallback that loads once.
    /// </summary>
    public static class TextureBudget
    {
        /// <summary>Bumped when a rule changes, so every covered texture re-imports (UiTexturePostprocessor.GetVersion).</summary>
        public const uint RulesVersion = 1;

        public const string UiRoot = "Assets/_Game/Resources/UI/";
        public const string StoryRoot = "Assets/_Game/Resources/Story/";
        public const string CardsRoot = "Assets/_Game/Art/Story/Resources/StoryCards/";

        /// <summary>Estimated in-build bytes of every covered texture together (the EditMode test's ceiling).</summary>
        public const long TotalBudgetBytes = 130L * 1024 * 1024;

        public struct Rule
        {
            public string Category;
            public int Max;          // longest side, pixels
            public bool Mips;
            public bool Crunch;
            public int CrunchQuality;
        }

        public static bool Covers(string path) =>
            path.StartsWith(UiRoot) || path.StartsWith(StoryRoot) || path.StartsWith(CardsRoot);

        /// <summary>The rule for a covered texture path (pure; tests).</summary>
        public static Rule For(string path)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (path.StartsWith(StoryRoot))
            {
                // Big-moment cut-ins draw in a match, up to the full width: native 1920, never crunched (no load hitch).
                if (name.StartsWith("cutin_")) return R("story cut-in", 2048, false, false);
                // The motion comic is the intro's fallback when the film cannot play: 1920x1080 stage, drawn about 1:1.
                // Glow and blur companions are soft by design: 1024 is enough.
                bool soft = name.EndsWith("_glow") || name.EndsWith("_blur");
                return R(soft ? "story soft layer" : "story layer", soft ? 1024 : 2048, false, true);
            }
            if (path.StartsWith(CardsRoot)) return R("story card", 1024, false, true);   // the oldest fallback (placeholders)
            if (path.Contains("/UI/Logo/")) return R("logo", 2048, true, false);         // drawn scaled down on the title
            if (path.Contains("/UI/Mascot/")) return R("mascot", 512, true, false);      // 148 px portrait, 84 px chip
            if (path.Contains("/UI/Icons/")) return R("icon", 512, true, false);         // the @4x icons draw smaller
            if (path.Contains("/UI/BigMoments/")) return R("big moment", 1024, false, false);
            return R("ui", 1024, false, false);                                          // minimap and the rest
        }

        static Rule R(string c, int max, bool mips, bool crunch) =>
            new Rule { Category = c, Max = max, Mips = mips, Crunch = crunch, CrunchQuality = 75 };

        /// <summary>Bytes in the build for a source of w x h under a rule (block formats; pure; tests).</summary>
        public static long EstimateBytes(int w, int h, bool alpha, Rule r)
        {
            float s = System.Math.Min(1f, r.Max / (float)System.Math.Max(1, System.Math.Max(w, h)));
            double px = System.Math.Max(4, System.Math.Round(w * s)) * System.Math.Max(4, System.Math.Round(h * s));
            double bytes = px * (alpha ? 1.0 : 0.5) * (r.Mips ? 4.0 / 3.0 : 1.0);
            if (r.Crunch) bytes *= 0.45;   // crunch on disk, measured range 0.3 to 0.6 for painted art; a ceiling, not a promise
            return (long)bytes;
        }

        /// <summary>Applies a rule to an importer (the Standalone override is what the Mac and Windows players get).</summary>
        public static void Apply(TextureImporter ti, string path, bool alpha)
        {
            Rule r = For(path);
            ti.textureType = TextureImporterType.Default;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.mipmapEnabled = r.Mips;
            ti.filterMode = r.Mips ? UnityEngine.FilterMode.Trilinear : UnityEngine.FilterMode.Bilinear;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.alphaIsTransparency = alpha;
            ti.isReadable = false;
            ti.sRGBTexture = true;
            ti.maxTextureSize = r.Max;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.crunchedCompression = r.Crunch;
            ti.compressionQuality = r.CrunchQuality;
            TextureImporterPlatformSettings s = ti.GetPlatformTextureSettings("Standalone");
            s.overridden = true;
            s.maxTextureSize = r.Max;
            s.format = FormatFor(alpha, r.Crunch);
            s.textureCompression = TextureImporterCompression.Compressed;
            s.crunchedCompression = r.Crunch;
            s.compressionQuality = r.CrunchQuality;
            ti.SetPlatformTextureSettings(s);
        }

        public static TextureImporterFormat FormatFor(bool alpha, bool crunch) =>
            alpha ? (crunch ? TextureImporterFormat.DXT5Crunched : TextureImporterFormat.DXT5)
                  : (crunch ? TextureImporterFormat.DXT1Crunched : TextureImporterFormat.DXT1);
    }
}

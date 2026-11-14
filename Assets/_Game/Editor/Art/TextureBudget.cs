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
        public const uint RulesVersion = 2;   // 2: no resize that leaves a side off a multiple of 4; mipped art made POT

        public const string UiRoot = "Assets/_Game/Resources/UI/";
        public const string StoryRoot = "Assets/_Game/Resources/Story/";
        public const string CardsRoot = "Assets/_Game/Art/Story/Resources/StoryCards/";

        /// <summary>Estimated in-build bytes of every covered texture together (the EditMode test's ceiling).</summary>
        public const long TotalBudgetBytes = 180L * 1024 * 1024;

        public struct Rule
        {
            public string Category;
            public int Max;          // longest side, pixels
            public bool Mips;
            public bool Crunch;
            public int CrunchQuality;
            /// <summary>Rules 2: mipped NPOT art (logos, icons) shipped uncompressed in build 1822b32, and so did every
            /// texture whose resize left a side off a multiple of 4 (2560x1428 to 2048x1142). Mipped art is scaled to a
            /// power of two; story art keeps its source size (every source side is a multiple of 4).</summary>
            public TextureImporterNPOTScale Npot;
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
                // Rules 2: never resized (StorySourceMax covers the largest source, 2560): a resize to 2048 gave 2048x1142,
                // which the block compressor refuses, and the still shipped at 4 bytes a pixel. Crunch keeps the disk small.
                // The motion comic is the intro's fallback when the film cannot play: 1920x1080 stage, drawn about 1:1.
                // Glow and blur companions are soft by design: 1024 is enough.
                bool soft = name.EndsWith("_glow") || name.EndsWith("_blur");
                return R(soft ? "story soft layer" : "story layer", StorySourceMax, false, true);
            }
            if (path.StartsWith(CardsRoot)) return R("story card", 1024, false, true);   // the oldest fallback (placeholders)
            // Mipped art is made a power of two: up for the logos (the title's sharpest art), to the nearest for icons.
            if (path.Contains("/UI/Logo/")) return R("logo", 2048, true, false, TextureImporterNPOTScale.ToLarger);
            if (path.Contains("/UI/Mascot/")) return R("mascot", 512, true, false, TextureImporterNPOTScale.ToNearest);
            if (path.Contains("/UI/Icons/")) return R("icon", 512, true, false, TextureImporterNPOTScale.ToNearest);
            if (path.Contains("/UI/BigMoments/")) return R("big moment", 1024, false, false);
            return R("ui", 1024, false, false);                                          // minimap and the rest
        }

        /// <summary>The largest story source side (2560): story art is never resized (rules 2).</summary>
        public const int StorySourceMax = 4096;

        static Rule R(string c, int max, bool mips, bool crunch, TextureImporterNPOTScale npot = TextureImporterNPOTScale.None) =>
            new Rule { Category = c, Max = max, Mips = mips, Crunch = crunch, CrunchQuality = 75, Npot = npot };

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
            ti.npotScale = r.Npot;
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

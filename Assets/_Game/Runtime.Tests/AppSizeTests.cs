using System.Collections.Generic;
using System.IO;
using System.Linq;
using CriticalMass.Editor.Art;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// perf/app-size-1 (2026-11-14): the app stays small. Every texture the game loads from Resources is imported within
    /// its category's size, block-compressed, never readable, with mipmaps only where the rule keeps them; the covered
    /// textures together stay under a memory ceiling and their source files under a source ceiling; the intro film stays
    /// under its size. The ceilings here are written out on their own (not read from TextureBudget), so a rule that grows
    /// fails here first.
    /// </summary>
    public sealed class AppSizeTests
    {
        static readonly string[] Roots = { "Assets/_Game/Resources/UI", "Assets/_Game/Resources/Story", "Assets/_Game/Art/Story/Resources/StoryCards" };

        /// <summary>Longest side per category, pixels: a top-down game draws nothing bigger than its 1920 px stage.</summary>
        static readonly Dictionary<string, int> Ceiling = new Dictionary<string, int>
        {
            { "story cut-in", 2048 }, { "story layer", 2560 }, { "story soft layer", 2560 }, { "story card", 1024 },
            { "logo", 2048 }, { "mascot", 512 }, { "icon", 512 }, { "big moment", 1024 }, { "ui", 1024 },
        };

        public const long MemoryCeilingBytes = 180L * 1024 * 1024;   // all covered textures loaded at once (was 785 MB in the build); story art unresized since rules 2
        public const long SourceCeilingBytes = 100L * 1024 * 1024;   // source images under the covered roots (82.7 MB on 2026-11-14)
        public const long FilmCeilingBytes = 35L * 1024 * 1024;      // StreamingAssets/Story/intro-film-1.mp4 (30 MB, was 55)

        static IEnumerable<string> CoveredPaths() =>
            AssetDatabase.FindAssets("t:Texture2D", Roots).Select(AssetDatabase.GUIDToAssetPath).Where(TextureBudget.Covers).Distinct();

        [Test]
        public void EveryCoveredTextureIsWithinItsCategory()
        {
            var bad = new List<string>();
            int seen = 0;
            foreach (string path in CoveredPaths())
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                seen++;
                TextureBudget.Rule r = TextureBudget.For(path);
                Assert.IsTrue(Ceiling.TryGetValue(r.Category, out int max), "a new category needs a ceiling here: " + r.Category);
                if (r.Max < TextureBudget.StorySourceMax) Assert.LessOrEqual(r.Max, max, r.Category + ": the rule grew past the ceiling");
                // Rules 2: a side off a multiple of 4 is refused by the block compressor (build 1822b32 shipped those at 4 B/px).
                if (tex.width % 4 != 0 || tex.height % 4 != 0) bad.Add($"{path} {tex.width}x{tex.height}: a side is not a multiple of 4");
                if (Mathf.Max(tex.width, tex.height) > max) bad.Add($"{path} {tex.width}x{tex.height} over {max}");
                if (tex.isReadable) bad.Add(path + " readable (a second copy in memory)");
                if (!r.Mips && tex.mipmapCount > 1) bad.Add(path + " has mipmaps");
                if (Mathf.Max(tex.width, tex.height) >= 64 && !GraphicsFormatUtility.IsCompressedFormat(tex.graphicsFormat))
                    bad.Add($"{path} {tex.graphicsFormat} (not block-compressed: 4 bytes a pixel)");
            }
            Assert.Greater(seen, 300, "the covered textures were found");
            Assert.IsEmpty(bad, string.Join("\n", bad.Take(30)));
        }

        [Test]
        public void CoveredTexturesStayUnderTheMemoryCeiling()
        {
            long total = 0;
            foreach (string path in CoveredPaths())
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                for (int m = 0; m < tex.mipmapCount; m++)
                    total += GraphicsFormatUtility.ComputeMipmapSize(Mathf.Max(1, tex.width >> m), Mathf.Max(1, tex.height >> m), tex.graphicsFormat);
            }
            TestContext.WriteLine($"covered textures {total / 1048576.0:F1} MB in memory");
            Assert.LessOrEqual(total, MemoryCeilingBytes);
        }

        [Test]
        public void SourceImagesAndTheFilmStayUnderTheirCeilings()
        {
            long src = 0;
            foreach (string root in Roots)
                foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (f.Contains("~") || f.EndsWith(".meta")) continue;
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    if (e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".tga") src += new FileInfo(f).Length;
                }
            Assert.LessOrEqual(src, SourceCeilingBytes, "source images under Resources");
            Assert.LessOrEqual(new FileInfo("Assets/StreamingAssets/Story/intro-film-1.mp4").Length, FilmCeilingBytes, "intro film");
        }

        [Test]
        public void RulesKeepDrawnSizesAndCompressEverything()
        {
            Assert.AreEqual(TextureBudget.StorySourceMax, TextureBudget.For("Assets/_Game/Resources/Story/sky_storm.jpg").Max, "story art is never resized");
            Assert.AreEqual(UnityEditor.TextureImporterNPOTScale.ToLarger, TextureBudget.For("Assets/_Game/Resources/UI/Logo/critical-mass-logo@2x.png").Npot, "mipped logos are POT");
            Assert.AreEqual(UnityEditor.TextureImporterNPOTScale.None, TextureBudget.For("Assets/_Game/Resources/Story/shrine_glow.png").Npot);
            Assert.IsFalse(TextureBudget.For("Assets/_Game/Resources/Story/cutin_kaiju.jpg").Crunch, "in-match art never crunched");
            Assert.IsTrue(TextureBudget.For("Assets/_Game/Resources/Story/hero.jpg").Crunch, "the fallback stills are");
            Assert.IsTrue(TextureBudget.For("Assets/_Game/Resources/UI/Logo/critical-mass-logo@2x.png").Mips, "logos draw scaled down");
            Assert.IsFalse(TextureBudget.For("Assets/_Game/Resources/UI/BigMoments/bm-band.png").Mips, "UI at its drawn size");
            Assert.AreEqual(UnityEditor.TextureImporterFormat.DXT1, TextureBudget.FormatFor(false, false));
            Assert.AreEqual(UnityEditor.TextureImporterFormat.DXT5Crunched, TextureBudget.FormatFor(true, true));
            var r = TextureBudget.For("Assets/_Game/Resources/Story/sky_storm.jpg");
            r.Crunch = false;
            Assert.AreEqual(2560L * 1428 / 2, TextureBudget.EstimateBytes(2560, 1428, false, r), 2048, "DXT1 at 0.5 byte a pixel");
            Assert.IsFalse(File.Exists("Assets/_Game/Resources/UI/Logo/critical-mass-badge@4x.png"), "nothing loads the badges above 1x");
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// UI, story and story-card textures (Resources/UI, Resources/Story, Art/Story/Resources/StoryCards), enforced on every
    /// import so a re-import cannot drift. DG-65 (2026-11-12) set BC7, size caps and mips for Resources/UI.
    /// perf/app-size-1 (2026-11-14): the rules live in <see cref="TextureBudget"/> and now cover the story stills too. BC7 on
    /// NPOT art with mipmaps shipped as uncompressed RGBA32 (the logos, the icons, the 76 story stills: 700 MB of the
    /// 785 MB of textures); DXT1/DXT5 at normal quality compress those reliably. GetVersion re-imports every covered
    /// texture when the rules change; TextureBuildCheck names any that still ship uncompressed after a build.
    /// </summary>
    public sealed class UiTexturePostprocessor : AssetPostprocessor
    {
        const string UiRoot = TextureBudget.UiRoot;
        public const int IconMax = 512;
        public const int LogoMax = 2048;

        public override uint GetVersion() => 100 + TextureBudget.RulesVersion;   // DG-65 had no version (0)

        /// <summary>The size cap for a covered texture path (pure; tests).</summary>
        public static int MaxSizeFor(string path) => TextureBudget.For(path).Max;

        /// <summary>Logos, the mascot portraits and the icons keep mipmaps: they are drawn scaled down (pure; tests).</summary>
        public static bool MipsFor(string path) => TextureBudget.For(path).Mips;

        /// <summary>A mascot portrait (Resources/UI/Mascot, MascotGuide.PortraitResource).</summary>
        public static bool IsMascot(string path) => path.Contains("/UI/Mascot/");

        void OnPreprocessTexture()
        {
            if (!TextureBudget.Covers(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            bool alpha = !assetPath.EndsWith(".jpg") && !assetPath.EndsWith(".jpeg") && ti.DoesSourceTextureHaveAlpha();
            TextureBudget.Apply(ti, assetPath, alpha);
        }
    }
}

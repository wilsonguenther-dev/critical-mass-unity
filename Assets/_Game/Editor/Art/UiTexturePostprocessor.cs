using UnityEditor;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// UI textures (Resources/UI), enforced on every import so a re-import cannot drift.
    /// DG-65 (2026-11-12 panel): the 189 UI textures were uncompressed with mipmaps, about 78 MB of graphics memory (the
    /// 4x logo alone 22.9 MB). Now: BC7 on desktop (high-quality block compression, alpha kept), at most 1024 px for icons
    /// and minimap art and 2048 px for logos, no mipmaps except the logos (they are drawn scaled down on the title, where
    /// trilinear mips keep them crisp; a judgment call against the plan's "no mipmaps", listed in the PR). Clamped, alpha
    /// as transparency, sRGB. Fonts and shaders under Resources/UI are not textures and are untouched.
    /// </summary>
    public sealed class UiTexturePostprocessor : AssetPostprocessor
    {
        const string UiRoot = "Assets/_Game/Resources/UI/";
        public const int IconMax = 1024;
        public const int LogoMax = 2048;

        /// <summary>The size cap for a UI texture path (pure; tests).</summary>
        public static int MaxSizeFor(string path) => path.Contains("/Logo/") ? LogoMax : IconMax;

        /// <summary>Only logos keep mipmaps (pure; tests).</summary>
        public static bool MipsFor(string path) => path.Contains("/Logo/");

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiRoot)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.npotScale = TextureImporterNPOTScale.None;
            bool mips = MipsFor(assetPath);
            ti.mipmapEnabled = mips;
            ti.filterMode = mips ? UnityEngine.FilterMode.Trilinear : UnityEngine.FilterMode.Bilinear;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            int max = MaxSizeFor(assetPath);
            ti.maxTextureSize = max;
            ti.sRGBTexture = true;
            TextureImporterPlatformSettings s = ti.GetPlatformTextureSettings("Standalone");
            s.overridden = true;
            s.maxTextureSize = max;
            s.format = TextureImporterFormat.BC7;
            s.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(s);
        }
    }
}

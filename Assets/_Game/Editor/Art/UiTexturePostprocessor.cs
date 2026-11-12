using UnityEditor;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// UI textures (Resources/UI): uncompressed, mipmapped with trilinear filtering so the logo exports stay crisp when drawn
    /// at or below their pixel size, clamped, alpha as transparency. Enforced on every import so a re-import cannot drift.
    /// </summary>
    public sealed class UiTexturePostprocessor : AssetPostprocessor
    {
        const string UiRoot = "Assets/_Game/Resources/UI/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiRoot)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.mipmapEnabled = true;
            ti.filterMode = UnityEngine.FilterMode.Trilinear;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 4096;
            ti.sRGBTexture = true;
        }
    }
}

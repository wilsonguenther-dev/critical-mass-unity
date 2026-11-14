using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// perf/app-size-1: after every player build, one line with the texture total and the covered textures that still
    /// shipped at 3 or more bytes a pixel (uncompressed), so a compressor fallback on the build machine is named in the
    /// build log instead of found in a 1 GB app. The size of each category is logged too (before and after numbers).
    /// </summary>
    public sealed class TextureBuildCheck : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            try
            {
                long total = 0;
                var byCategory = new Dictionary<string, long>();
                var loose = new List<string>();
                foreach (PackedAssets f in report.packedAssets)
                    foreach (PackedAssetInfo c in f.contents)
                    {
                        if (c.type != typeof(Texture2D)) continue;
                        total += (long)c.packedSize;
                        string path = c.sourceAssetPath;
                        if (string.IsNullOrEmpty(path) || !TextureBudget.Covers(path)) continue;
                        TextureBudget.Rule r = TextureBudget.For(path);
                        byCategory.TryGetValue(r.Category, out long had);
                        byCategory[r.Category] = had + (long)c.packedSize;
                        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                        ti.GetSourceTextureWidthAndHeight(out int w, out int h);
                        float s = Math.Min(1f, r.Max / (float)Math.Max(1, Math.Max(w, h)));
                        double px = Math.Max(1.0, Math.Round(w * s) * Math.Round(h * s)) * (r.Mips ? 4.0 / 3.0 : 1.0);
                        double bpp = c.packedSize / px;
                        if (bpp >= 3.0 && px >= 64 * 64) loose.Add($"{path} {bpp:F1} B/px");
                    }
                Debug.Log($"[TextureBudget] build textures {total / 1048576.0:F1} MB; covered: " +
                          string.Join(", ", byCategory.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value / 1048576.0:F1} MB")));
                if (loose.Count > 0)
                    Debug.LogWarning($"[TextureBudget] {loose.Count} covered textures shipped uncompressed: " + string.Join("; ", loose.Take(40)));
            }
            catch (Exception e)
            {
                Debug.Log($"[TextureBudget] build check skipped: {e.Message}");
            }
        }
    }
}

using System.Globalization;
using System.IO;
using System.Text;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Editor
{
    /// <summary>
    /// Bakes the minimap from the sim's own map (SkybreakBasinMap with the default config) at the gameplay camera's yaw:
    /// minimap.json (projection) and the sim-drawn reference image Resources/UI/Minimap/_source~/minimap-sim-ref@1x/2x/4x.png.
    /// hud-art-1 (2026-11-13): the SHIPPED minimap@Nx.png is no longer this drawing; it is the real arena, rendered straight down
    /// by CriticalMass.Editor.Flow.HudArtCaptureTool on this same projection and toon-simplified by _source~/minimap_world.py
    /// (which reads the base pads from the sim reference). MinimapTests.BakedImageIsTheRealArenaOnTheSimMap fails if that image
    /// stops matching the sim map. Batch: -executeMethod CriticalMass.Editor.MinimapBaker.BakeAll
    /// </summary>
    public static class MinimapBaker
    {
        public const string Dir = "Assets/_Game/Resources/UI/Minimap";

        public static MapDef SourceMap(GameConfig c) => SkybreakBasinMap.BuildSkybreakBasin(c);

        [MenuItem("Critical Mass/Bake Minimap")]
        public static void BakeAll()
        {
            GameConfig c = GameConfig.CreateDefault();
            var bake = new MinimapBake(SourceMap(c), c.Render.CameraYawDeg);
            Directory.CreateDirectory(Dir);
            foreach (int tier in new[] { 1, 2, 4 })
            {
                Color32[] px = bake.Render(tier, out int w, out int h);
                Texture2D t = MinimapBake.ToTexture(px, w, h);
                Directory.CreateDirectory(Path.Combine(Dir, "_source~"));
                File.WriteAllBytes(Path.Combine(Dir, "_source~", "minimap-sim-ref@" + tier + "x.png"), t.EncodeToPNG());
                Object.DestroyImmediate(t);
            }
            MinimapProjection p = bake.Proj;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"note\": \"Baked by CriticalMass.Editor.MinimapBaker from SkybreakBasinMap (default config). Camera-up: up on the minimap is up on the screen (gameplay camera yaw ").Append(F(p.YawDeg)).Append(").\",\n");
            sb.Append("  \"yawDeg\": ").Append(F(p.YawDeg)).Append(",\n");
            sb.Append("  \"pxPerUnit1x\": ").Append(F(MinimapProjection.PxPerUnit)).Append(",\n");
            sb.Append("  \"u0\": ").Append(F(p.U0)).Append(",\n  \"v0\": ").Append(F(p.V0)).Append(",\n");
            sb.Append("  \"size1x\": [").Append(p.Width).Append(", ").Append(p.Height).Append("],\n");
            sb.Append("  \"map\": [").Append(p.MapWidth).Append(", ").Append(p.MapHeight).Append("]\n}\n");
            File.WriteAllText(Path.Combine(Dir, "minimap.json"), sb.ToString());
            AssetDatabase.Refresh();
            Debug.Log("[MinimapBaker] baked " + p.Width + "x" + p.Height + " @1x");
        }

        static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}

using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// The map the minimap was baked from (Editor/MinimapBaker): Skybreak Basin with the default config, at the gameplay
    /// camera's yaw. The HUD lays the minimap out from this projection, so the frame, the image and every marker share
    /// one mapping.
    /// </summary>
    public static class MinimapGeometry
    {
        public static readonly MapDef BakedMap = SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateDefault());
        public static readonly MinimapProjection Projection = MinimapProjection.For(BakedMap, GameConfig.CreateDefault().Render.CameraYawDeg);

        /// <summary>Texture tier for a canvas scale: 1x up to 1080p (drawn 1:1), 2x up to 4K, else 4x.</summary>
        public static int TierFor(float canvasScale) => canvasScale <= 1.01f ? 1 : canvasScale <= 2.01f ? 2 : 4;
    }

    /// <summary>
    /// Ground changes since the bake, drawn over the baked image as cell diamonds: dug or broken ground (void now) in the
    /// sky tone, new ground (built or bridged) in grass, broken breakables as rubble, barricades in brass. Rebuilt only
    /// when World.GridVersion changes, never per frame.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MinimapGround : MaskableGraphic
    {
        static readonly Color Hole = CMTokens.ParseHex("#1E2D4A"), NewGround = CMTokens.ParseHex("#77B247"),
            Rubble = CMTokens.ParseHex("#8A6A4C"), Cracked = CMTokens.ParseHex("#A88762"), Brass = CMTokens.ToolBrass.Color,
            Floor = CMTokens.ParseHex("#77B247"), Bridge = CMTokens.ParseHex("#C99258");

        MinimapProjection _proj;
        byte[] _base;
        MapDef _map;
        int _version = int.MinValue;
        public int Changed { get; private set; }

        public void Bind(MinimapProjection proj, MapDef baked)
        {
            _proj = proj;
            _base = baked.Cells;
            _version = int.MinValue;
        }

        /// <summary>Call every frame; does work only when the grid version moved.</summary>
        public void Watch(MapDef map, int gridVersion)
        {
            if (gridVersion == _version && map == _map) return;
            _version = gridVersion;
            _map = map;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Changed = 0;
            if (_proj == null || _map == null || _base == null || _map.Cells == null || _map.Cells.Length != _base.Length) return;
            Rect r = rectTransform.rect;
            Vector2 tl = new Vector2(r.xMin, r.yMax);
            int w = _map.Width;
            for (int i = 0; i < _base.Length; i++)
            {
                byte was = _base[i], now = _map.Cells[i];
                if (was == now) continue;
                Color c;
                if (now == Cell.Void) c = Hole;
                else if (now == Cell.Barricade) c = Brass;
                else if (now == Cell.BreakableDestroyed) c = Rubble;
                else if (now == Cell.BreakableCracked) c = Cracked;
                else if (now == Cell.Bridge) c = Bridge;
                else if (was == Cell.Void) c = NewGround;
                else c = Floor;
                Diamond(vh, tl, i % w, i / w, c);
                Changed++;
            }
        }

        void Diamond(VertexHelper vh, Vector2 tl, int cx, int cz, Color c)
        {
            int s = vh.currentVertCount;
            for (int k = 0; k < 4; k++)
            {
                double x = cx + (k == 1 || k == 2 ? 1 : 0), z = cz + (k >= 2 ? 1 : 0);
                Vector2 p = _proj.ToPixel(x, z);
                vh.AddVert(new Vector3(tl.x + p.x, tl.y - p.y), c, Vector4.zero);
            }
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }
    }
}

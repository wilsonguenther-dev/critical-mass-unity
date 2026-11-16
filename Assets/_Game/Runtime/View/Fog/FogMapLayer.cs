using CriticalMass.Runtime.Hud.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.View.Fog
{
    /// <summary>
    /// Batch 12 item P: the grey over the minimap (GameHud) and the full map (FullMapView) where the local team sees
    /// nothing. One quad over the map image's rect; each corner's uv is the sim point under that minimap pixel
    /// (MinimapProjection.ToSim, the minimap's own mapping) over the map size, so the camera-up rotation of the minimap
    /// is exact (the mapping is linear). Samples FogOfWar.Texture (no copy, no extra texture); 4 vertices, 2 triangles,
    /// one draw in the map's canvas. Rebuilt only when bound; the texture's content changes need no rebuild.
    /// Hidden while the fog is off. View only.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FogMapLayer : MaskableGraphic
    {
        public const string ShaderResource = "CMFogMapUI";
        static Material _shared;
        static bool _tried;

        MinimapProjection _proj;
        Texture _bound;

        /// <summary>The shared UI material (null when the shader is missing: the layer then stays hidden).</summary>
        public static Material Shared
        {
            get
            {
                if (_shared == null && !_tried)
                {
                    _tried = true;
                    Shader s = Resources.Load<Shader>(ShaderResource);
                    if (s != null) _shared = new Material(s) { name = "Fog Map Grey" };
                    else Debug.LogWarning("[CM.Fog] map shader " + ShaderResource + " not found; the maps are not greyed (their enemy markers still follow the fog)");
                }
                return _shared;
            }
        }

        public override Texture mainTexture => _bound != null ? _bound : Texture2D.whiteTexture;

        /// <summary>Binds the minimap mapping the image under this layer was laid out from.</summary>
        public void Bind(MinimapProjection proj)
        {
            _proj = proj;
            raycastTarget = false;
            color = new Color(FogConfig.MapGrey.r, FogConfig.MapGrey.g, FogConfig.MapGrey.b, FogConfig.MapGreyAlpha);
            Material m = Shared;
            if (m != null) material = m;
            enabled = false;
            SetVerticesDirty();
        }

        /// <summary>Call once per HUD update: shows the layer while the fog is drawn and follows its texture.</summary>
        public void Sync()
        {
            bool on = FogOfWar.Active && FogOfWar.Texture != null && Shared != null && _proj != null;
            if (enabled != on) enabled = on;
            if (on && _bound != FogOfWar.Texture)
            {
                _bound = FogOfWar.Texture;
                SetMaterialDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_proj == null || _proj.MapWidth <= 0 || _proj.MapHeight <= 0) return;
            Rect r = rectTransform.rect;
            Vector2 tl = new Vector2(r.xMin, r.yMax);
            Color32 c = color;
            for (int k = 0; k < 4; k++)
            {
                // image pixels (1x) of the corners: top-left, top-right, bottom-right, bottom-left
                double px = k == 1 || k == 2 ? _proj.Width : 0, py = k >= 2 ? _proj.Height : 0;
                _proj.ToSim(px, py, 1, out double x, out double z);
                vh.AddVert(new Vector3(tl.x + (float)px, tl.y - (float)py), c,
                    new Vector4((float)(x / _proj.MapWidth), (float)(z / _proj.MapHeight), 0f, 0f));
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }
}

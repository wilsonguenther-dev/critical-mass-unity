using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// Minimap land: one quad per horizontal run of non-void Sim cells (vector, so it stays sharp at any HUD scale), walls a
    /// band lighter, water blue, plus dashed routes base to hill to shrine (bible p15 minimap). Oriented so the Azure base is
    /// on the left and the shrine is at the top. Rebuilt only when the map or the rect changes.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MinimapLand : MaskableGraphic
    {
        static readonly Color Land = CMTokens.ParseHex("#34465C");
        static readonly Color WallC = CMTokens.ParseHex("#4E5D72");
        static readonly Color WaterC = CMTokens.ParseHex("#2C6E96");
        static readonly Color Route = new Color(0.97f, 0.94f, 0.9f, 0.7f);

        MapDef _map;
        /// <summary>Land quads in the last mesh (capture log).</summary>
        public int Quads { get; private set; }
        bool _flipX, _flipZ;
        float _scale, _ox, _oy;

        public void SetMap(MapDef map)
        {
            _map = map;
            if (map.Pads != null && map.Pads.Length >= 2) _flipX = map.Pads[0].X > map.Pads[1].X;
            _flipZ = map.Shrine != null && map.Hill != null && map.Shrine.Z > map.Hill.Z;
            Fit();
            SetVerticesDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (_map != null) Fit();
        }

        void Fit()
        {
            Rect r = rectTransform.rect;
            if (_map == null || _map.Width <= 0 || _map.Height <= 0) return;
            _scale = Mathf.Min(r.width / _map.Width, r.height / _map.Height);
            _ox = (r.width - _map.Width * _scale) * 0.5f;
            _oy = (r.height - _map.Height * _scale) * 0.5f;
        }

        /// <summary>Sim position to (x right, y down negative) from this rect's top-left, canvas units.</summary>
        public Vector2 ToLocal(double simX, double simZ)
        {
            if (_map == null) return Vector2.zero;
            float u = (float)(_flipX ? _map.Width - simX : simX);
            float v = (float)(_flipZ ? _map.Height - simZ : simZ);
            return new Vector2(_ox + u * _scale, -(_oy + v * _scale));
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_map == null || _map.Cells == null) return;
            Fit();
            Rect r = rectTransform.rect;
            Vector2 tl = new Vector2(r.xMin, r.yMax);
            int w = _map.Width, h = _map.Height;
            Quads = 0;
            for (int cz = 0; cz < h; cz++)
            {
                int cx = 0;
                while (cx < w)
                {
                    byte c = _map.Cells[cz * w + cx];
                    if (c == Cell.Void) { cx++; continue; }
                    Color col = Colour(c);
                    int start = cx;
                    while (cx < w && _map.Cells[cz * w + cx] != Cell.Void && Colour(_map.Cells[cz * w + cx]) == col) cx++;
                    Quad(vh, tl, start, cz, cx, cz + 1, col);
                    Quads++;
                }
            }
            if (_map.Hill == null) return;
            if (_map.Pads != null)
                for (int t = 0; t < _map.Pads.Length; t++) Dashes(vh, tl, _map.Pads[t].X, _map.Pads[t].Z, _map.Hill.X, _map.Hill.Z);
            if (_map.Shrine != null) Dashes(vh, tl, _map.Hill.X, _map.Hill.Z, _map.Shrine.X, _map.Shrine.Z);
        }

        static Color Colour(byte c) => c == Cell.Wall ? WallC : c == Cell.Water ? WaterC : Land;

        void Quad(VertexHelper vh, Vector2 tl, float x0, float z0, float x1, float z1, Color c)
        {
            Vector2 a = Corner(tl, x0, z0), b = Corner(tl, x1, z1);
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y)), c, Vector4.zero);
            vh.AddVert(new Vector3(Mathf.Max(a.x, b.x), Mathf.Min(a.y, b.y)), c, Vector4.zero);
            vh.AddVert(new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)), c, Vector4.zero);
            vh.AddVert(new Vector3(Mathf.Min(a.x, b.x), Mathf.Max(a.y, b.y)), c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        Vector2 Corner(Vector2 tl, float x, float z)
        {
            Vector2 p = ToLocal(x, z);
            return tl + p;
        }

        void Dashes(VertexHelper vh, Vector2 tl, double x0, double z0, double x1, double z1)
        {
            Vector2 a = tl + ToLocal(x0, z0), b = tl + ToLocal(x1, z1);
            float len = Vector2.Distance(a, b);
            int n = Mathf.FloorToInt(len / 7f);
            for (int k = 1; k < n; k++)
            {
                Vector2 p = Vector2.Lerp(a, b, k / (float)n);
                int i = vh.currentVertCount;
                vh.AddVert(p + new Vector2(-1.6f, -1.6f), Route, Vector4.zero);
                vh.AddVert(p + new Vector2(1.6f, -1.6f), Route, Vector4.zero);
                vh.AddVert(p + new Vector2(1.6f, 1.6f), Route, Vector4.zero);
                vh.AddVert(p + new Vector2(-1.6f, 1.6f), Route, Vector4.zero);
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i, i + 2, i + 3);
            }
        }
    }

    /// <summary>
    /// Death state (art direction 6.3): a Void Navy wash (world to about 70% brightness) plus a navy vignette that starts
    /// 40% in from each edge. Drawn under the HUD clusters, which stay at full brightness. No red, no flash.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ScreenVignette : MaskableGraphic
    {
        public float Wash = 0.55f;   // linear blend: about 70% of the world's sRGB brightness
        public float EdgeAlpha = 0.75f;
        public float Band = 0.4f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Color navy = CMTokens.VoidNavy.Color;
            Color wash = navy; wash.a = Wash;
            Color edge = navy; edge.a = EdgeAlpha;
            Color none = navy; none.a = 0f;
            vh.AddVert(new Vector3(r.xMin, r.yMin), wash, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), wash, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), wash, Vector4.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), wash, Vector4.zero);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
            float ix = r.width * 0.5f * Band, iy = r.height * 0.5f * Band;
            Vector2[] outer = { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
            Vector2[] inner = { new Vector2(r.xMin + ix, r.yMin + iy), new Vector2(r.xMax - ix, r.yMin + iy), new Vector2(r.xMax - ix, r.yMax - iy), new Vector2(r.xMin + ix, r.yMax - iy) };
            int s = vh.currentVertCount;
            for (int i = 0; i < 4; i++)
            {
                vh.AddVert(outer[i], edge, Vector4.zero);
                vh.AddVert(inner[i], none, Vector4.zero);
            }
            for (int i = 0; i < 4; i++)
            {
                int a = s + i * 2, b = s + ((i + 1) % 4) * 2;
                vh.AddTriangle(a, b, a + 1);
                vh.AddTriangle(b, b + 1, a + 1);
            }
        }
    }
}

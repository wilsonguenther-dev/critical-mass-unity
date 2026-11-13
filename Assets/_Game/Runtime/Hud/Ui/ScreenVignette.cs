using CriticalMass.Runtime.Hud.Style;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
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
